using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Game;
using RefData;
using UnityEngine;

namespace TryGame.HomeDebugTools.Editor
{
    /// <summary>
    /// Home 调试工具的编辑器运行时辅助方法。
    /// 这些方法只修改当前 Play Mode 中的运行存档，方便测试，不参与正式解锁或经济流程。
    /// </summary>
    internal static class HomeAreaDebugUnlocks
    {
        public const string DefaultAreaIdsText = "10002";
        public const string DefaultItemIdText = "1001";
        public const string DefaultItemCountText = "1";

        private static readonly Regex SplitRegex = new Regex(@"[,\s;，；]+", RegexOptions.Compiled);

        public static bool ParseAreaIds(string text, List<int> result, bool logErrors = true)
        {
            if (result == null || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            bool hasAny = false;
            string[] parts = SplitRegex.Split(text.Trim());
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int areaId) || areaId <= 0)
                {
                    if (logErrors)
                    {
                        Debug.LogError($"[HomeAreaDebugUnlocks] 测试 HomeAreaId 非法：{part}");
                    }

                    continue;
                }

                if (!result.Contains(areaId))
                {
                    result.Add(areaId);
                }

                hasAny = true;
            }

            return hasAny;
        }

        public static bool TryParsePositiveInt(string text, string fieldName, out int value)
        {
            value = 0;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
            {
                Debug.LogError($"[HomeAreaDebugUnlocks] {fieldName} 必须是大于 0 的整数：{text}");
                return false;
            }

            value = parsed;
            return true;
        }

        public static bool TryAddItem(int itemId, int count)
        {
            return TryChangeItemCount(itemId, count, true);
        }

        public static bool TryRemoveItem(int itemId, int count)
        {
            return TryChangeItemCount(itemId, count, false);
        }

        public static bool RandomRefreshHomeShop()
        {
            int count = HomeShopRuntimeStore.RandomRefreshGoods();
            if (count <= 0)
            {
                Debug.LogError("[HomeAreaDebugUnlocks] 商店随机刷新失败：没有可用的家具商品。");
                return false;
            }

            MsgSend.SendMsg(MsgType.RefreshShop, null);
            Debug.Log($"[HomeAreaDebugUnlocks] 已随机刷新商店商品，数量：{count}");
            return true;
        }

        private static bool TryChangeItemCount(int itemId, int count, bool add)
        {
            if (count <= 0)
            {
                Debug.LogError($"[HomeAreaDebugUnlocks] 物品数量必须大于 0：{count}");
                return false;
            }

            SaveData save = SaveRuntime.Instance != null ? SaveRuntime.Instance.Current : null;
            if (save == null)
            {
                Debug.LogError("[HomeAreaDebugUnlocks] 当前没有运行中的存档，无法修改物品数量。");
                return false;
            }

            Item? itemConfig = TryGameConfigProvider.GetItem(itemId);
            if (!itemConfig.HasValue)
            {
                Debug.LogError($"[HomeAreaDebugUnlocks] Item 配置不存在，无法作弊修改：itemId={itemId}");
                return false;
            }

            switch (itemConfig.Value.ItemType)
            {
                case EnumItemType.Gold:
                    return TryChangeGold(save, count, add);
                case EnumItemType.Furniture:
                    return TryChangeFurniture(save, itemConfig.Value, count, add);
                default:
                    Debug.LogError($"[HomeAreaDebugUnlocks] 暂不支持该 ItemType 的作弊数量修改：itemId={itemId}, itemType={itemConfig.Value.ItemType}");
                    return false;
            }
        }

        private static bool TryChangeGold(SaveData save, int count, bool add)
        {
            if (save.economy == null)
            {
                save.economy = new EconomySaveData();
            }

            int oldGold = save.economy.gold;
            long changedGold = add ? (long)oldGold + count : (long)oldGold - count;
            if (changedGold < 0)
            {
                changedGold = 0;
            }
            else if (changedGold > int.MaxValue)
            {
                changedGold = int.MaxValue;
            }

            save.economy.gold = (int)changedGold;
            if (save.economy.gold == oldGold)
            {
                return false;
            }

            SaveRuntime.Instance.MarkDirty();
            MsgSend.SendMsg(MsgType.OnCoinChange, null);
            MsgSend.SendMsg(MsgType.OnItemChange, null);
            Debug.Log($"[HomeAreaDebugUnlocks] 已{(add ? "增加" : "减少")}金币：{oldGold} -> {save.economy.gold}");
            return true;
        }

        private static bool TryChangeFurniture(SaveData save, Item itemConfig, int count, bool add)
        {
            if (save.furniture == null)
            {
                save.furniture = new FurnitureSaveData
                {
                    inventory = new List<FurnitureStackData>(),
                    placed = new List<PlacedFurnitureData>(),
                    quarantine = new List<QuarantinedFurnitureData>(),
                };
            }

            if (save.furniture.inventory == null)
            {
                save.furniture.inventory = new List<FurnitureStackData>();
            }

            int furnitureId = itemConfig.TargetId;
            if (!TryGameConfigProvider.GetFurniture(furnitureId).HasValue)
            {
                Debug.LogError($"[HomeAreaDebugUnlocks] 家具 Item 关联的 HomeFurniture 不存在：itemId={itemConfig.Id}, furnitureId={furnitureId}");
                return false;
            }

            bool changed;
            if (add)
            {
                FurnitureInventory.Add(save.furniture.inventory, furnitureId, count);
                changed = true;
            }
            else
            {
                changed = FurnitureInventory.TryConsume(save.furniture.inventory, furnitureId, count);
                if (!changed)
                {
                    Debug.LogError($"[HomeAreaDebugUnlocks] 当前背包内家具数量不足，无法减少：itemId={itemConfig.Id}, furnitureId={furnitureId}, count={count}");
                    return false;
                }
            }

            SaveRuntime.Instance.MarkDirty();
            MsgSend.SendMsg(MsgType.FurnitureInventoryChanged, null);
            MsgSend.SendMsg(MsgType.OnItemChange, null);
            Debug.Log($"[HomeAreaDebugUnlocks] 已{(add ? "增加" : "减少")}家具物品：itemId={itemConfig.Id}, furnitureId={furnitureId}, count={count}");
            return changed;
        }
    }
}
