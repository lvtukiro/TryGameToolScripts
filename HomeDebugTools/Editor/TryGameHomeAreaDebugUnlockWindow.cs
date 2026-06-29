using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEngine;

namespace TryGame.HomeDebugTools.Editor
{
    /// <summary>
    /// 1.0C 运行时家区域解锁工具。只修改当前运行存档，不做进游戏自动解锁。
    /// </summary>
    public sealed class TryGameHomeAreaDebugUnlockWindow : EditorWindow
    {
        private string areaIdsText = HomeAreaDebugUnlocks.DefaultAreaIdsText;
        private Vector2 scrollPosition;

        [MenuItem("TryGame/Home/运行时家区域解锁工具")]
        public static void Open()
        {
            TryGameHomeAreaDebugUnlockWindow window = GetWindow<TryGameHomeAreaDebugUnlockWindow>("运行时家区域工具");
            window.minSize = new Vector2(440f, 260f);
            window.Show();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("运行时修改当前存档的 HomeArea 解锁状态", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只在 Play Mode 下生效。填入 HomeAreaId 后点击按钮，直接修改当前运行存档的 unlockedHomeAreaIds 并标记 Dirty。",
                MessageType.Info);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("请先进入 Play Mode，再点击解锁或锁定。", MessageType.Warning);
            }

            EditorGUILayout.LabelField("HomeAreaId 列表");
            areaIdsText = EditorGUILayout.TextArea(areaIdsText, GUILayout.MinHeight(72f));
            EditorGUILayout.HelpBox("支持逗号、空格、分号或换行分隔，例如：10002, 10003", MessageType.None);

            DrawPreview();
            DrawButtons();
            EditorGUILayout.EndScrollView();
        }

        private void DrawPreview()
        {
            List<int> areaIds = new List<int>();
            HomeAreaDebugUnlocks.ParseAreaIds(areaIdsText, areaIds, false);
            string preview = areaIds.Count > 0 ? string.Join(", ", areaIds) : "无";
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("输入区域", preview);
        }

        private void DrawButtons()
        {
            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("解锁到当前档"))
                {
                    ApplyUnlock(true);
                }

                if (GUILayout.Button("从当前档锁定"))
                {
                    ApplyUnlock(false);
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("清空输入"))
            {
                areaIdsText = string.Empty;
            }
        }

        private void ApplyUnlock(bool unlock)
        {
            SaveData save = SaveRuntime.Instance != null ? SaveRuntime.Instance.Current : null;
            if (save == null || save.world == null)
            {
                Debug.LogError("[TryGameHomeAreaDebugUnlockWindow] 当前没有运行中的存档，无法修改 HomeArea 解锁状态。");
                return;
            }

            if (save.world.unlockedHomeAreaIds == null)
            {
                save.world.unlockedHomeAreaIds = new List<int>();
            }

            List<int> areaIds = new List<int>();
            if (!HomeAreaDebugUnlocks.ParseAreaIds(areaIdsText, areaIds))
            {
                Debug.LogError("[TryGameHomeAreaDebugUnlockWindow] 没有有效 HomeAreaId。");
                return;
            }

            bool changed = false;
            for (int i = 0; i < areaIds.Count; i++)
            {
                int areaId = areaIds[i];
                changed |= unlock ? UnlockArea(save, areaId) : LockArea(save, areaId);
            }

            if (changed)
            {
                SaveRuntime.Instance.MarkDirty();
            }

            string action = unlock ? "解锁" : "锁定";
            Debug.Log($"[TryGameHomeAreaDebugUnlockWindow] 已{action}当前档 HomeArea：{string.Join(", ", areaIds)}");
        }

        private static bool UnlockArea(SaveData save, int homeAreaId)
        {
            if (!TryGameConfigProvider.GetHomeArea(homeAreaId).HasValue)
            {
                Debug.LogError($"[TryGameHomeAreaDebugUnlockWindow] HomeArea 配置不存在，不能解锁：{homeAreaId}");
                return false;
            }

            if (save.world.unlockedHomeAreaIds.Contains(homeAreaId))
            {
                return false;
            }

            save.world.unlockedHomeAreaIds.Add(homeAreaId);
            return true;
        }

        private static bool LockArea(SaveData save, int homeAreaId)
        {
            bool removed = save.world.unlockedHomeAreaIds.Remove(homeAreaId);
            if (removed && save.world.currentHomeAreaId == homeAreaId)
            {
                Debug.LogError($"[TryGameHomeAreaDebugUnlockWindow] 已锁定当前所在 HomeArea：{homeAreaId}。当前场景不会自动切区，下次进档会由 WorldRuntime 报错并回默认区域。");
            }

            return removed;
        }
    }
}
