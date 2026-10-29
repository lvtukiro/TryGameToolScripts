#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Game.SequenceFrameAnimation;
using Game.EditorTools.SequenceFrameAnimation;

namespace Game.EditorTools.SpriteSheetAnimation
{
    /// <summary>
    /// 从原图编辑裁框的独立工具。切割只读原图，所有操作均为非破坏性参数。
    /// 借鉴 Unity 的可编辑裁框、Aseprite 的逐帧锚点与预览、透明图块检测流程。
    /// 与视频拆帧工具和游戏播放逻辑保持独立。
    /// </summary>
    public sealed class SpriteSheetAnimationEditorWindow : EditorWindow
    {
        private static readonly string[] SliceModes = { "按行列数", "按单格尺寸", "透明区域识别", "手动裁框" };
        private static readonly string[] EditTools = { "选择 / 移动", "新建裁框", "逐帧锚点" };
        [SerializeField] private SpriteSheetAnimationDocument document = new SpriteSheetAnimationDocument();
        [SerializeField] private int selectedIndex = -1;
        private SpriteSheetPixels sourcePixels;
        private Texture2D sourceTexture, frameTexture, previousTexture, nextTexture, checker;
        private Vector2 leftScroll, rightScroll, pan;
        private float zoom = 1f, previewZoom = 1f;
        private bool fitRequested = true, previewDirty = true, dirty, playing, onionSkin, onionSkinNext;
        private double lastPlaybackTime;
        private int playbackIndex, editTool;
        private string status = "请选择原图。新工具不会自动抠图、缩放或对齐。", previewError = "", draftError = "";
        private MessageType statusType = MessageType.Info;
        private List<SpriteSheetFrameData> draftFrames = new List<SpriteSheetFrameData>();
        private bool showDraft = true;
        private Vector2 origin, canvasSize, dragStart, startPan;
        private SpriteSheetSliceRect startRect, pendingRect;
        private int startAnchorX, startAnchorY, canvasControl;
        private DragAction dragAction;
        private int resizeHandle = -1;
        private enum DragAction { None, Pan, Move, Resize, Create, AnchorBoth, AnchorX, AnchorY }

        [MenuItem("TryGame/工具/帧图角色动画工具")]
        public static void Open()
        {
            var window = GetWindow<SpriteSheetAnimationEditorWindow>("帧图切割工具");
            window.minSize = new Vector2(1050, 720);
            window.Show();
        }

        private void OnEnable()
        {
            if (document == null || document.formatVersion != 2) document = new SpriteSheetAnimationDocument();
            minSize = new Vector2(1050, 720);
            EditorApplication.update += UpdatePlayback;
            if (!string.IsNullOrWhiteSpace(document.sourceImagePath))
                Run(() => LoadSource(document.sourceImagePath, true));
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePlayback;
            EndDrag(); playing = false;
            DestroyTexture(ref sourceTexture); DestroyTexture(ref frameTexture);
            DestroyTexture(ref previousTexture); DestroyTexture(ref nextTexture); DestroyTexture(ref checker);
        }

        private void OnLostFocus() { EndDrag(); }

