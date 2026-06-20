using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TryGame.RefDataTools.Editor
{
    /// <summary>
    /// TryGame 配表导出窗口。
    /// </summary>
    public sealed class TryGameRefDataExportWindow : EditorWindow
    {
        private const string ExcelRootPrefsKey = "TryGame.RefData.ExcelRoot";
        private const string ExportAfterGeneratePrefsKey = "TryGame.RefData.ExportAfterGenerate";

        private readonly List<ExcelItem> excelItems = new List<ExcelItem>();
        private Vector2 scrollPosition;
        private string excelRootAssetPath;
        private bool generateConfigAfterExport;

        [MenuItem("TryGame/RefData/打开导表窗口")]
        public static void Open()
        {
            TryGameRefDataExportWindow window = GetWindow<TryGameRefDataExportWindow>("TryGame 配表导出");
            window.minSize = new Vector2(680f, 420f);
            window.Show();
        }

        [MenuItem("TryGame/RefData/导出全部配表并生成入口")]
        public static void ExportAllByMenu()
        {
            string excelRoot = EditorPrefs.GetString(ExcelRootPrefsKey, TryGameRefDataPaths.DefaultExcelRootAssetPath);
            List<string> excelPaths = FindExcelFiles(TryGameRefDataPaths.ToFullPath(excelRoot));
            ExportFiles(excelPaths, true);
        }

        private void OnEnable()
        {
            excelRootAssetPath = EditorPrefs.GetString(ExcelRootPrefsKey, TryGameRefDataPaths.DefaultExcelRootAssetPath);
            generateConfigAfterExport = EditorPrefs.GetBool(ExportAfterGeneratePrefsKey, true);
            RefreshExcelList();
        }

        private void OnGUI()
        {
            DrawPathBar();
            DrawToolbar();
            DrawExcelList();
        }

        private void DrawPathBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Excel Root", GUILayout.Width(80f));

            EditorGUI.BeginChangeCheck();
            excelRootAssetPath = EditorGUILayout.TextField(excelRootAssetPath);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(ExcelRootPrefsKey, excelRootAssetPath);
            }

            if (GUILayout.Button("选择", EditorStyles.toolbarButton, GUILayout.Width(56f)))
            {
                string selected = EditorUtility.OpenFolderPanel("选择配表目录", TryGameRefDataPaths.ToFullPath(excelRootAssetPath), "");
                if (!string.IsNullOrEmpty(selected))
                {
                    excelRootAssetPath = TryGameRefDataPaths.ToAssetPath(selected);
                    EditorPrefs.SetString(ExcelRootPrefsKey, excelRootAssetPath);
                    RefreshExcelList();
                }
            }

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(56f)))
            {
                RefreshExcelList();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("全选", GUILayout.Width(72f)))
            {
                SetAllSelected(true);
            }

            if (GUILayout.Button("全不选", GUILayout.Width(72f)))
            {
                SetAllSelected(false);
            }

            if (GUILayout.Button("导出选中项", GUILayout.Width(120f)))
            {
                ExportSelected();
            }

            if (GUILayout.Button("只生成 Config 入口", GUILayout.Width(140f)))
            {
                TryGameConfigGenerator.GenerateDefault();
            }

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            generateConfigAfterExport = GUILayout.Toggle(generateConfigAfterExport, "导表后生成 Config 入口", GUILayout.Width(180f));
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(ExportAfterGeneratePrefsKey, generateConfigAfterExport);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        private void DrawExcelList()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorGUILayout.HelpBox("Unity 正在刷新或编译，稍后再导表。", MessageType.Info);
                return;
            }

            if (excelItems.Count == 0)
            {
                EditorGUILayout.HelpBox("当前目录没有找到 .xlsx / .xlsm 配表。", MessageType.Warning);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < excelItems.Count; i++)
            {
                ExcelItem item = excelItems[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                item.Selected = EditorGUILayout.Toggle(item.Selected, GUILayout.Width(20f));
                EditorGUILayout.LabelField(item.Name);

                if (GUILayout.Button("单项导出", GUILayout.Width(92f)))
                {
                    ExportFiles(new List<string> { item.FullPath }, generateConfigAfterExport);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void RefreshExcelList()
        {
            excelItems.Clear();

            string root = TryGameRefDataPaths.ToFullPath(excelRootAssetPath);
            List<string> files = FindExcelFiles(root);
            for (int i = 0; i < files.Count; i++)
            {
                excelItems.Add(new ExcelItem(files[i]));
            }

            Repaint();
        }

        private void ExportSelected()
        {
            List<string> selected = new List<string>();
            for (int i = 0; i < excelItems.Count; i++)
            {
                if (excelItems[i].Selected)
                {
                    selected.Add(excelItems[i].FullPath);
                }
            }

            ExportFiles(selected, generateConfigAfterExport);
        }

        private static void ExportFiles(List<string> excelFullPaths, bool generateConfig)
        {
            bool autoRefresh = EditorPrefs.GetBool("kAutoRefresh");
            try
            {
                EditorPrefs.SetBool("kAutoRefresh", false);

                List<string> cltabtoyFiles = new List<string>();
                List<string> languageFiles = new List<string>();
                SplitExportFiles(excelFullPaths, cltabtoyFiles, languageFiles);

                bool success = true;
                if (cltabtoyFiles.Count > 0)
                {
                    TryGameCLTabtoyProcess process = new TryGameCLTabtoyProcess(
                        TryGameRefDataPaths.DefaultOutputAssetPath,
                        TryGameRefDataPaths.DefaultGeneratedTableAssetPath,
                        TryGameRefDataPaths.DefaultLuaOutputAssetPath);
                    success = process.Export(cltabtoyFiles);
                }

                if (success && languageFiles.Count > 0)
                {
                    success = TryGameLanguageExcelExport.Export(languageFiles, TryGameRefDataPaths.DefaultOutputAssetPath);
                }

                if (success && generateConfig)
                {
                    TryGameConfigGenerator.GenerateDefault();
                }
            }
            finally
            {
                EditorPrefs.SetBool("kAutoRefresh", autoRefresh);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// 语言表沿用原项目流程，不参与 cltabtoy 普通表导出。
        /// </summary>
        private static void SplitExportFiles(List<string> excelFullPaths, List<string> cltabtoyFiles, List<string> languageFiles)
        {
            if (excelFullPaths == null)
            {
                return;
            }

            for (int i = 0; i < excelFullPaths.Count; i++)
            {
                string path = excelFullPaths[i];
                if (IsLanguageTable(path))
                {
                    languageFiles.Add(path);
                }
                else
                {
                    cltabtoyFiles.Add(path);
                }
            }
        }

        private static bool IsLanguageTable(string excelFullPath)
        {
            string name = Path.GetFileNameWithoutExtension(excelFullPath);
            return name.IndexOf("Language", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("语言表", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetAllSelected(bool selected)
        {
            for (int i = 0; i < excelItems.Count; i++)
            {
                excelItems[i].Selected = selected;
            }
        }

        private static List<string> FindExcelFiles(string root)
        {
            List<string> result = new List<string>();
            if (!Directory.Exists(root))
            {
                return result;
            }

            string[] files = Directory.GetFiles(root, "*.*", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string extension = Path.GetExtension(file);
                string name = Path.GetFileName(file);
                if ((extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)) &&
                    !name.StartsWith("~$", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(file.Replace("\\", "/"));
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private sealed class ExcelItem
        {
            public readonly string FullPath;
            public readonly string Name;
            public bool Selected = true;

            public ExcelItem(string fullPath)
            {
                FullPath = fullPath;
                Name = Path.GetFileName(fullPath);
            }
        }
    }
}
