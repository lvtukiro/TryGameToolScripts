using Game;
using RefData;
using UnityEditor;
using UnityEngine;

namespace TryGame.Tools.Editor
{
    /// <summary>
    /// 2.0 唯一物品底座的开发期校验入口。只读配置和当前运行存档，
    /// 不创建测试物品、不修档，也不会替代候选存档管线的正式保护。
    /// </summary>
    internal static class TryGameItemInstanceValidationTools
    {
        private const string MenuPath =
            "TryGame/Validation/校验 2.0 唯一物品配置与当前存档";

        [MenuItem(MenuPath, false, 410)]
        private static void Validate()
        {
            if (!ItemInstanceDevelopmentVerifier.TryRun(
                    out string smokeReport,
                    out string smokeError))
            {
                Debug.LogError(
                    $"[TryGameItemInstanceValidationTools] 2.0 唯一物品纯内存 Smoke 失败：" +
                    $"error={smokeError}");
                return;
            }

            int checkedDefinitionCount = 0;
            int unavailableDefinitionCount = 0;
            int violationCount = ValidateDefinitions(
                ref checkedDefinitionCount,
                ref unavailableDefinitionCount);

            int runtimeItemCount = 0;
            int runtimeUnavailableCount = 0;
            if (EditorApplication.isPlaying
                && SaveRuntime.Instance != null
                && SaveRuntime.Instance.Current != null)
            {
                violationCount += ValidateCurrentSession(
                    ref runtimeItemCount,
                    ref runtimeUnavailableCount);
            }

            if (violationCount > 0)
            {
                Debug.LogError(
                    $"[TryGameItemInstanceValidationTools] 2.0 唯一物品校验失败：" +
                    $"violations={violationCount}, definitions={checkedDefinitionCount}, " +
                    $"unavailableDefinitions={unavailableDefinitionCount}, " +
                    $"runtimeItems={runtimeItemCount}, runtimeUnavailable={runtimeUnavailableCount}");
                return;
            }

            Debug.Log(
                $"[TryGameItemInstanceValidationTools] 2.0 唯一物品校验通过：" +
                $"smoke=({smokeReport}), " +
                $"definitions={checkedDefinitionCount}, runtimeItems={runtimeItemCount}, " +
                $"runtimeUnavailable={runtimeUnavailableCount}, " +
                $"checkedRuntime={EditorApplication.isPlaying && SaveRuntime.Instance?.Current != null}");
        }

        private static int ValidateDefinitions(
            ref int checkedCount,
            ref int unavailableCount)
        {
            if (!ItemTable.IsLoaded)
            {
                Debug.LogError(
                    "[TryGameItemInstanceValidationTools] Item 配置尚未初始化；" +
                    "请进入 PlayMode 并完成 RefData 初始化后再运行本校验。");
                return 1;
            }

            int violations = 0;
            for (int index = 0; index < ItemTable.Count; index++)
            {
                RefData.Item item = ItemTable.Items(index);
                if (!IsInstanceItemType(item.ItemType))
                {
                    continue;
                }

                checkedCount++;
                if (!TryGameItemInstanceDefinitionResolver.Instance.TryGetDefinition(
                        item.Id,
                        out ItemRuntimeDefinition definition,
                        out string error))
                {
                    unavailableCount++;
                    violations++;
                    Debug.LogError(
                        $"[TryGameItemInstanceValidationTools] 唯一物品配置不可用：" +
                        $"itemId={item.Id}, itemType={item.ItemType}, error={error}");
                    continue;
                }

                if (item.ItemType == EnumItemType.MemorialFurniture
                    && definition.CanSell)
                {
                    violations++;
                    Debug.LogError(
                        $"[TryGameItemInstanceValidationTools] 纪念家具禁止出售：" +
                        $"itemId={item.Id}, canSell={definition.CanSell}");
                }

                if (item.ItemType == EnumItemType.Furniture
                    && (!TryGameConfigProvider.GetFurniture(definition.TargetId).HasValue
                        || definition.TargetId <= 0))
                {
                    violations++;
                    Debug.LogError(
                        $"[TryGameItemInstanceValidationTools] 家具 Item.targetId 未关联有效 HomeFurniture：" +
                        $"itemId={item.Id}, targetId={definition.TargetId}");
                }
            }

            return violations;
        }

        private static int ValidateCurrentSession(
            ref int runtimeItemCount,
            ref int unavailableCount)
        {
            if (!ItemRuntimeSessionQuery.TryCreateCurrentSnapshot(
                    SaveRuntime.Instance,
                    out ItemRuntimeSessionSnapshot snapshot,
                    out string error))
            {
                Debug.LogError(
                    $"[TryGameItemInstanceValidationTools] 当前唯一物品会话无法建立：error={error}");
                return 1;
            }

            int violations = 0;
            runtimeItemCount = snapshot.Count;
            for (int index = 0; index < snapshot.OrderedUids.Count; index++)
            {
                string uid = snapshot.OrderedUids[index];
                if (!snapshot.TryGet(uid, out ItemViewData view, out _, out _))
                {
                    violations++;
                    Debug.LogError(
                        $"[TryGameItemInstanceValidationTools] Registry 顺序表指向不存在的 UID：uid={uid}");
                    continue;
                }

                if (view.Availability == ItemAvailability.Available)
                {
                    continue;
                }

                unavailableCount++;
                Debug.LogWarning(
                    $"[TryGameItemInstanceValidationTools] 当前物品实例配置不可用但数据已保留：" +
                    $"uid={uid}, itemId={view.ItemId}, " +
                    $"kind={view.Kind}, availability={view.Availability}");
            }

            return violations;
        }

        private static bool IsInstanceItemType(EnumItemType type)
        {
            return type == EnumItemType.Furniture
                || type == EnumItemType.Material
                || type == EnumItemType.RobotEquipment
                || type == EnumItemType.BattleSupply
                || type == EnumItemType.MemorialFurniture;
        }
    }
}
