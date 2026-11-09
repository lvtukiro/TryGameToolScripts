#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.SequenceFrameAnimation;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    public sealed class BattleActionTimelineEditorWindow : EditorWindow
    {
        private enum DragKind
        {
            None,
            Playhead,
            StartupEnd,
            SwitchStart,
            RecoveryStart,
            Duration,
            Step,
            Keyframe,
            MovementKeyframe,
            PreviewShapeGizmo,
        }

        private enum ShapeGizmoTool { Move, Size, Scale, Rotate }

        private sealed class PreviewShape
        {
            public BattleActionTimelineTableData Table;
            public BattleActionTimelineRecordData Shape;
            public BattleActionTimelineTransform Transform;
            public BattleAttackShapeType Type;
            public Vector2 Offset, BaseSize, Center, Size;
            public float LocalRotation, ScreenRotation;
            public bool Horizontal;
        }

        [Serializable]
        private sealed class AuthoringState : ScriptableObject
        {
            public BattleActionTimelineDocument document =
                new BattleActionTimelineDocument();
            public string baselineSignature = string.Empty;
            public string sourceWorkbookPath = string.Empty;
            public string sourceWorkbookHash = string.Empty;
            public string selectedActionSheet = string.Empty;
            public int selectedActionRowId;
            public string selectedStepSheet = string.Empty;
            public int selectedStepRowId;
            public string selectedSharedSheet = string.Empty;
            public int selectedSharedRowId;
            public float frameRate = 30f;
            public bool snapToFrames;
            public int facing = (int)BattleActionTimelineFacing.Right;
            public SequenceFrameClip sequencePreviewClip;
            public int sequencePreviewActionId;
            public int explicitSingleSkillId;
            public int filterCharacterId;
            public int filterActionType;
            public int filterSkillId;
            public bool filterApplied;
            public float previewHeight = 1.18f;
            public float previewZoom = 1f;
        }

        [Serializable]
        private sealed class DraftEnvelope
        {
            public string documentJson;
            public string baselineSignature;
            public string selectedActionSheet;
            public int selectedActionRowId;
            public string selectedStepSheet;
            public int selectedStepRowId;
            public string selectedSharedSheet;
            public int selectedSharedRowId;
            public float frameRate;
            public bool snapToFrames;
            public int facing;
            public int sequencePreviewActionId;
            public int explicitSingleSkillId;
            public double playheadSeconds;
            public string workbookPath;
            public string workbookHash;
        }

        private sealed class ResolvedStep
        {
            public BattleActionTimelineTableData StepTable;
            public BattleActionTimelineRecordData Step;
            public double TriggerTime;
            // 0=Normal, 1=Impact, 2=Defense, 3=Charged；颜色只用于编辑器预览。
            public int InteractionTypeIndex;
            public bool IsMelee;
            public bool IsProjectile;
            public BattleActionTimelineTableData OwnerTable;
            public BattleActionTimelineRecordData Owner;
            public BattleActionTimelineTableData ProjectileTable;
            public BattleActionTimelineRecordData Projectile;
            public BattleActionTimelineTableData BodyTable;
            public BattleActionTimelineRecordData Body;
            public BattleActionTimelineTableData ShapeTable;
            public BattleActionTimelineRecordData SelectedShape;
            public BattleActionTimelineTableData KeyframeTable;
            public List<BattleActionTimelineRecordData> Keyframes =
                new List<BattleActionTimelineRecordData>();
            public double Lifetime;
            public Vector2 SpawnOffset;
        }

        private const float DefaultInspectorWidth = 430f;
        private const float MinimumInspectorWidth = 300f;
        private const float MinimumTimelineWidth = 420f;
        private const float SplitterWidth = 6f;
        private const float DefaultTimelineHeight = 260f;
        private const float MinimumTimelineHeight = 150f;
        private const float MinimumPreviewHeight = 220f;
        private const float RulerHeight = 24f;
        private const float LaneHeight = 42f;
        private const int InspectorSplitterControlHint = 0x42A18;
        private const int TimelineSplitterControlHint = 0x42A19;
        private const string DraftKeyPrefix =
            "TryGame.BattleActionTimeline.Draft.";

        private AuthoringState authoringState;
        private BattleActionTimelineWorkbookSnapshot workbookSnapshot;
        private readonly List<string> validationIssues = new List<string>();
        private Vector2 inspectorScroll;
        private Vector2 issueScroll;
        private string status = "尚未读取正式动作源表。";
        private bool draftRestored;
        private bool draftSaveQueued;
        private bool playing;
        private double playheadSeconds;
        private double previousEditorTime;
        private DragKind dragKind;
        private string dragSheet = string.Empty;
        private int dragRowId;
        private int dragMovementKeyframeIndex = -1;
        private bool dragChanged;
        private SequenceFrameClip[] availablePreviewClips;
        private int automaticPreviewActionId = int.MinValue;
        private SequenceFrameClip automaticPreviewClip;
        private string automaticPreviewError = string.Empty;
        private string actionSelectionStatus = string.Empty;
        private string attackAuthoringStatus = string.Empty;
        [SerializeField] private int newAttackTypeIndex;
        [SerializeField] private int newProjectileMovementIndex;
        private bool attackAuthoringError;
        private BattleActionTimelineDocument filterSourceDocument;
        private BattleActionTimelineFilterCatalog filterCatalog;
        private RefDataLanguageProvider filterLanguage;
        private bool filterLanguageLoaded;
        private int filterCatalogCharacterId;
        private int filterCatalogSkillId;
        private string filterStatus = string.Empty;
        // 折叠仅属于窗口视图，不写入配置，也不参与草稿脏标记和 Undo。
        private readonly Dictionary<string, bool> hierarchyExpanded =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        [SerializeField] private ShapeGizmoTool shapeGizmoTool;
        [SerializeField] private bool previewEditKeyframes;
        // 仅保存窗口视图偏好；默认关闭，不写入技能配置或源表。
        [SerializeField] private bool showInactiveShapes;
        // 检视栏宽度是窗口布局偏好，不写入技能配置或源表。
        [SerializeField] private float inspectorWidth = DefaultInspectorWidth;
        // 时间轴高度是窗口布局偏好，不写入技能配置或源表。
        [SerializeField] private float timelineHeight = DefaultTimelineHeight;
        private bool inspectorSplitterDragging;
        private bool timelineSplitterDragging;
        private Vector2 gizmoStartMouse, gizmoStartOffset, gizmoHandle, gizmoPivot;
        private float gizmoStartRotation;
        private PreviewShape gizmoStartShape;
        private int gizmoHotControl;
        private const int ShapeGizmoControlHint = 0x42A17;

        private BattleActionTimelineDocument Document
        {
            get
            {
                EnsureState();
                return authoringState.document;
            }
            set
            {
                EnsureState();
                authoringState.document = value ??
                    new BattleActionTimelineDocument();
                authoringState.document.EnsureLists();
            }
        }
        /// <summary>
        /// 指示当前 `ResolvedStep` 实例是否处于“存在未保存修改”状态。
        /// </summary>

        private bool IsDirty => !string.Equals(
            Document.CanonicalSignature(),
            authoringState.baselineSignature ?? string.Empty,
            StringComparison.Ordinal);
        /// <summary>
        /// 获取当前 `ResolvedStep` 实例的草稿键。
        /// </summary>

        private string DraftKey => DraftKeyPrefix +
            Hash128.Compute(Application.dataPath).ToString();
        /// <summary>
        /// 打开当前 `ResolvedStep` 实例。
        /// </summary>

        [MenuItem("TryGame/战斗/动作时间轴编辑器", false, 432)]
        private static void Open()
        {
            BattleActionTimelineEditorWindow window =
                GetWindow<BattleActionTimelineEditorWindow>();
            window.titleContent = new GUIContent("动作时间轴编辑器");
            window.minSize = new Vector2(1080f, 680f);
            window.Show();
        }
        /// <summary>
        /// 组件启用时注册监听并刷新当前状态。
        /// </summary>

        private void OnEnable()
        {
            EnsureState();
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= SaveDraftNow;
            AssemblyReloadEvents.beforeAssemblyReload += SaveDraftNow;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.projectChanged -= OnPreviewAssetsChanged;
            EditorApplication.projectChanged += OnPreviewAssetsChanged;
            previousEditorTime = EditorApplication.timeSinceStartup;
            TryRestoreDraft();
            if (Document.tables.Count == 0)
            {
                LoadOfficial(false, false);
            }
            else if (workbookSnapshot == null || !workbookSnapshot.HasActionStructure)
            {
                RestoreWorkbookBinding();
            }

            ValidateDocument();
        }
        /// <summary>
        /// 组件停用时注销监听并释放临时状态。
        /// </summary>

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= SaveDraftNow;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.projectChanged -= OnPreviewAssetsChanged;
            playing = false;
            SaveDraftNow();
        }
        /// <summary>
        /// 组件销毁时释放订阅、运行时对象和临时资源。
        /// </summary>

        private void OnDestroy()
        {
            SaveDraftNow();
            if (authoringState != null)
            {
                Undo.ClearUndo(authoringState);
                DestroyImmediate(authoringState);
                authoringState = null;
            }
        }
        /// <summary>
        /// 确保状态。
        /// </summary>

        private void EnsureState()
        {
            if (authoringState == null)
            {
                authoringState = CreateInstance<AuthoringState>();
                authoringState.hideFlags = HideFlags.HideAndDontSave;
            }

            authoringState.document ??= new BattleActionTimelineDocument();
            authoringState.document.EnsureLists();
            if (authoringState.frameRate <= 0f ||
                float.IsNaN(authoringState.frameRate) ||
                float.IsInfinity(authoringState.frameRate))
            {
                authoringState.frameRate = 30f;
            }

            if (authoringState.facing != (int)BattleActionTimelineFacing.Left)
            {
                authoringState.facing = (int)BattleActionTimelineFacing.Right;
            }

            if (!BattleActionTimelineTime.IsFinite(authoringState.previewHeight) ||
                authoringState.previewHeight <= 0f)
            {
                authoringState.previewHeight = 1.18f;
            }

            if (!BattleActionTimelineTime.IsFinite(authoringState.previewZoom))
            {
                authoringState.previewZoom = 1f;
            }
            authoringState.previewZoom = Mathf.Clamp(authoringState.previewZoom, 0.5f, 3f);

            float maximumInspectorWidth = Mathf.Max(
                MinimumInspectorWidth,
                position.width - MinimumTimelineWidth - SplitterWidth);
            if (!BattleActionTimelineTime.IsFinite(inspectorWidth) ||
                inspectorWidth <= 0f)
            {
                inspectorWidth = DefaultInspectorWidth;
            }
            inspectorWidth = Mathf.Clamp(
                inspectorWidth,
                MinimumInspectorWidth,
                maximumInspectorWidth);
            if (!BattleActionTimelineTime.IsFinite(timelineHeight) ||
                timelineHeight <= 0f)
            {
                timelineHeight = DefaultTimelineHeight;
            }
        }
        /// <summary>
        /// 响应撤销重做事件，并同步当前组件状态。
        /// </summary>

        private void OnUndoRedo()
        {
            EnsureState();
            filterCatalog = null;
            playing = false;
            CancelDrag();
            NormalizeSelection();
            ClampPlayhead();
            ValidateDocument();
            QueueDraftSave();
            Repaint();
        }
        /// <summary>
        /// 将撤销或重做请求放入编辑器延迟队列，待当前界面事件结束后执行。
        /// </summary>

        private void QueueUndoRedo(bool redo)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null || authoringState == null)
                {
                    return;
                }

                if (redo)
                {
                    Undo.PerformRedo();
                }
                else
                {
                    Undo.PerformUndo();
                }
            };
        }
        /// <summary>
        /// 响应编辑器更新事件，并同步当前组件状态。
        /// </summary>

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            double delta = Math.Max(0d, Math.Min(0.25d, now - previousEditorTime));
            previousEditorTime = now;
            if (!playing)
            {
                return;
            }

            double duration = CurrentPreviewDuration();
            if (duration <= 0d)
            {
                playing = false;
                return;
            }

            playheadSeconds += delta;
            if (playheadSeconds >= duration)
            {
                playheadSeconds = duration;
                playing = false;
            }

            Repaint();
        }
        /// <summary>
        /// 绘制编辑器窗口内容并处理当前交互。
        /// </summary>

        private void OnGUI()
        {
            EnsureState();
            NormalizeSelection();
            DrawToolbar();

            Rect body = new Rect(0f, 24f, position.width, position.height - 24f);
            GUILayout.BeginArea(new Rect(body.x, body.y, inspectorWidth, body.height));
            DrawInspectorArea();
            GUILayout.EndArea();

            Rect content = new Rect(
                inspectorWidth + SplitterWidth,
                body.y + 6f,
                Mathf.Max(200f, body.width - inspectorWidth - SplitterWidth - 6f),
                Mathf.Max(200f, body.height - 12f));
            DrawTimelineAndPreview(content);

            Rect splitter = new Rect(
                inspectorWidth,
                body.y,
                SplitterWidth,
                body.height);
            DrawInspectorSplitter(splitter);
        }

        private void DrawInspectorSplitter(Rect splitter)
        {
            EditorGUIUtility.AddCursorRect(splitter, MouseCursor.ResizeHorizontal);
            EditorGUI.DrawRect(
                splitter,
                inspectorSplitterDragging
                    ? new Color(0.28f, 0.58f, 0.95f, 0.95f)
                    : new Color(0.20f, 0.23f, 0.27f, 1f));

            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                splitter.Contains(current.mousePosition))
            {
                inspectorSplitterDragging = true;
                GUIUtility.hotControl = InspectorSplitterControlHint;
                current.Use();
                return;
            }

            if (GUIUtility.hotControl != InspectorSplitterControlHint)
            {
                return;
            }

            if (current.type == EventType.MouseDrag)
            {
                SetInspectorWidth(current.mousePosition.x);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp ||
                current.type == EventType.Ignore)
            {
                inspectorSplitterDragging = false;
                GUIUtility.hotControl = 0;
                current.Use();
                Repaint();
            }
        }

        private void SetInspectorWidth(float value)
        {
            float maximumInspectorWidth = Mathf.Max(
                MinimumInspectorWidth,
                position.width - MinimumTimelineWidth - SplitterWidth);
            inspectorWidth = Mathf.Clamp(
                value,
                MinimumInspectorWidth,
                maximumInspectorWidth);
        }
        /// <summary>
        /// 绘制工具栏。
        /// </summary>

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(
                "重新读取正式配置",
                EditorStyles.toolbarButton,
                GUILayout.Width(116f)))
            {
                LoadOfficial(true, true);
            }

            using (new EditorGUI.DisabledScope(
                workbookSnapshot == null || !workbookSnapshot.HasActionStructure ||
                !IsDirty))
            {
                if (GUILayout.Button(
                    "安全写回源 xlsx",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(112f)))
                {
                    WriteOfficial();
                }
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                QueueUndoRedo(false);
            }

            if (GUILayout.Button("重做", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                QueueUndoRedo(true);
            }

            GUILayout.Space(8f);
            if (GUILayout.Button(
                playing ? "暂停" : "播放",
                EditorStyles.toolbarButton,
                GUILayout.Width(48f)))
            {
                playing = !playing;
                previousEditorTime = EditorApplication.timeSinceStartup;
                if (playheadSeconds >= CurrentPreviewDuration() -
                    BattleActionTimelineTime.Epsilon)
                {
                    playheadSeconds = 0d;
                }
            }

            if (GUILayout.Button("◀帧", EditorStyles.toolbarButton, GUILayout.Width(42f)))
            {
                StepFrame(-1);
            }

            if (GUILayout.Button("帧▶", EditorStyles.toolbarButton, GUILayout.Width(42f)))
            {
                StepFrame(1);
            }

            GUILayout.Space(6f);
            GUILayout.Label(
                "帧 " + (playheadSeconds * authoringState.frameRate).ToString("0.###", CultureInfo.InvariantCulture) +
                " / " + playheadSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s",
                EditorStyles.miniLabel,
                GUILayout.Width(142f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                IsDirty ? "● 草稿未写回" : "✓ 与读取基线一致",
                EditorStyles.miniLabel,
                GUILayout.Width(104f));
            EditorGUILayout.EndHorizontal();
        }
        /// <summary>
        /// 绘制检视面板区域。
        /// </summary>

        private void DrawInspectorArea()
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            EditorGUILayout.HelpBox(status, MessageType.None);
            DrawSelectionAndViewSettings();
            if (!Document.HasActionStructure)
            {
                DrawWaitingForStructure();
                DrawIssues();
                EditorGUILayout.EndScrollView();
                return;
            }

            if (workbookSnapshot == null || !workbookSnapshot.HasActionStructure)
            {
                EditorGUILayout.HelpBox(
                    "当前配置和组合形状仍可编辑、预览并保留草稿；源表基线未绑定，安全写回已禁用。\n" + status,
                    MessageType.Warning);
            }

            BattleActionTimelineDocument edited = Document.Clone();
            EditorGUI.BeginChangeCheck();
            DrawActionFields(edited);
            DrawExecutionSteps(edited);
            bool changed = EditorGUI.EndChangeCheck();
            if (changed)
            {
                Undo.RegisterCompleteObjectUndo(
                    authoringState,
                    "编辑动作时间轴");
                Document = edited;
                NormalizeStepOrderForSelectedAction();
                NormalizeKeyframeOrder();
                OnAuthoringChanged();
            }

            DrawIssues();
            EditorGUILayout.EndScrollView();
        }
        /// <summary>
        /// 绘制等待用于结构。
        /// </summary>

        private void DrawWaitingForStructure()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "等待源表结构：机器人工作簿当前没有可识别的 ActiveSingle / " +
                "ExecutionStep / AttackBody / MeleeSpawn / Projectile 工作表。" +
                "窗口保持可用，表结构落地后点击“重新读取正式配置”。",
                MessageType.Warning);
            if (workbookSnapshot != null && workbookSnapshot.AllSheetNames.Length > 0)
            {
                EditorGUILayout.LabelField("当前工作簿工作表", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(
                    string.Join("、", workbookSnapshot.AllSheetNames),
                    EditorStyles.wordWrappedLabel,
                    GUILayout.MinHeight(42f));
            }

            if (GUILayout.Button("复制建议示例表头"))
            {
                EditorGUIUtility.systemCopyBuffer =
                    BattleActionTimelineSchema.ExampleStructure;
                ShowNotification(new GUIContent("示例表头已复制；工具不会自行修改正式 xlsx"));
            }

            EditorGUILayout.HelpBox(
                "示例按钮只复制文本，不创建 Sheet、不导表、不写 Generated/Output。",
                MessageType.Info);
        }
        /// <summary>
        /// 绘制选择并视图设置。
        /// </summary>

        private void DrawSelectionAndViewSettings()
        {
            DrawSequencePreviewSettings();
            if (!string.IsNullOrEmpty(actionSelectionStatus))
            {
                EditorGUILayout.HelpBox(actionSelectionStatus, MessageType.Info);
            }
            if (authoringState.sequencePreviewActionId > 0)
            {
                int explicitId = EditorGUILayout.DelayedIntField(
                    new GUIContent("指定单段技能 ID（可选）", "多个单段引用同一动作时填写；0 表示仅在唯一匹配时定位。不是动画动作 ID。"),
                    authoringState.explicitSingleSkillId);
                if (explicitId != authoringState.explicitSingleSkillId)
                {
                    Undo.RecordObject(authoringState, "指定动作对应单段");
                    authoringState.explicitSingleSkillId = Mathf.Max(0, explicitId);
                    authoringState.filterApplied = false;
                    NormalizeSelection();
                    QueueDraftSave();
                    Repaint();
                }
            }
            if (TryGetSelectedAction(Document, out BattleActionTimelineTableData table,
                out BattleActionTimelineRecordData action))
            {
                EditorGUILayout.LabelField("当前编辑单段", table.sheetName + " / " +
                    BattleActionTimelineSchema.DisplayName(table, action), EditorStyles.wordWrappedLabel);
            }
            bool snapToFrames = EditorGUILayout.Toggle(
                new GUIContent("吸附到整帧", "关闭后，拖动和时间输入可精确到小数秒；开启后按下方 FPS 吸附。切换本身不修改已有配置或播放头，素材仍逐帧显示。"),
                authoringState.snapToFrames);
            if (snapToFrames != authoringState.snapToFrames)
            {
                Undo.RecordObject(authoringState, "切换时间轴帧吸附");
                authoringState.snapToFrames = snapToFrames;
                QueueDraftSave();
            }

            EditorGUILayout.BeginHorizontal();
            float frameRate = EditorGUILayout.FloatField(
                new GUIContent("步进／吸附 FPS", "控制逐帧按钮的步长；开启“吸附到整帧”后也控制编辑吸附精度，不改变素材播放速度。"),
                authoringState.frameRate);
            if (!Mathf.Approximately(frameRate, authoringState.frameRate) &&
                frameRate > 0f && !float.IsNaN(frameRate) &&
                !float.IsInfinity(frameRate))
            {
                Undo.RecordObject(authoringState, "修改动作时间轴 FPS");
                authoringState.frameRate = frameRate;
                // 修改编辑精度不重新吸附当前时间或已有配置。
                QueueDraftSave();
            }

            string facingLabel = authoringState.facing ==
                (int)BattleActionTimelineFacing.Left ? "朝左镜像" : "朝右配置";
            if (GUILayout.Button(facingLabel, GUILayout.Width(82f)))
            {
                Undo.RecordObject(authoringState, "切换动作镜像预览");
                authoringState.facing = authoringState.facing ==
                    (int)BattleActionTimelineFacing.Left
                    ? (int)BattleActionTimelineFacing.Right
                    : (int)BattleActionTimelineFacing.Left;
                QueueDraftSave();
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
            double previewTime = EditorGUILayout.DelayedDoubleField(
                new GUIContent("预览时间（秒）", "可直接输入例如 0.185；关闭帧吸附时保留小数秒，不改变技能或素材时长。"),
                playheadSeconds);
            if (BattleActionTimelineTime.IsFinite(previewTime) && !NearlyEqual(previewTime, playheadSeconds))
            {
                playing = false;
                playheadSeconds = SnapAndClamp(previewTime, CurrentPreviewDuration());
                QueueDraftSave();
                Repaint();
            }
        }
        /// <summary>
        /// 直接预览生成工具的 SequenceFrameClip，不采样或改动场景对象。
        /// </summary>

        private void DrawSequencePreviewSettings()
        {
            DrawAnimationFilters();
            int actionId = EditorGUILayout.DelayedIntField(
                new GUIContent("动画资源 ID（可直接填）", "填写序列帧资源 ID，例如 22；也可在上方独立一行直接填写技能 ID。最后操作的入口生效。"),
                authoringState.sequencePreviewActionId);
            if (actionId != authoringState.sequencePreviewActionId)
            {
                SetPreviewInput(Mathf.Max(0, actionId), null);
            }

            SequenceFrameClip currentClip = ResolveSequencePreviewClip(out string lookupError);
            SequenceFrameClip clip = (SequenceFrameClip)EditorGUILayout.ObjectField(
                "序列帧动画（仅预览）",
                currentClip,
                typeof(SequenceFrameClip),
                false);
            if (clip != currentClip)
            {
                SetPreviewInput(clip != null ? clip.ActionId : 0, clip);
                lookupError = string.Empty;
            }

            if (GUILayout.Button("按填写的动作 ID 重新加载"))
            {
                Undo.RecordObject(authoringState, "按动作 ID 加载序列帧");
                authoringState.sequencePreviewClip = null;
                OnPreviewAssetsChanged();
                clip = ResolveSequencePreviewClip(out lookupError);
                playing = false;
                playheadSeconds = 0d;
            }

            if (!string.IsNullOrEmpty(lookupError))
            {
                EditorGUILayout.HelpBox(lookupError, MessageType.Warning);
            }
            else if (clip != null)
            {
                if (!TrySampleSequencePreview(clip, out _, out int index, out string error))
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        $"素材帧 {index + 1}/{clip.FrameCount} · {clip.FrameRate:0.###} FPS · " +
                        $"{clip.FrameCount / (double)clip.FrameRate:0.###} 秒 · " +
                        (clip.Loop ? "循环" : "非循环，播完停在末帧"),
                        EditorStyles.wordWrappedMiniLabel);
                }

                if (TryGetSelectedAction(Document, out _, out _) &&
                    clip.ActionId != CurrentEditedActionAnimationId())
                {
                    EditorGUILayout.HelpBox(
                        $"当前仅试播动作 {clip.ActionId}；技能配置的 actionId=" +
                        $"{CurrentEditedActionAnimationId()} 不会因此改变。",
                        MessageType.Warning);
                }

                if (BattleActionTimelineTime.IsFinite(clip.FrameRate) && clip.FrameRate > 0f &&
                    clip.FrameCount / (double)clip.FrameRate > CurrentDuration() + BattleActionTimelineTime.Epsilon)
                {
                    EditorGUILayout.HelpBox(
                        "素材比技能持续时间更长，时间轴会延长供完整试播；游戏仍按技能配置结束。" +
                        "若要延长实际动作，请调整“动作持续时间”。",
                        MessageType.Info);
                }

                if (GUILayout.Button("时间轴吸附 FPS 使用素材 FPS") &&
                    BattleActionTimelineTime.IsFinite(clip.FrameRate) && clip.FrameRate > 0f)
                {
                    Undo.RecordObject(authoringState, "使用素材 FPS 吸附时间轴");
                    authoringState.frameRate = clip.FrameRate;
                    // 只改变之后的吸附精度，不改已有事件、阶段或当前播放时间。
                    QueueDraftSave();
                    Repaint();
                }
            }

            float height = EditorGUILayout.FloatField(
                new GUIContent("预览画布高度（世界单位）", "对应战斗中的角色高度：主角 1.18，敌人/Boss 当前为 1.25。统一以脚底为原点；只影响预览，不改角色或素材。"),
                authoringState.previewHeight);
            EditorGUILayout.LabelField("CombatOrigin", "统一脚底中心");
            if (BattleActionTimelineTime.IsFinite(height) && height > 0f &&
                !Mathf.Approximately(height, authoringState.previewHeight))
            {
                Undo.RecordObject(authoringState, "修改序列帧预览布局");
                authoringState.previewHeight = height;
                Repaint();
            }

            EditorGUILayout.LabelField(
                "可拖入刚生成的 clip_动作ID_1.asset，无须场景对象或先导表。" +
                "也可直接填写动画资源 ID 或技能 ID；唯一匹配时定位技能单段，不修改表内动作绑定。" +
                "素材按自身 FPS 播放；时间轴吸附 FPS 不改变动画速度。预览输入和布局不写表。",
                EditorStyles.wordWrappedMiniLabel);
            authoringState.previewZoom = EditorGUILayout.Slider(
                "预览缩放", authoringState.previewZoom, 0.5f, 3f);
        }

        /// <summary>角色／动作类型辅助筛选与直接技能ID入口；最后操作的入口决定预览和编辑目标。</summary>
        private void DrawAnimationFilters()
        {
            EnsureFilterCatalog();
            if (!filterLanguageLoaded)
            {
                filterLanguageLoaded = true;
                RefDataLanguageProvider.TryCreate(out filterLanguage);
            }

            string CharacterName(BattleActionTimelineCharacterFilter value)
            {
                string name = filterLanguage != null && filterLanguage.TryGet(value.NameLanguageKey, out string text)
                    ? text : value.CodeName;
                return $"{value.Id} · {name}";
            }

            string[] characters = new[] { "— 选择角色 —" }
                .Concat(filterCatalog.Characters.Select(CharacterName)).ToArray();
            int selectedCharacter = filterCatalog.Characters.FindIndex(value => value.Id == authoringState.filterCharacterId) + 1;
            int newCharacter = EditorGUILayout.Popup(
                new GUIContent("角色筛选", "角色 + 动作类型按角色默认/空手映射定位；技能动作另填技能 ID。"),
                selectedCharacter, characters.Select(value => new GUIContent(value)).ToArray());
            bool changed = false;
            if (newCharacter != selectedCharacter)
            {
                Undo.RecordObject(authoringState, "筛选动作所属角色");
                authoringState.filterCharacterId = newCharacter == 0 ? 0 : filterCatalog.Characters[newCharacter - 1].Id;
                changed = true;
            }

            var types = new[] { RefData.EnumBattleCharacterActionState.Invalid }.Concat(
                Enum.GetValues(typeof(RefData.EnumBattleCharacterActionState)).Cast<RefData.EnumBattleCharacterActionState>()
                    .Where(value => value > RefData.EnumBattleCharacterActionState.None)).ToArray();
            int selectedType = Math.Max(0, Array.FindIndex(types, value => (int)value == authoringState.filterActionType));
            int newType = EditorGUILayout.Popup("动作类型", selectedType, types.Select(value =>
                value == RefData.EnumBattleCharacterActionState.Invalid ? "— 选择动作类型 —" :
                BattleActionTimelineFilterCatalog.StateLabel(value.ToString())).ToArray());
            if (newType != selectedType)
            {
                Undo.RecordObject(authoringState, "筛选动作类型");
                authoringState.filterActionType = (int)types[newType];
                changed = true;
            }
            EditorGUILayout.BeginHorizontal();
            int skill = EditorGUILayout.DelayedIntField(
                new GUIContent("技能 ID（可直接填）", "BattleSkill.id，例如 2008；不需要先选角色或“技能”动作类型。多装备映射需要选择角色。与动画资源 ID 两种入口均可使用，最后操作的入口生效。"),
                authoringState.filterSkillId);
            bool loadSkill = GUILayout.Button("加载", GUILayout.Width(46f));
            EditorGUILayout.EndHorizontal();
            if (skill != authoringState.filterSkillId || loadSkill)
            {
                Undo.RecordObject(authoringState, "按技能 ID 加载动作");
                authoringState.filterSkillId = Math.Max(0, skill);
                authoringState.filterActionType = (int)RefData.EnumBattleCharacterActionState.Skill;
                changed = true;
            }
            if (changed) authoringState.filterApplied = true;
            EnsureFilterCatalog();
            if (authoringState.filterApplied)
            {
                if (filterCatalog.TryResolve(authoringState.filterCharacterId, authoringState.filterActionType,
                    authoringState.filterSkillId, out var value, out filterStatus))
                {
                    if (value.ActionId != authoringState.sequencePreviewActionId ||
                        value.SingleSkillId != authoringState.explicitSingleSkillId || authoringState.sequencePreviewClip != null)
                        SetPreviewInput(value.ActionId, null, value.SingleSkillId);
                }
                else if (authoringState.sequencePreviewActionId != 0 || authoringState.sequencePreviewClip != null)
                    SetPreviewInput(0, null);
                authoringState.filterApplied = true;
            }
            if (authoringState.filterApplied && !string.IsNullOrEmpty(filterStatus))
                EditorGUILayout.HelpBox(filterStatus, MessageType.Info);
        }

        private void EnsureFilterCatalog()
        {
            int skillId = authoringState.filterActionType == (int)RefData.EnumBattleCharacterActionState.Skill
                ? authoringState.filterSkillId : 0;
            if (filterCatalog != null && ReferenceEquals(filterSourceDocument, Document) &&
                filterCatalogCharacterId == authoringState.filterCharacterId && filterCatalogSkillId == skillId) return;
            filterSourceDocument = Document;
            filterCatalogCharacterId = authoringState.filterCharacterId;
            filterCatalogSkillId = skillId;
            filterCatalog = BattleActionTimelineFilterCatalog.Build(Document, filterCatalogCharacterId, skillId);
        }
        /// <summary>
        /// 绘制动作字段。
        /// </summary>

        private void DrawActionFields(BattleActionTimelineDocument edited)
        {
            if (!TryGetSelectedAction(
                edited,
                out BattleActionTimelineTableData table,
                out BattleActionTimelineRecordData action))
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "动作阶段 · " + table.sheetName + " / ID=" + action.rowId,
                EditorStyles.boldLabel);
            BattleActionTimelinePhaseTimes phases =
                BattleActionTimelineSchema.ReadPhases(table, action);
            double startup = EditorGUILayout.DoubleField(
                "前摇结束时间",
                phases.StartupEnd);
            double switchStart = EditorGUILayout.DoubleField(
                "共享切换窗开始",
                phases.SwitchWindowStart);
            double recovery = EditorGUILayout.DoubleField(
                "后摇开始时间",
                phases.RecoveryStart);
            double duration = EditorGUILayout.DoubleField(
                "动作持续时间",
                phases.Duration);
            double currentTime = SnapNonNegative(playheadSeconds);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("前摇结束 = 当前时间"))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    currentTime,
                    BattleActionTimelineSchema.StartupEndAliases);
                GUI.changed = true;
            }

            if (GUILayout.Button("切换窗 = 当前时间"))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    currentTime,
                    BattleActionTimelineSchema.SwitchStartAliases);
                GUI.changed = true;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("后摇开始 = 当前时间"))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    currentTime,
                    BattleActionTimelineSchema.RecoveryStartAliases);
                GUI.changed = true;
            }

            if (GUILayout.Button("动作结束 = 当前时间"))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    Math.Max(MinimumActionDuration(), currentTime),
                    BattleActionTimelineSchema.DurationAliases);
                GUI.changed = true;
            }

            EditorGUILayout.EndHorizontal();
            if (!NearlyEqual(startup, phases.StartupEnd))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    SnapNonNegative(startup),
                    BattleActionTimelineSchema.StartupEndAliases);
            }

            if (!NearlyEqual(switchStart, phases.SwitchWindowStart))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    SnapNonNegative(switchStart),
                    BattleActionTimelineSchema.SwitchStartAliases);
            }

            if (!NearlyEqual(recovery, phases.RecoveryStart))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    SnapNonNegative(recovery),
                    BattleActionTimelineSchema.RecoveryStartAliases);
            }

            if (!NearlyEqual(duration, phases.Duration))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    Math.Max(MinimumActionDuration(), SnapNonNegative(duration)),
                    BattleActionTimelineSchema.DurationAliases);
            }

            int attackRangeColumn = BattleActionTimelineSchema.FindColumn(
                table,
                BattleActionTimelineSchema.AttackRangeAliases);
            int movementOffsetXColumn = BattleActionTimelineSchema.FindColumn(
                table,
                BattleActionTimelineSchema.MovementOffsetXAliases);
            int movementOffsetYColumn = BattleActionTimelineSchema.FindColumn(
                table,
                BattleActionTimelineSchema.MovementOffsetYAliases);
            if (attackRangeColumn >= 0)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "技能攻击距离（当前 ActiveSingle，供 AI 站位使用）",
                    EditorStyles.miniBoldLabel);
                double attackRange = BattleActionTimelineSchema.GetDouble(
                    table,
                    action,
                    0d,
                    BattleActionTimelineSchema.AttackRangeAliases);
                double editedAttackRange = EditorGUILayout.DoubleField(
                    new GUIContent("攻击距离", "AI 会移动到这个距离附近再释放技能。"),
                    attackRange);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent(
                        "按首个攻击体 Shape 自动计算",
                        "按当前技能最早的近战/投射物步骤，以及该攻击体 shapeIndex 最小的 Shape，计算水平包围范围。")))
                {
                    if (TryCalculateAttackRangeFromFirstShape(
                            edited,
                            action.rowId,
                            out double calculatedRange,
                            out string calculationDescription,
                            out string calculationError))
                    {
                        BattleActionTimelineSchema.SetDouble(
                            table,
                            action,
                            calculatedRange,
                            BattleActionTimelineSchema.AttackRangeAliases);
                        attackRange = calculatedRange;
                        editedAttackRange = calculatedRange;
                        attackAuthoringStatus =
                            "已按 " + calculationDescription + " 自动填入攻击距离 " +
                            calculatedRange.ToString("0.###", CultureInfo.InvariantCulture) +
                            "。该值仍可手动微调，尚未写回源表。";
                        attackAuthoringError = false;
                        GUI.changed = true;
                        ShowNotification(new GUIContent("已自动填入攻击距离"));
                    }
                    else
                    {
                        attackAuthoringStatus = calculationError;
                        attackAuthoringError = true;
                        ShowNotification(new GUIContent("自动计算攻击距离失败"));
                    }
                }
                EditorGUILayout.EndHorizontal();
                if (!BattleActionTimelineTime.IsFinite(editedAttackRange) ||
                    editedAttackRange < 0d)
                {
                    EditorGUILayout.HelpBox(
                        "技能攻击距离必须是大于等于 0 的有限数值。",
                        MessageType.Error);
                }
                else
                {
                    if (!NearlyEqual(editedAttackRange, attackRange))
                    {
                        BattleActionTimelineSchema.SetDouble(
                            table,
                            action,
                            editedAttackRange,
                            BattleActionTimelineSchema.AttackRangeAliases);
                    }
                }

                if (attackRange <= 0d + BattleActionTimelineTime.Epsilon)
                {
                    EditorGUILayout.HelpBox(
                        "当前为 0，表示该技能按贴身距离处理。",
                        MessageType.Info);
                }

                if (movementOffsetXColumn >= 0)
                {
                    double movementOffsetX = BattleActionTimelineSchema.GetDouble(
                        table,
                        action,
                        0d,
                        BattleActionTimelineSchema.MovementOffsetXAliases);
                    double editedMovementOffsetX = EditorGUILayout.DoubleField(
                        new GUIContent("释放位移 X", "动作持续期间沿角色本地 X 轴累计移动量；负数表示向后。"),
                        movementOffsetX);
                    if (BattleActionTimelineTime.IsFinite(editedMovementOffsetX) &&
                        !NearlyEqual(editedMovementOffsetX, movementOffsetX))
                    {
                        BattleActionTimelineSchema.SetDouble(
                            table,
                            action,
                            editedMovementOffsetX,
                            BattleActionTimelineSchema.MovementOffsetXAliases);
                    }
                }

                if (movementOffsetYColumn >= 0)
                {
                    double movementOffsetY = BattleActionTimelineSchema.GetDouble(
                        table,
                        action,
                        0d,
                        BattleActionTimelineSchema.MovementOffsetYAliases);
                    double editedMovementOffsetY = EditorGUILayout.DoubleField(
                        new GUIContent("释放位移 Y", "动作持续期间沿世界 Y 轴累计移动量；落地类动作应谨慎使用。"),
                        movementOffsetY);
                    if (BattleActionTimelineTime.IsFinite(editedMovementOffsetY) &&
                        !NearlyEqual(editedMovementOffsetY, movementOffsetY))
                    {
                        BattleActionTimelineSchema.SetDouble(
                            table,
                            action,
                            editedMovementOffsetY,
                            BattleActionTimelineSchema.MovementOffsetYAliases);
                    }
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "攻击期间移动",
                EditorStyles.miniBoldLabel);
            int movementModeColumn = BattleActionTimelineSchema.FindColumn(
                table,
                BattleActionTimelineSchema.MovementModeAliases);
            if (movementModeColumn >= 0)
            {
                string rawMovementMode = BattleActionTimelineSchema.Get(
                    table,
                    action,
                    BattleActionTimelineSchema.MovementModeAliases);
                RefData.EnumBattleActionMovementMode parsedMovementMode =
                    Enum.TryParse(
                        rawMovementMode,
                        true,
                        out RefData.EnumBattleActionMovementMode movementMode)
                        ? movementMode
                        : RefData.EnumBattleActionMovementMode.Locked;
                RefData.EnumBattleActionMovementMode editedMovementMode =
                    (RefData.EnumBattleActionMovementMode)EditorGUILayout.EnumPopup(
                        new GUIContent(
                            "移动模式",
                            "Locked：攻击期间不能移动；Horizontal：允许水平移动；Full：允许完整移动。"),
                        parsedMovementMode);
                if (editedMovementMode != parsedMovementMode ||
                    !string.Equals(
                        rawMovementMode,
                        editedMovementMode.ToString(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    BattleActionTimelineSchema.Set(
                        table,
                        action,
                        editedMovementMode.ToString(),
                        BattleActionTimelineSchema.MovementModeAliases);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "当前 BattleActiveSingleSkill 源表缺少 movementMode 列。",
                    MessageType.Warning);
            }

            DrawMovementKeyframes(table, action, currentTime);

            if (attackRangeColumn < 0 || movementOffsetXColumn < 0 || movementOffsetYColumn < 0)
            {
                EditorGUILayout.HelpBox(
                    "当前 BattleActiveSingleSkill 源表还没有 attackRange 列；" +
                    "请在表头追加 attackRange、movementOffsetX、movementOffsetY 后重新读取。",
                    MessageType.Warning);
            }
        }

        private void DrawMovementKeyframes(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData action,
            double currentTime)
        {
            int movementKeyframesColumn = BattleActionTimelineSchema.FindColumn(
                table,
                BattleActionTimelineSchema.MovementKeyframesAliases);
            if (movementKeyframesColumn < 0)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("释放位移关键帧（累计位移）", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "每一行表示动作到达该时间点时的累计 X/Y 位移。时间用秒填写，帧号仅作为按上方 FPS 换算的提示。",
                EditorStyles.wordWrappedMiniLabel);
            double frameRate = authoringState.frameRate;
            if (!BattleActionTimelineTime.IsFinite(frameRate) || frameRate <= 0d)
            {
                frameRate = 30d;
            }
            string source = BattleActionTimelineSchema.Get(
                table,
                action,
                BattleActionTimelineSchema.MovementKeyframesAliases);
            if (!BattleActionTimelineInlineStructCodec.TryParse(
                    source,
                    out List<Dictionary<string, string>> values,
                    out string parseError))
            {
                EditorGUILayout.HelpBox("位移关键帧解析失败：" + parseError, MessageType.Error);
                return;
            }

            bool changed = false;
            for (int index = 0; index < values.Count; index++)
            {
                Dictionary<string, string> value = values[index];
                double localTime = ReadInlineDouble(value, "LocalTime", 0d);
                double offsetX = ReadInlineDouble(value, "OffsetX", 0d);
                double offsetY = ReadInlineDouble(value, "OffsetY", 0d);
                int frame = BattleActionTimelineTime.SecondsToFrame(
                    Math.Max(0d, localTime),
                    frameRate);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    "关键帧 " + (index + 1),
                    EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    "当前时间 " + localTime.ToString("0.###", CultureInfo.InvariantCulture) + "s",
                    EditorStyles.miniLabel);
                bool delete = GUILayout.Button("删除", GUILayout.Width(52f));
                EditorGUILayout.EndHorizontal();

                double editedTime = EditorGUILayout.DoubleField(
                    new GUIContent("时间（秒）", "动作从 0 秒开始计时；可填写小数，例如 0.185。"),
                    localTime);
                EditorGUILayout.LabelField(
                    "换算帧 " + frame.ToString(CultureInfo.InvariantCulture),
                    EditorStyles.miniLabel);
                double editedX = EditorGUILayout.DoubleField(
                    new GUIContent("累计 X", "到达这一时间点时的累计本地水平位移，不是单帧速度。"),
                    offsetX);
                double editedY = EditorGUILayout.DoubleField(
                    new GUIContent("累计 Y", "到达这一时间点时的累计世界垂直位移，不是单帧速度。"),
                    offsetY);
                string rawInterpolation = ReadInline(value, "Interpolation", "Linear");
                RefData.EnumBattleTransformInterpolation parsedInterpolation =
                    Enum.TryParse(
                        rawInterpolation,
                        true,
                        out RefData.EnumBattleTransformInterpolation interpolation)
                        ? interpolation
                        : RefData.EnumBattleTransformInterpolation.Linear;
                RefData.EnumBattleTransformInterpolation editedInterpolation =
                    (RefData.EnumBattleTransformInterpolation)EditorGUILayout.EnumPopup(
                        new GUIContent(
                            "曲线",
                            "控制当前关键帧到下一关键帧之间的位移变化；可选直线、缓入、缓出、缓入缓出或瞬切。"),
                        parsedInterpolation);
                if (delete)
                {
                    values.RemoveAt(index);
                    index--;
                    changed = true;
                }
                else
                {
                    if (!BattleActionTimelineTime.IsFinite(editedTime) || editedTime < 0d)
                    {
                        editedTime = localTime;
                    }

                    if (!NearlyEqual(editedTime, localTime) ||
                        !NearlyEqual(editedX, offsetX) || !NearlyEqual(editedY, offsetY) ||
                        editedInterpolation != parsedInterpolation ||
                        !string.Equals(
                            rawInterpolation,
                            editedInterpolation.ToString(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        value["LocalTime"] = editedTime.ToString(CultureInfo.InvariantCulture);
                        value["OffsetX"] = editedX.ToString(CultureInfo.InvariantCulture);
                        value["OffsetY"] = editedY.ToString(CultureInfo.InvariantCulture);
                        value["Interpolation"] = editedInterpolation.ToString();
                        changed = true;
                    }
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("新增位移关键帧（当前时间）"))
            {
                double newTime = Math.Max(0d, currentTime);
                bool duplicateFrame = values.Any(value =>
                    Math.Abs(ReadInlineDouble(value, "LocalTime", 0d) - newTime) <=
                    BattleActionTimelineTime.Epsilon);
                if (duplicateFrame)
                {
                    EditorGUILayout.HelpBox(
                        "当前帧已经有位移关键帧；请直接修改该行的 X/Y。",
                        MessageType.Info);
                }
                else
                {
                    values.Add(new Dictionary<string, string>
                    {
                        { "LocalTime", newTime.ToString(CultureInfo.InvariantCulture) },
                        { "OffsetX", "0" },
                        { "OffsetY", "0" },
                        { "RotationDegrees", "0" },
                        { "ScaleX", "1" },
                        { "ScaleY", "1" },
                        { "Interpolation", "Linear" },
                    });
                    changed = true;
                }
            }

            if (changed)
            {
                values.Sort((left, right) =>
                {
                    double leftTime = ReadInlineDouble(left, "LocalTime", 0d);
                    double rightTime = ReadInlineDouble(right, "LocalTime", 0d);
                    return leftTime.CompareTo(rightTime);
                });
            }

            if (!changed)
            {
                return;
            }

            List<BattleActionTimelineRecordData> records = values.Select(value =>
                new BattleActionTimelineRecordData
                {
                    cells = new[]
                    {
                        "", "", ReadInline(value, "LocalTime", "0"),
                        ReadInline(value, "OffsetX", "0"),
                        ReadInline(value, "OffsetY", "0"),
                        ReadInline(value, "RotationDegrees", "0"),
                        ReadInline(value, "ScaleX", "1"),
                        ReadInline(value, "ScaleY", "1"),
                        ReadInline(value, "Interpolation", "Linear"),
                    },
                }).ToList();
            BattleActionTimelineSchema.Set(
                table,
                action,
                BattleActionTimelineInlineStructCodec.Serialize(
                    BattleActionTimelineDocument.MovementInlineFields(),
                    records),
                BattleActionTimelineSchema.MovementKeyframesAliases);
            GUI.changed = true;
        }

        /// <summary>
        /// 绘制执行步骤。
        /// </summary>

        private void DrawExecutionSteps(BattleActionTimelineDocument edited)
        {
            if (!TryGetSelectedAction(edited, out _, out BattleActionTimelineRecordData action))
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("步骤 → Shape → 关键帧", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("展开标题编辑这一层；每层的新增按钮只添加到标明的所属对象。",
                EditorStyles.wordWrappedMiniLabel);
            BattleActionTimelineTableData stepTable = edited.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsExecutionStepTable);
            if (stepTable == null)
            {
                EditorGUILayout.HelpBox("缺少执行步骤工作表。", MessageType.Warning);
                return;
            }

            List<BattleActionTimelineRecordData> steps = stepTable.records.Where(
                value => BattleActionTimelineSchema.GetInt(
                    stepTable,
                    value,
                    0,
                    BattleActionTimelineSchema.StepOwnerAliases) == action.rowId).ToList();
            for (int index = 0; index < steps.Count; index++)
            {
                BattleActionTimelineRecordData step = steps[index];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                string stepLabel = "步骤 " + (index + 1);
                string typeLabel = BattleActionTimelineSchema.StepIsMelee(stepTable, step)
                    ? "近战" : BattleActionTimelineSchema.StepIsProjectile(stepTable, step)
                        ? "投射物" : BattleActionTimelineSchema.Get(
                            stepTable, step, BattleActionTimelineSchema.StepTypeAliases);
                bool expanded = DrawHierarchyHeader(stepTable, step,
                    stepLabel + " · " + typeLabel, 1,
                    IsSelectedStep(stepTable, step),
                    () => SelectStep(stepTable.sheetName, step.rowId), out bool delete);
                if (delete)
                {
                    BattleActionTimelineAttackAuthoring.DeleteStep(edited, stepTable.sheetName, step.rowId);
                    if (authoringState.selectedStepRowId == step.rowId &&
                        string.Equals(
                            authoringState.selectedStepSheet,
                            stepTable.sheetName,
                            StringComparison.Ordinal))
                    {
                        authoringState.selectedStepSheet = string.Empty;
                        authoringState.selectedStepRowId = 0;
                    }

                    GUI.changed = true;
                    EditorGUILayout.EndVertical();
                    continue;
                }

                if (!expanded)
                {
                    EditorGUILayout.EndVertical();
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(10f);
                EditorGUILayout.BeginVertical();
                double trigger = BattleActionTimelineSchema.GetDouble(
                    stepTable,
                    step,
                    0d,
                    BattleActionTimelineSchema.TriggerTimeAliases);
                EditorGUI.BeginChangeCheck();
                double editedTrigger = EditorGUILayout.DoubleField("触发时间", trigger);
                if (EditorGUI.EndChangeCheck())
                {
                    BattleActionTimelineSchema.SetDouble(
                        stepTable, step, SnapNonNegative(editedTrigger),
                        BattleActionTimelineSchema.TriggerTimeAliases);
                }
                string stepType = EditorGUILayout.TextField(
                    "步骤类型",
                    BattleActionTimelineSchema.Get(
                        stepTable,
                        step,
                        BattleActionTimelineSchema.StepTypeAliases));
                BattleActionTimelineSchema.Set(
                    stepTable,
                    step,
                    stepType,
                    BattleActionTimelineSchema.StepTypeAliases);
                if (BattleActionTimelineSchema.StepIsMelee(stepTable, step) ||
                    BattleActionTimelineSchema.StepIsProjectile(stepTable, step))
                {
                    int interactionIndex = BattleActionTimelineSchema.GetInteractionTypeIndex(
                        stepTable,
                        step);
                    EditorGUI.BeginChangeCheck();
                    int nextInteractionIndex = EditorGUILayout.Popup(
                        new GUIContent(
                            "步骤交互类型",
                            "交互类型属于当前执行步骤；旧步骤未填写时按普通攻击体处理。"),
                        interactionIndex,
                        new[] { "普通攻击体", "冲击体", "防御体", "蓄力体" });
                    if (EditorGUI.EndChangeCheck())
                    {
                        BattleActionTimelineSchema.SetInteractionType(
                            stepTable,
                            step,
                            nextInteractionIndex);
                    }
                }
                DrawStepConfiguration(edited, ResolveStep(edited, stepTable, step), stepLabel);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("为当前技能 " + action.rowId + " 新增步骤", EditorStyles.boldLabel);
            newAttackTypeIndex = EditorGUILayout.Popup("新增攻击体类型", newAttackTypeIndex,
                new[] { "近战攻击体", "投射物攻击体" });
            if (newAttackTypeIndex == 1)
                newProjectileMovementIndex = EditorGUILayout.Popup("投射物移动类型", newProjectileMovementIndex,
                    new[] { "直线", "抛物线" });
            if (GUILayout.Button("+ 给当前技能新增步骤（当前时间）"))
            {
                if (BattleActionTimelineAttackAuthoring.TryAddAttackStep(edited, authoringState.selectedActionSheet,
                    action.rowId, SnapAndClamp(playheadSeconds, CurrentDuration(edited)),
                    newAttackTypeIndex == 1 ? RefData.EnumBattleExecutionStepType.LaunchProjectile : RefData.EnumBattleExecutionStepType.SpawnMeleeAttackBody,
                    newProjectileMovementIndex == 1 ? RefData.EnumBattleProjectileMovementType.Ballistic : RefData.EnumBattleProjectileMovementType.Linear,
                    authoringState.selectedStepSheet, authoringState.selectedStepRowId, out var created, out string error))
                {
                    SelectStep(created.StepTable.sheetName, created.Step.rowId);
                    attackAuthoringStatus = created.IsProjectile
                        ? $"已新增独立投射物步骤：发射配置 {created.SpawnId} / 投射物 {created.ProjectileId} / 攻击体 {created.BodyId} / 移动配置 {created.MovementId}。"
                        : $"已新增独立近战步骤：生成配置 {created.SpawnId} / 攻击体 {created.BodyId}。";
                    attackAuthoringStatus +=
                        (created.Blank ? "当前为默认形状；仅在源表存在对应条目时自动绑定基础伤害/受击，请在命中效果中确认。" + (created.IsProjectile ? "投射物尚未配置表现资源。" : "")
                            : "初始配置来自当前动作的同类型步骤，后续修改互不影响。") +
                        "尚未写回源表。";
                    attackAuthoringError = false;
                    GUI.changed = true;
                }
                else
                {
                    attackAuthoringStatus = error;
                    attackAuthoringError = true;
                }
            }
            EditorGUILayout.LabelField("每个步骤拥有独立攻击配置；从同类步骤复制初值后，形状、关键帧和命中效果互不影响。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("攻击配置键由技能和步骤下标自动生成，无需手填；删除步骤时后续配置链同步重编号。", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(attackAuthoringStatus))
                EditorGUILayout.HelpBox(attackAuthoringStatus, attackAuthoringError ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.EndVertical();
        }
        /// <summary>
        /// 在所属步骤下绘制公共参数和 Shape，不能依赖全局当前选择决定编辑对象。
        /// </summary>

        private void DrawStepConfiguration(
            BattleActionTimelineDocument edited,
            ResolvedStep resolved,
            string stepLabel)
        {
            if (resolved == null)
            {
                return;
            }

            if (BattleActionTimelineSchema.StepIsSelfEffect(resolved.StepTable, resolved.Step))
            {
                int id = EditorGUILayout.IntField("自身效果 ID", BattleActionTimelineSchema.GetInt(
                    resolved.StepTable, resolved.Step, 0, BattleActionTimelineSchema.EffectConfigAliases));
                BattleActionTimelineSchema.SetInt(resolved.StepTable, resolved.Step, id,
                    BattleActionTimelineSchema.EffectConfigAliases);
                BattleActionTimelineSchema.TryGetSelfEffectReference(resolved.StepTable, resolved.Step, out var effect);
                string problem = BattleActionTimelineHitEffects.ReferenceError(edited, effect);
                EditorGUILayout.HelpBox("直接对释放者生效，不创建攻击体。" + BattleActionTimelineHitEffects.TableName(effect.EffectType) + "：" +
                    BattleActionTimelineHitEffects.Describe(edited, effect), string.IsNullOrEmpty(problem) ? MessageType.Info : MessageType.Error);
                return;
            }

            if (resolved.Owner == null || resolved.Body == null)
            {
                EditorGUILayout.HelpBox("此步骤缺少对应的生成／发射配置或攻击体。", MessageType.Error);
                return;
            }

            EditorGUILayout.Space(6f);
            bool showParameters = DrawViewFoldout(
                "parameters:" + HierarchyKey(resolved.StepTable, resolved.Step),
                "步骤参数（生成、命中）");
            if (showParameters)
            {
                DrawStepCommonFields(edited, resolved);
            }

            DrawShapes(edited, resolved, stepLabel);
        }

        private void DrawStepCommonFields(BattleActionTimelineDocument edited, ResolvedStep resolved)
        {
            if ((resolved.IsMelee || resolved.IsProjectile) && resolved.Owner != null && resolved.Body != null)
            {
                EditorGUILayout.HelpBox(
                    resolved.IsProjectile
                        ? "当前步骤拥有独立的发射配置、投射物、移动、攻击体、Shape 和变换轨。"
                        : "当前步骤拥有独立的近战生成配置、攻击体、Shape 和变换轨。",
                    MessageType.Info);
            }
            if (resolved.Owner != null)
            {
                EditorGUILayout.LabelField(resolved.OwnerTable.sheetName + " id=" + resolved.Owner.rowId,
                    EditorStyles.miniBoldLabel);
                DrawOwnerFields(resolved);
                DrawProjectileMovementFields(edited, resolved);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "当前步骤没有按技能和步骤下标解析到自己的攻击配置。",
                    MessageType.Error);
                return;
            }

            if (resolved.Body != null)
            {
                DrawAttackBody(edited, resolved);
            }
        }
        /// <summary>
        /// 绘制所有者字段。
        /// </summary>

        private void DrawOwnerFields(ResolvedStep resolved)
        {
            BattleActionTimelineTableData table = resolved.OwnerTable;
            BattleActionTimelineRecordData owner = resolved.Owner;
            if (resolved.IsMelee)
            {
                EditorGUI.BeginChangeCheck();
                double duration = EditorGUILayout.DoubleField(
                    "activeDuration",
                    BattleActionTimelineSchema.GetDouble(
                        table,
                        owner,
                        0d,
                        "activeDuration"));
                if (EditorGUI.EndChangeCheck())
                {
                    BattleActionTimelineSchema.SetDouble(
                        table, owner, SnapNonNegative(duration), "activeDuration");
                }
            }
            else if (resolved.IsProjectile)
            {
                float x = EditorGUILayout.FloatField(
                    "spawnOffsetX",
                    (float)BattleActionTimelineSchema.GetDouble(
                        table,
                        owner,
                        0d,
                        "spawnOffsetX",
                        "offsetX"));
                float y = EditorGUILayout.FloatField(
                    "spawnOffsetY",
                    (float)BattleActionTimelineSchema.GetDouble(
                        table,
                        owner,
                        0d,
                        "spawnOffsetY",
                        "offsetY"));
                BattleActionTimelineSchema.SetDouble(
                    table,
                    owner,
                    x,
                    "spawnOffsetX",
                    "offsetX");
                BattleActionTimelineSchema.SetDouble(
                    table,
                    owner,
                    y,
                    "spawnOffsetY",
                    "offsetY");
                string direction = EditorGUILayout.TextField(
                    "directionSource",
                    BattleActionTimelineSchema.Get(
                        table,
                        owner,
                        "directionSource",
                        "directionMode"));
                BattleActionTimelineSchema.Set(
                    table,
                    owner,
                    direction,
                    "directionSource",
                    "directionMode");
                double angle = EditorGUILayout.DoubleField(
                    "angleOffsetDegrees",
                    BattleActionTimelineSchema.GetDouble(
                        table,
                        owner,
                        0d,
                        "angleOffsetDegrees"));
                BattleActionTimelineSchema.SetDouble(
                    table,
                    owner,
                    angle,
                    "angleOffsetDegrees");

                if (resolved.Projectile != null)
                {
                    EditorGUILayout.LabelField(
                        resolved.ProjectileTable.sheetName + " id=" +
                        resolved.Projectile.rowId,
                        EditorStyles.miniBoldLabel);
                    EditorGUI.BeginChangeCheck();
                    double lifetime = EditorGUILayout.DoubleField(
                        "maxLifetime",
                        BattleActionTimelineSchema.GetDouble(
                            resolved.ProjectileTable,
                            resolved.Projectile,
                            0d,
                            "maxLifetime"));
                    if (EditorGUI.EndChangeCheck())
                    {
                        BattleActionTimelineSchema.SetDouble(
                            resolved.ProjectileTable, resolved.Projectile,
                            SnapNonNegative(lifetime), "maxLifetime");
                    }
                    string movementType = EditorGUILayout.TextField(
                        "movementType",
                        BattleActionTimelineSchema.Get(
                            resolved.ProjectileTable,
                            resolved.Projectile,
                            "movementType"));
                    BattleActionTimelineSchema.Set(
                        resolved.ProjectileTable,
                        resolved.Projectile,
                        movementType,
                        "movementType");
                    EditorGUILayout.LabelField(
                        "移动配置键（自动）",
                        BattleActionTimelineSchema.GetInt(
                            resolved.ProjectileTable,
                            resolved.Projectile,
                            0,
                            "movementConfigId").ToString(CultureInfo.InvariantCulture));
                }
            }
        }
        /// <summary>
        /// 绘制投射物移动字段。
        /// </summary>

        private static void DrawProjectileMovementFields(
            BattleActionTimelineDocument document,
            ResolvedStep resolved)
        {
            if (!resolved.IsProjectile || resolved.Projectile == null ||
                resolved.ProjectileTable == null)
            {
                return;
            }

            int movementId = BattleActionTimelineSchema.GetInt(
                resolved.ProjectileTable,
                resolved.Projectile,
                0,
                "movementConfigId");
            string movementType = BattleActionTimelineSchema.Get(
                resolved.ProjectileTable,
                resolved.Projectile,
                "movementType");
            if (!BattleActionTimelineAttackAuthoring.TryReadMovementType(movementType, out var parsedMovementType))
            {
                EditorGUILayout.HelpBox("movementType 无效，未回退编辑其它移动配置。", MessageType.Error);
                return;
            }
            bool ballistic = parsedMovementType == RefData.EnumBattleProjectileMovementType.Ballistic;
            BattleActionTimelineTableData table = ballistic
                ? document.tables.FirstOrDefault(
                    BattleActionTimelineSchema.IsBallisticMovementTable)
                : document.tables.FirstOrDefault(
                    BattleActionTimelineSchema.IsLinearMovementTable);
            BattleActionTimelineRecordData movement =
                BattleActionTimelineSchema.FindById(table, movementId);
            if (table == null || movement == null)
            {
                EditorGUILayout.HelpBox(
                    "movementConfigId=" + movementId +
                    " 没有对应的 " + (ballistic ? "Ballistic" : "Linear") +
                    " 移动配置。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(
                table.sheetName + " id=" + movement.rowId + "（移动配置）",
                EditorStyles.miniBoldLabel);
            if (ballistic)
            {
                double initialSpeed = EditorGUILayout.DoubleField(
                    "initialSpeed",
                    BattleActionTimelineSchema.GetDouble(
                        table,
                        movement,
                        0d,
                        "initialSpeed"));
                double gravityScale = EditorGUILayout.DoubleField(
                    "gravityScale",
                    BattleActionTimelineSchema.GetDouble(
                        table,
                        movement,
                        0d,
                        "gravityScale"));
                BattleActionTimelineSchema.SetDouble(
                    table,
                    movement,
                    initialSpeed,
                    "initialSpeed");
                BattleActionTimelineSchema.SetDouble(
                    table,
                    movement,
                    gravityScale,
                    "gravityScale");
            }
            else
            {
                double speed = EditorGUILayout.DoubleField(
                    "speed",
                    BattleActionTimelineSchema.GetDouble(
                        table,
                        movement,
                        0d,
                        "speed"));
                BattleActionTimelineSchema.SetDouble(
                    table,
                    movement,
                    speed,
                    "speed");
            }
        }
        /// <summary>
        /// 绘制攻击体。
        /// </summary>

        private void DrawAttackBody(BattleActionTimelineDocument edited, ResolvedStep resolved)
        {
            EditorGUILayout.LabelField(
                resolved.BodyTable.sheetName + " id=" + resolved.Body.rowId,
                EditorStyles.miniBoldLabel);
            int strength = EditorGUILayout.IntField(
                "clashStrength",
                BattleActionTimelineSchema.GetInt(
                    resolved.BodyTable,
                    resolved.Body,
                    0,
                    "clashStrength"));
            int resistance = EditorGUILayout.IntField(
                "clashResistance",
                BattleActionTimelineSchema.GetInt(
                    resolved.BodyTable,
                    resolved.Body,
                    0,
                    "clashResistance"));
            int maxPerTarget = EditorGUILayout.IntField(
                "maxHitsPerTarget",
                BattleActionTimelineSchema.GetInt(
                    resolved.BodyTable,
                    resolved.Body,
                    1,
                    "maxHitsPerTarget"));
            double interval = EditorGUILayout.DoubleField(
                "sameTargetHitInterval",
                BattleActionTimelineSchema.GetDouble(
                    resolved.BodyTable,
                    resolved.Body,
                    0d,
                    "sameTargetHitInterval"));
            int maxTotal = EditorGUILayout.IntField(
                "maxTotalHitCount",
                BattleActionTimelineSchema.GetInt(
                    resolved.BodyTable,
                    resolved.Body,
                    1,
                    "maxTotalHitCount"));
            BattleActionTimelineSchema.SetInt(
                resolved.BodyTable,
                resolved.Body,
                strength,
                "clashStrength");
            BattleActionTimelineSchema.SetInt(
                resolved.BodyTable,
                resolved.Body,
                resistance,
                "clashResistance");
            BattleActionTimelineSchema.SetInt(
                resolved.BodyTable,
                resolved.Body,
                maxPerTarget,
                "maxHitsPerTarget");
            BattleActionTimelineSchema.SetDouble(
                resolved.BodyTable,
                resolved.Body,
                interval,
                "sameTargetHitInterval");
            BattleActionTimelineSchema.SetInt(
                resolved.BodyTable,
                resolved.Body,
                maxTotal,
                "maxTotalHitCount");
            DrawHitEffects(edited, resolved);
        }

        private static void DrawHitEffects(BattleActionTimelineDocument edited, ResolvedStep resolved)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("命中效果（当前步骤的所有 Shape 共用）", EditorStyles.miniBoldLabel);
            bool defense = BattleActionTimelineSchema.GetInteractionTypeIndex(resolved.StepTable, resolved.Step) == 2;
            if (defense)
            {
                EditorGUILayout.HelpBox("防御体只参与体间交互，不对角色结算以下命中效果；列表可以为空。切回攻击类型时原效果仍保留。", MessageType.Info);
            }
            if (!BattleActionTimelineHitEffects.TryRead(resolved.BodyTable, resolved.Body, out var effects, out string error))
            {
                EditorGUILayout.HelpBox(error + " 原始内容已保留，请修正后再写回。", MessageType.Error);
                string raw = EditorGUILayout.TextArea(BattleActionTimelineSchema.Get(
                    resolved.BodyTable, resolved.Body, "effects", "hitEffects"));
                BattleActionTimelineSchema.Set(resolved.BodyTable, resolved.Body, raw, "effects", "hitEffects");
                return;
            }

            if (!defense && effects.Count == 0)
                EditorGUILayout.HelpBox("命中效果为空：形状重叠也不会扣血或造成普通受击。若要造成伤害，请添加 Damage；刻意无伤害的步骤可保持为空。", MessageType.Warning);
            else if (!defense && !effects.Any(effect => effect.EffectType == (int)RefData.EnumBattleCombatEffectType.Damage))
                EditorGUILayout.HelpBox("没有直接 Damage 效果，不会直接造成普通攻击伤害；Operation/Persistent 是否改血取决于各自配置。", MessageType.Info);

            bool changed = false;
            for (int index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var type = (RefData.EnumBattleCombatEffectType)EditorGUILayout.EnumPopup(
                    (RefData.EnumBattleCombatEffectType)effect.EffectType, GUILayout.Width(115f));
                int id = EditorGUILayout.IntField("效果 ID", effect.EffectId);
                if (EditorGUI.EndChangeCheck())
                {
                    effect = new BattleAttackEffectReference((int)type, id);
                    effects[index] = effect;
                    changed = true;
                }
                bool remove = GUILayout.Button("删除", GUILayout.Width(45f));
                EditorGUILayout.EndHorizontal();
                string problem = BattleActionTimelineHitEffects.ReferenceError(edited, effect);
                EditorGUILayout.HelpBox(BattleActionTimelineHitEffects.TableName(effect.EffectType) + "：" +
                    BattleActionTimelineHitEffects.Describe(edited, effect),
                    string.IsNullOrEmpty(problem) ? MessageType.None : MessageType.Error);
                if (remove) { effects.RemoveAt(index--); changed = true; }
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新增命中效果"))
            {
                effects.Add(new BattleAttackEffectReference((int)RefData.EnumBattleCombatEffectType.Damage, 1));
                changed = true;
            }
            using (new EditorGUI.DisabledScope(effects.Count != 0 || defense))
            {
                if (GUILayout.Button("填入基础伤害 / 受击"))
                {
                    effects = BattleActionTimelineHitEffects.BasicDefaults(edited);
                    changed = true;
                }
            }
            EditorGUILayout.EndHorizontal();
            if (changed) BattleActionTimelineHitEffects.Write(resolved.BodyTable, resolved.Body, effects);
        }
        /// <summary>
        /// 绘制形状。
        /// </summary>

        private void DrawShapes(
            BattleActionTimelineDocument edited,
            ResolvedStep resolved,
            string stepLabel)
        {
            BattleActionTimelineTableData table = edited.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsShapeTable);
            if (table == null)
            {
                EditorGUILayout.HelpBox("缺少 AttackBodyShape Sheet。", MessageType.Warning);
                return;
            }

            List<BattleActionTimelineRecordData> shapes = table.records.Where(
                record => BattleActionTimelineSchema.GetInt(
                    table,
                    record,
                    0,
                    BattleActionTimelineSchema.AttackBodyIdAliases) ==
                    resolved.Body.rowId).ToList();
            shapes = shapes.OrderBy(record => BattleActionTimelineSchema.GetInt(
                table,
                record,
                int.MaxValue,
                "shapeIndex",
                "index")).ToList();
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(stepLabel + " / 组合形状（" + shapes.Count + "）", EditorStyles.miniBoldLabel);
            ResolvedStep preview = ResolveSelectedStep(edited);
            for (int shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
            {
                BattleActionTimelineRecordData shape = shapes[shapeIndex];
                string shapeLabel = "Shape " + (shapeIndex + 1);
                string ownerLabel = stepLabel + " / " + shapeLabel;
                BindShapeKeyframes(edited, resolved, shape);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool expanded = DrawHierarchyHeader(table, shape,
                    shapeLabel + " · " + BattleActionTimelineSchema.Get(table, shape, "shapeType"), 2,
                    IsSelectedStep(resolved.StepTable, resolved.Step) &&
                        preview?.SelectedShape?.rowId == shape.rowId,
                    () => SelectStepChild(resolved, table, shape), out bool delete);
                if (delete)
                {
                    if (IsSelectedStep(resolved.StepTable, resolved.Step) &&
                        preview?.SelectedShape?.rowId == shape.rowId)
                        SelectStep(resolved.StepTable.sheetName, resolved.Step.rowId);
                    table.records.Remove(shape);
                    RemoveShapeKeyframes(edited, shape.rowId);
                    GUI.changed = true;
                    EditorGUILayout.EndVertical();
                    continue;
                }

                if (!expanded)
                {
                    EditorGUILayout.EndVertical();
                    continue;
                }
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(10f);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("所属：" + ownerLabel, EditorStyles.miniLabel);
                string type = EditorGUILayout.TextField(
                    "形状类型",
                    BattleActionTimelineSchema.Get(table, shape, "shapeType"));
                Vector2 offset = EditorGUILayout.Vector2Field(
                    "局部偏移",
                    new Vector2(
                        (float)BattleActionTimelineSchema.GetDouble(
                            table,
                            shape,
                            0d,
                            "offsetX",
                            "localOffsetX"),
                        (float)BattleActionTimelineSchema.GetDouble(
                            table,
                            shape,
                            0d,
                            "offsetY",
                            "localOffsetY")));
                float rotation = EditorGUILayout.FloatField(
                    "局部旋转角度",
                    (float)BattleActionTimelineSchema.GetDouble(
                        table,
                        shape,
                        0d,
                        "rotationDegrees",
                        "localRotationDegrees"));
                Vector2 size = EditorGUILayout.Vector2Field(
                    "尺寸（矩形/胶囊）",
                    new Vector2(
                        (float)BattleActionTimelineSchema.GetDouble(
                            table,
                            shape,
                            1d,
                            "width",
                            "sizeX"),
                        (float)BattleActionTimelineSchema.GetDouble(
                            table,
                            shape,
                            1d,
                            "height",
                            "sizeY")));
                float radius = EditorGUILayout.FloatField(
                    "半径（圆形）",
                    (float)BattleActionTimelineSchema.GetDouble(
                        table,
                        shape,
                        0.5d,
                        "radius"));
                string capsuleDirection = BattleActionTimelineSchema.Get(
                    table,
                    shape,
                    "capsuleDirection",
                    "direction");
                if (type.IndexOf("Capsule", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    type == "2")
                {
                    capsuleDirection = EditorGUILayout.TextField(
                        "胶囊方向",
                        string.IsNullOrWhiteSpace(capsuleDirection)
                            ? "Vertical"
                            : capsuleDirection);
                }

                BattleActionTimelineSchema.Set(table, shape, type, "shapeType");
                BattleActionTimelineSchema.SetDouble(
                    table,
                    shape,
                    offset.x,
                    "offsetX",
                    "localOffsetX");
                BattleActionTimelineSchema.SetDouble(
                    table,
                    shape,
                    offset.y,
                    "offsetY",
                    "localOffsetY");
                BattleActionTimelineSchema.SetDouble(
                    table,
                    shape,
                    rotation,
                    "rotationDegrees",
                    "localRotationDegrees");
                BattleActionTimelineSchema.SetDouble(
                    table,
                    shape,
                    size.x,
                    "width",
                    "sizeX");
                BattleActionTimelineSchema.SetDouble(
                    table,
                    shape,
                    size.y,
                    "height",
                    "sizeY");
                BattleActionTimelineSchema.SetDouble(table, shape, radius, "radius");
                BattleActionTimelineSchema.Set(
                    table,
                    shape,
                    capsuleDirection,
                    "capsuleDirection",
                    "direction");
                DrawKeyframes(edited, resolved, ownerLabel);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("为 " + stepLabel + " 新增 Shape", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 矩形"))
            {
                SelectStep(resolved.StepTable.sheetName, resolved.Step.rowId);
                AddShape(edited, table, resolved.Body.rowId, "Box");
                GUI.changed = true;
            }

            if (GUILayout.Button("+ 圆形"))
            {
                SelectStep(resolved.StepTable.sheetName, resolved.Step.rowId);
                AddShape(edited, table, resolved.Body.rowId, "Circle");
                GUI.changed = true;
            }

            if (GUILayout.Button("+ 胶囊"))
            {
                SelectStep(resolved.StepTable.sheetName, resolved.Step.rowId);
                AddShape(edited, table, resolved.Body.rowId, "Capsule");
                GUI.changed = true;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        /// <summary>
        /// 添加形状。
        /// </summary>

        private void AddShape(
            BattleActionTimelineDocument edited,
            BattleActionTimelineTableData table,
            int attackBodyId,
            string shapeType)
        {
            BattleActionTimelineRecordData shape = CreateRecord(table);
            BattleActionTimelineSchema.SetInt(
                table,
                shape,
                attackBodyId,
                BattleActionTimelineSchema.AttackBodyIdAliases);
            int nextShapeIndex = table.records
                .Where(record => BattleActionTimelineSchema.GetInt(
                    table,
                    record,
                    0,
                    BattleActionTimelineSchema.AttackBodyIdAliases) == attackBodyId)
                .Select(record => BattleActionTimelineSchema.GetInt(table, record, -1, "shapeIndex", "index"))
                .DefaultIfEmpty(-1)
                .Max() + 1;
            BattleActionTimelineSchema.SetInt(table, shape, nextShapeIndex, "shapeIndex", "index");
            BattleActionTimelineSchema.Set(table, shape, shapeType, "shapeType");
            bool circle = string.Equals(
                shapeType,
                "Circle",
                StringComparison.OrdinalIgnoreCase);
            bool capsule = string.Equals(
                shapeType,
                "Capsule",
                StringComparison.OrdinalIgnoreCase);
            BattleActionTimelineSchema.SetDouble(
                table,
                shape,
                circle ? 0d : capsule ? 0.75d : 1d,
                "width",
                "sizeX");
            BattleActionTimelineSchema.SetDouble(
                table,
                shape,
                circle ? 0d : capsule ? 1.5d : 1d,
                "height",
                "sizeY");
            BattleActionTimelineSchema.SetDouble(
                table,
                shape,
                circle ? 0.5d : 0d,
                "radius");
            BattleActionTimelineSchema.Set(
                table,
                shape,
                capsule ? "Vertical" : string.Empty,
                "capsuleDirection",
                "direction");
            table.records.Add(shape);
            AddDefaultShapeKeyframe(edited, shape.rowId);
            SetHierarchyExpanded(table, shape);
            SelectShared(table.sheetName, shape.rowId);
        }

        private static void RemoveShapeKeyframes(
            BattleActionTimelineDocument document,
            int shapeId)
        {
            BattleActionTimelineTableData keyframeTable = document?.tables.FirstOrDefault(table =>
                BattleActionTimelineSchema.IsKeyframeTable(table) &&
                (table.sheetName.IndexOf("ShapeTransformKeyframe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 table.sheetName.IndexOf("AttackShape", StringComparison.OrdinalIgnoreCase) >= 0));
            if (keyframeTable == null)
            {
                return;
            }

            int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(keyframeTable);
            if (ownerColumn < 0)
            {
                return;
            }

            keyframeTable.records.RemoveAll(record =>
                int.TryParse(
                    record.cells[ownerColumn],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int ownerId) && ownerId == shapeId);
            keyframeTable.inlineOwners.RemoveAll(owner => owner.ownerRowId == shapeId);
        }

        private static void AddDefaultShapeKeyframe(
            BattleActionTimelineDocument document,
            int shapeId)
        {
            AddShapeKeyframe(document, shapeId, 0d);
        }

        // 新增轨道始终由明确的 Shape 拥有，不能用步骤的 Spawn/Launch ID 代替。
        private static BattleActionTimelineRecordData AddShapeKeyframe(
            BattleActionTimelineDocument document,
            int shapeId,
            double localTime)
        {
            BattleActionTimelineTableData keyframeTable = document?.tables.FirstOrDefault(table =>
                BattleActionTimelineSchema.IsKeyframeTable(table) &&
                (table.sheetName.IndexOf("ShapeTransformKeyframe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 table.sheetName.IndexOf("AttackShape", StringComparison.OrdinalIgnoreCase) >= 0));
            if (keyframeTable == null || shapeId <= 0)
            {
                return null;
            }

            int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(keyframeTable);
            BattleActionTimelineTableData shapes = document.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsShapeTable);
            BattleActionTimelineRecordData shape = BattleActionTimelineSchema.FindById(shapes, shapeId);
            if (ownerColumn < 0 || shape == null || double.IsNaN(localTime) || double.IsInfinity(localTime))
            {
                return null;
            }

            if (keyframeTable.isInlineProjection &&
                !keyframeTable.inlineOwners.Any(owner => owner.ownerRowId == shapeId))
            {
                BattleActionTimelineSchema.Set(shapes, shape, "[]", keyframeTable.inlineSourceColumnName);
                keyframeTable.inlineOwners.Add(new BattleActionTimelineInlineOwnerData
                {
                    ownerRowId = shapeId, originalCell = "[]", baselineSignature = string.Empty,
                });
            }

            BattleActionTimelineRecordData keyframe = CreateRecord(keyframeTable);
            keyframe.cells[ownerColumn] = shapeId.ToString(CultureInfo.InvariantCulture);
            BattleActionTimelineSchema.SetDouble(keyframeTable, keyframe, Math.Max(0d, localTime), BattleActionTimelineSchema.LocalTimeAliases);
            BattleActionTimelineSchema.SetDouble(keyframeTable, keyframe, 0d, BattleActionTimelineSchema.OffsetXAliases);
            BattleActionTimelineSchema.SetDouble(keyframeTable, keyframe, 0d, BattleActionTimelineSchema.OffsetYAliases);
            BattleActionTimelineSchema.SetDouble(keyframeTable, keyframe, 0d, BattleActionTimelineSchema.RotationAliases);
            BattleActionTimelineSchema.SetDouble(keyframeTable, keyframe, 1d, BattleActionTimelineSchema.ScaleXAliases);
            BattleActionTimelineSchema.SetDouble(keyframeTable, keyframe, 1d, BattleActionTimelineSchema.ScaleYAliases);
            BattleActionTimelineSchema.Set(keyframeTable, keyframe, "Linear", BattleActionTimelineSchema.InterpolationAliases);
            keyframeTable.records.Add(keyframe);
            return keyframe;
        }
        /// <summary>
        /// 绘制关键帧。
        /// </summary>

        private void DrawKeyframes(
            BattleActionTimelineDocument edited,
            ResolvedStep resolved,
            string ownerLabel)
        {
            if (resolved.SelectedShape == null || resolved.KeyframeTable == null)
            {
                EditorGUILayout.HelpBox(
                    "当前 Shape 没有可识别的关键帧工作表。",
                    MessageType.Info);
                return;
            }

            BattleActionTimelineTableData table = resolved.KeyframeTable;
            int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(table);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("此 Shape 的关键帧（" + resolved.Keyframes.Count + "）", EditorStyles.miniBoldLabel);
            for (int index = 0; index < resolved.Keyframes.Count; index++)
            {
                BattleActionTimelineRecordData keyframe = resolved.Keyframes[index];
                double time = BattleActionTimelineSchema.GetDouble(
                    table, keyframe, 0d, BattleActionTimelineSchema.LocalTimeAliases);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool expanded = DrawHierarchyHeader(table, keyframe,
                    "关键帧 " + (index + 1) + " · " + time.ToString("0.###", CultureInfo.InvariantCulture) + "s", 3,
                    IsSelectedStep(resolved.StepTable, resolved.Step) && string.Equals(
                        authoringState.selectedSharedSheet,
                        table.sheetName,
                        StringComparison.Ordinal) &&
                    authoringState.selectedSharedRowId == keyframe.rowId,
                    () => SelectStepChild(resolved, table, keyframe), out bool delete);
                if (delete)
                {
                    if (IsSelectedStep(resolved.StepTable, resolved.Step) &&
                        authoringState.selectedSharedSheet == table.sheetName &&
                        authoringState.selectedSharedRowId == keyframe.rowId)
                        SelectStepChild(resolved, resolved.ShapeTable, resolved.SelectedShape);
                    table.records.Remove(keyframe);
                    GUI.changed = true;
                    EditorGUILayout.EndVertical();
                    continue;
                }

                if (!expanded)
                {
                    EditorGUILayout.EndVertical();
                    continue;
                }
                EditorGUILayout.LabelField("所属：" + ownerLabel, EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                double localTime = EditorGUILayout.DoubleField(
                    "局部时间",
                    BattleActionTimelineSchema.GetDouble(
                        table,
                        keyframe,
                        0d,
                        BattleActionTimelineSchema.LocalTimeAliases));
                if (EditorGUI.EndChangeCheck())
                {
                    BattleActionTimelineSchema.SetDouble(
                        table, keyframe,
                        Math.Max(0d, Math.Min(resolved.Lifetime, SnapNonNegative(localTime))),
                        BattleActionTimelineSchema.LocalTimeAliases);
                }
                Vector2 offset = EditorGUILayout.Vector2Field(
                    "偏移",
                    new Vector2(
                        (float)BattleActionTimelineSchema.GetDouble(
                            table,
                            keyframe,
                            0d,
                            BattleActionTimelineSchema.OffsetXAliases),
                        (float)BattleActionTimelineSchema.GetDouble(
                            table,
                            keyframe,
                            0d,
                            BattleActionTimelineSchema.OffsetYAliases)));
                float rotation = EditorGUILayout.FloatField(
                    "旋转角度",
                    (float)BattleActionTimelineSchema.GetDouble(
                        table,
                        keyframe,
                        0d,
                        BattleActionTimelineSchema.RotationAliases));
                Vector2 scale = EditorGUILayout.Vector2Field(
                    "缩放",
                    new Vector2(
                        (float)BattleActionTimelineSchema.GetDouble(
                            table,
                            keyframe,
                            1d,
                            BattleActionTimelineSchema.ScaleXAliases),
                        (float)BattleActionTimelineSchema.GetDouble(
                            table,
                            keyframe,
                            1d,
                            BattleActionTimelineSchema.ScaleYAliases)));
                string interpolation = EditorGUILayout.TextField(
                    "插值方式",
                    BattleActionTimelineSchema.Get(
                        table,
                        keyframe,
                        BattleActionTimelineSchema.InterpolationAliases));
                BattleActionTimelineSchema.SetDouble(
                    table,
                    keyframe,
                    offset.x,
                    BattleActionTimelineSchema.OffsetXAliases);
                BattleActionTimelineSchema.SetDouble(
                    table,
                    keyframe,
                    offset.y,
                    BattleActionTimelineSchema.OffsetYAliases);
                BattleActionTimelineSchema.SetDouble(
                    table,
                    keyframe,
                    rotation,
                    BattleActionTimelineSchema.RotationAliases);
                BattleActionTimelineSchema.SetDouble(
                    table,
                    keyframe,
                    scale.x,
                    BattleActionTimelineSchema.ScaleXAliases);
                BattleActionTimelineSchema.SetDouble(
                    table,
                    keyframe,
                    scale.y,
                    BattleActionTimelineSchema.ScaleYAliases);
                BattleActionTimelineSchema.Set(
                    table,
                    keyframe,
                    interpolation,
                    BattleActionTimelineSchema.InterpolationAliases);
                EditorGUILayout.EndVertical();
            }

            if (ownerColumn >= 0 && GUILayout.Button("+ 给 " + ownerLabel + " 新增关键帧"))
            {
                double local = Math.Max(
                    0d,
                    Math.Min(resolved.Lifetime, playheadSeconds - resolved.TriggerTime));
                BattleActionTimelineRecordData keyframe = AddShapeKeyframe(
                    edited, resolved.SelectedShape.rowId, SnapNonNegative(local));
                if (keyframe != null)
                {
                    SetHierarchyExpanded(table, keyframe);
                    SelectStepChild(resolved, table, keyframe);
                    GUI.changed = true;
                }
            }
        }
        /// <summary>
        /// 绘制分级标题；折叠、预览和删除按钮本身不把窗口视图计为数据修改。
        /// </summary>

        private bool DrawHierarchyHeader(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record,
            string title,
            int level,
            bool selected,
            Action onSelect,
            out bool delete)
        {
            bool previousChanged = GUI.changed;
            string key = HierarchyKey(table, record);
            bool expanded = !hierarchyExpanded.TryGetValue(key, out bool stored)
                ? level < 3 : stored;
            Rect header = EditorGUILayout.GetControlRect(false, 26f);
            Color tint = selected ? new Color(0.2f, 0.55f, 0.9f, 0.32f)
                : level == 1 ? new Color(0.25f, 0.5f, 0.8f, 0.18f)
                : level == 2 ? new Color(0.3f, 0.7f, 0.55f, 0.15f)
                : new Color(0.6f, 0.6f, 0.6f, 0.13f);
            EditorGUI.DrawRect(header, tint);
            EditorGUI.LabelField(new Rect(header.x + 4f, header.y + 4f, 30f, 18f),
                level == 1 ? "一级" : level == 2 ? "二级" : "三级", EditorStyles.miniBoldLabel);
            expanded = EditorGUI.Foldout(
                new Rect(header.x + 48f, header.y + 4f, Math.Max(40f, header.width - 136f), 18f),
                expanded, new GUIContent(title, table.sheetName + " / 内部行号 " + record.rowId), true);
            hierarchyExpanded[key] = expanded;
            if (GUI.Button(new Rect(header.xMax - 83f, header.y + 3f, 40f, 20f),
                new GUIContent("预览", "在时间轴和画面中选中这一项")))
                onSelect?.Invoke();
            delete = GUI.Button(new Rect(header.xMax - 41f, header.y + 3f, 38f, 20f), "删除");
            GUI.changed = previousChanged;
            return expanded;
        }

        private static string HierarchyKey(
            BattleActionTimelineTableData table, BattleActionTimelineRecordData record) =>
            table.sheetName + ":" + record.rowId.ToString(CultureInfo.InvariantCulture);

        private bool DrawViewFoldout(string key, string label)
        {
            bool previousChanged = GUI.changed;
            bool expanded = hierarchyExpanded.TryGetValue(key, out bool stored) && stored;
            expanded = EditorGUILayout.Foldout(expanded, label, true);
            hierarchyExpanded[key] = expanded;
            GUI.changed = previousChanged;
            return expanded;
        }

        private void SetHierarchyExpanded(
            BattleActionTimelineTableData table, BattleActionTimelineRecordData record)
        {
            hierarchyExpanded[HierarchyKey(table, record)] = true;
        }

        private bool IsSelectedStep(
            BattleActionTimelineTableData table, BattleActionTimelineRecordData step) =>
            authoringState.selectedStepSheet == table.sheetName &&
            authoringState.selectedStepRowId == step.rowId;

        private void SelectStepChild(
            ResolvedStep resolved, BattleActionTimelineTableData table, BattleActionTimelineRecordData record)
        {
            SelectStep(resolved.StepTable.sheetName, resolved.Step.rowId);
            if (resolved.SelectedShape != null)
                SetHierarchyExpanded(resolved.ShapeTable, resolved.SelectedShape);
            SetHierarchyExpanded(table, record);
            SelectShared(table.sheetName, record.rowId);
            if (BattleActionTimelineSchema.IsKeyframeTable(table))
            {
                previewEditKeyframes = true;
                LocatePreviewKeyframe(resolved, record);
            }
        }
        /// <summary>
        /// 绘制`Issues`。
        /// </summary>

        private void DrawIssues()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                validationIssues.Count == 0
                    ? "校验通过"
                    : "校验问题 (" + validationIssues.Count + ")",
                EditorStyles.boldLabel);
            issueScroll = EditorGUILayout.BeginScrollView(
                issueScroll,
                GUILayout.Height(130f));
            if (validationIssues.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    workbookSnapshot != null && workbookSnapshot.HasActionStructure
                        ? "当前动作工作集可安全写回源 xlsx；本工具不会调用导表。"
                        : "当前草稿结构校验通过，但源表基线未绑定，不能安全写回。",
                    MessageType.Info);
            }
            else
            {
                foreach (string issue in validationIssues)
                {
                    EditorGUILayout.HelpBox(issue, MessageType.Error);
                }
            }

            EditorGUILayout.EndScrollView();
        }
        /// <summary>
        /// 绘制时间轴并预览。
        /// </summary>

        private void DrawTimelineAndPreview(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            float availableHeight = Mathf.Max(
                MinimumTimelineHeight + MinimumPreviewHeight + SplitterWidth,
                rect.height - 16f);
            float maximumTimelineHeight = Mathf.Max(
                MinimumTimelineHeight,
                availableHeight - MinimumPreviewHeight - SplitterWidth);
            timelineHeight = Mathf.Clamp(
                timelineHeight,
                MinimumTimelineHeight,
                maximumTimelineHeight);
            Rect timeline = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f,
                timelineHeight);
            Rect splitter = new Rect(
                timeline.x,
                timeline.yMax,
                timeline.width,
                SplitterWidth);
            Rect preview = new Rect(
                timeline.x,
                splitter.yMax + 2f,
                timeline.width,
                Mathf.Max(120f, rect.yMax - splitter.yMax - 10f));
            DrawTimeline(timeline);
            DrawPreview(preview);
            DrawTimelineSplitter(splitter, rect);
        }

        private void DrawTimelineSplitter(Rect splitter, Rect parent)
        {
            EditorGUIUtility.AddCursorRect(splitter, MouseCursor.ResizeVertical);
            EditorGUI.DrawRect(
                splitter,
                timelineSplitterDragging
                    ? new Color(0.28f, 0.58f, 0.95f, 0.95f)
                    : new Color(0.20f, 0.23f, 0.27f, 1f));

            Event current = Event.current;
            if (current == null)
            {
                return;
            }

            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                splitter.Contains(current.mousePosition))
            {
                timelineSplitterDragging = true;
                GUIUtility.hotControl = TimelineSplitterControlHint;
                current.Use();
                return;
            }

            if (GUIUtility.hotControl != TimelineSplitterControlHint)
            {
                return;
            }

            if (current.type == EventType.MouseDrag)
            {
                SetTimelineHeight(current.mousePosition.y - parent.y - 8f, parent);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp ||
                current.type == EventType.Ignore)
            {
                timelineSplitterDragging = false;
                GUIUtility.hotControl = 0;
                current.Use();
                Repaint();
            }
        }

        private void SetTimelineHeight(float value, Rect parent)
        {
            float availableHeight = Mathf.Max(
                MinimumTimelineHeight + MinimumPreviewHeight + SplitterWidth,
                parent.height - 16f);
            float maximumTimelineHeight = Mathf.Max(
                MinimumTimelineHeight,
                availableHeight - MinimumPreviewHeight - SplitterWidth);
            timelineHeight = Mathf.Clamp(
                value,
                MinimumTimelineHeight,
                maximumTimelineHeight);
        }
        /// <summary>
        /// 绘制时间轴。
        /// </summary>

        private void DrawTimeline(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.045f, 0.058f, 0.075f, 1f));
            Rect content = new Rect(rect.x + 112f, rect.y, rect.width - 120f, rect.height);
            double duration = Math.Max(FrameSeconds(), CurrentPreviewDuration());
            DrawRuler(content, duration);
            Rect stageLane = LaneRect(content, 0);
            Rect stepLane = LaneRect(content, 1);
            Rect lifeLane = LaneRect(content, 2);
            Rect keyLane = LaneRect(content, 3);
            Rect movementLane = LaneRect(content, 4);
            DrawLaneLabel(rect, stageLane, "动作阶段");
            DrawLaneLabel(rect, stepLane, "executionStep");
            DrawLaneLabel(rect, lifeLane, "攻击体存活");
            DrawLaneLabel(rect, keyLane, "transform 关键帧");
            DrawLaneLabel(rect, movementLane, "位移关键帧");
            DrawStageLane(stageLane, duration);
            DrawStepLane(stepLane, duration);
            DrawLifetimeLane(lifeLane, duration);
            DrawKeyframeLane(keyLane, duration);
            DrawMovementLane(movementLane, duration);

            float playheadX = TimeToX(playheadSeconds, content, duration);
            Handles.BeginGUI();
            Handles.color = new Color(1f, 0.92f, 0.25f, 0.95f);
            Handles.DrawAAPolyLine(
                2f,
                new Vector3(playheadX, content.y),
                new Vector3(playheadX, content.yMax));
            Handles.EndGUI();
            GUI.Label(
                new Rect(playheadX + 3f, content.y + 2f, 70f, 18f),
                (playheadSeconds * authoringState.frameRate).ToString("0.###", CultureInfo.InvariantCulture),
                EditorStyles.miniLabel);
            HandleTimelineInput(
                content,
                stageLane,
                stepLane,
                keyLane,
                movementLane,
                duration);
        }
        /// <summary>
        /// 根据 `content`、`index` 构建轨道矩形并返回结果。
        /// </summary>

        private static Rect LaneRect(Rect content, int index)
        {
            return new Rect(
                content.x,
                content.y + RulerHeight + index * LaneHeight,
                content.width,
                LaneHeight - 2f);
        }
        /// <summary>
        /// 绘制轨道标签。
        /// </summary>

        private static void DrawLaneLabel(Rect outer, Rect lane, string label)
        {
            GUI.Label(
                new Rect(outer.x + 8f, lane.y + 10f, 98f, 20f),
                label,
                EditorStyles.miniBoldLabel);
        }
        /// <summary>
        /// 绘制`Ruler`。
        /// </summary>

        private static void DrawRuler(Rect content, double duration)
        {
            EditorGUI.DrawRect(
                new Rect(content.x, content.y, content.width, RulerHeight),
                new Color(0.08f, 0.095f, 0.12f, 1f));
            int divisions = Mathf.Clamp(Mathf.RoundToInt(content.width / 100f), 2, 12);
            Handles.BeginGUI();
            for (int index = 0; index <= divisions; index++)
            {
                float x = Mathf.Lerp(content.x, content.xMax, index / (float)divisions);
                Handles.color = new Color(0.45f, 0.5f, 0.58f, 0.65f);
                Handles.DrawLine(
                    new Vector3(x, content.yMax),
                    new Vector3(x, content.y + RulerHeight - 6f));
                GUI.Label(
                    new Rect(x + 2f, content.y + 2f, 72f, 18f),
                    (duration * index / divisions).ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                    EditorStyles.miniLabel);
            }

            Handles.EndGUI();
        }
        /// <summary>
        /// 绘制关卡轨道。
        /// </summary>

        private void DrawStageLane(Rect lane, double duration)
        {
            EditorGUI.DrawRect(lane, new Color(0.06f, 0.075f, 0.095f, 1f));
            if (!TryGetSelectedAction(
                Document,
                out BattleActionTimelineTableData table,
                out BattleActionTimelineRecordData action))
            {
                return;
            }

            BattleActionTimelinePhaseTimes phases =
                BattleActionTimelineSchema.ReadPhases(table, action);
            DrawTimeBand(lane, 0d, phases.StartupEnd, duration,
                new Color(0.88f, 0.48f, 0.18f, 0.72f), "前摇");
            DrawTimeBand(lane, phases.StartupEnd, phases.SwitchWindowStart, duration,
                new Color(0.75f, 0.25f, 0.22f, 0.72f), "锁定");
            DrawTimeBand(lane, phases.SwitchWindowStart, phases.RecoveryStart, duration,
                new Color(0.2f, 0.65f, 0.88f, 0.72f), "切换窗");
            DrawTimeBand(lane, phases.RecoveryStart, phases.Duration, duration,
                new Color(0.35f, 0.42f, 0.52f, 0.72f), "后摇");
            DrawMarker(lane, phases.StartupEnd, duration, Color.yellow);
            DrawMarker(lane, phases.SwitchWindowStart, duration, Color.cyan);
            DrawMarker(lane, phases.RecoveryStart, duration, Color.magenta);
            DrawMarker(lane, phases.Duration, duration, Color.white);
        }
        /// <summary>
        /// 绘制时间`Band`。
        /// </summary>

        private static void DrawTimeBand(
            Rect lane,
            double start,
            double end,
            double duration,
            Color color,
            string label)
        {
            float xMin = TimeToX(start, lane, duration);
            float xMax = TimeToX(end, lane, duration);
            Rect band = Rect.MinMaxRect(xMin, lane.y + 5f, xMax, lane.yMax - 5f);
            EditorGUI.DrawRect(band, color);
            if (band.width > 30f)
            {
                GUI.Label(band, label, EditorStyles.centeredGreyMiniLabel);
            }
        }
        /// <summary>
        /// 绘制步骤轨道。
        /// </summary>

        private void DrawStepLane(Rect lane, double duration)
        {
            EditorGUI.DrawRect(lane, new Color(0.055f, 0.07f, 0.09f, 1f));
            if (!TryGetSelectedAction(Document, out _, out BattleActionTimelineRecordData action))
            {
                return;
            }

            foreach (BattleActionTimelineTableData table in Document.tables
                .Where(BattleActionTimelineSchema.IsExecutionStepTable))
            {
                foreach (BattleActionTimelineRecordData step in table.records.Where(
                    value => BattleActionTimelineSchema.GetInt(
                        table,
                        value,
                        0,
                        BattleActionTimelineSchema.StepOwnerAliases) == action.rowId))
                {
                    double time = BattleActionTimelineSchema.GetDouble(
                        table,
                        step,
                        0d,
                        BattleActionTimelineSchema.TriggerTimeAliases);
                    bool selected = string.Equals(
                        table.sheetName,
                        authoringState.selectedStepSheet,
                        StringComparison.Ordinal) &&
                        step.rowId == authoringState.selectedStepRowId;
                    DrawDiamond(
                        new Vector2(TimeToX(time, lane, duration), lane.center.y),
                        selected ? Color.Lerp(
                            InteractionTypeColor(BattleActionTimelineSchema.GetInteractionTypeIndex(table, step)),
                            Color.white,
                            0.45f) : InteractionTypeColor(
                                BattleActionTimelineSchema.GetInteractionTypeIndex(table, step)),
                        selected ? 7f : 5f);
                    GUI.Label(
                        new Rect(TimeToX(time, lane, duration) + 6f, lane.y + 3f, 58f, 18f),
                        step.rowId.ToString(CultureInfo.InvariantCulture),
                        EditorStyles.miniLabel);
                }
            }
        }
        /// <summary>
        /// 绘制生命周期轨道。
        /// </summary>

        private void DrawLifetimeLane(Rect lane, double duration)
        {
            EditorGUI.DrawRect(lane, new Color(0.06f, 0.075f, 0.095f, 1f));
            if (!TryGetSelectedAction(Document, out _, out BattleActionTimelineRecordData action))
            {
                return;
            }

            foreach (BattleActionTimelineTableData table in Document.tables
                .Where(BattleActionTimelineSchema.IsExecutionStepTable))
            {
                foreach (BattleActionTimelineRecordData step in table.records.Where(
                    value => BattleActionTimelineSchema.GetInt(
                        table,
                        value,
                        0,
                        BattleActionTimelineSchema.StepOwnerAliases) == action.rowId))
                {
                    ResolvedStep resolved = ResolveStep(Document, table, step);
                    if (resolved == null || resolved.Owner == null || resolved.Lifetime <= 0d)
                    {
                        continue;
                    }

                    float xMin = TimeToX(resolved.TriggerTime, lane, duration);
                    float xMax = TimeToX(
                        Math.Min(duration, resolved.TriggerTime + resolved.Lifetime),
                        lane,
                        duration);
                    Rect bar = Rect.MinMaxRect(
                        xMin,
                        lane.y + 10f,
                        xMax,
                        lane.yMax - 9f);
                    Color interactionColor = InteractionTypeColor(resolved.InteractionTypeIndex);
                    interactionColor.a = 0.72f;
                    EditorGUI.DrawRect(bar, interactionColor);
                    if (bar.width > 42f)
                    {
                        GUI.Label(
                            bar,
                            InteractionTypeLabel(resolved.InteractionTypeIndex) + " " +
                            resolved.Owner.rowId,
                            EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }

        }
        /// <summary>
        /// 绘制关键帧轨道。
        /// </summary>

        private void DrawKeyframeLane(Rect lane, double duration)
        {
            EditorGUI.DrawRect(lane, new Color(0.055f, 0.07f, 0.09f, 1f));
            ResolvedStep resolved = ResolveSelectedStep(Document);
            if (resolved == null || resolved.KeyframeTable == null)
            {
                return;
            }

            foreach (BattleActionTimelineRecordData keyframe in resolved.Keyframes)
            {
                double local = BattleActionTimelineSchema.GetDouble(
                    resolved.KeyframeTable,
                    keyframe,
                    0d,
                    BattleActionTimelineSchema.LocalTimeAliases);
                double absolute = resolved.TriggerTime + local;
                bool selected = string.Equals(
                    authoringState.selectedSharedSheet,
                    resolved.KeyframeTable.sheetName,
                    StringComparison.Ordinal) &&
                    authoringState.selectedSharedRowId == keyframe.rowId;
                DrawDiamond(
                    new Vector2(TimeToX(absolute, lane, duration), lane.center.y),
                    selected ? Color.white : new Color(0.76f, 0.45f, 1f, 1f),
                    selected ? 7f : 5f);
            }
        }

        /// <summary>
        /// 绘制当前 ActiveSingle 的动作位移关键帧轨道。它与 Shape 关键帧分开，
        /// 时间从动作开始计算，点的位置对应累计 X/Y 位移。
        /// </summary>
        private void DrawMovementLane(Rect lane, double duration)
        {
            EditorGUI.DrawRect(lane, new Color(0.055f, 0.07f, 0.09f, 1f));
            if (!TryGetSelectedAction(
                Document,
                out BattleActionTimelineTableData table,
                out BattleActionTimelineRecordData action))
            {
                return;
            }

            string source = BattleActionTimelineSchema.Get(
                table,
                action,
                BattleActionTimelineSchema.MovementKeyframesAliases);
            if (!BattleActionTimelineInlineStructCodec.TryParse(
                    source,
                    out List<Dictionary<string, string>> values,
                    out _)
                || values.Count == 0)
            {
                return;
            }

            Vector3 previous = default(Vector3);
            bool hasPrevious = false;
            for (int index = 0; index < values.Count; index++)
            {
                double localTime = ReadInlineDouble(values[index], "LocalTime", 0d);
                float x = TimeToX(localTime, lane, duration);
                Vector3 point = new Vector3(x, lane.center.y);
                if (hasPrevious)
                {
                    Handles.BeginGUI();
                    Handles.color = new Color(0.25f, 0.85f, 0.95f, 0.75f);
                    Handles.DrawAAPolyLine(2f, previous, point);
                    Handles.EndGUI();
                }

                bool selected = dragKind == DragKind.MovementKeyframe &&
                    dragSheet == table.sheetName &&
                    dragRowId == action.rowId &&
                    dragMovementKeyframeIndex == index;
                DrawDiamond(
                    point,
                    selected ? Color.white : new Color(0.2f, 0.85f, 0.95f, 1f),
                    selected ? 7f : 5f);
                GUI.Label(
                    new Rect(x + 6f, lane.y + 3f, 56f, 18f),
                    "F" + BattleActionTimelineTime.SecondsToFrame(
                        Math.Max(0d, localTime),
                        authoringState.frameRate).ToString(CultureInfo.InvariantCulture),
                    EditorStyles.miniLabel);
                previous = point;
                hasPrevious = true;
            }
        }
        /// <summary>
        /// 绘制标记。
        /// </summary>

        private static void DrawMarker(Rect lane, double time, double duration, Color color)
        {
            float x = TimeToX(time, lane, duration);
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAPolyLine(
                2f,
                new Vector3(x, lane.y + 2f),
                new Vector3(x, lane.yMax - 2f));
            Handles.EndGUI();
        }
        /// <summary>
        /// 绘制`Diamond`。
        /// </summary>

        private static void DrawDiamond(Vector2 center, Color color, float radius)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Vector3[] points =
            {
                new Vector3(center.x, center.y - radius),
                new Vector3(center.x + radius, center.y),
                new Vector3(center.x, center.y + radius),
                new Vector3(center.x - radius, center.y),
            };
            Handles.DrawAAConvexPolygon(points);
            Handles.EndGUI();
        }
        /// <summary>
        /// 处理时间轴输入。
        /// </summary>

        private void HandleTimelineInput(
            Rect content,
            Rect stageLane,
            Rect stepLane,
            Rect keyLane,
            Rect movementLane,
            double duration)
        {
            // 预览已捕获鼠标时，不能被上方时间轴当成时间拖拽消费。
            if (dragKind == DragKind.PreviewShapeGizmo) return;
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 &&
                content.Contains(current.mousePosition))
            {
                playing = false;
                dragChanged = false;
                if (stageLane.Contains(current.mousePosition) &&
                    TryBeginStageDrag(current.mousePosition.x, stageLane, duration))
                {
                    Undo.RegisterCompleteObjectUndo(
                        authoringState,
                        "拖动动作阶段时间");
                }
                else if (stepLane.Contains(current.mousePosition) &&
                    TryBeginStepDrag(current.mousePosition.x, stepLane, duration))
                {
                    Undo.RegisterCompleteObjectUndo(
                        authoringState,
                        "拖动 executionStep");
                }
                else if (keyLane.Contains(current.mousePosition) &&
                    TryBeginKeyframeDrag(current.mousePosition.x, keyLane, duration))
                {
                    Undo.RegisterCompleteObjectUndo(
                        authoringState,
                        "拖动 transform keyframe");
                }
                else if (movementLane.Contains(current.mousePosition) &&
                    TryBeginMovementKeyframeDrag(
                        current.mousePosition.x,
                        movementLane,
                        duration))
                {
                    Undo.RegisterCompleteObjectUndo(
                        authoringState,
                        "拖动位移 keyframe");
                }
                else
                {
                    dragKind = DragKind.Playhead;
                    playheadSeconds = XToTime(current.mousePosition.x, content, duration);
                    playheadSeconds = SnapAndClamp(playheadSeconds, duration);
                }

                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 &&
                dragKind != DragKind.None)
            {
                double time = SnapAndClamp(
                    XToTime(current.mousePosition.x, content, duration),
                    duration);
                if (dragKind == DragKind.Playhead)
                {
                    playheadSeconds = time;
                }
                else
                {
                    ApplyTimelineDrag(time);
                    dragChanged = true;
                }

                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 &&
                dragKind != DragKind.None)
            {
                if (dragChanged)
                {
                    NormalizeStepOrderForSelectedAction();
                    NormalizeKeyframeOrder();
                    NormalizeMovementKeyframeOrder();
                    OnAuthoringChanged();
                }

                CancelDrag();
                current.Use();
            }
        }
        /// <summary>
        /// 根据 `x`、`lane`、`duration` 开始关卡拖拽；操作完成时返回 `true`，前置条件不满足时返回 `false`。
        /// </summary>

        private bool TryBeginStageDrag(float x, Rect lane, double duration)
        {
            if (!TryGetSelectedAction(
                Document,
                out BattleActionTimelineTableData table,
                out BattleActionTimelineRecordData action))
            {
                return false;
            }

            BattleActionTimelinePhaseTimes phases =
                BattleActionTimelineSchema.ReadPhases(table, action);
            (DragKind kind, double time)[] markers =
            {
                (DragKind.StartupEnd, phases.StartupEnd),
                (DragKind.SwitchStart, phases.SwitchWindowStart),
                (DragKind.RecoveryStart, phases.RecoveryStart),
                (DragKind.Duration, phases.Duration),
            };
            (DragKind kind, double time) nearest = markers
                .OrderBy(marker => Math.Abs(TimeToX(marker.time, lane, duration) - x))
                .First();
            if (Math.Abs(TimeToX(nearest.time, lane, duration) - x) > 9f)
            {
                return false;
            }

            dragKind = nearest.kind;
            dragSheet = table.sheetName;
            dragRowId = action.rowId;
            return true;
        }
        /// <summary>
        /// 根据 `x`、`lane`、`duration` 开始步骤拖拽；操作完成时返回 `true`，前置条件不满足时返回 `false`。
        /// </summary>

        private bool TryBeginStepDrag(float x, Rect lane, double duration)
        {
            if (!TryGetSelectedAction(Document, out _, out BattleActionTimelineRecordData action))
            {
                return false;
            }

            BattleActionTimelineTableData nearestTable = null;
            BattleActionTimelineRecordData nearestStep = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (BattleActionTimelineTableData table in Document.tables
                .Where(BattleActionTimelineSchema.IsExecutionStepTable))
            {
                foreach (BattleActionTimelineRecordData step in table.records.Where(
                    value => BattleActionTimelineSchema.GetInt(
                        table,
                        value,
                        0,
                        BattleActionTimelineSchema.StepOwnerAliases) == action.rowId))
                {
                    double time = BattleActionTimelineSchema.GetDouble(
                        table,
                        step,
                        0d,
                        BattleActionTimelineSchema.TriggerTimeAliases);
                    float distance = Math.Abs(TimeToX(time, lane, duration) - x);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestTable = table;
                        nearestStep = step;
                    }
                }
            }

            if (nearestStep == null || nearestDistance > 10f)
            {
                return false;
            }

            SelectStep(nearestTable.sheetName, nearestStep.rowId);
            dragKind = DragKind.Step;
            dragSheet = nearestTable.sheetName;
            dragRowId = nearestStep.rowId;
            return true;
        }
        /// <summary>
        /// 根据 `x`、`lane`、`duration` 开始关键帧拖拽；操作完成时返回 `true`，前置条件不满足时返回 `false`。
        /// </summary>

        private bool TryBeginKeyframeDrag(float x, Rect lane, double duration)
        {
            ResolvedStep resolved = ResolveSelectedStep(Document);
            if (resolved == null || resolved.KeyframeTable == null ||
                resolved.Keyframes.Count == 0)
            {
                return false;
            }

            BattleActionTimelineRecordData nearest = resolved.Keyframes
                .OrderBy(keyframe => Math.Abs(
                    TimeToX(
                        resolved.TriggerTime + BattleActionTimelineSchema.GetDouble(
                            resolved.KeyframeTable,
                            keyframe,
                            0d,
                            BattleActionTimelineSchema.LocalTimeAliases),
                        lane,
                        duration) - x))
                .First();
            double absolute = resolved.TriggerTime +
                BattleActionTimelineSchema.GetDouble(
                    resolved.KeyframeTable,
                    nearest,
                    0d,
                    BattleActionTimelineSchema.LocalTimeAliases);
            if (Math.Abs(TimeToX(absolute, lane, duration) - x) > 10f)
            {
                return false;
            }

            SelectShared(resolved.KeyframeTable.sheetName, nearest.rowId);
            dragKind = DragKind.Keyframe;
            dragSheet = resolved.KeyframeTable.sheetName;
            dragRowId = nearest.rowId;
            return true;
        }

        private bool TryBeginMovementKeyframeDrag(
            float x,
            Rect lane,
            double duration)
        {
            if (!TryGetSelectedAction(
                Document,
                out BattleActionTimelineTableData table,
                out BattleActionTimelineRecordData action))
            {
                return false;
            }

            string source = BattleActionTimelineSchema.Get(
                table,
                action,
                BattleActionTimelineSchema.MovementKeyframesAliases);
            if (!BattleActionTimelineInlineStructCodec.TryParse(
                    source,
                    out List<Dictionary<string, string>> values,
                    out _)
                || values.Count == 0)
            {
                return false;
            }

            int nearestIndex = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < values.Count; index++)
            {
                double localTime = ReadInlineDouble(values[index], "LocalTime", 0d);
                float distance = Math.Abs(TimeToX(localTime, lane, duration) - x);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = index;
                }
            }

            if (nearestIndex < 0 || nearestDistance > 10f)
            {
                return false;
            }

            dragKind = DragKind.MovementKeyframe;
            dragSheet = table.sheetName;
            dragRowId = action.rowId;
            dragMovementKeyframeIndex = nearestIndex;
            return true;
        }
        /// <summary>
        /// 应用时间轴拖拽。
        /// </summary>

        private void ApplyTimelineDrag(double absoluteTime)
        {
            if (dragKind == DragKind.MovementKeyframe)
            {
                BattleActionTimelineTableData actionTable = Document.FindTable(dragSheet);
                BattleActionTimelineRecordData action = actionTable?.records.FirstOrDefault(
                    value => value.rowId == dragRowId);
                if (action == null || dragMovementKeyframeIndex < 0)
                {
                    return;
                }

                string source = BattleActionTimelineSchema.Get(
                    actionTable,
                    action,
                    BattleActionTimelineSchema.MovementKeyframesAliases);
                if (!BattleActionTimelineInlineStructCodec.TryParse(
                        source,
                        out List<Dictionary<string, string>> values,
                        out _)
                    || dragMovementKeyframeIndex >= values.Count)
                {
                    return;
                }

                double actionDuration = BattleActionTimelineSchema.ReadPhases(
                    actionTable,
                    action).Duration;
                double localTime = Math.Max(
                    0d,
                    Math.Min(actionDuration, absoluteTime));
                values[dragMovementKeyframeIndex]["LocalTime"] =
                    localTime.ToString(CultureInfo.InvariantCulture);
                SetMovementKeyframes(actionTable, action, values);
                return;
            }

            BattleActionTimelineTableData table = Document.FindTable(dragSheet);
            BattleActionTimelineRecordData record = table?.records.FirstOrDefault(
                value => value.rowId == dragRowId);
            if (record == null)
            {
                return;
            }

            if (dragKind == DragKind.Step)
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    record,
                    absoluteTime,
                    BattleActionTimelineSchema.TriggerTimeAliases);
                return;
            }

            if (dragKind == DragKind.Keyframe)
            {
                ResolvedStep resolved = ResolveSelectedStep(Document);
                double local = resolved == null
                    ? 0d
                    : Math.Max(
                        0d,
                        Math.Min(resolved.Lifetime, absoluteTime - resolved.TriggerTime));
                BattleActionTimelineSchema.SetDouble(
                    table,
                    record,
                    local,
                    BattleActionTimelineSchema.LocalTimeAliases);
                return;
            }

            BattleActionTimelinePhaseTimes phases =
                BattleActionTimelineSchema.ReadPhases(table, record);
            switch (dragKind)
            {
                case DragKind.StartupEnd:
                    BattleActionTimelineSchema.SetDouble(
                        table,
                        record,
                        Math.Min(absoluteTime, phases.SwitchWindowStart),
                        BattleActionTimelineSchema.StartupEndAliases);
                    break;
                case DragKind.SwitchStart:
                    BattleActionTimelineSchema.SetDouble(
                        table,
                        record,
                        Math.Max(
                            phases.StartupEnd,
                            Math.Min(absoluteTime, phases.RecoveryStart)),
                        BattleActionTimelineSchema.SwitchStartAliases);
                    break;
                case DragKind.RecoveryStart:
                    BattleActionTimelineSchema.SetDouble(
                        table,
                        record,
                        Math.Max(
                            phases.SwitchWindowStart,
                            Math.Min(absoluteTime, phases.Duration)),
                        BattleActionTimelineSchema.RecoveryStartAliases);
                    break;
                case DragKind.Duration:
                    BattleActionTimelineSchema.SetDouble(
                        table,
                        record,
                        Math.Max(phases.RecoveryStart, absoluteTime),
                        BattleActionTimelineSchema.DurationAliases);
                    break;
            }
        }
        /// <summary>
        /// 绘制预览。
        /// </summary>

        private void DrawPreview(Rect rect)
        {
            // 固定预览视口，角色和攻击体都不能绘制/拖拽到时间轴或检视面板上。
            GUI.BeginGroup(rect);
            try
            {
                DrawPreviewContents(new Rect(0f, 0f, rect.width, rect.height));
            }
            finally
            {
                GUI.EndGroup();
            }
        }

        private void DrawPreviewContents(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.035f, 0.045f, 0.06f, 1f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            float pixelsPerUnit = Mathf.Clamp(
                Mathf.Min(rect.width / 6f, (rect.height - 60f) /
                    (authoringState.previewHeight * 2f)),
                24f,
                240f) * authoringState.previewZoom;
            Vector2 origin = new Vector2(rect.center.x, rect.center.y + rect.height * 0.18f);
            DrawPreviewGrid(rect, origin, pixelsPerUnit);
            Vector2 actionMovementOffset = EvaluateActionMovementOffset(
                Document,
                playheadSeconds);
            int facingSign = authoringState.facing ==
                (int)BattleActionTimelineFacing.Left ? -1 : 1;
            Vector2 displayMovementOffset = actionMovementOffset;
            displayMovementOffset.x *= facingSign;
            Vector2 movedOrigin = origin + new Vector2(
                displayMovementOffset.x * pixelsPerUnit,
                -displayMovementOffset.y * pixelsPerUnit);
            DrawActionMovementPath(
                rect,
                origin,
                pixelsPerUnit,
                CurrentDuration(Document),
                Document);
            DrawSequencePreview(rect, movedOrigin, pixelsPerUnit);
            GUI.Label(
                new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 20f),
                "序列帧 + CombatOrigin 判定预览 · " +
                (authoringState.facing == (int)BattleActionTimelineFacing.Left
                    ? "朝左镜像"
                    : "朝右配置") +
                " · 颜色：普通绿 / 冲击红 / 防御蓝 / 蓄力紫" +
                " · 青线为动作位移轨迹 · 点选形状后用手柄编辑",
                EditorStyles.whiteLabel);

            ResolvedStep resolved = ResolveSelectedStep(Document);
            if (resolved == null || resolved.Body == null)
            {
                DrawPreviewShapeToolbar(rect, resolved);
                GUI.Label(
                    new Rect(rect.x + 12f, rect.yMax - 40f, rect.width - 24f, 36f),
                    "选择 Melee/Projectile executionStep 后显示组合攻击形状。",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            double elapsed = playheadSeconds - resolved.TriggerTime;
            bool active = BattleActionTimelinePreviewLifetime.IsActive(elapsed, resolved.Lifetime);
            bool visible = BattleActionTimelinePreviewLifetime.IsVisible(elapsed, resolved.Lifetime, showInactiveShapes);
            double localTime = Math.Max(0d, Math.Min(resolved.Lifetime, elapsed));
            Vector2 ownerOffset = actionMovementOffset;
            if (resolved.IsProjectile)
            {
                ownerOffset += ProjectilePreviewOffset(Document, resolved, localTime);
            }
            Vector2 previewOwnerOffset = ownerOffset;
            previewOwnerOffset.x *= facingSign;
            Vector2 bodyOrigin = origin + new Vector2(
                previewOwnerOffset.x * pixelsPerUnit,
                -previewOwnerOffset.y * pixelsPerUnit);

            List<PreviewShape> previews = new List<PreviewShape>();
            BattleActionTimelineTableData shapeTable = Document.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsShapeTable);
            if (shapeTable != null && visible)
            {
                foreach (BattleActionTimelineRecordData shape in shapeTable.records.Where(
                    value => BattleActionTimelineSchema.GetInt(
                        shapeTable,
                        value,
                        0,
                        BattleActionTimelineSchema.AttackBodyIdAliases) ==
                        resolved.Body.rowId))
                {
                    BattleActionTimelineTransform transform = EvaluateShapeTransform(
                        Document,
                        shape,
                        localTime);
                    PreviewShape preview = BuildPreviewShape(shapeTable, shape, bodyOrigin,
                        transform, facingSign, pixelsPerUnit);
                    previews.Add(preview);
                    DrawPreviewShape(preview, active ? 1f : 0.3f,
                        resolved.SelectedShape?.rowId == shape.rowId,
                        resolved.InteractionTypeIndex);
                }
            }

            if (visible)
                EditorGUI.DrawRect(new Rect(bodyOrigin.x - 4f, bodyOrigin.y - 4f, 8f, 8f),
                    new Color(1f, 1f, 1f, active ? 1f : 0.3f));
            Rect interaction = new Rect(rect.x, rect.y + 102f, rect.width, Math.Max(0f, rect.height - 130f));
            HandlePreviewShapeGizmos(interaction, previews, resolved, bodyOrigin, pixelsPerUnit, facingSign, active ? 1f : 0.3f);
            DrawPreviewShapeToolbar(rect, resolved);
            GUI.Label(
                new Rect(rect.x + 10f, rect.yMax - 25f, rect.width - 20f, 18f),
                (active ? "有效判定" : elapsed < 0d ? "尚未生成" : "已失效") +
                (!active ? visible ? "（仅编辑预览）" : "（已隐藏）" : string.Empty) +
                " · localTime=" + elapsed.ToString("0.###", CultureInfo.InvariantCulture) +
                " · AttackBody=" + resolved.Body.rowId +
                " · " + InteractionTypeLabel(resolved.InteractionTypeIndex) +
                (previewEditKeyframes ? " · 正在编辑所选关键帧" : " · 正在编辑 Shape 本体（缩放写入尺寸）"),
                EditorStyles.miniLabel);
        }
        /// <summary>
        /// 绘制预览网格。
        /// </summary>

        private static void DrawPreviewGrid(
            Rect rect,
            Vector2 origin,
            float pixelsPerUnit)
        {
            Handles.BeginGUI();
            Handles.color = new Color(0.18f, 0.23f, 0.3f, 0.6f);
            for (float x = origin.x; x <= rect.xMax; x += pixelsPerUnit)
            {
                Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
            }

            for (float x = origin.x - pixelsPerUnit; x >= rect.x; x -= pixelsPerUnit)
            {
                Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
            }

            for (float y = origin.y; y <= rect.yMax; y += pixelsPerUnit)
            {
                Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
            }

            for (float y = origin.y - pixelsPerUnit; y >= rect.y; y -= pixelsPerUnit)
            {
                Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
            }

            Handles.color = new Color(0.45f, 0.9f, 0.55f, 0.9f);
            Handles.DrawAAPolyLine(
                2f,
                new Vector3(rect.x, origin.y),
                new Vector3(rect.xMax, origin.y));
            Handles.color = new Color(0.5f, 0.65f, 1f, 0.9f);
            Handles.DrawAAPolyLine(
                2f,
                new Vector3(origin.x, rect.y),
                new Vector3(origin.x, rect.yMax));
            Handles.EndGUI();
        }

        private Vector2 EvaluateActionMovementOffset(
            BattleActionTimelineDocument document,
            double actionTime)
        {
            if (document == null || !TryGetSelectedAction(
                    document,
                    out BattleActionTimelineTableData table,
                    out BattleActionTimelineRecordData action))
            {
                return Vector2.zero;
            }

            BattleActionTimelinePhaseTimes phases =
                BattleActionTimelineSchema.ReadPhases(table, action);
            double clock = Math.Max(
                0d,
                Math.Min(phases.Duration, actionTime));
            string source = BattleActionTimelineSchema.Get(
                table,
                action,
                BattleActionTimelineSchema.MovementKeyframesAliases);
            if (!BattleActionTimelineInlineStructCodec.TryParse(
                    source,
                    out List<Dictionary<string, string>> values,
                    out _)
                || values.Count == 0)
            {
                double ratio = phases.Duration <= 0d ? 1d : clock / phases.Duration;
                return new Vector2(
                    (float)(BattleActionTimelineSchema.GetDouble(
                        table,
                        action,
                        0d,
                        BattleActionTimelineSchema.MovementOffsetXAliases) * ratio),
                    (float)(BattleActionTimelineSchema.GetDouble(
                        table,
                        action,
                        0d,
                        BattleActionTimelineSchema.MovementOffsetYAliases) * ratio));
            }

            values = values
                .OrderBy(value => ReadInlineDouble(value, "LocalTime", 0d))
                .ToList();
            double firstTime = ReadInlineDouble(values[0], "LocalTime", 0d);
            if (clock <= firstTime)
            {
                double ratio = firstTime <= 0d ? 1d : clock / firstTime;
                ratio = ApplyMovementInterpolation(
                    ratio,
                    ReadInline(values[0], "Interpolation", "Linear"));
                return ReadMovementOffset(values[0], ratio, Vector2.zero);
            }

            for (int index = 1; index < values.Count; index++)
            {
                double nextTime = ReadInlineDouble(values[index], "LocalTime", 0d);
                if (clock <= nextTime)
                {
                    Dictionary<string, string> previous = values[index - 1];
                    double previousTime = ReadInlineDouble(previous, "LocalTime", 0d);
                    double span = nextTime - previousTime;
                    double ratio = span <= 0d ? 1d :
                        (clock - previousTime) / span;
                    ratio = ApplyMovementInterpolation(
                        ratio,
                        ReadInline(previous, "Interpolation", "Linear"));
                    Vector2 start = ReadMovementOffset(previous, 1d, Vector2.zero);
                    Vector2 end = ReadMovementOffset(values[index], 1d, Vector2.zero);
                    return Vector2.LerpUnclamped(start, end, (float)ratio);
                }
            }

            return ReadMovementOffset(values[values.Count - 1], 1d, Vector2.zero);
        }

        private static Vector2 ReadMovementOffset(
            IReadOnlyDictionary<string, string> value,
            double scale,
            Vector2 fallback)
        {
            if (value == null ||
                !double.TryParse(
                    ReadInline(value, "OffsetX", string.Empty),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double x) ||
                !double.TryParse(
                    ReadInline(value, "OffsetY", string.Empty),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double y))
            {
                return fallback;
            }

            return new Vector2((float)(x * scale), (float)(y * scale));
        }

        private static double ApplyMovementInterpolation(
            double normalizedTime,
            string interpolation)
        {
            return BattleActionTimelineSchema.ApplyInterpolation(
                (float)normalizedTime,
                interpolation);
        }

        private void DrawActionMovementPath(
            Rect rect,
            Vector2 origin,
            float pixelsPerUnit,
            double duration,
            BattleActionTimelineDocument document)
        {
            if (duration <= 0d)
            {
                return;
            }

            Handles.BeginGUI();
            Handles.color = new Color(0.25f, 0.85f, 0.95f, 0.7f);
            Vector3[] points = new Vector3[33];
            for (int index = 0; index < points.Length; index++)
            {
                double time = duration * index / (points.Length - 1d);
                Vector2 offset = EvaluateActionMovementOffset(
                    document,
                    time);
                points[index] = new Vector3(
                    origin.x + offset.x * pixelsPerUnit,
                    origin.y - offset.y * pixelsPerUnit);
            }

            Handles.DrawAAPolyLine(2f, points);
            Handles.EndGUI();
        }
        /// <summary>
        /// 绘制预览形状。
        /// </summary>

        private static PreviewShape BuildPreviewShape(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData shape,
            Vector2 bodyOrigin,
            BattleActionTimelineTransform transform,
            int facingSign,
            float pixelsPerUnit)
        {
            Vector2 localOffset = new Vector2(
                (float)BattleActionTimelineSchema.GetDouble(
                    table,
                    shape,
                    0d,
                    "offsetX",
                    "localOffsetX"),
                (float)BattleActionTimelineSchema.GetDouble(
                    table,
                    shape,
                    0d,
                    "offsetY",
                    "localOffsetY"));
            Vector2 center = bodyOrigin + BattleActionTimelineShapeGizmoMath.ToScreen(
                BattleActionTimelineShapeGizmoMath.Center(localOffset, transform), pixelsPerUnit, facingSign);
            float localRotation = (float)BattleActionTimelineSchema.GetDouble(
                table,
                shape,
                0d,
                "rotationDegrees",
                "localRotationDegrees");
            string type = BattleActionTimelineSchema.Get(table, shape, "shapeType");
            bool circle = type.IndexOf("Circle", StringComparison.OrdinalIgnoreCase) >= 0 || type == "1";
            bool capsule = type.IndexOf("Capsule", StringComparison.OrdinalIgnoreCase) >= 0 || type == "2";
            Vector2 size = new Vector2(
                (float)BattleActionTimelineSchema.GetDouble(
                    table,
                    shape,
                    1d,
                    "width",
                    "sizeX"),
                (float)BattleActionTimelineSchema.GetDouble(
                    table,
                    shape,
                    1d,
                    "height",
                    "sizeY"));
            if (circle)
                size = Vector2.one * (float)BattleActionTimelineSchema.GetDouble(table, shape, 0.5d, "radius") * 2f;
            string direction = BattleActionTimelineSchema.Get(table, shape, "capsuleDirection", "direction");
            return new PreviewShape
            {
                Table = table, Shape = shape, Transform = transform, Offset = localOffset,
                BaseSize = size, Center = center,
                Size = circle ? size * Mathf.Max(Mathf.Abs(transform.Scale.x), Mathf.Abs(transform.Scale.y)) * pixelsPerUnit
                    : Vector2.Scale(size, new Vector2(Mathf.Abs(transform.Scale.x), Mathf.Abs(transform.Scale.y))) * pixelsPerUnit,
                LocalRotation = localRotation, ScreenRotation = -(transform.RotationDegrees + localRotation) * facingSign,
                Type = circle ? BattleAttackShapeType.Circle : capsule ? BattleAttackShapeType.Capsule : BattleAttackShapeType.Box,
                Horizontal = direction.IndexOf("Horizontal", StringComparison.OrdinalIgnoreCase) >= 0 || direction == "1",
            };
        }

        private static void DrawPreviewShape(
            PreviewShape shape,
            float alpha,
            bool selected,
            int interactionTypeIndex)
        {
            Vector2 center = shape.Center, size = shape.Size;
            float rotation = shape.ScreenRotation;
            Color baseColor = InteractionTypeColor(interactionTypeIndex);
            Color fill = new Color(
                baseColor.r,
                baseColor.g,
                baseColor.b,
                (selected ? 0.3f : 0.2f) * alpha);
            Color outline = Color.Lerp(baseColor, Color.white, selected ? 0.45f : 0.08f);
            outline.a = (selected ? 1f : 0.9f) * alpha;
            if (shape.Type == BattleAttackShapeType.Circle)
            {
                Handles.BeginGUI();
                Handles.color = fill;
                Handles.DrawSolidDisc(center, Vector3.forward, size.x * 0.5f);
                Handles.color = outline;
                Handles.DrawWireDisc(center, Vector3.forward, size.x * 0.5f);
                Handles.EndGUI();
                return;
            }
            if (shape.Type == BattleAttackShapeType.Capsule)
            {
                Vector3[] capsulePoints = BuildCapsulePoints(size, shape.Horizontal)
                    .Select(value =>
                    {
                        Vector2 rotated = Rotate(value, rotation);
                        return new Vector3(
                            center.x + rotated.x,
                            center.y + rotated.y);
                    })
                    .ToArray();
                Handles.BeginGUI();
                Handles.color = fill;
                Handles.DrawAAConvexPolygon(capsulePoints);
                Handles.color = outline;
                Handles.DrawAAPolyLine(
                    2f,
                    capsulePoints.Concat(new[] { capsulePoints[0] }).ToArray());
                Handles.EndGUI();
                return;
            }

            Vector2[] corners =
            {
                new Vector2(-size.x, -size.y) * 0.5f,
                new Vector2(size.x, -size.y) * 0.5f,
                new Vector2(size.x, size.y) * 0.5f,
                new Vector2(-size.x, size.y) * 0.5f,
            };
            Vector3[] points = corners.Select(value =>
            {
                Vector2 rotated = Rotate(value, rotation);
                return new Vector3(center.x + rotated.x, center.y + rotated.y);
            }).ToArray();
            Handles.BeginGUI();
            Handles.color = fill;
            Handles.DrawAAConvexPolygon(points);
            Handles.color = outline;
            Handles.DrawAAPolyLine(
                2f,
                points[0], points[1], points[2], points[3], points[0]);
            Handles.EndGUI();
        }

        private static BattleActionTimelineTransform EvaluateShapeTransform(
            BattleActionTimelineDocument document,
            BattleActionTimelineRecordData shape,
            double localTime)
        {
            if (document == null || shape == null)
            {
                return BattleActionTimelineTransform.Identity;
            }

            BattleActionTimelineTableData keyframeTable = document.tables.FirstOrDefault(table =>
                BattleActionTimelineSchema.IsKeyframeTable(table) &&
                (table.sheetName.IndexOf("ShapeTransformKeyframe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 table.sheetName.IndexOf("AttackShape", StringComparison.OrdinalIgnoreCase) >= 0));
            if (keyframeTable == null)
            {
                return BattleActionTimelineTransform.Identity;
            }

            int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(keyframeTable);
            if (ownerColumn < 0)
            {
                return BattleActionTimelineTransform.Identity;
            }

            List<BattleActionTimelineRecordData> keyframes = keyframeTable.records.Where(record =>
                int.TryParse(
                    record.cells[ownerColumn],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int ownerId) && ownerId == shape.rowId).ToList();
            return BattleActionTimelineSchema.EvaluateKeyframes(
                keyframeTable,
                keyframes,
                localTime);
        }

        private static Color InteractionTypeColor(int index)
        {
            switch (Math.Max(0, Math.Min(3, index)))
            {
                case 1: return new Color(0.95f, 0.25f, 0.22f, 1f); // Impact
                case 2: return new Color(0.24f, 0.58f, 1f, 1f); // Defense
                case 3: return new Color(0.72f, 0.35f, 1f, 1f); // Charged
                default: return new Color(0.25f, 0.85f, 0.43f, 1f); // Normal
            }
        }

        private static string InteractionTypeLabel(int index)
        {
            switch (Math.Max(0, Math.Min(3, index)))
            {
                case 1: return "冲击体";
                case 2: return "防御体";
                case 3: return "蓄力体";
                default: return "普通攻击体";
            }
        }
        /// <summary>
        /// 构建胶囊体点位。
        /// </summary>

        private static Vector2[] BuildCapsulePoints(
            Vector2 size,
            bool horizontal)
        {
            const int HalfSegments = 12;
            List<Vector2> points = new List<Vector2>(HalfSegments * 2 + 2);
            float width = Mathf.Max(0f, Mathf.Abs(size.x));
            float height = Mathf.Max(0f, Mathf.Abs(size.y));
            if (horizontal)
            {
                float radius = Mathf.Min(width, height) * 0.5f;
                float halfLine = Mathf.Max(0f, width * 0.5f - radius);
                for (int index = 0; index <= HalfSegments; index++)
                {
                    float angle = Mathf.Lerp(-90f, 90f, index / (float)HalfSegments) *
                        Mathf.Deg2Rad;
                    points.Add(new Vector2(
                        halfLine + Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius));
                }

                for (int index = 0; index <= HalfSegments; index++)
                {
                    float angle = Mathf.Lerp(90f, 270f, index / (float)HalfSegments) *
                        Mathf.Deg2Rad;
                    points.Add(new Vector2(
                        -halfLine + Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius));
                }
            }
            else
            {
                float radius = Mathf.Min(width, height) * 0.5f;
                float halfLine = Mathf.Max(0f, height * 0.5f - radius);
                for (int index = 0; index <= HalfSegments; index++)
                {
                    float angle = Mathf.Lerp(0f, 180f, index / (float)HalfSegments) *
                        Mathf.Deg2Rad;
                    points.Add(new Vector2(
                        Mathf.Cos(angle) * radius,
                        halfLine + Mathf.Sin(angle) * radius));
                }

                for (int index = 0; index <= HalfSegments; index++)
                {
                    float angle = Mathf.Lerp(180f, 360f, index / (float)HalfSegments) *
                        Mathf.Deg2Rad;
                    points.Add(new Vector2(
                        Mathf.Cos(angle) * radius,
                        -halfLine + Mathf.Sin(angle) * radius));
                }
            }

            return points.ToArray();
        }
        private BattleActionTimelineRecordData SelectedPreviewKeyframe(ResolvedStep resolved) =>
            resolved?.KeyframeTable != null &&
            authoringState.selectedSharedSheet == resolved.KeyframeTable.sheetName
                ? resolved.Keyframes.FirstOrDefault(key => key.rowId == authoringState.selectedSharedRowId) : null;

        private void DrawPreviewShapeToolbar(Rect rect, ResolvedStep resolved)
        {
            EditorGUI.DrawRect(new Rect(1f, 28f, rect.width - 2f, 73f), new Color(0.09f, 0.11f, 0.14f, 0.98f));
            shapeGizmoTool = (ShapeGizmoTool)GUI.Toolbar(new Rect(10f, 31f, Math.Min(370f, rect.width - 20f), 21f),
                (int)shapeGizmoTool, new[] { "移动 W", "大小 T", "缩放 R", "旋转 E" });
            bool showInactive = GUI.Toggle(new Rect(390f, 31f, 160f, 21f), showInactiveShapes,
                new GUIContent("显示失效形状", "默认关闭。开启后可查看、编辑尚未生成或已结束的形状；不代表有效攻击判定。"));
            if (showInactive != showInactiveShapes)
            {
                showInactiveShapes = showInactive;
                Repaint();
            }
            using (new EditorGUI.DisabledScope(resolved?.SelectedShape == null || dragKind == DragKind.PreviewShapeGizmo))
            {
                int target = GUI.Toolbar(new Rect(10f, 56f, 200f, 20f),
                    previewEditKeyframes ? 1 : 0, new[] { "Shape 本体", "关键帧" });
                if ((target == 1) != previewEditKeyframes)
                {
                    previewEditKeyframes = target == 1;
                    if (!previewEditKeyframes)
                        SelectShared(resolved.ShapeTable.sheetName, resolved.SelectedShape.rowId);
                    else
                    {
                        double time = Math.Max(0d, Math.Min(resolved.Lifetime, playheadSeconds - resolved.TriggerTime));
                        var key = resolved.Keyframes.FirstOrDefault(value => Math.Abs(
                            BattleActionTimelineSchema.GetDouble(resolved.KeyframeTable, value, 0d,
                                BattleActionTimelineSchema.LocalTimeAliases) - time) < BattleActionTimelineTime.Epsilon);
                        if (key != null) SelectShared(resolved.KeyframeTable.sheetName, key.rowId);
                    }
                    Repaint();
                }
                if (GUI.Button(new Rect(218f, 56f, 135f, 20f), "+ 当前时间关键帧"))
                {
                    Undo.RegisterCompleteObjectUndo(authoringState, "添加预览 Shape 关键帧");
                    int count = resolved.KeyframeTable?.records.Count ?? 0;
                    double time = SnapNonNegative(Math.Max(0d, Math.Min(resolved.Lifetime,
                        playheadSeconds - resolved.TriggerTime)));
                    time = Math.Min(resolved.Lifetime, time);
                    var key = EnsurePreviewKeyframe(Document, resolved.SelectedShape.rowId, time);
                    if (key != null)
                    {
                        SelectStepChild(resolved, resolved.KeyframeTable, key);
                        if ((resolved.KeyframeTable?.records.Count ?? 0) != count)
                        {
                            NormalizeKeyframeOrder();
                            OnAuthoringChanged();
                        }
                    }
                }
                var selected = SelectedPreviewKeyframe(resolved);
                if (selected != null && GUI.Button(new Rect(361f, 56f, 82f, 20f), "定位关键帧"))
                    LocatePreviewKeyframe(resolved, selected);
            }
            var selectedKey = SelectedPreviewKeyframe(resolved);
            string label = resolved?.SelectedShape == null ? "先选择一个步骤，再点击预览中的形状。"
                : !previewEditKeyframes ? "编辑 Shape 本体 · 拖中心/箭头移动，边点改大小；缩放写入尺寸。"
                : selectedKey == null ? "关键帧模式：请在左侧/时间轴选择关键帧，或点击“当前时间关键帧”。"
                : "编辑 Shape " + (BattleActionTimelineSchema.GetInt(resolved.ShapeTable, resolved.SelectedShape, 0, "shapeIndex") + 1) +
                    " 的关键帧 @" + BattleActionTimelineSchema.GetDouble(resolved.KeyframeTable, selectedKey, 0d,
                        BattleActionTimelineSchema.LocalTimeAliases).ToString("0.###", CultureInfo.InvariantCulture) +
                    "s · 不在此帧时需点击“定位关键帧”；Shift 等比缩放。";
            if (resolved?.SelectedShape != null &&
                !BattleActionTimelinePreviewLifetime.IsActive(playheadSeconds - resolved.TriggerTime, resolved.Lifetime))
                label = showInactiveShapes ? "失效形状 · 仅编辑预览，不是有效判定。 " + label
                    : "当前不在攻击体存活区间，形状和手柄已隐藏；勾选“显示失效形状”可越界编辑。";
            GUI.Label(new Rect(10f, 79f, rect.width - 20f, 20f), new GUIContent(label, label), EditorStyles.miniLabel);
        }

        private void LocatePreviewKeyframe(ResolvedStep resolved, BattleActionTimelineRecordData keyframe)
        {
            playing = false;
            playheadSeconds = resolved.TriggerTime + BattleActionTimelineSchema.GetDouble(
                resolved.KeyframeTable, keyframe, 0d, BattleActionTimelineSchema.LocalTimeAliases);
            Repaint();
        }

        // 插帧保留当前采样姿态；已有同时间关键帧时只选择，不创建重叠帧。
        private static BattleActionTimelineRecordData EnsurePreviewKeyframe(
            BattleActionTimelineDocument document, int shapeId, double localTime)
        {
            var shapes = document.tables.FirstOrDefault(BattleActionTimelineSchema.IsShapeTable);
            var shape = BattleActionTimelineSchema.FindById(shapes, shapeId);
            if (shape == null || !BattleActionTimelineTime.IsFinite(localTime) || localTime < 0d) return null;
            var keys = document.tables.FirstOrDefault(table => BattleActionTimelineSchema.IsKeyframeTable(table) &&
                BattleActionTimelineSchema.FindColumn(table, "shapeId") >= 0);
            var existing = keys?.records.FirstOrDefault(key =>
                BattleActionTimelineSchema.GetInt(keys, key, 0, "shapeId") == shapeId &&
                Math.Abs(BattleActionTimelineSchema.GetDouble(keys, key, 0d,
                    BattleActionTimelineSchema.LocalTimeAliases) - localTime) < BattleActionTimelineTime.Epsilon);
            if (existing != null) return existing;
            BattleActionTimelineTransform sampled = EvaluateShapeTransform(document, shape, localTime);
            var added = AddShapeKeyframe(document, shapeId, localTime);
            if (added == null) return null;
            BattleActionTimelineSchema.SetDouble(keys, added, sampled.Offset.x, BattleActionTimelineSchema.OffsetXAliases);
            BattleActionTimelineSchema.SetDouble(keys, added, sampled.Offset.y, BattleActionTimelineSchema.OffsetYAliases);
            BattleActionTimelineSchema.SetDouble(keys, added, sampled.RotationDegrees, BattleActionTimelineSchema.RotationAliases);
            BattleActionTimelineSchema.SetDouble(keys, added, sampled.Scale.x, BattleActionTimelineSchema.ScaleXAliases);
            BattleActionTimelineSchema.SetDouble(keys, added, sampled.Scale.y, BattleActionTimelineSchema.ScaleYAliases);
            return added;
        }

        private void HandlePreviewShapeGizmos(Rect rect, List<PreviewShape> shapes, ResolvedStep resolved,
            Vector2 bodyOrigin, float pixelsPerUnit, int facing, float alpha)
        {
            int control = GUIUtility.GetControlID(ShapeGizmoControlHint, FocusType.Passive);
            Event current = Event.current;
            if (shapes.Count == 0 && dragKind == DragKind.PreviewShapeGizmo)
            {
                if (dragChanged) OnAuthoringChanged();
                CancelDrag();
            }
            if (dragKind == DragKind.PreviewShapeGizmo)
            {
                if (current.type == EventType.MouseDrag && current.button == 0)
                {
                    ApplyPreviewGizmoDrag(current.mousePosition, current.shift, pixelsPerUnit, facing);
                    current.Use();
                }
                else if (current.type == EventType.MouseUp && current.button == 0)
                {
                    if (dragChanged) OnAuthoringChanged();
                    CancelDrag();
                    current.Use();
                }
            }
            if (dragKind != DragKind.PreviewShapeGizmo && current.type == EventType.KeyDown && rect.Contains(current.mousePosition) &&
                GUIUtility.keyboardControl == 0)
            {
                ShapeGizmoTool? tool = current.keyCode == KeyCode.W ? ShapeGizmoTool.Move
                    : current.keyCode == KeyCode.T ? ShapeGizmoTool.Size
                    : current.keyCode == KeyCode.R ? ShapeGizmoTool.Scale
                    : current.keyCode == KeyCode.E ? ShapeGizmoTool.Rotate : (ShapeGizmoTool?)null;
                if (tool.HasValue) { shapeGizmoTool = tool.Value; current.Use(); Repaint(); }
            }
            PreviewShape selected = shapes.FirstOrDefault(shape => shape.Shape.rowId == resolved.SelectedShape?.rowId);
            var key = SelectedPreviewKeyframe(resolved);
            bool canEdit = selected != null && (!previewEditKeyframes || key != null &&
                Math.Abs(playheadSeconds - resolved.TriggerTime - BattleActionTimelineSchema.GetDouble(
                    resolved.KeyframeTable, key, 0d, BattleActionTimelineSchema.LocalTimeAliases)) < BattleActionTimelineTime.Epsilon);
            Vector2 pivot = selected == null ? bodyOrigin : previewEditKeyframes
                ? bodyOrigin + BattleActionTimelineShapeGizmoMath.ToScreen(selected.Transform.Offset, pixelsPerUnit, facing)
                : selected.Center;
            var handles = new List<(Vector2 point, Vector2 axis)>();
            ShapeGizmoTool toolToDraw = previewEditKeyframes && shapeGizmoTool == ShapeGizmoTool.Size
                ? ShapeGizmoTool.Scale : shapeGizmoTool;
            if (canEdit && !previewEditKeyframes &&
                (toolToDraw == ShapeGizmoTool.Size || toolToDraw == ShapeGizmoTool.Scale) &&
                (Math.Abs(selected.Transform.Scale.x) < 0.00001f || Math.Abs(selected.Transform.Scale.y) < 0.00001f))
            {
                canEdit = false;
                GUI.Label(new Rect(rect.x + 10f, rect.y + 3f, rect.width - 20f, 20f),
                    "此时关键帧有零缩放，请先切换到关键帧模式调整缩放。", EditorStyles.miniLabel);
            }
            if (canEdit)
            {
                Handles.BeginGUI();
                Handles.color = new Color(1f, 0.85f, 0.15f, alpha);
                if (toolToDraw == ShapeGizmoTool.Move)
                {
                    handles.Add((pivot, Vector2.zero));
                    handles.Add((pivot + new Vector2(38f * facing, 0f), Vector2.right));
                    handles.Add((pivot + new Vector2(0f, -38f), Vector2.up));
                    Handles.DrawLine(pivot, handles[1].point);
                    Handles.DrawLine(pivot, handles[2].point);
                }
                else if (toolToDraw == ShapeGizmoTool.Rotate)
                {
                    float radius = Math.Max(38f, Math.Min(130f, selected.Size.magnitude * 0.5f + 22f));
                    Handles.DrawWireDisc(pivot, Vector3.forward, radius);
                    handles.Add((pivot + new Vector2(0f, -radius), Vector2.zero));
                }
                else
                {
                    Vector2 size = new Vector2(Math.Max(20f, selected.Size.x), Math.Max(20f, selected.Size.y));
                    foreach (Vector2 axis in new[] { new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f),
                        new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f) })
                        handles.Add((selected.Center + Rotate(Vector2.Scale(size * 0.5f, axis), selected.ScreenRotation), axis));
                    Handles.DrawLine(pivot + new Vector2(-5f, 0f), pivot + new Vector2(5f, 0f));
                    Handles.DrawLine(pivot + new Vector2(0f, -5f), pivot + new Vector2(0f, 5f));
                }
                Handles.EndGUI();
                foreach (var handle in handles)
                {
                    Rect area = new Rect(handle.point.x - 5f, handle.point.y - 5f, 10f, 10f);
                    EditorGUI.DrawRect(area, new Color(1f, 0.85f, 0.15f, alpha));
                    EditorGUIUtility.AddCursorRect(area, toolToDraw == ShapeGizmoTool.Move ? MouseCursor.MoveArrow : MouseCursor.ResizeUpLeft);
                }
            }
            if (dragKind == DragKind.PreviewShapeGizmo || current.type != EventType.MouseDown ||
                current.button != 0 || !rect.Contains(current.mousePosition))
                return;
            GUIUtility.keyboardControl = 0;
            Focus();
            if (canEdit)
            {
                int hit = handles.FindIndex(handle => (handle.point - current.mousePosition).sqrMagnitude <= 100f);
                if (hit >= 0)
                {
                    BeginPreviewGizmoDrag(control, selected, resolved, key, toolToDraw, handles[hit].axis, pivot);
                    current.Use();
                    return;
                }
            }
            PreviewShape picked = shapes.LastOrDefault(shape => BattleActionTimelineShapeGizmoMath.Contains(
                current.mousePosition, shape.Center, shape.Size, shape.ScreenRotation, shape.Type, shape.Horizontal));
            if (picked == null) return;
            if (resolved.SelectedShape?.rowId != picked.Shape.rowId || !previewEditKeyframes)
            {
                BindShapeKeyframes(Document, resolved, picked.Shape);
                SelectStepChild(resolved, picked.Table, picked.Shape);
                previewEditKeyframes = false;
                canEdit = true;
            }
            if (canEdit && toolToDraw == ShapeGizmoTool.Move)
                BeginPreviewGizmoDrag(control, picked, resolved, SelectedPreviewKeyframe(resolved), toolToDraw, Vector2.zero,
                    previewEditKeyframes ? pivot : picked.Center);
            current.Use();
            Repaint();
        }

        private void BeginPreviewGizmoDrag(int control, PreviewShape shape, ResolvedStep resolved,
            BattleActionTimelineRecordData keyframe, ShapeGizmoTool tool, Vector2 axis, Vector2 pivot)
        {
            if (!previewEditKeyframes) SelectStepChild(resolved, shape.Table, shape.Shape);
            playing = false;
            dragKind = DragKind.PreviewShapeGizmo;
            dragSheet = previewEditKeyframes ? resolved.KeyframeTable.sheetName : shape.Table.sheetName;
            dragRowId = previewEditKeyframes ? keyframe.rowId : shape.Shape.rowId;
            gizmoStartShape = shape;
            gizmoStartMouse = Event.current.mousePosition;
            gizmoHandle = axis;
            gizmoPivot = pivot;
            gizmoStartOffset = previewEditKeyframes ? shape.Transform.Offset : shape.Offset;
            gizmoStartRotation = previewEditKeyframes ? shape.Transform.RotationDegrees : shape.LocalRotation;
            shapeGizmoTool = tool;
            dragChanged = false;
            gizmoHotControl = control;
            GUIUtility.hotControl = control;
            Undo.RegisterCompleteObjectUndo(authoringState, "拖动 Shape " + (previewEditKeyframes ? "关键帧" : "本体") + " " + tool);
        }

        private void ApplyPreviewGizmoDrag(Vector2 mouse, bool uniform, float pixelsPerUnit, int facing)
        {
            var table = Document.FindTable(dragSheet);
            var record = BattleActionTimelineSchema.FindById(table, dragRowId);
            if (record == null || gizmoStartShape == null) { CancelDrag(); return; }
            PreviewShape shape = gizmoStartShape;
            bool keyframe = BattleActionTimelineSchema.IsKeyframeTable(table);
            Vector2 screenDelta = mouse - gizmoStartMouse;
            Vector2 delta = BattleActionTimelineShapeGizmoMath.FromScreen(screenDelta, pixelsPerUnit, facing);
            if (shapeGizmoTool == ShapeGizmoTool.Move)
            {
                if (gizmoHandle.x != 0f) delta.y = 0f;
                if (gizmoHandle.y != 0f) delta.x = 0f;
                Vector2 offset = keyframe ? gizmoStartOffset + delta
                    : BattleActionTimelineShapeGizmoMath.MoveShapeOffset(gizmoStartOffset, delta, shape.Transform);
                BattleActionTimelineSchema.SetDouble(table, record, offset.x, BattleActionTimelineSchema.OffsetXAliases);
                BattleActionTimelineSchema.SetDouble(table, record, offset.y, BattleActionTimelineSchema.OffsetYAliases);
            }
            else if (shapeGizmoTool == ShapeGizmoTool.Rotate)
            {
                Vector2 start = gizmoStartMouse - gizmoPivot, end = mouse - gizmoPivot;
                double degrees = (Math.Atan2(end.y, end.x) - Math.Atan2(start.y, start.x)) * 180d / Math.PI;
                degrees = (degrees + 540d) % 360d - 180d;
                BattleActionTimelineSchema.SetDouble(table, record, gizmoStartRotation - degrees * facing,
                    BattleActionTimelineSchema.RotationAliases);
            }
            else if (keyframe)
            {
                Vector2 localHandle = new Vector2(gizmoHandle.x, -gizmoHandle.y);
                if (facing < 0) localHandle.x = -localHandle.x;
                Vector2 scale = BattleActionTimelineShapeGizmoMath.ScaleKeyframe(shape.Offset, shape.BaseSize,
                    shape.LocalRotation, shape.Transform, localHandle, delta, uniform,
                    shape.Type == BattleAttackShapeType.Circle);
                BattleActionTimelineSchema.SetDouble(table, record, scale.x, BattleActionTimelineSchema.ScaleXAliases);
                BattleActionTimelineSchema.SetDouble(table, record, scale.y, BattleActionTimelineSchema.ScaleYAliases);
            }
            else
            {
                Vector2 size = BattleActionTimelineShapeGizmoMath.Resize(shape.Size,
                    Rotate(screenDelta, -shape.ScreenRotation), gizmoHandle,
                    uniform || shapeGizmoTool == ShapeGizmoTool.Scale || shape.Type == BattleAttackShapeType.Circle);
                if (shape.Type == BattleAttackShapeType.Circle)
                {
                    float scale = Math.Max(0.00001f, Math.Max(shape.Transform.Scale.x, shape.Transform.Scale.y));
                    BattleActionTimelineSchema.SetDouble(table, record, size.x / (2f * pixelsPerUnit * scale), "radius");
                }
                else
                {
                    BattleActionTimelineSchema.SetDouble(table, record, size.x / (pixelsPerUnit * Math.Max(0.00001f, shape.Transform.Scale.x)), "width", "sizeX");
                    BattleActionTimelineSchema.SetDouble(table, record, size.y / (pixelsPerUnit * Math.Max(0.00001f, shape.Transform.Scale.y)), "height", "sizeY");
                }
            }
            dragChanged = true;
            ValidateDocument();
            QueueDraftSave();
            Repaint();
        }
        /// <summary>
        /// 根据 `document`、`resolved`、`localTime` 构建投射物预览偏移并返回结果。
        /// </summary>

        private Vector2 ProjectilePreviewOffset(
            BattleActionTimelineDocument document,
            ResolvedStep resolved,
            double localTime)
        {
            Vector2 offset = resolved.SpawnOffset;
            if (resolved.Projectile == null)
            {
                return offset;
            }

            int movementId = BattleActionTimelineSchema.GetInt(
                resolved.ProjectileTable,
                resolved.Projectile,
                0,
                "movementConfigId");
            string movementType = BattleActionTimelineSchema.Get(
                resolved.ProjectileTable,
                resolved.Projectile,
                "movementType");
            if (movementType.IndexOf("Ballistic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                movementType == "1")
            {
                BattleActionTimelineTableData table = document.tables.FirstOrDefault(
                    BattleActionTimelineSchema.IsBallisticMovementTable);
                BattleActionTimelineRecordData movement =
                    BattleActionTimelineSchema.FindById(table, movementId);
                if (movement != null)
                {
                    double speed = BattleActionTimelineSchema.GetDouble(
                        table,
                        movement,
                        0d,
                        "initialSpeed");
                    double gravity = BattleActionTimelineSchema.GetDouble(
                        table,
                        movement,
                        0d,
                        "gravityScale");
                    offset.x += (float)(speed * localTime);
                    offset.y -= (float)(0.5d * 9.81d * gravity * localTime * localTime);
                }
            }
            else
            {
                BattleActionTimelineTableData table = document.tables.FirstOrDefault(
                    BattleActionTimelineSchema.IsLinearMovementTable);
                BattleActionTimelineRecordData movement =
                    BattleActionTimelineSchema.FindById(table, movementId);
                if (movement != null)
                {
                    offset.x += (float)(BattleActionTimelineSchema.GetDouble(
                        table,
                        movement,
                        0d,
                        "speed") * localTime);
                }
            }

            return offset;
        }
        /// <summary>
        /// 解析已选步骤。
        /// </summary>

        private ResolvedStep ResolveSelectedStep(BattleActionTimelineDocument document)
        {
            BattleActionTimelineTableData table =
                document.FindTable(authoringState.selectedStepSheet);
            BattleActionTimelineRecordData step = table?.records.FirstOrDefault(
                value => value.rowId == authoringState.selectedStepRowId);
            if (step == null)
            {
                return null;
            }

            ResolvedStep result = ResolveStep(document, table, step);
            BindSelectedShapeKeyframes(document, result);
            return result;
        }

        private void BindSelectedShapeKeyframes(
            BattleActionTimelineDocument document,
            ResolvedStep resolved)
        {
            if (document == null || resolved == null || resolved.Body == null)
            {
                return;
            }

            resolved.ShapeTable = document.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsShapeTable);
            if (resolved.ShapeTable == null)
            {
                return;
            }

            List<BattleActionTimelineRecordData> shapes = resolved.ShapeTable.records
                .Where(value => BattleActionTimelineSchema.GetInt(
                    resolved.ShapeTable,
                    value,
                    0,
                    BattleActionTimelineSchema.AttackBodyIdAliases) == resolved.Body.rowId)
                .OrderBy(value => BattleActionTimelineSchema.GetInt(resolved.ShapeTable, value, 0, "shapeIndex"))
                .ToList();
            // 选中某个 Shape 的关键帧后，仍保持该 Shape，不能退回第一个 Shape。
            BattleActionTimelineTableData selectedTable = document.FindTable(authoringState?.selectedSharedSheet);
            BattleActionTimelineRecordData selectedRecord = BattleActionTimelineSchema.FindById(
                selectedTable, authoringState?.selectedSharedRowId ?? 0);
            int keyframeShapeId = BattleActionTimelineSchema.IsKeyframeTable(selectedTable)
                ? BattleActionTimelineSchema.GetInt(selectedTable, selectedRecord, 0, "shapeId")
                : 0;
            BattleActionTimelineRecordData selectedShape = shapes.FirstOrDefault(value =>
                keyframeShapeId == value.rowId || (string.Equals(
                    authoringState?.selectedSharedSheet,
                    resolved.ShapeTable.sheetName,
                    StringComparison.Ordinal) &&
                authoringState != null && authoringState.selectedSharedRowId == value.rowId)) ??
                shapes.FirstOrDefault();
            BindShapeKeyframes(document, resolved, selectedShape);
        }

        private static void BindShapeKeyframes(
            BattleActionTimelineDocument document,
            ResolvedStep resolved,
            BattleActionTimelineRecordData shape)
        {
            resolved.SelectedShape = shape;
            resolved.ShapeTable = document.tables.FirstOrDefault(BattleActionTimelineSchema.IsShapeTable);
            resolved.KeyframeTable = null;
            resolved.Keyframes = new List<BattleActionTimelineRecordData>();
            if (shape == null)
            {
                return;
            }

            resolved.KeyframeTable = document.tables.FirstOrDefault(table =>
                BattleActionTimelineSchema.IsKeyframeTable(table) &&
                (table.sheetName.IndexOf("ShapeTransformKeyframe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 table.sheetName.IndexOf("AttackShape", StringComparison.OrdinalIgnoreCase) >= 0));
            if (resolved.KeyframeTable == null)
            {
                return;
            }

            int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(
                resolved.KeyframeTable);
            if (ownerColumn < 0)
            {
                return;
            }

            resolved.Keyframes = resolved.KeyframeTable.records.Where(record =>
                int.TryParse(
                    record.cells[ownerColumn],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int ownerId) && ownerId == shape.rowId)
                .OrderBy(record => BattleActionTimelineSchema.GetDouble(
                    resolved.KeyframeTable, record, 0d, BattleActionTimelineSchema.LocalTimeAliases)).ToList();
        }

        /// <summary>
        /// 按当前技能最早的攻击步骤和该攻击体的第一个 Shape 估算 AI 站位距离。
        /// 这里计算的是从 CombatOrigin 到 Shape 水平包围边界的保守距离，左右朝向都能复用。
        /// </summary>
        private static bool TryCalculateAttackRangeFromFirstShape(
            BattleActionTimelineDocument document,
            int actionId,
            out double attackRange,
            out string description,
            out string error)
        {
            attackRange = 0d;
            description = string.Empty;
            error = string.Empty;
            if (document == null || document.tables == null || actionId <= 0)
            {
                error = "当前动作工作集为空，无法计算攻击距离。";
                return false;
            }

            BattleActionTimelineTableData stepTable = document.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsExecutionStepTable);
            if (stepTable == null)
            {
                error = "缺少执行步骤工作表，无法找到首个攻击步骤。";
                return false;
            }

            List<BattleActionTimelineRecordData> attackSteps = stepTable.records
                .Where(step =>
                    BattleActionTimelineSchema.GetInt(
                        stepTable,
                        step,
                        0,
                        BattleActionTimelineSchema.StepOwnerAliases) == actionId &&
                    (BattleActionTimelineSchema.StepIsMelee(stepTable, step) ||
                     BattleActionTimelineSchema.StepIsProjectile(stepTable, step)))
                .OrderBy(step => BattleActionTimelineSchema.GetDouble(
                    stepTable,
                    step,
                    0d,
                    BattleActionTimelineSchema.TriggerTimeAliases))
                .ThenBy(step => BattleActionTimelineSchema.GetStepIndex(stepTable, step))
                .ToList();
            if (attackSteps.Count == 0)
            {
                error = "当前技能没有近战或投射物攻击步骤，无法自动计算攻击距离。";
                return false;
            }

            BattleActionTimelineRecordData firstStep = attackSteps[0];
            ResolvedStep resolved = ResolveStep(document, stepTable, firstStep);
            if (resolved == null || resolved.Body == null)
            {
                error = "首个攻击步骤缺少有效的攻击体配置，无法自动计算攻击距离。";
                return false;
            }

            BattleActionTimelineTableData shapeTable = document.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsShapeTable);
            if (shapeTable == null)
            {
                error = "缺少 BattleAttackShape 工作表，无法自动计算攻击距离。";
                return false;
            }

            BattleActionTimelineRecordData shape = shapeTable.records
                .Where(value => BattleActionTimelineSchema.GetInt(
                    shapeTable,
                    value,
                    0,
                    BattleActionTimelineSchema.AttackBodyIdAliases) == resolved.Body.rowId)
                .OrderBy(value => BattleActionTimelineSchema.GetInt(
                    shapeTable,
                    value,
                    int.MaxValue,
                    "shapeIndex"))
                .ThenBy(value => value.rowId)
                .FirstOrDefault();
            if (shape == null)
            {
                error = "首个攻击体没有 Shape，无法自动计算攻击距离。";
                return false;
            }

            double maximum = 0d;
            foreach (double localTime in GetShapeRangeSampleTimes(document, shape))
            {
                BattleActionTimelineTransform transform = EvaluateShapeTransform(
                    document,
                    shape,
                    localTime);
                maximum = Math.Max(
                    maximum,
                    CalculateShapeHorizontalRange(shapeTable, shape, transform));
            }

            if (!BattleActionTimelineTime.IsFinite(maximum))
            {
                error = "首个 Shape 的尺寸或变换包含无效数值，无法自动计算攻击距离。";
                return false;
            }

            attackRange = Math.Max(0d, maximum);
            int shapeIndex = BattleActionTimelineSchema.GetInt(
                shapeTable,
                shape,
                0,
                "shapeIndex");
            description = "步骤 " +
                (BattleActionTimelineSchema.GetStepIndex(stepTable, firstStep) + 1).ToString(
                    CultureInfo.InvariantCulture) +
                " / Shape " +
                (shapeIndex + 1).ToString(CultureInfo.InvariantCulture) +
                "（攻击体 " + resolved.Body.rowId.ToString(CultureInfo.InvariantCulture) + "）";
            return true;
        }

        private static IEnumerable<double> GetShapeRangeSampleTimes(
            BattleActionTimelineDocument document,
            BattleActionTimelineRecordData shape)
        {
            SortedSet<double> anchors = new SortedSet<double> { 0d };
            BattleActionTimelineTableData keyframeTable = document.tables.FirstOrDefault(table =>
                BattleActionTimelineSchema.IsKeyframeTable(table) &&
                (table.sheetName.IndexOf("ShapeTransformKeyframe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 table.sheetName.IndexOf("AttackShape", StringComparison.OrdinalIgnoreCase) >= 0));
            if (keyframeTable != null)
            {
                int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(keyframeTable);
                if (ownerColumn >= 0)
                {
                    foreach (BattleActionTimelineRecordData keyframe in keyframeTable.records)
                    {
                        if (BattleActionTimelineSchema.GetInt(
                                keyframeTable,
                                keyframe,
                                0,
                                "shapeId") != shape.rowId)
                        {
                            continue;
                        }

                        double localTime = BattleActionTimelineSchema.GetDouble(
                            keyframeTable,
                            keyframe,
                            0d,
                            BattleActionTimelineSchema.LocalTimeAliases);
                        if (BattleActionTimelineTime.IsFinite(localTime) && localTime >= 0d)
                        {
                            anchors.Add(localTime);
                        }
                    }
                }
            }

            List<double> ordered = anchors.ToList();
            if (ordered.Count <= 1)
            {
                return ordered;
            }

            // 关键帧之间可能通过旋转、缩放和曲线形成更大的水平边界；增加采样点，避免只看端点漏算。
            List<double> samples = new List<double>();
            for (int index = 0; index < ordered.Count - 1; index++)
            {
                double start = ordered[index];
                double end = ordered[index + 1];
                const int subdivisions = 16;
                for (int sample = 0; sample < subdivisions; sample++)
                {
                    samples.Add(start + (end - start) * sample / subdivisions);
                }
            }

            samples.Add(ordered[ordered.Count - 1]);
            return samples;
        }

        private static double CalculateShapeHorizontalRange(
            BattleActionTimelineTableData shapeTable,
            BattleActionTimelineRecordData shape,
            BattleActionTimelineTransform transform)
        {
            Vector2 localOffset = new Vector2(
                (float)BattleActionTimelineSchema.GetDouble(
                    shapeTable,
                    shape,
                    0d,
                    "offsetX",
                    "localOffsetX"),
                (float)BattleActionTimelineSchema.GetDouble(
                    shapeTable,
                    shape,
                    0d,
                    "offsetY",
                    "localOffsetY"));
            Vector2 center = transform.Offset + Rotate(
                Vector2.Scale(localOffset, transform.Scale),
                transform.RotationDegrees);
            string type = BattleActionTimelineSchema.Get(shapeTable, shape, "shapeType");
            bool circle = type.IndexOf("Circle", StringComparison.OrdinalIgnoreCase) >= 0 || type == "1";
            double absoluteScaleX = Math.Abs(transform.Scale.x);
            double absoluteScaleY = Math.Abs(transform.Scale.y);
            double halfExtentX;
            if (circle)
            {
                double radius = BattleActionTimelineSchema.GetDouble(
                    shapeTable,
                    shape,
                    0d,
                    "radius");
                halfExtentX = Math.Abs(radius) * Math.Max(absoluteScaleX, absoluteScaleY);
            }
            else
            {
                double width = BattleActionTimelineSchema.GetDouble(
                    shapeTable,
                    shape,
                    0d,
                    "width",
                    "sizeX");
                double height = BattleActionTimelineSchema.GetDouble(
                    shapeTable,
                    shape,
                    0d,
                    "height",
                    "sizeY");
                double rotation =
                    BattleActionTimelineSchema.GetDouble(
                        shapeTable,
                        shape,
                        0d,
                        "rotationDegrees",
                        "localRotationDegrees") +
                    transform.RotationDegrees;
                double radians = rotation * Math.PI / 180d;
                halfExtentX =
                    Math.Abs(Math.Cos(radians)) * Math.Abs(width * absoluteScaleX) * 0.5d +
                    Math.Abs(Math.Sin(radians)) * Math.Abs(height * absoluteScaleY) * 0.5d;
            }

            return Math.Abs(center.x) + halfExtentX;
        }

        /// <summary>
        /// 解析步骤。
        /// </summary>

        private static ResolvedStep ResolveStep(
            BattleActionTimelineDocument document,
            BattleActionTimelineTableData stepTable,
            BattleActionTimelineRecordData step)
        {
            if (document == null || stepTable == null || step == null)
            {
                return null;
            }

            ResolvedStep result = new ResolvedStep
            {
                StepTable = stepTable,
                Step = step,
                TriggerTime = BattleActionTimelineSchema.GetDouble(
                    stepTable,
                    step,
                    0d,
                    BattleActionTimelineSchema.TriggerTimeAliases),
                InteractionTypeIndex = BattleActionTimelineSchema.GetInteractionTypeIndex(
                    stepTable,
                    step),
                IsMelee = BattleActionTimelineSchema.StepIsMelee(stepTable, step),
                IsProjectile = BattleActionTimelineSchema.StepIsProjectile(stepTable, step),
            };
            int configId = BattleActionTimelineSchema.GetStepConfigurationId(stepTable, step);
            int attackBodyId = 0;
            if (result.IsMelee)
            {
                result.OwnerTable = document.tables.FirstOrDefault(
                    BattleActionTimelineSchema.IsMeleeSpawnTable);
                result.Owner = BattleActionTimelineSchema.FindById(
                    result.OwnerTable,
                    configId);
                if (result.Owner != null)
                {
                    attackBodyId = BattleActionTimelineSchema.GetInt(
                        result.OwnerTable,
                        result.Owner,
                        0,
                        BattleActionTimelineSchema.AttackBodyIdAliases);
                    result.Lifetime = BattleActionTimelineSchema.GetDouble(
                        result.OwnerTable,
                        result.Owner,
                        0d,
                        "activeDuration");
                }
            }
            else if (result.IsProjectile)
            {
                result.OwnerTable = document.tables.FirstOrDefault(
                    BattleActionTimelineSchema.IsProjectileLaunchTable);
                result.Owner = BattleActionTimelineSchema.FindById(
                    result.OwnerTable,
                    configId);
                if (result.Owner != null)
                {
                    int projectileId = BattleActionTimelineSchema.GetInt(
                        result.OwnerTable,
                        result.Owner,
                        0,
                        "projectileId");
                    result.SpawnOffset = new Vector2(
                        (float)BattleActionTimelineSchema.GetDouble(
                            result.OwnerTable,
                            result.Owner,
                            0d,
                            "spawnOffsetX",
                            "offsetX"),
                        (float)BattleActionTimelineSchema.GetDouble(
                            result.OwnerTable,
                            result.Owner,
                            0d,
                            "spawnOffsetY",
                            "offsetY"));
                    result.ProjectileTable = document.tables.FirstOrDefault(
                        BattleActionTimelineSchema.IsProjectileTable);
                    result.Projectile = BattleActionTimelineSchema.FindById(
                        result.ProjectileTable,
                        projectileId);
                    if (result.Projectile != null)
                    {
                        attackBodyId = BattleActionTimelineSchema.GetInt(
                            result.ProjectileTable,
                            result.Projectile,
                            0,
                            BattleActionTimelineSchema.AttackBodyIdAliases);
                        result.Lifetime = BattleActionTimelineSchema.GetDouble(
                            result.ProjectileTable,
                            result.Projectile,
                            0d,
                            "maxLifetime");
                    }
                }
            }

            result.BodyTable = document.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsAttackBodyTable);
            result.Body = BattleActionTimelineSchema.FindById(
                result.BodyTable,
                attackBodyId);
            return result;
        }
        /// <summary>
        /// 加载正式。
        /// </summary>

        private void LoadOfficial(bool confirmDiscard, bool notify)
        {
            if (confirmDiscard && IsDirty && !EditorUtility.DisplayDialog(
                "重新读取正式动作配置",
                "当前草稿尚未写回。重新读取会丢弃草稿，是否继续？",
                "丢弃并读取",
                "取消"))
            {
                return;
            }

            if (!BattleActionTimelineWorkbookBridge.TryLoad(
                    out BattleActionTimelineWorkbookSnapshot snapshot,
                    out string error))
            {
                status = error;
                workbookSnapshot = null;
                if (notify)
                {
                    ShowNotification(new GUIContent("正式动作源表读取失败"));
                }

                return;
            }

            workbookSnapshot = snapshot;
            Document = BattleActionTimelineDocument.FromSnapshot(snapshot);
            authoringState.baselineSignature = Document.CanonicalSignature();
            authoringState.sourceWorkbookPath = snapshot.WorkbookPath;
            authoringState.sourceWorkbookHash = snapshot.ContentHash;
            Undo.ClearUndo(authoringState);
            ClearDraft();
            NormalizeSelection();
            playheadSeconds = 0d;
            playing = false;
            ValidateDocument();
            status = snapshot.HasActionStructure
                ? "已读取正式动作工作集：" + snapshot.WorkbookPath +
                  "；写回只更新源 xlsx，不调用导表。"
                : "已读取机器人工作簿，但新动作 Sheet 尚未落地；当前处于结构等待态。";
            if (notify)
            {
                ShowNotification(new GUIContent(
                    snapshot.HasActionStructure ? "动作工作集已读取" : "等待动作源表结构"));
            }

            Repaint();
        }
        /// <summary>
        /// 写入正式。
        /// </summary>

        private void WriteOfficial()
        {
            NormalizeShapeOrder();
            ValidateDocument();
            if (validationIssues.Count > 0)
            {
                ShowNotification(new GUIContent("校验未通过，未写回"));
                return;
            }

            if (workbookSnapshot == null || !workbookSnapshot.HasActionStructure)
            {
                status = "请先读取包含 ActiveSingle 的正式动作源表。";
                return;
            }

            BattleActionTimelineWorkbookWriteSet writeSet =
                Document.BuildWriteSet(workbookSnapshot);
            if (writeSet.IsEmpty)
            {
                status = "当前工作集与读取基线一致，无需写回。";
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "安全写回动作源 xlsx",
                "将事务性更新 " + writeSet.Replacements.Count + " 条记录，删除 " +
                writeSet.Deletions.Count + " 条记录：\n" +
                workbookSnapshot.WorkbookPath + "\n\n" +
                "工具会校验 hash、临时 xlsx 和回读结果，再 File.Replace 正式源表。" +
                "不生成持久备份，瞬时文件结束后清理。本操作不会导表，生成目录保持不变。",
                "仅写回源 xlsx",
                "取消"))
            {
                return;
            }

            if (!BattleActionTimelineWorkbookBridge.TryWrite(
                    workbookSnapshot,
                    writeSet,
                    out string error))
            {
                status = error;
                Debug.LogError("[BattleActionTimeline] " + error);
                ShowNotification(new GUIContent("动作源表写回失败；草稿已保留"));
                SaveDraftNow();
                EditorUtility.DisplayDialog("动作源表写回失败", error, "确定");
                return;
            }

            int selectedActionId = authoringState.selectedActionRowId;
            string selectedActionSheet = authoringState.selectedActionSheet;
            if (!BattleActionTimelineWorkbookBridge.TryLoad(
                    out BattleActionTimelineWorkbookSnapshot refreshed,
                    out string reloadError))
            {
                workbookSnapshot = null;
                status = "源 xlsx 已安全写回，但写后重新读取失败：" + reloadError;
                return;
            }

            workbookSnapshot = refreshed;
            Document = BattleActionTimelineDocument.FromSnapshot(refreshed);
            authoringState.baselineSignature = Document.CanonicalSignature();
            authoringState.sourceWorkbookPath = refreshed.WorkbookPath;
            authoringState.sourceWorkbookHash = refreshed.ContentHash;
            authoringState.selectedActionSheet = selectedActionSheet;
            authoringState.selectedActionRowId = selectedActionId;
            NormalizeSelection();
            Undo.ClearUndo(authoringState);
            ClearDraft();
            status = "动作源 xlsx 已原子写回并回读通过；未生成备份，未执行导表。";
            ShowNotification(new GUIContent("源 xlsx 写回成功；请最后统一导表"));
            Repaint();
        }
        /// <summary>
        /// 校验文档。
        /// </summary>

        private void ValidateDocument()
        {
            BattleActionTimelineSchema.Validate(Document, validationIssues);
        }
        /// <summary>
        /// 响应编辑数据变化事件，并同步当前组件状态。
        /// </summary>

        private void OnAuthoringChanged()
        {
            filterCatalog = null;
            ClampPlayhead();
            ValidateDocument();
            QueueDraftSave();
            Repaint();
        }
        /// <summary>
        /// 更新队列草稿存档。
        /// </summary>

        private void QueueDraftSave()
        {
            if (draftSaveQueued)
            {
                return;
            }

            draftSaveQueued = true;
            EditorApplication.delayCall += () =>
            {
                draftSaveQueued = false;
                if (this != null)
                {
                    SaveDraftNow();
                }
            };
        }
        /// <summary>
        /// 保存草稿当前时间。
        /// </summary>

        private void SaveDraftNow()
        {
            if (authoringState == null)
            {
                return;
            }

            if (!IsDirty)
            {
                ClearDraft();
                return;
            }

            DraftEnvelope envelope = new DraftEnvelope
            {
                documentJson = JsonUtility.ToJson(Document),
                baselineSignature = authoringState.baselineSignature,
                selectedActionSheet = authoringState.selectedActionSheet,
                selectedActionRowId = authoringState.selectedActionRowId,
                selectedStepSheet = authoringState.selectedStepSheet,
                selectedStepRowId = authoringState.selectedStepRowId,
                selectedSharedSheet = authoringState.selectedSharedSheet,
                selectedSharedRowId = authoringState.selectedSharedRowId,
                frameRate = authoringState.frameRate,
                snapToFrames = authoringState.snapToFrames,
                facing = authoringState.facing,
                sequencePreviewActionId = authoringState.sequencePreviewActionId,
                explicitSingleSkillId = authoringState.explicitSingleSkillId,
                playheadSeconds = playheadSeconds,
                workbookPath = workbookSnapshot?.WorkbookPath ?? authoringState.sourceWorkbookPath,
                workbookHash = workbookSnapshot?.ContentHash ?? authoringState.sourceWorkbookHash,
            };
            EditorPrefs.SetString(DraftKey, JsonUtility.ToJson(envelope));
        }
        /// <summary>
        /// 恢复草稿；操作完成时返回 `true`，前置条件不满足时返回 `false`。
        /// </summary>

        private void TryRestoreDraft()
        {
            if (draftRestored)
            {
                return;
            }

            draftRestored = true;
            string json = EditorPrefs.GetString(DraftKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                DraftEnvelope envelope = JsonUtility.FromJson<DraftEnvelope>(json);
                BattleActionTimelineDocument restored = JsonUtility.FromJson<
                    BattleActionTimelineDocument>(envelope.documentJson);
                if (restored == null)
                {
                    throw new InvalidOperationException("草稿文档为空。");
                }

                restored.EnsureLists();
                Document = restored;
                authoringState.baselineSignature = envelope.baselineSignature ?? string.Empty;
                authoringState.sourceWorkbookPath = envelope.workbookPath ?? string.Empty;
                authoringState.sourceWorkbookHash = envelope.workbookHash ?? string.Empty;
                authoringState.selectedActionSheet = envelope.selectedActionSheet ?? string.Empty;
                authoringState.selectedActionRowId = envelope.selectedActionRowId;
                authoringState.selectedStepSheet = envelope.selectedStepSheet ?? string.Empty;
                authoringState.selectedStepRowId = envelope.selectedStepRowId;
                authoringState.selectedSharedSheet = envelope.selectedSharedSheet ?? string.Empty;
                authoringState.selectedSharedRowId = envelope.selectedSharedRowId;
                // 旧草稿只有单段定位；一次性迁移到动作输入，不再任取工作簿第一行。
                authoringState.sequencePreviewActionId = envelope.sequencePreviewActionId > 0
                    ? envelope.sequencePreviewActionId : CurrentEditedActionAnimationId();
                authoringState.explicitSingleSkillId = envelope.sequencePreviewActionId > 0
                    ? envelope.explicitSingleSkillId : envelope.selectedActionRowId;
                authoringState.frameRate = envelope.frameRate > 0f ? envelope.frameRate : 30f;
                authoringState.snapToFrames = envelope.snapToFrames;
                authoringState.facing = envelope.facing ==
                    (int)BattleActionTimelineFacing.Left
                    ? envelope.facing
                    : (int)BattleActionTimelineFacing.Right;
                playheadSeconds = Math.Max(0d, envelope.playheadSeconds);

                workbookSnapshot = null;
                NormalizeSelection();
                ClampPlayhead();
            }
            catch (Exception exception)
            {
                Debug.LogError("[BattleActionTimeline] 草稿恢复失败：" + exception);
            }
        }
        /// <summary>
        /// 只读恢复源表快照；验证失败时不覆盖文档，也不放开写回。
        /// </summary>

        private void RestoreWorkbookBinding()
        {
            workbookSnapshot = null;
            string path = string.IsNullOrEmpty(authoringState.sourceWorkbookPath)
                ? BattleActionTimelineWorkbookBridge.DefaultWorkbookPath : authoringState.sourceWorkbookPath;
            if (!BattleActionTimelineWorkbookBridge.TryLoad(path, out var current, out string error) ||
                !BattleActionTimelineSourceBinding.CanRebind(current, authoringState.sourceWorkbookPath,
                    authoringState.sourceWorkbookHash, authoringState.baselineSignature, out error))
            {
                status = "已保留当前配置与草稿，源表基线恢复失败：" + error;
                return;
            }
            workbookSnapshot = current;
            Document.RestoreSourceRowIdBaselines(current);
            authoringState.sourceWorkbookPath = current.WorkbookPath;
            authoringState.sourceWorkbookHash = current.ContentHash;
            status = "已恢复正式源表基线；当前选择、配置和未写回草稿保持不变。";
        }

        /// <summary>清空编辑器草稿。</summary>
        private void ClearDraft()
        {
            EditorPrefs.DeleteKey(DraftKey);
        }
        /// <summary>
        /// 规范化选择。
        /// </summary>

        private void NormalizeSelection()
        {
            EnsureState();
            NormalizeActionForPreview();
            BattleActionTimelineTableData stepTable =
                Document.FindTable(authoringState.selectedStepSheet);
            if (stepTable == null || !stepTable.records.Any(
                value => value.rowId == authoringState.selectedStepRowId))
            {
                authoringState.selectedStepSheet = string.Empty;
                authoringState.selectedStepRowId = 0;
            }

            BattleActionTimelineTableData sharedTable =
                Document.FindTable(authoringState.selectedSharedSheet);
            if (sharedTable == null || !sharedTable.records.Any(
                value => value.rowId == authoringState.selectedSharedRowId))
            {
                authoringState.selectedSharedSheet = string.Empty;
                authoringState.selectedSharedRowId = 0;
            }
        }
        /// <summary>
        /// 只根据显式输入的动作定位编辑记录；未匹配或歧义时不能回退到无关单段。
        /// </summary>

        private void NormalizeActionForPreview()
        {
            if (BattleActionTimelineActionLookup.TryResolve(
                Document, authoringState.sequencePreviewActionId, authoringState.explicitSingleSkillId,
                out BattleActionTimelineTableData table, out BattleActionTimelineRecordData record,
                out actionSelectionStatus))
            {
                if (authoringState.selectedActionSheet != table.sheetName ||
                    authoringState.selectedActionRowId != record.rowId)
                {
                    SelectAction(table, record);
                }
                return;
            }

            if (authoringState.selectedActionRowId != 0 ||
                !string.IsNullOrEmpty(authoringState.selectedActionSheet))
            {
                playing = false;
                playheadSeconds = 0d;
            }
            authoringState.selectedActionSheet = string.Empty;
            authoringState.selectedActionRowId = 0;
            authoringState.selectedStepSheet = string.Empty;
            authoringState.selectedStepRowId = 0;
            authoringState.selectedSharedSheet = string.Empty;
            authoringState.selectedSharedRowId = 0;
        }
        /// <summary>
        /// 选择动作。
        /// </summary>

        private void SelectAction(BattleActionTimelineTableData table, BattleActionTimelineRecordData action)
        {
            authoringState.selectedActionSheet = table.sheetName;
            authoringState.selectedActionRowId = action.rowId;
            authoringState.selectedStepSheet = string.Empty;
            authoringState.selectedStepRowId = 0;
            authoringState.selectedSharedSheet = string.Empty;
            authoringState.selectedSharedRowId = 0;
            playheadSeconds = 0d;
            playing = false;
            QueueDraftSave();
            Repaint();
        }
        /// <summary>
        /// 选择步骤。
        /// </summary>

        private void SelectStep(string sheetName, int rowId)
        {
            hierarchyExpanded[(sheetName ?? string.Empty) + ":" + rowId.ToString(CultureInfo.InvariantCulture)] = true;
            authoringState.selectedStepSheet = sheetName ?? string.Empty;
            authoringState.selectedStepRowId = rowId;
            authoringState.selectedSharedSheet = string.Empty;
            authoringState.selectedSharedRowId = 0;
            QueueDraftSave();
            Repaint();
        }
        /// <summary>
        /// 选择共享。
        /// </summary>

        private void SelectShared(string sheetName, int rowId)
        {
            hierarchyExpanded[(sheetName ?? string.Empty) + ":" + rowId.ToString(CultureInfo.InvariantCulture)] = true;
            authoringState.selectedSharedSheet = sheetName ?? string.Empty;
            authoringState.selectedSharedRowId = rowId;
            BattleActionTimelineTableData table = Document.FindTable(sheetName);
            if (BattleActionTimelineSchema.IsKeyframeTable(table))
            {
                BattleActionTimelineRecordData keyframe = BattleActionTimelineSchema.FindById(table, rowId);
                int shapeId = BattleActionTimelineSchema.GetInt(table, keyframe, 0, "shapeId");
                BattleActionTimelineTableData shapes = Document.tables.FirstOrDefault(BattleActionTimelineSchema.IsShapeTable);
                BattleActionTimelineRecordData shape = BattleActionTimelineSchema.FindById(shapes, shapeId);
                if (shape != null) SetHierarchyExpanded(shapes, shape);
                previewEditKeyframes = true;
                ResolvedStep resolved = ResolveSelectedStep(Document);
                if (keyframe != null && resolved?.SelectedShape?.rowId == shapeId)
                    LocatePreviewKeyframe(resolved, keyframe);
            }
            else if (BattleActionTimelineSchema.IsShapeTable(table)) previewEditKeyframes = false;
            QueueDraftSave();
            Repaint();
        }
        /// <summary>
        /// 按 `document` 获取已选动作；命中时写出 `table`、`action` 并返回 `true`，未找到时返回 `false`。
        /// </summary>

        private bool TryGetSelectedAction(
            BattleActionTimelineDocument document,
            out BattleActionTimelineTableData table,
            out BattleActionTimelineRecordData action)
        {
            table = document?.FindTable(authoringState.selectedActionSheet);
            action = table?.records.FirstOrDefault(
                value => value.rowId == authoringState.selectedActionRowId);
            return action != null && BattleActionTimelineSchema.IsActiveSingleTable(table);
        }
        /// <summary>
        /// 规范化步骤顺序用于已选动作。
        /// </summary>

        private void NormalizeStepOrderForSelectedAction()
        {
            if (!TryGetSelectedAction(Document, out _, out BattleActionTimelineRecordData action))
            {
                return;
            }

            foreach (BattleActionTimelineTableData table in Document.tables
                .Where(BattleActionTimelineSchema.IsExecutionStepTable))
            {
                List<int> slots = new List<int>();
                List<BattleActionTimelineRecordData> owned =
                    new List<BattleActionTimelineRecordData>();
                for (int index = 0; index < table.records.Count; index++)
                {
                    BattleActionTimelineRecordData step = table.records[index];
                    if (BattleActionTimelineSchema.GetInt(
                            table,
                            step,
                            0,
                            BattleActionTimelineSchema.StepOwnerAliases) == action.rowId)
                    {
                        slots.Add(index);
                        owned.Add(step);
                    }
                }

                List<BattleActionTimelineRecordData> ordered = owned
                    .Select((record, originalIndex) => new { record, originalIndex })
                    .OrderBy(value => BattleActionTimelineSchema.GetDouble(
                        table,
                        value.record,
                        0d,
                        BattleActionTimelineSchema.TriggerTimeAliases))
                    .ThenBy(value => value.originalIndex)
                    .Select(value => value.record)
                    .ToList();
                for (int index = 0; index < slots.Count; index++)
                {
                    table.records[slots[index]] = ordered[index];
                }
            }
        }
        /// <summary>
        /// 规范化关键帧顺序。
        /// </summary>

        private void NormalizeKeyframeOrder()
        {
            foreach (BattleActionTimelineTableData table in Document.tables
                .Where(BattleActionTimelineSchema.IsKeyframeTable))
            {
                int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(table);
                if (ownerColumn < 0)
                {
                    continue;
                }

                List<int> slots = Enumerable.Range(0, table.records.Count).ToList();
                List<BattleActionTimelineRecordData> ordered = table.records
                    .Select((record, originalIndex) => new { record, originalIndex })
                    .OrderBy(value => ParseInt(value.record.cells[ownerColumn]))
                    .ThenBy(value => BattleActionTimelineSchema.GetDouble(
                        table,
                        value.record,
                        0d,
                        BattleActionTimelineSchema.LocalTimeAliases))
                    .ThenBy(value => value.originalIndex)
                    .Select(value => value.record)
                    .ToList();
                for (int index = 0; index < slots.Count; index++)
                {
                    table.records[slots[index]] = ordered[index];
                }
            }
        }

        private void NormalizeMovementKeyframeOrder()
        {
            foreach (BattleActionTimelineTableData table in Document.ActionTables())
            {
                foreach (BattleActionTimelineRecordData action in table.records)
                {
                    string source = BattleActionTimelineSchema.Get(
                        table,
                        action,
                        BattleActionTimelineSchema.MovementKeyframesAliases);
                    if (!BattleActionTimelineInlineStructCodec.TryParse(
                            source,
                            out List<Dictionary<string, string>> values,
                            out _)
                        || values.Count < 2)
                    {
                        continue;
                    }

                    values.Sort((left, right) =>
                        ReadInlineDouble(left, "LocalTime", 0d).CompareTo(
                            ReadInlineDouble(right, "LocalTime", 0d)));
                    SetMovementKeyframes(table, action, values);
                }
            }
        }

        private static void SetMovementKeyframes(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData action,
            IReadOnlyList<Dictionary<string, string>> values)
        {
            if (table == null || action == null || values == null)
            {
                return;
            }

            List<BattleActionTimelineRecordData> records = values.Select(value =>
                new BattleActionTimelineRecordData
                {
                    cells = new[]
                    {
                        "", "", ReadInline(value, "LocalTime", "0"),
                        ReadInline(value, "OffsetX", "0"),
                        ReadInline(value, "OffsetY", "0"),
                        ReadInline(value, "RotationDegrees", "0"),
                        ReadInline(value, "ScaleX", "1"),
                        ReadInline(value, "ScaleY", "1"),
                        ReadInline(value, "Interpolation", "Linear"),
                    },
                }).ToList();
            BattleActionTimelineSchema.Set(
                table,
                action,
                BattleActionTimelineInlineStructCodec.Serialize(
                    BattleActionTimelineDocument.MovementInlineFields(),
                    records),
                BattleActionTimelineSchema.MovementKeyframesAliases);
        }

        private void NormalizeShapeOrder()
        {
            foreach (BattleActionTimelineTableData table in Document.tables
                .Where(BattleActionTimelineSchema.IsShapeTable))
            {
                int ownerColumn = BattleActionTimelineSchema.FindColumn(
                    table,
                    BattleActionTimelineSchema.AttackBodyIdAliases);
                int indexColumn = BattleActionTimelineSchema.FindColumn(table, "shapeIndex", "index");
                if (ownerColumn < 0 || indexColumn < 0)
                {
                    continue;
                }

                foreach (IGrouping<int, BattleActionTimelineRecordData> group in table.records
                    .GroupBy(record => BattleActionTimelineSchema.GetInt(table, record, 0,
                        BattleActionTimelineSchema.AttackBodyIdAliases)))
                {
                    int index = 0;
                    foreach (BattleActionTimelineRecordData shape in group)
                    {
                        BattleActionTimelineSchema.SetInt(table, shape, index++, "shapeIndex", "index");
                    }
                }
            }

        }
        /// <summary>
        /// 创建记录。
        /// </summary>

        private static BattleActionTimelineRecordData CreateRecord(
            BattleActionTimelineTableData table)
        {
            int rowId = table.AllocateRowId();
            BattleActionTimelineRecordData record = new BattleActionTimelineRecordData
            {
                rowId = rowId,
                cells = new string[table.headers.Length],
            };
            for (int index = 0; index < record.cells.Length; index++)
            {
                record.cells[index] = string.Empty;
            }

            if (record.cells.Length > 0)
            {
                record.cells[0] = rowId.ToString(CultureInfo.InvariantCulture);
            }

            return record;
        }
        /// <summary>
        /// 资产新增/重导出后重建编辑器索引，不触发运行时配置加载或资源缓存。
        /// </summary>
        private void OnPreviewAssetsChanged()
        {
            filterCatalog = null;
            filterLanguageLoaded = false;
            filterLanguage = null;
            availablePreviewClips = null;
            automaticPreviewActionId = int.MinValue;
            automaticPreviewClip = null;
            automaticPreviewError = string.Empty;
            Repaint();
        }

        private int CurrentEditedActionAnimationId()
        {
            return TryGetSelectedAction(Document, out BattleActionTimelineTableData table,
                out BattleActionTimelineRecordData action)
                ? BattleActionTimelineSchema.GetInt(table, action, 0, "actionId")
                : 0;
        }

        private void SetPreviewInput(int actionId, SequenceFrameClip clip, int singleSkillId = 0)
        {
            Undo.RecordObject(authoringState, "输入动作 ID 或序列帧动画");
            authoringState.sequencePreviewActionId = actionId;
            authoringState.sequencePreviewClip = clip;
            authoringState.explicitSingleSkillId = singleSkillId;
            authoringState.filterApplied = false;
            NormalizeSelection();
            playing = false;
            playheadSeconds = 0d;
            QueueDraftSave();
            Repaint();
        }

        private SequenceFrameClip ResolveSequencePreviewClip(out string error)
        {
            error = string.Empty;
            if (authoringState.sequencePreviewClip != null)
            {
                return authoringState.sequencePreviewClip;
            }

            int actionId = authoringState.sequencePreviewActionId;
            if (automaticPreviewActionId != actionId)
            {
                automaticPreviewActionId = actionId;
                automaticPreviewClip = null;
                automaticPreviewError = string.Empty;
                if (actionId <= 0)
                {
                    automaticPreviewError = "请拖入序列帧动画，或填写动作 ID（例如 22）。";
                }
                else
                {
                    SequenceFrameClip[] matches = GetAvailablePreviewClips()
                        .Where(clip => clip != null && clip.ActionId == actionId).ToArray();
                    if (matches.Length == 1)
                    {
                        automaticPreviewClip = matches[0];
                    }
                    else
                    {
                        automaticPreviewError = matches.Length == 0
                            ? $"未找到 actionId={actionId} 的序列帧资源，请手动拖入刚生成的 .asset。"
                            : $"存在多个 actionId={actionId} 的序列帧资源，不能自动决定。请手动选择：\n" +
                                string.Join("\n", matches.Select(AssetDatabase.GetAssetPath));
                    }
                }
            }

            error = automaticPreviewError;
            return automaticPreviewClip;
        }

        private SequenceFrameClip[] GetAvailablePreviewClips()
        {
            return availablePreviewClips ??= AssetDatabase.FindAssets("t:SequenceFrameClip")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<SequenceFrameClip>)
                .Where(clip => clip != null).ToArray();
        }

        private bool TrySampleSequencePreview(
            SequenceFrameClip clip, out Sprite frame, out int frameIndex, out string error)
        {
            frame = null;
            frameIndex = -1;
            error = string.Empty;
            if (clip == null)
            {
                error = "拖入序列帧动画即可预览，不需要创建场景对象。";
                return false;
            }

            if (!BattleActionTimelineTime.IsFinite(clip.FrameRate) ||
                !clip.TryValidate(out error))
            {
                error = "无效序列帧资源：" + (string.IsNullOrEmpty(error) ? "FPS 不是有限数。" : error);
                return false;
            }

            frameIndex = BattleActionTimelineSequencePreview.SampleFrame(
                playheadSeconds, clip.FrameRate, clip.FrameCount, clip.Loop);
            frame = clip.GetFrame(frameIndex);
            if (frame == null)
            {
                error = "当前时间没有有效的序列帧 Sprite。";
                return false;
            }

            if (frame.texture == null || !BattleActionTimelineTime.IsFinite(frame.pixelsPerUnit) ||
                frame.pixelsPerUnit <= 0f || frame.bounds.size.y <= 0f)
            {
                error = "当前 Sprite 缺少纹理或有效的尺寸/PPU，未绘制错误预览。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 使用原始 Sprite 纹理而非低分辨率 AssetPreview；布局复现战斗播放器的 bounds 缩放/锚定。
        /// </summary>
        private void DrawSequencePreview(Rect viewport, Vector2 origin, float pixelsPerUnit)
        {
            SequenceFrameClip clip = ResolveSequencePreviewClip(out string lookupError);
            if (!TrySampleSequencePreview(clip, out Sprite frame, out int frameIndex, out string sampleError))
            {
                GUI.Label(new Rect(10f, 106f, viewport.width - 20f, 55f),
                    string.IsNullOrEmpty(lookupError) ? sampleError : lookupError,
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            GUI.Label(new Rect(10f, viewport.height - 48f, viewport.width - 20f, 20f),
                $"{clip.name} · 素材帧 {frameIndex + 1}/{clip.FrameCount} · {clip.FrameRate:0.###} FPS",
                EditorStyles.miniLabel);
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            // 本工具导出的帧为独立 FullRect Sprite。旋转/紧密图集不能当矩形纹理读取，明确提示而非错画。
            if (frame.packed && (frame.packingMode == SpritePackingMode.Tight ||
                frame.packingRotation != SpritePackingRotation.None))
            {
                GUI.Label(new Rect(10f, 106f, viewport.width - 20f, 38f),
                    "此帧位于旋转/紧密图集中，请选择帧图工具生成的原始 Sprite 资源预览。",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            Rect textureRect = frame.textureRect;
            bool flipX = clip.DefaultFacingLeft ^
                (authoringState.facing == (int)BattleActionTimelineFacing.Left);
            Rect drawRect = BattleActionTimelineSequencePreview.SpriteDrawRect(
                textureRect.size, frame.textureRectOffset, frame.pivot, frame.pixelsPerUnit,
                frame.bounds, authoringState.previewHeight,
                flipX, origin, pixelsPerUnit);
            Texture2D texture = frame.texture;
            Rect uv = new Rect(textureRect.x / texture.width, textureRect.y / texture.height,
                textureRect.width / texture.width, textureRect.height / texture.height);
            if (flipX)
            {
                uv.x += uv.width;
                uv.width = -uv.width;
            }

            GUI.DrawTextureWithTexCoords(drawRect, texture, uv, true);
        }
        /// <summary>
        /// 更新步骤帧。
        /// </summary>

        private void StepFrame(int direction)
        {
            playing = false;
            int frame = CurrentFrame() + direction;
            int maximum = BattleActionTimelineTime.SecondsToFrame(
                Math.Max(0d, CurrentPreviewDuration()),
                authoringState.frameRate);
            frame = Mathf.Clamp(frame, 0, maximum);
            playheadSeconds = Math.Min(CurrentPreviewDuration(),
                BattleActionTimelineTime.FrameToSeconds(frame, authoringState.frameRate));
            Repaint();
        }
        /// <summary>
        /// 构建当前帧并返回结果。
        /// </summary>

        private int CurrentFrame()
        {
            return BattleActionTimelineTime.SecondsToFrame(
                Math.Max(0d, playheadSeconds),
                authoringState.frameRate);
        }
        /// <summary>
        /// 构建当前`Duration`并返回结果。
        /// </summary>

        private double CurrentDuration()
        {
            return CurrentDuration(Document);
        }

        /// <summary>完整显示长素材，但不把素材时长写进技能配置。</summary>
        private double CurrentPreviewDuration()
        {
            SequenceFrameClip clip = ResolveSequencePreviewClip(out _);
            return clip != null && clip.FrameCount > 0 &&
                BattleActionTimelineTime.IsFinite(clip.FrameRate) && clip.FrameRate > 0f
                ? Math.Max(CurrentDuration(), clip.FrameCount / (double)clip.FrameRate)
                : CurrentDuration();
        }
        /// <summary>
        /// 根据 `document` 构建当前`Duration`并返回结果。
        /// </summary>

        private double CurrentDuration(BattleActionTimelineDocument document)
        {
            return TryGetSelectedAction(
                document,
                out BattleActionTimelineTableData table,
                out BattleActionTimelineRecordData action)
                ? Math.Max(
                    0d,
                    BattleActionTimelineSchema.ReadPhases(table, action).Duration)
                : 0d;
        }
        /// <summary>
        /// 更新限制播放头。
        /// </summary>

        private void ClampPlayhead()
        {
            playheadSeconds = Math.Max(
                0d,
                Math.Min(CurrentPreviewDuration(), playheadSeconds));
        }
        /// <summary>
        /// 根据 `value` 吸附非负数并返回结果。
        /// </summary>

        private double SnapNonNegative(double value)
        {
            return BattleActionTimelineTime.NormalizeEditedSeconds(
                value, authoringState.frameRate, authoringState.snapToFrames);
        }
        /// <summary>
        /// 根据 `value`、`maximum` 吸附并限制并返回结果。
        /// </summary>

        private double SnapAndClamp(double value, double maximum)
        {
            return Math.Max(0d, Math.Min(maximum, SnapNonNegative(value)));
        }
        /// <summary>
        /// 构建帧秒数并返回结果。
        /// </summary>

        private double FrameSeconds()
        {
            return BattleActionTimelineTime.FrameToSeconds(
                1,
                authoringState.frameRate);
        }

        private double MinimumActionDuration()
        {
            return BattleActionTimelineTime.MinimumDuration(authoringState.frameRate, authoringState.snapToFrames);
        }
        /// <summary>
        /// 取消拖拽。
        /// </summary>

        private void CancelDrag()
        {
            if (gizmoHotControl != 0 && GUIUtility.hotControl == gizmoHotControl)
                GUIUtility.hotControl = 0;
            gizmoHotControl = 0;
            gizmoStartShape = null;
            dragKind = DragKind.None;
            dragSheet = string.Empty;
            dragRowId = 0;
            dragMovementKeyframeIndex = -1;
            dragChanged = false;
        }
        /// <summary>
        /// 将时间转换为`X`。
        /// </summary>

        private static float TimeToX(double time, Rect rect, double duration)
        {
            return Mathf.Lerp(
                rect.x,
                rect.xMax,
                duration <= 0d ? 0f : Mathf.Clamp01((float)(time / duration)));
        }
        /// <summary>
        /// 将`X`转换为时间。
        /// </summary>

        private static double XToTime(float x, Rect rect, double duration)
        {
            return Mathf.InverseLerp(rect.x, rect.xMax, x) * duration;
        }
        /// <summary>
        /// 根据 `value`、`degrees` 旋转结果并返回结果。
        /// </summary>

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                cosine * value.x - sine * value.y,
                sine * value.x + cosine * value.y);
        }
        /// <summary>
        /// 解析整数。
        /// </summary>

        private static int ParseInt(string value)
        {
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result)
                ? result
                : 0;
        }

        private static string ReadInline(
            IReadOnlyDictionary<string, string> values,
            string key,
            string fallback)
        {
            return values != null && values.TryGetValue(key, out string value) &&
                !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        private static double ReadInlineDouble(
            IReadOnlyDictionary<string, string> values,
            string key,
            double fallback)
        {
            return double.TryParse(
                ReadInline(values, key, string.Empty),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) &&
                BattleActionTimelineTime.IsFinite(parsed)
                ? parsed
                : fallback;
        }
        /// <summary>
        /// 判断近似相等是否满足当前条件。
        /// </summary>

        private static bool NearlyEqual(double first, double second)
        {
            return Math.Abs(first - second) <= BattleActionTimelineTime.Epsilon;
        }
    }
}
#endif
