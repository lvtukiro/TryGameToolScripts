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

namespace Game.EditorTools
{
    internal readonly struct BattleActionTimelineRecordKey :
        IEquatable<BattleActionTimelineRecordKey>
    {
        public BattleActionTimelineRecordKey(string sheetName, int rowId)
        {
            SheetName = sheetName ?? string.Empty;
            RowId = rowId;
        }

        public string SheetName { get; }
        public int RowId { get; }

        public bool Equals(BattleActionTimelineRecordKey other)
        {
            return RowId == other.RowId && string.Equals(
                SheetName,
                other.SheetName,
                StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is BattleActionTimelineRecordKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((SheetName != null
                    ? StringComparer.Ordinal.GetHashCode(SheetName)
                    : 0) * 397) ^ RowId;
            }
        }

        public override string ToString()
        {
            return SheetName + ":" + RowId.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class BattleActionTimelineWorkbookRecord
    {
        public int RowId;
        public string[] Cells = Array.Empty<string>();

        public BattleActionTimelineWorkbookRecord Clone()
        {
            return new BattleActionTimelineWorkbookRecord
            {
                RowId = RowId,
                Cells = Cells != null
                    ? (string[])Cells.Clone()
                    : Array.Empty<string>(),
            };
        }
    }

    internal sealed class BattleActionTimelineWorkbookTable
    {
        public string Name = string.Empty;
        public string[] Headers = Array.Empty<string>();
        public readonly List<BattleActionTimelineWorkbookRecord> Records =
            new List<BattleActionTimelineWorkbookRecord>();

        public BattleActionTimelineWorkbookTable Clone()
        {
            BattleActionTimelineWorkbookTable result =
                new BattleActionTimelineWorkbookTable
                {
                    Name = Name,
                    Headers = Headers != null
                        ? (string[])Headers.Clone()
                        : Array.Empty<string>(),
                };
            for (int index = 0; index < Records.Count; index++)
            {
                result.Records.Add(Records[index].Clone());
            }

            return result;
        }

        public bool TryGet(int rowId, out BattleActionTimelineWorkbookRecord record)
        {
            for (int index = 0; index < Records.Count; index++)
            {
                if (Records[index].RowId == rowId)
                {
                    record = Records[index];
                    return true;
                }
            }

            record = null;
            return false;
        }
    }

    internal sealed class BattleActionTimelineWorkbookSnapshot
    {
        public string WorkbookPath = string.Empty;
        public string ContentHash = string.Empty;
        public string[] AllSheetNames = Array.Empty<string>();
        public readonly SortedDictionary<string, BattleActionTimelineWorkbookTable> Tables =
            new SortedDictionary<string, BattleActionTimelineWorkbookTable>(
                StringComparer.Ordinal);

        public bool HasActionStructure => Tables.Values.Any(
            BattleActionTimelineWorkbookBridge.IsActiveSingleTable);

        public bool TryGetTable(
            string sheetName,
            out BattleActionTimelineWorkbookTable table)
        {
            return Tables.TryGetValue(sheetName ?? string.Empty, out table);
        }
    }

    internal sealed class BattleActionTimelineWorkbookWriteSet
    {
        public readonly Dictionary<BattleActionTimelineRecordKey, string[]> Replacements =
            new Dictionary<BattleActionTimelineRecordKey, string[]>();
        public readonly HashSet<BattleActionTimelineRecordKey> Deletions =
            new HashSet<BattleActionTimelineRecordKey>();

        public bool IsEmpty => Replacements.Count == 0 && Deletions.Count == 0;
    }

    /// <summary>
    /// OpenXML bridge used only by the action authoring tool.  It deliberately edits existing
    /// worksheets instead of creating RefData output.  Every write is prepared and read back
    /// from a same-directory temporary workbook before the official xlsx is atomically replaced.
    /// </summary>
    internal static class BattleActionTimelineWorkbookBridge
    {
        private sealed class SheetData
        {
            public string Name;
            public string EntryPath;
            public string TableEntryPath;
            public XDocument Document;
            public List<string[]> Rows = new List<string[]>();
            public string[] StyleByColumn = Array.Empty<string>();
        }

        private sealed class WorkbookData
        {
            public readonly Dictionary<string, SheetData> Sheets =
                new Dictionary<string, SheetData>(StringComparer.Ordinal);
            public readonly List<string> SharedStrings = new List<string>();
        }

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
                string root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                return Path.Combine(
                    root,
                    "RefDataSource",
                    "TryGameRefdataRes",
                    "v2",
                    "r.机器人表.xlsx");
            }
        }

        internal static bool TryLoad(
            out BattleActionTimelineWorkbookSnapshot snapshot,
            out string error)
        {
            return TryLoad(DefaultWorkbookPath, out snapshot, out error);
        }

        internal static bool TryLoad(
            string workbookPath,
            out BattleActionTimelineWorkbookSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
            {
                error = "动作时间轴源工作簿不存在：" + workbookPath;
                return false;
            }

            try
            {
                WorkbookData workbook = ReadWorkbook(workbookPath);
                BattleActionTimelineWorkbookSnapshot result =
                    new BattleActionTimelineWorkbookSnapshot
                    {
                        WorkbookPath = Path.GetFullPath(workbookPath),
                        ContentHash = ComputeHash(workbookPath),
                        AllSheetNames = workbook.Sheets.Keys
                            .OrderBy(value => value, StringComparer.Ordinal)
                            .ToArray(),
                    };

                foreach (SheetData sheet in workbook.Sheets.Values)
                {
                    if (!IsWorksetSheet(sheet.Name, sheet.Rows))
                    {
                        continue;
                    }

                    if (sheet.Rows.Count < 4 || sheet.Rows[0].Length == 0)
                    {
                        throw new InvalidDataException(
                            "动作工作表缺少 cltabtoy 四行表头：" + sheet.Name);
                    }

                    BattleActionTimelineWorkbookTable table =
                        new BattleActionTimelineWorkbookTable
                        {
                            Name = sheet.Name,
                            Headers = (string[])sheet.Rows[0].Clone(),
                        };
                    HashSet<int> rowIds = new HashSet<int>();
                    for (int rowIndex = 4; rowIndex < sheet.Rows.Count; rowIndex++)
                    {
                        string[] cells = NormalizeWidth(
                            sheet.Rows[rowIndex],
                            table.Headers.Length);
                        if (!TryParsePositiveInt(Cell(cells, 0), out int rowId))
                        {
                            if (cells.Any(value => !string.IsNullOrWhiteSpace(value)))
                            {
                                throw new InvalidDataException(
                                    $"{sheet.Name} 第 {rowIndex + 1} 行缺少正的稳定 id。" );
                            }

                            continue;
                        }

                        if (!rowIds.Add(rowId))
                        {
                            throw new InvalidDataException(
                                $"{sheet.Name} 存在重复稳定 id={rowId}。" );
                        }

                        table.Records.Add(new BattleActionTimelineWorkbookRecord
                        {
                            RowId = rowId,
                            Cells = cells,
                        });
                    }

                    result.Tables.Add(table.Name, table);
                }

                snapshot = result;
                return true;
            }
            catch (Exception exception)
            {
                error = "读取动作时间轴源工作簿失败：" + exception.Message;
                return false;
            }
        }

        internal static bool TryWrite(
            BattleActionTimelineWorkbookSnapshot snapshot,
            BattleActionTimelineWorkbookWriteSet writeSet,
            out string backupPath,
            out string error)
        {
            return TryWrite(
                snapshot,
                writeSet,
                null,
                out backupPath,
                out error);
        }

        /// <summary>
        /// The optional validator is a test seam and an additional caller gate.  It runs against
        /// the fully rewritten temporary workbook, before backup and File.Replace.
        /// </summary>
        internal static bool TryWrite(
            BattleActionTimelineWorkbookSnapshot snapshot,
            BattleActionTimelineWorkbookWriteSet writeSet,
            Func<BattleActionTimelineWorkbookSnapshot, bool> temporaryValidator,
            out string backupPath,
            out string error)
        {
            backupPath = string.Empty;
            error = string.Empty;
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.WorkbookPath))
            {
                error = "写回缺少正式工作簿快照。";
                return false;
            }

