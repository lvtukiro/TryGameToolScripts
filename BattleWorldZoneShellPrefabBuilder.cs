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
    /// Idempotent 2.0f asset builder. It creates only the Battle WorldZone shell,
    /// its Normal UI, the selected-robot detail entry and Home_01 bindings.
    /// </summary>
    public static class BattleWorldZoneShellPrefabBuilder
    {
        private const string MenuPath =
            "TryGame/Battle WorldZone/Rebuild 2.0f Shell";
        private const string ScenePrefabPath =
            "Assets/Resources/TryGameBuildRes/battle/runtime/battle_world_zone_shell.prefab";
        private const string UiPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_world_zone.prefab";
        private const string PreparationUiPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_robot_detail.prefab";
        private const string HomeScenePath =
            "Assets/Resources/TryGameBuildRes/scene/Home_01.unity";
        private const string SceneMarker = "__BattleWorldZoneShell_v1";
        private const string UiMarker = "__BattleWorldZoneUi_v1";

        private const string ScenePresentationType =
            "Game.BattleWorldZoneScenePresentation";
        private const string SceneRuntimeType =
            "Game.BattleWorldZoneSceneRuntime";
        private const string UiMonoType = "Game.GUIMonoBattleWorldZone";
        private const string PreparationUiMonoType =
            "Game.GUIMonoBattleRobotDetail";
        private const string HomeCameraControllerType =
            "Game.HomeSceneCameraController";
        private const string BootstrapType = "Game.TryGameRuntimeBootstrap";

        private static readonly string[] RequiredRuntimeTypes =
        {
            ScenePresentationType,
            SceneRuntimeType,
            UiMonoType,
            PreparationUiMonoType,
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
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.delayCall -= EnsureBuiltAfterReload;
            EditorApplication.delayCall += EnsureBuiltAfterReload;
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
                    $"[BattleWorldZoneShellPrefabBuilder] Automatic 2.0f shell build failed. " +
                    $"After fixing compilation, run {MenuPath}.\n{exception}");
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
            if (scenePrefab == null
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    scenePrefab,
                    SceneMarker))
            {
                return true;
            }

            GameObject uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                UiPrefabPath);
            if (uiPrefab == null
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    uiPrefab,
                    UiMarker))
            {
                return true;
            }

            GameObject preparation = AssetDatabase.LoadAssetAtPath<GameObject>(
                PreparationUiPrefabPath);
            return preparation == null
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    preparation,
                    BattlePreparationUiPrefabBuilder.BattleDevelopmentEntryMarker)
                || !TryValidateHomeSceneBindings(out _);
        }

        private static void BuildAll(bool logSuccess)
        {
            if (!RuntimeTypesAreReady())
            {
                throw new InvalidOperationException(
                    "Battle WorldZone runtime types are not compiled yet.");
            }

            BuildScenePrefab();
            BuildUiPrefab();
            PatchRobotDetailDevelopmentEntry();
            BindHomeSceneRuntime();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateGeneratedAssets();

            if (logSuccess)
            {
                Debug.Log(
                    "[BattleWorldZoneShellPrefabBuilder] 2.0f Battle WorldZone shell, " +
                    "Normal UI, selected-robot entry and Home_01 bindings are ready.");
            }
        }

        private static void BuildScenePrefab()
        {
            GameObject root = new GameObject("battle_world_zone_shell");
            try
            {
                Component presentation =
                    BattlePreparationEditorUiFactory.AddRuntimeComponent(
                        root,
                        ScenePresentationType);

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

                BattlePreparationEditorUiFactory.SetObject(
                    presentation,
                    "sceneCamera",
                    camera);
                BattlePreparationEditorUiFactory.SetSerializedRect(
                    presentation,
                    "cameraBounds",
                    new Rect(-9f, -5f, 18f, 10f));
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
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(
                "win_battle_world_zone",
                null);
            try
            {
                BattlePreparationEditorUiFactory.Stretch(
                    root.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.AddImage(
                    root,
                    new Color(0f, 0f, 0f, 0f),
                    null,
                    false);
                Component mono =
                    BattlePreparationEditorUiFactory.AddRuntimeComponent(
                        root,
                        UiMonoType);

                GameObject topBar = BattlePreparationEditorUiFactory.AddPanel(
                    "TopBar",
                    root.transform,
                    new Color(0.025f, 0.05f, 0.09f, 0.94f),
                    true);
                BattlePreparationEditorUiFactory.SetRect(
                    topBar.GetComponent<RectTransform>(),
                    new Vector2(0f, 0.9f),
                    Vector2.one,
                    Vector2.zero,
                    Vector2.zero);

                GameObject titleObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "Title",
                        topBar.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    titleObject.GetComponent<RectTransform>(),
                    new Vector2(0f, 0f),
                    new Vector2(0.55f, 1f),
                    new Vector2(28f, 0f),
                    Vector2.zero);
                Text title = BattlePreparationEditorUiFactory.AddText(
                    titleObject,
                    "战斗区域（开发空壳）",
                    32,
                    TextAnchor.MiddleLeft);

                BattlePreparationEditorUiFactory.ButtonParts settings =
                    BattlePreparationEditorUiFactory.AddButton(
                        "SettingsButton",
                        topBar.transform,
                        "设置",
                        BattlePreparationEditorUiFactory.PanelLightColor,
                        22);
                BattlePreparationEditorUiFactory.Place(
                    settings.Rect,
                    new Vector2(0.82f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(124f, 50f));

                BattlePreparationEditorUiFactory.ButtonParts returnHome =
                    BattlePreparationEditorUiFactory.AddButton(
                        "ReturnHomeButton",
                        topBar.transform,
                        "返回家园",
                        BattlePreparationEditorUiFactory.AccentMutedColor,
                        22);
                BattlePreparationEditorUiFactory.Place(
                    returnHome.Rect,
                    new Vector2(0.94f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(164f, 50f));

                GameObject statusPanel =
                    BattlePreparationEditorUiFactory.AddPanel(
                        "ShellStatusPanel",
                        root.transform,
                        new Color(0.035f, 0.075f, 0.125f, 0.9f),
                        false);
                BattlePreparationEditorUiFactory.Place(
                    statusPanel.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.53f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(760f, 250f));
                Text status = BattlePreparationEditorUiFactory.AddTextChild(
                    "Status",
                    statusPanel.transform,
                    "WorldZone 400\n场景运行壳已就绪；地图与战斗将在后续版本接入。",
                    28,
                    TextAnchor.MiddleCenter,
                    new Color(0.76f, 0.88f, 1f, 1f),
                    24f);

                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "titleText",
                    title);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "statusText",
                    status);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "settingsButton",
                    settings.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "settingsButtonText",
                    settings.Text);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "returnHomeButton",
                    returnHome.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "returnHomeButtonText",
                    returnHome.Text);

                BattlePreparationEditorUiFactory.AddBuilderMarker(root, UiMarker);
                BattlePreparationEditorUiFactory.SavePrefab(root, UiPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void PatchRobotDetailDevelopmentEntry()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PreparationUiPrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException(
                    "Battle preparation UI prefab is missing.",
                    PreparationUiPrefabPath);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(
                PreparationUiPrefabPath);
            try
            {
                Type monoType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        PreparationUiMonoType);
                Component mono = root.GetComponent(monoType);
                Transform detailRoot = root.transform;
                if (mono == null)
                {
                    throw new InvalidOperationException(
                        "Battle robot detail UI cannot be patched because its mono is missing.");
                }

                BattlePreparationEditorUiFactory.DestroyChildIfPresent(
                    detailRoot,
                    "BattleDevelopmentButton");
                BattlePreparationEditorUiFactory.DestroyChildIfPresent(
                    detailRoot,
                    "EnterBattleButton");
                BattlePreparationEditorUiFactory.ButtonParts button =
                    BattlePreparationEditorUiFactory.AddButton(
                        "EnterBattleButton",
                        detailRoot,
                        "进入战斗区域",
                        new Color(0.16f, 0.36f, 0.58f, 0.96f),
                        21);
                BattlePreparationEditorUiFactory.Place(
                    button.Rect,
                    new Vector2(0.84f, 0.955f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(190f, 46f));
                button.Button.interactable = false;
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "enterBattleButton",
                    button.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "enterBattleButtonText",
                    button.Text);
                BattlePreparationEditorUiFactory.AddBuilderMarker(
                    root,
                    BattlePreparationUiPrefabBuilder.BattleDevelopmentEntryMarker);

                if (PrefabUtility.SaveAsPrefabAsset(root, PreparationUiPrefabPath) == null)
                {
                    throw new IOException(
                        $"Failed to save patched preparation UI: {PreparationUiPrefabPath}");
                }
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
                        $"homeWorld={homeWorld != null}, sceneRoot={sceneRoot != null}.");
                }

                Type runtimeType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        SceneRuntimeType);
                Component runtime = homeWorld.GetComponent(runtimeType)
                    ?? homeWorld.AddComponent(runtimeType);
                RemoveDuplicateComponents(scene, runtimeType, runtime);

                Type cameraType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        HomeCameraControllerType);
                Component cameraController = FindComponent(scene, cameraType);
                Type bootstrapType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        BootstrapType);
                Component bootstrap = FindComponent(scene, bootstrapType);
                if (cameraController == null || bootstrap == null)
                {
                    throw new InvalidOperationException(
                        $"Home_01 Battle shell binding prerequisites are missing: " +
                        $"cameraController={cameraController != null}, bootstrap={bootstrap != null}.");
                }

                BattlePreparationEditorUiFactory.SetObject(
                    runtime,
                    "sceneRoot",
                    sceneRoot);
                BattlePreparationEditorUiFactory.SetObject(
                    runtime,
                    "sceneCameraController",
                    cameraController);
                BattlePreparationEditorUiFactory.SetObject(
                    bootstrap,
                    "battleWorldZoneSceneRuntime",
                    runtime);
                EditorUtility.SetDirty(runtime);
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

        private static void ValidateGeneratedAssets()
        {
            GameObject scenePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ScenePrefabPath);
            GameObject uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                UiPrefabPath);
            GameObject preparationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PreparationUiPrefabPath);
            if (scenePrefab == null
                || uiPrefab == null
                || preparationPrefab == null
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    scenePrefab,
                    SceneMarker)
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    uiPrefab,
                    UiMarker)
                || !BattlePreparationEditorUiFactory.ContainsBuilderMarker(
                    preparationPrefab,
                    BattlePreparationUiPrefabBuilder.BattleDevelopmentEntryMarker))
            {
                throw new InvalidOperationException(
                    "Battle WorldZone generated prefab validation failed.");
            }

            Type presentationType =
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    ScenePresentationType);
            Component presentation = scenePrefab.GetComponent(presentationType);
            if (presentation == null
                || BattlePreparationEditorUiFactory.FindRequiredProperty(
                    presentation,
                    "sceneCamera").objectReferenceValue == null)
            {
                throw new InvalidOperationException(
                    "Battle WorldZone shell presentation binding is incomplete.");
            }

            Type uiType =
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    UiMonoType);
            Component uiMono = uiPrefab.GetComponent(uiType);
            if (uiMono == null)
            {
                throw new InvalidOperationException(
                    "Battle WorldZone Normal UI component is missing.");
            }

            string[] uiProperties =
            {
                "titleText",
                "statusText",
                "settingsButton",
                "settingsButtonText",
                "returnHomeButton",
                "returnHomeButtonText",
            };
            for (int index = 0; index < uiProperties.Length; index++)
            {
                if (BattlePreparationEditorUiFactory.FindRequiredProperty(
                        uiMono,
                        uiProperties[index]).objectReferenceValue == null)
                {
                    throw new InvalidOperationException(
                        $"Battle WorldZone UI binding is missing: {uiProperties[index]}.");
                }
            }

            if (!TryValidateHomeSceneBindings(out string sceneError))
            {
                throw new InvalidOperationException(
                    $"Battle WorldZone Home_01 binding validation failed: {sceneError}");
            }
        }

        private static bool TryValidateHomeSceneBindings(out string error)
        {
            error = string.Empty;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(HomeScenePath) == null)
            {
                error = $"Home scene asset is missing: {HomeScenePath}.";
                return false;
            }

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(HomeScenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            try
            {
                if (!wasLoaded)
                {
                    scene = EditorSceneManager.OpenScene(
                        HomeScenePath,
                        OpenSceneMode.Additive);
                }

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    error = $"Cannot open Home scene: {HomeScenePath}.";
                    return false;
                }

                Type runtimeType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        SceneRuntimeType);
                List<Component> runtimes = FindComponents(scene, runtimeType);
                if (runtimes.Count != 1)
                {
                    error =
                        $"Expected exactly one BattleWorldZoneSceneRuntime, " +
                        $"actual={runtimes.Count}.";
                    return false;
                }

                Transform expectedRoot = FindTransform(scene, "HomeSceneRoot");
                Type cameraType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        HomeCameraControllerType);
                Component expectedCamera = FindComponent(scene, cameraType);
                Type bootstrapType =
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        BootstrapType);
                Component bootstrap = FindComponent(scene, bootstrapType);
                if (expectedRoot == null || expectedCamera == null || bootstrap == null)
                {
                    error =
                        $"Home scene prerequisites are missing: root={expectedRoot != null}, " +
                        $"camera={expectedCamera != null}, bootstrap={bootstrap != null}.";
                    return false;
                }

                Component runtime = runtimes[0];
                UnityEngine.Object boundRoot =
                    BattlePreparationEditorUiFactory.FindRequiredProperty(
                        runtime,
                        "sceneRoot").objectReferenceValue;
                UnityEngine.Object boundCamera =
                    BattlePreparationEditorUiFactory.FindRequiredProperty(
                        runtime,
                        "sceneCameraController").objectReferenceValue;
                UnityEngine.Object boundRuntime =
                    BattlePreparationEditorUiFactory.FindRequiredProperty(
                        bootstrap,
                        "battleWorldZoneSceneRuntime").objectReferenceValue;
                if (!ReferenceEquals(boundRoot, expectedRoot)
                    || !ReferenceEquals(boundCamera, expectedCamera)
                    || !ReferenceEquals(boundRuntime, runtime))
                {
                    error =
                        $"Serialized binding mismatch: root={ReferenceEquals(boundRoot, expectedRoot)}, " +
                        $"camera={ReferenceEquals(boundCamera, expectedCamera)}, " +
                        $"bootstrap={ReferenceEquals(boundRuntime, runtime)}.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                return false;
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

        private static GameObject FindGameObject(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                GameObject found = FindGameObjectRecursive(roots[index], name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject FindGameObjectRecursive(GameObject current, string name)
        {
            if (current != null && string.Equals(current.name, name, StringComparison.Ordinal))
            {
                return current;
            }

            if (current == null)
            {
                return null;
            }

            for (int index = 0; index < current.transform.childCount; index++)
            {
                GameObject found = FindGameObjectRecursive(
                    current.transform.GetChild(index).gameObject,
                    name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindTransform(Scene scene, string name)
        {
            return FindGameObject(scene, name)?.transform;
        }

        private static Component FindComponent(Scene scene, Type type)
        {
            if (type == null)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                Component found = roots[index].GetComponentInChildren(type, true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static List<Component> FindComponents(Scene scene, Type type)
        {
            List<Component> result = new List<Component>();
            if (type == null)
            {
                return result;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                result.AddRange(roots[index].GetComponentsInChildren(type, true));
            }

            return result;
        }

        private static void RemoveDuplicateComponents(
            Scene scene,
            Type type,
            Component keep)
        {
            List<Component> found = FindComponents(scene, type);

            for (int index = 0; index < found.Count; index++)
            {
                Component component = found[index];
                if (component != null && !ReferenceEquals(component, keep))
                {
                    UnityEngine.Object.DestroyImmediate(component, true);
                }
            }
        }
    }
}
#endif
