#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 临时战斗诊断日志开关。设置只保存在 EditorPrefs，不进入游戏存档；
    /// 进入 Play Mode 时会重新应用到运行时日志入口。
    /// </summary>
    public sealed class BattleDiagnosticsLogSettingsWindow : EditorWindow
    {
        private const string AllEnabledKey =
            "TryGame.BattleDiagnosticsLogSettings.AllEnabled";
        private const string CategoryKeyPrefix =
            "TryGame.BattleDiagnosticsLogSettings.Category.";

        [MenuItem("TryGame/Tools/Battle Diagnostics Log Settings", false, 490)]
        private static void Open()
        {
            BattleDiagnosticsLogSettingsWindow window =
                GetWindow<BattleDiagnosticsLogSettingsWindow>();
            window.titleContent = new GUIContent("Battle 临时日志");
            window.minSize = new Vector2(420f, 440f);
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void InitializeEditorSettings()
        {
            ApplyEditorPrefs();
        }

        private void OnEnable()
        {
            ApplyEditorPrefs();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "这些是开发阶段临时诊断日志。关闭某类日志后不会写入 BattleRuntimeTrace.txt，\n" +
                "设置不进入存档，发布前会统一删除日志代码。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            bool allEnabled = EditorGUILayout.ToggleLeft(
                "启用全部临时日志",
                BattleDiagnosticsLogSettings.AllEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(AllEnabledKey, allEnabled);
                BattleDiagnosticsLogSettings.SetAllEnabled(allEnabled);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全部开启"))
            {
                BattleDiagnosticsLogSettings.EnableAllCategories();
                SaveCurrentPrefs();
            }

            if (GUILayout.Button("全部关闭"))
            {
                BattleDiagnosticsLogSettings.DisableAllCategories();
                SaveCurrentPrefs();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("按类别开关", EditorStyles.boldLabel);
            IReadOnlyList<string> categories = BattleDiagnosticsLogSettings.Categories;
            for (int index = 0; index < categories.Count; index++)
            {
                string category = categories[index];
                bool enabled = BattleDiagnosticsLogSettings.IsCategoryEnabled(category);
                EditorGUI.BeginChangeCheck();
                bool next = EditorGUILayout.ToggleLeft(category, enabled);
                if (EditorGUI.EndChangeCheck())
                {
                    // “全部关闭”后单独勾选某类日志时，自动恢复总开关；
                    // 其它类别仍保留关闭状态，因此可以真正做到只开一类。
                    if (next && !BattleDiagnosticsLogSettings.AllEnabled)
                    {
                        BattleDiagnosticsLogSettings.SetAllEnabled(true);
                        EditorPrefs.SetBool(AllEnabledKey, true);
                    }

                    EditorPrefs.SetBool(CategoryKeyPrefix + category, next);
                    BattleDiagnosticsLogSettings.SetCategoryEnabled(category, next);
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "当前状态：" +
                (BattleDiagnosticsLogSettings.AllEnabled ? "总开关开启" : "总开关关闭"),
                EditorStyles.miniLabel);
        }

        private static void ApplyEditorPrefs()
        {
            bool allEnabled = EditorPrefs.GetBool(AllEnabledKey, true);
            BattleDiagnosticsLogSettings.SetAllEnabled(allEnabled);
            IReadOnlyList<string> categories = BattleDiagnosticsLogSettings.Categories;
            bool otherEnabled = EditorPrefs.GetBool(
                CategoryKeyPrefix + "其它",
                true);
            for (int index = 0; index < categories.Count; index++)
            {
                string category = categories[index];
                // 新增类别沿用“其它”的旧设置，避免用户之前只开启搜刮
                // 日志时，新类别突然恢复为默认开启。
                bool enabled = EditorPrefs.GetBool(
                    CategoryKeyPrefix + category,
                    otherEnabled);
                BattleDiagnosticsLogSettings.SetCategoryEnabled(category, enabled);
            }
        }

        private static void SaveCurrentPrefs()
        {
            EditorPrefs.SetBool(AllEnabledKey, BattleDiagnosticsLogSettings.AllEnabled);
            IReadOnlyList<string> categories = BattleDiagnosticsLogSettings.Categories;
            for (int index = 0; index < categories.Count; index++)
            {
                string category = categories[index];
                EditorPrefs.SetBool(
                    CategoryKeyPrefix + category,
                    BattleDiagnosticsLogSettings.IsCategoryEnabled(category));
            }
        }
    }
}
#endif
