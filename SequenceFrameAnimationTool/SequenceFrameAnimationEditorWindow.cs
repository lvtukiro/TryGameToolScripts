#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        private string status = "请选择动作视频或序列帧图片。";
        private bool hasExtractedSource;

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

            EditorGUILayout.Space(8f);
            document.animationId = EditorGUILayout.TextField("导出名称", document.animationId);
            EditorGUILayout.LabelField(
                "用于帧目录、动作 JSON 和预览名称；Action ID 仍单独填写。",
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
                "每张序列帧都应包含人物和当前武器的完整画面；工具不会再单独读取或贴合武器帧。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawActionPreviewControlPanel()
        {
            EditorGUILayout.LabelField("动作预览", EditorStyles.boldLabel);
            document.animationId = EditorGUILayout.TextField("导出名称", document.animationId);
            document.actionId = EditorGUILayout.IntField("Action ID", document.actionId);
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
                // 非循环动作完成后停在最后一帧。再次点击播放时从第 0 帧
                // 重新开始；暂停中的其它帧仍保持原有的继续播放行为。
                if (!isPlaying
                    && !document.loop
                    && document.frames != null
                    && document.frames.Count > 0
                    && playbackFrame >= document.frames.Count - 1)
                {
                    playbackFrame = 0;
                    LoadPreviewFrame(playbackFrame);
                }

                isPlaying = !isPlaying;
                lastPlaybackTime = EditorApplication.timeSinceStartup;
            }

            if (GUILayout.Button("停止"))
            {
                isPlaying = false;
                playbackFrame = 0;
                LoadPreviewFrame(playbackFrame);
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
                document.frames == null || document.frames.Count == 0))
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
            Rect previewRect = GUILayoutUtility.GetRect(
                100f,
                10000f,
                100f,
                10000f,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.13f, 0.14f));
            DrawTextureFit(previewTexture, previewRect);

            DrawFrameList();
            EditorGUILayout.EndVertical();
        }

        private void DrawFrameList()
        {
            EditorGUILayout.LabelField(
                "完整角色帧（已选 " + CountSelectedFrames() + "/" + document.frames.Count + "）",
                EditorStyles.boldLabel);
            frameScroll = EditorGUILayout.BeginScrollView(
                frameScroll,
                GUILayout.Height(170f));
            List<int> displayOrder = BuildFrameDisplayOrder();
            for (int displayIndex = 0; displayIndex < displayOrder.Count; displayIndex++)
            {
                int frameIndex = displayOrder[displayIndex];
                SequenceFrameData frame = document.frames[frameIndex];
                if (frame == null)
                {
                    continue;
                }

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
            }
            EditorGUILayout.EndScrollView();
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
            document.frames.Clear();
            DestroyTexture(ref previewTexture);
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
                File.Copy(selected[i].sourceFilePath, ToAbsolutePath(assetPath), true);
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

            if (document.frames == null || document.frames.Count == 0)
            {
                EditorUtility.DisplayDialog("生成序列帧 Clip", "请先导出至少一张完整角色帧。", "确定");
                return;
            }

            List<Sprite> sprites = LoadSpriteAssets(document.frames);
            if (!AreFramesExportedToCurrentDestination()
                || sprites.Count != document.frames.Count)
            {
                // 拆帧后的 sourceFilePath 仍然指向临时帧文件；如果用户还没有点过
                // “保存选中完整帧并导出”，这里直接复用同一套导出逻辑，完成复制、
                // Sprite 导入和 JSON 保存，然后继续生成 Clip。
                if (!ExportFrames())
                {
                    return;
                }

                sprites = LoadSpriteAssets(document.frames);
                if (sprites.Count != document.frames.Count)
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

        private bool AreFramesExportedToCurrentDestination()
        {
            if (!TryGetProjectAssetFolder(outputAssetFolder, out string outputFolder))
            {
                return false;
            }

            string expectedFolder = outputFolder.TrimEnd('/') + "/" + SafeName(document.animationId);
            for (int i = 0; i < document.frames.Count; i++)
            {
                SequenceFrameData frame = document.frames[i];
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

            return document.frames.Count > 0;
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
            selectedFrameListIndex = index;
            DestroyTexture(ref previewTexture);
            previewTexture = LoadTexture(document.frames[index].sourceFilePath);
            Repaint();
        }

        private void UpdatePlayback()
        {
            if (!isPlaying || document == null || document.frames.Count == 0)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float fps = Mathf.Max(1f, document.frameRate);
            if (now - lastPlaybackTime < 1f / fps)
            {
                return;
            }

            playbackFrame++;
            lastPlaybackTime = now;
            if (playbackFrame >= document.frames.Count)
            {
                if (document.loop)
                {
                    playbackFrame = 0;
                }
                else
                {
                    // 非循环播放完成后停在最后一帧，便于检查结束姿态；
                    // 再次点击“播放”时由按钮逻辑回到第 0 帧重播。
                    playbackFrame = document.frames.Count - 1;
                    isPlaying = false;
                    lastPlaybackTime = now;
                    LoadPreviewFrame(playbackFrame);
                    return;
                }
            }

            LoadPreviewFrame(playbackFrame);
        }

        private void DrawTextureFit(Texture2D texture, Rect rect)
        {
            if (texture == null)
            {
                GUI.Label(rect, "暂无预览图", EditorStyles.centeredGreyMiniLabel);
                return;
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

            GUI.DrawTexture(drawRect, texture, ScaleMode.StretchToFill, true);
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
            hasExtractedSource = document.frames.Count > 0;
            selectedFrameListIndex = document.frames.Count > 0 ? 0 : -1;
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