            if (writeSet == null || writeSet.IsEmpty)
            {
                error = "没有需要写回的动作记录。";
                return false;
            }

            string workbookPath = snapshot.WorkbookPath;
            string temporaryPath = Path.Combine(
                Path.GetDirectoryName(workbookPath),
                "." + Path.GetFileName(workbookPath) + "." +
                Guid.NewGuid().ToString("N") + ".tmp");
            bool officialMayHaveChanged = false;
            try
            {
                string currentHash = ComputeHash(workbookPath);
                if (!string.Equals(
                        currentHash,
                        snapshot.ContentHash,
                        StringComparison.Ordinal))
                {
                    error =
                        "正式机器人工作簿在读取后已被其它程序或协作者修改；" +
                        "请重新读取并确认差异，未覆盖当前文件。";
                    return false;
                }

                ValidateWriteSet(snapshot, writeSet);
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
                    foreach (string sheetName in AffectedSheets(writeSet))
                    {
                        if (!workbook.Sheets.TryGetValue(sheetName, out SheetData sheet))
                        {
                            throw new InvalidDataException(
                                "临时工作簿缺少待写回 Sheet：" + sheetName);
                        }

                        ReplaceRows(archive, sheet, writeSet);
                    }
                }

                if (!TryLoad(
                        temporaryPath,
                        out BattleActionTimelineWorkbookSnapshot temporarySnapshot,
                        out string readbackError))
                {
                    throw new InvalidDataException(
                        "临时工作簿回读失败：" + readbackError);
                }

