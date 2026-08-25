#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Game.SequenceFrameAnimation;

namespace Game.EditorTools.SequenceFrameAnimation
{
    public static class SequenceFrameAnimationPrefabBuilder
    {
        public static void CreatePrefabInteractive()
        {
            string jsonAbsolutePath = EditorUtility.OpenFilePanel(
                "选择序列帧动作 JSON",
                Application.dataPath,
                "json");
            if (string.IsNullOrWhiteSpace(jsonAbsolutePath)
                || !TryGetProjectAssetPath(jsonAbsolutePath, out string jsonAssetPath))
            {
                return;
            }

            SequenceFrameAnimationDocument document = JsonUtility.FromJson<SequenceFrameAnimationDocument>(
                File.ReadAllText(jsonAbsolutePath));
            if (document == null)
            {
                EditorUtility.DisplayDialog("序列帧预制体", "动作 JSON 读取失败。", "确定");
                return;
            }

            string outputAbsolutePath = EditorUtility.SaveFilePanelInProject(
                "保存序列帧角色预制体",
                string.IsNullOrWhiteSpace(document.animationId)
                    ? "SequenceCharacterPreview"
                    : document.animationId + "Preview",
                "prefab",
                "选择预制体输出位置",
                "Assets");
            if (string.IsNullOrWhiteSpace(outputAbsolutePath))
            {
                return;
            }

            CreatePrefab(document, outputAbsolutePath);
        }

        public static void CreatePrefab(
            SequenceFrameAnimationDocument document,
            string prefabAssetPath)
        {
            if (document == null || document.bodyFrames == null || document.bodyFrames.Count == 0)
            {
                EditorUtility.DisplayDialog("序列帧预制体", "动作中没有身体序列帧。", "确定");
                return;
            }

            List<Sprite> bodySprites = LoadSprites(document.bodyFrames);
            if (bodySprites.Count != document.bodyFrames.Count)
            {
                EditorUtility.DisplayDialog(
                    "序列帧预制体",
                    "身体序列帧中有图片尚未导出或尚未导入 Unity。",
                    "确定");
                return;
            }

            List<Sprite> weaponSprites = new List<Sprite>();
            SequenceFrameLayerData weaponLayer = FindWeaponLayer(document);
            if (weaponLayer != null && weaponLayer.enabled)
            {
                weaponSprites = LoadSprites(weaponLayer.frameAssetPaths);
                if (weaponSprites.Count != weaponLayer.frameAssetPaths.Count)
                {
                    EditorUtility.DisplayDialog(
                        "序列帧预制体",
                        "武器序列帧中有图片尚未导出或尚未导入 Unity。",
                        "确定");
                    return;
                }
            }

            GameObject root = new GameObject(
                string.IsNullOrWhiteSpace(document.animationId)
                    ? "SequenceCharacter"
                    : document.animationId);
            GameObject bodyObject = new GameObject("BodyRenderer");
            bodyObject.transform.SetParent(root.transform, false);
            SpriteRenderer bodyRenderer = bodyObject.AddComponent<SpriteRenderer>();
            bodyRenderer.sortingOrder = 0;

            GameObject weaponObject = new GameObject("WeaponRenderer");
            weaponObject.transform.SetParent(root.transform, false);
            SpriteRenderer weaponRenderer = weaponObject.AddComponent<SpriteRenderer>();
            weaponRenderer.sortingOrder = weaponLayer == null ? 10 : weaponLayer.sortingOrder;

            SequenceFrameAnimationPlayer player = root.AddComponent<SequenceFrameAnimationPlayer>();
            SerializedObject serializedPlayer = new SerializedObject(player);
            serializedPlayer.FindProperty("bodyRenderer").objectReferenceValue = bodyRenderer;
            serializedPlayer.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
            SetSpriteArray(serializedPlayer.FindProperty("bodyFrames"), bodySprites);
            SetSpriteArray(serializedPlayer.FindProperty("weaponFrames"), weaponSprites);
            serializedPlayer.FindProperty("frameRate").floatValue = Mathf.Max(1f, document.frameRate);
            serializedPlayer.FindProperty("loop").boolValue = document.loop;
            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabAssetPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (saved == null)
            {
                EditorUtility.DisplayDialog("序列帧预制体", "预制体保存失败。", "确定");
                return;
            }

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            EditorUtility.DisplayDialog(
                "序列帧角色预制体已创建",
                "已生成：" + prefabAssetPath
                + "\n身体帧：" + bodySprites.Count
                + "\n武器帧：" + weaponSprites.Count,
                "确定");
        }

        private static List<Sprite> LoadSprites(List<SequenceFrameData> frames)
        {
            List<string> paths = new List<string>();
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] != null)
                {
                    paths.Add(frames[i].exportedAssetPath);
                }
            }

            return LoadSprites(paths);
        }

        private static List<Sprite> LoadSprites(List<string> assetPaths)
        {
            List<Sprite> result = new List<Sprite>();
            for (int i = 0; i < assetPaths.Count; i++)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPaths[i]);
                if (sprite == null)
                {
                    continue;
                }

                result.Add(sprite);
            }

            return result;
        }

        private static void SetSpriteArray(SerializedProperty property, List<Sprite> sprites)
        {
            property.arraySize = sprites.Count;
            for (int i = 0; i < sprites.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            }
        }

        private static SequenceFrameLayerData FindWeaponLayer(
            SequenceFrameAnimationDocument document)
        {
            if (document.layers == null)
            {
                return null;
            }

            for (int i = 0; i < document.layers.Count; i++)
            {
                if (document.layers[i] != null && document.layers[i].layerId == "weapon")
                {
                    return document.layers[i];
                }
            }

            return null;
        }

        private static bool TryGetProjectAssetPath(string absolutePath, out string assetPath)
        {
            assetPath = string.Empty;
            string fullPath = Path.GetFullPath(absolutePath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string assetsPath = Path.GetFullPath(Application.dataPath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(assetsPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, assetsPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            assetPath = "Assets" + fullPath.Substring(assetsPath.Length).Replace('\\', '/');
            return true;
        }
    }
}
#endif
