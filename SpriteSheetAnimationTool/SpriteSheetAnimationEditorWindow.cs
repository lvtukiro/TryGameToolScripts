#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Game.SequenceFrameAnimation;
using Game.EditorTools.SequenceFrameAnimation;

namespace Game.EditorTools.SpriteSheetAnimation
{
    /// <summary>
    /// 独立的帧图角色动画工具。
    ///
    /// 只负责读取一张网格帧图、切帧、透明背景处理、统一画布和导出。
    /// 视频拆帧仍由 SequenceFrameAnimationEditorWindow 负责，两者没有共享入口。
    /// </summary>
    public sealed class SpriteSheetAnimationEditorWindow : EditorWindow
    {
        private const int MaxGridCount = 128;
        private const float DefaultBackgroundTolerance = 0.08f;
        private const string OutputRoot =
            "Assets/Resources/TryGameBuildRes/clip_sprite/sequence_animation";

        private SpriteSheetAnimationDocument document;
        private string sourceImagePath = string.Empty;
        private string outputAssetFolder = OutputRoot;
        private int columns = 3;
        private int rows = 2;
        private int selectedFrameIndex = -1;
        private Texture2D previewTexture;
        private Vector2 frameListScroll;
        private bool hasCutFrames;
        private string temporaryFolder = string.Empty;
        private string status = "请选择一张帧图。";
        private bool backgroundColorSampled;
        private bool isPlaying;
        private double lastPlaybackTime;
        private int playbackFrame;

