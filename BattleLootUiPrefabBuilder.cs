#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// Creates the first 2.0i enemy-loot Addition with fixed templates. Item slots
    /// are still presentation instances under the configured roots; no item data is
    /// created or duplicated by this tool.
    /// </summary>
    internal static class BattleLootUiPrefabBuilder
    {
        private const string PrefabPath =
            "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_loot.prefab";
        private const string Marker = "__BattleLootUi_v2_0i_1";
        private const string MonoType = "Game.GUIMonoBattleLoot";
        private const string ItemViewType = "Game.BattleLootItemView";
        private const string ContainerCellType =
            "Game.BattleLootRobotContainerCellView";

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
                BattlePreparationEditorUiFactory.Place(
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
                BattlePreparationEditorUiFactory.Place(
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
                BattlePreparationEditorUiFactory.Place(
                    close.Rect,
                    new Vector2(1f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(-154f, -58f),
                    new Vector2(-20f, -14f));

                Text equipmentTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "EquipmentTitle",
                    panel.transform,
                    "装备",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    10f);
                BattlePreparationEditorUiFactory.Place(
                    equipmentTitle.rectTransform,
                    new Vector2(0.68f, 0.82f),
                    new Vector2(1f, 0.82f),
                    new Vector2(10f, -24f),
                    new Vector2(-20f, 24f));

                GameObject equipmentRootObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "EquipmentItems",
                    panel.transform);
                BattlePreparationEditorUiFactory.Place(
                    equipmentRootObject.GetComponent<RectTransform>(),
                    new Vector2(0.68f, 0.58f),
                    new Vector2(1f, 0.81f),
                    new Vector2(10f, 8f),
                    new Vector2(-20f, -8f));
                HorizontalLayoutGroup equipmentLayout =
                    equipmentRootObject.AddComponent<HorizontalLayoutGroup>();
                equipmentLayout.spacing = 12f;
                equipmentLayout.childAlignment = TextAnchor.MiddleLeft;
                equipmentLayout.childForceExpandWidth = false;
                equipmentLayout.childForceExpandHeight = false;

                BattleLootItemView equipmentTemplate = CreateItemTemplate(
                    equipmentRootObject.transform,
                    "EquipmentTemplate");
                equipmentTemplate.gameObject.SetActive(false);

                Text itemTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "ItemTitle",
                    panel.transform,
                    "物资",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    10f);
                BattlePreparationEditorUiFactory.Place(
                    itemTitle.rectTransform,
                    new Vector2(0.68f, 0.52f),
                    new Vector2(1f, 0.52f),
                    new Vector2(10f, -24f),
                    new Vector2(-20f, 24f));

                GameObject scrollObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "ItemsScroll",
                    panel.transform);
                BattlePreparationEditorUiFactory.Place(
                    scrollObject.GetComponent<RectTransform>(),
                    new Vector2(0.68f, 0.06f),
                    new Vector2(1f, 0.51f),
                    new Vector2(10f, 10f),
                    new Vector2(-20f, -8f));
                BattlePreparationEditorUiFactory.AddImage(
                    scrollObject,
                    new Color(0.01f, 0.02f, 0.04f, 0.7f),
                    null,
                    false);
                scrollObject.AddComponent<RectMask2D>();

                GameObject itemRootObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "ItemContent",
                    scrollObject.transform);
                BattlePreparationEditorUiFactory.SetRect(
                    itemRootObject.GetComponent<RectTransform>(),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    Vector2.zero,
                    new Vector2(0f, 0f));
                ContentSizeFitter contentFitter = itemRootObject.AddComponent<ContentSizeFitter>();
                contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                GridLayoutGroup itemGrid = itemRootObject.AddComponent<GridLayoutGroup>();
                itemGrid.cellSize = new Vector2(118f, 118f);
                itemGrid.spacing = new Vector2(10f, 10f);
                itemGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                itemGrid.constraintCount = 7;
                itemGrid.childAlignment = TextAnchor.UpperLeft;
                ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
                scroll.viewport = scrollObject.GetComponent<RectTransform>();
                scroll.content = itemRootObject.GetComponent<RectTransform>();
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;

                BattleLootItemView itemTemplate = CreateItemTemplate(
                    itemRootObject.transform,
                    "ItemTemplate");
                itemTemplate.gameObject.SetActive(false);

                Text backpackTitle = BattlePreparationEditorUiFactory.AddTextChild(
                    "BackpackTitle",
                    panel.transform,
                    "背包",
                    21,
                    TextAnchor.MiddleLeft,
                    new Color(0.76f, 0.88f, 0.96f, 1f),
                    8f);
                BattlePreparationEditorUiFactory.Place(
                    backpackTitle.rectTransform,
                    new Vector2(0.02f, 0.82f),
                    new Vector2(0.32f, 0.82f),
                    new Vector2(10f, -24f),
                    new Vector2(-10f, 24f));
                GameObject backpackRootObject = CreateContainerRoot(
                    panel.transform,
                    "BackpackItems",
                    new Vector2(0.02f, 0.12f),
                    new Vector2(0.32f, 0.81f));
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
                BattlePreparationEditorUiFactory.Place(
                    chestRigTitle.rectTransform,
                    new Vector2(0.35f, 0.82f),
                    new Vector2(0.65f, 0.82f),
                    new Vector2(10f, -24f),
                    new Vector2(-10f, 24f));
                GameObject chestRigRootObject = CreateContainerRoot(
                    panel.transform,
                    "ChestRigItems",
                    new Vector2(0.35f, 0.12f),
                    new Vector2(0.65f, 0.81f));
                BattleLootRobotContainerCellView chestRigCellTemplate =
                    CreateContainerCellTemplate(
                    chestRigRootObject.transform,
                    "ChestRigCellTemplate");
                chestRigCellTemplate.gameObject.SetActive(false);

                GameObject dragLayerObject = BattlePreparationEditorUiFactory.NewUiObject(
                    "DragLayer",
                    panel.transform);
                BattlePreparationEditorUiFactory.Stretch(
                    dragLayerObject.GetComponent<RectTransform>());
                dragLayerObject.transform.SetAsLastSibling();
                Image dragIcon = BattlePreparationEditorUiFactory.AddImage(
                    dragLayerObject,
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
                dragIcon.gameObject.SetActive(false);

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
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreateContainerRoot(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.Place(
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
            grid.cellSize = new Vector2(92f, 92f);
            grid.spacing = new Vector2(7f, 7f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
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
                new Vector2(92f, 92f));
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
            icon.enabled = false;

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
            string name)
        {
            GameObject item = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            BattlePreparationEditorUiFactory.Place(
                item.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(118f, 118f));
            Image background = BattlePreparationEditorUiFactory.AddImage(
                item,
                new Color(0.15f, 0.24f, 0.32f, 1f),
                null,
                false);

            GameObject iconObject = BattlePreparationEditorUiFactory.NewUiObject("Icon", item.transform);
            BattlePreparationEditorUiFactory.Stretch(iconObject.GetComponent<RectTransform>(), 12f);
            Image icon = BattlePreparationEditorUiFactory.AddImage(iconObject, Color.white, null, false, true);
            icon.enabled = false;

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
    }
}
#endif
