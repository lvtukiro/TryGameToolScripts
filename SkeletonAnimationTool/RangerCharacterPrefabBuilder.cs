#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.SkeletonAnimation
{
    /// <summary>
    /// Creates preview prefabs from an exported skeleton template.
    /// Parts are optional Sprite assets named after their bone id. For example:
    /// chest.png, head.png, arm_l.png. Each part should have its pivot at the
    /// corresponding bone anchor so it follows that bone correctly.
    /// </summary>
    public static class RangerCharacterPrefabBuilder
    {
        private const string RangerFolder = "Assets/BuildRes/Ranger";
        private const string TemplateAssetPath = RangerFolder + "/Ranger_template.json";
        private const string ReferenceAssetPath = RangerFolder + "/Ranger_reference.png";
        private const string PrefabAssetPath = RangerFolder + "/RangerPreview.prefab";

        [MenuItem("TryGame/Tools/Skeleton Animation Tool/Create Ranger Preview Prefab (Ranger快捷)")]
        public static void CreateRangerPreviewPrefab()
        {
            CreatePreviewPrefabFromPaths(
                TemplateAssetPath,
                RangerFolder,
                RangerFolder,
                "RangerPreview",
                true);
        }

        [MenuItem("TryGame/Tools/Skeleton Animation Tool/Create Character Preview Prefab...")]
        public static void CreateCharacterPreviewPrefab()
        {
            string templateAbsolutePath = EditorUtility.OpenFilePanel(
                "选择骨骼模板 JSON",
                Application.dataPath,
                "json");
            if (string.IsNullOrWhiteSpace(templateAbsolutePath))
            {
                return;
            }

            string templateAssetPath;
            if (!TryGetProjectAssetPath(templateAbsolutePath, out templateAssetPath))
            {
                return;
            }

            string partsAbsolutePath = EditorUtility.OpenFolderPanel(
                "选择角色透明部件目录（文件名对应 Bone ID）",
                Path.GetDirectoryName(templateAbsolutePath),
                string.Empty);
            if (string.IsNullOrWhiteSpace(partsAbsolutePath))
            {
                return;
            }

            string partsAssetFolder;
            if (!TryGetProjectAssetPath(partsAbsolutePath, out partsAssetFolder))
            {
                EditorUtility.DisplayDialog(
                    "角色预制体",
                    "角色部件目录必须位于当前 Unity 项目的 Assets 下。",
                    "确定");
                return;
            }

            string outputAbsoluteFolder = EditorUtility.OpenFolderPanel(
                "选择预制体输出目录（必须位于 Assets 下）",
                Path.GetDirectoryName(templateAbsolutePath),
                string.Empty);
            if (string.IsNullOrWhiteSpace(outputAbsoluteFolder))
            {
                return;
            }

            string outputAssetFolder;
            if (!TryGetProjectAssetPath(outputAbsoluteFolder, out outputAssetFolder))
            {
                EditorUtility.DisplayDialog(
                    "角色预制体",
                    "预制体输出目录必须位于当前 Unity 项目的 Assets 下。",
                    "确定");
                return;
            }

            string defaultName = Path.GetFileNameWithoutExtension(templateAbsolutePath);
            if (defaultName.EndsWith("_template", StringComparison.OrdinalIgnoreCase))
            {
                defaultName = defaultName.Substring(
                    0,
                    defaultName.Length - "_template".Length);
            }

            string prefabName = EditorUtility.SaveFilePanel(
                "设置预制体名称",
                outputAbsoluteFolder,
                MakeSafeFileName(defaultName + "Preview"),
                "prefab");
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                return;
            }

            string prefabAssetPath;
            if (!TryGetProjectAssetPath(prefabName, out prefabAssetPath))
            {
                EditorUtility.DisplayDialog(
                    "角色预制体",
                    "预制体输出路径必须位于当前 Unity 项目的 Assets 下。",
                    "确定");
                return;
            }

            CreatePreviewPrefabFromPaths(
                templateAssetPath,
                partsAssetFolder,
                outputAssetFolder,
                Path.GetFileNameWithoutExtension(prefabAssetPath),
                false,
                prefabAssetPath);
        }

        public static void CreatePreviewPrefabFromPaths(
            string templateAssetPath,
            string partsAssetFolder,
            string outputAssetFolder,
            string prefabName,
            bool useReferenceFallback,
            string explicitPrefabAssetPath = null)
        {
            if (string.IsNullOrWhiteSpace(templateAssetPath)
                || !File.Exists(ToAbsolutePath(templateAssetPath)))
            {
                EditorUtility.DisplayDialog(
                    "角色预制体",
                    "未找到骨骼模板：" + templateAssetPath,
                    "确定");
                return;
            }

            SkeletonTemplateDocument template = JsonUtility.FromJson<SkeletonTemplateDocument>(
                File.ReadAllText(ToAbsolutePath(templateAssetPath)));
            if (template == null || template.bones == null || template.bones.Count == 0)
            {
                EditorUtility.DisplayDialog("角色预制体", "模板中没有骨骼数据。", "确定");
                return;
            }

            if (template.sockets == null)
            {
                template.sockets = new List<SkeletonSocketData>();
            }

            if (template.visualParts == null)
            {
                template.visualParts = new List<SkeletonBoneVisualData>();
            }

            CreatePreviewPrefab(
                template,
                partsAssetFolder,
                outputAssetFolder,
                prefabName,
                useReferenceFallback,
                explicitPrefabAssetPath);
        }

        public static void CreatePreviewPrefabFromAssignments(
            SkeletonTemplateDocument template,
            string outputAssetPath)
        {
            CreatePreviewPrefab(
                template,
                "Assets",
                Path.GetDirectoryName(outputAssetPath),
                Path.GetFileNameWithoutExtension(outputAssetPath),
                false,
                outputAssetPath);
        }

        private static void CreatePreviewPrefab(
            SkeletonTemplateDocument template,
            string partsAssetFolder,
            string outputAssetFolder,
            string prefabName,
            bool useReferenceFallback,
            string explicitPrefabAssetPath)
        {
            if (template == null || template.bones == null || template.bones.Count == 0)
            {
                EditorUtility.DisplayDialog("角色预制体", "模板中没有骨骼数据。", "确定");
                return;
            }

            if (template.sockets == null)
            {
                template.sockets = new List<SkeletonSocketData>();
            }

            if (template.visualParts == null)
            {
                template.visualParts = new List<SkeletonBoneVisualData>();
            }

            EnsureFolder(outputAssetFolder);
            AssetDatabase.Refresh();

            string resolvedPrefabPath = explicitPrefabAssetPath;
            if (string.IsNullOrWhiteSpace(resolvedPrefabPath))
            {
                resolvedPrefabPath = outputAssetFolder.TrimEnd('/')
                    + "/" + MakeSafeFileName(prefabName) + ".prefab";
            }

            GameObject root = new GameObject(
                string.IsNullOrWhiteSpace(prefabName) ? "CharacterPreview" : prefabName);
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            Dictionary<string, Transform> boneTransforms = new Dictionary<string, Transform>();
            for (int i = 0; i < template.bones.Count; i++)
            {
                SkeletonBoneData bone = template.bones[i];
                if (bone == null || string.IsNullOrWhiteSpace(bone.boneId)
                    || boneTransforms.ContainsKey(bone.boneId))
                {
                    continue;
                }

                GameObject boneObject = new GameObject(bone.boneId);
                Transform parent = root.transform;
                if (!string.IsNullOrWhiteSpace(bone.parentBoneId)
                    && boneTransforms.TryGetValue(bone.parentBoneId, out Transform configuredParent))
                {
                    parent = configuredParent;
                }

                boneObject.transform.SetParent(parent, false);
                boneObject.transform.localPosition = new Vector3(
                    (bone.normalizedPosition.x - 0.5f) * 2f,
                    (0.5f - bone.normalizedPosition.y) * 2f,
                    0f);
                boneObject.transform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    -bone.rotationDegrees);
                boneTransforms.Add(bone.boneId, boneObject.transform);
            }

            for (int i = 0; i < template.sockets.Count; i++)
            {
                SkeletonSocketData socket = template.sockets[i];
                if (socket == null || string.IsNullOrWhiteSpace(socket.socketId)
                    || string.IsNullOrWhiteSpace(socket.parentBoneId)
                    || !boneTransforms.TryGetValue(socket.parentBoneId, out Transform parent))
                {
                    continue;
                }

                GameObject socketObject = new GameObject(socket.socketId);
                socketObject.transform.SetParent(parent, false);
                socketObject.transform.localPosition = new Vector3(
                    socket.normalizedOffset.x * 2f,
                    -socket.normalizedOffset.y * 2f,
                    -0.05f);
                socketObject.transform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    -socket.rotationDegrees);
            }

            Dictionary<string, Sprite> partSprites = LoadPartSprites(
                partsAssetFolder,
                template.visualParts);
            int generatedPartCount = 0;
            List<string> missingParts = new List<string>();
            for (int i = 0; i < template.bones.Count; i++)
            {
                SkeletonBoneData bone = template.bones[i];
                if (bone == null || string.IsNullOrWhiteSpace(bone.boneId)
                    || !boneTransforms.TryGetValue(bone.boneId, out Transform boneTransform))
                {
                    continue;
                }

                if (partSprites.TryGetValue(NormalizeKey(bone.boneId), out Sprite partSprite))
                {
                    GameObject visual = new GameObject("Visual_" + bone.boneId);
                    visual.transform.SetParent(boneTransform, false);
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.identity;
                    SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
                    renderer.sprite = partSprite;
                    renderer.sortingOrder = i;
                    generatedPartCount++;
                }
                else
                {
                    missingParts.Add(bone.boneId);
                }
            }

            if (useReferenceFallback && generatedPartCount == 0)
            {
                GameObject visual = new GameObject("Visual_Placeholder_ReplaceWithParts");
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0f, 0.1f);
                SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
                Sprite referenceSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ReferenceAssetPath);
                if (referenceSprite != null)
                {
                    renderer.sprite = referenceSprite;
                    renderer.sortingOrder = 0;
                    FitPlaceholderSprite(renderer, referenceSprite);
                }
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, resolvedPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (saved == null)
            {
                EditorUtility.DisplayDialog("角色预制体", "预制体保存失败。", "确定");
                return;
            }

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            EditorUtility.DisplayDialog(
                "角色预制体已创建",
                "已生成：" + resolvedPrefabPath
                + "\n\n已挂载透明部件：" + generatedPartCount + " 个。"
                + (missingParts.Count == 0
                    ? string.Empty
                    : "\n未找到部件（可后续补齐）：" + string.Join(", ", missingParts)),
                "确定");
        }

        private static Dictionary<string, Sprite> LoadPartSprites(
            string assetFolder,
            List<SkeletonBoneVisualData> configuredParts)
        {
            Dictionary<string, Sprite> result = new Dictionary<string, Sprite>();
            if (string.IsNullOrWhiteSpace(assetFolder) || !AssetDatabase.IsValidFolder(assetFolder))
            {
                return result;
            }

            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { assetFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    continue;
                }

                string key = NormalizeKey(Path.GetFileNameWithoutExtension(path));
                if (!result.ContainsKey(key))
                {
                    result.Add(key, sprite);
                }
            }

            if (configuredParts != null)
            {
                for (int i = 0; i < configuredParts.Count; i++)
                {
                    SkeletonBoneVisualData configured = configuredParts[i];
                    if (configured == null || string.IsNullOrWhiteSpace(configured.boneId)
                        || string.IsNullOrWhiteSpace(configured.assetPath))
                    {
                        continue;
                    }

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(configured.assetPath);
                    if (sprite != null)
                    {
                        result[NormalizeKey(configured.boneId)] = sprite;
                    }
                }
            }

            return result;
        }

        private static void FitPlaceholderSprite(SpriteRenderer renderer, Sprite sprite)
        {
            if (renderer == null || sprite == null)
            {
                return;
            }

            float maxDimension = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            if (maxDimension > 0f)
            {
                float scale = 1.8f / maxDimension;
                renderer.transform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool TryGetProjectAssetPath(string absolutePath, out string assetPath)
        {
            assetPath = string.Empty;
            string fullPath = Path.GetFullPath(absolutePath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string fullAssets = Path.GetFullPath(Application.dataPath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(fullAssets + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, fullAssets, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            assetPath = "Assets" + fullPath.Substring(fullAssets.Length).Replace('\\', '/');
            return true;
        }

        private static string NormalizeKey(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string MakeSafeFileName(string value)
        {
            string safe = string.IsNullOrWhiteSpace(value) ? "CharacterPreview" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(invalid.ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(safe) ? "CharacterPreview" : safe;
        }

        private static void EnsureFolder(string assetFolder)
        {
            string absolute = ToAbsolutePath(assetFolder);
            if (!Directory.Exists(absolute))
            {
                Directory.CreateDirectory(absolute);
            }
        }
    }
}
#endif
