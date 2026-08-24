#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 自研行为树图形编辑器第一版：节点拖拽、连线、保存、校验和发布。
    /// Excel 仍只负责 BattleEnemyAiProfile 的基础配置，行为图以独立资产保存。
    /// </summary>
    public sealed class BattleAiGraphEditorWindow : EditorWindow
    {
        // 详情模式中的每个词条会按“.”分成多行；固定宽度留出足够空间显示最长的层级字段。
        private const float NodeWidth = 260f;
        private const float NodeHeight = 64f;
        private const float CanvasMinHeight = 420f;
        private const float PortHitSize = 16f;
        private const float PortSpacing = 18f;
        private const float DetailRowHeight = 17f;
        private const float DetailTextLineHeight = 17f;
        private const float DetailTitleHeight = 30f;
        private const float CanvasMinZoom = 0.4f;
        private const float CanvasMaxZoom = 2.5f;

        private BattleAiGraphAsset graphAsset;
        private BattleAiGraphAssetNode selectedNode;
        private readonly List<string> validationIssues = new List<string>();
        // 画布平移量只属于编辑器视图，不写入行为图资产。
        private Vector2 canvasPan;
        private float canvasZoom = 1f;
        // 记录最近一次绘制的画布尺寸，工具栏添加节点时把新节点放到当前视口中心。
        private Vector2 lastCanvasSize = new Vector2(800f, CanvasMinHeight);
        private int draggingNodeId;
        private Vector2 dragOffset;
        private bool draggingCanvas;
        private int canvasHotControl;
        private int connectingParentNodeId;
        private int connectingChildIndex = -1;
        private Vector2 connectingMousePosition;
        private bool showNodeDetails;
        private string status = "请选择一个 BattleAiGraphAsset。";

        [MenuItem("TryGame/Battle/AI Graph Editor", false, 433)]
        private static void Open()
        {
            BattleAiGraphEditorWindow window =
                GetWindow<BattleAiGraphEditorWindow>();
            window.titleContent = new GUIContent("Battle AI Graph");
            window.minSize = new Vector2(1080f, 680f);
            window.Show();
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (graphAsset == null)
            {
                EditorGUILayout.HelpBox(
                    "创建或选择一个行为图资产。图资产通过 EnemyAiProfileId 关联正式 BattleEnemyAiProfile，" +
                    "不会新增 Excel 行为树表。",
                    MessageType.Info);
                return;
            }

            HandleGlobalEvents();
            DrawAssetHeader();
            EditorGUILayout.BeginHorizontal();
            DrawCanvasPanel();
            DrawInspectorPanel();
            EditorGUILayout.EndHorizontal();
            DrawValidationPanel();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            BattleAiGraphAsset next = (BattleAiGraphAsset)EditorGUILayout.ObjectField(
                graphAsset,
                typeof(BattleAiGraphAsset),
                false,
                GUILayout.Width(270f));
            if (next != graphAsset)
            {
                graphAsset = next;
                selectedNode = null;
                canvasPan = Vector2.zero;
                canvasZoom = 1f;
                draggingNodeId = 0;
                draggingCanvas = false;
                canvasHotControl = 0;
                ResetConnectionState();
                validationIssues.Clear();
                status = graphAsset == null
                    ? "请选择一个 BattleAiGraphAsset。"
                    : "已加载行为图资产。";
            }

            if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                CreateNewAsset();
            }

            using (new EditorGUI.DisabledScope(graphAsset == null))
            {
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    SaveAsset();
                }

                if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    ValidateAsset();
                }

                if (GUILayout.Button("发布", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    PublishAsset();
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(status, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssetHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "GraphId",
                graphAsset.GraphId.ToString(),
                GUILayout.Width(180f));
            EditorGUILayout.LabelField(
                "发布状态",
                graphAsset.IsPublished ? "已发布" : "草稿",
                GUILayout.Width(180f));
            if (graphAsset.IsPublished)
            {
                EditorGUILayout.LabelField(
                    "发布于",
                    graphAsset.PublishedAtUtc,
                    GUILayout.ExpandWidth(true));
            }

            EditorGUILayout.EndHorizontal();

            int profileId = EditorGUILayout.IntField(
                "BattleEnemyAiProfile ID",
                graphAsset.EnemyAiProfileId);
            string profileCodeName = EditorGUILayout.TextField(
                "Profile CodeName（可选）",
                graphAsset.ProfileCodeName);
            if (profileId != graphAsset.EnemyAiProfileId ||
                !string.Equals(
                    profileCodeName,
                    graphAsset.ProfileCodeName,
                    StringComparison.Ordinal))
            {
                Undo.RecordObject(graphAsset, "修改 AI Profile 关联");
                graphAsset.SetProfileReference(profileId, profileCodeName);
                EditorUtility.SetDirty(graphAsset);
                status = "Profile 关联已修改，需要重新校验和发布。";
            }

            int rootNodeId = EditorGUILayout.IntField(
                "根节点 ID",
                graphAsset.RootNodeId);
            if (rootNodeId != graphAsset.RootNodeId)
            {
                Undo.RecordObject(graphAsset, "修改行为图根节点");
                graphAsset.SetRootNodeId(rootNodeId);
                EditorUtility.SetDirty(graphAsset);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCanvasPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加 Selector", GUILayout.Width(105f)))
            {
                AddNode(BattleAiGraphNodeType.Selector);
            }

            if (GUILayout.Button("添加 Sequence", GUILayout.Width(105f)))
            {
                AddNode(BattleAiGraphNodeType.Sequence);
            }

            if (GUILayout.Button("添加 Condition", GUILayout.Width(110f)))
            {
                AddNode(BattleAiGraphNodeType.Condition);
            }

            if (GUILayout.Button("添加 Action", GUILayout.Width(100f)))
            {
                AddNode(BattleAiGraphNodeType.Action);
            }

            if (GUILayout.Button("添加 Wait", GUILayout.Width(85f)))
            {
                AddNode(BattleAiGraphNodeType.Wait);
            }

            if (GUILayout.Button("重置视图", GUILayout.Width(85f)))
            {
                canvasPan = Vector2.zero;
                canvasZoom = 1f;
                Repaint();
            }

            using (new EditorGUI.DisabledScope(graphAsset == null ||
                                               graphAsset.Nodes == null ||
                                               graphAsset.Nodes.Count == 0))
            {
            if (GUILayout.Button("重排节点 ID", GUILayout.Width(100f)))
            {
                RenumberNodeIds();
            }

            if (GUILayout.Button(
                    showNodeDetails ? "简易" : "详情",
                    GUILayout.Width(65f)))
            {
                showNodeDetails = !showNodeDetails;
                Repaint();
            }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "滚轮以工作区中心缩放；空白处左键/中键平移；右侧端口拖到目标左侧端口",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            Rect canvasRect = GUILayoutUtility.GetRect(
                0f,
                CanvasMinHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            lastCanvasSize = canvasRect.size;
            DrawCanvas(canvasRect);
            EditorGUILayout.EndVertical();
        }

        private void DrawCanvas(Rect canvasRect)
        {
            GUI.BeginGroup(canvasRect);
            Rect localCanvasRect = new Rect(
                0f,
                0f,
                canvasRect.width,
                canvasRect.height);
            GUI.Box(localCanvasRect, GUIContent.none, EditorStyles.textArea);
            // 工作区裁剪固定在红框内，缩放只改变节点内容坐标，不改变这个 Clip。
            GUI.BeginClip(localCanvasRect);
            Handles.BeginGUI();
            DrawGrid(
                canvasRect.width,
                canvasRect.height,
                canvasPan,
                canvasZoom);
            Handles.EndGUI();
            Handles.BeginGUI();
            for (int index = 0; index < graphAsset.Nodes.Count; index++)
            {
                BattleAiGraphAssetNode parent = graphAsset.Nodes[index];
                if (parent == null || parent.childNodeIds == null)
                {
                    continue;
                }

                for (int childIndex = 0;
                    childIndex < parent.childNodeIds.Count;
                    childIndex++)
                {
                    BattleAiGraphAssetNode child = FindNode(parent.childNodeIds[childIndex]);
                    if (child == null)
                    {
                        continue;
                    }

                    Vector2 start = GetScreenOutputPortCenter(
                        parent,
                        childIndex,
                        canvasPan);
                    Vector2 end = GetScreenInputPortCenter(child, canvasPan);
                    Handles.DrawBezier(
                        start,
                        end,
                        start + Vector2.right * 50f * GetCanvasContentScale(),
                        end + Vector2.left * 50f * GetCanvasContentScale(),
                        Color.white,
                        null,
                        2f);
                }
            }

            if (connectingParentNodeId > 0)
            {
                BattleAiGraphAssetNode parent = FindNode(connectingParentNodeId);
                if (parent != null && connectingChildIndex >= 0)
                {
                    Vector2 start = GetScreenOutputPortCenter(
                        parent,
                        connectingChildIndex,
                        canvasPan);
                    Vector2 end = connectingMousePosition;
                    Handles.DrawBezier(
                        start,
                        end,
                        start + Vector2.right * 50f * GetCanvasContentScale(),
                        end + Vector2.left * 50f * GetCanvasContentScale(),
                        new Color(0.4f, 0.85f, 1f),
                        null,
                        2f);
                }
            }

            Handles.EndGUI();
            for (int index = 0; index < graphAsset.Nodes.Count; index++)
            {
                DrawNode(graphAsset.Nodes[index], canvasPan);
            }

            GUI.EndClip();
            GUI.EndGroup();
            HandleCanvasEvents(canvasRect);
        }

        private void DrawNode(BattleAiGraphAssetNode node, Vector2 pan)
        {
            if (node == null)
            {
                return;
            }

            Rect rect = GetScreenNodeRect(node, pan);
            float contentScale = GetCanvasContentScale();
            bool selected = selectedNode == node;
            Color previous = GUI.color;
            GUI.color = selected ? new Color(0.35f, 0.75f, 1f) : Color.white;
            string label = node.nodeId + " · " + node.nodeType;
            if (node.nodeType == BattleAiGraphNodeType.Condition)
            {
                label += "\n" + node.conditionType;
            }
            else if (node.nodeType == BattleAiGraphNodeType.Action)
            {
                label += "\n" + node.handlerType;
            }

            // 节点的鼠标事件统一由 HandleCanvasEvents 处理。
            // 这里不能使用 GUI.Button：GUI.Button 会在 MouseDown 阶段先消费事件，
            // 导致后面的画布拖拽逻辑收不到 MouseDown，节点只能点击选中但无法拖动。
            if (showNodeDetails)
            {
                GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
                GUI.Label(
                    new Rect(
                        rect.x + 8f,
                        rect.y + 3f * contentScale,
                        rect.width - 16f,
                        DetailTitleHeight * contentScale - 3f),
                    label,
                    EditorStyles.miniLabel);
                DrawNodeDataDetails(node, rect, contentScale);
            }
            else
            {
                GUI.Box(rect, label, EditorStyles.helpBox);
            }

            Color portColor = selected
                ? new Color(0.3f, 0.9f, 1f)
                : new Color(0.75f, 0.75f, 0.75f);
            GUI.color = portColor;
            GUI.Box(
                GetScreenPortHitRect(GetScreenInputPortCenter(node, pan)),
                GUIContent.none,
                EditorStyles.miniButton);
            int outputPortCount = GetOutputPortCount(node);
            for (int index = 0; index < outputPortCount; index++)
            {
                bool connected = node.childNodeIds[index] > 0;
                GUI.color = connected
                    ? new Color(0.35f, 1f, 0.45f)
                    : new Color(1f, 0.75f, 0.25f);
                GUI.Box(
                    GetScreenPortHitRect(
                        GetScreenOutputPortCenter(node, index, pan)),
                    GUIContent.none,
                    EditorStyles.miniButton);
            }

            GUI.color = previous;
        }

        private void HandleCanvasEvents(Rect canvasRect)
        {
            Event current = Event.current;
            Vector2 localMouse = current.mousePosition - canvasRect.position;
            bool mouseInsideCanvas = canvasRect.Contains(current.mousePosition);
            Vector2 canvasMouse = ScreenToCanvasPoint(
                localMouse,
                canvasRect.size);
            Vector2 worldMouse = canvasMouse - canvasPan;

            if (mouseInsideCanvas && current.type == EventType.ScrollWheel)
            {
                float zoomFactor = Mathf.Pow(1.1f, -current.delta.y);
                canvasZoom = Mathf.Clamp(
                    canvasZoom * zoomFactor,
                    CanvasMinZoom,
                    CanvasMaxZoom);
                // 缩放中心固定在工作区中心。CanvasToScreenPoint 会围绕同一个
                // lastCanvasSize 中心变换，因此这里不再根据鼠标位置修改 canvasPan。
                current.Use();
                Repaint();
                return;
            }

            if (connectingParentNodeId > 0)
            {
                if (current.type == EventType.MouseDrag)
                {
                    connectingMousePosition = localMouse;
                    current.Use();
                    Repaint();
                    return;
                }

                if (current.type == EventType.MouseUp && current.button == 0)
                {
                    BattleAiGraphAssetNode target = FindNodeAtInputPort(localMouse);
                    CompleteConnection(target);
                    if (GUIUtility.hotControl == canvasHotControl)
                    {
                        GUIUtility.hotControl = 0;
                    }

                    ResetConnectionState();
                    canvasHotControl = 0;
                    current.Use();
                    Repaint();
                    return;
                }
            }

            if (mouseInsideCanvas &&
                current.type == EventType.MouseDown &&
                (current.button == 0 || current.button == 2))
            {
                canvasHotControl = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = canvasHotControl;
                if (current.button == 0)
                {
                    if (TryGetOutputPortAt(
                            localMouse,
                            out BattleAiGraphAssetNode parent,
                            out int childIndex))
                    {
                        selectedNode = parent;
                        draggingNodeId = 0;
                        draggingCanvas = false;
                        connectingParentNodeId = parent.nodeId;
                        connectingChildIndex = childIndex;
                        connectingMousePosition = localMouse;
                        current.Use();
                        Repaint();
                        return;
                    }

                    for (int index = graphAsset.Nodes.Count - 1; index >= 0; index--)
                    {
                        BattleAiGraphAssetNode node = graphAsset.Nodes[index];
                        if (node == null || !GetScreenNodeRect(
                                node,
                                canvasPan).Contains(localMouse))
                        {
                            continue;
                        }

                        selectedNode = node;
                        draggingNodeId = node.nodeId;
                        draggingCanvas = false;
                        dragOffset = worldMouse - node.editorPosition;
                        Undo.RecordObject(graphAsset, "移动行为图节点");
                        current.Use();
                        Repaint();
                        return;
                    }
                }

                // 空白处左键和任意位置中键都用于平移视图，不修改节点坐标。
                draggingCanvas = true;
                draggingNodeId = 0;
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseDrag && draggingNodeId > 0)
            {
                BattleAiGraphAssetNode node = FindNode(draggingNodeId);
                if (node != null)
                {
                    // 节点坐标不再限制为正数；画布平移后可把负坐标节点重新拖回视口。
                    node.editorPosition = worldMouse - dragOffset;
                    graphAsset.ClearPublishedState();
                    EditorUtility.SetDirty(graphAsset);
                    current.Use();
                    Repaint();
                }
            }

            if (current.type == EventType.MouseDrag && draggingCanvas)
            {
                // canvasPan 使用未缩放坐标保存，因此拖拽距离要除以当前缩放比例，
                // 保证不同缩放级别下平移手感一致。
                canvasPan += current.delta / canvasZoom;
                current.Use();
                Repaint();
            }

            if (current.type == EventType.MouseUp &&
                (draggingNodeId > 0 || draggingCanvas))
            {
                draggingNodeId = 0;
                draggingCanvas = false;
                if (GUIUtility.hotControl == canvasHotControl)
                {
                    GUIUtility.hotControl = 0;
                }

                canvasHotControl = 0;
                current.Use();
            }
        }

        private void DrawInspectorPanel()
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.Width(330f),
                GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("节点属性", EditorStyles.boldLabel);
            if (selectedNode == null)
            {
                EditorGUILayout.HelpBox("点击左侧节点查看属性。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField("NodeId", selectedNode.nodeId.ToString());
            BattleAiGraphNodeType nodeType = (BattleAiGraphNodeType)EditorGUILayout.EnumPopup(
                "节点类型",
                selectedNode.nodeType);
            if (nodeType != selectedNode.nodeType)
            {
                Undo.RecordObject(graphAsset, "修改行为图节点类型");
                selectedNode.nodeType = nodeType;
                NormalizeNodeReferences(selectedNode);
                graphAsset.ClearPublishedState();
                EditorUtility.SetDirty(graphAsset);
            }

            if (selectedNode.nodeType == BattleAiGraphNodeType.Condition)
            {
                BattleAiConditionType conditionType = (BattleAiConditionType)EditorGUILayout.EnumPopup(
                    "Condition",
                    selectedNode.conditionType);
                if (conditionType != selectedNode.conditionType)
                {
                    Undo.RecordObject(graphAsset, "修改行为图 Condition");
                    selectedNode.conditionType = conditionType;
                    graphAsset.ClearPublishedState();
                    EditorUtility.SetDirty(graphAsset);
                }
            }
            else if (selectedNode.nodeType == BattleAiGraphNodeType.Action)
            {
                BattleAiHandlerType handlerType = (BattleAiHandlerType)EditorGUILayout.EnumPopup(
                    "Handler",
                    selectedNode.handlerType);
                if (handlerType != selectedNode.handlerType)
                {
                    Undo.RecordObject(graphAsset, "修改行为图 Handler");
                    selectedNode.handlerType = handlerType;
                    graphAsset.ClearPublishedState();
                    EditorUtility.SetDirty(graphAsset);
                }
            }

            if (selectedNode.nodeType == BattleAiGraphNodeType.Selector ||
                selectedNode.nodeType == BattleAiGraphNodeType.Sequence)
            {
                EditorGUILayout.LabelField(
                    "子节点 ID（每项对应一个右侧输出端口）");
                selectedNode.childNodeIds ??= new List<int>();
                for (int index = 0; index < selectedNode.childNodeIds.Count; index++)
                {
                    EditorGUILayout.BeginHorizontal();
                    int childId = EditorGUILayout.IntField(
                        selectedNode.childNodeIds[index]);
                    if (childId != selectedNode.childNodeIds[index])
                    {
                        Undo.RecordObject(graphAsset, "修改行为图子节点");
                        selectedNode.childNodeIds[index] = childId;
                        graphAsset.ClearPublishedState();
                        EditorUtility.SetDirty(graphAsset);
                    }

                    if (GUILayout.Button("-", GUILayout.Width(24f)))
                    {
                        Undo.RecordObject(graphAsset, "移除行为图子节点");
                        selectedNode.childNodeIds.RemoveAt(index);
                        graphAsset.ClearPublishedState();
                        EditorUtility.SetDirty(graphAsset);
                        GUIUtility.ExitGUI();
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("添加子节点 ID"))
                {
                    Undo.RecordObject(graphAsset, "添加行为图子节点");
                    selectedNode.childNodeIds.Add(0);
                    graphAsset.ClearPublishedState();
                    EditorUtility.SetDirty(graphAsset);
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("删除节点"))
            {
                DeleteSelectedNode();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>处理不属于某个具体控件的编辑器快捷键。</summary>
        private void HandleGlobalEvents()
        {
            Event current = Event.current;
            if (selectedNode == null ||
                current.type != EventType.KeyDown ||
                EditorGUIUtility.editingTextField ||
                (current.keyCode != KeyCode.Delete &&
                 current.keyCode != KeyCode.Backspace))
            {
                return;
            }

            DeleteSelectedNode();
            current.Use();
            Repaint();
        }

        private static int GetOutputPortCount(BattleAiGraphAssetNode node)
        {
            if (node == null ||
                (node.nodeType != BattleAiGraphNodeType.Selector &&
                 node.nodeType != BattleAiGraphNodeType.Sequence))
            {
                return 0;
            }

            return node.childNodeIds?.Count ?? 0;
        }

        private float GetNodeHeight(BattleAiGraphAssetNode node)
        {
            int outputPortCount = GetOutputPortCount(node);
            float baseHeight = Mathf.Max(
                NodeHeight,
                outputPortCount <= 1
                    ? NodeHeight
                    : 32f + (outputPortCount - 1) * PortSpacing);
            if (!showNodeDetails)
            {
                return baseHeight;
            }

            GetNodeData(node, out List<string> required, out List<string> provided);
            return Mathf.Max(
                baseHeight,
                DetailTitleHeight + 22f + GetDetailRowsHeight(
                    required,
                    provided) + 6f);
        }

        private static float GetDetailRowsHeight(
            IReadOnlyList<string> required,
            IReadOnlyList<string> provided)
        {
            float totalHeight = 0f;
            List<float> rowHeights = GetDetailRowHeights(
                required,
                provided);
            for (int index = 0; index < rowHeights.Count; index++)
            {
                totalHeight += rowHeights[index];
            }

            return totalHeight > 0f ? totalHeight : DetailRowHeight;
        }

        private static List<float> GetDetailRowHeights(
            IReadOnlyList<string> required,
            IReadOnlyList<string> provided)
        {
            int rowCount = Mathf.Max(
                required?.Count ?? 0,
                provided?.Count ?? 0);
            List<float> rowHeights = new List<float>(rowCount);
            for (int index = 0; index < rowCount; index++)
            {
                float requiredHeight = index < (required?.Count ?? 0)
                    ? GetDataRowHeight(required[index])
                    : DetailRowHeight;
                float providedHeight = index < (provided?.Count ?? 0)
                    ? GetDataRowHeight(provided[index])
                    : DetailRowHeight;
                rowHeights.Add(Mathf.Max(requiredHeight, providedHeight));
            }

            return rowHeights;
        }

        private static float GetDataRowHeight(string value)
        {
            // GUIStyle.CalcHeight 对自定义换行在不同 Unity 版本中结果不一致，
            // 这里按字段层级数量明确计算，保证词条框和外层节点高度始终一致。
            int lineCount = string.IsNullOrEmpty(value)
                ? 1
                : value.Split('.').Length;
            float calculatedHeight = lineCount * DetailTextLineHeight + 4f;
            return Mathf.Max(
                DetailRowHeight,
                calculatedHeight);
        }

        private static float GetNodeWidth(BattleAiGraphAssetNode node)
        {
            // 词条现在在固定宽度的框内按层级分行，节点不再为了单行显示字段名而横向扩张。
            return NodeWidth;
        }

        private float GetCanvasContentScale()
        {
            // 工作区本身不缩放，节点、端口、连线和词条内容按实际缩放值缩放。
            // 不能把缩小时的比例强制抬回 1，否则滚轮只能放大、无法真正缩小节点。
            return Mathf.Max(canvasZoom, 0.0001f);
        }

        private Vector2 CanvasToScreenPoint(Vector2 canvasPoint)
        {
            Vector2 pivot = lastCanvasSize * 0.5f;
            return pivot + (canvasPoint - pivot) * canvasZoom;
        }

        private Rect GetNodeRect(
            BattleAiGraphAssetNode node,
            Vector2 pan)
        {
            return new Rect(
                node.editorPosition + pan,
                new Vector2(GetNodeWidth(node), GetNodeHeight(node)));
        }

        private Rect GetScreenNodeRect(
            BattleAiGraphAssetNode node,
            Vector2 pan)
        {
            Rect worldRect = GetNodeRect(node, pan);
            return new Rect(
                CanvasToScreenPoint(worldRect.position),
                worldRect.size * GetCanvasContentScale());
        }

        private Vector2 GetInputPortCenter(
            BattleAiGraphAssetNode node,
            Vector2 pan)
        {
            Rect rect = GetNodeRect(node, pan);
            return new Vector2(rect.x, rect.y + rect.height * 0.5f);
        }

        private Vector2 GetOutputPortCenter(
            BattleAiGraphAssetNode node,
            int childIndex,
            Vector2 pan)
        {
            Rect rect = GetNodeRect(node, pan);
            int outputPortCount = GetOutputPortCount(node);
            if (outputPortCount <= 1)
            {
                return new Vector2(rect.xMax, rect.y + rect.height * 0.5f);
            }

            float top = rect.y + 16f;
            float bottom = rect.yMax - 16f;
            float normalizedIndex = Mathf.Clamp01(
                childIndex / (float)(outputPortCount - 1));
            return new Vector2(
                rect.xMax,
                Mathf.Lerp(top, bottom, normalizedIndex));
        }

        private Vector2 GetScreenInputPortCenter(
            BattleAiGraphAssetNode node,
            Vector2 pan)
        {
            Rect rect = GetScreenNodeRect(node, pan);
            return new Vector2(rect.x, rect.y + rect.height * 0.5f);
        }

        private Vector2 GetScreenOutputPortCenter(
            BattleAiGraphAssetNode node,
            int childIndex,
            Vector2 pan)
        {
            Rect rect = GetScreenNodeRect(node, pan);
            int outputPortCount = GetOutputPortCount(node);
            if (outputPortCount <= 1)
            {
                return new Vector2(rect.xMax, rect.y + rect.height * 0.5f);
            }

            float top = rect.y + 16f * GetCanvasContentScale();
            float bottom = rect.yMax - 16f * GetCanvasContentScale();
            float normalizedIndex = Mathf.Clamp01(
                childIndex / (float)(outputPortCount - 1));
            return new Vector2(
                rect.xMax,
                Mathf.Lerp(top, bottom, normalizedIndex));
        }

        private static Rect GetPortHitRect(Vector2 center)
        {
            return new Rect(
                center - Vector2.one * (PortHitSize * 0.5f),
                Vector2.one * PortHitSize);
        }

        private Rect GetScreenPortHitRect(Vector2 center)
        {
            float size = PortHitSize * GetCanvasContentScale();
            return new Rect(
                center - Vector2.one * (size * 0.5f),
                Vector2.one * size);
        }

        private Vector2 ScreenToCanvasPoint(
            Vector2 localMouse,
            Vector2 canvasSize)
        {
            Vector2 pivot = canvasSize * 0.5f;
            return (localMouse - pivot) / canvasZoom + pivot;
        }

        /// <summary>
        /// 详情模式绘制节点的数据需求和数据提供。左列显示输入，
        /// 右列显示该节点成功后可写入共享上下文的数据。
        /// </summary>
        private void DrawNodeDataDetails(
            BattleAiGraphAssetNode node,
            Rect nodeRect,
            float contentScale)
        {
            GetNodeData(
                node,
                out List<string> required,
                out List<string> provided);
            HashSet<string> available = GetAvailableDataForNode(node.nodeId);
            bool requirementsSatisfied = AreRequirementsSatisfied(
                required,
                available);
            float columnWidth = (nodeRect.width - 18f * contentScale) * 0.5f;
            float leftX = nodeRect.x + 6f * contentScale;
            float rightX = leftX + columnWidth + 6f * contentScale;
            float headerY = nodeRect.y + DetailTitleHeight * contentScale;
            float rowsY = headerY + 17f * contentScale;
            List<float> rowHeights = GetDetailRowHeights(
                required,
                provided);
            for (int index = 0; index < rowHeights.Count; index++)
            {
                rowHeights[index] *= contentScale;
            }

            GUI.Label(
                new Rect(leftX, headerY, columnWidth, 16f * contentScale),
                "需要",
                EditorStyles.miniBoldLabel);
            GUI.Label(
                new Rect(rightX, headerY, columnWidth, 16f * contentScale),
                "提供",
                EditorStyles.miniBoldLabel);

            DrawDataRows(
                required,
                available,
                leftX,
                rowsY,
                columnWidth,
                true,
                requirementsSatisfied,
                rowHeights);
            DrawDataRows(
                provided,
                available,
                rightX,
                rowsY,
                columnWidth,
                false,
                requirementsSatisfied,
                rowHeights);
        }

        private static void DrawDataRows(
            IReadOnlyList<string> values,
            ISet<string> available,
            float x,
            float y,
            float width,
            bool isRequired,
            bool requirementsSatisfied,
            IReadOnlyList<float> rowHeights)
        {
            if (values == null || values.Count == 0)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.45f);
                GUI.Box(
                    new Rect(x, y, width, DetailRowHeight - 1f),
                    "—",
                    GetEmptyDataRowStyle());
                GUI.color = Color.white;
                return;
            }

            for (int index = 0; index < values.Count; index++)
            {
                string value = values[index] ?? string.Empty;
                bool satisfied = isRequired
                    ? available.Contains(value)
                    : requirementsSatisfied;
                GUI.color = satisfied
                    ? new Color(0.35f, 0.95f, 0.45f, 0.85f)
                    : new Color(1f, 0.35f, 0.35f, 0.9f);
                GUIContent content = new GUIContent(
                    FormatDataValue(satisfied ? "✓ " : "× ", value),
                    value);
                float rowY = y;
                for (int previousIndex = 0;
                    previousIndex < index && previousIndex < rowHeights.Count;
                    previousIndex++)
                {
                    rowY += rowHeights[previousIndex];
                }

                float rowHeight = index < rowHeights.Count
                    ? rowHeights[index]
                    : GetDataRowHeight(value);
                Rect rowRect = new Rect(
                    x,
                    rowY,
                    width,
                    rowHeight - 1f);
                // 背景和文字分开绘制。miniButton 自带的单行文本布局会裁剪
                // 第二行字段名，即使外层 Rect 已经足够高也无法正确显示。
                GUI.Box(
                    rowRect,
                    GUIContent.none,
                    GetDataRowStyle());
                GUI.Label(
                    new Rect(
                        rowRect.x + 4f,
                        rowRect.y + 2f,
                        rowRect.width - 6f,
                        rowRect.height - 4f),
                    content,
                    GetDataRowLabelStyle());
            }

            GUI.color = Color.white;
        }

        private static string FormatDataValue(string prefix, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return prefix;
            }

            // 一个字段仍然只占一个词条框，但字段的层级部分各占一行，
            // 避免长字段在“需要/提供”两列之间横向溢出。
            return prefix + value.Replace(".", "\n");
        }

        private static GUIStyle GetDataRowStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniButton)
            {
                clipping = TextClipping.Clip,
                fixedHeight = 0f,
                stretchHeight = true,
                padding = new RectOffset(0, 0, 0, 0),
            };
            return style;
        }

        private static GUIStyle GetDataRowLabelStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                wordWrap = false,
                fixedHeight = 0f,
                stretchHeight = true,
                padding = new RectOffset(0, 0, 0, 0),
            };
            return style;
        }

        private static GUIStyle GetEmptyDataRowStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            return style;
        }

        /// <summary>
        /// 返回节点的输入/输出数据键。数据键是编辑器诊断用的稳定名称，
        /// 运行时仍由 Blackboard 和 CombatPlanState 保存实际对象。
        /// </summary>
        private static void GetNodeData(
            BattleAiGraphAssetNode node,
            out List<string> required,
            out List<string> provided)
        {
            required = new List<string>();
            provided = new List<string>();
            if (node == null)
            {
                return;
            }

            switch (node.nodeType)
            {
                case BattleAiGraphNodeType.Selector:
                case BattleAiGraphNodeType.Sequence:
                    provided.Add("ControlFlow");
                    return;
                case BattleAiGraphNodeType.Condition:
                    AddConditionData(node.conditionType, required, provided);
                    return;
                case BattleAiGraphNodeType.Action:
                    AddHandlerData(node.handlerType, required, provided);
                    return;
                case BattleAiGraphNodeType.Wait:
                    provided.Add("WaitCompleted");
                    return;
            }
        }

        private static void AddConditionData(
            BattleAiConditionType type,
            ICollection<string> required,
            ICollection<string> provided)
        {
            switch (type)
            {
                case BattleAiConditionType.IsAlive:
                    required.Add("SelfAlive");
                    provided.Add("Alive");
                    break;
                case BattleAiConditionType.CurrentActionUninterruptible:
                    required.Add("CurrentAction");
                    provided.Add("ActionLocked");
                    break;
                case BattleAiConditionType.TargetValid:
                    required.Add("CurrentTarget");
                    provided.Add("TargetConfirmed");
                    break;
                case BattleAiConditionType.TargetVisible:
                    required.Add("TargetInSight");
                    provided.Add("TargetConfirmed");
                    break;
                case BattleAiConditionType.RecentHitPending:
                    required.Add("HitEvent");
                    provided.Add("HitPosition");
                    break;
                case BattleAiConditionType.SearchActive:
                    required.Add("SearchState");
                    provided.Add("SearchTarget");
                    break;
                case BattleAiConditionType.ReturnHomeRequired:
                    required.Add("HomePosition");
                    provided.Add("ReturnHome");
                    break;
                case BattleAiConditionType.HasPatrolRoute:
                    required.Add("PatrolRoute");
                    provided.Add("PatrolReady");
                    break;
                case BattleAiConditionType.IsAtHome:
                    required.Add("HomePosition");
                    provided.Add("AtHome");
                    break;
                case BattleAiConditionType.HasUsableSkill:
                    required.Add("SkillRuntimes");
                    provided.Add("UsableSkill");
                    break;
                default:
                    required.Add("ConditionContext");
                    provided.Add("ConditionResult");
                    break;
            }
        }

        private static void AddHandlerData(
            BattleAiHandlerType type,
            ICollection<string> required,
            ICollection<string> provided)
        {
            switch (type)
            {
                case BattleAiHandlerType.Idle:
                    provided.Add("IdleState");
                    break;
                case BattleAiHandlerType.Patrol:
                    required.Add("PatrolRoute");
                    provided.Add("PatrolMovement");
                    break;
                case BattleAiHandlerType.Wait:
                    provided.Add("WaitState");
                    break;
                case BattleAiHandlerType.SearchTarget:
                    required.Add("LastKnownPosition");
                    provided.Add("TargetSearchMovement");
                    break;
                case BattleAiHandlerType.MoveToLastHitPosition:
                    required.Add("LastHitPosition");
                    provided.Add("InvestigatePosition");
                    break;
                case BattleAiHandlerType.ReturnHome:
                    required.Add("HomePosition");
                    provided.Add("HomeMovement");
                    break;
                case BattleAiHandlerType.SelectSkill:
                    required.Add("CurrentTarget");
                    required.Add("SkillRuntimes");
                    // 与 BattleAiCombatPlanState 的实际字段一一对应，
                    // 不能把多个运行时字段合并成一个概念名称。
                    provided.Add("PlanState.SelectedBinding");
                    provided.Add("PlanState.HasSelectedBinding");
                    provided.Add("PlanState.SelectedMinimumRange");
                    provided.Add("PlanState.SelectedMaximumRange");
                    break;
                case BattleAiHandlerType.MoveToSkillRange:
                    required.Add("PlanState.SelectedBinding");
                    required.Add("PlanState.HasSelectedBinding");
                    required.Add("PlanState.SelectedMinimumRange");
                    required.Add("PlanState.SelectedMaximumRange");
                    provided.Add("InSkillRange");
                    break;
                case BattleAiHandlerType.ExecuteSkill:
                    // ExecuteSkill 实际读取的是 SelectSkill 写入的 CombatPlanState，
                    // 不能继续使用旧的 SelectedSkill 概念名，否则详情模式会误报缺少数据。
                    required.Add("PlanState.SelectedBinding");
                    required.Add("PlanState.HasSelectedBinding");
                    required.Add("InSkillRange");
                    provided.Add("SkillExecution");
                    break;
                case BattleAiHandlerType.AttackFinish:
                    required.Add("SkillExecution");
                    provided.Add("AttackFinished");
                    break;
                case BattleAiHandlerType.MoveToSafePosition:
                    required.Add("PlanState.SelectedBinding");
                    required.Add("PlanState.HasSelectedBinding");
                    provided.Add("SafePosition");
                    break;
                default:
                    required.Add("HandlerContext");
                    provided.Add("HandlerResult");
                    break;
            }
        }

        private static bool AreRequirementsSatisfied(
            IReadOnlyList<string> required,
            ISet<string> available)
        {
            if (required == null)
            {
                return true;
            }

            for (int index = 0; index < required.Count; index++)
            {
                if (!available.Contains(required[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private HashSet<string> GetAvailableDataForNode(int nodeId)
        {
            HashSet<string> available = new HashSet<string>(StringComparer.Ordinal)
            {
                "TargetInSight",
                "CurrentTarget",
                "SelfAlive",
                "SkillRuntimes",
                "HomePosition",
                "PatrolRoute",
                "SearchState",
                "LastKnownPosition",
                "LastHitPosition",
                "TargetPosition",
                "CurrentAction",
                "HitEvent",
            };
            AddAvailableDataFromParents(
                nodeId,
                available,
                new HashSet<int>());
            return available;
        }

        private void AddAvailableDataFromParents(
            int nodeId,
            ISet<string> available,
            ISet<int> visiting)
        {
            if (!visiting.Add(nodeId) || graphAsset == null ||
                graphAsset.Nodes == null)
            {
                return;
            }

            for (int parentIndex = 0;
                parentIndex < graphAsset.Nodes.Count;
                parentIndex++)
            {
                BattleAiGraphAssetNode parent = graphAsset.Nodes[parentIndex];
                if (parent == null || parent.childNodeIds == null)
                {
                    continue;
                }

                int childIndex = parent.childNodeIds.IndexOf(nodeId);
                if (childIndex < 0)
                {
                    continue;
                }

                AddAvailableDataFromParents(parent.nodeId, available, visiting);
                for (int siblingIndex = 0;
                    siblingIndex < childIndex;
                    siblingIndex++)
                {
                    BattleAiGraphAssetNode sibling = FindNode(
                        parent.childNodeIds[siblingIndex]);
                    if (sibling == null)
                    {
                        continue;
                    }

                    GetNodeData(
                        sibling,
                        out List<string> siblingRequired,
                        out List<string> siblingProvided);
                    if (AreRequirementsSatisfied(siblingRequired, available))
                    {
                        for (int dataIndex = 0;
                            dataIndex < siblingProvided.Count;
                            dataIndex++)
                        {
                            available.Add(siblingProvided[dataIndex]);
                        }
                    }
                }
            }

            visiting.Remove(nodeId);
        }

        private bool TryGetOutputPortAt(
            Vector2 screenMouse,
            out BattleAiGraphAssetNode parent,
            out int childIndex)
        {
            for (int nodeIndex = graphAsset.Nodes.Count - 1;
                nodeIndex >= 0;
                nodeIndex--)
            {
                BattleAiGraphAssetNode candidate = graphAsset.Nodes[nodeIndex];
                int outputPortCount = GetOutputPortCount(candidate);
                for (int index = outputPortCount - 1; index >= 0; index--)
                {
                    if (!GetScreenPortHitRect(
                            GetScreenOutputPortCenter(
                                candidate,
                                index,
                                canvasPan))
                        .Contains(screenMouse))
                    {
                        continue;
                    }

                    parent = candidate;
                    childIndex = index;
                    return true;
                }
            }

            parent = null;
            childIndex = -1;
            return false;
        }

        private BattleAiGraphAssetNode FindNodeAtInputPort(Vector2 screenMouse)
        {
            for (int index = graphAsset.Nodes.Count - 1; index >= 0; index--)
            {
                BattleAiGraphAssetNode node = graphAsset.Nodes[index];
                if (node == null ||
                    !GetScreenPortHitRect(
                            GetScreenInputPortCenter(node, canvasPan))
                        .Contains(screenMouse))
                {
                    continue;
                }

                return node;
            }

            return null;
        }

        private void CompleteConnection(BattleAiGraphAssetNode target)
        {
            BattleAiGraphAssetNode parent = FindNode(connectingParentNodeId);
            if (parent == null || connectingChildIndex < 0)
            {
                status = "连接失败：源节点不存在。";
                return;
            }

            if (parent.childNodeIds == null ||
                connectingChildIndex >= parent.childNodeIds.Count)
            {
                status = "连接失败：源节点的子节点槽位不存在。";
                return;
            }

            if (target == null)
            {
                // 从已有输出端口拖到空白处：清除该槽位的旧引用。
                // 未连接的空槽位不产生无意义的资产修改。
                if (parent.childNodeIds[connectingChildIndex] <= 0)
                {
                    status = "连接已取消：该输出端口当前没有连接。";
                    return;
                }

                Undo.RecordObject(graphAsset, "断开行为图节点连接");
                parent.childNodeIds[connectingChildIndex] = 0;
                graphAsset.ClearPublishedState();
                EditorUtility.SetDirty(graphAsset);
                status = "已断开：节点 " + parent.nodeId +
                    " 的第 " + (connectingChildIndex + 1) + " 个输出端口。";
                return;
            }

            if (target == parent)
            {
                status = "连接失败：节点不能引用自身。";
                return;
            }

            Undo.RecordObject(graphAsset, "连接行为图节点");
            parent.childNodeIds[connectingChildIndex] = target.nodeId;
            graphAsset.ClearPublishedState();
            EditorUtility.SetDirty(graphAsset);
            status = "已连接：" + parent.nodeId + " → " + target.nodeId + "。";
        }

        private void ResetConnectionState()
        {
            connectingParentNodeId = 0;
            connectingChildIndex = -1;
            connectingMousePosition = Vector2.zero;
        }

        private void DrawValidationPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(130f));
            EditorGUILayout.LabelField("校验结果", EditorStyles.boldLabel);
            if (validationIssues.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    graphAsset != null && graphAsset.IsPublished
                        ? "当前资产已发布且签名有效。"
                        : "尚未发现校验错误。点击“发布”生成可运行版本。",
                    MessageType.Info);
            }
            else
            {
                for (int index = 0; index < validationIssues.Count; index++)
                {
                    EditorGUILayout.LabelField("• " + validationIssues[index]);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void CreateNewAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "创建 Battle AI Graph",
                "BattleAiGraph",
                "asset",
                "选择行为图资产保存位置。");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            BattleAiGraphAsset asset = CreateInstance<BattleAiGraphAsset>();
            asset.InitializeNew(FindNextGraphId(), 0);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            graphAsset = asset;
            selectedNode = FindNode(asset.RootNodeId);
            canvasPan = Vector2.zero;
            canvasZoom = 1f;
            draggingNodeId = 0;
            draggingCanvas = false;
            canvasHotControl = 0;
            ResetConnectionState();
            validationIssues.Clear();
            status = "已创建草稿行为图。";
            Selection.activeObject = asset;
        }

        private void SaveAsset()
        {
            if (graphAsset == null)
            {
                return;
            }

            EditorUtility.SetDirty(graphAsset);
            AssetDatabase.SaveAssets();
            status = "行为图草稿已保存。";
        }

        private void ValidateAsset()
        {
            validationIssues.Clear();
            bool valid = graphAsset != null && graphAsset.Validate(validationIssues);
            status = valid
                ? "校验通过。"
                : "校验失败：" + validationIssues.Count + " 项。";
            Repaint();
        }

        private void PublishAsset()
        {
            ValidateAsset();
            if (validationIssues.Count > 0 || graphAsset == null)
            {
                return;
            }

            if (HasDuplicatePublishedProfile(graphAsset))
            {
                validationIssues.Add(
                    "已有其它已发布行为图关联同一个 BattleEnemyAiProfile ID：" +
                    graphAsset.EnemyAiProfileId + "。");
                status = "发布失败：Profile 关联重复。";
                return;
            }

            if (!graphAsset.TryPublish(out string error))
            {
                validationIssues.Add(error);
                status = "发布失败。";
                return;
            }

            EditorUtility.SetDirty(graphAsset);
            AssetDatabase.SaveAssets();
            status = "发布成功：运行时将只接受这份已校验签名。";
        }

        private bool HasDuplicatePublishedProfile(BattleAiGraphAsset current)
        {
            string[] guids = AssetDatabase.FindAssets("t:BattleAiGraphAsset");
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                BattleAiGraphAsset other = AssetDatabase.LoadAssetAtPath<BattleAiGraphAsset>(path);
                if (other == null || other == current || !other.IsPublished ||
                    other.EnemyAiProfileId <= 0)
                {
                    continue;
                }

                if (other.EnemyAiProfileId == current.EnemyAiProfileId)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddNode(BattleAiGraphNodeType nodeType)
        {
            if (graphAsset == null)
            {
                return;
            }

            Undo.RecordObject(graphAsset, "添加行为图节点");
            List<BattleAiGraphAssetNode> nodes = graphAsset.GetEditableNodes();
            int nextId = 1;
            for (int index = 0; index < nodes.Count; index++)
            {
                nextId = Mathf.Max(nextId, (nodes[index]?.nodeId ?? 0) + 1);
            }

            BattleAiGraphAssetNode node = new BattleAiGraphAssetNode
            {
                nodeId = nextId,
                nodeType = nodeType,
            };
            Vector2 canvasCenterInWorld = lastCanvasSize * 0.5f - canvasPan;
            node.editorPosition = canvasCenterInWorld -
                new Vector2(
                    GetNodeWidth(node),
                    GetNodeHeight(node)) * 0.5f;
            nodes.Add(node);
            graphAsset.ClearPublishedState();
            EditorUtility.SetDirty(graphAsset);
            selectedNode = node;
            status = "已添加节点 " + nextId + "，请配置连线和引用。";
        }

        private void DeleteSelectedNode()
        {
            if (graphAsset == null || selectedNode == null)
            {
                return;
            }

            Undo.RecordObject(graphAsset, "删除行为图节点");
            List<BattleAiGraphAssetNode> nodes = graphAsset.GetEditableNodes();
            int deletedId = selectedNode.nodeId;
            nodes.Remove(selectedNode);
            for (int index = 0; index < nodes.Count; index++)
            {
                BattleAiGraphAssetNode node = nodes[index];
                node.childNodeIds?.RemoveAll(id => id == deletedId);
            }

            selectedNode = null;
            graphAsset.ClearPublishedState();
            EditorUtility.SetDirty(graphAsset);
            status = "已删除节点 " + deletedId + "。";
        }

        /// <summary>
        /// 按资产节点列表顺序把节点 ID 重排为 1..N，并同步修正所有引用。
        /// 节点列表顺序就是当前编辑器资产中的顺序，不改变节点对象和编辑坐标。
        /// </summary>
        private void RenumberNodeIds()
        {
            if (graphAsset == null || graphAsset.Nodes == null ||
                graphAsset.Nodes.Count == 0)
            {
                return;
            }

            List<BattleAiGraphAssetNode> nodes = graphAsset.GetEditableNodes();
            Dictionary<int, int> oldToNew = new Dictionary<int, int>();
            int validNodeCount = 0;
            for (int index = 0; index < nodes.Count; index++)
            {
                BattleAiGraphAssetNode node = nodes[index];
                if (node == null)
                {
                    continue;
                }

                int newId = ++validNodeCount;
                if (node.nodeId > 0 && !oldToNew.ContainsKey(node.nodeId))
                {
                    oldToNew.Add(node.nodeId, newId);
                }
            }

            Undo.RecordObject(graphAsset, "重排行为图节点 ID");
            int oldRootNodeId = graphAsset.RootNodeId;

            // 先把节点 ID 改成临时负数，避免旧 ID 与新 ID 重叠时互相覆盖映射。
            int nextNewId = 1;
            for (int index = 0; index < nodes.Count; index++)
            {
                BattleAiGraphAssetNode node = nodes[index];
                if (node != null)
                {
                    node.nodeId = -nextNewId++;
                }
            }

            nextNewId = 1;
            for (int index = 0; index < nodes.Count; index++)
            {
                BattleAiGraphAssetNode node = nodes[index];
                if (node == null)
                {
                    continue;
                }

                node.nodeId = nextNewId++;
                if (node.childNodeIds == null)
                {
                    continue;
                }

                for (int childIndex = 0;
                    childIndex < node.childNodeIds.Count;
                    childIndex++)
                {
                    int oldChildId = node.childNodeIds[childIndex];
                    node.childNodeIds[childIndex] = oldToNew.TryGetValue(
                        oldChildId,
                        out int newChildId)
                        ? newChildId
                        : 0;
                }
            }

            int newRootNodeId = oldToNew.TryGetValue(
                oldRootNodeId,
                out int mappedRootNodeId)
                ? mappedRootNodeId
                : (validNodeCount > 0 ? 1 : 0);
            graphAsset.SetRootNodeId(newRootNodeId);
            graphAsset.ClearPublishedState();
            EditorUtility.SetDirty(graphAsset);
            status = "节点 ID 已按列表顺序重排为 1～" + validNodeCount +
                "，引用已同步。";
            Repaint();
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

        private static void NormalizeNodeReferences(BattleAiGraphAssetNode node)
        {
            if (node == null)
            {
                return;
            }

            if (node.nodeType != BattleAiGraphNodeType.Selector &&
                node.nodeType != BattleAiGraphNodeType.Sequence)
            {
                node.childNodeIds?.Clear();
            }

            if (node.nodeType != BattleAiGraphNodeType.Condition)
            {
                node.conditionType = BattleAiConditionType.None;
            }

            if (node.nodeType != BattleAiGraphNodeType.Action)
            {
                node.handlerType = BattleAiHandlerType.None;
            }
        }

        private static void DrawGrid(
            float width,
            float height,
            Vector2 pan,
            float zoom)
        {
            Color previous = Handles.color;
            Handles.color = new Color(1f, 1f, 1f, 0.05f);
            const float gridSize = 20f;
            float safeZoom = Mathf.Max(zoom, 0.0001f);
            Vector2 pivot = new Vector2(width, height) * 0.5f;
            // 网格直接按红框视口绘制，不使用节点内容的 GUI 缩放矩阵。
            // 先反算视口对应的世界范围，再把每条世界网格线转换回屏幕坐标，
            // 因此缩小时外围仍有网格，放大时也不会越过画布边界。
            float visibleMinWorldX =
                (0f - pivot.x) / safeZoom + pivot.x - pan.x;
            float visibleMaxWorldX =
                (width - pivot.x) / safeZoom + pivot.x - pan.x;
            float visibleMinWorldY =
                (0f - pivot.y) / safeZoom + pivot.y - pan.y;
            float visibleMaxWorldY =
                (height - pivot.y) / safeZoom + pivot.y - pan.y;
            float startWorldX = Mathf.Floor(visibleMinWorldX / gridSize) *
                gridSize;
            float startWorldY = Mathf.Floor(visibleMinWorldY / gridSize) *
                gridSize;
            for (float worldX = startWorldX;
                worldX <= visibleMaxWorldX;
                worldX += gridSize)
            {
                float x = pivot.x +
                    (worldX + pan.x - pivot.x) * safeZoom;
                Handles.DrawLine(
                    new Vector3(x, 0f),
                    new Vector3(x, height));
            }

            for (float worldY = startWorldY;
                worldY <= visibleMaxWorldY;
                worldY += gridSize)
            {
                float y = pivot.y +
                    (worldY + pan.y - pivot.y) * safeZoom;
                Handles.DrawLine(
                    new Vector3(0f, y),
                    new Vector3(width, y));
            }

            Handles.color = previous;
        }

        private int FindNextGraphId()
        {
            int nextId = 1;
            string[] guids = AssetDatabase.FindAssets("t:BattleAiGraphAsset");
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                BattleAiGraphAsset asset = AssetDatabase.LoadAssetAtPath<BattleAiGraphAsset>(path);
                if (asset != null)
                {
                    nextId = Mathf.Max(nextId, asset.GraphId + 1);
                }
            }

            return nextId;
        }
    }
}
#endif
