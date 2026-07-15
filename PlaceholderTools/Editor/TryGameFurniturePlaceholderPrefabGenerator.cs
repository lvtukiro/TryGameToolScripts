using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace TryGame.PlaceholderTools.Editor
{
    /// <summary>
    /// 按家具配表和图片资源生成缺失家具 prefab。
    /// </summary>
    public static class TryGameFurniturePlaceholderPrefabGenerator
    {
        private const string ExcelRoot = "Assets/Resources/TryGameRefdataRes/v2";
        private const string FurnitureExcelName = "h.家园1_0A.xlsx";
        private const string ResourceExcelName = "z.资源相关表.xlsx";
        private const string SourceImageRoot = "Assets/Resources/TryGameBuildRes";
        private const string GeneratedFurnitureRoot = "Assets/Resources/TryGameBuildRes/home/furniture";
        private const string GeneratedSpriteRoot = "Assets/Resources/TryGameBuildRes/gui/sprite";
        private const string FurnitureSheetName = "HomeFurniture";
        private const string FurnitureResourceSheetName = "FurnitureResource";
        private const string ResourceRuleSheetName = "ResourceRule";
        private const string CommonSpriteAssetPathFormat = "Assets/Resources/TryGameBuildRes/gui/sprite/spt_{0}/spt_{0}_{1}.png";
        private static readonly Regex ResourceJsonRegex = new Regex(
            @"""MainId""\s*:\s*(?<main>\d+)\s*,\s*""SubId""\s*:\s*(?<sub>\d+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 从配表读取家具列表，为还没有 prefab 的家具生成资源。
        /// </summary>
        [MenuItem("TryGame/Placeholder/按配表生成缺失家具 Prefab")]
        public static void GenerateMissingFromConfig()
        {
            List<HomeFurnitureRow> furnitureRows = LoadFurnitureRows();
            Dictionary<int, FurnitureResourceRow> resourceRows = LoadFurnitureResources();
            Dictionary<int, ResourceRuleRow> ruleRows = LoadResourceRules();
            Dictionary<string, string> imageByName = FindSourceImages();

            int generatedPrefabCount = 0;
            int generatedIconCount = 0;
            int skippedCount = 0;
            for (int i = 0; i < furnitureRows.Count; i++)
            {
                HomeFurnitureRow furniture = furnitureRows[i];
                if (!TryGetResource(resourceRows, furniture.resourceId, out FurnitureResourceRow prefabResource) ||
                    !TryGetResource(ruleRows, prefabResource.ruleId, out ResourceRuleRow prefabRule))
                {
                    Debug.LogWarning("[TryGame] 家具缺少 prefab 资源配置，已跳过：" + furniture.id);
                    skippedCount++;
                    continue;
                }

                string prefabAssetPath = ResolvePrefabAssetPath(prefabRule, prefabResource.resource);
                if (string.IsNullOrEmpty(prefabAssetPath))
                {
                    Debug.LogWarning("[TryGame] prefab 资源路径解析失败，已跳过：" + furniture.displayName);
                    skippedCount++;
                    continue;
                }

                string iconAssetPath = string.Empty;
                if (!furniture.icon.IsEmpty())
                {
                    iconAssetPath = string.Format(
                        CultureInfo.InvariantCulture,
                        CommonSpriteAssetPathFormat,
                        furniture.icon.mainId,
                        furniture.icon.subId);
                }

                string imageAssetPath = FindImageAssetPath(furniture, imageByName, iconAssetPath);
                if (string.IsNullOrEmpty(imageAssetPath))
                {
                    Debug.LogWarning("[TryGame] 找不到家具图片，已跳过：" + furniture.displayName);
                    skippedCount++;
                    continue;
                }

                if (!string.IsNullOrEmpty(iconAssetPath))
                {
                    string movedImagePath = MoveImageToIconPath(imageAssetPath, iconAssetPath);
                    if (string.IsNullOrEmpty(movedImagePath))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (!imageAssetPath.Equals(movedImagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        generatedIconCount++;
                    }

                    imageAssetPath = movedImagePath;
                }

                if (!File.Exists(ToFullPath(prefabAssetPath)))
                {
                    GeneratePrefab(furniture, prefabResource, imageAssetPath, prefabAssetPath);
                    generatedPrefabCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TryGame] 家具 prefab 生成完成。prefab:{generatedPrefabCount} icon:{generatedIconCount} skip:{skippedCount}");
        }

        /// <summary>
        /// 读取 HomeFurniture 表数据。
        /// </summary>
        private static List<HomeFurnitureRow> LoadFurnitureRows()
        {
            string path = Path.Combine(ExcelRoot, FurnitureExcelName);
            List<Dictionary<string, string>> rows = XlsxSheetReader.ReadTable(path, FurnitureSheetName, 4);
            List<HomeFurnitureRow> result = new List<HomeFurnitureRow>();
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, string> row = rows[i];
                int id = ParseInt(GetValue(row, "id"));
                if (id <= 0)
                {
                    continue;
                }

                result.Add(new HomeFurnitureRow
                {
                    id = id,
                    nameKey = GetValue(row, "nameKey"),
                    displayName = GetValue(row, "#家具名称"),
                    resourceId = ParseInt(GetValue(row, "resourceId")),
                    icon = ReadIconResource(row),
                    shapeRows = SplitShapeRows(GetValue(row, "shapeRows")),
                });
            }

            return result;
        }

        /// <summary>
        /// 读取 FurnitureResource 表数据。
        /// </summary>
        private static Dictionary<int, FurnitureResourceRow> LoadFurnitureResources()
        {
            string path = Path.Combine(ExcelRoot, ResourceExcelName);
            List<Dictionary<string, string>> rows = XlsxSheetReader.ReadTable(path, FurnitureResourceSheetName, 4);
            Dictionary<int, FurnitureResourceRow> result = new Dictionary<int, FurnitureResourceRow>();
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, string> row = rows[i];
                int id = ParseInt(GetValue(row, "id"));
                if (id <= 0)
                {
                    continue;
                }

                result[id] = new FurnitureResourceRow
                {
                    id = id,
                    ruleId = ParseInt(GetValue(row, "ruleId")),
                    resource = ParseResource(GetValue(row, "res")),
                };
            }

            return result;
        }

        /// <summary>
        /// 读取 ResourceRule 表数据。
        /// </summary>
        private static Dictionary<int, ResourceRuleRow> LoadResourceRules()
        {
            string path = Path.Combine(ExcelRoot, ResourceExcelName);
            List<Dictionary<string, string>> rows = XlsxSheetReader.ReadTable(path, ResourceRuleSheetName, 4);
            Dictionary<int, ResourceRuleRow> result = new Dictionary<int, ResourceRuleRow>();
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, string> row = rows[i];
                int id = ParseInt(GetValue(row, "id"));
                if (id <= 0)
                {
                    continue;
                }

                result[id] = new ResourceRuleRow
                {
                    id = id,
                    resourceType = GetValue(row, "resourceType"),
                    localPathTemplate = GetValue(row, "localPathTemplate"),
                };
            }

            return result;
        }

        /// <summary>
        /// 查找可用于家具生成的源图片。
        /// </summary>
        private static Dictionary<string, string> FindSourceImages()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SourceImageRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (IsGeneratedResourcePath(path))
                {
                    continue;
                }

                string key = NormalizeName(Path.GetFileNameWithoutExtension(path));
                if (!string.IsNullOrEmpty(key) && !result.ContainsKey(key))
                {
                    result.Add(key, path);
                }
            }

            return result;
        }

        /// <summary>
        /// 根据家具名称匹配图片资源。
        /// </summary>
        private static string FindImageAssetPath(HomeFurnitureRow furniture, Dictionary<string, string> imageByName, string iconAssetPath)
        {
            if (!string.IsNullOrEmpty(iconAssetPath) && File.Exists(ToFullPath(iconAssetPath)))
            {
                return iconAssetPath;
            }

            string displayName = NormalizeName(furniture.displayName);
            if (imageByName.TryGetValue(displayName, out string exactPath))
            {
                return exactPath;
            }

            foreach (KeyValuePair<string, string> pair in imageByName)
            {
                if (displayName.Contains(pair.Key) || pair.Key.Contains(displayName))
                {
                    return pair.Value;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 生成单个家具 prefab。
        /// </summary>
        private static void GeneratePrefab(HomeFurnitureRow furniture, FurnitureResourceRow resource, string imageAssetPath, string prefabAssetPath)
        {
            Sprite sprite = LoadSprite(imageAssetPath);
            if (sprite == null)
            {
                Debug.LogWarning("[TryGame] 图片不是可用 Sprite，无法生成 prefab：" + imageAssetPath);
                return;
            }

            EnsureAssetFolder(Path.GetDirectoryName(prefabAssetPath));
            GameObject root = new GameObject($"furniture_{resource.resource.mainId}_{resource.resource.subId}");
            root.AddComponent<Game.FurnitureInstance>();

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 0;

            FitSpriteToShape(visual.transform, sprite, furniture.shapeRows);
            PrefabUtility.SaveAsPrefabAsset(root, prefabAssetPath);
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log("[TryGame] 已生成家具 prefab：" + prefabAssetPath);
        }

        /// <summary>
        /// 把源图片移动到图标资源路径，避免根目录残留散图。
        /// </summary>
        private static string MoveImageToIconPath(string imageAssetPath, string iconAssetPath)
        {
            if (imageAssetPath.Equals(iconAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                SetupTextureAsSprite(iconAssetPath, 100f);
                return iconAssetPath;
            }

            if (File.Exists(ToFullPath(iconAssetPath)))
            {
                SetupTextureAsSprite(iconAssetPath, 100f);
                return iconAssetPath;
            }

            EnsureAssetFolder(Path.GetDirectoryName(iconAssetPath));
            string moveError = AssetDatabase.MoveAsset(imageAssetPath, iconAssetPath);
            if (!string.IsNullOrEmpty(moveError))
            {
                Debug.LogError("[TryGame] 移动家具图片失败：" + moveError);
                return string.Empty;
            }

            SetupTextureAsSprite(iconAssetPath, 100f);
            Debug.Log("[TryGame] 已移动家具图片到图标路径：" + iconAssetPath);
            return iconAssetPath;
        }

        /// <summary>
        /// 确保 Unity 工程内的资源目录存在。
        /// </summary>
        private static void EnsureAssetFolder(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath) ||
                AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            string normalizedPath = assetFolderPath.Replace("\\", "/").TrimEnd('/');
            string[] parts = normalizedPath.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                Directory.CreateDirectory(ToFullPath(normalizedPath));
                AssetDatabase.Refresh();
                return;
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        /// <summary>
        /// 加载图片并确保它被导入为 Sprite。
        /// </summary>
        private static Sprite LoadSprite(string imageAssetPath)
        {
            SetupTextureAsSprite(imageAssetPath, 100f);
            return AssetDatabase.LoadAssetAtPath<Sprite>(imageAssetPath);
        }

        /// <summary>
        /// 设置图片导入参数为 Sprite。
        /// </summary>
        private static void SetupTextureAsSprite(string assetPath, float pixelsPerUnit)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (Mathf.Abs(importer.spritePixelsPerUnit - pixelsPerUnit) > 0.01f)
            {
                importer.spritePixelsPerUnit = pixelsPerUnit;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// 按 shapeRows 占格尺寸缩放并居中图片。
        /// </summary>
        private static void FitSpriteToShape(Transform visual, Sprite sprite, string[] shapeRows)
        {
            int width = GetShapeWidth(shapeRows);
            int height = Mathf.Max(1, shapeRows == null ? 1 : shapeRows.Length);
            visual.localPosition = new Vector3(width * 0.5f, height * 0.5f, 0f);

            Vector2 spriteSize = sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
            {
                return;
            }

            float scale = Mathf.Min(width / spriteSize.x, height / spriteSize.y);
            visual.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// 根据资源规则解析 prefab 资源路径。
        /// </summary>
        private static string ResolvePrefabAssetPath(ResourceRuleRow rule, CommonResource resource)
        {
            string localPath = ApplyResourceTemplate(rule.localPathTemplate, resource);
            return ToGeneratedAssetPath(localPath, ".prefab");
        }

        /// <summary>
        /// 根据资源规则解析 Sprite 资源路径。
        /// </summary>
        private static string ResolveSpriteAssetPath(ResourceRuleRow rule, CommonResource resource)
        {
            string localPath = ApplyResourceTemplate(rule.localPathTemplate, resource);
            return ToGeneratedAssetPath(localPath, ".png");
        }

        /// <summary>
        /// 判断图片是否已经在工具生成目录里。
        /// </summary>
        private static bool IsGeneratedResourcePath(string assetPath)
        {
            return assetPath.StartsWith(GeneratedFurnitureRoot + "/", StringComparison.OrdinalIgnoreCase)
                || assetPath.StartsWith(GeneratedSpriteRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 把配表本地路径转换为 Unity 资源路径。
        /// </summary>
        private static string ToGeneratedAssetPath(string localPath, string extension)
        {
            if (string.IsNullOrEmpty(localPath))
            {
                return string.Empty;
            }

            string assetPath = localPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? localPath
                : "Assets/Resources/TryGameBuildRes/" + localPath.TrimStart('/');
            return assetPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? assetPath : assetPath + extension;
        }

        /// <summary>
        /// 应用资源模板里的 MainId/SubId 占位符。
        /// </summary>
        private static string ApplyResourceTemplate(string template, CommonResource resource)
        {
            if (string.IsNullOrEmpty(template) || resource.IsEmpty())
            {
                return string.Empty;
            }

            return template
                .Replace("{MainId}", resource.mainId.ToString())
                .Replace("{SubId}", resource.subId.ToString());
        }

        /// <summary>
        /// 解析资源结构体 JSON。
        /// </summary>
        private static CommonResource ParseResource(string value)
        {
            Match match = ResourceJsonRegex.Match(value ?? string.Empty);
            if (!match.Success)
            {
                return CommonResource.Empty;
            }

            return new CommonResource(ParseInt(match.Groups["main"].Value), ParseInt(match.Groups["sub"].Value));
        }

        /// <summary>
        /// 解析字符串整数。
        /// </summary>
        private static int ParseInt(string value)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        /// <summary>
        /// 读取图标 ID。新表字段是 iconId；旧表字段 iconResourceId 暂时兼容。
        /// </summary>
        private static CommonResource ReadIconResource(Dictionary<string, string> row)
        {
            return ParseResource(GetValue(row, "icon"));
        }

        /// <summary>
        /// 读取字典里的字段值。
        /// </summary>
        private static string GetValue(Dictionary<string, string> row, string key)
        {
            return row != null && row.TryGetValue(key, out string value) ? value : string.Empty;
        }

        /// <summary>
        /// 拆分 shapeRows 字段。
        /// </summary>
        private static string[] SplitShapeRows(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? new[] { "1" } : value.Split('|');
        }

        /// <summary>
        /// 获取 shapeRows 的最大宽度。
        /// </summary>
        private static int GetShapeWidth(string[] shapeRows)
        {
            int width = 1;
            if (shapeRows == null)
            {
                return width;
            }

            for (int i = 0; i < shapeRows.Length; i++)
            {
                width = Mathf.Max(width, string.IsNullOrEmpty(shapeRows[i]) ? 0 : shapeRows[i].Length);
            }

            return width;
        }

        /// <summary>
        /// 标准化名字，用于配表家具名称和图片文件名匹配。
        /// </summary>
        private static string NormalizeName(string value)
        {
            return (value ?? string.Empty)
                .Replace("基础", string.Empty)
                .Replace("家具", string.Empty)
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Trim()
                .ToLowerInvariant();
        }

        /// <summary>
        /// 从字典里读取资源行。
        /// </summary>
        private static bool TryGetResource<T>(Dictionary<int, T> rows, int id, out T row)
        {
            return rows.TryGetValue(id, out row);
        }

        /// <summary>
        /// 把项目相对路径转为完整路径。
        /// </summary>
        private static string ToFullPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath)).Replace("\\", "/");
        }

        private sealed class HomeFurnitureRow
        {
            public int id;
            public string nameKey;
            public string displayName;
            public int resourceId;
            public CommonResource icon;
            public string[] shapeRows;
        }

        private sealed class FurnitureResourceRow
        {
            public int id;
            public int ruleId;
            public CommonResource resource;
        }

        private sealed class ResourceRuleRow
        {
            public int id;
            public string resourceType;
            public string localPathTemplate;
        }

        private struct CommonResource
        {
            public static readonly CommonResource Empty = new CommonResource(0, 0);
            public readonly int mainId;
            public readonly int subId;

            /// <summary>
            /// 创建一个资源编号。
            /// </summary>
            public CommonResource(int mainId, int subId)
            {
                this.mainId = mainId;
                this.subId = subId;
            }

            /// <summary>
            /// 判断当前资源编号是否为空。
            /// </summary>
            public bool IsEmpty()
            {
                return mainId <= 0 || subId <= 0;
            }
        }

        private static class XlsxSheetReader
        {
            /// <summary>
            /// 按 cltabtoy 普通表格式读取 sheet 数据。
            /// </summary>
            public static List<Dictionary<string, string>> ReadTable(string assetPath, string sheetName, int dataStartRow)
            {
                List<List<string>> rows = ReadRows(assetPath, sheetName);
                List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
                if (rows.Count == 0)
                {
                    return result;
                }

                List<string> headers = rows[0];
                for (int i = dataStartRow; i < rows.Count; i++)
                {
                    Dictionary<string, string> row = new Dictionary<string, string>();
                    for (int column = 0; column < headers.Count; column++)
                    {
                        string header = headers[column];
                        if (string.IsNullOrWhiteSpace(header))
                        {
                            continue;
                        }

                        row[header] = column < rows[i].Count ? rows[i][column] : string.Empty;
                    }

                    result.Add(row);
                }

                return result;
            }

            /// <summary>
            /// 从 xlsx 文件读取指定 sheet 的所有行。
            /// </summary>
            private static List<List<string>> ReadRows(string assetPath, string sheetName)
            {
                using (ZipArchive archive = ZipFile.OpenRead(ToFullPath(assetPath)))
                {
                    List<string> sharedStrings = ReadSharedStrings(archive);
                    Dictionary<string, string> workbookRels = ReadWorkbookRelationships(archive);
                    ZipArchiveEntry workbookEntry = archive.GetEntry("xl/workbook.xml");
                    if (workbookEntry == null)
                    {
                        return new List<List<string>>();
                    }

                    XmlDocument workbook = LoadXml(workbookEntry);
                    XmlNamespaceManager ns = CreateSpreadsheetNamespace(workbook);
                    XmlNode sheetNode = workbook.SelectSingleNode("//x:sheet[@name='" + sheetName + "']", ns);
                    if (sheetNode == null)
                    {
                        return new List<List<string>>();
                    }

                    XmlAttribute ridAttr = sheetNode.Attributes["id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"];
                    if (ridAttr == null || !workbookRels.TryGetValue(ridAttr.Value, out string target))
                    {
                        return new List<List<string>>();
                    }

                    string sheetPath = target.Replace("\\", "/").TrimStart('/');
                    if (!sheetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                    {
                        sheetPath = "xl/" + sheetPath;
                    }

                    ZipArchiveEntry sheetEntry = archive.GetEntry(sheetPath);
                    return sheetEntry == null ? new List<List<string>>() : ReadSheetRows(sheetEntry, sharedStrings);
                }
            }

            /// <summary>
            /// 读取 xlsx 的共享字符串。
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
                XmlNamespaceManager ns = CreateSpreadsheetNamespace(doc);
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
            /// 读取 workbook 和 sheet 文件之间的关系。
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
            /// 读取 sheetData 的所有行。
            /// </summary>
            private static List<List<string>> ReadSheetRows(ZipArchiveEntry sheetEntry, List<string> sharedStrings)
            {
                List<List<string>> rows = new List<List<string>>();
                XmlDocument doc = LoadXml(sheetEntry);
                XmlNamespaceManager ns = CreateSpreadsheetNamespace(doc);
                XmlNodeList rowNodes = doc.SelectNodes("//x:sheetData/x:row", ns);
                for (int i = 0; i < rowNodes.Count; i++)
                {
                    List<string> row = new List<string>();
                    XmlNodeList cellNodes = rowNodes[i].SelectNodes("x:c", ns);
                    for (int j = 0; j < cellNodes.Count; j++)
                    {
                        XmlNode cellNode = cellNodes[j];
                        int columnIndex = GetColumnIndex(cellNode.Attributes["r"] == null ? string.Empty : cellNode.Attributes["r"].Value);
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
            /// 读取单元格文本。
            /// </summary>
            private static string ReadCellValue(XmlNode cellNode, List<string> sharedStrings, XmlNamespaceManager ns)
            {
                string type = cellNode.Attributes["t"] == null ? string.Empty : cellNode.Attributes["t"].Value;
                if (type == "inlineStr")
                {
                    XmlNode inlineText = cellNode.SelectSingleNode(".//x:t", ns);
                    return inlineText == null ? string.Empty : inlineText.InnerText;
                }

                XmlNode valueNode = cellNode.SelectSingleNode("x:v", ns);
                if (valueNode == null)
                {
                    return string.Empty;
                }

                if (type == "s")
                {
                    int index;
                    return int.TryParse(valueNode.InnerText, out index) && index >= 0 && index < sharedStrings.Count
                        ? sharedStrings[index]
                        : string.Empty;
                }

                return valueNode.InnerText;
            }

            /// <summary>
            /// 将 Excel 单元格引用转换为零基列索引。
            /// </summary>
            private static int GetColumnIndex(string cellRef)
            {
                int result = 0;
                for (int i = 0; i < cellRef.Length; i++)
                {
                    char c = cellRef[i];
                    if (c < 'A' || c > 'Z')
                    {
                        break;
                    }

                    result = result * 26 + c - 'A' + 1;
                }

                return Mathf.Max(0, result - 1);
            }

            /// <summary>
            /// 加载 xlsx 内部 XML。
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
            /// 创建表格 XML 命名空间。
            /// </summary>
            private static XmlNamespaceManager CreateSpreadsheetNamespace(XmlDocument doc)
            {
                XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                return ns;
            }
        }
    }
}
