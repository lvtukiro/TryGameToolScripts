using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FlatBuffers;
using RefData;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TryGame.HomeDebugTools.Editor
{
    /// <summary>
    /// SceneView HomeArea 覆盖范围编辑工具。
    /// 用于在场景 / prefab 视图里对照配表范围调整场景资源，并把 HomeArea 中心和尺寸写回导出配置。
    /// </summary>
    public sealed class TryGameHomeAreaBoundsEditorWindow : EditorWindow
    {
        private const string HomeAreaTxtAssetPath = "Assets/TryGameRefdataRes/v2/Output/txt_data/HomeArea.txt";
        private const string HomeAreaClientJsonAssetPath = "Assets/TryGameRefdataRes/v2/Output/Json/client/HomeArea.json";
        private const string HomeAreaServerJsonAssetPath = "Assets/TryGameRefdataRes/v2/Output/Json/server/HomeAreaRef.json";
        private const string HomeAreaBytesAssetPath = "Assets/TryGameRefdataRes/v2/Output/fb_data/HomeArea.bytes";

        private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
        private static readonly Regex DataLineRegex = new Regex(@"^\s*\d+\s*\t", RegexOptions.Compiled);
        private static readonly Regex JsonIdRegex = new Regex(@"""id""\s*:\s*(?<id>\d+)", RegexOptions.Compiled);

        private readonly List<HomeAreaRow> rows = new List<HomeAreaRow>();
        private readonly List<string> rawLines = new List<string>();
        private readonly Dictionary<int, HomeAreaRow> rowById = new Dictionary<int, HomeAreaRow>();

        private Vector2 scrollPosition;
        private int selectedAreaId;
        private bool showAllAreas = true;
        private bool showGridLines = true;
        private bool dirty;
        private int idColumn = -1;
        private int worldIdColumn = -1;
        private int nameKeyColumn = -1;
        private int gridWidthColumn = -1;
        private int gridHeightColumn = -1;
        private int cellSizeColumn = -1;
        private int originXColumn = -1;
        private int originYColumn = -1;

        [MenuItem("TryGame/Home/HomeArea 覆盖范围编辑器")]
        public static void Open()
        {
            TryGameHomeAreaBoundsEditorWindow window = GetWindow<TryGameHomeAreaBoundsEditorWindow>("HomeArea 覆盖范围");
            window.minSize = new Vector2(460f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadTable();
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawSelectedAreaEditor();
            DrawTablePreview();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("重新读取", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            {
                LoadTable();
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(rows.Count == 0 || !dirty))
            {
                if (GUILayout.Button("保存 HomeArea 配置", EditorStyles.toolbarButton, GUILayout.Width(132f)))
                {
                    SaveConfig();
                }
            }

            if (GUILayout.Button("保存当前场景/Prefab", EditorStyles.toolbarButton, GUILayout.Width(132f)))
            {
                SaveOpenScenesAndAssets();
            }

            GUILayout.FlexibleSpace();
            showAllAreas = GUILayout.Toggle(showAllAreas, "显示全部", EditorStyles.toolbarButton, GUILayout.Width(72f));
            showGridLines = GUILayout.Toggle(showGridLines, "显示格子", EditorStyles.toolbarButton, GUILayout.Width(72f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "SceneView 中黄色矩形是当前 HomeArea 覆盖范围。拖中心可移动 originX/originY，拖四边会按 cellSize 更新 gridWidth/gridHeight。场景 prefab 本身仍用 Unity 的 Transform 工具调整。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "保存 HomeArea 配置会写入 Output 下的 txt / bytes / JSON，游戏读取会生效。后续如果重新从 Excel 导表，仍会以 Excel 为准覆盖这些 Output 文件。",
                MessageType.None);

            if (dirty)
            {
                EditorGUILayout.HelpBox("HomeArea 配置有未保存修改。保存后会同步 txt、bytes、client/server JSON。", MessageType.Warning);
            }
        }

        private void DrawSelectedAreaEditor()
        {
            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox("没有读取到 HomeArea 数据。", MessageType.Warning);
                return;
            }

            int[] ids = new int[rows.Count];
            string[] labels = new string[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                HomeAreaRow row = rows[i];
                ids[i] = row.Id;
                labels[i] = $"{row.Id}  {row.NameKey}";
            }

            if (!rowById.ContainsKey(selectedAreaId))
            {
                selectedAreaId = rows[0].Id;
            }

            EditorGUI.BeginChangeCheck();
            selectedAreaId = EditorGUILayout.IntPopup("当前 HomeArea", selectedAreaId, labels, ids);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            HomeAreaRow selected = GetSelectedRow();
            if (selected == null)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            Vector2 center = EditorGUILayout.Vector2Field("区域中心 origin", selected.Center);
            float cellSize = Mathf.Max(0.01f, EditorGUILayout.FloatField("单格世界尺寸", selected.CellSize));
            int gridWidth = Mathf.Max(1, EditorGUILayout.IntField("格子宽度", selected.GridWidth));
            int gridHeight = Mathf.Max(2, EditorGUILayout.IntField("格子高度", selected.GridHeight));
            if (EditorGUI.EndChangeCheck())
            {
                selected.Center = center;
                selected.CellSize = cellSize;
                selected.GridWidth = gridWidth;
                selected.GridHeight = gridHeight;
                MarkDirty();
                SceneView.RepaintAll();
            }

            Rect worldRect = selected.WorldRect;
            EditorGUILayout.LabelField("世界覆盖范围", FormatRect(worldRect));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("用选中物体 Bounds 匹配"))
            {
                ApplySelectionBounds(selected);
            }

            if (GUILayout.Button("镜头定位到范围"))
            {
                FrameSelectedArea(selected);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTablePreview()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("HomeArea 列表", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < rows.Count; i++)
            {
                HomeAreaRow row = rows[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                if (GUILayout.Toggle(row.Id == selectedAreaId, string.Empty, GUILayout.Width(18f)))
                {
                    selectedAreaId = row.Id;
                    SceneView.RepaintAll();
                }

                EditorGUILayout.LabelField(row.Id.ToString(), GUILayout.Width(56f));
                EditorGUILayout.LabelField(row.NameKey, GUILayout.MinWidth(120f));
                EditorGUILayout.LabelField($"center=({FormatFloat(row.OriginX)}, {FormatFloat(row.OriginY)})", GUILayout.Width(150f));
                EditorGUILayout.LabelField($"size={FormatFloat(row.WorldWidth)} x {FormatFloat(row.WorldHeight)}", GUILayout.Width(120f));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (rows.Count == 0)
            {
                return;
            }

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            for (int i = 0; i < rows.Count; i++)
            {
                HomeAreaRow row = rows[i];
                bool selected = row.Id == selectedAreaId;
                if (!showAllAreas && !selected)
                {
                    continue;
                }

                DrawArea(row, selected);
            }
        }

        private void DrawArea(HomeAreaRow row, bool selected)
        {
            Rect rect = row.WorldRect;
            Color lineColor = selected ? new Color(1f, 0.86f, 0.15f, 1f) : new Color(0.2f, 0.75f, 1f, 0.75f);
            Color fillColor = selected ? new Color(1f, 0.86f, 0.15f, 0.12f) : new Color(0.2f, 0.75f, 1f, 0.07f);

            Vector3 p0 = new Vector3(rect.xMin, rect.yMin, 0f);
            Vector3 p1 = new Vector3(rect.xMax, rect.yMin, 0f);
            Vector3 p2 = new Vector3(rect.xMax, rect.yMax, 0f);
            Vector3 p3 = new Vector3(rect.xMin, rect.yMax, 0f);

            Handles.DrawSolidRectangleWithOutline(new[] { p0, p1, p2, p3 }, fillColor, lineColor);
            if (showGridLines && selected)
            {
                DrawGridLines(row, rect);
            }

            Handles.Label(
                new Vector3(rect.center.x, rect.yMax, 0f),
                $"HomeArea {row.Id}\n{FormatFloat(row.WorldWidth)} x {FormatFloat(row.WorldHeight)}",
                EditorStyles.boldLabel);

            if (selected)
            {
                DrawEditableHandles(row, rect);
            }
        }

        private void DrawGridLines(HomeAreaRow row, Rect rect)
        {
            int maxLines = 120;
            if (row.GridWidth + row.GridHeight > maxLines)
            {
                return;
            }

            Color oldColor = Handles.color;
            Handles.color = new Color(1f, 1f, 1f, 0.16f);
            for (int x = 1; x < row.GridWidth; x++)
            {
                float px = rect.xMin + x * row.CellSize;
                Handles.DrawLine(new Vector3(px, rect.yMin, 0f), new Vector3(px, rect.yMax, 0f));
            }

            for (int y = 1; y < row.GridHeight; y++)
            {
                float py = rect.yMin + y * row.CellSize;
                Handles.DrawLine(new Vector3(rect.xMin, py, 0f), new Vector3(rect.xMax, py, 0f));
            }

            Handles.color = oldColor;
        }

        private void DrawEditableHandles(HomeAreaRow row, Rect rect)
        {
            Vector3 center = new Vector3(rect.center.x, rect.center.y, 0f);
            float handleSize = HandleUtility.GetHandleSize(center) * 0.08f;

            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                row.Center = new Vector2(newCenter.x, newCenter.y);
                MarkDirty();
                return;
            }

            DrawEdgeHandle(row, rect, Edge.Left, handleSize);
            DrawEdgeHandle(row, rect, Edge.Right, handleSize);
            DrawEdgeHandle(row, rect, Edge.Bottom, handleSize);
            DrawEdgeHandle(row, rect, Edge.Top, handleSize);
        }

        private void DrawEdgeHandle(HomeAreaRow row, Rect rect, Edge edge, float handleSize)
        {
            Vector3 position;
            Vector3 direction;
            switch (edge)
            {
                case Edge.Left:
                    position = new Vector3(rect.xMin, rect.center.y, 0f);
                    direction = Vector3.right;
                    break;
                case Edge.Right:
                    position = new Vector3(rect.xMax, rect.center.y, 0f);
                    direction = Vector3.right;
                    break;
                case Edge.Bottom:
                    position = new Vector3(rect.center.x, rect.yMin, 0f);
                    direction = Vector3.up;
                    break;
                default:
                    position = new Vector3(rect.center.x, rect.yMax, 0f);
                    direction = Vector3.up;
                    break;
            }

            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.Slider(position, direction, handleSize, Handles.CubeHandleCap, 0f);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            ApplyEdgeDrag(row, rect, edge, newPosition);
            MarkDirty();
        }

        private static void ApplyEdgeDrag(HomeAreaRow row, Rect rect, Edge edge, Vector3 newPosition)
        {
            float minSize = Mathf.Max(0.01f, row.CellSize);
            float left = rect.xMin;
            float right = rect.xMax;
            float bottom = rect.yMin;
            float top = rect.yMax;

            switch (edge)
            {
                case Edge.Left:
                    left = Mathf.Min(newPosition.x, right - minSize);
                    break;
                case Edge.Right:
                    right = Mathf.Max(newPosition.x, left + minSize);
                    break;
                case Edge.Bottom:
                    bottom = Mathf.Min(newPosition.y, top - minSize);
                    break;
                case Edge.Top:
                    top = Mathf.Max(newPosition.y, bottom + minSize);
                    break;
            }

            Vector2 center = new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
            Vector2 size = new Vector2(right - left, top - bottom);
            row.Center = center;
            row.GridWidth = Mathf.Max(1, Mathf.RoundToInt(size.x / row.CellSize));
            row.GridHeight = Mathf.Max(2, Mathf.RoundToInt(size.y / row.CellSize));
        }

        private void ApplySelectionBounds(HomeAreaRow row)
        {
            if (!TryGetSelectionBounds(out Bounds bounds))
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] 当前选中物体没有 Renderer 或 Collider Bounds，无法匹配 HomeArea。");
                return;
            }

            row.Center = new Vector2(bounds.center.x, bounds.center.y);
            row.GridWidth = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / row.CellSize));
            row.GridHeight = Mathf.Max(2, Mathf.CeilToInt(bounds.size.y / row.CellSize));
            MarkDirty();
            SceneView.RepaintAll();
        }

        private static bool TryGetSelectionBounds(out Bounds bounds)
        {
            bounds = default(Bounds);
            bool hasBounds = false;
            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++)
            {
                Renderer[] renderers = selection[i].GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    EncapsulateBounds(renderers[j].bounds, ref bounds, ref hasBounds);
                }

                Collider[] colliders = selection[i].GetComponentsInChildren<Collider>(true);
                for (int j = 0; j < colliders.Length; j++)
                {
                    EncapsulateBounds(colliders[j].bounds, ref bounds, ref hasBounds);
                }
            }

            return hasBounds;
        }

        private static void EncapsulateBounds(Bounds source, ref Bounds bounds, ref bool hasBounds)
        {
            if (!hasBounds)
            {
                bounds = source;
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(source);
        }

        private void FrameSelectedArea(HomeAreaRow row)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }

            Rect rect = row.WorldRect;
            Bounds bounds = new Bounds(new Vector3(rect.center.x, rect.center.y, 0f), new Vector3(rect.width, rect.height, 1f));
            sceneView.Frame(bounds, false);
        }

        private void LoadTable()
        {
            rows.Clear();
            rawLines.Clear();
            rowById.Clear();
            dirty = false;

            string path = ToFullPath(HomeAreaTxtAssetPath);
            if (!File.Exists(path))
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] HomeArea.txt 不存在：" + path);
                return;
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            rawLines.AddRange(lines);
            if (lines.Length == 0)
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] HomeArea.txt 为空。");
                return;
            }

            string[] headers = SplitLine(lines[0]);
            idColumn = FindColumn(headers, "id");
            worldIdColumn = FindColumn(headers, "worldId");
            nameKeyColumn = FindColumn(headers, "nameKey");
            gridWidthColumn = FindColumn(headers, "gridWidth");
            gridHeightColumn = FindColumn(headers, "gridHeight");
            cellSizeColumn = FindColumn(headers, "cellSize");
            originXColumn = FindColumn(headers, "originX");
            originYColumn = FindColumn(headers, "originY");
            if (!ValidateColumns())
            {
                return;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                if (!DataLineRegex.IsMatch(lines[i]))
                {
                    continue;
                }

                string[] columns = SplitLine(lines[i]);
                HomeAreaRow row;
                if (!TryParseRow(i, columns, out row))
                {
                    continue;
                }

                rows.Add(row);
                rowById[row.Id] = row;
            }

            rows.Sort((a, b) => a.Id.CompareTo(b.Id));
            if (rows.Count > 0 && !rowById.ContainsKey(selectedAreaId))
            {
                selectedAreaId = rows[0].Id;
            }
        }

        private bool ValidateColumns()
        {
            bool valid = idColumn >= 0
                && worldIdColumn >= 0
                && nameKeyColumn >= 0
                && gridWidthColumn >= 0
                && gridHeightColumn >= 0
                && cellSizeColumn >= 0
                && originXColumn >= 0
                && originYColumn >= 0;

            if (!valid)
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] HomeArea.txt 缺少必要列：id/worldId/nameKey/gridWidth/gridHeight/cellSize/originX/originY。");
            }

            return valid;
        }

        private bool TryParseRow(int lineIndex, string[] columns, out HomeAreaRow row)
        {
            row = null;
            if (!TryParseInt(columns, idColumn, out int id)
                || !TryParseInt(columns, worldIdColumn, out int worldId)
                || !TryParseInt(columns, gridWidthColumn, out int gridWidth)
                || !TryParseInt(columns, gridHeightColumn, out int gridHeight)
                || !TryParseFloat(columns, cellSizeColumn, out float cellSize)
                || !TryParseFloat(columns, originXColumn, out float originX)
                || !TryParseFloat(columns, originYColumn, out float originY))
            {
                Debug.LogError($"[TryGameHomeAreaBoundsEditorWindow] HomeArea.txt 第 {lineIndex + 1} 行解析失败。");
                return false;
            }

            row = new HomeAreaRow
            {
                LineIndex = lineIndex,
                Columns = columns,
                Id = id,
                WorldId = worldId,
                NameKey = GetColumn(columns, nameKeyColumn),
                GridWidth = Mathf.Max(1, gridWidth),
                GridHeight = Mathf.Max(2, gridHeight),
                CellSize = Mathf.Max(0.01f, cellSize),
                OriginX = originX,
                OriginY = originY,
            };
            return true;
        }

        private void SaveConfig()
        {
            if (rows.Count == 0)
            {
                return;
            }

            WriteHomeAreaTxt();
            WriteHomeAreaBytes();
            WriteClientJson();
            WriteServerJson();
            AssetDatabase.Refresh();
            dirty = false;
            Debug.Log("[TryGameHomeAreaBoundsEditorWindow] HomeArea 配置已保存：txt / bytes / JSON。");
        }

        private void WriteHomeAreaTxt()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                HomeAreaRow row = rows[i];
                SetColumn(row.Columns, gridWidthColumn, row.GridWidth.ToString(InvariantCulture));
                SetColumn(row.Columns, gridHeightColumn, row.GridHeight.ToString(InvariantCulture));
                SetColumn(row.Columns, cellSizeColumn, FormatFloat(row.CellSize));
                SetColumn(row.Columns, originXColumn, FormatFloat(row.OriginX));
                SetColumn(row.Columns, originYColumn, FormatFloat(row.OriginY));
                rawLines[row.LineIndex] = string.Join("\t", row.Columns);
            }

            File.WriteAllLines(ToFullPath(HomeAreaTxtAssetPath), rawLines.ToArray(), new UTF8Encoding(false));
        }

        private void WriteHomeAreaBytes()
        {
            List<HomeAreaRow> sortedRows = GetRowsSortedById();
            FlatBufferBuilder builder = new FlatBufferBuilder(1024);
            Offset<HomeArea>[] offsets = new Offset<HomeArea>[sortedRows.Count];
            for (int i = 0; i < sortedRows.Count; i++)
            {
                HomeAreaRow row = sortedRows[i];
                StringOffset nameKeyOffset = builder.CreateString(row.NameKey ?? string.Empty);
                offsets[i] = HomeArea.CreateHomeArea(
                    builder,
                    row.Id,
                    row.WorldId,
                    nameKeyOffset,
                    row.GridWidth,
                    row.GridHeight,
                    row.CellSize,
                    row.OriginX,
                    row.OriginY);
            }

            VectorOffset vectorOffset = HomeArea.CreateSortedVectorOfHomeArea(builder, offsets);
            Offset<HomeAreaRefData> rootOffset = HomeAreaRefData.CreateHomeAreaRefData(builder, vectorOffset);
            HomeAreaRefData.FinishHomeAreaRefDataBuffer(builder, rootOffset);
            File.WriteAllBytes(ToFullPath(HomeAreaBytesAssetPath), builder.SizedByteArray());
        }

        private void WriteClientJson()
        {
            List<HomeAreaRow> sortedRows = GetRowsSortedById();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"HomeAreas\": [");
            AppendJsonRows(sb, sortedRows, 4);
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(ToFullPath(HomeAreaClientJsonAssetPath), sb.ToString(), new UTF8Encoding(false));
        }

        private void WriteServerJson()
        {
            HashSet<int> serverIds = ReadServerJsonIds();
            List<HomeAreaRow> sortedRows = new List<HomeAreaRow>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (serverIds.Count == 0 || serverIds.Contains(rows[i].Id))
                {
                    sortedRows.Add(rows[i]);
                }
            }

            sortedRows.Sort((a, b) => a.Id.CompareTo(b.Id));
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[");
            AppendJsonRows(sb, sortedRows, 2);
            sb.AppendLine("]");
            File.WriteAllText(ToFullPath(HomeAreaServerJsonAssetPath), sb.ToString(), new UTF8Encoding(false));
        }

        private static void AppendJsonRows(StringBuilder sb, List<HomeAreaRow> jsonRows, int indent)
        {
            string pad = new string(' ', indent);
            string fieldPad = new string(' ', indent + 2);
            for (int i = 0; i < jsonRows.Count; i++)
            {
                HomeAreaRow row = jsonRows[i];
                sb.AppendLine(pad + "{");
                sb.AppendLine(fieldPad + "\"id\": " + row.Id + ",");
                sb.AppendLine(fieldPad + "\"worldId\": " + row.WorldId + ",");
                sb.AppendLine(fieldPad + "\"nameKey\": \"" + EscapeJson(row.NameKey) + "\",");
                sb.AppendLine(fieldPad + "\"gridWidth\": " + row.GridWidth + ",");
                sb.AppendLine(fieldPad + "\"gridHeight\": " + row.GridHeight + ",");
                sb.AppendLine(fieldPad + "\"cellSize\": " + FormatFloat(row.CellSize) + ",");
                sb.AppendLine(fieldPad + "\"originX\": " + FormatFloat(row.OriginX) + ",");
                sb.AppendLine(fieldPad + "\"originY\": " + FormatFloat(row.OriginY));
                sb.Append(pad + "}");
                sb.AppendLine(i < jsonRows.Count - 1 ? "," : string.Empty);
            }
        }

        private HashSet<int> ReadServerJsonIds()
        {
            HashSet<int> ids = new HashSet<int>();
            string path = ToFullPath(HomeAreaServerJsonAssetPath);
            if (!File.Exists(path))
            {
                return ids;
            }

            string text = File.ReadAllText(path, Encoding.UTF8);
            MatchCollection matches = JsonIdRegex.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                if (int.TryParse(matches[i].Groups["id"].Value, NumberStyles.Integer, InvariantCulture, out int id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private List<HomeAreaRow> GetRowsSortedById()
        {
            List<HomeAreaRow> sortedRows = new List<HomeAreaRow>(rows);
            sortedRows.Sort((a, b) => a.Id.CompareTo(b.Id));
            return sortedRows;
        }

        private void SaveOpenScenesAndAssets()
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[TryGameHomeAreaBoundsEditorWindow] 已保存当前打开的场景/Prefab 和资源。");
        }

        private HomeAreaRow GetSelectedRow()
        {
            HomeAreaRow row;
            return rowById.TryGetValue(selectedAreaId, out row) ? row : null;
        }

        private void MarkDirty()
        {
            dirty = true;
            Repaint();
        }

        private static string[] SplitLine(string line)
        {
            return (line ?? string.Empty).Split('\t');
        }

        private static int FindColumn(string[] headers, string name)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                if (string.Equals(headers[i], name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryParseInt(string[] columns, int index, out int value)
        {
            return int.TryParse(GetColumn(columns, index), NumberStyles.Integer, InvariantCulture, out value);
        }

        private static bool TryParseFloat(string[] columns, int index, out float value)
        {
            return float.TryParse(GetColumn(columns, index), NumberStyles.Float, InvariantCulture, out value);
        }

        private static string GetColumn(string[] columns, int index)
        {
            return columns != null && index >= 0 && index < columns.Length ? columns[index] : string.Empty;
        }

        private static void SetColumn(string[] columns, int index, string value)
        {
            if (columns == null || index < 0 || index >= columns.Length)
            {
                return;
            }

            columns[index] = value;
        }

        private static string FormatRect(Rect rect)
        {
            return $"center=({FormatFloat(rect.center.x)}, {FormatFloat(rect.center.y)}), size={FormatFloat(rect.width)} x {FormatFloat(rect.height)}";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", InvariantCulture);
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath)).Replace("\\", "/");
        }

        private enum Edge
        {
            Left,
            Right,
            Bottom,
            Top,
        }

        private sealed class HomeAreaRow
        {
            public int LineIndex;
            public string[] Columns;
            public int Id;
            public int WorldId;
            public string NameKey;
            public int GridWidth;
            public int GridHeight;
            public float CellSize;
            public float OriginX;
            public float OriginY;

            public Vector2 Center
            {
                get { return new Vector2(OriginX, OriginY); }
                set
                {
                    OriginX = value.x;
                    OriginY = value.y;
                }
            }

            public float WorldWidth => GridWidth * CellSize;
            public float WorldHeight => GridHeight * CellSize;

            public Rect WorldRect
            {
                get
                {
                    float width = WorldWidth;
                    float height = WorldHeight;
                    return new Rect(OriginX - width * 0.5f, OriginY - height * 0.5f, width, height);
                }
            }
        }
    }
}
