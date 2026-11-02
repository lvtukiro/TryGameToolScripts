#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.BattlePreparationEditorUiFactory;

namespace Game.EditorTools
{
    /// <summary>只增量补齐备战入口和战斗 HUD，不重建场景或其它预制体。</summary>
    [InitializeOnLoad]
    public static class BattleSandboxUiInstaller
    {
        private const string HudPath = "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_world_zone.prefab";
        private const string PreparationPath = "Assets/Resources/TryGameBuildRes/gui/ui_game/win_battle_preparation.prefab";
        static BattleSandboxUiInstaller() { EditorApplication.delayCall += EnsureInstalled; }

        [MenuItem("TryGame/战斗/补齐游戏内临时战斗界面")]
        public static void EnsureInstalled()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (prefab == null) return;
            if (prefab.GetComponent<GUIMonoBattleWorldZone>()?.sandboxHud == null)
            {
                var root = PrefabUtility.LoadPrefabContents(HudPath);
                try { InstallHud(root); PrefabUtility.SaveAsPrefabAsset(root, HudPath); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PreparationPath);
            var preparation = prefab != null ? prefab.GetComponent<GUIMonoBattlePreparationMain>() : null;
            if (preparation?.battleDevelopmentButton != null &&
                (!preparation.battleDevelopmentButton.gameObject.activeSelf ||
                    preparation.battleDevelopmentButtonText?.text != "临时战斗"))
            {
                var root = PrefabUtility.LoadPrefabContents(PreparationPath);
                try
                {
                    preparation = root.GetComponent<GUIMonoBattlePreparationMain>();
                    preparation.battleDevelopmentButton.gameObject.SetActive(true);
                    preparation.battleDevelopmentButtonText.text = "临时战斗";
                    PrefabUtility.SaveAsPrefabAsset(root, PreparationPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        internal static void InstallHud(GameObject root)
        {
            var mono = root.GetComponent<GUIMonoBattleWorldZone>();
            if (mono == null || mono.sandboxHud != null) return;
            var panel = AddPanel("BattleSandboxControls", root.transform, PanelColor);
            Place(panel.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -120f), new Vector2(430f, 222f));
            var hud = panel.AddComponent<BattleSandboxHud>();
            var hero = AddButton("SelectHero", panel.transform, "主角：战士 ▸", fontSize: 19);
            Place(hero.Rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -12f), new Vector2(246f, 46f));
            var replace = AddButton("ReplaceHero", panel.transform, "切换 / 重置", fontSize: 19);
            Place(replace.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(150f, 46f));
            var enemy = AddButton("SelectEnemy", panel.transform, "敌人 ▸", fontSize: 19);
            Place(enemy.Rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -70f), new Vector2(246f, 46f));
            var add = AddButton("AddEnemy", panel.transform, "新增一只敌人", fontSize: 19);
            Place(add.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -70f), new Vector2(150f, 46f));
            var exit = AddButton("ExitSandbox", panel.transform, "返回备战", WarningColor, 19);
            Place(exit.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -128f), new Vector2(150f, 42f));
            var status = NewUiObject("Status", panel.transform);
            SetRect(status.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f),
                new Vector2(12f, 8f), new Vector2(-12f, 42f));
            hud.selectHeroButton = hero.Button; hud.heroText = hero.Text;
            hud.replaceHeroButton = replace.Button;
            hud.selectEnemyButton = enemy.Button; hud.enemyText = enemy.Text;
            hud.addEnemyButton = add.Button; hud.exitButton = exit.Button;
            hud.statusText = AddText(status, "临时战斗 · 不保存 · 切换主角会重置战局", 16);
            mono.sandboxHud = hud;
            panel.SetActive(false);
        }
    }
}
#endif
