using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
        public const string DefaultShopInstanceIdText = "90001";

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

        public static bool RandomRefreshHomeShop(int shopInstanceId)
        {
            int count = HomeShopRuntimeStore.RandomRefreshGoods(shopInstanceId);
            if (count <= 0)
            {
                Debug.LogError(
                    $"[HomeAreaDebugUnlocks] 商店实例随机刷新失败：" +
                    $"shopInstanceId={shopInstanceId}，实例不存在或没有可用的家具商品。");
                return false;
            }

            MsgSend.SendMsg(MsgType.RefreshShop, shopInstanceId);
            Debug.Log(
                $"[HomeAreaDebugUnlocks] 已随机刷新商店实例商品：" +
                $"shopInstanceId={shopInstanceId}, count={count}");
            return true;
        }

        public static bool TryGenerateRandomPendingPetFood(
            int count,
            out string summary)
        {
            summary = string.Empty;
            if (!PetFoodDropApplicationService.TryDebugGeneratePending(
                    SaveRuntime.Instance,
                    count,
                    out PetFoodDropGenerationResult[] generated,
                    out string error))
            {
                summary = error ?? "<none>";
                Debug.LogError(
                    $"[HomeAreaDebugUnlocks] 随机添加可收取食物失败：" +
                    $"count={count}, reason={summary}");
                return false;
            }

            Dictionary<int, int> countsByItemId = new Dictionary<int, int>();
            for (int index = 0; index < generated.Length; index++)
            {
                int itemId = generated[index].ItemId;
                countsByItemId.TryGetValue(itemId, out int oldCount);
                countsByItemId[itemId] = oldCount + 1;
            }

            StringBuilder detail = new StringBuilder(64);
            foreach (KeyValuePair<int, int> pair in countsByItemId)
            {
                if (detail.Length > 0)
                {
                    detail.Append(", ");
                }

                detail.Append(pair.Key);
                detail.Append('x');
                detail.Append(pair.Value);
            }

            summary = $"已加入未收获池：总数={generated.Length}，{detail}";
            Debug.Log($"[HomeAreaDebugUnlocks] {summary}");
            return true;
        }

        public static bool TrySetBattleRobotDamaged(
            string robotUid,
            bool damaged,
            out string summary)
        {
            summary = string.Empty;
            SaveRuntime runtime = SaveRuntime.Instance;
            SaveData save = runtime != null ? runtime.Current : null;
            if (save?.robotRoster?.robots == null
                || string.IsNullOrEmpty(robotUid))
            {
                summary = "当前没有可用机器人存档，或 robotUid 为空。";
                Debug.LogError($"[HomeAreaDebugUnlocks] {summary}");
                return false;
            }

            BattleRobotInstanceSaveData target = null;
            for (int index = 0; index < save.robotRoster.robots.Count; index++)
            {
                BattleRobotInstanceSaveData candidate = save.robotRoster.robots[index];
                if (candidate != null && candidate.uid == robotUid)
                {
                    target = candidate;
                    break;
                }
            }

            if (target == null)
            {
                summary = $"目标机器人不存在：robotUid={robotUid}";
                Debug.LogError($"[HomeAreaDebugUnlocks] {summary}");
                return false;
            }

            double next = damaged ? 1d : 0d;
            if (target.repairWorkRemaining == next)
            {
                summary = damaged ? "机器人已经是损毁状态。" : "机器人已经是可战斗状态。";
                return true;
            }

            target.repairWorkRemaining = next;
            runtime.MarkDirty();
            GUIWndBattlePreparationMain.instance.RefreshAfterMutation();
            summary = damaged
                ? $"已将 {target.customName} 设为损毁。"
                : $"已将 {target.customName} 恢复为可战斗。";
            Debug.Log($"[HomeAreaDebugUnlocks] {summary} robotUid={robotUid}");
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
            if (save.furniture?.placed == null
                || save.itemInstances?.standardItems == null
                || save.itemRecovery?.quarantinedItemUids == null
                || save.robotRoster?.robots == null
                || save.warehouse?.occupiedSlots == null)
            {
                Debug.LogError(
                    "[HomeAreaDebugUnlocks] 唯一家具作弊失败，存档家具、Registry 或共享仓库分块不完整。");
                return false;
            }

            int furnitureId = itemConfig.TargetId;
            if (!TryGameConfigProvider.GetFurniture(furnitureId).HasValue)
            {
                Debug.LogError($"[HomeAreaDebugUnlocks] 家具 Item 关联的 HomeFurniture 不存在：itemId={itemConfig.Id}, furnitureId={furnitureId}");
                return false;
            }

            if (!BattleRobotConfigCatalog.TryLoad(
                    out BattleRobotConfigCatalog catalog,
                    out string catalogError))
            {
                Debug.LogError(
                    $"[HomeAreaDebugUnlocks] 机器人配置目录不可用：{catalogError}");
                return false;
            }

            if (!BattlePreparationStorageCapacityResolver.TryGetWarehouseCapacity(
                    save,
                    catalog,
                    out int warehouseCapacity,
                    out string capacityError))
            {
                Debug.LogError(
                    $"[HomeAreaDebugUnlocks] 无法解析共享仓库容量：" +
                    $"error={capacityError}");
                return false;
            }

            SaveRuntime runtime = SaveRuntime.Instance;
            long sessionGeneration = runtime.SessionGeneration;
            List<ItemLocationReference> sourceReferences =
                BattleRobotItemLocationProjection.CreateAllReferences(save);
            if (!ItemMutationState.TryCreate(
                    save.itemInstances,
                    sourceReferences,
                    TryGameItemInstanceDefinitionResolver.Instance,
                    out ItemMutationState itemState,
                    out string stateError))
            {
                Debug.LogError(
                    $"[HomeAreaDebugUnlocks] 无法建立唯一家具作弊事务：{stateError}");
                return false;
            }

            if (add)
            {
                List<ItemCellReferenceSaveData> plannedCells =
                    FurnitureInventory.CloneCells(save.warehouse.occupiedSlots);
                for (int index = 0; index < count; index++)
                {
                    if (!FurnitureInventory.TryFindSmallestFreeCell(
                            plannedCells,
                            warehouseCapacity,
                            out int cellIndex))
                    {
                        Debug.LogError(
                            $"[HomeAreaDebugUnlocks] 共享仓库空间不足，家具作弊事务未提交：" +
                            $"itemId={itemConfig.Id}, requested={count}, accepted={index}");
                        return false;
                    }

                    if (!ItemInstanceCreationService.TryCreateAndPlace(
                            itemState,
                            sessionGeneration,
                            TryGameItemInstanceDefinitionResolver.Instance,
                            ItemInstanceKind.Standard,
                            itemConfig.Id,
                            BattleRobotItemLocationProjection.CreateWarehouseToken(cellIndex),
                            null,
                            out ItemCommandToken token,
                            out string creationError))
                    {
                        Debug.LogError(
                            $"[HomeAreaDebugUnlocks] 家具实例与首个仓库位置创建失败，" +
                            $"作弊事务未提交：itemId={itemConfig.Id}, index={index}, " +
                            $"error={creationError}");
                        return false;
                    }

                    plannedCells.Add(new ItemCellReferenceSaveData
                    {
                        cellIndex = cellIndex,
                        itemUid = token.ItemUid,
                    });
                }
            }
            else
            {
                List<ItemCellReferenceSaveData> candidates = new List<ItemCellReferenceSaveData>();
                for (int index = 0; index < save.warehouse.occupiedSlots.Count; index++)
                {
                    ItemCellReferenceSaveData cell = save.warehouse.occupiedSlots[index];
                    StandardItemInstanceSaveData instance =
                        save.itemInstances.standardItems.Find(value =>
                        value != null && value.uid == cell?.itemUid);
                    if (instance != null && instance.itemId == itemConfig.Id)
                    {
                        candidates.Add(cell);
                    }
                }

                candidates.Sort((left, right) => left.cellIndex.CompareTo(right.cellIndex));
                if (candidates.Count < count)
                {
                    Debug.LogError(
                        $"[HomeAreaDebugUnlocks] 共享仓库内家具数量不足，无法减少：" +
                        $"itemId={itemConfig.Id}, furnitureId={furnitureId}, " +
                        $"requested={count}, available={candidates.Count}");
                    return false;
                }

                ItemMutationBatch destroyBatch =
                    new ItemMutationBatch(itemState, sessionGeneration);
                for (int index = 0; index < count; index++)
                {
                    ItemCellReferenceSaveData cell = candidates[index];
                    ItemCommandToken token =
                        new ItemCommandToken(sessionGeneration, cell.itemUid);
                    if (!destroyBatch.TryDestroy(
                            token,
                            BattleRobotItemLocationProjection.CreateWarehouseToken(
                                cell.cellIndex),
                            out string destroyError))
                    {
                        Debug.LogError(
                            $"[HomeAreaDebugUnlocks] 家具实例销毁事务失败，" +
                            $"作弊事务未提交：uid={cell.itemUid}, " +
                            $"cell={cell.cellIndex}, error={destroyError}");
                        return false;
                    }
                }

                if (!destroyBatch.TryCommit(sessionGeneration, out string commitError))
                {
                    Debug.LogError(
                        $"[HomeAreaDebugUnlocks] 家具实例销毁事务提交失败：{commitError}");
                    return false;
                }
            }

            SaveData ownershipCandidate = CreateOwnershipCandidate(save, itemState);
            if (!BattleRobotItemLocationProjection.TryApplyReferences(
                    ownershipCandidate,
                    itemState.CreateLocationSnapshot(),
                    out string applyError))
            {
                Debug.LogError(
                    $"[HomeAreaDebugUnlocks] 家具作弊事务无法投影回正式容器：" +
                    $"itemId={itemConfig.Id}, error={applyError}");
                return false;
            }

            if (!runtime.IsSessionGenerationCurrent(sessionGeneration)
                || !ReferenceEquals(runtime.Current, save))
            {
                Debug.LogError(
                    "[HomeAreaDebugUnlocks] 家具作弊事务提交前存档会话已变化，已放弃提交。");
                return false;
            }

            save.itemInstances = ownershipCandidate.itemInstances;
            save.warehouse = ownershipCandidate.warehouse;
            runtime.MarkDirty();
            MsgSend.SendMsg(MsgType.FurnitureInventoryChanged, null);
            MsgSend.SendMsg(MsgType.OnItemChange, null);
            Debug.Log($"[HomeAreaDebugUnlocks] 已{(add ? "增加" : "减少")}家具物品：itemId={itemConfig.Id}, furnitureId={furnitureId}, count={count}");
            return true;
        }

        private static SaveData CreateOwnershipCandidate(
            SaveData save,
            ItemMutationState itemState)
        {
            FurnitureSaveData furniture = new FurnitureSaveData
            {
                placed = new List<PlacedFurnitureData>(),
            };
            for (int index = 0; index < save.furniture.placed.Count; index++)
            {
                furniture.placed.Add(save.furniture.placed[index]?.Clone());
            }

            return new SaveData
            {
                itemInstances = itemState.CreateRegistrySnapshot(),
                itemRecovery = save.itemRecovery.Clone(),
                warehouse = save.warehouse.Clone(),
                furniture = furniture,
                robotRoster = save.robotRoster.Clone(),
            };
        }
    }
}
