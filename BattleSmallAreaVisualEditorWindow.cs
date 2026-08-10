#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// BattleSmallArea 源数据可视化工具。支持 DTO/TSV、正式 xlsx 的当前或全部模板事务写回，
    /// 写回成功后必须经过统一增量导表事务与 Output 逐模板回读才报告完成。
    /// </summary>
    public sealed class BattleSmallAreaVisualEditorWindow : EditorWindow
    {
        [Serializable]
        private sealed class Document
        {
            public int id = 101;
            public string codeName = "NewBattleRoom";
            public string nameLanguageKey = "#BattleSmallArea_Name_101";
            public int usageType;
            public int backgroundResourceId = 6101;
            public Vector2 backgroundOffset;
            public Vector2 backgroundScale = Vector2.one;
            public Rect bounds = new Rect(-9.6f, -5.4f, 19.2f, 10.8f);
            public List<FloorRow> floors = new List<FloorRow>();
            public List<LadderRow> ladders = new List<LadderRow>();
            public List<DoorRow> doors = new List<DoorRow>();
            public List<EnemyAreaRow> enemyAreas = new List<EnemyAreaRow>();
            public List<LootRow> lootPoints = new List<LootRow>();
            public List<BossRow> bossPoints = new List<BossRow>();
            public List<ExtractionRow> extractionPoints = new List<ExtractionRow>();
        }

        [Serializable]
        private abstract class LocalRow
        {
            public int rowId;
            public int localId = 1;
            public int floorId = 1;
        }

        [Serializable]
        private sealed class FloorRow : LocalRow
        {
            public int collisionType;
            public float minX = -4f;
            public float maxX = 4f;
            public float y = -3f;
            public bool isSafeSpawnFloor;
            public int styleId = 1;
        }

        [Serializable]
        private sealed class LadderRow : LocalRow
        {
            public int upperFloorId = 2;
            public float x;
            public float interactionWidth = 1f;
            public int styleId = 1;
        }

        [Serializable]
        private sealed class DoorRow : LocalRow
        {
            public float x;
            public int styleId = 1;
        }

        [Serializable]
        private sealed class EnemyAreaRow : LocalRow
        {
            public float minX = -2f;
            public float maxX = 2f;
            public int spawnRuleId = 1;
        }

        [Serializable]
        private sealed class LootRow : LocalRow
        {
            public float x;
            public float baseSpawnChance = 70f;
            public int lootSourceId = 1;
        }

        [Serializable]
        private sealed class BossRow : LocalRow
        {
            public float x;
        }

        [Serializable]
        private sealed class ExtractionRow : LocalRow
        {
            public float x;
        }

        private enum ToolKind
        {
            Select,
            Floor,
            Ladder,
            Door,
            EnemyArea,
            Loot,
            Boss,
            Extraction,
        }

        [Flags]
        private enum PartialStageSection
        {
            None = 0,
            Main = 1 << 0,
            Floor = 1 << 1,
            Ladder = 1 << 2,
            Door = 1 << 3,
            EnemyArea = 1 << 4,
            Loot = 1 << 5,
            Boss = 1 << 6,
            Extraction = 1 << 7,
        }

        [Flags]
        private enum LayerKind
        {
            None = 0,
            Background = 1 << 0,
            Floor = 1 << 1,
            Ladder = 1 << 2,
            Door = 1 << 3,
            EnemyArea = 1 << 4,
            Loot = 1 << 5,
            Boss = 1 << 6,
            Extraction = 1 << 7,
            All = Background | Floor | Ladder | Door | EnemyArea | Loot | Boss | Extraction,
        }

        private enum DragMode
        {
            None,
            Move,
            MinimumEndpoint,
            MaximumEndpoint,
        }

        /// <summary>
        /// Unity Undo 只能可靠记录 UnityEngine.Object。把 DTO 作为隐藏 ScriptableObject 的
        /// 序列化子树保存，窗口、画布与 SceneView 的每一次编辑都走同一条 Undo 链。
        /// </summary>
        private sealed class AuthoringState : ScriptableObject
        {
            public Document document = new Document();
            public string baselineSourceRows = string.Empty;
            public bool documentDirty;
            public bool snapEnabled = true;
            public float snapSize = 0.5f;
            public int visibleLayers = (int)LayerKind.All;
            public int lockedLayers;
            public int[] nextRowIds = new int[8];
        }

        [Serializable]
        private sealed class DraftEnvelope
        {
            public string documentJson;
            public string baselineSourceRows;
            public bool documentDirty;
            public bool snapEnabled;
            public float snapSize;
            public int visibleLayers;
            public int lockedLayers;
            public int[] nextRowIds;
            public string workbookPath;
            public string workbookContentHash;
            public List<int> workingTemplateIds = new List<int>();
            public List<string> workingTemplateRows = new List<string>();
        }

        private sealed class PartialStagePlan
        {
            public Document Candidate;
            public readonly List<PartialStageSection> Accepted = new List<PartialStageSection>();
            public readonly List<PartialStageSection> Rejected = new List<PartialStageSection>();
        }

        private const float InspectorWidth = 390f;
        private const float MinimumSegmentWidth = 0.05f;
        private const string DraftKeyPrefix = "TryGame.BattleSmallAreaVisualEditor.Draft.";
        private AuthoringState authoringState;
        private Document document
        {
            get
            {
                EnsureAuthoringState();
                return authoringState.document;
            }
            set
            {
                EnsureAuthoringState();
                authoringState.document = value;
            }
        }
        private bool documentDirty
        {
            get => authoringState != null && authoringState.documentDirty;
            set
            {
                EnsureAuthoringState();
                authoringState.documentDirty = value;
            }
        }
        private Vector2 inspectorScroll;
        private Vector2 issueScroll;
        private ToolKind tool;
        private ToolKind selectedKind;
        private int selectedIndex = -1;
        private Vector2 lastMouseWorld;
        private bool dragging;
        private bool dragUndoRegistered;
        private DragMode dragMode;
        private Vector2 dragStartWorld;
        private float dragInitialMinimumX;
        private float dragInitialMaximumX;
        private float dragInitialY;
        private float dragInitialX;
        private readonly List<string> validationIssues = new List<string>();
        private BattleSmallAreaWorkbookBridge.Snapshot workbookSnapshot;
        private readonly SortedDictionary<int, string> workingTemplateRows =
            new SortedDictionary<int, string>();
        private int workbookTemplateIndex;
        private string workbookStatus = "尚未读取正式源表";
        private bool sourceSavedOutputStale;
        private BattleStageConfigurationRegistry previewRegistry;
        private readonly Dictionary<int, Sprite> previewSprites = new Dictionary<int, Sprite>();
        private string previewStatus = "运行时表现配置尚未读取";
        private bool draftRestored;
        private bool draftSaveQueued;

        [MenuItem("TryGame/Battle WorldZone/SmallArea 可视化编辑器", false, 431)]
        private static void Open()
        {
            BattleSmallAreaVisualEditorWindow window = GetWindow<BattleSmallAreaVisualEditorWindow>();
            window.titleContent = new GUIContent("Battle SmallArea");
            window.minSize = new Vector2(980f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureAuthoringState();
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload -= SaveDraftNow;
            AssemblyReloadEvents.beforeAssemblyReload += SaveDraftNow;
            TryRestoreDraft();
            EnsurePreviewRegistry();
            ValidateDocument();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.duringSceneGui -= OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload -= SaveDraftNow;
            SaveDraftNow();
        }

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

        private void EnsureAuthoringState()
        {
            if (authoringState != null)
            {
                if (authoringState.document == null)
                {
                    authoringState.document = new Document();
                }
                EnsureLists(authoringState.document);
                EnsureNextRowIdArray();
                return;
            }

            authoringState = CreateInstance<AuthoringState>();
            authoringState.hideFlags = HideFlags.HideAndDontSave;
            EnsureLists(authoringState.document);
            authoringState.baselineSourceRows = BuildSourceRows(authoringState.document);
            EnsureNextRowIdArray();
        }

        private void EnsureNextRowIdArray()
        {
            if (authoringState.nextRowIds == null || authoringState.nextRowIds.Length < 8)
            {
                int[] previous = authoringState.nextRowIds;
                authoringState.nextRowIds = new int[8];
                if (previous != null)
                {
                    Array.Copy(previous, authoringState.nextRowIds, Mathf.Min(previous.Length, 8));
                }
            }
        }

        private void OnUndoRedo()
        {
            EnsureAuthoringState();
            EnsureLists(document);
            CancelActiveDrag();
            NormalizeSelectionAfterDocumentChange();
            RefreshDirtyFlag();
            ValidateDocument();
            QueueDraftSave();
            Repaint();
            SceneView.RepaintAll();
        }

        private void QueueUndoRedo(bool redo)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null || authoringState == null) return;
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

        private void CancelActiveDrag()
        {
            dragging = false;
            dragUndoRegistered = false;
            dragMode = DragMode.None;
        }

        private bool IsSelectionValid()
        {
            return selectedKind != ToolKind.Select
                && selectedIndex >= 0
                && selectedIndex < GetCount(selectedKind);
        }

        private void NormalizeSelectionAfterDocumentChange()
        {
            if (IsSelectionValid()) return;
            selectedKind = ToolKind.Select;
            selectedIndex = -1;
            CancelActiveDrag();
        }

        private void OnGUI()
        {
            EnsureAuthoringState();
            EnsureLists(document);
            NormalizeSelectionAfterDocumentChange();
            DrawToolbar();
            Rect body = new Rect(0f, 24f, position.width, position.height - 24f);
            GUILayout.BeginArea(new Rect(body.x, body.y, InspectorWidth, body.height));
            DrawInspector();
            GUILayout.EndArea();

            Rect canvas = new Rect(
                InspectorWidth + 6f,
                body.y + 6f,
                Mathf.Max(100f, body.width - InspectorWidth - 12f),
                Mathf.Max(100f, body.height - 12f));
            DrawCanvas(canvas);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("读取全部正式模板", EditorStyles.toolbarButton, GUILayout.Width(122f)))
            {
                LoadOfficialWorkbook();
            }
            if (workbookSnapshot?.SmallAreaIds != null
                && workbookSnapshot.SmallAreaIds.Length > 0)
            {
                string[] labels = Array.ConvertAll(
                    workbookSnapshot.SmallAreaIds,
                    value => value.ToString(CultureInfo.InvariantCulture));
                workbookTemplateIndex = EditorGUILayout.Popup(
                    Mathf.Clamp(
                        workbookTemplateIndex,
                        0,
                        workbookSnapshot.SmallAreaIds.Length - 1),
                    labels,
                    EditorStyles.toolbarPopup,
                    GUILayout.Width(72f));
                if (GUILayout.Button("载入选择", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    LoadSelectedOfficialTemplate();
                }
                if (GUILayout.Button("写回当前", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    WriteCurrentTemplateToOfficialWorkbook();
                }
                if (GUILayout.Button("写回全部", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    WriteAllTemplatesToOfficialWorkbook();
                }
            }
            GUILayout.Space(8f);
            if (GUILayout.Button("导入 DTO/源行", EditorStyles.toolbarButton, GUILayout.Width(105f)))
            {
                ImportDocument();
            }
            if (GUILayout.Button("导出 DTO", EditorStyles.toolbarButton, GUILayout.Width(76f)))
            {
                ExportDocument();
            }
            if (GUILayout.Button("复制源表行", EditorStyles.toolbarButton, GUILayout.Width(88f)))
            {
                EditorGUIUtility.systemCopyBuffer = BuildSourceRows(document);
                ShowNotification(new GUIContent("已复制各 SmallArea Sheet 的 TSV 行"));
            }
            if (GUILayout.Button("从剪贴板导入", EditorStyles.toolbarButton, GUILayout.Width(105f)))
            {
                TryImportText(EditorGUIUtility.systemCopyBuffer, "clipboard");
            }

            GUILayout.Space(12f);
            GUILayout.Label("工具", GUILayout.Width(30f));
            tool = (ToolKind)EditorGUILayout.EnumPopup(tool, EditorStyles.toolbarPopup, GUILayout.Width(105f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(documentDirty ? "● 未暂存" : "✓ 已暂存", EditorStyles.miniLabel, GUILayout.Width(66f));
            if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(52f)))
            {
                ValidateDocument();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInspector()
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            DrawAuthoringControls();
            Document edited = CloneDocument(document);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("小区域", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(workbookStatus, MessageType.None);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("smallAreaId（稳定）", edited.id);
            }
            edited.codeName = EditorGUILayout.TextField("codeName", edited.codeName);
            edited.nameLanguageKey = EditorGUILayout.TextField(
                "nameLanguageKey",
                edited.nameLanguageKey);
            edited.usageType = EditorGUILayout.Popup("usageType", edited.usageType, new[] { "Normal", "Boss" });
            using (new EditorGUI.DisabledScope(IsLayerLocked(LayerKind.Background)))
            {
                edited.backgroundResourceId = EditorGUILayout.IntField("背景 ResourceId", edited.backgroundResourceId);
                edited.backgroundOffset = EditorGUILayout.Vector2Field("背景偏移", edited.backgroundOffset);
                edited.backgroundScale = EditorGUILayout.Vector2Field("背景缩放", edited.backgroundScale);
            }
            edited.bounds = EditorGUILayout.RectField("编辑/镜头边界", edited.bounds);

            DrawFloorList(edited);
            DrawLadderList(edited);
            DrawDoorList(edited);
            DrawEnemyAreaList(edited);
            DrawPointList("物资点", ToolKind.Loot, edited.lootPoints);
            DrawPointList("Boss 点", ToolKind.Boss, edited.bossPoints);
            DrawPointList("撤离候选点", ToolKind.Extraction, edited.extractionPoints);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(authoringState, "编辑 Battle SmallArea");
                document = edited;
                OnAuthoringChanged();
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                validationIssues.Count == 0 ? "校验通过" : $"校验问题 ({validationIssues.Count})",
                EditorStyles.boldLabel);
            issueScroll = EditorGUILayout.BeginScrollView(issueScroll, GUILayout.Height(120f));
            if (validationIssues.Count == 0)
            {
                EditorGUILayout.HelpBox("结构可输出；最终 ID 断链仍由导表/启动校验负责。", MessageType.Info);
            }
            else
            {
                for (int index = 0; index < validationIssues.Count; index++)
                {
                    EditorGUILayout.HelpBox(validationIssues[index], MessageType.Error);
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndScrollView();
        }

        private void DrawAuthoringControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("编辑控制", EditorStyles.boldLabel);
            if (GUILayout.Button("撤销", GUILayout.Width(48f))) QueueUndoRedo(false);
            if (GUILayout.Button("重做", GUILayout.Width(48f))) QueueUndoRedo(true);
            if (GUILayout.Button("Scene 聚焦", GUILayout.Width(76f))) FocusSceneView();
            EditorGUILayout.EndHorizontal();

            bool editedSnapEnabled = authoringState.snapEnabled;
            float editedSnapSize = authoringState.snapSize;
            int editedVisibleLayers = authoringState.visibleLayers;
            int editedLockedLayers = authoringState.lockedLayers;
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("重载正式 Sprite", GUILayout.Width(108f)))
            {
                ReloadPreviewRegistry();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            editedSnapEnabled = EditorGUILayout.ToggleLeft(
                "网格吸附",
                editedSnapEnabled,
                GUILayout.Width(78f));
            using (new EditorGUI.DisabledScope(!editedSnapEnabled))
            {
                int snapIndex = SnapSizeIndex(editedSnapSize);
                snapIndex = EditorGUILayout.Popup(
                    snapIndex,
                    new[] { "1", "0.5", "0.25" },
                    GUILayout.Width(60f));
                editedSnapSize = new[] { 1f, 0.5f, 0.25f }[snapIndex];
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            LayerControl("背景", LayerKind.Background, ref editedVisibleLayers, ref editedLockedLayers);
            LayerControl("Floor", LayerKind.Floor, ref editedVisibleLayers, ref editedLockedLayers);
            LayerControl("Ladder", LayerKind.Ladder, ref editedVisibleLayers, ref editedLockedLayers);
            LayerControl("Door", LayerKind.Door, ref editedVisibleLayers, ref editedLockedLayers);
            LayerControl("EnemyArea", LayerKind.EnemyArea, ref editedVisibleLayers, ref editedLockedLayers);
            LayerControl("Loot", LayerKind.Loot, ref editedVisibleLayers, ref editedLockedLayers);
            LayerControl("Boss", LayerKind.Boss, ref editedVisibleLayers, ref editedLockedLayers);
            LayerControl("Extraction", LayerKind.Extraction, ref editedVisibleLayers, ref editedLockedLayers);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(authoringState, "修改 SmallArea 编辑器设置");
                authoringState.snapEnabled = editedSnapEnabled;
                authoringState.snapSize = editedSnapSize;
                authoringState.visibleLayers = editedVisibleLayers;
                authoringState.lockedLayers = editedLockedLayers;
                QueueDraftSave();
                Repaint();
                SceneView.RepaintAll();
            }
            EditorGUILayout.HelpBox(
                $"{previewStatus}\n窗口画布显示正式 Sprite；SceneView 提供选择、移动和区间端点手柄。",
                previewRegistry == null ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private static void LayerControl(
            string label,
            LayerKind layer,
            ref int visibleLayers,
            ref int lockedLayers)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(90f));
            bool visible = ((LayerKind)visibleLayers & layer) != 0;
            bool locked = ((LayerKind)lockedLayers & layer) != 0;
            visible = EditorGUILayout.ToggleLeft("显示", visible, GUILayout.Width(52f));
            locked = EditorGUILayout.ToggleLeft("锁定", locked, GUILayout.Width(52f));
            SetLayerFlag(ref visibleLayers, layer, visible);
            SetLayerFlag(ref lockedLayers, layer, locked);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFloorList(Document target)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Floor ({target.floors.Count})", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(IsLayerLocked(LayerKind.Floor)))
            {
            for (int index = 0; index < target.floors.Count; index++)
            {
                FloorRow row = target.floors[index];
                EditorGUILayout.BeginVertical("box");
                DrawRowHeader(ToolKind.Floor, index, row, "Floor");
                row.collisionType = EditorGUILayout.Popup("碰撞", row.collisionType, new[] { "SolidGround", "OneWayPlatform" });
                row.minX = EditorGUILayout.FloatField("minX", row.minX);
                row.maxX = EditorGUILayout.FloatField("maxX", row.maxX);
                row.y = EditorGUILayout.FloatField("y", row.y);
                row.isSafeSpawnFloor = EditorGUILayout.Toggle("安全 Floor", row.isSafeSpawnFloor);
                row.styleId = EditorGUILayout.IntField("styleId", row.styleId);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ Floor")) { AddAtCenter(ToolKind.Floor); GUIUtility.ExitGUI(); }
            }
        }

        private void DrawLadderList(Document target)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Ladder ({target.ladders.Count})", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(IsLayerLocked(LayerKind.Ladder)))
            {
            for (int index = 0; index < target.ladders.Count; index++)
            {
                LadderRow row = target.ladders[index];
                EditorGUILayout.BeginVertical("box");
                DrawRowHeader(ToolKind.Ladder, index, row, "Ladder");
                row.floorId = EditorGUILayout.IntField("firstEndpoint (lowerFloorId)", row.floorId);
                row.upperFloorId = EditorGUILayout.IntField("secondEndpoint (upperFloorId)", row.upperFloorId);
                row.x = EditorGUILayout.FloatField("x", row.x);
                row.interactionWidth = EditorGUILayout.FloatField("interactionWidth", row.interactionWidth);
                row.styleId = EditorGUILayout.IntField("styleId", row.styleId);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ Ladder")) { AddAtCenter(ToolKind.Ladder); GUIUtility.ExitGUI(); }
            }
        }

        private void DrawDoorList(Document target)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Door ({target.doors.Count})", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(IsLayerLocked(LayerKind.Door)))
            {
            for (int index = 0; index < target.doors.Count; index++)
            {
                DoorRow row = target.doors[index];
                EditorGUILayout.BeginVertical("box");
                DrawRowHeader(ToolKind.Door, index, row, "Door");
                row.floorId = EditorGUILayout.IntField("floorId", row.floorId);
                row.x = EditorGUILayout.FloatField("x", row.x);
                row.styleId = EditorGUILayout.IntField("styleId", row.styleId);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ Door")) { AddAtCenter(ToolKind.Door); GUIUtility.ExitGUI(); }
            }
        }

        private void DrawEnemyAreaList(Document target)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Enemy 区 ({target.enemyAreas.Count})", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(IsLayerLocked(LayerKind.EnemyArea)))
            {
            for (int index = 0; index < target.enemyAreas.Count; index++)
            {
                EnemyAreaRow row = target.enemyAreas[index];
                EditorGUILayout.BeginVertical("box");
                DrawRowHeader(ToolKind.EnemyArea, index, row, "EnemyArea");
                row.floorId = EditorGUILayout.IntField("floorId", row.floorId);
                row.minX = EditorGUILayout.FloatField("minX", row.minX);
                row.maxX = EditorGUILayout.FloatField("maxX", row.maxX);
                row.spawnRuleId = EditorGUILayout.IntField("spawnRuleId", row.spawnRuleId);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ EnemyArea")) { AddAtCenter(ToolKind.EnemyArea); GUIUtility.ExitGUI(); }
            }
        }

        private void DrawPointList<T>(string title, ToolKind kind, List<T> rows)
            where T : LocalRow
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"{title} ({rows.Count})", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(IsLayerLocked(ToolLayer(kind))))
            {
            for (int index = 0; index < rows.Count; index++)
            {
                T row = rows[index];
                EditorGUILayout.BeginVertical("box");
                DrawRowHeader(kind, index, row, title);
                row.floorId = EditorGUILayout.IntField("floorId", row.floorId);
                if (row is LootRow loot)
                {
                    loot.x = EditorGUILayout.FloatField("x", loot.x);
                    loot.baseSpawnChance = EditorGUILayout.Slider("出现概率", loot.baseSpawnChance, 0f, 100f);
                    loot.lootSourceId = EditorGUILayout.IntField("lootSourceId", loot.lootSourceId);
                }
                else if (row is BossRow boss)
                {
                    boss.x = EditorGUILayout.FloatField("x", boss.x);
                }
                else if (row is ExtractionRow extraction)
                {
                    extraction.x = EditorGUILayout.FloatField("x", extraction.x);
                }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button($"+ {title}")) { AddAtCenter(kind); GUIUtility.ExitGUI(); }
            }
        }

        private void DrawRowHeader(ToolKind kind, int index, LocalRow row, string title)
        {
            EditorGUILayout.BeginHorizontal();
            bool selected = selectedKind == kind && selectedIndex == index;
            Color previous = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(0.42f, 0.78f, 1f, 1f);
            bool previousChanged = GUI.changed;
            if (GUILayout.Button($"{title} {row.localId}"))
            {
                selectedKind = kind;
                selectedIndex = index;
            }
            GUI.changed = previousChanged;
            GUI.backgroundColor = previous;
            if (GUILayout.Button("×", GUILayout.Width(26f)))
            {
                RemoveRow(kind, index);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("rowId（全表稳定）", row.rowId);
                EditorGUILayout.IntField("localId（模板稳定）", row.localId);
            }
        }

        private void DrawCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.035f, 0.055f, 0.08f, 1f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect inner = new Rect(rect.x + 18f, rect.y + 34f, rect.width - 36f, rect.height - 52f);
            EditorGUI.DrawRect(inner, new Color(0.06f, 0.085f, 0.115f, 1f));
            DrawBackgroundPreview(inner);
            DrawGrid(inner);
            DrawGeometry(inner);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, 20f),
                $"{document.codeName} ({document.id})  |  {tool}：选择模式拖动物体；其它模式点击空白新增",
                EditorStyles.whiteLabel);
            HandleCanvasInput(inner);
        }

        private void DrawGrid(Rect rect)
        {
            float minX = document.bounds.xMin;
            float maxX = document.bounds.xMax;
            float minY = document.bounds.yMin;
            float maxY = document.bounds.yMax;
            if (maxX <= minX || maxY <= minY) return;
            Handles.BeginGUI();
            Handles.color = new Color(0.22f, 0.3f, 0.38f, 0.55f);
            for (float x = Mathf.Ceil(minX); x <= maxX; x += 1f)
            {
                Handles.DrawLine(WorldToCanvas(new Vector2(x, minY), rect), WorldToCanvas(new Vector2(x, maxY), rect));
            }
            for (float y = Mathf.Ceil(minY); y <= maxY; y += 1f)
            {
                Handles.DrawLine(WorldToCanvas(new Vector2(minX, y), rect), WorldToCanvas(new Vector2(maxX, y), rect));
            }
            Handles.color = new Color(0.55f, 0.66f, 0.78f, 0.7f);
            Handles.DrawLine(WorldToCanvas(new Vector2(0f, minY), rect), WorldToCanvas(new Vector2(0f, maxY), rect));
            Handles.DrawLine(WorldToCanvas(new Vector2(minX, 0f), rect), WorldToCanvas(new Vector2(maxX, 0f), rect));
            Handles.EndGUI();
        }

        private void DrawGeometry(Rect rect)
        {
            if (IsLayerVisible(LayerKind.Floor))
            {
                for (int i = 0; i < document.floors.Count; i++)
                {
                    DrawFloorPreview(rect, document.floors[i], i);
                }
            }
            if (IsLayerVisible(LayerKind.EnemyArea))
            {
                for (int i = 0; i < document.enemyAreas.Count; i++)
                {
                    EnemyAreaRow row = document.enemyAreas[i];
                    float y = FloorY(row.floorId);
                    DrawWorldRect(rect, row.minX, row.maxX, y + 0.12f, y + 0.72f,
                        new Color(0.78f, 0.24f, 0.24f, 0.34f), ToolKind.EnemyArea, i, $"Enemy {row.localId}");
                    if (selectedKind == ToolKind.EnemyArea && selectedIndex == i)
                    {
                        DrawEndpointHandles(rect, row.minX, row.maxX, y + 0.42f);
                    }
                }
            }
            if (IsLayerVisible(LayerKind.Ladder))
            {
                for (int i = 0; i < document.ladders.Count; i++)
                {
                    DrawLadderPreview(rect, document.ladders[i], i);
                }
            }
            if (IsLayerVisible(LayerKind.Door)) DrawDoorPreviews(rect);
            if (IsLayerVisible(LayerKind.Loot)) DrawLootPreviews(rect);
            if (IsLayerVisible(LayerKind.Boss))
                DrawPointGeometry(rect, ToolKind.Boss, document.bossPoints, new Color(0.92f, 0.2f, 0.72f, 0.2f), "B（运行时决定）");
            if (IsLayerVisible(LayerKind.Extraction)) DrawExtractionPreviews(rect);
        }

        private void DrawBackgroundPreview(Rect canvas)
        {
            if (!IsLayerVisible(LayerKind.Background)) return;
            Sprite sprite = LoadPreviewSprite(document.backgroundResourceId);
            if (sprite == null)
            {
                DrawMissingSprite(canvas, document.bounds, $"背景资源缺失 {document.backgroundResourceId}");
                return;
            }

            Vector2 size = Vector2.Scale(sprite.bounds.size, new Vector2(
                Mathf.Abs(document.backgroundScale.x),
                Mathf.Abs(document.backgroundScale.y)));
            Vector2 center = document.backgroundOffset + Vector2.Scale(
                sprite.bounds.center,
                document.backgroundScale);
            DrawSpriteWorld(canvas, sprite, new Rect(center - size * 0.5f, size), Color.white);
        }

        private void DrawFloorPreview(Rect canvas, FloorRow row, int index)
        {
            float colliderThickness = BattleWorldZoneRuntimeTuning.FloorColliderThickness;
            if (previewRegistry == null
                || !previewRegistry.TryGetFloorStyle(row.styleId, out BattleFloorStyleDefinition style)
                || style == null)
            {
                DrawMissingSprite(canvas,
                    Rect.MinMaxRect(row.minX, row.y - colliderThickness, row.maxX, row.y),
                    $"Floor style {row.styleId} 缺失");
                DrawWorldRect(canvas, row.minX, row.maxX, row.y - colliderThickness, row.y,
                    Color.clear, ToolKind.Floor, index, $"F{row.localId}");
                return;
            }

            Sprite surfaceSprite = LoadPreviewSprite(style.ResourceId);
            Sprite fillSprite = style.FillResourceId > 0
                ? LoadPreviewSprite(style.FillResourceId)
                : null;
            Vector2 fillNativeSize = fillSprite != null
                ? (Vector2)fillSprite.bounds.size
                : Vector2.one;
            if (!BattleSmallAreaVisualLayoutUtility.TryCreateFloorLayout(
                    ToFloorDefinition(row),
                    style,
                    colliderThickness,
                    fillNativeSize,
                    out BattleSmallAreaFloorVisualLayout layout,
                    out string layoutError))
            {
                DrawMissingSprite(canvas,
                    Rect.MinMaxRect(row.minX, row.y - colliderThickness, row.maxX, row.y),
                    layoutError);
                return;
            }
            DrawSpriteTiledWorld(
                canvas,
                surfaceSprite,
                layout.Surface.WorldRect,
                layout.Surface.TileWorldSize,
                Color.white,
                $"Floor sprite {style.ResourceId} 缺失");
            if (layout.HasFill)
            {
                DrawSpriteTiledWorld(
                    canvas,
                    fillSprite,
                    layout.Fill.WorldRect,
                    layout.Fill.TileWorldSize,
                    Color.white,
                    $"Floor fill {style.FillResourceId} 缺失");
            }

            Rect visualRect = layout.HasFill
                ? Rect.MinMaxRect(
                    Mathf.Min(layout.Surface.WorldRect.xMin, layout.Fill.WorldRect.xMin),
                    Mathf.Min(layout.Surface.WorldRect.yMin, layout.Fill.WorldRect.yMin),
                    Mathf.Max(layout.Surface.WorldRect.xMax, layout.Fill.WorldRect.xMax),
                    Mathf.Max(layout.Surface.WorldRect.yMax, layout.Fill.WorldRect.yMax))
                : layout.Surface.WorldRect;
            DrawWorldOutline(canvas, visualRect,
                row.isSafeSpawnFloor ? new Color(0.22f, 1f, 0.48f, 1f) : new Color(0.64f, 0.84f, 1f, 0.85f),
                selectedKind == ToolKind.Floor && selectedIndex == index ? 3f : 1f);
            GUI.Label(WorldRectToCanvas(visualRect, canvas),
                $"F{row.localId}{(row.collisionType == 1 ? " · 单向" : string.Empty)}",
                EditorStyles.centeredGreyMiniLabel);
            if (selectedKind == ToolKind.Floor && selectedIndex == index)
            {
                DrawEndpointHandles(canvas, row.minX, row.maxX, row.y);
            }
        }

        private void DrawLadderPreview(Rect canvas, LadderRow row, int index)
        {
            if (!TryCreateLadderPreviewLayout(
                    row,
                    out Sprite sprite,
                    out BattleSmallAreaTiledVisualLayout layout,
                    out string layoutError))
            {
                Rect missing;
                if (!TryCreateLadderInteractionRect(document, row, out missing))
                {
                    float fallbackWidth = (float)BattleLadderGeometry.GetEffectiveInteractionWidth(
                        row.interactionWidth);
                    missing = new Rect(
                        row.x - fallbackWidth * 0.5f,
                        document.bounds.yMin,
                        fallbackWidth,
                        MinimumSegmentWidth);
                }
                DrawMissingSprite(canvas, missing, layoutError);
                return;
            }
            DrawSpriteTiledWorld(canvas, sprite, layout.WorldRect,
                layout.TileWorldSize, Color.white,
                $"Ladder style/sprite {row.styleId} 缺失");
            DrawWorldOutline(canvas, layout.WorldRect, new Color(1f, 0.78f, 0.18f, 0.9f),
                selectedKind == ToolKind.Ladder && selectedIndex == index ? 3f : 1f);
        }

        private bool TryCreateLadderPreviewLayout(
            LadderRow row,
            out Sprite sprite,
            out BattleSmallAreaTiledVisualLayout layout,
            out string error)
        {
            sprite = null;
            layout = default;
            error = $"Ladder style {row?.styleId ?? 0} 缺失";
            if (row == null
                || previewRegistry == null
                || !previewRegistry.TryGetLadderStyle(
                    row.styleId,
                    out BattleLadderStyleDefinition style)
                || style == null)
            {
                return false;
            }
            sprite = LoadPreviewSprite(style.ResourceId);
            FloorRow firstEndpoint = FindFloor(row.floorId);
            FloorRow secondEndpoint = FindFloor(row.upperFloorId);
            if (firstEndpoint == null || secondEndpoint == null)
            {
                error = $"Ladder {row.localId} 的 Floor 引用断链";
                return false;
            }
            return BattleSmallAreaVisualLayoutUtility.TryCreateLadderLayout(
                ToLadderDefinition(row),
                ToFloorDefinition(firstEndpoint),
                ToFloorDefinition(secondEndpoint),
                style,
                sprite != null ? (Vector2)sprite.bounds.size : Vector2.one,
                out layout,
                out error);
        }

        private static bool TryCreateLadderInteractionRect(
            Document value,
            LadderRow row,
            out Rect rect)
        {
            rect = default;
            if (value == null || row == null) return false;
            FloorRow first = FindFloor(value, row.floorId);
            FloorRow second = FindFloor(value, row.upperFloorId);
            if (first == null || second == null) return false;
            BattleLadderGeometry.OrderEndpointsByHeight(
                ToFloorDefinition(first),
                ToFloorDefinition(second),
                out BattleFloorDefinition lowerByY,
                out BattleFloorDefinition upperByY);
            float width = (float)BattleLadderGeometry.GetEffectiveInteractionWidth(
                row.interactionWidth);
            float height = (float)(upperByY.Y - lowerByY.Y);
            if (!IsFinite(width) || !IsFinite(height) || width <= 0f || height <= 0f)
                return false;
            rect = new Rect(
                row.x - width * 0.5f,
                (float)lowerByY.Y,
                width,
                height);
            return true;
        }

        private void DrawDoorPreviews(Rect canvas)
        {
            for (int i = 0; i < document.doors.Count; i++)
            {
                DoorRow row = document.doors[i];
                Sprite sprite = null;
                if (previewRegistry != null
                    && previewRegistry.TryGetDoorStyle(row.styleId, out BattleDoorStyleDefinition style)
                    && style != null)
                {
                    sprite = LoadPreviewSprite(style.ResourceId);
                }
                FloorRow floor = FindFloor(row.floorId);
                BattleSmallAreaEntityVisualLayout layout =
                    BattleSmallAreaVisualLayoutUtility.CreateDoorLayout(
                        new BattleDoorPointDefinition(row.localId, row.floorId, row.x),
                        floor != null ? ToFloorDefinition(floor) : null,
                        BattleWorldZoneRuntimeTuning.DoorTriggerWidth);
                DrawEntitySprite(canvas, sprite, layout.WorldRect, ToolKind.Door, i, $"D{row.localId}");
            }
        }

        private void DrawLootPreviews(Rect canvas)
        {
            for (int i = 0; i < document.lootPoints.Count; i++)
            {
                LootRow row = document.lootPoints[i];
                Sprite sprite = null;
                if (previewRegistry != null
                    && previewRegistry.TryGetLootSource(row.lootSourceId, out BattleLootSourceDefinition source)
                    && source != null)
                {
                    sprite = LoadPreviewSprite(source.ResourceId);
                }
                BattleSmallAreaEntityVisualLayout layout =
                    BattleSmallAreaVisualLayoutUtility.CreateEntityLayout(
                        row.x,
                        FloorY(row.floorId),
                        0.9f,
                        sprite != null ? (Vector2)sprite.bounds.size : Vector2.one);
                DrawEntitySprite(canvas, sprite, layout.WorldRect, ToolKind.Loot, i, $"Loot {row.localId}");
            }
        }

        private void DrawExtractionPreviews(Rect canvas)
        {
            Sprite sprite = null;
            if (previewRegistry != null
                && previewRegistry.TryGetExtractionStyle(1, out BattleExtractionStyleDefinition style)
                && style != null)
            {
                sprite = LoadPreviewSprite(style.ResourceId);
            }
            for (int i = 0; i < document.extractionPoints.Count; i++)
            {
                ExtractionRow row = document.extractionPoints[i];
                BattleSmallAreaEntityVisualLayout layout =
                    BattleSmallAreaVisualLayoutUtility.CreateFixedWidthEntityLayout(
                        row.x,
                        FloorY(row.floorId),
                        BattleWorldZoneRuntimeTuning.ExtractionTriggerWidth,
                        1.45f);
                DrawEntitySprite(canvas, sprite, layout.WorldRect,
                    ToolKind.Extraction, i, $"撤 {row.localId}（样式运行时决定）",
                    new Color(0.45f, 1f, 0.58f, 1f));
            }
        }

        private void DrawPointGeometry<T>(Rect canvas, ToolKind kind, List<T> rows, Color color, string prefix)
            where T : LocalRow
        {
            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                float x = GetPointX(row);
                float y = FloorY(row.floorId);
                Vector2 center = WorldToCanvas(new Vector2(x, y + 0.48f), canvas);
                Rect point = new Rect(center.x - 11f, center.y - 18f, 22f, 36f);
                EditorGUI.DrawRect(point, color);
                if (selectedKind == kind && selectedIndex == i)
                {
                    DrawSelection(point);
                }
                GUI.Label(point, prefix, EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void DrawEntitySprite(
            Rect canvas,
            Sprite sprite,
            Rect worldRect,
            ToolKind kind,
            int index,
            string label,
            Color? tint = null)
        {
            if (sprite != null)
            {
                DrawSpriteWorld(canvas, sprite, worldRect, tint ?? Color.white);
            }
            else
            {
                DrawMissingSprite(canvas, worldRect, label + " Sprite 缺失");
            }
            DrawWorldOutline(canvas, worldRect,
                selectedKind == kind && selectedIndex == index ? Color.white : new Color(1f, 1f, 1f, 0.38f),
                selectedKind == kind && selectedIndex == index ? 3f : 1f);
            GUI.Label(WorldRectToCanvas(worldRect, canvas), label, EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawSpriteTiledWorld(
            Rect canvas,
            Sprite sprite,
            Rect targetWorldRect,
            Vector2 worldTileSize,
            Color tint,
            string missingLabel)
        {
            if (sprite == null
                || worldTileSize.x <= 0.0001f
                || worldTileSize.y <= 0.0001f)
            {
                DrawMissingSprite(canvas, targetWorldRect, missingLabel);
                return;
            }

            int columns = Mathf.Clamp(
                Mathf.CeilToInt(targetWorldRect.width / worldTileSize.x),
                1,
                512);
            int rows = Mathf.Clamp(
                Mathf.CeilToInt(targetWorldRect.height / worldTileSize.y),
                1,
                512);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    Rect tile = new Rect(
                        targetWorldRect.xMin + x * worldTileSize.x,
                        targetWorldRect.yMin + y * worldTileSize.y,
                        worldTileSize.x,
                        worldTileSize.y);
                    DrawSpriteWorldClipped(canvas, sprite, tile, targetWorldRect, tint);
                }
            }
        }

        private void DrawSpriteWorld(Rect canvas, Sprite sprite, Rect worldRect, Color tint)
        {
            DrawSpriteWorldClipped(canvas, sprite, worldRect, document.bounds, tint);
        }

        private void DrawSpriteWorldClipped(
            Rect canvas,
            Sprite sprite,
            Rect fullWorldRect,
            Rect clipWorldRect,
            Color tint)
        {
            if (sprite == null || sprite.texture == null
                || fullWorldRect.width <= 0.0001f || fullWorldRect.height <= 0.0001f)
            {
                return;
            }

            Rect clippedWorld = Intersect(fullWorldRect, clipWorldRect);
            clippedWorld = Intersect(clippedWorld, document.bounds);
            if (clippedWorld.width <= 0f || clippedWorld.height <= 0f) return;

            Rect destination = WorldRectToCanvas(clippedWorld, canvas);
            Vector4 outer = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
            float left = Mathf.InverseLerp(fullWorldRect.xMin, fullWorldRect.xMax, clippedWorld.xMin);
            float right = Mathf.InverseLerp(fullWorldRect.xMin, fullWorldRect.xMax, clippedWorld.xMax);
            float bottom = Mathf.InverseLerp(fullWorldRect.yMin, fullWorldRect.yMax, clippedWorld.yMin);
            float top = Mathf.InverseLerp(fullWorldRect.yMin, fullWorldRect.yMax, clippedWorld.yMax);
            Rect uv = Rect.MinMaxRect(
                Mathf.Lerp(outer.x, outer.z, left),
                Mathf.Lerp(outer.y, outer.w, bottom),
                Mathf.Lerp(outer.x, outer.z, right),
                Mathf.Lerp(outer.y, outer.w, top));
            Color previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(destination, sprite.texture, uv, true);
            GUI.color = previous;
        }

        private void DrawMissingSprite(Rect canvas, Rect worldRect, string label)
        {
            Rect clipped = Intersect(worldRect, document.bounds);
            if (clipped.width <= 0f || clipped.height <= 0f) return;
            Rect destination = WorldRectToCanvas(clipped, canvas);
            if (destination.width <= 0f || destination.height <= 0f) return;
            EditorGUI.DrawRect(destination, new Color(0.48f, 0.04f, 0.22f, 0.42f));
            Handles.BeginGUI();
            Handles.color = new Color(1f, 0.16f, 0.58f, 0.9f);
            Handles.DrawLine(destination.min, destination.max);
            Handles.DrawLine(
                new Vector3(destination.xMin, destination.yMax),
                new Vector3(destination.xMax, destination.yMin));
            Handles.EndGUI();
            GUI.Label(destination, label, EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawWorldOutline(Rect canvas, Rect worldRect, Color color, float thickness)
        {
            Rect destination = WorldRectToCanvas(worldRect, canvas);
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAPolyLine(thickness,
                new Vector3(destination.xMin, destination.yMin),
                new Vector3(destination.xMax, destination.yMin),
                new Vector3(destination.xMax, destination.yMax),
                new Vector3(destination.xMin, destination.yMax),
                new Vector3(destination.xMin, destination.yMin));
            Handles.EndGUI();
        }

        private void DrawEndpointHandles(Rect canvas, float minimumX, float maximumX, float y)
        {
            Vector2 left = WorldToCanvas(new Vector2(minimumX, y), canvas);
            Vector2 right = WorldToCanvas(new Vector2(maximumX, y), canvas);
            EditorGUI.DrawRect(new Rect(left.x - 5f, left.y - 5f, 10f, 10f), Color.white);
            EditorGUI.DrawRect(new Rect(right.x - 5f, right.y - 5f, 10f, 10f), Color.white);
        }

        private Rect WorldRectToCanvas(Rect worldRect, Rect canvas)
        {
            Vector2 topLeft = WorldToCanvas(new Vector2(worldRect.xMin, worldRect.yMax), canvas);
            Vector2 bottomRight = WorldToCanvas(new Vector2(worldRect.xMax, worldRect.yMin), canvas);
            return Rect.MinMaxRect(
                Mathf.Min(topLeft.x, bottomRight.x),
                Mathf.Min(topLeft.y, bottomRight.y),
                Mathf.Max(topLeft.x, bottomRight.x),
                Mathf.Max(topLeft.y, bottomRight.y));
        }

        private static Rect Intersect(Rect left, Rect right)
        {
            return Rect.MinMaxRect(
                Mathf.Max(left.xMin, right.xMin),
                Mathf.Max(left.yMin, right.yMin),
                Mathf.Min(left.xMax, right.xMax),
                Mathf.Min(left.yMax, right.yMax));
        }

        private void DrawWorldRect(Rect canvas, float minX, float maxX, float minY, float maxY,
            Color color, ToolKind kind, int index, string label)
        {
            Vector2 a = WorldToCanvas(new Vector2(minX, maxY), canvas);
            Vector2 b = WorldToCanvas(new Vector2(maxX, minY), canvas);
            Rect worldRect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            EditorGUI.DrawRect(worldRect, color);
            if (selectedKind == kind && selectedIndex == index) DrawSelection(worldRect);
            GUI.Label(worldRect, label, EditorStyles.centeredGreyMiniLabel);
        }

        private static void DrawSelection(Rect rect)
        {
            Handles.BeginGUI();
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(3f,
                new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin),
                new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax),
                new Vector3(rect.xMin, rect.yMin));
            Handles.EndGUI();
        }

        private void HandleCanvasInput(Rect rect)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition)) return;
            Vector2 world = CanvasToWorld(current.mousePosition, rect);
            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (tool == ToolKind.Select)
                {
                    DragMode endpointMode = TryHitSelectedEndpoint(world, rect);
                    if (endpointMode != DragMode.None)
                    {
                        BeginCanvasDrag(world, endpointMode);
                    }
                    else if (TryHit(world, out ToolKind hitKind, out int hitIndex))
                    {
                        selectedKind = hitKind;
                        selectedIndex = hitIndex;
                        BeginCanvasDrag(world, DragMode.Move);
                    }
                }
                else
                {
                    if (IsLayerLocked(ToolLayer(tool)))
                    {
                        ShowNotification(new GUIContent($"{tool} 图层已锁定"));
                    }
                    else
                    {
                        if (AddAt(tool, world))
                        {
                            tool = ToolKind.Select;
                        }
                    }
                }
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && dragging)
            {
                if (!IsSelectionValid())
                {
                    CancelActiveDrag();
                    return;
                }

                if (!dragUndoRegistered)
                {
                    Undo.RegisterCompleteObjectUndo(authoringState, $"拖动 {selectedKind}");
                    dragUndoRegistered = true;
                }

                ApplyCanvasDrag(world);
                lastMouseWorld = world;
                OnAuthoringChanged();
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && current.button == 0)
            {
                CancelActiveDrag();
            }
        }

        private DragMode TryHitSelectedEndpoint(Vector2 world, Rect canvas)
        {
            if (selectedIndex < 0 || IsLayerLocked(ToolLayer(selectedKind))) return DragMode.None;
            float radius = Mathf.Max(0.06f, document.bounds.width * 8f / Mathf.Max(1f, canvas.width));
            float minimumX;
            float maximumX;
            float y;
            if (selectedKind == ToolKind.Floor && selectedIndex < document.floors.Count)
            {
                FloorRow row = document.floors[selectedIndex];
                minimumX = row.minX;
                maximumX = row.maxX;
                y = row.y;
            }
            else if (selectedKind == ToolKind.EnemyArea && selectedIndex < document.enemyAreas.Count)
            {
                EnemyAreaRow row = document.enemyAreas[selectedIndex];
                minimumX = row.minX;
                maximumX = row.maxX;
                y = FloorY(row.floorId) + 0.42f;
            }
            else
            {
                return DragMode.None;
            }

            if (Vector2.Distance(world, new Vector2(minimumX, y)) <= radius)
                return DragMode.MinimumEndpoint;
            if (Vector2.Distance(world, new Vector2(maximumX, y)) <= radius)
                return DragMode.MaximumEndpoint;
            return DragMode.None;
        }

        private void BeginCanvasDrag(Vector2 world, DragMode mode)
        {
            if (!IsSelectionValid() || IsLayerLocked(ToolLayer(selectedKind))) return;
            dragUndoRegistered = false;
            dragging = true;
            dragMode = mode;
            dragStartWorld = world;
            lastMouseWorld = world;
            if (selectedKind == ToolKind.Floor && selectedIndex < document.floors.Count)
            {
                FloorRow row = document.floors[selectedIndex];
                dragInitialMinimumX = row.minX;
                dragInitialMaximumX = row.maxX;
                dragInitialY = row.y;
            }
            else if (selectedKind == ToolKind.EnemyArea && selectedIndex < document.enemyAreas.Count)
            {
                EnemyAreaRow row = document.enemyAreas[selectedIndex];
                dragInitialMinimumX = row.minX;
                dragInitialMaximumX = row.maxX;
            }
            else
            {
                dragInitialX = GetSelectedX();
            }
        }

        private void ApplyCanvasDrag(Vector2 world)
        {
            if (!IsSelectionValid())
            {
                CancelActiveDrag();
                return;
            }

            Vector2 delta = world - dragStartWorld;
            float minimumWidth = authoringState.snapEnabled
                ? Mathf.Max(MinimumSegmentWidth, authoringState.snapSize)
                : MinimumSegmentWidth;
            switch (selectedKind)
            {
                case ToolKind.Floor:
                {
                    FloorRow row = document.floors[selectedIndex];
                    if (dragMode == DragMode.MinimumEndpoint)
                        row.minX = Mathf.Min(Snap(dragInitialMinimumX + delta.x), row.maxX - minimumWidth);
                    else if (dragMode == DragMode.MaximumEndpoint)
                        row.maxX = Mathf.Max(Snap(dragInitialMaximumX + delta.x), row.minX + minimumWidth);
                    else
                    {
                        float width = dragInitialMaximumX - dragInitialMinimumX;
                        float minimum = Snap(dragInitialMinimumX + delta.x);
                        row.minX = minimum;
                        row.maxX = minimum + width;
                        row.y = Snap(dragInitialY + delta.y);
                    }
                    break;
                }
                case ToolKind.EnemyArea:
                {
                    EnemyAreaRow row = document.enemyAreas[selectedIndex];
                    if (dragMode == DragMode.MinimumEndpoint)
                        row.minX = Mathf.Min(Snap(dragInitialMinimumX + delta.x), row.maxX - minimumWidth);
                    else if (dragMode == DragMode.MaximumEndpoint)
                        row.maxX = Mathf.Max(Snap(dragInitialMaximumX + delta.x), row.minX + minimumWidth);
                    else
                    {
                        float width = dragInitialMaximumX - dragInitialMinimumX;
                        float minimum = Snap(dragInitialMinimumX + delta.x);
                        row.minX = minimum;
                        row.maxX = minimum + width;
                    }
                    break;
                }
                case ToolKind.Ladder:
                case ToolKind.Door:
                case ToolKind.Loot:
                case ToolKind.Boss:
                case ToolKind.Extraction: SetSelectedX(Snap(dragInitialX + delta.x)); break;
            }
        }

        private bool TryHit(Vector2 world, out ToolKind kind, out int index)
        {
            if (CanCanvasSelect(ToolKind.Door)) for (int i = document.doors.Count - 1; i >= 0; i--)
                if (HitPoint(world, document.doors[i])) { kind = ToolKind.Door; index = i; return true; }
            if (CanCanvasSelect(ToolKind.Loot)) for (int i = document.lootPoints.Count - 1; i >= 0; i--)
                if (HitPoint(world, document.lootPoints[i])) { kind = ToolKind.Loot; index = i; return true; }
            if (CanCanvasSelect(ToolKind.Boss)) for (int i = document.bossPoints.Count - 1; i >= 0; i--)
                if (HitPoint(world, document.bossPoints[i])) { kind = ToolKind.Boss; index = i; return true; }
            if (CanCanvasSelect(ToolKind.Extraction)) for (int i = document.extractionPoints.Count - 1; i >= 0; i--)
                if (HitPoint(world, document.extractionPoints[i])) { kind = ToolKind.Extraction; index = i; return true; }
            if (CanCanvasSelect(ToolKind.Ladder)) for (int i = document.ladders.Count - 1; i >= 0; i--)
            {
                LadderRow row = document.ladders[i];
                if (TryCreateLadderInteractionRect(document, row, out Rect ladderRect)
                    && ladderRect.Contains(world))
                { kind = ToolKind.Ladder; index = i; return true; }
            }
            if (CanCanvasSelect(ToolKind.EnemyArea)) for (int i = document.enemyAreas.Count - 1; i >= 0; i--)
            {
                EnemyAreaRow row = document.enemyAreas[i];
                float y = FloorY(row.floorId);
                if (world.x >= row.minX && world.x <= row.maxX && world.y >= y && world.y <= y + 0.8f)
                { kind = ToolKind.EnemyArea; index = i; return true; }
            }
            if (CanCanvasSelect(ToolKind.Floor)) for (int i = document.floors.Count - 1; i >= 0; i--)
            {
                FloorRow row = document.floors[i];
                if (world.x >= row.minX && world.x <= row.maxX && Mathf.Abs(world.y - row.y) <= 0.35f)
                { kind = ToolKind.Floor; index = i; return true; }
            }
            kind = ToolKind.Select; index = -1; return false;
        }

        private bool HitPoint(Vector2 world, LocalRow row)
        {
            return Mathf.Abs(world.x - GetPointX(row)) <= 0.45f
                && Mathf.Abs(world.y - (FloorY(row.floorId) + 0.45f)) <= 0.65f;
        }

        private bool CanCanvasSelect(ToolKind kind)
        {
            LayerKind layer = ToolLayer(kind);
            return IsLayerVisible(layer) && !IsLayerLocked(layer);
        }

        private float GetSelectedX()
        {
            LocalRow row = SelectedRow();
            if (row is LadderRow ladder) return ladder.x;
            if (row is DoorRow door) return door.x;
            if (row is LootRow loot) return loot.x;
            if (row is BossRow boss) return boss.x;
            if (row is ExtractionRow extraction) return extraction.x;
            return 0f;
        }

        private void AddAtCenter(ToolKind kind)
        {
            AddAt(kind, document.bounds.center);
        }

        private bool AddAt(ToolKind kind, Vector2 world)
        {
            if (kind == ToolKind.Select || IsLayerLocked(ToolLayer(kind))) return false;
            world = new Vector2(Snap(world.x), Snap(world.y));
            FloorRow placementFloor = FindNearestFloor(world);
            if (kind != ToolKind.Floor && placementFloor == null)
            {
                ShowNotification(new GUIContent("请先创建至少一块 Floor"));
                return false;
            }
            if ((kind == ToolKind.EnemyArea || kind == ToolKind.Boss)
                && placementFloor.isSafeSpawnFloor)
            {
                ShowNotification(new GUIContent(
                    $"Floor {placementFloor.localId} 是安全 Floor，不能创建 {kind}"));
                return false;
            }

            int floorId = placementFloor?.localId ?? 1;
            float placementX = placementFloor == null
                ? world.x
                : Mathf.Clamp(world.x, placementFloor.minX, placementFloor.maxX);
            int ladderFirstFloorId = floorId;
            int ladderSecondFloorId = FindOtherFloorId(floorId);
            float ladderX = placementX;
            if (kind == ToolKind.Ladder
                && !TryResolveLadderPlacement(
                    world,
                    out ladderFirstFloorId,
                    out ladderSecondFloorId,
                    out ladderX))
            {
                ShowNotification(new GUIContent("点击位置附近没有可连接的两块 Floor"));
                return false;
            }

            Undo.RegisterCompleteObjectUndo(authoringState, $"新增 {kind}");
            switch (kind)
            {
                case ToolKind.Floor:
                    document.floors.Add(Initialize(new FloorRow { minX = Snap(world.x - 2f), maxX = Snap(world.x + 2f), y = world.y }, document.floors, kind)); break;
                case ToolKind.Ladder:
                {
                    LadderRow row = new LadderRow
                    {
                        x = ladderX,
                        floorId = ladderFirstFloorId,
                        upperFloorId = ladderSecondFloorId,
                    };
                    document.ladders.Add(Initialize(row, document.ladders, kind));
                    break;
                }
                case ToolKind.Door:
                    document.doors.Add(Initialize(new DoorRow { x = placementX, floorId = floorId }, document.doors, kind)); break;
                case ToolKind.EnemyArea:
                {
                    GetInitialFloorInterval(placementFloor, placementX, 3f, out float minimumX, out float maximumX);
                    document.enemyAreas.Add(Initialize(new EnemyAreaRow { minX = minimumX, maxX = maximumX, floorId = floorId }, document.enemyAreas, kind));
                    break;
                }
                case ToolKind.Loot:
                    document.lootPoints.Add(Initialize(new LootRow { x = placementX, floorId = floorId }, document.lootPoints, kind)); break;
                case ToolKind.Boss:
                    document.bossPoints.Add(Initialize(new BossRow { x = placementX, floorId = floorId }, document.bossPoints, kind)); break;
                case ToolKind.Extraction:
                    document.extractionPoints.Add(Initialize(new ExtractionRow { x = placementX, floorId = floorId }, document.extractionPoints, kind)); break;
            }
            selectedKind = kind;
            selectedIndex = GetCount(kind) - 1;
            OnAuthoringChanged();
            return true;
        }

        private T Initialize<T>(T row, List<T> rows, ToolKind kind) where T : LocalRow
        {
            row.localId = NextLocalId(rows);
            row.rowId = AllocateRowId(kind);
            return row;
        }

        private static int NextLocalId<T>(List<T> rows) where T : LocalRow
        {
            int max = 0;
            for (int i = 0; i < rows.Count; i++) max = Mathf.Max(max, rows[i].localId);
            return max + 1;
        }

        private int AllocateRowId(ToolKind kind)
        {
            EnsureNextRowIdArray();
            int slot = Mathf.Clamp((int)kind, 1, 7);
            int candidate = authoringState.nextRowIds[slot];
            if (candidate <= 0)
            {
                candidate = ComputeNextRowId(kind);
            }
            while (candidate > 0 && IsRowIdUsed(kind, candidate))
            {
                if (candidate == int.MaxValue)
                    throw new InvalidOperationException($"{kind} rowId 已耗尽。");
                candidate++;
            }
            if (candidate <= 0) throw new InvalidOperationException($"无法为 {kind} 分配稳定 rowId。");
            authoringState.nextRowIds[slot] = candidate == int.MaxValue ? int.MaxValue : candidate + 1;
            return candidate;
        }

        private int ComputeNextRowId(ToolKind kind)
        {
            int maximum = 0;
            foreach (string sourceRows in workingTemplateRows.Values)
            {
                try
                {
                    Document parsed = ParseSourceRows(sourceRows);
                    foreach (LocalRow row in RowsForKind(parsed, kind)) maximum = Mathf.Max(maximum, row.rowId);
                }
                catch
                {
                    // 工作簿快照会在读取时校验；单个草稿损坏不应导致 ID 回绕复用。
                }
            }
            foreach (LocalRow row in RowsForKind(document, kind)) maximum = Mathf.Max(maximum, row.rowId);
            if (maximum == int.MaxValue) throw new InvalidOperationException($"{kind} rowId 已耗尽。");
            return Mathf.Max(1, maximum + 1);
        }

        private bool IsRowIdUsed(ToolKind kind, int rowId)
        {
            foreach (LocalRow row in RowsForKind(document, kind))
                if (row.rowId == rowId) return true;
            foreach (string sourceRows in workingTemplateRows.Values)
            {
                try
                {
                    Document parsed = ParseSourceRows(sourceRows);
                    foreach (LocalRow row in RowsForKind(parsed, kind))
                        if (row.rowId == rowId) return true;
                }
                catch
                {
                    // 损坏的草稿会在写回前被完整校验；此处继续使用单调 seed。
                }
            }
            return false;
        }

        private static IEnumerable<LocalRow> RowsForKind(Document value, ToolKind kind)
        {
            switch (kind)
            {
                case ToolKind.Floor: return value.floors.Cast<LocalRow>();
                case ToolKind.Ladder: return value.ladders.Cast<LocalRow>();
                case ToolKind.Door: return value.doors.Cast<LocalRow>();
                case ToolKind.EnemyArea: return value.enemyAreas.Cast<LocalRow>();
                case ToolKind.Loot: return value.lootPoints.Cast<LocalRow>();
                case ToolKind.Boss: return value.bossPoints.Cast<LocalRow>();
                case ToolKind.Extraction: return value.extractionPoints.Cast<LocalRow>();
                default: return Enumerable.Empty<LocalRow>();
            }
        }

        private float Snap(float value)
        {
            if (!authoringState.snapEnabled || authoringState.snapSize <= 0.0001f) return value;
            return Mathf.Round(value / authoringState.snapSize) * authoringState.snapSize;
        }

        private static int SnapSizeIndex(float value)
        {
            if (Mathf.Abs(value - 1f) < 0.01f) return 0;
            if (Mathf.Abs(value - 0.25f) < 0.01f) return 2;
            return 1;
        }

        private static LayerKind ToolLayer(ToolKind kind)
        {
            switch (kind)
            {
                case ToolKind.Floor: return LayerKind.Floor;
                case ToolKind.Ladder: return LayerKind.Ladder;
                case ToolKind.Door: return LayerKind.Door;
                case ToolKind.EnemyArea: return LayerKind.EnemyArea;
                case ToolKind.Loot: return LayerKind.Loot;
                case ToolKind.Boss: return LayerKind.Boss;
                case ToolKind.Extraction: return LayerKind.Extraction;
                default: return LayerKind.None;
            }
        }

        private bool IsLayerVisible(LayerKind layer)
        {
            return layer == LayerKind.None || ((LayerKind)authoringState.visibleLayers & layer) != 0;
        }

        private bool IsLayerLocked(LayerKind layer)
        {
            return layer != LayerKind.None && ((LayerKind)authoringState.lockedLayers & layer) != 0;
        }

        private static void SetLayerFlag(ref int flags, LayerKind layer, bool enabled)
        {
            if (enabled) flags |= (int)layer;
            else flags &= ~(int)layer;
        }

        private void OnAuthoringChanged()
        {
            RefreshDirtyFlag();
            ValidateDocument();
            QueueDraftSave();
            Repaint();
            SceneView.RepaintAll();
        }

        private void RefreshDirtyFlag()
        {
            documentDirty = !string.Equals(
                NormalizeSourceRows(BuildSourceRows(document)),
                NormalizeSourceRows(authoringState.baselineSourceRows),
                StringComparison.Ordinal);
        }

        private static string NormalizeSourceRows(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").TrimEnd();
        }

        private FloorRow FindNearestFloor(Vector2 world)
        {
            FloorRow nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < document.floors.Count; i++)
            {
                FloorRow candidate = document.floors[i];
                float horizontalDistance = world.x < candidate.minX
                    ? candidate.minX - world.x
                    : world.x > candidate.maxX
                        ? world.x - candidate.maxX
                        : 0f;
                float verticalDistance = candidate.y - world.y;
                float distance = horizontalDistance * horizontalDistance * 4f
                    + verticalDistance * verticalDistance;
                if (distance >= nearestDistance) continue;
                nearest = candidate;
                nearestDistance = distance;
            }

            return nearest;
        }

        private bool TryResolveLadderPlacement(
            Vector2 world,
            out int firstFloorId,
            out int secondFloorId,
            out float x)
        {
            FloorRow nearest = FindNearestFloor(world);
            firstFloorId = nearest?.localId ?? 1;
            secondFloorId = FindOtherFloorId(firstFloorId);
            x = world.x;
            float halfWidth = (float)BattleLadderGeometry.GetEffectiveInteractionWidth(1f) * 0.5f;
            float bestScore = float.PositiveInfinity;
            FloorRow bestFirst = null;
            FloorRow bestSecond = null;
            float bestX = world.x;
            for (int firstIndex = 0; firstIndex < document.floors.Count; firstIndex++)
            {
                FloorRow first = document.floors[firstIndex];
                for (int secondIndex = firstIndex + 1; secondIndex < document.floors.Count; secondIndex++)
                {
                    FloorRow second = document.floors[secondIndex];
                    if (Mathf.Abs(first.y - second.y) < MinimumSegmentWidth) continue;
                    if (!TryGetLadderCenterRange(
                            first,
                            second,
                            halfWidth,
                            out float minimumX,
                            out float maximumX))
                    {
                        continue;
                    }

                    float candidateX = Mathf.Clamp(world.x, minimumX, maximumX);
                    float minimumY = Mathf.Min(first.y, second.y);
                    float maximumY = Mathf.Max(first.y, second.y);
                    float verticalDistance = world.y < minimumY
                        ? minimumY - world.y
                        : world.y > maximumY
                            ? world.y - maximumY
                            : 0f;
                    float horizontalDistance = Mathf.Abs(candidateX - world.x);
                    float midpointDistance = Mathf.Abs(
                        world.y - (first.y + second.y) * 0.5f);
                    float score = horizontalDistance * horizontalDistance * 16f
                        + verticalDistance * verticalDistance * 4f
                        + midpointDistance * midpointDistance * 0.01f;
                    if (score >= bestScore) continue;
                    bestScore = score;
                    bestFirst = first;
                    bestSecond = second;
                    bestX = candidateX;
                }
            }

            if (bestFirst == null || bestSecond == null) return false;
            if (bestFirst.y <= bestSecond.y)
            {
                firstFloorId = bestFirst.localId;
                secondFloorId = bestSecond.localId;
            }
            else
            {
                firstFloorId = bestSecond.localId;
                secondFloorId = bestFirst.localId;
            }
            x = bestX;
            return true;
        }

        private bool TryGetLadderCenterRange(
            FloorRow first,
            FloorRow second,
            float halfWidth,
            out float minimumX,
            out float maximumX)
        {
            const float overlapEpsilon = 0.001f;
            minimumX = document.bounds.xMin + halfWidth;
            maximumX = document.bounds.xMax - halfWidth;
            if (first != null)
            {
                minimumX = Mathf.Max(minimumX, first.minX - halfWidth + overlapEpsilon);
                maximumX = Mathf.Min(maximumX, first.maxX + halfWidth - overlapEpsilon);
            }
            if (second != null)
            {
                minimumX = Mathf.Max(minimumX, second.minX - halfWidth + overlapEpsilon);
                maximumX = Mathf.Min(maximumX, second.maxX + halfWidth - overlapEpsilon);
            }
            return minimumX <= maximumX;
        }

        private static void GetInitialFloorInterval(
            FloorRow floor,
            float centerX,
            float desiredWidth,
            out float minimumX,
            out float maximumX)
        {
            if (floor == null)
            {
                minimumX = centerX - desiredWidth * 0.5f;
                maximumX = centerX + desiredWidth * 0.5f;
                return;
            }

            float floorWidth = Mathf.Max(0f, floor.maxX - floor.minX);
            float width = Mathf.Min(desiredWidth, floorWidth);
            minimumX = Mathf.Clamp(centerX - width * 0.5f, floor.minX, floor.maxX - width);
            maximumX = minimumX + width;
        }

        private float ClampLadderX(LadderRow row, float value)
        {
            FloorRow first = FindFloor(row.floorId);
            FloorRow second = FindFloor(row.upperFloorId);
            float halfWidth = (float)BattleLadderGeometry.GetEffectiveInteractionWidth(
                row.interactionWidth) * 0.5f;
            return TryGetLadderCenterRange(first, second, halfWidth, out float minimumX, out float maximumX)
                ? Mathf.Clamp(value, minimumX, maximumX)
                : Mathf.Clamp(value, document.bounds.xMin + halfWidth, document.bounds.xMax - halfWidth);
        }

        private int FindOtherFloorId(int floorId)
        {
            for (int i = 0; i < document.floors.Count; i++)
                if (document.floors[i].localId != floorId) return document.floors[i].localId;
            return floorId + 1;
        }

        private float FloorY(int floorId)
        {
            for (int i = 0; i < document.floors.Count; i++)
                if (document.floors[i].localId == floorId) return document.floors[i].y;
            return document.bounds.yMin;
        }

        private static BattleFloorDefinition ToFloorDefinition(FloorRow row)
        {
            if (row == null) return null;
            return new BattleFloorDefinition(
                row.localId,
                row.collisionType == 1
                    ? BattleFloorCollisionType.OneWayPlatform
                    : BattleFloorCollisionType.SolidGround,
                row.minX,
                row.maxX,
                row.y,
                row.isSafeSpawnFloor,
                row.styleId);
        }

        private static BattleLadderDefinition ToLadderDefinition(LadderRow row)
        {
            if (row == null) return null;
            return new BattleLadderDefinition(
                row.localId,
                row.floorId,
                row.upperFloorId,
                row.x,
                row.interactionWidth,
                row.styleId);
        }

        private static float GetPointX(LocalRow row)
        {
            if (row is DoorRow door) return door.x;
            if (row is LootRow loot) return loot.x;
            if (row is BossRow boss) return boss.x;
            if (row is ExtractionRow extraction) return extraction.x;
            return 0f;
        }

        private void RemoveRow(ToolKind kind, int index)
        {
            if (IsLayerLocked(ToolLayer(kind))) return;
            if (kind == ToolKind.Floor && !CanRemoveFloor(index)) return;
            Undo.RegisterCompleteObjectUndo(authoringState, $"删除 {kind}");
            switch (kind)
            {
                case ToolKind.Floor: document.floors.RemoveAt(index); break;
                case ToolKind.Ladder: document.ladders.RemoveAt(index); break;
                case ToolKind.Door: document.doors.RemoveAt(index); break;
                case ToolKind.EnemyArea: document.enemyAreas.RemoveAt(index); break;
                case ToolKind.Loot: document.lootPoints.RemoveAt(index); break;
                case ToolKind.Boss: document.bossPoints.RemoveAt(index); break;
                case ToolKind.Extraction: document.extractionPoints.RemoveAt(index); break;
            }
            selectedKind = ToolKind.Select;
            selectedIndex = -1;
            OnAuthoringChanged();
        }

        private bool CanRemoveFloor(int index)
        {
            if (index < 0 || index >= document.floors.Count) return false;
            int floorId = document.floors[index].localId;
            List<string> references = new List<string>();
            for (int i = 0; i < document.ladders.Count; i++)
            {
                LadderRow row = document.ladders[i];
                if (row.floorId == floorId || row.upperFloorId == floorId)
                    references.Add($"Ladder {row.localId}");
            }
            AppendFloorReferences(document.doors, floorId, "Door", references);
            AppendFloorReferences(document.enemyAreas, floorId, "EnemyArea", references);
            AppendFloorReferences(document.lootPoints, floorId, "Loot", references);
            AppendFloorReferences(document.bossPoints, floorId, "Boss", references);
            AppendFloorReferences(document.extractionPoints, floorId, "Extraction", references);
            if (references.Count == 0) return true;

            EditorUtility.DisplayDialog(
                "不能删除仍被引用的 Floor",
                $"Floor {floorId} 仍被以下对象引用：\n\n{string.Join("、", references)}\n\n" +
                "请先修改或删除这些引用，工具不会自动级联破坏数据。",
                "知道了");
            return false;
        }

        private static void AppendFloorReferences<T>(
            IReadOnlyList<T> rows,
            int floorId,
            string label,
            ICollection<string> result)
            where T : LocalRow
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].floorId == floorId) result.Add($"{label} {rows[i].localId}");
            }
        }

        private int GetCount(ToolKind kind)
        {
            switch (kind)
            {
                case ToolKind.Floor: return document.floors.Count;
                case ToolKind.Ladder: return document.ladders.Count;
                case ToolKind.Door: return document.doors.Count;
                case ToolKind.EnemyArea: return document.enemyAreas.Count;
                case ToolKind.Loot: return document.lootPoints.Count;
                case ToolKind.Boss: return document.bossPoints.Count;
                case ToolKind.Extraction: return document.extractionPoints.Count;
                default: return 0;
            }
        }

        private Vector2 WorldToCanvas(Vector2 world, Rect canvas)
        {
            float x = Mathf.InverseLerp(document.bounds.xMin, document.bounds.xMax, world.x);
            float y = Mathf.InverseLerp(document.bounds.yMin, document.bounds.yMax, world.y);
            return new Vector2(Mathf.Lerp(canvas.xMin, canvas.xMax, x), Mathf.Lerp(canvas.yMax, canvas.yMin, y));
        }

        private Vector2 CanvasToWorld(Vector2 point, Rect canvas)
        {
            float x = Mathf.InverseLerp(canvas.xMin, canvas.xMax, point.x);
            float y = Mathf.InverseLerp(canvas.yMax, canvas.yMin, point.y);
            return new Vector2(Mathf.Lerp(document.bounds.xMin, document.bounds.xMax, x), Mathf.Lerp(document.bounds.yMin, document.bounds.yMax, y));
        }

        private void ValidateDocument()
        {
            ValidateDocument(document, validationIssues);
        }

        private void ValidateDocument(Document value, List<string> issues)
        {
            issues.Clear();
            if (value == null) { issues.Add("DTO 为空。"); return; }
            EnsureLists(value);
            if (value.id <= 0) issues.Add("smallAreaId 必须大于 0。");
            if (string.IsNullOrWhiteSpace(value.codeName)) issues.Add("codeName 不能为空。");
            if (string.IsNullOrWhiteSpace(value.nameLanguageKey)) issues.Add("nameLanguageKey 不能为空。");
            if (value.usageType < 0 || value.usageType > 1) issues.Add("usageType 只能是 Normal 或 Boss。");
            if (!IsFiniteRect(value.bounds) || value.bounds.width <= 0f || value.bounds.height <= 0f)
                issues.Add("边界必须是有限数且宽高大于 0。");
            if (value.backgroundResourceId <= 0) issues.Add("背景 ResourceId 必须大于 0。");
            if (!IsFinite(value.backgroundOffset.x) || !IsFinite(value.backgroundOffset.y))
                issues.Add("背景偏移必须是有限数。");
            if (!IsFinite(value.backgroundScale.x) || !IsFinite(value.backgroundScale.y)
                || Mathf.Abs(value.backgroundScale.x) <= 0.0001f
                || Mathf.Abs(value.backgroundScale.y) <= 0.0001f)
                issues.Add("背景缩放必须是非零有限数。");

            if (previewRegistry == null)
            {
                issues.Add($"运行时表现配置不可用：{previewStatus}");
            }
            else if (LoadPreviewSprite(value.backgroundResourceId) == null)
            {
                issues.Add($"背景 ResourceId={value.backgroundResourceId} 无法加载 Sprite。");
            }

            HashSet<int> floorIds = ValidateRows(value, value.floors, ToolKind.Floor, "Floor", issues);
            bool hasSafe = false;
            for (int i = 0; i < value.floors.Count; i++)
            {
                FloorRow row = value.floors[i];
                if (!IsFinite(row.minX) || !IsFinite(row.maxX) || !IsFinite(row.y))
                    issues.Add($"Floor {row.localId} 的坐标必须是有限数。");
                if (row.maxX <= row.minX) issues.Add($"Floor {row.localId} 的 maxX 必须大于 minX。");
                if (!ContainsX(value.bounds, row.minX) || !ContainsX(value.bounds, row.maxX)
                    || row.y < value.bounds.yMin || row.y > value.bounds.yMax)
                    issues.Add($"Floor {row.localId} 超出 SmallArea 边界。");
                ValidateFloorStyle(row, issues);
                hasSafe |= row.isSafeSpawnFloor;
            }
            if (!hasSafe) issues.Add("至少需要一块安全 Floor。");

            ValidateRows(value, value.ladders, ToolKind.Ladder, "Ladder", issues);
            for (int i = 0; i < value.ladders.Count; i++)
            {
                LadderRow row = value.ladders[i];
                FloorRow firstEndpoint = FindFloor(value, row.floorId);
                FloorRow secondEndpoint = FindFloor(value, row.upperFloorId);
                if (!floorIds.Contains(row.floorId)) issues.Add($"Ladder {row.localId} 引用了不存在的 first endpoint Floor {row.floorId}。");
                if (!floorIds.Contains(row.upperFloorId) || row.upperFloorId == row.floorId)
                    issues.Add($"Ladder {row.localId} 的 second endpoint Floor 断链或与 first endpoint 相同。");
                if (!IsFinite(row.x) || !IsFinite(row.interactionWidth) || row.interactionWidth <= 0f)
                    issues.Add($"Ladder {row.localId} 的 x 必须有限且 interactionWidth 必须大于 0。");
                float effectiveInteractionWidth =
                    (float)BattleLadderGeometry.GetEffectiveInteractionWidth(row.interactionWidth);
                float ladderMinimumX = row.x - effectiveInteractionWidth * 0.5f;
                float ladderMaximumX = row.x + effectiveInteractionWidth * 0.5f;
                if (!ContainsX(value.bounds, ladderMinimumX) || !ContainsX(value.bounds, ladderMaximumX))
                    issues.Add($"Ladder {row.localId} 的交互宽度超出 SmallArea 边界。");
                if (firstEndpoint != null && !IntervalsOverlap(
                        ladderMinimumX, ladderMaximumX, firstEndpoint.minX, firstEndpoint.maxX))
                    issues.Add($"Ladder {row.localId} 的有效交互宽度与 first endpoint Floor {row.floorId} 不相交。");
                if (secondEndpoint != null && !IntervalsOverlap(
                        ladderMinimumX, ladderMaximumX, secondEndpoint.minX, secondEndpoint.maxX))
                    issues.Add($"Ladder {row.localId} 的有效交互宽度与 second endpoint Floor {row.upperFloorId} 不相交。");
                ValidateLadderStyle(row, firstEndpoint, secondEndpoint, issues);
            }

            ValidateRows(value, value.doors, ToolKind.Door, "Door", issues);
            ValidatePointRows(value, value.doors, floorIds, "Door", row => row.x, issues);
            for (int i = 0; i < value.doors.Count; i++) ValidateDoorStyle(value.doors[i], issues);

            ValidateRows(value, value.enemyAreas, ToolKind.EnemyArea, "EnemyArea", issues);
            for (int i = 0; i < value.enemyAreas.Count; i++)
            {
                EnemyAreaRow row = value.enemyAreas[i];
                FloorRow floor = FindFloor(value, row.floorId);
                if (!floorIds.Contains(row.floorId)) issues.Add($"EnemyArea {row.localId} 引用了不存在的 Floor {row.floorId}。");
                if (!IsFinite(row.minX) || !IsFinite(row.maxX) || row.minX >= row.maxX)
                    issues.Add($"EnemyArea {row.localId} 必须满足有限的 minX < maxX。");
                if (!ContainsX(value.bounds, row.minX) || !ContainsX(value.bounds, row.maxX))
                    issues.Add($"EnemyArea {row.localId} 超出 SmallArea 边界。");
                if (floor != null && (row.minX < floor.minX || row.maxX > floor.maxX))
                    issues.Add($"EnemyArea {row.localId} 必须完整位于 Floor {row.floorId} 区间内。");
                if (row.spawnRuleId <= 0) issues.Add($"EnemyArea {row.localId} 的 spawnRuleId 必须大于 0。");
                if (floor?.isSafeSpawnFloor == true)
                    issues.Add($"EnemyArea {row.localId} 不得引用安全 Floor {row.floorId}。");
            }

            ValidateRows(value, value.lootPoints, ToolKind.Loot, "Loot", issues);
            ValidatePointRows(value, value.lootPoints, floorIds, "Loot", row => row.x, issues);
            for (int i = 0; i < value.lootPoints.Count; i++)
            {
                LootRow row = value.lootPoints[i];
                if (!IsFinite(row.baseSpawnChance) || row.baseSpawnChance < 0f || row.baseSpawnChance > 100f)
                    issues.Add($"Loot {row.localId} 的 baseSpawnChance 必须在 0～100。");
                if (row.lootSourceId <= 0) issues.Add($"Loot {row.localId} 的 lootSourceId 必须大于 0。");
                ValidateLootSource(row, issues);
            }

            ValidateRows(value, value.bossPoints, ToolKind.Boss, "Boss", issues);
            ValidatePointRows(value, value.bossPoints, floorIds, "Boss", row => row.x, issues);
            for (int i = 0; i < value.bossPoints.Count; i++)
                if (FindFloor(value, value.bossPoints[i].floorId)?.isSafeSpawnFloor == true)
                    issues.Add($"Boss {value.bossPoints[i].localId} 不得引用安全 Floor {value.bossPoints[i].floorId}。");

            ValidateRows(value, value.extractionPoints, ToolKind.Extraction, "Extraction", issues);
            ValidatePointRows(value, value.extractionPoints, floorIds, "Extraction", row => row.x, issues);

            ValidateFloorOverlaps(value, issues);
            ValidateInteractionConflicts(value, issues);
            if (value.doors.Count == 0) issues.Add("至少需要一扇门。");
            if (value.extractionPoints.Count == 0) issues.Add("至少需要一个撤离候选点。");
        }

        private HashSet<int> ValidateRows<T>(
            Document value,
            List<T> rows,
            ToolKind kind,
            string label,
            List<string> issues)
            where T : LocalRow
        {
            HashSet<int> ids = new HashSet<int>();
            HashSet<int> rowIds = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                if (row == null) { issues.Add($"{label} 第 {i + 1} 行为空。"); continue; }
                if (row.localId <= 0 || !ids.Add(row.localId)) issues.Add($"{label} localId 非法或重复：{row.localId}。");
                if (row.rowId <= 0 || !rowIds.Add(row.rowId)) issues.Add($"{label} rowId 非法或重复：{row.rowId}。");
            }
            ValidateExternalRowIds(value.id, kind, rowIds, label, issues);
            return ids;
        }

        private void ValidateExternalRowIds(
            int currentSmallAreaId,
            ToolKind kind,
            HashSet<int> currentRowIds,
            string label,
            List<string> issues)
        {
            foreach (KeyValuePair<int, string> pair in workingTemplateRows)
            {
                if (pair.Key == currentSmallAreaId) continue;
                try
                {
                    Document other = ParseSourceRows(pair.Value);
                    foreach (LocalRow row in RowsForKind(other, kind))
                    {
                        if (currentRowIds.Contains(row.rowId))
                            issues.Add($"{label} rowId={row.rowId} 与 SmallArea {pair.Key} 冲突。");
                    }
                }
                catch (Exception exception)
                {
                    issues.Add($"工作集 SmallArea {pair.Key} 无法参与全局 ID 校验：{exception.Message}");
                }
            }
        }

        private void ValidatePointRows<T>(
            Document value,
            IReadOnlyList<T> rows,
            HashSet<int> floors,
            string label,
            Func<T, float> getX,
            List<string> issues)
            where T : LocalRow
        {
            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                if (row == null) continue;
                float x = getX(row);
                if (!floors.Contains(row.floorId)) issues.Add($"{label} {row.localId} 引用了不存在的 Floor {row.floorId}。");
                if (!IsFinite(x)) issues.Add($"{label} {row.localId} 的 x 必须是有限数。");
                if (!ContainsX(value.bounds, x)) issues.Add($"{label} {row.localId} 的 x 超出 SmallArea 边界。");
                FloorRow floor = FindFloor(value, row.floorId);
                if (floor != null && !ContainsX(floor, x))
                    issues.Add($"{label} {row.localId} 的 x={x:0.###} 不在 Floor {floor.localId} 区间内。");
            }
        }

        private void ValidateFloorOverlaps(Document value, List<string> issues)
        {
            for (int leftIndex = 0; leftIndex < value.floors.Count; leftIndex++)
            {
                FloorRow left = value.floors[leftIndex];
                for (int rightIndex = leftIndex + 1; rightIndex < value.floors.Count; rightIndex++)
                {
                    FloorRow right = value.floors[rightIndex];
                    float overlap = Mathf.Min(left.maxX, right.maxX)
                        - Mathf.Max(left.minX, right.minX);
                    if (Mathf.Abs(left.y - right.y)
                            < BattleWorldZoneRuntimeTuning.FloorColliderThickness
                        && overlap > 0.001f)
                    {
                        issues.Add(
                            $"Floor {left.localId} 与 Floor {right.localId} 在同高度重叠 {overlap:0.###}。" );
                    }
                }
            }
        }

        private void ValidateFloorStyle(FloorRow row, List<string> issues)
        {
            if (previewRegistry == null) return;
            if (!previewRegistry.TryGetFloorStyle(row.styleId, out BattleFloorStyleDefinition style)
                || style == null
                || style.ResourceId <= 0)
            {
                issues.Add($"Floor {row.localId} 的 styleId={row.styleId} 配置非法或断链。");
                return;
            }
            Sprite surfaceSprite = LoadPreviewSprite(style.ResourceId);
            Sprite fillSprite = style.FillResourceId > 0
                ? LoadPreviewSprite(style.FillResourceId)
                : null;
            if (!BattleSmallAreaVisualLayoutUtility.TryCreateFloorLayout(
                    ToFloorDefinition(row),
                    style,
                    BattleWorldZoneRuntimeTuning.FloorColliderThickness,
                    fillSprite != null ? (Vector2)fillSprite.bounds.size : Vector2.one,
                    out _,
                    out string layoutError))
                issues.Add($"Floor {row.localId} 的视觉布局非法：{layoutError}");
            if (surfaceSprite == null)
                issues.Add($"Floor {row.localId} 的表面 Sprite 资源 {style.ResourceId} 缺失。");
            if (style.FillResourceId > 0 && fillSprite == null)
                issues.Add($"Floor {row.localId} 的填充 Sprite 资源 {style.FillResourceId} 缺失。");
        }

        private void ValidateLadderStyle(
            LadderRow row,
            FloorRow firstEndpoint,
            FloorRow secondEndpoint,
            List<string> issues)
        {
            if (previewRegistry == null) return;
            if (!previewRegistry.TryGetLadderStyle(row.styleId, out BattleLadderStyleDefinition style)
                || style == null || style.ResourceId <= 0)
            {
                issues.Add($"Ladder {row.localId} 的 styleId={row.styleId} 配置非法或断链。");
                return;
            }
            Sprite sprite = LoadPreviewSprite(style.ResourceId);
            if (firstEndpoint != null && secondEndpoint != null
                && !BattleSmallAreaVisualLayoutUtility.TryCreateLadderLayout(
                    ToLadderDefinition(row),
                    ToFloorDefinition(firstEndpoint),
                    ToFloorDefinition(secondEndpoint),
                    style,
                    sprite != null ? (Vector2)sprite.bounds.size : Vector2.one,
                    out _,
                    out string layoutError))
                issues.Add($"Ladder {row.localId} 的视觉布局非法：{layoutError}");
            if (sprite == null)
                issues.Add($"Ladder {row.localId} 的 Sprite 资源 {style.ResourceId} 缺失。");
        }

        private void ValidateDoorStyle(DoorRow row, List<string> issues)
        {
            if (previewRegistry == null) return;
            if (!previewRegistry.TryGetDoorStyle(row.styleId, out BattleDoorStyleDefinition style)
                || style == null || style.ResourceId <= 0)
            {
                issues.Add($"Door {row.localId} 的 styleId={row.styleId} 配置非法或断链。");
                return;
            }
            if (LoadPreviewSprite(style.ResourceId) == null)
                issues.Add($"Door {row.localId} 的 Sprite 资源 {style.ResourceId} 缺失。");
        }

        private void ValidateLootSource(LootRow row, List<string> issues)
        {
            if (previewRegistry == null || row.lootSourceId <= 0) return;
            if (!previewRegistry.TryGetLootSource(row.lootSourceId, out BattleLootSourceDefinition source)
                || source == null || source.ResourceId <= 0)
            {
                issues.Add($"Loot {row.localId} 的 lootSourceId={row.lootSourceId} 配置断链。");
                return;
            }
            if (LoadPreviewSprite(source.ResourceId) == null)
                issues.Add($"Loot {row.localId} 的 Sprite 资源 {source.ResourceId} 缺失。");
        }

        private void ValidateInteractionConflicts(Document value, List<string> issues)
        {
            List<InteractionFootprint> occupied = new List<InteractionFootprint>();
            float doorHalfWidth = BattleWorldZoneRuntimeTuning.DoorTriggerWidth * 0.5f;
            float extractionHalfWidth =
                BattleWorldZoneRuntimeTuning.ExtractionTriggerWidth * 0.5f;
            for (int index = 0; index < value.doors.Count; index++)
            {
                DoorRow row = value.doors[index];
                occupied.Add(new InteractionFootprint(
                    "Door",
                    row.localId,
                    row.floorId,
                    row.x,
                    doorHalfWidth));
            }
            for (int index = 0; index < value.ladders.Count; index++)
            {
                LadderRow row = value.ladders[index];
                float effectiveWidth =
                    (float)BattleLadderGeometry.GetEffectiveInteractionWidth(row.interactionWidth);
                float halfWidth = effectiveWidth * 0.5f;
                occupied.Add(new InteractionFootprint(
                    "Ladder",
                    row.localId,
                    row.floorId,
                    row.x,
                    halfWidth));
                occupied.Add(new InteractionFootprint(
                    "Ladder",
                    row.localId,
                    row.upperFloorId,
                    row.x,
                    halfWidth));
            }
            for (int index = 0; index < value.bossPoints.Count; index++)
            {
                BossRow row = value.bossPoints[index];
                occupied.Add(new InteractionFootprint(
                    "Boss",
                    row.localId,
                    row.floorId,
                    row.x,
                    (float)BattleStageConfigurationValidator.BossInteractionWidth * 0.5f));
            }
            for (int index = 0; index < value.extractionPoints.Count; index++)
            {
                ExtractionRow extraction = value.extractionPoints[index];
                for (int occupiedIndex = 0; occupiedIndex < occupied.Count; occupiedIndex++)
                {
                    InteractionFootprint other = occupied[occupiedIndex];
                    if (other.FloorId == extraction.floorId
                        && Mathf.Abs(other.X - extraction.x)
                            < other.HalfWidth + extractionHalfWidth)
                    {
                        issues.Add(
                            $"撤离点交互范围冲突：Extraction {extraction.localId} 与 " +
                            $"{other.Kind} {other.LocalId} 位于 Floor {extraction.floorId}，间距不足。");
                    }
                }
            }
        }

        private FloorRow FindFloor(int floorId)
        {
            return FindFloor(document, floorId);
        }

        private static FloorRow FindFloor(Document value, int floorId)
        {
            for (int index = 0; index < value.floors.Count; index++)
            {
                if (value.floors[index].localId == floorId)
                {
                    return value.floors[index];
                }
            }
            return null;
        }

        private static bool ContainsX(Rect bounds, float x)
        {
            return x >= bounds.xMin - 0.0001f && x <= bounds.xMax + 0.0001f;
        }

        private static bool ContainsX(FloorRow floor, float x)
        {
            return x >= floor.minX - 0.0001f && x <= floor.maxX + 0.0001f;
        }

        private static bool IntervalsOverlap(float leftMin, float leftMax, float rightMin, float rightMax)
        {
            return Mathf.Min(leftMax, rightMax) - Mathf.Max(leftMin, rightMin) > 0.0001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFiniteRect(Rect value)
        {
            return IsFinite(value.xMin) && IsFinite(value.xMax)
                && IsFinite(value.yMin) && IsFinite(value.yMax);
        }

        private readonly struct InteractionFootprint
        {
            public InteractionFootprint(
                string kind,
                int localId,
                int floorId,
                float x,
                float halfWidth)
            {
                Kind = kind;
                LocalId = localId;
                FloorId = floorId;
                X = x;
                HalfWidth = halfWidth;
            }

            public string Kind { get; }
            public int LocalId { get; }
            public int FloorId { get; }
            public float X { get; }
            public float HalfWidth { get; }
        }

        private static bool TryPreparePreviewRefData(
            bool forceReload,
            out string error)
        {
            error = string.Empty;
            try
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

                RefData.CLRefDataModuleCommon common =
                    RefData.CLRefDataModule.instance.refDataModuleCommon;
                if (!common.Inited)
                {
                    common.LoadRefData();
                    common.Init();
                }
                else if (forceReload)
                {
                    common.LoadRefData();
                    common.ReLoadAll_OnlyForEditor();
                    // General is a single-row generated table whose current editor
                    // reload method is empty, so refresh it explicitly.
                    new RefData.GeneralTable().Init();
                }

                if (!common.Inited || !common.LastInitSucceeded)
                {
                    throw new InvalidOperationException(
                        "The shared RefData module did not finish initializing every generated table.");
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "Editor RefData initialization failed: "
                    + exception.GetBaseException().Message;
                return false;
            }
        }

        private void EnsurePreviewRegistry()
        {
            if (previewRegistry != null) return;
            if (!TryPreparePreviewRefData(false, out string refDataError))
            {
                previewRegistry = null;
                previewStatus = refDataError;
                return;
            }
            if (BattleStageConfigurationRegistry.TryLoad(out previewRegistry, out string error))
            {
                previewStatus = "已读取正式运行时表现配置";
            }
            else
            {
                previewRegistry = null;
                previewStatus = "运行时表现配置读取失败：" + error;
            }
        }

        private void ReloadPreviewRegistry()
        {
            previewRegistry = null;
            previewSprites.Clear();
            if (!TryPreparePreviewRefData(true, out string refDataError))
            {
                previewStatus = refDataError;
            }
            else
            {
                EnsurePreviewRegistry();
            }
            ValidateDocument();
            Repaint();
            SceneView.RepaintAll();
        }

        private Sprite LoadPreviewSprite(int resourceId)
        {
            if (resourceId <= 0) return null;
            if (previewSprites.TryGetValue(resourceId, out Sprite cached)) return cached;
            Sprite sprite = null;
            try
            {
                sprite = BattleSmallAreaVisualLayoutUtility.LoadSpriteResource(resourceId);
            }
            catch (Exception exception)
            {
                previewStatus = $"Sprite {resourceId} 加载异常：{exception.Message}";
            }
            previewSprites[resourceId] = sprite;
            return sprite;
        }

        private string DraftKey => DraftKeyPrefix + Hash128.Compute(Application.dataPath).ToString();

        private void QueueDraftSave()
        {
            if (draftSaveQueued) return;
            draftSaveQueued = true;
            EditorApplication.delayCall += () =>
            {
                draftSaveQueued = false;
                if (this != null) SaveDraftNow();
            };
        }

        private void SaveDraftNow()
        {
            if (authoringState == null) return;
            if (!documentDirty && !HasStagedChanges())
            {
                ClearDraft();
                return;
            }

            DraftEnvelope envelope = new DraftEnvelope
            {
                documentJson = JsonUtility.ToJson(document),
                baselineSourceRows = authoringState.baselineSourceRows,
                documentDirty = documentDirty,
                snapEnabled = authoringState.snapEnabled,
                snapSize = authoringState.snapSize,
                visibleLayers = authoringState.visibleLayers,
                lockedLayers = authoringState.lockedLayers,
                nextRowIds = (int[])authoringState.nextRowIds.Clone(),
                workbookPath = workbookSnapshot?.WorkbookPath ?? string.Empty,
                workbookContentHash = workbookSnapshot?.ContentHash ?? string.Empty,
            };
            foreach (KeyValuePair<int, string> pair in workingTemplateRows)
            {
                envelope.workingTemplateIds.Add(pair.Key);
                envelope.workingTemplateRows.Add(pair.Value);
            }
            EditorPrefs.SetString(DraftKey, JsonUtility.ToJson(envelope));
        }

        private void TryRestoreDraft()
        {
            if (draftRestored) return;
            draftRestored = true;
            string json = EditorPrefs.GetString(DraftKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                DraftEnvelope envelope = JsonUtility.FromJson<DraftEnvelope>(json);
                Document restored = JsonUtility.FromJson<Document>(envelope.documentJson);
                if (restored == null) throw new InvalidDataException("草稿 DTO 为空。");
                EnsureLists(restored);
                authoringState.document = restored;
                authoringState.baselineSourceRows = envelope.baselineSourceRows ?? string.Empty;
                authoringState.documentDirty = envelope.documentDirty;
                authoringState.snapEnabled = envelope.snapEnabled;
                authoringState.snapSize = envelope.snapSize > 0f ? envelope.snapSize : 0.5f;
                authoringState.visibleLayers = envelope.visibleLayers == 0
                    ? (int)LayerKind.All
                    : envelope.visibleLayers;
                authoringState.lockedLayers = envelope.lockedLayers;
                authoringState.nextRowIds = envelope.nextRowIds ?? new int[8];
                EnsureNextRowIdArray();

                if (!string.IsNullOrWhiteSpace(envelope.workbookContentHash)
                    && BattleSmallAreaWorkbookBridge.TryLoad(
                        out BattleSmallAreaWorkbookBridge.Snapshot current,
                        out _)
                    && string.Equals(current.WorkbookPath, envelope.workbookPath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(current.ContentHash, envelope.workbookContentHash, StringComparison.Ordinal))
                {
                    workbookSnapshot = current;
                    workingTemplateRows.Clear();
                    int count = Mathf.Min(
                        envelope.workingTemplateIds?.Count ?? 0,
                        envelope.workingTemplateRows?.Count ?? 0);
                    for (int i = 0; i < count; i++)
                        workingTemplateRows[envelope.workingTemplateIds[i]] = envelope.workingTemplateRows[i];
                    if (workingTemplateRows.Count != current.Templates.Count)
                    {
                        workingTemplateRows.Clear();
                        foreach (KeyValuePair<int, string> pair in current.Templates)
                            workingTemplateRows[pair.Key] = pair.Value;
                    }
                    workbookTemplateIndex = Array.IndexOf(current.SmallAreaIds, restored.id);
                    if (workbookTemplateIndex < 0) workbookTemplateIndex = 0;
                    workbookStatus = $"已恢复 SmallArea {restored.id} 草稿和未写回工作集。";
                }
                else
                {
                    workbookSnapshot = null;
                    workingTemplateRows.Clear();
                    workbookStatus = $"已恢复 SmallArea {restored.id} 草稿；正式工作簿已变化，请人工合并后重新读取。";
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BattleSmallAreaVisualEditor] 草稿恢复失败：{exception}");
            }
        }

        private bool HasStagedChanges()
        {
            if (workbookSnapshot == null || workingTemplateRows.Count != workbookSnapshot.Templates.Count)
                return false;
            foreach (KeyValuePair<int, string> pair in workingTemplateRows)
            {
                if (!workbookSnapshot.Templates.TryGetValue(pair.Key, out string source)
                    || !string.Equals(
                        NormalizeSourceRows(pair.Value),
                        NormalizeSourceRows(source),
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private bool HasStagedChangesOtherThan(int smallAreaId)
        {
            if (workbookSnapshot == null) return false;
            foreach (KeyValuePair<int, string> pair in workingTemplateRows)
            {
                if (pair.Key == smallAreaId) continue;
                if (!workbookSnapshot.Templates.TryGetValue(pair.Key, out string source)
                    || !string.Equals(
                        NormalizeSourceRows(pair.Value),
                        NormalizeSourceRows(source),
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void ClearDraft()
        {
            EditorPrefs.DeleteKey(DraftKey);
        }

        private void FocusSceneView()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.in2DMode = true;
            view.Frame(new Bounds(
                new Vector3(document.bounds.center.x, document.bounds.center.y, 0f),
                new Vector3(document.bounds.width, document.bounds.height, 1f)), false);
            view.Focus();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (authoringState == null || document == null) return;
            NormalizeSelectionAfterDocumentChange();
            Handles.color = new Color(0.3f, 0.75f, 1f, 0.8f);
            Handles.DrawWireCube(
                new Vector3(document.bounds.center.x, document.bounds.center.y, 0f),
                new Vector3(document.bounds.width, document.bounds.height, 0f));

            if (IsLayerVisible(LayerKind.Floor))
            {
                for (int i = 0; i < document.floors.Count; i++)
                {
                    FloorRow row = document.floors[i];
                    Rect rect = Rect.MinMaxRect(
                        row.minX,
                        row.y - BattleWorldZoneRuntimeTuning.FloorColliderThickness,
                        row.maxX,
                        row.y);
                    DrawSceneRect(rect, row.isSafeSpawnFloor
                        ? new Color(0.2f, 0.9f, 0.42f, 0.22f)
                        : new Color(0.25f, 0.58f, 0.9f, 0.18f));
                    SceneSelectButton(ToolKind.Floor, i, rect.center, $"F{row.localId}");
                }
            }
            if (IsLayerVisible(LayerKind.EnemyArea))
            {
                for (int i = 0; i < document.enemyAreas.Count; i++)
                {
                    EnemyAreaRow row = document.enemyAreas[i];
                    float y = FloorY(row.floorId);
                    Rect rect = Rect.MinMaxRect(row.minX, y + 0.12f, row.maxX, y + 0.72f);
                    DrawSceneRect(rect, new Color(0.9f, 0.2f, 0.2f, 0.18f));
                    SceneSelectButton(ToolKind.EnemyArea, i, rect.center, $"Enemy {row.localId}");
                }
            }
            if (IsLayerVisible(LayerKind.Ladder))
            {
                for (int i = 0; i < document.ladders.Count; i++)
                {
                    LadderRow row = document.ladders[i];
                    Rect rect;
                    if (TryCreateLadderPreviewLayout(
                            row,
                            out _,
                            out BattleSmallAreaTiledVisualLayout layout,
                            out _))
                    {
                        rect = layout.WorldRect;
                    }
                    else
                    {
                        if (!TryCreateLadderInteractionRect(document, row, out rect))
                        {
                            float effectiveWidth =
                                (float)BattleLadderGeometry.GetEffectiveInteractionWidth(
                                    row.interactionWidth);
                            rect = new Rect(
                                row.x - effectiveWidth * 0.5f,
                                document.bounds.yMin,
                                effectiveWidth,
                                MinimumSegmentWidth);
                        }
                    }
                    DrawSceneRect(rect, new Color(1f, 0.75f, 0.15f, 0.18f));
                    SceneSelectButton(ToolKind.Ladder, i, rect.center, $"L{row.localId}");
                }
            }
            DrawScenePointButtons(ToolKind.Door, document.doors);
            DrawScenePointButtons(ToolKind.Loot, document.lootPoints);
            DrawScenePointButtons(ToolKind.Boss, document.bossPoints);
            DrawScenePointButtons(ToolKind.Extraction, document.extractionPoints);
            DrawSelectedSceneHandles();

            Handles.BeginGUI();
            GUI.Label(new Rect(10f, 10f, 430f, 38f),
                $"Battle SmallArea {document.id} · SceneView 编辑已启用 · " +
                $"吸附 {(authoringState.snapEnabled ? authoringState.snapSize.ToString("0.##") : "关闭")}",
                EditorStyles.helpBox);
            Handles.EndGUI();
        }

        private void DrawScenePointButtons<T>(ToolKind kind, IReadOnlyList<T> rows)
            where T : LocalRow
        {
            if (!IsLayerVisible(ToolLayer(kind))) return;
            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                SceneSelectButton(kind, i,
                    new Vector2(GetPointX(row), FloorY(row.floorId) + 0.45f),
                    $"{kind} {row.localId}");
            }
        }

        private static void DrawSceneRect(Rect rect, Color fill)
        {
            Vector3[] points =
            {
                new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin),
                new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax),
            };
            Handles.DrawSolidRectangleWithOutline(points, fill, new Color(fill.r, fill.g, fill.b, 0.85f));
        }

        private void SceneSelectButton(ToolKind kind, int index, Vector2 center, string label)
        {
            float size = HandleUtility.GetHandleSize(new Vector3(center.x, center.y, 0f)) * 0.075f;
            Handles.color = selectedKind == kind && selectedIndex == index ? Color.white : new Color(0.8f, 0.9f, 1f, 0.85f);
            if (Handles.Button(new Vector3(center.x, center.y, 0f), Quaternion.identity,
                    size, size, Handles.RectangleHandleCap))
            {
                selectedKind = kind;
                selectedIndex = index;
                Repaint();
            }
            Handles.Label(new Vector3(center.x, center.y + size * 1.6f, 0f), label);
        }

        private void DrawSelectedSceneHandles()
        {
            if (!IsSelectionValid() || IsLayerLocked(ToolLayer(selectedKind))) return;
            float snap = authoringState.snapEnabled ? authoringState.snapSize : 0f;
            if (selectedKind == ToolKind.Floor && selectedIndex < document.floors.Count)
            {
                FloorRow row = document.floors[selectedIndex];
                float centerX = (row.minX + row.maxX) * 0.5f;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(new Vector3(centerX, row.y, 0f), Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(authoringState, "SceneView 移动 Floor");
                    float newCenterX = Snap(moved.x);
                    float deltaX = newCenterX - centerX;
                    row.minX += deltaX;
                    row.maxX += deltaX;
                    row.y = Snap(moved.y);
                    OnAuthoringChanged();
                }
                DrawSceneEndpoints(row.minX, row.maxX, row.y, snap,
                    (minimum, maximum) => { row.minX = minimum; row.maxX = maximum; }, "Floor");
            }
            else if (selectedKind == ToolKind.EnemyArea && selectedIndex < document.enemyAreas.Count)
            {
                EnemyAreaRow row = document.enemyAreas[selectedIndex];
                float y = FloorY(row.floorId) + 0.42f;
                float centerX = (row.minX + row.maxX) * 0.5f;
                EditorGUI.BeginChangeCheck();
                float movedX = Handles.Slider(new Vector3(centerX, y, 0f), Vector3.right, 0.22f,
                    Handles.RectangleHandleCap, snap).x;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(authoringState, "SceneView 移动 EnemyArea");
                    float delta = Snap(movedX) - centerX;
                    row.minX += delta;
                    row.maxX += delta;
                    OnAuthoringChanged();
                }
                DrawSceneEndpoints(row.minX, row.maxX, y, snap,
                    (minimum, maximum) => { row.minX = minimum; row.maxX = maximum; }, "EnemyArea");
            }
            else
            {
                DrawScenePointMoveHandle(snap);
            }
        }

        private void DrawSceneEndpoints(
            float minimumX,
            float maximumX,
            float y,
            float snap,
            Action<float, float> apply,
            string label)
        {
            float size = HandleUtility.GetHandleSize(new Vector3(minimumX, y, 0f)) * 0.09f;
            EditorGUI.BeginChangeCheck();
            float newMinimum = Handles.Slider(new Vector3(minimumX, y, 0f), Vector3.right,
                size, Handles.CubeHandleCap, snap).x;
            float newMaximum = Handles.Slider(new Vector3(maximumX, y, 0f), Vector3.right,
                size, Handles.CubeHandleCap, snap).x;
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(authoringState, $"SceneView 调整 {label} 端点");
            float width = authoringState.snapEnabled
                ? Mathf.Max(MinimumSegmentWidth, authoringState.snapSize)
                : MinimumSegmentWidth;
            newMinimum = Snap(newMinimum);
            newMaximum = Snap(newMaximum);
            if (newMaximum - newMinimum < width)
            {
                if (Mathf.Abs(newMinimum - minimumX) > Mathf.Abs(newMaximum - maximumX))
                    newMinimum = newMaximum - width;
                else newMaximum = newMinimum + width;
            }
            apply(newMinimum, newMaximum);
            OnAuthoringChanged();
        }

        private void DrawScenePointMoveHandle(float snap)
        {
            LocalRow row = SelectedRow();
            if (row == null) return;
            float x = GetSelectedX();
            float y;
            if (row is LadderRow ladder
                && TryCreateLadderInteractionRect(document, ladder, out Rect ladderRect))
            {
                y = ladderRect.center.y;
            }
            else
            {
                y = FloorY(row.floorId) + 0.45f;
            }
            float size = HandleUtility.GetHandleSize(new Vector3(x, y, 0f)) * 0.1f;
            EditorGUI.BeginChangeCheck();
            float moved = Handles.Slider(new Vector3(x, y, 0f), Vector3.right,
                size, Handles.RectangleHandleCap, snap).x;
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(authoringState, $"SceneView 移动 {selectedKind}");
            SetSelectedX(Snap(moved));
            OnAuthoringChanged();
        }

        private LocalRow SelectedRow()
        {
            if (selectedIndex < 0) return null;
            switch (selectedKind)
            {
                case ToolKind.Floor: return selectedIndex < document.floors.Count ? document.floors[selectedIndex] : null;
                case ToolKind.Ladder: return selectedIndex < document.ladders.Count ? document.ladders[selectedIndex] : null;
                case ToolKind.Door: return selectedIndex < document.doors.Count ? document.doors[selectedIndex] : null;
                case ToolKind.EnemyArea: return selectedIndex < document.enemyAreas.Count ? document.enemyAreas[selectedIndex] : null;
                case ToolKind.Loot: return selectedIndex < document.lootPoints.Count ? document.lootPoints[selectedIndex] : null;
                case ToolKind.Boss: return selectedIndex < document.bossPoints.Count ? document.bossPoints[selectedIndex] : null;
                case ToolKind.Extraction: return selectedIndex < document.extractionPoints.Count ? document.extractionPoints[selectedIndex] : null;
                default: return null;
            }
        }

        private void SetSelectedX(float value)
        {
            LocalRow row = SelectedRow();
            if (row == null) return;
            if (row is LadderRow ladder)
            {
                ladder.x = ClampLadderX(ladder, value);
                return;
            }

            FloorRow floor = FindNearestFloor(new Vector2(value, FloorY(row.floorId)));
            if (floor != null)
            {
                row.floorId = floor.localId;
                value = Mathf.Clamp(value, floor.minX, floor.maxX);
            }
            if (row is DoorRow door) door.x = value;
            else if (row is LootRow loot) loot.x = value;
            else if (row is BossRow boss) boss.x = value;
            else if (row is ExtractionRow extraction) extraction.x = value;
        }

        private void ImportDocument()
        {
            string path = EditorUtility.OpenFilePanel("导入 BattleSmallArea DTO 或源行", string.Empty, "json,txt");
            if (!string.IsNullOrWhiteSpace(path)) TryImportText(File.ReadAllText(path, Encoding.UTF8), path);
        }

        private void LoadOfficialWorkbook()
        {
            if ((documentDirty || HasStagedChanges())
                && !EditorUtility.DisplayDialog(
                    "重新读取正式源表",
                    "当前画布或跨模板工作集有尚未写回的修改。重新读取会丢弃这些修改，是否继续？",
                    "丢弃并读取",
                    "取消"))
            {
                return;
            }

            if (!BattleSmallAreaWorkbookBridge.TryLoad(
                    out workbookSnapshot,
                    out string error))
            {
                workbookStatus = error;
                ShowNotification(new GUIContent("正式源表读取失败"));
                Debug.LogError($"[BattleSmallAreaVisualEditor] {error}");
                return;
            }

            workingTemplateRows.Clear();
            foreach (KeyValuePair<int, string> pair in workbookSnapshot.Templates)
            {
                workingTemplateRows.Add(pair.Key, pair.Value);
            }
            Undo.ClearUndo(authoringState);
            sourceSavedOutputStale = false;
            documentDirty = false;
            Array.Clear(authoringState.nextRowIds, 0, authoringState.nextRowIds.Length);
            workbookTemplateIndex = 0;
            for (int index = 0; index < workbookSnapshot.SmallAreaIds.Length; index++)
            {
                if (workbookSnapshot.SmallAreaIds[index] == document.id)
                {
                    workbookTemplateIndex = index;
                    break;
                }
            }
            workbookStatus =
                $"已读取 {workbookSnapshot.SmallAreaIds.Length} 个正式模板：" +
                $"{workbookSnapshot.WorkbookPath}";
            LoadSelectedOfficialTemplate();
        }

        private void LoadSelectedOfficialTemplate()
        {
            if (workbookSnapshot?.SmallAreaIds == null
                || workbookSnapshot.SmallAreaIds.Length == 0)
            {
                workbookStatus = "请先读取正式源表。";
                return;
            }

            if (documentDirty && !TryStageCurrentTemplate())
            {
                if (TryPlanPartialStage(out PartialStagePlan plan, out string partialStageError))
                {
                    CommitPartialStage(plan);
                    string accepted = plan.Accepted.Count > 0
                        ? string.Join("、", plan.Accepted.Select(PartialStageSectionName))
                        : "无（沿用上次已暂存版本）";
                    string rejected = plan.Rejected.Count > 0
                        ? string.Join("、", plan.Rejected.Select(PartialStageSectionName))
                        : "无";
                    string issues = validationIssues.Count > 0
                        ? "\n\n当前问题：\n" + string.Join(
                            "\n",
                            validationIssues.Take(8).Select(issue => "• " + issue))
                        : string.Empty;
                    bool continueLoad = EditorUtility.DisplayDialog(
                        "已暂存可安全保留的部分",
                        $"已暂存：{accepted}\n未暂存：{rejected}\n\n" +
                        "未暂存配置仍保留在当前画布；继续载入后会丢弃这些配置。\n" +
                        "部分暂存按配置块原子处理，并且不会提交删除操作。" + issues,
                        "继续载入（丢弃未暂存）",
                        "留在当前修复");
                    if (!continueLoad)
                    {
                        RestoreWorkbookTemplateIndexToDocument();
                        ShowNotification(new GUIContent("有效部分已暂存，未通过部分仍保留"));
                        return;
                    }
                }
                else
                {
                    bool discard = EditorUtility.DisplayDialog(
                        "当前修改无法部分暂存",
                        $"无法从当前修改生成完整合法的暂存版本：\n{partialStageError}\n\n" +
                        "可以丢弃当前修改并载入选择；正式源表不会被修改。",
                        "全部丢弃并载入",
                        "取消");
                    if (!discard)
                    {
                        RestoreWorkbookTemplateIndexToDocument();
                        ShowNotification(new GUIContent("已取消载入，当前修改仍保留"));
                        return;
                    }
                }
            }

            workbookTemplateIndex = Mathf.Clamp(
                workbookTemplateIndex,
                0,
                workbookSnapshot.SmallAreaIds.Length - 1);
            int id = workbookSnapshot.SmallAreaIds[workbookTemplateIndex];
            if (!workingTemplateRows.TryGetValue(id, out string sourceRows))
            {
                workbookStatus = $"当前工作集缺少 smallAreaId={id}。";
                return;
            }

            if (!TryImportText(
                    sourceRows,
                    $"当前工作集 smallAreaId={id}",
                    true,
                    false))
            {
                workbookStatus = $"工作集模板 {id} 未通过完整校验，保留当前画布。";
                RestoreWorkbookTemplateIndexToDocument();
                return;
            }
            workbookStatus =
                $"正在编辑正式模板 {id}；切换模板时会暂存已通过校验的修改。";
        }

        private void RestoreWorkbookTemplateIndexToDocument()
        {
            if (workbookSnapshot?.SmallAreaIds == null) return;
            int index = Array.IndexOf(workbookSnapshot.SmallAreaIds, document.id);
            if (index >= 0) workbookTemplateIndex = index;
        }

        private void WriteCurrentTemplateToOfficialWorkbook()
        {
            ValidateDocument();
            if (validationIssues.Count > 0)
            {
                workbookStatus =
                    $"当前模板有 {validationIssues.Count} 个结构/冲突问题，拒绝写回。";
                ShowNotification(new GUIContent("校验未通过，未写回"));
                return;
            }

            if (workbookSnapshot == null)
            {
                workbookStatus = "请先读取正式源表，再写回当前模板。";
                return;
            }

            if (HasStagedChangesOtherThan(document.id))
            {
                workbookStatus =
                    "其它 SmallArea 还有已暂存但未写回的修改；为避免写回当前后丢失工作集，请使用“写回全部”。";
                ShowNotification(new GUIContent("其它模板有修改，请写回全部"));
                return;
            }

            if (sourceSavedOutputStale)
            {
                workbookStatus =
                    "源表与 Output 状态尚未确认一致；请先用 RefData 导出工具重导 b.战斗关卡表.xlsx，再重新读取。";
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "写回 BattleSmallArea 正式源表",
                    $"将把 smallAreaId={document.id} 的 8 组源行事务性写回并增量导表：\n" +
                    $"{workbookSnapshot.WorkbookPath}\n\n" +
                    "只有源表原子替换、统一导表事务和 Output 回读全部通过才报告成功。",
                    "写回并导表",
                    "取消"))
            {
                return;
            }

            Dictionary<int, string> current = new Dictionary<int, string>
            {
                { document.id, BuildSourceRows(document) },
            };
            if (!BattleSmallAreaWorkbookBridge.TryWriteTemplatesAndExport(
                    workbookSnapshot,
                    current,
                    out string backupPath,
                    out sourceSavedOutputStale,
                    out string error))
            {
                workbookStatus = error;
                Debug.LogError($"[BattleSmallAreaVisualEditor] {error}");
                ShowNotification(new GUIContent(
                    sourceSavedOutputStale
                        ? "源表已写但 Output 未确认，请看状态"
                        : "写回/导表失败，未形成半套结果"));
                return;
            }

            workbookStatus = $"源表、增量导表与 Output 回读全部成功；备份：{backupPath}";
            ShowNotification(new GUIContent("正式源表与 Output 已同步"));
            int editedId = document.id;
            ReloadWorkbookAfterWrite(editedId);
        }

        private void WriteAllTemplatesToOfficialWorkbook()
        {
            if (workbookSnapshot == null)
            {
                workbookStatus = "请先读取全部正式模板。";
                return;
            }

            if (sourceSavedOutputStale)
            {
                workbookStatus =
                    "源表与 Output 状态尚未确认一致；请先重导正式表并重新读取。";
                return;
            }

            if (!TryStageCurrentTemplate())
            {
                ShowNotification(new GUIContent("当前模板未通过校验，未写回全部"));
                return;
            }

            if (workingTemplateRows.Count != workbookSnapshot.Templates.Count)
            {
                workbookStatus =
                    $"工作集模板数不完整：working={workingTemplateRows.Count}, " +
                    $"snapshot={workbookSnapshot.Templates.Count}。";
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "一次写回全部 BattleSmallArea 模板",
                    $"将把内存中的 {workingTemplateRows.Count} 个模板一次写入同一临时副本，" +
                    "完整回读后原子替换正式 xlsx，再执行统一增量导表并逐模板验证 Output。",
                    "全部写回并导表",
                    "取消"))
            {
                return;
            }

            if (!BattleSmallAreaWorkbookBridge.TryWriteTemplatesAndExport(
                    workbookSnapshot,
                    workingTemplateRows,
                    out string backupPath,
                    out sourceSavedOutputStale,
                    out string error))
            {
                workbookStatus = error;
                Debug.LogError($"[BattleSmallAreaVisualEditor] 全部模板写回失败：{error}");
                ShowNotification(new GUIContent(
                    sourceSavedOutputStale
                        ? "源表已写但 Output 未确认，请看状态"
                        : "全部模板事务失败"));
                return;
            }

            int editedId = document.id;
            workbookStatus =
                $"{workingTemplateRows.Count} 个模板、增量导表与 Output 回读全部成功；" +
                $"备份：{backupPath}";
            ShowNotification(new GUIContent("全部正式模板与 Output 已同步"));
            ReloadWorkbookAfterWrite(editedId);
        }

        private bool TryStageCurrentTemplate()
        {
            ValidateDocument();
            if (validationIssues.Count > 0)
            {
                workbookStatus =
                    $"当前模板有 {validationIssues.Count} 个结构/冲突问题，不能暂存。";
                return false;
            }

            if (!workingTemplateRows.ContainsKey(document.id))
            {
                workbookStatus =
                    $"smallAreaId={document.id} 不属于本次正式工作簿快照；" +
                    "新增模板请先在源表建立正式行后重新读取。";
                return false;
            }

            workingTemplateRows[document.id] = BuildSourceRows(document);
            authoringState.baselineSourceRows = workingTemplateRows[document.id];
            documentDirty = false;
            workbookStatus = $"模板 {document.id} 已暂存到当前工作集。";
            QueueDraftSave();
            return true;
        }

        private bool TryPlanPartialStage(out PartialStagePlan plan, out string error)
        {
            plan = null;
            error = string.Empty;
            if (!workingTemplateRows.TryGetValue(document.id, out string baselineRows))
            {
                error = $"当前工作集不存在 smallAreaId={document.id}。";
                return false;
            }

            try
            {
                Document baseline = ParseSourceRows(baselineRows);
                List<string> baselineIssues = new List<string>();
                ValidateDocument(baseline, baselineIssues);
                if (baselineIssues.Count > 0)
                {
                    error = "上次已暂存版本本身未通过校验，不能作为部分合并基线：" +
                        string.Join("；", baselineIssues.Take(4));
                    return false;
                }

                PartialStageSection[] sections =
                {
                    PartialStageSection.Main,
                    PartialStageSection.Floor,
                    PartialStageSection.Ladder,
                    PartialStageSection.Door,
                    PartialStageSection.EnemyArea,
                    PartialStageSection.Loot,
                    PartialStageSection.Boss,
                    PartialStageSection.Extraction,
                };
                List<PartialStageSection> changed = new List<PartialStageSection>();
                List<PartialStageSection> forcedRejected = new List<PartialStageSection>();
                string normalizedBaseline = NormalizeSourceRows(BuildSourceRows(baseline));
                for (int index = 0; index < sections.Length; index++)
                {
                    PartialStageSection section = sections[index];
                    Document rawSection = MergePartialStageSections(
                        baseline,
                        document,
                        section,
                        false);
                    if (string.Equals(
                            NormalizeSourceRows(BuildSourceRows(rawSection)),
                            normalizedBaseline,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (PartialStageSectionHasDeletion(baseline, document, section))
                        forcedRejected.Add(section);
                    else
                        changed.Add(section);
                }

                int combinationCount = 1 << changed.Count;
                int bestScore = -1;
                List<int> bestMasks = new List<int>();
                for (int mask = 0; mask < combinationCount; mask++)
                {
                    PartialStageSection selected = PartialStageSection.None;
                    for (int bit = 0; bit < changed.Count; bit++)
                    {
                        if ((mask & (1 << bit)) != 0) selected |= changed[bit];
                    }
                    Document candidate = MergePartialStageSections(
                        baseline,
                        document,
                        selected,
                        true);
                    List<string> candidateIssues = new List<string>();
                    ValidateDocument(candidate, candidateIssues);
                    if (candidateIssues.Count > 0) continue;
                    int score = CountBits(mask);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMasks.Clear();
                        bestMasks.Add(mask);
                    }
                    else if (score == bestScore)
                    {
                        bestMasks.Add(mask);
                    }
                }

                if (bestMasks.Count == 0)
                {
                    error = "没有找到能够保持模板整体合法的配置组合。";
                    return false;
                }

                int commonMask = combinationCount - 1;
                for (int index = 0; index < bestMasks.Count; index++)
                {
                    commonMask &= bestMasks[index];
                }
                PartialStageSection acceptedSections = PartialStageSection.None;
                for (int bit = 0; bit < changed.Count; bit++)
                {
                    if ((commonMask & (1 << bit)) != 0) acceptedSections |= changed[bit];
                }
                Document merged = MergePartialStageSections(
                    baseline,
                    document,
                    acceptedSections,
                    true);
                List<string> mergedIssues = new List<string>();
                ValidateDocument(merged, mergedIssues);
                if (mergedIssues.Count > 0)
                {
                    error = "保守合并候选未通过最终校验：" +
                        string.Join("；", mergedIssues.Take(4));
                    return false;
                }

                string roundTripRows = BuildSourceRows(merged);
                Document roundTrip = ParseSourceRows(roundTripRows);
                List<string> roundTripIssues = new List<string>();
                ValidateDocument(roundTrip, roundTripIssues);
                if (roundTripIssues.Count > 0)
                {
                    error = "部分暂存候选在序列化回读后未通过校验：" +
                        string.Join("；", roundTripIssues.Take(4));
                    return false;
                }

                plan = new PartialStagePlan { Candidate = roundTrip };
                for (int index = 0; index < changed.Count; index++)
                {
                    if ((acceptedSections & changed[index]) != 0)
                        plan.Accepted.Add(changed[index]);
                    else
                        plan.Rejected.Add(changed[index]);
                }
                plan.Rejected.AddRange(forcedRejected);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private void CommitPartialStage(PartialStagePlan plan)
        {
            if (plan?.Candidate == null) return;
            string sourceRows = BuildSourceRows(plan.Candidate);
            workingTemplateRows[document.id] = sourceRows;
            authoringState.baselineSourceRows = sourceRows;
            RefreshDirtyFlag();
            workbookStatus =
                $"模板 {document.id} 已部分暂存：接受 {plan.Accepted.Count} 个配置块，" +
                $"保留 {plan.Rejected.Count} 个未通过配置块在画布中。";
            QueueDraftSave();
        }

        private static Document MergePartialStageSections(
            Document baseline,
            Document current,
            PartialStageSection sections,
            bool preserveBaselineRows)
        {
            Document result = CloneDocument(baseline);
            Document source = CloneDocument(current);
            if ((sections & PartialStageSection.Main) != 0)
            {
                result.codeName = source.codeName;
                result.nameLanguageKey = source.nameLanguageKey;
                result.usageType = source.usageType;
                result.backgroundResourceId = source.backgroundResourceId;
                result.backgroundOffset = source.backgroundOffset;
                result.backgroundScale = source.backgroundScale;
                result.bounds = source.bounds;
            }
            if ((sections & PartialStageSection.Floor) != 0)
                result.floors = MergePartialStageRows(result.floors, source.floors, preserveBaselineRows);
            if ((sections & PartialStageSection.Ladder) != 0)
                result.ladders = MergePartialStageRows(result.ladders, source.ladders, preserveBaselineRows);
            if ((sections & PartialStageSection.Door) != 0)
                result.doors = MergePartialStageRows(result.doors, source.doors, preserveBaselineRows);
            if ((sections & PartialStageSection.EnemyArea) != 0)
                result.enemyAreas = MergePartialStageRows(result.enemyAreas, source.enemyAreas, preserveBaselineRows);
            if ((sections & PartialStageSection.Loot) != 0)
                result.lootPoints = MergePartialStageRows(result.lootPoints, source.lootPoints, preserveBaselineRows);
            if ((sections & PartialStageSection.Boss) != 0)
                result.bossPoints = MergePartialStageRows(result.bossPoints, source.bossPoints, preserveBaselineRows);
            if ((sections & PartialStageSection.Extraction) != 0)
                result.extractionPoints = MergePartialStageRows(result.extractionPoints, source.extractionPoints, preserveBaselineRows);
            return result;
        }

        private static List<T> MergePartialStageRows<T>(
            List<T> baseline,
            List<T> current,
            bool preserveBaselineRows)
            where T : LocalRow
        {
            if (!preserveBaselineRows) return current;
            List<T> merged = new List<T>(current);
            HashSet<int> currentRowIds = new HashSet<int>(current.Select(row => row.rowId));
            for (int index = 0; index < baseline.Count; index++)
            {
                if (!currentRowIds.Contains(baseline[index].rowId))
                    merged.Add(baseline[index]);
            }
            return merged;
        }

        private static bool PartialStageSectionHasDeletion(
            Document baseline,
            Document current,
            PartialStageSection section)
        {
            switch (section)
            {
                case PartialStageSection.Floor: return HasMissingRows(baseline.floors, current.floors);
                case PartialStageSection.Ladder: return HasMissingRows(baseline.ladders, current.ladders);
                case PartialStageSection.Door: return HasMissingRows(baseline.doors, current.doors);
                case PartialStageSection.EnemyArea: return HasMissingRows(baseline.enemyAreas, current.enemyAreas);
                case PartialStageSection.Loot: return HasMissingRows(baseline.lootPoints, current.lootPoints);
                case PartialStageSection.Boss: return HasMissingRows(baseline.bossPoints, current.bossPoints);
                case PartialStageSection.Extraction: return HasMissingRows(baseline.extractionPoints, current.extractionPoints);
                default: return false;
            }
        }

        private static bool HasMissingRows<T>(List<T> baseline, List<T> current)
            where T : LocalRow
        {
            HashSet<int> currentRowIds = new HashSet<int>(current.Select(row => row.rowId));
            return baseline.Any(row => !currentRowIds.Contains(row.rowId));
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        private static string PartialStageSectionName(PartialStageSection section)
        {
            switch (section)
            {
                case PartialStageSection.Main: return "SmallArea 主配置";
                case PartialStageSection.Floor: return "Floor";
                case PartialStageSection.Ladder: return "Ladder";
                case PartialStageSection.Door: return "Door";
                case PartialStageSection.EnemyArea: return "EnemyArea";
                case PartialStageSection.Loot: return "Loot";
                case PartialStageSection.Boss: return "Boss";
                case PartialStageSection.Extraction: return "Extraction";
                default: return section.ToString();
            }
        }

        private void ReloadWorkbookAfterWrite(int editedId)
        {
            if (BattleSmallAreaWorkbookBridge.TryLoad(
                    out BattleSmallAreaWorkbookBridge.Snapshot refreshed,
                    out string reloadError))
            {
                workbookSnapshot = refreshed;
                workingTemplateRows.Clear();
                foreach (KeyValuePair<int, string> pair in refreshed.Templates)
                {
                    workingTemplateRows.Add(pair.Key, pair.Value);
                }
                Undo.ClearUndo(authoringState);
                workbookTemplateIndex = Array.IndexOf(
                    refreshed.SmallAreaIds,
                    editedId);
                if (workbookTemplateIndex < 0) workbookTemplateIndex = 0;
                if (!workingTemplateRows.TryGetValue(editedId, out string refreshedRows)
                    || !TryImportText(
                        refreshedRows,
                        $"写回回读 smallAreaId={editedId}",
                        true,
                        false))
                {
                    workbookStatus += "；写回后当前模板回载失败，画布保留写回前版本，请重新读取正式模板。";
                    return;
                }
                sourceSavedOutputStale = false;
                ClearDraft();
            }
            else
            {
                workbookSnapshot = null;
                workbookStatus += $"；重新读取校验失败：{reloadError}";
            }
        }

        private bool TryImportText(
            string text,
            string source,
            bool markClean = false,
            bool requireConfirmation = true)
        {
            try
            {
                Document parsed = !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("{")
                    ? JsonUtility.FromJson<Document>(text)
                    : ParseSourceRows(text);
                if (parsed == null) throw new InvalidDataException("没有解析出 DTO。");
                EnsureLists(parsed);
                EnsurePreviewRegistry();
                List<string> candidateIssues = new List<string>();
                ValidateDocument(parsed, candidateIssues);
                if (candidateIssues.Count > 0)
                {
                    string details = string.Join("\n", candidateIssues.Select(issue => "- " + issue));
                    Debug.LogError(
                        $"[BattleSmallAreaVisualEditor] 候选导入被拒绝，当前画布未改变：" +
                        $"source={source}\n{details}");
                    EditorUtility.DisplayDialog(
                        "候选数据校验失败",
                        $"{source} 有 {candidateIssues.Count} 个问题，当前画布已原样保留。\n\n" +
                        string.Join("\n", candidateIssues.Take(12)) +
                        (candidateIssues.Count > 12 ? "\n……其余请看 Console" : string.Empty),
                        "知道了");
                    return false;
                }

                if (requireConfirmation)
                {
                    string summary =
                        $"SmallArea {document.id} → {parsed.id}\n" +
                        $"Floor {document.floors.Count} → {parsed.floors.Count}\n" +
                        $"Ladder {document.ladders.Count} → {parsed.ladders.Count}\n" +
                        $"Door {document.doors.Count} → {parsed.doors.Count}\n" +
                        $"EnemyArea {document.enemyAreas.Count} → {parsed.enemyAreas.Count}\n" +
                        $"Loot/Boss/Extraction " +
                        $"{document.lootPoints.Count}/{document.bossPoints.Count}/{document.extractionPoints.Count} → " +
                        $"{parsed.lootPoints.Count}/{parsed.bossPoints.Count}/{parsed.extractionPoints.Count}";
                    if (!EditorUtility.DisplayDialog(
                            "应用已校验候选",
                            $"候选已通过完整校验。确认原子替换当前画布？\n\n{summary}",
                            "替换",
                            "取消"))
                    {
                        return false;
                    }
                }

                if (!markClean)
                {
                    Undo.RecordObject(authoringState, "导入 Battle SmallArea 候选");
                }
                document = parsed;
                authoringState.baselineSourceRows = markClean
                    ? BuildSourceRows(parsed)
                    : workingTemplateRows.TryGetValue(parsed.id, out string baseline)
                        ? baseline
                        : string.Empty;
                selectedKind = ToolKind.Select;
                selectedIndex = -1;
                if (markClean)
                {
                    Undo.ClearUndo(authoringState);
                }
                ValidateDocument();
                RefreshDirtyFlag();
                QueueDraftSave();
                Repaint();
                SceneView.RepaintAll();
                ShowNotification(new GUIContent($"已导入 {source}"));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BattleSmallAreaVisualEditor] 导入失败：source={source}\n{exception}");
                EditorUtility.DisplayDialog(
                    "导入解析失败",
                    $"{source} 无法解析，当前画布已原样保留。\n\n{exception.Message}",
                    "知道了");
                return false;
            }
        }

        private void ExportDocument()
        {
            string path = EditorUtility.SaveFilePanel("导出 BattleSmallArea DTO", string.Empty, $"battle_small_area_{document.id}.json", "json");
            if (string.IsNullOrWhiteSpace(path)) return;
            File.WriteAllText(path, JsonUtility.ToJson(document, true), new UTF8Encoding(false));
            ShowNotification(new GUIContent("DTO 已导出"));
        }

        private static void EnsureLists(Document value)
        {
            value.floors ??= new List<FloorRow>(); value.ladders ??= new List<LadderRow>();
            value.doors ??= new List<DoorRow>(); value.enemyAreas ??= new List<EnemyAreaRow>();
            value.lootPoints ??= new List<LootRow>(); value.bossPoints ??= new List<BossRow>();
            value.extractionPoints ??= new List<ExtractionRow>();
        }

        private static Document CloneDocument(Document value)
        {
            Document clone = JsonUtility.FromJson<Document>(JsonUtility.ToJson(value));
            if (clone == null) throw new InvalidOperationException("无法克隆 SmallArea 编辑文档。");
            EnsureLists(clone);
            return clone;
        }

        private static string BuildSourceRows(Document value)
        {
            StringBuilder text = new StringBuilder(4096);
            text.AppendLine("[BattleSmallArea]");
            text.AppendLine("id\tcodeName\tnameLanguageKey\tusageType\tbackgroundResourceId\tbackgroundOffsetX\tbackgroundOffsetY\tbackgroundScaleX\tbackgroundScaleY\tminX\tmaxX\tminY\tmaxY");
            text.Append(value.id).Append('\t').Append(value.codeName).Append('\t')
                .Append(value.nameLanguageKey).Append('\t')
                .Append(value.usageType == 1 ? "Boss" : "Normal").Append('\t')
                .Append(value.backgroundResourceId).Append('\t').Append(F(value.backgroundOffset.x)).Append('\t').Append(F(value.backgroundOffset.y)).Append('\t')
                .Append(F(value.backgroundScale.x)).Append('\t').Append(F(value.backgroundScale.y)).Append('\t')
                .Append(F(value.bounds.xMin)).Append('\t').Append(F(value.bounds.xMax)).Append('\t').Append(F(value.bounds.yMin)).Append('\t').Append(F(value.bounds.yMax)).AppendLine();
            AppendFloorRows(text, value); AppendLadderRows(text, value); AppendDoorRows(text, value);
            AppendEnemyRows(text, value); AppendLootRows(text, value); AppendBossRows(text, value); AppendExtractionRows(text, value);
            return text.ToString();
        }

        private static void AppendFloorRows(StringBuilder text, Document value)
        {
            text.AppendLine("[BattleSmallAreaFloor]"); text.AppendLine("id\tsmallAreaId\tfloorId\tcollisionType\tminX\tmaxX\ty\tisSafeSpawnFloor\tfloorStyleId");
            foreach (FloorRow row in value.floors) text.Append(row.rowId).Append('\t').Append(value.id).Append('\t').Append(row.localId).Append('\t').Append(row.collisionType == 1 ? "OneWayPlatform" : "SolidGround").Append('\t').Append(F(row.minX)).Append('\t').Append(F(row.maxX)).Append('\t').Append(F(row.y)).Append('\t').Append(row.isSafeSpawnFloor ? "true" : "false").Append('\t').Append(row.styleId).AppendLine();
        }
        private static void AppendLadderRows(StringBuilder text, Document value)
        {
            text.AppendLine("[BattleSmallAreaLadder]"); text.AppendLine("id\tsmallAreaId\tladderId\tlowerFloorId\tupperFloorId\tx\tinteractionWidth\tladderStyleId");
            foreach (LadderRow row in value.ladders) text.Append(row.rowId).Append('\t').Append(value.id).Append('\t').Append(row.localId).Append('\t').Append(row.floorId).Append('\t').Append(row.upperFloorId).Append('\t').Append(F(row.x)).Append('\t').Append(F(row.interactionWidth)).Append('\t').Append(row.styleId).AppendLine();
        }
        private static void AppendDoorRows(StringBuilder text, Document value)
        {
            text.AppendLine("[BattleSmallAreaDoorPoint]"); text.AppendLine("id\tsmallAreaId\tdoorId\tfloorId\tx\tdoorStyleId");
            foreach (DoorRow row in value.doors) text.Append(row.rowId).Append('\t').Append(value.id).Append('\t').Append(row.localId).Append('\t').Append(row.floorId).Append('\t').Append(F(row.x)).Append('\t').Append(row.styleId).AppendLine();
        }
        private static void AppendEnemyRows(StringBuilder text, Document value)
        {
            text.AppendLine("[BattleSmallAreaEnemySpawnArea]"); text.AppendLine("id\tsmallAreaId\tslotId\tfloorId\tminX\tmaxX\tspawnRuleId");
            foreach (EnemyAreaRow row in value.enemyAreas) text.Append(row.rowId).Append('\t').Append(value.id).Append('\t').Append(row.localId).Append('\t').Append(row.floorId).Append('\t').Append(F(row.minX)).Append('\t').Append(F(row.maxX)).Append('\t').Append(row.spawnRuleId).AppendLine();
        }
        private static void AppendLootRows(StringBuilder text, Document value)
        {
            text.AppendLine("[BattleSmallAreaLootSpawnPoint]"); text.AppendLine("id\tsmallAreaId\tslotId\tfloorId\tx\tbaseSpawnChance\tlootSourceId");
            foreach (LootRow row in value.lootPoints) text.Append(row.rowId).Append('\t').Append(value.id).Append('\t').Append(row.localId).Append('\t').Append(row.floorId).Append('\t').Append(F(row.x)).Append('\t').Append(F(row.baseSpawnChance)).Append('\t').Append(row.lootSourceId).AppendLine();
        }
        private static void AppendBossRows(StringBuilder text, Document value)
        {
            text.AppendLine("[BattleSmallAreaBossSpawnPoint]"); text.AppendLine("id\tsmallAreaId\tslotId\tfloorId\tx");
            foreach (BossRow row in value.bossPoints) text.Append(row.rowId).Append('\t').Append(value.id).Append('\t').Append(row.localId).Append('\t').Append(row.floorId).Append('\t').Append(F(row.x)).AppendLine();
        }
        private static void AppendExtractionRows(StringBuilder text, Document value)
        {
            text.AppendLine("[BattleSmallAreaExtractionSpawnPoint]"); text.AppendLine("id\tsmallAreaId\tslotId\tfloorId\tx");
            foreach (ExtractionRow row in value.extractionPoints) text.Append(row.rowId).Append('\t').Append(value.id).Append('\t').Append(row.localId).Append('\t').Append(row.floorId).Append('\t').Append(F(row.x)).AppendLine();
        }

        private static Document ParseSourceRows(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("源行为空。");
            Document value = new Document(); EnsureLists(value);
            string[] requiredSections =
            {
                "BattleSmallArea", "BattleSmallAreaFloor", "BattleSmallAreaLadder",
                "BattleSmallAreaDoorPoint", "BattleSmallAreaEnemySpawnArea",
                "BattleSmallAreaLootSpawnPoint", "BattleSmallAreaBossSpawnPoint",
                "BattleSmallAreaExtractionSpawnPoint",
            };
            HashSet<string> required = new HashSet<string>(requiredSections, StringComparer.Ordinal);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<int> referencedAreaIds = new List<int>();
            bool hasAreaRow = false;
            string section = string.Empty;
            string[] lines = text.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim(); if (line.Length == 0) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2);
                    if (!required.Contains(section)) throw new InvalidDataException($"未知源表段：{section}。");
                    if (!seen.Add(section)) throw new InvalidDataException($"源表段重复：{section}。");
                    if (++i >= lines.Length || string.IsNullOrWhiteSpace(lines[i]))
                        throw new InvalidDataException($"{section} 缺少表头。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(section)) throw new InvalidDataException("数据行出现在任何源表段之前。");
                string[] c = lines[i].Split('\t');
                int rowId = I(c, 0, section);
                switch (section)
                {
                    case "BattleSmallArea":
                    {
                        if (hasAreaRow) throw new InvalidDataException("BattleSmallArea 只能有一行。");
                        string usage = Required(c, 3, section);
                        if (usage != "Normal" && usage != "Boss") throw new InvalidDataException($"usageType 非法：{usage}。");
                        value.id = rowId; value.codeName = Required(c, 1, section); value.nameLanguageKey = Required(c, 2, section);
                        value.usageType = usage == "Boss" ? 1 : 0; value.backgroundResourceId = I(c, 4, section);
                        value.backgroundOffset = new Vector2(V(c, 5, section), V(c, 6, section));
                        value.backgroundScale = new Vector2(V(c, 7, section), V(c, 8, section));
                        value.bounds = Rect.MinMaxRect(V(c, 9, section), V(c, 11, section), V(c, 10, section), V(c, 12, section));
                        hasAreaRow = true;
                        break;
                    }
                    case "BattleSmallAreaFloor": referencedAreaIds.Add(I(c, 1, section)); value.floors.Add(new FloorRow { rowId = rowId, localId = I(c, 2, section), collisionType = Collision(c, 3, section), minX = V(c, 4, section), maxX = V(c, 5, section), y = V(c, 6, section), isSafeSpawnFloor = B(c, 7, section), styleId = I(c, 8, section) }); break;
                    case "BattleSmallAreaLadder": referencedAreaIds.Add(I(c, 1, section)); value.ladders.Add(new LadderRow { rowId = rowId, localId = I(c, 2, section), floorId = I(c, 3, section), upperFloorId = I(c, 4, section), x = V(c, 5, section), interactionWidth = V(c, 6, section), styleId = I(c, 7, section) }); break;
                    case "BattleSmallAreaDoorPoint": referencedAreaIds.Add(I(c, 1, section)); value.doors.Add(new DoorRow { rowId = rowId, localId = I(c, 2, section), floorId = I(c, 3, section), x = V(c, 4, section), styleId = I(c, 5, section) }); break;
                    case "BattleSmallAreaEnemySpawnArea": referencedAreaIds.Add(I(c, 1, section)); value.enemyAreas.Add(new EnemyAreaRow { rowId = rowId, localId = I(c, 2, section), floorId = I(c, 3, section), minX = V(c, 4, section), maxX = V(c, 5, section), spawnRuleId = I(c, 6, section) }); break;
                    case "BattleSmallAreaLootSpawnPoint": referencedAreaIds.Add(I(c, 1, section)); value.lootPoints.Add(new LootRow { rowId = rowId, localId = I(c, 2, section), floorId = I(c, 3, section), x = V(c, 4, section), baseSpawnChance = V(c, 5, section), lootSourceId = I(c, 6, section) }); break;
                    case "BattleSmallAreaBossSpawnPoint": referencedAreaIds.Add(I(c, 1, section)); value.bossPoints.Add(new BossRow { rowId = rowId, localId = I(c, 2, section), floorId = I(c, 3, section), x = V(c, 4, section) }); break;
                    case "BattleSmallAreaExtractionSpawnPoint": referencedAreaIds.Add(I(c, 1, section)); value.extractionPoints.Add(new ExtractionRow { rowId = rowId, localId = I(c, 2, section), floorId = I(c, 3, section), x = V(c, 4, section) }); break;
                }
            }
            if (!hasAreaRow) throw new InvalidDataException("缺少 BattleSmallArea 主行。");
            string[] missing = required.Where(name => !seen.Contains(name)).ToArray();
            if (missing.Length > 0) throw new InvalidDataException("缺少源表段：" + string.Join("、", missing));
            for (int i = 0; i < referencedAreaIds.Count; i++)
                if (referencedAreaIds[i] != value.id)
                    throw new InvalidDataException($"子表 smallAreaId={referencedAreaIds[i]} 与主表 id={value.id} 不一致。");
            return value;
        }

        private static string C(string[] c, int index) => index < c.Length ? c[index].Trim() : string.Empty;
        private static string Required(string[] c, int index, string section)
        {
            string value = C(c, index);
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{section} 第 {index + 1} 列为空。");
            return value;
        }
        private static int I(string[] c, int index, string section)
        {
            if (!int.TryParse(Required(c, index, section), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new InvalidDataException($"{section} 第 {index + 1} 列不是整数。");
            return value;
        }
        private static float V(string[] c, int index, string section)
        {
            if (!float.TryParse(Required(c, index, section), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || !IsFinite(value))
                throw new InvalidDataException($"{section} 第 {index + 1} 列不是有限数。");
            return value;
        }
        private static bool B(string[] c, int index, string section)
        {
            if (!bool.TryParse(Required(c, index, section), out bool value))
                throw new InvalidDataException($"{section} 第 {index + 1} 列不是 bool。");
            return value;
        }
        private static int Collision(string[] c, int index, string section)
        {
            string value = Required(c, index, section);
            if (value == "SolidGround") return 0;
            if (value == "OneWayPlatform") return 1;
            throw new InvalidDataException($"{section} collisionType 非法：{value}。");
        }
        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
#endif
