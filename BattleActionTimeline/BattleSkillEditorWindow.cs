#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.SequenceFrameAnimation;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 独立的动画接入与技能配置工具。
    /// 动作时间轴只负责动作和攻击体；这里负责 BattleSkill、ActiveSingle、角色技能入口，
    /// 以及 Idle/Move/Hitstun/Dead 等 BattleCharacterAction 绑定。
    /// </summary>
    public sealed class BattleSkillEditorWindow : EditorWindow
    {
        private const string SkillSheet = "BattleSkill";
        private const string SingleSheet = "BattleActiveSingleSkill";
        private const string CharacterActionSheet = "BattleCharacterAction";
        private const string RobotSheet = "BattleRobot";
        private const float ListWidth = 330f;

        private enum EditorMode
        {
            Skills,
            CharacterActions,
        }

        private sealed class CharacterActionBinding
        {
            public string MatchType = "CharacterDefault";
            public int CharacterId;
            public int LeftWeaponId;
            public int RightWeaponId;
            public int ActionId;
        }

        private static readonly RefData.EnumBattleCharacterActionState[] ActionStates =
            Enum.GetValues(typeof(RefData.EnumBattleCharacterActionState))
                .Cast<RefData.EnumBattleCharacterActionState>()
                .Where(value => value != RefData.EnumBattleCharacterActionState.Invalid &&
                    value != RefData.EnumBattleCharacterActionState.None)
                .ToArray();

        private BattleActionTimelineWorkbookSnapshot baseline;
        private BattleActionTimelineDocument working;
        private Vector2 listScroll;
        private Vector2 detailScroll;
        private string search = string.Empty;
        private int selectedSkillId;
        private int newActionId;
        private string newCodeName = string.Empty;
        private string newNameKey = string.Empty;
        private int attachRobotId;
        private string status = "尚未读取技能源表。";
        private bool dirty;
        private bool newGrounded = true;
        private bool newAirborne;
        private EditorMode editorMode;
        private string actionSearch = string.Empty;
        private int selectedCharacterActionId;
        private int actionCharacterId;
        private int actionLeftWeaponId;
        private int actionRightWeaponId;
        private int actionId;
        private int actionStateIndex;
        private int actionMatchTypeIndex = 1;

        [MenuItem("TryGame/战斗/动画与技能配置编辑器", false, 434)]
        private static void Open()
        {
            BattleSkillEditorWindow window = GetWindow<BattleSkillEditorWindow>();
            window.titleContent = new GUIContent("动画与技能配置");
            window.minSize = new Vector2(1120f, 680f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadOfficial(false);
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (working == null)
            {
                EditorGUILayout.HelpBox(status, MessageType.Warning);
                return;
            }

            if (editorMode == EditorMode.CharacterActions)
            {
                DrawCharacterActions();
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawSkillList();
            DrawDetails();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("重新读取", EditorStyles.toolbarButton, GUILayout.Width(78f)))
            {
                if (!dirty || EditorUtility.DisplayDialog("放弃未保存动画/技能修改？",
                    "当前动画或技能草稿尚未写回，重新读取会丢弃修改。", "重新读取", "取消"))
                    LoadOfficial(true);
            }

            using (new EditorGUI.DisabledScope(!dirty))
            {
                if (GUILayout.Button("安全写回源 xlsx", EditorStyles.toolbarButton, GUILayout.Width(112f)))
                    WriteOfficial(false);
                if (GUILayout.Button("写回并导表", EditorStyles.toolbarButton, GUILayout.Width(92f)))
                    WriteOfficial(true);
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("技能配置", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                editorMode = EditorMode.Skills;
            if (GUILayout.Button("角色动作", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                editorMode = EditorMode.CharacterActions;

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(dirty ? "● 草稿未写回" : "✓ 与读取基线一致",
                EditorStyles.toolbarButton, GUILayout.Width(130f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "本工具只修改 r.机器人表.xlsx；写回前会检查源表 hash、临时文件回读和引用完整性，不生成持久备份。" +
                "动作帧和攻击体仍在动作时间轴编辑器中配置；角色动作页用于绑定待机、移动、受击、死亡、跳跃等 Action ID。",
                MessageType.Info);
        }

        private void DrawCharacterActions()
        {
            BattleActionTimelineTableData table = working.FindTable(CharacterActionSheet);
            if (table == null)
            {
                EditorGUILayout.HelpBox("源表缺少 BattleCharacterAction，无法绑定待机/移动/受击等角色动作。", MessageType.Error);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawCharacterActionList(table);
            DrawCharacterActionDetails(table);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCharacterActionList(BattleActionTimelineTableData table)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("角色动作列表", EditorStyles.boldLabel);
            if (GUILayout.Button("清空筛选", EditorStyles.miniButton, GUILayout.Width(62f)))
                actionSearch = string.Empty;
            EditorGUILayout.EndHorizontal();
            actionSearch = EditorGUILayout.TextField(
                new GUIContent("搜索", "按动作类型、codeName 或状态 ID 筛选。"), actionSearch);
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox("这里绑定待机、移动、跳跃、受击、死亡等普通动作；技能动作仍由技能配置页关联。",
                MessageType.Info);

            IEnumerable<BattleActionTimelineRecordData> rows = table.records.Where(row => row != null);
            string filter = (actionSearch ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(filter))
            {
                rows = rows.Where(row =>
                    row.rowId.ToString(CultureInfo.InvariantCulture).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    BattleActionTimelineSchema.Get(table, row, "codeName").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    ActionStateName(row.rowId).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            foreach (BattleActionTimelineRecordData row in rows.OrderBy(value => value.rowId))
            {
                string label = row.rowId.ToString(CultureInfo.InvariantCulture) + " · " +
                    ActionStateName(row.rowId) + " · " +
                    BattleActionTimelineSchema.Get(table, row, "codeName");
                Color previous = GUI.backgroundColor;
                if (row.rowId == selectedCharacterActionId)
                    GUI.backgroundColor = new Color(0.35f, 0.72f, 1f, 1f);
                if (GUILayout.Button(label, EditorStyles.miniButton))
                {
                    selectedCharacterActionId = row.rowId;
                    SyncActionStateSelection(row.rowId);
                }
                GUI.backgroundColor = previous;
            }
            if (!rows.Any())
                EditorGUILayout.HelpBox("没有符合筛选条件的角色动作行。可在右侧选择动作类型后直接创建。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCharacterActionDetails(BattleActionTimelineTableData table)
        {
            EditorGUILayout.BeginVertical();
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            int selectedState = StateIndexForId(selectedCharacterActionId);
            int nextState = EditorGUILayout.Popup(
                new GUIContent("动作类型", "动作类型对应 BattleCharacterAction 的行 ID。"),
                selectedState,
                ActionStates.Select(value => ActionStateName((int)value)).ToArray());
            if (nextState != selectedState)
            {
                actionStateIndex = nextState;
                selectedCharacterActionId = (int)ActionStates[nextState];
            }
            else
            {
                actionStateIndex = selectedState;
            }

            int stateId = (int)ActionStates[Mathf.Clamp(actionStateIndex, 0, ActionStates.Length - 1)];
            selectedCharacterActionId = stateId;
            BattleActionTimelineRecordData row = working.FindRecord(CharacterActionSheet, stateId);
            EditorGUILayout.LabelField("动作映射 · " + ActionStateName(stateId) + " / " + stateId,
                EditorStyles.boldLabel);
            if (row != null)
                EditorGUILayout.LabelField("codeName", BattleActionTimelineSchema.Get(table, row, "codeName"));
            else
                EditorGUILayout.HelpBox("这个动作类型当前还没有配置行；点击下方“新增/更新绑定”会自动创建。",
                    MessageType.Info);

            string[] matchTypeNames = { "ExactCombination", "CharacterDefault", "GlobalDefault" };
            string[] matchTypeLabels = { "精确装备组合", "角色默认（空手）", "全局默认" };
            actionMatchTypeIndex = EditorGUILayout.Popup("匹配方式", Mathf.Clamp(actionMatchTypeIndex, 0, 2), matchTypeLabels);
            string matchType = matchTypeNames[actionMatchTypeIndex];
            actionCharacterId = DrawCharacterId(actionCharacterId);
            if (matchType == "ExactCombination")
            {
                actionLeftWeaponId = EditorGUILayout.IntField("左手装备 ID", actionLeftWeaponId);
                actionRightWeaponId = EditorGUILayout.IntField("右手装备 ID", actionRightWeaponId);
            }
            else
            {
                actionLeftWeaponId = 0;
                actionRightWeaponId = 0;
                if (matchType == "GlobalDefault") actionCharacterId = 0;
            }
            actionId = EditorGUILayout.IntField(new GUIContent("动画 Action ID", "导出序列帧后填写的 Action ID。"), actionId);
            if (GUILayout.Button("读取动画信息"))
            {
                if (TryFindClip(actionId, out SequenceFrameClip clip))
                    status = $"已找到动作 {actionId}：{clip.FrameCount} 帧 / {clip.FrameRate:0.###} FPS。";
                else
                    status = "未找到对应 SequenceFrameClip；仍可绑定 Action ID。";
            }
            using (new EditorGUI.DisabledScope(actionId <= 0 ||
                (matchType == "CharacterDefault" && actionCharacterId <= 0)))
            {
                if (GUILayout.Button("新增/更新角色动作绑定", GUILayout.Height(28f)))
                    UpsertCharacterAction(stateId, matchType, actionCharacterId,
                        actionLeftWeaponId, actionRightWeaponId, actionId);
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("当前动作已有绑定", EditorStyles.boldLabel);
            string bindingError = string.Empty;
            if (row != null && TryReadCharacterActionBindings(table, row,
                out List<CharacterActionBinding> bindings, out bindingError))
            {
                if (bindings.Count == 0)
                    EditorGUILayout.LabelField("暂无绑定。", EditorStyles.miniLabel);
                for (int index = 0; index < bindings.Count; index++)
                {
                    CharacterActionBinding binding = bindings[index];
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    EditorGUILayout.LabelField(
                        $"{DisplayMatchType(binding.MatchType)} 角色 {binding.CharacterId} / " +
                        $"装备 {binding.LeftWeaponId},{binding.RightWeaponId} → Action {binding.ActionId}",
                        EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("编辑", GUILayout.Width(48f)))
                        LoadCharacterActionBinding(binding);
                    if (GUILayout.Button("删除", GUILayout.Width(48f)))
                    {
                        RemoveCharacterActionBinding(stateId, binding);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndScrollView();
                        EditorGUILayout.EndVertical();
                        return;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            else if (row != null)
            {
                EditorGUILayout.HelpBox("entries 解析失败：" + bindingError, MessageType.Error);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "CharacterDefault 适合给某个角色配置空手待机/移动/受击/死亡；" +
                "ExactCombination 适合区分武器组合；GlobalDefault 是所有角色的兜底动作。",
                MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void LoadCharacterActionBinding(CharacterActionBinding binding)
        {
            actionCharacterId = binding.CharacterId;
            actionLeftWeaponId = binding.LeftWeaponId;
            actionRightWeaponId = binding.RightWeaponId;
            actionId = binding.ActionId;
            actionMatchTypeIndex = Array.IndexOf(
                new[] { "ExactCombination", "CharacterDefault", "GlobalDefault" },
                NormalizeMatchType(binding.MatchType));
            if (actionMatchTypeIndex < 0) actionMatchTypeIndex = 1;
            status = "已载入这条角色动作绑定，可修改后点击“新增/更新角色动作绑定”。";
            Repaint();
        }

        private int DrawCharacterId(int current)
        {
            BattleActionTimelineTableData robots = working.FindTable(RobotSheet);
            List<int> ids = robots?.records.Select(row => row.rowId).Where(id => id > 0)
                .Distinct().OrderBy(id => id).ToList() ?? new List<int>();
            if (current != 0 && !ids.Contains(current)) ids.Add(current);
            ids = ids.Distinct().OrderBy(id => id).ToList();
            if (ids.Count == 0) return EditorGUILayout.IntField("角色 ID", current);
            string[] labels = ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            int popup = EditorGUILayout.IntPopup("角色 ID（选择）", current, labels, ids.ToArray());
            return EditorGUILayout.IntField("角色 ID（最终值）", popup);
        }

        private void SyncActionStateSelection(int stateId)
        {
            int index = StateIndexForId(stateId);
            actionStateIndex = index;
        }

        private static int StateIndexForId(int stateId)
        {
            int index = Array.FindIndex(ActionStates, value => (int)value == stateId);
            return index >= 0 ? index : 0;
        }

        private void UpsertCharacterAction(int stateId, string matchType, int characterId,
            int leftWeaponId, int rightWeaponId, int nextActionId)
        {
            if (nextActionId <= 0)
            {
                status = "动画 Action ID 必须大于 0。";
                return;
            }
            if (matchType == "CharacterDefault" && characterId <= 0)
            {
                status = "角色默认绑定必须填写有效角色 ID。";
                return;
            }
            if (matchType == "GlobalDefault") characterId = leftWeaponId = rightWeaponId = 0;
            if (matchType == "CharacterDefault") leftWeaponId = rightWeaponId = 0;

            BattleActionTimelineDocument edited = working.Clone();
            BattleActionTimelineTableData table = edited.FindTable(CharacterActionSheet);
            if (table == null) return;
            BattleActionTimelineRecordData row = edited.FindRecord(CharacterActionSheet, stateId);
            if (row == null)
            {
                row = NewRow(table, stateId);
                SetCell(table, row, "codeName",
                    ActionStates.First(value => (int)value == stateId).ToString());
                SetCell(table, row, "entries", "[]");
                table.records.Add(row);
            }

            if (!TryReadCharacterActionBindings(table, row,
                out List<CharacterActionBinding> bindings, out string error))
            {
                status = "entries 解析失败，未覆盖原有绑定：" + error;
                return;
            }

            CharacterActionBinding existing = bindings.FirstOrDefault(value =>
                string.Equals(value.MatchType, matchType, StringComparison.Ordinal) &&
                value.CharacterId == characterId && value.LeftWeaponId == leftWeaponId &&
                value.RightWeaponId == rightWeaponId);
            if (existing == null)
            {
                bindings.Add(new CharacterActionBinding
                {
                    MatchType = matchType,
                    CharacterId = characterId,
                    LeftWeaponId = leftWeaponId,
                    RightWeaponId = rightWeaponId,
                    ActionId = nextActionId,
                });
            }
            else
            {
                existing.ActionId = nextActionId;
            }
            SetCell(table, row, "entries", SerializeCharacterActionBindings(bindings));
            working = edited;
            selectedCharacterActionId = stateId;
            dirty = true;
            status = $"{ActionStateName(stateId)} 已绑定 Action {nextActionId}；尚未写回源表。";
            GUI.changed = true;
        }

        private void RemoveCharacterActionBinding(int stateId, CharacterActionBinding binding)
        {
            BattleActionTimelineDocument edited = working.Clone();
            BattleActionTimelineTableData table = edited.FindTable(CharacterActionSheet);
            BattleActionTimelineRecordData row = edited.FindRecord(CharacterActionSheet, stateId);
            if (table == null || row == null ||
                !TryReadCharacterActionBindings(table, row,
                    out List<CharacterActionBinding> bindings, out _))
                return;
            bindings.RemoveAll(value => CharacterActionBindingEquals(value, binding));
            SetCell(table, row, "entries", SerializeCharacterActionBindings(bindings));
            working = edited;
            dirty = true;
            status = $"{ActionStateName(stateId)} 已删除一条动作绑定；尚未写回源表。";
            GUI.changed = true;
        }

        private static bool TryReadCharacterActionBindings(BattleActionTimelineTableData table,
            BattleActionTimelineRecordData row, out List<CharacterActionBinding> bindings, out string error)
        {
            bindings = new List<CharacterActionBinding>();
            if (!BattleActionTimelineInlineStructCodec.TryParse(
                    BattleActionTimelineSchema.Get(table, row, "entries"),
                    out List<Dictionary<string, string>> values, out error))
                return false;
            foreach (Dictionary<string, string> value in values)
            {
                bindings.Add(new CharacterActionBinding
                {
                    MatchType = NormalizeMatchType(GetValue(value, "MatchType", "CharacterDefault")),
                    CharacterId = ParseInt(GetValue(value, "CharacterId", "0")),
                    LeftWeaponId = ParseInt(GetValue(value, "LeftWeaponId", "0")),
                    RightWeaponId = ParseInt(GetValue(value, "RightWeaponId", "0")),
                    ActionId = ParseInt(GetValue(value, "ActionId", "0")),
                });
            }
            return true;
        }

        private static bool CharacterActionBindingEquals(CharacterActionBinding first,
            CharacterActionBinding second) =>
            first != null && second != null &&
            string.Equals(first.MatchType, second.MatchType, StringComparison.Ordinal) &&
            first.CharacterId == second.CharacterId &&
            first.LeftWeaponId == second.LeftWeaponId &&
            first.RightWeaponId == second.RightWeaponId &&
            first.ActionId == second.ActionId;

        private static string SerializeCharacterActionBindings(
            IEnumerable<CharacterActionBinding> bindings)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder("[");
            bool first = true;
            foreach (CharacterActionBinding binding in bindings)
            {
                if (!first) text.Append(',');
                first = false;
                text.Append("{\"MatchType\":").Append(binding.MatchType)
                    .Append(",\"CharacterId\":").Append(
                        binding.CharacterId.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"LeftWeaponId\":").Append(
                        binding.LeftWeaponId.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"RightWeaponId\":").Append(
                        binding.RightWeaponId.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"ActionId\":").Append(
                        binding.ActionId.ToString(CultureInfo.InvariantCulture))
                    .Append('}');
            }
            return text.Append(']').ToString();
        }

        private static string NormalizeMatchType(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "1": return "ExactCombination";
                case "2": return "CharacterDefault";
                case "3": return "GlobalDefault";
                case "ExactCombination": return "ExactCombination";
                case "CharacterDefault": return "CharacterDefault";
                case "GlobalDefault": return "GlobalDefault";
                default: return string.IsNullOrWhiteSpace(value) ? "CharacterDefault" : value.Trim();
            }
        }

        private static int ParseInt(string value)
        {
            return int.TryParse((value ?? string.Empty).Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : 0;
        }

        private static string GetValue(
            IReadOnlyDictionary<string, string> values, string key, string fallback)
        {
            return values != null && values.TryGetValue(key, out string value)
                ? value
                : fallback;
        }

        private static string DisplayMatchType(string value)
        {
            switch (NormalizeMatchType(value))
            {
                case "ExactCombination": return "精确组合";
                case "GlobalDefault": return "全局默认";
                default: return "角色默认";
            }
        }

        private static string ActionStateName(int stateId)
        {
            if (!Enum.IsDefined(typeof(RefData.EnumBattleCharacterActionState),
                (RefData.EnumBattleCharacterActionState)stateId))
                return "未知动作";
            switch ((RefData.EnumBattleCharacterActionState)stateId)
            {
                case RefData.EnumBattleCharacterActionState.Idle: return "待机 Idle";
                case RefData.EnumBattleCharacterActionState.Move: return "移动 Move";
                case RefData.EnumBattleCharacterActionState.Jump: return "跳跃 Jump";
                case RefData.EnumBattleCharacterActionState.Fall: return "下落 Fall";
                case RefData.EnumBattleCharacterActionState.Climb: return "攀爬 Climb";
                case RefData.EnumBattleCharacterActionState.Dash: return "闪避 Dash";
                case RefData.EnumBattleCharacterActionState.Skill: return "技能 Skill";
                case RefData.EnumBattleCharacterActionState.Hitstun: return "受击 Hitstun";
                case RefData.EnumBattleCharacterActionState.Dead: return "死亡 Dead";
                case RefData.EnumBattleCharacterActionState.PostureBreak: return "架势破坏 PostureBreak";
                case RefData.EnumBattleCharacterActionState.ExecutionAttacker: return "处决攻击者 ExecutionAttacker";
                case RefData.EnumBattleCharacterActionState.ExecutionVictim: return "处决受害者 ExecutionVictim";
                case RefData.EnumBattleCharacterActionState.ExecutionVictimDeath: return "处决死亡 ExecutionVictimDeath";
                case RefData.EnumBattleCharacterActionState.Rise: return "起身 Rise";
                case RefData.EnumBattleCharacterActionState.JumpStart: return "起跳 JumpStart";
                case RefData.EnumBattleCharacterActionState.JumpAir: return "空中 JumpAir";
                case RefData.EnumBattleCharacterActionState.JumpLand: return "落地 JumpLand";
                case RefData.EnumBattleCharacterActionState.AirHitstun: return "空中受击 AirHitstun";
                case RefData.EnumBattleCharacterActionState.AirHitRecovery: return "空中受击恢复 AirHitRecovery";
                default: return ((RefData.EnumBattleCharacterActionState)stateId).ToString();
            }
        }

        private void DrawSkillList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("技能列表", EditorStyles.boldLabel);
            if (GUILayout.Button("清空筛选", EditorStyles.miniButton, GUILayout.Width(62f)))
                search = string.Empty;
            EditorGUILayout.EndHorizontal();
            search = EditorGUILayout.TextField(new GUIContent("搜索", "按技能 ID、codeName 或技能类型筛选。"), search);

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("从导出动画新建单段技能", EditorStyles.boldLabel);
                newActionId = EditorGUILayout.IntField(new GUIContent("动画 Action ID", "例如 33；会绑定到新建 ActiveSingleSkill.actionId。"), newActionId);
                if (GUILayout.Button("读取动画信息"))
                {
                    if (TryFindClip(newActionId, out SequenceFrameClip clip))
                    {
                        newCodeName = string.IsNullOrEmpty(newCodeName) ? "Skill_" + newActionId : newCodeName;
                        status = $"已找到动作 {newActionId}：{clip.FrameCount} 帧 / {clip.FrameRate:0.###} FPS。";
                    }
                    else status = "未找到对应 SequenceFrameClip；仍可创建，但动作时长需要手动填写。";
                }
                newCodeName = EditorGUILayout.TextField("技能 codeName", newCodeName);
                newNameKey = EditorGUILayout.TextField("名称 LanguageKey", newNameKey);
                newGrounded = EditorGUILayout.ToggleLeft("允许地面释放", newGrounded);
                newAirborne = EditorGUILayout.ToggleLeft("允许空中释放", newAirborne);
                using (new EditorGUI.DisabledScope(newActionId <= 0 || string.IsNullOrWhiteSpace(newCodeName)))
                {
                    if (GUILayout.Button("+ 创建单段技能", GUILayout.Height(28f)))
                        CreateSingleSkill();
                }
            }

            List<BattleActionTimelineRecordData> rows = GetVisibleSkills();
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            foreach (BattleActionTimelineRecordData row in rows)
            {
                BattleActionTimelineTableData table = working.FindTable(SkillSheet);
                string label = row.rowId.ToString(CultureInfo.InvariantCulture) + " · " +
                    BattleActionTimelineSchema.Get(table, row, "codeName") + " · " +
                    BattleActionTimelineSchema.Get(table, row, "skillType");
                Color previous = GUI.backgroundColor;
                if (row.rowId == selectedSkillId) GUI.backgroundColor = new Color(0.35f, 0.72f, 1f, 1f);
                if (GUILayout.Button(label, EditorStyles.miniButton))
                    selectedSkillId = row.rowId;
                GUI.backgroundColor = previous;
            }
            if (rows.Count == 0) EditorGUILayout.HelpBox("没有符合筛选条件的技能。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private List<BattleActionTimelineRecordData> GetVisibleSkills()
        {
            BattleActionTimelineTableData table = working.FindTable(SkillSheet);
            string text = (search ?? string.Empty).Trim();
            IEnumerable<BattleActionTimelineRecordData> rows = table?.records ?? new List<BattleActionTimelineRecordData>();
            if (!string.IsNullOrEmpty(text))
                rows = rows.Where(row => row != null &&
                    (row.rowId.ToString(CultureInfo.InvariantCulture).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     BattleActionTimelineSchema.Get(table, row, "codeName").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     BattleActionTimelineSchema.Get(table, row, "skillType").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0));
            return rows.Where(row => row != null).OrderBy(row => row.rowId).ToList();
        }

        private void DrawDetails()
        {
            EditorGUILayout.BeginVertical();
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            BattleActionTimelineTableData skills = working.FindTable(SkillSheet);
            BattleActionTimelineRecordData skill = working.FindRecord(SkillSheet, selectedSkillId);
            if (skill == null)
            {
                EditorGUILayout.HelpBox("从左侧选择一个技能，或先从动画 ID 创建单段技能。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            BattleActionTimelineDocument edited = working.Clone();
            BattleActionTimelineTableData editedSkills = edited.FindTable(SkillSheet);
            BattleActionTimelineRecordData editedSkill = edited.FindRecord(SkillSheet, selectedSkillId);
            EditorGUI.BeginChangeCheck();
            DrawRootSkill(edited, editedSkills, editedSkill);
            EditorGUILayout.Space(10f);
            DrawCharacterAttach(edited, editedSkill);
            if (EditorGUI.EndChangeCheck())
            {
                working = edited;
                dirty = true;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRootSkill(BattleActionTimelineDocument document,
            BattleActionTimelineTableData table, BattleActionTimelineRecordData skill)
        {
            EditorGUILayout.LabelField("技能本体 · BattleSkill / " + skill.rowId, EditorStyles.boldLabel);
            SetTextField(table, skill, "codeName", "技能 codeName");
            SetTextField(table, skill, "nameLanguageKey", "名称 LanguageKey");
            SetTextField(table, skill, "descriptionLanguageKey", "描述 LanguageKey");
            SetIntField(table, skill, "iconResourceId", "图标资源 ID");

            string typeRaw = BattleActionTimelineSchema.Get(table, skill, "skillType");
            string[] types = { "ActiveSingle", "ActiveMulti", "MultiEquipment", "Passive" };
            int typeIndex = Math.Max(0, Array.IndexOf(types, typeRaw));
            int nextType = EditorGUILayout.Popup("技能类型", typeIndex, types);
            if (nextType != typeIndex) BattleActionTimelineSchema.Set(table, skill, types[nextType], "skillType");
            SetEnumField(table, skill, "targetType", "目标类型", new[] { "Self", "Friendly", "Enemy" });
            SetIntField(table, skill, "targetId", "技能目标配置 ID");
            SetDoubleField(table, skill, "baseCooldownSeconds", "基础 CD（秒）");

            string skillType = BattleActionTimelineSchema.Get(table, skill, "skillType");
            if (string.Equals(skillType, "ActiveSingle", StringComparison.OrdinalIgnoreCase))
            {
                BattleActionTimelineTableData singles = document.FindTable(SingleSheet);
                int singleId = BattleActionTimelineSchema.GetInt(table, skill, 0, "targetId");
                BattleActionTimelineRecordData single = document.FindRecord(SingleSheet, singleId);
                if (single == null)
                {
                    EditorGUILayout.HelpBox("targetId 未找到 ActiveSingle 配置，可输入已有 ID 或使用左侧创建流程。", MessageType.Error);
                    return;
                }
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("动作段 · BattleActiveSingleSkill / " + single.rowId, EditorStyles.boldLabel);
                SetTextField(singles, single, "codeName", "动作段 codeName");
                SetIntField(singles, single, "actionId", "动画 Action ID");
                DrawMovementStates(singles, single);
                SetEnumField(singles, single, "movementMode", "攻击期间移动", new[] { "Locked", "Horizontal", "Full" });
                SetDoubleField(singles, single, "startupEndTime", "前摇结束时间");
                SetDoubleField(singles, single, "actionSwitchWindowStartTime", "连段切换窗开始");
                SetDoubleField(singles, single, "recoveryStartTime", "后摇开始时间");
                SetDoubleField(singles, single, "actionDuration", "动作持续时间");
                SetDoubleField(singles, single, "stanceGain", "架势增量");
                SetDoubleField(singles, single, "attackRange", "攻击距离");
                SetDoubleField(singles, single, "movementOffsetX", "释放位移 X");
                SetDoubleField(singles, single, "movementOffsetY", "释放位移 Y");
                if (GUILayout.Button("按动画 Clip 自动填动作时长"))
                {
                    int actionId = BattleActionTimelineSchema.GetInt(singles, single, 0, "actionId");
                    if (TryFindClip(actionId, out SequenceFrameClip clip))
                    {
                        BattleActionTimelineSchema.SetDouble(singles, single,
                            clip.FrameCount / Math.Max(0.0001f, clip.FrameRate), "actionDuration");
                        if (BattleActionTimelineSchema.GetDouble(singles, single, 0d, "recoveryStartTime") <= 0d)
                            BattleActionTimelineSchema.SetDouble(singles, single,
                                clip.FrameCount / Math.Max(0.0001f, clip.FrameRate) * 0.75d, "recoveryStartTime");
                        status = $"已按动作 {actionId} 的 Clip 填入时长。";
                        GUI.changed = true;
                    }
                    else status = "未找到对应 SequenceFrameClip。";
                }
            }
            else
            {
                EditorGUILayout.HelpBox("当前版本先完整支持 ActiveSingle；ActiveMulti、MultiEquipment 仍可修改根技能字段，阶段配置沿用现有映射工具。", MessageType.Info);
            }
        }

        private void DrawMovementStates(BattleActionTimelineTableData table, BattleActionTimelineRecordData row)
        {
            string source = BattleActionTimelineSchema.Get(table, row, "allowedMovementStates");
            bool grounded = source.IndexOf("Grounded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("0", StringComparison.OrdinalIgnoreCase) >= 0;
            bool airborne = source.IndexOf("Airborne", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("1", StringComparison.OrdinalIgnoreCase) >= 0;
            bool nextGrounded = EditorGUILayout.ToggleLeft("允许地面释放", grounded);
            bool nextAirborne = EditorGUILayout.ToggleLeft("允许空中释放", airborne);
            if (nextGrounded != grounded || nextAirborne != airborne)
            {
                BattleActionTimelineSchema.Set(table, row,
                    string.Join(",", new[] { nextGrounded ? "Grounded" : null, nextAirborne ? "Airborne" : null }.Where(value => value != null)),
                    "allowedMovementStates");
            }
            if (!nextGrounded && !nextAirborne)
                EditorGUILayout.HelpBox("至少选择地面或空中一种状态。", MessageType.Error);
        }

        private void DrawCharacterAttach(BattleActionTimelineDocument document, BattleActionTimelineRecordData skill)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("接入角色", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("把当前根技能加入 BattleRobot.innateSkillIds；基础攻击入口单独设置。装备提供技能仍在技能/动作映射工具中配置。", MessageType.Info);
            BattleActionTimelineTableData robots = document.FindTable(RobotSheet);
            int[] ids = robots?.records.Select(row => row.rowId).OrderBy(id => id).ToArray() ?? Array.Empty<int>();
            int index = Array.IndexOf(ids, attachRobotId);
            int nextIndex = EditorGUILayout.Popup("角色", Math.Max(0, index), ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
            if (ids.Length > 0) attachRobotId = ids[Mathf.Clamp(nextIndex, 0, ids.Length - 1)];
            using (new EditorGUI.DisabledScope(attachRobotId <= 0))
            {
                if (GUILayout.Button("加入角色固有技能"))
                {
                    BattleActionTimelineRecordData robot = document.FindRecord(RobotSheet, attachRobotId);
                    string idsText = BattleActionTimelineSchema.Get(document.FindTable(RobotSheet), robot, "innateSkillIds");
                    List<int> values = ParsePrimitiveIds(idsText);
                    if (!values.Contains(selectedSkillId)) values.Add(selectedSkillId);
                    BattleActionTimelineSchema.Set(document.FindTable(RobotSheet), robot,
                        string.Join(",", values), "innateSkillIds");
                    dirty = true;
                    status = $"技能 {selectedSkillId} 已加入角色 {attachRobotId} 的固有技能列表。";
                    GUI.changed = true;
                }
                if (GUILayout.Button("设为角色基础攻击"))
                {
                    BattleActionTimelineRecordData robot = document.FindRecord(RobotSheet, attachRobotId);
                    BattleActionTimelineSchema.Set(document.FindTable(RobotSheet), robot,
                        selectedSkillId.ToString(CultureInfo.InvariantCulture), "basicAttackSkillId");
                    dirty = true;
                    status = $"技能 {selectedSkillId} 已设为角色 {attachRobotId} 的基础攻击。";
                    GUI.changed = true;
                }
            }
        }

        private void CreateSingleSkill()
        {
            BattleActionTimelineDocument edited = working.Clone();
            BattleActionTimelineTableData skills = edited.FindTable(SkillSheet);
            BattleActionTimelineTableData singles = edited.FindTable(SingleSheet);
            if (skills == null || singles == null)
            {
                status = "源表缺少 BattleSkill 或 BattleActiveSingleSkill。";
                return;
            }
            if (!newGrounded && !newAirborne)
            {
                status = "至少允许地面或空中一种状态。";
                return;
            }
            int skillId = skills.AllocateRowId();
            int singleId = singles.AllocateRowId();
            string codeName = newCodeName.Trim();
            float duration = 0.8f;
            if (TryFindClip(newActionId, out SequenceFrameClip clip))
                duration = clip.FrameCount / Math.Max(0.0001f, clip.FrameRate);

            BattleActionTimelineRecordData single = NewRow(singles, singleId);
            SetCell(singles, single, "codeName", codeName + "Action");
            SetCell(singles, single, "actionId", newActionId.ToString(CultureInfo.InvariantCulture));
            SetCell(singles, single, "allowedMovementStates",
                string.Join(",", new[] { newGrounded ? "Grounded" : null, newAirborne ? "Airborne" : null }.Where(value => value != null)));
            SetCell(singles, single, "movementMode", "Locked");
            BattleActionTimelineSchema.SetDouble(singles, single, duration * 0.25d, "startupEndTime");
            BattleActionTimelineSchema.SetDouble(singles, single, duration * 0.45d, "actionSwitchWindowStartTime");
            BattleActionTimelineSchema.SetDouble(singles, single, duration * 0.75d, "recoveryStartTime");
            BattleActionTimelineSchema.SetDouble(singles, single, duration, "actionDuration");
            // 结构体数组使用 [] 表示空数组；留空会让 cltabtoy 按字段类型解析失败。
            SetCell(singles, single, "costs", "[]");
            SetCell(singles, single, "executionSteps", "[]");
            SetCell(singles, single, "movementKeyframes", "[]");
            singles.records.Add(single);

            BattleActionTimelineRecordData skill = NewRow(skills, skillId);
            SetCell(skills, skill, "codeName", codeName);
            SetCell(skills, skill, "nameLanguageKey", string.IsNullOrWhiteSpace(newNameKey) ? "#" + codeName : newNameKey.Trim());
            SetCell(skills, skill, "descriptionLanguageKey", "#" + codeName + "_Desc");
            SetCell(skills, skill, "iconResourceId", "0");
            SetCell(skills, skill, "skillType", "ActiveSingle");
            SetCell(skills, skill, "targetType", "Enemy");
            SetCell(skills, skill, "targetId", singleId.ToString(CultureInfo.InvariantCulture));
            SetCell(skills, skill, "baseCooldownSeconds", "0");
            skills.records.Add(skill);
            working = edited;
            selectedSkillId = skillId;
            dirty = true;
            status = $"已创建技能 {skillId} / 单段 {singleId}，动作 {newActionId}；尚未写回源表。";
            GUI.changed = true;
        }

        private static BattleActionTimelineRecordData NewRow(BattleActionTimelineTableData table, int rowId)
        {
            BattleActionTimelineRecordData row = new BattleActionTimelineRecordData
            {
                rowId = rowId,
                cells = Enumerable.Repeat(string.Empty, table.headers.Length).ToArray(),
            };
            // rowId 是编辑器内部索引；写回 xlsx 时第一列也必须带稳定 id。
            BattleActionTimelineSchema.SetInt(table, row, rowId, "id");
            return row;
        }

        private static void SetCell(BattleActionTimelineTableData table, BattleActionTimelineRecordData row, string field, string value) =>
            BattleActionTimelineSchema.Set(table, row, value ?? string.Empty, field);

        private static void SetTextField(BattleActionTimelineTableData table, BattleActionTimelineRecordData row, string field, string label)
        {
            string old = BattleActionTimelineSchema.Get(table, row, field);
            string value = EditorGUILayout.TextField(label, old);
            if (!string.Equals(old, value, StringComparison.Ordinal))
                BattleActionTimelineSchema.Set(table, row, value, field);
        }

        private static void SetIntField(BattleActionTimelineTableData table, BattleActionTimelineRecordData row, string field, string label)
        {
            int old = BattleActionTimelineSchema.GetInt(table, row, 0, field);
            int value = EditorGUILayout.IntField(label, old);
            if (value != old) BattleActionTimelineSchema.Set(table, row, value.ToString(CultureInfo.InvariantCulture), field);
        }

        private static void SetDoubleField(BattleActionTimelineTableData table, BattleActionTimelineRecordData row, string field, string label)
        {
            double old = BattleActionTimelineSchema.GetDouble(table, row, 0d, field);
            double value = EditorGUILayout.DoubleField(label, old);
            if (!double.Equals(old, value))
                BattleActionTimelineSchema.SetDouble(table, row, Math.Max(0d, value), field);
        }

        private static void SetEnumField(BattleActionTimelineTableData table, BattleActionTimelineRecordData row, string field, string label, string[] values)
        {
            string raw = BattleActionTimelineSchema.Get(table, row, field);
            int index = Math.Max(0, Array.IndexOf(values, raw));
            int next = EditorGUILayout.Popup(label, index, values);
            if (next != index) BattleActionTimelineSchema.Set(table, row, values[next], field);
        }

        private static List<int> ParsePrimitiveIds(string text) =>
            (text ?? string.Empty).Trim().Trim('[', ']').Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0)
                .Where(id => id > 0).Distinct().ToList();

        private static bool TryFindClip(int actionId, out SequenceFrameClip clip)
        {
            clip = null;
            if (actionId <= 0) return false;
            foreach (string guid in AssetDatabase.FindAssets("t:SequenceFrameClip"))
            {
                SequenceFrameClip candidate = AssetDatabase.LoadAssetAtPath<SequenceFrameClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.ActionId == actionId)
                {
                    clip = candidate;
                    return true;
                }
            }
            return false;
        }

        private void LoadOfficial(bool notify)
        {
            if (!BattleActionTimelineWorkbookBridge.TryLoad(
                    out BattleActionTimelineWorkbookSnapshot snapshot, out string error))
            {
                baseline = null;
                working = null;
                status = error;
                return;
            }
            baseline = snapshot;
            working = BattleActionTimelineDocument.FromSnapshot(snapshot);
            selectedSkillId = GetVisibleSkills().FirstOrDefault()?.rowId ?? 0;
            selectedCharacterActionId = working.FindTable(CharacterActionSheet)?.records
                .Where(row => row != null)
                .OrderBy(row => row.rowId)
                .FirstOrDefault()?.rowId ?? (int)RefData.EnumBattleCharacterActionState.Idle;
            SyncActionStateSelection(selectedCharacterActionId);
            dirty = false;
            status = "已读取：" + snapshot.WorkbookPath;
            if (notify) ShowNotification(new GUIContent("技能源表已读取"));
            Repaint();
        }

        private void WriteOfficial(bool exportAfterWrite)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                status = "当前 Unity 正在运行战局，已阻止安全写回；请先停止 Play，再写回源 xlsx。";
                Debug.LogWarning("[BattleSkillEditor] " + status);
                ShowNotification(new GUIContent("请先停止 Unity 运行"));
                return;
            }

            if (baseline == null || working == null)
            {
                status = "技能写回失败：尚未读取有效的技能源表。";
                Debug.LogError("[BattleSkillEditor] " + status);
                ShowNotification(new GUIContent("尚未读取技能源表"));
                return;
            }
            List<string> issues = new List<string>();
            BattleActionTimelineSchema.Validate(working, issues);
            ValidateCharacterActions(working, issues);
            if (issues.Count > 0)
            {
                status = "校验未通过：" + string.Join("\n", issues.Take(8));
                Debug.LogError("[BattleSkillEditor] 技能写回校验未通过：\n" + status);
                ShowNotification(new GUIContent("技能配置校验未通过"));
                EditorUtility.DisplayDialog("技能写回校验未通过", status, "确定");
                return;
            }
            BattleActionTimelineWorkbookWriteSet writeSet = working.BuildWriteSet(baseline);
            if (writeSet.IsEmpty)
            {
                status = "当前没有需要写回的技能修改。";
                if (exportAfterWrite) ExportRobotWorkbook();
                return;
            }
            if (!EditorUtility.DisplayDialog("写回技能源表",
                "将更新 " + writeSet.Replacements.Count + " 条记录，删除 " + writeSet.Deletions.Count + " 条记录。",
                exportAfterWrite ? "写入并导表" : "仅写入源表", "取消")) return;
            if (!BattleActionTimelineWorkbookBridge.TryWrite(baseline, writeSet, out string error))
            {
                status = "技能写回失败：" + error;
                Debug.LogError("[BattleSkillEditor] 技能写回失败：\n" + error);
                ShowNotification(new GUIContent("技能写回失败"));
                EditorUtility.DisplayDialog("技能写回失败", error, "确定");
                return;
            }
            LoadOfficial(false);
            status = exportAfterWrite ? "技能源表已写回，正在导表。" : "技能源表写回成功，尚未导表。";
            if (exportAfterWrite) ExportRobotWorkbook();
        }

        private static void ValidateCharacterActions(
            BattleActionTimelineDocument document, IList<string> issues)
        {
            BattleActionTimelineTableData table = document.FindTable(CharacterActionSheet);
            if (table == null)
            {
                issues.Add("源表缺少 BattleCharacterAction。");
                return;
            }

            BattleActionTimelineTableData robots = document.FindTable(RobotSheet);
            BattleActionTimelineTableData equipment = document.FindTable("RobotEquipment");
            HashSet<int> rowIds = new HashSet<int>();
            foreach (BattleActionTimelineRecordData row in table.records.Where(value => value != null))
            {
                if (row.rowId <= 0 || !rowIds.Add(row.rowId) ||
                    !Enum.IsDefined(typeof(RefData.EnumBattleCharacterActionState),
                        (RefData.EnumBattleCharacterActionState)row.rowId) ||
                    row.rowId == (int)RefData.EnumBattleCharacterActionState.Invalid ||
                    row.rowId == (int)RefData.EnumBattleCharacterActionState.None)
                {
                    issues.Add($"BattleCharacterAction 存在非法状态行 ID={row.rowId}。");
                    continue;
                }
                if (!TryReadCharacterActionBindings(table, row,
                    out List<CharacterActionBinding> bindings, out string parseError))
                {
                    issues.Add($"BattleCharacterAction/{row.rowId} 的 entries 无法解析：{parseError}");
                    continue;
                }

                HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (CharacterActionBinding binding in bindings)
                {
                    string key = binding.MatchType + ":" + binding.CharacterId + ":" +
                        binding.LeftWeaponId + ":" + binding.RightWeaponId;
                    if (!keys.Add(key))
                        issues.Add($"BattleCharacterAction/{row.rowId} 存在重复匹配层：{key}");
                    if (binding.ActionId < 0)
                        issues.Add($"BattleCharacterAction/{row.rowId} 的 ActionId 不能小于 0。");
                    if (binding.CharacterId < -1 || binding.LeftWeaponId < 0 ||
                        binding.RightWeaponId < 0)
                        issues.Add($"BattleCharacterAction/{row.rowId} 存在非法负数 ID。");
                    if (binding.MatchType == "ExactCombination")
                    {
                        if (binding.CharacterId == 0 ||
                            (binding.LeftWeaponId == 0 && binding.RightWeaponId == 0))
                            issues.Add($"BattleCharacterAction/{row.rowId} 的精确组合必须指定角色和至少一把装备。");
                    }
                    else if (binding.MatchType == "CharacterDefault")
                    {
                        if (binding.CharacterId <= 0 || binding.LeftWeaponId != 0 ||
                            binding.RightWeaponId != 0)
                            issues.Add($"BattleCharacterAction/{row.rowId} 的角色默认必须指定角色且双手为 0。");
                    }
                    else if (binding.MatchType == "GlobalDefault")
                    {
                        if (binding.CharacterId != 0 || binding.LeftWeaponId != 0 ||
                            binding.RightWeaponId != 0)
                            issues.Add($"BattleCharacterAction/{row.rowId} 的全局默认必须全部为 0。");
                    }
                    else
                    {
                        issues.Add($"BattleCharacterAction/{row.rowId} 存在未知匹配类型：{binding.MatchType}");
                    }
                    if (binding.CharacterId > 0 && robots?.records.All(value => value.rowId != binding.CharacterId) == true)
                        issues.Add($"BattleCharacterAction/{row.rowId} 引用了不存在的角色 {binding.CharacterId}。");
                    if (binding.LeftWeaponId > 0 && equipment?.records.All(value => value.rowId != binding.LeftWeaponId) == true)
                        issues.Add($"BattleCharacterAction/{row.rowId} 引用了不存在的左手装备 {binding.LeftWeaponId}。");
                    if (binding.RightWeaponId > 0 && equipment?.records.All(value => value.rowId != binding.RightWeaponId) == true)
                        issues.Add($"BattleCharacterAction/{row.rowId} 引用了不存在的右手装备 {binding.RightWeaponId}。");
                }
            }
        }

        private void ExportRobotWorkbook()
        {
            if (TryGame.RefDataTools.Editor.TryGameRefDataExportWindow.ExportFiles(
                new List<string> { BattleActionTimelineWorkbookBridge.DefaultWorkbookPath },
                TryGame.RefDataTools.Editor.TryGameRefDataExportMode.Incremental))
                status = "技能源表写回并导表成功。";
            else status = "技能源表已写回，但导表失败，请查看导表日志。";
        }
    }
}
#endif
