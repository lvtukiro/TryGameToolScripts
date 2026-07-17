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
        private const string ExcelRoot = "RefDataSource/TryGameRefdataRes/v2";
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
        private static Dictionary<string, TextureImporterSnapshot> activeImporterSnapshots;

        /// <summary>
        /// 从配表读取家具列表，为还没有 prefab 的家具生成资源。
        /// </summary>
        [MenuItem("TryGame/Placeholder/按配表生成缺失家具 Prefab")]
        public static void GenerateMissingFromConfig()
        {
            try
            {
                List<HomeFurnitureRow> furnitureRows = LoadFurnitureRows();
                Dictionary<int, FurnitureResourceRow> resourceRows = LoadFurnitureResources();
                Dictionary<int, ResourceRuleRow> ruleRows = LoadResourceRules();
                Dictionary<string, List<string>> imagesByName = FindSourceImages();
                if (!TryBuildGenerationPlan(furnitureRows, resourceRows, ruleRows, imagesByName, out List<GenerationPlanItem> plan))
                {
                    Debug.LogError("[TryGameFurniturePlaceholderPrefabGenerator] 生成计划存在错误，未修改任何资产。请修复此前日志后重试。");
                    return;
                }

                if (plan.Count == 0)
                {
                    Debug.Log("[TryGameFurniturePlaceholderPrefabGenerator] 没有缺失的家具图标或 Prefab，无需生成。");
                    return;
                }

                int iconCount = plan.FindAll(item => item.copyIcon).Count;
                int prefabCount = plan.FindAll(item => item.generatePrefab).Count;
                Debug.Log($"[TryGameFurniturePlaceholderPrefabGenerator] 生成计划：items={plan.Count}, copyIcons={iconCount}, prefabs={prefabCount}");
                for (int i = 0; i < plan.Count; i++)
                {
                    GenerationPlanItem item = plan[i];
                    Debug.Log($"[TryGameFurniturePlaceholderPrefabGenerator] PLAN furnitureId={item.furniture.id}, copyIcon={item.copyIcon}, source={item.sourceImagePath}, icon={item.iconAssetPath}, generatePrefab={item.generatePrefab}, prefab={item.prefabAssetPath}");
                }

                if (!EditorUtility.DisplayDialog("确认生成家具占位资源", $"计划复制 {iconCount} 张图标、生成 {prefabCount} 个 Prefab。\n\n完整路径已输出到 Console。是否执行？", "执行", "取消"))
                {
                    Debug.LogWarning("[TryGameFurniturePlaceholderPrefabGenerator] 用户取消执行，未修改任何资产。");
                    return;
                }

                ExecuteGenerationPlan(plan);
            }
            catch (Exception exception)
            {
                Debug.LogError("[TryGameFurniturePlaceholderPrefabGenerator] 构建或执行生成计划异常，流程已停止：\n" + exception);
            }
        }

        private static bool TryBuildGenerationPlan(List<HomeFurnitureRow> furnitureRows, Dictionary<int, FurnitureResourceRow> resourceRows, Dictionary<int, ResourceRuleRow> ruleRows, Dictionary<string, List<string>> imagesByName, out List<GenerationPlanItem> plan)
        {
            plan = new List<GenerationPlanItem>();
            bool valid = true;
            Dictionary<string, string> iconSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> prefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < furnitureRows.Count; i++)
            {
                HomeFurnitureRow furniture = furnitureRows[i];
                if (!TryGetResource(resourceRows, furniture.resourceId, out FurnitureResourceRow prefabResource) || !TryGetResource(ruleRows, prefabResource.ruleId, out ResourceRuleRow prefabRule))
                {
                    Debug.LogError($"[TryGameFurniturePlaceholderPrefabGenerator] 家具缺少 Prefab 资源配置：furnitureId={furniture.id}, resourceId={furniture.resourceId}");
                    valid = false;
                    continue;
                }

                string prefabAssetPath = ResolvePrefabAssetPath(prefabRule, prefabResource.resource);
                if (string.IsNullOrEmpty(prefabAssetPath))
                {
                    Debug.LogError($"[TryGameFurniturePlaceholderPrefabGenerator] Prefab 资源路径解析失败：furnitureId={furniture.id}, resourceId={furniture.resourceId}");
                    valid = false;
                    continue;
                }

                bool generatePrefab = !File.Exists(ToFullPath(prefabAssetPath));
                string iconAssetPath = furniture.icon.IsEmpty() ? string.Empty : string.Format(CultureInfo.InvariantCulture, CommonSpriteAssetPathFormat, furniture.icon.mainId, furniture.icon.subId);
                bool copyIcon = !string.IsNullOrEmpty(iconAssetPath) && !File.Exists(ToFullPath(iconAssetPath));
                if (!generatePrefab && !copyIcon) continue;
                string sourceImagePath = FindImageAssetPath(furniture, imagesByName, iconAssetPath, out string matchError);
                if (string.IsNullOrEmpty(sourceImagePath))
                {
                    Debug.LogError($"[TryGameFurniturePlaceholderPrefabGenerator] 家具图片匹配失败：furnitureId={furniture.id}, name={furniture.displayName}, reason={matchError}");
                    valid = false;
                    continue;
                }

                if (copyIcon && iconSources.TryGetValue(iconAssetPath, out string existingSource))
                {
                    if (!string.Equals(existingSource, sourceImagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogError($"[TryGameFurniturePlaceholderPrefabGenerator] 多个源图计划写入同一图标路径：icon={iconAssetPath}, sourceA={existingSource}, sourceB={sourceImagePath}");
                        valid = false;
                        continue;
                    }

                    copyIcon = false;
                }

                if (copyIcon) iconSources.Add(iconAssetPath, sourceImagePath);
                if (generatePrefab && !prefabPaths.Add(prefabAssetPath))
                {
                    Debug.LogError($"[TryGameFurniturePlaceholderPrefabGenerator] 多个家具计划生成同一 Prefab：furnitureId={furniture.id}, prefab={prefabAssetPath}");
                    valid = false;
                    continue;
                }

                plan.Add(new GenerationPlanItem { furniture = furniture, prefabResource = prefabResource, sourceImagePath = sourceImagePath, iconAssetPath = iconAssetPath, prefabAssetPath = prefabAssetPath, copyIcon = copyIcon, generatePrefab = generatePrefab });
            }

            return valid;
        }

        private static void ExecuteGenerationPlan(List<GenerationPlanItem> plan)
        {
            List<string> createdAssets = new List<string>();
            int iconCount = 0;
            int prefabCount = 0;
            activeImporterSnapshots = new Dictionary<string, TextureImporterSnapshot>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    GenerationPlanItem item = plan[i];
                    string imagePath = item.sourceImagePath;
                    if (item.copyIcon)
                    {
                        createdAssets.Add(item.iconAssetPath);
                        if (!TryCopyImageToIconPath(item.sourceImagePath, item.iconAssetPath)) throw new InvalidOperationException($"复制家具图标失败：furnitureId={item.furniture.id}, source={item.sourceImagePath}, target={item.iconAssetPath}");
                        imagePath = item.iconAssetPath;
                        iconCount++;
                    }
                    else if (!string.IsNullOrEmpty(item.iconAssetPath) && File.Exists(ToFullPath(item.iconAssetPath))) imagePath = item.iconAssetPath;

                    if (item.generatePrefab)
                    {
                        createdAssets.Add(item.prefabAssetPath);
                        if (!GeneratePrefab(item.furniture, item.prefabResource, imagePath, item.prefabAssetPath)) throw new InvalidOperationException($"生成家具 Prefab 失败：furnitureId={item.furniture.id}, prefab={item.prefabAssetPath}");
                        prefabCount++;
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[TryGameFurniturePlaceholderPrefabGenerator] 家具资源生成完成：prefabs={prefabCount}, icons={iconCount}");
                activeImporterSnapshots = null;
            }
            catch (Exception exception)
            {
                Debug.LogError("[TryGameFurniturePlaceholderPrefabGenerator] 执行生成计划失败，开始回滚本轮新建资产：\n" + exception);
                RollbackCreatedAssets(createdAssets);
                RestoreImporterSnapshots(activeImporterSnapshots);
                activeImporterSnapshots = null;
                throw;
            }
        }

        /// <summary>
        /// 读取 HomeFurniture 表数据。
        /// </summary>
        private static List<HomeFurnitureRow> LoadFurnitureRows()
        {
            string path = Path.Combine(ExcelRoot, FurnitureExcelName);
            List<Dictionary<string, string>> rows = XlsxSheetReader.ReadTable(path, FurnitureSheetName, 4);
            List<HomeFurnitureRow> result = new List<HomeFurnitureRow>();
            HashSet<int> ids = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, string> row = rows[i];
                int id = ParseInt(GetValue(row, "id"));
                if (id <= 0)
                {
                    continue;
                }

                if (!ids.Add(id))
                {
                    throw new InvalidDataException($"{FurnitureSheetName} 存在重复 id：{id}");
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

            if (result.Count == 0)
            {
                throw new InvalidDataException($"{FurnitureSheetName} 没有读取到任何有效家具行，拒绝按空表生成。");
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

                if (result.ContainsKey(id))
                {
                    throw new InvalidDataException($"{FurnitureResourceSheetName} 存在重复 id：{id}");
                }

                result.Add(id, new FurnitureResourceRow
                {
                    id = id,
                    ruleId = ParseInt(GetValue(row, "ruleId")),
                    resource = ParseResource(GetValue(row, "res")),
                });
            }

            if (result.Count == 0)
            {
                throw new InvalidDataException($"{FurnitureResourceSheetName} 没有读取到任何有效资源行，拒绝按空表生成。");
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

                if (result.ContainsKey(id))
                {
                    throw new InvalidDataException($"{ResourceRuleSheetName} 存在重复 id：{id}");
                }

                result.Add(id, new ResourceRuleRow
                {
                    id = id,
                    resourceType = GetValue(row, "resourceType"),
                    localPathTemplate = GetValue(row, "localPathTemplate"),
                });
            }

            if (result.Count == 0)
            {
                throw new InvalidDataException($"{ResourceRuleSheetName} 没有读取到任何有效规则行，拒绝按空表生成。");
            }

            return result;
        }

        /// <summary>
        /// 查找可用于家具生成的源图片。
        /// </summary>
        private static Dictionary<string, List<string>> FindSourceImages()
        {
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>();
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SourceImageRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (IsGeneratedResourcePath(path))
                {
                    continue;
                }

                string key = NormalizeName(Path.GetFileNameWithoutExtension(path));
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (!result.TryGetValue(key, out List<string> paths))
                {
                    paths = new List<string>();
                    result.Add(key, paths);
                }

                paths.Add(path);
            }

            return result;
        }

        /// <summary>
        /// 根据家具名称匹配图片资源。
        /// </summary>
        private static string FindImageAssetPath(HomeFurnitureRow furniture, Dictionary<string, List<string>> imagesByName, string iconAssetPath, out string error)
        {
            if (!string.IsNullOrEmpty(iconAssetPath) && File.Exists(ToFullPath(iconAssetPath)))
            {
                error = string.Empty;
                return iconAssetPath;
            }

            string displayName = NormalizeName(furniture.displayName);
            if (string.IsNullOrEmpty(displayName))
            {
                error = "家具显示名为空，无法匹配图片";
                return string.Empty;
            }

            if (imagesByName.TryGetValue(displayName, out List<string> exactPaths))
            {
                if (exactPaths.Count == 1) { error = string.Empty; return exactPaths[0]; }
                error = "精确名称匹配到多个图片：" + string.Join(",", exactPaths);
                return string.Empty;
            }

            List<string> candidates = new List<string>();
            foreach (KeyValuePair<string, List<string>> pair in imagesByName)
            {
                if (!displayName.Contains(pair.Key) && !pair.Key.Contains(displayName)) continue;
                for (int i = 0; i < pair.Value.Count; i++) if (!candidates.Contains(pair.Value[i])) candidates.Add(pair.Value[i]);
            }

            if (candidates.Count == 1) { error = string.Empty; return candidates[0]; }
            error = candidates.Count == 0 ? "没有名称候选" : "模糊名称匹配到多个图片，拒绝猜测：" + string.Join(",", candidates);
            return string.Empty;
        }

        /// <summary>
        /// 生成单个家具 prefab。
        /// </summary>
        private static bool GeneratePrefab(HomeFurnitureRow furniture, FurnitureResourceRow resource, string imageAssetPath, string prefabAssetPath)
        {
            Sprite sprite = LoadSprite(imageAssetPath);
            if (sprite == null)
            {
                Debug.LogError("[TryGameFurniturePlaceholderPrefabGenerator] 图片不是可用 Sprite，无法生成 Prefab：" + imageAssetPath);
                return false;
            }

            EnsureAssetFolder(Path.GetDirectoryName(prefabAssetPath));
            GameObject root = new GameObject($"furniture_{resource.resource.mainId}_{resource.resource.subId}");
            root.AddComponent<Game.FurnitureInstance>();

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 0;

            if (!FitSpriteToShape(visual.transform, sprite, furniture.shapeRows))
            {
                UnityEngine.Object.DestroyImmediate(root);
                return false;
            }

            GameObject savedPrefab = null;
            try { savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabAssetPath); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            if (savedPrefab == null || !File.Exists(ToFullPath(prefabAssetPath)))
            {
                Debug.LogError("[TryGameFurniturePlaceholderPrefabGenerator] Prefab 保存后校验失败：" + prefabAssetPath);
                return false;
            }

            Debug.Log("[TryGameFurniturePlaceholderPrefabGenerator] 已生成家具 Prefab：" + prefabAssetPath);
            return true;
        }

        /// <summary>
        /// 把源图片复制到图标资源路径，保留原始素材不动。
        /// </summary>
        private static bool TryCopyImageToIconPath(string imageAssetPath, string iconAssetPath)
        {
            if (imageAssetPath.Equals(iconAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                SetupTextureAsSprite(iconAssetPath, 100f);
                return AssetDatabase.LoadAssetAtPath<Sprite>(iconAssetPath) != null;
            }

            if (File.Exists(ToFullPath(iconAssetPath)))
            {
                SetupTextureAsSprite(iconAssetPath, 100f);
                return AssetDatabase.LoadAssetAtPath<Sprite>(iconAssetPath) != null;
            }

            EnsureAssetFolder(Path.GetDirectoryName(iconAssetPath));
            if (!AssetDatabase.CopyAsset(imageAssetPath, iconAssetPath))
            {
                Debug.LogError($"[TryGameFurniturePlaceholderPrefabGenerator] 复制家具图片失败：source={imageAssetPath}, target={iconAssetPath}");
                return false;
            }

            SetupTextureAsSprite(iconAssetPath, 100f);
            if (!File.Exists(ToFullPath(iconAssetPath)) || AssetDatabase.LoadAssetAtPath<Sprite>(iconAssetPath) == null)
            {
                Debug.LogError($"[TryGameFurniturePlaceholderPrefabGenerator] 家具图标复制后校验失败：source={imageAssetPath}, target={iconAssetPath}");
                return false;
            }

            Debug.Log($"[TryGameFurniturePlaceholderPrefabGenerator] 已复制家具图片到图标路径：source={imageAssetPath}, target={iconAssetPath}");
            return true;
        }

        private static void RollbackCreatedAssets(List<string> createdAssets)
        {
            for (int i = createdAssets.Count - 1; i >= 0; i--)
            {
                string assetPath = createdAssets[i];
                if (!File.Exists(ToFullPath(assetPath)) && AssetDatabase.LoadMainAssetAtPath(assetPath) == null) continue;
                if (!AssetDatabase.DeleteAsset(assetPath)) Debug.LogError("[TryGameFurniturePlaceholderPrefabGenerator] 回滚新建资产失败，请手动删除：" + assetPath);
                else Debug.LogWarning("[TryGameFurniturePlaceholderPrefabGenerator] 已回滚本轮新建资产：" + assetPath);
            }

            AssetDatabase.Refresh();
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

            CaptureImporterSnapshot(assetPath, importer);

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

        private static void CaptureImporterSnapshot(string assetPath, TextureImporter importer)
        {
            if (activeImporterSnapshots == null
                || importer == null
                || string.IsNullOrEmpty(assetPath)
                || activeImporterSnapshots.ContainsKey(assetPath))
            {
                return;
            }

            activeImporterSnapshots.Add(
                assetPath,
                new TextureImporterSnapshot(importer.textureType, importer.spritePixelsPerUnit));
        }

        private static void RestoreImporterSnapshots(Dictionary<string, TextureImporterSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, TextureImporterSnapshot> pair in snapshots)
            {
                TextureImporter importer = AssetImporter.GetAtPath(pair.Key) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                try
                {
                    importer.textureType = pair.Value.textureType;
                    importer.spritePixelsPerUnit = pair.Value.spritePixelsPerUnit;
                    importer.SaveAndReimport();
                    Debug.LogWarning($"[TryGameFurniturePlaceholderPrefabGenerator] 失败回滚已恢复图片导入设置：asset={pair.Key}, textureType={pair.Value.textureType}, pixelsPerUnit={pair.Value.spritePixelsPerUnit}");
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[TryGameFurniturePlaceholderPrefabGenerator] 失败回滚恢复图片导入设置失败，请手动检查：asset={pair.Key}\n{exception}");
                }
            }
        }

        /// <summary>
        /// 按 shapeRows 占格尺寸缩放并居中图片。
        /// </summary>
        private static bool FitSpriteToShape(Transform visual, Sprite sprite, string[] shapeRows)
        {
            int width = GetShapeWidth(shapeRows);
            int height = Mathf.Max(1, shapeRows == null ? 1 : shapeRows.Length);
            visual.localPosition = new Vector3(width * 0.5f, height * 0.5f, 0f);

            Vector2 spriteSize = sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
            {
                Debug.LogError($"[TryGameFurniturePlaceholderPrefabGenerator] Sprite 尺寸非法，无法适配家具占格：sprite={sprite.name}, size={spriteSize}");
                return false;
            }

            float scale = Mathf.Min(width / spriteSize.x, height / spriteSize.y);
            visual.localScale = new Vector3(scale, scale, 1f);
            return true;
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

        private sealed class GenerationPlanItem
        {
            public HomeFurnitureRow furniture;
            public FurnitureResourceRow prefabResource;
            public string sourceImagePath;
            public string iconAssetPath;
            public string prefabAssetPath;
            public bool copyIcon;
            public bool generatePrefab;
        }

        private sealed class TextureImporterSnapshot
        {
            public readonly TextureImporterType textureType;
            public readonly float spritePixelsPerUnit;

            public TextureImporterSnapshot(TextureImporterType textureType, float spritePixelsPerUnit)
            {
                this.textureType = textureType;
                this.spritePixelsPerUnit = spritePixelsPerUnit;
            }
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
                    throw new InvalidDataException($"Excel sheet 没有可读行：file={assetPath}, sheet={sheetName}");
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
                string fullPath = ToFullPath(assetPath);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("配表 Excel 不存在。", fullPath);
                }

                using (ZipArchive archive = ZipFile.OpenRead(fullPath))
                {
                    List<string> sharedStrings = ReadSharedStrings(archive);
                    Dictionary<string, string> workbookRels = ReadWorkbookRelationships(archive);
                    ZipArchiveEntry workbookEntry = archive.GetEntry("xl/workbook.xml");
                    if (workbookEntry == null)
                    {
                        throw new InvalidDataException("Excel 缺少 xl/workbook.xml：" + fullPath);
                    }

                    XmlDocument workbook = LoadXml(workbookEntry);
                    XmlNamespaceManager ns = CreateSpreadsheetNamespace(workbook);
                    XmlNode sheetNode = workbook.SelectSingleNode("//x:sheet[@name='" + sheetName + "']", ns);
                    if (sheetNode == null)
                    {
                        throw new InvalidDataException($"Excel 缺少目标 sheet：file={fullPath}, sheet={sheetName}");
                    }

                    XmlAttribute ridAttr = sheetNode.Attributes["id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"];
                    if (ridAttr == null || !workbookRels.TryGetValue(ridAttr.Value, out string target))
                    {
                        throw new InvalidDataException($"Excel 无法解析目标 sheet 关系：file={fullPath}, sheet={sheetName}, relationship={ridAttr?.Value ?? "<null>"}");
                    }

                    string sheetPath = target.Replace("\\", "/").TrimStart('/');
                    if (!sheetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                    {
                        sheetPath = "xl/" + sheetPath;
                    }

                    ZipArchiveEntry sheetEntry = archive.GetEntry(sheetPath);
                    if (sheetEntry == null)
                    {
                        throw new InvalidDataException($"Excel 缺少目标 sheet XML：file={fullPath}, sheet={sheetName}, path={sheetPath}");
                    }

                    return ReadSheetRows(sheetEntry, sharedStrings);
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
