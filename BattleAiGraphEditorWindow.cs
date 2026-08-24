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
        private const float NodeWidth = 190f;
        private const float NodeHeight = 64f;
        private const float CanvasMinHeight = 420f;
        private const float PortHitSize = 16f;
        private const float PortSpacing = 18f;

        private BattleAiGraphAsset graphAsset;
        private BattleAiGraphAssetNode selectedNode;
        private readonly List<string> validationIssues = new List<string>();
        // 画布平移量只属于编辑器视图，不写入行为图资产。
        private Vector2 canvasPan;
        private int draggingNodeId;
        private Vector2 dragOffset;
        private bool draggingCanvas;
        private int canvasHotControl;
        private int connectingParentNodeId;
        private int connectingChildIndex = -1;
        private Vector2 connectingMousePosition;
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
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "空白处左键/中键平移；右侧端口拖到目标左侧端口",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            Rect canvasRect = GUILayoutUtility.GetRect(
                0f,
                CanvasMinHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
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
            DrawGrid(canvasRect.width, canvasRect.height, canvasPan);
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

                    Vector2 start = GetOutputPortCenter(parent, childIndex, canvasPan);
                    Vector2 end = GetInputPortCenter(child, canvasPan);
                    Handles.DrawBezier(
                        start,
                        end,
                        start + Vector2.right * 50f,
                        end + Vector2.left * 50f,
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
                    Vector2 start = GetOutputPortCenter(
                        parent,
                        connectingChildIndex,
                        canvasPan);
                    Vector2 end = connectingMousePosition;
                    Handles.DrawBezier(
                        start,
                        end,
                        start + Vector2.right * 50f,
                        end + Vector2.left * 50f,
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

            GUI.EndGroup();
            HandleCanvasEvents(canvasRect);
        }

        private void DrawNode(BattleAiGraphAssetNode node, Vector2 pan)
        {
            if (node == null)
            {
                return;
            }

            Rect rect = GetNodeRect(node, pan);
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
            GUI.Box(rect, label, EditorStyles.helpBox);

            Color portColor = selected
                ? new Color(0.3f, 0.9f, 1f)
                : new Color(0.75f, 0.75f, 0.75f);
            GUI.color = portColor;
            GUI.Box(
                GetPortHitRect(GetInputPortCenter(node, pan)),
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
                    GetPortHitRect(GetOutputPortCenter(node, index, pan)),
                    GUIContent.none,
                    EditorStyles.miniButton);
            }

            GUI.color = previous;
        }

        private void HandleCanvasEvents(Rect canvasRect)
        {
            Event current = Event.current;
            Vector2 localMouse = current.mousePosition - canvasRect.position;
            Vector2 worldMouse = localMouse - canvasPan;
            bool mouseInsideCanvas = canvasRect.Contains(current.mousePosition);

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
                    BattleAiGraphAssetNode target = FindNodeAtInputPort(worldMouse);
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
                            worldMouse,
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
                        if (node == null || !new Rect(
                                node.editorPosition,
                                new Vector2(NodeWidth, GetNodeHeight(node))).Contains(worldMouse))
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
                canvasPan += current.delta;
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

        private static float GetNodeHeight(BattleAiGraphAssetNode node)
        {
            int outputPortCount = GetOutputPortCount(node);
            return Mathf.Max(
                NodeHeight,
                outputPortCount <= 1
                    ? NodeHeight
                    : 32f + (outputPortCount - 1) * PortSpacing);
        }

        private static Rect GetNodeRect(
            BattleAiGraphAssetNode node,
            Vector2 pan)
        {
            return new Rect(
                node.editorPosition + pan,
                new Vector2(NodeWidth, GetNodeHeight(node)));
        }

        private static Vector2 GetInputPortCenter(
            BattleAiGraphAssetNode node,
            Vector2 pan)
        {
            Rect rect = GetNodeRect(node, pan);
            return new Vector2(rect.x, rect.y + rect.height * 0.5f);
        }

        private static Vector2 GetOutputPortCenter(
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

        private static Rect GetPortHitRect(Vector2 center)
        {
            return new Rect(
                center - Vector2.one * (PortHitSize * 0.5f),
                Vector2.one * PortHitSize);
        }

        private bool TryGetOutputPortAt(
            Vector2 worldMouse,
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
                    if (!GetPortHitRect(
                            GetOutputPortCenter(candidate, index, Vector2.zero))
                        .Contains(worldMouse))
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

        private BattleAiGraphAssetNode FindNodeAtInputPort(Vector2 worldMouse)
        {
            for (int index = graphAsset.Nodes.Count - 1; index >= 0; index--)
            {
                BattleAiGraphAssetNode node = graphAsset.Nodes[index];
                if (node == null ||
                    !GetPortHitRect(GetInputPortCenter(node, Vector2.zero))
                        .Contains(worldMouse))
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

            if (target == null)
            {
                status = "连接已取消：请把线拖到目标节点左侧输入端口。";
                return;
            }

            if (target == parent)
            {
                status = "连接失败：节点不能引用自身。";
                return;
            }

            if (parent.childNodeIds == null ||
                connectingChildIndex >= parent.childNodeIds.Count)
            {
                status = "连接失败：源节点的子节点槽位不存在。";
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
                editorPosition = new Vector2(
                    90f + (nodes.Count % 4) * 230f,
                    100f + (nodes.Count / 4) * 100f),
            };
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

        private static void DrawGrid(float width, float height, Vector2 pan)
        {
            Color previous = Handles.color;
            Handles.color = new Color(1f, 1f, 1f, 0.05f);
            const float gridSize = 20f;
            float startX = -gridSize + Mathf.Repeat(pan.x, gridSize);
            float startY = -gridSize + Mathf.Repeat(pan.y, gridSize);
            for (float x = startX; x < width; x += gridSize)
            {
                Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, height));
            }

            for (float y = startY; y < height; y += gridSize)
            {
                Handles.DrawLine(new Vector3(0f, y), new Vector3(width, y));
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
