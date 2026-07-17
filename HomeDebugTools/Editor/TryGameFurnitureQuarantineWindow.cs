using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEngine;

namespace TryGame.HomeDebugTools.Editor
{
    /// <summary>
    /// 只读查看当前运行存档中因配置缺失而隔离、不会展示给玩家的家具记录。
    /// </summary>
    public sealed class TryGameFurnitureQuarantineWindow : EditorWindow
    {
        private Vector2 scrollPosition;

        [MenuItem("TryGame/Home/家具隐藏隔离区查看器")]
        public static void Open()
        {
            TryGameFurnitureQuarantineWindow window = GetWindow<TryGameFurnitureQuarantineWindow>("家具隔离区");
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
            EditorGUILayout.LabelField("当前运行存档的隐藏家具隔离区", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这里仅显示配置不存在或配置无法解析的家具。工具不会把记录暴露给玩家，也不会修改、恢复或删除存档数据。",
                MessageType.Info);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("请进入 Play Mode 并加载存档后查看。", MessageType.Warning);
                return;
            }

            SaveData save = SaveRuntime.Instance != null ? SaveRuntime.Instance.Current : null;
            if (save == null || save.furniture == null)
            {
                EditorGUILayout.HelpBox("当前没有已加载的运行存档或家具数据。", MessageType.Warning);
                return;
            }

            List<QuarantinedFurnitureData> quarantine = save.furniture.quarantine;
            int count = quarantine != null ? quarantine.Count : 0;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"隔离记录：{count}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("输出全部到 Console", GUILayout.Width(150f)))
            {
                LogAll(quarantine, save.slotId);
            }
            EditorGUILayout.EndHorizontal();

            if (count == 0)
            {
                EditorGUILayout.HelpBox("当前存档没有隐藏隔离家具。", MessageType.None);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < quarantine.Count; i++)
            {
                DrawRecord(i, quarantine[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawRecord(int index, QuarantinedFurnitureData item)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (item == null)
            {
                EditorGUILayout.HelpBox($"记录 {index + 1} 为空；存档规范化时应修复此项，请检查相关错误日志。", MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField($"#{index + 1}  Furniture {item.furnitureId} × {item.count}", EditorStyles.boldLabel);
            DrawSelectable("隔离 ID", item.quarantineId);
            DrawSelectable("来源 / 原因", $"{item.source} / {item.reason}");
            DrawSelectable("原 UID", item.originalUid);
            DrawSelectable("原区域 / 坐标", $"area={item.homeAreaId}, grid=({item.gridX},{item.gridY})");
            DrawSelectable("发现时版本 / 游戏时长", $"saveVersion={item.detectedSaveVersion}, gameTime={item.detectedGameTimeSeconds:F2}s");
            DrawSelectable("详情", item.details);
            EditorGUILayout.EndVertical();
        }

        private static void DrawSelectable(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(130f));
            EditorGUILayout.SelectableLabel(value ?? string.Empty, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private static void LogAll(List<QuarantinedFurnitureData> quarantine, string slotId)
        {
            if (quarantine == null || quarantine.Count == 0)
            {
                Debug.LogWarning($"[TryGameFurnitureQuarantineWindow] 当前存档没有隐藏隔离家具：slotId={slotId ?? "<none>"}");
                return;
            }

            for (int i = 0; i < quarantine.Count; i++)
            {
                QuarantinedFurnitureData item = quarantine[i];
                if (item == null)
                {
                    Debug.LogError($"[TryGameFurnitureQuarantineWindow] 隐藏隔离记录为空：slotId={slotId ?? "<none>"}, index={i}");
                    continue;
                }

                Debug.LogWarning(
                    $"[TryGameFurnitureQuarantineWindow] 隔离家具：slotId={slotId ?? "<none>"}, index={i}, " +
                    $"quarantineId={item.quarantineId ?? "<null>"}, furnitureId={item.furnitureId}, count={item.count}, " +
                    $"source={item.source}, reason={item.reason}, originalUid={item.originalUid ?? "<null>"}, " +
                    $"area={item.homeAreaId}, grid=({item.gridX},{item.gridY}), saveVersion={item.detectedSaveVersion}, " +
                    $"gameTime={item.detectedGameTimeSeconds:F2}, details={item.details ?? "<none>"}");
            }
        }
    }
}
