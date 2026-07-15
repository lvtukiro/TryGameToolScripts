using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace TryGame.RefDataTools.Editor
{
    /// <summary>
    /// 语言表专用导出，不走 cltabtoy。
    /// 原项目语言表第 2 行是字段名，第 3 行开始是数据。
    /// </summary>
    internal static class TryGameLanguageExcelExport
    {
        private const string LanguageSheetName = "Language";

        /// <summary>
        /// 批量导出语言表文件。
        /// </summary>
        public static bool Export(IReadOnlyList<string> excelFullPaths, string outputAssetPath)
        {
            if (excelFullPaths == null || excelFullPaths.Count == 0)
            {
                return true;
            }

            if (excelFullPaths.Count > 1)
            {
                Debug.LogError($"语言表导出失败：当前只允许一个 Language 源表，避免多个文件互相覆盖。count={excelFullPaths.Count}");
                return false;
            }

            bool success = true;
            for (int i = 0; i < excelFullPaths.Count; i++)
            {
                try
                {
                    if (!ExportSingle(excelFullPaths[i], outputAssetPath))
                    {
                        success = false;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError($"语言表导出发生异常：{excelFullPaths[i]}\n{exception}");
                    success = false;
                }
            }

            return success;
        }

        /// <summary>
        /// 导出单个语言表到 txt_data/Language.bytes。
        /// </summary>
        private static bool ExportSingle(string excelFullPath, string outputAssetPath)
        {
            if (!File.Exists(excelFullPath))
            {
                Debug.LogError("语言表不存在：" + excelFullPath);
                return false;
            }

            List<List<string>> rows;
            if (!TryReadSheet(excelFullPath, LanguageSheetName, out rows))
            {
                Debug.LogError("语言表导出失败，找不到 Language sheet：" + excelFullPath);
                return false;
            }

            if (rows.Count < 2)
            {
                Debug.LogError("语言表导出失败，至少需要说明行和字段名行：" + excelFullPath);
                return false;
            }

            if (!ValidateRequiredColumns(rows[1], excelFullPath) || !ValidateLanguageRows(rows, excelFullPath))
            {
                return false;
            }

            List<int> exportColumns = BuildExportColumns(rows[1]);
            if (exportColumns.Count == 0)
            {
                Debug.LogError("语言表导出失败，字段名行为空：" + excelFullPath);
                return false;
            }

            string output = BuildTabText(rows, exportColumns);
            string outputDir = Path.Combine(TryGameRefDataPaths.ToFullPath(outputAssetPath), "txt_data");
            Directory.CreateDirectory(outputDir);

            string outputPath = Path.Combine(outputDir, LanguageSheetName + ".bytes");
            WriteAllTextAtomic(outputPath, output);
            AssetDatabase.Refresh();

            Debug.Log("语言表导出完成：" + TryGameRefDataPaths.ToAssetPath(outputPath));
            return true;
        }

        private static bool ValidateRequiredColumns(List<string> headerRow, string excelFullPath)
        {
            string[] requiredColumns = { "id", "zh_cn", "en_US" };
            for (int i = 0; i < requiredColumns.Length; i++)
            {
                if (FindColumn(headerRow, requiredColumns[i]) < 0)
                {
                    Debug.LogError($"语言表导出失败，缺少必要列：column={requiredColumns[i]}, file={excelFullPath}");
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateLanguageRows(List<List<string>> rows, string excelFullPath)
        {
            int keyColumn = FindColumn(rows[1], "id");
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            bool valid = true;
            for (int i = 2; i < rows.Count; i++)
            {
                string key = GetCell(rows[i], keyColumn).Trim();
                if (string.IsNullOrEmpty(key))
                {
                    if (HasAnyValue(rows[i], BuildExportColumns(rows[1])))
                    {
                        Debug.LogError($"语言表存在内容但 key 为空：file={excelFullPath}, row={i + 1}");
                        valid = false;
                    }

                    continue;
                }

                if (!keys.Add(key))
                {
                    Debug.LogError($"语言表 key 重复：file={excelFullPath}, row={i + 1}, key={key}");
                    valid = false;
                }
            }

            return valid;
        }

        private static int FindColumn(List<string> headerRow, string columnName)
        {
            for (int i = 0; i < headerRow.Count; i++)
            {
                if (string.Equals(headerRow[i]?.Trim(), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void WriteAllTextAtomic(string outputPath, string content)
        {
            string tempPath = outputPath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, content, new UTF8Encoding(false));
                if (File.Exists(outputPath))
                {
                    try
                    {
                        File.Replace(tempPath, outputPath, null, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(tempPath, outputPath, true);
                        File.Delete(tempPath);
                    }
                    catch (NotSupportedException)
                    {
                        File.Copy(tempPath, outputPath, true);
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Move(tempPath, outputPath);
                }
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                throw;
            }
        }

        /// <summary>
        /// 根据字段名行筛选需要导出的列，空列后面的内容不再导出。
        /// </summary>
        private static List<int> BuildExportColumns(List<string> headerRow)
        {
            List<int> result = new List<int>();
            for (int i = 0; i < headerRow.Count; i++)
            {
                string value = headerRow[i];
                if (string.IsNullOrWhiteSpace(value))
                {
                    break;
                }

                if (value.Contains("#"))
                {
                    continue;
                }

                result.Add(i);
            }

            return result;
        }

        /// <summary>
        /// 按原项目格式生成 tab 分隔文本。
        /// </summary>
        private static string BuildTabText(List<List<string>> rows, List<int> exportColumns)
        {
            StringBuilder sb = new StringBuilder();
            AppendRow(sb, rows[1], exportColumns);

            for (int i = 2; i < rows.Count; i++)
            {
                if (!HasAnyValue(rows[i], exportColumns))
                {
                    continue;
                }

                AppendRow(sb, rows[i], exportColumns);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 写入一行 tab 分隔文本。
        /// </summary>
        private static void AppendRow(StringBuilder sb, List<string> row, List<int> exportColumns)
        {
            for (int i = 0; i < exportColumns.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\t');
                }

                string value = GetCell(row, exportColumns[i])
                    .Trim()
                    .Replace("||", " ")
                    .Replace('\t', ' ')
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');
                sb.Append(value);
            }

            sb.Append("\r\n");
        }

        /// <summary>
        /// 判断一行在导出列范围内是否有有效内容。
        /// </summary>
        private static bool HasAnyValue(List<string> row, List<int> exportColumns)
        {
            for (int i = 0; i < exportColumns.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(GetCell(row, exportColumns[i])))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 安全读取行里的单元格文本。
        /// </summary>
        private static string GetCell(List<string> row, int index)
        {
            return index >= 0 && index < row.Count ? row[index] ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// 从 xlsx 文件中读取指定 sheet 的所有行。
        /// </summary>
        private static bool TryReadSheet(string excelFullPath, string sheetName, out List<List<string>> rows)
        {
            rows = new List<List<string>>();

            using (ZipArchive archive = ZipFile.OpenRead(excelFullPath))
            {
                List<string> sharedStrings = ReadSharedStrings(archive);
                Dictionary<string, string> workbookRels = ReadWorkbookRelationships(archive);

                ZipArchiveEntry workbookEntry = archive.GetEntry("xl/workbook.xml");
                if (workbookEntry == null)
                {
                    return false;
                }

                XmlDocument workbook = LoadXml(workbookEntry);
                XmlNamespaceManager ns = CreateNamespaceManager(workbook);
                XmlNode sheetNode = workbook.SelectSingleNode("//x:sheet[@name='" + sheetName + "']", ns);
                if (sheetNode == null)
                {
                    return false;
                }

                XmlAttribute ridAttr = sheetNode.Attributes["id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"];
                if (ridAttr == null || !workbookRels.ContainsKey(ridAttr.Value))
                {
                    return false;
                }

                string target = workbookRels[ridAttr.Value].Replace("\\", "/").TrimStart('/');
                string sheetPath = target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : "xl/" + target;
                ZipArchiveEntry sheetEntry = archive.GetEntry(sheetPath);
                if (sheetEntry == null)
                {
                    return false;
                }

                rows = ReadRows(sheetEntry, sharedStrings);
                return true;
            }
        }

        /// <summary>
        /// 读取 xlsx 的共享字符串表。
        /// </summary>
        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            List<string> result = new List<string>();
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return result;
            }

            XmlDocument doc = LoadXml(entry);
            XmlNamespaceManager ns = CreateNamespaceManager(doc);
            XmlNodeList nodes = doc.SelectNodes("//x:si", ns);
            for (int i = 0; i < nodes.Count; i++)
            {
                StringBuilder sb = new StringBuilder();
                XmlNodeList texts = nodes[i].SelectNodes(".//x:t", ns);
                for (int j = 0; j < texts.Count; j++)
                {
                    sb.Append(texts[j].InnerText);
                }

                result.Add(sb.ToString());
            }

            return result;
        }

        /// <summary>
        /// 读取 workbook 到具体 sheet 文件的关系映射。
        /// </summary>
        private static Dictionary<string, string> ReadWorkbookRelationships(ZipArchive archive)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            ZipArchiveEntry entry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (entry == null)
            {
                return result;
            }

            XmlDocument doc = LoadXml(entry);
            XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("r", "http://schemas.openxmlformats.org/package/2006/relationships");

            XmlNodeList nodes = doc.SelectNodes("//r:Relationship", ns);
            for (int i = 0; i < nodes.Count; i++)
            {
                XmlAttribute id = nodes[i].Attributes["Id"];
                XmlAttribute target = nodes[i].Attributes["Target"];
                if (id != null && target != null)
                {
                    result[id.Value] = target.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// 读取 sheetData 中的所有行数据。
        /// </summary>
        private static List<List<string>> ReadRows(ZipArchiveEntry sheetEntry, List<string> sharedStrings)
        {
            List<List<string>> rows = new List<List<string>>();
            XmlDocument doc = LoadXml(sheetEntry);
            XmlNamespaceManager ns = CreateNamespaceManager(doc);
            XmlNodeList rowNodes = doc.SelectNodes("//x:sheetData/x:row", ns);

            for (int i = 0; i < rowNodes.Count; i++)
            {
                List<string> row = new List<string>();
                XmlNodeList cellNodes = rowNodes[i].SelectNodes("x:c", ns);
                for (int j = 0; j < cellNodes.Count; j++)
                {
                    XmlNode cellNode = cellNodes[j];
                    XmlAttribute refAttr = cellNode.Attributes["r"];
                    int columnIndex = refAttr == null ? j : ParseColumnIndex(refAttr.Value);
                    while (row.Count <= columnIndex)
                    {
                        row.Add(string.Empty);
                    }

                    row[columnIndex] = ReadCellValue(cellNode, sharedStrings, ns);
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// 读取单个 xlsx 单元格的文本值。
        /// </summary>
        private static string ReadCellValue(XmlNode cellNode, List<string> sharedStrings, XmlNamespaceManager ns)
        {
            string cellType = cellNode.Attributes["t"] == null ? string.Empty : cellNode.Attributes["t"].Value;
            if (cellType == "inlineStr")
            {
                XmlNode inlineText = cellNode.SelectSingleNode(".//x:t", ns);
                return inlineText == null ? string.Empty : inlineText.InnerText;
            }

            XmlNode valueNode = cellNode.SelectSingleNode("x:v", ns);
            if (valueNode == null)
            {
                return string.Empty;
            }

            if (cellType == "s")
            {
                int sharedIndex;
                if (int.TryParse(valueNode.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out sharedIndex) &&
                    sharedIndex >= 0 &&
                    sharedIndex < sharedStrings.Count)
                {
                    return sharedStrings[sharedIndex];
                }

                return string.Empty;
            }

            return valueNode.InnerText;
        }

        /// <summary>
        /// 把 Excel 单元格引用中的列名转成零基列索引。
        /// </summary>
        private static int ParseColumnIndex(string cellReference)
        {
            int result = 0;
            for (int i = 0; i < cellReference.Length; i++)
            {
                char c = cellReference[i];
                if (c < 'A' || c > 'Z')
                {
                    break;
                }

                result = result * 26 + c - 'A' + 1;
            }

            return Mathf.Max(0, result - 1);
        }

        /// <summary>
        /// 从 xlsx 压缩包条目中加载 XML 文档。
        /// </summary>
        private static XmlDocument LoadXml(ZipArchiveEntry entry)
        {
            XmlDocument doc = new XmlDocument();
            using (Stream stream = entry.Open())
            {
                doc.Load(stream);
            }

            return doc;
        }

        /// <summary>
        /// 创建 xlsx XML 默认命名空间管理器。
        /// </summary>
        private static XmlNamespaceManager CreateNamespaceManager(XmlDocument doc)
        {
            XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            return ns;
        }
    }
}
