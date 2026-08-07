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

        internal const string BuilderMarker = "__BattlePreparationUiBuilder_v3";

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
        private const string WorkbenchMono = "Game.GUIMonoBattlePreparationWorkbench";

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
            WorkbenchMono,
        };

        internal static readonly string[] GeneratedPrefabPaths =
        {
            MainPrefabPath,
            ProductionPrefabPath,
            NameInputPrefabPath,
            RobotDetailPrefabPath,
            ItemDetailPrefabPath,
            WorkbenchPrefabPath,
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
        }

        private static void BuildMainPrefab(IReadOnlyDictionary<int, Sprite> sprites)
        {
            GameObject root = CreateWindowRoot(
                "win_battle_preparation",
                new Color(0f, 0f, 0f, 0f),
                false);
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
                new Vector2(0f, 0.76f),
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                BattlePreparationEditorUiFactory.SubtleTextColor);
            Component itemCell = BuildItemCell(
                "ItemCell",
                root.transform,
                new Vector2(0.08f, 0.05f),
                new Vector2(0.92f, 0.76f),
                true);
            GameObject unavailable = CreatePanel(
                "Unavailable",
                root.transform,
                new Vector2(0.08f, 0.05f),
                new Vector2(0.92f, 0.76f),
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
                "unavailableRoot",
                unavailable);
            root.SetActive(active);
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
                    new Vector2(0.18f, 0.12f),
                    new Vector2(0.82f, 0.88f),
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.PanelColor);
                Text title = CreateText(
                    "Title",
                    panel.transform,
                    "工作台",
                    34,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.05f, 0.87f),
                    new Vector2(0.65f, 0.97f),
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
                    new Vector2(0.78f, 0.89f),
                    new Vector2(0.95f, 0.96f),
                    Vector2.zero,
                    Vector2.zero);
                RectTransform content = BattlePreparationEditorUiFactory.NewRect(
                    "FutureContent",
                    panel.transform,
                    new Vector2(0.05f, 0.08f),
                    new Vector2(0.95f, 0.84f),
                    Vector2.zero,
                    Vector2.zero);
                Text empty = CreateText(
                    "EmptyState",
                    content,
                    "仓库升级与锻造功能将在后续版本开放。",
                    25,
                    TextAnchor.MiddleCenter,
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero,
                    Vector2.zero,
                    BattlePreparationEditorUiFactory.SubtleTextColor);

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(mono, "emptyStateText", empty);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "futureContentRoot",
                    content);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "closeButton",
                    close.Button);

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
            bool blocksRaycasts)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, null);
            BattlePreparationEditorUiFactory.Stretch(root.GetComponent<RectTransform>());
            BattlePreparationEditorUiFactory.AddImage(
                root,
                background,
                null,
                blocksRaycasts);
            BattlePreparationEditorUiFactory.AddBuilderMarker(root, BuilderMarker);
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
