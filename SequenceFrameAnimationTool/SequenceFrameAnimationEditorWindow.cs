#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.EditorTools.SequenceFrameAnimation
{
    public sealed class SequenceFrameAnimationEditorWindow : EditorWindow
    {
        private enum WorkflowTab
        {
            Body = 0,
            Weapon = 1,
            Preview = 2,
        }

        private const int DefaultExtractFrameRate = 12;
        private const float DefaultSelectThreshold = 0.06f;
        private const int DefaultSelectMinGap = 2;

        private WorkflowTab workflowTab;
        private SequenceFrameAnimationDocument document;
        private string sourceAnimationPath = string.Empty;
        private string extractedFrameFolder = string.Empty;
        private string weaponFrameFolder = string.Empty;
        private string outputAssetFolder = "Assets/BuildRes";
        private Texture2D previewTexture;
        private Texture2D previewWeaponTexture;
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

        // 主窗口唯一入口；预制体生成从“组合预览”页进入。
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
            DestroyTexture(ref previewWeaponTexture);
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
                new[] { "身体序列帧", "武器序列帧", "组合预览" },
                EditorStyles.toolbarButton,
                GUILayout.Height(24f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(360f));
            if (workflowTab == WorkflowTab.Body)
            {
                DrawBodyPanel();
            }
            else if (workflowTab == WorkflowTab.Weapon)
            {
                DrawWeaponPanel();
            }
            else
            {
                DrawPreviewControlPanel();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBodyPanel()
        {
            EditorGUILayout.LabelField("动作来源", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                string.IsNullOrWhiteSpace(sourceAnimationPath)
                    ? "未选择"
                    : sourceAnimationPath,
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("选择视频 / 序列帧图片"))
            {
                SelectBodySource();
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(sourceAnimationPath)))
            {
                if (GUILayout.Button(hasExtractedSource ? "重新拆帧" : "拆帧"))
                {
                    ExtractSelectedBodySource();
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
            using (new EditorGUI.DisabledScope(!hasExtractedSource || document.bodyFrames.Count == 0))
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
            outputAssetFolder = EditorGUILayout.TextField("输出 Assets 目录", outputAssetFolder);
            if (GUILayout.Button("选择输出目录"))
            {
                SelectOutputFolder();
            }

            using (new EditorGUI.DisabledScope(
                !hasExtractedSource || CountSelectedFrames() == 0))
            {
                if (GUILayout.Button("保存选中帧并导出身体序列帧"))
                {
                    ExportBodyFrames();
                }
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                "身体序列帧负责完整角色画面。后续武器只需按相同帧号叠加，不再依赖骨骼。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawWeaponPanel()
        {
            EditorGUILayout.LabelField("武器序列帧", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "武器图片按帧号排序，并与身体序列帧一一对应。\n"
                + "建议使用相同画布尺寸和相同 FPS。",
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("选择武器序列帧目录"))
            {
                string folder = EditorUtility.OpenFolderPanel(
                    "选择武器序列帧目录",
                    Application.dataPath,
                    string.Empty);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    weaponFrameFolder = folder;
                    status = "已选择武器序列帧目录。";
                    LoadWeaponPreview(0);
                }
            }

            EditorGUILayout.LabelField(
                string.IsNullOrWhiteSpace(weaponFrameFolder)
                    ? "未选择"
                    : weaponFrameFolder,
                EditorStyles.wordWrappedMiniLabel);
            outputAssetFolder = EditorGUILayout.TextField("输出 Assets 目录", outputAssetFolder);
            if (GUILayout.Button("选择输出目录"))
            {
                SelectOutputFolder();
            }

            using (new EditorGUI.DisabledScope(
                string.IsNullOrWhiteSpace(weaponFrameFolder)
                || !hasExtractedSource
                || CountSelectedFrames() == 0))
            {
            if (GUILayout.Button("保存武器序列帧并更新动作清单"))
                {
                    ExportWeaponFrames();
                }
            }
        }

        private void DrawPreviewControlPanel()
        {
            EditorGUILayout.LabelField("组合预览", EditorStyles.boldLabel);
            document.loop = EditorGUILayout.Toggle("循环播放", document.loop);
            document.frameRate = EditorGUILayout.FloatField("播放 FPS", document.frameRate);
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

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "组合顺序：身体 → 武器。后续如果加入披风/特效，可以继续增加图层。",
                EditorStyles.wordWrappedMiniLabel);
            for (int i = 0; i < document.layers.Count; i++)
            {
                SequenceFrameLayerData layer = document.layers[i];
                if (layer == null)
                {
                    continue;
                }

                layer.enabled = EditorGUILayout.ToggleLeft(
                    layer.displayName + "（排序 " + layer.sortingOrder + "）",
                    layer.enabled);
            }
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
            if (workflowTab == WorkflowTab.Body)
            {
                DrawTextureFit(previewTexture, previewRect);
            }
            else if (workflowTab == WorkflowTab.Weapon)
            {
                DrawTextureFit(
                    previewWeaponTexture == null ? previewTexture : previewWeaponTexture,
                    previewRect);
            }
            else
            {
                DrawTextureFit(previewTexture, previewRect);
                if (previewWeaponTexture != null)
                {
                    DrawTextureFit(previewWeaponTexture, previewRect);
                }
            }

            DrawFrameList();
            EditorGUILayout.EndVertical();
        }

        private void DrawFrameList()
        {
            EditorGUILayout.LabelField(
                "身体帧（已选 " + CountSelectedFrames() + "/" + document.bodyFrames.Count + "）",
                EditorStyles.boldLabel);
            frameScroll = EditorGUILayout.BeginScrollView(
                frameScroll,
                GUILayout.Height(170f));
            for (int i = 0; i < document.bodyFrames.Count; i++)
            {
                SequenceFrameData frame = document.bodyFrames[i];
                if (frame == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                bool selected = EditorGUILayout.Toggle(frame.selected, GUILayout.Width(20f));
                if (selected != frame.selected)
                {
                    frame.selected = selected;
                }

                if (GUILayout.Button(
                        "源帧 " + frame.sourceFrameIndex + "  差异 "
                        + frame.differenceScore.ToString("0.000"),
                        EditorStyles.miniButton))
                {
                    selectedFrameListIndex = i;
                    LoadPreviewFrame(i);
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void SelectBodySource()
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
            document.bodyFrames.Clear();
            DestroyTexture(ref previewTexture);
            DestroyTexture(ref previewWeaponTexture);
            status = "已选择动作文件，请点击“拆帧”。";
            Repaint();
        }

        private void ExtractSelectedBodySource()
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
                document.bodyFrames = new List<SequenceFrameData>
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
                status = "已载入一张身体序列帧。";
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

            document.bodyFrames = LoadFrameFiles(extractedFrameFolder);
            document.frameRate = extractFrameRate;
            hasExtractedSource = document.bodyFrames.Count > 0;
            selectedFrameListIndex = document.bodyFrames.Count > 0 ? 0 : -1;
            if (hasExtractedSource)
            {
                LoadPreviewFrame(0);
                status = "已拆出 " + document.bodyFrames.Count
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
            if (document.bodyFrames.Count == 0)
            {
                return;
            }

            for (int i = 0; i < document.bodyFrames.Count; i++)
            {
                document.bodyFrames[i].selected = i == 0;
            }

            int lastSelected = 0;
            for (int i = 1; i < document.bodyFrames.Count; i++)
            {
                SequenceFrameData frame = document.bodyFrames[i];
                float difference = ComputeFrameDifference(
                    document.bodyFrames[lastSelected].sourceFilePath,
                    frame.sourceFilePath);
                frame.differenceScore = difference;
                if (difference >= autoSelectThreshold
                    && i - lastSelected >= autoSelectMinGap)
                {
                    frame.selected = true;
                    lastSelected = i;
                }
            }

            status = "自动选帧完成，已选 " + CountSelectedFrames() + " 帧。";
            Repaint();
        }

        private void SetAllFrameSelection(bool selected)
        {
            for (int i = 0; i < document.bodyFrames.Count; i++)
            {
                if (document.bodyFrames[i] != null)
                {
                    document.bodyFrames[i].selected = selected;
                }
            }
        }

        private void ExportBodyFrames()
        {
            if (!TryGetProjectAssetFolder(outputAssetFolder, out string outputFolder))
            {
                EditorUtility.DisplayDialog("导出序列帧", "输出目录必须位于当前项目 Assets 下。", "确定");
                return;
            }

            string animationFolder = outputFolder.TrimEnd('/') + "/" + SafeName(document.animationId);
            Directory.CreateDirectory(ToAbsolutePath(animationFolder));
            List<SequenceFrameData> selected = GetSelectedFrames();
            for (int i = 0; i < selected.Count; i++)
            {
                string assetPath = animationFolder + "/body_" + i.ToString("D4") + ".png";
                File.Copy(selected[i].sourceFilePath, ToAbsolutePath(assetPath), true);
                selected[i].exportedAssetPath = assetPath;
            }

            document.bodyFrames = selected;
            document.canvasWidth = GetImageWidth(selected[0].sourceFilePath);
            document.canvasHeight = GetImageHeight(selected[0].sourceFilePath);
            AssetDatabase.Refresh();
            SaveDocumentToPath(animationFolder + "/" + SafeName(document.animationId) + ".sequence.json");
            status = "已导出身体序列帧：" + animationFolder;
        }

        private void ExportWeaponFrames()
        {
            if (!TryGetProjectAssetFolder(outputAssetFolder, out string outputFolder))
            {
                EditorUtility.DisplayDialog("导出序列帧", "输出目录必须位于当前项目 Assets 下。", "确定");
                return;
            }

            List<string> weaponFrames = GetImageFiles(weaponFrameFolder);
            List<SequenceFrameData> selected = GetSelectedFrames();
            if (weaponFrames.Count < selected.Count)
            {
                EditorUtility.DisplayDialog(
                    "武器序列帧数量不足",
                    "身体已选 " + selected.Count + " 帧，但武器只有 " + weaponFrames.Count + " 帧。",
                    "确定");
                return;
            }

            string animationFolder = outputFolder.TrimEnd('/') + "/" + SafeName(document.animationId);
            Directory.CreateDirectory(ToAbsolutePath(animationFolder));
            SequenceFrameLayerData weaponLayer = FindOrCreateWeaponLayer();
            weaponLayer.frameAssetPaths.Clear();
            for (int i = 0; i < selected.Count; i++)
            {
                string assetPath = animationFolder + "/weapon_" + i.ToString("D4") + ".png";
                File.Copy(weaponFrames[i], ToAbsolutePath(assetPath), true);
                weaponLayer.frameAssetPaths.Add(assetPath);
            }

            AssetDatabase.Refresh();
            SaveDocumentToPath(animationFolder + "/" + SafeName(document.animationId) + ".sequence.json");
            status = "已导出武器序列帧并更新动作清单。";
        }

        private SequenceFrameLayerData FindOrCreateWeaponLayer()
        {
            for (int i = 0; i < document.layers.Count; i++)
            {
                if (document.layers[i] != null && document.layers[i].layerId == "weapon")
                {
                    return document.layers[i];
                }
            }

            SequenceFrameLayerData layer = new SequenceFrameLayerData();
            document.layers.Add(layer);
            return layer;
        }

        private List<SequenceFrameData> GetSelectedFrames()
        {
            List<SequenceFrameData> result = new List<SequenceFrameData>();
            for (int i = 0; i < document.bodyFrames.Count; i++)
            {
                if (document.bodyFrames[i] != null && document.bodyFrames[i].selected)
                {
                    result.Add(document.bodyFrames[i]);
                }
            }

            return result;
        }

        private void LoadPreviewFrame(int index)
        {
            if (document.bodyFrames.Count == 0)
            {
                DestroyTexture(ref previewTexture);
                return;
            }

            index = Mathf.Clamp(index, 0, document.bodyFrames.Count - 1);
            selectedFrameListIndex = index;
            DestroyTexture(ref previewTexture);
            previewTexture = LoadTexture(document.bodyFrames[index].sourceFilePath);
            LoadWeaponPreview(index);
            Repaint();
        }

        private void LoadWeaponPreview(int index)
        {
            DestroyTexture(ref previewWeaponTexture);
            SequenceFrameLayerData layer = FindWeaponLayer();
            if (layer != null && layer.enabled && index >= 0
                && index < layer.frameAssetPaths.Count)
            {
                string path = layer.frameAssetPaths[index];
                if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    path = ToAbsolutePath(path);
                }

                previewWeaponTexture = LoadTexture(path);
                return;
            }

            List<string> sourceWeaponFrames = GetImageFiles(weaponFrameFolder);
            if (index < 0 || index >= sourceWeaponFrames.Count)
            {
                return;
            }

            previewWeaponTexture = LoadTexture(sourceWeaponFrames[index]);
        }

        private SequenceFrameLayerData FindWeaponLayer()
        {
            for (int i = 0; i < document.layers.Count; i++)
            {
                if (document.layers[i] != null && document.layers[i].layerId == "weapon")
                {
                    return document.layers[i];
                }
            }

            return null;
        }

        private void UpdatePlayback()
        {
            if (!isPlaying || document == null || document.bodyFrames.Count == 0)
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
            if (playbackFrame >= document.bodyFrames.Count)
            {
                if (document.loop)
                {
                    playbackFrame = 0;
                }
                else
                {
                    playbackFrame = document.bodyFrames.Count - 1;
                    isPlaying = false;
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

            document = JsonUtility.FromJson<SequenceFrameAnimationDocument>(File.ReadAllText(path));
            EnsureDocument();
            hasExtractedSource = document.bodyFrames.Count > 0;
            selectedFrameListIndex = document.bodyFrames.Count > 0 ? 0 : -1;
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

        private static List<string> GetImageFiles(string folder)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return result;
            }

            foreach (string extension in new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" })
            {
                result.AddRange(Directory.GetFiles(folder, extension));
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
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
            for (int i = 0; i < document.bodyFrames.Count; i++)
            {
                if (document.bodyFrames[i] != null && document.bodyFrames[i].selected)
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

            if (document.bodyFrames == null)
            {
                document.bodyFrames = new List<SequenceFrameData>();
            }

            if (document.layers == null)
            {
                document.layers = new List<SequenceFrameLayerData>();
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