        /// <summary>
        /// 打开独立的帧图工具入口。
        /// </summary>
        [MenuItem("TryGame/工具/帧图角色动画工具")]
        public static void Open()
        {
            SpriteSheetAnimationEditorWindow window =
                GetWindow<SpriteSheetAnimationEditorWindow>("帧图角色动画工具");
            window.minSize = new Vector2(1000f, 680f);
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

        private void OnDestroy()
        {
            CleanupTemporaryFolder();
        }

        private void OnGUI()
        {
            EnsureDocument();
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawControlPanel();
            DrawPreviewPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("帧图角色动画工具", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(status, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawControlPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(360f));
            EditorGUILayout.LabelField("帧图来源", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                string.IsNullOrWhiteSpace(sourceImagePath) ? "未选择" : sourceImagePath,
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("选择帧图"))
            {
                SelectSourceImage();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("网格切割", EditorStyles.boldLabel);
            columns = Mathf.Clamp(EditorGUILayout.IntField("列数", columns), 1, MaxGridCount);
            rows = Mathf.Clamp(EditorGUILayout.IntField("行数", rows), 1, MaxGridCount);
            EditorGUILayout.LabelField(
                "切割顺序：从上到下、从左到右。图片宽高必须能被列数和行数整除。",
                EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(sourceImagePath)))
            {
                if (GUILayout.Button(hasCutFrames ? "重新切割网格" : "切割网格"))
                {
                    CutGridFrames();
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("动作信息", EditorStyles.boldLabel);
            document.animationId = EditorGUILayout.TextField("导出名称", document.animationId);
            document.actionId = EditorGUILayout.IntField("动作 ID", document.actionId);
            document.frameRate = Mathf.Max(1f, EditorGUILayout.FloatField("播放 FPS", document.frameRate));
            document.loop = EditorGUILayout.Toggle("循环播放", document.loop);
            document.defaultFacingLeft = EditorGUILayout.Toggle(
                "素材默认朝向左",
                document.defaultFacingLeft);
            document.pivotNormalized = EditorGUILayout.Vector2Field(
                "Sprite Pivot",
                document.pivotNormalized);
            document.pivotNormalized = new Vector2(
                Mathf.Clamp01(document.pivotNormalized.x),
                Mathf.Clamp01(document.pivotNormalized.y));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("透明背景", EditorStyles.boldLabel);
            document.removeBackground = EditorGUILayout.Toggle("扣除纯色背景", document.removeBackground);
            EditorGUILayout.BeginHorizontal();
            document.backgroundKeyColor = EditorGUILayout.ColorField(
                "背景色",
                document.backgroundKeyColor);
            if (GUILayout.Button("取底色", GUILayout.Width(72f)))
            {
                SampleBackgroundColor();
            }
            EditorGUILayout.EndHorizontal();
            document.backgroundTolerance = Mathf.Clamp01(EditorGUILayout.Slider(
                "扣底容差",
                document.backgroundTolerance <= 0f
                    ? DefaultBackgroundTolerance
                    : document.backgroundTolerance,
                0f,
                0.3f));
            EditorGUILayout.LabelField(
                "已有透明通道的图片会保留透明像素；只有勾选扣底色时才处理不透明背景。",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("输出尺寸与角色缩放", EditorStyles.boldLabel);
            document.canvasWidth = Mathf.Max(1, EditorGUILayout.IntField(
                "输出画布宽度",
                document.canvasWidth));
            document.canvasHeight = Mathf.Max(1, EditorGUILayout.IntField(
                "输出画布高度",
                document.canvasHeight));
            float scalePercent = EditorGUILayout.Slider(
                "角色内容缩放 %",
                GetContentScalePercent(),
                1f,
                400f);
            SetContentScalePercent(scalePercent);
            EditorGUILayout.LabelField(
                "100% 以本动作的最大内容高度为统一参考，各帧等比对齐到同一角色尺度。",
                EditorStyles.wordWrappedMiniLabel);
            document.footBaselineNormalized = EditorGUILayout.Slider(
                "脚底基准线",
                document.footBaselineNormalized,
                0f,
                0.5f);
            document.canvasCenterOffsetNormalized = EditorGUILayout.Vector2Field(
                "画布中心偏移",
                document.canvasCenterOffsetNormalized);
            document.canvasCenterOffsetNormalized = new Vector2(
                Mathf.Clamp(document.canvasCenterOffsetNormalized.x, -0.5f, 0.5f),
                Mathf.Clamp(document.canvasCenterOffsetNormalized.y, -0.5f, 0.5f));
            if (GUILayout.Button("从当前帧设置默认输出尺寸"))
            {
                SetCanvasSizeFromCurrentFrame();
            }
            using (new EditorGUI.DisabledScope(!hasCutFrames))
            {
                if (GUILayout.Button("按输出尺寸和缩放标准化全部帧"))
                {
                    StandardizeFrames();
                }
            }

            EditorGUILayout.Space(6f);
            outputAssetFolder = EditorGUILayout.TextField("输出 Assets 目录", outputAssetFolder);
            if (GUILayout.Button("选择输出目录"))
            {
                SelectOutputFolder();
            }
            using (new EditorGUI.DisabledScope(!hasCutFrames || CountSelectedFrames() == 0))
            {
                if (GUILayout.Button("导出选中帧和动作 JSON"))
                {
                    ExportFrames();
                }
                if (GUILayout.Button("生成正式序列帧 Clip"))
                {
                    ExportClip();
                }
            }
            if (GUILayout.Button("读取帧图动作 JSON"))
            {
                LoadDocument();
            }
            if (GUILayout.Button("生成序列帧角色预制体..."))
            {
                SequenceFrameAnimationPrefabBuilder.CreatePrefabInteractive();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewPanel()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(
                "预览：" + (hasCutFrames ? "已切出 " + document.frames.Count + " 帧" : "尚未切割"),
                EditorStyles.boldLabel);
            Rect previewRect = GUILayoutUtility.GetRect(
                100f,
                10000f,
                100f,
                10000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.13f, 0.14f));
            if (previewTexture != null)
            {
                Rect textureRect = GetTextureDrawRect(previewTexture, previewRect);
                GUI.DrawTexture(textureRect, previewTexture, ScaleMode.StretchToFill, true);
            }
            DrawFrameList();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isPlaying ? "暂停" : "播放"))
            {
                isPlaying = !isPlaying;
                lastPlaybackTime = EditorApplication.timeSinceStartup;
            }
            if (GUILayout.Button("停止"))
            {
                isPlaying = false;
                playbackFrame = 0;
                LoadPreviewFrameFromSelected();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawFrameList()
        {
            if (!hasCutFrames || document.frames == null)
            {
                return;
            }

            EditorGUILayout.LabelField("帧列表（可取消不需要的帧）", EditorStyles.boldLabel);
            frameListScroll = EditorGUILayout.BeginScrollView(
                frameListScroll,
                GUILayout.Height(Mathf.Min(180f, 24f * Mathf.Max(1, document.frames.Count))));
            for (int i = 0; i < document.frames.Count; i++)
            {
                SpriteSheetFrameData frame = document.frames[i];
                if (frame == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                bool selected = EditorGUILayout.Toggle(frame.selected, GUILayout.Width(18f));
                if (selected != frame.selected)
                {
                    frame.selected = selected;
                }
                if (GUILayout.Button(
                        "帧 " + (i + 1) + (selectedFrameIndex == i ? "  ◀" : ""),
                        EditorStyles.miniButton))
                {
                    LoadPreviewFrame(i);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void EnsureDocument()
        {
            if (document == null)
            {
                document = new SpriteSheetAnimationDocument();
            }

            if (document.backgroundTolerance <= 0f)
            {
                document.backgroundTolerance = DefaultBackgroundTolerance;
            }
            if (document.canvasWidth <= 0 || document.canvasHeight <= 0)
            {
                document.canvasWidth = 512;
                document.canvasHeight = 512;
            }
        }

        private void SelectSourceImage()
        {
            string path = EditorUtility.OpenFilePanel(
                "选择帧图",
                Application.dataPath,
                "png,jpg,jpeg,tga,bmp");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            sourceImagePath = path;
            hasCutFrames = false;
            document.frames.Clear();
            selectedFrameIndex = -1;
            backgroundColorSampled = false;
            DestroyTexture(ref previewTexture);
            status = "已选择帧图，请设置行列后切割。";
            Repaint();
        }

        private void CutGridFrames()
        {
            Texture2D source = LoadTexture(sourceImagePath);
            if (source == null)
            {
                status = "读取帧图失败。";
                return;
            }

            try
            {
                if (source.width % columns != 0 || source.height % rows != 0)
                {
                    EditorUtility.DisplayDialog(
                        "切割网格",
                        "图片尺寸 " + source.width + "×" + source.height
                        + " 不能被 " + columns + "×" + rows + " 整除。",
                        "确定");
                    return;
                }

                CleanupTemporaryFolder();
                temporaryFolder = Path.Combine(
                    Path.GetTempPath(),
                    "TryAiGame",
                    "SpriteSheetAnimation",
                    SafeName(Path.GetFileNameWithoutExtension(sourceImagePath))
                    + "_" + DateTime.Now.Ticks);
                Directory.CreateDirectory(temporaryFolder);
                int frameWidth = source.width / columns;
                int frameHeight = source.height / rows;
                document.frames.Clear();
                backgroundColorSampled = false;
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int sourceY = source.height - (row + 1) * frameHeight;
                        Color[] pixels = source.GetPixels(
                            column * frameWidth,
                            sourceY,
                            frameWidth,
                            frameHeight);
                        Texture2D frameTexture = new Texture2D(
                            frameWidth,
                            frameHeight,
                            TextureFormat.RGBA32,
                            false);
                        try
                        {
                            frameTexture.SetPixels(pixels);
                            frameTexture.Apply(false, false);
                            string path = Path.Combine(
                                temporaryFolder,
                                "frame_" + document.frames.Count.ToString("D4") + ".png");
                            File.WriteAllBytes(path, frameTexture.EncodeToPNG());
                            document.frames.Add(new SpriteSheetFrameData
                            {
                                sourceFrameIndex = document.frames.Count,
                                sourceFilePath = path,
                                selected = true,
                            });
                        }
                        finally
                        {
                            DestroyImmediate(frameTexture);
                        }
                    }
                }

                document.canvasWidth = frameWidth;
                document.canvasHeight = frameHeight;
                hasCutFrames = document.frames.Count > 0;
                selectedFrameIndex = hasCutFrames ? 0 : -1;
                LoadPreviewFrame(0);
                if (document.removeBackground || AllFramesHaveTransparency())
                {
                    // 已有透明通道的帧可以直接统一脚底；勾选扣底色时，
                    // 则先按每帧边缘背景色抠图，再统一脚底。
                    StandardizeFrames();
                    status = "已切割并统一 " + document.frames.Count + " 张帧（"
                        + frameWidth + "×" + frameHeight + "）。";
                }
                else
                {
                    status = "已切割 " + document.frames.Count + " 张帧（" + frameWidth
                        + "×" + frameHeight + "）。源图仍是不透明背景，请勾选“扣除纯色背景”后再标准化。";
                }
            }
            finally
            {
                DestroyImmediate(source);
            }
            Repaint();
        }

        /// <summary>
        /// 判断切出的所有帧是否已有透明像素。只有全部透明帧才能在不抠背景的
        /// 情况下正确计算人物脚底；只要有一帧不透明，就必须先执行纯色背景处理。
        /// </summary>
        private bool AllFramesHaveTransparency()
        {
            if (document == null || document.frames == null)
            {
                return false;
            }

            for (int i = 0; i < document.frames.Count; i++)
            {
                SpriteSheetFrameData frame = document.frames[i];
                Texture2D texture = LoadTexture(frame == null ? string.Empty : frame.sourceFilePath);
                if (texture == null)
                {
                    continue;
                }
                try
                {
                    Color32[] pixels = texture.GetPixels32();
                    bool hasTransparency = false;
                    for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                    {
                        if (pixels[pixelIndex].a < 250)
                        {
                            hasTransparency = true;
                            break;
                        }
                    }
                    if (!hasTransparency)
                    {
                        return false;
                    }
                }
                finally
                {
                    DestroyImmediate(texture);
                }
            }

            return document.frames.Count > 0;
        }

        private void SampleBackgroundColor()
        {
            Texture2D texture = LoadTexture(GetCurrentFramePath());
            if (texture == null)
            {
                status = "请先切割网格并选择帧。";
                return;
            }

            try
            {
                document.backgroundKeyColor = SampleCornerColor(texture);
                backgroundColorSampled = true;
                status = "已取底色：" + ColorUtility.ToHtmlStringRGBA(
                    document.backgroundKeyColor);
            }
            finally
            {
                DestroyImmediate(texture);
            }
            Repaint();
        }

        private void SetCanvasSizeFromCurrentFrame()
        {
            Texture2D texture = LoadTexture(GetCurrentFramePath());
            if (texture == null)
            {
                return;
            }
            document.canvasWidth = texture.width;
            document.canvasHeight = texture.height;
            DestroyImmediate(texture);
            status = "已设置输出画布为当前帧尺寸。";
            Repaint();
        }

        private float GetContentScalePercent()
        {
            return document.contentScalePercent <= 0f ? 100f : document.contentScalePercent;
        }

        private void SetContentScalePercent(float value)
        {
            document.contentScalePercent = Mathf.Clamp(value, 1f, 400f);
        }

        private void StandardizeFrames()
        {
            if (!hasCutFrames || document.frames == null || document.frames.Count == 0)
            {
                return;
            }
            if (document.canvasWidth <= 0 || document.canvasHeight <= 0)
            {
                status = "输出画布宽高必须大于 0。";
                return;
            }

            int processed = 0;
            int failed = 0;
            // 先统一处理所有帧的背景，再用同一个参考内容高度标准化，
            // 避免前后帧分别按自己的边界缩放。
            for (int i = 0; i < document.frames.Count; i++)
            {
                SpriteSheetFrameData frame = document.frames[i];
                if (frame == null || string.IsNullOrWhiteSpace(frame.sourceFilePath))
                {
                    failed++;
                    continue;
                }
                if (document.removeBackground && !RemoveBackgroundFromFrame(frame.sourceFilePath))
                {
                    failed++;
                    continue;
                }
            }
            int referenceContentHeight = FindReferenceContentHeight();
            if (referenceContentHeight <= 0)
            {
                status = "没有检测到可标准化的人物内容。";
                Repaint();
                return;
            }

            processed = 0;
            LoadPreviewFrame(selectedFrameIndex < 0 ? 0 : selectedFrameIndex);
            for (int i = 0; i < document.frames.Count; i++)
            {
                SpriteSheetFrameData frame = document.frames[i];
                if (frame == null || string.IsNullOrWhiteSpace(frame.sourceFilePath))
                {
                    failed++;
                    continue;
                }
                if (TryStandardizeFrame(
                        frame.sourceFilePath,
                        referenceContentHeight,
                        out bool clipped))
                {
                    processed++;
                    if (clipped)
                    {
                        failed++;
                    }
                }
                else
                {
                    failed++;
                }
            }
            LoadPreviewFrame(selectedFrameIndex < 0 ? 0 : selectedFrameIndex);
            status = "已按公共内容高度 " + referenceContentHeight + " 像素标准化 "
                + processed + " 张帧" + (failed > 0
                ? "，" + failed + " 张失败或内容超出画布。"
                : "。");
            Repaint();
        }

        /// <summary>
        /// 读取所有帧的透明内容边界，取最大高度作为动作的公共参考高度。
        /// 这样小一号的帧会等比放大到同一角色尺度，而不会各自使用不同缩放值。
        /// </summary>
        private int FindReferenceContentHeight()
        {
            int referenceHeight = 0;
            for (int i = 0; i < document.frames.Count; i++)
            {
                SpriteSheetFrameData frame = document.frames[i];
                Texture2D texture = LoadTexture(frame == null ? string.Empty : frame.sourceFilePath);
                if (texture == null)
                {
                    continue;
                }
                try
                {
                    Color32[] pixels = texture.GetPixels32();
                    int bottom = texture.height;
                    int top = -1;
                    for (int y = 0; y < texture.height; y++)
                    {
                        for (int x = 0; x < texture.width; x++)
                        {
                            if (pixels[y * texture.width + x].a <= 8)
                            {
                                continue;
                            }
                            bottom = Mathf.Min(bottom, y);
                            top = Mathf.Max(top, y);
                        }
                    }
                    if (top >= bottom && bottom < texture.height)
                    {
                        referenceHeight = Mathf.Max(referenceHeight, top - bottom + 1);
                    }
                }
                finally
                {
                    DestroyImmediate(texture);
                }
            }
            return referenceHeight;
        }

        /// <summary>
        /// 从图片边缘连通地移除背景色，避免误删角色内部同色区域。
        /// 已有透明像素的帧不会重新做颜色键处理。
        /// </summary>
        private bool RemoveBackgroundFromFrame(string path)
        {
            Texture2D texture = LoadTexture(path);
            if (texture == null)
            {
                return false;
            }
            try
            {
                Color32[] pixels = texture.GetPixels32();
                bool hasTransparency = false;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a < 250)
                    {
                        hasTransparency = true;
                        break;
                    }
                }
                if (hasTransparency)
                {
                    return true;
                }

                Color key = backgroundColorSampled
                    ? document.backgroundKeyColor
                    : SampleCornerColor(texture);
                float tolerance = Mathf.Clamp01(document.backgroundTolerance);
                float threshold = tolerance * tolerance;
                bool[] candidate = new bool[pixels.Length];
                bool[] visited = new bool[pixels.Length];
                Queue<int> pending = new Queue<int>();
                for (int i = 0; i < pixels.Length; i++)
                {
                    candidate[i] = ColorDistanceSquared(pixels[i], key) <= threshold;
                }
                for (int y = 0; y < texture.height; y++)
                {
                    TryQueueBackgroundPixel(y * texture.width, candidate, visited, pending);
                    TryQueueBackgroundPixel(
                        y * texture.width + texture.width - 1,
                        candidate,
                        visited,
                        pending);
                }
                for (int x = 0; x < texture.width; x++)
                {
                    TryQueueBackgroundPixel(x, candidate, visited, pending);
                    TryQueueBackgroundPixel(
                        (texture.height - 1) * texture.width + x,
                        candidate,
                        visited,
                        pending);
                }
                while (pending.Count > 0)
                {
                    int index = pending.Dequeue();
                    pixels[index].a = 0;
                    int x = index % texture.width;
                    int y = index / texture.width;
                    TryQueueBackgroundNeighbor(index - 1, x > 0, candidate, visited, pending);
                    TryQueueBackgroundNeighbor(
                        index + 1,
                        x + 1 < texture.width,
                        candidate,
                        visited,
                        pending);
                    TryQueueBackgroundNeighbor(
                        index - texture.width,
                        y > 0,
                        candidate,
                        visited,
                        pending);
                    TryQueueBackgroundNeighbor(
                        index + texture.width,
                        y + 1 < texture.height,
                        candidate,
                        visited,
                        pending);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return true;
            }
            finally
            {
                DestroyImmediate(texture);
            }
        }

        private static void TryQueueBackgroundPixel(
            int index,
            bool[] candidate,
            bool[] visited,
            Queue<int> pending)
        {
            if (index < 0 || index >= candidate.Length || visited[index] || !candidate[index])
            {
                return;
            }
            visited[index] = true;
            pending.Enqueue(index);
        }

        private static void TryQueueBackgroundNeighbor(
            int index,
            bool valid,
            bool[] candidate,
            bool[] visited,
            Queue<int> pending)
        {
            if (valid)
            {
                TryQueueBackgroundPixel(index, candidate, visited, pending);
            }
        }

        private static float ColorDistanceSquared(Color32 pixel, Color key)
        {
            float r = pixel.r / 255f - key.r;
            float g = pixel.g / 255f - key.g;
            float b = pixel.b / 255f - key.b;
            return r * r + g * g + b * b;
        }

        private bool TryStandardizeFrame(
            string path,
            int referenceContentHeight,
            out bool clipped)
        {
            clipped = false;
            Texture2D source = LoadTexture(path);
            if (source == null)
            {
                return false;
            }
            try
            {
                Color32[] pixels = source.GetPixels32();
                int contentLeft = source.width;
                int contentRight = -1;
                int contentBottom = source.height;
                int contentTop = -1;
                for (int y = 0; y < source.height; y++)
                {
                    for (int x = 0; x < source.width; x++)
                    {
                        if (pixels[y * source.width + x].a > 8)
                        {
                            contentLeft = Mathf.Min(contentLeft, x);
                            contentRight = Mathf.Max(contentRight, x);
                            contentBottom = Mathf.Min(contentBottom, y);
                            contentTop = Mathf.Max(contentTop, y);
                        }
                    }
                }
                if (contentRight < contentLeft || contentTop < contentBottom)
                {
                    return false;
                }

                // 所有帧共用同一个参考内容高度；在此基础上再乘统一的
                // 内容缩放配置，避免不同源帧的角色大小不一致。
                float scale = GetContentScalePercent() / 100f
                    * referenceContentHeight / (contentTop - contentBottom + 1f);
                Color32[] output = new Color32[document.canvasWidth * document.canvasHeight];
                int baselineY = Mathf.Clamp(
                    Mathf.RoundToInt((document.canvasHeight - 1)
                        * document.footBaselineNormalized),
                    0,
                    document.canvasHeight - 1);
                // 每帧使用自己的内容中心作为锚点，避免透明边缘或披风伸展
                // 把人物整体推离画布中心；缩放比例仍然是所有帧共用的。
                float sourceCenterX = (contentLeft + contentRight) * 0.5f;
                float targetCenterX = (document.canvasWidth - 1) * 0.5f
                    + document.canvasCenterOffsetNormalized.x * document.canvasWidth;
                int targetOffsetY = baselineY
                    + Mathf.RoundToInt(
                        document.canvasCenterOffsetNormalized.y * document.canvasHeight);
                float transformedLeft = targetCenterX
                    + (contentLeft - sourceCenterX) * scale;
                float transformedRight = targetCenterX
                    + (contentRight - sourceCenterX) * scale;
                float transformedBottom = targetOffsetY
                    + (contentBottom - contentBottom) * scale;
                float transformedTop = targetOffsetY
                    + (contentTop - contentBottom) * scale;
                clipped = transformedLeft < 0f
                    || transformedRight >= document.canvasWidth
                    || transformedBottom < 0f
                    || transformedTop >= document.canvasHeight;
                for (int outputY = 0; outputY < document.canvasHeight; outputY++)
                {
                    int sourceY = Mathf.RoundToInt(
                        contentBottom + (outputY - targetOffsetY) / scale);
                    if (sourceY < 0 || sourceY >= source.height)
                    {
                        continue;
                    }
                    for (int outputX = 0; outputX < document.canvasWidth; outputX++)
                    {
                        int sourceX = Mathf.RoundToInt(
                            sourceCenterX + (outputX - targetCenterX) / scale);
                        if (sourceX < 0 || sourceX >= source.width)
                        {
                            continue;
                        }
                        Color32 pixel = pixels[sourceY * source.width + sourceX];
                        if (pixel.a > 0)
                        {
                            output[outputY * document.canvasWidth + outputX] = pixel;
                        }
                    }
                }

                Texture2D standardized = new Texture2D(
                    document.canvasWidth,
                    document.canvasHeight,
                    TextureFormat.RGBA32,
                    false);
                try
                {
                    standardized.SetPixels32(output);
                    standardized.Apply(false, false);
                    File.WriteAllBytes(path, standardized.EncodeToPNG());
                }
                finally
                {
                    DestroyImmediate(standardized);
                }
                return !clipped;
            }
            finally
            {
                DestroyImmediate(source);
            }
        }

        private void ExportFrames()
        {
            if (!TryGetOutputFolder(out string outputFolder))
            {
                EditorUtility.DisplayDialog("导出帧图", "输出目录必须位于当前项目 Assets 下。", "确定");
                return;
            }
            List<SpriteSheetFrameData> selected = GetSelectedFrames();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("导出帧图", "请至少保留一帧。", "确定");
                return;
            }

            string animationFolder = outputFolder.TrimEnd('/') + "/" + SafeName(document.animationId);
            Directory.CreateDirectory(ToAbsolutePath(animationFolder));
            DeleteOldExportedFrames(animationFolder);
            for (int i = 0; i < selected.Count; i++)
            {
                string assetPath = animationFolder + "/frame_" + i.ToString("D4") + ".png";
                File.Copy(selected[i].sourceFilePath, ToAbsolutePath(assetPath), true);
                selected[i].exportedAssetPath = assetPath;
            }
            document.frames = selected;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < selected.Count; i++)
            {
                ConfigureSpriteImporter(selected[i].exportedAssetPath, document.pivotNormalized);
            }
            SaveDocumentToPath(animationFolder + "/" + SafeName(document.animationId) + ".sequence.json");
            status = "已导出 " + selected.Count + " 张帧和动作 JSON。";
            Repaint();
        }

        private void ExportClip()
        {
            if (document.actionId <= 0)
            {
                EditorUtility.DisplayDialog("生成序列帧 Clip", "动作 ID 必须大于 0。", "确定");
                return;
            }
            if (CountSelectedFrames() == 0)
            {
                EditorUtility.DisplayDialog("生成序列帧 Clip", "请至少保留一帧。", "确定");
                return;
            }
            if (!AreSelectedFramesExported())
            {
                ExportFrames();
            }
            List<Sprite> sprites = LoadSpriteAssets(document.frames);
            if (sprites.Count != document.frames.Count)
            {
                EditorUtility.DisplayDialog("生成序列帧 Clip", "请先确认导出帧已导入为 Sprite。", "确定");
                return;
            }

            string assetPath = OutputRoot + "/clip_" + document.actionId + "_1.asset";
            SequenceFrameClip existing = AssetDatabase.LoadAssetAtPath<SequenceFrameClip>(assetPath);
            if (existing != null && !EditorUtility.DisplayDialog(
                    "覆盖序列帧 Clip",
                    "Action ID " + document.actionId + " 已存在，是否覆盖？",
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
            SerializedObject serialized = new SerializedObject(clip);
            serialized.FindProperty("actionId").intValue = document.actionId;
            serialized.FindProperty("frames").arraySize = sprites.Count;
            for (int i = 0; i < sprites.Count; i++)
            {
                serialized.FindProperty("frames").GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            }
            serialized.FindProperty("frameRate").floatValue = document.frameRate;
            serialized.FindProperty("loop").boolValue = document.loop;
            SerializedProperty facing = serialized.FindProperty("defaultFacingLeft");
            if (facing != null)
            {
                facing.boolValue = document.defaultFacingLeft;
            }
            serialized.FindProperty("canvasWidth").intValue = document.canvasWidth;
            serialized.FindProperty("canvasHeight").intValue = document.canvasHeight;
            serialized.FindProperty("pivotNormalized").vector2Value = document.pivotNormalized;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!clip.TryValidate(out string validationError))
            {
                DestroyImmediate(clip);
                EditorUtility.DisplayDialog("生成序列帧 Clip", validationError, "确定");
                return;
            }
            clip.name = Path.GetFileNameWithoutExtension(assetPath);
            Directory.CreateDirectory(ToAbsolutePath(OutputRoot));
            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SequenceFrameClip>(assetPath);
            status = "已生成序列帧 Clip：" + assetPath;
        }

        private bool AreSelectedFramesExported()
        {
            if (document.frames == null || document.frames.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < document.frames.Count; i++)
            {
                SpriteSheetFrameData frame = document.frames[i];
                if (frame == null || !frame.selected || string.IsNullOrWhiteSpace(frame.exportedAssetPath)
                    || !File.Exists(ToAbsolutePath(frame.exportedAssetPath)))
                {
                    return false;
                }
            }
            return true;
        }

        private List<SpriteSheetFrameData> GetSelectedFrames()
        {
            List<SpriteSheetFrameData> result = new List<SpriteSheetFrameData>();
            if (document.frames == null)
            {
                return result;
            }
            for (int i = 0; i < document.frames.Count; i++)
            {
                if (document.frames[i] != null && document.frames[i].selected)
                {
                    result.Add(document.frames[i]);
                }
            }
            return result;
        }

        private int CountSelectedFrames()
        {
            int count = 0;
            if (document != null && document.frames != null)
            {
                for (int i = 0; i < document.frames.Count; i++)
                {
                    if (document.frames[i] != null && document.frames[i].selected)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private void LoadDocument()
        {
            string path = EditorUtility.OpenFilePanel("读取帧图动作清单", Application.dataPath, "json,sequence.json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            string json = File.ReadAllText(path);
            SpriteSheetAnimationDocument source = JsonUtility.FromJson<SpriteSheetAnimationDocument>(json);
            if (source == null || source.frames == null)
            {
                status = "动作 JSON 读取失败。";
                return;
            }
            document = source;
            for (int i = 0; i < document.frames.Count; i++)
            {
                SpriteSheetFrameData frame = document.frames[i];
                if (frame != null && !File.Exists(frame.sourceFilePath))
                {
                    frame.sourceFilePath = ResolveAssetPath(frame.exportedAssetPath);
                }
            }
            sourceImagePath = string.Empty;
            hasCutFrames = document.frames.Count > 0;
            backgroundColorSampled = false;
            selectedFrameIndex = hasCutFrames ? 0 : -1;
            LoadPreviewFrame(0);
            status = "已读取帧图动作：" + path;
        }

        private void SaveDocumentToPath(string assetPath)
        {
            // 额外的 contentScalePercent 字段对旧的动作/预制体读取器透明，
            // 但帧图工具重新打开清单时可以保留本次标准化配置。
            File.WriteAllText(ToAbsolutePath(assetPath), JsonUtility.ToJson(document, true));
            AssetDatabase.ImportAsset(assetPath);
        }

        private void SelectOutputFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("选择输出 Assets 目录", Application.dataPath, string.Empty);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }
            if (!TryGetProjectAssetPath(folder, out string assetPath))
            {
                EditorUtility.DisplayDialog("输出目录", "请选择当前项目 Assets 内的目录。", "确定");
                return;
            }
            outputAssetFolder = assetPath;
        }

        private bool TryGetOutputFolder(out string outputFolder)
        {
            outputFolder = outputAssetFolder.Replace('\\', '/').TrimEnd('/');
            return outputFolder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(ToAbsolutePath(outputFolder));
        }

        private void LoadPreviewFrame(int index)
        {
            if (document == null || document.frames == null || document.frames.Count == 0)
            {
                selectedFrameIndex = -1;
                DestroyTexture(ref previewTexture);
                return;
            }
            selectedFrameIndex = Mathf.Clamp(index, 0, document.frames.Count - 1);
            DestroyTexture(ref previewTexture);
            previewTexture = LoadTexture(GetCurrentFramePath());
            Repaint();
        }

        private void LoadPreviewFrameFromSelected()
        {
            List<SpriteSheetFrameData> selected = GetSelectedFrames();
            if (selected.Count == 0)
            {
                return;
            }
            playbackFrame = Mathf.Clamp(playbackFrame, 0, selected.Count - 1);
            int sourceIndex = document.frames.IndexOf(selected[playbackFrame]);
            LoadPreviewFrame(sourceIndex);
        }

        private void UpdatePlayback()
        {
            if (!isPlaying || document == null || CountSelectedFrames() == 0)
            {
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            if (now - lastPlaybackTime < 1d / Mathf.Max(1f, document.frameRate))
            {
                return;
            }
            lastPlaybackTime = now;
            List<SpriteSheetFrameData> selected = GetSelectedFrames();
            playbackFrame++;
            if (playbackFrame >= selected.Count)
            {
                if (document.loop)
                {
                    playbackFrame = 0;
                }
                else
                {
                    playbackFrame = selected.Count - 1;
                    isPlaying = false;
                }
            }
            LoadPreviewFrameFromSelected();
        }

        private string GetCurrentFramePath()
        {
            if (document == null || document.frames == null
                || selectedFrameIndex < 0 || selectedFrameIndex >= document.frames.Count
                || document.frames[selectedFrameIndex] == null)
            {
                return string.Empty;
            }
            SpriteSheetFrameData frame = document.frames[selectedFrameIndex];
            if (!string.IsNullOrWhiteSpace(frame.sourceFilePath) && File.Exists(frame.sourceFilePath))
            {
                return frame.sourceFilePath;
            }
            return ResolveAssetPath(frame.exportedAssetPath);
        }

        private static string ResolveAssetPath(string assetPath)
        {
            return string.IsNullOrWhiteSpace(assetPath) ? string.Empty : ToAbsolutePath(assetPath);
        }

        private static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path), false))
            {
                DestroyImmediate(texture);
                return null;
            }
            return texture;
        }

        /// <summary>
        /// 释放当前窗口持有的临时预览纹理，并清空引用。
        /// </summary>
        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            DestroyImmediate(texture);
            texture = null;
        }

        private static Color SampleCornerColor(Texture2D texture)
        {
            Color sum = Color.clear;
            int count = 0;
            int size = Mathf.Clamp(Mathf.Min(texture.width, texture.height) / 16, 2, 16);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    sum += texture.GetPixel(x, y);
                    sum += texture.GetPixel(texture.width - 1 - x, y);
                    sum += texture.GetPixel(x, texture.height - 1 - y);
                    sum += texture.GetPixel(texture.width - 1 - x, texture.height - 1 - y);
                    count += 4;
                }
            }
            return count == 0 ? Color.white : sum / count;
        }

        private static Rect GetTextureDrawRect(Texture2D texture, Rect rect)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return rect;
            }
            float scale = Mathf.Min(rect.width / texture.width, rect.height / texture.height);
            float width = texture.width * scale;
            float height = texture.height * scale;
            return new Rect(
                rect.x + (rect.width - width) * 0.5f,
                rect.y + (rect.height - height) * 0.5f,
                width,
                height);
        }

