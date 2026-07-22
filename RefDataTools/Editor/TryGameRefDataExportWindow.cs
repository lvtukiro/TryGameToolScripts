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
        private readonly List<ExcelItem> excelItems = new List<ExcelItem>();
        private Vector2 scrollPosition;
        private string excelRootAssetPath;

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
            string excelRoot = TryGameRefDataPaths.DefaultExcelRootAssetPath;
            List<string> excelPaths = TryGameRefDataPaths.FindExportableExcelFiles(
                TryGameRefDataPaths.ToFullPath(excelRoot));
            ExportFiles(excelPaths, TryGameRefDataExportMode.FullCleanRebuild);
        }

        /// <summary>
        /// 初始化窗口配置并刷新配表列表。
        /// </summary>
        private void OnEnable()
        {
            excelRootAssetPath = TryGameRefDataPaths.DefaultExcelRootAssetPath;
            RefreshExcelList();
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
        /// 显示固定的正式源表目录。manifest v3 只允许 canonical 源快照，不能从临时目录发布正式产物。
        /// </summary>
        private void DrawPathBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Excel Root", GUILayout.Width(80f));
            EditorGUILayout.LabelField(excelRootAssetPath);

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(56f)))
            {
                RefreshExcelList();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制导出和选择按钮。
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

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "单项/选中项导出是增量模式，不会删除旧表产物。删除或重命名表后，" +
                "请使用菜单 TryGame/RefData/导出全部配表并生成入口 执行全量清洁重建。",
                MessageType.Info);
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
                    ExportFiles(
                        new List<string> { item.FullPath },
                        TryGameRefDataExportMode.Incremental);
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
            List<string> files = TryGameRefDataPaths.FindExportableExcelFiles(root);
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

            ExportFiles(selected, TryGameRefDataExportMode.Incremental);
        }

        /// <summary>
        /// 导出指定 Excel 列表，并始终在同一 staging 事务内重新生成 Config 入口。
        /// </summary>
        internal static bool ExportFiles(
            List<string> excelFullPaths,
            TryGameRefDataExportMode exportMode)
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
                success = TryGameRefDataExportTransaction.Execute(excelFullPaths, exportMode);

                if (!success)
                {
                    UnityEngine.Debug.LogError(
                        "[TryGameRefDataExportWindow] 配表事务导出失败。正式目录应保持原样或已执行回滚；" +
                        "请查看此前日志中的 staging/backup 路径。");
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
                try
                {
                    EditorPrefs.SetBool("kAutoRefresh", autoRefresh);
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogError(
                        "[TryGameRefDataExportWindow] 恢复 Unity 自动刷新设置失败。" +
                        $"exportSucceeded={success}, expectedAutoRefresh={autoRefresh}\n{exception}");
                }

                try
                {
                    AssetDatabase.Refresh();
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogError(
                        "[TryGameRefDataExportWindow] 导表结束后的 Unity 资源刷新失败，请手动刷新。" +
                        $"exportSucceeded={success}\n{exception}");
                }
            }
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
