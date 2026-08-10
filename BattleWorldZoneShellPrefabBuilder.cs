#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// 幂等生成 2.0g Battle WorldZone 场景根、Normal HUD、Home_01 绑定和正式彩色占位 Sprite。
    /// 已存在的 Sprite 视为用户资源，绝不覆盖；后续替图不会被自动 Builder 擦掉。
    /// </summary>
    public static class BattleWorldZoneShellPrefabBuilder
    {
        private const string MenuPath =
            "TryGame/Battle WorldZone/Rebuild 2.0g Runtime";
        private const string ScenePrefabPath =
            "Assets/Resources/TryGameBuildRes/battle/runtime/battle_world_zone_shell.prefab";
        private const string UiPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_world_zone.prefab";
        private const string AreaMapUiPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_world_zone_map.prefab";
        private const string HomeScenePath =
            "Assets/Resources/TryGameBuildRes/scene/Home_01.unity";
        private const string SpriteRoot =
            "Assets/Resources/TryGameBuildRes/gui/sprite";
        private const string SceneMarker = "__BattleWorldZoneShell_v2_0g";
        private const string UiMarker = "__BattleWorldZoneUi_v2_0g_6";
        private const string AreaMapUiMarker = "__BattleWorldZoneAreaMapUi_v2_0g_1";

        private const string ScenePresentationType =
            "Game.BattleWorldZoneScenePresentation";
        private const string SceneRuntimeType =
            "Game.BattleWorldZoneSceneRuntime";
        private const string UiMonoType = "Game.GUIMonoBattleWorldZone";
        private const string MinimapViewType =
            "Game.BattleWorldZoneMinimapView";
        private const string AreaMapUiMonoType =
            "Game.GUIMonoBattleWorldZoneMap";
        private const string AreaMapViewType =
            "Game.BattleWorldZoneAreaMapView";
        private const string AreaMapNodeViewType =
            "Game.BattleWorldZoneAreaMapNodeView";
        private const string HomeCameraControllerType =
            "Game.HomeSceneCameraController";
        private const string BootstrapType = "Game.TryGameRuntimeBootstrap";

        private static readonly int[] PlaceholderResourceIds =
        {
            6001, 6002,
            6011, 6012, 6013,
            6021, 6022, 6023,
            6101, 6102, 6103, 6104, 6105, 6106, 6107,
            6201, 6202, 6203, 6204,
            6301, 6302, 6303,
            6401, 6402,
            6501, 6502,
        };

        private static readonly string[] RequiredRuntimeTypes =
        {
            ScenePresentationType,
            SceneRuntimeType,
            UiMonoType,
            MinimapViewType,
            AreaMapUiMonoType,
            AreaMapViewType,
            AreaMapNodeViewType,
            HomeCameraControllerType,
            BootstrapType,
        };

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureBuilt()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall -= EnsureBuiltAfterReload;
            EditorApplication.delayCall += EnsureBuiltAfterReload;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall -= EnsureBuiltAfterReload;
                EditorApplication.delayCall += EnsureBuiltAfterReload;
            }
        }

        [MenuItem(MenuPath, false, 430)]
        public static void RebuildAll()
        {
            BuildAll(true);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateRebuildAll()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating;
        }

        private static void EnsureBuiltAfterReload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall -= EnsureBuiltAfterReload;
                EditorApplication.delayCall += EnsureBuiltAfterReload;
                return;
            }
            if (!RuntimeTypesAreReady() || !NeedsBuild())
            {
                return;
            }
            try
            {
                BuildAll(false);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[BattleWorldZoneShellPrefabBuilder] 自动生成 2.0g 资源失败。" +
                    $"修复编译后可手动执行 {MenuPath}。\n{exception}");
            }
        }

        private static bool RuntimeTypesAreReady()
        {
            return BattlePreparationEditorUiFactory.AreRuntimeTypesAvailable(
                RequiredRuntimeTypes);
        }

        private static bool NeedsBuild()
        {
            GameObject scenePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ScenePrefabPath);
            GameObject uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                UiPrefabPath);
            GameObject areaMapUiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AreaMapUiPrefabPath);
            if (scenePrefab == null
                || uiPrefab == null
                || areaMapUiPrefab == null
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    scenePrefab,
                    SceneMarker)
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    uiPrefab,
                    UiMarker)
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    areaMapUiPrefab,
                    AreaMapUiMarker)
                || !TryValidateHomeSceneBindings(out _))
            {
                return true;
            }
            for (int index = 0; index < PlaceholderResourceIds.Length; index++)
            {
                if (AssetDatabase.LoadAssetAtPath<Sprite>(
                        GetSpriteAssetPath(PlaceholderResourceIds[index])) == null)
                {
                    return true;
                }
            }
            return false;
        }

        private static void BuildAll(bool logSuccess)
        {
            if (!RuntimeTypesAreReady())
            {
                throw new InvalidOperationException(
                    "Battle WorldZone runtime types are not compiled yet.");
            }

            EnsurePlaceholderSpriteAssets();
            BuildScenePrefab();
            BuildUiPrefab();
            BuildAreaMapUiPrefab();
            BindHomeSceneRuntime();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateGeneratedAssets();
            if (logSuccess)
            {
                Debug.Log(
                    "[BattleWorldZoneShellPrefabBuilder] 2.0g 场景、HUD、彩色占位资源和 Home_01 绑定已生成。");
            }
        }

        private static void EnsurePlaceholderSpriteAssets()
        {
            for (int index = 0; index < PlaceholderResourceIds.Length; index++)
            {
                int resourceId = PlaceholderResourceIds[index];
                string assetPath = GetSpriteAssetPath(resourceId);
                if (!File.Exists(ToAbsoluteProjectPath(assetPath)))
                {
                    CreatePlaceholderPng(resourceId, assetPath);
                }
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport
                        | ImportAssetOptions.ForceUpdate);
                ConfigureSpriteImporter(resourceId, assetPath);
            }
        }

        private static void CreatePlaceholderPng(int resourceId, string assetPath)
        {
            ResolveTextureSize(resourceId, out int width, out int height);
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = $"spt_{resourceId}_1";
            texture.filterMode = FilterMode.Point;
            Color32[] pixels = BuildPixels(resourceId, width, height);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            string absolutePath = ToAbsoluteProjectPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static Color32[] BuildPixels(int resourceId, int width, int height)
        {
            Color32[] pixels = new Color32[width * height];
            Color baseColor = Palette(resourceId, 0);
            Color accent = Palette(resourceId, 1);
            Color dark = Color.Lerp(baseColor, Color.black, 0.62f);
            bool transparent = resourceId >= 6011 && resourceId < 6100
                || resourceId >= 6202 && resourceId != 6204;
            Fill(pixels, transparent ? new Color(0f, 0f, 0f, 0f) : dark);

            if (resourceId >= 6101 && resourceId <= 6107)
            {
                DrawGradientBackground(pixels, width, height, baseColor, accent, resourceId == 6107);
            }
            else if (resourceId == 6201)
            {
                DrawFloorTile(pixels, width, height, baseColor, accent);
            }
            else if (resourceId == 6202)
            {
                DrawLadder(pixels, width, height, baseColor, accent);
            }
            else if (resourceId == 6203)
            {
                DrawDoor(pixels, width, height, baseColor, accent);
            }
            else if (resourceId == 6204)
            {
                DrawFloorFillTile(pixels, width, height, baseColor, accent);
            }
            else if (resourceId >= 6301 && resourceId <= 6303)
            {
                DrawCrate(pixels, width, height, baseColor, accent, resourceId - 6301);
            }
            else if (resourceId >= 6401 && resourceId <= 6402)
            {
                DrawExtraction(pixels, width, height, baseColor, accent, resourceId == 6402);
            }
            else if (resourceId >= 6501 && resourceId <= 6502)
            {
                DrawRobot(pixels, width, height, baseColor, accent, resourceId == 6502);
            }
            else
            {
                DrawUiBadge(pixels, width, height, baseColor, accent, resourceId);
            }
            return pixels;
        }

        private static void DrawGradientBackground(
            Color32[] pixels,
            int width,
            int height,
            Color baseColor,
            Color accent,
            bool boss)
        {
            for (int y = 0; y < height; y++)
            {
                float t = (float)y / Mathf.Max(1, height - 1);
                Color row = Color.Lerp(Color.Lerp(baseColor, Color.black, 0.72f), Color.Lerp(baseColor, Color.black, 0.25f), t);
                DrawRect(pixels, width, height, 0, y, width, 1, row);
            }
            for (int x = 0; x < width; x += Mathf.Max(32, width / 12))
            {
                DrawRect(pixels, width, height, x, 0, 4, height, new Color(accent.r, accent.g, accent.b, 0.24f));
            }
            int horizon = Mathf.RoundToInt(height * 0.32f);
            DrawRect(pixels, width, height, 0, horizon, width, 6, accent);
            for (int x = 24; x < width; x += 96)
            {
                DrawRect(pixels, width, height, x, horizon + 12, 58, 42, Color.Lerp(baseColor, Color.black, 0.35f));
                DrawRect(pixels, width, height, x + 8, horizon + 21, 42, 6, accent);
            }
            if (boss)
            {
                DrawCircle(pixels, width, height, width / 2, height * 2 / 3, height / 5, new Color(accent.r, accent.g, accent.b, 0.4f));
            }
        }

        private static void DrawFloorTile(Color32[] p, int w, int h, Color baseColor, Color accent)
        {
            Fill(p, Color.Lerp(baseColor, Color.black, 0.38f));
            DrawRect(p, w, h, 0, h - 10, w, 10, accent);
            DrawRect(p, w, h, 0, 0, w, 5, Color.Lerp(baseColor, Color.black, 0.72f));
            for (int x = 12; x < w; x += 28) DrawCircle(p, w, h, x, h / 2, 3, accent);
            DrawRect(p, w, h, w / 2 - 2, 0, 4, h, Color.Lerp(baseColor, Color.black, 0.55f));
        }

        private static void DrawFloorFillTile(
            Color32[] p,
            int w,
            int h,
            Color baseColor,
            Color accent)
        {
            Color soil = Color.Lerp(baseColor, Color.black, 0.46f);
            Color darkSoil = Color.Lerp(baseColor, Color.black, 0.64f);
            Fill(p, soil);
            for (int y = 14; y < h; y += 31)
            {
                int offset = (y / 31 & 1) == 0 ? 13 : 29;
                for (int x = offset; x < w; x += 37)
                {
                    DrawCircle(p, w, h, x, y, 3, darkSoil);
                    DrawRect(p, w, h, x + 5, y - 1, 8, 2, accent);
                }
            }
        }

        private static void DrawLadder(Color32[] p, int w, int h, Color baseColor, Color accent)
        {
            int rail = Mathf.Max(5, w / 10);
            DrawRect(p, w, h, w / 5, 0, rail, h, baseColor);
            DrawRect(p, w, h, w - w / 5 - rail, 0, rail, h, baseColor);
            for (int y = 8; y < h; y += Mathf.Max(12, h / 8))
                DrawRect(p, w, h, w / 5, y, w * 3 / 5, rail, accent);
        }

        private static void DrawDoor(Color32[] p, int w, int h, Color baseColor, Color accent)
        {
            int border = Mathf.Max(8, w / 10);
            DrawRect(p, w, h, border, border, w - border * 2, h - border, new Color(accent.r, accent.g, accent.b, 0.35f));
            DrawRect(p, w, h, 0, 0, border, h, baseColor);
            DrawRect(p, w, h, w - border, 0, border, h, baseColor);
            DrawRect(p, w, h, 0, h - border, w, border, accent);
            for (int y = border; y < h - border; y += border * 2)
                DrawRect(p, w, h, w / 2 - 2, y, 4, border, accent);
        }

        private static void DrawCrate(Color32[] p, int w, int h, Color baseColor, Color accent, int variant)
        {
            DrawRect(p, w, h, 8, 8, w - 16, h - 16, baseColor);
            DrawRect(p, w, h, 8, h - 24, w - 16, 16, accent);
            DrawRect(p, w, h, w / 2 - 6, 8, 12, h - 16, Color.Lerp(baseColor, Color.black, 0.5f));
            if (variant == 0)
                DrawRect(p, w, h, 20, h / 2 - 6, w - 40, 12, accent);
            else if (variant == 1)
                for (int x = 22; x < w - 12; x += 24) DrawCircle(p, w, h, x, h / 2, 6, accent);
            else
                DrawCircle(p, w, h, w / 2, h / 2, Mathf.Min(w, h) / 5, accent);
        }

        private static void DrawExtraction(Color32[] p, int w, int h, Color baseColor, Color accent, bool red)
        {
            Color glow = red ? new Color(1f, 0.18f, 0.2f, 0.9f) : accent;
            int radius = Mathf.Min(w, h) / 2 - 8;
            DrawCircle(p, w, h, w / 2, h / 2, radius, new Color(glow.r, glow.g, glow.b, 0.28f));
            DrawRing(p, w, h, w / 2, h / 2, radius, Mathf.Max(5, radius / 7), glow);
            DrawRect(p, w, h, w / 2 - 5, 10, 10, h - 20, baseColor);
        }

        private static void DrawRobot(Color32[] p, int w, int h, Color baseColor, Color accent, bool boss)
        {
            int bodyW = boss ? w * 3 / 4 : w * 2 / 3;
            int bodyH = boss ? h * 3 / 5 : h / 2;
            int x = (w - bodyW) / 2;
            DrawRect(p, w, h, x, h / 5, bodyW, bodyH, baseColor);
            DrawRect(p, w, h, x + bodyW / 5, h / 5 + bodyH * 2 / 3, bodyW * 3 / 5, bodyH / 5, accent);
            DrawCircle(p, w, h, w / 2, h / 5 + bodyH / 2, Mathf.Max(5, bodyW / 10), accent);
            DrawRect(p, w, h, x - bodyW / 7, h / 4, bodyW / 7, bodyH / 3, accent);
            DrawRect(p, w, h, x + bodyW, h / 4, bodyW / 7, bodyH / 3, accent);
            DrawRect(p, w, h, x + bodyW / 5, 4, bodyW / 5, h / 5, baseColor);
            DrawRect(p, w, h, x + bodyW * 3 / 5, 4, bodyW / 5, h / 5, baseColor);
        }

        private static void DrawUiBadge(Color32[] p, int w, int h, Color baseColor, Color accent, int id)
        {
            int radius = Mathf.Min(w, h) / 2 - 6;
            DrawCircle(p, w, h, w / 2, h / 2, radius, baseColor);
            DrawRing(p, w, h, w / 2, h / 2, radius, Mathf.Max(3, radius / 8), accent);
            int bars = 2 + Mathf.Abs(id) % 4;
            for (int i = 0; i < bars; i++)
            {
                int y = h / 3 + i * Mathf.Max(4, h / 10);
                DrawRect(p, w, h, w / 4, y, w / 2, Mathf.Max(3, h / 24), accent);
            }
        }

        private static Color Palette(int id, int channel)
        {
            float hue = Mathf.Repeat(id * 0.173f + channel * 0.11f, 1f);
            float saturation = id >= 6101 && id <= 6107 ? 0.48f : 0.72f;
            float value = channel == 0 ? 0.62f : 0.95f;
            return Color.HSVToRGB(hue, saturation, value);
        }

        private static void Fill(Color32[] pixels, Color color)
        {
            Color32 value = color;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = value;
        }

        private static void DrawRect(Color32[] p, int w, int h, int x, int y, int width, int height, Color color)
        {
            Color32 value = color;
            int minX = Mathf.Clamp(x, 0, w), maxX = Mathf.Clamp(x + width, 0, w);
            int minY = Mathf.Clamp(y, 0, h), maxY = Mathf.Clamp(y + height, 0, h);
            for (int py = minY; py < maxY; py++)
                for (int px = minX; px < maxX; px++) p[py * w + px] = value;
        }

        private static void DrawCircle(Color32[] p, int w, int h, int cx, int cy, int radius, Color color)
        {
            Color32 value = color; int rr = radius * radius;
            for (int y = Mathf.Max(0, cy - radius); y < Mathf.Min(h, cy + radius + 1); y++)
                for (int x = Mathf.Max(0, cx - radius); x < Mathf.Min(w, cx + radius + 1); x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rr) p[y * w + x] = value;
        }

        private static void DrawRing(Color32[] p, int w, int h, int cx, int cy, int radius, int thickness, Color color)
        {
            Color32 value = color; int outer = radius * radius; int innerRadius = Mathf.Max(0, radius - thickness); int inner = innerRadius * innerRadius;
            for (int y = Mathf.Max(0, cy - radius); y < Mathf.Min(h, cy + radius + 1); y++)
                for (int x = Mathf.Max(0, cx - radius); x < Mathf.Min(w, cx + radius + 1); x++)
                { int d = (x - cx) * (x - cx) + (y - cy) * (y - cy); if (d <= outer && d >= inner) p[y * w + x] = value; }
        }

        private static void ResolveTextureSize(int id, out int width, out int height)
        {
            if (id >= 6101 && id <= 6107) { width = 1920; height = 1080; }
            else if (id == 6201) { width = 128; height = 64; }
            else if (id == 6202) { width = 128; height = 128; }
            else if (id == 6204) { width = 128; height = 128; }
            else if (id == 6203 || id == 6401 || id == 6402) { width = 128; height = 192; }
            else if (id == 6502) { width = 192; height = 192; }
            else if (id == 6001 || id == 6002) { width = 512; height = 288; }
            else { width = 128; height = 128; }
        }

        private static void ConfigureSpriteImporter(int resourceId, string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException($"Sprite importer missing: {assetPath}");
            bool tiled = resourceId == 6201 || resourceId == 6202 || resourceId == 6204;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit = 100f;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static string GetSpriteAssetPath(int resourceId)
        {
            return $"{SpriteRoot}/spt_{resourceId}/spt_{resourceId}_1.png";
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void BuildScenePrefab()
        {
            GameObject root = new GameObject("battle_world_zone_shell");
            try
            {
                Component presentation = BattlePreparationEditorUiFactory.AddRuntimeComponent(root, ScenePresentationType);

                GameObject backgroundObject = new GameObject("SmallAreaBackground");
                backgroundObject.transform.SetParent(root.transform, false);
                SpriteRenderer background = backgroundObject.AddComponent<SpriteRenderer>();
                background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GetSpriteAssetPath(6101));
                background.sortingOrder = -100;

                GameObject contentObject = new GameObject("SmallAreaContentRoot");
                contentObject.transform.SetParent(root.transform, false);

                GameObject cameraObject = new GameObject("BattleWorldCamera");
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5.4f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.018f, 0.035f, 0.065f, 1f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                camera.enabled = false;

                BattlePreparationEditorUiFactory.SetObject(presentation, "sceneCamera", camera);
                BattlePreparationEditorUiFactory.SetObject(presentation, "contentRoot", contentObject.transform);
                BattlePreparationEditorUiFactory.SetObject(presentation, "backgroundRenderer", background);
                BattlePreparationEditorUiFactory.SetSerializedRect(presentation, "cameraBounds", new Rect(-9.6f, -5.4f, 19.2f, 10.8f));
                BattlePreparationEditorUiFactory.AddBuilderMarker(root, SceneMarker);
                BattlePreparationEditorUiFactory.SavePrefab(root, ScenePrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildUiPrefab()
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject("win_battle_world_zone", null);
            try
            {
                BattlePreparationEditorUiFactory.Stretch(root.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.AddImage(root, new Color(0f, 0f, 0f, 0f), null, false);
                Component mono = BattlePreparationEditorUiFactory.AddRuntimeComponent(root, UiMonoType);

                GameObject topBar = BattlePreparationEditorUiFactory.AddPanel(
                    "TopBar", root.transform, new Color(0.025f, 0.05f, 0.09f, 0.94f), true);
                BattlePreparationEditorUiFactory.SetRect(topBar.GetComponent<RectTransform>(), new Vector2(0f, 0.9f), Vector2.one, Vector2.zero, Vector2.zero);

                Text title = AddTopText("Title", topBar.transform, "战斗区域", 30, new Vector2(0f, 0f), new Vector2(0.25f, 1f), new Vector2(24f, 0f));
                Text area = AddTopText("CurrentArea", topBar.transform, "当前区域", 24, new Vector2(0.23f, 0f), new Vector2(0.7f, 1f), Vector2.zero);

                BattlePreparationEditorUiFactory.ButtonParts settings = BattlePreparationEditorUiFactory.AddButton(
                    "SettingsButton", topBar.transform, "设置", BattlePreparationEditorUiFactory.PanelLightColor, 21);
                BattlePreparationEditorUiFactory.Place(settings.Rect, new Vector2(0.82f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(118f, 48f));
                BattlePreparationEditorUiFactory.ButtonParts manualSave = BattlePreparationEditorUiFactory.AddButton(
                    "ManualSaveButton", topBar.transform, "保存", new Color(0.16f, 0.46f, 0.58f, 0.96f), 21);
                BattlePreparationEditorUiFactory.Place(manualSave.Rect, new Vector2(0.94f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(132f, 48f));

                GameObject statsPanel = BattlePreparationEditorUiFactory.AddPanel(
                    "MapStatisticsPanel", root.transform, new Color(0.025f, 0.06f, 0.1f, 0.84f), false);
                BattlePreparationEditorUiFactory.Place(statsPanel.GetComponent<RectTransform>(), new Vector2(0.02f, 0.82f), new Vector2(0f, 1f), Vector2.zero, new Vector2(390f, 132f));
                Text stats = BattlePreparationEditorUiFactory.AddTextChild(
                    "MapStatistics", statsPanel.transform, "地图统计", 20, TextAnchor.MiddleLeft, new Color(0.78f, 0.9f, 1f, 1f), 14f);

                GameObject minimapPanel = BattlePreparationEditorUiFactory.AddPanel(
                    "BattleMinimapPanel",
                    root.transform,
                    new Color(0.018f, 0.045f, 0.075f, 0.92f),
                    true);
                BattlePreparationEditorUiFactory.Place(
                    minimapPanel.GetComponent<RectTransform>(),
                    new Vector2(0.98f, 0.87f),
                    Vector2.one,
                    Vector2.zero,
                    new Vector2(370f, 250f));
                Button minimapButton = minimapPanel.AddComponent<Button>();
                minimapButton.targetGraphic = minimapPanel.GetComponent<Image>();
                Component minimapView = BattlePreparationEditorUiFactory.AddRuntimeComponent(
                    minimapPanel,
                    MinimapViewType);

                GameObject minimapTitleObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "Title",
                    minimapPanel.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    minimapTitleObject.GetComponent<RectTransform>(),
                    new Vector2(0f, 1f),
                    Vector2.one,
                    new Vector2(12f, -38f),
                    new Vector2(-12f, -4f));
                Text minimapTitle = BattlePreparationEditorUiFactory.AddText(
                    minimapTitleObject,
                    "战区地图",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.78f, 0.93f, 1f, 1f));
                minimapTitle.fontStyle = FontStyle.Bold;

                GameObject projectionObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "LocalProjection",
                    minimapPanel.transform);
                RectTransform projectionRoot = projectionObject.GetComponent<RectTransform>();
                BattlePreparationEditorUiFactory.SetRect(
                    projectionRoot,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(14f, 31f),
                    new Vector2(-14f, -40f));
                BattlePreparationEditorUiFactory.AddImage(
                    projectionObject,
                    new Color(0.008f, 0.022f, 0.038f, 0.8f),
                    null,
                    false);
                projectionObject.AddComponent<RectMask2D>();

                GameObject floorTemplateObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "FloorTemplate",
                        projectionObject.transform);
                BattlePreparationEditorUiFactory.Place(
                    floorTemplateObject.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(40f, 5f));
                Image floorTemplate = BattlePreparationEditorUiFactory.AddImage(
                    floorTemplateObject,
                    new Color(0.32f, 0.62f, 0.76f, 0.95f),
                    null,
                    false);
                floorTemplateObject.SetActive(false);

                GameObject ladderTemplateObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "LadderTemplate",
                        projectionObject.transform);
                BattlePreparationEditorUiFactory.Place(
                    ladderTemplateObject.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(5f, 30f));
                Image ladderTemplate = BattlePreparationEditorUiFactory.AddImage(
                    ladderTemplateObject,
                    new Color(0.85f, 0.67f, 0.28f, 0.92f),
                    null,
                    false);
                ladderTemplateObject.SetActive(false);

                GameObject doorTemplateObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "DoorTemplate",
                        projectionObject.transform);
                BattlePreparationEditorUiFactory.Place(
                    doorTemplateObject.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(8f, 16f));
                Image doorTemplate = BattlePreparationEditorUiFactory.AddImage(
                    doorTemplateObject,
                    new Color(0.36f, 0.9f, 0.95f, 0.92f),
                    null,
                    false);
                doorTemplateObject.SetActive(false);

                Sprite markerSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(
                    "UI/Skin/Knob.psd");
                GameObject playerMarkerObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "PlayerMarker",
                        projectionObject.transform);
                BattlePreparationEditorUiFactory.Place(
                    playerMarkerObject.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(16f, 16f));
                Image playerMarker = BattlePreparationEditorUiFactory.AddImage(
                    playerMarkerObject,
                    new Color(0.24f, 1f, 0.38f, 1f),
                    markerSprite,
                    false);
                playerMarkerObject.SetActive(false);

                GameObject enemyMarkerTemplateObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "EnemyMarkerTemplate",
                        projectionObject.transform);
                BattlePreparationEditorUiFactory.Place(
                    enemyMarkerTemplateObject.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(13f, 13f));
                Image enemyMarkerTemplate = BattlePreparationEditorUiFactory.AddImage(
                    enemyMarkerTemplateObject,
                    new Color(1f, 0.22f, 0.2f, 1f),
                    markerSprite,
                    false);
                enemyMarkerTemplateObject.SetActive(false);

                Text minimapUnavailable = BattlePreparationEditorUiFactory.AddTextChild(
                    "Unavailable",
                    projectionObject.transform,
                    "地图暂不可用",
                    18,
                    TextAnchor.MiddleCenter,
                    new Color(0.62f, 0.7f, 0.76f, 1f),
                    8f);
                minimapUnavailable.gameObject.SetActive(false);

                GameObject legendObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "Legend",
                    minimapPanel.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    legendObject.GetComponent<RectTransform>(),
                    Vector2.zero,
                    new Vector2(1f, 0f),
                    new Vector2(10f, 4f),
                    new Vector2(-10f, 29f));
                Text minimapLegend = BattlePreparationEditorUiFactory.AddText(
                    legendObject,
                    "<color=#3DFF61>● 玩家</color>    <color=#FF3833>● 敌人</color>",
                    13,
                    TextAnchor.MiddleCenter,
                    new Color(0.72f, 0.8f, 0.86f, 1f));
                minimapLegend.supportRichText = true;

                BattlePreparationEditorUiFactory.SetObject(
                    minimapView,
                    "titleText",
                    minimapTitle);
                BattlePreparationEditorUiFactory.SetObject(
                    minimapView,
                    "projectionRoot",
                    projectionRoot);
                BattlePreparationEditorUiFactory.SetObject(
                    minimapView,
                    "floorTemplate",
                    floorTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    minimapView,
                    "ladderTemplate",
                    ladderTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    minimapView,
                    "doorTemplate",
                    doorTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    minimapView,
                    "playerMarker",
                    playerMarker);
                BattlePreparationEditorUiFactory.SetObject(
                    minimapView,
                    "enemyMarkerTemplate",
                    enemyMarkerTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    minimapView,
                    "unavailableText",
                    minimapUnavailable);
                BattlePreparationEditorUiFactory.SetObject(
                    minimapView,
                    "legendText",
                    minimapLegend);

                GameObject hintPanel = BattlePreparationEditorUiFactory.AddPanel(
                    "InteractionHintPanel", root.transform, new Color(0.025f, 0.05f, 0.08f, 0.88f), false);
                BattlePreparationEditorUiFactory.Place(hintPanel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.035f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(760f, 54f));
                Text hint = BattlePreparationEditorUiFactory.AddTextChild(
                    "InteractionHint", hintPanel.transform, "靠近传送门按 E / ↑", 20, TextAnchor.MiddleCenter, new Color(0.66f, 0.95f, 1f, 1f), 12f);

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(mono, "currentAreaText", area);
                BattlePreparationEditorUiFactory.SetObject(mono, "minimapButton", minimapButton);
                BattlePreparationEditorUiFactory.SetObject(mono, "minimapView", minimapView);
                BattlePreparationEditorUiFactory.SetObject(mono, "mapStatisticsPanelRoot", statsPanel);
                BattlePreparationEditorUiFactory.SetObject(mono, "mapStatisticsText", stats);
                BattlePreparationEditorUiFactory.SetObject(mono, "interactionHintPanelRoot", hintPanel);
                BattlePreparationEditorUiFactory.SetObject(mono, "interactionHintText", hint);
                BattlePreparationEditorUiFactory.SetObject(mono, "settingsButton", settings.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "settingsButtonText", settings.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "manualSaveButton", manualSave.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "manualSaveButtonText", manualSave.Text);
                BattlePreparationEditorUiFactory.AddBuilderMarker(root, UiMarker);
                BattlePreparationEditorUiFactory.SavePrefab(root, UiPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildAreaMapUiPrefab()
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(
                "win_battle_world_zone_map",
                null);
            try
            {
                BattlePreparationEditorUiFactory.Stretch(
                    root.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.AddImage(
                    root,
                    new Color(0.006f, 0.015f, 0.028f, 0.9f),
                    null,
                    true);
                Component mono = BattlePreparationEditorUiFactory.AddRuntimeComponent(
                    root,
                    AreaMapUiMonoType);

                GameObject frame = BattlePreparationEditorUiFactory.AddPanel(
                    "AreaMapFrame",
                    root.transform,
                    new Color(0.025f, 0.055f, 0.085f, 0.99f),
                    true);
                BattlePreparationEditorUiFactory.SetRect(
                    frame.GetComponent<RectTransform>(),
                    new Vector2(0.055f, 0.065f),
                    new Vector2(0.945f, 0.935f),
                    Vector2.zero,
                    Vector2.zero);

                GameObject titleObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "Title",
                    frame.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    titleObject.GetComponent<RectTransform>(),
                    new Vector2(0f, 0.9f),
                    new Vector2(0.72f, 1f),
                    new Vector2(30f, 0f),
                    Vector2.zero);
                Text title = BattlePreparationEditorUiFactory.AddText(
                    titleObject,
                    "战区全图",
                    32,
                    TextAnchor.MiddleLeft,
                    new Color(0.8f, 0.94f, 1f, 1f));
                title.fontStyle = FontStyle.Bold;

                BattlePreparationEditorUiFactory.ButtonParts close =
                    BattlePreparationEditorUiFactory.AddButton(
                        "CloseButton",
                        frame.transform,
                        "关闭",
                        new Color(0.18f, 0.3f, 0.4f, 0.98f),
                        21);
                BattlePreparationEditorUiFactory.Place(
                    close.Rect,
                    new Vector2(0.965f, 0.95f),
                    new Vector2(1f, 0.5f),
                    Vector2.zero,
                    new Vector2(126f, 48f));

                GameObject mapPanel = BattlePreparationEditorUiFactory.AddPanel(
                    "TopologyPanel",
                    frame.transform,
                    new Color(0.008f, 0.025f, 0.047f, 0.98f),
                    false);
                BattlePreparationEditorUiFactory.SetRect(
                    mapPanel.GetComponent<RectTransform>(),
                    new Vector2(0.02f, 0.055f),
                    new Vector2(0.72f, 0.89f),
                    Vector2.zero,
                    Vector2.zero);
                Component mapView = BattlePreparationEditorUiFactory.AddRuntimeComponent(
                    mapPanel,
                    AreaMapViewType);

                GameObject graphObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "Graph",
                    mapPanel.transform);
                RectTransform graphRoot = graphObject.GetComponent<RectTransform>();
                BattlePreparationEditorUiFactory.SetRect(
                    graphRoot,
                    new Vector2(0f, 0.1f),
                    Vector2.one,
                    new Vector2(22f, 8f),
                    new Vector2(-22f, -18f));
                BattlePreparationEditorUiFactory.AddImage(
                    graphObject,
                    new Color(0.008f, 0.022f, 0.04f, 0.88f),
                    null,
                    false);

                GameObject connectionTemplateObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "ConnectionTemplate",
                        graphObject.transform);
                BattlePreparationEditorUiFactory.Place(
                    connectionTemplateObject.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(20f, 6f));
                Image connectionTemplate = BattlePreparationEditorUiFactory.AddImage(
                    connectionTemplateObject,
                    new Color(0.29f, 0.63f, 0.78f, 0.78f),
                    null,
                    false);
                connectionTemplateObject.SetActive(false);

                GameObject nodeTemplateObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "NodeTemplate",
                        graphObject.transform);
                BattlePreparationEditorUiFactory.Place(
                    nodeTemplateObject.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(104f, 104f));
                Image nodeHitImage = BattlePreparationEditorUiFactory.AddImage(
                    nodeTemplateObject,
                    new Color(0f, 0f, 0f, 0.001f),
                    null,
                    true);
                Button nodeButton = nodeTemplateObject.AddComponent<Button>();
                nodeButton.targetGraphic = nodeHitImage;
                Component nodeView = BattlePreparationEditorUiFactory.AddRuntimeComponent(
                    nodeTemplateObject,
                    AreaMapNodeViewType);

                Image selectedRing = AddCenteredMapNodeImage(
                    "SelectedRing",
                    nodeTemplateObject.transform,
                    new Vector2(90f, 90f),
                    new Color(0.97f, 0.77f, 0.26f, 1f));
                Image currentRing = AddCenteredMapNodeImage(
                    "CurrentRing",
                    nodeTemplateObject.transform,
                    new Vector2(78f, 78f),
                    new Color(0.29f, 0.87f, 0.96f, 1f));
                Image nodeBody = AddCenteredMapNodeImage(
                    "Body",
                    nodeTemplateObject.transform,
                    new Vector2(64f, 64f),
                    new Color(0.12f, 0.34f, 0.46f, 1f));
                Text nodeIndex = BattlePreparationEditorUiFactory.AddTextChild(
                    "Index",
                    nodeBody.transform,
                    "1",
                    23,
                    TextAnchor.MiddleCenter,
                    Color.white);
                nodeIndex.fontStyle = FontStyle.Bold;

                GameObject markerObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "Marker",
                    nodeTemplateObject.transform);
                BattlePreparationEditorUiFactory.Place(
                    markerObject.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -55f),
                    new Vector2(152f, 28f));
                Text nodeMarker = BattlePreparationEditorUiFactory.AddText(
                    markerObject,
                    "起  中撤",
                    15,
                    TextAnchor.MiddleCenter,
                    Color.white);
                nodeMarker.supportRichText = true;

                BattlePreparationEditorUiFactory.SetObject(
                    nodeView,
                    "nodeRect",
                    nodeTemplateObject.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(
                    nodeView,
                    "button",
                    nodeButton);
                BattlePreparationEditorUiFactory.SetObject(
                    nodeView,
                    "bodyImage",
                    nodeBody);
                BattlePreparationEditorUiFactory.SetObject(
                    nodeView,
                    "currentRingImage",
                    currentRing);
                BattlePreparationEditorUiFactory.SetObject(
                    nodeView,
                    "selectedRingImage",
                    selectedRing);
                BattlePreparationEditorUiFactory.SetObject(
                    nodeView,
                    "indexText",
                    nodeIndex);
                BattlePreparationEditorUiFactory.SetObject(
                    nodeView,
                    "markerText",
                    nodeMarker);
                nodeTemplateObject.SetActive(false);

                Text unavailable = BattlePreparationEditorUiFactory.AddTextChild(
                    "Unavailable",
                    graphObject.transform,
                    "地图暂不可用",
                    24,
                    TextAnchor.MiddleCenter,
                    new Color(0.68f, 0.76f, 0.82f, 1f),
                    12f);
                unavailable.gameObject.SetActive(false);

                GameObject legendObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "Legend",
                    mapPanel.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    legendObject.GetComponent<RectTransform>(),
                    Vector2.zero,
                    new Vector2(1f, 0.1f),
                    new Vector2(18f, 0f),
                    new Vector2(-18f, 0f));
                Text legend = BattlePreparationEditorUiFactory.AddText(
                    legendObject,
                    "当前  普通  Boss  中撤  远撤",
                    17,
                    TextAnchor.MiddleCenter,
                    new Color(0.72f, 0.82f, 0.9f, 1f));
                legend.supportRichText = true;

                BattlePreparationEditorUiFactory.SetObject(
                    mapView,
                    "graphRoot",
                    graphRoot);
                BattlePreparationEditorUiFactory.SetObject(
                    mapView,
                    "connectionTemplate",
                    connectionTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    mapView,
                    "nodeTemplate",
                    nodeView);
                BattlePreparationEditorUiFactory.SetObject(
                    mapView,
                    "unavailableText",
                    unavailable);

                GameObject detailPanel = BattlePreparationEditorUiFactory.AddPanel(
                    "AreaDetailPanel",
                    frame.transform,
                    new Color(0.055f, 0.09f, 0.13f, 0.99f),
                    true);
                BattlePreparationEditorUiFactory.SetRect(
                    detailPanel.GetComponent<RectTransform>(),
                    new Vector2(0.735f, 0.055f),
                    new Vector2(0.98f, 0.89f),
                    Vector2.zero,
                    Vector2.zero);

                GameObject detailTitleObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "DetailTitle",
                        detailPanel.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    detailTitleObject.GetComponent<RectTransform>(),
                    new Vector2(0f, 0.84f),
                    Vector2.one,
                    new Vector2(22f, 0f),
                    new Vector2(-22f, -8f));
                Text detailTitle = BattlePreparationEditorUiFactory.AddText(
                    detailTitleObject,
                    "区域 1",
                    28,
                    TextAnchor.MiddleLeft,
                    new Color(0.82f, 0.94f, 1f, 1f));
                detailTitle.fontStyle = FontStyle.Bold;

                GameObject detailBodyObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "DetailBody",
                        detailPanel.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    detailBodyObject.GetComponent<RectTransform>(),
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0.84f),
                    new Vector2(22f, 24f),
                    new Vector2(-22f, -12f));
                Text detailBody = BattlePreparationEditorUiFactory.AddText(
                    detailBodyObject,
                    "模板编号：0\n状态：普通区域\n存活敌人：0\n存活 Boss：0\n可搜物资：0\n撤离点：无",
                    20,
                    TextAnchor.UpperLeft,
                    new Color(0.9f, 0.94f, 0.98f, 1f));
                detailBody.lineSpacing = 1.35f;

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(mono, "closeButton", close.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "closeButtonText", close.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "mapView", mapView);
                BattlePreparationEditorUiFactory.SetObject(mono, "legendText", legend);
                BattlePreparationEditorUiFactory.SetObject(mono, "detailRoot", detailPanel);
                BattlePreparationEditorUiFactory.SetObject(mono, "detailTitleText", detailTitle);
                BattlePreparationEditorUiFactory.SetObject(mono, "detailBodyText", detailBody);
                BattlePreparationEditorUiFactory.AddBuilderMarker(root, AreaMapUiMarker);
                BattlePreparationEditorUiFactory.SavePrefab(root, AreaMapUiPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Image AddCenteredMapNodeImage(
            string name,
            Transform parent,
            Vector2 size,
            Color color)
        {
            GameObject value = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.Place(
                value.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                size);
            return BattlePreparationEditorUiFactory.AddImage(
                value,
                color,
                null,
                false);
        }

        private static Text AddTopText(
            string name,
            Transform parent,
            string value,
            int fontSize,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin)
        {
            GameObject objectValue = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.SetRect(objectValue.GetComponent<RectTransform>(), anchorMin, anchorMax, offsetMin, Vector2.zero);
            return BattlePreparationEditorUiFactory.AddText(objectValue, value, fontSize, TextAnchor.MiddleLeft);
        }

        private static void BindHomeSceneRuntime()
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(HomeScenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(HomeScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException($"Cannot open Home scene: {HomeScenePath}");
            try
            {
                GameObject homeWorld = FindGameObject(scene, "HomeWorld");
                Transform sceneRoot = FindTransform(scene, "HomeSceneRoot");
                if (homeWorld == null || sceneRoot == null) throw new InvalidOperationException("Home_01 is missing HomeWorld/HomeSceneRoot.");
                Type runtimeType = BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(SceneRuntimeType);
                Component runtime = homeWorld.GetComponent(runtimeType) ?? homeWorld.AddComponent(runtimeType);
                RemoveDuplicateComponents(scene, runtimeType, runtime);
                Component cameraController = FindComponent(scene, BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(HomeCameraControllerType));
                Component bootstrap = FindComponent(scene, BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(BootstrapType));
                if (cameraController == null || bootstrap == null) throw new InvalidOperationException("Home_01 Battle runtime prerequisites are missing.");
                BattlePreparationEditorUiFactory.SetObject(runtime, "sceneRoot", sceneRoot);
                BattlePreparationEditorUiFactory.SetObject(runtime, "sceneCameraController", cameraController);
                BattlePreparationEditorUiFactory.SetObject(bootstrap, "battleWorldZoneSceneRuntime", runtime);
                EditorUtility.SetDirty(runtime); EditorUtility.SetDirty(bootstrap);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Failed to save scene: {HomeScenePath}");
            }
            finally
            {
                if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }
        }

        private static void ValidateGeneratedAssets()
        {
            GameObject scenePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScenePrefabPath);
            GameObject uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiPrefabPath);
            GameObject areaMapUiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AreaMapUiPrefabPath);
            if (scenePrefab == null || uiPrefab == null || areaMapUiPrefab == null
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(scenePrefab, SceneMarker)
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(uiPrefab, UiMarker)
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    areaMapUiPrefab,
                    AreaMapUiMarker))
                throw new InvalidOperationException("Battle WorldZone generated prefab validation failed.");
            Component presentation = scenePrefab.GetComponent(BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(ScenePresentationType));
            string[] sceneProperties = { "sceneCamera", "contentRoot", "backgroundRenderer" };
            for (int i = 0; i < sceneProperties.Length; i++)
                if (presentation == null || BattlePreparationEditorUiFactory.FindRequiredProperty(presentation, sceneProperties[i]).objectReferenceValue == null)
                    throw new InvalidOperationException($"Battle scene binding missing: {sceneProperties[i]}");
            Component ui = uiPrefab.GetComponent(BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(UiMonoType));
            string[] uiProperties = { "titleText", "currentAreaText", "minimapButton", "minimapView", "mapStatisticsPanelRoot", "mapStatisticsText", "interactionHintPanelRoot", "interactionHintText", "settingsButton", "settingsButtonText", "manualSaveButton", "manualSaveButtonText" };
            for (int i = 0; i < uiProperties.Length; i++)
                if (ui == null || BattlePreparationEditorUiFactory.FindRequiredProperty(ui, uiProperties[i]).objectReferenceValue == null)
                    throw new InvalidOperationException($"Battle HUD binding missing: {uiProperties[i]}");
            Component minimap = BattlePreparationEditorUiFactory
                .FindRequiredProperty(ui, "minimapView")
                .objectReferenceValue as Component;
            string[] minimapProperties =
            {
                "titleText",
                "projectionRoot",
                "floorTemplate",
                "ladderTemplate",
                "doorTemplate",
                "playerMarker",
                "enemyMarkerTemplate",
                "unavailableText",
                "legendText",
            };
            for (int i = 0; i < minimapProperties.Length; i++)
                if (minimap == null || BattlePreparationEditorUiFactory.FindRequiredProperty(minimap, minimapProperties[i]).objectReferenceValue == null)
                    throw new InvalidOperationException($"Battle minimap binding missing: {minimapProperties[i]}");

            Component areaMapUi = areaMapUiPrefab.GetComponent(
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    AreaMapUiMonoType));
            string[] areaMapUiProperties =
            {
                "titleText",
                "closeButton",
                "closeButtonText",
                "mapView",
                "legendText",
                "detailRoot",
                "detailTitleText",
                "detailBodyText",
            };
            for (int i = 0; i < areaMapUiProperties.Length; i++)
                if (areaMapUi == null || BattlePreparationEditorUiFactory.FindRequiredProperty(areaMapUi, areaMapUiProperties[i]).objectReferenceValue == null)
                    throw new InvalidOperationException($"Battle area map UI binding missing: {areaMapUiProperties[i]}");

            Component areaMapView = BattlePreparationEditorUiFactory
                .FindRequiredProperty(areaMapUi, "mapView")
                .objectReferenceValue as Component;
            string[] areaMapViewProperties =
            {
                "graphRoot",
                "connectionTemplate",
                "nodeTemplate",
                "unavailableText",
            };
            for (int i = 0; i < areaMapViewProperties.Length; i++)
                if (areaMapView == null || BattlePreparationEditorUiFactory.FindRequiredProperty(areaMapView, areaMapViewProperties[i]).objectReferenceValue == null)
                    throw new InvalidOperationException($"Battle area map view binding missing: {areaMapViewProperties[i]}");

            Component areaMapNode = BattlePreparationEditorUiFactory
                .FindRequiredProperty(areaMapView, "nodeTemplate")
                .objectReferenceValue as Component;
            string[] areaMapNodeProperties =
            {
                "nodeRect",
                "button",
                "bodyImage",
                "currentRingImage",
                "selectedRingImage",
                "indexText",
                "markerText",
            };
            for (int i = 0; i < areaMapNodeProperties.Length; i++)
                if (areaMapNode == null || BattlePreparationEditorUiFactory.FindRequiredProperty(areaMapNode, areaMapNodeProperties[i]).objectReferenceValue == null)
                    throw new InvalidOperationException($"Battle area map node binding missing: {areaMapNodeProperties[i]}");
            for (int i = 0; i < PlaceholderResourceIds.Length; i++)
                if (AssetDatabase.LoadAssetAtPath<Sprite>(GetSpriteAssetPath(PlaceholderResourceIds[i])) == null)
                    throw new InvalidOperationException($"Battle placeholder Sprite missing: {PlaceholderResourceIds[i]}");
            if (!TryValidateHomeSceneBindings(out string error)) throw new InvalidOperationException($"Battle Home_01 binding invalid: {error}");
        }

        private static bool TryValidateHomeSceneBindings(out string error)
        {
            error = string.Empty;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(HomeScenePath) == null) { error = $"Home scene missing: {HomeScenePath}"; return false; }
            Scene previousActive = SceneManager.GetActiveScene(); Scene scene = SceneManager.GetSceneByPath(HomeScenePath); bool wasLoaded = scene.IsValid() && scene.isLoaded;
            try
            {
                if (!wasLoaded) scene = EditorSceneManager.OpenScene(HomeScenePath, OpenSceneMode.Additive);
                Type runtimeType = BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(SceneRuntimeType);
                List<Component> runtimes = FindComponents(scene, runtimeType);
                if (runtimes.Count != 1) { error = $"Expected one BattleWorldZoneSceneRuntime, actual={runtimes.Count}."; return false; }
                Transform expectedRoot = FindTransform(scene, "HomeSceneRoot");
                Component expectedCamera = FindComponent(scene, BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(HomeCameraControllerType));
                Component bootstrap = FindComponent(scene, BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(BootstrapType));
                if (expectedRoot == null || expectedCamera == null || bootstrap == null) { error = "Home scene prerequisites are missing."; return false; }
                Component runtime = runtimes[0];
                bool valid = ReferenceEquals(BattlePreparationEditorUiFactory.FindRequiredProperty(runtime, "sceneRoot").objectReferenceValue, expectedRoot)
                    && ReferenceEquals(BattlePreparationEditorUiFactory.FindRequiredProperty(runtime, "sceneCameraController").objectReferenceValue, expectedCamera)
                    && ReferenceEquals(BattlePreparationEditorUiFactory.FindRequiredProperty(bootstrap, "battleWorldZoneSceneRuntime").objectReferenceValue, runtime);
                if (!valid) error = "Serialized Battle runtime binding mismatch.";
                return valid;
            }
            catch (Exception exception) { error = exception.ToString(); return false; }
            finally
            {
                if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }
        }

        private static GameObject FindGameObject(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) { GameObject found = FindGameObjectRecursive(roots[i], name); if (found != null) return found; }
            return null;
        }
        private static GameObject FindGameObjectRecursive(GameObject current, string name)
        {
            if (current != null && string.Equals(current.name, name, StringComparison.Ordinal)) return current;
            if (current == null) return null;
            for (int i = 0; i < current.transform.childCount; i++) { GameObject found = FindGameObjectRecursive(current.transform.GetChild(i).gameObject, name); if (found != null) return found; }
            return null;
        }
        private static Transform FindTransform(Scene scene, string name) => FindGameObject(scene, name)?.transform;
        private static Component FindComponent(Scene scene, Type type)
        {
            if (type == null) return null; GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) { Component found = roots[i].GetComponentInChildren(type, true); if (found != null) return found; }
            return null;
        }
        private static List<Component> FindComponents(Scene scene, Type type)
        {
            List<Component> result = new List<Component>(); if (type == null) return result;
            GameObject[] roots = scene.GetRootGameObjects(); for (int i = 0; i < roots.Length; i++) result.AddRange(roots[i].GetComponentsInChildren(type, true));
            return result;
        }
        private static void RemoveDuplicateComponents(Scene scene, Type type, Component keep)
        {
            List<Component> found = FindComponents(scene, type);
            for (int i = 0; i < found.Count; i++) if (found[i] != null && !ReferenceEquals(found[i], keep)) UnityEngine.Object.DestroyImmediate(found[i], true);
        }
    }
}
#endif
