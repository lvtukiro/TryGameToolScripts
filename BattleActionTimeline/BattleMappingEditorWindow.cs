#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    public sealed class BattleMappingEditorWindow : EditorWindow
    {
        private const string CharacterActionSheet = "BattleCharacterAction";
        private const string MultiEquipmentSheet = "BattleSkillMultiEquipment";
        private const string RobotSheet = "BattleRobot";
        private const string EquipmentSheet = "RobotEquipment";
        private const string SkillSheet = "BattleSkill";

        private enum MappingKind
        {
            CharacterAction,
            MultiEquipment,
        }

        [Serializable]
        private sealed class MappingEntry
        {
            public string MatchType = "GlobalDefault";
            public int CharacterId;
            public int LeftWeaponId;
            public int RightWeaponId;
            public int ResultId;
            /// <summary>
            /// 创建当前数据的独立副本，避免后续修改共享可变状态。
            /// </summary>

            public MappingEntry Clone()
            {
                return new MappingEntry
                {
                    MatchType = MatchType,
                    CharacterId = CharacterId,
                    LeftWeaponId = LeftWeaponId,
                    RightWeaponId = RightWeaponId,
                    ResultId = ResultId,
                };
            }
        }

        private sealed class MappingRow
        {
            public int Id;
            public string CodeName = string.Empty;
            public string[] Cells = Array.Empty<string>();
            public List<MappingEntry> Entries = new List<MappingEntry>();
            public string ParseError = string.Empty;
            /// <summary>
            /// 创建当前数据的独立副本，避免后续修改共享可变状态。
            /// </summary>

            public MappingRow Clone()
            {
                MappingRow clone = new MappingRow
                {
                    Id = Id,
                    CodeName = CodeName,
                    Cells = Cells != null ? (string[])Cells.Clone() : Array.Empty<string>(),
                    ParseError = ParseError,
                };
                for (int index = 0; index < Entries.Count; index++)
                {
                    clone.Entries.Add(Entries[index].Clone());
                }

                return clone;
            }
        }

        private BattleActionTimelineWorkbookSnapshot baseline;
        private BattleActionTimelineWorkbookSnapshot working;
        private MappingKind mappingKind;
        private int selectedRowId;
        private Vector2 rowScroll;
        private Vector2 entryScroll;
        private string status = "尚未读取机器人配置源表。";
        private bool dirty;
        private string characterFilterText = string.Empty;
        private string leftWeaponFilterText = string.Empty;
        private string rightWeaponFilterText = string.Empty;
        private string rowIdFilterText = string.Empty;
        /// <summary>
        /// 获取当前 `MappingRow` 实例的当前`Sheet`。
        /// </summary>

        private string CurrentSheet => mappingKind == MappingKind.CharacterAction
            ? CharacterActionSheet
            : MultiEquipmentSheet;
        /// <summary>
        /// 打开当前 `MappingRow` 实例。
        /// </summary>

        [MenuItem("TryGame/战斗/技能映射编辑器", false, 433)]
        private static void Open()
        {
            BattleMappingEditorWindow window =
                GetWindow<BattleMappingEditorWindow>();
            window.titleContent = new GUIContent("技能/动作映射");
            window.minSize = new Vector2(1180f, 680f);
            window.Show();
        }
        /// <summary>
        /// 组件启用时注册监听并刷新当前状态。
        /// </summary>

        private void OnEnable()
        {
            LoadOfficial(false);
        }
        /// <summary>
        /// 绘制编辑器窗口内容并处理当前交互。
        /// </summary>

        private void OnGUI()
        {
            DrawToolbar();
            if (working == null)
            {
                EditorGUILayout.HelpBox(status, MessageType.Info);
                return;
            }

            DrawTabBar();
            BattleActionTimelineWorkbookTable table = GetWorkingTable(CurrentSheet);
            if (table == null)
            {
                EditorGUILayout.HelpBox(
                    "正式源表缺少：" + DisplaySheetName(CurrentSheet),
                    MessageType.Error);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawRowList(table);
            DrawSelectedRow(table);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }
        /// <summary>
        /// 绘制工具栏。
        /// </summary>

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("重新读取", EditorStyles.toolbarButton, GUILayout.Width(76f)))
            {
                if (!dirty || EditorUtility.DisplayDialog(
                    "放弃未保存修改？",
                    "当前窗口有未写回的映射修改。重新读取会丢弃这些修改。",
                    "重新读取",
                    "取消"))
                {
                    LoadOfficial(true);
                }
            }

            GUI.enabled = working != null && dirty;
            if (GUILayout.Button("写入源表", EditorStyles.toolbarButton, GUILayout.Width(82f)))
            {
                WriteOfficial(false);
            }

            if (GUILayout.Button("写入并导表", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            {
                WriteOfficial(true);
            }

            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                dirty ? "有未写回修改" : "与读取基线一致",
                EditorStyles.toolbarButton,
                GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "本工具只修改正式 r.机器人表.xlsx；写入时先做 hash、临时文件、回读和备份校验。" +
                "“写入并导表”会在源表写回成功后调用现有增量导表事务。",
                MessageType.Info);
        }
        /// <summary>
        /// 绘制标签页栏。
        /// </summary>

        private void DrawTabBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            MappingKind next = (MappingKind)GUILayout.Toolbar(
                (int)mappingKind,
                new[] { "角色动作映射", "多装备技能映射" },
                EditorStyles.toolbarButton);
            if (next != mappingKind)
            {
                mappingKind = next;
                selectedRowId = 0;
                entryScroll = Vector2.zero;
            }

            EditorGUILayout.EndHorizontal();
        }
        /// <summary>
        /// 绘制数据行列表。
        /// </summary>

        private void DrawRowList(BattleActionTimelineWorkbookTable table)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(310f));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                DisplaySheetName(CurrentSheet) + "（" + table.Records.Count + " 条）",
                EditorStyles.boldLabel);
            if (GUILayout.Button("新增映射", GUILayout.Width(82f)))
            {
                AddRow(table);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("映射 ID", GUILayout.Width(58f));
            rowIdFilterText = EditorGUILayout.TextField(
                rowIdFilterText,
                GUILayout.Width(100f));
            if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                rowIdFilterText = string.Empty;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            List<BattleActionTimelineWorkbookRecord> visibleRecords =
                GetVisibleRows(table);
            if (selectedRowId != 0 && !visibleRecords.Any(record =>
                record.RowId == selectedRowId))
            {
                selectedRowId = visibleRecords.Count > 0
                    ? visibleRecords[0].RowId
                    : 0;
                entryScroll = Vector2.zero;
            }

            rowScroll = EditorGUILayout.BeginScrollView(rowScroll);
            for (int index = 0; index < visibleRecords.Count; index++)
            {
                BattleActionTimelineWorkbookRecord record = visibleRecords[index];
                if (record == null)
                {
                    continue;
                }

                string codeName = GetCell(table, record, "codeName");
                string label = "ID " + record.RowId.ToString(CultureInfo.InvariantCulture) +
                    "  " + (string.IsNullOrWhiteSpace(codeName) ? "<未命名>" : codeName);
                bool selected = selectedRowId == record.RowId;
                Color previous = GUI.backgroundColor;
                if (selected)
                {
                    GUI.backgroundColor = new Color(0.35f, 0.72f, 1f, 1f);
                }

                if (GUILayout.Button(label, EditorStyles.miniButton))
                {
                    selectedRowId = record.RowId;
                    entryScroll = Vector2.zero;
                }

                GUI.backgroundColor = previous;
            }

            if (visibleRecords.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有符合当前映射 ID 的记录。",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
        /// <summary>
        /// 绘制映射筛选。
        /// </summary>

        private void DrawMappingFilter(MappingRow row)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                mappingKind == MappingKind.MultiEquipment
                    ? "筛选多装备技能映射"
                    : "筛选角色动作映射",
                EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("角色 ID", GUILayout.Width(52f));
            characterFilterText = EditorGUILayout.TextField(
                characterFilterText,
                GUILayout.Width(100f));
            GUILayout.Label("左手 ID", GUILayout.Width(52f));
            leftWeaponFilterText = EditorGUILayout.TextField(
                leftWeaponFilterText,
                GUILayout.Width(100f));
            GUILayout.Label("右手 ID", GUILayout.Width(52f));
            rightWeaponFilterText = EditorGUILayout.TextField(
                rightWeaponFilterText,
                GUILayout.Width(100f));
            EditorGUILayout.EndHorizontal();

            bool hasInvalidFilter =
                !IsFilterValueValid(characterFilterText) ||
                !IsFilterValueValid(leftWeaponFilterText) ||
                !IsFilterValueValid(rightWeaponFilterText);
            if (hasInvalidFilter)
            {
                EditorGUILayout.HelpBox(
                    "筛选 ID 只能填写整数；留空表示不限制。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "匹配任意一层同时满足的角色和左右手装备组合。",
                    EditorStyles.miniLabel);
            }

            if (GUILayout.Button("清空筛选", EditorStyles.miniButton))
            {
                characterFilterText = string.Empty;
                leftWeaponFilterText = string.Empty;
                rightWeaponFilterText = string.Empty;
                GUI.FocusControl(null);
            }

            EditorGUILayout.LabelField(
                "当前显示：" + GetVisibleEntryIndices(row).Count +
                " / " + row.Entries.Count + " 层",
                EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }
        /// <summary>
        /// 获取可见行。
        /// </summary>

        private List<BattleActionTimelineWorkbookRecord> GetVisibleRows(
            BattleActionTimelineWorkbookTable table)
        {
            if (table == null || table.Records == null)
            {
                return new List<BattleActionTimelineWorkbookRecord>();
            }

            if (string.IsNullOrWhiteSpace(rowIdFilterText))
            {
                return table.Records.Where(record => record != null).ToList();
            }

            if (!int.TryParse(
                rowIdFilterText.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int rowId))
            {
                return new List<BattleActionTimelineWorkbookRecord>();
            }

            return table.Records
                .Where(record => record != null && record.RowId == rowId)
                .ToList();
        }
        /// <summary>
        /// 获取可见条目索引。
        /// </summary>

        private List<int> GetVisibleEntryIndices(MappingRow row)
        {
            List<int> result = new List<int>();
            if (row == null || row.Entries == null)
            {
                return result;
            }

            if (string.IsNullOrWhiteSpace(characterFilterText) &&
                string.IsNullOrWhiteSpace(leftWeaponFilterText) &&
                string.IsNullOrWhiteSpace(rightWeaponFilterText))
            {
                for (int index = 0; index < row.Entries.Count; index++)
                {
                    result.Add(index);
                }

                return result;
            }

            if (!TryParseFilterValue(characterFilterText, out int? characterId) ||
                !TryParseFilterValue(leftWeaponFilterText, out int? leftWeaponId) ||
                !TryParseFilterValue(rightWeaponFilterText, out int? rightWeaponId))
            {
                return result;
            }

            for (int index = 0; index < row.Entries.Count; index++)
            {
                MappingEntry entry = row.Entries[index];
                if ((!characterId.HasValue || entry.CharacterId == characterId.Value) &&
                    (!leftWeaponId.HasValue || entry.LeftWeaponId == leftWeaponId.Value) &&
                    (!rightWeaponId.HasValue || entry.RightWeaponId == rightWeaponId.Value))
                {
                    result.Add(index);
                }
            }

            return result;
        }
        /// <summary>
        /// 检查 `text` 是否满足“筛选值`Valid`”条件。
        /// </summary>

        private static bool IsFilterValueValid(string text)
        {
            return string.IsNullOrWhiteSpace(text) ||
                int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }
        /// <summary>
        /// 根据 `text` 解析筛选值；成功时写出 `value` 并返回 `true`，前置条件不满足时返回 `false`。
        /// </summary>

        private static bool TryParseFilterValue(string text, out int? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (!int.TryParse(
                text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed))
            {
                return false;
            }

            value = parsed;
            return true;
        }
        /// <summary>
        /// 绘制已选数据行。
        /// </summary>

        private void DrawSelectedRow(BattleActionTimelineWorkbookTable table)
        {
            List<BattleActionTimelineWorkbookRecord> visibleRecords =
                GetVisibleRows(table);
            BattleActionTimelineWorkbookRecord record = visibleRecords.FirstOrDefault(
                candidate => candidate.RowId == selectedRowId);
            if (record == null)
            {
                if (visibleRecords.Count > 0)
                {
                    selectedRowId = visibleRecords[0].RowId;
                    record = visibleRecords[0];
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        table.Records.Count == 0
                            ? "请先新增一条映射。"
                            : "当前筛选条件没有可编辑的映射。",
                        MessageType.Info);
                    return;
                }
            }

            MappingRow row = ReadRow(table, record);
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "映射 ID：" + row.Id + "  /  配置代码名：" + row.CodeName,
                EditorStyles.boldLabel);
            if (GUILayout.Button("删除整条映射", GUILayout.Width(110f)))
            {
                DeleteRow(table, row.Id);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrWhiteSpace(row.ParseError))
            {
                EditorGUILayout.HelpBox(
                    "映射内容解析失败，修复前不能写回：" + row.ParseError,
                    MessageType.Error);
            }

            string codeName = EditorGUILayout.TextField("配置代码名", row.CodeName);
            if (!string.Equals(codeName, row.CodeName, StringComparison.Ordinal))
            {
                SetCell(table, record, "codeName", codeName);
                dirty = true;
                row.CodeName = codeName;
            }

            DrawMappingFilter(row);
            List<int> visibleEntryIndices = GetVisibleEntryIndices(row);
            EditorGUILayout.LabelField(
                "映射层（" + visibleEntryIndices.Count + " / " +
                row.Entries.Count + " 条，按匹配优先级逐层显示）",
                EditorStyles.boldLabel);
            entryScroll = EditorGUILayout.BeginScrollView(entryScroll);
            for (int visibleIndex = 0;
                visibleIndex < visibleEntryIndices.Count;
                visibleIndex++)
            {
                int index = visibleEntryIndices[visibleIndex];
                if (index < 0 || index >= row.Entries.Count)
                {
                    continue;
                }

                if (DrawEntry(row, index, out bool deleted))
                {
                    SetEntries(table, record, row.Entries);
                    dirty = true;
                    if (deleted)
                    {
                        break;
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("+ 新增一层映射", GUILayout.Height(28f)))
            {
                row.Entries.Add(CreateDefaultEntry());
                SetEntries(table, record, row.Entries);
                dirty = true;
            }

            EditorGUILayout.EndVertical();
        }
        /// <summary>
        /// 根据 `row`、`index`、`deleted` 绘制`Entry`；完成预期变更时返回 `true`，条件不足或执行失败时返回 `false`。
        /// </summary>

        private bool DrawEntry(MappingRow row, int index, out bool deleted)
        {
            deleted = false;
            MappingEntry entry = row.Entries[index];
            bool changed = false;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("第 " + (index + 1) + " 层", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("删除", GUILayout.Width(50f)))
            {
                row.Entries.RemoveAt(index);
                deleted = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return true;
            }

            EditorGUILayout.EndHorizontal();
            string[] matchTypes = { "精确组合", "角色默认", "全局默认" };
            string[] matchTypeValues = { "ExactCombination", "CharacterDefault", "GlobalDefault" };
            int matchIndex = Array.IndexOf(matchTypeValues, entry.MatchType);
            matchIndex = Mathf.Clamp(matchIndex, 0, matchTypes.Length - 1);
            int nextMatchIndex = EditorGUILayout.Popup("匹配类型", matchIndex, matchTypes);
            if (nextMatchIndex != matchIndex)
            {
                entry.MatchType = matchTypeValues[nextMatchIndex];
                ApplyMatchDefaults(entry);
                changed = true;
            }

            entry.CharacterId = DrawIdPicker(
                "角色 ID",
                entry.CharacterId,
                GetKnownIds(RobotSheet, entry.CharacterId, true));
            entry.LeftWeaponId = DrawIdPicker(
                "左手装备 ID",
                entry.LeftWeaponId,
                GetKnownIds(EquipmentSheet, entry.LeftWeaponId, true));
            entry.RightWeaponId = DrawIdPicker(
                "右手装备 ID",
                entry.RightWeaponId,
                GetKnownIds(EquipmentSheet, entry.RightWeaponId, true));
            entry.ResultId = DrawIdPicker(
                mappingKind == MappingKind.CharacterAction ? "动作 ID" : "解析技能 ID",
                entry.ResultId,
                mappingKind == MappingKind.CharacterAction
                    ? GetActionIds(entry.ResultId)
                    : GetKnownIds(SkillSheet, entry.ResultId, true));

            changed |= GUI.changed;
            EditorGUILayout.EndVertical();
            return changed;
        }
        /// <summary>
        /// 绘制标识`Picker`。
        /// </summary>

        private int DrawIdPicker(string label, int value, IReadOnlyList<int> knownIds)
        {
            List<int> values = new List<int>(knownIds ?? Array.Empty<int>());
            if (!values.Contains(value))
            {
                values.Add(value);
            }

            values = values.Distinct().OrderBy(id => id).ToList();
            string[] labels = values
                .Select(id => id == -1 ? "-1（通配）" : id == 0 ? "0（空/默认）" : id.ToString())
                .ToArray();
            int popupValue = EditorGUILayout.IntPopup(label + "（选择）", value, labels, values.ToArray());
            return EditorGUILayout.IntField(label + "（最终值）", popupValue);
        }
        /// <summary>
        /// 添加数据行。
        /// </summary>

        private void AddRow(BattleActionTimelineWorkbookTable table)
        {
            int nextId = table.Records.Count == 0
                ? 1
                : table.Records.Max(record => record.RowId) + 1;
            string[] cells = new string[table.Headers.Length];
            SetCell(cells, table.Headers, "id", nextId.ToString(CultureInfo.InvariantCulture));
            SetCell(cells, table.Headers, "codeName", "NewMapping_" + nextId);
            SetCell(cells, table.Headers, "entries", "[]");
            table.Records.Add(new BattleActionTimelineWorkbookRecord
            {
                RowId = nextId,
                Cells = cells,
            });
            selectedRowId = nextId;
            dirty = true;
        }
        /// <summary>
        /// 删除数据行。
        /// </summary>

        private void DeleteRow(BattleActionTimelineWorkbookTable table, int rowId)
        {
            if (!EditorUtility.DisplayDialog(
                "删除映射",
                "确定删除映射 id=" + rowId + "？",
                "删除",
                "取消"))
            {
                return;
            }

            table.Records.RemoveAll(record => record.RowId == rowId);
            selectedRowId = table.Records.Count > 0 ? table.Records[0].RowId : 0;
            dirty = true;
        }
        /// <summary>
        /// 读取数据行。
        /// </summary>

        private MappingRow ReadRow(
            BattleActionTimelineWorkbookTable table,
            BattleActionTimelineWorkbookRecord record)
        {
            MappingKind kind = KindForSheet(table.Name);
            MappingRow result = new MappingRow
            {
                Id = record.RowId,
                CodeName = GetCell(table, record, "codeName"),
                Cells = record.Cells,
            };
            string source = GetCell(table, record, "entries");
            if (!BattleActionTimelineInlineStructCodec.TryParse(
                    source,
                    out List<Dictionary<string, string>> values,
                    out string error))
            {
                result.ParseError = error;
                return result;
            }

            for (int index = 0; index < values.Count; index++)
            {
                Dictionary<string, string> value = values[index];
                result.Entries.Add(new MappingEntry
                {
                    MatchType = GetValue(value, "MatchType", "GlobalDefault"),
                    CharacterId = ParseInt(GetValue(value, "CharacterId", "0")),
                    LeftWeaponId = ParseInt(GetValue(value, "LeftWeaponId", "0")),
                    RightWeaponId = ParseInt(GetValue(value, "RightWeaponId", "0")),
                    ResultId = ParseInt(GetValue(
                        value,
                        kind == MappingKind.CharacterAction ? "ActionId" : "ResolvedSkillId",
                        "0")),
                });
            }

            return result;
        }
        /// <summary>
        /// 设置条目。
        /// </summary>

        private void SetEntries(
            BattleActionTimelineWorkbookTable table,
            BattleActionTimelineWorkbookRecord record,
            IReadOnlyList<MappingEntry> entries)
        {
            StringBuilder text = new StringBuilder("[");
            for (int index = 0; index < entries.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                MappingEntry entry = entries[index];
                text.Append("{\"MatchType\":")
                    .Append(entry.MatchType)
                    .Append(",\"CharacterId\":")
                    .Append(entry.CharacterId.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"LeftWeaponId\":")
                    .Append(entry.LeftWeaponId.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"RightWeaponId\":")
                    .Append(entry.RightWeaponId.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"")
                    .Append(mappingKind == MappingKind.CharacterAction ? "ActionId" : "ResolvedSkillId")
                    .Append("\":")
                    .Append(entry.ResultId.ToString(CultureInfo.InvariantCulture))
                    .Append('}');
            }

            text.Append(']');
            SetCell(table, record, "entries", text.ToString());
        }
        /// <summary>
        /// 加载正式。
        /// </summary>

        private void LoadOfficial(bool notify)
        {
            if (!BattleActionTimelineWorkbookBridge.TryLoad(
                    out BattleActionTimelineWorkbookSnapshot snapshot,
                    out string error))
            {
                baseline = null;
                working = null;
                status = error;
                if (notify)
                {
                    ShowNotification(new GUIContent("读取源表失败"));
                }

                return;
            }

            baseline = snapshot;
            working = CloneSnapshot(snapshot);
            dirty = false;
            selectedRowId = FirstRowId(CurrentSheet);
            status = "已读取：" + snapshot.WorkbookPath;
            if (notify)
            {
                ShowNotification(new GUIContent("映射源表已读取"));
            }

            Repaint();
        }
        /// <summary>
        /// 写入正式。
        /// </summary>

        private void WriteOfficial(bool exportAfterWrite)
        {
            if (baseline == null || working == null)
            {
                return;
            }

            if (!ValidateWorking(out string validationError))
            {
                status = validationError;
                ShowNotification(new GUIContent("校验未通过"));
                return;
            }

            BattleActionTimelineWorkbookWriteSet writeSet = BuildWriteSet();
            if (writeSet.IsEmpty)
            {
                status = "当前没有需要写回的映射修改。";
                if (exportAfterWrite)
                {
                    ExportRobotWorkbook();
                }

                return;
            }

            if (!EditorUtility.DisplayDialog(
                "写回映射源表",
                "将更新 " + writeSet.Replacements.Count + " 条记录，删除 " +
                writeSet.Deletions.Count + " 条记录。\n\n" +
                "写入后会自动回读校验并生成备份。",
                exportAfterWrite ? "写入并导表" : "仅写入源表",
                "取消"))
            {
                return;
            }

            if (!BattleActionTimelineWorkbookBridge.TryWrite(
                    baseline,
                    writeSet,
                    out string backupPath,
                    out string error))
            {
                status = error;
                Debug.LogError("[BattleMappingEditor] " + error);
                ShowNotification(new GUIContent("映射写回失败"));
                return;
            }

            LoadOfficial(false);
            status = "源表写回成功；备份：" + backupPath;
            if (exportAfterWrite)
            {
                ExportRobotWorkbook();
            }
            else
            {
                ShowNotification(new GUIContent("源表写回成功，尚未导表"));
            }
        }
        /// <summary>
        /// 导出机器人工作簿。
        /// </summary>

        private void ExportRobotWorkbook()
        {
            string path = BattleActionTimelineWorkbookBridge.DefaultWorkbookPath;
            if (TryGame.RefDataTools.Editor.TryGameRefDataExportWindow.ExportFiles(
                    new List<string> { path },
                    TryGame.RefDataTools.Editor.TryGameRefDataExportMode.Incremental))
            {
                status = "源表写回并导表成功：" + path;
                ShowNotification(new GUIContent("写回并导表成功"));
            }
            else
            {
                status = "源表已写回，但导表失败；请查看导表日志。";
                ShowNotification(new GUIContent("源表成功，导表失败"));
            }
        }
        /// <summary>
        /// 校验`Working`所需的结构、引用和状态约束；全部满足时返回 `true`，否则返回 `false`。
        /// </summary>

        private bool ValidateWorking(out string error)
        {
            error = string.Empty;
            foreach (string sheetName in new[] { CharacterActionSheet, MultiEquipmentSheet })
            {
                MappingKind kind = KindForSheet(sheetName);
                BattleActionTimelineWorkbookTable table = GetWorkingTable(sheetName);
                if (table == null)
                {
                    error = "缺少映射表：" + DisplaySheetName(sheetName);
                    return false;
                }

                HashSet<int> ids = new HashSet<int>();
                for (int index = 0; index < table.Records.Count; index++)
                {
                    BattleActionTimelineWorkbookRecord record = table.Records[index];
                    if (record == null || record.RowId <= 0 || !ids.Add(record.RowId))
                    {
                    error = DisplaySheetName(sheetName) + " 存在非法或重复 ID。";
                        return false;
                    }

                    MappingRow row = ReadRow(table, record);
                    if (!string.IsNullOrWhiteSpace(row.ParseError))
                    {
                        error = DisplaySheetName(sheetName) + " ID=" + row.Id +
                            " 的映射内容无法解析：" + row.ParseError;
                        return false;
                    }

                    HashSet<string> entryKeys = new HashSet<string>(StringComparer.Ordinal);
                    for (int entryIndex = 0; entryIndex < row.Entries.Count; entryIndex++)
                    {
                        MappingEntry entry = row.Entries[entryIndex];
                        if (entry.MatchType != "ExactCombination" &&
                            entry.MatchType != "CharacterDefault" &&
                            entry.MatchType != "GlobalDefault")
                        {
                            error = DisplaySheetName(sheetName) + " ID=" + row.Id +
                                " 存在未知匹配类型。";
                            return false;
                        }

                        string entryKey = DisplayMatchType(entry.MatchType) + ":" + entry.CharacterId + ":" +
                            entry.LeftWeaponId + ":" + entry.RightWeaponId;
                        if (!entryKeys.Add(entryKey))
                        {
                            error = DisplaySheetName(sheetName) + " ID=" + row.Id +
                                " 存在重复匹配层：" + entryKey;
                            return false;
                        }

                        if (entry.ResultId < 0 ||
                            (kind == MappingKind.MultiEquipment && entry.ResultId == 0))
                        {
                            error = DisplaySheetName(sheetName) + " ID=" + row.Id + " 第 " +
                                (entryIndex + 1) + " 层结果 ID 非法。";
                            return false;
                        }

                        if (!ValidateEntry(entry, kind, out string entryError))
                        {
                            error = DisplaySheetName(sheetName) + " ID=" + row.Id + " 第 " +
                                (entryIndex + 1) + " 层：" + entryError;
                            return false;
                        }
                    }
                }
            }

            return true;
        }
        /// <summary>
        /// 根据 `entry`、`kind` 校验`Entry`所需的结构、引用和状态约束；全部满足时返回 `true`，否则返回 `false`。
        /// </summary>

        private bool ValidateEntry(
            MappingEntry entry,
            MappingKind kind,
            out string error)
        {
            error = string.Empty;
            if (kind == MappingKind.CharacterAction)
            {
                if (entry.CharacterId < -1 || entry.LeftWeaponId < 0 || entry.RightWeaponId < 0)
                {
                    error = "角色动作映射的 ID 不能使用非法负数。";
                    return false;
                }

                switch (entry.MatchType)
                {
                    case "ExactCombination":
                        if (entry.CharacterId == 0 ||
                            (entry.LeftWeaponId == 0 && entry.RightWeaponId == 0))
                        {
                            error = "精确组合需要角色（可为 -1）和至少一把武器。";
                            return false;
                        }

                        break;
                    case "CharacterDefault":
                        if (entry.CharacterId <= 0 || entry.LeftWeaponId != 0 || entry.RightWeaponId != 0)
                        {
                            error = "角色默认需要具体角色且左右手必须为 0。";
                            return false;
                        }

                        break;
                    case "GlobalDefault":
                        if (entry.CharacterId != 0 || entry.LeftWeaponId != 0 || entry.RightWeaponId != 0)
                        {
                            error = "全局默认需要角色和左右手都为 0。";
                            return false;
                        }

                        break;
                }

                return true;
            }

            if (entry.CharacterId < -1 || entry.LeftWeaponId < -1 || entry.RightWeaponId < -1)
            {
                error = "多装备技能映射的 ID 只能使用 -1 通配、0 空手或正数。";
                return false;
            }

            switch (entry.MatchType)
            {
                case "ExactCombination":
                    if (entry.CharacterId == 0 || entry.LeftWeaponId < 0 || entry.RightWeaponId < 0)
                    {
                        error = "精确组合需要角色（可为 -1）以及明确左右手 ID。";
                        return false;
                    }

                    break;
                case "CharacterDefault":
                    if (entry.CharacterId <= 0 || entry.LeftWeaponId != -1 || entry.RightWeaponId != -1)
                    {
                        error = "角色默认需要具体角色且左右手必须为 -1。";
                        return false;
                    }

                    break;
                case "GlobalDefault":
                    if (entry.CharacterId != -1 || entry.LeftWeaponId != -1 || entry.RightWeaponId != -1)
                    {
                        error = "全局默认需要角色和左右手都为 -1。";
                        return false;
                    }

                    break;
            }

            return true;
        }
        /// <summary>
        /// 创建默认条目。
        /// </summary>

        private MappingEntry CreateDefaultEntry()
        {
            MappingEntry entry = new MappingEntry();
            ApplyMatchDefaults(entry);
            return entry;
        }
        /// <summary>
        /// 应用匹配默认值。
        /// </summary>

        private void ApplyMatchDefaults(MappingEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (mappingKind == MappingKind.MultiEquipment)
            {
                entry.CharacterId = -1;
                entry.LeftWeaponId = -1;
                entry.RightWeaponId = -1;
            }
            else
            {
                entry.CharacterId = 0;
                entry.LeftWeaponId = 0;
                entry.RightWeaponId = 0;
            }
        }
        /// <summary>
        /// 根据 `sheetName` 构建种类对应表页并返回结果。
        /// </summary>

        private static MappingKind KindForSheet(string sheetName)
        {
            return string.Equals(
                    sheetName,
                    CharacterActionSheet,
                    StringComparison.Ordinal)
                ? MappingKind.CharacterAction
                : MappingKind.MultiEquipment;
        }
        /// <summary>
        /// 构建写入集合。
        /// </summary>

        private BattleActionTimelineWorkbookWriteSet BuildWriteSet()
        {
            BattleActionTimelineWorkbookWriteSet result =
                new BattleActionTimelineWorkbookWriteSet();
            foreach (KeyValuePair<string, BattleActionTimelineWorkbookTable> pair
                in baseline.Tables)
            {
                if (!working.Tables.TryGetValue(pair.Key, out BattleActionTimelineWorkbookTable current))
                {
                    continue;
                }

                BattleActionTimelineWorkbookTable original = pair.Value;
                Dictionary<int, BattleActionTimelineWorkbookRecord> currentById = current.Records
                    .Where(record => record != null)
                    .ToDictionary(record => record.RowId);
                for (int index = 0; index < original.Records.Count; index++)
                {
                    BattleActionTimelineWorkbookRecord oldRecord = original.Records[index];
                    BattleActionTimelineRecordKey key =
                        new BattleActionTimelineRecordKey(pair.Key, oldRecord.RowId);
                    if (!currentById.TryGetValue(oldRecord.RowId, out BattleActionTimelineWorkbookRecord currentRecord))
                    {
                        result.Deletions.Add(key);
                        continue;
                    }

                    if (!BattleActionTimelineWorkbookBridge.CellsEqual(oldRecord.Cells, currentRecord.Cells))
                    {
                        result.Replacements[key] = (string[])currentRecord.Cells.Clone();
                    }
                }

                HashSet<int> originalIds = new HashSet<int>(original.Records.Select(record => record.RowId));
                for (int index = 0; index < current.Records.Count; index++)
                {
                    BattleActionTimelineWorkbookRecord record = current.Records[index];
                    if (record != null && !originalIds.Contains(record.RowId))
                    {
                        result.Replacements.Add(
                            new BattleActionTimelineRecordKey(pair.Key, record.RowId),
                            (string[])record.Cells.Clone());
                    }
                }
            }

            return result;
        }
        /// <summary>
        /// 获取`Working`数据表。
        /// </summary>

        private BattleActionTimelineWorkbookTable GetWorkingTable(string sheetName)
        {
            return working != null && working.Tables.TryGetValue(sheetName, out BattleActionTimelineWorkbookTable table)
                ? table
                : null;
        }
        /// <summary>
        /// 根据 `sheetName` 构建首个数据行`Id`并返回结果。
        /// </summary>

        private int FirstRowId(string sheetName)
        {
            BattleActionTimelineWorkbookTable table = GetWorkingTable(sheetName);
            return table != null && table.Records.Count > 0 ? table.Records[0].RowId : 0;
        }
        /// <summary>
        /// 查找记录。
        /// </summary>

        private static BattleActionTimelineWorkbookRecord FindRecord(
            BattleActionTimelineWorkbookTable table,
            int rowId)
        {
            return table?.Records.FirstOrDefault(record => record != null && record.RowId == rowId);
        }
        /// <summary>
        /// 获取格子。
        /// </summary>

        private static string GetCell(
            BattleActionTimelineWorkbookTable table,
            BattleActionTimelineWorkbookRecord record,
            string header)
        {
            int column = FindColumn(table, header);
            return column >= 0 && record?.Cells != null && column < record.Cells.Length
                ? record.Cells[column] ?? string.Empty
                : string.Empty;
        }
        /// <summary>
        /// 设置格子。
        /// </summary>

        private static void SetCell(
            BattleActionTimelineWorkbookTable table,
            BattleActionTimelineWorkbookRecord record,
            string header,
            string value)
        {
            SetCell(record.Cells, table.Headers, header, value);
        }
        /// <summary>
        /// 设置格子。
        /// </summary>

        private static void SetCell(
            string[] cells,
            IReadOnlyList<string> headers,
            string header,
            string value)
        {
            int column = FindColumn(headers, header);
            if (column >= 0 && column < cells.Length)
            {
                cells[column] = value ?? string.Empty;
            }
        }
        /// <summary>
        /// 查找列。
        /// </summary>

        private static int FindColumn(
            BattleActionTimelineWorkbookTable table,
            string header)
        {
            return FindColumn(table?.Headers, header);
        }
        /// <summary>
        /// 查找列。
        /// </summary>

        private static int FindColumn(IReadOnlyList<string> headers, string header)
        {
            string expected = Normalize(header);
            for (int index = 0; index < (headers?.Count ?? 0); index++)
            {
                if (Normalize(headers[index]) == expected)
                {
                    return index;
                }
            }

            return -1;
        }
        /// <summary>
        /// 获取已知标识。
        /// </summary>

        private List<int> GetKnownIds(string sheetName, int current, bool includeSpecial)
        {
            List<int> values = new List<int>();
            if (includeSpecial)
            {
                values.Add(-1);
                values.Add(0);
            }

            if (working?.Tables.TryGetValue(sheetName, out BattleActionTimelineWorkbookTable table) == true)
            {
                values.AddRange(table.Records.Select(record => record.RowId));
            }

            if (current != 0 && !values.Contains(current))
            {
                values.Add(current);
            }

            return values.Distinct().OrderBy(value => value).ToList();
        }
        /// <summary>
        /// 获取动作标识。
        /// </summary>

        private List<int> GetActionIds(int current)
        {
            List<int> values = new List<int> { 0 };
            BattleActionTimelineWorkbookTable table = GetWorkingTable(CharacterActionSheet);
            if (table != null)
            {
                for (int index = 0; index < table.Records.Count; index++)
                {
                    MappingRow row = ReadRow(table, table.Records[index]);
                    values.AddRange(row.Entries.Select(entry => entry.ResultId));
                }
            }

            if (current != 0)
            {
                values.Add(current);
            }

            return values.Distinct().OrderBy(value => value).ToList();
        }
        /// <summary>
        /// 解析整数。
        /// </summary>

        private static int ParseInt(string value)
        {
            return int.TryParse(
                (value ?? string.Empty).Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result)
                ? result
                : 0;
        }
        /// <summary>
        /// 从映射记录中读取指定字段值；记录或字段不存在时返回空字符串。
        /// </summary>

        private static string GetValue(
            IReadOnlyDictionary<string, string> values,
            string key,
            string fallback)
        {
            return values != null && values.TryGetValue(key, out string value)
                ? value
                : fallback;
        }
        /// <summary>
        /// 规范化当前 `MappingRow` 实例。
        /// </summary>

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().Replace("_", string.Empty).ToLowerInvariant();
        }
        /// <summary>
        /// 根据 `sheetName` 构建显示表页名称并返回结果。
        /// </summary>

        private static string DisplaySheetName(string sheetName)
        {
            if (string.Equals(sheetName, CharacterActionSheet, StringComparison.Ordinal))
            {
                return "角色动作映射表";
            }

            if (string.Equals(sheetName, MultiEquipmentSheet, StringComparison.Ordinal))
            {
                return "多装备技能映射表";
            }

            return sheetName ?? string.Empty;
        }
        /// <summary>
        /// 根据 `matchType` 构建显示匹配`Type`并返回结果。
        /// </summary>

        private static string DisplayMatchType(string matchType)
        {
            switch (matchType)
            {
                case "ExactCombination":
                    return "精确组合";
                case "CharacterDefault":
                    return "角色默认";
                case "GlobalDefault":
                    return "全局默认";
                default:
                    return matchType ?? string.Empty;
            }
        }
        /// <summary>
        /// 复制快照。
        /// </summary>

        private static BattleActionTimelineWorkbookSnapshot CloneSnapshot(
            BattleActionTimelineWorkbookSnapshot source)
        {
            BattleActionTimelineWorkbookSnapshot result = new BattleActionTimelineWorkbookSnapshot
            {
                WorkbookPath = source.WorkbookPath,
                ContentHash = source.ContentHash,
                AllSheetNames = source.AllSheetNames != null
                    ? (string[])source.AllSheetNames.Clone()
                    : Array.Empty<string>(),
            };
            foreach (KeyValuePair<string, BattleActionTimelineWorkbookTable> pair in source.Tables)
            {
                result.Tables.Add(pair.Key, pair.Value.Clone());
            }

            return result;
        }
    }
}
#endif
