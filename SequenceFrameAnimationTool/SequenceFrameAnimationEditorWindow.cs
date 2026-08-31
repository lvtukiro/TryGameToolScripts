#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Game.SequenceFrameAnimation;
using Debug = UnityEngine.Debug;

namespace Game.EditorTools.SequenceFrameAnimation
{
    public sealed class SequenceFrameAnimationEditorWindow : EditorWindow
    {
        private enum WorkflowTab
        {
            Frames = 0,
            Preview = 1,
        }

        private const int DefaultExtractFrameRate = 12;
        private const float DefaultSelectThreshold = 0.06f;
        private const int DefaultSelectMinGap = 2;
        private const float DefaultBackgroundTolerance = 0.08f;
        private const int SequenceFrameResourceSubId = 1;
        private const string SequenceFrameResourceFolder =
            "Assets/Resources/TryGameBuildRes/clip_sprite/sequence_animation";

        private WorkflowTab workflowTab;
        private SequenceFrameAnimationDocument document;
        private string sourceAnimationPath = string.Empty;
        private string extractedFrameFolder = string.Empty;
        private string outputAssetFolder = "Assets/Resources/TryGameBuildRes/clip_sprite";
        private Texture2D previewTexture;
        private int selectedFrameListIndex = -1;
        private int extractFrameRate = DefaultExtractFrameRate;
        private float autoSelectThreshold = DefaultSelectThreshold;
        private int autoSelectMinGap = DefaultSelectMinGap;
        private bool isPlaying;
        private double lastPlaybackTime;
        private int playbackFrame;
        private Vector2 frameScroll;
        private bool scrollFrameListToSelected;
        private string status = "请选择动作视频或序列帧图片。";
        private bool hasExtractedSource;
        private bool refDataTablesRegistered;
        // 仅表示当前窗口中的临时拆帧是否已经执行过批量扣底色。
        // 该状态不写入动作 JSON；重新读取动作时仍以 JSON 和正式导出帧为准。
        private bool temporaryFramesBackgroundRemoved;
        private bool backgroundColorSampled;
        private readonly HashSet<string> temporaryBackgroundProcessedPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool regionSelectionMode;
        private Rect selectedRegionNormalized;
        private bool hasSelectedRegion;
        private Color regionKeyColor = Color.white;
        private bool regionColorSampled;
        private bool[] selectedRegionMask;
        private int selectedRegionWidth;
        private int selectedRegionHeight;
        private int selectedRegionPixelCount;
        private string regionUndoPath = string.Empty;
        private byte[] regionUndoPng;

        // 主窗口唯一入口；预制体生成从“动作预览”页进入。
        [MenuItem("TryGame/Tools/Sequence Frame Animation Tool")]
        public static void Open()
        {
            SequenceFrameAnimationEditorWindow window =
                GetWindow<SequenceFrameAnimationEditorWindow>("序列帧动画工具");
            window.minSize = new Vector2(1050f, 680f);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureDocument();
            EditorApplication.update += UpdatePlayback;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePlayback;
            isPlaying = false;
            DestroyTexture(ref previewTexture);
        }

