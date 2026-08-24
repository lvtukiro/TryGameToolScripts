#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.SkeletonAnimation
{
    public sealed class SkeletonAnimationEditorWindow : EditorWindow
    {
        private const string DefaultFolder =
            "Assets/TryGameToolScripts/SkeletonAnimationTool";
        private const float InspectorWidth = 360f;
        private const float LeftPanelWidth = 300f;
        private const float BonePickRadius = 12f;
        private const float SocketPickRadius = 12f;
        private const float DefaultAutoSelectThreshold = 0.06f;
        private const int DefaultAutoSelectMinGap = 2;
        private const int DefaultExtractFrameRate = 12;

        private enum SkeletonDisplayMode
        {
            All = 0,
            BonesOnly = 1,
            SocketsOnly = 2,
        }

        private readonly ISkeletonRecognitionEngine recognitionEngine =
            new HeuristicSkeletonRecognitionEngine();

        private SkeletonTemplateDocument templateDocument;
        private SkeletonAnimationDocument animationDocument;
        private Texture2D sourceImage;
        private Texture2D actionFramePreviewTexture;
        private string sourceAssetPath = string.Empty;
        private string actionSourcePath = string.Empty;
        private string actionFramePreviewPath = string.Empty;
        private string selectedBoneId = string.Empty;
        private string selectedSocketId = string.Empty;
        private string status = "尚未创建骨骼模板。";
        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private Vector2 warningsScroll;
        private readonly List<string> warnings = new List<string>();
        private bool draggingBone;
        private string draggingBoneId = string.Empty;
        private bool draggingSocket;
        private string draggingSocketId = string.Empty;
        private int currentFrame;
        private float previewFrameRate = 30f;
        private int extractFrameRate = DefaultExtractFrameRate;
        private float autoSelectThreshold = DefaultAutoSelectThreshold;
        private int autoSelectMinGap = DefaultAutoSelectMinGap;
        private float bodyFitWidthScale = 0.68f;
        private float bodyFitHeightScale = 0.96f;
        private float bodyFitOffsetX;
        private float bodyFitOffsetY;
        private SkeletonDisplayMode displayMode = SkeletonDisplayMode.All;

        [MenuItem("TryGame/Tools/Skeleton Animation Tool")]
        public static void Open()
        {
            SkeletonAnimationEditorWindow window =
                GetWindow<SkeletonAnimationEditorWindow>("骨骼动画工具");
            window.minSize = new Vector2(1180f, 720f);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureDocuments();
        }

        private void OnDisable()
        {
            if (actionFramePreviewTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(actionFramePreviewTexture);
                actionFramePreviewTexture = null;
            }

            TryCleanupTempActionFrames();
        }

        private void OnGUI()
        {
            EnsureDocuments();
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawCanvasPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();

            HandleKeyboard(Event.current);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("TryGame 骨骼动画工具", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(status, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LeftPanelWidth));
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

            EditorGUILayout.LabelField("导入", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            sourceImage = (Texture2D)EditorGUILayout.ObjectField(
                "参考图",
                sourceImage,
                typeof(Texture2D),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                sourceAssetPath = sourceImage == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(sourceImage);
                if (templateDocument != null)
                {
                    templateDocument.sourceAssetPath = sourceAssetPath;
                }
            }

            using (new EditorGUI.DisabledScope(sourceImage == null))
            {
                if (GUILayout.Button("自动识别骨骼草稿"))
                {
                    RecognizeDraft();
                }
            }

            if (GUILayout.Button("新建空模板"))
            {
                templateDocument = new SkeletonTemplateDocument();
                animationDocument = new SkeletonAnimationDocument
                {
                    templateId = templateDocument.templateId,
                    frameRate = previewFrameRate,
                };
                selectedBoneId = string.Empty;
                selectedSocketId = string.Empty;
                warnings.Clear();
                status = "已新建空模板。";
            }

            if (GUILayout.Button("生成人形机器人默认模板"))
            {
                sourceImage = sourceImage == null ? null : sourceImage;
                RecognizeDraft();
            }

            DrawSkeletonTransformTools();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("模板文件", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("读取模板"))
            {
                LoadTemplate();
            }

            if (GUILayout.Button("保存模板"))
            {
                SaveTemplate();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("动作参考", EditorStyles.boldLabel);
            extractFrameRate = EditorGUILayout.IntSlider(
                "动画拆帧 FPS",
                extractFrameRate,
                1,
                30);
            autoSelectThreshold = EditorGUILayout.Slider(
                "自动选帧阈值",
                autoSelectThreshold,
                0.01f,
                0.25f);
            autoSelectMinGap = EditorGUILayout.IntSlider(
                "最小帧间隔",
                autoSelectMinGap,
                1,
                10);
            EditorGUILayout.LabelField("骨架贴合参数", EditorStyles.boldLabel);
            bodyFitWidthScale = EditorGUILayout.Slider(
                "贴合宽度",
                bodyFitWidthScale,
                0.35f,
                1.0f);
            bodyFitHeightScale = EditorGUILayout.Slider(
                "贴合高度",
                bodyFitHeightScale,
                0.55f,
                1.0f);
            bodyFitOffsetX = EditorGUILayout.Slider(
                "中心X偏移",
                bodyFitOffsetX,
                -0.2f,
                0.2f);
            bodyFitOffsetY = EditorGUILayout.Slider(
                "中心Y偏移",
                bodyFitOffsetY,
                -0.2f,
                0.2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField(actionSourcePath);
            if (GUILayout.Button("选择", GUILayout.Width(56f)))
            {
                string selected = EditorUtility.OpenFilePanel(
                    "选择动作序列图片或动画文件",
                    Application.dataPath,
                    "png,jpg,jpeg,bmp,gif,mp4,mov,avi,webm");
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    LoadActionSource(selected);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(actionSourcePath)))
            {
                if (GUILayout.Button("自动选帧"))
                {
                    AutoSelectActionFrames();
                }
            }

            using (new EditorGUI.DisabledScope(animationDocument.frameSelections.Count == 0))
            {
                if (GUILayout.Button("清空手工修正"))
                {
                    ClearManualOverrides();
                }
            }
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(true))
            {
                GUILayout.Button("根据模板生成动作草稿（接口预留）");
            }

            DrawActionFramePreview();

            EditorGUILayout.HelpBox(
                "第一版先做模板编辑闭环：导入参考图 → 自动生成骨骼草稿 → 手动拖点 → 保存模板。" +
                "动作序列先按同目录图片做帧差选帧，后续再接视频抽帧和姿态识别。",
                MessageType.Info);

            DrawActionFrameList();
            DrawWarnings();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCanvasPanel()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("点位显示", EditorStyles.miniLabel, GUILayout.Width(60f));
            int nextDisplayMode = GUILayout.Toolbar(
                (int)displayMode,
                new[] { "全部", "显示蓝点", "显示红点" },
                EditorStyles.toolbarButton,
                GUILayout.Width(230f));
            if (nextDisplayMode != (int)displayMode)
            {
                displayMode = (SkeletonDisplayMode)nextDisplayMode;
                draggingBone = false;
                draggingSocket = false;
                Repaint();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            Rect canvasRect = GUILayoutUtility.GetRect(
                100f,
                10000f,
                100f,
                10000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));

            GUI.Box(canvasRect, GUIContent.none);
            Rect contentRect = CalculateContentRect(canvasRect);
            DrawCanvasBackground(canvasRect, contentRect);
            DrawSkeleton(contentRect);
            HandleCanvasInput(contentRect, Event.current);

            DrawAnimationStrip();
            EditorGUILayout.EndVertical();
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(InspectorWidth));
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

            EditorGUILayout.LabelField("模板", EditorStyles.boldLabel);
            templateDocument.templateId = EditorGUILayout.TextField(
                "模板 ID",
                templateDocument.templateId);
            templateDocument.displayName = EditorGUILayout.TextField(
                "显示名",
                templateDocument.displayName);
            templateDocument.category = EditorGUILayout.TextField(
                "分类",
                templateDocument.category);
            templateDocument.notes = EditorGUILayout.TextField(
                "备注",
                templateDocument.notes);

            EditorGUILayout.Space(10f);
            DrawBoneList();
            EditorGUILayout.Space(10f);
            DrawSelectedBoneInspector();
            EditorGUILayout.Space(10f);
            DrawSocketList();
            EditorGUILayout.Space(10f);
            DrawSelectedSocketInspector();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSkeletonTransformTools()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("骨架整体微调", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(templateDocument == null || templateDocument.bones.Count == 0))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("←", GUILayout.Width(48f)))
                {
                    TranslateSkeleton(new Vector2(-0.02f, 0f));
                }

                if (GUILayout.Button("→", GUILayout.Width(48f)))
                {
                    TranslateSkeleton(new Vector2(0.02f, 0f));
                }

                if (GUILayout.Button("↑", GUILayout.Width(48f)))
                {
                    TranslateSkeleton(new Vector2(0f, -0.02f));
                }

                if (GUILayout.Button("↓", GUILayout.Width(48f)))
                {
                    TranslateSkeleton(new Vector2(0f, 0.02f));
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("缩小", GUILayout.Width(98f)))
                {
                    ScaleSkeleton(0.92f);
                }

                if (GUILayout.Button("放大", GUILayout.Width(98f)))
                {
                    ScaleSkeleton(1.08f);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void TranslateSkeleton(Vector2 delta)
        {
            if (templateDocument == null || templateDocument.bones == null)
            {
                return;
            }

            for (int i = 0; i < templateDocument.bones.Count; i++)
            {
                SkeletonBoneData bone = templateDocument.bones[i];
                bone.normalizedPosition = new Vector2(
                    Mathf.Clamp01(bone.normalizedPosition.x + delta.x),
                    Mathf.Clamp01(bone.normalizedPosition.y + delta.y));
            }

            status = "已整体移动骨架。";
            Repaint();
        }

        private void ScaleSkeleton(float scale)
        {
            if (templateDocument == null || templateDocument.bones == null || templateDocument.bones.Count == 0)
            {
                return;
            }

            Vector2 center = CalculateSkeletonCenter();
            for (int i = 0; i < templateDocument.bones.Count; i++)
            {
                SkeletonBoneData bone = templateDocument.bones[i];
                Vector2 offset = bone.normalizedPosition - center;
                Vector2 scaled = center + offset * scale;
                bone.normalizedPosition = new Vector2(
                    Mathf.Clamp01(scaled.x),
                    Mathf.Clamp01(scaled.y));
            }

            status = "已整体缩放骨架。";
            Repaint();
        }

        private Vector2 CalculateSkeletonCenter()
        {
            if (templateDocument == null || templateDocument.bones == null || templateDocument.bones.Count == 0)
            {
                return new Vector2(0.5f, 0.5f);
            }

            Vector2 sum = Vector2.zero;
            for (int i = 0; i < templateDocument.bones.Count; i++)
            {
                sum += templateDocument.bones[i].normalizedPosition;
            }

            return sum / templateDocument.bones.Count;
        }

        private void DrawBoneList()
        {
            EditorGUILayout.LabelField("骨骼", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加骨骼"))
            {
                AddBone();
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selectedBoneId)))
            {
                if (GUILayout.Button("删除选中"))
                {
                    DeleteSelectedBone();
                }
            }
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < templateDocument.bones.Count; i++)
            {
                SkeletonBoneData bone = templateDocument.bones[i];
                GUIStyle style = bone.boneId == selectedBoneId
                    ? EditorStyles.toolbarButton
                    : EditorStyles.miniButton;
                if (GUILayout.Button(GetBoneLabel(bone), style))
                {
                    selectedBoneId = bone.boneId;
                    selectedSocketId = string.Empty;
                    Repaint();
                }
            }
        }

        private void DrawSelectedBoneInspector()
        {
            SkeletonBoneData bone = FindBone(selectedBoneId);
            if (bone == null)
            {
                EditorGUILayout.HelpBox("未选中骨骼。", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("选中骨骼", EditorStyles.boldLabel);
            bone.boneId = EditorGUILayout.TextField("Bone ID", bone.boneId);
            selectedBoneId = bone.boneId;
            bone.displayName = EditorGUILayout.TextField("显示名", bone.displayName);
            bone.parentBoneId = DrawParentPopup("父骨骼", bone.parentBoneId, bone.boneId);
            bone.normalizedPosition = EditorGUILayout.Vector2Field(
                "归一化坐标",
                bone.normalizedPosition);
            bone.length = EditorGUILayout.FloatField("长度", bone.length);
            bone.rotationDegrees = EditorGUILayout.FloatField(
                "旋转",
                bone.rotationDegrees);
            bone.locked = EditorGUILayout.Toggle("锁定", bone.locked);
            bone.confidence = EditorGUILayout.Slider("识别置信度", bone.confidence, 0f, 1f);
        }

        private void DrawSocketList()
        {
            EditorGUILayout.LabelField("挂点", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加挂点"))
            {
                AddSocket();
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selectedSocketId)))
            {
                if (GUILayout.Button("删除选中"))
                {
                    DeleteSelectedSocket();
                }
            }
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < templateDocument.sockets.Count; i++)
            {
                SkeletonSocketData socket = templateDocument.sockets[i];
                GUIStyle style = socket.socketId == selectedSocketId
                    ? EditorStyles.toolbarButton
                    : EditorStyles.miniButton;
                if (GUILayout.Button(GetSocketLabel(socket), style))
                {
                    selectedSocketId = socket.socketId;
                    selectedBoneId = string.Empty;
                    Repaint();
                }
            }
        }

        private void DrawSelectedSocketInspector()
        {
            SkeletonSocketData socket = FindSocket(selectedSocketId);
            if (socket == null)
            {
                EditorGUILayout.HelpBox("未选中挂点。", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("选中挂点", EditorStyles.boldLabel);
            socket.socketId = EditorGUILayout.TextField("Socket ID", socket.socketId);
            selectedSocketId = socket.socketId;
            socket.displayName = EditorGUILayout.TextField("显示名", socket.displayName);
            socket.parentBoneId = DrawParentPopup("绑定骨骼", socket.parentBoneId, string.Empty);
            socket.socketType = EditorGUILayout.TextField("类型", socket.socketType);
            socket.normalizedOffset = EditorGUILayout.Vector2Field(
                "相对偏移",
                socket.normalizedOffset);
            socket.rotationDegrees = EditorGUILayout.FloatField(
                "旋转",
                socket.rotationDegrees);
            socket.locked = EditorGUILayout.Toggle("锁定", socket.locked);
        }

        private void DrawWarnings()
        {
            if (warnings.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("提示", EditorStyles.boldLabel);
            warningsScroll = EditorGUILayout.BeginScrollView(
                warningsScroll,
                GUILayout.MinHeight(80f),
                GUILayout.MaxHeight(160f));
            for (int i = 0; i < warnings.Count; i++)
            {
                EditorGUILayout.HelpBox(warnings[i], MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawCanvasBackground(Rect canvasRect, Rect contentRect)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.13f, 0.14f, 0.14f));
            EditorGUI.DrawRect(contentRect, new Color(0.86f, 0.84f, 0.76f));

            if (sourceImage != null)
            {
                GUI.DrawTexture(contentRect, sourceImage, ScaleMode.ScaleToFit, true);
            }

            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.08f);
            for (int i = 1; i < 4; i++)
            {
                float x = Mathf.Lerp(contentRect.xMin, contentRect.xMax, i / 4f);
                Handles.DrawLine(new Vector3(x, contentRect.yMin), new Vector3(x, contentRect.yMax));
            }

            for (int i = 1; i < 4; i++)
            {
                float y = Mathf.Lerp(contentRect.yMin, contentRect.yMax, i / 4f);
                Handles.DrawLine(new Vector3(contentRect.xMin, y), new Vector3(contentRect.xMax, y));
            }
            Handles.EndGUI();
        }

        private void DrawSkeleton(Rect contentRect)
        {
            if (templateDocument == null)
            {
                return;
            }

            Handles.BeginGUI();
            if (displayMode != SkeletonDisplayMode.SocketsOnly)
            {
                for (int i = 0; i < templateDocument.bones.Count; i++)
                {
                    SkeletonBoneData bone = templateDocument.bones[i];
                    SkeletonBoneData parent = FindBone(bone.parentBoneId);
                    if (parent == null)
                    {
                        continue;
                    }

                    Vector2 a = ToCanvas(parent.normalizedPosition, contentRect);
                    Vector2 b = ToCanvas(bone.normalizedPosition, contentRect);
                    Handles.color = bone.confidence < 0.5f
                        ? new Color(1f, 0.75f, 0.2f, 0.95f)
                        : new Color(0.35f, 0.9f, 1f, 0.95f);
                    Handles.DrawAAPolyLine(4f, a, b);
                }
            }

            if (displayMode != SkeletonDisplayMode.SocketsOnly)
            {
                for (int i = 0; i < templateDocument.bones.Count; i++)
                {
                    DrawBoneNode(templateDocument.bones[i], contentRect);
                }
            }

            if (displayMode != SkeletonDisplayMode.BonesOnly)
            {
                DrawSockets(contentRect);
            }
            Handles.EndGUI();
        }

        private void DrawBoneNode(SkeletonBoneData bone, Rect contentRect)
        {
            Vector2 position = ToCanvas(bone.normalizedPosition, contentRect);
            bool selected = bone.boneId == selectedBoneId;
            Color color = bone.locked
                ? new Color(0.55f, 0.55f, 0.55f, 1f)
                : selected
                    ? new Color(1f, 0.85f, 0.2f, 1f)
                    : new Color(0.15f, 0.95f, 0.95f, 1f);
            Handles.color = color;
            Handles.DrawSolidDisc(position, Vector3.forward, selected ? 7f : 5f);
            Handles.color = Color.black;
            Handles.DrawWireDisc(position, Vector3.forward, selected ? 7f : 5f);

            Rect labelRect = new Rect(position.x + 8f, position.y - 10f, 140f, 20f);
            GUI.Label(labelRect, bone.displayName, EditorStyles.miniLabel);
        }

        private void DrawSockets(Rect contentRect)
        {
            for (int i = 0; i < templateDocument.sockets.Count; i++)
            {
                SkeletonSocketData socket = templateDocument.sockets[i];
                SkeletonBoneData parent = FindBone(socket.parentBoneId);
                if (parent == null)
                {
                    continue;
                }

                Vector2 normalized = parent.normalizedPosition + socket.normalizedOffset;
                Vector2 parentPosition = ToCanvas(parent.normalizedPosition, contentRect);
                Vector2 position = ToCanvas(normalized, contentRect);
                bool selected = socket.socketId == selectedSocketId;
                Handles.color = selected
                    ? new Color(1f, 0.35f, 0.85f, 0.95f)
                    : new Color(1f, 0.35f, 0.85f, 0.55f);
                Handles.DrawDottedLine(parentPosition, position, 4f);

                Color socketColor = socket.locked
                    ? new Color(0.95f, 0.25f, 0.18f, 0.9f)
                    : selected
                        ? new Color(1f, 0.85f, 0.25f, 1f)
                        : new Color(0.95f, 0.55f, 0.25f, 0.9f);
                Handles.color = socketColor;
                Handles.DrawSolidRectangleWithOutline(
                    new Rect(position.x - 4f, position.y - 4f, 8f, 8f),
                    socketColor,
                    Color.black);
                GUI.Label(
                    new Rect(position.x + 7f, position.y - 9f, 220f, 18f),
                    GetSocketCanvasLabel(socket, parent),
                    EditorStyles.miniLabel);
            }
        }

        private void DrawAnimationStrip()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(70f));
            EditorGUILayout.LabelField("动作草稿", GUILayout.Width(70f));
            currentFrame = EditorGUILayout.IntField("当前帧", currentFrame, GUILayout.Width(150f));
            previewFrameRate = EditorGUILayout.FloatField("帧率", previewFrameRate, GUILayout.Width(150f));

            if (GUILayout.Button("记录当前姿势为关键帧", GUILayout.Width(160f)))
            {
                RecordCurrentPoseAsKeyframe();
            }

            if (GUILayout.Button("导出动作 JSON", GUILayout.Width(120f)))
            {
                SaveAnimation();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                animationDocument.keyframes.Count + " 个关键帧",
                GUILayout.Width(100f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActionFrameList()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("动作帧候选", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("单击行名预览；双击行名切到参考图并允许调骨骼。", MessageType.None);

            if (animationDocument.frameSelections.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "先选择动作序列中的任意一张图片，工具会读取同目录图片并自动选帧。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"总帧数：{animationDocument.frameSelections.Count}",
                GUILayout.Width(120f));
            EditorGUILayout.LabelField(
                $"已选：{CountSelectedFrames()}",
                GUILayout.Width(100f));
            EditorGUILayout.EndHorizontal();

            List<SkeletonActionFrameSelectionData> sortedFrames =
                new List<SkeletonActionFrameSelectionData>(animationDocument.frameSelections);
            sortedFrames.Sort((a, b) =>
            {
                int selectedCompare = b.selected.CompareTo(a.selected);
                if (selectedCompare != 0)
                {
                    return selectedCompare;
                }

                return a.frameIndex.CompareTo(b.frameIndex);
            });

            for (int i = 0; i < sortedFrames.Count; i++)
            {
                SkeletonActionFrameSelectionData frame = sortedFrames[i];
                bool isCurrent = frame.frameIndex == currentFrame;
                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = isCurrent
                    ? new Color(0.45f, 0.65f, 0.95f, 1f)
                    : oldColor;
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                EditorGUI.BeginChangeCheck();
                bool nextSelected = EditorGUILayout.Toggle(frame.selected, GUILayout.Width(18f));
                if (EditorGUI.EndChangeCheck())
                {
                    frame.selected = nextSelected;
                    frame.manualOverride = true;
                    status = nextSelected
                        ? "已选中帧 #" + frame.frameIndex
                        : "已取消帧 #" + frame.frameIndex;
                    Repaint();
                }

                string tag = frame.selected ? " [选中]" : string.Empty;

                Rect labelRect = GUILayoutUtility.GetRect(
                    new GUIContent($"#{frame.frameIndex} score={frame.differenceScore:0.000}{tag}"),
                    EditorStyles.label,
                    GUILayout.Width(190f),
                    GUILayout.Height(18f));
                GUI.Label(labelRect, $"#{frame.frameIndex} score={frame.differenceScore:0.000}{tag}");

                Event evt = Event.current;
                if (evt.type == EventType.MouseDown
                    && evt.button == 0
                    && labelRect.Contains(evt.mousePosition))
                {
                    currentFrame = frame.frameIndex;
                    LoadActionFramePreview(frame.sourceFilePath);
                    status = evt.clickCount >= 2
                        ? "已切换到参考图 #" + frame.frameIndex + "，可直接调骨骼。"
                        : "已预览帧 #" + frame.frameIndex;

                    if (evt.clickCount >= 2)
                    {
                        LoadReferenceImageFromFrame(frame.sourceFilePath);
                    }

                    evt.Use();
                    Repaint();
                }

                if (GUILayout.Button("看", GUILayout.Width(32f)))
                {
                    currentFrame = frame.frameIndex;
                    LoadActionFramePreview(frame.sourceFilePath);
                    status = "正在预览帧 #" + frame.frameIndex;
                    Repaint();
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = oldColor;
            }

            int maxFrameIndex = Mathf.Max(0, animationDocument.frameSelections.Count - 1);
            int nextFrame = EditorGUILayout.IntSlider(
                "预览滑条",
                Mathf.Clamp(currentFrame, 0, maxFrameIndex),
                0,
                maxFrameIndex);
            if (nextFrame != currentFrame)
            {
                currentFrame = nextFrame;
                SkeletonActionFrameSelectionData frame = FindActionFrame(currentFrame);
                if (frame != null)
                {
                    LoadActionFramePreview(frame.sourceFilePath);
                    status = "已切换到帧 #" + currentFrame;
                    Repaint();
                }
            }
        }

        private void DrawActionFramePreview()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("当前帧预览", EditorStyles.boldLabel);
            if (string.IsNullOrWhiteSpace(actionFramePreviewPath) || actionFramePreviewTexture == null)
            {
                EditorGUILayout.HelpBox("点击“看”后，这里会显示当前动作帧。", MessageType.Info);
                return;
            }

            Rect previewRect = GUILayoutUtility.GetRect(
                100f,
                240f,
                100f,
                240f,
                GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.1f, 0.1f, 0.1f, 0.35f));
            GUI.DrawTexture(previewRect, actionFramePreviewTexture, ScaleMode.ScaleToFit, true);
            GUI.Label(
                new Rect(previewRect.x + 8f, previewRect.y + 8f, previewRect.width - 16f, 20f),
                Path.GetFileName(actionFramePreviewPath),
                EditorStyles.whiteMiniLabel);
        }

        private void HandleCanvasInput(Rect contentRect, Event evt)
        {
            if (templateDocument == null || evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                string hitSocket = HitTestSocket(evt.mousePosition, contentRect);
                if (!string.IsNullOrEmpty(hitSocket))
                {
                    SkeletonSocketData socket = FindSocket(hitSocket);
                    selectedSocketId = hitSocket;
                    selectedBoneId = string.Empty;
                    if (socket != null && !socket.locked)
                    {
                        draggingSocket = true;
                        draggingSocketId = hitSocket;
                    }

                    evt.Use();
                    Repaint();
                    return;
                }

                string hitBone = HitTestBone(evt.mousePosition, contentRect);
                if (!string.IsNullOrEmpty(hitBone))
                {
                    SkeletonBoneData bone = FindBone(hitBone);
                    selectedBoneId = hitBone;
                    selectedSocketId = string.Empty;
                    if (bone != null && !bone.locked)
                    {
                        draggingBone = true;
                        draggingBoneId = hitBone;
                    }

                    evt.Use();
                    Repaint();
                }
            }
            else if (evt.type == EventType.MouseDrag && draggingBone)
            {
                SkeletonBoneData bone = FindBone(draggingBoneId);
                if (bone != null && !bone.locked)
                {
                    bone.normalizedPosition = ToNormalized(evt.mousePosition, contentRect);
                    evt.Use();
                    Repaint();
                }
            }
            else if (evt.type == EventType.MouseDrag && draggingSocket)
            {
                SkeletonSocketData socket = FindSocket(draggingSocketId);
                SkeletonBoneData parent = socket == null
                    ? null
                    : FindBone(socket.parentBoneId);
                if (socket != null && parent != null && !socket.locked)
                {
                    Vector2 normalized = ToNormalized(evt.mousePosition, contentRect);
                    socket.normalizedOffset = normalized - parent.normalizedPosition;
                    evt.Use();
                    Repaint();
                }
            }
            else if (evt.type == EventType.MouseUp && draggingBone)
            {
                draggingBone = false;
                draggingBoneId = string.Empty;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && draggingSocket)
            {
                draggingSocket = false;
                draggingSocketId = string.Empty;
                evt.Use();
            }
        }

        private void HandleKeyboard(Event evt)
        {
            if (evt == null || evt.type != EventType.KeyDown)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                if (!string.IsNullOrEmpty(selectedBoneId))
                {
                    DeleteSelectedBone();
                    evt.Use();
                }
                else if (!string.IsNullOrEmpty(selectedSocketId))
                {
                    DeleteSelectedSocket();
                    evt.Use();
                }
            }
        }

        private void RecognizeDraft()
        {
            SkeletonRecognitionResult result = recognitionEngine.RecognizeTemplateDraft(
                new SkeletonRecognitionInput
                {
                    SourceImage = sourceImage,
                    SourceAssetPath = sourceAssetPath,
                    ExistingTemplate = templateDocument,
                    PreferredCategory = templateDocument == null
                        ? "Humanoid"
                        : templateDocument.category,
                    BodyFitWidthScale = bodyFitWidthScale,
                    BodyFitHeightScale = bodyFitHeightScale,
                    BodyFitOffsetX = bodyFitOffsetX,
                    BodyFitOffsetY = bodyFitOffsetY,
                });

            templateDocument = result.Template;
            animationDocument = new SkeletonAnimationDocument
            {
                templateId = templateDocument.templateId,
                frameRate = previewFrameRate,
            };
            selectedBoneId = templateDocument.bones.Count > 0
                ? templateDocument.bones[0].boneId
                : string.Empty;
            selectedSocketId = string.Empty;
            warnings.Clear();
            warnings.AddRange(result.Warnings);
            status = "已生成骨骼草稿，请人工校正后保存模板。";
            Repaint();
        }

        private void AddBone()
        {
            string id = MakeUniqueId("bone");
            string parent = string.IsNullOrEmpty(selectedBoneId)
                ? string.Empty
                : selectedBoneId;
            templateDocument.bones.Add(new SkeletonBoneData
            {
                boneId = id,
                displayName = id,
                parentBoneId = parent,
                normalizedPosition = new Vector2(0.5f, 0.5f),
                length = 0.1f,
                confidence = 1f,
            });
            selectedBoneId = id;
            selectedSocketId = string.Empty;
            status = "已添加骨骼：" + id;
        }

        private void DeleteSelectedBone()
        {
            string boneId = selectedBoneId;
            if (string.IsNullOrEmpty(boneId))
            {
                return;
            }

            templateDocument.bones.RemoveAll(bone => bone.boneId == boneId);
            for (int i = 0; i < templateDocument.bones.Count; i++)
            {
                if (templateDocument.bones[i].parentBoneId == boneId)
                {
                    templateDocument.bones[i].parentBoneId = string.Empty;
                }
            }

            templateDocument.sockets.RemoveAll(socket => socket.parentBoneId == boneId);
            selectedBoneId = string.Empty;
            status = "已删除骨骼：" + boneId;
            Repaint();
        }

        private void AddSocket()
        {
            string id = MakeUniqueSocketId("socket");
            string parent = string.IsNullOrEmpty(selectedBoneId)
                ? templateDocument.bones.Count > 0 ? templateDocument.bones[0].boneId : string.Empty
                : selectedBoneId;
            templateDocument.sockets.Add(new SkeletonSocketData
            {
                socketId = id,
                displayName = id,
                parentBoneId = parent,
                socketType = "Equipment",
                locked = true,
            });
            selectedSocketId = id;
            selectedBoneId = string.Empty;
            status = "已添加挂点：" + id;
        }

        private void DeleteSelectedSocket()
        {
            string socketId = selectedSocketId;
            if (string.IsNullOrEmpty(socketId))
            {
                return;
            }

            templateDocument.sockets.RemoveAll(socket => socket.socketId == socketId);
            selectedSocketId = string.Empty;
            status = "已删除挂点：" + socketId;
            Repaint();
        }

        private void SaveTemplate()
        {
            string defaultName = string.IsNullOrWhiteSpace(templateDocument.templateId)
                ? "skeleton_template"
                : templateDocument.templateId;
            string path = EditorUtility.SaveFilePanelInProject(
                "保存骨骼模板",
                defaultName,
                "json",
                "选择模板保存位置",
                DefaultFolder);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string json = JsonUtility.ToJson(templateDocument, true);
            File.WriteAllText(path, json);
            AssetDatabase.ImportAsset(path);
            status = "已保存模板：" + path;
        }

        private void LoadTemplate()
        {
            string path = EditorUtility.OpenFilePanel(
                "读取骨骼模板",
                Path.Combine(Application.dataPath, "TryGameToolScripts/SkeletonAnimationTool"),
                "json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string json = File.ReadAllText(path);
            SkeletonTemplateDocument loaded =
                JsonUtility.FromJson<SkeletonTemplateDocument>(json);
            if (loaded == null)
            {
                status = "模板读取失败：" + path;
                return;
            }

            templateDocument = loaded;
            animationDocument = new SkeletonAnimationDocument
            {
                templateId = templateDocument.templateId,
                frameRate = previewFrameRate,
            };
            selectedBoneId = templateDocument.bones.Count > 0
                ? templateDocument.bones[0].boneId
                : string.Empty;
            selectedSocketId = string.Empty;
            status = "已读取模板：" + path;
            Repaint();
        }

        private void SaveAnimation()
        {
            animationDocument.templateId = templateDocument.templateId;
            animationDocument.frameRate = previewFrameRate;
            string defaultName = string.IsNullOrWhiteSpace(animationDocument.animationId)
                ? "skeleton_animation"
                : animationDocument.animationId;
            string path = EditorUtility.SaveFilePanelInProject(
                "保存骨骼动作",
                defaultName,
                "json",
                "选择动作保存位置",
                DefaultFolder);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string json = JsonUtility.ToJson(animationDocument, true);
            File.WriteAllText(path, json);
            AssetDatabase.ImportAsset(path);
            status = "已保存动作：" + path;
        }

        private void RecordCurrentPoseAsKeyframe()
        {
            SkeletonAnimationKeyframeData keyframe = null;
            for (int i = 0; i < animationDocument.keyframes.Count; i++)
            {
                if (animationDocument.keyframes[i].frame == currentFrame)
                {
                    keyframe = animationDocument.keyframes[i];
                    break;
                }
            }

            if (keyframe == null)
            {
                keyframe = new SkeletonAnimationKeyframeData
                {
                    frame = currentFrame,
                };
                animationDocument.keyframes.Add(keyframe);
            }

            keyframe.bonePoses.Clear();
            for (int i = 0; i < templateDocument.bones.Count; i++)
            {
                SkeletonBoneData bone = templateDocument.bones[i];
                keyframe.bonePoses.Add(new SkeletonBonePoseData
                {
                    boneId = bone.boneId,
                    normalizedPosition = bone.normalizedPosition,
                    rotationDegrees = bone.rotationDegrees,
                });
            }

            status = "已记录关键帧：" + currentFrame;
        }

        private void LoadActionSource(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            string extension = Path.GetExtension(selectedPath).ToLowerInvariant();
            if (IsAnimationFileExtension(extension))
            {
                TryCleanupTempActionFrames();
                if (!TryExtractAnimationFrames(selectedPath, out string frameFolder, out string error))
                {
                    status = error;
                    Debug.LogError("[SkeletonAnimationEditorWindow] 动画拆帧失败：" + error);
                    return;
                }

                actionSourcePath = frameFolder;
                LoadActionFrameSequence(frameFolder);
                return;
            }

            actionSourcePath = selectedPath;
            LoadActionFrameSequence(selectedPath);
        }

        private void LoadActionFramePreview(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return;
            }

            if (actionFramePreviewTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(actionFramePreviewTexture);
                actionFramePreviewTexture = null;
            }

            actionFramePreviewTexture = LoadTextureFromFile(imagePath);
            actionFramePreviewPath = imagePath;
        }

        private void LoadReferenceImageFromFrame(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return;
            }

            Texture2D texture = LoadTextureFromFile(imagePath);
            if (texture == null)
            {
                return;
            }

            sourceImage = texture;
            sourceAssetPath = imagePath;
            if (templateDocument != null)
            {
                templateDocument.sourceAssetPath = imagePath;
            }
        }

        private void LoadActionFrameSequence(string selectedPath)
        {
            string folderPath = GetActionFrameFolder(selectedPath);
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                status = "动作序列目录不存在：" + folderPath;
                return;
            }

            string[] files = Directory.GetFiles(folderPath);
            List<string> imageFiles = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                string extension = Path.GetExtension(files[i]).ToLowerInvariant();
                if (IsImageFileExtension(extension))
                {
                    imageFiles.Add(files[i]);
                }
            }

            imageFiles.Sort(StringComparer.OrdinalIgnoreCase);
            if (imageFiles.Count == 0)
            {
                animationDocument.frameSelections.Clear();
                status = "动作序列目录里没有找到图片：" + folderPath;
                Repaint();
                return;
            }

            Dictionary<string, SkeletonActionFrameSelectionData> previous =
                new Dictionary<string, SkeletonActionFrameSelectionData>();
            for (int i = 0; i < animationDocument.frameSelections.Count; i++)
            {
                SkeletonActionFrameSelectionData frame = animationDocument.frameSelections[i];
                if (!string.IsNullOrWhiteSpace(frame.sourceFilePath) && !previous.ContainsKey(frame.sourceFilePath))
                {
                    previous.Add(frame.sourceFilePath, frame);
                }
            }

            animationDocument.frameSelections.Clear();
            for (int i = 0; i < imageFiles.Count; i++)
            {
                SkeletonActionFrameSelectionData frame = new SkeletonActionFrameSelectionData
                {
                    frameIndex = i,
                    sourceFilePath = imageFiles[i],
                    differenceScore = i == 0
                        ? 1f
                        : CalculateFrameDifferenceScore(imageFiles[i - 1], imageFiles[i]),
                };

                if (previous.TryGetValue(frame.sourceFilePath, out SkeletonActionFrameSelectionData oldFrame))
                {
                    frame.autoSelected = oldFrame.autoSelected;
                    frame.selected = oldFrame.selected;
                    frame.manualOverride = oldFrame.manualOverride;
                }

                animationDocument.frameSelections.Add(frame);
            }

            status = "已读取动作序列图片：" + imageFiles.Count + " 帧。";
            if (animationDocument.frameSelections.Count > 0)
            {
                currentFrame = animationDocument.frameSelections[0].frameIndex;
                LoadActionFramePreview(animationDocument.frameSelections[0].sourceFilePath);
            }
            else
            {
                actionFramePreviewPath = string.Empty;
                if (actionFramePreviewTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(actionFramePreviewTexture);
                    actionFramePreviewTexture = null;
                }
            }
            Repaint();
        }

        private void AutoSelectActionFrames()
        {
            if (animationDocument.frameSelections.Count == 0)
            {
                LoadActionFrameSequence(actionSourcePath);
            }

            if (animationDocument.frameSelections.Count == 0)
            {
                status = "没有可自动选帧的动作图片。";
                return;
            }

            int lastSelectedIndex = -autoSelectMinGap;
            float cumulativeScore = 0f;
            for (int i = 0; i < animationDocument.frameSelections.Count; i++)
            {
                SkeletonActionFrameSelectionData frame = animationDocument.frameSelections[i];
                cumulativeScore += frame.differenceScore;
                bool shouldSelect = i == 0 || cumulativeScore >= autoSelectThreshold;
                if (shouldSelect && i - lastSelectedIndex < autoSelectMinGap)
                {
                    shouldSelect = false;
                }

                frame.autoSelected = shouldSelect;
                if (!frame.manualOverride)
                {
                    frame.selected = shouldSelect;
                }

                if (frame.selected)
                {
                    lastSelectedIndex = i;
                    cumulativeScore = 0f;
                }
            }

            status = $"已自动选帧，命中 {CountSelectedFrames()} 帧，可继续手动增删。";
            Repaint();
        }

        private void ClearManualOverrides()
        {
            for (int i = 0; i < animationDocument.frameSelections.Count; i++)
            {
                SkeletonActionFrameSelectionData frame = animationDocument.frameSelections[i];
                frame.manualOverride = false;
                frame.selected = frame.autoSelected;
            }

            status = "已清空手工修正，恢复自动结果。";
            Repaint();
        }

        private int CountSelectedFrames()
        {
            int count = 0;
            for (int i = 0; i < animationDocument.frameSelections.Count; i++)
            {
                if (animationDocument.frameSelections[i].selected)
                {
                    count++;
                }
            }

            return count;
        }

        private SkeletonActionFrameSelectionData FindActionFrame(int frameIndex)
        {
            for (int i = 0; i < animationDocument.frameSelections.Count; i++)
            {
                if (animationDocument.frameSelections[i].frameIndex == frameIndex)
                {
                    return animationDocument.frameSelections[i];
                }
            }

            return null;
        }

        private static string GetActionFrameFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (Directory.Exists(path))
            {
                return path;
            }

            return Path.GetDirectoryName(path) ?? string.Empty;
        }

        private bool TryExtractAnimationFrames(
            string animationPath,
            out string frameFolder,
            out string error)
        {
            frameFolder = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(animationPath) || !File.Exists(animationPath))
            {
                error = "动画文件不存在：" + animationPath;
                return false;
            }

            string ffmpegPath = FindFfmpegExecutable();
            if (string.IsNullOrWhiteSpace(ffmpegPath))
            {
                error =
                    "未找到 ffmpeg，无法拆帧。请把 ffmpeg.exe 放到 " +
                    "Assets/TryGameToolScripts/SkeletonAnimationTool 下，或加入系统 PATH。";
                return false;
            }

            string extractedRoot = Path.Combine(
                Path.GetTempPath(),
                "TryAiGame",
                "SkeletonAnimationTool",
                "ExtractedFrames");
            string safeName = MakeSafeFileName(Path.GetFileNameWithoutExtension(animationPath));
            string outputFolder = Path.Combine(
                extractedRoot,
                safeName + "_fps" + Mathf.Clamp(extractFrameRate, 1, 30) + "_" + DateTime.Now.Ticks);

            string fullExtractedRoot = Path.GetFullPath(extractedRoot);
            string fullOutputFolder = Path.GetFullPath(outputFolder);
            if (!fullOutputFolder.StartsWith(
                    fullExtractedRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "拆帧输出目录非法：" + fullOutputFolder;
                return false;
            }

            Directory.CreateDirectory(outputFolder);
            string[] oldFrames = Directory.GetFiles(outputFolder, "*.png");
            for (int i = 0; i < oldFrames.Length; i++)
            {
                File.Delete(oldFrames[i]);
            }

            string outputPattern = Path.Combine(outputFolder, "frame_%05d.png");
            System.Diagnostics.ProcessStartInfo startInfo =
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments =
                        "-y -i \"" + animationPath + "\" -vf fps=" +
                        Mathf.Clamp(extractFrameRate, 1, 30) +
                        " \"" + outputPattern + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = false,
                    RedirectStandardOutput = false,
                };

            using (System.Diagnostics.Process process =
                   System.Diagnostics.Process.Start(startInfo))
            {
                if (process == null)
                {
                    error = "ffmpeg 进程启动失败。";
                    return false;
                }

                if (!process.WaitForExit(60000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                        // best effort
                    }

                    error = "ffmpeg 拆帧超时，请先用较短动画验收。";
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    error = "ffmpeg 拆帧失败，退出码：" + process.ExitCode;
                    return false;
                }
            }

            string[] frames = Directory.GetFiles(outputFolder, "*.png");
            if (frames.Length == 0)
            {
                error = "ffmpeg 已执行，但没有生成图片帧：" + outputFolder;
                return false;
            }

            frameFolder = outputFolder;
            status = "动画拆帧完成：" + frames.Length + " 帧。";
            return true;
        }

        private void TryCleanupTempActionFrames()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "TryAiGame",
                "SkeletonAnimationTool",
                "ExtractedFrames");
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SkeletonAnimationEditorWindow] 清理临时拆帧目录失败：" + exception.Message);
            }
        }

        private static string FindFfmpegExecutable()
        {
            string local = Path.Combine(
                Application.dataPath,
                "TryGameToolScripts/SkeletonAnimationTool/ffmpeg.exe");
            if (File.Exists(local))
            {
                return local;
            }

            string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] parts = pathVariable.Split(Path.PathSeparator);
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                {
                    continue;
                }

                string candidate = Path.Combine(parts[i], "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static bool IsImageFileExtension(string extension)
        {
            return extension == ".png"
                || extension == ".jpg"
                || extension == ".jpeg"
                || extension == ".bmp";
        }

        private static bool IsAnimationFileExtension(string extension)
        {
            return extension == ".gif"
                || extension == ".mp4"
                || extension == ".mov"
                || extension == ".avi"
                || extension == ".webm";
        }

        private static string MakeSafeFileName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "animation";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            string result = raw;
            for (int i = 0; i < invalid.Length; i++)
            {
                result = result.Replace(invalid[i], '_');
            }

            return string.IsNullOrWhiteSpace(result) ? "animation" : result;
        }

        private static float CalculateFrameDifferenceScore(string previousPath, string currentPath)
        {
            Texture2D previous = LoadTextureFromFile(previousPath);
            Texture2D current = LoadTextureFromFile(currentPath);
            if (previous == null || current == null)
            {
                return 0f;
            }

            try
            {
                const int sampleSize = 24;
                float total = 0f;
                for (int y = 0; y < sampleSize; y++)
                {
                    for (int x = 0; x < sampleSize; x++)
                    {
                        Color a = previous.GetPixelBilinear(
                            (x + 0.5f) / sampleSize,
                            (y + 0.5f) / sampleSize);
                        Color b = current.GetPixelBilinear(
                            (x + 0.5f) / sampleSize,
                            (y + 0.5f) / sampleSize);
                        total += Mathf.Abs(a.r - b.r);
                        total += Mathf.Abs(a.g - b.g);
                        total += Mathf.Abs(a.b - b.b);
                    }
                }

                return Mathf.Clamp01(total / (sampleSize * sampleSize * 3f));
            }
            finally
            {
                if (previous != null)
                {
                    UnityEngine.Object.DestroyImmediate(previous);
                }

                if (current != null)
                {
                    UnityEngine.Object.DestroyImmediate(current);
                }
            }
        }

        private static Texture2D LoadTextureFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            byte[] data = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, data, false))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }

            return texture;
        }

        private Rect CalculateContentRect(Rect canvasRect)
        {
            Rect padded = new Rect(
                canvasRect.x + 16f,
                canvasRect.y + 16f,
                canvasRect.width - 32f,
                canvasRect.height - 32f);
            if (sourceImage == null)
            {
                return padded;
            }

            float imageRatio = (float)sourceImage.width / sourceImage.height;
            float rectRatio = padded.width / padded.height;
            if (imageRatio > rectRatio)
            {
                float height = padded.width / imageRatio;
                return new Rect(
                    padded.x,
                    padded.y + (padded.height - height) * 0.5f,
                    padded.width,
                    height);
            }

            float width = padded.height * imageRatio;
            return new Rect(
                padded.x + (padded.width - width) * 0.5f,
                padded.y,
                width,
                padded.height);
        }

        private Vector2 ToCanvas(Vector2 normalized, Rect contentRect)
        {
            return new Vector2(
                contentRect.xMin + normalized.x * contentRect.width,
                contentRect.yMin + normalized.y * contentRect.height);
        }

        private Vector2 ToNormalized(Vector2 canvasPosition, Rect contentRect)
        {
            float x = (canvasPosition.x - contentRect.xMin) / contentRect.width;
            float y = (canvasPosition.y - contentRect.yMin) / contentRect.height;
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        private string HitTestBone(Vector2 mousePosition, Rect contentRect)
        {
            if (displayMode == SkeletonDisplayMode.SocketsOnly)
            {
                return string.Empty;
            }

            string result = string.Empty;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < templateDocument.bones.Count; i++)
            {
                SkeletonBoneData bone = templateDocument.bones[i];
                Vector2 position = ToCanvas(bone.normalizedPosition, contentRect);
                float distance = Vector2.Distance(mousePosition, position);
                if (distance <= BonePickRadius && distance < bestDistance)
                {
                    result = bone.boneId;
                    bestDistance = distance;
                }
            }

            return result;
        }

        private string HitTestSocket(Vector2 mousePosition, Rect contentRect)
        {
            if (displayMode == SkeletonDisplayMode.BonesOnly)
            {
                return string.Empty;
            }

            string result = string.Empty;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < templateDocument.sockets.Count; i++)
            {
                SkeletonSocketData socket = templateDocument.sockets[i];
                SkeletonBoneData parent = FindBone(socket.parentBoneId);
                if (parent == null)
                {
                    continue;
                }

                Vector2 position = ToCanvas(
                    parent.normalizedPosition + socket.normalizedOffset,
                    contentRect);
                float distance = Vector2.Distance(mousePosition, position);
                if (distance <= SocketPickRadius && distance < bestDistance)
                {
                    result = socket.socketId;
                    bestDistance = distance;
                }
            }

            return result;
        }

        private SkeletonBoneData FindBone(string boneId)
        {
            if (templateDocument == null || string.IsNullOrEmpty(boneId))
            {
                return null;
            }

            for (int i = 0; i < templateDocument.bones.Count; i++)
            {
                if (templateDocument.bones[i].boneId == boneId)
                {
                    return templateDocument.bones[i];
                }
            }

            return null;
        }

        private SkeletonSocketData FindSocket(string socketId)
        {
            if (templateDocument == null || string.IsNullOrEmpty(socketId))
            {
                return null;
            }

            for (int i = 0; i < templateDocument.sockets.Count; i++)
            {
                if (templateDocument.sockets[i].socketId == socketId)
                {
                    return templateDocument.sockets[i];
                }
            }

            return null;
        }

        private string DrawParentPopup(string label, string currentValue, string selfBoneId)
        {
            List<string> values = new List<string>();
            List<string> labels = new List<string>();
            values.Add(string.Empty);
            labels.Add("<none>");
            for (int i = 0; i < templateDocument.bones.Count; i++)
            {
                SkeletonBoneData bone = templateDocument.bones[i];
                if (!string.IsNullOrEmpty(selfBoneId) && bone.boneId == selfBoneId)
                {
                    continue;
                }

                values.Add(bone.boneId);
                labels.Add(GetBoneLabel(bone));
            }

            int index = values.IndexOf(currentValue);
            if (index < 0)
            {
                index = 0;
            }

            int nextIndex = EditorGUILayout.Popup(label, index, labels.ToArray());
            return values[nextIndex];
        }

        private string MakeUniqueId(string prefix)
        {
            int index = templateDocument.bones.Count + 1;
            while (FindBone(prefix + "_" + index) != null)
            {
                index++;
            }

            return prefix + "_" + index;
        }

        private string MakeUniqueSocketId(string prefix)
        {
            int index = templateDocument.sockets.Count + 1;
            while (FindSocket(prefix + "_" + index) != null)
            {
                index++;
            }

            return prefix + "_" + index;
        }

        private static string GetBoneLabel(SkeletonBoneData bone)
        {
            return string.IsNullOrWhiteSpace(bone.displayName)
                ? bone.boneId
                : bone.displayName + " (" + bone.boneId + ")";
        }

        private static string GetSocketLabel(SkeletonSocketData socket)
        {
            return string.IsNullOrWhiteSpace(socket.displayName)
                ? socket.socketId
                : socket.displayName + " (" + socket.socketId + ")";
        }

        private static string GetSocketCanvasLabel(
            SkeletonSocketData socket,
            SkeletonBoneData parent)
        {
            return string.IsNullOrWhiteSpace(socket.displayName)
                ? socket.socketId
                : socket.displayName;
        }

        private void EnsureDocuments()
        {
            if (templateDocument == null)
            {
                templateDocument = new SkeletonTemplateDocument();
            }

            if (templateDocument.bones == null)
            {
                templateDocument.bones = new List<SkeletonBoneData>();
            }

            if (templateDocument.sockets == null)
            {
                templateDocument.sockets = new List<SkeletonSocketData>();
            }

            if (templateDocument.viewTemplates == null)
            {
                templateDocument.viewTemplates = new List<SkeletonViewTemplateData>();
            }

            if (animationDocument == null)
            {
                animationDocument = new SkeletonAnimationDocument
                {
                    templateId = templateDocument.templateId,
                    frameRate = previewFrameRate,
                };
            }

            if (animationDocument.keyframes == null)
            {
                animationDocument.keyframes = new List<SkeletonAnimationKeyframeData>();
            }

            if (animationDocument.frameSelections == null)
            {
                animationDocument.frameSelections =
                    new List<SkeletonActionFrameSelectionData>();
            }
        }
    }
}
#endif