        private static void ConfigureSpriteImporter(string assetPath, Vector2 pivot)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = pivot;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        private static void DeleteOldExportedFrames(string animationFolder)
        {
            string absoluteFolder = ToAbsolutePath(animationFolder);
            if (!Directory.Exists(absoluteFolder))
            {
                return;
            }
            string[] oldFrames = Directory.GetFiles(absoluteFolder, "frame_*.png", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < oldFrames.Length; i++)
            {
                File.Delete(oldFrames[i]);
                if (File.Exists(oldFrames[i] + ".meta"))
                {
                    File.Delete(oldFrames[i] + ".meta");
                }
            }
        }

        private static List<Sprite> LoadSpriteAssets(List<SpriteSheetFrameData> frames)
        {
            List<Sprite> sprites = new List<Sprite>();
            for (int i = 0; i < frames.Count; i++)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(frames[i].exportedAssetPath);
                if (sprite != null)
                {
                    sprites.Add(sprite);
                }
            }
            return sprites;
        }

        private void CleanupTemporaryFolder()
        {
            if (string.IsNullOrWhiteSpace(temporaryFolder) || !Directory.Exists(temporaryFolder))
            {
                return;
            }
            try
            {
                Directory.Delete(temporaryFolder, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SpriteSheetAnimationTool] 清理临时帧失败：" + exception.Message);
            }
            temporaryFolder = string.Empty;
        }

        private static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "sprite_sheet_animation";
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            string result = value;
            for (int i = 0; i < invalid.Length; i++)
            {
                result = result.Replace(invalid[i], '_');
            }
            return result.Trim();
        }

        private static string ToAbsolutePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }
            return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));
        }

        private static bool TryGetProjectAssetPath(string absolutePath, out string assetPath)
        {
            assetPath = string.Empty;
            string fullPath = Path.GetFullPath(absolutePath).TrimEnd(
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
    }
}
#endif