        private void OnGUI()
        {
            EnsureDocument();
            DrawToolbar();
            DrawTabs();
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawPreviewPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("TryGame 序列帧动画工具", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(status, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            workflowTab = (WorkflowTab)GUILayout.Toolbar(
                (int)workflowTab,
                new[] { "完整角色帧", "动作预览" },
                EditorStyles.toolbarButton,
                GUILayout.Height(24f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(360f));
            if (workflowTab == WorkflowTab.Frames)
            {
                DrawFramesPanel();
            }
            else
            {
                DrawActionPreviewControlPanel();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFramesPanel()
        {
            EditorGUILayout.LabelField("动作来源", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                string.IsNullOrWhiteSpace(sourceAnimationPath)
                    ? "未选择"
                    : sourceAnimationPath,
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("选择视频 / 序列帧图片"))
            {
                SelectFrameSource();
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(sourceAnimationPath)))
            {
                if (GUILayout.Button(hasExtractedSource ? "重新拆帧" : "拆帧"))
                {
                    ExtractSelectedFrameSource();
                }
            }

            EditorGUILayout.LabelField(
                hasExtractedSource
                    ? "第 1 步完成：已拆帧。现在可以勾选需要保留的帧。"
                    : "流程：选择动作文件 → 拆帧 → 选择帧 → 最后保存。",
                EditorStyles.wordWrappedMiniLabel);

            extractFrameRate = EditorGUILayout.IntSlider(
                "拆帧 FPS",
                extractFrameRate,
                1,
                30);
            document.frameRate = EditorGUILayout.FloatField(
                "播放 FPS",
                document.frameRate);
            autoSelectThreshold = EditorGUILayout.Slider(
                "自动选帧阈值",
                autoSelectThreshold,
                0.005f,
                0.5f);
            autoSelectMinGap = EditorGUILayout.IntSlider(
                "最小选帧间隔",
                autoSelectMinGap,
                0,
                30);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!hasExtractedSource || document.frames.Count == 0))
            {
                if (GUILayout.Button("自动选帧"))
                {
                    AutoSelectFrames();
                }

                if (GUILayout.Button("全选"))
                {
                    SetAllFrameSelection(true);
                }

                if (GUILayout.Button("全不选"))
                {
                    SetAllFrameSelection(false);
                }
            }
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(!hasExtractedSource || document.frames.Count == 0))
            {
                if (GUILayout.Button("选中下一帧"))
                {
                    SelectNextFrameForPreview();
                }
            }

            EditorGUILayout.Space(8f);
            document.animationId = EditorGUILayout.TextField("导出名称", document.animationId);
            EditorGUILayout.LabelField(
                "用于帧目录、动作 JSON 和预览名称；Action ID 仍单独填写。",
                EditorStyles.wordWrappedMiniLabel);
            document.removeBackground = EditorGUILayout.Toggle("扣底色", document.removeBackground);
            EditorGUILayout.BeginHorizontal();
            document.backgroundKeyColor = EditorGUILayout.ColorField(
                "背景色",
                document.backgroundKeyColor);
            if (GUILayout.Button("取底色", GUILayout.Width(72f)))
            {
                SampleBackgroundColorFromCurrentFrame();
            }
            EditorGUILayout.EndHorizontal();
            document.backgroundTolerance = EditorGUILayout.Slider(
                "扣底容差",
                document.backgroundTolerance,
                0f,
                0.3f);
            using (new EditorGUI.DisabledScope(
                !CanBatchRemoveTemporaryBackground()))
            {
                if (GUILayout.Button("批量扣除临时帧背景"))
                {
                    RemoveBackgroundFromTemporaryFrames();
                }
            }
            EditorGUILayout.LabelField(
                "先取底色；批量处理时每帧会重新采样四角底色，并允许小范围近色断缝连通。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("单帧局部抠图", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ColorField("点击颜色", regionKeyColor);
            }
            if (GUILayout.Button(regionSelectionMode ? "取消选取" : "点击选取区域", GUILayout.Width(108f)))
            {
                regionSelectionMode = !regionSelectionMode;
                status = regionSelectionMode
                    ? "请在右侧预览图上点击要扣除的区域，工具会按点击处颜色识别连通区域。"
                    : "已取消局部区域选取。";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!hasSelectedRegion || !regionColorSampled))
            {
                if (GUILayout.Button("抠除选中区域"))
                {
                    RemoveBackgroundFromSelectedRegion();
                }
            }

            using (new EditorGUI.DisabledScope(regionUndoPng == null))
            {
                if (GUILayout.Button("撤销单帧抠图"))
                {
                    UndoRegionBackgroundRemoval();
                }
            }
            using (new EditorGUI.DisabledScope(!hasSelectedRegion))
            {
                if (GUILayout.Button("清除选区"))
                {
                    ClearSelectedRegion();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                hasSelectedRegion
                    ? "已选中红色区域；抠图只作用于当前帧。"
                    : "点击选取区域后，在右侧预览图上点击需要处理的颜色区域。",
                EditorStyles.wordWrappedMiniLabel);
            outputAssetFolder = EditorGUILayout.TextField("输出 Assets 目录", outputAssetFolder);
            if (GUILayout.Button("选择输出目录"))
            {
                SelectOutputFolder();
            }

            using (new EditorGUI.DisabledScope(
                !hasExtractedSource || CountSelectedFrames() == 0))
            {
                if (GUILayout.Button("保存选中完整帧并导出"))
                {
                    ExportFrames();
                }
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                "每张序列帧都应包含人物和当前武器的完整画面；工具不会再单独读取或贴合武器帧。"
                + " 勾选“扣底色”后会把导出的纯色背景扣成透明。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawActionPreviewControlPanel()
        {
            EditorGUILayout.LabelField("动作预览", EditorStyles.boldLabel);
            document.animationId = EditorGUILayout.TextField("导出名称", document.animationId);
            EditorGUILayout.BeginHorizontal();
            document.actionId = EditorGUILayout.IntField("Action ID", document.actionId);
            if (GUILayout.Button("取下一个 ID", GUILayout.Width(96f)))
            {
                AssignNextSequenceFrameActionId();
            }
            EditorGUILayout.EndHorizontal();
            document.loop = EditorGUILayout.Toggle("循环播放", document.loop);
            document.defaultFacingLeft = EditorGUILayout.Toggle(
                "素材默认朝向左",
                document.defaultFacingLeft);
            document.frameRate = EditorGUILayout.FloatField("播放 FPS", document.frameRate);
            document.pivotNormalized = EditorGUILayout.Vector2Field(
                "Sprite Pivot",
                document.pivotNormalized);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isPlaying ? "暂停" : "播放"))
            {
                List<SequenceFrameData> selectedFrames = GetSelectedFrames();
                if (selectedFrames.Count == 0)
                {
                    isPlaying = false;
                    status = "请至少勾选一帧后再播放。";
                    Repaint();
                    EditorGUILayout.EndHorizontal();
                    return;
                }

                // 非循环动作完成后停在最后一帧。再次点击播放时从第 0 帧
                // 重新开始；暂停中的其它帧仍保持原有的继续播放行为。
                if (!isPlaying
                    && !document.loop
                    && playbackFrame >= selectedFrames.Count - 1)
                {
                    playbackFrame = 0;
                }

                // 第一次播放或从暂停继续时，先显示当前“已选帧”位置；
                // 否则初始预览若停在未勾选源帧，会在第一拍直接跳过已选列表第 0 帧。
                if (!isPlaying)
                {
                    LoadPreviewSelectedFrame(playbackFrame);
                }

                isPlaying = !isPlaying;
                lastPlaybackTime = EditorApplication.timeSinceStartup;
            }

            if (GUILayout.Button("停止"))
            {
                isPlaying = false;
                playbackFrame = 0;
                LoadPreviewSelectedFrame(playbackFrame);
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("读取序列帧动作 JSON"))
            {
                LoadDocument();
            }

            if (GUILayout.Button("生成序列帧角色预制体..."))
            {
                SequenceFrameAnimationPrefabBuilder.CreatePrefabInteractive();
            }

            if (GUILayout.Button("保存序列帧动作 JSON"))
            {
                SaveDocument();
            }

            using (new EditorGUI.DisabledScope(
                document.frames == null || CountSelectedFrames() == 0))
            {
                if (GUILayout.Button("生成正式 SequenceFrameClip.asset"))
                {
                    ExportSequenceFrameClip();
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "生成 Clip 时如果帧还没有导出，工具会自动保存当前选中帧并导入为 Sprite；"
                + "不需要再手动返回上一页导出。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawPreviewPanel()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(
                document.defaultFacingLeft
                    ? "预览方向：素材默认朝向左"
                    : "预览方向：素材默认朝向右",
                EditorStyles.miniLabel);
            Rect previewRect = GUILayoutUtility.GetRect(
                100f,
                10000f,
                100f,
                10000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.13f, 0.14f));
            bool mirrorHorizontally = !document.defaultFacingLeft;
            Rect textureRect = GetTextureDrawRect(previewTexture, previewRect);
            DrawTextureFit(previewTexture, textureRect, mirrorHorizontally);
            DrawSelectedRegion(textureRect);
            HandleRegionPreviewInput(textureRect, mirrorHorizontally);

            DrawFrameList();
            EditorGUILayout.EndVertical();
        }

        private void DrawFrameList()
        {
            EditorGUILayout.LabelField(
                "完整角色帧（已选 " + CountSelectedFrames() + "/" + document.frames.Count + "）",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "播放预览只使用已勾选的帧；未勾选帧仍可单击查看。",
                EditorStyles.miniLabel);
            frameScroll = EditorGUILayout.BeginScrollView(
                frameScroll,
                GUILayout.Height(170f));
            List<int> displayOrder = BuildFrameDisplayOrder();
            if (scrollFrameListToSelected && selectedFrameListIndex >= 0)
            {
                int displayIndex = displayOrder.IndexOf(selectedFrameListIndex);
                if (displayIndex >= 0)
                {
                    // 每行高度会因编辑器字体略有变化，使用略保守的行高并留出
                    // 一行上下边距，确保当前帧在滚动区域中可见。
                    frameScroll.y = Mathf.Max(0f, displayIndex * 27f - 54f);
                }

                scrollFrameListToSelected = false;
            }
            for (int displayIndex = 0; displayIndex < displayOrder.Count; displayIndex++)
            {
                int frameIndex = displayOrder[displayIndex];
                SequenceFrameData frame = document.frames[frameIndex];
                if (frame == null)
                {
                    continue;
                }

                bool isCurrentPreviewFrame = frameIndex == selectedFrameListIndex;
                Color previousBackgroundColor = GUI.backgroundColor;
                GUI.backgroundColor = isCurrentPreviewFrame
                    ? new Color(0.35f, 0.65f, 1f, 1f)
                    : Color.white;
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                bool selected = EditorGUILayout.Toggle(frame.selected, GUILayout.Width(20f));
                if (selected != frame.selected)
                {
                    frame.selected = selected;
                    RecalculateDifferenceScoresFromSelectedFrames();
                }

                if (GUILayout.Button(
                        "源帧 " + frame.sourceFrameIndex + "  与前一个已选帧差异 "
                        + frame.differenceScore.ToString("0.000"),
                        EditorStyles.miniButton))
                {
                    selectedFrameListIndex = frameIndex;
                    LoadPreviewFrame(frameIndex);
                }

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = previousBackgroundColor;
            }
            EditorGUILayout.EndScrollView();
        }

        private void SelectNextFrameForPreview()
        {
            if (document == null || document.frames == null || document.frames.Count == 0)
            {
                return;
            }

            int nextIndex = selectedFrameListIndex < 0
                ? 0
                : (selectedFrameListIndex + 1) % document.frames.Count;
            LoadPreviewFrame(nextIndex);
            scrollFrameListToSelected = true;
            status = "当前预览帧：源帧 " + document.frames[nextIndex].sourceFrameIndex;
            Repaint();
        }

        private void SelectFrameSource()
        {
            string path = EditorUtility.OpenFilePanel(
                "选择动作视频或序列帧图片",
                Application.dataPath,
                "png,jpg,jpeg,bmp,gif,mp4,mov,avi,webm");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            sourceAnimationPath = path;
            extractedFrameFolder = string.Empty;
            hasExtractedSource = false;
            selectedFrameListIndex = -1;
            isPlaying = false;
            playbackFrame = 0;
            document.frames.Clear();
            DestroyTexture(ref previewTexture);
            temporaryFramesBackgroundRemoved = false;
            backgroundColorSampled = false;
            temporaryBackgroundProcessedPaths.Clear();
            ResetRegionSelectionState(true);
            scrollFrameListToSelected = false;
            status = "已选择动作文件，请点击“拆帧”。";
            Repaint();
        }

        private void ExtractSelectedFrameSource()
        {
            if (string.IsNullOrWhiteSpace(sourceAnimationPath))
            {
                status = "请先选择视频或图片。";
                return;
            }

            string extension = Path.GetExtension(sourceAnimationPath).ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg"
                || extension == ".bmp" || extension == ".gif")
            {
                extractedFrameFolder = Path.GetDirectoryName(sourceAnimationPath);
                document.frames = new List<SequenceFrameData>
                {
                    new SequenceFrameData
                    {
                        sourceFrameIndex = 0,
                        sourceFilePath = sourceAnimationPath,
                        selected = true,
                    },
                };
                hasExtractedSource = true;
                isPlaying = false;
                playbackFrame = 0;
                temporaryFramesBackgroundRemoved = false;
                backgroundColorSampled = false;
                temporaryBackgroundProcessedPaths.Clear();
                ResetRegionSelectionState(true);
                scrollFrameListToSelected = false;
                LoadPreviewFrame(0);
                status = "已载入一张完整角色帧。";
                return;
            }

            if (!TryExtractFrames(
                    sourceAnimationPath,
                    out extractedFrameFolder,
                    out string error))
            {
                status = error;
                Debug.LogError("[SequenceFrameAnimationEditorWindow] " + error);
                return;
            }

            document.frames = LoadFrameFiles(extractedFrameFolder);
            document.frameRate = extractFrameRate;
            hasExtractedSource = document.frames.Count > 0;
            selectedFrameListIndex = document.frames.Count > 0 ? 0 : -1;
            isPlaying = false;
            playbackFrame = 0;
            temporaryFramesBackgroundRemoved = false;
            backgroundColorSampled = false;
            temporaryBackgroundProcessedPaths.Clear();
            ResetRegionSelectionState(true);
            scrollFrameListToSelected = false;
            if (hasExtractedSource)
            {
                LoadPreviewFrame(0);
                status = "已拆出 " + document.frames.Count
                    + " 帧，尚未保存。请先选择要保留的帧。";
            }
            else
            {
                status = "拆帧完成，但没有得到图片帧。";
            }

            Repaint();
        }