        private void OnGUI()
        {
            if (document == null) document = new SpriteSheetAnimationDocument();
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawSettings();
            DrawSourceCanvas();
            DrawFramePanel();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(status, statusType);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("选择原图", EditorStyles.toolbarButton, GUILayout.Width(75))) ChooseSource();
            if (GUILayout.Button("读取切割方案", EditorStyles.toolbarButton, GUILayout.Width(100))) LoadRecipe();
            using (new EditorGUI.DisabledScope(sourcePixels == null))
                if (GUILayout.Button("保存切割方案", EditorStyles.toolbarButton, GUILayout.Width(100))) SaveRecipe();
            GUILayout.FlexibleSpace();
            GUILayout.Label(dirty ? "方案有未保存修改" : "非破坏性切割", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(270));
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            EditorGUILayout.LabelField("原图", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(document.sourceImagePath) ? "尚未选择" : document.sourceImagePath,
                EditorStyles.wordWrappedMiniLabel);
            if (sourcePixels != null)
                EditorGUILayout.LabelField(sourcePixels.width + " × " + sourcePixels.height + " 像素（原文件不会改写）",
                    EditorStyles.miniLabel);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("1. 生成初始裁框", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            document.sliceMode = (SpriteSheetSliceMode)EditorGUILayout.Popup("切割方式", (int)document.sliceMode, SliceModes);
            if (document.sliceMode == SpriteSheetSliceMode.CellCount || document.sliceMode == SpriteSheetSliceMode.CellSize)
            {
                if (document.sliceMode == SpriteSheetSliceMode.CellCount)
                {
                    document.columns = PositiveInt("列数", document.columns);
                    document.rows = PositiveInt("行数", document.rows);
                }
                else
                {
                    document.cellWidth = PositiveInt("单格宽度", document.cellWidth);
                    document.cellHeight = PositiveInt("单格高度", document.cellHeight);
                    EditorGUILayout.LabelField("末尾不足一整格的区域不生成裁框。", EditorStyles.wordWrappedMiniLabel);
                }
                document.marginLeft = NonnegativeInt("左边距", document.marginLeft);
                document.marginTop = NonnegativeInt("上边距", document.marginTop);
                document.marginRight = NonnegativeInt("右边距", document.marginRight);
                document.marginBottom = NonnegativeInt("下边距", document.marginBottom);
                document.gapX = NonnegativeInt("水平格间距", document.gapX);
                document.gapY = NonnegativeInt("垂直格间距", document.gapY);
            }
            else if (document.sliceMode == SpriteSheetSliceMode.TransparentRegions)
            {
                document.alphaThreshold = EditorGUILayout.IntSlider("Alpha 阈值", document.alphaThreshold, 0, 254);
                document.minimumRegionPixels = PositiveInt("最小图块像素数", document.minimumRegionPixels);
                document.mergeDistance = NonnegativeInt("图块合并距离", document.mergeDistance);
                EditorGUILayout.HelpBox("只适合图块之间有透明间隔的图片。断开的披风、特效可能识别成单独一帧，需要手动修框。识别不会删除原图像素。", MessageType.Info);
            }
            else
                EditorGUILayout.HelpBox("点击中间工具栏“新建裁框”，在原图上拖出矩形。没有固定行列要求。", MessageType.Info);
            if (EditorGUI.EndChangeCheck())
            { Changed(); showDraft = true; UpdateDraft(); }
            if (!string.IsNullOrEmpty(draftError) && sourcePixels != null)
                EditorGUILayout.HelpBox(draftError, MessageType.Warning);
            using (new EditorGUI.DisabledScope(sourcePixels == null))
            {
                if (document.sliceMode == SpriteSheetSliceMode.Manual)
                {
                    if (GUILayout.Button("在原图上手动画框")) { editTool = 1; showDraft = false; }
                }
                else if (GUILayout.Button("生成裁框（替换当前裁框）")) Run(GenerateFrames);
                if (GUILayout.Button("添加整张原图为一帧")) Run(() =>
                    AddFrame(new SpriteSheetSliceRect(0, 0, sourcePixels.width, sourcePixels.height)));
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2. 切割 / 对齐输出", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            document.alignAnchors = EditorGUILayout.Toggle("启用逐帧锚点对齐", document.alignAnchors);
            if (document.alignAnchors)
            {
                document.canvasWidth = PositiveInt("输出宽度", document.canvasWidth);
                document.canvasHeight = PositiveInt("输出高度", document.canvasHeight);
                document.targetAnchorX = EditorGUILayout.IntSlider("输出中线 X", document.targetAnchorX, 0, document.canvasWidth);
                document.targetAnchorY = EditorGUILayout.IntSlider("输出底线 Y", document.targetAnchorY, 0, document.canvasHeight);
                document.scalePercent = EditorGUILayout.Slider("统一缩放 %", document.scalePercent, 1, 400);
            }
            if (EditorGUI.EndChangeCheck()) Changed();
            EditorGUILayout.HelpBox(document.alignAnchors
                ? "所有坐标从左上角计算。每帧锚点 → 相同输出锚点；100% 为整数平移，不按人物内容重新缩放。画布不足会报错。"
                : "原样切割：直接复制裁框内的 RGBA 像素。不抠图、不清理碎片、不缩放、不自动改变位置。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!document.alignAnchors || document.frames.Count == 0))
                if (GUILayout.Button("按所有启用帧自动适配画布")) Run(() =>
                { SpriteSheetSlicing.FitCanvas(document); Changed(); SetStatus("已适配画布，未改变任何帧的裁框或锚点。"); });

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("3. 动画和导出", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            document.animationId = EditorGUILayout.TextField("导出名称", document.animationId);
            document.actionId = EditorGUILayout.IntField("动作 ID", document.actionId);
            document.frameRate = Mathf.Clamp(EditorGUILayout.FloatField("播放 FPS", document.frameRate), 1, 120);
            document.loop = EditorGUILayout.Toggle("循环", document.loop);
            document.defaultFacingLeft = EditorGUILayout.Toggle("素材默认朝向左", document.defaultFacingLeft);
            document.outputFolder = EditorGUILayout.TextField("Assets 输出目录", document.outputFolder);
            if (EditorGUI.EndChangeCheck()) Changed();
            if (GUILayout.Button("选择输出目录")) ChooseOutputFolder();
            using (new EditorGUI.DisabledScope(sourcePixels == null || IncludedCount() == 0))
            {
                if (GUILayout.Button("导出 PNG 和动作清单")) Run(() => Export(false));
                if (GUILayout.Button("导出并生成正式序列帧 Clip")) Run(() => Export(true));
            }
            if (GUILayout.Button("从动作清单生成角色预制体…")) SequenceFrameAnimationPrefabBuilder.CreatePrefabInteractive();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSourceCanvas()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            editTool = GUILayout.Toolbar(editTool, EditTools, EditorStyles.toolbarButton);
            if (GUILayout.Button("适配", EditorStyles.toolbarButton, GUILayout.Width(42))) fitRequested = true;
            if (GUILayout.Button("1:1", EditorStyles.toolbarButton, GUILayout.Width(38)))
            { zoom = 1; pan = Vector2.zero; fitRequested = false; }
            GUILayout.Label(Mathf.RoundToInt(zoom * 100) + "%", EditorStyles.miniLabel, GUILayout.Width(42));
            EditorGUILayout.EndHorizontal();
            Rect viewport = GUILayoutUtility.GetRect(100, 10000, 300, 10000,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(viewport, new Color(0.13f, 0.14f, 0.15f));
            GUI.BeginGroup(viewport);
            canvasSize = viewport.size;
            Rect localViewport = new Rect(Vector2.zero, viewport.size);
            if (sourceTexture != null)
            {
                if (fitRequested && viewport.width > 1 && viewport.height > 1)
                {
                    zoom = Mathf.Clamp(Mathf.Min((viewport.width - 30) / sourceTexture.width,
                        (viewport.height - 30) / sourceTexture.height), 0.02f, 16f);
                    pan = Vector2.zero; fitRequested = false;
                }
                origin = canvasSize * 0.5f + pan - new Vector2(sourceTexture.width, sourceTexture.height) * zoom * 0.5f;
                Rect imageRect = new Rect(origin, new Vector2(sourceTexture.width, sourceTexture.height) * zoom);
                DrawChecker(localViewport);
                GUI.DrawTexture(imageRect, sourceTexture, ScaleMode.StretchToFill, true);
                if (showDraft)
                    foreach (var draft in draftFrames) DrawOutline(ViewRect(draft.rect), new Color(0.3f, 0.65f, 1f, 0.5f), 1);
                for (int i = 0; i < document.frames.Count; i++) DrawFrameOverlay(i);
                if (dragAction == DragAction.Create)
                    DrawOutline(ViewRect(pendingRect), Color.yellow, 2);
                HandleCanvasInput(localViewport);
            }
            else GUI.Label(new Rect(15, 15, Mathf.Max(20, viewport.width - 30), 70),
                "选择 PNG / JPEG 原图后，在这里编辑裁框。\n支持任意行列、单格尺寸、透明区域识别或不规则手动裁框。", EditorStyles.wordWrappedLabel);
            GUI.EndGroup();
            EditorGUILayout.LabelField("空白处左键 / 中键 / Alt+左键平移；滚轮缩放；Delete 删除选中框。蓝色框为待生成预览。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawFramePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(290));
            EditorGUILayout.LabelField("输出预览（与导出使用同一算法）", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("−", GUILayout.Width(26))) previewZoom = Mathf.Max(0.125f, previewZoom * 0.8f);
            if (GUILayout.Button("+", GUILayout.Width(26))) previewZoom = Mathf.Min(8, previewZoom * 1.25f);
            if (GUILayout.Button("适配", GUILayout.Width(42))) previewZoom = 1;
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            onionSkin = EditorGUILayout.ToggleLeft(new GUIContent("叠加上一帧", "以青色半透明显示上一张启用帧，仅影响预览。"), onionSkin);
            onionSkinNext = EditorGUILayout.ToggleLeft(new GUIContent("叠加下一帧", "以橙色半透明显示下一张启用帧，仅影响预览。"), onionSkinNext);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck()) { previewDirty = true; Repaint(); }
            Rect preview = GUILayoutUtility.GetRect(100, 280, 215, 215, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && previewDirty) RebuildPreview();
            DrawOutputPreview(preview);
            if (!string.IsNullOrEmpty(previewError)) EditorGUILayout.HelpBox(previewError, MessageType.Warning);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(IncludedCount() == 0))
            {
                if (GUILayout.Button(playing ? "暂停" : "播放"))
                {
                    if (playing) selectedIndex = playbackIndex;
                    playing = !playing; lastPlaybackTime = EditorApplication.timeSinceStartup;
                    if (playing) { playbackIndex = Mathf.Max(0, selectedIndex); AdvanceToIncluded(false); }
                    previewDirty = true;
                }
                if (GUILayout.Button("停止")) { playing = false; previewDirty = true; }
                if (GUILayout.Button("上一帧")) StepFrame(-1);
                if (GUILayout.Button("下一帧")) StepFrame(1);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(playing ? "播放中：帧 " + (playbackIndex + 1) : "选中：帧 " + (selectedIndex + 1), EditorStyles.miniLabel);
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
            DrawSelectedFrameProperties();
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("帧顺序（可取消导出）", EditorStyles.boldLabel);
            for (int i = 0; i < document.frames.Count; i++)
            {
                var frame = document.frames[i];
                EditorGUILayout.BeginHorizontal();
                bool included = EditorGUILayout.Toggle(frame.included, GUILayout.Width(18));
                if (included != frame.included) { frame.included = included; Changed(); }
                if (GUILayout.Button((i + 1) + ". " + frame.name + (i == selectedIndex ? " ◀" : ""), EditorStyles.miniButton)) SelectFrame(i);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedFrameProperties()
        {
            var frame = SelectedFrame();
            if (frame == null) { EditorGUILayout.LabelField("在原图上选择一个裁框。", EditorStyles.wordWrappedMiniLabel); return; }
            EditorGUILayout.LabelField("当前帧（源图左上角为原点）", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            frame.name = EditorGUILayout.TextField("名称", frame.name);
            var rect = frame.rect;
            rect.x = EditorGUILayout.IntField("原图 X", rect.x);
            rect.y = EditorGUILayout.IntField("原图 Y", rect.y);
            rect.width = EditorGUILayout.IntField("宽度", rect.width);
            rect.height = EditorGUILayout.IntField("高度", rect.height);
            if (EditorGUI.EndChangeCheck())
            {
                if (sourcePixels != null && rect.IsInside(sourcePixels.width, sourcePixels.height))
                { SetFrameRect(frame, rect, true); Changed(); }
                else SetStatus("裁框必须完整位于原图内，宽高大于 0。本次无效输入未应用。", MessageType.Warning);
            }
            EditorGUI.BeginChangeCheck();
            frame.anchorX = EditorGUILayout.IntSlider("中线 X（框内）", frame.anchorX, 0, frame.rect.width);
            frame.anchorY = EditorGUILayout.IntSlider("底线 Y（框内）", frame.anchorY, 0, frame.rect.height);
            if (EditorGUI.EndChangeCheck()) Changed();
            if (GUILayout.Button("在原图拖动此帧的中线 / 底线")) { editTool = 2; showDraft = false; }
            if (GUILayout.Button("重置此帧锚点为底部中点"))
            { frame.anchorX = frame.rect.width / 2; frame.anchorY = frame.rect.height; Changed(); }
            if (GUILayout.Button("将此帧锚点按相对位置复制到所有帧"))
            {
                if (EditorUtility.DisplayDialog("复制锚点", "这会覆盖其它帧的人工锚点，是否继续？", "复制", "取消"))
                {
                    foreach (var other in document.frames)
                    {
                        other.anchorX = Mathf.RoundToInt((float)frame.anchorX / frame.rect.width * other.rect.width);
                        other.anchorY = Mathf.RoundToInt((float)frame.anchorY / frame.rect.height * other.rect.height);
                    }
                    Changed();
                }
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("前移")) MoveFrame(-1);
            if (GUILayout.Button("后移")) MoveFrame(1);
            if (GUILayout.Button("删除")) DeleteSelected();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFrameOverlay(int index)
        {
            var frame = document.frames[index];
            Rect rect = ViewRect(frame.rect);
            bool selected = index == selectedIndex;
            Color color = selected ? Color.yellow : frame.included ? new Color(0.2f, 1, 0.5f) : Color.gray;
            DrawOutline(rect, color, selected ? 2 : 1);
            GUI.Label(new Rect(rect.x + 3, rect.y + 2, Mathf.Max(25, rect.width - 6), 20), (index + 1).ToString(), EditorStyles.whiteMiniLabel);
            if (!selected) return;
            Vector2 anchor = SourceToView(new Vector2(frame.rect.x + frame.anchorX, frame.rect.y + frame.anchorY));
            EditorGUI.DrawRect(new Rect(anchor.x - 1, rect.y, 2, rect.height), new Color(1, 0.25f, 0.25f));
            EditorGUI.DrawRect(new Rect(rect.x, anchor.y - 1, rect.width, 2), new Color(0.2f, 0.65f, 1));
            GUI.Label(new Rect(anchor.x + 4, rect.y + 17, 110, 18), "中线 " + frame.anchorX, EditorStyles.whiteMiniLabel);
            GUI.Label(new Rect(rect.x + 4, anchor.y - 19, 110, 18), "底线 " + frame.anchorY, EditorStyles.whiteMiniLabel);
            if (editTool == 0)
                for (int h = 0; h < 8; h++)
                {
                    Vector2 point = HandlePoint(rect, h);
                    EditorGUI.DrawRect(new Rect(point.x - 3, point.y - 3, 6, 6), Color.yellow);
                }
        }

        private void DrawOutputPreview(Rect rect)
        {
            GUI.BeginGroup(rect);
            DrawChecker(new Rect(Vector2.zero, rect.size));
            // 每帧都使用同一参考画布比例，不随当前帧边界自动放大缩小。
            GetPreviewReferenceSize(out int width, out int height);
            float scale = Mathf.Min((rect.width - 12) / width, (rect.height - 12) / height) * previewZoom;
            Vector2 center = rect.size * 0.5f;
            Rect draw = new Rect(center.x - width * scale * 0.5f, center.y - height * scale * 0.5f, width * scale, height * scale);
            if (onionSkin && previousTexture != null)
            {
                Color saved = GUI.color; GUI.color = new Color(0.4f, 0.9f, 1, 0.35f);
                DrawPreviewTexture(previousTexture, draw, scale, height); GUI.color = saved;
            }
            if (onionSkinNext && nextTexture != null)
            {
                Color saved = GUI.color; GUI.color = new Color(1, 0.65f, 0.3f, 0.35f);
                DrawPreviewTexture(nextTexture, draw, scale, height); GUI.color = saved;
            }
            if (frameTexture != null) DrawPreviewTexture(frameTexture, draw, scale, height);
            if (document.alignAnchors)
            {
                float x = draw.x + document.targetAnchorX * scale, y = draw.y + document.targetAnchorY * scale;
                EditorGUI.DrawRect(new Rect(x, draw.y, 1, draw.height), new Color(1, 0.25f, 0.25f));
                EditorGUI.DrawRect(new Rect(draw.x, y, draw.width, 1), new Color(0.2f, 0.65f, 1));
            }
            GUI.EndGroup();
        }

        private static void DrawPreviewTexture(Texture2D texture, Rect reference, float scale, int referenceHeight)
        {
            // 原样切割尺寸不同时以底部中点放置，避免逐帧适配预览造成假跳动。
            Rect target = new Rect(reference.center.x - texture.width * scale * 0.5f,
                reference.y + (referenceHeight - texture.height) * scale,
                texture.width * scale, texture.height * scale);
            GUI.DrawTexture(target, texture, ScaleMode.StretchToFill, true);
        }

        private void GetPreviewReferenceSize(out int width, out int height)
        {
            width = document.alignAnchors ? document.canvasWidth : 1;
            height = document.alignAnchors ? document.canvasHeight : 1;
            if (!document.alignAnchors)
                foreach (var frame in document.frames)
                { width = Mathf.Max(width, frame.rect.width); height = Mathf.Max(height, frame.rect.height); }
            width = Mathf.Max(1, width); height = Mathf.Max(1, height);
        }

        private void DrawChecker(Rect rect)
        {
            if (checker == null)
            {
                checker = new Texture2D(16, 16, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                var pixels = new Color32[256];
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                    { byte value = (byte)((x / 8 + y / 8) % 2 == 0 ? 48 : 60); pixels[y * 16 + x] = new Color32(value, value, value, 255); }
                checker.SetPixels32(pixels); checker.Apply();
            }
            GUI.DrawTextureWithTexCoords(rect, checker, new Rect(0, 0, rect.width / 16, rect.height / 16));
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        /// <summary>视口不缩放，只对图像和裁框做坐标映射；拖动使用独占热控件。</summary>
        private void HandleCanvasInput(Rect viewport)
        {
            Event e = Event.current;
            canvasControl = GUIUtility.GetControlID("SpriteSheetSlicerCanvas".GetHashCode(), FocusType.Passive);
            bool owns = GUIUtility.hotControl == canvasControl;
            Vector2 mouse = e.mousePosition;
            if (e.type == EventType.ScrollWheel && viewport.Contains(mouse))
            {
                Vector2 before = ViewToSource(mouse);
                zoom = Mathf.Clamp(zoom * Mathf.Pow(1.12f, -e.delta.y), 0.02f, 16);
                pan = mouse - canvasSize * 0.5f - (before - new Vector2(sourcePixels.width, sourcePixels.height) * 0.5f) * zoom;
                fitRequested = false; e.Use(); Repaint(); return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && owns)
            {
                var frame = SelectedFrame();
                if (frame != null && dragAction != DragAction.Pan && dragAction != DragAction.Create)
                { frame.rect = startRect; frame.anchorX = startAnchorX; frame.anchorY = startAnchorY; Changed(); }
                pan = startPan; EndDrag(); e.Use(); Repaint(); return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete
                && GUIUtility.keyboardControl == 0 && !owns && selectedIndex >= 0)
            { DeleteSelected(); e.Use(); return; }

            if (e.type == EventType.MouseDown && viewport.Contains(mouse)
                && (e.button == 0 || e.button == 2))
            {
                GUIUtility.keyboardControl = 0;
                dragStart = ViewToSource(mouse); startPan = pan;
                var selected = SelectedFrame();
                if (selected != null)
                { startRect = selected.rect; startAnchorX = selected.anchorX; startAnchorY = selected.anchorY; }
                resizeHandle = -1;
                if (e.button == 2 || e.alt) dragAction = DragAction.Pan;
                else if (editTool == 1 && IsOnImage(dragStart))
                {
                    if (document.frames.Count >= SpriteSheetSlicing.MaximumFrames)
                    { SetStatus("裁框数量已达到 4096 上限。", MessageType.Warning); return; }
                    dragAction = DragAction.Create;
                    pendingRect = new SpriteSheetSliceRect(Mathf.FloorToInt(dragStart.x), Mathf.FloorToInt(dragStart.y), 1, 1);
                }
                else
                {
                    if (selected != null && editTool == 0)
                    {
                        Rect box = ViewRect(selected.rect);
                        for (int i = 0; i < 8; i++)
                            if (Vector2.Distance(mouse, HandlePoint(box, i)) <= 7) { resizeHandle = i; break; }
                    }
                    if (resizeHandle >= 0) dragAction = DragAction.Resize;
                    else if (editTool == 2 && selected != null && Expanded(ViewRect(selected.rect), 8).Contains(mouse))
                    {
                        Vector2 anchor = SourceToView(new Vector2(selected.rect.x + selected.anchorX, selected.rect.y + selected.anchorY));
                        bool nearX = Mathf.Abs(mouse.x - anchor.x) <= 8;
                        bool nearY = Mathf.Abs(mouse.y - anchor.y) <= 8;
                        dragAction = nearX && !nearY ? DragAction.AnchorX : nearY && !nearX ? DragAction.AnchorY : DragAction.AnchorBoth;
                        ApplyAnchor(mouse, dragAction); Changed();
                    }
                    else
                    {
                        int hit = HitFrame(mouse);
                        if (hit >= 0)
                        {
                            SelectFrame(hit); selected = SelectedFrame(); startRect = selected.rect;
                            startAnchorX = selected.anchorX; startAnchorY = selected.anchorY;
                            dragAction = editTool == 0 ? DragAction.Move : DragAction.None;
                        }
                        else dragAction = DragAction.Pan;
                    }
                }
                showDraft = false; playing = false;
                if (dragAction != DragAction.None) GUIUtility.hotControl = canvasControl;
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.MouseDrag && owns)
            {
                Vector2 point = ViewToSource(mouse);
                Vector2 delta = point - dragStart;
                var frame = SelectedFrame();
                if (dragAction == DragAction.Pan)
                    pan += e.delta;
                else if (dragAction == DragAction.Create)
                {
                    int x1 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(dragStart.x, point.x)), 0, sourcePixels.width - 1);
                    int y1 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(dragStart.y, point.y)), 0, sourcePixels.height - 1);
                    int x2 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(dragStart.x, point.x)), x1 + 1, sourcePixels.width);
                    int y2 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(dragStart.y, point.y)), y1 + 1, sourcePixels.height);
                    pendingRect = new SpriteSheetSliceRect(x1, y1, x2 - x1, y2 - y1);
                }
                else if (frame != null && dragAction == DragAction.Move)
                {
                    frame.rect = new SpriteSheetSliceRect(
                        Mathf.Clamp(startRect.x + Mathf.RoundToInt(delta.x), 0, sourcePixels.width - startRect.width),
                        Mathf.Clamp(startRect.y + Mathf.RoundToInt(delta.y), 0, sourcePixels.height - startRect.height),
                        startRect.width, startRect.height);
                    Changed();
                }
                else if (frame != null && dragAction == DragAction.Resize)
                {
                    int left = startRect.x, top = startRect.y, right = startRect.Right, bottom = startRect.Bottom;
                    int dx = Mathf.RoundToInt(delta.x), dy = Mathf.RoundToInt(delta.y);
                    if (resizeHandle == 0 || resizeHandle == 6 || resizeHandle == 7) left = Mathf.Clamp(left + dx, 0, right - 1);
                    if (resizeHandle == 0 || resizeHandle == 1 || resizeHandle == 2) top = Mathf.Clamp(top + dy, 0, bottom - 1);
                    if (resizeHandle == 2 || resizeHandle == 3 || resizeHandle == 4) right = Mathf.Clamp(right + dx, left + 1, sourcePixels.width);
                    if (resizeHandle == 4 || resizeHandle == 5 || resizeHandle == 6) bottom = Mathf.Clamp(bottom + dy, top + 1, sourcePixels.height);
                    frame.rect = new SpriteSheetSliceRect(left, top, right - left, bottom - top);
                    frame.anchorX = Mathf.Clamp(startRect.x + startAnchorX - left, 0, frame.rect.width);
                    frame.anchorY = Mathf.Clamp(startRect.y + startAnchorY - top, 0, frame.rect.height);
                    Changed();
                }
                else if (frame != null)
                { ApplyAnchor(mouse, dragAction); Changed(); }
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.MouseUp && owns)
            {
                if (dragAction == DragAction.Create) Run(() => AddFrame(pendingRect));
                EndDrag(); e.Use(); Repaint();
            }
        }

        private void ApplyAnchor(Vector2 mouse, DragAction action)
        {
            var frame = SelectedFrame();
            if (frame == null) return;
            Vector2 point = ViewToSource(mouse);
            if (action == DragAction.AnchorBoth || action == DragAction.AnchorX)
                frame.anchorX = Mathf.Clamp(Mathf.RoundToInt(point.x) - frame.rect.x, 0, frame.rect.width);
            if (action == DragAction.AnchorBoth || action == DragAction.AnchorY)
                frame.anchorY = Mathf.Clamp(Mathf.RoundToInt(point.y) - frame.rect.y, 0, frame.rect.height);
        }

        private void EndDrag()
        {
            if (canvasControl != 0 && GUIUtility.hotControl == canvasControl) GUIUtility.hotControl = 0;
            dragAction = DragAction.None;
        }

        private Vector2 SourceToView(Vector2 point) => origin + point * zoom;
        private Vector2 ViewToSource(Vector2 point) => (point - origin) / zoom;
        private Rect ViewRect(SpriteSheetSliceRect rect) => new Rect(SourceToView(new Vector2(rect.x, rect.y)), new Vector2(rect.width, rect.height) * zoom);
        private bool IsOnImage(Vector2 point) => point.x >= 0 && point.y >= 0 && point.x < sourcePixels.width && point.y < sourcePixels.height;
        private static Rect Expanded(Rect rect, float amount) => new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2, rect.height + amount * 2);
        private static Vector2 HandlePoint(Rect rect, int handle)
        {
            switch (handle)
            {
                case 0: return rect.min;
                case 1: return new Vector2(rect.center.x, rect.y);
                case 2: return new Vector2(rect.xMax, rect.y);
                case 3: return new Vector2(rect.xMax, rect.center.y);
                case 4: return rect.max;
                case 5: return new Vector2(rect.center.x, rect.yMax);
                case 6: return new Vector2(rect.x, rect.yMax);
                default: return new Vector2(rect.x, rect.center.y);
            }
        }
        private int HitFrame(Vector2 mouse)
        {
            // 选最小的命中框，大框不会挡住内部的小框。
            int hit = -1; long area = long.MaxValue;
            for (int i = 0; i < document.frames.Count; i++)
            {
                var rect = document.frames[i].rect;
                if (ViewRect(rect).Contains(mouse) && (long)rect.width * rect.height < area)
                { hit = i; area = (long)rect.width * rect.height; }
            }
            return hit;
        }

        private void ChooseSource()
        {
            string path = EditorUtility.OpenFilePanel("选择原始帧图（PNG / JPEG）", Application.dataPath, "png,jpg,jpeg");
            if (string.IsNullOrWhiteSpace(path) || !ConfirmDiscard()) return;
            Run(() =>
            {
                LoadSource(path, false); document.frames.Clear(); selectedIndex = -1;
                document.animationId = Path.GetFileNameWithoutExtension(path); Changed();
                showDraft = true; UpdateDraft();
                SetStatus("已读取原图。先生成或手动画框，再逐帧预览；不会自动改变图片内容。");
            });
        }

        /// <summary>只读取 PNG/JPEG；Unity 坐标翻转在此集中处理，算法层始终使用左上角原点。</summary>
        private void LoadSource(string path, bool verifyIdentity)
        {
            string absolute = Path.GetFullPath(path.StartsWith("Assets/", StringComparison.Ordinal)
                ? Path.Combine(Application.dataPath, "..", path) : path);
            byte[] bytes = File.ReadAllBytes(absolute);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(bytes, false)) throw new ArgumentException("无法解码图片，请使用 PNG 或 JPEG。");
                SpriteSheetSlicing.ValidatePixels(texture.width, texture.height);
                if (verifyIdentity && (texture.width != document.sourceWidth || texture.height != document.sourceHeight
                    || !string.Equals(hash, document.sourceSha256, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("原图内容或尺寸与保存方案不一致。请重新选择正确的原图，不能把旧裁框静默套到另一张图片。");
                Color32[] colors = texture.GetPixels32();
                var rgba = new byte[colors.Length * 4];
                for (int y = 0; y < texture.height; y++)
                    for (int x = 0; x < texture.width; x++)
                    {
                        Color32 color = colors[(texture.height - 1 - y) * texture.width + x];
                        int i = (y * texture.width + x) * 4;
                        rgba[i] = color.r; rgba[i + 1] = color.g; rgba[i + 2] = color.b; rgba[i + 3] = color.a;
                    }
                var pixels = new SpriteSheetPixels(texture.width, texture.height, rgba);
                if (verifyIdentity)
                    foreach (var frame in document.frames) SpriteSheetSlicing.ValidateFrame(pixels, frame);
                texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
                texture.hideFlags = HideFlags.HideAndDontSave;
                DestroyTexture(ref sourceTexture); sourceTexture = texture; texture = null;
                sourcePixels = pixels;
                document.sourceImagePath = absolute; document.sourceSha256 = hash;
                document.sourceWidth = pixels.width; document.sourceHeight = pixels.height;
                fitRequested = true; playing = false; previewDirty = true; UpdateDraft();
            }
            finally { if (texture != null) DestroyImmediate(texture); }
        }

        private void UpdateDraft()
        {
            draftFrames.Clear(); draftError = "";
            if (sourcePixels == null) return;
            try
            {
                if (document.sliceMode == SpriteSheetSliceMode.CellCount)
                    draftFrames = SpriteSheetSlicing.GridByCount(sourcePixels.width, sourcePixels.height, document);
                else if (document.sliceMode == SpriteSheetSliceMode.CellSize)
                    draftFrames = SpriteSheetSlicing.GridBySize(sourcePixels.width, sourcePixels.height, document);
            }
            catch (ArgumentException exception) { draftError = exception.Message; }
        }

        private void GenerateFrames()
        {
            List<SpriteSheetFrameData> frames;
            if (document.sliceMode == SpriteSheetSliceMode.TransparentRegions)
                frames = SpriteSheetSlicing.DetectTransparentRegions(sourcePixels, document.alphaThreshold,
                    document.minimumRegionPixels, document.mergeDistance);
            else
            {
                UpdateDraft();
                if (!string.IsNullOrEmpty(draftError)) throw new ArgumentException(draftError);
                frames = new List<SpriteSheetFrameData>(draftFrames);
            }
            if (frames.Count == 0) throw new ArgumentException("没有检测到可用裁框。可调整参数，或改用手动裁框。");
            if (document.frames.Count > 0 && !EditorUtility.DisplayDialog("替换裁框",
                "将重新生成 " + frames.Count + " 个裁框，覆盖当前裁框和逐帧锚点。是否继续？", "替换", "取消")) return;
            document.frames = frames; selectedIndex = 0; playing = false; showDraft = false; Changed();
            SetStatus("已生成 " + frames.Count + " 个可编辑裁框，顺序从左到右、从上到下。尚未导出或改变源图。");
        }

        private void AddFrame(SpriteSheetSliceRect rect)
        {
            if (!rect.IsInside(sourcePixels.width, sourcePixels.height)) throw new ArgumentException("裁框无效。");
            if (document.frames.Count >= SpriteSheetSlicing.MaximumFrames) throw new ArgumentException("裁框数量达到上限。");
            document.frames.Add(new SpriteSheetFrameData(rect, "frame_" + document.frames.Count.ToString("D4")));
            selectedIndex = document.frames.Count - 1; playing = false; showDraft = false; Changed();
        }

        private void SetFrameRect(SpriteSheetFrameData frame, SpriteSheetSliceRect rect, bool preserveAnchor)
        {
            int x = frame.rect.x + frame.anchorX, y = frame.rect.y + frame.anchorY;
            frame.rect = rect;
            frame.anchorX = Mathf.Clamp(preserveAnchor ? x - rect.x : frame.anchorX, 0, rect.width);
            frame.anchorY = Mathf.Clamp(preserveAnchor ? y - rect.y : frame.anchorY, 0, rect.height);
        }

        private void SelectFrame(int index)
        { selectedIndex = index; playing = false; showDraft = false; previewDirty = true; Repaint(); }
        private SpriteSheetFrameData SelectedFrame() => selectedIndex >= 0 && selectedIndex < document.frames.Count ? document.frames[selectedIndex] : null;
        private void DeleteSelected()
        {
            if (SelectedFrame() == null) return;
            document.frames.RemoveAt(selectedIndex);
            selectedIndex = Mathf.Min(selectedIndex, document.frames.Count - 1); playing = false; Changed();
        }
        private void MoveFrame(int delta)
        {
            int other = selectedIndex + delta;
            if (SelectedFrame() == null || other < 0 || other >= document.frames.Count) return;
            var frame = document.frames[selectedIndex]; document.frames[selectedIndex] = document.frames[other]; document.frames[other] = frame;
            selectedIndex = other; playing = false; Changed();
        }
        private int IncludedCount()
        { int count = 0; foreach (var frame in document.frames) if (frame != null && frame.included) count++; return count; }

        private void UpdatePlayback()
        {
            if (!playing || IncludedCount() == 0) return;
            double now = EditorApplication.timeSinceStartup;
            double interval = 1d / Math.Max(1, document.frameRate);
            if (now - lastPlaybackTime < interval) return;
            lastPlaybackTime = now;
            int next = playbackIndex + 1;
            while (next < document.frames.Count && !document.frames[next].included) next++;
            if (next >= document.frames.Count)
            {
                if (!document.loop) { playing = false; selectedIndex = playbackIndex; previewDirty = true; Repaint(); return; }
                next = 0;
            }
            playbackIndex = next; AdvanceToIncluded(false); previewDirty = true; Repaint();
        }

        private void AdvanceToIncluded(bool backwards)
        {
            if (document.frames.Count == 0) return;
            for (int tries = 0; tries < document.frames.Count; tries++)
            {
                playbackIndex = (playbackIndex + document.frames.Count) % document.frames.Count;
                if (document.frames[playbackIndex].included) return;
                playbackIndex += backwards ? -1 : 1;
            }
        }

        private void StepFrame(int delta)
        {
            if (IncludedCount() == 0) return;
            playing = false; playbackIndex = Mathf.Max(0, selectedIndex) + delta;
            AdvanceToIncluded(delta < 0); selectedIndex = playbackIndex; previewDirty = true; Repaint();
        }

        private void RebuildPreview()
        {
            previewDirty = false; previewError = "";
            DestroyTexture(ref frameTexture); DestroyTexture(ref previousTexture); DestroyTexture(ref nextTexture);
            int index = playing ? playbackIndex : selectedIndex;
            if (sourcePixels == null || index < 0 || index >= document.frames.Count) return;
            try
            {
                frameTexture = ToTexture(SpriteSheetSlicing.Render(sourcePixels, document.frames[index], document));
                int previous = onionSkin ? FindAdjacentPreviewFrame(index, -1) : -1;
                if (previous >= 0)
                    previousTexture = ToTexture(SpriteSheetSlicing.Render(sourcePixels, document.frames[previous], document));
                int next = onionSkinNext ? FindAdjacentPreviewFrame(index, 1) : -1;
                if (next >= 0)
                    nextTexture = ToTexture(SpriteSheetSlicing.Render(sourcePixels, document.frames[next], document));
            }
            catch (ArgumentException exception) { previewError = exception.Message; }
        }

        /// <summary>按播放顺序寻找相邻启用帧；仅循环动画跨越首尾，不重复叠加当前帧。</summary>
        private int FindAdjacentPreviewFrame(int index, int direction)
        {
            int count = document.frames.Count;
            for (int step = 1; step < count; step++)
            {
                int candidate = index + direction * step;
                if (candidate < 0 || candidate >= count)
                {
                    if (!document.loop) return -1;
                    candidate = (candidate % count + count) % count;
                }
                if (document.frames[candidate].included) return candidate;
            }
            return -1;
        }

        private static Texture2D ToTexture(SpriteSheetPixels pixels)
        {
            var colors = new Color32[pixels.width * pixels.height];
            for (int y = 0; y < pixels.height; y++)
                for (int x = 0; x < pixels.width; x++)
                {
                    int i = (y * pixels.width + x) * 4;
                    colors[(pixels.height - 1 - y) * pixels.width + x] = new Color32(
                        pixels.rgba[i], pixels.rgba[i + 1], pixels.rgba[i + 2], pixels.rgba[i + 3]);
                }
            var texture = new Texture2D(pixels.width, pixels.height, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(colors); texture.Apply(false, false); return texture;
        }

        private void Changed() { dirty = true; previewDirty = true; Repaint(); }
        private void SetStatus(string message, MessageType type = MessageType.Info)
        { status = message; statusType = type; Repaint(); }
        private void Run(Action action)
        {
            try { action(); }
            catch (Exception exception)
            { SetStatus(exception.Message, MessageType.Error); Debug.LogError("[SpriteSheetSlicer] " + exception); }
        }
        private static void DestroyTexture(ref Texture2D texture)
        { if (texture != null) DestroyImmediate(texture); texture = null; }
        private static int PositiveInt(string label, int value) => Mathf.Clamp(EditorGUILayout.IntField(label, value), 1, 100000);
        private static int NonnegativeInt(string label, int value) => Mathf.Clamp(EditorGUILayout.IntField(label, value), 0, 100000);
        private bool ConfirmDiscard() => !dirty || EditorUtility.DisplayDialog("放弃当前修改？", "当前切割方案尚未保存。是否继续？", "继续", "取消");

        [Serializable] private sealed class RecipeHeader { public int formatVersion = 0; }

        private void SaveRecipe()
        {
            string path = EditorUtility.SaveFilePanel("保存新版切割方案", Application.dataPath,
                SafeName(document.animationId) + ".slices", "json");
            if (string.IsNullOrWhiteSpace(path)) return;
            Run(() =>
            {
                if (string.Equals(Path.GetFullPath(path), document.sourceImagePath, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("不能用方案文件覆盖原图。");
                File.WriteAllText(path, JsonUtility.ToJson(document, true));
                if (TryAssetPath(path, out string assetPath)) AssetDatabase.ImportAsset(assetPath);
                dirty = false; SetStatus("已保存裁框、逐帧锚点和输出参数：" + path);
            });
        }

        private void LoadRecipe()
        {
            string path = EditorUtility.OpenFilePanel("读取新版 .slices.json 切割方案", Application.dataPath, "json");
            if (string.IsNullOrWhiteSpace(path) || !ConfirmDiscard()) return;
            Run(() =>
            {
                string json = File.ReadAllText(path);
                var header = JsonUtility.FromJson<RecipeHeader>(json);
                if (header == null || header.formatVersion != 2)
                    throw new ArgumentException("这不是新版 .slices.json 方案。旧工具的处理参数不能可靠还原为原图裁框；已有 PNG / Clip 仍可正常使用。");
                var loaded = JsonUtility.FromJson<SpriteSheetAnimationDocument>(json);
                if (loaded == null || loaded.frames == null || loaded.frames.Count > SpriteSheetSlicing.MaximumFrames
                    || string.IsNullOrWhiteSpace(loaded.sourceSha256) || !Enum.IsDefined(typeof(SpriteSheetSliceMode), loaded.sliceMode))
                    throw new ArgumentException("方案数据不完整或版本不受支持。");
                string source = loaded.sourceImagePath;
                if (!File.Exists(source))
                {
                    source = EditorUtility.OpenFilePanel("原图已移动，请重新定位同一张原图", Path.GetDirectoryName(path), "png,jpg,jpeg");
                    if (string.IsNullOrWhiteSpace(source)) return;
                }
                var before = document;
                document = loaded;
                try { LoadSource(source, true); }
                catch { document = before; throw; }
                selectedIndex = loaded.frames.Count > 0 ? 0 : -1;
                dirty = false; showDraft = loaded.frames.Count == 0; playing = false; previewDirty = true;
                SetStatus("已恢复裁框和逐帧锚点，原图尺寸与 SHA-256 校验一致。");
            });
        }

        private void ChooseOutputFolder()
        {
            string path = EditorUtility.OpenFolderPanel("选择 Assets 内输出目录", Application.dataPath, "");
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!TryAssetPath(path, out string assetPath))
            { SetStatus("请选择当前项目 Assets 目录内部。", MessageType.Warning); return; }
            document.outputFolder = assetPath; Changed();
        }

        private static bool TryAssetPath(string path, out string assetPath)
        {
            string absolute = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            if (string.Equals(absolute, assets, StringComparison.OrdinalIgnoreCase)) { assetPath = "Assets"; return true; }
            if (absolute.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase))
            { assetPath = "Assets/" + absolute.Substring(assets.Length + 1); return true; }
            assetPath = ""; return false;
        }

        private static string AbsoluteAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !(assetPath == "Assets" || assetPath.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new ArgumentException("输出路径必须以 Assets/ 开头。");
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            if (!TryAssetPath(absolute, out _)) throw new ArgumentException("输出路径越过 Assets 边界。");
            return absolute;
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "sprite_animation";
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            name = name.Trim().Trim('.');
            if (name.Length == 0) return "sprite_animation";
            return name;
        }

        /// <summary>
        /// 所有帧先校验、再在 Library 临时目录生成，完成后才复制到 Assets。
        /// 重导出不删除 PNG 的 meta，不销毁旧 Clip，避免破坏已经存在的 GUID 引用。
        /// 不清空输出文件夹，也不删除目录内不属于本次导出的其它资源。
        /// </summary>
        private void Export(bool generateClip)
        {
            if (sourcePixels == null || IncludedCount() == 0) throw new ArgumentException("请选择原图并启用至少一帧。");
            var frames = document.frames.FindAll(frame => frame != null && frame.included);
            var sizes = new List<Vector2Int>();
            int maximumWidth = 0, maximumHeight = 0;
            long totalBytes = 0;
            foreach (var frame in frames)
            {
                SpriteSheetSlicing.ValidateFrame(sourcePixels, frame);
                int width = frame.rect.width, height = frame.rect.height;
                if (document.alignAnchors)
                {
                    width = document.canvasWidth; height = document.canvasHeight;
                    SpriteSheetSlicing.GetPlacement(frame, document, out int sw, out int sh, out int ox, out int oy);
                    if (ox < 0 || oy < 0 || (long)ox + sw > width || (long)oy + sh > height)
                        throw new ArgumentException("“" + frame.name + "”超出输出画布，请先自动适配画布。尚未写出任何文件。");
                    if (document.targetAnchorX < 0 || document.targetAnchorX > width
                        || document.targetAnchorY < 0 || document.targetAnchorY > height)
                        throw new ArgumentException("输出锚点越过输出画布。");
                }
                SpriteSheetSlicing.ValidatePixels(width, height);
                if (width > 16384 || height > 16384)
                    throw new ArgumentException("Unity Sprite 导出单边不能超过 16384 像素；不会自动缩小，请调整裁框或输出尺寸。");
                sizes.Add(new Vector2Int(width, height));
                totalBytes += (long)width * height * 4;
                maximumWidth = Mathf.Max(maximumWidth, width); maximumHeight = Mathf.Max(maximumHeight, height);
            }
            if (totalBytes > 512L * 1024 * 1024) throw new ArgumentException("本次未压缩输出超过 512 MiB，请减少帧数或分批导出。");
            if (generateClip)
            {
                if (document.actionId <= 0) throw new ArgumentException("生成正式 Clip 时，动作 ID 必须大于 0。");
                foreach (var size in sizes)
                    if (size != sizes[0]) throw new ArgumentException("原样裁框尺寸不一致，可以分别导出 PNG。生成角色 Clip 请先开启逐帧锚点对齐，统一输出画布。");
            }

            string folder = document.outputFolder.Replace('\\', '/').TrimEnd('/') + "/" + SafeName(document.animationId);
            string absoluteFolder = AbsoluteAssetPath(folder);
            string clipPath = "Assets/Resources/TryGameBuildRes/clip_sprite/sequence_animation/clip_" + document.actionId + "_1.asset";
            var existingClip = generateClip ? AssetDatabase.LoadAssetAtPath<SequenceFrameClip>(clipPath) : null;
            if (generateClip && File.Exists(AbsoluteAssetPath(clipPath)) && existingClip == null)
                throw new ArgumentException("Clip 目标路径已经被其它资源占用，无法覆盖。");
            if ((Directory.Exists(absoluteFolder) && Directory.GetFileSystemEntries(absoluteFolder).Length > 0) || existingClip != null)
                if (!EditorUtility.DisplayDialog("确认导出", "将更新同名帧图和清单" + (existingClip != null ? "，并更新现有 Clip（保留 GUID）" : "")
                    + "。不会清空目录或删除其它文件。\n" + folder, "继续", "取消")) return;
            for (int i = 0; i < frames.Count; i++)
                if (string.Equals(Path.Combine(absoluteFolder, "frame_" + i.ToString("D4") + ".png"), document.sourceImagePath, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("输出会覆盖原图，请换一个输出目录或导出名称。");

            Vector2 pivot = document.alignAnchors ? new Vector2((float)document.targetAnchorX / document.canvasWidth,
                1f - (float)document.targetAnchorY / document.canvasHeight) : new Vector2(0.5f, 0);
            var sequence = new SequenceFrameAnimationDocument
            {
                animationId = SafeName(document.animationId), displayName = document.animationId, actionId = document.actionId,
                frameRate = document.frameRate, loop = document.loop, defaultFacingLeft = document.defaultFacingLeft,
                canvasWidth = maximumWidth, canvasHeight = maximumHeight, pivotNormalized = pivot,
            };
            string stage = Path.Combine(Application.dataPath, "..", "Library", "SpriteSheetSlicer", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                for (int i = 0; i < frames.Count; i++)
                {
                    string name = "frame_" + i.ToString("D4") + ".png";
                    Texture2D rendered = ToTexture(SpriteSheetSlicing.Render(sourcePixels, frames[i], document));
                    try { File.WriteAllBytes(Path.Combine(stage, name), rendered.EncodeToPNG()); }
                    finally { DestroyImmediate(rendered); }
                    sequence.frames.Add(new SequenceFrameData { sourceFrameIndex = document.frames.IndexOf(frames[i]),
                        sourceFilePath = Path.Combine(absoluteFolder, name), exportedAssetPath = folder + "/" + name, selected = true });
                }
                string basename = SafeName(document.animationId);
                File.WriteAllText(Path.Combine(stage, basename + ".sequence.json"), JsonUtility.ToJson(sequence, true));
                File.WriteAllText(Path.Combine(stage, basename + ".slices.json"), JsonUtility.ToJson(document, true));
                Directory.CreateDirectory(absoluteFolder);
                foreach (string stagedFile in Directory.GetFiles(stage))
                    File.Copy(stagedFile, Path.Combine(absoluteFolder, Path.GetFileName(stagedFile)), true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                for (int i = 0; i < sequence.frames.Count; i++)
                    ConfigureImporter(sequence.frames[i].exportedAssetPath, pivot, sizes[i]);
                if (generateClip) SaveClip(sequence, clipPath, existingClip);
                dirty = false;
                SetStatus("已导出 " + frames.Count + " 帧、动作清单和新版切割方案" + (generateClip ? "，并生成正式 Clip" : "")
                    + "。源图未改写；输出目录：" + folder);
                Debug.Log("[SpriteSheetSlicer] Export frames=" + frames.Count + " aligned=" + document.alignAnchors
                    + " scale=" + (document.alignAnchors ? document.scalePercent : 100) + " sourceSHA256=" + document.sourceSha256
                    + " output=" + folder);
            }
            finally
            {
                // 这里只清理由本次操作创建的扁平临时目录，不递归删除，不触碰 Assets。
                foreach (string file in Directory.GetFiles(stage)) File.Delete(file);
                Directory.Delete(stage, false);
            }
        }

        private static void ConfigureImporter(string path, Vector2 pivot, Vector2Int size)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("PNG 导入器不存在：" + path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            importer.npotScale = TextureImporterNPOTScale.None; importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100;
            // 覆盖旧文件时，平台级覆盖也可能偷偷缩图或压缩，统一恢复为本工具的默认导出设置。
            foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                importer.ClearPlatformTextureSettings(platform);
            importer.alphaIsTransparency = false; // 不让导入器对透明区域执行颜色扩散。
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(size.x, size.y)), 32, 16384);
            importer.SaveAndReimport();
        }

        private static void SaveClip(SequenceFrameAnimationDocument sequence, string path, SequenceFrameClip existing)
        {
            var temporary = CreateInstance<SequenceFrameClip>();
            try
            {
                var serialized = new SerializedObject(temporary);
                serialized.FindProperty("actionId").intValue = sequence.actionId;
                var frames = serialized.FindProperty("frames"); frames.arraySize = sequence.frames.Count;
                for (int i = 0; i < sequence.frames.Count; i++)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sequence.frames[i].exportedAssetPath);
                    if (sprite == null) throw new InvalidOperationException("导出帧未能导入为 Sprite：" + sequence.frames[i].exportedAssetPath);
                    frames.GetArrayElementAtIndex(i).objectReferenceValue = sprite;
                }
                serialized.FindProperty("frameRate").floatValue = sequence.frameRate;
                serialized.FindProperty("loop").boolValue = sequence.loop;
                serialized.FindProperty("defaultFacingLeft").boolValue = sequence.defaultFacingLeft;
                serialized.FindProperty("canvasWidth").intValue = sequence.canvasWidth;
                serialized.FindProperty("canvasHeight").intValue = sequence.canvasHeight;
                serialized.FindProperty("pivotNormalized").vector2Value = sequence.pivotNormalized;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (!temporary.TryValidate(out string error)) throw new InvalidOperationException(error);
                if (existing != null)
                { EditorUtility.CopySerialized(temporary, existing); EditorUtility.SetDirty(existing); }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(AbsoluteAssetPath(path)));
                    temporary.name = Path.GetFileNameWithoutExtension(path);
                    AssetDatabase.CreateAsset(temporary, path); existing = temporary; temporary = null;
                }
                AssetDatabase.SaveAssets(); Selection.activeObject = existing; EditorGUIUtility.PingObject(existing);
            }
            finally { if (temporary != null) DestroyImmediate(temporary); }
        }
    }
}
#endif
