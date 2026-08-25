#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 战斗中行为图只读监视器。
    ///
    /// 运行方式：打开窗口后，在 Scene 中选中带有 BattleWorldZoneCombatActor
    /// 的角色。窗口会从当前战斗协调器找到对应的 BattleAiRuntime，并用和行为图
    /// 编辑器相同的节点/连线样式显示最近一次运行到的节点。这个工具只读运行时
    /// 状态，不修改行为图资产，也不把当前游标写入存档。
    /// </summary>
    public sealed class BattleAiRuntimeMonitorWindow : EditorWindow
    {
        private const string GraphResourcesPath = "TryGameRefdataRes/AiGraph";
        private const float CanvasMinHeight = 520f;
        private const float NodeWidth = 260f;
        private const float NodeHeight = 64f;
        private const float PortSpacing = 18f;
        private const float PortSize = 10f;
        private const float CanvasMinZoom = 0.4f;
        private const float CanvasMaxZoom = 2.5f;

        private BattleWorldZoneCombatActor selectedActor;
        private BattleWorldZoneCombatCoordinator coordinator;
        private BattleAiRuntime runtime;
        private BattleAiGraphAsset graphAsset;
        private Vector2 canvasPan;
        private float canvasZoom = 1f;
        private Vector2 lastCanvasSize = new Vector2(700f, CanvasMinHeight);
        private bool draggingCanvas;
        private int canvasHotControl;
        private Vector2 lastMousePosition;
        private BattleWorldZoneCombatActor[] sceneActors = Array.Empty<BattleWorldZoneCombatActor>();
        private int actorPopupIndex = -1;
        private double lastRefreshTime;

        [MenuItem("TryGame/Battle/AI Runtime Monitor", false, 434)]
        private static void Open()
        {
            BattleAiRuntimeMonitorWindow window =
                GetWindow<BattleAiRuntimeMonitorWindow>();
            window.titleContent = new GUIContent("Battle AI Runtime Monitor");
            window.minSize = new Vector2(1240f, 760f);
            window.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += OnEditorUpdate;
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup - lastRefreshTime < 0.05d)
            {
                return;
            }

            lastRefreshTime = EditorApplication.timeSinceStartup;
            ResolveRuntime();
            Repaint();
        }

        private void OnSelectionChanged()
        {
            GameObject selectedObject = Selection.activeGameObject;
            selectedActor = selectedObject != null
                ? selectedObject.GetComponentInParent<BattleWorldZoneCombatActor>()
                : null;
            actorPopupIndex = -1;
            ResolveRuntime();
            Repaint();
        }

        private void ResolveRuntime()
        {
            coordinator = FindObjectOfType<BattleWorldZoneCombatCoordinator>();
            runtime = null;
            graphAsset = null;
            if (selectedActor == null || coordinator == null ||
                string.IsNullOrEmpty(selectedActor.PersistentUid) ||
                !coordinator.TryGetHostileAiRuntime(
                    selectedActor.PersistentUid,
                    out BattleAiRuntime valueRuntime))
            {
                RefreshSceneActors();
                return;
            }

            runtime = valueRuntime;
            graphAsset = FindGraphAsset(runtime.EnemyAiProfileId);
            RefreshSceneActors();
        }

        private static BattleAiGraphAsset FindGraphAsset(int profileId)
        {
            if (profileId <= 0)
            {
                return null;
            }

            BattleAiGraphAsset[] assets = Resources.LoadAll<BattleAiGraphAsset>(GraphResourcesPath);
            BattleAiGraphAsset published = null;
            for (int index = 0; index < assets.Length; index++)
            {
                BattleAiGraphAsset asset = assets[index];
                if (asset == null || asset.EnemyAiProfileId != profileId ||
                    !asset.IsPublished)
                {
                    continue;
                }

                if (published != null)
                {
                    // 运行时已经会拒绝重复绑定；监视器只显示第一个并在右侧给出提示。
                    return published;
                }

                published = asset;
            }

            return published;
        }

        private void RefreshSceneActors()
        {
            sceneActors = FindObjectsOfType<BattleWorldZoneCombatActor>();
            if (selectedActor == null)
            {
                actorPopupIndex = -1;
                return;
            }

            actorPopupIndex = Array.IndexOf(sceneActors, selectedActor);
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawActorSelectionBar();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "请先进入 Play，再在 Scene 中选中敌人。运行监视器只读取战斗中的临时行为状态。",
                    MessageType.Info);
                return;
            }

            if (selectedActor == null)
            {
                EditorGUILayout.HelpBox(
                    "当前没有选中 BattleWorldZoneCombatActor。请在 Scene 中选中一个角色。",
                    MessageType.Info);
                return;
            }

            if (runtime == null)
            {
                EditorGUILayout.HelpBox(
                    "当前角色没有 BattleAiRuntime。玩家目前不使用敌人行为图；如果这是敌人，检查战斗协调器和 PersistentUid。",
                    MessageType.Warning);
                DrawActorIdentityOnly();
                return;
            }

            DrawRuntimeHeader();
            EditorGUILayout.BeginHorizontal();
            DrawGraphPanel();
            DrawBlackboardPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Battle AI Runtime Monitor", EditorStyles.toolbarButton, GUILayout.Width(190f));
            if (GUILayout.Button("刷新角色", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            {
                ResolveRuntime();
            }

            if (GUILayout.Button("重置视图", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            {
                canvasPan = Vector2.zero;
                canvasZoom = 1f;
                Repaint();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                EditorApplication.isPlaying ? "运行中：只读监视" : "未运行",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActorSelectionBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Scene 角色", GUILayout.Width(72f));
            UnityEngine.Object next = EditorGUILayout.ObjectField(
                selectedActor,
                typeof(BattleWorldZoneCombatActor),
                true,
                GUILayout.Width(300f));
            if (next != selectedActor)
            {
                selectedActor = next as BattleWorldZoneCombatActor;
                if (selectedActor != null)
                {
                    Selection.activeGameObject = selectedActor.gameObject;
                }
                ResolveRuntime();
            }

            if (sceneActors.Length > 0)
            {
                string[] names = new string[sceneActors.Length];
                for (int index = 0; index < sceneActors.Length; index++)
                {
                    BattleWorldZoneCombatActor actor = sceneActors[index];
                    names[index] = actor == null
                        ? "<missing>"
                        : actor.name + " · " + actor.PersistentUid;
                }

                int nextIndex = EditorGUILayout.Popup(
                    Mathf.Max(0, actorPopupIndex),
                    names,
                    GUILayout.Width(360f));
                if (nextIndex != actorPopupIndex &&
                    nextIndex >= 0 && nextIndex < sceneActors.Length &&
                    sceneActors[nextIndex] != null)
                {
                    selectedActor = sceneActors[nextIndex];
                    Selection.activeGameObject = selectedActor.gameObject;
                    ResolveRuntime();
                }
            }
            else
            {
                GUILayout.Label("当前场景没有角色组件", EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRuntimeHeader()
        {
            BattleAiBlackboard blackboard = runtime.Blackboard;
            int currentNodeId = GetDisplayedNodeId();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("角色", selectedActor.name, GUILayout.Width(260f));
            EditorGUILayout.LabelField("Profile", runtime.EnemyAiProfileId.ToString(), GUILayout.Width(120f));
            EditorGUILayout.LabelField("图", graphAsset != null ? graphAsset.GraphId.ToString() : "未找到", GUILayout.Width(120f));
            EditorGUILayout.LabelField("节点", currentNodeId > 0 ? currentNodeId.ToString() : "—", GUILayout.Width(110f));
            EditorGUILayout.LabelField("Handler", blackboard.CurrentNode.ToString(), GUILayout.Width(190f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "当前计划：" + runtime.CurrentPlan +
                "    高层状态：" + runtime.State +
                "    图节点：" + (runtime.CurrentGraphNodeId > 0 ? "运行中" : "最近访问"),
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawActorIdentityOnly()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("角色身份", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("GameObject", selectedActor.name);
            EditorGUILayout.LabelField("RuntimeUid", selectedActor.RuntimeUid.ToString());
            EditorGUILayout.LabelField("PersistentUid", selectedActor.PersistentUid);
            EditorGUILayout.LabelField("Faction", selectedActor.FactionId.ToString());
            EditorGUILayout.EndVertical();
        }

        private void DrawGraphPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("行为图运行视图", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("空白处中键平移；滚轮缩放；当前节点自动高亮", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (graphAsset == null || graphAsset.Nodes == null || graphAsset.Nodes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "找不到当前 Profile 对应的已发布 BattleAiGraphAsset。运行时仍有图定义，但监视器没有编辑坐标可用。",
                    MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            Rect rect = GUILayoutUtility.GetRect(
                0f,
                CanvasMinHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            lastCanvasSize = rect.size;
            DrawGraphCanvas(rect);
            EditorGUILayout.EndVertical();
        }

        private void DrawGraphCanvas(Rect canvasRect)
        {
            GUI.BeginGroup(canvasRect);
            Rect localRect = new Rect(0f, 0f, canvasRect.width, canvasRect.height);
            GUI.Box(localRect, GUIContent.none, EditorStyles.textArea);
            GUI.BeginClip(localRect);
            DrawGrid(canvasRect.size);

            Handles.BeginGUI();
            for (int index = 0; index < graphAsset.Nodes.Count; index++)
            {
                BattleAiGraphAssetNode parent = graphAsset.Nodes[index];
                if (parent == null || parent.childNodeIds == null)
                {
                    continue;
                }

                for (int childIndex = 0; childIndex < parent.childNodeIds.Count; childIndex++)
                {
                    BattleAiGraphAssetNode child = FindNode(parent.childNodeIds[childIndex]);
                    if (child == null)
                    {
                        continue;
                    }

                    Vector2 start = GetOutputCenter(parent, childIndex);
                    Vector2 end = GetInputCenter(child);
                    Handles.DrawBezier(
                        start,
                        end,
                        start + Vector2.right * 50f * canvasZoom,
                        end + Vector2.left * 50f * canvasZoom,
                        new Color(0.78f, 0.78f, 0.82f),
                        null,
                        2f);
                }
            }
            Handles.EndGUI();

            for (int index = 0; index < graphAsset.Nodes.Count; index++)
            {
                DrawGraphNode(graphAsset.Nodes[index]);
            }

            GUI.EndClip();
            GUI.EndGroup();
            HandleCanvasEvents(canvasRect);
        }

        private void DrawGrid(Vector2 size)
        {
            float spacing = 32f * canvasZoom;
            if (spacing < 8f)
            {
                spacing = 8f;
            }

            Vector2 offset = CanvasToScreen(Vector2.zero);
            Handles.BeginGUI();
            Color previous = Handles.color;
            Handles.color = new Color(0.22f, 0.22f, 0.25f, 0.55f);
            for (float x = Mathf.Repeat(offset.x, spacing); x < size.x; x += spacing)
            {
                Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, size.y));
            }
            for (float y = Mathf.Repeat(offset.y, spacing); y < size.y; y += spacing)
            {
                Handles.DrawLine(new Vector3(0f, y), new Vector3(size.x, y));
            }
            Handles.color = previous;
            Handles.EndGUI();
        }

        private void DrawGraphNode(BattleAiGraphAssetNode node)
        {
            if (node == null)
            {
                return;
            }

            Rect rect = GetNodeRect(node);
            int displayedNodeId = GetDisplayedNodeId();
            bool current = node.nodeId == displayedNodeId;
            bool active = runtime != null && node.nodeId == runtime.CurrentGraphNodeId;
            Color previous = GUI.color;
            GUI.color = active
                ? new Color(0.25f, 0.95f, 0.45f, 1f)
                : current
                    ? new Color(1f, 0.78f, 0.2f, 1f)
                    : new Color(0.8f, 0.8f, 0.84f, 1f);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.color = Color.white;

            string label = node.nodeId + " · " + node.nodeType;
            if (node.nodeType == BattleAiGraphNodeType.Condition)
            {
                label += "\n" + node.conditionType;
            }
            else if (node.nodeType == BattleAiGraphNodeType.Action)
            {
                label += "\n" + node.handlerType;
            }

            GUI.Label(
                new Rect(rect.x + 8f * canvasZoom, rect.y + 7f * canvasZoom,
                    rect.width - 16f * canvasZoom, rect.height - 14f * canvasZoom),
                label,
                EditorStyles.miniLabel);

            GUI.color = current ? new Color(1f, 0.86f, 0.25f) : Color.gray;
            GUI.Box(GetPortRect(GetInputCenter(node)), GUIContent.none, EditorStyles.miniButton);
            int outputCount = GetOutputPortCount(node);
            for (int index = 0; index < outputCount; index++)
            {
                GUI.Box(GetPortRect(GetOutputCenter(node, index)), GUIContent.none, EditorStyles.miniButton);
            }
            GUI.color = previous;
        }

        private void DrawBlackboardPanel()
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.Width(310f),
                GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("运行时黑板", EditorStyles.boldLabel);
            if (runtime == null)
            {
                EditorGUILayout.HelpBox("暂无运行时。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            BattleAiBlackboard bb = runtime.Blackboard;
            EditorGUILayout.LabelField("状态", runtime.State.ToString());
            EditorGUILayout.LabelField("计划", runtime.CurrentPlan.ToString());
            EditorGUILayout.LabelField("当前图节点", runtime.CurrentGraphNodeId > 0
                ? runtime.CurrentGraphNodeId.ToString()
                : "无锁定节点");
            EditorGUILayout.LabelField("最近访问节点", runtime.LastEvaluatedGraphNodeId > 0
                ? runtime.LastEvaluatedGraphNodeId.ToString()
                : "—");
            EditorGUILayout.LabelField("图 Handler", runtime.CurrentGraphHandlerType.ToString());
            EditorGUILayout.LabelField("黑板 Handler", bb.CurrentNode.ToString());
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("目标", string.IsNullOrEmpty(bb.CurrentTargetId) ? "无" : bb.CurrentTargetId);
            EditorGUILayout.LabelField("目标可见", bb.TargetInSight ? "是" : "否");
            EditorGUILayout.LabelField("目标距离", FormatDouble(bb.TargetDistance));
            EditorGUILayout.LabelField("目标最后位置", FormatDouble(bb.TargetLastKnownPosition));
            EditorGUILayout.LabelField("最近受击", bb.WasHitRecently ? "是" : "否");
            EditorGUILayout.LabelField("受击硬直", bb.IsInHitStun ? "是" : "否");
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("已选技能", bb.SelectedSkillId > 0
                ? bb.SelectedSkillId.ToString()
                : "无");
            EditorGUILayout.LabelField("动作状态", bb.CurrentActionStatus.ToString());
            EditorGUILayout.LabelField("动作 Token", bb.ActionRunToken.ToString());
            EditorGUILayout.LabelField("中断窗口", bb.CurrentInterruptWindow.ToString());
            EditorGUILayout.LabelField("上次动作结果", string.IsNullOrEmpty(bb.LastActionResult)
                ? "—"
                : bb.LastActionResult);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("存档说明", "当前行为游标只存在于战斗内，不写入存档。", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private int GetDisplayedNodeId()
        {
            if (runtime == null)
            {
                return 0;
            }

            return runtime.CurrentGraphNodeId > 0
                ? runtime.CurrentGraphNodeId
                : runtime.LastEvaluatedGraphNodeId;
        }

        private BattleAiGraphAssetNode FindNode(int nodeId)
        {
            if (graphAsset == null || graphAsset.Nodes == null)
            {
                return null;
            }

            for (int index = 0; index < graphAsset.Nodes.Count; index++)
            {
                BattleAiGraphAssetNode node = graphAsset.Nodes[index];
                if (node != null && node.nodeId == nodeId)
                {
                    return node;
                }
            }

            return null;
        }

        private static int GetOutputPortCount(BattleAiGraphAssetNode node)
        {
            if (node == null ||
                (node.nodeType != BattleAiGraphNodeType.Selector &&
                 node.nodeType != BattleAiGraphNodeType.Sequence))
            {
                return 0;
            }

            return node.childNodeIds == null ? 0 : node.childNodeIds.Count;
        }

        private Rect GetNodeRect(BattleAiGraphAssetNode node)
        {
            Vector2 position = node.editorPosition + canvasPan;
            Vector2 pivot = lastCanvasSize * 0.5f;
            Vector2 screenPosition = pivot + (position - pivot) * canvasZoom;
            return new Rect(
                screenPosition,
                new Vector2(NodeWidth, NodeHeight) * canvasZoom);
        }

        private Vector2 GetInputCenter(BattleAiGraphAssetNode node)
        {
            Rect rect = GetNodeRect(node);
            return new Vector2(rect.x, rect.y + rect.height * 0.5f);
        }

        private Vector2 GetOutputCenter(BattleAiGraphAssetNode node, int childIndex)
        {
            Rect rect = GetNodeRect(node);
            int count = GetOutputPortCount(node);
            if (count <= 1)
            {
                return new Vector2(rect.xMax, rect.y + rect.height * 0.5f);
            }

            float top = rect.y + 16f * canvasZoom;
            float bottom = rect.yMax - 16f * canvasZoom;
            return new Vector2(
                rect.xMax,
                Mathf.Lerp(top, bottom, Mathf.Clamp01(childIndex / (float)(count - 1))));
        }

        private static Rect GetPortRect(Vector2 center)
        {
            return new Rect(
                center - Vector2.one * (PortSize * 0.5f),
                Vector2.one * PortSize);
        }

        private Vector2 CanvasToScreen(Vector2 point)
        {
            Vector2 pivot = lastCanvasSize * 0.5f;
            return pivot + (point + canvasPan - pivot) * canvasZoom;
        }

        private void HandleCanvasEvents(Rect canvasRect)
        {
            Event current = Event.current;
            bool inside = canvasRect.Contains(current.mousePosition);
            Vector2 localMouse = current.mousePosition - canvasRect.position;

            if (inside && current.type == EventType.ScrollWheel)
            {
                canvasZoom = Mathf.Clamp(
                    canvasZoom * Mathf.Pow(1.1f, -current.delta.y),
                    CanvasMinZoom,
                    CanvasMaxZoom);
                current.Use();
                Repaint();
                return;
            }

            // 和行为图制作器保持一致：空白处左键或任意位置中键都平移画布；
            // 点在节点上时不消费左键，避免阻断节点查看/选择行为。
            bool leftBlankMouseDown = inside &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                !IsNodeAt(localMouse);
            bool middleMouseDown = inside &&
                current.type == EventType.MouseDown &&
                current.button == 2;
            if (leftBlankMouseDown || middleMouseDown)
            {
                canvasHotControl = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = canvasHotControl;
                draggingCanvas = true;
                lastMousePosition = localMouse;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && draggingCanvas)
            {
                canvasPan += (localMouse - lastMousePosition) / Mathf.Max(canvasZoom, 0.0001f);
                lastMousePosition = localMouse;
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseUp && draggingCanvas)
            {
                draggingCanvas = false;
                if (GUIUtility.hotControl == canvasHotControl)
                {
                    GUIUtility.hotControl = 0;
                }
                canvasHotControl = 0;
                current.Use();
            }
        }

        private bool IsNodeAt(Vector2 localMouse)
        {
            if (graphAsset == null || graphAsset.Nodes == null)
            {
                return false;
            }

            for (int index = graphAsset.Nodes.Count - 1; index >= 0; index--)
            {
                BattleAiGraphAssetNode node = graphAsset.Nodes[index];
                if (node != null && GetNodeRect(node).Contains(localMouse))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "—";
            }

            return value.ToString("0.00");
        }
    }
}
#endif
