#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// 2.0g 关卡地图、详情、挑战矩阵和定向选择四个 Addition 的固定层级生成器。
    /// 点位和模板布局属于 Prefab；业务数据仍由 Application 快照填充。
    /// </summary>
    internal static class BattleStageSelectionUiPrefabBuilder
    {
        private const string Marker = "__BattleStageSelectionUiBuilder_v2_2_0g";

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

        internal static void BuildAll()
        {
            BuildStageMap();
            BuildStageDetail();
            BuildChallengeSelection();
            BuildTargetSelection();
        }

        private static void BuildStageMap()
        {
            GameObject root = CreateRoot("win_battle_stage_map");
            try
            {
                Component mono = Runtime(root, StageMapMono);
                Image background = root.GetComponent<Image>();
                background.color = new Color(0.035f, 0.055f, 0.09f, 1f);
                background.preserveAspect = false;

                Text title = Text(
                    "Title",
                    root.transform,
                    "选择关卡",
                    40,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.04f, 0.90f),
                    new Vector2(0.55f, 0.98f));
                BattlePreparationEditorUiFactory.ButtonParts close = Button(
                    "CloseButton",
                    root.transform,
                    "返回",
                    new Vector2(0.91f, 0.91f),
                    new Vector2(0.98f, 0.97f),
                    BattlePreparationEditorUiFactory.WarningColor);

                Rect[] pointRects =
                {
                    new Rect(0.10f, 0.58f, 0.18f, 0.20f),
                    new Rect(0.34f, 0.35f, 0.18f, 0.20f),
                    new Rect(0.57f, 0.58f, 0.18f, 0.20f),
                    new Rect(0.78f, 0.27f, 0.18f, 0.20f),
                };
                List<UnityEngine.Object> points = new List<UnityEngine.Object>();
                for (int index = 0; index < pointRects.Length; index++)
                {
                    points.Add(BuildStageMapPoint(root.transform, index + 1, pointRects[index]));
                }

                Set(mono, "titleText", title);
                Set(mono, "closeButton", close.Button);
                Set(mono, "closeButtonText", close.Text);
                Set(mono, "mapBackgroundImage", background);
                BattlePreparationEditorUiFactory.SetObjects(mono, "stagePoints", points);
                Save(root, BattlePreparationUiPrefabBuilder.StageMapPrefabPath);
                root = null;
            }
            finally
            {
                Destroy(root);
            }
        }

        private static Component BuildStageMapPoint(Transform parent, int stageId, Rect rect)
        {
            GameObject root = Panel(
                $"StagePoint_{stageId}",
                parent,
                new Color(0.08f, 0.13f, 0.19f, 0.96f));
            SetRect(
                root.GetComponent<RectTransform>(),
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMax));
            Image hitImage = root.GetComponent<Image>();
            Button button = root.AddComponent<Button>();
            button.targetGraphic = hitImage;
            Component view = Runtime(root, StageMapPointMono);

            Image stageImage = Image(
                "StageImage",
                root.transform,
                new Vector2(0.04f, 0.22f),
                new Vector2(0.96f, 0.96f),
                Color.white);
            Text stageName = Text(
                "StageName",
                root.transform,
                $"关卡 {stageId}",
                21,
                TextAnchor.MiddleCenter,
                new Vector2(0.04f, 0.02f),
                new Vector2(0.96f, 0.24f));
            GameObject unavailable = Panel(
                "Unavailable",
                root.transform,
                new Color(0.03f, 0.04f, 0.06f, 0.86f));
            Stretch(unavailable.GetComponent<RectTransform>());
            Text(
                "Text",
                unavailable.transform,
                "未配置",
                20,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                Vector2.one);

            BattlePreparationEditorUiFactory.SetInt(view, "stageId", stageId);
            Set(view, "button", button);
            Set(view, "stageImage", stageImage);
            Set(view, "stageNameText", stageName);
            Set(view, "unavailableRoot", unavailable);
            return view;
        }

        private static void BuildStageDetail()
        {
            GameObject root = CreateRoot("win_battle_stage_detail");
            try
            {
                Component mono = Runtime(root, StageDetailMono);
                GameObject panel = Panel(
                    "DetailPanel",
                    root.transform,
                    BattlePreparationEditorUiFactory.PanelColor);
                SetRect(
                    panel.GetComponent<RectTransform>(),
                    new Vector2(0.025f, 0.035f),
                    new Vector2(0.975f, 0.965f));

                Text title = Text(
                    "Title",
                    panel.transform,
                    "关卡详情",
                    38,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.03f, 0.89f),
                    new Vector2(0.54f, 0.98f));
                BattlePreparationEditorUiFactory.ButtonParts close = Button(
                    "CloseButton",
                    panel.transform,
                    "返回",
                    new Vector2(0.89f, 0.91f),
                    new Vector2(0.98f, 0.97f),
                    BattlePreparationEditorUiFactory.WarningColor);

                Image stageImage = Image(
                    "StageImage",
                    panel.transform,
                    new Vector2(0.03f, 0.51f),
                    new Vector2(0.31f, 0.87f),
                    new Color(0.08f, 0.11f, 0.16f, 1f));
                Text description = Text(
                    "Description",
                    panel.transform,
                    string.Empty,
                    20,
                    TextAnchor.UpperLeft,
                    new Vector2(0.03f, 0.29f),
                    new Vector2(0.31f, 0.49f));

                GameObject difficultyPanel = Panel(
                    "DifficultyPanel",
                    panel.transform,
                    BattlePreparationEditorUiFactory.PanelLightColor);
                SetRect(
                    difficultyPanel.GetComponent<RectTransform>(),
                    new Vector2(0.33f, 0.60f),
                    new Vector2(0.64f, 0.87f));
                Text score = Text(
                    "Score",
                    difficultyPanel.transform,
                    "积分 0",
                    28,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.06f, 0.70f),
                    new Vector2(0.94f, 0.96f));
                Text tier = Text(
                    "Tier",
                    difficultyPanel.transform,
                    "普通",
                    25,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.06f, 0.45f),
                    new Vector2(0.94f, 0.70f),
                    BattlePreparationEditorUiFactory.AccentColor);
                Text nextTier = Text(
                    "NextTier",
                    difficultyPanel.transform,
                    string.Empty,
                    18,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.06f, 0.08f),
                    new Vector2(0.94f, 0.42f),
                    BattlePreparationEditorUiFactory.SubtleTextColor);

                BattlePreparationEditorUiFactory.ButtonParts challenges = Button(
                    "ChallengeSelectionButton",
                    panel.transform,
                    "选择挑战词条",
                    new Vector2(0.33f, 0.50f),
                    new Vector2(0.48f, 0.57f),
                    BattlePreparationEditorUiFactory.AccentMutedColor);
                BattlePreparationEditorUiFactory.ButtonParts targets = Button(
                    "TargetSelectionButton",
                    panel.transform,
                    "选择定向",
                    new Vector2(0.49f, 0.50f),
                    new Vector2(0.64f, 0.57f),
                    BattlePreparationEditorUiFactory.AccentMutedColor);

                GameObject restrictionPanel = Panel(
                    "RestrictionPanel",
                    panel.transform,
                    BattlePreparationEditorUiFactory.PanelLightColor);
                SetRect(
                    restrictionPanel.GetComponent<RectTransform>(),
                    new Vector2(0.66f, 0.50f),
                    new Vector2(0.97f, 0.87f));
                Text(
                    "RestrictionTitle",
                    restrictionPanel.transform,
                    "当前档位限制",
                    24,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.05f, 0.83f),
                    new Vector2(0.95f, 0.98f));
                BattlePreparationEditorUiFactory.ScrollParts restrictionScroll =
                    BattlePreparationEditorUiFactory.AddVerticalScroll(
                        "Restrictions",
                        restrictionPanel.transform,
                        8f,
                        new Vector4(8f, 8f, 8f, 8f),
                        false,
                        Vector2.zero,
                        1);
                SetRect(
                    restrictionScroll.ScrollRect.GetComponent<RectTransform>(),
                    new Vector2(0.03f, 0.05f),
                    new Vector2(0.97f, 0.82f));
                Component restrictionTemplate = BuildRestrictionTemplate(
                    restrictionScroll.Content,
                    "RestrictionTemplate");
                restrictionTemplate.gameObject.SetActive(false);
                Text emptyRestriction = Text(
                    "EmptyRestriction",
                    restrictionPanel.transform,
                    "当前没有限制词条",
                    18,
                    TextAnchor.MiddleCenter,
                    new Vector2(0.10f, 0.30f),
                    new Vector2(0.90f, 0.60f),
                    BattlePreparationEditorUiFactory.SubtleTextColor);

                GameObject robotPanel = Panel(
                    "RobotPanel",
                    panel.transform,
                    BattlePreparationEditorUiFactory.PanelLightColor);
                SetRect(
                    robotPanel.GetComponent<RectTransform>(),
                    new Vector2(0.03f, 0.06f),
                    new Vector2(0.82f, 0.26f));
                Text(
                    "RobotTitle",
                    robotPanel.transform,
                    "选择一台机器人",
                    21,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.02f, 0.75f),
                    new Vector2(0.30f, 0.98f));
                List<UnityEngine.Object> robotSlots = new List<UnityEngine.Object>();
                for (int index = 0; index < 4; index++)
                {
                    float xMin = 0.02f + index * 0.245f;
                    robotSlots.Add(BuildDeploymentRobotSlot(
                        robotPanel.transform,
                        index + 1,
                        new Rect(xMin, 0.06f, 0.225f, 0.68f)));
                }

                BattlePreparationEditorUiFactory.ButtonParts deploy = Button(
                    "DeployButton",
                    panel.transform,
                    "出战",
                    new Vector2(0.84f, 0.07f),
                    new Vector2(0.97f, 0.21f),
                    new Color(0.17f, 0.58f, 0.35f, 1f),
                    28);

                GameObject restrictionDetail = Panel(
                    "RestrictionDetail",
                    panel.transform,
                    new Color(0.035f, 0.055f, 0.085f, 0.99f));
                SetRect(
                    restrictionDetail.GetComponent<RectTransform>(),
                    new Vector2(0.54f, 0.25f),
                    new Vector2(0.91f, 0.72f));
                Image restrictionDetailIcon = Image(
                    "Icon",
                    restrictionDetail.transform,
                    new Vector2(0.06f, 0.67f),
                    new Vector2(0.25f, 0.91f),
                    Color.white);
                Text restrictionDetailName = Text(
                    "Name",
                    restrictionDetail.transform,
                    string.Empty,
                    26,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.29f, 0.69f),
                    new Vector2(0.82f, 0.91f));
                BattlePreparationEditorUiFactory.ScrollParts restrictionDetailScroll =
                    BattlePreparationEditorUiFactory.AddVerticalScroll(
                        "DescriptionScroll",
                        restrictionDetail.transform,
                        0f,
                        new Vector4(8f, 8f, 8f, 8f),
                        false,
                        Vector2.zero,
                        1);
                SetRect(
                    restrictionDetailScroll.ScrollRect.GetComponent<RectTransform>(),
                    new Vector2(0.05f, 0.07f),
                    new Vector2(0.95f, 0.64f));
                Text restrictionDetailDescription = Text(
                    "Description",
                    restrictionDetailScroll.Content,
                    string.Empty,
                    19,
                    TextAnchor.UpperLeft,
                    Vector2.zero,
                    Vector2.one,
                    BattlePreparationEditorUiFactory.SubtleTextColor);
                restrictionDetailDescription.horizontalOverflow =
                    HorizontalWrapMode.Wrap;
                restrictionDetailDescription.verticalOverflow =
                    VerticalWrapMode.Overflow;
                ContentSizeFitter restrictionDescriptionFitter =
                    restrictionDetailDescription.gameObject.AddComponent<ContentSizeFitter>();
                restrictionDescriptionFitter.horizontalFit =
                    ContentSizeFitter.FitMode.Unconstrained;
                restrictionDescriptionFitter.verticalFit =
                    ContentSizeFitter.FitMode.PreferredSize;
                LayoutElement restrictionDescriptionLayout =
                    restrictionDetailDescription.gameObject.AddComponent<LayoutElement>();
                restrictionDescriptionLayout.minHeight = 120f;
                BattlePreparationEditorUiFactory.ButtonParts restrictionDetailClose = Button(
                    "CloseButton",
                    restrictionDetail.transform,
                    "×",
                    new Vector2(0.84f, 0.82f),
                    new Vector2(0.95f, 0.94f),
                    BattlePreparationEditorUiFactory.WarningColor,
                    24);
                restrictionDetail.SetActive(false);

                Set(mono, "titleText", title);
                Set(mono, "descriptionText", description);
                Set(mono, "stageImage", stageImage);
                Set(mono, "closeButton", close.Button);
                Set(mono, "closeButtonText", close.Text);
                Set(mono, "scoreText", score);
                Set(mono, "tierText", tier);
                Set(mono, "nextTierText", nextTier);
                Set(mono, "challengeSelectionButton", challenges.Button);
                Set(mono, "challengeSelectionButtonText", challenges.Text);
                Set(mono, "targetSelectionButton", targets.Button);
                Set(mono, "targetSelectionButtonText", targets.Text);
                Set(mono, "deployButton", deploy.Button);
                Set(mono, "deployButtonText", deploy.Text);
                BattlePreparationEditorUiFactory.SetObjects(mono, "robotSlots", robotSlots);
                Set(mono, "restrictionRoot", restrictionScroll.Content);
                Set(mono, "restrictionTemplate", restrictionTemplate);
                Set(mono, "emptyRestrictionText", emptyRestriction);
                Set(mono, "restrictionDetailRoot", restrictionDetail);
                Set(mono, "restrictionDetailIcon", restrictionDetailIcon);
                Set(mono, "restrictionDetailNameText", restrictionDetailName);
                Set(
                    mono,
                    "restrictionDetailDescriptionText",
                    restrictionDetailDescription);
                Set(
                    mono,
                    "restrictionDetailScrollRect",
                    restrictionDetailScroll.ScrollRect);
                Set(
                    mono,
                    "restrictionDetailCloseButton",
                    restrictionDetailClose.Button);
                Save(root, BattlePreparationUiPrefabBuilder.StageDetailPrefabPath);
                ValidateStageDetailPrefab();
                root = null;
            }
            finally
            {
                Destroy(root);
            }
        }

        private static Component BuildDeploymentRobotSlot(
            Transform parent,
            int slotId,
            Rect rect)
        {
            GameObject root = Panel(
                $"RobotSlot_{slotId}",
                parent,
                BattlePreparationEditorUiFactory.CellColor);
            SetRect(
                root.GetComponent<RectTransform>(),
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMax));
            Button button = root.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            Component view = Runtime(root, DeploymentRobotSlotMono);

            GameObject locked = State("Locked", root.transform);
            Text unlockPrice = Text(
                "Price",
                locked.transform,
                "0",
                17,
                TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.05f),
                new Vector2(0.95f, 0.95f));
            GameObject empty = State("Empty", root.transform);
            Text emptyText = Text(
                "Text",
                empty.transform,
                "+",
                35,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                Vector2.one);
            GameObject occupied = State("Occupied", root.transform);
            Image robotImage = Image(
                "RobotImage",
                occupied.transform,
                new Vector2(0.03f, 0.25f),
                new Vector2(0.35f, 0.95f),
                Color.white);
            Text robotName = Text(
                "Name",
                occupied.transform,
                string.Empty,
                16,
                TextAnchor.MiddleLeft,
                new Vector2(0.38f, 0.45f),
                new Vector2(0.96f, 0.86f));
            Text robotState = Text(
                "State",
                occupied.transform,
                string.Empty,
                14,
                TextAnchor.MiddleLeft,
                new Vector2(0.38f, 0.12f),
                new Vector2(0.96f, 0.44f));
            GameObject selected = Panel(
                "Selected",
                root.transform,
                new Color(0.12f, 0.74f, 0.95f, 0.18f));
            Stretch(selected.GetComponent<RectTransform>());
            selected.transform.SetAsFirstSibling();
            selected.SetActive(false);
            empty.SetActive(false);
            occupied.SetActive(false);

            BattlePreparationEditorUiFactory.SetInt(view, "slotId", slotId);
            Set(view, "mainButton", button);
            Set(view, "lockedRoot", locked);
            Set(view, "unlockPriceText", unlockPrice);
            Set(view, "emptyRoot", empty);
            Set(view, "emptyText", emptyText);
            Set(view, "occupiedRoot", occupied);
            Set(view, "robotImage", robotImage);
            Set(view, "robotNameText", robotName);
            Set(view, "robotStateText", robotState);
            Set(view, "selectedRoot", selected);
            return view;
        }

        private static Component BuildRestrictionTemplate(Transform parent, string name)
        {
            GameObject root = Panel(name, parent, BattlePreparationEditorUiFactory.CellColor);
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 72f;
            Button button = root.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            Component view = Runtime(root, RestrictionEntryMono);
            Image icon = Image(
                "Icon",
                root.transform,
                new Vector2(0.02f, 0.12f),
                new Vector2(0.20f, 0.88f),
                Color.white);
            Text nameText = Text(
                "Name",
                root.transform,
                string.Empty,
                17,
                TextAnchor.MiddleLeft,
                new Vector2(0.23f, 0.50f),
                new Vector2(0.97f, 0.92f));
            Text description = Text(
                "Description",
                root.transform,
                string.Empty,
                13,
                TextAnchor.MiddleLeft,
                new Vector2(0.23f, 0.08f),
                new Vector2(0.97f, 0.52f),
                BattlePreparationEditorUiFactory.SubtleTextColor);
            Set(view, "button", button);
            Set(view, "icon", icon);
            Set(view, "nameText", nameText);
            Set(view, "descriptionText", description);
            return view;
        }

        private static void BuildChallengeSelection()
        {
            GameObject root = CreateRoot("win_battle_challenge_selection");
            try
            {
                Component mono = Runtime(root, ChallengeSelectionMono);
                GameObject panel = Panel(
                    "Panel",
                    root.transform,
                    BattlePreparationEditorUiFactory.PanelColor);
                SetRect(panel.GetComponent<RectTransform>(), new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.97f));
                Text title = Text("Title", panel.transform, "挑战词条", 36, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.90f), new Vector2(0.40f, 0.98f));
                Text stageName = Text("StageName", panel.transform, string.Empty, 22, TextAnchor.MiddleLeft, new Vector2(0.30f, 0.90f), new Vector2(0.58f, 0.98f), BattlePreparationEditorUiFactory.SubtleTextColor);
                Text score = Text("Score", panel.transform, "积分 0", 24, TextAnchor.MiddleRight, new Vector2(0.59f, 0.90f), new Vector2(0.72f, 0.98f));
                Text tier = Text("Tier", panel.transform, "普通", 24, TextAnchor.MiddleRight, new Vector2(0.72f, 0.90f), new Vector2(0.84f, 0.98f), BattlePreparationEditorUiFactory.AccentColor);
                BattlePreparationEditorUiFactory.ButtonParts close = Button("Close", panel.transform, "返回", new Vector2(0.88f, 0.91f), new Vector2(0.97f, 0.97f), BattlePreparationEditorUiFactory.WarningColor);

                GameObject matrixPanel = Panel("MatrixPanel", panel.transform, BattlePreparationEditorUiFactory.PanelLightColor);
                SetRect(matrixPanel.GetComponent<RectTransform>(), new Vector2(0.03f, 0.06f), new Vector2(0.75f, 0.88f));
                BattlePreparationEditorUiFactory.ScrollParts matrixScroll = BattlePreparationEditorUiFactory.AddVerticalScroll(
                    "MatrixScroll", matrixPanel.transform, 12f, new Vector4(10f, 10f, 10f, 10f), false, Vector2.zero, 1);
                SetRect(matrixScroll.ScrollRect.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
                matrixScroll.ScrollRect.horizontal = true;
                matrixScroll.ScrollRect.vertical = false;
                matrixScroll.Content.anchorMin = new Vector2(0f, 0f);
                matrixScroll.Content.anchorMax = new Vector2(0f, 1f);
                matrixScroll.Content.pivot = new Vector2(0f, 0.5f);
                matrixScroll.Content.anchoredPosition = Vector2.zero;
                matrixScroll.Content.sizeDelta = Vector2.zero;
                HorizontalLayoutGroup horizontal = matrixScroll.Content.gameObject.GetComponent<VerticalLayoutGroup>() != null
                    ? ReplaceWithHorizontalLayout(matrixScroll.Content.gameObject)
                    : matrixScroll.Content.gameObject.AddComponent<HorizontalLayoutGroup>();
                horizontal.spacing = 12f;
                horizontal.padding = new RectOffset(10, 10, 10, 10);
                horizontal.childControlWidth = false;
                horizontal.childControlHeight = true;
                horizontal.childForceExpandHeight = true;
                Component columnTemplate = BuildChallengeColumn(matrixScroll.Content);
                columnTemplate.gameObject.SetActive(false);

                GameObject selectedPanel = Panel("SelectedPanel", panel.transform, BattlePreparationEditorUiFactory.PanelLightColor);
                SetRect(selectedPanel.GetComponent<RectTransform>(), new Vector2(0.77f, 0.06f), new Vector2(0.97f, 0.88f));
                Text("SelectedTitle", selectedPanel.transform, "已选词条", 23, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.90f), new Vector2(0.94f, 0.98f));
                BattlePreparationEditorUiFactory.ScrollParts selectedScroll = BattlePreparationEditorUiFactory.AddVerticalScroll(
                    "SelectedScroll", selectedPanel.transform, 8f, new Vector4(6f, 6f, 6f, 6f), false, Vector2.zero, 1);
                SetRect(selectedScroll.ScrollRect.GetComponent<RectTransform>(), new Vector2(0.03f, 0.08f), new Vector2(0.97f, 0.89f));
                Component selectedTemplate = BuildSelectedChallengeTemplate(selectedScroll.Content);
                selectedTemplate.gameObject.SetActive(false);
                Text empty = Text("Empty", selectedPanel.transform, "尚未选择", 18, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.38f), new Vector2(0.92f, 0.62f), BattlePreparationEditorUiFactory.SubtleTextColor);

                Set(mono, "titleText", title);
                Set(mono, "stageNameText", stageName);
                Set(mono, "scoreText", score);
                Set(mono, "tierText", tier);
                Set(mono, "closeButton", close.Button);
                Set(mono, "closeButtonText", close.Text);
                Set(mono, "columnsRoot", matrixScroll.Content);
                Set(mono, "columnTemplate", columnTemplate);
                Set(mono, "selectedRoot", selectedScroll.Content);
                Set(mono, "selectedTemplate", selectedTemplate);
                Set(mono, "emptySelectionText", empty);
                Save(root, BattlePreparationUiPrefabBuilder.ChallengeSelectionPrefabPath);
                root = null;
            }
            finally
            {
                Destroy(root);
            }
        }

        private static HorizontalLayoutGroup ReplaceWithHorizontalLayout(GameObject target)
        {
            VerticalLayoutGroup vertical = target.GetComponent<VerticalLayoutGroup>();
            if (vertical != null)
            {
                UnityEngine.Object.DestroyImmediate(vertical);
            }

            ContentSizeFitter fitter = target.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }

            return target.AddComponent<HorizontalLayoutGroup>();
        }

        private static Component BuildChallengeColumn(Transform parent)
        {
            GameObject root = Panel("ColumnTemplate", parent, new Color(0.07f, 0.10f, 0.15f, 1f));
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredWidth = 220f;
            layout.minWidth = 180f;
            Component view = Runtime(root, ChallengeColumnMono);
            Text name = Text("Name", root.transform, "词条", 21, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.89f), new Vector2(0.96f, 0.99f));
            RectTransform levelRoot = BattlePreparationEditorUiFactory.NewRect("Levels", root.transform, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero);
            VerticalLayoutGroup levelLayout = levelRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            levelLayout.spacing = 8f;
            levelLayout.childControlHeight = false;
            levelLayout.childControlWidth = true;
            levelLayout.childForceExpandWidth = true;
            Component template = BuildChallengeLevelTemplate(levelRoot);
            template.gameObject.SetActive(false);
            Set(view, "nameText", name);
            Set(view, "levelRoot", levelRoot);
            Set(view, "levelTemplate", template);
            return view;
        }

        private static Component BuildChallengeLevelTemplate(Transform parent)
        {
            GameObject root = Panel("LevelTemplate", parent, BattlePreparationEditorUiFactory.CellColor);
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 86f;
            Image background = root.GetComponent<Image>();
            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            Component view = Runtime(root, ChallengeLevelCellMono);
            Text point = Text("Point", root.transform, "1", 26, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.08f), new Vector2(0.24f, 0.92f), BattlePreparationEditorUiFactory.AccentColor);
            Text description = Text("Description", root.transform, string.Empty, 15, TextAnchor.MiddleLeft, new Vector2(0.27f, 0.08f), new Vector2(0.97f, 0.92f));
            Set(view, "button", button);
            Set(view, "background", background);
            Set(view, "pointText", point);
            Set(view, "descriptionText", description);
            return view;
        }

        private static Component BuildSelectedChallengeTemplate(Transform parent)
        {
            GameObject root = Panel("SelectedTemplate", parent, BattlePreparationEditorUiFactory.CellColor);
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 104f;
            Component view = Runtime(root, SelectedChallengeEntryMono);
            Image icon = Image(
                "Icon",
                root.transform,
                new Vector2(0.03f, 0.17f),
                new Vector2(0.24f, 0.83f),
                Color.white);
            Text name = Text(
                "Name",
                root.transform,
                string.Empty,
                16,
                TextAnchor.MiddleLeft,
                new Vector2(0.27f, 0.53f),
                new Vector2(0.72f, 0.91f));
            Text description = Text(
                "Description",
                root.transform,
                string.Empty,
                13,
                TextAnchor.UpperLeft,
                new Vector2(0.27f, 0.09f),
                new Vector2(0.77f, 0.55f),
                BattlePreparationEditorUiFactory.SubtleTextColor);
            Text point = Text(
                "Point",
                root.transform,
                string.Empty,
                18,
                TextAnchor.MiddleCenter,
                new Vector2(0.73f, 0.20f),
                new Vector2(0.84f, 0.82f),
                BattlePreparationEditorUiFactory.AccentColor);
            BattlePreparationEditorUiFactory.ButtonParts remove = Button(
                "Remove",
                root.transform,
                "×",
                new Vector2(0.85f, 0.18f),
                new Vector2(0.97f, 0.82f),
                BattlePreparationEditorUiFactory.WarningColor,
                21);
            Set(view, "icon", icon);
            Set(view, "nameText", name);
            Set(view, "descriptionText", description);
            Set(view, "pointText", point);
            Set(view, "removeButton", remove.Button);
            return view;
        }

        private static void BuildTargetSelection()
        {
            GameObject root = CreateRoot("win_battle_target_selection");
            try
            {
                Component mono = Runtime(root, TargetSelectionMono);
                GameObject panel = Panel("Panel", root.transform, BattlePreparationEditorUiFactory.PanelColor);
                SetRect(panel.GetComponent<RectTransform>(), new Vector2(0.08f, 0.07f), new Vector2(0.92f, 0.93f));
                Text title = Text("Title", panel.transform, "定向选择", 36, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.88f), new Vector2(0.60f, 0.98f));
                BattlePreparationEditorUiFactory.ButtonParts close = Button("Close", panel.transform, "返回", new Vector2(0.86f, 0.90f), new Vector2(0.96f, 0.97f), BattlePreparationEditorUiFactory.WarningColor);
                Text hint = Text("Hint", panel.transform, "任一列表清空时，不进行定向替换", 17, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.88f), BattlePreparationEditorUiFactory.SubtleTextColor);

                GameObject heroPanel = Panel("HeroPanel", panel.transform, BattlePreparationEditorUiFactory.PanelLightColor);
                SetRect(heroPanel.GetComponent<RectTransform>(), new Vector2(0.04f, 0.08f), new Vector2(0.48f, 0.80f));
                Text heroTitle = Text("HeroTitle", heroPanel.transform, "英雄类型", 25, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.98f));
                BattlePreparationEditorUiFactory.ScrollParts heroScroll = BattlePreparationEditorUiFactory.AddVerticalScroll(
                    "HeroScroll", heroPanel.transform, 10f, new Vector4(8f, 8f, 8f, 8f), true, new Vector2(180f, 82f), 2);
                SetRect(heroScroll.ScrollRect.GetComponent<RectTransform>(), new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.86f));
                Component heroTemplate = BuildTargetOptionTemplate(heroScroll.Content, "HeroTemplate");
                heroTemplate.gameObject.SetActive(false);

                GameObject affixPanel = Panel("MajorAffixPanel", panel.transform, BattlePreparationEditorUiFactory.PanelLightColor);
                SetRect(affixPanel.GetComponent<RectTransform>(), new Vector2(0.52f, 0.08f), new Vector2(0.96f, 0.80f));
                Text affixTitle = Text("MajorAffixTitle", affixPanel.transform, "主词条", 25, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.98f));
                BattlePreparationEditorUiFactory.ScrollParts affixScroll = BattlePreparationEditorUiFactory.AddVerticalScroll(
                    "MajorAffixScroll", affixPanel.transform, 10f, new Vector4(8f, 8f, 8f, 8f), true, new Vector2(180f, 82f), 2);
                SetRect(affixScroll.ScrollRect.GetComponent<RectTransform>(), new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.86f));
                Component affixTemplate = BuildTargetOptionTemplate(affixScroll.Content, "MajorAffixTemplate");
                affixTemplate.gameObject.SetActive(false);

                Set(mono, "titleText", title);
                Set(mono, "heroTitleText", heroTitle);
                Set(mono, "majorAffixTitleText", affixTitle);
                Set(mono, "emptyTargetHintText", hint);
                Set(mono, "closeButton", close.Button);
                Set(mono, "closeButtonText", close.Text);
                Set(mono, "heroRoot", heroScroll.Content);
                Set(mono, "heroTemplate", heroTemplate);
                Set(mono, "majorAffixRoot", affixScroll.Content);
                Set(mono, "majorAffixTemplate", affixTemplate);
                Save(root, BattlePreparationUiPrefabBuilder.TargetSelectionPrefabPath);
                root = null;
            }
            finally
            {
                Destroy(root);
            }
        }

        private static Component BuildTargetOptionTemplate(Transform parent, string name)
        {
            GameObject root = Panel(name, parent, BattlePreparationEditorUiFactory.CellColor);
            Image background = root.GetComponent<Image>();
            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            Component view = Runtime(root, TargetToggleOptionMono);
            Image icon = Image("Icon", root.transform, new Vector2(0.04f, 0.14f), new Vector2(0.30f, 0.86f), Color.white);
            Text nameText = Text("Name", root.transform, string.Empty, 17, TextAnchor.MiddleLeft, new Vector2(0.34f, 0.10f), new Vector2(0.94f, 0.90f));
            GameObject selected = Panel("Selected", root.transform, new Color(0.12f, 0.74f, 0.95f, 0.20f));
            Stretch(selected.GetComponent<RectTransform>());
            selected.transform.SetAsFirstSibling();
            selected.SetActive(false);
            Set(view, "button", button);
            Set(view, "background", background);
            Set(view, "icon", icon);
            Set(view, "nameText", nameText);
            Set(view, "selectedRoot", selected);
            return view;
        }

        private static GameObject CreateRoot(string name)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, null);
            Stretch(root.GetComponent<RectTransform>());
            BattlePreparationEditorUiFactory.AddImage(
                root,
                new Color(0.02f, 0.03f, 0.05f, 0.96f),
                null,
                true);
            BattlePreparationEditorUiFactory.AddBuilderMarker(root, Marker);
            BattlePreparationEditorUiFactory.AddBuilderMarker(
                root,
                BattlePreparationUiPrefabBuilder.BuilderMarker);
            return root;
        }

        private static GameObject Panel(string name, Transform parent, Color color)
        {
            return BattlePreparationEditorUiFactory.AddPanel(name, parent, color, true);
        }

        private static GameObject State(string name, Transform parent)
        {
            GameObject root = BattlePreparationEditorUiFactory.NewUiObject(name, parent);
            Stretch(root.GetComponent<RectTransform>());
            return root;
        }

        private static Text Text(
            string name,
            Transform parent,
            string value,
            int fontSize,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color? color = null)
        {
            RectTransform rect = BattlePreparationEditorUiFactory.NewRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
            return BattlePreparationEditorUiFactory.AddText(
                rect.gameObject,
                value,
                fontSize,
                alignment,
                color);
        }

        private static Image Image(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color)
        {
            RectTransform rect = BattlePreparationEditorUiFactory.NewRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
            return BattlePreparationEditorUiFactory.AddImage(
                rect.gameObject,
                color,
                null,
                false,
                true);
        }

        private static BattlePreparationEditorUiFactory.ButtonParts Button(
            string name,
            Transform parent,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color,
            int fontSize = 20)
        {
            BattlePreparationEditorUiFactory.ButtonParts button =
                BattlePreparationEditorUiFactory.AddButton(
                    name,
                    parent,
                    label,
                    color,
                    fontSize);
            SetRect(button.Rect, anchorMin, anchorMax);
            return button;
        }

        private static Component Runtime(GameObject target, string typeName)
        {
            return BattlePreparationEditorUiFactory.AddRuntimeComponent(target, typeName);
        }

        private static void Set(Component component, string field, UnityEngine.Object value)
        {
            BattlePreparationEditorUiFactory.SetObject(component, field, value);
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            BattlePreparationEditorUiFactory.SetRect(
                rect,
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
        }

        private static void Stretch(RectTransform rect)
        {
            BattlePreparationEditorUiFactory.Stretch(rect);
        }

        private static void Save(GameObject root, string path)
        {
            BattlePreparationEditorUiFactory.SetLayerRecursively(
                root,
                LayerMask.NameToLayer("UI"));
            BattlePreparationEditorUiFactory.SavePrefab(root, path);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void ValidateStageDetailPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BattlePreparationUiPrefabBuilder.StageDetailPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Battle stage detail prefab was not generated.");
            }

            Component mono = prefab.GetComponent(
                BattlePreparationEditorUiFactory.ResolveRuntimeComponentType(
                    StageDetailMono));
            string[] requiredBindings =
            {
                "restrictionDetailRoot",
                "restrictionDetailIcon",
                "restrictionDetailNameText",
                "restrictionDetailDescriptionText",
                "restrictionDetailScrollRect",
                "restrictionDetailCloseButton",
            };
            for (int index = 0; index < requiredBindings.Length; index++)
            {
                string propertyName = requiredBindings[index];
                if (mono == null
                    || BattlePreparationEditorUiFactory
                        .FindRequiredProperty(mono, propertyName)
                        .objectReferenceValue == null)
                {
                    throw new InvalidOperationException(
                        $"Battle stage restriction detail binding is missing: " +
                        propertyName);
                }
            }
        }

        private static void Destroy(GameObject root)
        {
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
