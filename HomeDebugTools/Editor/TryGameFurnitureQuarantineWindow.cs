using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEngine;

namespace TryGame.HomeDebugTools.Editor
{
    /// <summary>
    /// v20 全局 ItemRecovery 只读查看器。保留旧类名和源文件路径，避免 Unity 菜单缓存
    /// 与现有 csproj 在导入刷新前丢失引用。
    /// </summary>
    public sealed class TryGameFurnitureQuarantineWindow : EditorWindow
    {
        private Vector2 scrollPosition;

        [MenuItem("TryGame/Home/全局物品恢复区查看器")]
        public static void Open()
        {
            TryGameFurnitureQuarantineWindow window =
                GetWindow<TryGameFurnitureQuarantineWindow>("物品恢复区");
            window.minSize = new Vector2(620f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("当前运行存档的全局物品恢复区", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这里只读显示无法安全放回正式容器的唯一物品 UID；工具不会修改、恢复或删除存档。",
                MessageType.Info);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("请进入 Play Mode 并加载存档后查看。", MessageType.Warning);
                return;
            }

            SaveData save = SaveRuntime.Instance?.Current;
            List<string> recovery = save?.itemRecovery?.quarantinedItemUids;
            if (save == null || recovery == null)
            {
                EditorGUILayout.HelpBox("当前没有已加载的运行存档或 ItemRecovery 数据。", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"恢复区记录：{recovery.Count}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("输出全部到 Console", GUILayout.Width(150f)))
            {
                LogAll(save);
            }
            EditorGUILayout.EndHorizontal();
            if (recovery.Count == 0)
            {
                EditorGUILayout.HelpBox("当前存档没有隔离的唯一物品。", MessageType.None);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int index = 0; index < recovery.Count; index++)
            {
                string uid = recovery[index];
                TryResolveItem(save.itemInstances, uid, out int itemId, out string kind);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"#{index + 1}  Item {itemId}", EditorStyles.boldLabel);
                DrawSelectable("UID", uid);
                DrawSelectable("实例类型", kind);
                RefData.Item? item = TryGameConfigProvider.GetItem(itemId);
                DrawSelectable(
                    "当前配置",
                    item.HasValue
                        ? $"type={item.Value.ItemType}, targetId={item.Value.TargetId}"
                        : "Item 配置不存在");
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawSelectable(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(130f));
            EditorGUILayout.SelectableLabel(
                value ?? string.Empty,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private static void LogAll(SaveData save)
        {
            List<string> recovery = save?.itemRecovery?.quarantinedItemUids;
            if (recovery == null || recovery.Count == 0)
            {
                Debug.LogWarning(
                    $"[TryGameFurnitureQuarantineWindow] 当前存档没有隔离物品：" +
                    $"slotId={save?.slotId ?? "<none>"}");
                return;
            }

            for (int index = 0; index < recovery.Count; index++)
            {
                string uid = recovery[index];
                TryResolveItem(save.itemInstances, uid, out int itemId, out string kind);
                Debug.LogWarning(
                    $"[TryGameFurnitureQuarantineWindow] ItemRecovery：" +
                    $"slotId={save.slotId}, index={index}, uid={uid ?? "<null>"}, " +
                    $"itemId={itemId}, kind={kind}");
            }
        }

        private static bool TryResolveItem(
            ItemInstanceRegistrySaveData registry,
            string uid,
            out int itemId,
            out string kind)
        {
            itemId = 0;
            kind = "Missing";
            if (TryFind(registry?.standardItems, uid, out itemId))
            {
                kind = "Standard";
                return true;
            }

            if (TryFind(registry?.robotEquipmentItems, uid, out itemId))
            {
                kind = "RobotEquipment";
                return true;
            }

            if (TryFind(registry?.memorialItems, uid, out itemId))
            {
                kind = "Memorial";
                return true;
            }

            return false;
        }

        private static bool TryFind<T>(
            IReadOnlyList<T> source,
            string uid,
            out int itemId)
            where T : ItemInstanceSaveData
        {
            itemId = 0;
            if (source == null)
            {
                return false;
            }

            for (int index = 0; index < source.Count; index++)
            {
                T item = source[index];
                if (item != null && item.uid == uid)
                {
                    itemId = item.itemId;
                    return true;
                }
            }

            return false;
        }
    }
}
