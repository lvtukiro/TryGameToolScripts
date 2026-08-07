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
            "TryGame/Battle Preparation/Rebuild 2.0b Assets";
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
                    $"[BattlePreparationPrefabBuilder] 自动生成 2.0b 资源失败。" +
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
                    "[BattlePreparationPrefabBuilder] 2.0b 备战间资源已完成：" +
                    "Sprite、6 个 UI Prefab、场景 Prefab、HomeMain 和 Home_01 引用均已更新。");
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
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null
                    || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                        prefab,
                        BattlePreparationUiPrefabBuilder.BuilderMarker))
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
            }

            ValidateYamlHasNoMissingScript(HomeScenePath);
            ValidateScenePrefabBindings();
            ValidateMainOverlayBindings();
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
