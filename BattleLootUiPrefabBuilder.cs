#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// 生成 2.0i 三栏搜刮 Addition 预制体。格子只是界面实例，
    /// 工具不会创建、复制或修改任何正式物品数据。
    /// </summary>
    internal static class BattleLootUiPrefabBuilder
    {
        private const string PrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_loot.prefab";
        private const string Marker = "__BattleLootUi_v2_0i_3_three_columns_skills";
        private const string MonoType = "Game.GUIMonoBattleLoot";
        private const string ItemViewType = "Game.BattleLootItemView";
        private const string ContainerCellType =
            "Game.BattleLootRobotContainerCellView";
        private const string LootSkillSlotType =
            "Game.BattleLootSkillSlotView";
        private const string LootSkillViewType =
            "Game.BattleLootSkillView";

        [MenuItem("TryGame/Battle WorldZone/Rebuild 2.0i Loot UI", false, 435)]
        private static void Rebuild()
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(
                "win_battle_loot",
                null);
            try
            {
                BattlePreparationEditorUiFactory.Stretch(
                    root.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.AddImage(
                    root,
                    new Color(0f, 0f, 0f, 0.68f),
                    null,
                    true);
                Component mono = BattlePreparationEditorUiFactory.AddRuntimeComponent(
                    root,
                    MonoType);

                GameObject panel = BattlePreparationEditorUiFactory.AddPanel(
                    "Panel",
                    root.transform,
                    new Color(0.025f, 0.045f, 0.08f, 0.98f),
                    true);
                BattlePreparationEditorUiFactory.Place(
                    panel.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(1050f, 650f));

                Text title = BattlePreparationEditorUiFactory.AddTextChild(
                    "Title",
                    panel.transform,
                    "战利品",
                    30,
                    TextAnchor.MiddleLeft,
                    new Color(0.82f, 0.94f, 1f, 1f),
                    12f);
                BattlePreparationEditorUiFactory.SetRect(
                    title.rectTransform,
                    new Vector2(0f, 1f),
                    new Vector2(0.55f, 1f),
                    new Vector2(22f, -64f),
                    new Vector2(-12f, -14f));

                Text owner = BattlePreparationEditorUiFactory.AddTextChild(
                    "Owner",
                    panel.transform,
                    string.Empty,
                    16,
                    TextAnchor.MiddleLeft,
                    new Color(0.54f, 0.68f, 0.78f, 1f),
                    12f);
                BattlePreparationEditorUiFactory.SetRect(
                    owner.rectTransform,
                    new Vector2(0.55f, 1f),
                    new Vector2(0.82f, 1f),
                    new Vector2(0f, -62f),
                    new Vector2(0f, -16f));

                BattlePreparationEditorUiFactory.ButtonParts close =
                    BattlePreparationEditorUiFactory.AddButton(
                        "Close",
                        panel.transform,
                        "关闭",
                        new Color(0.24f, 0.34f, 0.45f, 1f),
                        18);
                BattlePreparationEditorUiFactory.SetRect(
                    close.Rect,
                    new Vector2(1f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(-154f, -58f),
                    new Vector2(-20f, -14f));

                // 左栏直接沿用机器人详情的身体装备位置，不再使用临时通用网格。
                Text playerEquipmentTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "PlayerEquipmentTitle",
                    panel.transform,
                    "装备",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    10f);
                BattlePreparationEditorUiFactory.SetRect(
                    playerEquipmentTitle.rectTransform,
                    new Vector2(0.02f, 0.90f),
                    new Vector2(0.32f, 0.90f),
                    new Vector2(10f, -24f),
                    new Vector2(-10f, 24f));
                RectTransform playerEquipmentRootObject =
                    BattlePreparationEditorUiFactory.NewRect(
                        "PlayerEquipmentSlots",
                        panel.transform,
                        new Vector2(0.02f, 0.27f),
                        new Vector2(0.32f, 0.89f),
                        Vector2.zero,
                        Vector2.zero);
                int[] playerEquipmentPositionIds = { 9, 1, 2, 3, 4, 5, 6, 7, 8 };
                Vector2[] playerEquipmentAnchors =
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
                string[] playerEquipmentLabels =
                {
                    "脑部", "头部", "上装", "左手", "右手",
                    "左腿", "右腿", "背包", "胸挂",
                };
                BattleLootEquipmentSlotView[] playerEquipmentSlots =
                    new BattleLootEquipmentSlotView[playerEquipmentPositionIds.Length];
                for (int index = 0; index < playerEquipmentSlots.Length; index++)
                {
                    BattleLootEquipmentSlotView slot = CreateEquipmentSlot(
                        playerEquipmentRootObject,
                        $"Equipment_{playerEquipmentPositionIds[index]}",
                        playerEquipmentPositionIds[index],
                        playerEquipmentLabels[index]);
                    BattlePreparationEditorUiFactory.Place(
                        slot.GetComponent<RectTransform>(),
                        playerEquipmentAnchors[index],
                        new Vector2(0.5f, 0.5f),
                        Vector2.zero,
                        new Vector2(72f, 98f));
                    playerEquipmentSlots[index] = slot;
                }

                Text skillTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "SkillTitle",
                    panel.transform,
                    "Brain 技能",
                    15,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    6f);
                BattlePreparationEditorUiFactory.SetRect(
                    skillTitle.rectTransform,
                    new Vector2(0.02f, 0.235f),
                    new Vector2(0.32f, 0.265f),
                    new Vector2(10f, 0f),
                    new Vector2(-10f, 0f));
                GameObject skillSlotRootObject = CreateItemGridRoot(
                    panel.transform,
                    "SkillSlots",
                    new Vector2(0.02f, 0.145f),
                    new Vector2(0.32f, 0.235f),
                    3,
                    52f);
                BattleLootSkillSlotView skillSlotTemplate =
                    CreateSkillSlotTemplate(
                        skillSlotRootObject.transform,
                        "SkillSlotTemplate");
                skillSlotTemplate.gameObject.SetActive(false);

                Text availableSkillTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "AvailableSkillTitle",
                    panel.transform,
                    "可用技能",
                    13,
                    TextAnchor.MiddleLeft,
                    new Color(0.60f, 0.76f, 0.86f, 1f),
                    4f);
                BattlePreparationEditorUiFactory.SetRect(
                    availableSkillTitle.rectTransform,
                    new Vector2(0.02f, 0.115f),
                    new Vector2(0.32f, 0.145f),
                    new Vector2(10f, 0f),
                    new Vector2(-10f, 0f));
                GameObject availableSkillRootObject = CreateItemGridRoot(
                    panel.transform,
                    "AvailableSkills",
                    new Vector2(0.02f, 0.06f),
                    new Vector2(0.32f, 0.115f),
                    3,
                    52f);
                BattleLootSkillView availableSkillTemplate =
                    CreateSkillTemplate(
                        availableSkillRootObject.transform,
                        "AvailableSkillTemplate");
                availableSkillTemplate.gameObject.SetActive(false);

                Text equipmentTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "EquipmentTitle",
                    panel.transform,
                    "装备",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    10f);
                BattlePreparationEditorUiFactory.SetRect(
                    equipmentTitle.rectTransform,
                    new Vector2(0.68f, 0.90f),
                    new Vector2(1f, 0.90f),
                    new Vector2(10f, -24f),
                    new Vector2(-20f, 24f));

                GameObject equipmentRootObject = CreateItemGridRoot(
                    panel.transform,
                    "EquipmentItems",
                    new Vector2(0.68f, 0.61f),
                    new Vector2(1f, 0.89f),
                    3,
                    92f);
                BattleLootItemView equipmentTemplate = CreateItemTemplate(
                    equipmentRootObject.transform,
                    "EquipmentTemplate",
                    new Vector2(92f, 92f));
                equipmentTemplate.gameObject.SetActive(false);

                Text itemTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "ItemTitle",
                    panel.transform,
                    "物资",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    10f);
                BattlePreparationEditorUiFactory.SetRect(
                    itemTitle.rectTransform,
                    new Vector2(0.68f, 0.55f),
                    new Vector2(1f, 0.55f),
                    new Vector2(10f, -24f),
                    new Vector2(-20f, 24f));

                // 右栏物资同样复用仓库的 ScrollRect/Viewport/Content 层级，
                // 只是把仓库格模板替换成带搜索遮罩和进度条的 BattleLootItemView。
                BattlePreparationEditorUiFactory.ScrollParts itemsScroll =
                    BattlePreparationEditorUiFactory.AddVerticalScroll(
                        "ItemsScroll",
                        panel.transform,
                        7f,
                        new Vector4(10f, 10f, 10f, 10f),
                        true,
                        new Vector2(92f, 92f),
                        3);
                BattlePreparationEditorUiFactory.SetRect(
                    itemsScroll.ScrollRect.GetComponent<RectTransform>(),
                    new Vector2(0.68f, 0.06f),
                    new Vector2(1f, 0.54f),
                    new Vector2(10f, 10f),
                    new Vector2(-20f, -8f));
                GameObject itemRootObject = itemsScroll.Content.gameObject;

                BattleLootItemView itemTemplate = CreateItemTemplate(
                    itemRootObject.transform,
                    "ItemTemplate",
                    new Vector2(92f, 92f));
                itemTemplate.gameObject.SetActive(false);

                // 中栏仍然是玩家自己的随身容器，顺序与仓库详情一致。
                Text backpackTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "BackpackTitle",
                    panel.transform,
                    "背包",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    8f);
                BattlePreparationEditorUiFactory.SetRect(
                    backpackTitle.rectTransform,
                    new Vector2(0.35f, 0.90f),
                    new Vector2(0.65f, 0.90f),
                    new Vector2(10f, -24f),
                    new Vector2(-10f, 24f));
                GameObject backpackRootObject = CreateContainerRoot(
                    panel.transform,
                    "BackpackItems",
                    new Vector2(0.35f, 0.62f),
                    new Vector2(0.65f, 0.89f));
                BattleLootRobotContainerCellView backpackCellTemplate =
                    CreateContainerCellTemplate(
                    backpackRootObject.transform,
                    "BackpackCellTemplate");
                backpackCellTemplate.gameObject.SetActive(false);

                Text chestRigTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "ChestRigTitle",
                    panel.transform,
                    "胸挂",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    8f);
                BattlePreparationEditorUiFactory.SetRect(
                    chestRigTitle.rectTransform,
                    new Vector2(0.35f, 0.60f),
                    new Vector2(0.65f, 0.60f),
                    new Vector2(10f, -24f),
                    new Vector2(-10f, 24f));
                GameObject chestRigRootObject = CreateContainerRoot(
                    panel.transform,
                    "ChestRigItems",
                    new Vector2(0.35f, 0.32f),
                    new Vector2(0.65f, 0.59f));
                BattleLootRobotContainerCellView chestRigCellTemplate =
                    CreateContainerCellTemplate(
                    chestRigRootObject.transform,
                    "ChestRigCellTemplate");
                chestRigCellTemplate.gameObject.SetActive(false);

                Text insuranceTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "InsuranceTitle",
                    panel.transform,
                    "保险箱",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    8f);
                BattlePreparationEditorUiFactory.SetRect(
                    insuranceTitle.rectTransform,
                    new Vector2(0.35f, 0.29f),
                    new Vector2(0.65f, 0.29f),
                    new Vector2(10f, -24f),
                    new Vector2(-10f, 24f));
                GameObject insuranceRootObject = CreateContainerRoot(
                    panel.transform,
                    "InsuranceItems",
                    new Vector2(0.35f, 0.06f),
                    new Vector2(0.65f, 0.28f));
                BattleLootRobotContainerCellView insuranceCellTemplate =
                    CreateContainerCellTemplate(
                    insuranceRootObject.transform,
                    "InsuranceCellTemplate");
                insuranceCellTemplate.gameObject.SetActive(false);

                GameObject dragLayerObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "DragLayer",
                    panel.transform);
                // 拖拽层本身必须保持激活，运行时只开关 DragIcon；否则旧预制体
                // 即使打开了图标，父节点 inactive 仍然不会跟随鼠标绘制。
                dragLayerObject.SetActive(true);
                BattlePreparationEditorUiFactory.Stretch(
                    dragLayerObject.GetComponent<RectTransform>());
                dragLayerObject.transform.SetAsLastSibling();

                // 和仓库预制体保持相同的层级：DragLayer 是稳定的全屏坐标平面，
                // DragIcon 是其子节点。不能把图标 Image 直接挂在 DragLayer 上，
                // 否则移动图标时会连坐标基准一起移动，造成拖拽图标闪烁偏移。
                GameObject dragIconObject =
                    BattlePreparationEditorUiFactory.NewUiObject(
                        "DragIcon",
                        dragLayerObject.transform);
                Image dragIcon = BattlePreparationEditorUiFactory.AddImage(
                    dragIconObject,
                    Color.white,
                    null,
                    false,
                    true);
                BattlePreparationEditorUiFactory.Place(
                    dragIcon.rectTransform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(96f, 96f));
                dragIcon.raycastTarget = false;
                dragIconObject.SetActive(false);

                Text unavailable = BattlePreparationEditorUiFactory.AddTextChild(
                    "Unavailable",
                    panel.transform,
                    "战利品暂不可用",
                    20,
                    TextAnchor.MiddleCenter,
                    new Color(0.68f, 0.74f, 0.8f, 1f),
                    8f);
                BattlePreparationEditorUiFactory.Stretch(
                    unavailable.rectTransform,
                    20f);
                unavailable.gameObject.SetActive(false);

                BattlePreparationEditorUiFactory.SetObject(mono, "titleText", title);
                BattlePreparationEditorUiFactory.SetObject(mono, "ownerText", owner);
                BattlePreparationEditorUiFactory.SetObject(mono, "closeButton", close.Button);
                BattlePreparationEditorUiFactory.SetObject(mono, "closeButtonText", close.Text);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "equipmentTitleText",
                    equipmentTitle);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "itemTitleText",
                    itemTitle);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "itemsScrollRoot",
                    itemsScroll.ScrollRect.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "playerEquipmentRoot",
                    playerEquipmentRootObject);
                List<UnityEngine.Object> playerEquipmentSlotObjects =
                    new List<UnityEngine.Object>(playerEquipmentSlots.Length);
                for (int index = 0; index < playerEquipmentSlots.Length; index++)
                {
                    playerEquipmentSlotObjects.Add(playerEquipmentSlots[index]);
                }
                BattlePreparationEditorUiFactory.SetObjects(
                    mono,
                    "playerEquipmentSlots",
                    playerEquipmentSlotObjects);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "skillSlotRoot",
                    skillSlotRootObject.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "skillSlotTemplate",
                    skillSlotTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "availableSkillRoot",
                    availableSkillRootObject.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "availableSkillTemplate",
                    availableSkillTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "equipmentRoot",
                    equipmentRootObject.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(mono, "equipmentTemplate", equipmentTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "itemRoot",
                    itemRootObject.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(mono, "itemTemplate", itemTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "backpackRoot",
                    backpackRootObject.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "backpackCellTemplate",
                    backpackCellTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "chestRigRoot",
                    chestRigRootObject.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "chestRigCellTemplate",
                    chestRigCellTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "insuranceBoxRoot",
                    insuranceRootObject.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "insuranceBoxCellTemplate",
                    insuranceCellTemplate);
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "dragLayer",
                    dragLayerObject.GetComponent<RectTransform>());
                BattlePreparationEditorUiFactory.SetObject(
                    mono,
                    "dragIcon",
                    dragIcon);
                BattlePreparationEditorUiFactory.SetObject(mono, "unavailableText", unavailable);
                BattlePreparationEditorUiFactory.AddBuilderMarker(root, Marker);
                BattlePreparationEditorUiFactory.SavePrefab(root, PrefabPath);
                Debug.Log($"[BattleLootUiPrefabBuilder] 已生成 {PrefabPath}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreateContainerRoot(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            // 这里直接复用仓库 BuildContainerGrid 的滚动容器结构：
            // Panel -> CellsScroll -> Viewport -> Content。
            // 搜刮格仍然使用自己的 BattleLootRobotContainerCellView，
            // 这样可以在同一套仓库布局上增加搜索遮罩和搜刮拖拽，而不会再造
            // 一套容易出现锚点/ContentSizeFitter 尺寸问题的容器。
            GameObject panel = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.SetRect(
                panel.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                new Vector2(8f, 8f),
                new Vector2(-8f, -8f));
            BattlePreparationEditorUiFactory.AddImage(
                panel,
                BattlePreparationEditorUiFactory.PanelLightColor,
                null,
                true);
            BattlePreparationEditorUiFactory.AddTextChild(
                "Title",
                panel.transform,
                name == "BackpackItems"
                    ? "背包"
                    : name == "ChestRigItems"
                        ? "胸挂"
                        : "保险箱",
                16,
                TextAnchor.MiddleLeft,
                BattlePreparationEditorUiFactory.AccentColor,
                4f);

            BattlePreparationEditorUiFactory.ScrollParts scroll =
                BattlePreparationEditorUiFactory.AddVerticalScroll(
                    "CellsScroll",
                    panel.transform,
                    7f,
                    new Vector4(8f, 8f, 8f, 8f),
                    true,
                    new Vector2(62f, 62f),
                    5);
            BattlePreparationEditorUiFactory.SetRect(
                scroll.ScrollRect.GetComponent<RectTransform>(),
                new Vector2(0.02f, 0.03f),
                new Vector2(0.98f, 0.84f),
                Vector2.zero,
                Vector2.zero);
            return scroll.Content.gameObject;
        }

        private static BattleLootEquipmentSlotView CreateEquipmentSlot(
            Transform parent,
            string name,
            int layoutSlotId,
            string label)
        {
            GameObject slot = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            Image background = BattlePreparationEditorUiFactory.AddImage(
                slot,
                new Color(0.12f, 0.18f, 0.24f, 1f),
                null,
                false);

            GameObject iconObject = BattlePreparationEditorUiFactory.NewUiObject(
                "Icon",
                slot.transform);
            BattlePreparationEditorUiFactory.Stretch(
                iconObject.GetComponent<RectTransform>(),
                8f);
            Image icon = BattlePreparationEditorUiFactory.AddImage(
                iconObject,
                Color.white,
                null,
                false,
                true);

            GameObject highlightObject = BattlePreparationEditorUiFactory.NewUiObject(
                "DropHighlight",
                slot.transform);
            BattlePreparationEditorUiFactory.Stretch(
                highlightObject.GetComponent<RectTransform>(),
                0f);
            Image dropHighlight = BattlePreparationEditorUiFactory.AddImage(
                highlightObject,
                new Color(0.22f, 0.92f, 0.42f, 0.72f),
                null,
                false);
            dropHighlight.raycastTarget = false;
            highlightObject.SetActive(false);

            Text positionName = BattlePreparationEditorUiFactory.AddTextChild(
                "PositionName",
                slot.transform,
                label,
                11,
                TextAnchor.UpperCenter,
                new Color(0.86f, 0.92f, 0.96f, 1f),
                2f);
            BattlePreparationEditorUiFactory.SetRect(
                positionName.rectTransform,
                new Vector2(0f, 0.80f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero);

            Text cell = BattlePreparationEditorUiFactory.AddTextChild(
                "Cell",
                slot.transform,
                string.Empty,
                11,
                TextAnchor.LowerLeft,
                Color.white,
                3f);
            BattleLootEquipmentSlotView view =
                (BattleLootEquipmentSlotView)
                    BattlePreparationEditorUiFactory.AddRuntimeComponent(
                        slot,
                        "Game.BattleLootEquipmentSlotView");
            view.EditorConfigure(
                layoutSlotId,
                background,
                icon,
                null,
                positionName,
                cell,
                dropHighlight);
            return view;
        }

        private static GameObject CreateItemGridRoot(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int columnCount,
            float cellSize)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.SetRect(
                root.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                new Vector2(8f, 8f),
                new Vector2(-8f, -8f));
            BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(0.01f, 0.02f, 0.04f, 0.7f),
                null,
                false);
            GridLayoutGroup grid = root.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cellSize, cellSize);
            grid.spacing = new Vector2(7f, 7f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, columnCount);
            grid.childAlignment = TextAnchor.UpperLeft;
            return root;
        }

        private static BattleLootRobotContainerCellView CreateContainerCellTemplate(
            Transform parent,
            string name)
        {
            GameObject cell = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.Place(
                cell.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(62f, 62f));
            Image background = BattlePreparationEditorUiFactory.AddImage(
                cell,
                new Color(0.12f, 0.18f, 0.24f, 1f),
                null,
                false);

            GameObject iconObject = BattlePreparationEditorUiFactory.NewUiObject(
                "Icon",
                cell.transform);
            BattlePreparationEditorUiFactory.Stretch(
                iconObject.GetComponent<RectTransform>(),
                10f);
            Image icon = BattlePreparationEditorUiFactory.AddImage(
                iconObject,
                Color.white,
                null,
                false,
                true);

            GameObject maskObject = BattlePreparationEditorUiFactory.NewUiObject(
                "Mask",
                cell.transform);
            BattlePreparationEditorUiFactory.Stretch(
                maskObject.GetComponent<RectTransform>(),
                0f);
            Image mask = BattlePreparationEditorUiFactory.AddImage(
                maskObject,
                new Color(0.015f, 0.02f, 0.03f, 0.88f),
                null,
                false);

            GameObject highlightObject = BattlePreparationEditorUiFactory.NewUiObject(
                "Highlight",
                cell.transform);
            BattlePreparationEditorUiFactory.Stretch(
                highlightObject.GetComponent<RectTransform>(),
                0f);
            Image highlight = BattlePreparationEditorUiFactory.AddImage(
                highlightObject,
                new Color(0.22f, 0.92f, 0.42f, 0.72f),
                null,
                false);
            highlight.raycastTarget = false;
            highlightObject.SetActive(false);

            Text cellText = BattlePreparationEditorUiFactory.AddTextChild(
                "Cell",
                cell.transform,
                string.Empty,
                13,
                TextAnchor.UpperLeft,
                Color.white,
                4f);
            BattleLootRobotContainerCellView view =
                (BattleLootRobotContainerCellView)
                    BattlePreparationEditorUiFactory.AddRuntimeComponent(
                        cell,
                        ContainerCellType);
            view.EditorConfigure(background, icon, mask, highlight, cellText);
            return view;
        }

        private static BattleLootItemView CreateItemTemplate(
            Transform parent,
            string name,
            Vector2 size)
        {
            GameObject item = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.Place(
                item.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                size);
            Image background = BattlePreparationEditorUiFactory.AddImage(
                item,
                new Color(0.15f, 0.24f, 0.32f, 1f),
                null,
                false);

            GameObject iconObject = BattlePreparationEditorUiFactory.NewUiObject("Icon", item.transform);
            BattlePreparationEditorUiFactory.Stretch(iconObject.GetComponent<RectTransform>(), 12f);
            Image icon = BattlePreparationEditorUiFactory.AddImage(iconObject, Color.white, null, false, true);

            GameObject maskObject = BattlePreparationEditorUiFactory.NewUiObject("Mask", item.transform);
            BattlePreparationEditorUiFactory.Stretch(maskObject.GetComponent<RectTransform>(), 0f);
            Image mask = BattlePreparationEditorUiFactory.AddImage(
                maskObject,
                new Color(0.015f, 0.02f, 0.03f, 0.88f),
                null,
                false);

            GameObject progressObject = BattlePreparationEditorUiFactory.NewUiObject("Progress", item.transform);
            BattlePreparationEditorUiFactory.SetRect(
                progressObject.GetComponent<RectTransform>(),
                new Vector2(0.08f, 0.08f),
                new Vector2(0.92f, 0.18f),
                Vector2.zero,
                Vector2.zero);
            Image progress = BattlePreparationEditorUiFactory.AddImage(
                progressObject,
                new Color(0.2f, 0.75f, 1f, 0.95f),
                null,
                false);
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Horizontal;
            progress.fillOrigin = 0;
            progress.fillAmount = 0f;
            progressObject.SetActive(false);

            Text cell = BattlePreparationEditorUiFactory.AddTextChild(
                "Cell",
                item.transform,
                string.Empty,
                13,
                TextAnchor.UpperLeft,
                Color.white,
                4f);
            Text state = BattlePreparationEditorUiFactory.AddTextChild(
                "State",
                item.transform,
                string.Empty,
                13,
                TextAnchor.LowerCenter,
                Color.white,
                4f);
            Text nameText = BattlePreparationEditorUiFactory.AddTextChild(
                "Name",
                item.transform,
                string.Empty,
                11,
                TextAnchor.LowerLeft,
                Color.white,
                4f);
            BattleLootItemView view = (BattleLootItemView)
                BattlePreparationEditorUiFactory.AddRuntimeComponent(item, ItemViewType);
            view.EditorConfigure(background, icon, mask, progress, nameText, state, cell);
            return view;
        }

        private static BattleLootSkillSlotView CreateSkillSlotTemplate(
            Transform parent,
            string name)
        {
            GameObject slot = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.Place(
                slot.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(52f, 52f));
            Image background = BattlePreparationEditorUiFactory.AddImage(
                slot,
                new Color(0.12f, 0.18f, 0.24f, 1f),
                null,
                false);
            GameObject iconObject = BattlePreparationEditorUiFactory.NewUiObject(
                "Icon",
                slot.transform);
            BattlePreparationEditorUiFactory.Stretch(
                iconObject.GetComponent<RectTransform>(),
                7f);
            Image icon = BattlePreparationEditorUiFactory.AddImage(
                iconObject,
                Color.white,
                null,
                false,
                true);
            GameObject highlightObject = BattlePreparationEditorUiFactory.NewUiObject(
                "Highlight",
                slot.transform);
            BattlePreparationEditorUiFactory.Stretch(
                highlightObject.GetComponent<RectTransform>(),
                0f);
            Image highlight = BattlePreparationEditorUiFactory.AddImage(
                highlightObject,
                new Color(0.22f, 0.92f, 0.42f, 0.72f),
                null,
                false);
            highlight.raycastTarget = false;
            highlightObject.SetActive(false);
            Text slotText = BattlePreparationEditorUiFactory.AddTextChild(
                "Slot",
                slot.transform,
                string.Empty,
                11,
                TextAnchor.UpperLeft,
                Color.white,
                2f);
            Text nameText = BattlePreparationEditorUiFactory.AddTextChild(
                "Name",
                slot.transform,
                string.Empty,
                8,
                TextAnchor.LowerCenter,
                Color.white,
                1f);
            BattleLootSkillSlotView view =
                (BattleLootSkillSlotView)
                    BattlePreparationEditorUiFactory.AddRuntimeComponent(
                        slot,
                        LootSkillSlotType);
            view.EditorConfigure(1, background, icon, highlight, slotText, nameText);
            return view;
        }

        private static BattleLootSkillView CreateSkillTemplate(
            Transform parent,
            string name)
        {
            GameObject skill = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.Place(
                skill.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(52f, 52f));
            Image background = BattlePreparationEditorUiFactory.AddImage(
                skill,
                new Color(0.12f, 0.18f, 0.24f, 1f),
                null,
                false);
            GameObject iconObject = BattlePreparationEditorUiFactory.NewUiObject(
                "Icon",
                skill.transform);
            BattlePreparationEditorUiFactory.Stretch(
                iconObject.GetComponent<RectTransform>(),
                7f);
            Image icon = BattlePreparationEditorUiFactory.AddImage(
                iconObject,
                Color.white,
                null,
                false,
                true);
            Text nameText = BattlePreparationEditorUiFactory.AddTextChild(
                "Name",
                skill.transform,
                string.Empty,
                8,
                TextAnchor.LowerCenter,
                Color.white,
                1f);
            BattleLootSkillView view =
                (BattleLootSkillView)
                    BattlePreparationEditorUiFactory.AddRuntimeComponent(
                        skill,
                        LootSkillViewType);
            view.EditorConfigure(background, icon, nameText);
            return view;
        }
    }
}
#endif
