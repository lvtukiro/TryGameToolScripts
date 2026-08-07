using System.Collections.Generic;
using Game;
using RefData;
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
        private string shopInstanceIdText = HomeAreaDebugUnlocks.DefaultShopInstanceIdText;
        private string petCommandStatus = "尚未执行宠物行为命令。";
        private MessageType petCommandStatusType = MessageType.None;
        private string foodDropCommandStatus = "尚未执行食物掉落命令。";
        private MessageType foodDropCommandStatusType = MessageType.None;
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
            DrawFoodDropSection();
            DrawShopSection();
            DrawBattleRobotSection();
            DrawPetSection();
            EditorGUILayout.EndScrollView();
        }

        private static void DrawBattleRobotSection()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("备战机器人状态验收", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "仅用于 2.0b 验收：指定机器人可切换为损毁或恢复可战斗。" +
                "修改会标记当前档 Dirty；正式包不存在这个入口。",
                MessageType.Info);

            SaveData save = SaveRuntime.Instance != null
                ? SaveRuntime.Instance.Current
                : null;
            List<BattleRobotInstanceSaveData> robots = save?.robotRoster?.robots;
            if (!EditorApplication.isPlaying || robots == null)
            {
                EditorGUILayout.LabelField("当前没有可读取的运行存档。", EditorStyles.miniLabel);
                return;
            }

            if (robots.Count == 0)
            {
                EditorGUILayout.LabelField("当前档还没有机器人。", EditorStyles.miniLabel);
                return;
            }

            for (int index = 0; index < robots.Count; index++)
            {
                BattleRobotInstanceSaveData robot = robots[index];
                if (robot == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                string state = robot.repairWorkRemaining > 0d ? "损毁" : "可战斗";
                EditorGUILayout.LabelField(
                    $"{robot.customName}  [{state}]  slot={robot.rosterSlotId}");
                EditorGUILayout.SelectableLabel(
                    robot.uid ?? string.Empty,
                    EditorStyles.miniLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(robot.repairWorkRemaining > 0d))
                {
                    if (GUILayout.Button("设为损毁"))
                    {
                        HomeAreaDebugUnlocks.TrySetBattleRobotDamaged(
                            robot.uid,
                            true,
                            out _);
                    }
                }

                using (new EditorGUI.DisabledScope(robot.repairWorkRemaining <= 0d))
                {
                    if (GUILayout.Button("恢复可战斗"))
                    {
                        HomeAreaDebugUnlocks.TrySetBattleRobotDamaged(
                            robot.uid,
                            false,
                            out _);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
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
                "按商店实例 ID 随机刷新；shopId 只由该实例绑定的配置决定。实例必须已由商人入口创建。",
                MessageType.None);
            shopInstanceIdText = EditorGUILayout.TextField("商店实例 id", shopInstanceIdText);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("商人随机刷新商品"))
                {
                    if (HomeAreaDebugUnlocks.TryParsePositiveInt(
                            shopInstanceIdText,
                            "商店实例 id",
                            out int shopInstanceId))
                    {
                        HomeAreaDebugUnlocks.RandomRefreshHomeShop(shopInstanceId);
                    }
                }
            }
        }

        private void DrawFoodDropSection()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("可收取食物作弊", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "直接向当前存档的未收获池加入随机食物，不走输入概率和冷却。" +
                "在 Full 家园中会立即按正式规则显示；不在 Full 时，返回家园后显示。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("随机食物 ×1"))
                {
                    RunFoodDropCommand(1);
                }

                if (GUILayout.Button("随机食物 ×5"))
                {
                    RunFoodDropCommand(5);
                }

                if (GUILayout.Button("随机食物 ×10"))
                {
                    RunFoodDropCommand(10);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("食物栏重复格 ×3"))
                {
                    RunFoodBarRepeatCommand(3);
                }

                if (GUILayout.Button("恢复真实食物格"))
                {
                    RunFoodBarRepeatCommand(1);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.HelpBox(foodDropCommandStatus, foodDropCommandStatusType);
        }

        private void RunFoodDropCommand(int count)
        {
            if (!EditorApplication.isPlaying)
            {
                foodDropCommandStatus = "请先进入 Play Mode。";
                foodDropCommandStatusType = MessageType.Warning;
                return;
            }

            bool succeeded = HomeAreaDebugUnlocks.TryGenerateRandomPendingPetFood(
                count,
                out foodDropCommandStatus);
            foodDropCommandStatusType = succeeded
                ? MessageType.Info
                : MessageType.Warning;
        }

        private void RunFoodBarRepeatCommand(int repeatCount)
        {
            bool succeeded = HomePetFoodPresentationRoot.TrySetDebugItemRepeatCount(
                repeatCount,
                out string error);
            foodDropCommandStatus = succeeded
                ? repeatCount == 1
                    ? "已恢复真实食物格显示。"
                    : $"每种已有食物临时显示 {repeatCount} 格；只用于滚动测试，不修改存档。"
                : error ?? "<none>";
            foodDropCommandStatusType = succeeded
                ? MessageType.Info
                : MessageType.Warning;
        }

        private void DrawPetSection()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("宠物自主行为验收", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只跳过随机抽签，仍走正式资格、技能、家具目录、A*、预占和状态机。" +
                "“睡桌子”会临时把 Energy 压到睡眠阈值，并且必须存在桌面 Slot；" +
                "桌面上的其它家具不会禁用睡眠位置。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("强制 Idle"))
                {
                    RunPetCommand(PetIntentType.Idle, false);
                }

                if (GUILayout.Button("强制 Walk"))
                {
                    RunPetCommand(PetIntentType.Walk, false);
                }

                if (GUILayout.Button("强制走楼梯"))
                {
                    RunPetCommand(PetIntentType.TraversePath, false);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("强制 Rest"))
                {
                    RunPetCommand(PetIntentType.Rest, false);
                }

                if (GUILayout.Button("强制睡桌子"))
                {
                    RunPetCommand(PetIntentType.Sleep, true);
                }

                if (GUILayout.Button("强制 Play 电视"))
                {
                    RunPetCommand(PetIntentType.Play, true);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.HelpBox(petCommandStatus, petCommandStatusType);
        }

        private void RunPetCommand(
            PetIntentType intent,
            bool requireFurnitureTarget)
        {
            if (!EditorApplication.isPlaying)
            {
                petCommandStatus = "请先进入 Play Mode。";
                petCommandStatusType = MessageType.Warning;
                return;
            }

            if (!HomePetSceneService.TryGetCurrentMainPetController(
                    out HomePetController controller,
                    out string controllerError))
            {
                petCommandStatus =
                    $"当前没有可用宠物运行对象：{controllerError ?? "<none>"}";
                petCommandStatusType = MessageType.Warning;
                return;
            }

            if (!controller.TryDebugForceBehaviour(
                    intent,
                    requireFurnitureTarget,
                    out string error))
            {
                petCommandStatus =
                    $"命令未执行：intent={intent}, reason={error ?? "<none>"}";
                petCommandStatusType = MessageType.Warning;
                return;
            }

            petCommandStatus =
                $"命令已提交：intent={intent}, state={controller.StateType}, " +
                $"energy={controller.Energy:F3}";
            petCommandStatusType = MessageType.Info;
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

            if (save.home == null)
            {
                Debug.LogError("[TryGameHomeAreaDebugUnlockWindow] 当前存档缺少 HomeProgressSaveData，无法修改 HomeArea 解锁状态。");
                return;
            }

            if (!TryResolveCurrentHomeWorldZone(save, out int homeWorldZoneId))
            {
                Debug.LogError("[TryGameHomeAreaDebugUnlockWindow] 无法解析当前存档对应的 Home WorldZone，已拒绝修改 HomeArea 解锁状态。");
                return;
            }

            if (save.home.unlockedHomeAreaIds == null)
            {
                save.home.unlockedHomeAreaIds = new List<int>();
            }

            List<int> areaIds = new List<int>();
            if (!HomeAreaDebugUnlocks.ParseAreaIds(areaIdsText, areaIds))
            {
                Debug.LogError("[TryGameHomeAreaDebugUnlockWindow] 没有有效 HomeAreaId。");
                return;
            }

            if (!ValidateAreasBelongToHomeWorldZone(areaIds, homeWorldZoneId))
            {
                Debug.LogError(
                    $"[TryGameHomeAreaDebugUnlockWindow] 输入中至少有一个 HomeArea 不属于当前 Home WorldZone，" +
                    $"已拒绝整批修改：worldZoneId={homeWorldZoneId}");
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
            Debug.Log(
                $"[TryGameHomeAreaDebugUnlockWindow] 已{action}当前档 HomeArea：" +
                $"worldZoneId={homeWorldZoneId}, areas={string.Join(", ", areaIds)}");
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
            if (save.home.unlockedHomeAreaIds.Contains(homeAreaId))
            {
                return false;
            }

            save.home.unlockedHomeAreaIds.Add(homeAreaId);
            return true;
        }

        private static bool LockArea(SaveData save, int homeAreaId)
        {
            bool removed = save.home.unlockedHomeAreaIds.Remove(homeAreaId);
            if (removed && save.home.lastHomeAreaId == homeAreaId)
            {
                Debug.LogError(
                    $"[TryGameHomeAreaDebugUnlockWindow] 已锁定当前 Home Zone 最后使用的 HomeArea：{homeAreaId}。" +
                    "当前场景不会自动切区，下次进入 Home Zone 时会由 WorldRuntime 校验并回默认区域。");
            }

            return removed;
        }

        private static bool TryResolveCurrentHomeWorldZone(SaveData save, out int homeWorldZoneId)
        {
            homeWorldZoneId = 0;
            if (save?.world == null || save.home == null)
            {
                Debug.LogError("[TryGameHomeAreaDebugUnlockWindow] 解析 Home WorldZone 失败，存档世界或 Home 进度为空。");
                return false;
            }

            HomeArea? lastHomeArea = TryGameConfigProvider.GetHomeArea(save.home.lastHomeAreaId);
            if (!lastHomeArea.HasValue)
            {
                Debug.LogError(
                    $"[TryGameHomeAreaDebugUnlockWindow] 解析 Home WorldZone 失败，最后 HomeArea 配置不存在：" +
                    $"homeAreaId={save.home.lastHomeAreaId}");
                return false;
            }

            homeWorldZoneId = lastHomeArea.Value.WorldZoneId;
            WorldZone? homeZone = TryGameConfigProvider.GetWorldZone(homeWorldZoneId);
            if (!homeZone.HasValue
                || homeZone.Value.ZoneType != EnumWorldZoneType.Home
                || homeZone.Value.WorldId != save.world.currentWorldId)
            {
                Debug.LogError(
                    $"[TryGameHomeAreaDebugUnlockWindow] 最后 HomeArea 没有归属当前世界的 Home WorldZone：" +
                    $"homeAreaId={lastHomeArea.Value.Id}, worldZoneId={homeWorldZoneId}, " +
                    $"currentWorldId={save.world.currentWorldId}, hasZone={homeZone.HasValue}, " +
                    $"zoneType={(homeZone.HasValue ? homeZone.Value.ZoneType.ToString() : "<missing>")}, " +
                    $"zoneWorldId={(homeZone.HasValue ? homeZone.Value.WorldId : 0)}");
                homeWorldZoneId = 0;
                return false;
            }

            WorldZone? activeZone = TryGameConfigProvider.GetWorldZone(save.world.currentWorldZoneId);
            if (!activeZone.HasValue || activeZone.Value.WorldId != save.world.currentWorldId)
            {
                Debug.LogError(
                    $"[TryGameHomeAreaDebugUnlockWindow] 当前 WorldZone 配置无效：" +
                    $"worldZoneId={save.world.currentWorldZoneId}, currentWorldId={save.world.currentWorldId}");
                homeWorldZoneId = 0;
                return false;
            }

            if (activeZone.Value.ZoneType == EnumWorldZoneType.Home
                && activeZone.Value.Id != homeWorldZoneId)
            {
                Debug.LogError(
                    $"[TryGameHomeAreaDebugUnlockWindow] 当前 Home WorldZone 与最后 HomeArea 归属不一致：" +
                    $"activeWorldZoneId={activeZone.Value.Id}, areaWorldZoneId={homeWorldZoneId}, " +
                    $"homeAreaId={lastHomeArea.Value.Id}");
                homeWorldZoneId = 0;
                return false;
            }

            if (save.world.unlockedWorldZoneIds == null
                || !save.world.unlockedWorldZoneIds.Contains(homeWorldZoneId))
            {
                Debug.LogError(
                    $"[TryGameHomeAreaDebugUnlockWindow] Home WorldZone 尚未解锁或解锁列表为空：" +
                    $"worldZoneId={homeWorldZoneId}");
                homeWorldZoneId = 0;
                return false;
            }

            return true;
        }

        private static bool ValidateAreasBelongToHomeWorldZone(List<int> areaIds, int homeWorldZoneId)
        {
            bool valid = true;
            for (int i = 0; i < areaIds.Count; i++)
            {
                int homeAreaId = areaIds[i];
                HomeArea? area = TryGameConfigProvider.GetHomeArea(homeAreaId);
                if (!area.HasValue)
                {
                    Debug.LogError(
                        $"[TryGameHomeAreaDebugUnlockWindow] HomeArea 配置不存在，不能修改解锁状态：" +
                        $"homeAreaId={homeAreaId}");
                    valid = false;
                    continue;
                }

                if (area.Value.WorldZoneId != homeWorldZoneId)
                {
                    Debug.LogError(
                        $"[TryGameHomeAreaDebugUnlockWindow] HomeArea 不属于当前 Home WorldZone：" +
                        $"homeAreaId={homeAreaId}, areaWorldZoneId={area.Value.WorldZoneId}, " +
                        $"currentHomeWorldZoneId={homeWorldZoneId}");
                    valid = false;
                }
            }

            return valid;
        }
    }
}
