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
        private const float CanvasWidth = 1200f;
        private const float CanvasHeight = 760f;

        private BattleAiGraphAsset graphAsset;
        private BattleAiGraphAssetNode selectedNode;
        private readonly List<string> validationIssues = new List<string>();
        private Vector2 canvasScroll;
        private int draggingNodeId;
        private Vector2 dragOffset;
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

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            canvasScroll = EditorGUILayout.BeginScrollView(
                canvasScroll,
                EditorStyles.helpBox,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            Rect canvasRect = GUILayoutUtility.GetRect(
                CanvasWidth,
                CanvasHeight,
                GUILayout.ExpandWidth(false),
                GUILayout.ExpandHeight(false));
            DrawCanvas(canvasRect);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCanvas(Rect canvasRect)
        {
            GUI.Box(canvasRect, GUIContent.none, EditorStyles.textArea);
            GUI.BeginGroup(canvasRect);
            DrawGrid(canvasRect.width, canvasRect.height);
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

                    Vector2 start = parent.editorPosition +
                        new Vector2(NodeWidth, NodeHeight * 0.5f);
                    Vector2 end = child.editorPosition +
                        new Vector2(0f, NodeHeight * 0.5f);
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

            Handles.EndGUI();
            for (int index = 0; index < graphAsset.Nodes.Count; index++)
            {
                DrawNode(graphAsset.Nodes[index]);
            }

            GUI.EndGroup();
            HandleCanvasEvents(canvasRect);
        }

        private void DrawNode(BattleAiGraphAssetNode node)
        {
            if (node == null)
            {
                return;
            }

            Rect rect = new Rect(
                node.editorPosition,
                new Vector2(NodeWidth, NodeHeight));
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

            if (GUI.Button(rect, label, EditorStyles.helpBox))
            {
                selectedNode = node;
                Repaint();
            }

            GUI.color = previous;
        }

        private void HandleCanvasEvents(Rect canvasRect)
        {
            Event current = Event.current;
            Vector2 localMouse = current.mousePosition - canvasRect.position;
            if (current.type == EventType.MouseDown && current.button == 0)
            {
                for (int index = graphAsset.Nodes.Count - 1; index >= 0; index--)
                {
                    BattleAiGraphAssetNode node = graphAsset.Nodes[index];
                    if (node == null || !new Rect(
                            node.editorPosition,
                            new Vector2(NodeWidth, NodeHeight)).Contains(localMouse))
                    {
                        continue;
                    }

                    selectedNode = node;
                    draggingNodeId = node.nodeId;
                    dragOffset = localMouse - node.editorPosition;
                    Undo.RecordObject(graphAsset, "移动行为图节点");
                    current.Use();
                    Repaint();
                    return;
                }
            }

            if (current.type == EventType.MouseDrag && draggingNodeId > 0)
            {
                BattleAiGraphAssetNode node = FindNode(draggingNodeId);
                if (node != null)
                {
                    node.editorPosition = localMouse - dragOffset;
                    node.editorPosition.x = Mathf.Max(10f, node.editorPosition.x);
                    node.editorPosition.y = Mathf.Max(10f, node.editorPosition.y);
                    graphAsset.ClearPublishedState();
                    EditorUtility.SetDirty(graphAsset);
                    current.Use();
                    Repaint();
                }
            }

            if (current.type == EventType.MouseUp && draggingNodeId > 0)
            {
                draggingNodeId = 0;
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
                EditorGUILayout.LabelField("子节点 ID");
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

            Vector2 position = EditorGUILayout.Vector2Field(
                "编辑坐标",
                selectedNode.editorPosition);
            if (position != selectedNode.editorPosition)
            {
                Undo.RecordObject(graphAsset, "修改行为图节点坐标");
                selectedNode.editorPosition = position;
                graphAsset.ClearPublishedState();
                EditorUtility.SetDirty(graphAsset);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("删除节点"))
            {
                DeleteSelectedNode();
            }

            EditorGUILayout.EndVertical();
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

        private static void DrawGrid(float width, float height)
        {
            Color previous = Handles.color;
            Handles.color = new Color(1f, 1f, 1f, 0.05f);
            for (float x = 0f; x < width; x += 20f)
            {
                Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, height));
            }

            for (float y = 0f; y < height; y += 20f)
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
