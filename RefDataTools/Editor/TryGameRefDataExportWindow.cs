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
        private const string CommonDefineExcelName = "共用枚举结构体.xlsx";
        private const string ExcelRootPrefsKey = "TryGame.RefData.ExcelRoot";
        private const string ExportAfterGeneratePrefsKey = "TryGame.RefData.ExportAfterGenerate";
        private const string LegacyExcelRootAssetPath = "Assets/Resources/TryGameRefdataRes/v2";
        private const string LegacyRuntimeExcelRootAssetPath = "Assets/Resources/TryGameRefdataRuntimeRes/v2";

        private readonly List<ExcelItem> excelItems = new List<ExcelItem>();
        private Vector2 scrollPosition;
        private string excelRootAssetPath;
        private bool generateConfigAfterExport;

        /// <summary>
        /// 打开 TryGame 配表导出窗口。
        /// </summary>
        [MenuItem("TryGame/RefData/打开导表窗口")]
        public static void Open()
        {
            TryGameRefDataExportWindow window = GetWindow<TryGameRefDataExportWindow>("TryGame 配表导出");
            window.minSize = new Vector2(680f, 420f);
            window.Show();
        }

        /// <summary>
        /// 从菜单直接导出默认目录下的全部配表。
        /// </summary>
        [MenuItem("TryGame/RefData/导出全部配表并生成入口")]
        public static void ExportAllByMenu()
        {
            string excelRoot = ResolveConfiguredExcelRoot();
            List<string> excelPaths = FindExcelFiles(TryGameRefDataPaths.ToFullPath(excelRoot));
            ExportFiles(excelPaths, true);
        }

        /// <summary>
        /// 初始化窗口配置并刷新配表列表。
        /// </summary>
        private void OnEnable()
        {
            excelRootAssetPath = ResolveConfiguredExcelRoot();
            generateConfigAfterExport = EditorPrefs.GetBool(ExportAfterGeneratePrefsKey, true);
            RefreshExcelList();
        }

        private static string ResolveConfiguredExcelRoot()
        {
            string configured = EditorPrefs.GetString(ExcelRootPrefsKey, TryGameRefDataPaths.DefaultExcelRootAssetPath);
            if (IsLegacyExcelRoot(configured))
            {
                configured = TryGameRefDataPaths.DefaultExcelRootAssetPath;
                EditorPrefs.SetString(ExcelRootPrefsKey, configured);
                UnityEngine.Debug.LogWarning($"[TryGameRefDataExportWindow] 已把旧源表路径迁移为：{configured}");
            }

            return configured;
        }

        private static bool IsLegacyExcelRoot(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return false;
            }

            string normalized = configured.Trim().Replace("\\", "/").TrimEnd('/');
            if (string.Equals(normalized, LegacyExcelRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, LegacyRuntimeExcelRootAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                string configuredFullPath = TryGameRefDataPaths.ToFullPath(normalized).TrimEnd('/');
                string legacyFullPath = TryGameRefDataPaths.ToFullPath(LegacyExcelRootAssetPath).TrimEnd('/');
                string legacyRuntimeFullPath = TryGameRefDataPaths.ToFullPath(LegacyRuntimeExcelRootAssetPath).TrimEnd('/');
                return string.Equals(configuredFullPath, legacyFullPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(configuredFullPath, legacyRuntimeFullPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[TryGameRefDataExportWindow] 无法解析已保存的 Excel Root，保留原值等待用户修正：path={configured}\n{exception}");
                return false;
            }
        }

        /// <summary>
        /// 绘制配表导出窗口。
        /// </summary>
        private void OnGUI()
        {
            DrawPathBar();
            DrawToolbar();
            DrawExcelList();
        }

        /// <summary>
        /// 绘制配表根目录选择栏。
        /// </summary>
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

        /// <summary>
        /// 绘制导出、刷新和生成入口按钮。
        /// </summary>
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

        /// <summary>
        /// 绘制当前目录下的 Excel 配表列表。
        /// </summary>
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

        /// <summary>
        /// 重新扫描当前目录下的 Excel 配表。
        /// </summary>
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

        /// <summary>
        /// 收集窗口中勾选的配表并执行导出。
        /// </summary>
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

        /// <summary>
        /// 导出指定 Excel 列表，并在成功后按需生成 Config 入口。
        /// </summary>
        internal static bool ExportFiles(List<string> excelFullPaths, bool generateConfig)
        {
            if (excelFullPaths == null || excelFullPaths.Count == 0)
            {
                UnityEngine.Debug.LogError("[TryGameRefDataExportWindow] 配表导出失败，没有传入任何 Excel 文件。");
                return false;
            }

            for (int i = 0; i < excelFullPaths.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(excelFullPaths[i]) || !File.Exists(excelFullPaths[i]))
                {
                    UnityEngine.Debug.LogError($"[TryGameRefDataExportWindow] 配表导出失败，Excel 文件不存在：index={i}, path={excelFullPaths[i] ?? "<null>"}");
                    return false;
                }
            }

            bool autoRefresh = EditorPrefs.GetBool("kAutoRefresh");
            bool success = false;
            try
            {
                EditorPrefs.SetBool("kAutoRefresh", false);

                if (!TryGameRefDataRuntimeSync.PrepareSourceOutputForExport())
                {
                    UnityEngine.Debug.LogError("[TryGameRefDataExportWindow] 配表导出失败：无法清理上次残留的源 bytes，未启动导表进程。");
                    return false;
                }

                List<string> cltabtoyFiles = new List<string>();
                List<string> languageFiles = new List<string>();
                SplitExportFiles(excelFullPaths, cltabtoyFiles, languageFiles);

                success = true;
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

                if (success)
                {
                    success = TryGameRefDataRuntimeSync.SyncFromSourceOutput();
                }

                if (success && generateConfig)
                {
                    TryGameConfigGenerator.GenerateDefault();
                }

                if (!success)
                {
                    UnityEngine.Debug.LogError("[TryGameRefDataExportWindow] 配表导出失败，未继续生成 Config 入口。请查看此前的导出错误日志。");
                }

                return success;
            }
            catch (Exception exception)
            {
                success = false;
                UnityEngine.Debug.LogError("[TryGameRefDataExportWindow] 配表导出流程异常：\n" + exception);
                return false;
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

        /// <summary>
        /// 判断当前 Excel 是否是语言表。
        /// </summary>
        private static bool IsLanguageTable(string excelFullPath)
        {
            string name = Path.GetFileNameWithoutExtension(excelFullPath);
            return name.IndexOf("Language", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("语言表", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 设置列表中所有配表的选中状态。
        /// </summary>
        private void SetAllSelected(bool selected)
        {
            for (int i = 0; i < excelItems.Count; i++)
            {
                excelItems[i].Selected = selected;
            }
        }

        /// <summary>
        /// 查找指定目录下可导出的 Excel 文件。
        /// </summary>
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
                    !name.StartsWith("~$", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals(CommonDefineExcelName, StringComparison.OrdinalIgnoreCase))
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

            /// <summary>
            /// 创建一个导出窗口里的 Excel 列表项。
            /// </summary>
            public ExcelItem(string fullPath)
            {
                FullPath = fullPath;
                Name = Path.GetFileName(fullPath);
            }
        }
    }
}
