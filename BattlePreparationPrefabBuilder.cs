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
    /// 2.0b 备战间资源的唯一 Editor 编排入口。
    /// 幂等生成 Sprite 设置、场景/UI Prefab、HomeMain 入口以及 Home_01 运行时引用。
    /// </summary>
    public static class BattlePreparationPrefabBuilder
    {
        private const string MenuPath =
            "TryGame/Battle Preparation/Rebuild 2.0g Assets";
        private const string ScenePrefabPath =
            "Assets/Resources/TryGameBuildRes/battle/preparation/battle_preparation_scene.prefab";
        private const string HomeMainPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_home_main.prefab";
        private const string HomeScenePath =
            "Assets/Resources/TryGameBuildRes/scene/Home_01.unity";
        private const string SceneBuilderMarker = "__BattlePreparationSceneBuilder_v2";
        private const string HomeMainBuilderMarker = "__BattlePreparationHomeEntry_v1";
        private const string HomeMainButtonName = "BattlePreparationButton";

        private const string ScenePresentationType =
            "Game.BattlePreparationScenePresentation";
        private const string SceneOverlayFollowerType =
            "Game.BattlePreparationSceneOverlayFollower";
        private const string SceneRuntimeType = "Game.BattlePreparationSceneRuntime";
        private const string HomeCameraControllerType = "Game.HomeSceneCameraController";
        private const string BootstrapType = "Game.TryGameRuntimeBootstrap";
        private const string HomeMainMonoType = "Game.GUIMonoHomeMain";
        private const string RobotDetailMonoType = "Game.GUIMonoBattleRobotDetail";
        private const string ItemDetailMonoType = "Game.GUIMonoBattleRobotItemDetail";
        private const string EquipmentSlotType = "Game.BattleRobotEquipmentSlotView";
        private const string SkillSlotType = "Game.BattleRobotSkillSlotView";

        private static readonly int[] SpriteResourceIds =
        {
            4001,
            4002,
            4003,
            4004,
            4005,
            4006,
            4007,
            4010,
            4011,
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
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.delayCall -= EnsureBuiltAfterReload;
            EditorApplication.delayCall += EnsureBuiltAfterReload;
        }

        [MenuItem(MenuPath, false, 420)]
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

            // A domain reload can finish while the asset database is still updating.
            // Keep the one-shot ensure pending until the editor is actually ready;
            // otherwise a changed builder marker can be missed until the menu is run.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall -= EnsureBuiltAfterReload;
                EditorApplication.delayCall += EnsureBuiltAfterReload;
                return;
            }

            if (!RuntimeTypesAreReady())
            {
                return;
            }

            if (!NeedsBuild())
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
                    $"[BattlePreparationPrefabBuilder] 自动生成 2.0g 资源失败。" +
                    $"可在修复编译问题后手动执行 {MenuPath}。\n{exception}");
            }
        }

        private static void BuildAll(bool logSuccess)
        {
            if (!RuntimeTypesAreReady())
            {
                throw new InvalidOperationException(
                    "Battle preparation runtime component types are not compiled yet.");
            }

            Dictionary<int, Sprite> sprites = ConfigureAndLoadSprites();
            BattlePreparationUiPrefabBuilder.BuildAll(sprites);
            BuildScenePrefab(sprites[4010]);
            BindHomeMainEntry(sprites[4004]);
            BindHomeSceneRuntime();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateGeneratedAssets();

            if (logSuccess)
            {
                Debug.Log(
                    "[BattlePreparationPrefabBuilder] 2.0g 备战与选择资源已完成：" +
                    "Sprite、10 个 UI Prefab、场景 Prefab、HomeMain 和 Home_01 引用均已更新。");
            }
        }

        private static bool RuntimeTypesAreReady()
        {
            if (!BattlePreparationEditorUiFactory.AreRuntimeTypesAvailable(
                    BattlePreparationUiPrefabBuilder.RequiredRuntimeTypes))
            {
                return false;
            }

            string[] sceneTypes =
            {
                ScenePresentationType,
                SceneRuntimeType,
                HomeCameraControllerType,
                BootstrapType,
                HomeMainMonoType,
            };
            return BattlePreparationEditorUiFactory.AreRuntimeTypesAvailable(sceneTypes);
        }

        private static bool NeedsBuild()
        {
            for (int index = 0;
                index < BattlePreparationUiPrefabBuilder.GeneratedPrefabPaths.Length;
                index++)
            {
                string path = BattlePreparationUiPrefabBuilder.GeneratedPrefabPaths[index];
                string expectedMarker = GetExpectedUiPrefabMarker(path);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null
                    || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                        prefab,
                        expectedMarker))
                {
                    return true;
                }
            }

            GameObject scenePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ScenePrefabPath);
            if (scenePrefab == null
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    scenePrefab,
                    SceneBuilderMarker))
            {
                return true;
            }

            GameObject homeMain = AssetDatabase.LoadAssetAtPath<GameObject>(
                HomeMainPrefabPath);
            return homeMain == null
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    homeMain,
                    HomeMainBuilderMarker);
        }

        private static string GetExpectedUiPrefabMarker(string path)
        {
            return string.Equals(
                    path,
                    BattlePreparationUiPrefabBuilder.MainPrefabPath,
                    StringComparison.Ordinal)
                ? BattlePreparationUiPrefabBuilder.MainBuilderMarker
                : BattlePreparationUiPrefabBuilder.BuilderMarker;
        }

        private static Dictionary<int, Sprite> ConfigureAndLoadSprites()
        {
            Dictionary<int, Sprite> result = new Dictionary<int, Sprite>();
            for (int index = 0; index < SpriteResourceIds.Length; index++)
            {
                int resourceId = SpriteResourceIds[index];
                string path = SpritePath(resourceId);
                if (!File.Exists(ProjectAssetFullPath(path)))
                {
                    throw new FileNotFoundException(
                        $"Battle preparation image is missing: resourceId={resourceId}",
                        path);
                }

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    AssetDatabase.ImportAsset(
                        path,
                        ImportAssetOptions.ForceSynchronousImport
                            | ImportAssetOptions.ForceUpdate);
                    importer = AssetImporter.GetAtPath(path) as TextureImporter;
                }

                if (importer == null)
                {
                    throw new InvalidOperationException(
                        $"TextureImporter is unavailable: {path}");
                }

                bool changed = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }

                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    changed = true;
                }

                if (Mathf.Abs(importer.spritePixelsPerUnit - 100f) > 0.001f)
                {
                    importer.spritePixelsPerUnit = 100f;
                    changed = true;
                }

                if (!importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    changed = true;
                }

                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    changed = true;
                }

                if (importer.wrapMode != TextureWrapMode.Clamp)
                {
                    importer.wrapMode = TextureWrapMode.Clamp;
                    changed = true;
                }

                if (importer.filterMode != FilterMode.Bilinear)
                {
                    importer.filterMode = FilterMode.Bilinear;
                    changed = true;
                }

                if (importer.maxTextureSize != 4096)
                {
                    importer.maxTextureSize = 4096;
                    changed = true;
                }

                if (importer.npotScale != TextureImporterNPOTScale.None)
                {
                    importer.npotScale = TextureImporterNPOTScale.None;
                    changed = true;
                }

                if (importer.spritePivot != new Vector2(0.5f, 0.5f))
                {
                    importer.spritePivot = new Vector2(0.5f, 0.5f);
                    changed = true;
                }

                if (importer.isReadable)
                {
                    importer.isReadable = false;
                    changed = true;
                }
                if (changed)
                {
                    importer.SaveAndReimport();
                }

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    AssetDatabase.ImportAsset(
                        path,
                        ImportAssetOptions.ForceSynchronousImport
                            | ImportAssetOptions.ForceUpdate);
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }

                if (sprite == null)
                {
                    throw new InvalidOperationException(
                        $"Sprite import failed: resourceId={resourceId}, path={path}");
                }

                result.Add(resourceId, sprite);
            }

            return result;
        }

        private static void BuildScenePrefab(Sprite backgroundSprite)
        {
            if (backgroundSprite == null)
            {
                throw new ArgumentNullException(nameof(backgroundSprite));
            }

            GameObject root = new GameObject("battle_preparation_scene");
            try
            {
                Component presentation =
                    BattlePreparationEditorUiFactory.AddRuntimeComponent(
                        root,
                        ScenePresentationType);
                GameObject backgroundObject = new GameObject("Background");
                backgroundObject.transform.SetParent(root.transform, false);
                SpriteRenderer background = backgroundObject.AddComponent<SpriteRenderer>();
                background.sprite = backgroundSprite;
                background.sortingOrder = -100;

                Transform workbenchAnchor = CreateSceneUiAnchor(
                    root.transform,
                    "WorkbenchUiAnchor",
                    BattlePreparationUiPrefabBuilder.WorkbenchSceneNormalizedRect,
                    backgroundSprite.bounds);
                List<UnityEngine.Object> rosterAnchors =
                    new List<UnityEngine.Object>(4);
                for (int slotId = 1; slotId <= 4; slotId++)
                {
                    rosterAnchors.Add(CreateSceneUiAnchor(
                        root.transform,
                        $"RobotSlotUiAnchor_{slotId}",
                        BattlePreparationUiPrefabBuilder
                            .RosterSceneNormalizedRects[slotId - 1],
                        backgroundSprite.bounds));
                }

                GameObject cameraObject = new GameObject("BattlePreparationCamera");
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.transform.localPosition = new Vector3(
                    backgroundSprite.bounds.center.x,
                    backgroundSprite.bounds.center.y,
                    -10f);
                Camera sceneCamera = cameraObject.AddComponent<Camera>();
                sceneCamera.orthographic = true;
                sceneCamera.orthographicSize = backgroundSprite.bounds.extents.y;
                sceneCamera.clearFlags = CameraClearFlags.SolidColor;
                sceneCamera.backgroundColor = new Color(0.78f, 0.86f, 0.88f, 1f);
                sceneCamera.nearClipPlane = 0.1f;
                sceneCamera.farClipPlane = 100f;
                sceneCamera.depth = -1f;
                sceneCamera.enabled = false;

                BattlePreparationEditorUiFactory.SetObject(
                    presentation,
                    "backgroundRenderer",
                    background);
                BattlePreparationEditorUiFactory.SetObject(
                    presentation,
                    "sceneCamera",
                    sceneCamera);
                BattlePreparationEditorUiFactory.SetSerializedRect(
                    presentation,
                    "cameraBounds",
                    new Rect(
                        backgroundSprite.bounds.min.x,
                        backgroundSprite.bounds.min.y,
                        backgroundSprite.bounds.size.x,
                        backgroundSprite.bounds.size.y));
                BattlePreparationEditorUiFactory.SetObject(
                    presentation,
                    "workbenchUiAnchor",
                    workbenchAnchor);
                BattlePreparationEditorUiFactory.SetObjects(
                    presentation,
                    "rosterSlotUiAnchors",
                    rosterAnchors);
                BattlePreparationEditorUiFactory.AddBuilderMarker(
                    root,
                    SceneBuilderMarker);
                BattlePreparationEditorUiFactory.SavePrefab(root, ScenePrefabPath);
                UnityEngine.Object.DestroyImmediate(root);
                root = null;
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static Transform CreateSceneUiAnchor(
            Transform parent,
            string name,
            Rect normalizedRect,
            Bounds backgroundBounds)
        {
            GameObject anchorObject = new GameObject(name);
            Transform anchor = anchorObject.transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = new Vector3(
                Mathf.Lerp(
                    backgroundBounds.min.x,
                    backgroundBounds.max.x,
                    normalizedRect.center.x),
                Mathf.Lerp(
                    backgroundBounds.min.y,
                    backgroundBounds.max.y,
                    normalizedRect.center.y),
                0f);
            anchor.localRotation = Quaternion.identity;
            anchor.localScale = new Vector3(
                backgroundBounds.size.x * normalizedRect.width,
                backgroundBounds.size.y * normalizedRect.height,
                1f);
            return anchor;
        }

        private static void BindHomeMainEntry(Sprite icon)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(HomeMainPrefabPath);
            if (root == null)
            {
                throw new InvalidOperationException(
                    $"Cannot load HomeMain prefab: {HomeMainPrefabPath}");
            }

            try
            {
                Transform bottomBar = BattlePreparationEditorUiFactory.FindChildRecursive(
                    root.transform,
                    "BottomBar");
                if (bottomBar == null)
                {
                    throw new InvalidOperationException(
                        "win_home_main.prefab does not contain BottomBar.");
                }

                BattlePreparationEditorUiFactory.DestroyChildIfPresent(
                    bottomBar,
                    HomeMainButtonName);
                BattlePreparationEditorUiFactory.ButtonParts button =
                    BattlePreparationEditorUiFactory.AddButton(
                        HomeMainButtonName,
                        bottomBar,
                        "#UI_HomeMain_GoBattlePreparation",
                        new Color(0.10f, 0.20f, 0.27f, 0.96f),
                        18,
                        icon);
                LayoutElement layout = button.GameObject.AddComponent<LayoutElement>();
                layout.preferredWidth = 190f;
                layout.preferredHeight = 50f;
                layout.minWidth = 150f;
                layout.flexibleWidth = 0f;

                Type monoType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        HomeMainMonoType);
                Component homeMain = root.GetComponent(monoType);
                if (homeMain == null)
                {
                    throw new InvalidOperationException(
                        $"HomeMain runtime mono is missing: {HomeMainMonoType}");
                }

                BattlePreparationEditorUiFactory.SetObject(
                    homeMain,
                    "battlePreparationButton",
                    button.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    homeMain,
                    "battlePreparationButtonText",
                    button.Text);
                BattlePreparationEditorUiFactory.AddBuilderMarker(
                    root,
                    HomeMainBuilderMarker);
                PrefabUtility.SaveAsPrefabAsset(root, HomeMainPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void BindHomeSceneRuntime()
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(HomeScenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded)
            {
                scene = EditorSceneManager.OpenScene(
                    HomeScenePath,
                    OpenSceneMode.Additive);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    $"Cannot open Home scene: {HomeScenePath}");
            }

            try
            {
                GameObject homeWorld = FindGameObject(scene, "HomeWorld");
                Transform sceneRoot = FindTransform(scene, "HomeSceneRoot");
                if (homeWorld == null || sceneRoot == null)
                {
                    throw new InvalidOperationException(
                        $"Home_01 is missing HomeWorld/HomeSceneRoot: " +
                        $"homeWorld={homeWorld != null}, sceneRoot={sceneRoot != null}");
                }

                Type cameraType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        HomeCameraControllerType);
                Component cameraController = FindComponent(scene, cameraType);
                if (cameraController == null)
                {
                    throw new InvalidOperationException(
                        $"Home_01 is missing {HomeCameraControllerType}.");
                }

                Type runtimeType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        SceneRuntimeType);
                Component sceneRuntime = homeWorld.GetComponent(runtimeType)
                    ?? homeWorld.AddComponent(runtimeType);
                RemoveDuplicateComponents(scene, runtimeType, sceneRuntime);
                BattlePreparationEditorUiFactory.SetObject(
                    sceneRuntime,
                    "sceneRoot",
                    sceneRoot);
                BattlePreparationEditorUiFactory.SetObject(
                    sceneRuntime,
                    "sceneCameraController",
                    cameraController);

                Type bootstrapType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        BootstrapType);
                Component bootstrap = FindComponent(scene, bootstrapType);
                if (bootstrap == null)
                {
                    throw new InvalidOperationException(
                        $"Home_01 is missing {BootstrapType}.");
                }

                BattlePreparationEditorUiFactory.SetObject(
                    bootstrap,
                    "battlePreparationSceneRuntime",
                    sceneRuntime);
                EditorUtility.SetDirty(sceneRuntime);
                EditorUtility.SetDirty(bootstrap);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new IOException($"Failed to save scene: {HomeScenePath}");
                }
            }
            finally
            {
                if (!wasLoaded && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }
            }
        }

        private static void RemoveDuplicateComponents(
            Scene scene,
            Type type,
            Component keeper)
        {
            List<Component> all = FindComponents(scene, type);
            for (int index = 0; index < all.Count; index++)
            {
                Component component = all[index];
                if (component != null && !ReferenceEquals(component, keeper))
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }
        }

        private static void ValidateGeneratedAssets()
        {
            List<string> paths = new List<string>(
                BattlePreparationUiPrefabBuilder.GeneratedPrefabPaths)
            {
                ScenePrefabPath,
                HomeMainPrefabPath,
            };
            for (int index = 0; index < paths.Count; index++)
            {
                string path = paths[index];
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Generated prefab is unavailable: {path}");
                }

                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    prefab);
                if (missing != 0)
                {
                    throw new InvalidOperationException(
                        $"Generated prefab contains missing scripts: path={path}, count={missing}");
                }

                ValidateYamlHasNoMissingScript(path);
                if (index < BattlePreparationUiPrefabBuilder.GeneratedPrefabPaths.Length
                    && !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                        prefab,
                        GetExpectedUiPrefabMarker(path)))
                {
                    throw new InvalidOperationException(
                        $"Generated UI prefab does not carry the current builder marker: " +
                        $"path={path}, marker={GetExpectedUiPrefabMarker(path)}");
                }
            }

            ValidateYamlHasNoMissingScript(HomeScenePath);
            ValidateScenePrefabBindings();
            ValidateMainOverlayBindings();
            ValidateRobotDetailUiBindings();
            ValidateItemDetailUiBindings();
            ValidateHomeMainBindings();
            ValidateHomeSceneBindings();
            ValidateSpriteImports();
        }

        private static void ValidateScenePrefabBindings()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScenePrefabPath);
            Type presentationType =
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    ScenePresentationType);
            Component presentation = prefab != null
                ? prefab.GetComponent(presentationType)
                : null;
            if (presentation == null)
            {
                throw new InvalidOperationException(
                    "Battle preparation scene presentation is missing.");
            }

            SerializedProperty workbench =
                BattlePreparationEditorUiFactory.FindRequiredProperty(
                    presentation,
                    "workbenchUiAnchor");
            SerializedProperty roster =
                BattlePreparationEditorUiFactory.FindRequiredProperty(
                    presentation,
                    "rosterSlotUiAnchors");
            if (workbench.objectReferenceValue == null
                || !roster.isArray
                || roster.arraySize != 4)
            {
                throw new InvalidOperationException(
                    $"Battle preparation scene anchors are incomplete: " +
                    $"workbench={workbench.objectReferenceValue != null}, " +
                    $"rosterCount={(roster.isArray ? roster.arraySize : -1)}");
            }

            for (int index = 0; index < roster.arraySize; index++)
            {
                if (roster.GetArrayElementAtIndex(index).objectReferenceValue == null)
                {
                    throw new InvalidOperationException(
                        $"Battle preparation scene roster anchor is missing: " +
                        $"slotId={index + 1}");
                }
            }
        }

        private static void ValidateMainOverlayBindings()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BattlePreparationUiPrefabBuilder.MainPrefabPath);
            Type followerType =
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    SceneOverlayFollowerType);
            Component follower = prefab != null ? prefab.GetComponent(followerType) : null;
            if (follower == null)
            {
                throw new InvalidOperationException(
                    "Battle preparation scene overlay follower is missing.");
            }

            SerializedProperty overlayRoot =
                BattlePreparationEditorUiFactory.FindRequiredProperty(
                    follower,
                    "overlayRoot");
            SerializedProperty workbenchRect =
                BattlePreparationEditorUiFactory.FindRequiredProperty(
                    follower,
                    "workbenchRect");
            SerializedProperty slotRects =
                BattlePreparationEditorUiFactory.FindRequiredProperty(
                    follower,
                    "rosterSlotRects");
            if (overlayRoot.objectReferenceValue == null
                || workbenchRect.objectReferenceValue == null
                || !slotRects.isArray
                || slotRects.arraySize != 4)
            {
                throw new InvalidOperationException(
                    $"Battle preparation overlay bindings are incomplete: " +
                    $"root={overlayRoot.objectReferenceValue != null}, " +
                    $"workbench={workbenchRect.objectReferenceValue != null}, " +
                    $"rosterCount={(slotRects.isArray ? slotRects.arraySize : -1)}");
            }

            for (int index = 0; index < slotRects.arraySize; index++)
            {
                if (slotRects.GetArrayElementAtIndex(index).objectReferenceValue == null)
                {
                    throw new InvalidOperationException(
                        $"Battle preparation overlay slot rect is missing: " +
                        $"slotId={index + 1}");
                }
            }
        }

        private static void ValidateRobotDetailUiBindings()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BattlePreparationUiPrefabBuilder.RobotDetailPrefabPath);
            Type monoType =
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    RobotDetailMonoType);
            Type equipmentType =
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    EquipmentSlotType);
            Type skillSlotType =
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    SkillSlotType);
            Component mono = prefab != null ? prefab.GetComponent(monoType) : null;
            if (mono == null)
            {
                throw new InvalidOperationException(
                    "Battle robot detail prefab or GUIMono binding is missing.");
            }

            SerializedProperty equipmentSlots =
                BattlePreparationEditorUiFactory.FindRequiredProperty(
                    mono,
                    "equipmentSlotViews");
            if (!equipmentSlots.isArray || equipmentSlots.arraySize != 9)
            {
                throw new InvalidOperationException(
                    $"Battle robot detail must contain nine fixed equipment slots: " +
                    $"actual={(equipmentSlots.isArray ? equipmentSlots.arraySize : -1)}");
            }

            HashSet<int> fixedPositions = new HashSet<int>();
            int brainCount = 0;
            for (int index = 0; index < equipmentSlots.arraySize; index++)
            {
                Component equipment = equipmentSlots.GetArrayElementAtIndex(index)
                    .objectReferenceValue as Component;
                if (equipment == null || !equipmentType.IsInstanceOfType(equipment))
                {
                    throw new InvalidOperationException(
                        $"Fixed equipment slot binding is invalid: index={index}");
                }

                SerializedProperty accepted =
                    BattlePreparationEditorUiFactory.FindRequiredProperty(
                        equipment,
                        "acceptedPositionTypes");
                if (!accepted.isArray || accepted.arraySize != 1)
                {
                    throw new InvalidOperationException(
                        $"Fixed equipment slot must map exactly one position: " +
                        $"slot={equipment.name}");
                }

                int position = accepted.GetArrayElementAtIndex(0).intValue;
                if (!fixedPositions.Add(position))
                {
                    throw new InvalidOperationException(
                        $"Fixed equipment position is duplicated: positionType={position}");
                }

                if (position == 9)
                {
                    brainCount++;
                }

                ValidateEquipmentSkillStrip(equipment, $"fixed equipment {position}");
            }

            for (int position = 1; position <= 9; position++)
            {
                if (!fixedPositions.Contains(position))
                {
                    throw new InvalidOperationException(
                        $"Fixed equipment position is missing: positionType={position}");
                }
            }

            if (brainCount != 1)
            {
                throw new InvalidOperationException(
                    $"Brain equipment slot must map positionType=9 exactly once: " +
                    $"actual={brainCount}");
            }

            Component extensionTemplate = RequireReference(
                mono,
                "extensionEquipmentTemplate",
                "battle robot detail") as Component;
            if (extensionTemplate == null
                || !equipmentType.IsInstanceOfType(extensionTemplate))
            {
                throw new InvalidOperationException(
                    "Extension equipment template binding is invalid.");
            }

            ValidateEquipmentSkillStrip(extensionTemplate, "extension equipment template");

            SerializedProperty skillSlots =
                BattlePreparationEditorUiFactory.FindRequiredProperty(
                    mono,
                    "skillSlotViews");
            if (!skillSlots.isArray || skillSlots.arraySize != 6)
            {
                throw new InvalidOperationException(
                    $"Battle robot detail must prebuild six stable skill slots: " +
                    $"actual={(skillSlots.isArray ? skillSlots.arraySize : -1)}");
            }

            HashSet<int> stableSlotIds = new HashSet<int>();
            for (int index = 0; index < skillSlots.arraySize; index++)
            {
                Component skillSlot = skillSlots.GetArrayElementAtIndex(index)
                    .objectReferenceValue as Component;
                if (skillSlot == null || !skillSlotType.IsInstanceOfType(skillSlot))
                {
                    throw new InvalidOperationException(
                        $"Skill slot binding is invalid: index={index}");
                }

                int slotId = BattlePreparationEditorUiFactory.FindRequiredProperty(
                    skillSlot,
                    "slotId").intValue;
                if (slotId < 1 || slotId > 6 || !stableSlotIds.Add(slotId))
                {
                    throw new InvalidOperationException(
                        $"Skill slot id must be unique and within 1..6: slotId={slotId}");
                }

                Image hitImage = RequireReference(
                    skillSlot,
                    "backgroundImage",
                    $"skill slot {slotId}") as Image;
                if (hitImage == null || !hitImage.raycastTarget)
                {
                    throw new InvalidOperationException(
                        $"Empty skill slot must retain a raycast hit graphic: slotId={slotId}");
                }

                string[] skillSlotFields =
                {
                    "hotkeyText",
                    "dropHighlightImage",
                    "iconView",
                    "emptyRoot",
                    "readOnlyRoot",
                };
                ValidateRequiredReferences(
                    skillSlot,
                    skillSlotFields,
                    $"skill slot {slotId}");
                ValidateSkillIcon(
                    RequireReference(
                        skillSlot,
                        "iconView",
                        $"skill slot {slotId}") as Component,
                    $"skill slot {slotId}");
            }

            string[] robotDetailFields =
            {
                "enterBattleButton",
                "enterBattleButtonText",
                "skillSlotsTitleText",
                "skillDetailView",
                "comparisonCurrentSideView",
                "comparisonCandidateSideView",
                "comparisonRoot",
                "dragLayer",
                "dragIcon",
                "itemDetail",
            };
            ValidateRequiredReferences(mono, robotDetailFields, "battle robot detail");

            Component currentSide = RequireReference(
                mono,
                "comparisonCurrentSideView",
                "battle robot comparison") as Component;
            Component candidateSide = RequireReference(
                mono,
                "comparisonCandidateSideView",
                "battle robot comparison") as Component;
            ValidateComparisonSide(currentSide, "comparison current side");
            ValidateComparisonSide(candidateSide, "comparison candidate side");

            GameObject comparison = RequireReference(
                mono,
                "comparisonRoot",
                "battle robot detail") as GameObject;
            CanvasGroup comparisonGroup = comparison != null
                ? comparison.GetComponent<CanvasGroup>()
                : null;
            Image comparisonImage = comparison != null
                ? comparison.GetComponent<Image>()
                : null;
            if (comparisonGroup == null
                || comparisonGroup.blocksRaycasts
                || comparisonImage == null
                || !comparisonImage.raycastTarget)
            {
                throw new InvalidOperationException(
                    "Equipment comparison must keep its background graphic while the " +
                    "parent CanvasGroup remains raycast-transparent.");
            }

            Image dragIcon = RequireReference(
                mono,
                "dragIcon",
                "battle robot detail") as Image;
            if (dragIcon == null || dragIcon.raycastTarget)
            {
                throw new InvalidOperationException(
                    "Battle robot detail drag icon must not block raycasts.");
            }

            Component skillDetail = RequireReference(
                mono,
                "skillDetailView",
                "battle robot detail") as Component;
            string[] skillDetailFields =
            {
                "closeButton",
                "closeButtonText",
                "titleText",
                "iconImage",
                "nameText",
                "cooldownText",
                "descriptionText",
                "sourceText",
            };
            ValidateRequiredReferences(
                skillDetail,
                skillDetailFields,
                "shared skill detail");

            Transform itemDetailTransform = ReferenceTransform(RequireReference(
                mono,
                "itemDetail",
                "battle robot detail"));
            Transform skillDetailTransform = ReferenceTransform(skillDetail);
            Transform comparisonTransform = ReferenceTransform(comparison);
            Transform dragLayerTransform = ReferenceTransform(RequireReference(
                mono,
                "dragLayer",
                "battle robot detail"));
            if (itemDetailTransform == null
                || skillDetailTransform == null
                || comparisonTransform == null
                || dragLayerTransform == null
                || itemDetailTransform.GetSiblingIndex()
                    >= skillDetailTransform.GetSiblingIndex()
                || skillDetailTransform.GetSiblingIndex()
                    >= comparisonTransform.GetSiblingIndex()
                || comparisonTransform.GetSiblingIndex()
                    >= dragLayerTransform.GetSiblingIndex()
                || dragLayerTransform.GetSiblingIndex()
                    != prefab.transform.childCount - 1)
            {
                throw new InvalidOperationException(
                    "Robot detail overlay order must be ItemDetail, SkillDetail, " +
                    "Comparison, then DragLayer last.");
            }
        }

        private static void ValidateItemDetailUiBindings()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BattlePreparationUiPrefabBuilder.ItemDetailPrefabPath);
            Type monoType =
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    ItemDetailMonoType);
            Component mono = prefab != null ? prefab.GetComponent(monoType) : null;
            if (mono == null)
            {
                throw new InvalidOperationException(
                    "Battle robot item detail prefab or GUIMono binding is missing.");
            }

            string[] fields =
            {
                "descriptionText",
                "capacityRoot",
                "capacityText",
                "providedSkillsRoot",
                "providedSkillsTitleText",
                "providedSkillsList",
                "equipmentEffectList",
                "majorAffixView",
            };
            ValidateRequiredReferences(mono, fields, "battle robot item detail");
            Component skillList = RequireReference(
                mono,
                "providedSkillsList",
                "battle robot item detail") as Component;
            ValidateSkillList(skillList, "item detail provided skills");

            Transform description = ReferenceTransform(RequireReference(
                mono,
                "descriptionText",
                "battle robot item detail"));
            Transform capacity = ReferenceTransform(RequireReference(
                mono,
                "capacityRoot",
                "battle robot item detail"));
            Transform providedSkills = ReferenceTransform(RequireReference(
                mono,
                "providedSkillsRoot",
                "battle robot item detail"));
            Component effects = RequireReference(
                mono,
                "equipmentEffectList",
                "battle robot item detail") as Component;
            Component majorAffix = RequireReference(
                mono,
                "majorAffixView",
                "battle robot item detail") as Component;
            ValidateEffectList(effects, "item detail direct effects");
            ValidateMajorAffix(majorAffix, "item detail major affix");
            Transform effectsTransform = ReferenceTransform(effects);
            Transform majorAffixTransform = ReferenceTransform(majorAffix);
            if (description == null
                || capacity == null
                || providedSkills == null
                || effectsTransform == null
                || majorAffixTransform == null
                || description.GetComponentInParent<ScrollRect>(true) == null
                || capacity.GetComponentInParent<ScrollRect>(true) == null
                || providedSkills.GetComponentInParent<ScrollRect>(true) == null
                || effectsTransform.GetComponentInParent<ScrollRect>(true) == null
                || majorAffixTransform.GetComponentInParent<ScrollRect>(true) == null
                || capacity.GetSiblingIndex() >= providedSkills.GetSiblingIndex()
                || providedSkills.GetSiblingIndex() >= effectsTransform.GetSiblingIndex()
                || effectsTransform.GetSiblingIndex()
                    >= majorAffixTransform.GetSiblingIndex())
            {
                throw new InvalidOperationException(
                    "Item detail capacity, skills, effects, and major affix must " +
                    "appear in that order inside the scrollable content.");
            }
        }

        private static void ValidateComparisonSide(Component side, string context)
        {
            string[] fields =
            {
                "scrollRect",
                "sideTitleText",
                "itemHeaderRoot",
                "itemNameText",
                "qualityText",
                "emptyItemRoot",
                "emptyItemText",
                "attributesRoot",
                "attributesTitleText",
                "attributesText",
                "capacityRoot",
                "capacityTitleText",
                "capacityText",
                "providedSkillsRoot",
                "providedSkillsTitleText",
                "providedSkillsList",
                "equipmentEffectList",
                "majorAffixView",
            };
            ValidateRequiredReferences(side, fields, context);
            ScrollRect scroll = RequireReference(side, "scrollRect", context) as ScrollRect;
            Component skillList = RequireReference(
                side,
                "providedSkillsList",
                context) as Component;
            Component effects = RequireReference(
                side,
                "equipmentEffectList",
                context) as Component;
            Component majorAffix = RequireReference(
                side,
                "majorAffixView",
                context) as Component;
            ValidateSkillList(skillList, context + " skills");
            ValidateEffectList(effects, context + " direct effects");
            ValidateMajorAffix(majorAffix, context + " major affix");
            if (scroll == null
                || scroll.content == null
                || ReferenceTransform(skillList)?.GetComponentInParent<ScrollRect>(true)
                    != scroll
                || ReferenceTransform(effects)?.GetComponentInParent<ScrollRect>(true)
                    != scroll
                || ReferenceTransform(majorAffix)?.GetComponentInParent<ScrollRect>(true)
                    != scroll)
            {
                throw new InvalidOperationException(
                    $"Comparison side sections must share one scroll content: {context}");
            }
        }

        private static void ValidateEffectList(Component view, string context)
        {
            string[] fields =
            {
                "sectionRoot",
                "titleText",
                "entriesRoot",
                "entryTemplate",
                "sectionLayout",
            };
            ValidateRequiredReferences(view, fields, context);
        }

        private static void ValidateMajorAffix(Component view, string context)
        {
            string[] fields =
            {
                "sectionRoot",
                "titleText",
                "headerRoot",
                "nameText",
                "descriptionText",
                "equippedCountText",
                "stagesSectionRoot",
                "stagesRoot",
                "stageTemplate",
                "sectionLayout",
            };
            ValidateRequiredReferences(view, fields, context);
        }

        private static void ValidateEquipmentSkillStrip(
            Component equipment,
            string context)
        {
            Component itemCell = RequireReference(
                equipment,
                "itemCell",
                context) as Component;
            Component strip = RequireReference(
                equipment,
                "providedSkillStrip",
                context) as Component;
            if (itemCell == null
                || strip == null
                || itemCell.transform.parent != equipment.transform
                || strip.transform.parent != equipment.transform)
            {
                throw new InvalidOperationException(
                    $"Provided skill strip must be an ItemCell sibling: context={context}");
            }

            Image qualityFrame = RequireReference(
                itemCell,
                "qualityFrameImage",
                context + " item cell") as Image;
            Image majorAffixBadge = RequireReference(
                itemCell,
                "majorAffixBadgeImage",
                context + " item cell") as Image;
            if (qualityFrame == null
                || majorAffixBadge == null
                || majorAffixBadge.raycastTarget
                || majorAffixBadge.sprite != null)
            {
                throw new InvalidOperationException(
                    $"Item cells must retain the quality frame and use a sprite-free, " +
                    $"raycast-transparent major-affix badge: context={context}");
            }

            SerializedProperty skillViews =
                BattlePreparationEditorUiFactory.FindRequiredProperty(
                    strip,
                    "skillViews");
            if (!skillViews.isArray || skillViews.arraySize != 3)
            {
                throw new InvalidOperationException(
                    $"Equipment skill strip must prebuild three icons: context={context}");
            }

            for (int index = 0; index < skillViews.arraySize; index++)
            {
                Component icon = skillViews.GetArrayElementAtIndex(index)
                    .objectReferenceValue as Component;
                ValidateSkillIcon(icon, $"{context} provided skill {index + 1}");
            }
        }

        private static void ValidateSkillIcon(Component icon, string context)
        {
            if (icon == null)
            {
                throw new InvalidOperationException(
                    $"Skill icon component is missing: context={context}");
            }

            string[] fields =
            {
                "backgroundImage",
                "iconImage",
                "dragHighlightImage",
            };
            ValidateRequiredReferences(icon, fields, context);
            Image background = RequireReference(
                icon,
                "backgroundImage",
                context) as Image;
            Image image = RequireReference(icon, "iconImage", context) as Image;
            Image highlight = RequireReference(
                icon,
                "dragHighlightImage",
                context) as Image;
            if (background == null
                || !background.raycastTarget
                || image == null
                || image.raycastTarget
                || highlight == null
                || highlight.raycastTarget)
            {
                throw new InvalidOperationException(
                    $"Skill icon raycast configuration is invalid: context={context}");
            }
        }

        private static void ValidateSkillList(Component list, string context)
        {
            if (list == null)
            {
                throw new InvalidOperationException(
                    $"Skill list component is missing: context={context}");
            }

            Component template = RequireReference(
                list,
                "entryTemplate",
                context) as Component;
            if (RequireReference(list, "entriesRoot", context) == null
                || template == null)
            {
                throw new InvalidOperationException(
                    $"Skill list template binding is incomplete: context={context}");
            }

            string[] fields =
            {
                "iconImage",
                "nameText",
                "cooldownText",
                "descriptionText",
            };
            ValidateRequiredReferences(template, fields, $"{context} entry template");
        }

        private static void ValidateRequiredReferences(
            Component component,
            IReadOnlyList<string> propertyNames,
            string context)
        {
            if (component == null)
            {
                throw new InvalidOperationException(
                    $"Component is missing while validating references: context={context}");
            }

            for (int index = 0; index < propertyNames.Count; index++)
            {
                RequireReference(component, propertyNames[index], context);
            }
        }

        private static UnityEngine.Object RequireReference(
            Component component,
            string propertyName,
            string context)
        {
            SerializedProperty property =
                BattlePreparationEditorUiFactory.FindRequiredProperty(
                    component,
                    propertyName);
            UnityEngine.Object value = property.objectReferenceValue;
            if (value == null)
            {
                throw new InvalidOperationException(
                    $"Required prefab reference is missing: context={context}, " +
                    $"property={propertyName}");
            }

            return value;
        }

        private static Transform ReferenceTransform(UnityEngine.Object value)
        {
            if (value is Component component)
            {
                return component.transform;
            }

            if (value is GameObject gameObject)
            {
                return gameObject.transform;
            }

            return null;
        }

        private static void ValidateHomeMainBindings()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HomeMainPrefabPath);
            Type monoType =
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    HomeMainMonoType);
            Component mono = prefab != null ? prefab.GetComponent(monoType) : null;
            if (mono == null
                || BattlePreparationEditorUiFactory.FindRequiredProperty(
                    mono,
                    "battlePreparationButton").objectReferenceValue == null
                || BattlePreparationEditorUiFactory.FindRequiredProperty(
                    mono,
                    "battlePreparationButtonText").objectReferenceValue == null)
            {
                throw new InvalidOperationException(
                    "HomeMain battle preparation entry is not fully bound.");
            }
        }

        private static void ValidateHomeSceneBindings()
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(HomeScenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded)
            {
                scene = EditorSceneManager.OpenScene(
                    HomeScenePath,
                    OpenSceneMode.Additive);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    $"Cannot open Home scene for validation: {HomeScenePath}");
            }

            try
            {
                Type runtimeType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        SceneRuntimeType);
                List<Component> runtimes = FindComponents(scene, runtimeType);
                if (runtimes.Count != 1)
                {
                    throw new InvalidOperationException(
                        $"Home_01 must contain one BattlePreparationSceneRuntime: " +
                        $"actual={runtimes.Count}");
                }

                Component runtime = runtimes[0];
                if (BattlePreparationEditorUiFactory.FindRequiredProperty(
                        runtime,
                        "sceneRoot").objectReferenceValue == null
                    || BattlePreparationEditorUiFactory.FindRequiredProperty(
                        runtime,
                        "sceneCameraController").objectReferenceValue == null)
                {
                    throw new InvalidOperationException(
                        "BattlePreparationSceneRuntime references are incomplete.");
                }

                Type bootstrapType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        BootstrapType);
                Component bootstrap = FindComponent(scene, bootstrapType);
                UnityEngine.Object bound = bootstrap != null
                    ? BattlePreparationEditorUiFactory.FindRequiredProperty(
                        bootstrap,
                        "battlePreparationSceneRuntime").objectReferenceValue
                    : null;
                if (!ReferenceEquals(bound, runtime))
                {
                    throw new InvalidOperationException(
                        "TryGameRuntimeBootstrap is not bound to the scene runtime.");
                }
            }
            finally
            {
                if (!wasLoaded && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }
            }
        }

        private static void ValidateSpriteImports()
        {
            for (int index = 0; index < SpriteResourceIds.Length; index++)
            {
                int resourceId = SpriteResourceIds[index];
                string path = SpritePath(resourceId);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (importer == null
                    || sprite == null
                    || importer.textureType != TextureImporterType.Sprite
                    || importer.spriteImportMode != SpriteImportMode.Single
                    || Mathf.Abs(importer.spritePixelsPerUnit - 100f) > 0.001f
                    || !importer.alphaIsTransparency
                    || importer.mipmapEnabled
                    || importer.wrapMode != TextureWrapMode.Clamp)
                {
                    throw new InvalidOperationException(
                        $"Sprite importer validation failed: resourceId={resourceId}, path={path}");
                }
            }
        }

        private static void ValidateYamlHasNoMissingScript(string assetPath)
        {
            string fullPath = ProjectAssetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Cannot validate generated YAML: {assetPath}",
                    fullPath);
            }

            string yaml = File.ReadAllText(fullPath);
            if (yaml.IndexOf(
                    "m_Script: {fileID: 0}",
                    StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    $"Generated asset contains an empty MonoBehaviour script: {assetPath}");
            }
        }

        private static GameObject FindGameObject(Scene scene, string name)
        {
            Transform transform = FindTransform(scene, name);
            return transform != null ? transform.gameObject : null;
        }

        private static Transform FindTransform(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                Transform found = FindTransform(roots[index].transform, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindTransform(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform found = FindTransform(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Component FindComponent(Scene scene, Type type)
        {
            List<Component> all = FindComponents(scene, type);
            return all.Count > 0 ? all[0] : null;
        }

        private static List<Component> FindComponents(Scene scene, Type type)
        {
            List<Component> result = new List<Component>();
            if (type == null)
            {
                return result;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Component[] components = roots[rootIndex].GetComponentsInChildren(
                    type,
                    true);
                for (int componentIndex = 0;
                    componentIndex < components.Length;
                    componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component != null && component.gameObject.scene == scene)
                    {
                        result.Add(component);
                    }
                }
            }

            return result;
        }

        private static string SpritePath(int resourceId)
        {
            return $"Assets/Resources/TryGameBuildRes/gui/sprite/" +
                $"spt_{resourceId}/spt_{resourceId}_1.png";
        }

        private static string ProjectAssetFullPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException(
                    "Project asset path cannot be empty.",
                    nameof(assetPath));
            }

            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

    }
}
#endif
