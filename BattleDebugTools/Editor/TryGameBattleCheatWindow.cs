using Game;
using UnityEditor;
using UnityEngine;

namespace TryGame.BattleDebugTools.Editor
{
    public sealed class TryGameBattleCheatWindow : EditorWindow
    {
        private string enemyIdText = "102";
        private string leftWeaponIdText = "0";
        private string rightWeaponIdText = "0";
        private string equipmentIdText = "4102";
        private string commandStatus = "尚未执行战斗作弊命令。";
        private MessageType commandStatusType = MessageType.None;
        private Vector2 scrollPosition;
        /// <summary>
        /// 打开当前 `TryGameBattleCheatWindow` 实例。
        /// </summary>

        [MenuItem("TryGame/战斗/运行时战斗作弊工具")]
        public static void Open()
        {
            TryGameBattleCheatWindow window =
                GetWindow<TryGameBattleCheatWindow>("战斗作弊工具");
            window.minSize = new Vector2(420f, 300f);
            window.Show();
        }
        /// <summary>
        /// 响应检视面板更新事件，并同步当前组件状态。
        /// </summary>

        private void OnInspectorUpdate()
        {
            Repaint();
        }
        /// <summary>
        /// 绘制编辑器窗口内容并处理当前交互。
        /// </summary>

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("运行时战斗作弊", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "进入正式战局后使用。新增敌人只追加这一只，不重建场景、不重置玩家和已有敌人的状态。" +
                "生成的敌人和装备会进入当前战局及后续存档，建议使用测试存档。",
                MessageType.Info);

            BattleWorldZoneSceneRuntime runtime =
                BattleWorldZoneSceneRuntime.ActiveInstance;
            if (runtime == null || !runtime.IsSceneApplied || !runtime.HasActiveRun)
            {
                EditorGUILayout.HelpBox(
                    "当前没有已应用的 Battle WorldZone 战局。请先进入战场。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "当前战局",
                    $"WorldZone={runtime.CurrentWorldZoneId}, " +
                    $"SmallArea={runtime.Presentation?.ActiveSmallAreaId ?? 0}, " +
                    $"Revision={runtime.CheckpointRevision}");
            }

            DrawEnemySpawnSection(runtime);
            DrawPlayerEquipmentSection(runtime);
            DrawPlayerNoDamageSection(runtime);
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(commandStatus, commandStatusType);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 绘制正式战局的装备生成命令。
        /// </summary>
        private void DrawPlayerEquipmentSection(BattleWorldZoneSceneRuntime runtime)
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("生成指定装备（正式战场）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "使用 RobotEquipment.id（不是 Item.id、技能 id 或动画 id）。" +
                "生成在玩家脚下，按 X 拾取、B 打开背包后正常穿戴并装配技能。" +
                "不会覆盖已有装备；背包满时装备留在地面。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "会修改当前正式战局；后续保存/撤离会按正常规则处理这些装备。建议使用测试存档。",
                MessageType.Warning);

            equipmentIdText = EditorGUILayout.TextField("装备 id", equipmentIdText);

            using (new EditorGUI.DisabledScope(
                !EditorApplication.isPlaying || EditorApplication.isPaused || runtime == null ||
                !runtime.IsSceneApplied || !runtime.HasActiveRun))
            {
                if (GUILayout.Button("生成装备到玩家脚下"))
                {
                    RunGenerateEquipmentCommand(runtime);
                }
            }
        }
        /// <summary>
        /// 绘制敌人生成区段。
        /// </summary>

        private void DrawEnemySpawnSection(BattleWorldZoneSceneRuntime runtime)
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("新增敌人（正式战场）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "enemyId 使用 BattleEnemy.id；左右武器使用 RobotEquipment.id。0 表示空手。" +
                "双手武器只填写一个武器格即可，另一格会按正式规则保持为空。" +
                "敌人生成在玩家附近的合法生成区，使用配置的行为图和动作。",
                MessageType.None);

            if (runtime != null && runtime.IsSceneApplied) DrawEnemySelector();
            enemyIdText = EditorGUILayout.TextField("敌人 id", enemyIdText);
            leftWeaponIdText = EditorGUILayout.TextField("左手武器 id", leftWeaponIdText);
            rightWeaponIdText = EditorGUILayout.TextField("右手武器 id", rightWeaponIdText);

