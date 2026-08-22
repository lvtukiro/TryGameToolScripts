#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// 2.0b 备战间 UI Prefab 的纯生成实现。
    /// 运行时逻辑只存在于 Presentation/Application；这里仅创建层级、布局和序列化引用。
    /// </summary>
    internal static class BattlePreparationUiPrefabBuilder
    {
        internal const string MainPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_preparation.prefab";
        internal const string ProductionPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_robot_production.prefab";
        internal const string NameInputPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/sub_robot_name_input.prefab";
        internal const string RobotDetailPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_robot_detail.prefab";
        internal const string ItemDetailPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/sub_robot_item_detail.prefab";
        internal const string WorkbenchPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_workbench.prefab";
        internal const string StageMapPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_stage_map.prefab";
        internal const string StageDetailPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_stage_detail.prefab";
        internal const string ChallengeSelectionPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_challenge_selection.prefab";
        internal const string TargetSelectionPrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_target_selection.prefab";

        // 维修倒计时只属于主备战界面；其它备战相关预制体继续沿用旧标记，
        // 避免每次主界面增加字段时被总 Builder 一并重建。
        internal const string BuilderMarker = "__BattlePreparationUiBuilder_v10_2_0g";
        internal const string MainBuilderMarker =
            "__BattlePreparationUiBuilder_v11_repair_countdown";
        internal const string BattleDevelopmentEntryMarker =
            "__BattleRobotDetailBattleEntry_v2";

        private const string MainMono = "Game.GUIMonoBattlePreparationMain";
        private const string RosterSlotMono = "Game.BattleRobotRosterSlotView";
        private const string SceneOverlayFollowerMono =
            "Game.BattlePreparationSceneOverlayFollower";
        private const string ProductionMono = "Game.GUIMonoBattleRobotProduction";
        private const string ProductionCardMono = "Game.BattleRobotProductionCardView";
        private const string NameInputMono = "Game.GUIMonoBattleRobotNameInput";
        private const string RobotDetailMono = "Game.GUIMonoBattleRobotDetail";
        private const string ItemDetailMono = "Game.GUIMonoBattleRobotItemDetail";
        private const string EquipmentSlotMono = "Game.BattleRobotEquipmentSlotView";
        private const string ContainerGridMono = "Game.BattleRobotContainerGridView";
        private const string ItemCellMono = "Game.BattleRobotItemCellView";
        private const string EquipmentSkillStripMono =
            "Game.BattleRobotEquipmentSkillStripView";
        private const string SkillIconMono = "Game.BattleRobotSkillIconView";
        private const string SkillSlotMono = "Game.BattleRobotSkillSlotView";
        private const string SkillListMono = "Game.BattleRobotSkillListView";
        private const string SkillListEntryMono = "Game.BattleRobotSkillListEntryView";
        private const string EquipmentEffectListMono =
            "Game.BattleRobotEquipmentEffectListView";
        private const string MajorAffixMono = "Game.BattleRobotMajorAffixView";
        private const string ComparisonSideMono = "Game.BattleRobotComparisonSideView";
        private const string SkillDetailMono = "Game.BattleRobotSkillDetailView";
        private const string WorkbenchMono = "Game.GUIMonoBattlePreparationWorkbench";
        private const string StageMapMono = "Game.GUIMonoBattleStageMap";
        private const string StageMapPointMono = "Game.BattleStageMapPointView";
        private const string StageDetailMono = "Game.GUIMonoBattleStageDetail";
        private const string DeploymentRobotSlotMono = "Game.BattleDeploymentRobotSlotView";
        private const string RestrictionEntryMono = "Game.BattleRestrictionEntryView";
        private const string ChallengeSelectionMono = "Game.GUIMonoBattleChallengeSelection";
        private const string ChallengeColumnMono = "Game.BattleChallengeColumnView";
        private const string ChallengeLevelCellMono = "Game.BattleChallengeLevelCellView";
        private const string SelectedChallengeEntryMono = "Game.BattleSelectedChallengeEntryView";
        private const string TargetSelectionMono = "Game.GUIMonoBattleTargetSelection";
        private const string TargetToggleOptionMono = "Game.BattleTargetToggleOptionView";

        internal static readonly string[] RequiredRuntimeTypes =
        {
            MainMono,
            RosterSlotMono,
            SceneOverlayFollowerMono,
            ProductionMono,
            ProductionCardMono,
            NameInputMono,
            RobotDetailMono,
            ItemDetailMono,
            EquipmentSlotMono,
            ContainerGridMono,
            ItemCellMono,
            EquipmentSkillStripMono,
            SkillIconMono,
            SkillSlotMono,
            SkillListMono,
            SkillListEntryMono,
            EquipmentEffectListMono,
            MajorAffixMono,
            ComparisonSideMono,
            SkillDetailMono,
            WorkbenchMono,
            StageMapMono,
            StageMapPointMono,
            StageDetailMono,
            DeploymentRobotSlotMono,
            RestrictionEntryMono,
            ChallengeSelectionMono,
            ChallengeColumnMono,
            ChallengeLevelCellMono,
            SelectedChallengeEntryMono,
            TargetSelectionMono,
            TargetToggleOptionMono,
        };

        internal static readonly string[] GeneratedPrefabPaths =
        {
            MainPrefabPath,
            ProductionPrefabPath,
            NameInputPrefabPath,
            RobotDetailPrefabPath,
            ItemDetailPrefabPath,
            WorkbenchPrefabPath,
            StageMapPrefabPath,
            StageDetailPrefabPath,
            ChallengeSelectionPrefabPath,
            TargetSelectionPrefabPath,
        };

        // 以背景图片左下角为 (0, 0)、右上角为 (1, 1)。这些区域对应产品图上
        // 的工作台和四个机器人站位；场景 prefab 与 UI fallback 共用同一事实。
        internal static readonly Rect WorkbenchSceneNormalizedRect =
            new Rect(0.032f, 0.148f, 0.263f, 0.632f);

        internal static readonly Rect[] RosterSceneNormalizedRects =
        {
            new Rect(0.518f, 0.234f, 0.088f, 0.335f),
            new Rect(0.668f, 0.306f, 0.114f, 0.266f),
            new Rect(0.827f, 0.289f, 0.090f, 0.282f),
            new Rect(0.930f, 0.306f, 0.062f, 0.266f),
        };

        internal static void BuildAll(IReadOnlyDictionary<int, Sprite> sprites)
        {
            BuildNameInputPrefab();
            BuildItemDetailPrefab();
            BuildMainPrefab(sprites);
            BuildProductionPrefab(sprites);
            BuildRobotDetailPrefab(sprites);
            BuildWorkbenchPrefab();
            BattleStageSelectionUiPrefabBuilder.BuildAll();
        }

        private static void BuildMainPrefab(IReadOnlyDictionary<int, Sprite> sprites)
        {
            GameObject root = CreateWindowRoot(
                "win_battle_preparation",
                new Color(0f, 0f, 0f, 0f),
                false,
                MainBuilderMarker);
            try
            {
                Component mono = Runtime(root, MainMono);
                Component sceneOverlayFollower = Runtime(
                    root,
                    SceneOverlayFollowerMono);

                GameObject topBar = CreatePanel(
                    "TopBar",
                    root.transform,
                    new Vector2(0f, 0.91f),
                    Vector2.one,
                    new Vector2(18f, -6f),
                    new Vector2(-18f, -12f),
                    new Color(0.025f, 0.045f, 0.075f, 0.88f));
                Text title = CreateText(
                    "Title",
                    topBar.transform,
                    "备战间",
                    34,
                    TextAnchor.MiddleLeft,
                    new Vector2(0f, 0f),
                    new Vector2(0.34f, 1f),
                    new Vector2(24f, 0f),
                    Vector2.zero);
                Text gold = CreateText(
                    "Gold",
                    topBar.transform,
                    "0",
                    26,
                    TextAnchor.MiddleRight,
                    new Vector2(0.58f, 0f),
                    new Vector2(0.72f, 1f),
                    Vector2.zero,
                    new Vector2(-12f, 0f),
                    new Color(1f, 0.82f, 0.26f, 1f));

                BattlePreparationEditorUiFactory.ButtonParts settings =
                    BattlePreparationEditorUiFactory.AddButton(
                        "SettingsButton",
                        topBar.transform,
                        "设置",
                        BattlePreparationEditorUiFactory.PanelLightColor,
                        22);
                BattlePreparationEditorUiFactory.Place(
                    settings.Rect,
                    new Vector2(0.83f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(118f, 48f));

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
                    new Vector2(156f, 48f));

                BattlePreparationEditorUiFactory.ButtonParts battleDevelopment =
                    BattlePreparationEditorUiFactory.AddButton(
                        "BattleDevelopmentButton",
                        topBar.transform,
                        "进入战斗区域",
                        new Color(0.16f, 0.36f, 0.58f, 0.96f),
                        21);
                BattlePreparationEditorUiFactory.Place(
                    battleDevelopment.Rect,
                    new Vector2(0.9f, 0f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -34f),
                    new Vector2(190f, 48f));
                battleDevelopment.Button.gameObject.SetActive(false);

                BattlePreparationEditorUiFactory.ButtonParts battle =
                    BattlePreparationEditorUiFactory.AddButton(
                        "BattleButton",
                        topBar.transform,
                        "出战",
                        new Color(0.17f, 0.58f, 0.35f, 0.98f),
                        22);
                BattlePreparationEditorUiFactory.Place(
                    battle.Rect,
                    new Vector2(0.735f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(130f, 48f));

                BattlePreparationEditorUiFactory.ButtonParts workbench =
                    CreateTransparentHotspot(
                        "WorkbenchButton",
                        root.transform,
                        WorkbenchSceneNormalizedRect);

                List<UnityEngine.Object> slotViews = new List<UnityEngine.Object>(4);
                List<UnityEngine.Object> slotRects = new List<UnityEngine.Object>(4);
                for (int slotId = 1; slotId <= 4; slotId++)
                {
                    Component slotView = BuildRosterSlot(
                        root.transform,
                        slotId,
                        SpriteAt(sprites, 4001),
                        SpriteAt(sprites, 4002),
                        SpriteAt(sprites, 4003),
                        SpriteAt(sprites, 4004));
                    RectTransform slotRect = slotView.GetComponent<RectTransform>();
                    SetNormalizedRect(
                        slotRect,
                        RosterSceneNormalizedRects[slotId - 1]);
                    slotViews.Add(slotView);
                    slotRects.Add(slotRect);
                }

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(mono, "goldText", gold);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "settingsButton",
                    settings.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "returnHomeButton",
                    returnHome.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "returnHomeButtonText",
                    returnHome.Text);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "workbenchButton",
                    workbench.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "battleButton",
                    battle.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "battleButtonText",
                    battle.Text);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "battleDevelopmentButton",
                    battleDevelopment.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "battleDevelopmentButtonText",
                    battleDevelopment.Text);
                BattlePreparationEditorUiFactory.SetObjects(
                    mono,
                    "rosterSlotViews",
                    slotViews);
                BattlePreparationEditorUiFactory.SetObject(
                    sceneOverlayFollower,
                    "overlayRoot",
                    root.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(
                    sceneOverlayFollower,
                    "workbenchRect",
                    workbench.Rect);
                BattlePreparationEditorUiFactory.SetObjects(
                    sceneOverlayFollower,
                    "rosterSlotRects",
                    slotRects);
                SaveAndDestroy(root, MainPrefabPath);
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

        private static Component BuildRosterSlot(
            Transform parent,
            int slotId,
            Sprite addSprite,
            Sprite deleteSprite,
            Sprite lockSprite,
            Sprite robotSprite)
        {
            GameObject slot = BattlePreparationEditorUiFactory.NewUiObject(
                $"RobotSlot_{slotId}",
                parent);
            Image slotImage = BattlePreparationEditorUiFactory.AddImage(
                slot,
                new Color(1f, 1f, 1f, 0.001f),
                null,
                true);
            Button slotButton = slot.AddComponent<Button>();
            slotButton.targetGraphic = slotImage;
            Component view = Runtime(slot, RosterSlotMono);

            GameObject lockedRoot = CreateStateRoot("Locked", slot.transform);
            Image lockedImage = CreateImage(
                "LockIcon",
                lockedRoot.transform,
                lockSprite,
                Color.white,
                true,
                new Vector2(0.28f, 0.34f),
                new Vector2(0.72f, 0.79f),
                Vector2.zero,
                Vector2.zero);
            Text price = CreateText(
                "UnlockPrice",
                lockedRoot.transform,
                "0",
                15,
                TextAnchor.MiddleCenter,
                new Vector2(0.12f, 0.10f),
                new Vector2(0.88f, 0.33f),
                Vector2.zero,
                Vector2.zero,
                new Color(1f, 0.78f, 0.22f, 1f));

            GameObject emptyRoot = CreateStateRoot("Empty", slot.transform);
            Image emptyImage = CreateImage(
                "AddIcon",
                emptyRoot.transform,
                addSprite,
                Color.white,
                true,
                new Vector2(0.30f, 0.37f),
                new Vector2(0.70f, 0.78f),
                Vector2.zero,
                Vector2.zero);
            Text emptyText = CreateText(
                "EmptyText",
                emptyRoot.transform,
                "生产机器人",
                13,
                TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.10f),
                new Vector2(0.92f, 0.34f),
                Vector2.zero,
                Vector2.zero);

            GameObject occupiedRoot = CreateStateRoot("Occupied", slot.transform);
            Image robotImage = CreateImage(
                "RobotImage",
                occupiedRoot.transform,
                robotSprite,
                Color.white,
                true,
                new Vector2(0.15f, 0.22f),
                new Vector2(0.85f, 0.92f),
                Vector2.zero,
                Vector2.zero);
            Text name = CreateText(
                "RobotName",
                occupiedRoot.transform,
                string.Empty,
                14,
                TextAnchor.MiddleCenter,
                new Vector2(0.06f, 0.10f),
                new Vector2(0.68f, 0.24f),
                Vector2.zero,
                Vector2.zero);
            Text state = CreateText(
                "RobotState",
                occupiedRoot.transform,
                string.Empty,
                12,
                TextAnchor.MiddleRight,
                new Vector2(0.66f, 0.10f),
                new Vector2(0.94f, 0.24f),
                Vector2.zero,
                Vector2.zero);
            Text repairCountdown = CreateText(
                "RepairCountdown",
                occupiedRoot.transform,
                string.Empty,
                11,
                TextAnchor.MiddleRight,
                new Vector2(0.48f, 0.02f),
                new Vector2(0.94f, 0.13f),
                Vector2.zero,
                Vector2.zero,
                new Color(1f, 0.72f, 0.28f, 1f));

            GameObject destroyObject = BattlePreparationEditorUiFactory.NewUiObject(
                "DestroyButton",
                occupiedRoot.transform);
            BattlePreparationEditorUiFactory.Place(
                destroyObject.GetComponent<RectTransform>(),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-4f, -4f),
                new Vector2(28f, 28f));
            Image destroyImage = BattlePreparationEditorUiFactory.AddImage(
                destroyObject,
                new Color(1f, 0.35f, 0.31f, 1f),
                deleteSprite,
                true,
                true);
            Button destroyButton = destroyObject.AddComponent<Button>();
            destroyButton.targetGraphic = destroyImage;

            emptyRoot.SetActive(false);
            occupiedRoot.SetActive(false);

            BattlePreparationEditorUiFactory.SetInt(view, "slotId", slotId);
            BattlePreparationEditorUiFactory.SetObject(view, "mainButton", slotButton);
            BattlePreparationEditorUiFactory.SetObject(view, "lockedRoot", lockedRoot);
            BattlePreparationEditorUiFactory.SetObject(view, "lockedImage", lockedImage);
            BattlePreparationEditorUiFactory.SetObject(view, "unlockPriceText", price);
            BattlePreparationEditorUiFactory.SetObject(view, "emptyRoot", emptyRoot);
            BattlePreparationEditorUiFactory.SetObject(view, "emptyImage", emptyImage);
            BattlePreparationEditorUiFactory.SetObject(view, "emptyText", emptyText);
            BattlePreparationEditorUiFactory.SetObject(view, "occupiedRoot", occupiedRoot);
            BattlePreparationEditorUiFactory.SetObject(view, "robotImage", robotImage);
            BattlePreparationEditorUiFactory.SetObject(view, "robotNameText", name);
            BattlePreparationEditorUiFactory.SetObject(view, "robotStateText", state);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "repairCountdownText",
                repairCountdown);
            BattlePreparationEditorUiFactory.SetObject(view, "destroyButton", destroyButton);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "destroyButtonImage",
                destroyImage);
            return view;
        }

        private static void BuildProductionPrefab(IReadOnlyDictionary<int, Sprite> sprites)
        {
            GameObject root = CreateWindowRoot(
                "win_robot_production",
                BattlePreparationEditorUiFactory.OverlayColor,
                true);
            try
            {
                Component mono = Runtime(root, ProductionMono);
                Image background = CreateImage(
                    "SelectionBackground",
                    root.transform,
                    SpriteAt(sprites, 4011),
                    Color.white,
                    false,
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero,
                    Vector2.zero);
                background.transform.SetAsFirstSibling();

                Text title = CreateText(
                    "Title",
                    root.transform,
                    "生产机器人",
                    38,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.06f, 0.88f),
                    new Vector2(0.55f, 0.97f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts close =
                    BattlePreparationEditorUiFactory.AddButton(
                        "CloseButton",
                        root.transform,
                        "关闭",
                        BattlePreparationEditorUiFactory.WarningColor,
                        22);
                BattlePreparationEditorUiFactory.Place(
                    close.Rect,
                    new Vector2(0.95f, 0.93f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(130f, 48f));

                RectTransform cardRoot = BattlePreparationEditorUiFactory.NewRect(
                    "Cards",
                    root.transform,
                    new Vector2(0.07f, 0.10f),
                    new Vector2(0.93f, 0.86f),
                    Vector2.zero,
                    Vector2.zero);
                GridLayoutGroup grid = cardRoot.gameObject.AddComponent<GridLayoutGroup>();
                grid.padding = new RectOffset(24, 24, 24, 24);
                grid.spacing = new Vector2(24f, 20f);
                grid.cellSize = new Vector2(390f, 650f);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 3;
                grid.childAlignment = TextAnchor.MiddleCenter;

                int[] previewSprites = { 4005, 4006, 4007 };
                string[] previewNames = { "战士", "法师", "游侠" };
                List<UnityEngine.Object> cards = new List<UnityEngine.Object>(3);
                for (int index = 0; index < previewSprites.Length; index++)
                {
                    cards.Add(BuildProductionCard(
                        $"ProductionCard_{index + 1}",
                        cardRoot,
                        SpriteAt(sprites, previewSprites[index]),
                        previewNames[index],
                        true));
                }

                Component template = BuildProductionCard(
                    "DynamicCardTemplate",
                    cardRoot,
                    SpriteAt(sprites, 4004),
                    string.Empty,
                    false);
                template.gameObject.SetActive(false);

                GameObject namePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    NameInputPrefabPath);
                if (namePrefab == null)
                {
                    throw new InvalidOperationException(
                        $"Name input prefab is unavailable: {NameInputPrefabPath}");
                }

                GameObject nameInputObject = PrefabUtility.InstantiatePrefab(
                    namePrefab,
                    root.transform) as GameObject;
                if (nameInputObject == null)
                {
                    throw new InvalidOperationException(
                        "Failed to instantiate robot name input sub view.");
                }

                nameInputObject.name = "RobotNameInput";
                nameInputObject.SetActive(false);
                Component nameInput = nameInputObject.GetComponent(
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        NameInputMono));

                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "backgroundImage",
                    background);
                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "closeButton",
                    close.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "cardRoot", cardRoot);
                BattlePreparationEditorUiFactory.SetObjects(
                    mono,
                    "productionCards",
                    cards);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "dynamicCardTemplate",
                    template);
                BattlePreparationEditorUiFactory.SetObject(mono, "nameInput", nameInput);

                SaveAndDestroy(root, ProductionPrefabPath);
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

        private static Component BuildProductionCard(
            string name,
            Transform parent,
            Sprite preview,
            string previewName,
            bool active)
        {
            GameObject card = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            Image cardImage = BattlePreparationEditorUiFactory.AddImage(
                card,
                new Color(0.055f, 0.075f, 0.14f, 0.96f),
                null,
                true);
            Button cardButton = card.AddComponent<Button>();
            cardButton.targetGraphic = cardImage;
            Component view = Runtime(card, ProductionCardMono);

            Image robot = CreateImage(
                "RobotImage",
                card.transform,
                preview,
                Color.white,
                true,
                new Vector2(0.10f, 0.41f),
                new Vector2(0.90f, 0.94f),
                Vector2.zero,
                Vector2.zero);
            Text cardName = CreateText(
                "Name",
                card.transform,
                previewName,
                30,
                TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.34f),
                new Vector2(0.92f, 0.43f),
                Vector2.zero,
                Vector2.zero);
            Text description = CreateText(
                "Description",
                card.transform,
                string.Empty,
                19,
                TextAnchor.UpperLeft,
                new Vector2(0.08f, 0.23f),
                new Vector2(0.92f, 0.35f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            Text attributes = CreateText(
                "BaseAttributes",
                card.transform,
                string.Empty,
                20,
                TextAnchor.MiddleCenter,
                new Vector2(0.06f, 0.15f),
                new Vector2(0.94f, 0.24f),
                Vector2.zero,
                Vector2.zero);

            RectTransform skillRoot = BattlePreparationEditorUiFactory.NewRect(
                "InnateSkills",
                card.transform,
                new Vector2(0.08f, 0.075f),
                new Vector2(0.42f, 0.15f),
                Vector2.zero,
                Vector2.zero);
            HorizontalLayoutGroup skillLayout =
                skillRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            skillLayout.spacing = 5f;
            skillLayout.childControlWidth = false;
            skillLayout.childControlHeight = false;
            skillLayout.childForceExpandWidth = false;
            skillLayout.childForceExpandHeight = false;
            skillLayout.childAlignment = TextAnchor.MiddleLeft;
            Image skillTemplate = CreateImage(
                "SkillIconTemplate",
                skillRoot,
                null,
                Color.white,
                true,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero);
            skillTemplate.rectTransform.sizeDelta = new Vector2(36f, 36f);
            skillTemplate.gameObject.SetActive(false);
            Text skillText = CreateText(
                "SkillText",
                card.transform,
                string.Empty,
                18,
                TextAnchor.MiddleLeft,
                new Vector2(0.42f, 0.075f),
                new Vector2(0.93f, 0.15f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            Text selectText = CreateText(
                "SelectText",
                card.transform,
                "选择",
                22,
                TextAnchor.MiddleCenter,
                new Vector2(0.28f, 0.012f),
                new Vector2(0.72f, 0.07f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.AccentColor);

            BattlePreparationEditorUiFactory.SetObject(view, "selectButton", cardButton);
            BattlePreparationEditorUiFactory.SetObject(view, "robotImage", robot);
            BattlePreparationEditorUiFactory.SetObject(view, "nameText", cardName);
            BattlePreparationEditorUiFactory.SetObject(view, "descriptionText", description);
            BattlePreparationEditorUiFactory.SetObject(view, "baseAttributeText", attributes);
            BattlePreparationEditorUiFactory.SetObject(view, "innateSkillRoot", skillRoot);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "innateSkillIconTemplate",
                skillTemplate);
            BattlePreparationEditorUiFactory.SetObject(view, "innateSkillText", skillText);
            BattlePreparationEditorUiFactory.SetObject(view, "selectButtonText", selectText);
            card.SetActive(active);
            return view;
        }

        private static void BuildNameInputPrefab()
        {
            GameObject root = CreateWindowRoot(
                "sub_robot_name_input",
                new Color(0f, 0f, 0f, 0.72f),
                true);
            try
            {
                Component mono = Runtime(root, NameInputMono);
                GameObject panel = CreatePanel(
                    "Dialog",
                    root.transform,
                    new Vector2(0.32f, 0.31f),
                    new Vector2(0.68f, 0.69f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Text title = CreateText(
                    "Title",
                    panel.transform,
                    "命名机器人",
                    32,
                    TextAnchor.MiddleCenter,
                    new Vector2(0.08f, 0.75f),
                    new Vector2(0.92f, 0.94f),
                    Vector2.zero,
                    Vector2.zero);

                GameObject inputObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "NameInput",
                    panel.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    inputObject.GetComponent<RectTransform>(),
                    new Vector2(0.10f, 0.47f),
                    new Vector2(0.90f, 0.68f),
                    Vector2.zero,
                    Vector2.zero);
                Image inputBackground = BattlePreparationEditorUiFactory.AddImage(
                    inputObject,
                    new Color(0.035f, 0.05f, 0.075f, 1f),
                    null,
                    true);
                InputField input = inputObject.AddComponent<InputField>();
                input.targetGraphic = inputBackground;
                input.lineType = InputField.LineType.SingleLine;

                Text inputText = CreateText(
                    "Text",
                    inputObject.transform,
                    string.Empty,
                    24,
                    TextAnchor.MiddleLeft,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(16f, 3f),
                    new Vector2(-16f, -3f));
                Text placeholder = CreateText(
                    "Placeholder",
                    inputObject.transform,
                    "输入机器人名称",
                    24,
                    TextAnchor.MiddleLeft,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(16f, 3f),
                    new Vector2(-16f, -3f),
                    new Color(0.55f, 0.62f, 0.72f, 0.75f));
                input.textComponent = inputText;
                input.placeholder = placeholder;

                Text validation = CreateText(
                    "Validation",
                    panel.transform,
                    string.Empty,
                    18,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.10f, 0.35f),
                    new Vector2(0.90f, 0.46f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.WarningColor);
                validation.gameObject.SetActive(false);

                BattlePreparationEditorUiFactory.ButtonParts confirm =
                    BattlePreparationEditorUiFactory.AddButton(
                        "ConfirmButton",
                        panel.transform,
                        "确定",
                        BattlePreparationEditorUiFactory.AccentMutedColor,
                        22);
                BattlePreparationEditorUiFactory.SetRect(
                    confirm.Rect,
                    new Vector2(0.54f, 0.12f),
                    new Vector2(0.88f, 0.30f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts cancel =
                    BattlePreparationEditorUiFactory.AddButton(
                        "CancelButton",
                        panel.transform,
                        "取消",
                        BattlePreparationEditorUiFactory.PanelLightColor,
                        22);
                BattlePreparationEditorUiFactory.SetRect(
                    cancel.Rect,
                    new Vector2(0.12f, 0.12f),
                    new Vector2(0.46f, 0.30f),
                    Vector2.zero,
                    Vector2.zero);

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(mono, "nameInput", input);
                BattlePreparationEditorUiFactory.SetObject(mono, "placeholderText", placeholder);
                BattlePreparationEditorUiFactory.SetObject(mono, "validationText", validation);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "confirmButton",
                    confirm.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "confirmButtonText",
                    confirm.Text);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "cancelButton",
                    cancel.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "cancelButtonText",
                    cancel.Text);

                root.SetActive(false);
                SaveAndDestroy(root, NameInputPrefabPath);
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

        private static void BuildRobotDetailPrefabV5(IReadOnlyDictionary<int, Sprite> sprites)
        {
            GameObject root = CreateWindowRoot(
                "win_battle_robot_detail",
                BattlePreparationEditorUiFactory.OverlayColor,
                true);
            try
            {
                Component mono = Runtime(root, RobotDetailMono);

                Text title = CreateText(
                    "Title",
                    root.transform,
                    "机器人详情",
                    34,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.025f, 0.92f),
                    new Vector2(0.55f, 0.985f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts close =
                    BattlePreparationEditorUiFactory.AddButton(
                        "CloseButton",
                        root.transform,
                        "关闭",
                        BattlePreparationEditorUiFactory.WarningColor,
                        22);
                BattlePreparationEditorUiFactory.Place(
                    close.Rect,
                    new Vector2(0.955f, 0.955f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(125f, 46f));

                GameObject left = CreatePanel(
                    "RobotAndEquipment",
                    root.transform,
                    new Vector2(0.015f, 0.025f),
                    new Vector2(0.365f, 0.91f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Image robotImage = CreateImage(
                    "RobotImage",
                    left.transform,
                    SpriteAt(sprites, 4004),
                    Color.white,
                    true,
                    new Vector2(0.32f, 0.68f),
                    new Vector2(0.68f, 0.96f),
                    Vector2.zero,
                    Vector2.zero);
                Text robotName = CreateText(
                    "RobotName",
                    left.transform,
                    string.Empty,
                    28,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.04f, 0.61f),
                    new Vector2(0.72f, 0.68f),
                    Vector2.zero,
                    Vector2.zero);
                Text robotState = CreateText(
                    "RobotState",
                    left.transform,
                    string.Empty,
                    22,
                    TextAnchor.MiddleRight,
                    new Vector2(0.72f, 0.61f),
                    new Vector2(0.96f, 0.68f),
                    Vector2.zero,
                    Vector2.zero);
                Text attributesTitle = CreateText(
                    "AttributesTitle",
                    left.transform,
                    "属性",
                    24,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.04f, 0.54f),
                    new Vector2(0.30f, 0.61f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);
                Text attributes = CreateText(
                    "Attributes",
                    left.transform,
                    string.Empty,
                    18,
                    TextAnchor.UpperLeft,
                    new Vector2(0.28f, 0.50f),
                    new Vector2(0.96f, 0.61f),
                    Vector2.zero,
                    Vector2.zero);
                Text equipmentTitle = CreateText(
                    "EquipmentTitle",
                    left.transform,
                    "装备",
                    24,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.04f, 0.45f),
                    new Vector2(0.30f, 0.51f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);

                RectTransform equipmentCanvas = BattlePreparationEditorUiFactory.NewRect(
                    "EquipmentSlots",
                    left.transform,
                    new Vector2(0.03f, 0.05f),
                    new Vector2(0.97f, 0.46f),
                    Vector2.zero,
                    Vector2.zero);
                int[] positions = { 1, 2, 3, 4, 5, 6, 7, 8 };
                Vector2[] anchors =
                {
                    new Vector2(0.50f, 0.78f),
                    new Vector2(0.50f, 0.48f),
                    new Vector2(0.20f, 0.52f),
                    new Vector2(0.80f, 0.52f),
                    new Vector2(0.34f, 0.17f),
                    new Vector2(0.66f, 0.17f),
                    new Vector2(0.16f, 0.85f),
                    new Vector2(0.84f, 0.85f),
                };
                string[] labels =
                {
                    "头部", "上身", "左手", "右手",
                    "左腿", "右腿", "背包", "胸挂",
                };
                List<UnityEngine.Object> equipmentSlots =
                    new List<UnityEngine.Object>(positions.Length);
                for (int index = 0; index < positions.Length; index++)
                {
                    Component slot = BuildEquipmentSlot(
                        $"Equipment_{positions[index]}",
                        equipmentCanvas,
                        positions[index],
                        labels[index],
                        true);
                    BattlePreparationEditorUiFactory.Place(
                        slot.GetComponent<RectTransform>(),
                        anchors[index],
                        new Vector2(0.5f, 0.5f),
                        Vector2.zero,
                        new Vector2(84f, 96f));
                    equipmentSlots.Add(slot);
                }

                RectTransform extensionRoot = BattlePreparationEditorUiFactory.NewRect(
                    "ExtensionEquipment",
                    equipmentCanvas,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0.18f),
                    Vector2.zero,
                    Vector2.zero);
                GridLayoutGroup extensionGrid =
                    extensionRoot.gameObject.AddComponent<GridLayoutGroup>();
                extensionGrid.cellSize = new Vector2(84f, 96f);
                extensionGrid.spacing = new Vector2(6f, 4f);
                extensionGrid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                extensionGrid.constraintCount = 1;
                extensionGrid.childAlignment = TextAnchor.MiddleCenter;
                Component extensionTemplate = BuildEquipmentSlot(
                    "ExtensionEquipmentTemplate",
                    extensionRoot,
                    0,
                    string.Empty,
                    false);
                extensionTemplate.gameObject.SetActive(false);

                GameObject comparison = CreatePanel(
                    "EquipmentComparison",
                    root.transform,
                    new Vector2(0.22f, 0.38f),
                    new Vector2(0.50f, 0.62f),
                    Vector2.zero,
                    Vector2.zero,
                    new Color(0.03f, 0.065f, 0.09f, 0.98f));
                CanvasGroup comparisonGroup = comparison.AddComponent<CanvasGroup>();
                comparisonGroup.interactable = false;
                comparisonGroup.blocksRaycasts = false;
                Text comparisonTitle = CreateText(
                    "Title",
                    comparison.transform,
                    "装备后属性对比",
                    22,
                    TextAnchor.MiddleCenter,
                    new Vector2(0.05f, 0.72f),
                    new Vector2(0.95f, 0.96f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);
                Text comparisonText = CreateText(
                    "Values",
                    comparison.transform,
                    string.Empty,
                    19,
                    TextAnchor.UpperLeft,
                    new Vector2(0.08f, 0.08f),
                    new Vector2(0.92f, 0.72f),
                    Vector2.zero,
                    Vector2.zero);
                comparison.SetActive(false);

                GameObject center = CreatePanel(
                    "RobotContainers",
                    root.transform,
                    new Vector2(0.375f, 0.025f),
                    new Vector2(0.685f, 0.91f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Component backpack = BuildContainerGrid(
                    "Backpack",
                    center.transform,
                    new Vector2(0.03f, 0.68f),
                    new Vector2(0.97f, 0.98f),
                    "背包",
                    5);
                Component chestRig = BuildContainerGrid(
                    "ChestRig",
                    center.transform,
                    new Vector2(0.03f, 0.36f),
                    new Vector2(0.97f, 0.66f),
                    "胸挂",
                    5);
                Component insurance = BuildContainerGrid(
                    "InsuranceBox",
                    center.transform,
                    new Vector2(0.03f, 0.03f),
                    new Vector2(0.97f, 0.34f),
                    "保险箱",
                    5);

                GameObject right = CreatePanel(
                    "WarehousePanel",
                    root.transform,
                    new Vector2(0.695f, 0.025f),
                    new Vector2(0.985f, 0.91f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Component warehouse = BuildContainerGrid(
                    "Warehouse",
                    right.transform,
                    new Vector2(0.03f, 0.02f),
                    new Vector2(0.97f, 0.98f),
                    "仓库",
                    5);

                BattlePreparationEditorUiFactory.ButtonParts warehouseSort =
                    BattlePreparationEditorUiFactory.AddButton(
                        "WarehouseSort",
                        right.transform,
                        "整理",
                        BattlePreparationEditorUiFactory.AccentColor,
                        17);
                BattlePreparationEditorUiFactory.SetRect(
                    warehouseSort.Rect,
                    new Vector2(0.70f, 0.91f),
                    new Vector2(0.95f, 0.975f),
                    Vector2.zero,
                    Vector2.zero);

                RectTransform dragLayer = BattlePreparationEditorUiFactory.NewRect(
                    "DragLayer",
                    root.transform,
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero,
                    Vector2.zero);
                Image dragIcon = CreateImage(
                    "DragIcon",
                    dragLayer,
                    null,
                    Color.white,
                    true,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    Vector2.zero);
                dragIcon.rectTransform.sizeDelta = new Vector2(78f, 78f);
                dragIcon.raycastTarget = false;
                dragIcon.gameObject.SetActive(false);

                GameObject itemDetailPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    ItemDetailPrefabPath);
                if (itemDetailPrefab == null)
                {
                    throw new InvalidOperationException(
                        $"Item detail prefab is unavailable: {ItemDetailPrefabPath}");
                }

                GameObject itemDetailObject = PrefabUtility.InstantiatePrefab(
                    itemDetailPrefab,
                    root.transform) as GameObject;
                if (itemDetailObject == null)
                {
                    throw new InvalidOperationException(
                        "Failed to instantiate item detail sub view.");
                }

                itemDetailObject.name = "ItemDetail";
                itemDetailObject.SetActive(false);
                itemDetailObject.transform.SetAsLastSibling();
                comparison.transform.SetAsLastSibling();
                dragLayer.SetAsLastSibling();
                Component itemDetail = itemDetailObject.GetComponent(
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        ItemDetailMono));

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "closeButton",
                    close.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "robotImage", robotImage);
                BattlePreparationEditorUiFactory.SetObject(mono, "robotNameText", robotName);
                BattlePreparationEditorUiFactory.SetObject(mono, "robotStateText", robotState);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "attributesTitleText",
                    attributesTitle);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "attributesText",
                    attributes);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "equipmentTitleText",
                    equipmentTitle);
                BattlePreparationEditorUiFactory.SetObjects(
                    mono,
                    "equipmentSlotViews",
                    equipmentSlots);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "extensionEquipmentRoot",
                    extensionRoot);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "extensionEquipmentTemplate",
                    extensionTemplate);
                BattlePreparationEditorUiFactory.SetObject(mono, "backpackGrid", backpack);
                BattlePreparationEditorUiFactory.SetObject(mono, "chestRigGrid", chestRig);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "insuranceBoxGrid",
                    insurance);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseGrid", warehouse);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "warehouseSortButton",
                    warehouseSort.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "warehouseSortButtonText",
                    warehouseSort.Text);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "comparisonRoot",
                    comparison);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "comparisonTitleText",
                    comparisonTitle);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "comparisonText",
                    comparisonText);
                BattlePreparationEditorUiFactory.SetObject(mono, "dragLayer", dragLayer);
                BattlePreparationEditorUiFactory.SetObject(mono, "dragIcon", dragIcon);
                BattlePreparationEditorUiFactory.SetObject(mono, "itemDetail", itemDetail);

                SaveAndDestroy(root, RobotDetailPrefabPath);
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

        private static void BuildRobotDetailPrefab(IReadOnlyDictionary<int, Sprite> sprites)
        {
            GameObject root = CreateWindowRoot(
                "win_battle_robot_detail",
                BattlePreparationEditorUiFactory.OverlayColor,
                true);
            try
            {
                Component mono = Runtime(root, RobotDetailMono);
                Text title = CreateText(
                    "Title", root.transform, "Robot Details", 34,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.025f, 0.92f), new Vector2(0.55f, 0.985f),
                    Vector2.zero, Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts close =
                    BattlePreparationEditorUiFactory.AddButton(
                        "CloseButton", root.transform, "Close",
                        BattlePreparationEditorUiFactory.WarningColor, 22);
                BattlePreparationEditorUiFactory.Place(
                    close.Rect, new Vector2(0.955f, 0.955f),
                    new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(125f, 46f));
                BattlePreparationEditorUiFactory.ButtonParts enterBattle =
                    BattlePreparationEditorUiFactory.AddButton(
                        "EnterBattleButton", root.transform, "Enter Battle",
                        new Color(0.16f, 0.36f, 0.58f, 0.96f), 21);
                BattlePreparationEditorUiFactory.Place(
                    enterBattle.Rect, new Vector2(0.84f, 0.955f),
                    new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(190f, 46f));
                enterBattle.Button.interactable = false;

                GameObject left = CreatePanel(
                    "RobotAndEquipment", root.transform,
                    new Vector2(0.015f, 0.025f), new Vector2(0.365f, 0.91f),
                    Vector2.zero, Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Image robotImage = CreateImage(
                    "RobotImage", left.transform, SpriteAt(sprites, 4004),
                    Color.white, true,
                    new Vector2(0.34f, 0.75f), new Vector2(0.66f, 0.96f),
                    Vector2.zero, Vector2.zero);
                Text robotName = CreateText(
                    "RobotName", left.transform, string.Empty, 27,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.04f, 0.68f), new Vector2(0.72f, 0.75f),
                    Vector2.zero, Vector2.zero);
                Text robotState = CreateText(
                    "RobotState", left.transform, string.Empty, 21,
                    TextAnchor.MiddleRight,
                    new Vector2(0.72f, 0.68f), new Vector2(0.96f, 0.75f),
                    Vector2.zero, Vector2.zero);
                Text attributesTitle = CreateText(
                    "AttributesTitle", left.transform, "Attributes", 22,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.04f, 0.61f), new Vector2(0.29f, 0.68f),
                    Vector2.zero, Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);
                Text attributes = CreateText(
                    "Attributes", left.transform, string.Empty, 17,
                    TextAnchor.UpperLeft,
                    new Vector2(0.27f, 0.58f), new Vector2(0.96f, 0.68f),
                    Vector2.zero, Vector2.zero);
                Text equipmentTitle = CreateText(
                    "EquipmentTitle", left.transform, "Equipment", 22,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.04f, 0.525f), new Vector2(0.34f, 0.585f),
                    Vector2.zero, Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);

                RectTransform equipmentCanvas =
                    BattlePreparationEditorUiFactory.NewRect(
                        "EquipmentSlots", left.transform,
                        new Vector2(0.03f, 0.105f), new Vector2(0.97f, 0.535f),
                        Vector2.zero, Vector2.zero);
                int[] positions = { 9, 1, 2, 3, 4, 5, 6, 7, 8 };
                Vector2[] anchors =
                {
                    new Vector2(0.50f, 0.86f),
                    new Vector2(0.50f, 0.57f),
                    new Vector2(0.50f, 0.28f),
                    new Vector2(0.25f, 0.31f),
                    new Vector2(0.75f, 0.31f),
                    new Vector2(0.34f, 0.08f),
                    new Vector2(0.66f, 0.08f),
                    new Vector2(0.15f, 0.72f),
                    new Vector2(0.85f, 0.72f),
                };
                string[] labels =
                {
                    "Brain", "Head", "Upper Body", "Left Hand", "Right Hand",
                    "Left Leg", "Right Leg", "Backpack", "Chest Rig",
                };
                List<UnityEngine.Object> equipmentSlots =
                    new List<UnityEngine.Object>(positions.Length);
                for (int index = 0; index < positions.Length; index++)
                {
                    Component slot = BuildEquipmentSlot(
                        $"Equipment_{positions[index]}", equipmentCanvas,
                        positions[index], labels[index], true);
                    BattlePreparationEditorUiFactory.Place(
                        slot.GetComponent<RectTransform>(), anchors[index],
                        new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(82f, 116f));
                    equipmentSlots.Add(slot);
                }

                RectTransform extensionRoot =
                    BattlePreparationEditorUiFactory.NewRect(
                        "ExtensionEquipment", equipmentCanvas,
                        Vector2.zero, new Vector2(1f, 0.24f),
                        Vector2.zero, Vector2.zero);
                GridLayoutGroup extensionGrid =
                    extensionRoot.gameObject.AddComponent<GridLayoutGroup>();
                extensionGrid.cellSize = new Vector2(82f, 116f);
                extensionGrid.spacing = new Vector2(6f, 4f);
                extensionGrid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                extensionGrid.constraintCount = 1;
                extensionGrid.childAlignment = TextAnchor.MiddleCenter;
                Component extensionTemplate = BuildEquipmentSlot(
                    "ExtensionEquipmentTemplate", extensionRoot, 0, string.Empty, false);
                extensionTemplate.gameObject.SetActive(false);

                Text skillSlotsTitle = CreateText(
                    "SkillSlotsTitle", left.transform, "Skills", 20,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.04f, 0.065f), new Vector2(0.32f, 0.105f),
                    Vector2.zero, Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);
                RectTransform skillSlotsRoot =
                    BattlePreparationEditorUiFactory.NewRect(
                        "SkillSlots", left.transform,
                        new Vector2(0.04f, 0.008f), new Vector2(0.96f, 0.067f),
                        Vector2.zero, Vector2.zero);
                HorizontalLayoutGroup skillSlotsLayout =
                    skillSlotsRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
                skillSlotsLayout.spacing = 7f;
                skillSlotsLayout.childAlignment = TextAnchor.MiddleCenter;
                skillSlotsLayout.childControlWidth = false;
                skillSlotsLayout.childControlHeight = false;
                skillSlotsLayout.childForceExpandWidth = false;
                skillSlotsLayout.childForceExpandHeight = false;
                string[] hotkeys = { "S", "D", "Q", "W", "E", "R" };
                List<UnityEngine.Object> skillSlots = new List<UnityEngine.Object>(6);
                for (int index = 0; index < hotkeys.Length; index++)
                {
                    Component skillSlot = BuildSkillSlot(
                        $"SkillSlot_{index + 1}", skillSlotsRoot,
                        index + 1, hotkeys[index]);
                    skillSlot.GetComponent<RectTransform>().sizeDelta =
                        new Vector2(50f, 54f);
                    skillSlots.Add(skillSlot);
                }

                GameObject center = CreatePanel(
                    "RobotContainers", root.transform,
                    new Vector2(0.375f, 0.025f), new Vector2(0.685f, 0.91f),
                    Vector2.zero, Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Component backpack = BuildContainerGrid(
                    "Backpack", center.transform,
                    new Vector2(0.03f, 0.68f), new Vector2(0.97f, 0.98f),
                    "Backpack", 5);
                Component chestRig = BuildContainerGrid(
                    "ChestRig", center.transform,
                    new Vector2(0.03f, 0.36f), new Vector2(0.97f, 0.66f),
                    "Chest Rig", 5);
                Component insurance = BuildContainerGrid(
                    "InsuranceBox", center.transform,
                    new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.34f),
                    "Insurance", 5);

                GameObject right = CreatePanel(
                    "WarehousePanel", root.transform,
                    new Vector2(0.695f, 0.025f), new Vector2(0.985f, 0.91f),
                    Vector2.zero, Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Component warehouse = BuildContainerGrid(
                    "Warehouse", right.transform,
                    new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.98f),
                    "Warehouse", 5);
                BattlePreparationEditorUiFactory.ButtonParts warehouseSort =
                    BattlePreparationEditorUiFactory.AddButton(
                        "WarehouseSort", right.transform, "Sort",
                        BattlePreparationEditorUiFactory.AccentColor, 17);
                BattlePreparationEditorUiFactory.SetRect(
                    warehouseSort.Rect,
                    new Vector2(0.70f, 0.91f), new Vector2(0.95f, 0.975f),
                    Vector2.zero, Vector2.zero);

                GameObject comparison = CreatePanel(
                    "EquipmentComparison", root.transform,
                    new Vector2(0.18f, 0.14f), new Vector2(0.82f, 0.83f),
                    Vector2.zero, Vector2.zero,
                    new Color(0.03f, 0.065f, 0.09f, 0.985f));
                CanvasGroup comparisonGroup = comparison.AddComponent<CanvasGroup>();
                comparisonGroup.interactable = false;
                comparisonGroup.blocksRaycasts = false;
                Text comparisonTitle = CreateText(
                    "Title", comparison.transform, "Equipment Comparison", 24,
                    TextAnchor.MiddleCenter,
                    new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.98f),
                    Vector2.zero, Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);
                Component comparisonCurrentSide = BuildComparisonSide(
                    "CurrentSide",
                    comparison.transform,
                    new Vector2(0.025f, 0.035f),
                    new Vector2(0.493f, 0.90f));
                Component comparisonCandidateSide = BuildComparisonSide(
                    "CandidateSide",
                    comparison.transform,
                    new Vector2(0.507f, 0.035f),
                    new Vector2(0.975f, 0.90f));
                comparison.SetActive(false);

                GameObject itemDetailPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    ItemDetailPrefabPath);
                if (itemDetailPrefab == null)
                {
                    throw new InvalidOperationException(
                        $"Item detail prefab is unavailable: {ItemDetailPrefabPath}");
                }

                GameObject itemDetailObject = PrefabUtility.InstantiatePrefab(
                    itemDetailPrefab, root.transform) as GameObject;
                if (itemDetailObject == null)
                {
                    throw new InvalidOperationException(
                        "Failed to instantiate item detail sub view.");
                }

                itemDetailObject.name = "ItemDetail";
                itemDetailObject.SetActive(false);
                Component itemDetail = itemDetailObject.GetComponent(
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        ItemDetailMono));
                Component skillDetail = BuildSkillDetailView(root.transform);

                RectTransform dragLayer = BattlePreparationEditorUiFactory.NewRect(
                    "DragLayer", root.transform, Vector2.zero, Vector2.one,
                    Vector2.zero, Vector2.zero);
                Image dragIcon = CreateImage(
                    "DragIcon", dragLayer, null, Color.white, true,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero);
                dragIcon.rectTransform.sizeDelta = new Vector2(78f, 78f);
                dragIcon.raycastTarget = false;
                dragIcon.gameObject.SetActive(false);

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(mono, "closeButton", close.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "enterBattleButton", enterBattle.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "enterBattleButtonText", enterBattle.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "robotImage", robotImage);
                BattlePreparationEditorUiFactory.SetObject(mono, "robotNameText", robotName);
                BattlePreparationEditorUiFactory.SetObject(mono, "robotStateText", robotState);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "attributesTitleText", attributesTitle);
                BattlePreparationEditorUiFactory.SetObject(mono, "attributesText", attributes);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "equipmentTitleText", equipmentTitle);
                BattlePreparationEditorUiFactory.SetObjects(
                    mono, "equipmentSlotViews", equipmentSlots);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "extensionEquipmentRoot", extensionRoot);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "extensionEquipmentTemplate", extensionTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "skillSlotsTitleText", skillSlotsTitle);
                BattlePreparationEditorUiFactory.SetObjects(
                    mono, "skillSlotViews", skillSlots);
                BattlePreparationEditorUiFactory.SetObject(mono, "backpackGrid", backpack);
                BattlePreparationEditorUiFactory.SetObject(mono, "chestRigGrid", chestRig);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "insuranceBoxGrid", insurance);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseGrid", warehouse);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "warehouseSortButton", warehouseSort.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "warehouseSortButtonText", warehouseSort.Text);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "comparisonRoot", comparison);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "comparisonTitleText", comparisonTitle);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "comparisonCurrentSideView", comparisonCurrentSide);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "comparisonCandidateSideView", comparisonCandidateSide);
                BattlePreparationEditorUiFactory.SetObject(mono, "dragLayer", dragLayer);
                BattlePreparationEditorUiFactory.SetObject(mono, "dragIcon", dragIcon);
                BattlePreparationEditorUiFactory.SetObject(mono, "itemDetail", itemDetail);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "skillDetailView", skillDetail);
                BattlePreparationEditorUiFactory.AddBuilderMarker(
                    root,
                    BattleDevelopmentEntryMarker);

                // Builder markers are hidden implementation details. Keep the four
                // runtime overlays above every marker and in their real render order.
                itemDetailObject.transform.SetAsLastSibling();
                skillDetail.transform.SetAsLastSibling();
                comparison.transform.SetAsLastSibling();
                dragLayer.SetAsLastSibling();

                SaveAndDestroy(root, RobotDetailPrefabPath);
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

        private static Component BuildEquipmentSlot(
            string name,
            Transform parent,
            int positionType,
            string label,
            bool active)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(0.06f, 0.09f, 0.13f, 0.96f),
                null,
                false);
            Component view = Runtime(root, EquipmentSlotMono);
            Text positionName = CreateText(
                "PositionName",
                root.transform,
                label,
                14,
                TextAnchor.MiddleCenter,
                new Vector2(0f, 0.82f),
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            Component itemCell = BuildItemCell(
                "ItemCell",
                root.transform,
                new Vector2(0.08f, 0.28f),
                new Vector2(0.92f, 0.82f),
                true);
            Component providedSkillStrip = BuildEquipmentSkillStrip(
                "ProvidedSkillStrip",
                root.transform,
                new Vector2(0.06f, 0.04f),
                new Vector2(0.94f, 0.27f));
            GameObject unavailable = CreatePanel(
                "Unavailable",
                root.transform,
                new Vector2(0.08f, 0.04f),
                new Vector2(0.92f, 0.82f),
                Vector2.zero,
                Vector2.zero,
                new Color(0f, 0f, 0f, 0.72f));
            CreateText(
                "Text",
                unavailable.transform,
                "—",
                28,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            unavailable.SetActive(false);

            BattlePreparationEditorUiFactory.SetInts(
                view,
                "acceptedPositionTypes",
                positionType > 0 ? new[] { positionType } : Array.Empty<int>());
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "positionNameText",
                positionName);
            BattlePreparationEditorUiFactory.SetObject(view, "itemCell", itemCell);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "providedSkillStrip",
                providedSkillStrip);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "unavailableRoot",
                unavailable);
            root.SetActive(active);
            return view;
        }

        private static Component BuildEquipmentSkillStrip(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.SetRect(
                root.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
            BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(1f, 1f, 1f, 0.001f),
                null,
                true);
            Component view = Runtime(root, EquipmentSkillStripMono);
            HorizontalLayoutGroup layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            List<UnityEngine.Object> skillViews = new List<UnityEngine.Object>(3);
            for (int index = 0; index < 3; index++)
            {
                Component skill = BuildSkillIcon($"SkillIcon_{index + 1}", root.transform);
                skill.GetComponent<RectTransform>().sizeDelta = new Vector2(22f, 22f);
                skillViews.Add(skill);
            }

            BattlePreparationEditorUiFactory.SetObjects(
                view,
                "skillViews",
                skillViews);
            root.SetActive(false);
            return view;
        }

        private static Component BuildSkillIcon(string name, Transform parent)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            Image background = BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(0.09f, 0.15f, 0.22f, 0.98f),
                null,
                true);
            Component view = Runtime(root, SkillIconMono);
            Image icon = CreateImage(
                "Icon",
                root.transform,
                null,
                Color.white,
                true,
                new Vector2(0.08f, 0.08f),
                new Vector2(0.92f, 0.92f),
                Vector2.zero,
                Vector2.zero);
            icon.raycastTarget = false;
            Image highlight = CreateImage(
                "DragHighlight",
                root.transform,
                null,
                new Color(0.24f, 0.90f, 1f, 0.62f),
                false,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            highlight.raycastTarget = false;
            highlight.gameObject.SetActive(false);

            BattlePreparationEditorUiFactory.SetObject(
                view,
                "backgroundImage",
                background);
            BattlePreparationEditorUiFactory.SetObject(view, "iconImage", icon);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "dragHighlightImage",
                highlight);
            return view;
        }

        private static Component BuildSkillSlot(
            string name,
            Transform parent,
            int slotId,
            string hotkey)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            Image background = BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(0.055f, 0.10f, 0.16f, 0.98f),
                null,
                true);
            Component view = Runtime(root, SkillSlotMono);
            Component icon = BuildSkillIcon("SkillIcon", root.transform);
            BattlePreparationEditorUiFactory.SetRect(
                icon.GetComponent<RectTransform>(),
                new Vector2(0.08f, 0.10f),
                new Vector2(0.92f, 0.88f),
                Vector2.zero,
                Vector2.zero);
            icon.gameObject.SetActive(false);

            Text empty = CreateText(
                "Empty",
                root.transform,
                "+",
                24,
                TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.10f),
                new Vector2(0.92f, 0.88f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            empty.raycastTarget = false;
            Text hotkeyText = CreateText(
                "Hotkey",
                root.transform,
                hotkey,
                13,
                TextAnchor.UpperLeft,
                new Vector2(0.04f, 0.68f),
                new Vector2(0.42f, 0.98f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.AccentColor);
            hotkeyText.raycastTarget = false;
            Image highlight = CreateImage(
                "DropHighlight",
                root.transform,
                null,
                new Color(0.22f, 0.92f, 0.42f, 0.72f),
                false,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            highlight.raycastTarget = false;
            highlight.gameObject.SetActive(false);
            GameObject readOnly = BattlePreparationEditorUiFactory.NewUiObject(
                "ReadOnly",
                root.transform);
            BattlePreparationEditorUiFactory.Stretch(
                readOnly.GetComponent<RectTransform>());
            BattlePreparationEditorUiFactory.AddImage(
                readOnly,
                new Color(0f, 0f, 0f, 0.42f),
                null,
                false);
            Text readOnlyText = CreateText(
                "Text",
                readOnly.transform,
                "x",
                15,
                TextAnchor.LowerRight,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                new Vector2(-3f, 2f),
                BattlePreparationEditorUiFactory.WarningColor);
            readOnlyText.raycastTarget = false;
            readOnly.SetActive(false);

            BattlePreparationEditorUiFactory.SetInt(view, "slotId", slotId);
            BattlePreparationEditorUiFactory.SetObject(view, "hotkeyText", hotkeyText);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "backgroundImage",
                background);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "dropHighlightImage",
                highlight);
            BattlePreparationEditorUiFactory.SetObject(view, "iconView", icon);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "emptyRoot",
                empty.gameObject);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "readOnlyRoot",
                readOnly);
            root.SetActive(false);
            return view;
        }

        private static Component BuildSkillList(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            float entryHeight)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.SetRect(
                root.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
            Component view = Runtime(root, SkillListMono);
            RectTransform entries = BattlePreparationEditorUiFactory.NewRect(
                "Entries",
                root.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            VerticalLayoutGroup layout = entries.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            Component template = BuildSkillListEntry(
                "SkillEntryTemplate",
                entries,
                entryHeight);
            template.gameObject.SetActive(false);

            BattlePreparationEditorUiFactory.SetObject(view, "entriesRoot", entries);
            BattlePreparationEditorUiFactory.SetObject(view, "entryTemplate", template);
            return view;
        }

        private static Component BuildSkillListEntry(
            string name,
            Transform parent,
            float height)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            LayoutElement layoutElement = root.AddComponent<LayoutElement>();
            layoutElement.minHeight = height;
            layoutElement.preferredHeight = height;
            BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(0.045f, 0.075f, 0.11f, 0.92f),
                null,
                false);
            Component view = Runtime(root, SkillListEntryMono);
            Image icon = CreateImage(
                "Icon",
                root.transform,
                null,
                Color.white,
                true,
                new Vector2(0.02f, 0.24f),
                new Vector2(0.18f, 0.88f),
                Vector2.zero,
                Vector2.zero);
            icon.raycastTarget = false;
            Text skillName = CreateText(
                "Name",
                root.transform,
                string.Empty,
                18,
                TextAnchor.MiddleLeft,
                new Vector2(0.21f, 0.68f),
                new Vector2(0.98f, 0.96f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.AccentColor);
            Text cooldown = CreateText(
                "Cooldown",
                root.transform,
                string.Empty,
                15,
                TextAnchor.MiddleLeft,
                new Vector2(0.21f, 0.45f),
                new Vector2(0.98f, 0.69f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            Text description = CreateText(
                "Description",
                root.transform,
                string.Empty,
                14,
                TextAnchor.UpperLeft,
                new Vector2(0.21f, 0.04f),
                new Vector2(0.98f, 0.45f),
                Vector2.zero,
                Vector2.zero);
            skillName.raycastTarget = false;
            cooldown.raycastTarget = false;
            description.raycastTarget = false;

            BattlePreparationEditorUiFactory.SetObject(view, "iconImage", icon);
            BattlePreparationEditorUiFactory.SetObject(view, "nameText", skillName);
            BattlePreparationEditorUiFactory.SetObject(view, "cooldownText", cooldown);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "descriptionText",
                description);
            return view;
        }

        private static Component BuildEquipmentEffectList(
            string name,
            Transform parent,
            float entryHeight)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(0.035f, 0.065f, 0.095f, 0.86f),
                null,
                false);
            LayoutElement sectionLayout = root.AddComponent<LayoutElement>();
            sectionLayout.minHeight = 46f + entryHeight;
            sectionLayout.preferredHeight = 46f + entryHeight * 2f;
            Component view = Runtime(root, EquipmentEffectListMono);
            Text title = CreateText(
                "Title",
                root.transform,
                "Direct Effects",
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0f, 0.80f),
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f),
                BattlePreparationEditorUiFactory.AccentColor);
            title.raycastTarget = false;
            RectTransform entries = BattlePreparationEditorUiFactory.NewRect(
                "Entries",
                root.transform,
                Vector2.zero,
                new Vector2(1f, 0.80f),
                new Vector2(8f, 6f),
                new Vector2(-8f, -2f));
            VerticalLayoutGroup entriesLayout =
                entries.gameObject.AddComponent<VerticalLayoutGroup>();
            entriesLayout.spacing = 5f;
            entriesLayout.childAlignment = TextAnchor.UpperLeft;
            entriesLayout.childControlWidth = true;
            entriesLayout.childControlHeight = false;
            entriesLayout.childForceExpandWidth = true;
            entriesLayout.childForceExpandHeight = false;
            Text template = CreateLayoutText(
                "EffectEntryTemplate",
                entries,
                16,
                entryHeight,
                BattlePreparationEditorUiFactory.TextColor);
            template.supportRichText = true;
            template.gameObject.SetActive(false);

            BattlePreparationEditorUiFactory.SetObject(view, "sectionRoot", root);
            BattlePreparationEditorUiFactory.SetObject(view, "titleText", title);
            BattlePreparationEditorUiFactory.SetObject(view, "entriesRoot", entries);
            BattlePreparationEditorUiFactory.SetObject(view, "entryTemplate", template);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "sectionLayout",
                sectionLayout);
            return view;
        }

        private static Component BuildMajorAffixView(
            string name,
            Transform parent,
            float stageHeight)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(0.025f, 0.08f, 0.095f, 0.90f),
                null,
                false);
            LayoutElement sectionLayout = root.AddComponent<LayoutElement>();
            sectionLayout.minHeight = 112f + stageHeight;
            sectionLayout.preferredHeight = 112f + stageHeight * 2f;
            Component view = Runtime(root, MajorAffixMono);
            Text title = CreateText(
                "Title",
                root.transform,
                "Major Affix",
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0f, 0.89f),
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f),
                new Color(0.19f, 0.92f, 0.94f, 1f));
            title.raycastTarget = false;
            GameObject header = BattlePreparationEditorUiFactory.NewUiObject(
                "Header",
                root.transform);
            BattlePreparationEditorUiFactory.SetRect(
                header.GetComponent<RectTransform>(),
                new Vector2(0f, 0.63f),
                new Vector2(1f, 0.89f),
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f));
            Text affixName = CreateText(
                "Name",
                header.transform,
                string.Empty,
                19,
                TextAnchor.MiddleLeft,
                new Vector2(0f, 0.62f),
                new Vector2(0.70f, 1f),
                Vector2.zero,
                Vector2.zero,
                new Color(0.19f, 0.92f, 0.94f, 1f));
            Text equippedCount = CreateText(
                "EquippedCount",
                header.transform,
                string.Empty,
                15,
                TextAnchor.MiddleRight,
                new Vector2(0.70f, 0.62f),
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            Text description = CreateText(
                "Description",
                header.transform,
                string.Empty,
                15,
                TextAnchor.UpperLeft,
                Vector2.zero,
                new Vector2(1f, 0.62f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            GameObject stagesSection = BattlePreparationEditorUiFactory.NewUiObject(
                "Stages",
                root.transform);
            BattlePreparationEditorUiFactory.SetRect(
                stagesSection.GetComponent<RectTransform>(),
                Vector2.zero,
                new Vector2(1f, 0.63f),
                new Vector2(8f, 6f),
                new Vector2(-8f, -2f));
            RectTransform stages = BattlePreparationEditorUiFactory.NewRect(
                "Entries",
                stagesSection.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            VerticalLayoutGroup stagesLayout =
                stages.gameObject.AddComponent<VerticalLayoutGroup>();
            stagesLayout.spacing = 5f;
            stagesLayout.childAlignment = TextAnchor.UpperLeft;
            stagesLayout.childControlWidth = true;
            stagesLayout.childControlHeight = false;
            stagesLayout.childForceExpandWidth = true;
            stagesLayout.childForceExpandHeight = false;
            Text stageTemplate = CreateLayoutText(
                "StageTemplate",
                stages,
                15,
                stageHeight,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            stageTemplate.supportRichText = true;
            stageTemplate.gameObject.SetActive(false);

            BattlePreparationEditorUiFactory.SetObject(view, "sectionRoot", root);
            BattlePreparationEditorUiFactory.SetObject(view, "titleText", title);
            BattlePreparationEditorUiFactory.SetObject(view, "headerRoot", header);
            BattlePreparationEditorUiFactory.SetObject(view, "nameText", affixName);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "descriptionText",
                description);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "equippedCountText",
                equippedCount);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "stagesSectionRoot",
                stagesSection);
            BattlePreparationEditorUiFactory.SetObject(view, "stagesRoot", stages);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "stageTemplate",
                stageTemplate);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "sectionLayout",
                sectionLayout);
            return view;
        }

        private static Component BuildComparisonSide(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.SetRect(
                root.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
            BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(0.025f, 0.05f, 0.075f, 0.94f),
                null,
                false);
            Component view = Runtime(root, ComparisonSideMono);
            BattlePreparationEditorUiFactory.ScrollParts scroll =
                BattlePreparationEditorUiFactory.AddVerticalScroll(
                    "Scroll",
                    root.transform,
                    7f,
                    new Vector4(7f, 7f, 7f, 7f),
                    false,
                    Vector2.zero,
                    1);
            BattlePreparationEditorUiFactory.SetRect(
                scroll.ScrollRect.GetComponent<RectTransform>(),
                Vector2.zero,
                Vector2.one,
                new Vector2(3f, 3f),
                new Vector2(-3f, -3f));

            Text sideTitle = CreateLayoutText(
                "SideTitle",
                scroll.Content,
                21,
                42f,
                BattlePreparationEditorUiFactory.AccentColor);
            sideTitle.alignment = TextAnchor.MiddleCenter;

            GameObject itemHeader = BattlePreparationEditorUiFactory.NewUiObject(
                "ItemHeader",
                scroll.Content);
            BattlePreparationEditorUiFactory.AddImage(
                itemHeader,
                new Color(0.055f, 0.09f, 0.125f, 0.94f),
                null,
                false);
            LayoutElement itemHeaderLayout = itemHeader.AddComponent<LayoutElement>();
            itemHeaderLayout.minHeight = 76f;
            itemHeaderLayout.preferredHeight = 76f;
            Text itemName = CreateText(
                "ItemName",
                itemHeader.transform,
                string.Empty,
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0.03f, 0.42f),
                new Vector2(0.97f, 0.96f),
                Vector2.zero,
                Vector2.zero);
            Text quality = CreateText(
                "Quality",
                itemHeader.transform,
                string.Empty,
                16,
                TextAnchor.MiddleLeft,
                new Vector2(0.03f, 0.04f),
                new Vector2(0.97f, 0.43f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);

            GameObject emptyItem = BattlePreparationEditorUiFactory.NewUiObject(
                "EmptyItem",
                scroll.Content);
            LayoutElement emptyLayout = emptyItem.AddComponent<LayoutElement>();
            emptyLayout.minHeight = 64f;
            emptyLayout.preferredHeight = 64f;
            Text emptyText = BattlePreparationEditorUiFactory.AddTextChild(
                "Text",
                emptyItem.transform,
                "Empty equipment slot",
                18,
                TextAnchor.MiddleCenter,
                BattlePreparationEditorUiFactory.SubtleTextColor,
                6f);
            emptyItem.SetActive(false);

            GameObject attributes = BattlePreparationEditorUiFactory.NewUiObject(
                "FinalAttributes",
                scroll.Content);
            BattlePreparationEditorUiFactory.AddImage(
                attributes,
                new Color(0.04f, 0.07f, 0.10f, 0.90f),
                null,
                false);
            LayoutElement attributeLayout = attributes.AddComponent<LayoutElement>();
            attributeLayout.minHeight = 120f;
            attributeLayout.preferredHeight = 300f;
            Text attributesTitle = CreateText(
                "Title",
                attributes.transform,
                "Final Attributes",
                18,
                TextAnchor.MiddleLeft,
                new Vector2(0f, 0.84f),
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f),
                BattlePreparationEditorUiFactory.AccentColor);
            Text attributesText = CreateText(
                "Values",
                attributes.transform,
                string.Empty,
                16,
                TextAnchor.UpperLeft,
                Vector2.zero,
                new Vector2(1f, 0.84f),
                new Vector2(8f, 5f),
                new Vector2(-8f, -2f));
            attributesText.supportRichText = true;

            GameObject capacity = BattlePreparationEditorUiFactory.NewUiObject(
                "CapacityAndBrainSlots",
                scroll.Content);
            BattlePreparationEditorUiFactory.AddImage(
                capacity,
                new Color(0.04f, 0.07f, 0.10f, 0.90f),
                null,
                false);
            LayoutElement capacityLayout = capacity.AddComponent<LayoutElement>();
            capacityLayout.minHeight = 96f;
            capacityLayout.preferredHeight = 96f;
            Text capacityTitle = CreateText(
                "Title",
                capacity.transform,
                "Capacity / Brain Slots",
                18,
                TextAnchor.MiddleLeft,
                new Vector2(0f, 0.60f),
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f),
                BattlePreparationEditorUiFactory.AccentColor);
            Text capacityText = CreateText(
                "Values",
                capacity.transform,
                string.Empty,
                16,
                TextAnchor.UpperLeft,
                Vector2.zero,
                new Vector2(1f, 0.60f),
                new Vector2(8f, 4f),
                new Vector2(-8f, 0f));
            capacity.SetActive(false);

            GameObject skills = BattlePreparationEditorUiFactory.NewUiObject(
                "ProvidedSkills",
                scroll.Content);
            LayoutElement skillsLayout = skills.AddComponent<LayoutElement>();
            skillsLayout.minHeight = 134f;
            skillsLayout.preferredHeight = 310f;
            Text skillsTitle = CreateText(
                "Title",
                skills.transform,
                "Provided Skills",
                18,
                TextAnchor.MiddleLeft,
                new Vector2(0f, 0.86f),
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f),
                BattlePreparationEditorUiFactory.AccentColor);
            Component skillList = BuildSkillList(
                "List",
                skills.transform,
                Vector2.zero,
                new Vector2(1f, 0.86f),
                82f);
            skills.SetActive(false);

            Component effects = BuildEquipmentEffectList(
                "DirectEffects",
                scroll.Content,
                82f);
            effects.gameObject.SetActive(false);
            Component majorAffix = BuildMajorAffixView(
                "MajorAffix",
                scroll.Content,
                116f);
            majorAffix.gameObject.SetActive(false);

            BattlePreparationEditorUiFactory.SetObject(
                view,
                "scrollRect",
                scroll.ScrollRect);
            BattlePreparationEditorUiFactory.SetObject(view, "sideTitleText", sideTitle);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "itemHeaderRoot",
                itemHeader);
            BattlePreparationEditorUiFactory.SetObject(view, "itemNameText", itemName);
            BattlePreparationEditorUiFactory.SetObject(view, "qualityText", quality);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "emptyItemRoot",
                emptyItem);
            BattlePreparationEditorUiFactory.SetObject(view, "emptyItemText", emptyText);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "attributesRoot",
                attributes);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "attributesTitleText",
                attributesTitle);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "attributesText",
                attributesText);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "capacityRoot",
                capacity);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "capacityTitleText",
                capacityTitle);
            BattlePreparationEditorUiFactory.SetObject(view, "capacityText", capacityText);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "providedSkillsRoot",
                skills);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "providedSkillsTitleText",
                skillsTitle);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "providedSkillsList",
                skillList);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "equipmentEffectList",
                effects);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "majorAffixView",
                majorAffix);
            return view;
        }

        private static Component BuildSkillDetailView(Transform parent)
        {
            GameObject root = CreatePanel(
                "SkillDetail",
                parent,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Color(0f, 0f, 0f, 0.78f));
            Component view = Runtime(root, SkillDetailMono);
            GameObject panel = CreatePanel(
                "Panel",
                root.transform,
                new Vector2(0.34f, 0.24f),
                new Vector2(0.66f, 0.76f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.PanelColor);
            Text title = CreateText(
                "Title",
                panel.transform,
                "Skill Details",
                28,
                TextAnchor.MiddleLeft,
                new Vector2(0.06f, 0.86f),
                new Vector2(0.68f, 0.97f),
                Vector2.zero,
                Vector2.zero);
            BattlePreparationEditorUiFactory.ButtonParts close =
                BattlePreparationEditorUiFactory.AddButton(
                    "CloseButton",
                    panel.transform,
                    "Close",
                    BattlePreparationEditorUiFactory.WarningColor,
                    18);
            BattlePreparationEditorUiFactory.SetRect(
                close.Rect,
                new Vector2(0.73f, 0.88f),
                new Vector2(0.94f, 0.96f),
                Vector2.zero,
                Vector2.zero);
            Image icon = CreateImage(
                "Icon",
                panel.transform,
                null,
                Color.white,
                true,
                new Vector2(0.06f, 0.61f),
                new Vector2(0.27f, 0.84f),
                Vector2.zero,
                Vector2.zero);
            icon.raycastTarget = false;
            Text skillName = CreateText(
                "Name",
                panel.transform,
                string.Empty,
                25,
                TextAnchor.MiddleLeft,
                new Vector2(0.31f, 0.73f),
                new Vector2(0.94f, 0.84f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.AccentColor);
            Text cooldown = CreateText(
                "Cooldown",
                panel.transform,
                string.Empty,
                18,
                TextAnchor.MiddleLeft,
                new Vector2(0.31f, 0.63f),
                new Vector2(0.94f, 0.73f),
                Vector2.zero,
                Vector2.zero);
            Text source = CreateText(
                "Source",
                panel.transform,
                string.Empty,
                17,
                TextAnchor.MiddleLeft,
                new Vector2(0.06f, 0.51f),
                new Vector2(0.94f, 0.60f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            Text description = CreateText(
                "Description",
                panel.transform,
                string.Empty,
                18,
                TextAnchor.UpperLeft,
                new Vector2(0.06f, 0.08f),
                new Vector2(0.94f, 0.49f),
                Vector2.zero,
                Vector2.zero);

            BattlePreparationEditorUiFactory.SetObject(view, "closeButton", close.Button);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "closeButtonText",
                close.Text);
            BattlePreparationEditorUiFactory.SetObject(view, "titleText", title);
            BattlePreparationEditorUiFactory.SetObject(view, "iconImage", icon);
            BattlePreparationEditorUiFactory.SetObject(view, "nameText", skillName);
            BattlePreparationEditorUiFactory.SetObject(view, "cooldownText", cooldown);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "descriptionText",
                description);
            BattlePreparationEditorUiFactory.SetObject(view, "sourceText", source);
            root.SetActive(false);
            return view;
        }

        private static Component BuildContainerGrid(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            string titleText,
            int columns)
        {
            GameObject panel = CreatePanel(
                name,
                parent,
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.PanelLightColor);
            Component view = Runtime(panel, ContainerGridMono);
            Text title = CreateText(
                "Title",
                panel.transform,
                titleText,
                22,
                TextAnchor.MiddleLeft,
                new Vector2(0.04f, 0.84f),
                new Vector2(0.96f, 0.98f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.AccentColor);
            BattlePreparationEditorUiFactory.ScrollParts scroll =
                BattlePreparationEditorUiFactory.AddVerticalScroll(
                    "CellsScroll",
                    panel.transform,
                    7f,
                    new Vector4(8f, 8f, 8f, 8f),
                    true,
                    new Vector2(62f, 62f),
                    columns);
            BattlePreparationEditorUiFactory.SetRect(
                scroll.ScrollRect.GetComponent<RectTransform>(),
                new Vector2(0.02f, 0.03f),
                new Vector2(0.98f, 0.84f),
                Vector2.zero,
                Vector2.zero);
            Component template = BuildItemCell(
                "CellTemplate",
                scroll.Content,
                Vector2.zero,
                Vector2.zero,
                false);
            template.GetComponent<RectTransform>().sizeDelta = new Vector2(62f, 62f);
            template.gameObject.SetActive(false);
            Text empty = CreateText(
                "Empty",
                panel.transform,
                string.Empty,
                18,
                TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.18f),
                new Vector2(0.92f, 0.75f),
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            empty.gameObject.SetActive(false);

            BattlePreparationEditorUiFactory.SetObject(view, "titleText", title);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "cellsRoot",
                scroll.Content);
            BattlePreparationEditorUiFactory.SetObject(view, "cellTemplate", template);
            BattlePreparationEditorUiFactory.SetObject(view, "emptyText", empty);
            return view;
        }

        private static Component BuildItemCell(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            bool stretch)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            RectTransform rect = root.GetComponent<RectTransform>();
            if (stretch)
            {
                BattlePreparationEditorUiFactory.SetRect(
                    rect,
                    anchorMin,
                    anchorMax,
                    Vector2.zero,
                    Vector2.zero);
            }
            else
            {
                rect.sizeDelta = new Vector2(62f, 62f);
            }

            Image background = BattlePreparationEditorUiFactory.AddImage(
                root,
                BattlePreparationEditorUiFactory.CellColor,
                null,
                true);
            Component view = Runtime(root, ItemCellMono);
            Image quality = CreateImage(
                "QualityFrame",
                root.transform,
                null,
                new Color(0.38f, 0.38f, 0.38f, 0.75f),
                false,
                Vector2.zero,
                Vector2.one,
                new Vector2(2f, 2f),
                new Vector2(-2f, -2f));
            Image icon = CreateImage(
                "Icon",
                root.transform,
                null,
                Color.white,
                true,
                new Vector2(0.10f, 0.10f),
                new Vector2(0.90f, 0.90f),
                Vector2.zero,
                Vector2.zero);
            Image highlight = CreateImage(
                "DropHighlight",
                root.transform,
                null,
                new Color(0.22f, 0.92f, 0.42f, 0.72f),
                false,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            highlight.gameObject.SetActive(false);
            GameObject blocked = CreatePanel(
                "Blocked",
                root.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Color(0f, 0f, 0f, 0.66f));
            CreateText(
                "Text",
                blocked.transform,
                "×",
                26,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.WarningColor);
            blocked.SetActive(false);
            Text label = CreateText(
                "SlotLabel",
                root.transform,
                string.Empty,
                13,
                TextAnchor.LowerRight,
                new Vector2(0.42f, 0f),
                Vector2.one,
                Vector2.zero,
                new Vector2(-3f, 2f));
            RectTransform badgeRect = BattlePreparationEditorUiFactory.NewRect(
                "MajorAffixBadge",
                root.transform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-22f, 7f),
                new Vector2(-8f, 21f));
            badgeRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image majorAffixBadge = BattlePreparationEditorUiFactory.AddImage(
                badgeRect.gameObject,
                new Color(0.19f, 0.92f, 0.94f, 0.96f),
                null,
                false);
            majorAffixBadge.raycastTarget = false;
            majorAffixBadge.gameObject.SetActive(false);

            BattlePreparationEditorUiFactory.SetObject(
                view,
                "backgroundImage",
                background);
            BattlePreparationEditorUiFactory.SetObject(view, "iconImage", icon);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "qualityFrameImage",
                quality);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "majorAffixBadgeImage",
                majorAffixBadge);
            BattlePreparationEditorUiFactory.SetObject(
                view,
                "dropHighlightImage",
                highlight);
            BattlePreparationEditorUiFactory.SetObject(view, "blockedRoot", blocked);
            BattlePreparationEditorUiFactory.SetObject(view, "slotLabelText", label);
            return view;
        }

        private static void BuildItemDetailPrefab()
        {
            GameObject root = CreateWindowRoot(
                "sub_robot_item_detail",
                new Color(0f, 0f, 0f, 0.76f),
                true);
            try
            {
                Component mono = Runtime(root, ItemDetailMono);
                GameObject panel = CreatePanel(
                    "ItemDetailPanel",
                    root.transform,
                    new Vector2(0.28f, 0.08f),
                    new Vector2(0.72f, 0.92f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Text title = CreateText(
                    "Title", panel.transform, "Item Details", 30,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.05f, 0.89f), new Vector2(0.70f, 0.98f),
                    Vector2.zero, Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts close =
                    BattlePreparationEditorUiFactory.AddButton(
                        "CloseButton", panel.transform, "Close",
                        BattlePreparationEditorUiFactory.WarningColor, 20);
                BattlePreparationEditorUiFactory.SetRect(
                    close.Rect,
                    new Vector2(0.75f, 0.90f), new Vector2(0.95f, 0.97f),
                    Vector2.zero, Vector2.zero);
                Image icon = CreateImage(
                    "Icon", panel.transform, null, Color.white, true,
                    new Vector2(0.05f, 0.65f), new Vector2(0.30f, 0.87f),
                    Vector2.zero, Vector2.zero);
                icon.raycastTarget = false;
                Text itemName = CreateText(
                    "Name", panel.transform, string.Empty, 27,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.34f, 0.78f), new Vector2(0.95f, 0.87f),
                    Vector2.zero, Vector2.zero);
                Text quality = CreateText(
                    "Quality", panel.transform, string.Empty, 19,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.34f, 0.71f), new Vector2(0.95f, 0.78f),
                    Vector2.zero, Vector2.zero);
                Text value = CreateText(
                    "Value", panel.transform, string.Empty, 19,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.34f, 0.64f), new Vector2(0.95f, 0.71f),
                    Vector2.zero, Vector2.zero);

                BattlePreparationEditorUiFactory.ScrollParts body =
                    BattlePreparationEditorUiFactory.AddVerticalScroll(
                        "BodyScroll",
                        panel.transform,
                        7f,
                        new Vector4(8f, 8f, 8f, 8f),
                        false,
                        Vector2.zero,
                        1);
                BattlePreparationEditorUiFactory.SetRect(
                    body.ScrollRect.GetComponent<RectTransform>(),
                    new Vector2(0.045f, 0.045f),
                    new Vector2(0.955f, 0.62f),
                    Vector2.zero,
                    Vector2.zero);

                Text equipmentType = CreateLayoutText(
                    "EquipmentType", body.Content, 19, 36f,
                    BattlePreparationEditorUiFactory.TextColor);
                Text location = CreateLayoutText(
                    "Location", body.Content, 19, 36f,
                    BattlePreparationEditorUiFactory.TextColor);
                Text mainAttributes = CreateLayoutText(
                    "MainAttributes", body.Content, 20, 56f,
                    BattlePreparationEditorUiFactory.AccentColor);
                Text description = CreateLayoutText(
                    "Description", body.Content, 18, 112f,
                    BattlePreparationEditorUiFactory.SubtleTextColor);

                GameObject capacityRoot =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "CapacityAndBrainSlots",
                        body.Content);
                BattlePreparationEditorUiFactory.AddImage(
                    capacityRoot,
                    new Color(0.045f, 0.075f, 0.11f, 0.82f),
                    null,
                    false);
                LayoutElement capacityLayout = capacityRoot.AddComponent<LayoutElement>();
                capacityLayout.minHeight = 70f;
                capacityLayout.preferredHeight = 70f;
                Text capacityText =
                    BattlePreparationEditorUiFactory.AddTextChild(
                        "Values",
                        capacityRoot.transform,
                        string.Empty,
                        19,
                        TextAnchor.MiddleLeft,
                        BattlePreparationEditorUiFactory.AccentColor,
                        10f);
                capacityRoot.SetActive(false);

                GameObject providedSkillsRoot =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "ProvidedSkills",
                        body.Content);
                LayoutElement skillsLayout = providedSkillsRoot.AddComponent<LayoutElement>();
                skillsLayout.minHeight = 300f;
                skillsLayout.preferredHeight = 300f;
                Text providedSkillsTitle = CreateText(
                    "Title",
                    providedSkillsRoot.transform,
                    "Provided Skills",
                    20,
                    TextAnchor.MiddleLeft,
                    new Vector2(0f, 0.86f),
                    Vector2.one,
                    new Vector2(4f, 0f),
                    new Vector2(-4f, 0f),
                    BattlePreparationEditorUiFactory.AccentColor);
                providedSkillsTitle.raycastTarget = false;
                Component providedSkillsList = BuildSkillList(
                    "List",
                    providedSkillsRoot.transform,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0.86f),
                    82f);
                providedSkillsRoot.SetActive(false);
                Component effectList = BuildEquipmentEffectList(
                    "DirectEffects",
                    body.Content,
                    82f);
                effectList.gameObject.SetActive(false);
                Component majorAffix = BuildMajorAffixView(
                    "MajorAffix",
                    body.Content,
                    116f);
                majorAffix.gameObject.SetActive(false);

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(mono, "closeButton", close.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "closeButtonText", close.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "iconImage", icon);
                BattlePreparationEditorUiFactory.SetObject(mono, "nameText", itemName);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "descriptionText", description);
                BattlePreparationEditorUiFactory.SetObject(mono, "qualityText", quality);
                BattlePreparationEditorUiFactory.SetObject(mono, "valueText", value);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "equipmentTypeText", equipmentType);
                BattlePreparationEditorUiFactory.SetObject(mono, "locationText", location);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "mainAttributeText", mainAttributes);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "skillSlotCountText", null);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "capacityRoot", capacityRoot);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "capacityText", capacityText);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "providedSkillsRoot", providedSkillsRoot);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "providedSkillsTitleText", providedSkillsTitle);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "providedSkillsList", providedSkillsList);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "equipmentEffectList", effectList);
                BattlePreparationEditorUiFactory.SetObject(
                    mono, "majorAffixView", majorAffix);

                root.SetActive(false);
                SaveAndDestroy(root, ItemDetailPrefabPath);
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

        private static void BuildItemDetailPrefabV5()
        {
            GameObject root = CreateWindowRoot(
                "sub_robot_item_detail",
                new Color(0f, 0f, 0f, 0.76f),
                true);
            try
            {
                Component mono = Runtime(root, ItemDetailMono);
                GameObject panel = CreatePanel(
                    "ItemDetailPanel",
                    root.transform,
                    new Vector2(0.31f, 0.18f),
                    new Vector2(0.69f, 0.82f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Text title = CreateText(
                    "Title",
                    panel.transform,
                    "物品详情",
                    30,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.06f, 0.88f),
                    new Vector2(0.70f, 0.98f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts close =
                    BattlePreparationEditorUiFactory.AddButton(
                        "CloseButton",
                        panel.transform,
                        "关闭",
                        BattlePreparationEditorUiFactory.WarningColor,
                        20);
                BattlePreparationEditorUiFactory.SetRect(
                    close.Rect,
                    new Vector2(0.74f, 0.89f),
                    new Vector2(0.94f, 0.97f),
                    Vector2.zero,
                    Vector2.zero);
                Image icon = CreateImage(
                    "Icon",
                    panel.transform,
                    null,
                    Color.white,
                    true,
                    new Vector2(0.06f, 0.60f),
                    new Vector2(0.34f, 0.86f),
                    Vector2.zero,
                    Vector2.zero);
                Text itemName = CreateText(
                    "Name",
                    panel.transform,
                    string.Empty,
                    27,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.38f, 0.76f),
                    new Vector2(0.94f, 0.86f),
                    Vector2.zero,
                    Vector2.zero);
                Text quality = CreateText(
                    "Quality",
                    panel.transform,
                    string.Empty,
                    20,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.38f, 0.68f),
                    new Vector2(0.94f, 0.76f),
                    Vector2.zero,
                    Vector2.zero);
                Text value = CreateText(
                    "Value",
                    panel.transform,
                    string.Empty,
                    20,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.38f, 0.60f),
                    new Vector2(0.94f, 0.68f),
                    Vector2.zero,
                    Vector2.zero);
                Text equipmentType = CreateText(
                    "EquipmentType",
                    panel.transform,
                    string.Empty,
                    19,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.06f, 0.50f),
                    new Vector2(0.94f, 0.58f),
                    Vector2.zero,
                    Vector2.zero);
                Text location = CreateText(
                    "Location",
                    panel.transform,
                    string.Empty,
                    19,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.06f, 0.42f),
                    new Vector2(0.94f, 0.50f),
                    Vector2.zero,
                    Vector2.zero);
                Text mainAttributes = CreateText(
                    "MainAttributes",
                    panel.transform,
                    string.Empty,
                    20,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.06f, 0.32f),
                    new Vector2(0.94f, 0.42f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);
                Text description = CreateText(
                    "Description",
                    panel.transform,
                    string.Empty,
                    19,
                    TextAnchor.UpperLeft,
                    new Vector2(0.06f, 0.07f),
                    new Vector2(0.94f, 0.30f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.SubtleTextColor);

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "closeButton",
                    close.Button);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "closeButtonText",
                    close.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "iconImage", icon);
                BattlePreparationEditorUiFactory.SetObject(mono, "nameText", itemName);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "descriptionText",
                    description);
                BattlePreparationEditorUiFactory.SetObject(mono, "qualityText", quality);
                BattlePreparationEditorUiFactory.SetObject(mono, "valueText", value);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "equipmentTypeText",
                    equipmentType);
                BattlePreparationEditorUiFactory.SetObject(mono, "locationText", location);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "mainAttributeText",
                    mainAttributes);

                root.SetActive(false);
                SaveAndDestroy(root, ItemDetailPrefabPath);
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

        private static void BuildWorkbenchPrefab()
        {
            GameObject root = CreateWindowRoot(
                "win_battle_workbench",
                BattlePreparationEditorUiFactory.OverlayColor,
                true);
            try
            {
                Component mono = Runtime(root, WorkbenchMono);
                GameObject panel = CreatePanel(
                    "WorkbenchPanel",
                    root.transform,
                    new Vector2(0.12f, 0.08f),
                    new Vector2(0.88f, 0.92f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Text title = CreateText(
                    "Title",
                    panel.transform,
                    "工作台",
                    34,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.05f, 0.88f),
                    new Vector2(0.65f, 0.98f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts close =
                    BattlePreparationEditorUiFactory.AddButton(
                        "CloseButton",
                        panel.transform,
                        "关闭",
                        BattlePreparationEditorUiFactory.WarningColor,
                        22);
                BattlePreparationEditorUiFactory.SetRect(
                    close.Rect,
                    new Vector2(0.80f, 0.90f),
                    new Vector2(0.95f, 0.97f),
                    Vector2.zero,
                    Vector2.zero);

                GameObject menuRoot = CreateStateRoot("MenuRoot", panel.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    menuRoot.GetComponent<RectTransform>(),
                    new Vector2(0.05f, 0.08f),
                    new Vector2(0.95f, 0.84f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts equipmentEntry =
                    BattlePreparationEditorUiFactory.AddButton(
                        "EquipmentEntry",
                        menuRoot.transform,
                        "强化装备",
                        new Color(0.22f, 0.31f, 0.40f, 1f),
                        34);
                BattlePreparationEditorUiFactory.SetRect(
                    equipmentEntry.Rect,
                    new Vector2(0.02f, 0.14f),
                    new Vector2(0.32f, 0.86f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts infrastructureEntry =
                    BattlePreparationEditorUiFactory.AddButton(
                        "InfrastructureEntry",
                        menuRoot.transform,
                        "强化基建",
                        new Color(0.32f, 0.36f, 0.28f, 1f),
                        34);
                BattlePreparationEditorUiFactory.SetRect(
                    infrastructureEntry.Rect,
                    new Vector2(0.35f, 0.14f),
                    new Vector2(0.65f, 0.86f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts warehouseEntry =
                    BattlePreparationEditorUiFactory.AddButton(
                        "WarehouseEntry",
                        menuRoot.transform,
                        "仓库",
                        new Color(0.29f, 0.25f, 0.38f, 1f),
                        34);
                BattlePreparationEditorUiFactory.SetRect(
                    warehouseEntry.Rect,
                    new Vector2(0.68f, 0.14f),
                    new Vector2(0.98f, 0.86f),
                    Vector2.zero,
                    Vector2.zero);

                GameObject infrastructureRoot = CreateStateRoot(
                    "InfrastructureRoot",
                    panel.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    infrastructureRoot.GetComponent<RectTransform>(),
                    new Vector2(0.04f, 0.06f),
                    new Vector2(0.96f, 0.85f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts back =
                    BattlePreparationEditorUiFactory.AddButton(
                        "BackToMenu",
                        infrastructureRoot.transform,
                        "返回",
                        BattlePreparationEditorUiFactory.WarningColor,
                        20);
                BattlePreparationEditorUiFactory.SetRect(
                    back.Rect,
                    new Vector2(0.00f, 0.90f),
                    new Vector2(0.16f, 1.00f),
                    Vector2.zero,
                    Vector2.zero);

                GameObject tabs = CreatePanel(
                    "FacilityTabs",
                    infrastructureRoot.transform,
                    new Vector2(0.00f, 0.00f),
                    new Vector2(0.28f, 0.87f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelLightColor);
                BattlePreparationEditorUiFactory.ButtonParts warehouseTab =
                    BattlePreparationEditorUiFactory.AddButton(
                        "WarehouseTab",
                        tabs.transform,
                        "仓库",
                        new Color(0.88f, 0.67f, 0.22f, 1f),
                        26);
                BattlePreparationEditorUiFactory.SetRect(
                    warehouseTab.Rect,
                    new Vector2(0.08f, 0.66f),
                    new Vector2(0.92f, 0.89f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts insuranceTab =
                    BattlePreparationEditorUiFactory.AddButton(
                        "InsuranceBoxTab",
                        tabs.transform,
                        "安全箱",
                        new Color(0.22f, 0.28f, 0.34f, 1f),
                        26);
                BattlePreparationEditorUiFactory.SetRect(
                    insuranceTab.Rect,
                    new Vector2(0.08f, 0.39f),
                    new Vector2(0.92f, 0.62f),
                    Vector2.zero,
                    Vector2.zero);

                GameObject detail = CreatePanel(
                    "FacilityDetail",
                    infrastructureRoot.transform,
                    new Vector2(0.30f, 0.00f),
                    new Vector2(1.00f, 0.87f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelLightColor);
                Text currentLevel = CreateText(
                    "CurrentLevel",
                    detail.transform,
                    "Lv.1",
                    36,
                    TextAnchor.MiddleCenter,
                    new Vector2(0.06f, 0.76f),
                    new Vector2(0.36f, 0.94f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);
                CreateText(
                    "Arrow",
                    detail.transform,
                    "→",
                    38,
                    TextAnchor.MiddleCenter,
                    new Vector2(0.40f, 0.76f),
                    new Vector2(0.60f, 0.94f),
                    Vector2.zero,
                    Vector2.zero);
                Text nextLevel = CreateText(
                    "NextLevel",
                    detail.transform,
                    "Lv.2",
                    36,
                    TextAnchor.MiddleCenter,
                    new Vector2(0.64f, 0.76f),
                    new Vector2(0.94f, 0.94f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.AccentColor);
                Text capacity = CreateText(
                    "Capacity",
                    detail.transform,
                    string.Empty,
                    27,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.10f, 0.57f),
                    new Vector2(0.90f, 0.70f),
                    Vector2.zero,
                    Vector2.zero);
                Text addedCapacity = CreateText(
                    "AddedCapacity",
                    detail.transform,
                    string.Empty,
                    22,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.10f, 0.45f),
                    new Vector2(0.90f, 0.57f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.SubtleTextColor);
                Text cost = CreateText(
                    "Cost",
                    detail.transform,
                    string.Empty,
                    25,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.10f, 0.30f),
                    new Vector2(0.90f, 0.43f),
                    Vector2.zero,
                    Vector2.zero);
                Text gold = CreateText(
                    "Gold",
                    detail.transform,
                    string.Empty,
                    21,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.10f, 0.21f),
                    new Vector2(0.90f, 0.30f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.SubtleTextColor);
                BattlePreparationEditorUiFactory.ButtonParts upgrade =
                    BattlePreparationEditorUiFactory.AddButton(
                        "Upgrade",
                        detail.transform,
                        "升级",
                        BattlePreparationEditorUiFactory.AccentColor,
                        27);
                BattlePreparationEditorUiFactory.SetRect(
                    upgrade.Rect,
                    new Vector2(0.30f, 0.06f),
                    new Vector2(0.70f, 0.18f),
                    Vector2.zero,
                    Vector2.zero);
                infrastructureRoot.SetActive(false);

                GameObject warehouseRoot = CreateStateRoot(
                    "WarehouseRoot",
                    panel.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    warehouseRoot.GetComponent<RectTransform>(),
                    new Vector2(0.04f, 0.06f),
                    new Vector2(0.96f, 0.85f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts warehouseBack =
                    BattlePreparationEditorUiFactory.AddButton(
                        "WarehouseBack",
                        warehouseRoot.transform,
                        "返回",
                        BattlePreparationEditorUiFactory.WarningColor,
                        20);
                BattlePreparationEditorUiFactory.SetRect(
                    warehouseBack.Rect,
                    new Vector2(0.00f, 0.90f),
                    new Vector2(0.16f, 1.00f),
                    Vector2.zero,
                    Vector2.zero);
                BattlePreparationEditorUiFactory.ButtonParts warehouseSort =
                    BattlePreparationEditorUiFactory.AddButton(
                        "WarehouseSort",
                        warehouseRoot.transform,
                        "整理",
                        BattlePreparationEditorUiFactory.AccentColor,
                        20);
                BattlePreparationEditorUiFactory.SetRect(
                    warehouseSort.Rect,
                    new Vector2(0.82f, 0.90f),
                    new Vector2(1.00f, 1.00f),
                    Vector2.zero,
                    Vector2.zero);
                Component warehouseGrid = BuildContainerGrid(
                    "SharedWarehouse",
                    warehouseRoot.transform,
                    new Vector2(0.00f, 0.00f),
                    new Vector2(1.00f, 0.87f),
                    "仓库",
                    10);
                warehouseRoot.SetActive(false);

                GameObject itemDetailPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    ItemDetailPrefabPath);
                if (itemDetailPrefab == null)
                {
                    throw new InvalidOperationException(
                        $"Item detail prefab is unavailable: {ItemDetailPrefabPath}");
                }

                GameObject warehouseItemDetailObject = PrefabUtility.InstantiatePrefab(
                    itemDetailPrefab,
                    root.transform) as GameObject;
                if (warehouseItemDetailObject == null)
                {
                    throw new InvalidOperationException(
                        "Failed to instantiate workbench warehouse item detail sub view.");
                }

                warehouseItemDetailObject.name = "WarehouseItemDetail";
                warehouseItemDetailObject.SetActive(false);
                Component warehouseItemDetail = warehouseItemDetailObject.GetComponent(
                    BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                        ItemDetailMono));

                RectTransform warehouseDragLayer = BattlePreparationEditorUiFactory.NewRect(
                    "WarehouseDragLayer",
                    root.transform,
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero,
                    Vector2.zero);
                Image warehouseDragIcon = CreateImage(
                    "WarehouseDragIcon",
                    warehouseDragLayer,
                    null,
                    Color.white,
                    true,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    Vector2.zero);
                warehouseDragIcon.rectTransform.sizeDelta = new Vector2(78f, 78f);
                warehouseDragIcon.raycastTarget = false;
                warehouseDragIcon.gameObject.SetActive(false);
                warehouseItemDetailObject.transform.SetAsLastSibling();
                warehouseDragLayer.SetAsLastSibling();

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(mono, "closeButton", close.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "menuRoot", menuRoot);
                BattlePreparationEditorUiFactory.SetObject(mono, "equipmentEntryButton", equipmentEntry.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "equipmentEntryText", equipmentEntry.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "infrastructureEntryButton", infrastructureEntry.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "infrastructureEntryText", infrastructureEntry.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseEntryButton", warehouseEntry.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseEntryText", warehouseEntry.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "infrastructureRoot", infrastructureRoot);
                BattlePreparationEditorUiFactory.SetObject(mono, "backToMenuButton", back.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "backToMenuText", back.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseTabButton", warehouseTab.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseTabText", warehouseTab.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "insuranceBoxTabButton", insuranceTab.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "insuranceBoxTabText", insuranceTab.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "currentLevelText", currentLevel);
                BattlePreparationEditorUiFactory.SetObject(mono, "nextLevelText", nextLevel);
                BattlePreparationEditorUiFactory.SetObject(mono, "capacityText", capacity);
                BattlePreparationEditorUiFactory.SetObject(mono, "addedCapacityText", addedCapacity);
                BattlePreparationEditorUiFactory.SetObject(mono, "costText", cost);
                BattlePreparationEditorUiFactory.SetObject(mono, "goldText", gold);
                BattlePreparationEditorUiFactory.SetObject(mono, "upgradeButton", upgrade.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "upgradeButtonText", upgrade.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseRoot", warehouseRoot);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseBackButton", warehouseBack.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseBackText", warehouseBack.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseGrid", warehouseGrid);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseSortButton", warehouseSort.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseSortButtonText", warehouseSort.Text);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseDragLayer", warehouseDragLayer);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseDragIcon", warehouseDragIcon);
                BattlePreparationEditorUiFactory.SetObject(mono, "warehouseItemDetail", warehouseItemDetail);

                SaveAndDestroy(root, WorkbenchPrefabPath);
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

        private static GameObject CreateWindowRoot(
            string name,
            Color background,
            bool blocksRaycasts,
            string builderMarker = null)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, null);
            BattlePreparationEditorUiFactory.Stretch(root.GetComponent<RectTransform>());
            BattlePreparationEditorUiFactory.AddImage(
                root,
                background,
                null,
                blocksRaycasts);
            BattlePreparationEditorUiFactory.AddBuilderMarker(
                root,
                string.IsNullOrEmpty(builderMarker) ? BuilderMarker : builderMarker);
            return root;
        }

        private static GameObject CreatePanel(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            Color color)
        {
            GameObject panel = BattlePreparationEditorUiFactory.AddPanel(
                name,
                parent,
                color,
                true);
            BattlePreparationEditorUiFactory.SetRect(
                panel.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                offsetMin,
                offsetMax);
            return panel;
        }

        private static GameObject CreateStateRoot(string name, Transform parent)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.Stretch(root.GetComponent<RectTransform>());
            return root;
        }

        private static BattlePreparationEditorUiFactory.ButtonParts CreateTransparentHotspot(
            string name,
            Transform parent,
            Rect normalizedRect)
        {
            GameObject hotspot = BattlePreparationEditorUiFactory.NewUiObject(
                name,
                parent);
            RectTransform rect = hotspot.GetComponent<RectTransform>();
            SetNormalizedRect(rect, normalizedRect);
            Image image = BattlePreparationEditorUiFactory.AddImage(
                hotspot,
                new Color(1f, 1f, 1f, 0.001f),
                null,
                true);
            Button button = hotspot.AddComponent<Button>();
            button.targetGraphic = image;
            return new BattlePreparationEditorUiFactory.ButtonParts(
                hotspot,
                button,
                image,
                null);
        }

        private static void SetNormalizedRect(RectTransform rect, Rect normalizedRect)
        {
            BattlePreparationEditorUiFactory.SetRect(
                rect,
                new Vector2(normalizedRect.xMin, normalizedRect.yMin),
                new Vector2(normalizedRect.xMax, normalizedRect.yMax),
                Vector2.zero,
                Vector2.zero);
        }

        private static Text CreateLayoutText(
            string name,
            Transform parent,
            int fontSize,
            float preferredHeight,
            Color color)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            Text text = BattlePreparationEditorUiFactory.AddText(
                root,
                string.Empty,
                fontSize,
                TextAnchor.UpperLeft,
                color);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.minHeight = preferredHeight;
            layout.preferredHeight = preferredHeight;
            return text;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            string value,
            int fontSize,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            Color? color = null)
        {
            RectTransform rect = BattlePreparationEditorUiFactory.NewRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                offsetMin,
                offsetMax);
            return BattlePreparationEditorUiFactory.AddText(
                rect.gameObject,
                value,
                fontSize,
                alignment,
                color);
        }

        private static Image CreateImage(
            string name,
            Transform parent,
            Sprite sprite,
            Color color,
            bool preserveAspect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            RectTransform rect = BattlePreparationEditorUiFactory.NewRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                offsetMin,
                offsetMax);
            return BattlePreparationEditorUiFactory.AddImage(
                rect.gameObject,
                color,
                sprite,
                false,
                preserveAspect);
        }

        private static Component Runtime(GameObject target, string typeName)
        {
            return BattlePreparationEditorUiFactory.AddRuntimeComponent(target, typeName);
        }

        private static Sprite SpriteAt(
            IReadOnlyDictionary<int, Sprite> sprites,
            int resourceId)
        {
            if (sprites != null
                && sprites.TryGetValue(resourceId, out Sprite sprite)
                && sprite != null)
            {
                return sprite;
            }

            throw new InvalidOperationException(
                $"Battle preparation sprite is unavailable: resourceId={resourceId}");
        }

        private static void SaveAndDestroy(GameObject root, string path)
        {
            BattlePreparationEditorUiFactory.SetLayerRecursively(
                root,
                LayerMask.NameToLayer("UI"));
            BattlePreparationEditorUiFactory.SavePrefab(root, path);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
#endif
