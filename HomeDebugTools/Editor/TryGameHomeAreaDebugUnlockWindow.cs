using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEngine;

namespace TryGame.HomeDebugTools.Editor
{
    /// <summary>
    /// Home 运行时作弊工具。
    /// 只在 Play Mode 中修改当前运行存档，用于测试区域解锁、物品数量和商店刷新。
    /// </summary>
    public sealed class TryGameHomeAreaDebugUnlockWindow : EditorWindow
    {
        private string areaIdsText = HomeAreaDebugUnlocks.DefaultAreaIdsText;
        private string itemIdText = HomeAreaDebugUnlocks.DefaultItemIdText;
        private string addItemCountText = HomeAreaDebugUnlocks.DefaultItemCountText;
        private string removeItemCountText = HomeAreaDebugUnlocks.DefaultItemCountText;
        private Vector2 scrollPosition;

        [MenuItem("TryGame/Home/运行时 Home 作弊工具")]
        public static void Open()
        {
            TryGameHomeAreaDebugUnlockWindow window = GetWindow<TryGameHomeAreaDebugUnlockWindow>("Home 作弊工具");
            window.minSize = new Vector2(460f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("运行时修改当前存档", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只在 Play Mode 下生效。工具会直接修改 SaveRuntime.Current，并标记 Dirty；玩家正常存档时会一起写盘。",
                MessageType.Info);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("请先进入 Play Mode，再使用作弊命令。", MessageType.Warning);
            }

            DrawAreaSection();
            DrawItemSection();
            DrawShopSection();
            EditorGUILayout.EndScrollView();
        }

        private void DrawAreaSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("HomeArea 解锁", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("HomeAreaId 列表");
            areaIdsText = EditorGUILayout.TextArea(areaIdsText, GUILayout.MinHeight(72f));
            EditorGUILayout.HelpBox("支持逗号、空格、分号或换行分隔，例如：10002, 10003", MessageType.None);

            DrawAreaPreview();

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

            if (GUILayout.Button("清空区域输入"))
            {
                areaIdsText = string.Empty;
            }
        }

        private void DrawItemSection()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("物品数量作弊", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "当前支持金币 Item 和家具 Item。金币会修改 economy.gold；家具会修改当前档家具背包数量，不会处理已经摆放在场景里的实例。",
                MessageType.None);

            itemIdText = EditorGUILayout.TextField("物品 id", itemIdText);
            addItemCountText = EditorGUILayout.TextField("物品添加数量", addItemCountText);
            removeItemCountText = EditorGUILayout.TextField("物品减少数量", removeItemCountText);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("添加物品数量"))
                {
                    ApplyItemCountChange(true);
                }

                if (GUILayout.Button("减少物品数量"))
                {
                    ApplyItemCountChange(false);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawShopSection()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("商店测试", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "随机刷新会按 HomeShopGoods 表权重抽取商品，并发送 RefreshShop 消息让已打开的商店界面立即刷新。",
                MessageType.None);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("商人随机刷新商品"))
                {
                    HomeAreaDebugUnlocks.RandomRefreshHomeShop();
                }
            }
        }

        private void DrawAreaPreview()
        {
            List<int> areaIds = new List<int>();
            HomeAreaDebugUnlocks.ParseAreaIds(areaIdsText, areaIds, false);
            string preview = areaIds.Count > 0 ? string.Join(", ", areaIds) : "无";
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("输入区域", preview);
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

        private void ApplyItemCountChange(bool add)
        {
            if (!HomeAreaDebugUnlocks.TryParsePositiveInt(itemIdText, "物品 id", out int itemId))
            {
                return;
            }

            string countText = add ? addItemCountText : removeItemCountText;
            string fieldName = add ? "物品添加数量" : "物品减少数量";
            if (!HomeAreaDebugUnlocks.TryParsePositiveInt(countText, fieldName, out int count))
            {
                return;
            }

            if (add)
            {
                HomeAreaDebugUnlocks.TryAddItem(itemId, count);
            }
            else
            {
                HomeAreaDebugUnlocks.TryRemoveItem(itemId, count);
            }
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
                Debug.LogError($"[TryGameHomeAreaDebugUnlockWindow] 已锁定当前所在 HomeArea：{homeAreaId}。当前场景不会自动切区，下次进档会由 WorldRuntime 校验并回默认区域。");
            }

            return removed;
        }
    }
}