                ValidateReadback(snapshot, temporarySnapshot, writeSet);
                if (temporaryValidator != null && !temporaryValidator(temporarySnapshot))
                {
                    throw new InvalidDataException("临时工作簿附加回读校验失败。");
                }

                string backupDirectory = Path.Combine(
                    Path.GetDirectoryName(workbookPath),
                    ".battle-action-timeline-backups");
                Directory.CreateDirectory(backupDirectory);
                backupPath = Path.Combine(
                    backupDirectory,
                    Path.GetFileNameWithoutExtension(workbookPath) + "_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) +
                    "_" + Guid.NewGuid().ToString("N") + ".xlsx");
                File.Copy(workbookPath, backupPath, false);

                officialMayHaveChanged = true;
                File.Replace(temporaryPath, workbookPath, null, true);
                temporaryPath = string.Empty;

                if (!TryLoad(
                        workbookPath,
                        out BattleActionTimelineWorkbookSnapshot officialReadback,
                        out string officialReadbackError))
                {
                    throw new InvalidDataException(
                        "正式工作簿替换后回读失败：" + officialReadbackError);
                }

                ValidateReadback(snapshot, officialReadback, writeSet);
                return true;
            }
            catch (Exception exception)
            {
                string failure = officialMayHaveChanged
                    ? "动作源表可能已替换后发生失败：" + exception.Message
                    : "动作源表临时写回失败，正式文件未修改：" + exception.Message;
                if (officialMayHaveChanged)
                {
                    if (TryRestoreWorkbook(workbookPath, backupPath, out string restoreError))
                    {
                        error = failure + "；正式工作簿已从写前备份恢复。";
                    }
                    else
                    {
                        error = failure + "；自动恢复失败：" + restoreError;
                    }
                }
                else
                {
                    error = failure;
                }

                return false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
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

        internal static bool IsActiveSingleTable(
            BattleActionTimelineWorkbookTable table)
        {
            return table != null && HasAllHeaders(
                table.Headers,
                "startupEndTime",
                "actionSwitchWindowStartTime",
                "recoveryStartTime",
                "actionDuration");
        }

        internal static bool HeadersEqual(
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

        internal static bool CellsEqual(
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
                        CanonicalCell(first[index]),
                        CanonicalCell(second[index]),
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<string> AffectedSheets(
            BattleActionTimelineWorkbookWriteSet writeSet)
        {
            return writeSet.Replacements.Keys
                .Select(value => value.SheetName)
                .Concat(writeSet.Deletions.Select(value => value.SheetName))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal);
        }

        private static void ValidateWriteSet(
            BattleActionTimelineWorkbookSnapshot snapshot,
            BattleActionTimelineWorkbookWriteSet writeSet)
        {
            foreach (KeyValuePair<BattleActionTimelineRecordKey, string[]> pair
                in writeSet.Replacements)
            {
                BattleActionTimelineRecordKey key = pair.Key;
                if (key.RowId <= 0 || !snapshot.Tables.TryGetValue(
                        key.SheetName,
                        out BattleActionTimelineWorkbookTable table))
                {
                    throw new InvalidDataException("待写回记录不属于快照工作集：" + key);
                }

                string[] cells = pair.Value;
                if (cells == null || cells.Length != table.Headers.Length ||
                    !TryParsePositiveInt(Cell(cells, 0), out int rowId) ||
                    rowId != key.RowId)
                {
                    throw new InvalidDataException(
                        "待写回记录列宽或稳定 id 非法：" + key);
                }

                if (writeSet.Deletions.Contains(key))
                {
                    throw new InvalidDataException(
                        "同一记录不能同时替换和删除：" + key);
                }
            }

            foreach (BattleActionTimelineRecordKey key in writeSet.Deletions)
            {
                if (key.RowId <= 0 ||
                    !snapshot.Tables.TryGetValue(key.SheetName, out _) )
                {
                    throw new InvalidDataException("待删除记录不属于快照工作集：" + key);
                }
            }
        }

        private static void ValidateReadback(
            BattleActionTimelineWorkbookSnapshot original,
            BattleActionTimelineWorkbookSnapshot actual,
            BattleActionTimelineWorkbookWriteSet writeSet)
        {
            foreach (KeyValuePair<string, BattleActionTimelineWorkbookTable> tablePair
                in original.Tables)
            {
                if (!actual.Tables.TryGetValue(
                        tablePair.Key,
                        out BattleActionTimelineWorkbookTable actualTable))
                {
                    throw new InvalidDataException(
                        "回读缺少动作工作表：" + tablePair.Key);
                }

                if (!HeadersEqual(tablePair.Value.Headers, actualTable.Headers))
                {
                    throw new InvalidDataException(
                        "回读动作工作表表头发生变化：" + tablePair.Key);
                }

                foreach (BattleActionTimelineWorkbookRecord expectedRecord
                    in tablePair.Value.Records)
                {
                    BattleActionTimelineRecordKey key =
                        new BattleActionTimelineRecordKey(
                            tablePair.Key,
                            expectedRecord.RowId);
                    if (writeSet.Deletions.Contains(key))
                    {
                        if (actualTable.TryGet(expectedRecord.RowId, out _))
                        {
                            throw new InvalidDataException("回读仍包含待删除记录：" + key);
                        }

                        continue;
                    }

                    string[] expectedCells = writeSet.Replacements.TryGetValue(
                        key,
                        out string[] replacement)
                        ? replacement
                        : expectedRecord.Cells;
                    if (!actualTable.TryGet(
                            expectedRecord.RowId,
                            out BattleActionTimelineWorkbookRecord actualRecord) ||
                        !CellsEqual(expectedCells, actualRecord.Cells))
                    {
                        throw new InvalidDataException("回读记录不一致：" + key);
                    }
                }
            }

            foreach (KeyValuePair<BattleActionTimelineRecordKey, string[]> replacement
                in writeSet.Replacements)
            {
                if (!actual.Tables.TryGetValue(
                        replacement.Key.SheetName,
                        out BattleActionTimelineWorkbookTable table) ||
                    !table.TryGet(
                        replacement.Key.RowId,
                        out BattleActionTimelineWorkbookRecord record) ||
                    !CellsEqual(replacement.Value, record.Cells))
                {
                    throw new InvalidDataException(
                        "回读缺少新增或替换记录：" + replacement.Key);
                }
            }
        }

        private static void ReplaceRows(
            ZipArchive archive,
            SheetData sheet,
            BattleActionTimelineWorkbookWriteSet writeSet)
        {
            int width = sheet.Rows.Count > 0 ? sheet.Rows[0].Length : 0;
            if (width <= 0)
            {
                throw new InvalidDataException(sheet.Name + " 缺少字段名表头。");
            }

            Dictionary<int, string[]> replacements = writeSet.Replacements
                .Where(pair => string.Equals(
                    pair.Key.SheetName,
                    sheet.Name,
                    StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key.RowId, pair => pair.Value);
            HashSet<int> deletions = new HashSet<int>(
                writeSet.Deletions
                    .Where(key => string.Equals(
                        key.SheetName,
                        sheet.Name,
                        StringComparison.Ordinal))
                    .Select(key => key.RowId));
            HashSet<int> applied = new HashSet<int>();
            List<string[]> rows = new List<string[]>();
            for (int index = 0; index < Math.Min(4, sheet.Rows.Count); index++)
            {
                rows.Add(NormalizeWidth(sheet.Rows[index], width));
            }

            while (rows.Count < 4)
            {
                rows.Add(new string[width]);
            }

            for (int index = 4; index < sheet.Rows.Count; index++)
            {
                string[] original = NormalizeWidth(sheet.Rows[index], width);
                if (!TryParsePositiveInt(Cell(original, 0), out int rowId))
                {
                    rows.Add(original);
                    continue;
                }

                if (deletions.Contains(rowId))
                {
                    continue;
                }

                if (replacements.TryGetValue(rowId, out string[] replacement))
                {
                    rows.Add((string[])replacement.Clone());
                    applied.Add(rowId);
                }
                else
                {
                    rows.Add(original);
                }
            }

            foreach (KeyValuePair<int, string[]> replacement in replacements
                .OrderBy(pair => pair.Key))
            {
                if (!applied.Contains(replacement.Key))
                {
                    rows.Add((string[])replacement.Value.Clone());
                }
            }

            XElement sheetData = sheet.Document.Root?.Element(SpreadsheetNs + "sheetData")
                ?? throw new InvalidDataException(sheet.Name + " 缺少 sheetData。");
            sheetData.RemoveNodes();
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                sheetData.Add(CreateRow(
                    rowIndex + 1,
                    rows[rowIndex],
                    sheet.StyleByColumn));
            }

            string range = "A1:" + ColumnName(width) +
                Math.Max(1, rows.Count).ToString(CultureInfo.InvariantCulture);
            XElement dimension = sheet.Document.Root?.Element(
                SpreadsheetNs + "dimension");
            if (dimension != null)
            {
                dimension.SetAttributeValue("ref", range);
            }

            ReplaceXmlEntry(archive, sheet.EntryPath, sheet.Document);
            if (!string.IsNullOrWhiteSpace(sheet.TableEntryPath))
            {
                XDocument tableDocument = LoadXml(
                    RequireEntry(archive, sheet.TableEntryPath));
                tableDocument.Root?.SetAttributeValue("ref", range);
                XElement autoFilter = tableDocument.Root?.Element(
                    SpreadsheetNs + "autoFilter");
                autoFilter?.SetAttributeValue("ref", range);
                ReplaceXmlEntry(archive, sheet.TableEntryPath, tableDocument);
            }
        }

        private static bool TryRestoreWorkbook(
            string workbookPath,
            string backupPath,
            out string error)
        {
            error = string.Empty;
            string restorePath = workbookPath + "." +
                Guid.NewGuid().ToString("N") + ".restore.tmp";
            try
            {
                if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                {
                    error = "写前备份不存在。";
                    return false;
                }

                File.Copy(backupPath, restorePath, true);
                if (File.Exists(workbookPath))
                {
                    File.Replace(restorePath, workbookPath, null, true);
                }
                else
                {
                    File.Move(restorePath, workbookPath);
                }

                restorePath = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(restorePath) && File.Exists(restorePath))
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

        private static bool IsWorksetSheet(string sheetName, List<string[]> rows)
        {
            string normalized = (sheetName ?? string.Empty)
                .Replace("_", string.Empty)
                .ToLowerInvariant();
            if (normalized.Contains("activesingle") ||
                normalized.Contains("executionstep") ||
                normalized.Contains("attackbody") ||
                normalized.Contains("meleespawn") ||
                normalized.Contains("meleeattackspawn") ||
                normalized.Contains("projectilelaunch") ||
                normalized.Contains("projectilemovement") ||
                normalized.Contains("linearprojectile") ||
                normalized.Contains("ballisticprojectile") ||
                (normalized.Contains("projectile") &&
                    normalized.Contains("movement")) ||
                normalized == "battleprojectile")
            {
                return true;
            }

            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            string[] header = rows[0];
            return HasAllHeaders(
                    header,
                    "startupEndTime",
                    "recoveryStartTime",
                    "actionDuration") ||
                HasAllHeaders(header, "triggerTime", "stepType", "stepConfigId") ||
                HasAllHeaders(header, "clashStrength", "clashResistance") ||
                HasAllHeaders(header, "shapeType", "offsetX", "offsetY") ||
                HasAllHeaders(header, "attackBodyId", "activeDuration") ||
                HasAllHeaders(header, "projectileId", "spawnOffsetX", "spawnOffsetY") ||
                HasAllHeaders(header, "movementConfigId", "maxLifetime") ||
                HasAllHeaders(header, "localTime", "scaleX", "scaleY") ||
                HasAllHeaders(header, "initialSpeed", "gravityScale");
        }

        private static bool HasAllHeaders(
            IReadOnlyList<string> headers,
            params string[] required)
        {
            if (headers == null)
            {
                return false;
            }

            HashSet<string> values = new HashSet<string>(
                headers.Select(NormalizeHeader),
                StringComparer.Ordinal);
            for (int index = 0; index < required.Length; index++)
            {
                if (!values.Contains(NormalizeHeader(required[index])))
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeHeader(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace("_", string.Empty)
                .ToLowerInvariant();
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
            ZipArchiveEntry sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
            if (sharedEntry != null)
            {
                XDocument shared = LoadXml(sharedEntry);
                foreach (XElement item in shared.Descendants(SpreadsheetNs + "si"))
                {
                    result.SharedStrings.Add(string.Concat(
                        item.Descendants(SpreadsheetNs + "t")
                            .Select(value => value.Value)));
                }
            }

            XDocument workbook = LoadXml(RequireEntry(archive, "xl/workbook.xml"));
            XDocument relationships = LoadXml(
                RequireEntry(archive, "xl/_rels/workbook.xml.rels"));
            Dictionary<string, string> relationshipTargets = relationships
                .Descendants(PackageRelNs + "Relationship")
                .Where(value => value.Attribute("Id") != null &&
                    value.Attribute("Target") != null)
                .ToDictionary(
                    value => value.Attribute("Id").Value,
                    value => value.Attribute("Target").Value,
                    StringComparer.Ordinal);

            foreach (XElement sheetElement in workbook.Descendants(
                SpreadsheetNs + "sheet"))
            {
                string name = (string)sheetElement.Attribute("name") ?? string.Empty;
                string relationshipId =
                    (string)sheetElement.Attribute(OfficeRelNs + "id") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) ||
                    !relationshipTargets.TryGetValue(
                        relationshipId,
                        out string target))
                {
                    continue;
                }

                string entryPath = NormalizeZipPath("xl", target);
                XDocument document = LoadXml(RequireEntry(archive, entryPath));
                SheetData sheet = new SheetData
                {
                    Name = name,
                    EntryPath = entryPath,
                    Document = document,
                };
                sheet.Rows = ReadRows(document, result.SharedStrings);
                sheet.StyleByColumn = ReadTemplateStyles(document);
                sheet.TableEntryPath = ResolveFirstTableEntryPath(
                    archive,
                    entryPath,
                    document);
                result.Sheets.Add(name, sheet);
            }

            return result;
        }

        private static List<string[]> ReadRows(
            XDocument document,
            IReadOnlyList<string> sharedStrings)
        {
            List<string[]> rows = new List<string[]>();
            XElement sheetData = document.Root?.Element(SpreadsheetNs + "sheetData");
            if (sheetData == null)
            {
                return rows;
            }

            foreach (XElement row in sheetData.Elements(SpreadsheetNs + "row"))
            {
                Dictionary<int, string> values = new Dictionary<int, string>();
                int maximumColumn = -1;
                foreach (XElement cell in row.Elements(SpreadsheetNs + "c"))
                {
                    int column = ParseColumnIndex((string)cell.Attribute("r"));
                    maximumColumn = Math.Max(maximumColumn, column);
                    values[column] = ReadCellValue(cell, sharedStrings);
                }

                string[] result = maximumColumn >= 0
                    ? new string[maximumColumn + 1]
                    : Array.Empty<string>();
                foreach (KeyValuePair<int, string> value in values)
                {
                    result[value.Key] = value.Value;
                }

                rows.Add(result);
            }

            return rows;
        }

        private static string[] ReadTemplateStyles(XDocument document)
        {
            XElement sheetData = document.Root?.Element(SpreadsheetNs + "sheetData");
            XElement template = sheetData?.Elements(SpreadsheetNs + "row")
                .Skip(4)
                .FirstOrDefault() ??
                sheetData?.Elements(SpreadsheetNs + "row").FirstOrDefault();
            if (template == null)
            {
                return Array.Empty<string>();
            }

            Dictionary<int, string> styles = new Dictionary<int, string>();
            int maximum = -1;
            foreach (XElement cell in template.Elements(SpreadsheetNs + "c"))
            {
                int column = ParseColumnIndex((string)cell.Attribute("r"));
                maximum = Math.Max(maximum, column);
                styles[column] = (string)cell.Attribute("s") ?? string.Empty;
            }

            string[] result = maximum >= 0
                ? new string[maximum + 1]
                : Array.Empty<string>();
            foreach (KeyValuePair<int, string> style in styles)
            {
                result[style.Key] = style.Value;
            }

            return result;
        }

        private static string ReadCellValue(
            XElement cell,
            IReadOnlyList<string> sharedStrings)
        {
            string type = (string)cell.Attribute("t") ?? string.Empty;
            if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
            {
                return string.Concat(
                    cell.Descendants(SpreadsheetNs + "t")
                        .Select(value => value.Value));
            }

            string raw = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
            if (string.Equals(type, "s", StringComparison.Ordinal) &&
                int.TryParse(
                    raw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int sharedIndex) &&
                sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
            {
                return sharedStrings[sharedIndex];
            }

            if (string.Equals(type, "b", StringComparison.Ordinal))
            {
                return raw == "1" ? "True" : "False";
            }

            return raw;
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
                    new XAttribute(
                        "r",
                        ColumnName(column + 1) +
                        rowNumber.ToString(CultureInfo.InvariantCulture)));
                if (styles != null && column < styles.Count &&
                    !string.IsNullOrWhiteSpace(styles[column]))
                {
                    cell.SetAttributeValue("s", styles[column]);
                }

                if (bool.TryParse(value, out bool boolean))
                {
                    cell.SetAttributeValue("t", "b");
                    cell.Add(new XElement(SpreadsheetNs + "v", boolean ? "1" : "0"));
                }
                else if (double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number) &&
                    !double.IsNaN(number) && !double.IsInfinity(number))
                {
                    cell.Add(new XElement(
                        SpreadsheetNs + "v",
                        number.ToString("R", CultureInfo.InvariantCulture)));
                }
                else
                {
                    cell.SetAttributeValue("t", "inlineStr");
                    XElement text = new XElement(SpreadsheetNs + "t", value);
                    if (value.Length != value.Trim().Length)
                    {
                        text.SetAttributeValue(
                            XNamespace.Xml + "space",
                            "preserve");
                    }

                    cell.Add(new XElement(SpreadsheetNs + "is", text));
                }

                row.Add(cell);
            }

            return row;
        }

        private static string ResolveFirstTableEntryPath(
            ZipArchive archive,
            string worksheetEntryPath,
            XDocument worksheet)
        {
            XElement tablePart = worksheet.Descendants(SpreadsheetNs + "tablePart")
                .FirstOrDefault();
            string relationshipId =
                (string)tablePart?.Attribute(OfficeRelNs + "id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                return string.Empty;
            }

            string directory = Path.GetDirectoryName(worksheetEntryPath)
                ?.Replace('\\', '/') ?? string.Empty;
            string fileName = Path.GetFileName(worksheetEntryPath);
            string relationshipsPath = directory + "/_rels/" + fileName + ".rels";
            ZipArchiveEntry relationshipsEntry = archive.GetEntry(relationshipsPath);
            if (relationshipsEntry == null)
            {
                return string.Empty;
            }

            XDocument relationships = LoadXml(relationshipsEntry);
            XElement relationship = relationships
                .Descendants(PackageRelNs + "Relationship")
                .FirstOrDefault(value => string.Equals(
                    (string)value.Attribute("Id"),
                    relationshipId,
                    StringComparison.Ordinal));
            string target = (string)relationship?.Attribute("Target") ?? string.Empty;
            return string.IsNullOrWhiteSpace(target)
                ? string.Empty
                : NormalizeZipPath(directory, target);
        }

        private static string[] NormalizeWidth(string[] values, int width)
        {
            string[] result = new string[Math.Max(0, width)];
            if (values != null)
            {
                Array.Copy(values, result, Math.Min(values.Length, result.Length));
            }

            for (int index = 0; index < result.Length; index++)
            {
                result[index] ??= string.Empty;
            }

            return result;
        }

        private static string CanonicalCell(string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (bool.TryParse(trimmed, out bool boolean))
            {
                return boolean ? "true" : "false";
            }

            if (double.TryParse(
                trimmed,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double number) &&
                !double.IsNaN(number) && !double.IsInfinity(number))
            {
                return number.ToString("R", CultureInfo.InvariantCulture);
            }

            return trimmed;
        }

        private static bool TryParsePositiveInt(string value, out int result)
        {
            return int.TryParse(
                    (value ?? string.Empty).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out result) && result > 0;
        }

        private static string Cell(IReadOnlyList<string> row, int index)
        {
            return row != null && index >= 0 && index < row.Count
                ? row[index] ?? string.Empty
                : string.Empty;
        }

        private static int ParseColumnIndex(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return 0;
            }

            int value = 0;
            int index = 0;
            while (index < reference.Length && char.IsLetter(reference[index]))
            {
                value = value * 26 +
                    (char.ToUpperInvariant(reference[index]) - 'A' + 1);
                index++;
            }

            return Math.Max(0, value - 1);
        }

        private static string ColumnName(int oneBasedColumn)
        {
            int value = Math.Max(1, oneBasedColumn);
            StringBuilder result = new StringBuilder();
            while (value > 0)
            {
                value--;
                result.Insert(0, (char)('A' + value % 26));
                value /= 26;
            }

            return result.ToString();
        }

        private static string NormalizeZipPath(string baseDirectory, string target)
        {
            string normalizedTarget = (target ?? string.Empty).Replace('\\', '/');
            if (normalizedTarget.StartsWith("/", StringComparison.Ordinal))
            {
                return normalizedTarget.TrimStart('/');
            }

            Uri baseUri = new Uri(
                "http://local/" +
                (baseDirectory ?? string.Empty).Trim('/').Replace(" ", "%20") + "/");
            Uri resolved = new Uri(baseUri, normalizedTarget.Replace(" ", "%20"));
            return Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
        }

        private static ZipArchiveEntry RequireEntry(ZipArchive archive, string path)
        {
            return archive.GetEntry(path)
                ?? throw new InvalidDataException("xlsx 缺少 OpenXML 项：" + path);
        }

        private static XDocument LoadXml(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            {
                return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            }
        }

        private static void ReplaceXmlEntry(
            ZipArchive archive,
            string path,
            XDocument document)
        {
            ZipArchiveEntry existing = archive.GetEntry(path);
            existing?.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry(
                path,
                CompressionLevel.Optimal);
            using (Stream stream = replacement.Open())
            using (StreamWriter writer = new StreamWriter(
                stream,
                new UTF8Encoding(false)))
            {
                document.Save(writer, SaveOptions.DisableFormatting);
            }
        }

        private static string ComputeHash(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }
    }
}
#endif
