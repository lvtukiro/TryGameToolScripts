#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            PreviewKeyframeOffset,
            PreviewShapeOffset,
        }

        [Serializable]
        private sealed class AuthoringState : ScriptableObject
        {
            public BattleActionTimelineDocument document =
                new BattleActionTimelineDocument();
            public string baselineSignature = string.Empty;
            public string selectedActionSheet = string.Empty;
            public int selectedActionRowId;
            public string selectedStepSheet = string.Empty;
            public int selectedStepRowId;
            public string selectedSharedSheet = string.Empty;
            public int selectedSharedRowId;
            public float frameRate = 30f;
            public int facing = (int)BattleActionTimelineFacing.Right;
            public GameObject animationPreviewTarget;
            public AnimationClip animationPreviewClip;
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
            public int facing;
            public double playheadSeconds;
            public string workbookPath;
            public string workbookHash;
        }

        private sealed class ActionReference
        {
            public BattleActionTimelineTableData Table;
            public BattleActionTimelineRecordData Record;
            public string Label;
        }

        private sealed class ResolvedStep
        {
            public BattleActionTimelineTableData StepTable;
            public BattleActionTimelineRecordData Step;
            public double TriggerTime;
            public bool IsMelee;
            public bool IsProjectile;
            public BattleActionTimelineTableData OwnerTable;
            public BattleActionTimelineRecordData Owner;
            public BattleActionTimelineTableData ProjectileTable;
            public BattleActionTimelineRecordData Projectile;
            public BattleActionTimelineTableData BodyTable;
            public BattleActionTimelineRecordData Body;
            public BattleActionTimelineTableData KeyframeTable;
            public List<BattleActionTimelineRecordData> Keyframes =
                new List<BattleActionTimelineRecordData>();
            public double Lifetime;
            public Vector2 SpawnOffset;
        }

        private const float InspectorWidth = 430f;
        private const float TimelineHeight = 218f;
        private const float RulerHeight = 24f;
        private const float LaneHeight = 42f;
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
        private bool dragChanged;
        private bool ownsAnimationMode;
        private string animationPreviewStatus = string.Empty;

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

        private bool IsDirty => !string.Equals(
            Document.CanonicalSignature(),
            authoringState.baselineSignature ?? string.Empty,
            StringComparison.Ordinal);

        private string DraftKey => DraftKeyPrefix +
            Hash128.Compute(Application.dataPath).ToString();

        [MenuItem("TryGame/Battle/Action Timeline Editor", false, 432)]
        private static void Open()
        {
            BattleActionTimelineEditorWindow window =
                GetWindow<BattleActionTimelineEditorWindow>();
            window.titleContent = new GUIContent("Action Timeline");
            window.minSize = new Vector2(1080f, 680f);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureState();
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= SaveDraftNow;
            AssemblyReloadEvents.beforeAssemblyReload += SaveDraftNow;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            previousEditorTime = EditorApplication.timeSinceStartup;
            TryRestoreDraft();
            if (!draftRestored || Document.tables.Count == 0)
            {
                LoadOfficial(false, false);
            }

            ValidateDocument();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= SaveDraftNow;
            EditorApplication.update -= OnEditorUpdate;
            playing = false;
            StopAnimationPreview();
            SaveDraftNow();
        }

        private void OnDestroy()
        {
            StopAnimationPreview();
            SaveDraftNow();
            if (authoringState != null)
            {
                Undo.ClearUndo(authoringState);
                DestroyImmediate(authoringState);
                authoringState = null;
            }
        }

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
        }

        private void OnUndoRedo()
        {
            EnsureState();
            playing = false;
            CancelDrag();
            NormalizeSelection();
            ClampPlayhead();
            ValidateDocument();
            QueueDraftSave();
            Repaint();
        }

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

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            double delta = Math.Max(0d, Math.Min(0.25d, now - previousEditorTime));
            previousEditorTime = now;
            if (!playing)
            {
                return;
            }

            double duration = CurrentDuration();
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

            SampleAnimationPreview();
            Repaint();
        }

        private void OnGUI()
        {
            EnsureState();
            NormalizeSelection();
            DrawToolbar();

            Rect body = new Rect(0f, 24f, position.width, position.height - 24f);
            GUILayout.BeginArea(new Rect(body.x, body.y, InspectorWidth, body.height));
            DrawInspectorArea();
            GUILayout.EndArea();

            Rect content = new Rect(
                InspectorWidth + 6f,
                body.y + 6f,
                Mathf.Max(200f, body.width - InspectorWidth - 12f),
                Mathf.Max(200f, body.height - 12f));
            DrawTimelineAndPreview(content);
        }

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
                if (playheadSeconds >= CurrentDuration() -
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
                "帧 " + CurrentFrame().ToString(CultureInfo.InvariantCulture) +
                " / " + playheadSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s",
                EditorStyles.miniLabel,
                GUILayout.Width(112f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                IsDirty ? "● 草稿未写回" : "✓ 与读取基线一致",
                EditorStyles.miniLabel,
                GUILayout.Width(104f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInspectorArea()
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            EditorGUILayout.HelpBox(status, MessageType.None);
            if (workbookSnapshot == null || !workbookSnapshot.HasActionStructure)
            {
                DrawWaitingForStructure();
                DrawIssues();
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawSelectionAndViewSettings();
            BattleActionTimelineDocument edited = Document.Clone();
            EditorGUI.BeginChangeCheck();
            DrawActionFields(edited);
            DrawExecutionSteps(edited);
            DrawSharedConfiguration(edited);
            bool changed = EditorGUI.EndChangeCheck();
            if (changed)
            {
                Undo.RegisterCompleteObjectUndo(
                    authoringState,
                    "编辑 Battle Action Timeline");
                Document = edited;
                NormalizeStepOrderForSelectedAction();
                NormalizeKeyframeOrder();
                OnAuthoringChanged();
            }

            DrawIssues();
            EditorGUILayout.EndScrollView();
        }

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
                EditorGUILayout.LabelField("当前工作簿 Sheet", EditorStyles.boldLabel);
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

        private void DrawSelectionAndViewSettings()
        {
            List<ActionReference> actions = BuildActionReferences(Document);
            if (actions.Count == 0)
            {
                EditorGUILayout.HelpBox("工作集没有 ActiveSingle 数据行。", MessageType.Warning);
                return;
            }

            int selectedIndex = actions.FindIndex(value =>
                string.Equals(
                    value.Table.sheetName,
                    authoringState.selectedActionSheet,
                    StringComparison.Ordinal) &&
                value.Record.rowId == authoringState.selectedActionRowId);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            int newIndex = EditorGUILayout.Popup(
                "ActiveSingle（稳定 id）",
                selectedIndex,
                actions.Select(value => value.Label).ToArray());
            if (newIndex != selectedIndex ||
                authoringState.selectedActionRowId <= 0)
            {
                SelectAction(actions[Mathf.Clamp(newIndex, 0, actions.Count - 1)]);
            }

            DrawAnimationPreviewSettings();
            EditorGUILayout.BeginHorizontal();
            float frameRate = EditorGUILayout.FloatField(
                "预览 FPS",
                authoringState.frameRate);
            if (!Mathf.Approximately(frameRate, authoringState.frameRate) &&
                frameRate > 0f && !float.IsNaN(frameRate) &&
                !float.IsInfinity(frameRate))
            {
                Undo.RecordObject(authoringState, "修改动作时间轴 FPS");
                authoringState.frameRate = frameRate;
                playheadSeconds = SnapAndClamp(playheadSeconds, CurrentDuration());
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
        }

        private void DrawAnimationPreviewSettings()
        {
            GameObject target = (GameObject)EditorGUILayout.ObjectField(
                "动画预览对象（场景）",
                authoringState.animationPreviewTarget,
                typeof(GameObject),
                true);
            AnimationClip clip = (AnimationClip)EditorGUILayout.ObjectField(
                "动作动画（仅预览）",
                authoringState.animationPreviewClip,
                typeof(AnimationClip),
                false);
            if (target != authoringState.animationPreviewTarget ||
                clip != authoringState.animationPreviewClip)
            {
                StopAnimationPreview();
                Undo.RecordObject(authoringState, "修改动作动画预览");
                authoringState.animationPreviewTarget = target;
                authoringState.animationPreviewClip = clip;
                animationPreviewStatus = string.Empty;
                Repaint();
            }

            if (clip != null && target == null)
            {
                EditorGUILayout.HelpBox(
                    "已选择动画；再指定场景中的机器人对象即可按当前动作时钟采样。",
                    MessageType.Info);
            }
            else if (target != null && EditorUtility.IsPersistent(target))
            {
                EditorGUILayout.HelpBox(
                    "动画预览对象必须是场景实例，不能直接采样 Prefab 资源。",
                    MessageType.Warning);
            }
            else if (!string.IsNullOrWhiteSpace(animationPreviewStatus))
            {
                EditorGUILayout.HelpBox(animationPreviewStatus, MessageType.Warning);
            }

            EditorGUILayout.LabelField(
                "动画选择仅用于本次编辑器会话，不写入源表或项目草稿。",
                EditorStyles.wordWrappedMiniLabel);
        }

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
                "动作阶段 · " + table.sheetName + " / id=" + action.rowId,
                EditorStyles.boldLabel);
            BattleActionTimelinePhaseTimes phases =
                BattleActionTimelineSchema.ReadPhases(table, action);
            double startup = EditorGUILayout.DoubleField(
                "前摇结束 startupEndTime",
                phases.StartupEnd);
            double switchStart = EditorGUILayout.DoubleField(
                "共享切换窗开始",
                phases.SwitchWindowStart);
            double recovery = EditorGUILayout.DoubleField(
                "后摇开始 recoveryStartTime",
                phases.RecoveryStart);
            double duration = EditorGUILayout.DoubleField(
                "动作结束 actionDuration",
                phases.Duration);
            double currentTime = SnapNonNegative(playheadSeconds);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("前摇结束 = 当前帧"))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    currentTime,
                    BattleActionTimelineSchema.StartupEndAliases);
                GUI.changed = true;
            }

            if (GUILayout.Button("切换窗 = 当前帧"))
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
            if (GUILayout.Button("后摇开始 = 当前帧"))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    currentTime,
                    BattleActionTimelineSchema.RecoveryStartAliases);
                GUI.changed = true;
            }

            if (GUILayout.Button("动作结束 = 当前帧"))
            {
                BattleActionTimelineSchema.SetDouble(
                    table,
                    action,
                    Math.Max(FrameSeconds(), currentTime),
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
                    Math.Max(FrameSeconds(), SnapNonNegative(duration)),
                    BattleActionTimelineSchema.DurationAliases);
            }
        }

        private void DrawExecutionSteps(BattleActionTimelineDocument edited)
        {
            if (!TryGetSelectedAction(edited, out _, out BattleActionTimelineRecordData action))
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("executionStep 事件轨", EditorStyles.boldLabel);
            BattleActionTimelineTableData stepTable = edited.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsExecutionStepTable);
            if (stepTable == null)
            {
                EditorGUILayout.HelpBox("缺少 ExecutionStep Sheet。", MessageType.Warning);
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
                EditorGUILayout.BeginHorizontal();
                DrawSelectionButton(
                    "Step " + step.rowId,
                    string.Equals(
                        authoringState.selectedStepSheet,
                        stepTable.sheetName,
                        StringComparison.Ordinal) &&
                    authoringState.selectedStepRowId == step.rowId,
                    () => SelectStep(stepTable.sheetName, step.rowId));
                if (GUILayout.Button("删除", GUILayout.Width(42f)))
                {
                    stepTable.records.Remove(step);
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
                }

                EditorGUILayout.EndHorizontal();
                double trigger = BattleActionTimelineSchema.GetDouble(
                    stepTable,
                    step,
                    0d,
                    BattleActionTimelineSchema.TriggerTimeAliases);
                double editedTrigger = EditorGUILayout.DoubleField("triggerTime", trigger);
                string stepType = EditorGUILayout.TextField(
                    "stepType",
                    BattleActionTimelineSchema.Get(
                        stepTable,
                        step,
                        BattleActionTimelineSchema.StepTypeAliases));
                int configId = EditorGUILayout.IntField(
                    "stepConfigId",
                    BattleActionTimelineSchema.GetInt(
                        stepTable,
                        step,
                        0,
                        BattleActionTimelineSchema.StepConfigAliases));
                BattleActionTimelineSchema.SetDouble(
                    stepTable,
                    step,
                    SnapNonNegative(editedTrigger),
                    BattleActionTimelineSchema.TriggerTimeAliases);
                BattleActionTimelineSchema.Set(
                    stepTable,
                    step,
                    stepType,
                    BattleActionTimelineSchema.StepTypeAliases);
                BattleActionTimelineSchema.SetInt(
                    stepTable,
                    step,
                    configId,
                    BattleActionTimelineSchema.StepConfigAliases);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("+ 在当前帧新增 executionStep"))
            {
                BattleActionTimelineRecordData step = CreateRecord(stepTable);
                BattleActionTimelineSchema.SetInt(
                    stepTable,
                    step,
                    action.rowId,
                    BattleActionTimelineSchema.StepOwnerAliases);
                BattleActionTimelineSchema.SetDouble(
                    stepTable,
                    step,
                    SnapAndClamp(playheadSeconds, CurrentDuration(edited)),
                    BattleActionTimelineSchema.TriggerTimeAliases);
                BattleActionTimelineSchema.Set(
                    stepTable,
                    step,
                    "SpawnMeleeAttackBody",
                    BattleActionTimelineSchema.StepTypeAliases);
                BattleActionTimelineSchema.SetInt(
                    stepTable,
                    step,
                    1,
                    BattleActionTimelineSchema.StepConfigAliases);
                stepTable.records.Add(step);
                SelectStep(stepTable.sheetName, step.rowId);
                GUI.changed = true;
            }
        }

        private void DrawSharedConfiguration(BattleActionTimelineDocument edited)
        {
            ResolvedStep resolved = ResolveSelectedStep(edited);
            if (resolved == null)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(
                    "选择 executionStep 后可编辑它引用的共享记录。共享记录按正式 id " +
                    "独立保存，不会为当前动作复制。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("共享记录（不会随动作复制）", EditorStyles.boldLabel);
            if (resolved.Owner != null)
            {
                EditorGUILayout.HelpBox(
                    resolved.OwnerTable.sheetName + " id=" + resolved.Owner.rowId +
                    " 是全工作簿共享记录；修改会影响所有引用它的动作。",
                    MessageType.Warning);
                DrawOwnerFields(resolved);
                DrawProjectileMovementFields(edited, resolved);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "stepConfigId 在当前工作集中没有对应共享记录。",
                    MessageType.Error);
                return;
            }

            if (resolved.Body != null)
            {
                DrawAttackBody(resolved);
                DrawShapes(edited, resolved);
            }

            DrawKeyframes(edited, resolved);
        }

        private void DrawOwnerFields(ResolvedStep resolved)
        {
            BattleActionTimelineTableData table = resolved.OwnerTable;
            BattleActionTimelineRecordData owner = resolved.Owner;
            if (resolved.IsMelee)
            {
                double duration = EditorGUILayout.DoubleField(
                    "activeDuration",
                    BattleActionTimelineSchema.GetDouble(
                        table,
                        owner,
                        0d,
                        "activeDuration"));
                BattleActionTimelineSchema.SetDouble(
                    table,
                    owner,
                    SnapNonNegative(duration),
                    "activeDuration");
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
                    double lifetime = EditorGUILayout.DoubleField(
                        "maxLifetime",
                        BattleActionTimelineSchema.GetDouble(
                            resolved.ProjectileTable,
                            resolved.Projectile,
                            0d,
                            "maxLifetime"));
                    BattleActionTimelineSchema.SetDouble(
                        resolved.ProjectileTable,
                        resolved.Projectile,
                        SnapNonNegative(lifetime),
                        "maxLifetime");
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
                    int movementId = EditorGUILayout.IntField(
                        "movementConfigId",
                        BattleActionTimelineSchema.GetInt(
                            resolved.ProjectileTable,
                            resolved.Projectile,
                            0,
                            "movementConfigId"));
                    BattleActionTimelineSchema.SetInt(
                        resolved.ProjectileTable,
                        resolved.Projectile,
                        movementId,
                        "movementConfigId");
                }
            }
        }

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
            bool ballistic = movementType.IndexOf(
                    "Ballistic",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                movementType == "1";
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
                    " 共享移动记录。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(
                table.sheetName + " id=" + movement.rowId + "（共享移动记录）",
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

        private void DrawAttackBody(ResolvedStep resolved)
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
        }

        private void DrawShapes(
            BattleActionTimelineDocument edited,
            ResolvedStep resolved)
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
            EditorGUILayout.LabelField("组合 Shapes", EditorStyles.miniBoldLabel);
            foreach (BattleActionTimelineRecordData shape in shapes)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                DrawSelectionButton(
                    "Shape " + shape.rowId,
                    string.Equals(
                        authoringState.selectedSharedSheet,
                        table.sheetName,
                        StringComparison.Ordinal) &&
                    authoringState.selectedSharedRowId == shape.rowId,
                    () => SelectShared(table.sheetName, shape.rowId));
                if (GUILayout.Button("删除", GUILayout.Width(42f)))
                {
                    table.records.Remove(shape);
                    GUI.changed = true;
                }

                EditorGUILayout.EndHorizontal();
                string type = EditorGUILayout.TextField(
                    "shapeType",
                    BattleActionTimelineSchema.Get(table, shape, "shapeType"));
                Vector2 offset = EditorGUILayout.Vector2Field(
                    "localOffset",
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
                    "localRotationDegrees",
                    (float)BattleActionTimelineSchema.GetDouble(
                        table,
                        shape,
                        0d,
                        "rotationDegrees",
                        "localRotationDegrees"));
                Vector2 size = EditorGUILayout.Vector2Field(
                    "size (Box/Capsule)",
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
                    "radius (Circle)",
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
                        "capsuleDirection",
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
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Box"))
            {
                AddShape(table, resolved.Body.rowId, "Box");
                GUI.changed = true;
            }

            if (GUILayout.Button("+ Circle"))
            {
                AddShape(table, resolved.Body.rowId, "Circle");
                GUI.changed = true;
            }

            if (GUILayout.Button("+ Capsule"))
            {
                AddShape(table, resolved.Body.rowId, "Capsule");
                GUI.changed = true;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void AddShape(
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
            SelectShared(table.sheetName, shape.rowId);
        }

        private void DrawKeyframes(
            BattleActionTimelineDocument edited,
            ResolvedStep resolved)
        {
            if (resolved.KeyframeTable == null)
            {
                EditorGUILayout.HelpBox(
                    "当前共享记录没有可识别的 TransformKeyframe Sheet。",
                    MessageType.Info);
                return;
            }

            BattleActionTimelineTableData table = resolved.KeyframeTable;
            int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(table);
            EditorGUILayout.LabelField("transform 关键帧轨", EditorStyles.miniBoldLabel);
            foreach (BattleActionTimelineRecordData keyframe in resolved.Keyframes)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                DrawSelectionButton(
                    "Key " + keyframe.rowId,
                    string.Equals(
                        authoringState.selectedSharedSheet,
                        table.sheetName,
                        StringComparison.Ordinal) &&
                    authoringState.selectedSharedRowId == keyframe.rowId,
                    () => SelectShared(table.sheetName, keyframe.rowId));
                if (GUILayout.Button("删除", GUILayout.Width(42f)))
                {
                    table.records.Remove(keyframe);
                    GUI.changed = true;
                }

                EditorGUILayout.EndHorizontal();
                double localTime = EditorGUILayout.DoubleField(
                    "localTime",
                    BattleActionTimelineSchema.GetDouble(
                        table,
                        keyframe,
                        0d,
                        BattleActionTimelineSchema.LocalTimeAliases));
                Vector2 offset = EditorGUILayout.Vector2Field(
                    "offset",
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
                    "rotationDegrees",
                    (float)BattleActionTimelineSchema.GetDouble(
                        table,
                        keyframe,
                        0d,
                        BattleActionTimelineSchema.RotationAliases));
                Vector2 scale = EditorGUILayout.Vector2Field(
                    "scale",
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
                    "interpolation",
                    BattleActionTimelineSchema.Get(
                        table,
                        keyframe,
                        BattleActionTimelineSchema.InterpolationAliases));
                BattleActionTimelineSchema.SetDouble(
                    table,
                    keyframe,
                    Math.Max(0d, Math.Min(resolved.Lifetime, SnapNonNegative(localTime))),
                    BattleActionTimelineSchema.LocalTimeAliases);
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

            if (ownerColumn >= 0 && GUILayout.Button("+ 在当前帧新增 transform keyframe"))
            {
                BattleActionTimelineRecordData keyframe = CreateRecord(table);
                keyframe.cells[ownerColumn] = resolved.Owner.rowId.ToString(
                    CultureInfo.InvariantCulture);
                double local = Math.Max(
                    0d,
                    Math.Min(resolved.Lifetime, playheadSeconds - resolved.TriggerTime));
                BattleActionTimelineSchema.SetDouble(
                    table,
                    keyframe,
                    SnapNonNegative(local),
                    BattleActionTimelineSchema.LocalTimeAliases);
                BattleActionTimelineSchema.SetDouble(
                    table,
                    keyframe,
                    1d,
                    BattleActionTimelineSchema.ScaleXAliases);
                BattleActionTimelineSchema.SetDouble(
                    table,
                    keyframe,
                    1d,
                    BattleActionTimelineSchema.ScaleYAliases);
                BattleActionTimelineSchema.Set(
                    table,
                    keyframe,
                    "Linear",
                    BattleActionTimelineSchema.InterpolationAliases);
                table.records.Add(keyframe);
                SelectShared(table.sheetName, keyframe.rowId);
                GUI.changed = true;
            }
        }

        private static void DrawSelectionButton(
            string label,
            bool selected,
            Action onClick)
        {
            Color previousColor = GUI.backgroundColor;
            bool previousChanged = GUI.changed;
            if (selected)
            {
                GUI.backgroundColor = new Color(0.35f, 0.72f, 1f, 1f);
            }

            if (GUILayout.Button(label))
            {
                onClick?.Invoke();
            }

            GUI.changed = previousChanged;
            GUI.backgroundColor = previousColor;
        }

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
                    "当前动作工作集可安全写回源 xlsx；本工具不会调用导表。",
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

        private void DrawTimelineAndPreview(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            if (!TryGetSelectedAction(Document, out _, out _))
            {
                GUI.Label(
                    new Rect(rect.x + 16f, rect.y + 16f, rect.width - 32f, 60f),
                    "读取包含 ActiveSingle 的正式源表后，这里会显示阶段、事件、" +
                    "攻击体存活和 transform 关键帧四条独立轨道。",
                    EditorStyles.wordWrappedLabel);
                return;
            }

            SampleAnimationPreview();
            Rect timeline = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f,
                Mathf.Min(TimelineHeight, rect.height * 0.43f));
            Rect preview = new Rect(
                rect.x + 8f,
                timeline.yMax + 8f,
                rect.width - 16f,
                Mathf.Max(120f, rect.yMax - timeline.yMax - 16f));
            DrawTimeline(timeline);
            DrawPreview(preview);
        }

        private void DrawTimeline(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.045f, 0.058f, 0.075f, 1f));
            Rect content = new Rect(rect.x + 112f, rect.y, rect.width - 120f, rect.height);
            double duration = Math.Max(FrameSeconds(), CurrentDuration());
            DrawRuler(content, duration);
            Rect stageLane = LaneRect(content, 0);
            Rect stepLane = LaneRect(content, 1);
            Rect lifeLane = LaneRect(content, 2);
            Rect keyLane = LaneRect(content, 3);
            DrawLaneLabel(rect, stageLane, "动作阶段");
            DrawLaneLabel(rect, stepLane, "executionStep");
            DrawLaneLabel(rect, lifeLane, "攻击体存活");
            DrawLaneLabel(rect, keyLane, "transform 关键帧");
            DrawStageLane(stageLane, duration);
            DrawStepLane(stepLane, duration);
            DrawLifetimeLane(lifeLane, duration);
            DrawKeyframeLane(keyLane, duration);

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
                CurrentFrame().ToString(CultureInfo.InvariantCulture),
                EditorStyles.miniLabel);
            HandleTimelineInput(content, stageLane, stepLane, keyLane, duration);
        }

        private static Rect LaneRect(Rect content, int index)
        {
            return new Rect(
                content.x,
                content.y + RulerHeight + index * LaneHeight,
                content.width,
                LaneHeight - 2f);
        }

        private static void DrawLaneLabel(Rect outer, Rect lane, string label)
        {
            GUI.Label(
                new Rect(outer.x + 8f, lane.y + 10f, 98f, 20f),
                label,
                EditorStyles.miniBoldLabel);
        }

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
                        selected ? Color.white : new Color(1f, 0.55f, 0.18f, 1f),
                        selected ? 7f : 5f);
                    GUI.Label(
                        new Rect(TimeToX(time, lane, duration) + 6f, lane.y + 3f, 58f, 18f),
                        step.rowId.ToString(CultureInfo.InvariantCulture),
                        EditorStyles.miniLabel);
                }
            }
        }

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
                    EditorGUI.DrawRect(
                        bar,
                        resolved.IsProjectile
                            ? new Color(0.26f, 0.72f, 0.95f, 0.72f)
                            : new Color(0.35f, 0.9f, 0.48f, 0.72f));
                    if (bar.width > 42f)
                    {
                        GUI.Label(
                            bar,
                            (resolved.IsProjectile ? "Projectile " : "Melee ") +
                            resolved.Owner.rowId,
                            EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }
        }

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

        private void HandleTimelineInput(
            Rect content,
            Rect stageLane,
            Rect stepLane,
            Rect keyLane,
            double duration)
        {
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
                    OnAuthoringChanged();
                }

                CancelDrag();
                current.Use();
            }
        }

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

        private void ApplyTimelineDrag(double absoluteTime)
        {
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

        private void DrawPreview(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.035f, 0.045f, 0.06f, 1f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            float pixelsPerUnit = Mathf.Clamp(
                Mathf.Min(rect.width, rect.height) / 10f,
                28f,
                72f);
            Vector2 origin = new Vector2(rect.center.x, rect.center.y + rect.height * 0.18f);
            DrawPreviewGrid(rect, origin, pixelsPerUnit);
            GUI.Label(
                new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 20f),
                "CombatOrigin 预览 · " +
                (authoringState.facing == (int)BattleActionTimelineFacing.Left
                    ? "朝左镜像"
                    : "朝右配置") +
                " · 白色拖关键帧原点，黄色拖选中 Shape",
                EditorStyles.whiteLabel);

            ResolvedStep resolved = ResolveSelectedStep(Document);
            if (resolved == null || resolved.Body == null)
            {
                GUI.Label(
                    new Rect(rect.x + 12f, rect.y + 38f, rect.width - 24f, 42f),
                    "选择 Melee/Projectile executionStep 后显示组合攻击形状。",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            double localTime = playheadSeconds - resolved.TriggerTime;
            bool active = localTime >= -BattleActionTimelineTime.Epsilon &&
                localTime <= resolved.Lifetime + BattleActionTimelineTime.Epsilon;
            localTime = Math.Max(0d, Math.Min(resolved.Lifetime, localTime));
            BattleActionTimelineTransform transform =
                BattleActionTimelineSchema.EvaluateKeyframes(
                    resolved.KeyframeTable,
                    resolved.Keyframes,
                    localTime);
            Vector2 ownerOffset = resolved.IsProjectile
                ? ProjectilePreviewOffset(Document, resolved, localTime)
                : Vector2.zero;
            int facingSign = authoringState.facing ==
                (int)BattleActionTimelineFacing.Left ? -1 : 1;
            Vector2 previewOwnerOffset = ownerOffset;
            previewOwnerOffset.x *= facingSign;
            Vector2 transformOffset = transform.Offset;
            transformOffset.x *= facingSign;
            float transformRotation = transform.RotationDegrees * facingSign;
            Vector2 bodyOrigin = origin + new Vector2(
                (previewOwnerOffset.x + transformOffset.x) * pixelsPerUnit,
                -(previewOwnerOffset.y + transformOffset.y) * pixelsPerUnit);

            BattleActionTimelineTableData shapeTable = Document.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsShapeTable);
            if (shapeTable != null)
            {
                foreach (BattleActionTimelineRecordData shape in shapeTable.records.Where(
                    value => BattleActionTimelineSchema.GetInt(
                        shapeTable,
                        value,
                        0,
                        BattleActionTimelineSchema.AttackBodyIdAliases) ==
                        resolved.Body.rowId))
                {
                    Vector2 shapeCenter = DrawPreviewShape(
                        shapeTable,
                        shape,
                        bodyOrigin,
                        transform,
                        transformRotation,
                        facingSign,
                        pixelsPerUnit,
                        active ? 1f : 0.3f);
                    bool selected = string.Equals(
                            authoringState.selectedSharedSheet,
                            shapeTable.sheetName,
                            StringComparison.Ordinal) &&
                        authoringState.selectedSharedRowId == shape.rowId;
                    if (selected)
                    {
                        EditorGUI.DrawRect(
                            new Rect(
                                shapeCenter.x - 4f,
                                shapeCenter.y - 4f,
                                8f,
                                8f),
                            new Color(1f, 0.82f, 0.15f, 1f));
                        HandlePreviewShapeOffsetDrag(
                            rect,
                            shapeCenter,
                            bodyOrigin,
                            transform,
                            shapeTable,
                            shape,
                            pixelsPerUnit,
                            facingSign);
                    }
                }
            }

            EditorGUI.DrawRect(
                new Rect(bodyOrigin.x - 4f, bodyOrigin.y - 4f, 8f, 8f),
                Color.white);
            HandlePreviewOffsetDrag(
                rect,
                bodyOrigin,
                origin,
                ownerOffset,
                resolved,
                pixelsPerUnit,
                facingSign);
            GUI.Label(
                new Rect(rect.x + 10f, rect.yMax - 25f, rect.width - 20f, 18f),
                (active ? "ACTIVE" : "inactive") +
                " · localTime=" + localTime.ToString("0.###", CultureInfo.InvariantCulture) +
                " · shared AttackBody=" + resolved.Body.rowId,
                EditorStyles.miniLabel);
        }

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

        private static Vector2 DrawPreviewShape(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData shape,
            Vector2 bodyOrigin,
            BattleActionTimelineTransform transform,
            float transformRotation,
            int facingSign,
            float pixelsPerUnit,
            float alpha)
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
            localOffset = Vector2.Scale(localOffset, transform.Scale);
            localOffset = Rotate(localOffset, transform.RotationDegrees);
            localOffset.x *= facingSign;
            Vector2 center = bodyOrigin + new Vector2(
                localOffset.x * pixelsPerUnit,
                -localOffset.y * pixelsPerUnit);
            float localRotation = (float)BattleActionTimelineSchema.GetDouble(
                table,
                shape,
                0d,
                "rotationDegrees",
                "localRotationDegrees");
            float rotation = -(transformRotation + localRotation * facingSign);
            string type = BattleActionTimelineSchema.Get(table, shape, "shapeType");
            Color fill = new Color(1f, 0.25f, 0.2f, 0.22f * alpha);
            Color outline = new Color(1f, 0.55f, 0.35f, 0.95f * alpha);
            if (type.IndexOf("Circle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type == "1")
            {
                float radius = (float)BattleActionTimelineSchema.GetDouble(
                    table,
                    shape,
                    0.5d,
                    "radius") *
                    Mathf.Max(Mathf.Abs(transform.Scale.x), Mathf.Abs(transform.Scale.y)) *
                    pixelsPerUnit;
                Handles.BeginGUI();
                Handles.color = fill;
                Handles.DrawSolidDisc(center, Vector3.forward, radius);
                Handles.color = outline;
                Handles.DrawWireDisc(center, Vector3.forward, radius);
                Handles.EndGUI();
                return center;
            }

            Vector2 size = new Vector2(
                (float)BattleActionTimelineSchema.GetDouble(
                    table,
                    shape,
                    1d,
                    "width",
                    "sizeX") * Mathf.Abs(transform.Scale.x) * pixelsPerUnit,
                (float)BattleActionTimelineSchema.GetDouble(
                    table,
                    shape,
                    1d,
                    "height",
                    "sizeY") * Mathf.Abs(transform.Scale.y) * pixelsPerUnit);
            if (type.IndexOf("Capsule", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type == "2")
            {
                string direction = BattleActionTimelineSchema.Get(
                    table,
                    shape,
                    "capsuleDirection",
                    "direction");
                bool horizontal = direction.IndexOf(
                        "Horizontal",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    direction == "1";
                Vector3[] capsulePoints = BuildCapsulePoints(size, horizontal)
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
                return center;
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
            return center;
        }

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

        private void HandlePreviewShapeOffsetDrag(
            Rect rect,
            Vector2 shapeCenter,
            Vector2 bodyOrigin,
            BattleActionTimelineTransform transform,
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData shape,
            float pixelsPerUnit,
            int facingSign)
        {
            Event current = Event.current;
            Rect handle = new Rect(
                shapeCenter.x - 8f,
                shapeCenter.y - 8f,
                16f,
                16f);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.MoveArrow);
            if (current.type == EventType.MouseDown && current.button == 0 &&
                handle.Contains(current.mousePosition))
            {
                playing = false;
                dragKind = DragKind.PreviewShapeOffset;
                dragSheet = table.sheetName;
                dragRowId = shape.rowId;
                dragChanged = false;
                Undo.RegisterCompleteObjectUndo(
                    authoringState,
                    "拖动攻击组合 Shape offset");
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 &&
                dragKind == DragKind.PreviewShapeOffset &&
                string.Equals(dragSheet, table.sheetName, StringComparison.Ordinal) &&
                dragRowId == shape.rowId && rect.Contains(current.mousePosition))
            {
                Vector2 transformed = new Vector2(
                    (current.mousePosition.x - bodyOrigin.x) / pixelsPerUnit,
                    -(current.mousePosition.y - bodyOrigin.y) / pixelsPerUnit);
                transformed.x *= facingSign;
                Vector2 scaledLocal = Rotate(
                    transformed,
                    -transform.RotationDegrees);
                double x = BattleActionTimelineSchema.GetDouble(
                    table,
                    shape,
                    0d,
                    "offsetX",
                    "localOffsetX");
                double y = BattleActionTimelineSchema.GetDouble(
                    table,
                    shape,
                    0d,
                    "offsetY",
                    "localOffsetY");
                if (Mathf.Abs(transform.Scale.x) > Mathf.Epsilon)
                {
                    x = scaledLocal.x / transform.Scale.x;
                }

                if (Mathf.Abs(transform.Scale.y) > Mathf.Epsilon)
                {
                    y = scaledLocal.y / transform.Scale.y;
                }

                BattleActionTimelineSchema.SetDouble(
                    table,
                    shape,
                    x,
                    "offsetX",
                    "localOffsetX");
                BattleActionTimelineSchema.SetDouble(
                    table,
                    shape,
                    y,
                    "offsetY",
                    "localOffsetY");
                dragChanged = true;
                ValidateDocument();
                QueueDraftSave();
                Repaint();
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 &&
                dragKind == DragKind.PreviewShapeOffset &&
                string.Equals(dragSheet, table.sheetName, StringComparison.Ordinal) &&
                dragRowId == shape.rowId)
            {
                if (dragChanged)
                {
                    OnAuthoringChanged();
                }

                CancelDrag();
                current.Use();
            }
        }

        private void HandlePreviewOffsetDrag(
            Rect rect,
            Vector2 bodyOrigin,
            Vector2 combatOrigin,
            Vector2 ownerOffset,
            ResolvedStep resolved,
            float pixelsPerUnit,
            int facingSign)
        {
            BattleActionTimelineRecordData selectedKeyframe =
                resolved.KeyframeTable?.records.FirstOrDefault(value =>
                    value.rowId == authoringState.selectedSharedRowId &&
                    string.Equals(
                        resolved.KeyframeTable.sheetName,
                        authoringState.selectedSharedSheet,
                        StringComparison.Ordinal));
            if (selectedKeyframe == null)
            {
                return;
            }

            Event current = Event.current;
            Rect handle = new Rect(bodyOrigin.x - 8f, bodyOrigin.y - 8f, 16f, 16f);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.MoveArrow);
            if (current.type == EventType.MouseDown && current.button == 0 &&
                handle.Contains(current.mousePosition))
            {
                playing = false;
                dragKind = DragKind.PreviewKeyframeOffset;
                dragSheet = resolved.KeyframeTable.sheetName;
                dragRowId = selectedKeyframe.rowId;
                dragChanged = false;
                Undo.RegisterCompleteObjectUndo(
                    authoringState,
                    "拖动攻击体关键帧 offset");
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 &&
                dragKind == DragKind.PreviewKeyframeOffset && rect.Contains(current.mousePosition))
            {
                BattleActionTimelineTableData table = Document.FindTable(dragSheet);
                BattleActionTimelineRecordData keyframe = table?.records.FirstOrDefault(
                    value => value.rowId == dragRowId);
                if (keyframe != null)
                {
                    Vector2 relative = new Vector2(
                        (current.mousePosition.x - combatOrigin.x) / pixelsPerUnit,
                        -(current.mousePosition.y - combatOrigin.y) / pixelsPerUnit);
                    relative.x *= facingSign;
                    relative -= ownerOffset;
                    BattleActionTimelineSchema.SetDouble(
                        table,
                        keyframe,
                        relative.x,
                        BattleActionTimelineSchema.OffsetXAliases);
                    BattleActionTimelineSchema.SetDouble(
                        table,
                        keyframe,
                        relative.y,
                        BattleActionTimelineSchema.OffsetYAliases);
                    dragChanged = true;
                    ValidateDocument();
                    QueueDraftSave();
                    Repaint();
                }

                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 &&
                dragKind == DragKind.PreviewKeyframeOffset)
            {
                if (dragChanged)
                {
                    OnAuthoringChanged();
                }

                CancelDrag();
                current.Use();
            }
        }

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

        private ResolvedStep ResolveSelectedStep(BattleActionTimelineDocument document)
        {
            BattleActionTimelineTableData table =
                document.FindTable(authoringState.selectedStepSheet);
            BattleActionTimelineRecordData step = table?.records.FirstOrDefault(
                value => value.rowId == authoringState.selectedStepRowId);
            return step == null ? null : ResolveStep(document, table, step);
        }

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
                IsMelee = BattleActionTimelineSchema.StepIsMelee(stepTable, step),
                IsProjectile = BattleActionTimelineSchema.StepIsProjectile(stepTable, step),
            };
            int configId = BattleActionTimelineSchema.GetInt(
                stepTable,
                step,
                0,
                BattleActionTimelineSchema.StepConfigAliases);
            int attackBodyId = 0;
            int keyframeOwnerId = 0;
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
                    keyframeOwnerId = result.Owner.rowId;
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
                        keyframeOwnerId = result.Projectile.rowId;
                    }
                }
            }

            result.BodyTable = document.tables.FirstOrDefault(
                BattleActionTimelineSchema.IsAttackBodyTable);
            result.Body = BattleActionTimelineSchema.FindById(
                result.BodyTable,
                attackBodyId);
            result.KeyframeTable = FindKeyframeTable(
                document,
                result.IsProjectile,
                keyframeOwnerId);
            if (result.KeyframeTable != null && keyframeOwnerId > 0)
            {
                int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(
                    result.KeyframeTable);
                if (ownerColumn >= 0)
                {
                    result.Keyframes = result.KeyframeTable.records.Where(record =>
                        int.TryParse(
                            record.cells[ownerColumn],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int ownerId) && ownerId == keyframeOwnerId).ToList();
                }
            }

            return result;
        }

        private static BattleActionTimelineTableData FindKeyframeTable(
            BattleActionTimelineDocument document,
            bool projectile,
            int ownerId)
        {
            List<BattleActionTimelineTableData> candidates = document.tables
                .Where(BattleActionTimelineSchema.IsKeyframeTable)
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            string token = projectile ? "projectile" : "melee";
            BattleActionTimelineTableData named = candidates.FirstOrDefault(table =>
                table.sheetName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
            if (named != null)
            {
                return named;
            }

            if (ownerId > 0)
            {
                BattleActionTimelineTableData containing = candidates.FirstOrDefault(table =>
                {
                    int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(table);
                    return ownerColumn >= 0 && table.records.Any(record =>
                        int.TryParse(
                            record.cells[ownerColumn],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int value) && value == ownerId);
                });
                if (containing != null)
                {
                    return containing;
                }
            }

            return candidates.Count == 1 ? candidates[0] : null;
        }

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
            Undo.ClearUndo(authoringState);
            ClearDraft();
            SelectFirstActionIfNecessary();
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

        private void WriteOfficial()
        {
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
                "本操作不会导表，生成目录保持不变。",
                "仅写回源 xlsx",
                "取消"))
            {
                return;
            }

            if (!BattleActionTimelineWorkbookBridge.TryWrite(
                    workbookSnapshot,
                    writeSet,
                    out string backupPath,
                    out string error))
            {
                status = error;
                Debug.LogError("[BattleActionTimeline] " + error);
                ShowNotification(new GUIContent("动作源表写回失败；草稿已保留"));
                QueueDraftSave();
                return;
            }

            int selectedActionId = authoringState.selectedActionRowId;
            string selectedActionSheet = authoringState.selectedActionSheet;
            if (!BattleActionTimelineWorkbookBridge.TryLoad(
                    out BattleActionTimelineWorkbookSnapshot refreshed,
                    out string reloadError))
            {
                workbookSnapshot = null;
                status = "源 xlsx 已安全写回，但写后重新读取失败：" + reloadError +
                    "；备份：" + backupPath;
                return;
            }

            workbookSnapshot = refreshed;
            Document = BattleActionTimelineDocument.FromSnapshot(refreshed);
            authoringState.baselineSignature = Document.CanonicalSignature();
            authoringState.selectedActionSheet = selectedActionSheet;
            authoringState.selectedActionRowId = selectedActionId;
            NormalizeSelection();
            Undo.ClearUndo(authoringState);
            ClearDraft();
            status = "动作源 xlsx 已原子写回并回读通过；未执行导表。备份：" + backupPath;
            ShowNotification(new GUIContent("源 xlsx 写回成功；请最后统一导表"));
            Repaint();
        }

        private void ValidateDocument()
        {
            BattleActionTimelineSchema.Validate(Document, validationIssues);
        }

        private void OnAuthoringChanged()
        {
            ClampPlayhead();
            ValidateDocument();
            QueueDraftSave();
            Repaint();
        }

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
                facing = authoringState.facing,
                playheadSeconds = playheadSeconds,
                workbookPath = workbookSnapshot?.WorkbookPath ?? string.Empty,
                workbookHash = workbookSnapshot?.ContentHash ?? string.Empty,
            };
            EditorPrefs.SetString(DraftKey, JsonUtility.ToJson(envelope));
        }

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
                authoringState.selectedActionSheet = envelope.selectedActionSheet ?? string.Empty;
                authoringState.selectedActionRowId = envelope.selectedActionRowId;
                authoringState.selectedStepSheet = envelope.selectedStepSheet ?? string.Empty;
                authoringState.selectedStepRowId = envelope.selectedStepRowId;
                authoringState.selectedSharedSheet = envelope.selectedSharedSheet ?? string.Empty;
                authoringState.selectedSharedRowId = envelope.selectedSharedRowId;
                authoringState.frameRate = envelope.frameRate > 0f ? envelope.frameRate : 30f;
                authoringState.facing = envelope.facing ==
                    (int)BattleActionTimelineFacing.Left
                    ? envelope.facing
                    : (int)BattleActionTimelineFacing.Right;
                playheadSeconds = Math.Max(0d, envelope.playheadSeconds);

                if (!string.IsNullOrWhiteSpace(envelope.workbookHash) &&
                    BattleActionTimelineWorkbookBridge.TryLoad(
                        out BattleActionTimelineWorkbookSnapshot current,
                        out _) &&
                    string.Equals(
                        current.WorkbookPath,
                        envelope.workbookPath,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        current.ContentHash,
                        envelope.workbookHash,
                        StringComparison.Ordinal))
                {
                    workbookSnapshot = current;
                    status = "已恢复动作草稿，并绑定未变化的正式 workbook 快照。";
                }
                else
                {
                    workbookSnapshot = null;
                    status =
                        "已恢复动作草稿，但正式机器人工作簿已变化。草稿保持只读安全状态；" +
                        "请人工确认后重新读取，工具不会覆盖新源表。";
                }

                NormalizeSelection();
                ClampPlayhead();
            }
            catch (Exception exception)
            {
                Debug.LogError("[BattleActionTimeline] 草稿恢复失败：" + exception);
            }
        }

        private void ClearDraft()
        {
            EditorPrefs.DeleteKey(DraftKey);
        }

        private void NormalizeSelection()
        {
            EnsureState();
            SelectFirstActionIfNecessary();
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

        private void SelectFirstActionIfNecessary()
        {
            List<ActionReference> actions = BuildActionReferences(Document);
            if (actions.Count == 0)
            {
                authoringState.selectedActionSheet = string.Empty;
                authoringState.selectedActionRowId = 0;
                return;
            }

            if (!actions.Any(value =>
                string.Equals(
                    value.Table.sheetName,
                    authoringState.selectedActionSheet,
                    StringComparison.Ordinal) &&
                value.Record.rowId == authoringState.selectedActionRowId))
            {
                SelectAction(actions[0]);
            }
        }

        private static List<ActionReference> BuildActionReferences(
            BattleActionTimelineDocument document)
        {
            List<ActionReference> result = new List<ActionReference>();
            foreach (BattleActionTimelineTableData table in document.ActionTables())
            {
                foreach (BattleActionTimelineRecordData record in table.records)
                {
                    result.Add(new ActionReference
                    {
                        Table = table,
                        Record = record,
                        Label = BattleActionTimelineSchema.DisplayName(table, record),
                    });
                }
            }

            return result
                .OrderBy(value => value.Record.rowId)
                .ThenBy(value => value.Table.sheetName, StringComparer.Ordinal)
                .ToList();
        }

        private void SelectAction(ActionReference action)
        {
            authoringState.selectedActionSheet = action.Table.sheetName;
            authoringState.selectedActionRowId = action.Record.rowId;
            authoringState.selectedStepSheet = string.Empty;
            authoringState.selectedStepRowId = 0;
            authoringState.selectedSharedSheet = string.Empty;
            authoringState.selectedSharedRowId = 0;
            playheadSeconds = 0d;
            playing = false;
            QueueDraftSave();
            Repaint();
        }

        private void SelectStep(string sheetName, int rowId)
        {
            authoringState.selectedStepSheet = sheetName ?? string.Empty;
            authoringState.selectedStepRowId = rowId;
            authoringState.selectedSharedSheet = string.Empty;
            authoringState.selectedSharedRowId = 0;
            QueueDraftSave();
            Repaint();
        }

        private void SelectShared(string sheetName, int rowId)
        {
            authoringState.selectedSharedSheet = sheetName ?? string.Empty;
            authoringState.selectedSharedRowId = rowId;
            QueueDraftSave();
            Repaint();
        }

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

        private void SampleAnimationPreview()
        {
            if (authoringState == null ||
                authoringState.animationPreviewTarget == null ||
                authoringState.animationPreviewClip == null)
            {
                StopAnimationPreview();
                return;
            }

            if (EditorUtility.IsPersistent(authoringState.animationPreviewTarget))
            {
                StopAnimationPreview();
                animationPreviewStatus =
                    "动画预览对象必须是场景实例，当前未执行采样。";
                return;
            }

            if (!ownsAnimationMode && AnimationMode.InAnimationMode())
            {
                animationPreviewStatus =
                    "Unity 当前已有其它 Animation Mode 会话；关闭后再使用动作时间轴采样。";
                return;
            }

            try
            {
                if (!AnimationMode.InAnimationMode())
                {
                    AnimationMode.StartAnimationMode();
                    ownsAnimationMode = true;
                }

                if (!ownsAnimationMode)
                {
                    return;
                }

                float sampleTime = Mathf.Clamp(
                    (float)playheadSeconds,
                    0f,
                    Mathf.Max(0f, authoringState.animationPreviewClip.length));
                AnimationMode.BeginSampling();
                try
                {
                    AnimationMode.SampleAnimationClip(
                        authoringState.animationPreviewTarget,
                        authoringState.animationPreviewClip,
                        sampleTime);
                }
                finally
                {
                    AnimationMode.EndSampling();
                }

                animationPreviewStatus =
                    "正在按动作时钟采样 " +
                    authoringState.animationPreviewClip.name + " @ " +
                    sampleTime.ToString("0.###", CultureInfo.InvariantCulture) + "s";
            }
            catch (Exception exception)
            {
                StopAnimationPreview();
                animationPreviewStatus = "动画采样失败：" + exception.Message;
            }
        }

        private void StopAnimationPreview()
        {
            if (!ownsAnimationMode)
            {
                return;
            }

            try
            {
                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }
            }
            finally
            {
                ownsAnimationMode = false;
            }
        }

        private void StepFrame(int direction)
        {
            playing = false;
            int frame = CurrentFrame() + direction;
            int maximum = BattleActionTimelineTime.SecondsToFrame(
                Math.Max(0d, CurrentDuration()),
                authoringState.frameRate);
            frame = Mathf.Clamp(frame, 0, maximum);
            playheadSeconds = BattleActionTimelineTime.FrameToSeconds(
                frame,
                authoringState.frameRate);
            Repaint();
        }

        private int CurrentFrame()
        {
            return BattleActionTimelineTime.SecondsToFrame(
                Math.Max(0d, playheadSeconds),
                authoringState.frameRate);
        }

        private double CurrentDuration()
        {
            return CurrentDuration(Document);
        }

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

        private void ClampPlayhead()
        {
            playheadSeconds = Math.Max(
                0d,
                Math.Min(CurrentDuration(), playheadSeconds));
        }

        private double SnapNonNegative(double value)
        {
            if (!BattleActionTimelineTime.IsFinite(value))
            {
                return 0d;
            }

            return Math.Max(
                0d,
                BattleActionTimelineTime.SnapSeconds(
                    value,
                    authoringState.frameRate));
        }

        private double SnapAndClamp(double value, double maximum)
        {
            return Math.Max(0d, Math.Min(maximum, SnapNonNegative(value)));
        }

        private double FrameSeconds()
        {
            return BattleActionTimelineTime.FrameToSeconds(
                1,
                authoringState.frameRate);
        }

        private void CancelDrag()
        {
            dragKind = DragKind.None;
            dragSheet = string.Empty;
            dragRowId = 0;
            dragChanged = false;
        }

        private static float TimeToX(double time, Rect rect, double duration)
        {
            return Mathf.Lerp(
                rect.x,
                rect.xMax,
                duration <= 0d ? 0f : Mathf.Clamp01((float)(time / duration)));
        }

        private static double XToTime(float x, Rect rect, double duration)
        {
            return Mathf.InverseLerp(rect.x, rect.xMax, x) * duration;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                cosine * value.x - sine * value.y,
                sine * value.x + cosine * value.y);
        }

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

        private static bool NearlyEqual(double first, double second)
        {
            return Math.Abs(first - second) <= BattleActionTimelineTime.Epsilon;
        }
    }
}
#endif
