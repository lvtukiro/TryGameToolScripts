using System.IO;
using UnityEditor;
using UnityEngine;

namespace TryGame.PlaceholderTools.Editor
{
    /// <summary>
    /// 家具占位 prefab 生成工具。
    /// 只用于 1.0A 没有正式美术资源时，把当前几张家具表的 shapeRows 拼成可摆放的方块 prefab。
    /// </summary>
    public static class TryGameFurniturePlaceholderPrefabGenerator
    {
        private const string ResourceRoot = "Assets/TryGameBuildRes/Resources/BuildRes/home/furniture";
        private const string SpriteRoot = "Assets/TryGameBuildRes/Resources/BuildRes/gui/sprite";
        private const string SpriteAssetPath = "Assets/TryGameBuildRes/EditorGenerated/placeholder_block.png";

        private static readonly PlaceholderFurnitureSpec[] Specs =
        {
            new PlaceholderFurnitureSpec(1001, 1, new[] { "111", "101" }, new Color(0.76f, 0.54f, 0.34f, 1f)),
            new PlaceholderFurnitureSpec(1002, 1, new[] { "11", "11" }, new Color(0.42f, 0.68f, 0.86f, 1f)),
            new PlaceholderFurnitureSpec(1003, 1, new[] { "11" }, new Color(0.68f, 0.82f, 0.46f, 1f)),
        };

        /// <summary>
        /// 生成当前 1.0A 家具的占位 prefab。
        /// </summary>
        [MenuItem("TryGame/Placeholder/生成家具方块 Prefab")]
        public static void Generate()
        {
            Sprite blockSprite = LoadOrCreateBlockSprite();
            for (int i = 0; i < Specs.Length; i++)
            {
                GeneratePrefab(Specs[i], blockSprite);
                GenerateIconSprite(Specs[i]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TryGame] 家具占位 prefab 生成完成。记得让 FurnitureResource.res 对应 MainId/SubId。");
        }

        /// <summary>
        /// 生成单个家具 prefab。
        /// </summary>
        private static void GeneratePrefab(PlaceholderFurnitureSpec spec, Sprite blockSprite)
        {
            string folder = $"{ResourceRoot}/furniture_{spec.mainId}";
            Directory.CreateDirectory(folder);

            GameObject root = new GameObject($"furniture_{spec.mainId}_{spec.subId}");
            root.AddComponent<Game.FurnitureInstance>();

            GameObject visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(root.transform, false);

            for (int row = 0; row < spec.shapeRows.Length; row++)
            {
                string line = spec.shapeRows[row];
                int localY = spec.shapeRows.Length - 1 - row;
                for (int x = 0; x < line.Length; x++)
                {
                    if (line[x] != '1')
                    {
                        continue;
                    }

                    CreateCell(visualRoot.transform, blockSprite, spec.color, x, localY);
                }
            }

            string prefabPath = $"{folder}/furniture_{spec.mainId}_{spec.subId}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// 生成单个家具图标 Sprite。
        /// </summary>
        private static void GenerateIconSprite(PlaceholderFurnitureSpec spec)
        {
            string folder = $"{SpriteRoot}/spt_{spec.iconMainId}";
            Directory.CreateDirectory(folder);

            string assetPath = $"{folder}/spt_{spec.iconMainId}_{spec.subId}.png";
            Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = spec.color;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 64f;
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// 创建一个方块格子。
        /// </summary>
        private static void CreateCell(Transform parent, Sprite sprite, Color color, int x, int y)
        {
            GameObject cell = new GameObject($"Cell_{x}_{y}");
            cell.transform.SetParent(parent, false);
            cell.transform.localPosition = new Vector3(x + 0.5f, y + 0.5f, 0f);

            SpriteRenderer renderer = cell.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = y;
        }

        /// <summary>
        /// 加载或创建 1x1 方块 Sprite。
        /// </summary>
        private static Sprite LoadOrCreateBlockSprite()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteAssetPath);
            if (sprite != null)
            {
                return sprite;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(SpriteAssetPath));
            Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[32 * 32];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(SpriteAssetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(SpriteAssetPath);
            TextureImporter importer = AssetImporter.GetAtPath(SpriteAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 32f;
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteAssetPath);
        }

        private sealed class PlaceholderFurnitureSpec
        {
            public readonly int mainId;
            public readonly int iconMainId;
            public readonly int subId;
            public readonly string[] shapeRows;
            public readonly Color color;

            public PlaceholderFurnitureSpec(int mainId, int subId, string[] shapeRows, Color color)
            {
                this.mainId = mainId;
                iconMainId = mainId + 1000;
                this.subId = subId;
                this.shapeRows = shapeRows;
                this.color = color;
            }
        }
    }
}
