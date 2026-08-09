#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using TryGame.RefDataTools.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// SmallArea 编辑器专用的最小 OpenXML 桥。只改源工作簿的八个 SmallArea Sheet，
    /// 保留其它 Sheet/样式/关系；不触碰 Output、GeneratedTables、bytes 或 Manifest。
    /// </summary>
    internal static class BattleSmallAreaWorkbookBridge
    {
        internal sealed class Snapshot
        {
            public string WorkbookPath;
            public string ContentHash;
            public SortedDictionary<int, string> Templates =
                new SortedDictionary<int, string>();
            public int[] SmallAreaIds = Array.Empty<int>();
        }

        private sealed class SheetData
        {
            public string Name;
            public string EntryPath;
            public XDocument Document;
            public List<string[]> Rows = new List<string[]>();
            public string[] StyleByColumn = Array.Empty<string>();
            public string TableEntryPath;
        }

        private sealed class WorkbookData
        {
            public readonly Dictionary<string, SheetData> Sheets =
                new Dictionary<string, SheetData>(StringComparer.Ordinal);
            public readonly List<string> SharedStrings = new List<string>();
        }

        private sealed class ParsedTemplateRows
        {
            public readonly Dictionary<string, string[]> Headers =
                new Dictionary<string, string[]>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<string[]>> Rows =
                new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        }

        private static readonly string[] EditableSheets =
        {
            "BattleSmallArea",
            "BattleSmallAreaFloor",
            "BattleSmallAreaLadder",
            "BattleSmallAreaDoorPoint",
            "BattleSmallAreaEnemySpawnArea",
            "BattleSmallAreaLootSpawnPoint",
            "BattleSmallAreaBossSpawnPoint",
            "BattleSmallAreaExtractionSpawnPoint",
        };

        private static readonly XNamespace SpreadsheetNs =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace OfficeRelNs =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelNs =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        public static string DefaultWorkbookPath
        {
            get
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                return Path.Combine(
                    root,
                    "RefDataSource",
                    "TryGameRefdataRes",
                    "v2",
                    "b.战斗关卡表.xlsx");
            }
        }

        internal static bool TryLoad(out Snapshot snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;
            string path = DefaultWorkbookPath;
            if (!File.Exists(path))
            {
                error = $"正式战斗源工作簿不存在：{path}";
                return false;
            }

            try
            {
                WorkbookData workbook = ReadWorkbook(path);
                ValidateRequiredSheets(workbook);
                SortedDictionary<int, string> templates = BuildTemplateSourceRows(workbook);
                if (templates.Count == 0)
                {
                    error = "BattleSmallArea Sheet 没有可导入的数据行。";
                    return false;
                }

                snapshot = new Snapshot
                {
                    WorkbookPath = path,
                    ContentHash = ComputeHash(path),
                    Templates = templates,
                    SmallAreaIds = templates.Keys.ToArray(),
                };
                return true;
            }
            catch (Exception exception)
            {
                error = $"读取正式战斗源工作簿失败：{exception.Message}";
                return false;
            }
        }

        internal static bool TryWriteTemplate(
            Snapshot snapshot,
            int smallAreaId,
            string sourceRows,
            out string backupPath,
            out string error)
        {
            return TryWriteTemplates(
                snapshot,
                new Dictionary<int, string>
                {
                    { smallAreaId, sourceRows },
                },
                out backupPath,
                out error);
        }

        internal static bool TryWriteTemplates(
            Snapshot snapshot,
            IReadOnlyDictionary<int, string> sourceRowsBySmallAreaId,
            out string backupPath,
            out string error)
        {
            backupPath = string.Empty;
            error = string.Empty;
            if (snapshot == null
                || string.IsNullOrWhiteSpace(snapshot.WorkbookPath)
                || sourceRowsBySmallAreaId == null
                || sourceRowsBySmallAreaId.Count == 0)
            {
                error = "写回请求缺少工作簿快照或模板源行。";
                return false;
            }

            string workbookPath = snapshot.WorkbookPath;
            string temporaryPath = Path.Combine(
                Path.GetDirectoryName(workbookPath),
                $".{Path.GetFileName(workbookPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                string currentHash = ComputeHash(workbookPath);
                if (!string.Equals(
                        currentHash,
                        snapshot.ContentHash,
                        StringComparison.Ordinal))
                {
                    error =
                        "正式源表在导入后已被其它程序或协作者修改。请重新读取全部模板，" +
                        "确认差异后再写回，避免覆盖他人变更。";
                    return false;
                }

                SortedDictionary<int, ParsedTemplateRows> replacements =
                    new SortedDictionary<int, ParsedTemplateRows>();
                foreach (KeyValuePair<int, string> pair in sourceRowsBySmallAreaId)
                {
                    if (pair.Key <= 0
                        || string.IsNullOrWhiteSpace(pair.Value)
                        || !snapshot.Templates.ContainsKey(pair.Key))
                    {
                        throw new InvalidDataException(
                            $"写回模板身份非法或不在读取快照中：smallAreaId={pair.Key}。" );
                    }

                    ParsedTemplateRows replacement =
                        ParseSectionRows(pair.Value);
                    ValidateReplacementShape(pair.Key, replacement.Rows);
                    replacements.Add(pair.Key, replacement);
                }

                File.Copy(workbookPath, temporaryPath, true);
                using (FileStream stream = new FileStream(
                    temporaryPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
                using (ZipArchive archive = new ZipArchive(
                    stream,
                    ZipArchiveMode.Update,
                    false,
                    Encoding.UTF8))
                {
                    WorkbookData workbook = ReadWorkbook(archive);
                    ValidateRequiredSheets(workbook);
                    foreach (KeyValuePair<int, ParsedTemplateRows> pair
                        in replacements)
                    {
                        ValidateReplacementSchema(workbook, pair.Value);
                        ValidateCrossReferences(workbook, pair.Value.Rows);
                    }

                    for (int index = 0; index < EditableSheets.Length; index++)
                    {
                        string sheetName = EditableSheets[index];
                        ReplaceAreaRowsBatch(
                            archive,
                            workbook.Sheets[sheetName],
                            replacements);
                    }
                }

                // 在临时副本上重新完整读取，任何 XML/关系/行断裂都在替换原文件前失败。
                WorkbookData validationWorkbook = ReadWorkbook(temporaryPath);
                ValidateRequiredSheets(validationWorkbook);
                SortedDictionary<int, string> validationTemplates =
                    BuildTemplateSourceRows(validationWorkbook);
                foreach (KeyValuePair<int, string> original in snapshot.Templates)
                {
                    if (!validationTemplates.TryGetValue(
                            original.Key,
                            out string actualRows))
                    {
                        throw new InvalidDataException(
                            $"临时工作簿写回后找不到 smallAreaId={original.Key}。" );
                    }

                    string expectedRows = sourceRowsBySmallAreaId.TryGetValue(
                        original.Key,
                        out string changedRows)
                        ? changedRows
                        : original.Value;
                    if (!TemplateRowsEqual(expectedRows, actualRows))
                    {
                        throw new InvalidDataException(
                            $"临时工作簿逐模板回读不一致：smallAreaId={original.Key}。" );
                    }
                }

                if (validationTemplates.Count != snapshot.Templates.Count)
                    throw new InvalidDataException(
                        $"临时工作簿模板数量发生变化：expected={snapshot.Templates.Count}, " +
                        $"actual={validationTemplates.Count}。" );

                string backupDirectory = Path.Combine(
                    Path.GetDirectoryName(workbookPath),
                    ".battle-small-area-backups");
                Directory.CreateDirectory(backupDirectory);
                backupPath = Path.Combine(
                    backupDirectory,
                    $"{Path.GetFileNameWithoutExtension(workbookPath)}_" +
                    $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.xlsx");
                File.Copy(workbookPath, backupPath, false);
                File.Replace(temporaryPath, workbookPath, null, true);
                temporaryPath = string.Empty;
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return true;
            }
            catch (Exception exception)
            {
                error = $"事务性写回失败，正式源表未被半套修改：{exception.Message}";
                return false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath)
                    && File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        internal static bool TryWriteTemplatesAndExport(
            Snapshot snapshot,
            IReadOnlyDictionary<int, string> sourceRowsBySmallAreaId,
            out string backupPath,
            out bool sourceSavedOutputStale,
            out string error)
        {
            backupPath = string.Empty;
            sourceSavedOutputStale = false;
            error = string.Empty;
            if (snapshot == null
                || string.IsNullOrWhiteSpace(snapshot.WorkbookPath))
            {
                error = "写回并导表缺少正式工作簿快照。";
                return false;
            }

            List<string> exportInputs = new List<string>
            {
                snapshot.WorkbookPath,
            };
            if (!TryGameRefDataExportTransaction.ValidateIncrementalPreflight(
                    exportInputs))
            {
                error =
                    "BattleSmallArea 增量导表预检失败，源 Excel 与正式 Output 均未修改。";
                return false;
            }

            if (!TryWriteTemplates(
                    snapshot,
                    sourceRowsBySmallAreaId,
                    out backupPath,
                    out error))
            {
                return false;
            }

            sourceSavedOutputStale = true;
            if (!TryGameRefDataExportWindow.ExportFiles(
                    exportInputs,
                    TryGameRefDataExportMode.Incremental))
            {
                bool restored = TryRestoreWorkbookFromBackup(
                    snapshot.WorkbookPath,
                    backupPath,
                    out string restoreError);
                sourceSavedOutputStale = !restored;
                error = restored
                    ? "正式增量导表失败；源 Excel 已从写前备份恢复，Output 事务应保持旧版本。"
                    : "SourceSavedOutputStale：正式增量导表失败，且源 Excel 自动恢复失败。" +
                      $" restoreError={restoreError}";
                return false;
            }

            if (!TryValidateExportedTemplates(
                    sourceRowsBySmallAreaId,
                    out string validationError))
            {
                error =
                    "SourceSavedOutputStale：导表事务返回成功，但 Output 逐模板回读不一致。" +
                    $" {validationError}";
                return false;
            }

            sourceSavedOutputStale = false;
            return true;
        }

        private static WorkbookData ReadWorkbook(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (ZipArchive archive = new ZipArchive(
                stream,
                ZipArchiveMode.Read,
                false,
                Encoding.UTF8))
            {
                return ReadWorkbook(archive);
            }
        }

        private static WorkbookData ReadWorkbook(ZipArchive archive)
        {
            WorkbookData result = new WorkbookData();
            ZipArchiveEntry shared = archive.GetEntry("xl/sharedStrings.xml");
            if (shared != null)
            {
                XDocument sharedDocument = LoadXml(shared);
                foreach (XElement item in sharedDocument.Descendants(SpreadsheetNs + "si"))
                {
                    result.SharedStrings.Add(string.Concat(
                        item.Descendants(SpreadsheetNs + "t")
                            .Select(value => value.Value)));
                }
            }

            XDocument workbook = LoadXml(RequireEntry(archive, "xl/workbook.xml"));
            XDocument relationships = LoadXml(RequireEntry(
                archive,
                "xl/_rels/workbook.xml.rels"));
            Dictionary<string, string> relationshipTargets = relationships
                .Descendants(PackageRelNs + "Relationship")
                .ToDictionary(
                    value => (string)value.Attribute("Id"),
                    value => NormalizeZipPath("xl", (string)value.Attribute("Target")),
                    StringComparer.Ordinal);

            foreach (XElement sheet in workbook.Descendants(SpreadsheetNs + "sheet"))
            {
                string name = (string)sheet.Attribute("name");
                string relationshipId = (string)sheet.Attribute(OfficeRelNs + "id");
                if (string.IsNullOrWhiteSpace(name)
                    || !relationshipTargets.TryGetValue(
                        relationshipId,
                        out string entryPath))
                {
                    continue;
                }
                ZipArchiveEntry entry = RequireEntry(archive, entryPath);
                SheetData data = new SheetData
                {
                    Name = name,
                    EntryPath = entryPath,
                    Document = LoadXml(entry),
                };
                data.Rows = ReadRows(data.Document, result.SharedStrings);
                data.StyleByColumn = ReadTemplateStyles(data.Document);
                data.TableEntryPath = ResolveFirstTableEntryPath(
                    archive,
                    entryPath,
                    data.Document);
                result.Sheets[name] = data;
            }
            return result;
        }

        private static SortedDictionary<int, string> BuildTemplateSourceRows(
            WorkbookData workbook)
        {
            SortedDictionary<int, string> result =
                new SortedDictionary<int, string>();
            SheetData master = workbook.Sheets["BattleSmallArea"];
            for (int rowIndex = 4; rowIndex < master.Rows.Count; rowIndex++)
            {
                string[] masterRow = master.Rows[rowIndex];
                if (!TryParseInt(Cell(masterRow, 0), out int areaId) || areaId <= 0)
                {
                    continue;
                }
                StringBuilder text = new StringBuilder(4096);
                for (int sheetIndex = 0; sheetIndex < EditableSheets.Length; sheetIndex++)
                {
                    string sheetName = EditableSheets[sheetIndex];
                    SheetData sheet = workbook.Sheets[sheetName];
                    text.Append('[').Append(sheetName).AppendLine("]");
                    text.AppendLine(string.Join("\t", sheet.Rows[0]));
                    for (int dataIndex = 4; dataIndex < sheet.Rows.Count; dataIndex++)
                    {
                        string[] row = sheet.Rows[dataIndex];
                        int ownerColumn = sheetName == "BattleSmallArea" ? 0 : 1;
                        if (TryParseInt(Cell(row, ownerColumn), out int ownerId)
                            && ownerId == areaId)
                        {
                            text.AppendLine(string.Join("\t", row));
                        }
                    }
                }
                result.Add(areaId, text.ToString());
            }
            return result;
        }

        private static ParsedTemplateRows ParseSectionRows(
            string sourceRows)
        {
            ParsedTemplateRows result = new ParsedTemplateRows();
            string current = string.Empty;
            bool awaitingHeader = false;
            string[] lines = (sourceRows ?? string.Empty)
                .Replace("\r", string.Empty)
                .Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string trimmed = lines[index].Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    current = trimmed.Substring(1, trimmed.Length - 2);
                    if (!EditableSheets.Contains(current))
                        throw new InvalidDataException($"源行包含不允许写回的 Sheet：{current}。" );
                    if (result.Rows.ContainsKey(current))
                        throw new InvalidDataException($"源行重复包含 Sheet：{current}。" );
                    result.Rows[current] = new List<string[]>();
                    awaitingHeader = true;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(current))
                    throw new InvalidDataException("源行在首个 [Sheet] 之前包含数据。" );
                if (awaitingHeader)
                {
                    result.Headers[current] = lines[index].Split('\t');
                    awaitingHeader = false;
                    continue;
                }

                result.Rows[current].Add(lines[index].Split('\t'));
            }
            return result;
        }

        private static void ValidateReplacementSchema(
            WorkbookData workbook,
            ParsedTemplateRows replacement)
        {
            for (int index = 0; index < EditableSheets.Length; index++)
            {
                string sheetName = EditableSheets[index];
                if (!workbook.Sheets.TryGetValue(sheetName, out SheetData sheet)
                    || sheet.Rows.Count == 0)
                {
                    throw new InvalidDataException(
                        $"正式源工作簿的 {sheetName} 缺少字段名表头。" );
                }

                if (!replacement.Headers.TryGetValue(
                        sheetName,
                        out string[] suppliedHeader))
                {
                    throw new InvalidDataException(
                        $"写回源行的 [{sheetName}] 缺少字段名表头。" );
                }

                string[] officialHeader = sheet.Rows[0];
                if (!RowsExactlyEqual(suppliedHeader, officialHeader))
                {
                    throw new InvalidDataException(
                        $"写回源行的 [{sheetName}] 表头与正式 Sheet 不一致。" +
                        $"expected=[{string.Join(", ", officialHeader)}], " +
                        $"actual=[{string.Join(", ", suppliedHeader)}]。" );
                }

                List<string[]> rows = replacement.Rows[sheetName];
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    int actualColumns = rows[rowIndex]?.Length ?? 0;
                    if (actualColumns != officialHeader.Length)
                    {
                        throw new InvalidDataException(
                            $"写回源行的 [{sheetName}] 第 {rowIndex + 1} 行列数错误：" +
                            $"expected={officialHeader.Length}, actual={actualColumns}。" );
                    }
                }
            }
        }

        private static bool RowsExactlyEqual(
            IReadOnlyList<string> first,
            IReadOnlyList<string> second)
        {
            if (first == null || second == null || first.Count != second.Count)
            {
                return false;
            }

            for (int index = 0; index < first.Count; index++)
            {
                if (!string.Equals(
                        first[index] ?? string.Empty,
                        second[index] ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateReplacementShape(
            int smallAreaId,
            IReadOnlyDictionary<string, List<string[]>> replacement)
        {
            for (int index = 0; index < EditableSheets.Length; index++)
            {
                string sheet = EditableSheets[index];
                if (!replacement.TryGetValue(sheet, out List<string[]> rows))
                    throw new InvalidDataException($"写回源行缺少 [{sheet}]。" );
                if (sheet == "BattleSmallArea" && rows.Count != 1)
                    throw new InvalidDataException("BattleSmallArea 必须且只能有一行当前模板。" );
                HashSet<int> rowIds = new HashSet<int>();
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    string[] row = rows[rowIndex];
                    int ownerColumn = sheet == "BattleSmallArea" ? 0 : 1;
                    if (!TryParseInt(Cell(row, 0), out int rowId)
                        || rowId <= 0
                        || !rowIds.Add(rowId)
                        || !TryParseInt(Cell(row, ownerColumn), out int owner)
                        || owner != smallAreaId)
                    {
                        throw new InvalidDataException(
                            $"{sheet} 行身份非法/重复或 owner 不匹配：" +
                            $"row={rowIndex + 1}, expectedArea={smallAreaId}。" );
                    }
                }
            }
        }

        private static void ValidateCrossReferences(
            WorkbookData workbook,
            IReadOnlyDictionary<string, List<string[]>> replacement)
        {
            HashSet<int> floorStyles = ReadIdSet(workbook, "BattleFloorStyle");
            HashSet<int> ladderStyles = ReadIdSet(workbook, "BattleLadderStyle");
            HashSet<int> doorStyles = ReadIdSet(workbook, "BattleDoorStyle");
            HashSet<int> spawnRules = ReadIdSet(workbook, "BattleEnemySpawnRule");
            HashSet<int> lootSources = ReadIdSet(workbook, "BattleLootSource");
            ValidateColumnReferences(replacement["BattleSmallAreaFloor"], 8, floorStyles, "floorStyleId");
            ValidateColumnReferences(replacement["BattleSmallAreaLadder"], 7, ladderStyles, "ladderStyleId");
            ValidateColumnReferences(replacement["BattleSmallAreaDoorPoint"], 5, doorStyles, "doorStyleId");
            ValidateColumnReferences(replacement["BattleSmallAreaEnemySpawnArea"], 6, spawnRules, "spawnRuleId");
            ValidateColumnReferences(replacement["BattleSmallAreaLootSpawnPoint"], 6, lootSources, "lootSourceId");
        }

        private static HashSet<int> ReadIdSet(WorkbookData workbook, string sheetName)
        {
            if (!workbook.Sheets.TryGetValue(sheetName, out SheetData sheet))
                throw new InvalidDataException($"引用目标 Sheet 不存在：{sheetName}。" );
            HashSet<int> result = new HashSet<int>();
            for (int index = 4; index < sheet.Rows.Count; index++)
                if (TryParseInt(Cell(sheet.Rows[index], 0), out int id)) result.Add(id);
            return result;
        }

        private static void ValidateColumnReferences(
            IReadOnlyList<string[]> rows,
            int column,
            ISet<int> targetIds,
            string label)
        {
            for (int index = 0; index < rows.Count; index++)
                if (!TryParseInt(Cell(rows[index], column), out int id) || !targetIds.Contains(id))
                    throw new InvalidDataException($"{label} 断链：row={index + 1}, id={Cell(rows[index], column)}。" );
        }

        private static void ReplaceAreaRowsBatch(
            ZipArchive archive,
            SheetData sheet,
            IReadOnlyDictionary<int, ParsedTemplateRows> replacements)
        {
            List<string[]> finalRows = new List<string[]>();
            for (int index = 0; index < Math.Min(4, sheet.Rows.Count); index++)
                finalRows.Add(sheet.Rows[index]);
            int ownerColumn = sheet.Name == "BattleSmallArea" ? 0 : 1;
            for (int index = 4; index < sheet.Rows.Count; index++)
            {
                string[] row = sheet.Rows[index];
                if (!TryParseInt(Cell(row, ownerColumn), out int owner)
                    || !replacements.ContainsKey(owner))
                {
                    finalRows.Add(row);
                }
            }

            foreach (KeyValuePair<int, ParsedTemplateRows> pair
                in replacements.OrderBy(value => value.Key))
            {
                finalRows.AddRange(
                    pair.Value.Rows[sheet.Name]
                        .Select(value => (string[])value.Clone()));
            }
            List<string[]> header = finalRows.Take(4).ToList();
            List<string[]> data = finalRows.Skip(4)
                .OrderBy(value => ParseIntOrMaximum(Cell(value, 0)))
                .ToList();
            finalRows = header.Concat(data).ToList();

            XElement sheetData = sheet.Document.Root?.Element(SpreadsheetNs + "sheetData")
                ?? throw new InvalidDataException($"{sheet.Name} 缺少 sheetData。" );
            List<XElement> originalHeaderRows = sheetData.Elements(SpreadsheetNs + "row")
                .Take(4)
                .Select(value => new XElement(value))
                .ToList();
            sheetData.RemoveNodes();
            for (int rowIndex = 0; rowIndex < finalRows.Count; rowIndex++)
            {
                int excelRow = rowIndex + 1;
                if (rowIndex < originalHeaderRows.Count)
                {
                    XElement headerRow = originalHeaderRows[rowIndex];
                    headerRow.SetAttributeValue("r", excelRow);
                    sheetData.Add(headerRow);
                }
                else
                {
                    sheetData.Add(CreateRow(
                        excelRow,
                        finalRows[rowIndex],
                        sheet.StyleByColumn));
                }
            }

            int columnCount = finalRows.Count > 0
                ? finalRows.Max(value => value?.Length ?? 0)
                : 1;
            string range = $"A1:{ColumnName(Math.Max(1, columnCount))}{Math.Max(1, finalRows.Count)}";
            XElement dimension = sheet.Document.Root?.Element(SpreadsheetNs + "dimension");
            dimension?.SetAttributeValue("ref", range);
            ReplaceXmlEntry(archive, sheet.EntryPath, sheet.Document);
            if (!string.IsNullOrWhiteSpace(sheet.TableEntryPath))
            {
                ZipArchiveEntry tableEntry = archive.GetEntry(sheet.TableEntryPath);
                if (tableEntry != null)
                {
                    XDocument table = LoadXml(tableEntry);
                    table.Root?.SetAttributeValue("ref", range);
                    table.Root?.Element(SpreadsheetNs + "autoFilter")
                        ?.SetAttributeValue("ref", range);
                    ReplaceXmlEntry(archive, sheet.TableEntryPath, table);
                }
            }
        }

        private static bool TemplateRowsEqual(string expected, string actual)
        {
            ParsedTemplateRows expectedTemplate =
                ParseSectionRows(expected);
            ParsedTemplateRows actualTemplate =
                ParseSectionRows(actual);
            for (int sheetIndex = 0; sheetIndex < EditableSheets.Length; sheetIndex++)
            {
                string sheetName = EditableSheets[sheetIndex];
                if (!expectedTemplate.Headers.TryGetValue(
                        sheetName,
                        out string[] expectedHeader)
                    || !actualTemplate.Headers.TryGetValue(
                        sheetName,
                        out string[] actualHeader)
                    || !RowsExactlyEqual(expectedHeader, actualHeader)
                    || !expectedTemplate.Rows.TryGetValue(
                        sheetName,
                        out List<string[]> expectedRows)
                    || !actualTemplate.Rows.TryGetValue(
                        sheetName,
                        out List<string[]> actualRows)
                    || expectedRows.Count != actualRows.Count)
                {
                    return false;
                }

                List<string[]> orderedExpected = expectedRows
                    .OrderBy(value => ParseIntOrMaximum(Cell(value, 0)))
                    .ToList();
                List<string[]> orderedActual = actualRows
                    .OrderBy(value => ParseIntOrMaximum(Cell(value, 0)))
                    .ToList();
                for (int rowIndex = 0; rowIndex < orderedExpected.Count; rowIndex++)
                {
                    int columnCount = Math.Max(
                        LastMeaningfulColumn(orderedExpected[rowIndex]),
                        LastMeaningfulColumn(orderedActual[rowIndex]));
                    for (int column = 0; column <= columnCount; column++)
                    {
                        if (!string.Equals(
                                CanonicalCell(Cell(orderedExpected[rowIndex], column)),
                                CanonicalCell(Cell(orderedActual[rowIndex], column)),
                                StringComparison.Ordinal))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static string CanonicalCell(string value)
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (bool.TryParse(trimmed, out bool boolean))
            {
                return boolean ? "1" : "0";
            }

            if (double.TryParse(
                    trimmed,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number))
            {
                return number.ToString("R", CultureInfo.InvariantCulture);
            }

            return trimmed;
        }

        private static bool TryValidateExportedTemplates(
            IReadOnlyDictionary<int, string> expectedTemplates,
            out string error)
        {
            error = string.Empty;
            string txtDirectory = Path.Combine(
                TryGameRefDataPaths.ToFullPath(
                    TryGameRefDataPaths.DefaultOutputAssetPath),
                "txt_data");
            Dictionary<string, List<string[]>> exported =
                new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
            for (int sheetIndex = 0; sheetIndex < EditableSheets.Length; sheetIndex++)
            {
                string sheetName = EditableSheets[sheetIndex];
                string path = Path.Combine(txtDirectory, sheetName + ".txt");
                if (!File.Exists(path))
                {
                    error = $"导出结果缺少 {sheetName}.txt。";
                    return false;
                }

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 4)
                {
                    error = $"导出结果 {sheetName}.txt 少于四行表头。";
                    return false;
                }

                List<string[]> rows = new List<string[]>();
                for (int lineIndex = 4; lineIndex < lines.Length; lineIndex++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[lineIndex]))
                    {
                        rows.Add(lines[lineIndex].Split('\t'));
                    }
                }
                exported.Add(sheetName, rows);
            }

            foreach (KeyValuePair<int, string> expectedTemplate in expectedTemplates)
            {
                StringBuilder actualText = new StringBuilder(4096);
                for (int sheetIndex = 0; sheetIndex < EditableSheets.Length; sheetIndex++)
                {
                    string sheetName = EditableSheets[sheetIndex];
                    actualText.Append('[').Append(sheetName).AppendLine("]");
                    actualText.AppendLine("header");
                    int ownerColumn = sheetName == "BattleSmallArea" ? 0 : 1;
                    List<string[]> rows = exported[sheetName];
                    for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                    {
                        if (TryParseInt(
                                Cell(rows[rowIndex], ownerColumn),
                                out int owner)
                            && owner == expectedTemplate.Key)
                        {
                            actualText.AppendLine(string.Join("\t", rows[rowIndex]));
                        }
                    }
                }

                if (!TemplateRowsEqual(
                        expectedTemplate.Value,
                        actualText.ToString()))
                {
                    error = $"导出结果与源模板不一致：smallAreaId={expectedTemplate.Key}。";
                    return false;
                }
            }

            return true;
        }

        private static bool TryRestoreWorkbookFromBackup(
            string workbookPath,
            string backupPath,
            out string error)
        {
            error = string.Empty;
            string restorePath = workbookPath + $".{Guid.NewGuid():N}.restore.tmp";
            try
            {
                if (!File.Exists(workbookPath) || !File.Exists(backupPath))
                {
                    error = "正式工作簿或写前备份不存在。";
                    return false;
                }

                File.Copy(backupPath, restorePath, true);
                File.Replace(restorePath, workbookPath, null, true);
                restorePath = string.Empty;
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(restorePath)
                    && File.Exists(restorePath))
                {
                    try
                    {
                        File.Delete(restorePath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static int LastMeaningfulColumn(IReadOnlyList<string> row)
        {
            if (row == null)
            {
                return -1;
            }

            for (int index = row.Count - 1; index >= 0; index--)
            {
                if (!string.IsNullOrEmpty(row[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        private static XElement CreateRow(
            int rowNumber,
            IReadOnlyList<string> values,
            IReadOnlyList<string> styles)
        {
            XElement row = new XElement(
                SpreadsheetNs + "row",
                new XAttribute("r", rowNumber));
            for (int column = 0; column < values.Count; column++)
            {
                string value = values[column] ?? string.Empty;
                XElement cell = new XElement(
                    SpreadsheetNs + "c",
                    new XAttribute("r", $"{ColumnName(column + 1)}{rowNumber}"));
                if (column < styles.Count && !string.IsNullOrWhiteSpace(styles[column]))
                    cell.SetAttributeValue("s", styles[column]);
                if (bool.TryParse(value, out bool boolean))
                {
                    cell.SetAttributeValue("t", "b");
                    cell.Add(new XElement(SpreadsheetNs + "v", boolean ? "1" : "0"));
                }
                else if (double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number))
                {
                    cell.Add(new XElement(
                        SpreadsheetNs + "v",
                        number.ToString("R", CultureInfo.InvariantCulture)));
                }
                else
                {
                    cell.SetAttributeValue("t", "inlineStr");
                    XElement text = new XElement(SpreadsheetNs + "t", value);
                    if (value.StartsWith(" ", StringComparison.Ordinal)
                        || value.EndsWith(" ", StringComparison.Ordinal))
                        text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                    cell.Add(new XElement(SpreadsheetNs + "is", text));
                }
                row.Add(cell);
            }
            return row;
        }

        private static List<string[]> ReadRows(
            XDocument document,
            IReadOnlyList<string> sharedStrings)
        {
            List<string[]> result = new List<string[]>();
            XElement sheetData = document.Root?.Element(SpreadsheetNs + "sheetData");
            if (sheetData == null) return result;
            foreach (XElement row in sheetData.Elements(SpreadsheetNs + "row"))
            {
                Dictionary<int, string> cells = new Dictionary<int, string>();
                int maximum = -1;
                foreach (XElement cell in row.Elements(SpreadsheetNs + "c"))
                {
                    int column = ParseColumnIndex((string)cell.Attribute("r"));
                    if (column < 0) continue;
                    cells[column] = ReadCellValue(cell, sharedStrings);
                    maximum = Math.Max(maximum, column);
                }
                string[] values = new string[maximum + 1];
                foreach (KeyValuePair<int, string> pair in cells) values[pair.Key] = pair.Value;
                result.Add(values);
            }
            return result;
        }

        private static string[] ReadTemplateStyles(XDocument document)
        {
            XElement template = document.Root?
                .Element(SpreadsheetNs + "sheetData")?
                .Elements(SpreadsheetNs + "row")
                .Skip(4)
                .FirstOrDefault();
            if (template == null) return Array.Empty<string>();
            Dictionary<int, string> styles = new Dictionary<int, string>();
            int maximum = -1;
            foreach (XElement cell in template.Elements(SpreadsheetNs + "c"))
            {
                int column = ParseColumnIndex((string)cell.Attribute("r"));
                if (column < 0) continue;
                styles[column] = (string)cell.Attribute("s") ?? string.Empty;
                maximum = Math.Max(maximum, column);
            }
            string[] result = new string[maximum + 1];
            foreach (KeyValuePair<int, string> pair in styles) result[pair.Key] = pair.Value;
            return result;
        }

        private static string ReadCellValue(
            XElement cell,
            IReadOnlyList<string> sharedStrings)
        {
            string type = (string)cell.Attribute("t") ?? string.Empty;
            if (type == "inlineStr")
                return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(value => value.Value));
            string raw = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
            if (type == "s" && TryParseInt(raw, out int index)
                && index >= 0 && index < sharedStrings.Count)
                return sharedStrings[index];
            if (type == "b") return raw == "1" ? "true" : "false";
            return raw;
        }

        private static string ResolveFirstTableEntryPath(
            ZipArchive archive,
            string worksheetPath,
            XDocument worksheet)
        {
            XElement tablePart = worksheet.Descendants(SpreadsheetNs + "tablePart")
                .FirstOrDefault();
            string relationshipId = (string)tablePart?.Attribute(OfficeRelNs + "id");
            if (string.IsNullOrWhiteSpace(relationshipId)) return string.Empty;
            string directory = Path.GetDirectoryName(worksheetPath).Replace('\\', '/');
            string file = Path.GetFileName(worksheetPath);
            string relPath = $"{directory}/_rels/{file}.rels";
            ZipArchiveEntry relEntry = archive.GetEntry(relPath);
            if (relEntry == null) return string.Empty;
            XDocument relationships = LoadXml(relEntry);
            XElement relationship = relationships
                .Descendants(PackageRelNs + "Relationship")
                .FirstOrDefault(value => string.Equals(
                    (string)value.Attribute("Id"),
                    relationshipId,
                    StringComparison.Ordinal));
            return relationship == null
                ? string.Empty
                : NormalizeZipPath(directory, (string)relationship.Attribute("Target"));
        }

        private static void ValidateRequiredSheets(WorkbookData workbook)
        {
            for (int index = 0; index < EditableSheets.Length; index++)
                if (!workbook.Sheets.ContainsKey(EditableSheets[index]))
                    throw new InvalidDataException($"正式源工作簿缺少 Sheet：{EditableSheets[index]}。" );
        }

        private static ZipArchiveEntry RequireEntry(ZipArchive archive, string path)
        {
            return archive.GetEntry(path)
                ?? throw new InvalidDataException($"xlsx 内部文件不存在：{path}。" );
        }

        private static XDocument LoadXml(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open()) return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        private static void ReplaceXmlEntry(
            ZipArchive archive,
            string path,
            XDocument document)
        {
            archive.GetEntry(path)?.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry(
                path,
                System.IO.Compression.CompressionLevel.Optimal);
            using (Stream stream = replacement.Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                document.Save(writer, SaveOptions.DisableFormatting);
        }

        private static string NormalizeZipPath(string baseDirectory, string target)
        {
            string combined = target != null && target.StartsWith("/", StringComparison.Ordinal)
                ? target.TrimStart('/')
                : $"{baseDirectory}/{target ?? string.Empty}";
            Stack<string> parts = new Stack<string>();
            foreach (string part in combined.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") { if (parts.Count > 0) parts.Pop(); }
                else parts.Push(part);
            }
            return string.Join("/", parts.Reverse());
        }

        private static int ParseColumnIndex(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return -1;
            int value = 0; int letters = 0;
            for (int index = 0; index < reference.Length; index++)
            {
                char c = reference[index];
                if (c >= 'A' && c <= 'Z') { value = value * 26 + (c - 'A' + 1); letters++; }
                else if (c >= 'a' && c <= 'z') { value = value * 26 + (c - 'a' + 1); letters++; }
                else break;
            }
            return letters == 0 ? -1 : value - 1;
        }

        private static string ColumnName(int oneBasedColumn)
        {
            StringBuilder name = new StringBuilder(); int value = oneBasedColumn;
            while (value > 0) { value--; name.Insert(0, (char)('A' + value % 26)); value /= 26; }
            return name.ToString();
        }

        private static string Cell(IReadOnlyList<string> row, int index)
        {
            return row != null && index >= 0 && index < row.Count
                ? row[index] ?? string.Empty
                : string.Empty;
        }

        private static bool TryParseInt(string value, out int result)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static int ParseIntOrMaximum(string value)
        {
            return TryParseInt(value, out int parsed) ? parsed : int.MaxValue;
        }

        private static string ComputeHash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }

}
#endif
