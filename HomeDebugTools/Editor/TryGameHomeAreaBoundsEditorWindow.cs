using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using TryGame.RefDataTools.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TryGame.HomeDebugTools.Editor
{
    /// <summary>
    /// SceneView HomeArea 覆盖范围编辑工具。
    /// 用于在场景或 prefab 视图里对照配表范围调整场景资源，并把 HomeArea 中心和尺寸写回导出配置。
    /// </summary>
    public sealed class TryGameHomeAreaBoundsEditorWindow : EditorWindow
    {
        private static readonly string HomeAreaTxtAssetPath = TryGameRefDataPaths.DefaultOutputAssetPath + "/txt_data/HomeArea.txt";
        private static readonly string HomeAreaExcelAssetPath = TryGameRefDataPaths.DefaultExcelRootAssetPath + "/h.家园1_0A.xlsx";

        private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
        private static readonly Regex DataLineRegex = new Regex(@"^\s*\d+\s*\t", RegexOptions.Compiled);

        private readonly List<HomeAreaRow> rows = new List<HomeAreaRow>();
        private readonly Dictionary<int, HomeAreaRow> rowById = new Dictionary<int, HomeAreaRow>();

        private Vector2 scrollPosition;
        private int selectedAreaId;
        private bool showAllAreas = true;
        private bool showGridLines = true;
        private bool dirty;
        private bool tableLoadSucceeded;
        private bool sourceSavedOutputStale;
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

            using (new EditorGUI.DisabledScope(rows.Count == 0 || !dirty || !tableLoadSucceeded))
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
                "保存 HomeArea 配置会先写入源 Excel 的 HomeArea sheet，校验成功后再走正式导表。现有 cltabtoy 会打开控制台，导出完成后需要在控制台按任意键退出。",
                MessageType.Info);

            if (dirty)
            {
                EditorGUILayout.HelpBox("HomeArea 配置有未保存修改。保存会更新源 Excel，再由正式导表同步 txt、bytes、JSON 和生成代码。", MessageType.Warning);
            }

            if (sourceSavedOutputStale)
            {
                EditorGUILayout.HelpBox("SourceSavedOutputStale：源 Excel 已写入，但正式 Output 未通过导出或逐 ID 校验。请保留当前值并重新执行保存导表。", MessageType.Error);
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

            if (GUILayout.Button("用配表值填入上方字段"))
            {
                ResetCurrentFieldsFromTable(selected);
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
            const int MaxLines = 120;
            if (row.GridWidth + row.GridHeight > MaxLines)
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

        private void ResetCurrentFieldsFromTable(HomeAreaRow row)
        {
            if (row == null)
            {
                return;
            }

            int gridWidth;
            int gridHeight;
            float cellSize;
            float originX;
            float originY;
            if (!TryParseInt(row.Columns, gridWidthColumn, out gridWidth)
                || !TryParseInt(row.Columns, gridHeightColumn, out gridHeight)
                || !TryParseFloat(row.Columns, cellSizeColumn, out cellSize)
                || !TryParseFloat(row.Columns, originXColumn, out originX)
                || !TryParseFloat(row.Columns, originYColumn, out originY))
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] 当前 HomeArea 的配表数值解析失败，无法覆盖当前编辑值。");
                return;
            }

            row.GridWidth = Mathf.Max(1, gridWidth);
            row.GridHeight = Mathf.Max(2, gridHeight);
            row.CellSize = Mathf.Max(0.01f, cellSize);
            row.OriginX = originX;
            row.OriginY = originY;
            RefreshDirtyState();
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
            rowById.Clear();
            dirty = false;
            tableLoadSucceeded = false;

            string path = ToFullPath(HomeAreaTxtAssetPath);
            if (!File.Exists(path))
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] HomeArea.txt 不存在：" + path);
                return;
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
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

            bool rowParseFailed = false;
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
                    rowParseFailed = true;
                    continue;
                }

                if (rowById.ContainsKey(row.Id))
                {
                    Debug.LogError($"[TryGameHomeAreaBoundsEditorWindow] HomeArea.txt 存在重复 id：{row.Id}，已禁止保存。 ");
                    rowParseFailed = true;
                    continue;
                }

                rows.Add(row);
                rowById.Add(row.Id, row);
            }

            rows.Sort((a, b) => a.Id.CompareTo(b.Id));
            if (rows.Count == 0)
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] HomeArea.txt 没有读取到任何有效数据行。");
                return;
            }

            if (rowParseFailed)
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] HomeArea.txt 存在解析失败或重复数据，已禁止写入源 Excel。");
                return;
            }

            tableLoadSucceeded = true;
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

            if (id <= 0 || gridWidth <= 0 || gridHeight <= 1
                || float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0f
                || float.IsNaN(originX) || float.IsInfinity(originX)
                || float.IsNaN(originY) || float.IsInfinity(originY))
            {
                Debug.LogError($"[TryGameHomeAreaBoundsEditorWindow] HomeArea.txt 第 {lineIndex + 1} 行数值非法：id={id}, grid=({gridWidth},{gridHeight}), cellSize={cellSize}, origin=({originX},{originY})");
                return false;
            }

            row = new HomeAreaRow
            {
                Columns = columns,
                Id = id,
                WorldId = worldId,
                NameKey = GetColumn(columns, nameKeyColumn),
                GridWidth = gridWidth,
                GridHeight = gridHeight,
                CellSize = cellSize,
                OriginX = originX,
                OriginY = originY,
            };
            return true;
        }

        private void SaveConfig()
        {
            if (!tableLoadSucceeded)
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] 保存失败，HomeArea.txt 未完整加载。请先修复此前的解析错误并重新读取。");
                return;
            }

            if (rows.Count == 0)
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] 保存失败，没有可写入的 HomeArea 行。");
                return;
            }

            if (!ValidateRowsForSave())
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] 保存失败，待写入的 HomeArea 数值非法。源 Excel 和 Output 均未修改。");
                return;
            }

            if (!EditorUtility.DisplayDialog("写入源 Excel 并导表", "将修改 h.家园1_0A.xlsx 的 HomeArea sheet，然后启动正式导表。\n\n现有 cltabtoy 控制台导出完成后需要按任意键退出。是否继续？", "写入并导表", "取消"))
            {
                Debug.LogWarning("[TryGameHomeAreaBoundsEditorWindow] 用户取消保存，源 Excel 和 Output 均未修改。");
                return;
            }

            List<HomeAreaRow> expectedRows = CloneRows(rows);
            if (!TryWriteHomeAreaExcel(out string excelFullPath))
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] HomeArea 源 Excel 写入或写后校验失败，已停止导表。");
                return;
            }

            if (!TryGameRefDataExportWindow.ExportFiles(
                new List<string> { excelFullPath },
                TryGameRefDataExportMode.Incremental))
            {
                sourceSavedOutputStale = true;
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] SourceSavedOutputStale：源 Excel 已保存，但正式导表失败，Output 仍可能是旧数据。请修复此前错误后重新导出：" + excelFullPath);
                return;
            }

            LoadTable();
            AssetDatabase.Refresh();
            if (!tableLoadSucceeded)
            {
                sourceSavedOutputStale = true;
                RestoreRows(expectedRows);
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] 正式导表已返回成功，但重新读取 HomeArea.txt 失败。请检查导出结果，当前窗口不会报告同步完成。");
                return;
            }

            if (!ValidateExportedRows(expectedRows))
            {
                sourceSavedOutputStale = true;
                RestoreRows(expectedRows);
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] SourceSavedOutputStale：正式导表进程返回成功，但 Output 的五个目标字段未通过逐 ID 对比；窗口已保留待导出的目标值。");
                return;
            }

            dirty = false;
            sourceSavedOutputStale = false;
            Debug.Log("[TryGameHomeAreaBoundsEditorWindow] HomeArea 源 Excel 与正式导出结果已同步完成。");
        }

        private bool ValidateExportedRows(List<HomeAreaRow> expectedRows)
        {
            if (expectedRows == null || expectedRows.Count != rows.Count)
            {
                Debug.LogError($"[TryGameHomeAreaBoundsEditorWindow] Output HomeArea 行数不一致：expected={expectedRows?.Count ?? 0}, actual={rows.Count}");
                return false;
            }

            bool valid = true;
            for (int i = 0; i < expectedRows.Count; i++)
            {
                HomeAreaRow expected = expectedRows[i];
                if (!rowById.TryGetValue(expected.Id, out HomeAreaRow actual))
                {
                    Debug.LogError($"[TryGameHomeAreaBoundsEditorWindow] Output HomeArea 缺少预期 ID：id={expected.Id}");
                    valid = false;
                    continue;
                }

                bool rowValid = actual.GridWidth == expected.GridWidth
                    && actual.GridHeight == expected.GridHeight
                    && Mathf.Approximately(actual.CellSize, expected.CellSize)
                    && Mathf.Approximately(actual.OriginX, expected.OriginX)
                    && Mathf.Approximately(actual.OriginY, expected.OriginY);
                if (!rowValid)
                {
                    Debug.LogError(
                        $"[TryGameHomeAreaBoundsEditorWindow] Output HomeArea 五字段不一致：id={expected.Id}, " +
                        $"expected=({expected.GridWidth},{expected.GridHeight},{expected.CellSize},{expected.OriginX},{expected.OriginY}), " +
                        $"actual=({actual.GridWidth},{actual.GridHeight},{actual.CellSize},{actual.OriginX},{actual.OriginY})");
                    valid = false;
                }
            }

            return valid;
        }

        private static List<HomeAreaRow> CloneRows(List<HomeAreaRow> source)
        {
            List<HomeAreaRow> clones = new List<HomeAreaRow>(source != null ? source.Count : 0);
            if (source == null)
            {
                return clones;
            }

            for (int i = 0; i < source.Count; i++)
            {
                HomeAreaRow row = source[i];
                if (row != null)
                {
                    clones.Add(row.Clone());
                }
            }

            return clones;
        }

        private void RestoreRows(List<HomeAreaRow> expectedRows)
        {
            rows.Clear();
            rowById.Clear();
            for (int i = 0; i < expectedRows.Count; i++)
            {
                HomeAreaRow clone = expectedRows[i].Clone();
                rows.Add(clone);
                rowById.Add(clone.Id, clone);
            }

            tableLoadSucceeded = rows.Count > 0;
            dirty = true;
            Repaint();
            SceneView.RepaintAll();
        }

        private bool ValidateRowsForSave()
        {
            bool valid = true;
            for (int i = 0; i < rows.Count; i++)
            {
                HomeAreaRow row = rows[i];
                bool rowValid = row != null
                    && row.Id > 0
                    && row.GridWidth > 0
                    && row.GridHeight > 1
                    && !float.IsNaN(row.CellSize) && !float.IsInfinity(row.CellSize) && row.CellSize > 0f
                    && !float.IsNaN(row.OriginX) && !float.IsInfinity(row.OriginX)
                    && !float.IsNaN(row.OriginY) && !float.IsInfinity(row.OriginY);
                if (rowValid)
                {
                    continue;
                }

                valid = false;
                Debug.LogError($"[TryGameHomeAreaBoundsEditorWindow] HomeArea 待保存数值非法：id={row?.Id ?? 0}, grid=({row?.GridWidth ?? 0},{row?.GridHeight ?? 0}), cellSize={row?.CellSize ?? 0f}, origin=({row?.OriginX ?? 0f},{row?.OriginY ?? 0f})");
            }

            return valid;
        }

        private bool TryWriteHomeAreaExcel(out string excelFullPath)
        {
            excelFullPath = ToFullPath(HomeAreaExcelAssetPath);
            if (!File.Exists(excelFullPath))
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] HomeArea 源 Excel 不存在：" + excelFullPath);
                return false;
            }

            string backupPath = excelFullPath + ".trygame-backup-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(excelFullPath, backupPath, false);
                using (ZipArchive archive = ZipFile.Open(excelFullPath, ZipArchiveMode.Update))
                {
                    UpdateHomeAreaExcelSheet(archive);
                }

                using (ZipArchive archive = ZipFile.OpenRead(excelFullPath))
                {
                    ValidateHomeAreaExcelSheet(archive);
                }

                TryDeleteExcelBackup(backupPath);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] 写入 HomeArea 源 Excel 失败，开始恢复事务备份：excel=" + excelFullPath + ", backup=" + backupPath + "\n" + exception);
                if (File.Exists(backupPath))
                {
                    try
                    {
                        File.Copy(backupPath, excelFullPath, true);
                        Debug.LogWarning("[TryGameHomeAreaBoundsEditorWindow] HomeArea 源 Excel 写入失败后已恢复旧文件。");
                    }
                    catch (Exception restoreException)
                    {
                        Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] 恢复 HomeArea 源 Excel 失败，请立即从版本控制或备份恢复：excel=" + excelFullPath + ", backup=" + backupPath + "\n" + restoreException);
                    }
                }
                else
                {
                    Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] HomeArea 源 Excel 写入失败，且事务备份不存在：" + backupPath);
                }

                return false;
            }
        }

        private void UpdateHomeAreaExcelSheet(ZipArchive archive)
        {
            LoadHomeAreaSheet(archive, out ZipArchiveEntry sheetEntry, out XmlDocument sheetDocument, out XmlNamespaceManager sheetNs, out List<string> sharedStrings);
            XmlNodeList rowNodes = sheetDocument.SelectNodes("//x:sheetData/x:row", sheetNs);
            XmlNode headerRow = FindHeaderRow(rowNodes, sharedStrings, sheetNs, out Dictionary<string, int> columns);
            RequireExcelColumns(columns);
            HashSet<int> updatedIds = new HashSet<int>();
            for (int i = 0; i < rowNodes.Count; i++)
            {
                XmlNode rowNode = rowNodes[i];
                if (ReferenceEquals(rowNode, headerRow))
                {
                    continue;
                }

                if (!TryParseExcelInt(ReadCellValue(FindCell(rowNode, columns["id"]), sharedStrings, sheetNs), out int id) || !rowById.TryGetValue(id, out HomeAreaRow row))
                {
                    continue;
                }

                SetNumericCell(rowNode, columns["gridWidth"], row.GridWidth.ToString(InvariantCulture), sheetDocument, sheetNs);
                SetNumericCell(rowNode, columns["gridHeight"], row.GridHeight.ToString(InvariantCulture), sheetDocument, sheetNs);
                SetNumericCell(rowNode, columns["cellSize"], FormatFloat(row.CellSize), sheetDocument, sheetNs);
                SetNumericCell(rowNode, columns["originX"], FormatFloat(row.OriginX), sheetDocument, sheetNs);
                SetNumericCell(rowNode, columns["originY"], FormatFloat(row.OriginY), sheetDocument, sheetNs);
                updatedIds.Add(id);
            }

            if (updatedIds.Count != rows.Count)
            {
                throw new InvalidDataException("HomeArea sheet 待更新行数不一致：expected=" + rows.Count + ", actual=" + updatedIds.Count);
            }

            using (Stream output = sheetEntry.Open())
            {
                output.SetLength(0);
                sheetDocument.Save(output);
            }
        }

        private void ValidateHomeAreaExcelSheet(ZipArchive archive)
        {
            LoadHomeAreaSheet(archive, out _, out XmlDocument sheetDocument, out XmlNamespaceManager sheetNs, out List<string> sharedStrings);
            XmlNodeList rowNodes = sheetDocument.SelectNodes("//x:sheetData/x:row", sheetNs);
            FindHeaderRow(rowNodes, sharedStrings, sheetNs, out Dictionary<string, int> columns);
            RequireExcelColumns(columns);
            HashSet<int> validatedIds = new HashSet<int>();
            for (int i = 0; i < rowNodes.Count; i++)
            {
                XmlNode rowNode = rowNodes[i];
                if (!TryParseExcelInt(ReadCellValue(FindCell(rowNode, columns["id"]), sharedStrings, sheetNs), out int id) || !rowById.TryGetValue(id, out HomeAreaRow expected))
                {
                    continue;
                }

                bool valid = TryParseExcelInt(ReadCellValue(FindCell(rowNode, columns["gridWidth"]), sharedStrings, sheetNs), out int gridWidth)
                    && TryParseExcelInt(ReadCellValue(FindCell(rowNode, columns["gridHeight"]), sharedStrings, sheetNs), out int gridHeight)
                    && TryParseExcelFloat(ReadCellValue(FindCell(rowNode, columns["cellSize"]), sharedStrings, sheetNs), out float cellSize)
                    && TryParseExcelFloat(ReadCellValue(FindCell(rowNode, columns["originX"]), sharedStrings, sheetNs), out float originX)
                    && TryParseExcelFloat(ReadCellValue(FindCell(rowNode, columns["originY"]), sharedStrings, sheetNs), out float originY)
                    && gridWidth == expected.GridWidth && gridHeight == expected.GridHeight
                    && Mathf.Approximately(cellSize, expected.CellSize) && Mathf.Approximately(originX, expected.OriginX) && Mathf.Approximately(originY, expected.OriginY);
                if (!valid)
                {
                    throw new InvalidDataException("HomeArea 源 Excel 写后校验不一致：id=" + id);
                }

                validatedIds.Add(id);
            }

            if (validatedIds.Count != rows.Count)
            {
                throw new InvalidDataException("HomeArea 源 Excel 写后校验行数不一致：expected=" + rows.Count + ", actual=" + validatedIds.Count);
            }
        }

        private static void LoadHomeAreaSheet(ZipArchive archive, out ZipArchiveEntry sheetEntry, out XmlDocument sheetDocument, out XmlNamespaceManager sheetNs, out List<string> sharedStrings)
        {
            sharedStrings = ReadExcelSharedStrings(archive);
            XmlDocument workbook = LoadExcelXml(archive.GetEntry("xl/workbook.xml"), "workbook.xml");
            XmlNamespaceManager workbookNs = new XmlNamespaceManager(workbook.NameTable);
            workbookNs.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            XmlNode sheetNode = workbook.SelectSingleNode("//x:sheet[@name='HomeArea']", workbookNs);
            XmlAttribute relationshipId = sheetNode?.Attributes?["id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"];
            if (relationshipId == null)
            {
                throw new InvalidDataException("h.家园1_0A.xlsx 缺少 HomeArea sheet 或 relationship id。");
            }

            XmlDocument relationships = LoadExcelXml(archive.GetEntry("xl/_rels/workbook.xml.rels"), "workbook.xml.rels");
            XmlNamespaceManager relationshipsNs = new XmlNamespaceManager(relationships.NameTable);
            relationshipsNs.AddNamespace("r", "http://schemas.openxmlformats.org/package/2006/relationships");
            XmlNode relationship = relationships.SelectSingleNode("//r:Relationship[@Id='" + relationshipId.Value + "']", relationshipsNs);
            XmlAttribute targetAttribute = relationship?.Attributes?["Target"];
            if (targetAttribute == null)
            {
                throw new InvalidDataException("无法解析 HomeArea sheet 的目标 XML。");
            }

            string sheetPath = targetAttribute.Value.Replace("\\", "/").TrimStart('/');
            if (!sheetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            {
                sheetPath = "xl/" + sheetPath;
            }

            sheetEntry = archive.GetEntry(sheetPath);
            sheetDocument = LoadExcelXml(sheetEntry, sheetPath);
            sheetNs = new XmlNamespaceManager(sheetDocument.NameTable);
            sheetNs.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        }

        private static XmlNode FindHeaderRow(XmlNodeList rowNodes, List<string> sharedStrings, XmlNamespaceManager sheetNs, out Dictionary<string, int> columns)
        {
            columns = null;
            for (int i = 0; i < rowNodes.Count; i++)
            {
                Dictionary<string, int> candidate = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                XmlNodeList cells = rowNodes[i].SelectNodes("x:c", sheetNs);
                for (int j = 0; j < cells.Count; j++)
                {
                    string value = ReadCellValue(cells[j], sharedStrings, sheetNs);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        candidate[value.Trim()] = GetExcelColumnIndex(cells[j].Attributes?["r"]?.Value);
                    }
                }

                if (candidate.ContainsKey("id") && candidate.ContainsKey("gridWidth") && candidate.ContainsKey("gridHeight") && candidate.ContainsKey("cellSize") && candidate.ContainsKey("originX") && candidate.ContainsKey("originY"))
                {
                    columns = candidate;
                    return rowNodes[i];
                }
            }

            throw new InvalidDataException("HomeArea sheet 未找到包含必要字段的表头行。");
        }

        private static void RequireExcelColumns(Dictionary<string, int> columns)
        {
            string[] required = { "id", "gridWidth", "gridHeight", "cellSize", "originX", "originY" };
            for (int i = 0; i < required.Length; i++)
            {
                if (columns == null || !columns.ContainsKey(required[i]))
                {
                    throw new InvalidDataException("HomeArea sheet 缺少必要列：" + required[i]);
                }
            }
        }

        private static XmlNode FindCell(XmlNode rowNode, int columnIndex)
        {
            if (rowNode == null) return null;
            for (int i = 0; i < rowNode.ChildNodes.Count; i++)
            {
                XmlNode cell = rowNode.ChildNodes[i];
                if (cell.LocalName == "c" && GetExcelColumnIndex(cell.Attributes?["r"]?.Value) == columnIndex) return cell;
            }

            return null;
        }

        private static void SetNumericCell(XmlNode rowNode, int columnIndex, string value, XmlDocument document, XmlNamespaceManager sheetNs)
        {
            XmlNode cell = FindCell(rowNode, columnIndex);
            if (cell == null) throw new InvalidDataException("HomeArea sheet 数据行缺少单元格：columnIndex=" + columnIndex);
            if (cell.SelectSingleNode("x:f", sheetNs) != null) throw new InvalidDataException("HomeArea sheet 目标单元格含公式，拒绝覆盖：cell=" + cell.Attributes?["r"]?.Value);
            XmlAttribute type = cell.Attributes?["t"];
            if (type != null && !string.IsNullOrEmpty(type.Value) && !string.Equals(type.Value, "n", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("HomeArea sheet 目标单元格不是数值类型，拒绝覆盖：cell=" + cell.Attributes?["r"]?.Value + ", type=" + type.Value);
            }

            if (type != null) cell.Attributes.Remove(type);
            XmlNode valueNode = cell.SelectSingleNode("x:v", sheetNs);
            if (valueNode == null)
            {
                valueNode = document.CreateElement("v", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                cell.AppendChild(valueNode);
            }

            valueNode.InnerText = value;
        }

        private static string ReadCellValue(XmlNode cellNode, List<string> sharedStrings, XmlNamespaceManager sheetNs)
        {
            if (cellNode == null) return string.Empty;
            string type = cellNode.Attributes?["t"]?.Value ?? string.Empty;
            if (type == "inlineStr") return cellNode.SelectSingleNode(".//x:t", sheetNs)?.InnerText ?? string.Empty;
            XmlNode valueNode = cellNode.SelectSingleNode("x:v", sheetNs);
            if (valueNode == null) return string.Empty;
            if (type == "s" && int.TryParse(valueNode.InnerText, NumberStyles.Integer, InvariantCulture, out int index) && index >= 0 && index < sharedStrings.Count) return sharedStrings[index];
            return valueNode.InnerText;
        }

        private static List<string> ReadExcelSharedStrings(ZipArchive archive)
        {
            List<string> result = new List<string>();
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return result;
            XmlDocument document = LoadExcelXml(entry, "sharedStrings.xml");
            XmlNamespaceManager ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            XmlNodeList stringNodes = document.SelectNodes("//x:si", ns);
            for (int i = 0; i < stringNodes.Count; i++)
            {
                StringBuilder value = new StringBuilder();
                XmlNodeList textNodes = stringNodes[i].SelectNodes(".//x:t", ns);
                for (int j = 0; j < textNodes.Count; j++) value.Append(textNodes[j].InnerText);
                result.Add(value.ToString());
            }

            return result;
        }

        private static XmlDocument LoadExcelXml(ZipArchiveEntry entry, string label)
        {
            if (entry == null) throw new InvalidDataException("Excel 内缺少文件：" + label);
            XmlDocument document = new XmlDocument();
            using (Stream stream = entry.Open()) document.Load(stream);
            return document;
        }

        private static int GetExcelColumnIndex(string cellReference)
        {
            if (string.IsNullOrEmpty(cellReference)) return -1;
            int result = 0;
            for (int i = 0; i < cellReference.Length; i++)
            {
                char c = char.ToUpperInvariant(cellReference[i]);
                if (c < 'A' || c > 'Z') break;
                result = result * 26 + c - 'A' + 1;
            }

            return result - 1;
        }

        private static bool TryParseExcelInt(string value, out int result) => int.TryParse(value, NumberStyles.Integer, InvariantCulture, out result);
        private static bool TryParseExcelFloat(string value, out float result) => float.TryParse(value, NumberStyles.Float, InvariantCulture, out result);

        private static void TryDeleteExcelBackup(string backupPath)
        {
            try
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
            catch (Exception cleanupException)
            {
                Debug.LogError("[TryGameHomeAreaBoundsEditorWindow] 清理 Excel 事务备份失败，请手动删除：" + backupPath + "\n" + cleanupException);
            }
        }

        private void SaveOpenScenesAndAssets()
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[TryGameHomeAreaBoundsEditorWindow] 已保存当前打开的场景、Prefab 和资源。");
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

        private void RefreshDirtyState()
        {
            dirty = false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!IsSameAsTableValues(rows[i]))
                {
                    dirty = true;
                    break;
                }
            }

            Repaint();
        }

        private bool IsSameAsTableValues(HomeAreaRow row)
        {
            if (row == null)
            {
                return true;
            }

            int gridWidth;
            int gridHeight;
            float cellSize;
            float originX;
            float originY;
            if (!TryParseInt(row.Columns, gridWidthColumn, out gridWidth)
                || !TryParseInt(row.Columns, gridHeightColumn, out gridHeight)
                || !TryParseFloat(row.Columns, cellSizeColumn, out cellSize)
                || !TryParseFloat(row.Columns, originXColumn, out originX)
                || !TryParseFloat(row.Columns, originYColumn, out originY))
            {
                return true;
            }

            return row.GridWidth == Mathf.Max(1, gridWidth)
                && row.GridHeight == Mathf.Max(2, gridHeight)
                && Mathf.Approximately(row.CellSize, Mathf.Max(0.01f, cellSize))
                && Mathf.Approximately(row.OriginX, originX)
                && Mathf.Approximately(row.OriginY, originY);
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

        private static string FormatRect(Rect rect)
        {
            return $"center=({FormatFloat(rect.center.x)}, {FormatFloat(rect.center.y)}), size={FormatFloat(rect.width)} x {FormatFloat(rect.height)}";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", InvariantCulture);
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
            public string[] Columns;
            public int Id;
            public int WorldId;
            public string NameKey;
            public int GridWidth;
            public int GridHeight;
            public float CellSize;
            public float OriginX;
            public float OriginY;

            public HomeAreaRow Clone()
            {
                return new HomeAreaRow
                {
                    Columns = Columns != null ? (string[])Columns.Clone() : null,
                    Id = Id,
                    WorldId = WorldId,
                    NameKey = NameKey,
                    GridWidth = GridWidth,
                    GridHeight = GridHeight,
                    CellSize = CellSize,
                    OriginX = OriginX,
                    OriginY = OriginY,
                };
            }

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