        private List<SequenceFrameData> LoadFrameFiles(string folder)
        {
            List<SequenceFrameData> result = new List<SequenceFrameData>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return result;
            }

            string[] files = Directory.GetFiles(folder, "*.png");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                result.Add(new SequenceFrameData
                {
                    sourceFrameIndex = i,
                    sourceFilePath = files[i],
                    selected = true,
                    differenceScore = i == 0
                        ? 1f
                        : ComputeFrameDifference(files[i - 1], files[i]),
                });
            }

            return result;
        }

        private void AutoSelectFrames()
        {
            if (document.frames.Count == 0)
            {
                return;
            }

            for (int i = 0; i < document.frames.Count; i++)
            {
                document.frames[i].selected = i == 0;
                document.frames[i].differenceScore = i == 0 ? 1f : 0f;
            }

            // 差异基准始终是“上一个已选中的帧”，而不是 sourceFrameIndex - 1。
            // 例如 0、5 已选中时，帧 6、7、8 都要和帧 5 比较。
            int lastSelected = 0;
            for (int i = 1; i < document.frames.Count; i++)
            {
                SequenceFrameData frame = document.frames[i];
                float difference = ComputeFrameDifference(
                    document.frames[lastSelected].sourceFilePath,
                    frame.sourceFilePath);
                frame.differenceScore = difference;
                if (difference >= autoSelectThreshold
                    && i - lastSelected >= autoSelectMinGap)
                {
                    frame.selected = true;
                    lastSelected = i;
                }
            }

            status = "自动选帧完成（与上一个已选帧比较），已选 "
                + CountSelectedFrames() + " 帧。";
            Repaint();
        }

        private List<int> BuildFrameDisplayOrder()
        {
            List<int> result = new List<int>(document.frames.Count);
            for (int i = 0; i < document.frames.Count; i++)
            {
                if (document.frames[i] != null && document.frames[i].selected)
                {
                    result.Add(i);
                }
            }

            for (int i = 0; i < document.frames.Count; i++)
            {
                if (document.frames[i] != null && !document.frames[i].selected)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private void RecalculateDifferenceScoresFromSelectedFrames()
        {
            int previousSelected = -1;
            for (int i = 0; i < document.frames.Count; i++)
            {
                SequenceFrameData frame = document.frames[i];
                if (frame == null)
                {
                    continue;
                }

                if (previousSelected < 0)
                {
                    frame.differenceScore = frame.selected ? 1f : 0f;
                }
                else
                {
                    frame.differenceScore = ComputeFrameDifference(
                        document.frames[previousSelected].sourceFilePath,
                        frame.sourceFilePath);
                }

                if (frame.selected)
                {
                    previousSelected = i;
                }
            }
        }

        private void SetAllFrameSelection(bool selected)
        {
            for (int i = 0; i < document.frames.Count; i++)
            {
                if (document.frames[i] != null)
                {
                    document.frames[i].selected = selected;
                }
            }

            RecalculateDifferenceScoresFromSelectedFrames();
        }

        private bool ExportFrames()
        {
            if (!TryGetProjectAssetFolder(outputAssetFolder, out string outputFolder))
            {
                EditorUtility.DisplayDialog("导出序列帧", "输出目录必须位于当前项目 Assets 下。", "确定");
                return false;
            }

            string animationFolder = outputFolder.TrimEnd('/') + "/" + SafeName(document.animationId);
            Directory.CreateDirectory(ToAbsolutePath(animationFolder));
            List<SequenceFrameData> selected = GetSelectedFrames();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("导出序列帧", "请至少选择一帧。", "确定");
                return false;
            }

            // 导出是覆盖当前动作的完整结果。清理旧的工具生成帧，避免本次选 8 帧但目录里还残留上次的 15 帧。
            DeletePreviouslyExportedFrameFiles(animationFolder);
            for (int i = 0; i < selected.Count; i++)
            {
                string assetPath = animationFolder + "/frame_" + i.ToString("D4") + ".png";
                // 临时帧已经通过“批量扣除临时帧背景”处理过时，直接复制透明 PNG。
                // 再次执行颜色键会在某些底色（尤其黑色）下误删角色本身的像素。
                if (document.removeBackground && !temporaryFramesBackgroundRemoved)
                {
                    if (!TryExportProcessedFrame(
                            selected[i].sourceFilePath,
                            ToAbsolutePath(assetPath),
                            document.backgroundTolerance))
                    {
                        return false;
                    }
                }
                else
                {
                    File.Copy(selected[i].sourceFilePath, ToAbsolutePath(assetPath), true);
                }
                selected[i].exportedAssetPath = assetPath;
            }

            document.frames = selected;
            document.canvasWidth = GetImageWidth(selected[0].sourceFilePath);
            document.canvasHeight = GetImageHeight(selected[0].sourceFilePath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < selected.Count; i++)
            {
                ConfigureSpriteImporter(selected[i].exportedAssetPath, document.pivotNormalized);
            }
            SaveDocumentToPath(animationFolder + "/" + SafeName(document.animationId) + ".sequence.json");
            status = "已导出完整角色序列帧：" + selected.Count + " 张：" + animationFolder;
            return true;
        }

        private static void DeletePreviouslyExportedFrameFiles(string animationFolder)
        {
            string absoluteFolder = ToAbsolutePath(animationFolder);
            if (!Directory.Exists(absoluteFolder))
            {
                return;
            }

            string[] oldFrameFiles = Directory.GetFiles(
                absoluteFolder,
                "frame_*.png",
                SearchOption.TopDirectoryOnly);
            for (int i = 0; i < oldFrameFiles.Length; i++)
            {
                File.Delete(oldFrameFiles[i]);
                string metaPath = oldFrameFiles[i] + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }
            }
        }

        private void ExportSequenceFrameClip()
        {
            if (!TryGetActionId(out int actionId))
            {
                EditorUtility.DisplayDialog(
                    "生成序列帧 Clip",
                    "请填写大于 0 的 Action ID。Action ID 必须与 SequenceFrameActionResource 表的 id 一致。",
                    "确定");
                return;
            }

            List<SequenceFrameData> selectedFrames = GetSelectedFrames();
            if (selectedFrames.Count == 0)
            {
                EditorUtility.DisplayDialog("生成序列帧 Clip", "请至少勾选一张完整角色帧。", "确定");
                return;
            }

            List<Sprite> sprites = LoadSpriteAssets(selectedFrames);
            if (!AreFramesExportedToCurrentDestination(selectedFrames)
                || sprites.Count != selectedFrames.Count)
            {
                // 拆帧后的 sourceFilePath 仍然指向临时帧文件；如果用户还没有点过
                // “保存选中完整帧并导出”，这里直接复用同一套导出逻辑，完成复制、
                // Sprite 导入和 JSON 保存，然后继续生成 Clip。
                if (!ExportFrames())
                {
                    return;
                }

                selectedFrames = GetSelectedFrames();
                sprites = LoadSpriteAssets(selectedFrames);
                if (sprites.Count != selectedFrames.Count)
                {
                    EditorUtility.DisplayDialog(
                        "生成序列帧 Clip",
                        "选中的完整角色帧导入 Sprite 失败，请检查输出目录和图片文件。",
                        "确定");
                    return;
                }
            }

            if (document.canvasWidth <= 0 || document.canvasHeight <= 0)
            {
                EditorUtility.DisplayDialog(
                    "生成序列帧 Clip",
                    "完整角色帧画布尺寸无效，请先重新导出。",
                    "确定");
                return;
            }

            if (document.frameRate <= 0f
                || document.pivotNormalized.x < 0f
                || document.pivotNormalized.x > 1f
                || document.pivotNormalized.y < 0f
                || document.pivotNormalized.y > 1f)
            {
                EditorUtility.DisplayDialog(
                    "生成序列帧 Clip",
                    "播放 FPS 必须大于 0，Sprite Pivot 必须在 0 到 1 之间。",
                    "确定");
                return;
            }

            string assetPath = GetSequenceFrameClipAssetPath(actionId);
            string absoluteFolder = ToAbsolutePath(SequenceFrameResourceFolder);
            Directory.CreateDirectory(absoluteFolder);
            SequenceFrameClip existing = AssetDatabase.LoadAssetAtPath<SequenceFrameClip>(assetPath);
            if (existing != null
                && !EditorUtility.DisplayDialog(
                    "覆盖序列帧 Clip",
                    "Action ID " + actionId + " 已存在 Clip，是否覆盖？\n" + assetPath,
                    "覆盖",
                    "取消"))
            {
                return;
            }

            if (existing != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            SequenceFrameClip clip = ScriptableObject.CreateInstance<SequenceFrameClip>();
            SerializedObject serializedClip = new SerializedObject(clip);
            serializedClip.FindProperty("actionId").intValue = actionId;
            serializedClip.FindProperty("frameRate").floatValue = document.frameRate;
            serializedClip.FindProperty("loop").boolValue = document.loop;
            SerializedProperty defaultFacingLeftProperty =
                serializedClip.FindProperty("defaultFacingLeft");
            if (defaultFacingLeftProperty != null)
            {
                defaultFacingLeftProperty.boolValue = document.defaultFacingLeft;
            }
            serializedClip.FindProperty("canvasWidth").intValue = document.canvasWidth;
            serializedClip.FindProperty("canvasHeight").intValue = document.canvasHeight;
            serializedClip.FindProperty("pivotNormalized").vector2Value = document.pivotNormalized;
            SetSpriteArray(serializedClip.FindProperty("frames"), sprites);
            serializedClip.ApplyModifiedPropertiesWithoutUndo();

            if (!clip.TryValidate(out string validationError))
            {
                DestroyImmediate(clip);
                EditorUtility.DisplayDialog("生成序列帧 Clip", validationError, "确定");
                return;
            }

            clip.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SequenceFrameClip saved = AssetDatabase.LoadAssetAtPath<SequenceFrameClip>(assetPath);
            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            status = "已生成正式序列帧 Clip：" + assetPath;
        }

        private bool TryGetActionId(out int actionId)
        {
            actionId = document.actionId;
            if (actionId <= 0 && int.TryParse(document.animationId, out int parsedActionId))
            {
                actionId = parsedActionId;
                document.actionId = parsedActionId;
            }

            return actionId > 0;
        }

        /// <summary>
        /// 从正式 SequenceFrameActionResource 表取下一个可用的 actionId。
        /// 编辑器窗口可能在运行时 RefData 初始化前打开，因此这里按正式编辑器加载
        /// 流程补做一次表注册/初始化；不读取临时帧目录，也不改动任何导出文件。
        /// </summary>
        private void AssignNextSequenceFrameActionId()
        {
            try
            {
                RefData.CLRefDataModuleCommon common =
                    RefData.CLRefDataModule.instance.refDataModuleCommon;
                if (!RefData.SequenceFrameActionResourceTable.IsLoaded
                    && !refDataTablesRegistered)
                {
                    List<Type> tableTypes = TypeCache
                        .GetTypesDerivedFrom<RefData.IRefData>()
                        .Where(type => type != null
                            && !type.IsAbstract
                            && string.Equals(type.Namespace, "RefData", StringComparison.Ordinal)
                            && type.Name.EndsWith("Table", StringComparison.Ordinal))
                        .OrderBy(type => type.FullName, StringComparer.Ordinal)
                        .ToList();
                    if (tableTypes.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "No generated RefData table types were discovered.");
                    }

                    for (int index = 0; index < tableTypes.Count; index++)
                    {
                        Type tableType = tableTypes[index];
                        System.Reflection.MethodInfo register = tableType.GetMethod(
                            "Register",
                            System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.Static,
                            null,
                            Type.EmptyTypes,
                            null);
                        if (register == null)
                        {
                            throw new InvalidOperationException(
                                $"{tableType.FullName} is missing public static Register().");
                        }

                        register.Invoke(null, null);
                    }

                    refDataTablesRegistered = true;
                }
                else if (RefData.SequenceFrameActionResourceTable.IsLoaded)
                {
                    refDataTablesRegistered = true;
                }

                if (!common.Inited)
                {
                    common.LoadRefData();
                    common.Init();
                }
                else if (!RefData.SequenceFrameActionResourceTable.IsLoaded)
                {
                    common.LoadRefData();
                    common.ReLoadAll_OnlyForEditor();
                }

                if (!RefData.SequenceFrameActionResourceTable.IsLoaded)
                {
                    status = "读取 SequenceFrameActionResource 配置失败，请先完成导表。";
                    Debug.LogError(
                        "[SequenceFrameAnimationTool] SequenceFrameActionResource 表尚未加载，无法获取下一个 Action ID。 ");
                    Repaint();
                    return;
                }

                int maximumId = 0;
                for (int index = 0;
                     index < RefData.SequenceFrameActionResourceTable.Count;
                     index++)
                {
                    RefData.SequenceFrameActionResource row =
                        RefData.SequenceFrameActionResourceTable.SequenceFrameActionResources(index);
                    maximumId = Mathf.Max(maximumId, row.Id);
                }

                if (maximumId >= int.MaxValue)
                {
                    status = "SequenceFrameActionResource 的 Action ID 已耗尽。";
                    Debug.LogError(
                        "[SequenceFrameAnimationTool] SequenceFrameActionResource 的 Action ID 已达到 int.MaxValue。 ");
                    Repaint();
                    return;
                }

                document.actionId = maximumId + 1;
                status = "已取得下一个 Action ID：" + document.actionId;
                Repaint();
            }
            catch (Exception exception)
            {
                status = "读取 SequenceFrameActionResource 配置失败：" + exception.Message;
                Debug.LogError(
                    "[SequenceFrameAnimationTool] 获取下一个 Action ID 失败："
                    + exception);
                Repaint();
            }
        }

        private static string GetSequenceFrameClipAssetPath(int actionId)
        {
            return SequenceFrameResourceFolder
                + "/clip_"
                + actionId
                + "_"
                + SequenceFrameResourceSubId
                + ".asset";
        }

        private static List<Sprite> LoadSpriteAssets(List<SequenceFrameData> frames)
        {
            List<string> assetPaths = new List<string>();
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] != null)
                {
                    assetPaths.Add(frames[i].exportedAssetPath);
                }
            }

            return LoadSpriteAssets(assetPaths);
        }

        private static List<Sprite> LoadSpriteAssets(List<string> assetPaths)
        {
            List<Sprite> result = new List<Sprite>();
            for (int i = 0; i < assetPaths.Count; i++)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPaths[i]);
                if (sprite == null)
                {
                    continue;
                }

                result.Add(sprite);
            }

            return result;
        }

        private bool AreFramesExportedToCurrentDestination(
            List<SequenceFrameData> frames)
        {
            if (!TryGetProjectAssetFolder(outputAssetFolder, out string outputFolder))
            {
                return false;
            }

            string expectedFolder = outputFolder.TrimEnd('/') + "/" + SafeName(document.animationId);
            for (int i = 0; i < frames.Count; i++)
            {
                SequenceFrameData frame = frames[i];
                if (frame == null
                    || string.IsNullOrWhiteSpace(frame.exportedAssetPath)
                    || !frame.exportedAssetPath.StartsWith(
                        expectedFolder + "/",
                        StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(ToAbsolutePath(frame.exportedAssetPath)))
                {
                    return false;
                }
            }

            return frames.Count > 0;
        }

        private static void SetSpriteArray(SerializedProperty property, List<Sprite> sprites)
        {
            property.arraySize = sprites.Count;
            for (int i = 0; i < sprites.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            }
        }

        private static void ConfigureSpriteImporter(string assetPath, Vector2 pivotNormalized)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = pivotNormalized;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        private List<SequenceFrameData> GetSelectedFrames()
        {
            List<SequenceFrameData> result = new List<SequenceFrameData>();
            for (int i = 0; i < document.frames.Count; i++)
            {
                if (document.frames[i] != null && document.frames[i].selected)
                {
                    result.Add(document.frames[i]);
                }
            }

            return result;
        }

        private void LoadPreviewFrame(int index)
        {
            if (document.frames.Count == 0)
            {
                DestroyTexture(ref previewTexture);
                return;
            }

            index = Mathf.Clamp(index, 0, document.frames.Count - 1);
            if (selectedFrameListIndex != index)
            {
                ResetRegionSelectionState(true);
            }
            selectedFrameListIndex = index;
            DestroyTexture(ref previewTexture);
            previewTexture = LoadTexture(document.frames[index].sourceFilePath);
            Repaint();
        }

        /// <summary>
        /// 按已勾选帧的播放序号加载预览。未勾选帧保留在源帧列表中，
        /// 但不应进入动作播放时间轴。
        /// </summary>
        private void LoadPreviewSelectedFrame(int index)
        {
            List<SequenceFrameData> selectedFrames = GetSelectedFrames();
            if (selectedFrames.Count == 0)
            {
                selectedFrameListIndex = -1;
                DestroyTexture(ref previewTexture);
                Repaint();
                return;
            }

            index = Mathf.Clamp(index, 0, selectedFrames.Count - 1);
            SequenceFrameData frame = selectedFrames[index];
            if (frame == null)
            {
                return;
            }

            int sourceIndex = document.frames.IndexOf(frame);
            if (sourceIndex >= 0)
            {
                LoadPreviewFrame(sourceIndex);
            }
        }

        private void UpdatePlayback()
        {
            if (!isPlaying || document == null || document.frames.Count == 0)
            {
                return;
            }

            List<SequenceFrameData> selectedFrames = GetSelectedFrames();
            if (selectedFrames.Count == 0)
            {
                isPlaying = false;
                playbackFrame = 0;
                LoadPreviewSelectedFrame(0);
                return;
            }

            // 勾选状态可以在播放过程中修改，避免旧的播放序号超出新的已选帧列表。
            playbackFrame = Mathf.Clamp(playbackFrame, 0, selectedFrames.Count - 1);

            double now = EditorApplication.timeSinceStartup;
            float fps = Mathf.Max(1f, document.frameRate);
            if (now - lastPlaybackTime < 1f / fps)
            {
                return;
            }

            playbackFrame++;
            lastPlaybackTime = now;
            if (playbackFrame >= selectedFrames.Count)
            {
                if (document.loop)
                {
                    playbackFrame = 0;
                }
                else
                {
                    // 非循环播放完成后停在最后一帧，便于检查结束姿态；
                    // 再次点击“播放”时由按钮逻辑回到第 0 帧重播。
                    playbackFrame = selectedFrames.Count - 1;
                    isPlaying = false;
                    lastPlaybackTime = now;
                    LoadPreviewSelectedFrame(playbackFrame);
                    return;
                }
            }

            LoadPreviewSelectedFrame(playbackFrame);
        }

        private static Rect GetTextureDrawRect(Texture2D texture, Rect rect)
        {
            if (texture == null)
            {
                return rect;
            }

            float imageRatio = (float)texture.width / Mathf.Max(1, texture.height);
            float rectRatio = rect.width / Mathf.Max(1f, rect.height);
            Rect drawRect = rect;
            if (imageRatio > rectRatio)
            {
                float height = rect.width / imageRatio;
                drawRect.y += (rect.height - height) * 0.5f;
                drawRect.height = height;
            }
            else
            {
                float width = rect.height * imageRatio;
                drawRect.x += (rect.width - width) * 0.5f;
                drawRect.width = width;
            }

            return drawRect;
        }

        private void DrawTextureFit(Texture2D texture, Rect rect, bool mirrorHorizontally)
        {
            if (texture == null)
            {
                GUI.Label(rect, "暂无预览图", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (mirrorHorizontally)
            {
                Matrix4x4 previousMatrix = GUI.matrix;
                try
                {
                    GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), rect.center);
                    GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
                }
                finally
                {
                    GUI.matrix = previousMatrix;
                }
            }
            else
            {
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
            }
        }

        private void DrawSelectedRegion(Rect textureRect)
        {
            if (!hasSelectedRegion || textureRect.width <= 0f || textureRect.height <= 0f)
            {
                return;
            }

            Rect regionRect = new Rect(
                textureRect.x + selectedRegionNormalized.x * textureRect.width,
                textureRect.y + selectedRegionNormalized.y * textureRect.height,
                selectedRegionNormalized.width * textureRect.width,
                selectedRegionNormalized.height * textureRect.height);
            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(1f, 0.15f, 0.1f, 1f);
            Handles.DrawSolidRectangleWithOutline(
                regionRect,
                new Color(1f, 0.1f, 0.1f, 0.08f),
                new Color(1f, 0.15f, 0.1f, 1f));
            Handles.color = previousColor;
            Handles.EndGUI();
        }

        private void HandleRegionPreviewInput(Rect textureRect, bool mirrorHorizontally)
        {
            if (!regionSelectionMode
                || previewTexture == null
                || textureRect.width <= 0f
                || textureRect.height <= 0f)
            {
                return;
            }

            Event current = Event.current;
            if (current.type != EventType.MouseDown
                || current.button != 0
                || !textureRect.Contains(current.mousePosition))
            {
                return;
            }

            Vector2 normalized = GuiToTextureNormalized(
                current.mousePosition,
                textureRect,
                mirrorHorizontally);
            int x = Mathf.Clamp(
                Mathf.FloorToInt(normalized.x * previewTexture.width),
                0,
                previewTexture.width - 1);
            int y = Mathf.Clamp(
                Mathf.FloorToInt(normalized.y * previewTexture.height),
                0,
                previewTexture.height - 1);
            SelectConnectedColorRegion(x, y, normalized);
            regionSelectionMode = false;
            current.Use();
            Repaint();
        }

        private void SelectConnectedColorRegion(int startX, int startY, Vector2 clickedNormalized)
        {
            Color32[] pixels = previewTexture.GetPixels32();
            selectedRegionWidth = previewTexture.width;
            selectedRegionHeight = previewTexture.height;
            selectedRegionMask = new bool[pixels.Length];
            regionKeyColor = pixels[startY * selectedRegionWidth + startX];
            regionColorSampled = true;
            float threshold = Mathf.Clamp01(document.backgroundTolerance);
            float thresholdSq = threshold * threshold;
            Queue<int> pending = new Queue<int>();
            int startIndex = startY * selectedRegionWidth + startX;
            selectedRegionMask[startIndex] = true;
            pending.Enqueue(startIndex);
            selectedRegionPixelCount = 0;
            int minX = startX;
            int maxX = startX;
            int minY = startY;
            int maxY = startY;
            while (pending.Count > 0)
            {
                int index = pending.Dequeue();
                selectedRegionPixelCount++;
                int x = index % selectedRegionWidth;
                int y = index / selectedRegionWidth;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
                TryQueueColorRegionNeighbor(
                    index - 1,
                    x > 0,
                    pixels,
                    regionKeyColor,
                    thresholdSq,
                    pending);
                TryQueueColorRegionNeighbor(
                    index + 1,
                    x + 1 < selectedRegionWidth,
                    pixels,
                    regionKeyColor,
                    thresholdSq,
                    pending);
                TryQueueColorRegionNeighbor(
                    index - selectedRegionWidth,
                    y > 0,
                    pixels,
                    regionKeyColor,
                    thresholdSq,
                    pending);
                TryQueueColorRegionNeighbor(
                    index + selectedRegionWidth,
                    y + 1 < selectedRegionHeight,
                    pixels,
                    regionKeyColor,
                    thresholdSq,
                    pending);
            }

            selectedRegionNormalized = new Rect(
                (float)minX / selectedRegionWidth,
                1f - (float)(maxY + 1) / selectedRegionHeight,
                (float)(maxX - minX + 1) / selectedRegionWidth,
                (float)(maxY - minY + 1) / selectedRegionHeight);
            hasSelectedRegion = selectedRegionPixelCount > 0;
            status = "已选中区域：" + selectedRegionPixelCount
                + " 像素，颜色 " + ColorUtility.ToHtmlStringRGBA(regionKeyColor)
                + "。点击“抠除选中区域”执行。";
        }

        private void TryQueueColorRegionNeighbor(
            int index,
            bool valid,
            Color32[] pixels,
            Color keyColor,
            float thresholdSq,
            Queue<int> pending)
        {
            if (!valid || selectedRegionMask[index])
            {
                return;
            }

            if (ColorDistanceSquared(pixels[index], keyColor) <= thresholdSq)
            {
                selectedRegionMask[index] = true;
                pending.Enqueue(index);
            }
        }

        private void RemoveBackgroundFromSelectedRegion()
        {
            if (!hasSelectedRegion
                || selectedRegionMask == null
                || selectedRegionMask.Length == 0
                || string.IsNullOrWhiteSpace(previewTexture != null ? GetCurrentFramePath() : string.Empty))
            {
                status = "请先在当前帧上点击选取一个区域。";
                Repaint();
                return;
            }

            string path = GetCurrentFramePath();
            if (!IsTemporaryExtractedFrame(path))
            {
                status = "局部抠图只允许处理视频拆出的临时帧。";
                Repaint();
                return;
            }

            if (regionUndoPng == null || !string.Equals(regionUndoPath, path, StringComparison.OrdinalIgnoreCase))
            {
                regionUndoPath = path;
                regionUndoPng = File.ReadAllBytes(path);
            }

            Texture2D texture = LoadTexture(path);
            if (texture == null)
            {
                status = "读取当前帧失败，无法执行局部抠图。";
                return;
            }

            try
            {
                Color32[] pixels = texture.GetPixels32();
                int count = Mathf.Min(pixels.Length, selectedRegionMask.Length);
                for (int i = 0; i < count; i++)
                {
                    if (selectedRegionMask[i])
                    {
                        pixels[i].a = 0;
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                DestroyTexture(ref texture);
            }

            LoadPreviewFrame(selectedFrameListIndex);
            status = "已抠除当前帧选中区域；如不满意可点击“撤销单帧抠图”。";
            Repaint();
        }

        private void UndoRegionBackgroundRemoval()
        {
            if (regionUndoPng == null || string.IsNullOrWhiteSpace(regionUndoPath))
            {
                return;
            }

            File.WriteAllBytes(regionUndoPath, regionUndoPng);
            regionUndoPng = null;
            string restoredPath = regionUndoPath;
            regionUndoPath = string.Empty;
            LoadPreviewFrame(selectedFrameListIndex);
            status = "已撤销当前帧的局部抠图：" + Path.GetFileName(restoredPath);
            Repaint();
        }

        private void ClearSelectedRegion()
        {
            hasSelectedRegion = false;
            regionSelectionMode = false;
            selectedRegionMask = null;
            selectedRegionPixelCount = 0;
            selectedRegionNormalized = new Rect();
            regionColorSampled = false;
            status = "已清除局部抠图选区。";
            Repaint();
        }

        private string GetCurrentFramePath()
        {
            if (document == null
                || document.frames == null
                || selectedFrameListIndex < 0
                || selectedFrameListIndex >= document.frames.Count
                || document.frames[selectedFrameListIndex] == null)
            {
                return string.Empty;
            }

            return document.frames[selectedFrameListIndex].sourceFilePath;
        }

        private void ResetRegionSelectionState(bool clearUndo)
        {
            regionSelectionMode = false;
            hasSelectedRegion = false;
            regionColorSampled = false;
            selectedRegionMask = null;
            selectedRegionPixelCount = 0;
            selectedRegionNormalized = new Rect();
            if (clearUndo)
            {
                regionUndoPath = string.Empty;
                regionUndoPng = null;
            }
        }

        private static Vector2 GuiToTextureNormalized(
            Vector2 guiPoint,
            Rect textureRect,
            bool mirrorHorizontally)
        {
            float x = Mathf.Clamp01((guiPoint.x - textureRect.x) / textureRect.width);
            float y = Mathf.Clamp01(1f - (guiPoint.y - textureRect.y) / textureRect.height);
            if (mirrorHorizontally)
            {
                x = 1f - x;
            }

            return new Vector2(x, y);
        }

        private bool TryExtractFrames(string animationPath, out string frameFolder, out string error)
        {
            frameFolder = string.Empty;
            error = string.Empty;
            if (!File.Exists(animationPath))
            {
                error = "动作文件不存在：" + animationPath;
                return false;
            }

            string ffmpeg = Path.Combine(
                Application.dataPath,
                "TryGameToolScripts/SequenceFrameAnimationTool/ffmpeg.exe");
            if (!File.Exists(ffmpeg))
            {
                error = "未找到 ffmpeg.exe。";
                return false;
            }

            string root = Path.Combine(
                Path.GetTempPath(),
                "TryAiGame",
                "SequenceFrameAnimation",
                "ExtractedFrames");
            string safeName = SafeName(Path.GetFileNameWithoutExtension(animationPath));
            frameFolder = Path.Combine(
                root,
                safeName + "_fps" + Mathf.Clamp(extractFrameRate, 1, 30) + "_" + DateTime.Now.Ticks);
            Directory.CreateDirectory(frameFolder);
            string pattern = Path.Combine(frameFolder, "frame_%05d.png");
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = "-y -i \"" + animationPath + "\" -vf fps="
                    + Mathf.Clamp(extractFrameRate, 1, 30) + " \"" + pattern + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (Process process = Process.Start(info))
            {
                if (process == null)
                {
                    error = "ffmpeg 启动失败。";
                    return false;
                }

                if (!process.WaitForExit(60000))
                {
                    try { process.Kill(); } catch { }
                    error = "ffmpeg 拆帧超时。";
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    error = "ffmpeg 拆帧失败，退出码：" + process.ExitCode;
                    return false;
                }
            }

            return Directory.GetFiles(frameFolder, "*.png").Length > 0;
        }

        private float ComputeFrameDifference(string firstPath, string secondPath)
        {
            Texture2D first = LoadTexture(firstPath);
            Texture2D second = LoadTexture(secondPath);
            if (first == null || second == null)
            {
                DestroyTexture(ref first);
                DestroyTexture(ref second);
                return 0f;
            }

            float difference = 0f;
            const int sampleSize = 24;
            for (int y = 0; y < sampleSize; y++)
            {
                for (int x = 0; x < sampleSize; x++)
                {
                    Color a = first.GetPixelBilinear(
                        (x + 0.5f) / sampleSize,
                        (y + 0.5f) / sampleSize);
                    Color b = second.GetPixelBilinear(
                        (x + 0.5f) / sampleSize,
                        (y + 0.5f) / sampleSize);
                    difference += Mathf.Abs(a.r - b.r)
                        + Mathf.Abs(a.g - b.g)
                        + Mathf.Abs(a.b - b.b);
                }
            }

            DestroyTexture(ref first);
            DestroyTexture(ref second);
            return difference / (sampleSize * sampleSize * 3f);
        }

        private void SaveDocument()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "保存序列帧动作清单",
                SafeName(document.animationId),
                "sequence.json",
                "选择清单保存位置",
                outputAssetFolder);
            if (!string.IsNullOrWhiteSpace(path))
            {
                SaveDocumentToPath(path);
            }
        }

        private void SaveDocumentToPath(string assetPath)
        {
            string json = JsonUtility.ToJson(document, true);
            File.WriteAllText(ToAbsolutePath(assetPath), json);
            AssetDatabase.ImportAsset(assetPath);
        }

        private void LoadDocument()
        {
            string path = EditorUtility.OpenFilePanel(
                "读取序列帧动作清单",
                Application.dataPath,
                "json,sequence.json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string json = File.ReadAllText(path);
            document = JsonUtility.FromJson<SequenceFrameAnimationDocument>(json);
            EnsureDocument();
            // 兼容早期没有默认朝向字段的动作清单。当前项目素材约定原图面向左，
            // 只有清单显式写入字段时才使用用户保存的右向设置。
            if (json.IndexOf("\"defaultFacingLeft\"", StringComparison.Ordinal) < 0)
            {
                document.defaultFacingLeft = true;
            }
            if (json.IndexOf("\"removeBackground\"", StringComparison.Ordinal) < 0)
            {
                document.removeBackground = false;
            }
            if (json.IndexOf("\"backgroundKeyColor\"", StringComparison.Ordinal) < 0)
            {
                document.backgroundKeyColor = Color.white;
            }
            if (json.IndexOf("\"backgroundTolerance\"", StringComparison.Ordinal) < 0)
            {
                document.backgroundTolerance = DefaultBackgroundTolerance;
            }
            hasExtractedSource = document.frames.Count > 0;
            selectedFrameListIndex = document.frames.Count > 0 ? 0 : -1;
            isPlaying = false;
            playbackFrame = 0;
            temporaryFramesBackgroundRemoved = false;
            backgroundColorSampled = false;
            temporaryBackgroundProcessedPaths.Clear();
            ResetRegionSelectionState(true);
            scrollFrameListToSelected = false;
            LoadPreviewFrame(0);
            status = "已读取序列帧动作：" + path;
        }

        private void SelectOutputFolder()
        {
            string folder = EditorUtility.OpenFolderPanel(
                "选择输出 Assets 目录",
                Application.dataPath,
                string.Empty);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            if (TryGetProjectAssetFolder(folder, out string assetFolder))
            {
                outputAssetFolder = assetFolder;
            }
            else
            {
                EditorUtility.DisplayDialog("输出目录", "目录必须位于当前项目 Assets 下。", "确定");
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }

            return texture;
        }

        private void SampleBackgroundColorFromCurrentFrame()
        {
            string path = GetBackgroundSampleSourcePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                status = "请先选中一张完整角色帧再取底色。";
                return;
            }

            Texture2D texture = LoadTexture(path);
            if (texture == null)
            {
                status = "底色采样失败，无法读取图片。";
                return;
            }

            try
            {
                document.backgroundKeyColor = SampleCornerBackgroundColor(texture);
                backgroundColorSampled = true;
                if (document.backgroundTolerance <= 0f)
                {
                    document.backgroundTolerance = DefaultBackgroundTolerance;
                }

                status = "已取底色：" + ColorUtility.ToHtmlStringRGBA(document.backgroundKeyColor);
            }
            finally
            {
                DestroyTexture(ref texture);
            }

            Repaint();
        }

        private bool CanBatchRemoveTemporaryBackground()
        {
            if (!hasExtractedSource
                || document == null
                || document.frames == null
                || document.frames.Count == 0
                || !backgroundColorSampled
                || temporaryFramesBackgroundRemoved)
            {
                return false;
            }

            for (int i = 0; i < document.frames.Count; i++)
            {
                SequenceFrameData frame = document.frames[i];
                if (frame != null && IsTemporaryExtractedFrame(frame.sourceFilePath))
                {
                    return true;
                }
            }

            return false;
        }

        private void RemoveBackgroundFromTemporaryFrames()
        {
            if (!backgroundColorSampled)
            {
                status = "请先点击“取底色”，再批量扣除临时帧背景。";
                Repaint();
                return;
            }

            int processed = 0;
            int failed = 0;
            for (int i = 0; i < document.frames.Count; i++)
            {
                SequenceFrameData frame = document.frames[i];
                if (frame == null
                    || !IsTemporaryExtractedFrame(frame.sourceFilePath)
                    || temporaryBackgroundProcessedPaths.Contains(frame.sourceFilePath))
                {
                    continue;
                }

                if (TryExportProcessedFrame(
                        frame.sourceFilePath,
                        frame.sourceFilePath,
                        document.backgroundTolerance))
                {
                    processed++;
                    temporaryBackgroundProcessedPaths.Add(frame.sourceFilePath);
                }
                else
                {
                    failed++;
                }
            }

            if (processed > 0 && failed == 0)
            {
                temporaryFramesBackgroundRemoved = true;
                document.removeBackground = true;
                RecalculateDifferenceScoresFromSelectedFrames();
                if (selectedFrameListIndex >= 0
                    && selectedFrameListIndex < document.frames.Count)
                {
                    LoadPreviewFrame(selectedFrameListIndex);
                }

                status = "已批量扣除 " + processed + " 张临时帧背景。现在可继续选帧并导出。";
            }
            else if (processed > 0)
            {
                document.removeBackground = true;
                status = "已处理 " + processed + " 张临时帧，失败 " + failed + " 张。";
            }
            else
            {
                status = "没有可处理的临时帧。";
            }

            Repaint();
        }

        private static bool IsTemporaryExtractedFrame(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string fullPath;
            string temporaryRoot;
            try
            {
                fullPath = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                temporaryRoot = Path.GetFullPath(Path.Combine(
                        Path.GetTempPath(),
                        "TryAiGame",
                        "SequenceFrameAnimation",
                        "ExtractedFrames"))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return false;
            }

            return fullPath.StartsWith(
                temporaryRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(
                    temporaryRoot + Path.AltDirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }

        private string GetBackgroundSampleSourcePath()
        {
            if (document.frames == null || document.frames.Count == 0)
            {
                return string.Empty;
            }

            if (selectedFrameListIndex >= 0 && selectedFrameListIndex < document.frames.Count)
            {
                SequenceFrameData selectedFrame = document.frames[selectedFrameListIndex];
                if (selectedFrame != null && !string.IsNullOrWhiteSpace(selectedFrame.sourceFilePath))
                {
                    return selectedFrame.sourceFilePath;
                }
            }

            for (int i = 0; i < document.frames.Count; i++)
            {
                SequenceFrameData frame = document.frames[i];
                if (frame != null && !string.IsNullOrWhiteSpace(frame.sourceFilePath))
                {
                    return frame.sourceFilePath;
                }
            }

            return string.Empty;
        }

        private static Color SampleCornerBackgroundColor(Texture2D texture)
        {
            int width = Mathf.Max(1, texture.width);
            int height = Mathf.Max(1, texture.height);
            int sampleSize = Mathf.Clamp(Mathf.Min(width, height) / 16, 4, 24);
            Color[] pixels = texture.GetPixels();
            Vector4 sum = Vector4.zero;
            int count = 0;
            AccumulateCornerSample(
                pixels,
                width,
                height,
                0,
                0,
                sampleSize,
                ref sum,
                ref count);
            AccumulateCornerSample(
                pixels,
                width,
                height,
                Mathf.Max(0, width - sampleSize),
                0,
                sampleSize,
                ref sum,
                ref count);
            AccumulateCornerSample(
                pixels,
                width,
                height,
                0,
                Mathf.Max(0, height - sampleSize),
                sampleSize,
                ref sum,
                ref count);
            AccumulateCornerSample(
                pixels,
                width,
                height,
                Mathf.Max(0, width - sampleSize),
                Mathf.Max(0, height - sampleSize),
                sampleSize,
                ref sum,
                ref count);

            if (count <= 0)
            {
                return Color.white;
            }

            Vector4 average = sum / count;
            return new Color(average.x, average.y, average.z, 1f);
        }

        private static void AccumulateCornerSample(
            Color[] pixels,
            int width,
            int height,
            int startX,
            int startY,
            int sampleSize,
            ref Vector4 sum,
            ref int count)
        {
            int maxY = Mathf.Min(height, startY + sampleSize);
            int maxX = Mathf.Min(width, startX + sampleSize);
            for (int y = startY; y < maxY; y++)
            {
                int row = y * width;
                for (int x = startX; x < maxX; x++)
                {
                    sum += (Vector4)pixels[row + x];
                    count++;
                }
            }
        }

        private static bool TryExportProcessedFrame(
            string sourcePath,
            string outputPath,
            float tolerance)
        {
            Texture2D texture = LoadTexture(sourcePath);
            if (texture == null)
            {
                EditorUtility.DisplayDialog(
                    "导出序列帧",
                    "读取图片失败，无法扣底色：" + sourcePath,
                    "确定");
                return false;
            }

            try
            {
                Color32[] pixels = texture.GetPixels32();
                int width = texture.width;
                int height = texture.height;
                float threshold = Mathf.Clamp01(tolerance);
                float thresholdSq = threshold * threshold;
                float bridgeThreshold = Mathf.Clamp01(Mathf.Max(threshold, threshold * 1.5f));
                float bridgeThresholdSq = bridgeThreshold * bridgeThreshold;
                // 抠图只从画面边缘开始连通，避免把角色内部接近底色的高光
                // 一并删除；允许跨过颜色差距很小的窄断缝，再对边缘做柔化。
                // 每帧独立采样四角，适应视频压缩或生成过程造成的底色漂移。
                Color key = SampleCornerBackgroundColor(texture);
                bool[] backgroundMask = new bool[pixels.Length];
                bool[] connectedBackground = new bool[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    backgroundMask[i] = ColorDistanceSquared(pixel, key) <= bridgeThresholdSq;
                }

                Queue<int> pending = new Queue<int>();
                for (int y = 0; y < height; y++)
                {
                    TryQueueBackgroundPixel(y * width, backgroundMask, connectedBackground, pending);
                    if (width > 1)
                    {
                        TryQueueBackgroundPixel(
                            y * width + width - 1,
                            backgroundMask,
                            connectedBackground,
                            pending);
                    }
                }

                for (int x = 1; x < width - 1; x++)
                {
                    TryQueueBackgroundPixel(x, backgroundMask, connectedBackground, pending);
                    if (height > 1)
                    {
                        TryQueueBackgroundPixel(
                            (height - 1) * width + x,
                            backgroundMask,
                            connectedBackground,
                            pending);
                    }
                }

                while (pending.Count > 0)
                {
                    int index = pending.Dequeue();
                    int x = index % width;
                    int y = index / width;
                    TryQueueBackgroundNeighbor(
                        index - 1,
                        x > 0,
                        backgroundMask,
                        connectedBackground,
                        pending);
                    TryQueueBackgroundNeighbor(
                        index + 1,
                        x + 1 < width,
                        backgroundMask,
                        connectedBackground,
                        pending);
                    TryQueueBackgroundNeighbor(
                        index - width,
                        y > 0,
                        backgroundMask,
                        connectedBackground,
                        pending);
                    TryQueueBackgroundNeighbor(
                        index + width,
                        y + 1 < height,
                        backgroundMask,
                        connectedBackground,
                        pending);
                    TryQueueBackgroundNeighbor(
                        index - width - 1,
                        x > 0 && y > 0,
                        backgroundMask,
                        connectedBackground,
                        pending);
                    TryQueueBackgroundNeighbor(
                        index - width + 1,
                        x + 1 < width && y > 0,
                        backgroundMask,
                        connectedBackground,
                        pending);
                    TryQueueBackgroundNeighbor(
                        index + width - 1,
                        x > 0 && y + 1 < height,
                        backgroundMask,
                        connectedBackground,
                        pending);
                    TryQueueBackgroundNeighbor(
                        index + width + 1,
                        x + 1 < width && y + 1 < height,
                        backgroundMask,
                        connectedBackground,
                        pending);
                }

                float edgeThreshold = Mathf.Clamp01(bridgeThreshold * 1.5f);
                float edgeThresholdSq = edgeThreshold * edgeThreshold;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    if (connectedBackground[i])
                    {
                        float distance = Mathf.Sqrt(ColorDistanceSquared(pixel, key));
                        // 精确匹配和小断缝都视为背景删除；桥接阈值只比
                        // 用户容差放宽 50%，不会把差异明显的角色像素连进去。
                        if (distance <= bridgeThreshold)
                        {
                            pixel.a = 0;
                        }
                    }
                    else if (ColorDistanceSquared(pixel, key) <= edgeThresholdSq
                        && HasConnectedBackgroundNeighbor(i, width, height, connectedBackground))
                    {
                        float distance = Mathf.Sqrt(ColorDistanceSquared(pixel, key));
                        float edgeAlpha = Mathf.InverseLerp(bridgeThreshold, edgeThreshold, distance);
                        pixel.a = (byte)Mathf.Clamp(Mathf.RoundToInt(edgeAlpha * 255f), 0, 255);
                    }

                    pixels[i] = pixel;
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
                return true;
            }
            finally
            {
                DestroyTexture(ref texture);
            }
        }

        private static float ColorDistanceSquared(Color32 pixel, Color key)
        {
            float dr = (pixel.r / 255f) - key.r;
            float dg = (pixel.g / 255f) - key.g;
            float db = (pixel.b / 255f) - key.b;
            return dr * dr + dg * dg + db * db;
        }

        private static void TryQueueBackgroundPixel(
            int index,
            bool[] backgroundMask,
            bool[] connectedBackground,
            Queue<int> pending)
        {
            if (backgroundMask[index] && !connectedBackground[index])
            {
                connectedBackground[index] = true;
                pending.Enqueue(index);
            }
        }

        private static void TryQueueBackgroundNeighbor(
            int index,
            bool valid,
            bool[] backgroundMask,
            bool[] connectedBackground,
            Queue<int> pending)
        {
            if (valid)
            {
                TryQueueBackgroundPixel(index, backgroundMask, connectedBackground, pending);
            }
        }

        private static bool HasConnectedBackgroundNeighbor(
            int index,
            int width,
            int height,
            bool[] connectedBackground)
        {
            int x = index % width;
            int y = index / width;
            return (x > 0 && connectedBackground[index - 1])
                || (x + 1 < width && connectedBackground[index + 1])
                || (y > 0 && connectedBackground[index - width])
                || (y + 1 < height && connectedBackground[index + width]);
        }

        private static int GetImageWidth(string path)
        {
            Texture2D texture = LoadTexture(path);
            int width = texture == null ? 0 : texture.width;
            DestroyTexture(ref texture);
            return width;
        }

        private static int GetImageHeight(string path)
        {
            Texture2D texture = LoadTexture(path);
            int height = texture == null ? 0 : texture.height;
            DestroyTexture(ref texture);
            return height;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
            {
                UnityEngine.Object.DestroyImmediate(texture);
                texture = null;
            }
        }

        private int CountSelectedFrames()
        {
            int count = 0;
            for (int i = 0; i < document.frames.Count; i++)
            {
                if (document.frames[i] != null && document.frames[i].selected)
                {
                    count++;
                }
            }

            return count;
        }

        private void EnsureDocument()
        {
            if (document == null)
            {
                document = new SequenceFrameAnimationDocument();
            }

            if (document.frames == null)
            {
                document.frames = new List<SequenceFrameData>();
            }
        }

        private static bool TryGetProjectAssetFolder(string path, out string assetPath)
        {
            assetPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string assetsPath = Path.GetFullPath(Application.dataPath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(assetsPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, assetsPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            assetPath = "Assets" + fullPath.Substring(assetsPath.Length).Replace('\\', '/');
            return true;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string SafeName(string value)
        {
            string result = string.IsNullOrWhiteSpace(value) ? "sequence_animation" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid.ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(result) ? "sequence_animation" : result;
        }
    }
}
#endif
