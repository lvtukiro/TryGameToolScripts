#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        private const float InspectorWidth = 390f;
        private Document document = new Document();
        private Vector2 inspectorScroll;
        private Vector2 issueScroll;
        private ToolKind tool;
        private ToolKind selectedKind;
        private int selectedIndex = -1;
        private Vector2 lastMouseWorld;
        private bool dragging;
        private readonly List<string> validationIssues = new List<string>();
        private BattleSmallAreaWorkbookBridge.Snapshot workbookSnapshot;
        private readonly SortedDictionary<int, string> workingTemplateRows =
            new SortedDictionary<int, string>();
        private int workbookTemplateIndex;
        private string workbookStatus = "尚未读取正式源表";
        private bool documentDirty;
        private bool sourceSavedOutputStale;

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
            if (document == null)
            {
                document = new Document();
            }

            ValidateDocument();
        }

        private void OnGUI()
        {
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
            if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(52f)))
            {
                ValidateDocument();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInspector()
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("小区域", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(workbookStatus, MessageType.None);
            document.id = EditorGUILayout.IntField("smallAreaId", document.id);
            document.codeName = EditorGUILayout.TextField("codeName", document.codeName);
            document.nameLanguageKey = EditorGUILayout.TextField(
                "nameLanguageKey",
                document.nameLanguageKey);
            document.usageType = EditorGUILayout.Popup("usageType", document.usageType, new[] { "Normal", "Boss" });
            document.backgroundResourceId = EditorGUILayout.IntField("背景 ResourceId", document.backgroundResourceId);
            document.backgroundOffset = EditorGUILayout.Vector2Field("背景偏移", document.backgroundOffset);
            document.backgroundScale = EditorGUILayout.Vector2Field("背景缩放", document.backgroundScale);
            document.bounds = EditorGUILayout.RectField("编辑/镜头边界", document.bounds);

            DrawFloorList();
            DrawLadderList();
            DrawDoorList();
            DrawEnemyAreaList();
            DrawPointList("物资点", ToolKind.Loot, document.lootPoints);
            DrawPointList("Boss 点", ToolKind.Boss, document.bossPoints);
            DrawPointList("撤离候选点", ToolKind.Extraction, document.extractionPoints);

            if (EditorGUI.EndChangeCheck())
            {
                documentDirty = true;
                ValidateDocument();
                Repaint();
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

        private void DrawFloorList()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Floor ({document.floors.Count})", EditorStyles.boldLabel);
            for (int index = 0; index < document.floors.Count; index++)
            {
                FloorRow row = document.floors[index];
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
            if (GUILayout.Button("+ Floor")) AddAtCenter(ToolKind.Floor);
        }

        private void DrawLadderList()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Ladder ({document.ladders.Count})", EditorStyles.boldLabel);
            for (int index = 0; index < document.ladders.Count; index++)
            {
                LadderRow row = document.ladders[index];
                EditorGUILayout.BeginVertical("box");
                DrawRowHeader(ToolKind.Ladder, index, row, "Ladder");
                row.floorId = EditorGUILayout.IntField("lowerFloorId", row.floorId);
                row.upperFloorId = EditorGUILayout.IntField("upperFloorId", row.upperFloorId);
                row.x = EditorGUILayout.FloatField("x", row.x);
                row.interactionWidth = EditorGUILayout.FloatField("interactionWidth", row.interactionWidth);
                row.styleId = EditorGUILayout.IntField("styleId", row.styleId);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ Ladder")) AddAtCenter(ToolKind.Ladder);
        }

        private void DrawDoorList()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Door ({document.doors.Count})", EditorStyles.boldLabel);
            for (int index = 0; index < document.doors.Count; index++)
            {
                DoorRow row = document.doors[index];
                EditorGUILayout.BeginVertical("box");
                DrawRowHeader(ToolKind.Door, index, row, "Door");
                row.floorId = EditorGUILayout.IntField("floorId", row.floorId);
                row.x = EditorGUILayout.FloatField("x", row.x);
                row.styleId = EditorGUILayout.IntField("styleId", row.styleId);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ Door")) AddAtCenter(ToolKind.Door);
        }

        private void DrawEnemyAreaList()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Enemy 区 ({document.enemyAreas.Count})", EditorStyles.boldLabel);
            for (int index = 0; index < document.enemyAreas.Count; index++)
            {
                EnemyAreaRow row = document.enemyAreas[index];
                EditorGUILayout.BeginVertical("box");
                DrawRowHeader(ToolKind.EnemyArea, index, row, "EnemyArea");
                row.floorId = EditorGUILayout.IntField("floorId", row.floorId);
                row.minX = EditorGUILayout.FloatField("minX", row.minX);
                row.maxX = EditorGUILayout.FloatField("maxX", row.maxX);
                row.spawnRuleId = EditorGUILayout.IntField("spawnRuleId", row.spawnRuleId);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("+ EnemyArea")) AddAtCenter(ToolKind.EnemyArea);
        }

        private void DrawPointList<T>(string title, ToolKind kind, List<T> rows)
            where T : LocalRow
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"{title} ({rows.Count})", EditorStyles.boldLabel);
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
            if (GUILayout.Button($"+ {title}")) AddAtCenter(kind);
        }

        private void DrawRowHeader(ToolKind kind, int index, LocalRow row, string title)
        {
            EditorGUILayout.BeginHorizontal();
            bool selected = selectedKind == kind && selectedIndex == index;
            if (GUILayout.Toggle(selected, $"{title} {row.localId}", "Button"))
            {
                selectedKind = kind;
                selectedIndex = index;
            }
            if (GUILayout.Button("×", GUILayout.Width(26f)))
            {
                RemoveRow(kind, index);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            row.rowId = EditorGUILayout.IntField("rowId", row.rowId);
            row.localId = EditorGUILayout.IntField("localId", row.localId);
        }

        private void DrawCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.035f, 0.055f, 0.08f, 1f));
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect inner = new Rect(rect.x + 18f, rect.y + 34f, rect.width - 36f, rect.height - 52f);
            DrawGrid(inner);
            DrawGeometry(inner);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, 20f),
                $"{document.codeName} ({document.id})  |  {tool}：选择模式拖动物体；其它模式点击空白新增",
                EditorStyles.whiteLabel);
            HandleCanvasInput(inner);
        }

        private void DrawGrid(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.06f, 0.085f, 0.115f, 1f));
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
            for (int i = 0; i < document.floors.Count; i++)
            {
                FloorRow row = document.floors[i];
                float colliderThickness =
                    BattleWorldZoneRuntimeTuning.FloorColliderThickness;
                DrawWorldRect(
                    rect,
                    row.minX,
                    row.maxX,
                    row.y - colliderThickness,
                    row.y,
                    row.isSafeSpawnFloor ? new Color(0.16f, 0.78f, 0.42f, 0.9f) : new Color(0.27f, 0.55f, 0.78f, 0.9f),
                    ToolKind.Floor, i, $"F{row.localId}");
            }
            for (int i = 0; i < document.enemyAreas.Count; i++)
            {
                EnemyAreaRow row = document.enemyAreas[i];
                float y = FloorY(row.floorId);
                DrawWorldRect(rect, row.minX, row.maxX, y + 0.12f, y + 0.72f,
                    new Color(0.78f, 0.24f, 0.24f, 0.42f), ToolKind.EnemyArea, i, $"E{row.localId}");
            }
            for (int i = 0; i < document.ladders.Count; i++)
            {
                LadderRow row = document.ladders[i];
                float y1 = FloorY(row.floorId);
                float y2 = FloorY(row.upperFloorId);
                DrawWorldRect(rect, row.x - row.interactionWidth * 0.5f, row.x + row.interactionWidth * 0.5f,
                    Mathf.Min(y1, y2), Mathf.Max(y1, y2), new Color(0.92f, 0.72f, 0.18f, 0.72f),
                    ToolKind.Ladder, i, $"L{row.localId}");
            }
            DrawPointGeometry(rect, ToolKind.Door, document.doors, new Color(0.24f, 0.9f, 0.95f, 0.95f), "D");
            DrawPointGeometry(rect, ToolKind.Loot, document.lootPoints, new Color(0.96f, 0.76f, 0.2f, 0.95f), "箱");
            DrawPointGeometry(rect, ToolKind.Boss, document.bossPoints, new Color(0.92f, 0.2f, 0.72f, 0.95f), "B");
            DrawPointGeometry(rect, ToolKind.Extraction, document.extractionPoints, new Color(0.2f, 0.92f, 0.48f, 0.95f), "撤");
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
                    if (TryHit(world, out ToolKind hitKind, out int hitIndex))
                    {
                        selectedKind = hitKind;
                        selectedIndex = hitIndex;
                        dragging = true;
                        lastMouseWorld = world;
                    }
                }
                else
                {
                    AddAt(tool, world);
                    tool = ToolKind.Select;
                }
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && dragging)
            {
                MoveSelected(world - lastMouseWorld);
                lastMouseWorld = world;
                ValidateDocument();
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && current.button == 0)
            {
                dragging = false;
            }
        }

        private bool TryHit(Vector2 world, out ToolKind kind, out int index)
        {
            for (int i = document.doors.Count - 1; i >= 0; i--)
                if (HitPoint(world, document.doors[i])) { kind = ToolKind.Door; index = i; return true; }
            for (int i = document.lootPoints.Count - 1; i >= 0; i--)
                if (HitPoint(world, document.lootPoints[i])) { kind = ToolKind.Loot; index = i; return true; }
            for (int i = document.bossPoints.Count - 1; i >= 0; i--)
                if (HitPoint(world, document.bossPoints[i])) { kind = ToolKind.Boss; index = i; return true; }
            for (int i = document.extractionPoints.Count - 1; i >= 0; i--)
                if (HitPoint(world, document.extractionPoints[i])) { kind = ToolKind.Extraction; index = i; return true; }
            for (int i = document.ladders.Count - 1; i >= 0; i--)
            {
                LadderRow row = document.ladders[i];
                float y1 = FloorY(row.floorId), y2 = FloorY(row.upperFloorId);
                if (Mathf.Abs(world.x - row.x) <= Mathf.Max(0.35f, row.interactionWidth * 0.5f)
                    && world.y >= Mathf.Min(y1, y2) && world.y <= Mathf.Max(y1, y2))
                { kind = ToolKind.Ladder; index = i; return true; }
            }
            for (int i = document.enemyAreas.Count - 1; i >= 0; i--)
            {
                EnemyAreaRow row = document.enemyAreas[i];
                float y = FloorY(row.floorId);
                if (world.x >= row.minX && world.x <= row.maxX && world.y >= y && world.y <= y + 0.8f)
                { kind = ToolKind.EnemyArea; index = i; return true; }
            }
            for (int i = document.floors.Count - 1; i >= 0; i--)
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

        private void MoveSelected(Vector2 delta)
        {
            documentDirty = true;
            switch (selectedKind)
            {
                case ToolKind.Floor:
                    FloorRow floor = document.floors[selectedIndex]; floor.minX += delta.x; floor.maxX += delta.x; floor.y += delta.y; break;
                case ToolKind.Ladder: document.ladders[selectedIndex].x += delta.x; break;
                case ToolKind.Door: document.doors[selectedIndex].x += delta.x; break;
                case ToolKind.EnemyArea:
                    EnemyAreaRow area = document.enemyAreas[selectedIndex]; area.minX += delta.x; area.maxX += delta.x; break;
                case ToolKind.Loot: document.lootPoints[selectedIndex].x += delta.x; break;
                case ToolKind.Boss: document.bossPoints[selectedIndex].x += delta.x; break;
                case ToolKind.Extraction: document.extractionPoints[selectedIndex].x += delta.x; break;
            }
        }

        private void AddAtCenter(ToolKind kind)
        {
            AddAt(kind, document.bounds.center);
            ValidateDocument();
            Repaint();
        }

        private void AddAt(ToolKind kind, Vector2 world)
        {
            documentDirty = true;
            int floorId = FindNearestFloorId(world.y);
            switch (kind)
            {
                case ToolKind.Floor:
                    document.floors.Add(Initialize(new FloorRow { minX = world.x - 2f, maxX = world.x + 2f, y = world.y }, document.floors)); break;
                case ToolKind.Ladder:
                    document.ladders.Add(Initialize(new LadderRow { x = world.x, floorId = floorId, upperFloorId = FindOtherFloorId(floorId) }, document.ladders)); break;
                case ToolKind.Door:
                    document.doors.Add(Initialize(new DoorRow { x = world.x, floorId = floorId }, document.doors)); break;
                case ToolKind.EnemyArea:
                    document.enemyAreas.Add(Initialize(new EnemyAreaRow { minX = world.x - 1.5f, maxX = world.x + 1.5f, floorId = floorId }, document.enemyAreas)); break;
                case ToolKind.Loot:
                    document.lootPoints.Add(Initialize(new LootRow { x = world.x, floorId = floorId }, document.lootPoints)); break;
                case ToolKind.Boss:
                    document.bossPoints.Add(Initialize(new BossRow { x = world.x, floorId = floorId }, document.bossPoints)); break;
                case ToolKind.Extraction:
                    document.extractionPoints.Add(Initialize(new ExtractionRow { x = world.x, floorId = floorId }, document.extractionPoints)); break;
            }
            selectedKind = kind;
            selectedIndex = GetCount(kind) - 1;
        }

        private T Initialize<T>(T row, List<T> rows) where T : LocalRow
        {
            row.localId = NextLocalId(rows);
            row.rowId = document.id * 10 + row.localId;
            return row;
        }

        private static int NextLocalId<T>(List<T> rows) where T : LocalRow
        {
            int max = 0;
            for (int i = 0; i < rows.Count; i++) max = Mathf.Max(max, rows[i].localId);
            return max + 1;
        }

        private int FindNearestFloorId(float y)
        {
            if (document.floors.Count == 0) return 1;
            FloorRow nearest = document.floors[0];
            float distance = Mathf.Abs(nearest.y - y);
            for (int i = 1; i < document.floors.Count; i++)
            {
                float candidate = Mathf.Abs(document.floors[i].y - y);
                if (candidate < distance) { nearest = document.floors[i]; distance = candidate; }
            }
            return nearest.localId;
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
            documentDirty = true;
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
            ValidateDocument();
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
            validationIssues.Clear();
            if (document == null) { validationIssues.Add("DTO 为空。"); return; }
            if (document.id <= 0) validationIssues.Add("smallAreaId 必须大于 0。");
            if (string.IsNullOrWhiteSpace(document.codeName)) validationIssues.Add("codeName 不能为空。");
            if (document.bounds.width <= 0f || document.bounds.height <= 0f) validationIssues.Add("边界宽高必须大于 0。");
            HashSet<int> floorIds = ValidateRows(document.floors, "Floor");
            bool hasSafe = false;
            for (int i = 0; i < document.floors.Count; i++)
            {
                FloorRow row = document.floors[i];
                if (row.maxX <= row.minX) validationIssues.Add($"Floor {row.localId} 的 maxX 必须大于 minX。");
                hasSafe |= row.isSafeSpawnFloor;
            }
            if (!hasSafe) validationIssues.Add("至少需要一块安全 Floor。");
            ValidateFloorReferences(document.ladders, floorIds, "Ladder", true);
            ValidateFloorReferences(document.doors, floorIds, "Door", false);
            ValidateFloorReferences(document.enemyAreas, floorIds, "EnemyArea", false);
            ValidateFloorReferences(document.lootPoints, floorIds, "Loot", false);
            ValidateFloorReferences(document.bossPoints, floorIds, "Boss", false);
            ValidateFloorReferences(document.extractionPoints, floorIds, "Extraction", false);
            ValidateFloorGeometry();
            ValidateSafeFloorExclusions();
            ValidateInteractionConflicts();
            if (document.doors.Count == 0) validationIssues.Add("至少需要一扇门。");
            if (document.extractionPoints.Count == 0) validationIssues.Add("至少需要一个撤离候选点。");
        }

        private HashSet<int> ValidateRows<T>(List<T> rows, string label) where T : LocalRow
        {
            HashSet<int> ids = new HashSet<int>();
            HashSet<int> rowIds = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                if (row.localId <= 0 || !ids.Add(row.localId)) validationIssues.Add($"{label} localId 非法或重复：{row.localId}。");
                if (row.rowId <= 0 || !rowIds.Add(row.rowId)) validationIssues.Add($"{label} rowId 非法或重复：{row.rowId}。");
            }
            return ids;
        }

        private void ValidateFloorReferences<T>(List<T> rows, HashSet<int> floors, string label, bool validateUpper)
            where T : LocalRow
        {
            ValidateRows(rows, label);
            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                if (!floors.Contains(row.floorId)) validationIssues.Add($"{label} {row.localId} 引用了不存在的 Floor {row.floorId}。");
                if (validateUpper && row is LadderRow ladder && (!floors.Contains(ladder.upperFloorId) || ladder.upperFloorId == ladder.floorId))
                    validationIssues.Add($"Ladder {row.localId} 的 upperFloorId 断链或与 lowerFloorId 相同。");
            }
        }

        private void ValidateFloorGeometry()
        {
            for (int leftIndex = 0; leftIndex < document.floors.Count; leftIndex++)
            {
                FloorRow left = document.floors[leftIndex];
                for (int rightIndex = leftIndex + 1; rightIndex < document.floors.Count; rightIndex++)
                {
                    FloorRow right = document.floors[rightIndex];
                    float overlap = Mathf.Min(left.maxX, right.maxX)
                        - Mathf.Max(left.minX, right.minX);
                    if (Mathf.Abs(left.y - right.y)
                            < BattleWorldZoneRuntimeTuning.FloorColliderThickness
                        && overlap > 0.001f)
                    {
                        validationIssues.Add(
                            $"Floor {left.localId} 与 Floor {right.localId} 在同高度重叠 {overlap:0.###}。" );
                    }
                }
            }

            ValidatePointsInsideFloor(document.doors, "Door", value => value.x);
            ValidatePointsInsideFloor(document.lootPoints, "Loot", value => value.x);
            ValidatePointsInsideFloor(document.bossPoints, "Boss", value => value.x);
            ValidatePointsInsideFloor(document.extractionPoints, "Extraction", value => value.x);
        }

        private void ValidatePointsInsideFloor<T>(
            IReadOnlyList<T> rows,
            string label,
            Func<T, float> getX)
            where T : LocalRow
        {
            for (int index = 0; index < rows.Count; index++)
            {
                T row = rows[index];
                FloorRow floor = FindFloor(row.floorId);
                float x = getX(row);
                if (floor != null && (x < floor.minX || x > floor.maxX))
                {
                    validationIssues.Add(
                        $"{label} {row.localId} 的 x={x:0.###} 不在 Floor {floor.localId} 区间内。" );
                }
            }
        }

        private void ValidateSafeFloorExclusions()
        {
            for (int index = 0; index < document.enemyAreas.Count; index++)
            {
                EnemyAreaRow row = document.enemyAreas[index];
                if (FindFloor(row.floorId)?.isSafeSpawnFloor == true)
                {
                    validationIssues.Add(
                        $"EnemyArea {row.localId} 不得引用安全 Floor {row.floorId}。" );
                }
            }
            for (int index = 0; index < document.bossPoints.Count; index++)
            {
                BossRow row = document.bossPoints[index];
                if (FindFloor(row.floorId)?.isSafeSpawnFloor == true)
                {
                    validationIssues.Add(
                        $"Boss {row.localId} 不得引用安全 Floor {row.floorId}。" );
                }
            }
        }

        private void ValidateInteractionConflicts()
        {
            List<InteractionFootprint> footprints = new List<InteractionFootprint>();
            float doorHalfWidth = BattleWorldZoneRuntimeTuning.DoorTriggerWidth * 0.5f;
            float extractionHalfWidth =
                BattleWorldZoneRuntimeTuning.ExtractionTriggerWidth * 0.5f;
            for (int index = 0; index < document.doors.Count; index++)
            {
                DoorRow row = document.doors[index];
                footprints.Add(new InteractionFootprint(
                    "Door",
                    row.localId,
                    row.floorId,
                    row.x,
                    doorHalfWidth));
            }
            for (int index = 0; index < document.ladders.Count; index++)
            {
                LadderRow row = document.ladders[index];
                float halfWidth = Mathf.Max(0.1f, row.interactionWidth * 0.5f);
                footprints.Add(new InteractionFootprint(
                    "Ladder",
                    row.localId,
                    row.floorId,
                    row.x,
                    halfWidth));
                footprints.Add(new InteractionFootprint(
                    "Ladder",
                    row.localId,
                    row.upperFloorId,
                    row.x,
                    halfWidth));
            }
            for (int index = 0; index < document.bossPoints.Count; index++)
            {
                BossRow row = document.bossPoints[index];
                footprints.Add(new InteractionFootprint(
                    "Boss",
                    row.localId,
                    row.floorId,
                    row.x,
                    0.55f));
            }
            for (int index = 0; index < document.extractionPoints.Count; index++)
            {
                ExtractionRow row = document.extractionPoints[index];
                footprints.Add(new InteractionFootprint(
                    "Extraction",
                    row.localId,
                    row.floorId,
                    row.x,
                    extractionHalfWidth));
            }

            for (int left = 0; left < footprints.Count; left++)
            {
                for (int right = left + 1; right < footprints.Count; right++)
                {
                    InteractionFootprint a = footprints[left];
                    InteractionFootprint b = footprints[right];
                    if (a.FloorId != b.FloorId
                        || (a.Kind == "Ladder"
                            && b.Kind == "Ladder"
                            && a.LocalId == b.LocalId))
                    {
                        continue;
                    }

                    if (Mathf.Abs(a.X - b.X) < a.HalfWidth + b.HalfWidth)
                    {
                        validationIssues.Add(
                            $"交互范围冲突：{a.Kind} {a.LocalId} 与 {b.Kind} {b.LocalId} " +
                            $"位于 Floor {a.FloorId}，间距不足。" );
                    }
                }
            }
        }

        private FloorRow FindFloor(int floorId)
        {
            for (int index = 0; index < document.floors.Count; index++)
            {
                if (document.floors[index].localId == floorId)
                {
                    return document.floors[index];
                }
            }
            return null;
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

        private void ImportDocument()
        {
            string path = EditorUtility.OpenFilePanel("导入 BattleSmallArea DTO 或源行", string.Empty, "json,txt");
            if (!string.IsNullOrWhiteSpace(path)) TryImportText(File.ReadAllText(path, Encoding.UTF8), path);
        }

        private void LoadOfficialWorkbook()
        {
            if (documentDirty
                && !EditorUtility.DisplayDialog(
                    "重新读取正式源表",
                    "当前画布有尚未写回的修改。重新读取会丢弃这些修改，是否继续？",
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
            sourceSavedOutputStale = false;
            documentDirty = false;
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
                ShowNotification(new GUIContent("当前修改未通过校验，不能切换"));
                return;
            }

            workbookTemplateIndex = Mathf.Clamp(
                workbookTemplateIndex,
                0,
                workbookSnapshot.SmallAreaIds.Length - 1);
            int id = workbookSnapshot.SmallAreaIds[workbookTemplateIndex];
            if (!workbookSnapshot.Templates.TryGetValue(id, out string sourceRows))
            {
                workbookStatus = $"正式源表快照缺少 smallAreaId={id}。";
                return;
            }

            TryImportText(sourceRows, $"正式源表 smallAreaId={id}");
            documentDirty = false;
            workbookStatus =
                $"正在编辑正式模板 {id}；切换模板时会暂存已通过校验的修改。";
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
            documentDirty = false;
            workbookStatus = $"模板 {document.id} 已暂存到当前工作集。";
            return true;
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
                workbookTemplateIndex = Array.IndexOf(
                    refreshed.SmallAreaIds,
                    editedId);
                if (workbookTemplateIndex < 0) workbookTemplateIndex = 0;
                documentDirty = false;
                sourceSavedOutputStale = false;
            }
            else
            {
                workbookSnapshot = null;
                workbookStatus += $"；重新读取校验失败：{reloadError}";
            }
        }

        private void TryImportText(string text, string source)
        {
            try
            {
                Document parsed = !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("{")
                    ? JsonUtility.FromJson<Document>(text)
                    : ParseSourceRows(text);
                if (parsed == null) throw new InvalidDataException("没有解析出 DTO。");
                document = parsed;
                EnsureLists(document);
                selectedKind = ToolKind.Select;
                selectedIndex = -1;
                ValidateDocument();
                Repaint();
                ShowNotification(new GUIContent($"已导入 {source}"));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BattleSmallAreaVisualEditor] 导入失败：source={source}\n{exception}");
                ShowNotification(new GUIContent("导入失败，详见 Console"));
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
            Document value = new Document(); EnsureLists(value);
            string section = string.Empty; string[] lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim(); if (line.Length == 0) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); i++; continue; }
                string[] c = lines[i].Split('\t'); if (c.Length == 0 || !int.TryParse(c[0], out int rowId)) continue;
                switch (section)
                {
                    case "BattleSmallArea": value.id = rowId; value.codeName = C(c, 1); value.nameLanguageKey = C(c, 2); value.usageType = C(c, 3) == "Boss" ? 1 : 0; value.backgroundResourceId = I(c, 4); value.backgroundOffset = new Vector2(V(c, 5), V(c, 6)); value.backgroundScale = new Vector2(V(c, 7), V(c, 8)); value.bounds = Rect.MinMaxRect(V(c, 9), V(c, 11), V(c, 10), V(c, 12)); break;
                    case "BattleSmallAreaFloor": value.floors.Add(new FloorRow { rowId = rowId, localId = I(c, 2), collisionType = C(c, 3) == "OneWayPlatform" ? 1 : 0, minX = V(c, 4), maxX = V(c, 5), y = V(c, 6), isSafeSpawnFloor = B(c, 7), styleId = I(c, 8) }); break;
                    case "BattleSmallAreaLadder": value.ladders.Add(new LadderRow { rowId = rowId, localId = I(c, 2), floorId = I(c, 3), upperFloorId = I(c, 4), x = V(c, 5), interactionWidth = V(c, 6), styleId = I(c, 7) }); break;
                    case "BattleSmallAreaDoorPoint": value.doors.Add(new DoorRow { rowId = rowId, localId = I(c, 2), floorId = I(c, 3), x = V(c, 4), styleId = I(c, 5) }); break;
                    case "BattleSmallAreaEnemySpawnArea": value.enemyAreas.Add(new EnemyAreaRow { rowId = rowId, localId = I(c, 2), floorId = I(c, 3), minX = V(c, 4), maxX = V(c, 5), spawnRuleId = I(c, 6) }); break;
                    case "BattleSmallAreaLootSpawnPoint": value.lootPoints.Add(new LootRow { rowId = rowId, localId = I(c, 2), floorId = I(c, 3), x = V(c, 4), baseSpawnChance = V(c, 5), lootSourceId = I(c, 6) }); break;
                    case "BattleSmallAreaBossSpawnPoint": value.bossPoints.Add(new BossRow { rowId = rowId, localId = I(c, 2), floorId = I(c, 3), x = V(c, 4) }); break;
                    case "BattleSmallAreaExtractionSpawnPoint": value.extractionPoints.Add(new ExtractionRow { rowId = rowId, localId = I(c, 2), floorId = I(c, 3), x = V(c, 4) }); break;
                }
            }
            return value;
        }

        private static string C(string[] c, int index) => index < c.Length ? c[index].Trim() : string.Empty;
        private static int I(string[] c, int index) => int.TryParse(C(c, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        private static float V(string[] c, int index) => float.TryParse(C(c, index), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        private static bool B(string[] c, int index) => bool.TryParse(C(c, index), out bool v) && v;
        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
#endif
