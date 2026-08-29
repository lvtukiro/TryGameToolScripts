using Game;
using UnityEditor;
using UnityEngine;

namespace TryGame.BattleDebugTools.Editor
{
    /// <summary>
    /// Battle WorldZone play-mode cheat entry.  Commands are deliberately routed through the
    /// scene runtime instead of editing scene objects directly, so checkpoint, Robot equipment
    /// and combat-runtime validation remain identical to normal gameplay.
    /// </summary>
    public sealed class TryGameBattleCheatWindow : EditorWindow
    {
        private string enemyIdText = "102";
        private string leftWeaponIdText = "0";
        private string rightWeaponIdText = "0";
        private string commandStatus = "尚未执行战斗作弊命令。";
        private MessageType commandStatusType = MessageType.None;
        private Vector2 scrollPosition;

        [MenuItem("TryGame/Battle/运行时战斗作弊工具")]
        public static void Open()
        {
            TryGameBattleCheatWindow window =
                GetWindow<TryGameBattleCheatWindow>("战斗作弊工具");
            window.minSize = new Vector2(420f, 300f);
            window.Show();
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("运行时战斗作弊", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只在 Play Mode 下生效。生成敌人会写入当前 ActiveRun，随后按正常 SmallArea 流程重建，" +
                "因此属性、装备、技能和序列帧动作都会走正式代码。",
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
            DrawPlayerNoDamageSection(runtime);
            EditorGUILayout.EndScrollView();
        }

        private void DrawEnemySpawnSection(BattleWorldZoneSceneRuntime runtime)
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("生成指定敌人", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "enemyId 使用 BattleEnemy.id；左右武器使用 RobotEquipment.id。0 表示空手。" +
                "双手武器只填写一个武器格即可，另一格会按正式规则保持为空。",
                MessageType.None);

            enemyIdText = EditorGUILayout.TextField("敌人 id", enemyIdText);
            leftWeaponIdText = EditorGUILayout.TextField("左手武器 id", leftWeaponIdText);
            rightWeaponIdText = EditorGUILayout.TextField("右手武器 id", rightWeaponIdText);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("生成敌人"))
                {
                    RunSpawnEnemyCommand(runtime);
                }
            }

            EditorGUILayout.HelpBox(commandStatus, commandStatusType);
        }

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
        }

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

        private void SetCommandStatus(string message, MessageType type)
        {
            commandStatus = message ?? string.Empty;
            commandStatusType = type;
        }
    }
}