            using (new EditorGUI.DisabledScope(
                !EditorApplication.isPlaying || EditorApplication.isPaused || runtime == null ||
                !runtime.IsSceneApplied || !runtime.HasActiveRun))
            {
                if (GUILayout.Button("新增一只敌人"))
                {
                    RunSpawnEnemyCommand(runtime);
                }
            }

        }

        private void DrawEnemySelector()
        {
            int count = RefData.BattleEnemyTable.Count;
            var labels = new string[count + 1];
            var ids = new int[count + 1];
            labels[0] = "手动填写 id";
            int.TryParse(enemyIdText, out int currentId);
            int selected = 0;
            for (int i = 0; i < count; i++)
            {
                var enemy = RefData.BattleEnemyTable.BattleEnemys(i);
                ids[i + 1] = enemy.Id;
                string name = string.IsNullOrEmpty(enemy.NameLanguageKey) ? enemy.CodeName :
                    Lang.Get(enemy.NameLanguageKey);
                if (string.IsNullOrWhiteSpace(name) || name == enemy.NameLanguageKey)
                    name = enemy.CodeName;
                labels[i + 1] = $"{enemy.Id} · {name}";
                if (enemy.Id == currentId) selected = i + 1;
            }
            int next = EditorGUILayout.Popup("按配置选择", selected, labels);
            if (next != selected && next > 0) enemyIdText = ids[next].ToString();
        }
        /// <summary>
        /// 绘制玩家无伤害区段。
        /// </summary>

        private void DrawPlayerNoDamageSection(BattleWorldZoneSceneRuntime runtime)
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("玩家受击测试", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "启用后只抑制玩家的负生命变化；不影响敌人、不影响资源扣除，也不会写入存档。",
                MessageType.None);

            bool enabled = BattleWorldZoneSceneRuntime.PlayerNoDamageCheatEnabled;
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || runtime == null))
            {
                bool next = EditorGUILayout.Toggle("玩家受击不掉血", enabled);
                if (next != enabled)
                {
                    BattleWorldZoneSceneRuntime.PlayerNoDamageCheatEnabled = next;
                    commandStatus = next
                        ? "已启用玩家受击不掉血。"
                        : "已关闭玩家受击不掉血。";
                    commandStatusType = MessageType.Info;
                }
            }

            if (runtime == null || !runtime.IsSceneApplied)
            {
                EditorGUILayout.LabelField(
                    "未处于有效战场时，开关不可用。",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                "直接加满玩家架势，走正常的动作打断、击破动画和恢复计时；不扣血、不改技能配置。" +
                "用于测试击破动画和恢复流程。",
                MessageType.None);
            using (new EditorGUI.DisabledScope(
                !EditorApplication.isPlaying || EditorApplication.isPaused || runtime == null ||
                !runtime.IsSceneApplied || !runtime.HasActiveRun))
            {
                if (GUILayout.Button("触发玩家架势击破"))
                {
                    if (runtime.TryTriggerPlayerPostureBreakCheat(out string error))
                    {
                        SetCommandStatus("已触发玩家架势击破，将按 General 配置自动恢复。", MessageType.Info);
                    }
                    else
                    {
                        SetCommandStatus("触发失败：" + error, MessageType.Warning);
                    }
                }
            }
        }
        /// <summary>
        /// 运行生成敌人命令。
        /// </summary>

        private void RunSpawnEnemyCommand(BattleWorldZoneSceneRuntime runtime)
        {
            if (!EditorApplication.isPlaying)
            {
                SetCommandStatus("请先进入 Play Mode。", MessageType.Warning);
                return;
            }

            if (runtime == null)
            {
                SetCommandStatus("当前没有 BattleWorldZoneSceneRuntime。", MessageType.Warning);
                return;
            }

            if (!TryParseInt(enemyIdText, "敌人 id", false, out int enemyId)
                || !TryParseInt(leftWeaponIdText, "左手武器 id", true, out int leftWeaponId)
                || !TryParseInt(rightWeaponIdText, "右手武器 id", true, out int rightWeaponId))
            {
                return;
            }

            if (runtime.TrySpawnEnemyCheat(
                    enemyId,
                    leftWeaponId,
                    rightWeaponId,
                    out string error))
            {
                SetCommandStatus(
                    $"生成成功：enemyId={enemyId}, left={leftWeaponId}, right={rightWeaponId}。",
                    MessageType.Info);
            }
            else
            {
                SetCommandStatus("生成失败：" + error, MessageType.Error);
            }
        }

        /// <summary>
        /// 运行正式战局的装备生成命令。
        /// </summary>
        private void RunGenerateEquipmentCommand(BattleWorldZoneSceneRuntime runtime)
        {
            if (!EditorApplication.isPlaying)
            {
                SetCommandStatus("请先进入 Play Mode。", MessageType.Warning);
                return;
            }

            if (runtime == null)
            {
                SetCommandStatus("当前没有 BattleWorldZoneSceneRuntime。", MessageType.Warning);
                return;
            }

            if (!TryParseInt(equipmentIdText, "装备 id", false, out int equipmentId))
            {
                return;
            }

            if (runtime.TryGenerateEquipmentCheat(
                    equipmentId,
                    out string itemUid,
                    out string error))
            {
                SetCommandStatus(
                    $"已生成到脚下：equipmentId={equipmentId}, itemUid={itemUid}。" +
                    "按 X 拾取，B 打开背包换装，再将技能拖入 Brain 槽。",
                    MessageType.Info);
            }
            else
            {
                SetCommandStatus("生成装备失败：" + error, MessageType.Error);
            }
        }
        /// <summary>
        /// 根据 `text`、`fieldName`、`allowZero` 解析整数；成功时写出 `value` 并返回 `true`，前置条件不满足时返回 `false`。
        /// </summary>

        private bool TryParseInt(
            string text,
            string fieldName,
            bool allowZero,
            out int value)
        {
            value = 0;
            if (!int.TryParse(text, out value)
                || (allowZero ? value < 0 : value <= 0))
            {
                SetCommandStatus(
                    $"{fieldName} 必须是{(allowZero ? "大于等于 0" : "大于 0")}的整数。",
                    MessageType.Warning);
                return false;
            }

            return true;
        }
        /// <summary>
        /// 设置命令状态。
        /// </summary>

        private void SetCommandStatus(string message, MessageType type)
        {
            commandStatus = message ?? string.Empty;
            commandStatusType = type;
        }
    }
}
