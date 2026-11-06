#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Game.EditorTools
{
    [Serializable]
    internal sealed class BattleActionTimelineDocument
    {
        public List<BattleActionTimelineTableData> tables =
            new List<BattleActionTimelineTableData>();
        /// <summary>
        /// 创建当前数据的独立副本，避免后续修改共享可变状态。
        /// </summary>

        public BattleActionTimelineDocument Clone()
        {
            BattleActionTimelineDocument clone = JsonUtility.FromJson<
                BattleActionTimelineDocument>(JsonUtility.ToJson(this));
            clone ??= new BattleActionTimelineDocument();
            clone.EnsureLists();
            return clone;
        }
        /// <summary>
        /// 确保`Lists`。
        /// </summary>

        public void EnsureLists()
        {
            tables ??= new List<BattleActionTimelineTableData>();
            for (int tableIndex = 0; tableIndex < tables.Count; tableIndex++)
            {
                BattleActionTimelineTableData table = tables[tableIndex];
                if (table == null)
                {
                    table = new BattleActionTimelineTableData();
                    tables[tableIndex] = table;
                }

                table.headers ??= Array.Empty<string>();
                table.records ??= new List<BattleActionTimelineRecordData>();
                table.sourceRowIds ??= new List<int>();
                table.inlineSourceSheetName ??= string.Empty;
                table.inlineSourceColumnName ??= string.Empty;
                table.inlineFields ??=
                    new List<BattleActionTimelineInlineFieldData>();
                table.inlineOwners ??=
                    new List<BattleActionTimelineInlineOwnerData>();
                for (int fieldIndex = 0;
                    fieldIndex < table.inlineFields.Count;
                    fieldIndex++)
                {
                    BattleActionTimelineInlineFieldData field =
                        table.inlineFields[fieldIndex];
                    if (field == null)
                    {
                        field = new BattleActionTimelineInlineFieldData();
                        table.inlineFields[fieldIndex] = field;
                    }

                    field.headerName ??= string.Empty;
                    field.serializedName ??= string.Empty;
                }

                for (int ownerIndex = 0;
                    ownerIndex < table.inlineOwners.Count;
                    ownerIndex++)
                {
                    BattleActionTimelineInlineOwnerData owner =
                        table.inlineOwners[ownerIndex];
                    if (owner == null)
                    {
                        owner = new BattleActionTimelineInlineOwnerData();
                        table.inlineOwners[ownerIndex] = owner;
                    }

                    owner.originalCell ??= string.Empty;
                    owner.baselineSignature ??= string.Empty;
                    owner.parseError ??= string.Empty;
                }

                for (int recordIndex = 0; recordIndex < table.records.Count; recordIndex++)
                {
                    BattleActionTimelineRecordData record = table.records[recordIndex];
                    if (record == null)
                    {
                        record = new BattleActionTimelineRecordData();
                        table.records[recordIndex] = record;
                    }

                    record.cells ??= new string[table.headers.Length];
                    if (record.cells.Length != table.headers.Length)
                    {
                        string[] resized = new string[table.headers.Length];
                        Array.Copy(
                            record.cells,
                            resized,
                            Math.Min(record.cells.Length, resized.Length));
                        record.cells = resized;
                    }

                    for (int cellIndex = 0; cellIndex < record.cells.Length; cellIndex++)
                    {
                        record.cells[cellIndex] ??= string.Empty;
                    }
                }
            }
        }
        /// <summary>
        /// 查找数据表。
        /// </summary>

        public BattleActionTimelineTableData FindTable(string sheetName)
        {
            return tables.FirstOrDefault(table => table != null && string.Equals(
                table.sheetName,
                sheetName,
                StringComparison.Ordinal));
        }
        /// <summary>
        /// 查找记录。
        /// </summary>

        public BattleActionTimelineRecordData FindRecord(string sheetName, int rowId)
        {
            return FindTable(sheetName)?.records.FirstOrDefault(
                record => record != null && record.rowId == rowId);
        }
        /// <summary>
        /// 构建动作数据表并返回结果。
        /// </summary>

        public IEnumerable<BattleActionTimelineTableData> ActionTables()
        {
            return tables.Where(BattleActionTimelineSchema.IsActiveSingleTable);
        }

        public bool HasActionStructure => ActionTables().Any();
        /// <summary>
        /// 将 `snapshot` 转换为 `BattleActionTimelineDocument` 值并返回。
        /// </summary>

        public static BattleActionTimelineDocument FromSnapshot(
            BattleActionTimelineWorkbookSnapshot snapshot)
        {
            BattleActionTimelineDocument result = new BattleActionTimelineDocument();
            if (snapshot == null)
            {
                return result;
            }

            foreach (BattleActionTimelineWorkbookTable source in snapshot.Tables.Values)
            {
                BattleActionTimelineTableData table = new BattleActionTimelineTableData
                {
                    sheetName = source.Name,
                    headers = source.Headers != null
                        ? (string[])source.Headers.Clone()
                        : Array.Empty<string>(),
                };
                for (int index = 0; index < source.Records.Count; index++)
                {
                    BattleActionTimelineWorkbookRecord record = source.Records[index];
                    table.records.Add(new BattleActionTimelineRecordData
                    {
                        rowId = record.RowId,
                        cells = record.Cells != null
                            ? (string[])record.Cells.Clone()
                            : Array.Empty<string>(),
                    });
                }

                result.tables.Add(table);
            }

            result.EnsureLists();
            result.AddInlineProjections();
            result.EnsureLists();
            foreach (BattleActionTimelineTableData table in result.tables)
            {
                table.SetSourceRowIds(table.records.Select(record => record.rowId));
            }
            return result;
        }

        /// <summary>旧草稿恢复时只补充已验证源表的编号基线，不替换任何编辑记录。</summary>
        public void RestoreSourceRowIdBaselines(BattleActionTimelineWorkbookSnapshot snapshot)
        {
            if (snapshot == null || tables.All(table => table.hasSourceRowIdBaseline)) return;
            BattleActionTimelineDocument source = FromSnapshot(snapshot);
            foreach (BattleActionTimelineTableData table in tables.Where(table => !table.hasSourceRowIdBaseline))
            {
                table.SetSourceRowIds(source.FindTable(table.sheetName)?.sourceRowIds ?? Enumerable.Empty<int>());
            }
        }
        /// <summary>
        /// 添加内联投影。
        /// </summary>

        private void AddInlineProjections()
        {
            List<BattleActionTimelineTableData> sourceTables = tables
                .Where(table => table != null && !table.isInlineProjection)
                .ToList();
            bool hasExecutionStepSheet = sourceTables.Any(
                BattleActionTimelineSchema.IsExecutionStepTable);
            bool hasKeyframeSheet = sourceTables.Any(
                BattleActionTimelineSchema.IsKeyframeTable);

            for (int index = 0; index < sourceTables.Count; index++)
            {
                BattleActionTimelineTableData source = sourceTables[index];
                if (!hasExecutionStepSheet &&
                    BattleActionTimelineSchema.IsActiveSingleTable(source))
                {
                    AddInlineProjection(
                        source,
                        new[] { "executionSteps", "steps" },
                        "ExecutionStep",
                        "activeSingleId",
                        InlineField("triggerTime", "TriggerTime"),
                        InlineField("stepType", "StepType"),
                        InlineField("effectConfigId", "EffectConfigId"),
                        InlineField("interactionType", "InteractionType"));
                }

                if (!hasKeyframeSheet &&
                    BattleActionTimelineSchema.IsShapeTable(source))
                {
                    AddInlineProjection(
                        source,
                        new[] { "transformKeyframes" },
                        "ShapeTransformKeyframe",
                        "shapeId",
                        TransformInlineFields());
                }
            }
        }
        /// <summary>
        /// 添加内联投影。
        /// </summary>

        private void AddInlineProjection(
            BattleActionTimelineTableData source,
            string[] sourceColumnAliases,
            string suffix,
            string ownerHeader,
            params BattleActionTimelineInlineFieldData[] fields)
        {
            int sourceColumn = BattleActionTimelineSchema.FindColumn(
                source,
                sourceColumnAliases);
            if (sourceColumn < 0)
            {
                return;
            }

            BattleActionTimelineTableData projection =
                new BattleActionTimelineTableData
                {
                    sheetName = source.sheetName + "::" + suffix,
                    isInlineProjection = true,
                    inlineSourceSheetName = source.sheetName,
                    inlineSourceColumnName = source.headers[sourceColumn],
                    inlineFields = fields.Select(CloneInlineField).ToList(),
                    headers = new[] { "id", ownerHeader }
                        .Concat(fields.Select(field => field.headerName))
                        .ToArray(),
                };
            int nextRowId = 1;
            for (int recordIndex = 0;
                recordIndex < source.records.Count;
                recordIndex++)
            {
                BattleActionTimelineRecordData owner = source.records[recordIndex];
                string originalCell = sourceColumn < owner.cells.Length
                    ? owner.cells[sourceColumn] ?? string.Empty
                    : string.Empty;
                BattleActionTimelineInlineOwnerData ownerData =
                    new BattleActionTimelineInlineOwnerData
                    {
                        ownerRowId = owner.rowId,
                        originalCell = originalCell,
                    };
                if (BattleActionTimelineInlineStructCodec.TryParse(
                        originalCell,
                        out List<Dictionary<string, string>> values,
                        out string parseError))
                {
                    for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
                    {
                        Dictionary<string, string> value = values[valueIndex];
                        string[] cells = new string[projection.headers.Length];
                        cells[0] = nextRowId.ToString(CultureInfo.InvariantCulture);
                        cells[1] = owner.rowId.ToString(CultureInfo.InvariantCulture);
                        for (int fieldIndex = 0;
                            fieldIndex < projection.inlineFields.Count;
                            fieldIndex++)
                        {
                            BattleActionTimelineInlineFieldData field =
                                projection.inlineFields[fieldIndex];
                            cells[fieldIndex + 2] = value.TryGetValue(
                                field.serializedName,
                                out string cell)
                                ? cell
                                : string.Empty;
                        }

                        projection.records.Add(new BattleActionTimelineRecordData
                        {
                            rowId = nextRowId,
                            cells = cells,
                        });
                        nextRowId++;
                    }
                }
                else
                {
                    ownerData.parseError = parseError;
                }

                projection.inlineOwners.Add(ownerData);
            }

            for (int ownerIndex = 0;
                ownerIndex < projection.inlineOwners.Count;
                ownerIndex++)
            {
                BattleActionTimelineInlineOwnerData owner =
                    projection.inlineOwners[ownerIndex];
                owner.baselineSignature = InlineOwnerSignature(
                    projection,
                    owner.ownerRowId);
            }

            tables.Add(projection);
        }

        private Dictionary<BattleActionTimelineRecordKey, string[]>
            BuildInlineMaterializedRecords()
        {
            Dictionary<BattleActionTimelineRecordKey, string[]> result =
                new Dictionary<BattleActionTimelineRecordKey, string[]>();
            foreach (BattleActionTimelineTableData projection in tables.Where(
                table => table != null && table.isInlineProjection))
            {
                BattleActionTimelineTableData source =
                    FindTable(projection.inlineSourceSheetName);
                int sourceColumn = BattleActionTimelineSchema.FindColumn(
                    source,
                    projection.inlineSourceColumnName);
                if (source == null || sourceColumn < 0)
                {
                    continue;
                }

                for (int ownerIndex = 0;
                    ownerIndex < projection.inlineOwners.Count;
                    ownerIndex++)
                {
                    BattleActionTimelineInlineOwnerData owner =
                        projection.inlineOwners[ownerIndex];
                    if (owner == null || !string.IsNullOrWhiteSpace(owner.parseError))
                    {
                        continue;
                    }

                    string currentSignature = InlineOwnerSignature(
                        projection,
                        owner.ownerRowId);
                    BattleActionTimelineRecordData[] ownedRecords = projection.records.Where(record =>
                        InlineOwnerId(projection, record) == owner.ownerRowId).ToArray();
                    if (string.Equals(
                            currentSignature,
                            owner.baselineSignature,
                            StringComparison.Ordinal) &&
                        !BattleActionTimelineInlineStructCodec.HasMissingTransformDefaults(
                            projection.inlineFields, ownedRecords))
                    {
                        continue;
                    }

                    BattleActionTimelineRecordData sourceRecord =
                        source.records.FirstOrDefault(
                            record => record != null &&
                                record.rowId == owner.ownerRowId);
                    if (sourceRecord == null)
                    {
                        continue;
                    }

                    BattleActionTimelineRecordKey key =
                        new BattleActionTimelineRecordKey(
                            source.sheetName,
                            sourceRecord.rowId);
                    if (!result.TryGetValue(key, out string[] cells))
                    {
                        cells = (string[])sourceRecord.cells.Clone();
                        result.Add(key, cells);
                    }

                    cells[sourceColumn] =
                        BattleActionTimelineInlineStructCodec.Serialize(
                            projection.inlineFields,
                            ownedRecords);
                }
            }

            return result;
        }
        /// <summary>
        /// 根据 `table`、`ownerRowId` 构建内联所有者签名并返回结果。
        /// </summary>

        private static string InlineOwnerSignature(
            BattleActionTimelineTableData table,
            int ownerRowId)
        {
            StringBuilder signature = new StringBuilder();
            foreach (BattleActionTimelineRecordData record in table.records.Where(
                value => InlineOwnerId(table, value) == ownerRowId))
            {
                for (int index = 2; index < record.cells.Length; index++)
                {
                    string value = record.cells[index] ?? string.Empty;
                    signature.Append(value.Length)
                        .Append(':')
                        .Append(value)
                        .Append('|');
                }

                signature.Append(';');
            }

            return signature.ToString();
        }
        /// <summary>
        /// 根据 `table`、`record` 构建内联所有者`Id`并返回结果。
        /// </summary>

        private static int InlineOwnerId(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record)
        {
            if (table == null || record?.cells == null || record.cells.Length < 2)
            {
                return 0;
            }

            return int.TryParse(
                record.cells[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int ownerId)
                ? ownerId
                : 0;
        }
        /// <summary>
        /// 根据 `headerName`、`serializedName` 构建内联字段并返回结果。
        /// </summary>

        private static BattleActionTimelineInlineFieldData InlineField(
            string headerName,
            string serializedName)
        {
            return new BattleActionTimelineInlineFieldData
            {
                headerName = headerName,
                serializedName = serializedName,
            };
        }
        /// <summary>
        /// 复制内联字段。
        /// </summary>

        private static BattleActionTimelineInlineFieldData CloneInlineField(
            BattleActionTimelineInlineFieldData source)
        {
            return InlineField(source.headerName, source.serializedName);
        }
        /// <summary>
        /// 构建转换内联`Fields`并返回结果。
        /// </summary>

        private static BattleActionTimelineInlineFieldData[] TransformInlineFields()
        {
            return new[]
            {
                InlineField("localTime", "LocalTime"),
                InlineField("offsetX", "OffsetX"),
                InlineField("offsetY", "OffsetY"),
                InlineField("rotationDegrees", "RotationDegrees"),
                InlineField("scaleX", "ScaleX"),
                InlineField("scaleY", "ScaleY"),
                InlineField("interpolation", "Interpolation"),
            };
        }
        /// <summary>
        /// 构建写入集合。
        /// </summary>

        public BattleActionTimelineWorkbookWriteSet BuildWriteSet(
            BattleActionTimelineWorkbookSnapshot baseline)
        {
            BattleActionTimelineWorkbookWriteSet result =
                new BattleActionTimelineWorkbookWriteSet();
            if (baseline == null)
            {
                return result;
            }

            Dictionary<BattleActionTimelineRecordKey, string[]> materialized =
                BuildInlineMaterializedRecords();
            foreach (KeyValuePair<string, BattleActionTimelineWorkbookTable> pair
                in baseline.Tables)
            {
                BattleActionTimelineTableData current = FindTable(pair.Key);
                if (current == null)
                {
                    continue;
                }

                Dictionary<int, BattleActionTimelineRecordData> currentById =
                    current.records
                        .Where(record => record != null && record.rowId > 0)
                        .ToDictionary(record => record.rowId);
                foreach (BattleActionTimelineWorkbookRecord original
                    in pair.Value.Records)
                {
                    BattleActionTimelineRecordKey key =
                        new BattleActionTimelineRecordKey(pair.Key, original.RowId);
                    if (!currentById.TryGetValue(
                            original.RowId,
                            out BattleActionTimelineRecordData currentRecord))
                    {
                        result.Deletions.Add(key);
                    }
                    else
                    {
                        string[] currentCells = materialized.TryGetValue(
                            key,
                            out string[] inlineCells)
                            ? inlineCells
                            : currentRecord.cells;
                        if (!BattleActionTimelineWorkbookBridge.CellsEqual(
                                original.Cells,
                                currentCells))
                        {
                            result.Replacements.Add(
                                key,
                                (string[])currentCells.Clone());
                        }
                    }
                }

                HashSet<int> originalIds = new HashSet<int>(
                    pair.Value.Records.Select(record => record.RowId));
                foreach (BattleActionTimelineRecordData currentRecord
                    in current.records)
                {
                    if (currentRecord != null && currentRecord.rowId > 0 &&
                        !originalIds.Contains(currentRecord.rowId))
                    {
                        BattleActionTimelineRecordKey key =
                            new BattleActionTimelineRecordKey(
                                pair.Key,
                                currentRecord.rowId);
                        string[] currentCells = materialized.TryGetValue(
                            key,
                            out string[] inlineCells)
                            ? inlineCells
                            : currentRecord.cells;
                        result.Replacements.Add(
                            key,
                            (string[])currentCells.Clone());
                    }
                }
            }

            return result;
        }
        /// <summary>
        /// 生成当前时间轴记录用于比较和去重的规范签名。
        /// </summary>

        public string CanonicalSignature()
        {
            StringBuilder text = new StringBuilder(8192);
            foreach (BattleActionTimelineTableData table in tables
                .Where(value => value != null)
                .OrderBy(value => value.sheetName, StringComparer.Ordinal))
            {
                text.Append('[').Append(table.sheetName).AppendLine("]");
                text.AppendLine(string.Join("\t", table.headers ?? Array.Empty<string>()));
                foreach (BattleActionTimelineRecordData record in table.records)
                {
                    if (record == null)
                    {
                        continue;
                    }

                    text.Append(record.rowId.ToString(CultureInfo.InvariantCulture))
                        .Append('|')
                        .AppendLine(string.Join("\t", record.cells ?? Array.Empty<string>()));
                }
            }

            return text.ToString().Replace("\r\n", "\n").TrimEnd();
        }
    }

    [Serializable]
    internal sealed class BattleActionTimelineTableData
    {
        public string sheetName = string.Empty;
        public string[] headers = Array.Empty<string>();
        public List<BattleActionTimelineRecordData> records =
            new List<BattleActionTimelineRecordData>();
        // 编号基线随草稿/Undo 序列化；修改正式记录的字段不等于创建新记录。
        public bool hasSourceRowIdBaseline;
        public List<int> sourceRowIds = new List<int>();
        public bool isInlineProjection;
        public string inlineSourceSheetName = string.Empty;
        public string inlineSourceColumnName = string.Empty;
        public List<BattleActionTimelineInlineFieldData> inlineFields =
            new List<BattleActionTimelineInlineFieldData>();
        public List<BattleActionTimelineInlineOwnerData> inlineOwners =
            new List<BattleActionTimelineInlineOwnerData>();
        /// <summary>
        /// 有未保存新增记录时取其最大 ID + 1，否则取正式配置最大 ID + 1。
        /// </summary>

        public int AllocateRowId(IEnumerable<BattleActionTimelineRecordData> pendingRecords = null,
            ISet<int> removedRowIds = null)
        {
            var officialIds = new HashSet<int>(hasSourceRowIdBaseline
                ? sourceRowIds ?? Enumerable.Empty<int>() : Enumerable.Empty<int>());
            IEnumerable<BattleActionTimelineRecordData> current = records.Where(record =>
                record == null || removedRowIds == null || !removedRowIds.Contains(record.rowId));
            if (pendingRecords != null) current = current.Concat(pendingRecords);
            int draftMaximum = 0;
            foreach (BattleActionTimelineRecordData record in current)
            {
                if (record != null && !officialIds.Contains(record.rowId))
                    draftMaximum = Math.Max(draftMaximum, record.rowId);
            }

            int maximum = draftMaximum > 0 ? draftMaximum : officialIds.DefaultIfEmpty(0).Max();
            if (maximum == int.MaxValue)
            {
                throw new InvalidOperationException(
                    sheetName + " 的稳定 rowId 已耗尽。");
            }

            int next = Math.Max(1, maximum + 1);
            if (officialIds.Contains(next))
                throw new InvalidOperationException(sheetName + " 的新增 ID " + next + " 与正式配置冲突，未创建记录。");
            return next;
        }

        public void SetSourceRowIds(IEnumerable<int> ids)
        {
            sourceRowIds = ids.Where(id => id > 0).Distinct().ToList();
            hasSourceRowIdBaseline = true;
        }
    }

    [Serializable]
    internal sealed class BattleActionTimelineRecordData
    {
        public int rowId;
        public string[] cells = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class BattleActionTimelineInlineFieldData
    {
        public string headerName = string.Empty;
        public string serializedName = string.Empty;
    }

    [Serializable]
    internal sealed class BattleActionTimelineInlineOwnerData
    {
        public int ownerRowId;
        public string originalCell = string.Empty;
        public string baselineSignature = string.Empty;
        public string parseError = string.Empty;
    }

    internal enum BattleActionTimelineFacing
    {
        Right = 1,
        Left = -1,
    }

    internal readonly struct BattleActionTimelineTransform
    {
        /// <summary>
        /// 创建 `BattleActionTimelineTransform`，记录`offset`（偏移）、`rotationDegrees`（rotationDegrees）和`scale`（缩放）字段，形成可供后续流程传递的数据对象。
        /// </summary>
        public BattleActionTimelineTransform(
            Vector2 offset,
            float rotationDegrees,
            Vector2 scale)
        {
            Offset = offset;
            RotationDegrees = rotationDegrees;
            Scale = scale;
        }
        /// <summary>
        /// 获取当前 `BattleActionTimelineTransform` 实例的身份。
        /// </summary>

        public static BattleActionTimelineTransform Identity =>
            new BattleActionTimelineTransform(Vector2.zero, 0f, Vector2.one);
        /// <summary>
        /// 获取当前 `BattleActionTimelineTransform` 实例的偏移。
        /// </summary>

        public Vector2 Offset { get; }
        /// <summary>
        /// 获取当前 `BattleActionTimelineTransform` 实例的旋转角度。
        /// </summary>
        public float RotationDegrees { get; }
        /// <summary>
        /// 获取当前 `BattleActionTimelineTransform` 实例的缩放。
        /// </summary>
        public Vector2 Scale { get; }
    }

    internal readonly struct BattleActionTimelinePhaseTimes
    {
        /// <summary>
        /// 创建 `BattleActionTimelinePhaseTimes`，记录`startupEnd`（startup结束）、`switchWindowStart`（switch窗口开始）、`recoveryStart`（recovery开始）和`duration`（持续时间）字段，形成可供后续流程传递的数据对象。
        /// </summary>
        public BattleActionTimelinePhaseTimes(
            double startupEnd,
            double switchWindowStart,
            double recoveryStart,
            double duration)
        {
            StartupEnd = startupEnd;
            SwitchWindowStart = switchWindowStart;
            RecoveryStart = recoveryStart;
            Duration = duration;
        }
        /// <summary>
        /// 获取当前 `BattleActionTimelinePhaseTimes` 实例的启动结束。
        /// </summary>

        public double StartupEnd { get; }
        /// <summary>
        /// 获取当前 `BattleActionTimelinePhaseTimes` 实例的切换窗口开始。
        /// </summary>
        public double SwitchWindowStart { get; }
        /// <summary>
        /// 获取当前 `BattleActionTimelinePhaseTimes` 实例的恢复开始。
        /// </summary>
        public double RecoveryStart { get; }
        /// <summary>
        /// 获取当前 `BattleActionTimelinePhaseTimes` 实例的持续时间。
        /// </summary>
        public double Duration { get; }
    }

    internal static class BattleActionTimelineTime
    {
        public const double Epsilon = 0.0000001d;
        /// <summary>
        /// 将秒转换为帧。
        /// </summary>

        public static int SecondsToFrame(double seconds, double frameRate)
        {
            ValidateFrameRate(frameRate);
            if (!IsFinite(seconds))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            double frames = seconds * frameRate;
            if (frames > int.MaxValue || frames < int.MinValue)
            {
                throw new OverflowException("时间超出可表示帧范围。");
            }

            return (int)Math.Round(frames, MidpointRounding.AwayFromZero);
        }
        /// <summary>
        /// 将帧转换为秒。
        /// </summary>

        public static double FrameToSeconds(int frame, double frameRate)
        {
            ValidateFrameRate(frameRate);
            return frame / frameRate;
        }
        /// <summary>
        /// 根据 `seconds`、`frameRate` 吸附秒数并返回结果。
        /// </summary>

        public static double SnapSeconds(double seconds, double frameRate)
        {
            return FrameToSeconds(SecondsToFrame(seconds, frameRate), frameRate);
        }

        /// <summary>编辑时间默认连续；仅显式开启吸附时才换算成整数帧。</summary>
        public static double NormalizeEditedSeconds(double seconds, double frameRate, bool snapToFrames)
        {
            if (!IsFinite(seconds)) return 0d;
            return Math.Max(0d, snapToFrames ? SnapSeconds(seconds, frameRate) : seconds);
        }

        public static double MinimumDuration(double frameRate, bool snapToFrames)
        {
            return snapToFrames ? FrameToSeconds(1, frameRate) : Epsilon;
        }
        /// <summary>
        /// 校验 `Phases` 的结构与业务约束，通过时返回 `true`；校验失败时通过 `error` 返回原因。
        /// </summary>

        public static bool TryValidatePhases(
            BattleActionTimelinePhaseTimes phases,
            out string error)
        {
            if (!IsFinite(phases.StartupEnd) ||
                !IsFinite(phases.SwitchWindowStart) ||
                !IsFinite(phases.RecoveryStart) ||
                !IsFinite(phases.Duration) ||
                phases.StartupEnd < 0d ||
                phases.SwitchWindowStart < phases.StartupEnd ||
                phases.RecoveryStart < phases.SwitchWindowStart ||
                phases.Duration < phases.RecoveryStart ||
                phases.Duration <= 0d)
            {
                error =
                    "阶段时间必须满足 0 <= startupEnd <= switchWindowStart <= " +
                    "recoveryStart <= actionDuration，且 actionDuration > 0。";
                return false;
            }

            error = string.Empty;
            return true;
        }
        /// <summary>
        /// 检查数值是否为有限数，即既不是 NaN，也不是正负无穷。
        /// </summary>

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
        /// <summary>
        /// 校验帧`Rate`。
        /// </summary>

        private static void ValidateFrameRate(double frameRate)
        {
            if (!IsFinite(frameRate) || frameRate <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate));
            }
        }
    }

    internal sealed class BattleActionTimelineIndependentAttack
    {
        public BattleActionTimelineTableData StepTable;
        public BattleActionTimelineRecordData Step;
        public int SpawnId;
        public int BodyId;
        public bool IsProjectile;
        public int ProjectileId;
        public int MovementId;
        public bool Blank;
    }

    /// <summary>只修改编辑草稿；攻击体步骤默认独立复制完整配置链，不支持的类型明确拒绝而不复用旧ID。</summary>
    internal static class BattleActionTimelineAttackAuthoring
    {
        // 只回收已验证正式基线之外、当前已无引用的新配置；正式行不因删除步骤而被删除。
        private sealed class UnusedDraftConfigurations
        {
            private readonly BattleActionTimelineDocument document;
            private readonly Dictionary<BattleActionTimelineTableData, HashSet<int>> removed =
                new Dictionary<BattleActionTimelineTableData, HashSet<int>>();

            public UnusedDraftConfigurations(BattleActionTimelineDocument document)
            {
                this.document = document;
                Prune(BattleActionTimelineSchema.IsMeleeSpawnTable,
                    (table, row) => HasStepConfigurationReference(document, row.rowId, false));
                Prune(BattleActionTimelineSchema.IsProjectileLaunchTable,
                    (table, row) => HasStepConfigurationReference(document, row.rowId, true));
                Prune(BattleActionTimelineSchema.IsProjectileTable,
                    (table, row) => HasReference(BattleActionTimelineSchema.IsProjectileLaunchTable,
                        row.rowId, "projectileId"));
                Prune(BattleActionTimelineSchema.IsAttackBodyTable,
                    (table, row) => HasReference(parent => BattleActionTimelineSchema.IsMeleeSpawnTable(parent) ||
                        BattleActionTimelineSchema.IsProjectileTable(parent), row.rowId,
                        BattleActionTimelineSchema.AttackBodyIdAliases));
                PruneMovement(RefData.EnumBattleProjectileMovementType.Linear);
                PruneMovement(RefData.EnumBattleProjectileMovementType.Ballistic);

                // 固定按攻击体→Shape→关键帧回收，父行已被手动删除时同样不能留下孤儿轨迹。
                Prune(BattleActionTimelineSchema.IsShapeTable,
                    (table, row) => ParentExists(BattleActionTimelineSchema.IsAttackBodyTable,
                        BattleActionTimelineSchema.GetInt(table, row, 0, BattleActionTimelineSchema.AttackBodyIdAliases)));
                Prune(BattleActionTimelineSchema.IsKeyframeTable,
                    (table, row) => ParentExists(BattleActionTimelineSchema.IsShapeTable,
                        BattleActionTimelineSchema.GetInt(table, row, 0, "shapeId")));
            }

            public ISet<int> RemovedIds(BattleActionTimelineTableData table) =>
                removed.TryGetValue(table, out var ids) ? ids : null;

            private bool HasReference(Func<BattleActionTimelineTableData, bool> matches, int id, params string[] columns) =>
                document.tables.Where(matches).Any(table => table.records.Any(row =>
                    !IsRemoved(table, row) && BattleActionTimelineSchema.GetInt(table, row, 0, columns) == id));

            private bool IsRemoved(BattleActionTimelineTableData table, BattleActionTimelineRecordData row) =>
                row != null && removed.TryGetValue(table, out var ids) && ids.Contains(row.rowId);

            private bool ParentExists(Func<BattleActionTimelineTableData, bool> matches, int id) =>
                document.tables.Where(matches).Any(table => table.records.Any(row =>
                    row.rowId == id && !IsRemoved(table, row)));

            private void Prune(Func<BattleActionTimelineTableData, bool> matches,
                Func<BattleActionTimelineTableData, BattleActionTimelineRecordData, bool> used)
            {
                foreach (BattleActionTimelineTableData table in document.tables.Where(matches))
                {
                    if (!table.hasSourceRowIdBaseline) continue; // 未能核对旧草稿源表时，不能猜哪些行可删。
                    var official = new HashSet<int>(table.sourceRowIds);
                    foreach (BattleActionTimelineRecordData row in table.records)
                    {
                        if (row == null || official.Contains(row.rowId) || used(table, row)) continue;
                        if (!removed.TryGetValue(table, out var ids)) removed.Add(table, ids = new HashSet<int>());
                        ids.Add(row.rowId);
                    }
                }
            }

            private void PruneMovement(RefData.EnumBattleProjectileMovementType type)
            {
                // 与创建时的移动表解析一致，不额外依赖表名含某个单词。
                var movementTable = document.tables.FirstOrDefault(table => type == RefData.EnumBattleProjectileMovementType.Linear
                        ? BattleActionTimelineSchema.IsLinearMovementTable(table)
                        : BattleActionTimelineSchema.IsBallisticMovementTable(table));
                Prune(table => table == movementTable,
                    (movementTable, movement) => document.tables.Where(BattleActionTimelineSchema.IsProjectileTable)
                        .Any(table => table.records.Any(row => !IsRemoved(table, row) &&
                            BattleActionTimelineSchema.GetInt(table, row, 0, "movementConfigId") == movement.rowId &&
                            (!TryReadMovementType(BattleActionTimelineSchema.Get(table, row, "movementType"), out var actual) ||
                                actual == type))));
            }

            public void Apply()
            {
                foreach (var pair in removed) pair.Key.records.RemoveAll(row => row != null && pair.Value.Contains(row.rowId));
                foreach (BattleActionTimelineTableData child in document.tables.Where(table => table.isInlineProjection))
                {
                    BattleActionTimelineTableData parent = document.FindTable(child.inlineSourceSheetName);
                    if (parent != null)
                        child.inlineOwners.RemoveAll(owner => !parent.records.Any(row => row.rowId == owner.ownerRowId));
                }
            }
        }

        public static bool DeleteStep(BattleActionTimelineDocument document, string stepSheet, int stepId)
        {
            BattleActionTimelineTableData table = document?.FindTable(stepSheet);
            BattleActionTimelineRecordData step = BattleActionTimelineSchema.FindById(table, stepId);
            if (step == null || !BattleActionTimelineSchema.IsExecutionStepTable(table)) return false;
            RekeyAfterStepDeletion(document, table, step);
            table.records.Remove(step);
            new UnusedDraftConfigurations(document).Apply();
            return true;
        }

        // 步骤下标属于运行时寻址的一部分。删除中间步骤时必须同时移动它后面的整条配置链，
        // 不能只删事件，否则后续步骤会读到前一个攻击体（连同错误的形状和命中效果）。
        private static void RekeyAfterStepDeletion(BattleActionTimelineDocument document,
            BattleActionTimelineTableData steps, BattleActionTimelineRecordData deleted)
        {
            int owner = BattleActionTimelineSchema.GetInt(steps, deleted, 0, BattleActionTimelineSchema.StepOwnerAliases);
            var owned = steps.records.Where(row => BattleActionTimelineSchema.GetInt(
                steps, row, 0, BattleActionTimelineSchema.StepOwnerAliases) == owner).ToArray();
            int removedIndex = Array.IndexOf(owned, deleted);
            var keys = new Dictionary<int, int>();
            for (int index = removedIndex; index < owned.Length; index++)
                keys.Add(BattleActionTimelineSchema.ComposeStepConfigurationId(owner, index),
                    index == removedIndex ? 0 : BattleActionTimelineSchema.ComposeStepConfigurationId(owner, index - 1));
            var changes = new List<Action>();
            var shapeKeys = new Dictionary<int, int>();
            foreach (var table in document.tables.Where(value =>
                BattleActionTimelineSchema.IsMeleeSpawnTable(value) || BattleActionTimelineSchema.IsProjectileLaunchTable(value) ||
                BattleActionTimelineSchema.IsProjectileTable(value) || BattleActionTimelineSchema.IsAttackBodyTable(value) ||
                BattleActionTimelineSchema.IsLinearMovementTable(value) || BattleActionTimelineSchema.IsBallisticMovementTable(value)))
            {
                foreach (var row in table.records.ToArray())
                {
                    if (keys.TryGetValue(row.rowId, out int next))
                    {
                        if (next == 0) { changes.Add(() => table.records.Remove(row)); continue; }
                        changes.Add(() => { row.rowId = next; BattleActionTimelineSchema.SetInt(table, row, next, "id"); });
                    }
                    foreach (string field in new[] { "attackBodyId", "projectileId", "movementConfigId" })
                    {
                        int old = BattleActionTimelineSchema.GetInt(table, row, 0, field);
                        if (keys.TryGetValue(old, out int replacement) && replacement > 0)
                            changes.Add(() => BattleActionTimelineSchema.SetInt(table, row, replacement, field));
                    }
                }
            }
            foreach (var shapes in document.tables.Where(BattleActionTimelineSchema.IsShapeTable))
                foreach (var shape in shapes.records.ToArray())
                    if (keys.TryGetValue(BattleActionTimelineSchema.GetInt(shapes, shape, 0,
                        BattleActionTimelineSchema.AttackBodyIdAliases), out int body))
                    {
                        int shapeId = body == 0 ? 0 : BattleActionTimelineSchema.ComposeShapeId(body,
                            BattleActionTimelineSchema.GetInt(shapes, shape, 0, "shapeIndex"));
                        shapeKeys.Add(shape.rowId, shapeId);
                        if (body == 0) changes.Add(() => shapes.records.Remove(shape));
                        else changes.Add(() =>
                        {
                            shape.rowId = shapeId;
                            BattleActionTimelineSchema.SetInt(shapes, shape, shapeId, "id");
                            BattleActionTimelineSchema.SetInt(shapes, shape, body, BattleActionTimelineSchema.AttackBodyIdAliases);
                        });
                    }
            foreach (var tracks in document.tables.Where(BattleActionTimelineSchema.IsKeyframeTable))
            {
                foreach (var key in tracks.records.ToArray())
                    if (shapeKeys.TryGetValue(BattleActionTimelineSchema.GetInt(tracks, key, 0, "shapeId"), out int shapeId))
                    {
                        if (shapeId == 0) changes.Add(() => tracks.records.Remove(key));
                        else changes.Add(() => BattleActionTimelineSchema.SetInt(tracks, key, shapeId, "shapeId"));
                    }
                foreach (var binding in tracks.inlineOwners.ToArray())
                    if (shapeKeys.TryGetValue(binding.ownerRowId, out int shapeId))
                    {
                        if (shapeId == 0) changes.Add(() => tracks.inlineOwners.Remove(binding));
                        else changes.Add(() => binding.ownerRowId = shapeId);
                    }
            }
            foreach (var change in changes) change();
        }

        private sealed class Context
        {
            public BattleActionTimelineTableData Spawns, Bodies, Shapes, Keys;
            public BattleActionTimelineRecordData Spawn, Body;
            public BattleActionTimelineTableData Projectiles, Movements;
            public BattleActionTimelineRecordData Projectile, Movement;
            public bool IsProjectile;
            public RefData.EnumBattleProjectileMovementType MovementType;
        }

        // 先在内存准备所有新记录，ID耗尽或结构不完整时不留下半套引用。
        private sealed class Plan
        {
            public BattleActionTimelineDocument Document { get; }
            private readonly UnusedDraftConfigurations unused;
            private readonly Dictionary<BattleActionTimelineTableData, List<BattleActionTimelineRecordData>> rows =
                new Dictionary<BattleActionTimelineTableData, List<BattleActionTimelineRecordData>>();
            private readonly List<(BattleActionTimelineTableData Table, BattleActionTimelineInlineOwnerData Owner)> owners =
                new List<(BattleActionTimelineTableData, BattleActionTimelineInlineOwnerData)>();

            public Plan(BattleActionTimelineDocument document)
            {
                Document = document;
                unused = new UnusedDraftConfigurations(document);
            }

            public BattleActionTimelineRecordData New(BattleActionTimelineTableData table,
                BattleActionTimelineRecordData template = null,
                int forcedId = 0)
            {
                if (!rows.TryGetValue(table, out var added))
                    rows.Add(table, added = new List<BattleActionTimelineRecordData>());
                int id = forcedId > 0
                    ? forcedId
                    : table.AllocateRowId(added, unused.RemovedIds(table));
                if (id <= 0 ||
                    table.records.Any(row => row != null && row.rowId == id &&
                        !(unused.RemovedIds(table)?.Contains(row.rowId) ?? false)) ||
                    added.Any(row => row != null && row.rowId == id))
                {
                    throw new InvalidOperationException(
                        $"配置表 {table?.sheetName} 的新记录 ID {id} 已存在或非法。");
                }
                var record = new BattleActionTimelineRecordData
                {
                    rowId = id,
                    cells = template == null ? Enumerable.Repeat(string.Empty, table.headers.Length).ToArray()
                        : (string[])template.cells.Clone(),
                };
                Require(table, "id");
                BattleActionTimelineSchema.SetInt(table, record, id, "id");
                added.Add(record);
                return record;
            }

            public void BindChildren(BattleActionTimelineTableData children,
                BattleActionTimelineTableData parentTable, BattleActionTimelineRecordData parent)
            {
                if (!children.isInlineProjection) return;
                if (children.inlineSourceSheetName != parentTable.sheetName)
                    throw new InvalidOperationException("内联形状或变换轨的所属工作表不匹配，未修改草稿。");
                Require(parentTable, children.inlineSourceColumnName);
                BattleActionTimelineSchema.Set(parentTable, parent, "[]", children.inlineSourceColumnName);
                owners.Add((children, new BattleActionTimelineInlineOwnerData
                {
                    ownerRowId = parent.rowId, originalCell = "[]", baselineSignature = string.Empty,
                }));
            }

            public void Commit()
            {
                unused.Apply();
                foreach (var pair in rows) pair.Key.records.AddRange(pair.Value);
                foreach (var owner in owners) owner.Table.inlineOwners.Add(owner.Owner);
            }
        }

        public static bool TryAddMeleeStep(BattleActionTimelineDocument document, string actionSheet, int actionId,
            double triggerTime, string preferredStepSheet, int preferredStepId,
            out BattleActionTimelineIndependentAttack result, out string error) =>
            TryAddAttackStep(document, actionSheet, actionId, triggerTime,
                RefData.EnumBattleExecutionStepType.SpawnMeleeAttackBody, RefData.EnumBattleProjectileMovementType.Linear,
                preferredStepSheet, preferredStepId, out result, out error);

        public static bool TryAddAttackStep(BattleActionTimelineDocument document, string actionSheet, int actionId,
            double triggerTime, RefData.EnumBattleExecutionStepType stepType,
            RefData.EnumBattleProjectileMovementType movementType, string preferredStepSheet, int preferredStepId,
            out BattleActionTimelineIndependentAttack result, out string error)
        {
            result = null;
            error = string.Empty;
            try
            {
                bool projectile = stepType == RefData.EnumBattleExecutionStepType.LaunchProjectile;
                if (stepType != RefData.EnumBattleExecutionStepType.SpawnMeleeAttackBody && !projectile)
                    throw new InvalidOperationException("此步骤类型尚未接入独立攻击体创建，未创建或复用任何配置。");
                if (projectile && !Enum.IsDefined(typeof(RefData.EnumBattleProjectileMovementType), movementType))
                    throw new InvalidOperationException("不支持的投射物移动类型，未创建配置。");
                var actions = document?.FindTable(actionSheet);
                var action = BattleActionTimelineSchema.FindById(actions, actionId);
                if (action == null || !BattleActionTimelineSchema.IsActiveSingleTable(actions))
                    throw new InvalidOperationException("请先选择要编辑的技能单段。");
                double duration = BattleActionTimelineSchema.ReadPhases(actions, action).Duration;
                if (!BattleActionTimelineTime.IsFinite(triggerTime) || triggerTime < 0d || triggerTime >= duration)
                    throw new InvalidOperationException("请把播放头移到动作结束之前，再新增攻击体步骤。");
                var steps = document.tables.FirstOrDefault(table => BattleActionTimelineSchema.IsExecutionStepTable(table) &&
                    (!table.isInlineProjection || table.inlineSourceSheetName == actionSheet));
                if (steps == null) throw new InvalidOperationException("当前动作缺少可编辑的执行步骤结构。");
                Require(steps, BattleActionTimelineSchema.StepOwnerAliases);
                Require(steps, BattleActionTimelineSchema.StepTypeAliases);
                Require(steps, BattleActionTimelineSchema.TriggerTimeAliases);
                var templateTable = document.FindTable(preferredStepSheet);
                var template = BattleActionTimelineSchema.FindById(templateTable, preferredStepId);
                bool Matches(BattleActionTimelineRecordData row) => row != null &&
                    BattleActionTimelineSchema.GetInt(steps, row, 0, BattleActionTimelineSchema.StepOwnerAliases) == actionId &&
                    (projectile ? BattleActionTimelineSchema.StepIsProjectile(steps, row) &&
                        MatchesProjectileMovement(document, steps, row, movementType)
                        : BattleActionTimelineSchema.StepIsMelee(steps, row));
                if (templateTable != steps || !Matches(template))
                    template = steps.records.FirstOrDefault(Matches);
                var context = projectile ? ResolveProjectile(document, steps, template, movementType) : Resolve(document, steps, template);
                double lifetime = template == null ? (projectile ? 1d : Math.Min(0.2d, duration - triggerTime))
                    : ReadLifetime(context);
                if (!BattleActionTimelineTime.IsFinite(lifetime) || lifetime <= 0d)
                    throw new InvalidOperationException("参考步骤的存活时间无效，未创建新记录。");
                // 投射物可以在动作结束后继续飞行；只有附着动作的近战判定受剩余动作时长限制。
                if (!projectile && triggerTime + lifetime > duration + BattleActionTimelineTime.Epsilon)
                    throw new InvalidOperationException("当前位置剩余时间不足以容纳参考判定，请前移播放头或延长动作时长。");
                int nextStepIndex = steps.records.Count(row =>
                    BattleActionTimelineSchema.GetInt(
                        steps,
                        row,
                        0,
                        BattleActionTimelineSchema.StepOwnerAliases) == actionId);
                int configurationId = BattleActionTimelineSchema.ComposeStepConfigurationId(
                    actionId,
                    nextStepIndex);
                var plan = new Plan(document);
                result = CopyAttack(plan, context, lifetime, configurationId);
                var step = plan.New(steps);
                BattleActionTimelineSchema.SetInt(steps, step, actionId, BattleActionTimelineSchema.StepOwnerAliases);
                BattleActionTimelineSchema.SetDouble(steps, step, triggerTime, BattleActionTimelineSchema.TriggerTimeAliases);
                BattleActionTimelineSchema.Set(steps, step, stepType.ToString(), BattleActionTimelineSchema.StepTypeAliases);
                // 新增步骤默认按普通攻击体处理；只有用户明确选择其它类型时才改写。
                BattleActionTimelineSchema.Set(
                    steps,
                    step,
                    "Normal",
                    BattleActionTimelineSchema.InteractionTypeAliases);
                result.StepTable = steps;
                result.Step = step;
                plan.Commit();
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is OverflowException)
            {
                result = null;
                error = exception.Message;
                return false;
            }
        }

        private static bool HasStepConfigurationReference(
            BattleActionTimelineDocument document,
            int configurationId,
            bool projectile)
        {
            return document.tables
                .Where(BattleActionTimelineSchema.IsExecutionStepTable)
                .Any(table => table.records.Any(row =>
                    (projectile
                        ? BattleActionTimelineSchema.StepIsProjectile(table, row)
                        : BattleActionTimelineSchema.StepIsMelee(table, row)) &&
                    BattleActionTimelineSchema.GetStepConfigurationId(table, row) == configurationId));
        }

        public static bool TryReadMovementType(string text, out RefData.EnumBattleProjectileMovementType type) =>
            Enum.TryParse((text ?? string.Empty).Trim(), true, out type) &&
                Enum.IsDefined(typeof(RefData.EnumBattleProjectileMovementType), type);

        private static double ReadLifetime(Context context) => context.IsProjectile
            ? BattleActionTimelineSchema.GetDouble(context.Projectiles, context.Projectile, double.NaN, "maxLifetime")
            : BattleActionTimelineSchema.GetDouble(context.Spawns, context.Spawn, double.NaN, "activeDuration");

        private static bool MatchesProjectileMovement(BattleActionTimelineDocument document, BattleActionTimelineTableData steps,
            BattleActionTimelineRecordData step, RefData.EnumBattleProjectileMovementType requested)
        {
            var launches = document.tables.FirstOrDefault(BattleActionTimelineSchema.IsProjectileLaunchTable);
            var launch = BattleActionTimelineSchema.FindById(launches,
                BattleActionTimelineSchema.GetStepConfigurationId(steps, step));
            var projectiles = document.tables.FirstOrDefault(BattleActionTimelineSchema.IsProjectileTable);
            var projectile = BattleActionTimelineSchema.FindById(projectiles,
                BattleActionTimelineSchema.GetInt(launches, launch, 0, "projectileId"));
            // 不把断链/非法类型当成“没有模板”偷偷绕过；交给Resolve明确报错。
            return projectile == null || !TryReadMovementType(
                BattleActionTimelineSchema.Get(projectiles, projectile, "movementType"), out var type) || type == requested;
        }

        private static Context ResolveProjectile(BattleActionTimelineDocument document, BattleActionTimelineTableData steps,
            BattleActionTimelineRecordData template, RefData.EnumBattleProjectileMovementType movementType)
        {
            var context = new Context
            {
                IsProjectile = true, MovementType = movementType,
                Spawns = document.tables.FirstOrDefault(BattleActionTimelineSchema.IsProjectileLaunchTable),
                Projectiles = document.tables.FirstOrDefault(BattleActionTimelineSchema.IsProjectileTable),
                Bodies = document.tables.FirstOrDefault(BattleActionTimelineSchema.IsAttackBodyTable),
            };
            if (context.Spawns == null || context.Projectiles == null || context.Bodies == null)
                throw new InvalidOperationException("工作簿缺少投射物发射、投射物或攻击体结构，未创建半套配置。");
            Require(context.Spawns, "projectileId");
            Require(context.Spawns, "directionSource", "directionMode");
            Require(context.Projectiles, "movementType");
            if (template != null)
            {
                context.Spawn = BattleActionTimelineSchema.FindById(context.Spawns,
                    BattleActionTimelineSchema.GetStepConfigurationId(steps, template));
                context.Projectile = BattleActionTimelineSchema.FindById(context.Projectiles,
                    BattleActionTimelineSchema.GetInt(context.Spawns, context.Spawn, 0, "projectileId"));
                context.Body = BattleActionTimelineSchema.FindById(context.Bodies,
                    BattleActionTimelineSchema.GetInt(context.Projectiles, context.Projectile, 0, BattleActionTimelineSchema.AttackBodyIdAliases));
                if (context.Spawn == null || context.Projectile == null || context.Body == null)
                    throw new InvalidOperationException("参考步骤的发射配置、投射物或攻击体引用断开，请先修正引用。");
                if (!TryReadMovementType(BattleActionTimelineSchema.Get(context.Projectiles, context.Projectile, "movementType"),
                    out context.MovementType))
                    throw new InvalidOperationException("参考投射物的移动类型无效，未复制配置。");
            }
            context.Movements = document.tables.FirstOrDefault(table => context.MovementType == RefData.EnumBattleProjectileMovementType.Linear
                ? BattleActionTimelineSchema.IsLinearMovementTable(table) : BattleActionTimelineSchema.IsBallisticMovementTable(table));
            context.Shapes = document.tables.FirstOrDefault(table => BattleActionTimelineSchema.IsShapeTable(table) &&
                (!table.isInlineProjection || table.inlineSourceSheetName == context.Bodies.sheetName));
            context.Keys = document.tables.FirstOrDefault(table => BattleActionTimelineSchema.IsKeyframeTable(table) &&
                (table.isInlineProjection
                    ? table.inlineSourceSheetName == context.Shapes?.sheetName
                    : table.sheetName.IndexOf("ShapeTransformKeyframe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      table.sheetName.IndexOf("AttackShape", StringComparison.OrdinalIgnoreCase) >= 0));
            if (context.Keys == null)
            {
                var genericKeys = document.tables.Where(table => BattleActionTimelineSchema.IsKeyframeTable(table) &&
                    !table.isInlineProjection && BattleActionTimelineSchema.FindKeyframeOwnerColumn(table) >= 0 &&
                    table.sheetName.IndexOf("ShapeTransformKeyframe", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                if (genericKeys.Length == 1) context.Keys = genericKeys[0];
            }
            if (context.Movements == null || context.Shapes == null || context.Keys == null)
                throw new InvalidOperationException("缺少可编辑的投射物移动、形状或变换轨结构，未创建半套配置。");
            Require(context.Shapes, BattleActionTimelineSchema.AttackBodyIdAliases);
            if (BattleActionTimelineSchema.FindKeyframeOwnerColumn(context.Keys) < 0)
                throw new InvalidOperationException("Shape 变换轨缺少所属 Shape 行号。");
            if (template != null)
            {
                context.Movement = BattleActionTimelineSchema.FindById(context.Movements,
                    BattleActionTimelineSchema.GetInt(context.Projectiles, context.Projectile, 0, "movementConfigId"));
                if (context.Movement == null)
                    throw new InvalidOperationException("参考投射物的移动配置引用断开，未修改草稿。");
            }
            return context;
        }

        private static Context Resolve(BattleActionTimelineDocument document, BattleActionTimelineTableData steps,
            BattleActionTimelineRecordData template)
        {
            var context = new Context
            {
                Spawns = document.tables.FirstOrDefault(BattleActionTimelineSchema.IsMeleeSpawnTable),
                Bodies = document.tables.FirstOrDefault(BattleActionTimelineSchema.IsAttackBodyTable),
            };
            if (context.Spawns == null || context.Bodies == null)
                throw new InvalidOperationException("工作簿缺少近战生成配置或攻击体工作表，无法新增独立记录。");
            Require(context.Spawns, BattleActionTimelineSchema.AttackBodyIdAliases);
            Require(context.Spawns, "activeDuration");
            context.Shapes = document.tables.FirstOrDefault(table => BattleActionTimelineSchema.IsShapeTable(table) &&
                (!table.isInlineProjection || table.inlineSourceSheetName == context.Bodies.sheetName));
            context.Keys = document.tables.FirstOrDefault(table => BattleActionTimelineSchema.IsKeyframeTable(table) &&
                (table.isInlineProjection
                    ? table.inlineSourceSheetName == context.Shapes?.sheetName
                    : table.sheetName.IndexOf("ShapeTransformKeyframe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      table.sheetName.IndexOf("AttackShape", StringComparison.OrdinalIgnoreCase) >= 0));
            if (context.Keys == null)
            {
                var genericKeys = document.tables.Where(table => BattleActionTimelineSchema.IsKeyframeTable(table) &&
                    !table.isInlineProjection && BattleActionTimelineSchema.FindKeyframeOwnerColumn(table) >= 0 &&
                    table.sheetName.IndexOf("ShapeTransformKeyframe", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                if (genericKeys.Length == 1) context.Keys = genericKeys[0];
            }
            if (context.Shapes == null || context.Keys == null)
                throw new InvalidOperationException("缺少可编辑的近战形状或变换轨结构，未创建半套配置。");
            Require(context.Shapes, BattleActionTimelineSchema.AttackBodyIdAliases);
            if (BattleActionTimelineSchema.FindKeyframeOwnerColumn(context.Keys) < 0)
                throw new InvalidOperationException("Shape 变换轨缺少所属 Shape 行号。");
            if (template != null)
            {
                context.Spawn = BattleActionTimelineSchema.FindById(context.Spawns,
                    BattleActionTimelineSchema.GetStepConfigurationId(steps, template));
                context.Body = BattleActionTimelineSchema.FindById(context.Bodies,
                    BattleActionTimelineSchema.GetInt(context.Spawns, context.Spawn, 0, BattleActionTimelineSchema.AttackBodyIdAliases));
                if (context.Spawn == null || context.Body == null)
                    throw new InvalidOperationException("参考步骤的生成配置或攻击体引用断开，请先修正引用。");
            }
            return context;
        }

        private static BattleActionTimelineIndependentAttack CopyAttack(
            Plan plan,
            Context context,
            double lifetime,
            int configurationId)
        {
            var body = CopyBody(plan, context, configurationId);
            if (context.IsProjectile) return CopyProjectile(plan, context, body, lifetime, configurationId);
            var spawn = plan.New(context.Spawns, context.Spawn, configurationId);
            BattleActionTimelineSchema.Set(context.Spawns, spawn, "TimelineMelee_" + spawn.rowId, "codeName");
            BattleActionTimelineSchema.SetInt(context.Spawns, spawn, body.rowId, BattleActionTimelineSchema.AttackBodyIdAliases);
            BattleActionTimelineSchema.SetDouble(context.Spawns, spawn, lifetime, "activeDuration");
            return new BattleActionTimelineIndependentAttack { BodyId = body.rowId, SpawnId = spawn.rowId, Blank = context.Body == null };
        }

        private static BattleActionTimelineRecordData CopyBody(
            Plan plan,
            Context context,
            int configurationId)
        {
            var body = plan.New(context.Bodies, context.Body, configurationId);
            BattleActionTimelineSchema.Set(context.Bodies, body, "TimelineBody_" + body.rowId, "codeName");
            plan.BindChildren(context.Shapes, context.Bodies, body);
            int shapeCount = CopyShapesWithTracks(
                plan,
                context.Shapes,
                context.Keys,
                context.Body?.rowId ?? 0,
                body.rowId,
                BattleActionTimelineSchema.FindColumn(
                    context.Shapes,
                    BattleActionTimelineSchema.AttackBodyIdAliases));
            if (context.Body == null)
            {
                BattleActionTimelineSchema.SetInt(context.Bodies, body, 1, "clashStrength");
                BattleActionTimelineSchema.SetInt(context.Bodies, body, 1, "clashResistance");
                BattleActionTimelineSchema.SetInt(context.Bodies, body, 1, "maxHitsPerTarget");
                BattleActionTimelineSchema.SetInt(context.Bodies, body, -1, "maxTotalHitCount");
                BattleActionTimelineSchema.SetDouble(context.Bodies, body, 0d, "sameTargetHitInterval");
                BattleActionTimelineHitEffects.Write(context.Bodies, body,
                    BattleActionTimelineHitEffects.BasicDefaults(plan.Document));
                var shape = plan.New(
                    context.Shapes,
                    null,
                    BattleActionTimelineSchema.ComposeShapeId(body.rowId, 0));
                BattleActionTimelineSchema.SetInt(context.Shapes, shape, body.rowId, BattleActionTimelineSchema.AttackBodyIdAliases);
                BattleActionTimelineSchema.Set(context.Shapes, shape, "Box", "shapeType");
                BattleActionTimelineSchema.SetDouble(context.Shapes, shape, context.IsProjectile ? 0d : 0.5d, "offsetX");
                BattleActionTimelineSchema.SetDouble(context.Shapes, shape, context.IsProjectile ? 0d : 0.5d, "offsetY");
                BattleActionTimelineSchema.SetDouble(context.Shapes, shape, 0d, "rotationDegrees");
                BattleActionTimelineSchema.SetDouble(context.Shapes, shape, context.IsProjectile ? 0.25d : 1d, "sizeX", "width");
                BattleActionTimelineSchema.SetDouble(context.Shapes, shape, context.IsProjectile ? 0.25d : 1d, "sizeY", "height");
                BattleActionTimelineSchema.SetDouble(context.Shapes, shape, 0d, "radius");
                BattleActionTimelineSchema.Set(context.Shapes, shape, "Vertical", "capsuleDirection");
                BattleActionTimelineSchema.SetInt(context.Shapes, shape, 0, "shapeIndex");
                plan.BindChildren(context.Keys, context.Shapes, shape);
                AddDefaultShapeKeyframe(plan, context.Keys, shape.rowId);
            }
            else if (shapeCount == 0)
                throw new InvalidOperationException("参考攻击体没有可复制的合法形状，未修改草稿。");
            return body;
        }

        private static BattleActionTimelineIndependentAttack CopyProjectile(
            Plan plan,
            Context context,
            BattleActionTimelineRecordData body,
            double lifetime,
            int configurationId)
        {
            var movement = plan.New(context.Movements, context.Movement, configurationId);
            var projectile = plan.New(context.Projectiles, context.Projectile, configurationId);
            var launch = plan.New(context.Spawns, context.Spawn, configurationId);
            BattleActionTimelineSchema.Set(context.Movements, movement, "TimelineMovement_" + movement.rowId, "codeName");
            BattleActionTimelineSchema.Set(context.Projectiles, projectile, "TimelineProjectile_" + projectile.rowId, "codeName");
            BattleActionTimelineSchema.Set(context.Spawns, launch, "TimelineLaunch_" + launch.rowId, "codeName");
            BattleActionTimelineSchema.SetInt(context.Spawns, launch, projectile.rowId, "projectileId");
            BattleActionTimelineSchema.SetInt(context.Projectiles, projectile, body.rowId, BattleActionTimelineSchema.AttackBodyIdAliases);
            BattleActionTimelineSchema.SetInt(context.Projectiles, projectile, movement.rowId, "movementConfigId");
            BattleActionTimelineSchema.Set(context.Projectiles, projectile, context.MovementType.ToString(), "movementType");
            BattleActionTimelineSchema.SetDouble(context.Projectiles, projectile, lifetime, "maxLifetime");
            if (context.Projectile == null)
            {
                BattleActionTimelineSchema.SetDouble(context.Movements, movement, 5d,
                    context.MovementType == RefData.EnumBattleProjectileMovementType.Linear ? "speed" : "initialSpeed");
                BattleActionTimelineSchema.SetDouble(context.Movements, movement, 1d, "gravityScale");
                BattleActionTimelineSchema.SetInt(context.Projectiles, projectile, 0, "presentationResourceId");
                BattleActionTimelineSchema.Set(context.Projectiles, projectile, "Destroy", "worldCollisionMode");
                BattleActionTimelineSchema.Set(context.Projectiles, projectile, "TreatAsSolid", "oneWayPlatformCollisionMode");
                BattleActionTimelineSchema.Set(context.Projectiles, projectile, "true", "destroyWhenAttackBodyExhausted");
                BattleActionTimelineSchema.SetDouble(context.Spawns, launch, 0d, "spawnOffsetX", "offsetX");
                BattleActionTimelineSchema.SetDouble(context.Spawns, launch, 0.5d, "spawnOffsetY", "offsetY");
                BattleActionTimelineSchema.SetDouble(context.Spawns, launch, 0d, "angleOffsetDegrees");
                BattleActionTimelineSchema.Set(context.Spawns, launch, "ActionFacing", "directionSource", "directionMode");
            }
            return new BattleActionTimelineIndependentAttack
            {
                BodyId = body.rowId, SpawnId = launch.rowId, ProjectileId = projectile.rowId,
                MovementId = movement.rowId, IsProjectile = true, Blank = context.Projectile == null,
            };
        }

        private static int CopyShapesWithTracks(
            Plan plan,
            BattleActionTimelineTableData shapes,
            BattleActionTimelineTableData keys,
            int fromBodyId,
            int toBodyId,
            int ownerColumn)
        {
            if (shapes == null || ownerColumn < 0 || fromBodyId <= 0)
            {
                return 0;
            }

            int keyOwnerColumn = keys == null
                ? -1
                : BattleActionTimelineSchema.FindKeyframeOwnerColumn(keys);
            int copiedCount = 0;
            foreach (BattleActionTimelineRecordData source in shapes.records.Where(row =>
                BattleActionTimelineSchema.GetInt(
                    shapes,
                    row,
                    0,
                    BattleActionTimelineSchema.AttackBodyIdAliases) == fromBodyId).ToList())
            {
                int sourceShapeIndex = BattleActionTimelineSchema.GetInt(
                    shapes,
                    source,
                    copiedCount,
                    "shapeIndex");
                BattleActionTimelineRecordData copy = plan.New(
                    shapes,
                    source,
                    BattleActionTimelineSchema.ComposeShapeId(toBodyId, sourceShapeIndex));
                BattleActionTimelineSchema.SetInt(
                    shapes,
                    copy,
                    toBodyId,
                    BattleActionTimelineSchema.AttackBodyIdAliases);
                copiedCount++;

                if (keys != null && keyOwnerColumn >= 0)
                {
                    plan.BindChildren(keys, shapes, copy);
                    int sourceKeyCount = CopyChildren(
                        plan,
                        keys,
                        source.rowId,
                        copy.rowId,
                        keyOwnerColumn);
                    if (sourceKeyCount == 0)
                    {
                        AddDefaultShapeKeyframe(plan, keys, copy.rowId);
                    }
                }
            }

            return copiedCount;
        }

        private static void AddDefaultShapeKeyframe(
            Plan plan,
            BattleActionTimelineTableData keys,
            int shapeId)
        {
            if (keys == null || shapeId <= 0)
            {
                return;
            }

            int ownerColumn = BattleActionTimelineSchema.FindKeyframeOwnerColumn(keys);
            if (ownerColumn < 0)
            {
                return;
            }

            BattleActionTimelineRecordData key = plan.New(keys);
            key.cells[ownerColumn] = shapeId.ToString(CultureInfo.InvariantCulture);
            BattleActionTimelineSchema.SetDouble(keys, key, 0d, BattleActionTimelineSchema.LocalTimeAliases);
            BattleActionTimelineSchema.SetDouble(keys, key, 0d, BattleActionTimelineSchema.OffsetXAliases);
            BattleActionTimelineSchema.SetDouble(keys, key, 0d, BattleActionTimelineSchema.OffsetYAliases);
            BattleActionTimelineSchema.SetDouble(keys, key, 0d, BattleActionTimelineSchema.RotationAliases);
            BattleActionTimelineSchema.SetDouble(keys, key, 1d, BattleActionTimelineSchema.ScaleXAliases);
            BattleActionTimelineSchema.SetDouble(keys, key, 1d, BattleActionTimelineSchema.ScaleYAliases);
            BattleActionTimelineSchema.Set(keys, key, "Linear", BattleActionTimelineSchema.InterpolationAliases);
        }

        private static int CopyChildren(Plan plan, BattleActionTimelineTableData table, int from, int to, int ownerColumn)
        {
            if (from <= 0) return 0;
            if (table.isInlineProjection && table.inlineOwners.Any(owner => owner.ownerRowId == from &&
                !string.IsNullOrEmpty(owner.parseError)))
                throw new InvalidOperationException("参考形状或变换轨解析失败，未复制损坏的数据。");
            var children = table.records.Where(row => int.TryParse(row.cells[ownerColumn], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int owner) && owner == from).ToArray();
            foreach (var child in children)
            {
                var copied = plan.New(table, child);
                copied.cells[ownerColumn] = to.ToString(CultureInfo.InvariantCulture);
            }
            return children.Length;
        }

        private static void Require(BattleActionTimelineTableData table, params string[] columns)
        {
            if (BattleActionTimelineSchema.FindColumn(table, columns) < 0)
                throw new InvalidOperationException((table?.sheetName ?? "工作表") + " 缺少必要字段 " + string.Join("/", columns));
        }
    }

    /// <summary>重编译后只恢复源表基线，不覆盖当前文档、选择或未写回修改。</summary>
    internal static class BattleActionTimelineSourceBinding
    {
        public static bool CanRebind(BattleActionTimelineWorkbookSnapshot snapshot,
            string expectedPath, string expectedHash, string baselineSignature, out string error)
        {
            error = string.Empty;
            if (snapshot == null || !snapshot.HasActionStructure)
            {
                error = "未读取到有效的动作源表快照。";
                return false;
            }
            if (!string.IsNullOrEmpty(expectedPath) && !string.Equals(
                expectedPath, snapshot.WorkbookPath, StringComparison.OrdinalIgnoreCase))
            {
                error = "源工作簿路径与草稿基线不一致，未绑定写回。";
                return false;
            }
            if (!string.IsNullOrEmpty(expectedHash))
            {
                if (string.Equals(expectedHash, snapshot.ContentHash, StringComparison.Ordinal)) return true;
                error = "源工作簿已变化，保留草稿但禁止直接写回；请确认后重新读取正式配置。";
                return false;
            }
            // 旧窗口没有保存hash时，必须核对原始基线，不能拿有修改的当前文档充当基线。
            if (!string.IsNullOrEmpty(baselineSignature) && string.Equals(
                baselineSignature, BattleActionTimelineDocument.FromSnapshot(snapshot).CanonicalSignature(),
                StringComparison.Ordinal)) return true;
            error = "无法验证草稿的原始源表基线，保留草稿但禁止写回。";
            return false;
        }
    }

    internal sealed class BattleActionTimelineCharacterFilter
    {
        public int Id;
        public string CodeName;
        public string NameLanguageKey;
    }

    internal sealed class BattleActionTimelineAnimationFilter
    {
        public int CharacterId;
        public bool IsCommon;
        public int ActionId;
        public int ActionType;
        public bool IsDefault;
        public int SkillId;
        public int SingleSkillId;
        public string Label;
        public string Key => CharacterId + "/" + ActionType + "/" + ActionId + "/" + SingleSkillId + "/" + Label;
    }

    /// <summary>仅构建查找列表，不改动作绑定；技能只保留ID，不提供技能下拉选择。</summary>
    internal sealed class BattleActionTimelineFilterCatalog
    {
        public readonly List<BattleActionTimelineCharacterFilter> Characters =
            new List<BattleActionTimelineCharacterFilter>();
        public readonly List<BattleActionTimelineAnimationFilter> Actions =
            new List<BattleActionTimelineAnimationFilter>();
        public readonly List<string> Issues = new List<string>();

        public static BattleActionTimelineFilterCatalog Build(
            BattleActionTimelineDocument document, int selectedCharacterId = 0, int selectedSkillId = 0)
        {
            var result = new BattleActionTimelineFilterCatalog();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var robots = document?.FindTable("BattleRobot");
            foreach (var robot in robots?.records ?? new List<BattleActionTimelineRecordData>())
            {
                result.Characters.Add(new BattleActionTimelineCharacterFilter
                {
                    Id = robot.rowId,
                    CodeName = BattleActionTimelineSchema.Get(robots, robot, "codeName"),
                    NameLanguageKey = BattleActionTimelineSchema.Get(robots, robot, "nameLanguageKey"),
                });
            }
            result.Characters.Sort((a, b) => a.Id.CompareTo(b.Id));

            void Add(int characterId, bool common, int actionId, int singleSkillId, string label,
                int actionType = (int)RefData.EnumBattleCharacterActionState.Skill, bool isDefault = true, int skillId = 0)
            {
                if (actionId < 0) return;
                var value = new BattleActionTimelineAnimationFilter
                {
                    CharacterId = characterId, IsCommon = common, ActionId = actionId,
                    ActionType = actionType, IsDefault = isDefault, SkillId = skillId,
                    SingleSkillId = singleSkillId, Label = label + $" · 动作 ID {actionId}" +
                        (singleSkillId > 0 ? $" · 单段 ID {singleSkillId}" : string.Empty),
                };
                if (keys.Add(value.Key)) result.Actions.Add(value);
            }

            List<Dictionary<string, string>> Entries(BattleActionTimelineTableData table,
                BattleActionTimelineRecordData row)
            {
                if (BattleActionTimelineInlineStructCodec.TryParse(
                    BattleActionTimelineSchema.Get(table, row, "entries"), out var values, out string error))
                    return values;
                result.Issues.Add($"{table.sheetName}/{row.rowId} 的 entries 无法读取：{error}");
                return new List<Dictionary<string, string>>();
            }

            var mapping = document?.FindTable("BattleCharacterAction");
            foreach (var row in mapping?.records ?? new List<BattleActionTimelineRecordData>())
            {
                string state = BattleActionTimelineSchema.Get(mapping, row, "codeName");
                foreach (var entry in Entries(mapping, row))
                {
                    int characterId = EntryInt(entry, "CharacterId");
                    int left = EntryInt(entry, "LeftWeaponId");
                    int right = EntryInt(entry, "RightWeaponId");
                    string weapons = left != 0 || right != 0 ? $" · 左手 {left}/右手 {right}" : string.Empty;
                    Add(characterId, characterId <= 0, EntryInt(entry, "ActionId"), 0,
                        StateLabel(state) + weapons + (characterId <= 0 ? " · 通用映射" : string.Empty),
                        row.rowId, left == 0 && right == 0);
                }
            }

            // 普攻可能是多段/多装备技能，必须沿正式引用遍历，不能从动作文件名猜角色。
            void VisitSkill(int characterId, int skillId, string label, HashSet<int> path)
            {
                if (skillId <= 0) return;
                if (path.Count >= 64 || !path.Add(skillId))
                {
                    result.Issues.Add($"角色 {characterId} 的技能引用循环或过深：技能 ID {skillId}");
                    return;
                }
                try
                {
                    var skills = document?.FindTable("BattleSkill");
                    var skill = skills?.records.FirstOrDefault(value => value.rowId == skillId);
                    if (skill == null)
                    {
                        result.Issues.Add($"角色 {characterId} 引用不存在的技能 ID {skillId}");
                        return;
                    }
                    Enum.TryParse(BattleActionTimelineSchema.Get(skills, skill, "skillType"), true,
                        out RefData.EnumBattleSkillType type);
                    int target = BattleActionTimelineSchema.GetInt(skills, skill, 0, "targetId");
                    string targetSheet = type == RefData.EnumBattleSkillType.ActiveSingle ? "BattleActiveSingleSkill" :
                        type == RefData.EnumBattleSkillType.ActiveMulti ? "BattleActiveMultiSkill" :
                        type == RefData.EnumBattleSkillType.MultiEquipment ? "BattleSkillMultiEquipment" : string.Empty;
                    if (type == RefData.EnumBattleSkillType.Passive) return;
                    var targetTable = document?.FindTable(targetSheet);
                    var targetRow = targetTable?.records.FirstOrDefault(value => value.rowId == target);
                    if (targetRow == null)
                    {
                        result.Issues.Add($"技能 ID {skillId} 的目标不存在：{targetSheet}/{target}");
                        return;
                    }
                    if (type == RefData.EnumBattleSkillType.ActiveSingle)
                        Add(characterId, false, BattleActionTimelineSchema.GetInt(targetTable, targetRow, 0, "actionId"),
                            targetRow.rowId, label, skillId: skillId);
                    else if (type == RefData.EnumBattleSkillType.ActiveMulti)
                    {
                        foreach (var field in new[] { ("groundStageSkillIds", "地面"), ("airStageSkillIds", "空中") })
                        {
                            int stage = 0;
                            foreach (int child in Ids(BattleActionTimelineSchema.Get(targetTable, targetRow, field.Item1),
                                result.Issues, $"{targetSheet}/{target}/{field.Item1}"))
                                VisitSkill(characterId, child, label + $" · {field.Item2}第{++stage}段", path);
                        }
                    }
                    else if (type == RefData.EnumBattleSkillType.MultiEquipment)
                    {
                        if (characterId <= 0)
                        {
                            result.Issues.Add($"技能 ID {skillId} 包含多装备映射，请先选择角色，再按技能 ID 加载。");
                            return;
                        }
                        foreach (var entry in Entries(targetTable, targetRow))
                        {
                            int entryCharacter = EntryInt(entry, "CharacterId");
                            if (entryCharacter > 0 && entryCharacter != characterId) continue;
                            int child = EntryInt(entry, "ResolvedSkillId");
                            if (child <= 0)
                            {
                                result.Issues.Add($"{targetSheet}/{target} 包含无效的 ResolvedSkillId。");
                                continue;
                            }
                            VisitSkill(characterId, child, label +
                                $" · 左手 {EntryInt(entry, "LeftWeaponId")}/右手 {EntryInt(entry, "RightWeaponId")}", path);
                        }
                    }
                }
                finally { path.Remove(skillId); }
            }

            if (selectedSkillId > 0)
                VisitSkill(selectedCharacterId, selectedSkillId, $"技能 ID {selectedSkillId}", new HashSet<int>());
            result.Actions.Sort((a, b) =>
            {
                int id = a.ActionId.CompareTo(b.ActionId);
                return id != 0 ? id : StringComparer.Ordinal.Compare(a.Key, b.Key);
            });
            return result;
        }

        public bool TryResolve(int characterId, int actionType, int skillId,
            out BattleActionTimelineAnimationFilter selection, out string error)
        {
            selection = null;
            error = string.Empty;
            bool isSkill = actionType == (int)RefData.EnumBattleCharacterActionState.Skill;
            if (actionType <= 0 || characterId < 0 ||
                (characterId > 0 && !Characters.Any(value => value.Id == characterId)) ||
                (!isSkill && characterId == 0))
            {
                error = "普通动作请选择角色和动作类型；也可以直接填写技能 ID、动画资源 ID 或拖入动画。";
                return false;
            }

            if (isSkill && skillId <= 0)
            {
                error = "技能动作请填写技能 ID（BattleSkill.id），不需要选择具体动画。";
                return false;
            }
            if (isSkill && Issues.Count > 0)
            {
                error = string.Join("\n", Issues.Distinct());
                return false;
            }

            var candidates = Actions.Where(value => value.ActionType == actionType &&
                (isSkill ? value.SkillId > 0 && value.CharacterId == characterId : value.IsDefault)).ToArray();
            if (!isSkill)
            {
                var specific = candidates.Where(value => value.CharacterId == characterId).ToArray();
                candidates = specific.Length > 0 ? specific : candidates.Where(value => value.IsCommon).ToArray();
            }
            candidates = candidates.GroupBy(value => value.ActionId + "/" + value.SingleSkillId)
                .Select(group => group.First()).ToArray();
            if (candidates.Length != 1)
            {
                error = isSkill && candidates.Length > 1
                    ? $"技能 ID {skillId} 包含多个动作，请填写对应的单段技能 ID：" +
                        string.Join("、", candidates.Select(value => value.SkillId).Distinct())
                    : candidates.Length == 0 ? "该角色的动作类型没有配置动画。"
                    : "该角色的动作类型存在多个默认动画，请检查角色动作映射配置。";
                return false;
            }
            if (candidates[0].ActionId <= 0)
            {
                error = isSkill ? $"技能 ID {skillId} 尚未配置动画。" : "该角色的动作类型配置为空动画（ID=0）。";
                return false;
            }
            selection = candidates[0];
            return true;
        }

        private static string EntryValue(Dictionary<string, string> entry, string key) =>
            entry.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
        private static int EntryInt(Dictionary<string, string> entry, string key) =>
            int.TryParse(EntryValue(entry, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0;
        private static IEnumerable<int> Ids(string value, List<string> issues, string context)
        {
            foreach (string part in (value ?? string.Empty).Trim().Trim('[', ']')
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id > 0)
                    yield return id;
                else
                    issues.Add($"{context} 包含无效技能 ID：{part.Trim()}");
        }
        public static string StateLabel(string state)
        {
            switch (state)
            {
                case "Idle": return "待机";
                case "Move": return "移动";
                case "Jump": return "跳跃";
                case "Fall": return "下落";
                case "Climb": return "攀爬";
                case "Dash": return "闪避";
                case "Skill": return "技能";
                case "Hitstun": return "受击";
                case "Dead": return "死亡";
                case "PostureBreak": return "架势击破";
                case "Rise": return "起身";
                case "JumpStart": return "起跳";
                case "JumpAir": return "空中";
                case "JumpLand": return "落地";
                case "AirHitstun": return "空中受击";
                case "AirHitRecovery": return "落地受击恢复";
                default: return state;
            }
        }
    }

    /// <summary>显式动作输入到源表单段的只读定位，禁止歧义时自动选第一行。</summary>
    internal static class BattleActionTimelineActionLookup
    {
        public static bool TryResolve(
            BattleActionTimelineDocument document, int actionId, int singleSkillId,
            out BattleActionTimelineTableData table, out BattleActionTimelineRecordData record,
            out string error)
        {
            table = null;
            record = null;
            error = string.Empty;
            if (actionId <= 0)
            {
                error = "请拖入动画或填写动作 ID，工具将定位引用该动作的技能单段。";
                return false;
            }

            var matches = (document?.ActionTables() ?? Enumerable.Empty<BattleActionTimelineTableData>())
                .SelectMany(candidate => candidate.records.Select(row => (Table: candidate, Record: row)))
                .Where(value => BattleActionTimelineSchema.GetInt(
                    value.Table, value.Record, 0, "actionId") == actionId)
                .OrderBy(value => value.Record.rowId)
                .ThenBy(value => value.Table.sheetName, StringComparer.Ordinal)
                .ToArray();
            if (matches.Length == 0)
            {
                error = $"当前源表没有单段引用动作 {actionId}，可预览动画；未载入无关技能配置。";
                return false;
            }

            var selected = singleSkillId > 0
                ? matches.Where(value => value.Record.rowId == singleSkillId).ToArray() : matches;
            if (selected.Length != 1)
            {
                error = singleSkillId > 0 && selected.Length == 0
                    ? $"单段 {singleSkillId} 未引用动作 {actionId}，未载入技能配置。"
                    : $"多个单段共用动作 {actionId}，请填写要编辑的单段技能 ID：\n" +
                        string.Join("\n", matches.Select(value => value.Table.sheetName + " / " + value.Record.rowId));
                return false;
            }

            table = selected[0].Table;
            record = selected[0].Record;
            return true;
        }
    }

    /// <summary>序列帧采样不使用时间轴的四舍五入吸附；布局与战斗 SpriteRenderer 保持同一世界单位。</summary>
    internal static class BattleActionTimelineSequencePreview
    {
        public static int SampleFrame(double seconds, double frameRate, int frameCount, bool loop)
        {
            if (!BattleActionTimelineTime.IsFinite(seconds) ||
                !BattleActionTimelineTime.IsFinite(frameRate) || frameRate <= 0d || frameCount <= 0)
            {
                return -1;
            }

            double frame = Math.Max(0d, seconds) * frameRate;
            if (!BattleActionTimelineTime.IsFinite(frame))
            {
                return -1;
            }

            // 吸附后的 1/FPS 可能有浮点误差；只修正边界误差，不把半帧提前四舍五入。
            frame = Math.Floor(frame + 0.0000001d);
            return loop ? (int)(frame % frameCount) : (int)Math.Min(frameCount - 1d, frame);
        }

        public static Rect SpriteDrawRect(
            Vector2 textureSize, Vector2 textureOffset, Vector2 pivot, float spritePixelsPerUnit,
            Bounds bounds, float height, bool flipX,
            Vector2 screenOrigin, float screenPixelsPerUnit)
        {
            // 与 BattleWorldZoneSequenceFrameVisual.ApplyClipLayout 相同：
            // 依 bounds 高度统一缩放，先放好未翻转的 pivot，再绕 pivot 翻转 Sprite。
            float scale = height / Math.Max(0.0001f, bounds.size.y);
            float localX = (textureOffset.x - pivot.x) / spritePixelsPerUnit;
            float localY = (textureOffset.y - pivot.y) / spritePixelsPerUnit;
            float width = textureSize.x / spritePixelsPerUnit;
            float nativeHeight = textureSize.y / spritePixelsPerUnit;
            if (flipX)
            {
                localX = -localX - width;
            }

            float worldX = (localX - bounds.center.x) * scale;
            float worldTop = (localY + nativeHeight - bounds.min.y) * scale;
            return new Rect(
                screenOrigin.x + worldX * screenPixelsPerUnit,
                screenOrigin.y - worldTop * screenPixelsPerUnit,
                width * scale * screenPixelsPerUnit,
                nativeHeight * scale * screenPixelsPerUnit);
        }
    }

    /// <summary>形状与编辑手柄共用同一显示门禁；存活结束点不再显示有效判定。</summary>
    internal static class BattleActionTimelinePreviewLifetime
    {
        public static bool IsActive(double localTime, double lifetime) =>
            BattleActionTimelineTime.IsFinite(localTime) &&
            BattleActionTimelineTime.IsFinite(lifetime) && lifetime > 0d &&
            localTime >= 0d && localTime + 1e-9d < lifetime;

        public static bool IsVisible(double localTime, double lifetime, bool showInactiveShapes) =>
            BattleActionTimelineTime.IsFinite(localTime) &&
            BattleActionTimelineTime.IsFinite(lifetime) && lifetime > 0d &&
            (IsActive(localTime, lifetime) || showInactiveShapes);
    }

    /// <summary>预览手柄的纯数学部分。坐标与运行时一致：先缩放局部偏移，再旋转，最后加关键帧偏移。</summary>
    internal static class BattleActionTimelineShapeGizmoMath
    {
        public static Vector2 Rotate(Vector2 value, float degrees)
        {
            double radians = degrees * Math.PI / 180d;
            float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
            return new Vector2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
        }

        public static Vector2 Center(Vector2 shapeOffset, BattleActionTimelineTransform transform) =>
            transform.Offset + Rotate(Vector2.Scale(shapeOffset, transform.Scale), transform.RotationDegrees);

        public static Vector2 ToScreen(Vector2 value, float pixelsPerUnit, int facing) =>
            new Vector2(value.x * facing * pixelsPerUnit, -value.y * pixelsPerUnit);

        public static Vector2 FromScreen(Vector2 delta, float pixelsPerUnit, int facing) =>
            new Vector2(delta.x * facing / pixelsPerUnit, -delta.y / pixelsPerUnit);

        public static Vector2 MoveShapeOffset(Vector2 original, Vector2 worldDelta, BattleActionTimelineTransform transform)
        {
            Vector2 delta = Rotate(worldDelta, -transform.RotationDegrees);
            return original + new Vector2(
                Math.Abs(transform.Scale.x) < 0.00001f ? 0f : delta.x / transform.Scale.x,
                Math.Abs(transform.Scale.y) < 0.00001f ? 0f : delta.y / transform.Scale.y);
        }

        public static bool Contains(Vector2 point, Vector2 center, Vector2 size, float screenRotation,
            BattleAttackShapeType type, bool horizontal, float tolerance = 4f)
        {
            Vector2 local = Rotate(point - center, -screenRotation);
            Vector2 half = size * 0.5f;
            if (type == BattleAttackShapeType.Circle)
                return local.sqrMagnitude <= (half.x + tolerance) * (half.x + tolerance);
            if (type == BattleAttackShapeType.Capsule)
            {
                float radius = Math.Min(half.x, half.y);
                float line = Math.Max(0f, (horizontal ? half.x : half.y) - radius);
                Vector2 closest = horizontal
                    ? new Vector2(Math.Max(-line, Math.Min(line, local.x)), 0f)
                    : new Vector2(0f, Math.Max(-line, Math.Min(line, local.y)));
                return (local - closest).sqrMagnitude <= (radius + tolerance) * (radius + tolerance);
            }
            return Math.Abs(local.x) <= half.x + tolerance && Math.Abs(local.y) <= half.y + tolerance;
        }

        public static Vector2 Resize(Vector2 originalSize, Vector2 localDelta, Vector2 handle, bool uniform)
        {
            if (uniform)
            {
                Vector2 fromCenter = Vector2.Scale(originalSize * 0.5f, handle);
                float factor = Math.Max(0.001f,
                    1f + Vector2.Dot(localDelta, fromCenter) / Math.Max(0.00001f, fromCenter.sqrMagnitude));
                return originalSize * factor;
            }
            return new Vector2(
                handle.x == 0f ? originalSize.x : Math.Max(0.001f, originalSize.x + 2f * localDelta.x * handle.x),
                handle.y == 0f ? originalSize.y : Math.Max(0.001f, originalSize.y + 2f * localDelta.y * handle.y));
        }

        // 关键帧缩放围绕它自己的枢轴。解线性方程，让抓住的角/边跟随鼠标，而不是中心随缩放漂移。
        public static Vector2 ScaleKeyframe(Vector2 shapeOffset, Vector2 baseSize, float shapeRotation,
            BattleActionTimelineTransform initial, Vector2 handle, Vector2 worldDelta, bool uniform, bool circle)
        {
            Vector2 corner = Vector2.Scale(baseSize * 0.5f, handle);
            float maximum = Math.Max(initial.Scale.x, initial.Scale.y);
            Vector2 x = Rotate(new Vector2(shapeOffset.x, 0f), initial.RotationDegrees) +
                Rotate(new Vector2(corner.x, 0f), initial.RotationDegrees + shapeRotation);
            Vector2 y = Rotate(new Vector2(0f, shapeOffset.y), initial.RotationDegrees) +
                Rotate(new Vector2(0f, corner.y), initial.RotationDegrees + shapeRotation);
            Vector2 start = circle
                ? Rotate(Vector2.Scale(shapeOffset, initial.Scale), initial.RotationDegrees) +
                    Rotate(corner * maximum, initial.RotationDegrees + shapeRotation)
                : x * initial.Scale.x + y * initial.Scale.y;
            Vector2 target = start + worldDelta;
            if (uniform || circle)
            {
                if (start.sqrMagnitude < 0.00001f)
                {
                    Vector2 basis = Rotate(shapeOffset, initial.RotationDegrees) +
                        Rotate(corner, initial.RotationDegrees + shapeRotation);
                    if (basis.sqrMagnitude < 0.00001f) return initial.Scale;
                    float amount = Vector2.Dot(worldDelta, basis) / basis.sqrMagnitude;
                    return new Vector2(Math.Max(0f, initial.Scale.x + amount), Math.Max(0f, initial.Scale.y + amount));
                }
                float factor = Math.Max(0.001f, Vector2.Dot(target, start) / Math.Max(0.00001f, start.sqrMagnitude));
                return initial.Scale * factor;
            }
            if (handle.x == 0f)
                return new Vector2(initial.Scale.x, Math.Max(0.001f,
                    Vector2.Dot(target - x * initial.Scale.x, y) / Math.Max(0.00001f, y.sqrMagnitude)));
            if (handle.y == 0f)
                return new Vector2(Math.Max(0.001f,
                    Vector2.Dot(target - y * initial.Scale.y, x) / Math.Max(0.00001f, x.sqrMagnitude)), initial.Scale.y);
            float determinant = x.x * y.y - x.y * y.x;
            if (Math.Abs(determinant) < 0.00001f) return initial.Scale;
            return new Vector2(Math.Max(0.001f, (target.x * y.y - target.y * y.x) / determinant),
                Math.Max(0.001f, (x.x * target.y - x.y * target.x) / determinant));
        }
    }

    /// <summary>命中效果直接保存在步骤所属 AttackBody.effects，不引入另一套共享攻击配置。</summary>
    internal static class BattleActionTimelineHitEffects
    {
        private static readonly BattleActionTimelineInlineFieldData[] Fields =
        {
            new BattleActionTimelineInlineFieldData { headerName = "effectType", serializedName = "EffectType" },
            new BattleActionTimelineInlineFieldData { headerName = "effectId", serializedName = "EffectId" },
        };

        public static bool TryRead(BattleActionTimelineTableData table, BattleActionTimelineRecordData body,
            out List<BattleAttackEffectReference> effects, out string error)
        {
            effects = new List<BattleAttackEffectReference>();
            if (!BattleActionTimelineInlineStructCodec.TryParse(
                    BattleActionTimelineSchema.Get(table, body, "effects", "hitEffects"), out var values, out error))
                return false;
            for (int index = 0; index < values.Count; index++)
            {
                var value = values[index];
                if (value.Count != 2 || !value.TryGetValue("EffectType", out string typeText) ||
                    !value.TryGetValue("EffectId", out string idText) ||
                    !Enum.TryParse(typeText, true, out RefData.EnumBattleCombatEffectType type) ||
                    !Enum.IsDefined(typeof(RefData.EnumBattleCombatEffectType), type) ||
                    !int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                {
                    error = $"effects[{index}] 必须包含 EffectType 枚举和整数 EffectId，且不能有多余字段。";
                    effects.Clear();
                    return false;
                }
                effects.Add(new BattleAttackEffectReference((int)type, id));
            }
            return true;
        }

        public static void Write(BattleActionTimelineTableData table, BattleActionTimelineRecordData body,
            IEnumerable<BattleAttackEffectReference> effects)
        {
            string text = BattleActionTimelineInlineStructCodec.Serialize(Fields, effects.Select(effect =>
                new BattleActionTimelineRecordData
                {
                    cells = new[] { "", "", ((RefData.EnumBattleCombatEffectType)effect.EffectType).ToString(),
                        effect.EffectId.ToString(CultureInfo.InvariantCulture) },
                }));
            BattleActionTimelineSchema.Set(table, body, text, "effects", "hitEffects");
        }

        public static string TableName(int type)
        {
            switch ((RefData.EnumBattleCombatEffectType)type)
            {
                case RefData.EnumBattleCombatEffectType.Operation: return "BattleOperationEffect";
                case RefData.EnumBattleCombatEffectType.Damage: return "BattleDamageEffect";
                case RefData.EnumBattleCombatEffectType.HitReaction: return "BattleHitReactionEffect";
                case RefData.EnumBattleCombatEffectType.Persistent: return "BattlePersistentEffect";
                default: return string.Empty;
            }
        }

        public static string ReferenceError(BattleActionTimelineDocument document, BattleAttackEffectReference effect)
        {
            string name = TableName(effect.EffectType);
            if (string.IsNullOrEmpty(name) || effect.EffectId <= 0)
                return $"非法命中效果 type={effect.EffectType}, id={effect.EffectId}。";
            if (BattleActionTimelineSchema.FindById(document.FindTable(name), effect.EffectId) == null)
                return $"命中效果引用不存在：{name} id={effect.EffectId}。";
            return string.Empty;
        }

        // 仅使用源表中确实存在的基础效果，不凭空制造引用，也不修改已有步骤的定制效果。
        public static List<BattleAttackEffectReference> BasicDefaults(BattleActionTimelineDocument document)
        {
            var effects = new List<BattleAttackEffectReference>();
            foreach (var type in new[] { RefData.EnumBattleCombatEffectType.Damage, RefData.EnumBattleCombatEffectType.HitReaction })
            {
                var effect = new BattleAttackEffectReference((int)type, 1);
                if (string.IsNullOrEmpty(ReferenceError(document, effect))) effects.Add(effect);
            }
            return effects;
        }

        public static string Describe(BattleActionTimelineDocument document, BattleAttackEffectReference effect)
        {
            string problem = ReferenceError(document, effect);
            if (!string.IsNullOrEmpty(problem)) return problem;
            var table = document.FindTable(TableName(effect.EffectType));
            var row = BattleActionTimelineSchema.FindById(table, effect.EffectId);
            return string.Join("，", table.headers.Select((header, index) =>
                header == "id" || index >= row.cells.Length ? null : header + "=" + row.cells[index])
                .Where(value => value != null));
        }
    }

    internal static class BattleActionTimelineInlineStructCodec
    {
        // cltabtoy 的 struct 不接受省略成员。只补有明确预览语义的变换默认值；
        // LocalTime 和执行步骤引用不猜值，仍交给校验拒绝不完整配置。
        private static string TransformDefault(string fieldName)
        {
            switch (fieldName)
            {
                case "OffsetX":
                case "OffsetY":
                case "RotationDegrees": return "0";
                case "ScaleX":
                case "ScaleY": return "1";
                case "Interpolation": return "Linear";
                default: return null;
            }
        }

        public static bool HasMissingTransformDefaults(
            IReadOnlyList<BattleActionTimelineInlineFieldData> fields,
            IEnumerable<BattleActionTimelineRecordData> records)
        {
            return records.Any(record => record?.cells != null && fields.Where((field, index) =>
                TransformDefault(field.serializedName) != null &&
                (index + 2 >= record.cells.Length || string.IsNullOrWhiteSpace(record.cells[index + 2]))).Any());
        }

        /// <summary>
        /// 根据 `source` 解析`values`；成功时写出 `values` 并返回 `true`，失败时通过 `error` 返回原因。
        /// </summary>
        public static bool TryParse(
            string source,
            out List<Dictionary<string, string>> values,
            out string error)
        {
            values = new List<Dictionary<string, string>>();
            error = string.Empty;
            string text = (source ?? string.Empty).Trim();
            if (text.Length == 0 || string.Equals(
                    text,
                    "null",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                Parser parser = new Parser(text);
                values = parser.ParseArray();
                return true;
            }
            catch (FormatException exception)
            {
                error = exception.Message;
                values.Clear();
                return false;
            }
        }
        /// <summary>
        /// 根据 `fields`、`records` 序列化结果并返回结果。
        /// </summary>

        public static string Serialize(
            IReadOnlyList<BattleActionTimelineInlineFieldData> fields,
            IEnumerable<BattleActionTimelineRecordData> records)
        {
            StringBuilder text = new StringBuilder();
            text.Append('[');
            bool firstRecord = true;
            foreach (BattleActionTimelineRecordData record in records)
            {
                if (record?.cells == null)
                {
                    continue;
                }

                if (!firstRecord)
                {
                    text.Append(',');
                }

                firstRecord = false;
                text.Append('{');
                bool firstField = true;
                for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                {
                    string value = fieldIndex + 2 < record.cells.Length
                        ? record.cells[fieldIndex + 2] ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        value = TransformDefault(fields[fieldIndex].serializedName);
                        if (value == null) continue;
                    }

                    if (!firstField)
                    {
                        text.Append(',');
                    }

                    firstField = false;
                    text.Append('"')
                        .Append(Escape(fields[fieldIndex].serializedName))
                        .Append("\":")
                        .Append(FormatValue(value));
                }

                text.Append('}');
            }

            return text.Append(']').ToString();
        }
        /// <summary>
        /// 格式化值。
        /// </summary>

        private static string FormatValue(string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (double.TryParse(
                    trimmed,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out _) ||
                bool.TryParse(trimmed, out _) ||
                string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase) ||
                IsIdentifier(trimmed))
            {
                return trimmed;
            }

            return "\"" + Escape(trimmed) + "\"";
        }
        /// <summary>
        /// 检查 `value` 是否满足“标识符”条件。
        /// </summary>

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                !(char.IsLetter(value[0]) || value[0] == '_'))
            {
                return false;
            }

            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                if (!char.IsLetterOrDigit(character) && character != '_' &&
                    character != '.')
                {
                    return false;
                }
            }

            return true;
        }
        /// <summary>
        /// 根据 `value` 构建转义并返回结果。
        /// </summary>

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;
            /// <summary>
            /// 创建 `Parser`，记录`text`（文本）字段，形成可供后续流程传递的数据对象。
            /// </summary>

            public Parser(string text)
            {
                this.text = text;
            }
            /// <summary>
            /// 解析数组。
            /// </summary>

            public List<Dictionary<string, string>> ParseArray()
            {
                List<Dictionary<string, string>> result =
                    new List<Dictionary<string, string>>();
                SkipWhitespace();
                Expect('[');
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    EnsureEnd();
                    return result;
                }

                while (true)
                {
                    result.Add(ParseObject());
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        EnsureEnd();
                        return result;
                    }

                    Expect(',');
                }
            }
            /// <summary>
            /// 解析对象。
            /// </summary>

            private Dictionary<string, string> ParseObject()
            {
                Dictionary<string, string> result =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                SkipWhitespace();
                Expect('{');
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    string name = Peek() == '"'
                        ? ReadQuoted()
                        : ReadToken(':');
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        throw Error("字段名为空");
                    }

                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    string value = Peek() == '"'
                        ? ReadQuoted()
                        : ReadValueToken();
                    if (string.Equals(
                            value,
                            "null",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        value = string.Empty;
                    }

                    if (result.ContainsKey(name))
                    {
                        throw Error("重复字段 " + name);
                    }

                    result.Add(name, value);
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }
            /// <summary>
            /// 读取引号。
            /// </summary>

            private string ReadQuoted()
            {
                Expect('"');
                StringBuilder result = new StringBuilder();
                while (index < text.Length)
                {
                    char character = text[index++];
                    if (character == '"')
                    {
                        return result.ToString();
                    }

                    if (character != '\\')
                    {
                        result.Append(character);
                        continue;
                    }

                    if (index >= text.Length)
                    {
                        throw Error("字符串转义不完整");
                    }

                    char escaped = text[index++];
                    switch (escaped)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        default: result.Append(escaped); break;
                    }
                }

                throw Error("字符串缺少结束引号");
            }
            /// <summary>
            /// 读取值令牌。
            /// </summary>

            private string ReadValueToken()
            {
                int start = index;
                while (index < text.Length && text[index] != ',' &&
                    text[index] != '}')
                {
                    index++;
                }

                string value = text.Substring(start, index - start).Trim();
                if (value.Length == 0)
                {
                    throw Error("字段值为空");
                }

                return value;
            }
            /// <summary>
            /// 读取令牌。
            /// </summary>

            private string ReadToken(char terminator)
            {
                int start = index;
                while (index < text.Length && text[index] != terminator)
                {
                    index++;
                }

                return text.Substring(start, index - start).Trim();
            }
            /// <summary>
            /// 更新跳过空白字符。
            /// </summary>

            private void SkipWhitespace()
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }
            }
            /// <summary>
            /// 构建预读并返回结果。
            /// </summary>

            private char Peek()
            {
                if (index >= text.Length)
                {
                    throw Error("意外到达文本末尾");
                }

                return text[index];
            }
            /// <summary>
            /// 根据 `expected` 消费`expected`；操作完成时返回 `true`，前置条件不满足时返回 `false`。
            /// </summary>

            private bool TryConsume(char expected)
            {
                SkipWhitespace();
                if (index >= text.Length || text[index] != expected)
                {
                    return false;
                }

                index++;
                return true;
            }
            /// <summary>
            /// 更新`Expect`。
            /// </summary>

            private void Expect(char expected)
            {
                SkipWhitespace();
                if (index >= text.Length || text[index] != expected)
                {
                    throw Error("应为 '" + expected + "'");
                }

                index++;
            }
            /// <summary>
            /// 确保结束。
            /// </summary>

            private void EnsureEnd()
            {
                SkipWhitespace();
                if (index != text.Length)
                {
                    throw Error("数组结束后仍有多余内容");
                }
            }
            /// <summary>
            /// 根据 `message` 构建错误并返回结果。
            /// </summary>

            private FormatException Error(string message)
            {
                return new FormatException(
                    message + "（位置 " +
                    index.ToString(CultureInfo.InvariantCulture) + "）");
            }
        }
    }

    internal static class BattleActionTimelineSchema
    {
        internal static readonly string[] StartupEndAliases =
            { "startupEndTime", "startupEnd" };
        internal static readonly string[] SwitchStartAliases =
            { "actionSwitchWindowStartTime", "switchWindowStartTime", "switchStartTime" };
        internal static readonly string[] RecoveryStartAliases =
            { "recoveryStartTime", "recoveryStart" };
        internal static readonly string[] DurationAliases =
            { "actionDuration", "duration" };
        internal static readonly string[] StepOwnerAliases =
            { "activeSingleId", "activeSingleSkillId", "actionId", "ownerId", "skillId" };
        internal static readonly string[] TriggerTimeAliases =
            { "triggerTime", "time" };
        internal static readonly string[] StepTypeAliases =
            { "stepType", "executionType" };
        internal static readonly string[] EffectConfigAliases =
            { "effectConfigId", "EffectConfigId" };
        internal static readonly string[] InteractionTypeAliases =
            { "interactionType", "attackInteractionType" };
        internal static readonly string[] InteractionTypeNames =
            { "Normal", "Impact", "Defense", "Charged" };
        internal static readonly string[] AttackBodyIdAliases =
            { "attackBodyId", "bodyId" };
        internal static readonly string[] LocalTimeAliases =
            { "localTime", "time" };
        internal static readonly string[] OffsetXAliases =
            { "offsetX", "localOffsetX", "spawnOffsetX" };
        internal static readonly string[] OffsetYAliases =
            { "offsetY", "localOffsetY", "spawnOffsetY" };
        internal static readonly string[] RotationAliases =
            { "rotationDegrees", "localRotationDegrees", "rotation" };
        internal static readonly string[] ScaleXAliases =
            { "scaleX" };
        internal static readonly string[] ScaleYAliases =
            { "scaleY" };
        internal static readonly string[] InterpolationAliases =
            { "interpolation", "interpolationType" };

        internal const string ExampleStructure =
            "[BattleActiveSingleSkill]\n" +
            "id\tcodeName\tanimationId\tstartupEndTime\tactionSwitchWindowStartTime\trecoveryStartTime\tactionDuration\texecutionSteps\n" +
            "[BattleAttackBody]\n" +
            "id\tcodeName\tclashStrength\tclashResistance\tmaxHitsPerTarget\tsameTargetHitInterval\tmaxTotalHitCount\n" +
            "[BattleAttackShape]\n" +
            "id\tcodeName\tattackBodyId\tshapeIndex\tshapeType\toffsetX\toffsetY\trotationDegrees\tsizeX\tsizeY\tradius\tcapsuleDirection\ttransformKeyframes\n" +
            "[BattleMeleeAttackSpawn]\n" +
            "id\tcodeName\tattackBodyId\tactiveDuration\n" +
            "[BattleProjectileLaunch]\n" +
            "id\tcodeName\tprojectileId\tspawnOffsetX\tspawnOffsetY\tdirectionMode\tangleOffsetDegrees\n" +
            "[BattleProjectile]\n" +
            "id\tcodeName\tpresentationResourceId\tattackBodyId\tmovementType\tmovementConfigId\tmaxLifetime\tworldCollisionMode\toneWayPlatformCollisionMode\tdestroyWhenAttackBodyExhausted\n" +
            "[BattleProjectileLinearMovement]\n" +
            "id\tcodeName\tspeed\n" +
            "[BattleProjectileBallisticMovement]\n" +
            "id\tcodeName\tinitialSpeed\tgravityScale\n\n" +
            "executionSteps 的每个元素包含 InteractionType（Normal/Impact/Defense/Charged）和仅供自身效果步骤使用的 EffectConfigId；" +
            "Shape 的关键帧只属于当前 Shape。";
        /// <summary>
        /// 检查 `table` 是否满足“主动单体数据表”条件。
        /// </summary>

        public static bool IsActiveSingleTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, StartupEndAliases) >= 0 &&
                FindColumn(table, SwitchStartAliases) >= 0 &&
                FindColumn(table, RecoveryStartAliases) >= 0 &&
                FindColumn(table, DurationAliases) >= 0;
        }
        /// <summary>
        /// 检查 `table` 是否满足“执行步骤数据表”条件。
        /// </summary>

        public static bool IsExecutionStepTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, TriggerTimeAliases) >= 0 &&
                FindColumn(table, StepTypeAliases) >= 0 &&
                FindColumn(table, StepOwnerAliases) >= 0;
        }
        /// <summary>
        /// 检查 `table` 是否满足“攻击主体数据表”条件。
        /// </summary>

        public static bool IsAttackBodyTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, "clashStrength") >= 0 &&
                FindColumn(table, "clashResistance") >= 0;
        }
        /// <summary>
        /// 检查 `table` 是否满足“形状数据表”条件。
        /// </summary>

        public static bool IsShapeTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, "shapeType") >= 0 &&
                FindColumn(table, AttackBodyIdAliases) >= 0;
        }
        /// <summary>
        /// 检查 `table` 是否满足“近战生成数据表”条件。
        /// </summary>

        public static bool IsMeleeSpawnTable(BattleActionTimelineTableData table)
        {
            return table != null && !IsActiveSingleTable(table) &&
                FindColumn(table, "activeDuration") >= 0 &&
                FindColumn(table, AttackBodyIdAliases) >= 0;
        }
        /// <summary>
        /// 检查 `table` 是否满足“投射物发射数据表”条件。
        /// </summary>

        public static bool IsProjectileLaunchTable(BattleActionTimelineTableData table)
        {
            // 投射物变换轨同样有projectileId/offsetX/offsetY，但不是发射配置。
            return table != null && !table.isInlineProjection && !IsKeyframeTable(table) &&
                FindColumn(table, "projectileId") >= 0 &&
                FindColumn(table, "spawnOffsetX", "offsetX") >= 0 &&
                FindColumn(table, "spawnOffsetY", "offsetY") >= 0;
        }
        /// <summary>
        /// 检查 `table` 是否满足“投射物数据表”条件。
        /// </summary>

        public static bool IsProjectileTable(BattleActionTimelineTableData table)
        {
            return table != null && !IsProjectileLaunchTable(table) &&
                FindColumn(table, "movementConfigId") >= 0 &&
                FindColumn(table, "maxLifetime") >= 0 &&
                FindColumn(table, AttackBodyIdAliases) >= 0;
        }
        /// <summary>
        /// 检查 `table` 是否满足“关键帧数据表”条件。
        /// </summary>

        public static bool IsKeyframeTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, LocalTimeAliases) >= 0 &&
                FindColumn(table, ScaleXAliases) >= 0 &&
                FindColumn(table, ScaleYAliases) >= 0;
        }
        /// <summary>
        /// 检查 `table` 是否满足“线性移动数据表”条件。
        /// </summary>

        public static bool IsLinearMovementTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, "speed") >= 0 &&
                FindColumn(table, "initialSpeed") < 0;
        }
        /// <summary>
        /// 检查 `table` 是否满足“抛物线移动数据表”条件。
        /// </summary>

        public static bool IsBallisticMovementTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, "initialSpeed") >= 0 &&
                FindColumn(table, "gravityScale") >= 0;
        }
        /// <summary>
        /// 查找列。
        /// </summary>

        public static int FindColumn(
            BattleActionTimelineTableData table,
            params string[] aliases)
        {
            return table == null ? -1 : FindColumn(table.headers, aliases);
        }
        /// <summary>
        /// 查找列。
        /// </summary>

        public static int FindColumn(
            IReadOnlyList<string> headers,
            params string[] aliases)
        {
            if (headers == null || aliases == null)
            {
                return -1;
            }

            HashSet<string> wanted = new HashSet<string>(
                aliases.Select(Normalize),
                StringComparer.Ordinal);
            for (int index = 0; index < headers.Count; index++)
            {
                if (wanted.Contains(Normalize(headers[index])))
                {
                    return index;
                }
            }

            return -1;
        }
        /// <summary>
        /// 获取当前 `BattleActionTimelineSchema` 实例。
        /// </summary>

        public static string Get(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record,
            params string[] aliases)
        {
            int column = FindColumn(table, aliases);
            return record != null && column >= 0 && column < record.cells.Length
                ? record.cells[column] ?? string.Empty
                : string.Empty;
        }
        /// <summary>
        /// 获取整数。
        /// </summary>

        public static int GetInt(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record,
            int fallback,
            params string[] aliases)
        {
            return int.TryParse(
                Get(table, record, aliases),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : fallback;
        }

        /// <summary>
        /// 获取步骤在所属 ActiveSingle 中的零基下标。
        /// </summary>
        public static int GetStepIndex(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record)
        {
            if (table == null || record == null || !IsExecutionStepTable(table))
            {
                return -1;
            }

            int ownerId = GetInt(table, record, 0, StepOwnerAliases);
            if (ownerId <= 0)
            {
                return -1;
            }

            int index = 0;
            for (int i = 0; i < table.records.Count; i++)
            {
                BattleActionTimelineRecordData current = table.records[i];
                if (current == record)
                {
                    return index;
                }

                if (GetInt(table, current, 0, StepOwnerAliases) == ownerId)
                {
                    index++;
                }
            }

            return -1;
        }

        /// <summary>
        /// 按所属 ActiveSingle 行和步骤下标生成攻击配置键。
        /// </summary>
        public static int GetStepConfigurationId(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record)
        {
            int ownerId = GetInt(table, record, 0, StepOwnerAliases);
            int stepIndex = GetStepIndex(table, record);
            if (ownerId <= 0 || stepIndex < 0)
            {
                return 0;
            }

            long value = (long)ownerId * 1000L + stepIndex + 1L;
            return value > 0L && value <= int.MaxValue ? (int)value : 0;
        }

        /// <summary>按技能和步骤下标生成攻击配置键。</summary>
        public static int ComposeStepConfigurationId(int activeSingleSkillId, int stepIndex)
        {
            long value = (long)activeSingleSkillId * 1000L + stepIndex + 1L;
            if (activeSingleSkillId <= 0 || stepIndex < 0 || stepIndex >= 1000 ||
                value <= 0L || value > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "技能 ID 或步骤下标无法生成正的 Int32 攻击配置键。");
            }

            return (int)value;
        }

        /// <summary>按攻击体键和 Shape 下标生成 Shape 键。</summary>
        public static int ComposeShapeId(int attackBodyId, int shapeIndex)
        {
            long value = (long)attackBodyId * 1000L + shapeIndex + 1L;
            if (attackBodyId <= 0 || shapeIndex < 0 || shapeIndex >= 1000 ||
                value <= 0L || value > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "攻击体 ID 或 Shape 下标无法生成正的 Int32 Shape 键。");
            }

            return (int)value;
        }
        /// <summary>
        /// 获取`Double`。
        /// </summary>

        public static double GetDouble(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record,
            double fallback,
            params string[] aliases)
        {
            return double.TryParse(
                Get(table, record, aliases),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value)
                ? value
                : fallback;
        }
        /// <summary>
        /// 获取`Bool`。
        /// </summary>

        public static bool GetBool(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record,
            bool fallback,
            params string[] aliases)
        {
            string text = Get(table, record, aliases);
            if (bool.TryParse(text, out bool value))
            {
                return value;
            }

            return text == "1" ? true : text == "0" ? false : fallback;
        }
        /// <summary>
        /// 设置当前 `BattleActionTimelineSchema` 实例。
        /// </summary>

        public static void Set(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record,
            string value,
            params string[] aliases)
        {
            int column = FindColumn(table, aliases);
            if (record == null || column < 0)
            {
                return;
            }

            record.cells[column] = value ?? string.Empty;
            if (column == 0 && int.TryParse(
                    record.cells[column],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int rowId))
            {
                record.rowId = rowId;
            }
        }
        /// <summary>
        /// 设置整数。
        /// </summary>

        public static void SetInt(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record,
            int value,
            params string[] aliases)
        {
            Set(
                table,
                record,
                value.ToString(CultureInfo.InvariantCulture),
                aliases);
        }
        /// <summary>
        /// 设置`Double`。
        /// </summary>

        public static void SetDouble(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record,
            double value,
            params string[] aliases)
        {
            Set(
                table,
                record,
                value.ToString("R", CultureInfo.InvariantCulture),
                aliases);
        }
        /// <summary>
        /// 读取`Phases`。
        /// </summary>

        public static BattleActionTimelinePhaseTimes ReadPhases(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData action)
        {
            return new BattleActionTimelinePhaseTimes(
                GetDouble(table, action, 0d, StartupEndAliases),
                GetDouble(table, action, 0d, SwitchStartAliases),
                GetDouble(table, action, 0d, RecoveryStartAliases),
                GetDouble(table, action, 0d, DurationAliases));
        }
        /// <summary>
        /// 根据 `document`、`actionId` 构建步骤对应动作并返回结果。
        /// </summary>

        public static IEnumerable<BattleActionTimelineRecordData> StepsForAction(
            BattleActionTimelineDocument document,
            int actionId)
        {
            foreach (BattleActionTimelineTableData table in document.tables
                .Where(IsExecutionStepTable))
            {
                foreach (BattleActionTimelineRecordData record in table.records)
                {
                    if (GetInt(table, record, 0, StepOwnerAliases) == actionId)
                    {
                        yield return record;
                    }
                }
            }
        }
        /// <summary>
        /// 查找包含指定记录的时间轴数据表，未找到时返回空引用。
        /// </summary>

        public static BattleActionTimelineTableData TableContaining(
            BattleActionTimelineDocument document,
            BattleActionTimelineRecordData record)
        {
            return document.tables.FirstOrDefault(
                table => table?.records != null && table.records.Contains(record));
        }
        /// <summary>
        /// 根据 `table`、`record` 构建显示名称并返回结果。
        /// </summary>

        public static string DisplayName(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record)
        {
            string name = Get(table, record, "codeName", "name", "displayName");
            return string.IsNullOrWhiteSpace(name)
                ? record.rowId.ToString(CultureInfo.InvariantCulture)
                : record.rowId.ToString(CultureInfo.InvariantCulture) + " · " + name;
        }
        /// <summary>
        /// 判断步骤是否投射物是否满足当前条件。
        /// </summary>

        public static bool StepIsProjectile(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData step)
        {
            string value = Get(table, step, StepTypeAliases).Trim();
            return value.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Launch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value == "1";
        }
        /// <summary>
        /// 判断步骤是否近战是否满足当前条件。
        /// </summary>

        public static bool StepIsMelee(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData step)
        {
            string value = Get(table, step, StepTypeAliases).Trim();
            return value.IndexOf("Melee", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value == "0";
        }

        /// <summary>
        /// 读取步骤交互类型。旧步骤没有字段或字段为 Invalid 时，统一按普通攻击体显示。
        /// </summary>
        public static int GetInteractionTypeIndex(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData step)
        {
            string value = Get(table, step, InteractionTypeAliases).Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals("Invalid", StringComparison.OrdinalIgnoreCase) ||
                value == "0")
            {
                return 0;
            }

            for (int index = 0; index < InteractionTypeNames.Length; index++)
            {
                if (value.Equals(InteractionTypeNames[index], StringComparison.OrdinalIgnoreCase) ||
                    value == (index + 1).ToString(CultureInfo.InvariantCulture))
                {
                    return index;
                }
            }

            return 0;
        }

        public static void SetInteractionType(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData step,
            int index)
        {
            index = Math.Max(0, Math.Min(InteractionTypeNames.Length - 1, index));
            Set(table, step, InteractionTypeNames[index], InteractionTypeAliases);
        }
        /// <summary>
        /// 判断步骤是否自身效果是否满足当前条件。
        /// </summary>

        public static bool StepIsSelfEffect(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData step)
        {
            return TryGetSelfEffectReference(table, step, out _);
        }

        public static bool TryGetSelfEffectReference(BattleActionTimelineTableData table,
            BattleActionTimelineRecordData step, out BattleAttackEffectReference effect)
        {
            effect = default;
            if (!Enum.TryParse(Get(table, step, StepTypeAliases).Trim(), true,
                    out RefData.EnumBattleExecutionStepType type) ||
                (type != RefData.EnumBattleExecutionStepType.ApplySelfOperation &&
                 type != RefData.EnumBattleExecutionStepType.ApplySelfPersistent)) return false;
            effect = new BattleAttackEffectReference(type == RefData.EnumBattleExecutionStepType.ApplySelfOperation
                ? (int)RefData.EnumBattleCombatEffectType.Operation : (int)RefData.EnumBattleCombatEffectType.Persistent,
                GetInt(table, step, 0, EffectConfigAliases));
            return true;
        }
        /// <summary>
        /// 根据 `document`、`predicate` 构建首个`Table`并返回结果。
        /// </summary>

        public static BattleActionTimelineTableData FirstTable(
            BattleActionTimelineDocument document,
            Func<BattleActionTimelineTableData, bool> predicate)
        {
            return document?.tables.FirstOrDefault(predicate);
        }
        /// <summary>
        /// 查找按标识。
        /// </summary>

        public static BattleActionTimelineRecordData FindById(
            BattleActionTimelineTableData table,
            int id)
        {
            return table?.records.FirstOrDefault(record => record != null && record.rowId == id);
        }
        /// <summary>
        /// 根据 `table`、`keyframes`、`localTime` 计算关键帧并返回结果。
        /// </summary>

        public static BattleActionTimelineTransform EvaluateKeyframes(
            BattleActionTimelineTableData table,
            IReadOnlyList<BattleActionTimelineRecordData> keyframes,
            double localTime)
        {
            if (table == null || keyframes == null || keyframes.Count == 0)
            {
                return BattleActionTimelineTransform.Identity;
            }

            List<BattleActionTimelineRecordData> ordered = keyframes
                .OrderBy(record => GetDouble(table, record, 0d, LocalTimeAliases))
                .ToList();
            if (localTime <= GetDouble(table, ordered[0], 0d, LocalTimeAliases))
            {
                return ReadTransform(table, ordered[0]);
            }

            if (localTime >= GetDouble(
                    table,
                    ordered[ordered.Count - 1],
                    0d,
                    LocalTimeAliases))
            {
                return ReadTransform(table, ordered[ordered.Count - 1]);
            }

            for (int index = 1; index < ordered.Count; index++)
            {
                double upperTime = GetDouble(table, ordered[index], 0d, LocalTimeAliases);
                if (localTime >= upperTime)
                {
                    continue;
                }

                BattleActionTimelineRecordData lower = ordered[index - 1];
                double lowerTime = GetDouble(table, lower, 0d, LocalTimeAliases);
                float normalized = (float)Math.Max(
                    0d,
                    Math.Min(1d, (localTime - lowerTime) / (upperTime - lowerTime)));
                float weight = ApplyInterpolation(
                    normalized,
                    Get(table, lower, InterpolationAliases));
                BattleActionTimelineTransform first = ReadTransform(table, lower);
                BattleActionTimelineTransform second = ReadTransform(table, ordered[index]);
                return new BattleActionTimelineTransform(
                    Vector2.LerpUnclamped(first.Offset, second.Offset, weight),
                    Mathf.LerpUnclamped(
                        first.RotationDegrees,
                        second.RotationDegrees,
                        weight),
                    Vector2.LerpUnclamped(first.Scale, second.Scale, weight));
            }

            return ReadTransform(table, ordered[ordered.Count - 1]);
        }
        /// <summary>
        /// 应用插值表达式。
        /// </summary>

        public static float ApplyInterpolation(float time, string interpolation)
        {
            float value = Mathf.Clamp01(time);
            string normalized = (interpolation ?? string.Empty).Trim();
            if (normalized.Equals("Step", StringComparison.OrdinalIgnoreCase) ||
                normalized == "0")
            {
                return 0f;
            }

            if (normalized.Equals("EaseIn", StringComparison.OrdinalIgnoreCase) ||
                normalized == "2")
            {
                return value * value;
            }

            if (normalized.Equals("EaseOut", StringComparison.OrdinalIgnoreCase) ||
                normalized == "3")
            {
                float inverse = 1f - value;
                return 1f - inverse * inverse;
            }

            if (normalized.Equals("EaseInOut", StringComparison.OrdinalIgnoreCase) ||
                normalized == "4")
            {
                return value * value * (3f - 2f * value);
            }

            return value;
        }
        /// <summary>
        /// 校验当前 `in` 实例。
        /// </summary>

        public static void Validate(
            BattleActionTimelineDocument document,
            IList<string> issues)
        {
            issues.Clear();
            if (document == null)
            {
                issues.Add("动作工作集为空。");
                return;
            }

            document.EnsureLists();
            foreach (BattleActionTimelineTableData table in document.tables)
            {
                if (table == null || string.IsNullOrWhiteSpace(table.sheetName))
                {
                    issues.Add("动作工作集包含空 Sheet。");
                    continue;
                }

                if (table.isInlineProjection)
                {
                    BattleActionTimelineTableData source = document.FindTable(
                        table.inlineSourceSheetName);
                    if (source == null || FindColumn(
                            source,
                            table.inlineSourceColumnName) < 0)
                    {
                        issues.Add(table.sheetName + " 的行内投影源列已断开。");
                    }

                    foreach (BattleActionTimelineInlineOwnerData owner
                        in table.inlineOwners)
                    {
                        if (owner != null &&
                            !string.IsNullOrWhiteSpace(owner.parseError))
                        {
                            issues.Add(
                                table.inlineSourceSheetName + " id=" +
                                owner.ownerRowId + " 的 " +
                                table.inlineSourceColumnName + " 无法解析：" +
                                owner.parseError);
                        }
                    }
                }

                HashSet<int> ids = new HashSet<int>();
                foreach (BattleActionTimelineRecordData record in table.records)
                {
                    if (record == null || record.rowId <= 0 || !ids.Add(record.rowId))
                    {
                        issues.Add(table.sheetName + " 包含非法或重复稳定 id。" );
                    }
                    else if (record.cells == null ||
                        record.cells.Length != table.headers.Length)
                    {
                        issues.Add(
                            $"{table.sheetName} id={record.rowId} 的列宽与字段头不一致。" );
                    }
                }
            }

            foreach (BattleActionTimelineTableData actionTable in document.ActionTables())
            {
                foreach (BattleActionTimelineRecordData action in actionTable.records)
                {
                    BattleActionTimelinePhaseTimes phases = ReadPhases(actionTable, action);
                    if (!BattleActionTimelineTime.TryValidatePhases(phases, out string error))
                    {
                        issues.Add($"ActiveSingle {action.rowId}：{error}" );
                    }

                    double previous = -1d;
                    foreach (BattleActionTimelineTableData stepTable in document.tables
                        .Where(IsExecutionStepTable))
                    {
                        foreach (BattleActionTimelineRecordData step in stepTable.records.Where(
                            value => GetInt(stepTable, value, 0, StepOwnerAliases) == action.rowId))
                        {
                            double trigger = GetDouble(
                                stepTable,
                                step,
                                double.NaN,
                                TriggerTimeAliases);
                            if (!BattleActionTimelineTime.IsFinite(trigger) ||
                                trigger < 0d || trigger > phases.Duration)
                            {
                                issues.Add(
                                    $"ExecutionStep {step.rowId} triggerTime 必须位于动作时长内。" );
                            }

                            if (trigger + BattleActionTimelineTime.Epsilon < previous)
                            {
                                issues.Add(
                                    $"ActiveSingle {action.rowId} 的 executionStep 必须按 triggerTime 非递减排列。" );
                            }

                            int configId = GetStepConfigurationId(stepTable, step);
                            if (TryGetSelfEffectReference(stepTable, step, out var selfEffect))
                            {
                                string problem = BattleActionTimelineHitEffects.ReferenceError(document, selfEffect);
                                if (!string.IsNullOrEmpty(problem)) issues.Add($"ExecutionStep {step.rowId} 自身效果：{problem}");
                            }
                            else if (configId <= 0)
                            {
                                issues.Add($"ExecutionStep {step.rowId} 无法按所属技能和步骤下标生成攻击配置键。" );
                            }
                            else if (StepIsMelee(stepTable, step))
                            {
                                BattleActionTimelineTableData meleeTable =
                                    document.tables.FirstOrDefault(table =>
                                        IsMeleeSpawnTable(table) &&
                                        FindById(table, configId) != null);
                                BattleActionTimelineRecordData melee =
                                    FindById(meleeTable, configId);
                                if (melee == null)
                                {
                                    issues.Add(
                                        $"ExecutionStep {step.rowId} 引用的 MeleeSpawn {configId} 不存在。" );
                                }
                                else
                                {
                                    double activeDuration = GetDouble(
                                        meleeTable,
                                        melee,
                                        double.NaN,
                                        "activeDuration");
                                    if (BattleActionTimelineTime.IsFinite(trigger) &&
                                        BattleActionTimelineTime.IsFinite(activeDuration) &&
                                        trigger + activeDuration > phases.Duration +
                                        BattleActionTimelineTime.Epsilon)
                                    {
                                        issues.Add(
                                            $"ExecutionStep {step.rowId} 的近战攻击体结束时间晚于动作结束。" );
                                    }
                                }
                            }
                            else if (StepIsProjectile(stepTable, step))
                            {
                                bool launchExists = document.tables
                                    .Where(IsProjectileLaunchTable)
                                    .Any(table => FindById(table, configId) != null);
                                if (!launchExists)
                                {
                                    issues.Add(
                                        $"ExecutionStep {step.rowId} 引用的 ProjectileLaunch {configId} 不存在。" );
                                }
                            }
                            else if (!StepIsSelfEffect(stepTable, step))
                            {
                                issues.Add(
                                    $"ExecutionStep {step.rowId} 的 stepType 不受支持。" );
                            }

                            previous = trigger;
                        }
                    }
                }
            }

            ValidateBodies(document, issues);
            ValidateSpawnsAndProjectiles(document, issues);
            ValidateKeyframes(document, issues);
        }
        /// <summary>
        /// 校验`Bodies`。
        /// </summary>

        private static void ValidateBodies(
            BattleActionTimelineDocument document,
            IList<string> issues)
        {
            BattleActionTimelineTableData shapeTable = FirstTable(document, IsShapeTable);
            foreach (BattleActionTimelineTableData bodyTable in document.tables
                .Where(IsAttackBodyTable))
            {
                foreach (BattleActionTimelineRecordData body in bodyTable.records)
                {
                    int strength = GetInt(bodyTable, body, 0, "clashStrength");
                    int resistance = GetInt(bodyTable, body, -1, "clashResistance");
                    int maxPerTarget = GetInt(
                        bodyTable,
                        body,
                        0,
                        "maxHitsPerTarget");
                    int maxTotal = GetInt(bodyTable, body, 0, "maxTotalHitCount");
                    double interval = GetDouble(
                        bodyTable,
                        body,
                        double.NaN,
                        "sameTargetHitInterval");
                    if (strength <= 0 || resistance < 0 ||
                        !UnlimitedOrPositive(maxPerTarget) ||
                        !UnlimitedOrPositive(maxTotal) ||
                        !BattleActionTimelineTime.IsFinite(interval) || interval < 0d ||
                        ((maxPerTarget == -1 || maxPerTarget > 1) && interval <= 0d))
                    {
                        issues.Add($"AttackBody {body.rowId} 的抵消或命中限制非法。" );
                    }

                    if (shapeTable != null && !shapeTable.records.Any(shape =>
                        GetInt(shapeTable, shape, 0, AttackBodyIdAliases) == body.rowId))
                    {
                        issues.Add($"AttackBody {body.rowId} 至少需要一个 Shape。" );
                    }

                    if (!BattleActionTimelineHitEffects.TryRead(bodyTable, body, out var effects, out string effectError))
                        issues.Add($"AttackBody {body.rowId} 命中效果解析失败：{effectError}");
                    else
                        foreach (var effect in effects)
                        {
                            string problem = BattleActionTimelineHitEffects.ReferenceError(document, effect);
                            if (!string.IsNullOrEmpty(problem))
                                issues.Add($"AttackBody {body.rowId}：{problem}");
                        }
                }
            }

            if (shapeTable == null)
            {
                return;
            }

            foreach (BattleActionTimelineRecordData shape in shapeTable.records)
            {
                string type = Get(shapeTable, shape, "shapeType").Trim();
                double width = GetDouble(shapeTable, shape, 0d, "width", "sizeX");
                double height = GetDouble(shapeTable, shape, 0d, "height", "sizeY");
                double radius = GetDouble(shapeTable, shape, 0d, "radius");
                bool box = type.Equals("Box", StringComparison.OrdinalIgnoreCase) ||
                    type == "0";
                bool circle = type.Equals(
                        "Circle",
                        StringComparison.OrdinalIgnoreCase) ||
                    type == "1";
                bool capsule = type.Equals(
                        "Capsule",
                        StringComparison.OrdinalIgnoreCase) ||
                    type == "2";
                string capsuleDirection = Get(
                    shapeTable,
                    shape,
                    "capsuleDirection",
                    "direction").Trim();
                bool capsuleDirectionValid = !capsule ||
                    capsuleDirection.Equals(
                        "Vertical",
                        StringComparison.OrdinalIgnoreCase) ||
                    capsuleDirection.Equals(
                        "Horizontal",
                        StringComparison.OrdinalIgnoreCase) ||
                    capsuleDirection == "0" || capsuleDirection == "1";
                bool valid = (circle ? radius > 0d :
                        (box || capsule) && width > 0d && height > 0d) &&
                    capsuleDirectionValid;
                if (!valid || GetInt(shapeTable, shape, 0, AttackBodyIdAliases) <= 0)
                {
                    issues.Add($"Shape {shape.rowId} 的尺寸或 attackBodyId 非法。" );
                }
            }
        }
        /// <summary>
        /// 校验`Spawns`并`Projectiles`。
        /// </summary>

        private static void ValidateSpawnsAndProjectiles(
            BattleActionTimelineDocument document,
            IList<string> issues)
        {
            HashSet<int> bodyIds = new HashSet<int>(document.tables
                .Where(IsAttackBodyTable)
                .SelectMany(table => table.records)
                .Select(record => record.rowId));
            foreach (BattleActionTimelineTableData meleeTable in document.tables
                .Where(IsMeleeSpawnTable))
            {
                foreach (BattleActionTimelineRecordData melee in meleeTable.records)
                {
                    int bodyId = GetInt(meleeTable, melee, 0, AttackBodyIdAliases);
                    double duration = GetDouble(
                        meleeTable,
                        melee,
                        double.NaN,
                        "activeDuration");
                    if (!bodyIds.Contains(bodyId) ||
                        !BattleActionTimelineTime.IsFinite(duration) || duration <= 0d)
                    {
                        issues.Add($"共享 MeleeSpawn {melee.rowId} 的引用或 activeDuration 非法。" );
                    }
                }
            }

            HashSet<int> projectileIds = new HashSet<int>();
            foreach (BattleActionTimelineTableData projectileTable in document.tables
                .Where(IsProjectileTable))
            {
                foreach (BattleActionTimelineRecordData projectile
                    in projectileTable.records)
                {
                    projectileIds.Add(projectile.rowId);
                    int bodyId = GetInt(
                        projectileTable,
                        projectile,
                        0,
                        AttackBodyIdAliases);
                    double lifetime = GetDouble(
                        projectileTable,
                        projectile,
                        double.NaN,
                        "maxLifetime");
                    int movementId = GetInt(
                        projectileTable,
                        projectile,
                        0,
                        "movementConfigId");
                    if (!bodyIds.Contains(bodyId) ||
                        movementId <= 0 ||
                        !BattleActionTimelineTime.IsFinite(lifetime) || lifetime <= 0d)
                    {
                        issues.Add($"共享 Projectile {projectile.rowId} 的引用或寿命非法。" );
                    }

                    string movementType = Get(
                        projectileTable,
                        projectile,
                        "movementType").Trim();
                    bool linear = movementType.Equals(
                            "Linear",
                            StringComparison.OrdinalIgnoreCase) ||
                        movementType == "0";
                    bool ballistic = movementType.Equals(
                            "Ballistic",
                            StringComparison.OrdinalIgnoreCase) ||
                        movementType == "1";
                    bool movementExists = linear
                        ? document.tables.Where(IsLinearMovementTable)
                            .Any(table => FindById(table, movementId) != null)
                        : ballistic && document.tables.Where(IsBallisticMovementTable)
                            .Any(table => FindById(table, movementId) != null);
                    if ((!linear && !ballistic) || !movementExists)
                    {
                        issues.Add(
                            $"共享 Projectile {projectile.rowId} 的 movementType 或 movementConfigId 断链。" );
                    }
                }
            }

            foreach (BattleActionTimelineTableData movementTable in document.tables
                .Where(IsLinearMovementTable))
            {
                foreach (BattleActionTimelineRecordData movement
                    in movementTable.records)
                {
                    double speed = GetDouble(
                        movementTable,
                        movement,
                        double.NaN,
                        "speed");
                    if (!BattleActionTimelineTime.IsFinite(speed) || speed <= 0d)
                    {
                        issues.Add(
                            $"共享 LinearMovement {movement.rowId} 的 speed 必须为正数。" );
                    }
                }
            }

            foreach (BattleActionTimelineTableData movementTable in document.tables
                .Where(IsBallisticMovementTable))
            {
                foreach (BattleActionTimelineRecordData movement
                    in movementTable.records)
                {
                    double speed = GetDouble(
                        movementTable,
                        movement,
                        double.NaN,
                        "initialSpeed");
                    double gravity = GetDouble(
                        movementTable,
                        movement,
                        double.NaN,
                        "gravityScale");
                    if (!BattleActionTimelineTime.IsFinite(speed) || speed <= 0d ||
                        !BattleActionTimelineTime.IsFinite(gravity) || gravity < 0d)
                    {
                        issues.Add(
                            $"共享 BallisticMovement {movement.rowId} 的速度或重力倍率非法。" );
                    }
                }
            }

            foreach (BattleActionTimelineTableData launchTable in document.tables
                .Where(IsProjectileLaunchTable))
            {
                foreach (BattleActionTimelineRecordData launch in launchTable.records)
                {
                    bool projectileExists = projectileIds.Contains(GetInt(
                            launchTable,
                            launch,
                            0,
                            "projectileId"));
                    string direction = Get(
                        launchTable,
                        launch,
                        "directionSource",
                        "directionMode").Trim();
                    bool directionValid = direction.Equals(
                            "ActionFacing",
                            StringComparison.OrdinalIgnoreCase) ||
                        direction.Equals(
                            "ActionAim",
                            StringComparison.OrdinalIgnoreCase) ||
                        direction == "0" || direction == "1";
                    double offsetX = GetDouble(
                        launchTable,
                        launch,
                        double.NaN,
                        "spawnOffsetX",
                        "offsetX");
                    double offsetY = GetDouble(
                        launchTable,
                        launch,
                        double.NaN,
                        "spawnOffsetY",
                        "offsetY");
                    double angle = GetDouble(
                        launchTable,
                        launch,
                        0d,
                        "angleOffsetDegrees");
                    if (!projectileExists || !directionValid ||
                        !BattleActionTimelineTime.IsFinite(offsetX) ||
                        !BattleActionTimelineTime.IsFinite(offsetY) ||
                        !BattleActionTimelineTime.IsFinite(angle))
                    {
                        issues.Add(
                            $"共享 ProjectileLaunch {launch.rowId} 的引用、方向或变换非法。" );
                    }
                }
            }
        }
        /// <summary>
        /// 校验关键帧。
        /// </summary>

        private static void ValidateKeyframes(
            BattleActionTimelineDocument document,
            IList<string> issues)
        {
            foreach (BattleActionTimelineTableData table in document.tables
                .Where(IsKeyframeTable))
            {
                int ownerColumn = FindKeyframeOwnerColumn(table);
                BattleActionTimelineTableData shapes = FirstTable(document, IsShapeTable);
                Dictionary<int, double> previousByOwner = new Dictionary<int, double>();
                foreach (BattleActionTimelineRecordData keyframe in table.records)
                {
                    int ownerId = ownerColumn >= 0
                        ? ParseInt(keyframe.cells[ownerColumn])
                        : 0;
                    double localTime = GetDouble(
                        table,
                        keyframe,
                        double.NaN,
                        LocalTimeAliases);
                    double offsetX = ReadOptionalKeyframeNumber(table, keyframe, 0d, OffsetXAliases);
                    double offsetY = ReadOptionalKeyframeNumber(table, keyframe, 0d, OffsetYAliases);
                    double rotation = ReadOptionalKeyframeNumber(table, keyframe, 0d, RotationAliases);
                    double scaleX = ReadOptionalKeyframeNumber(table, keyframe, 1d, ScaleXAliases);
                    double scaleY = ReadOptionalKeyframeNumber(table, keyframe, 1d, ScaleYAliases);
                    string interpolation = Get(table, keyframe, InterpolationAliases);
                    bool validInterpolation = string.IsNullOrWhiteSpace(interpolation) ||
                        Enum.TryParse(interpolation, true, out RefData.EnumBattleTransformInterpolation mode) &&
                        Enum.IsDefined(typeof(RefData.EnumBattleTransformInterpolation), mode);
                    if (ownerId <= 0 ||
                        !BattleActionTimelineTime.IsFinite(localTime) || localTime < 0d ||
                        !BattleActionTimelineTime.IsFinite(offsetX) ||
                        !BattleActionTimelineTime.IsFinite(offsetY) ||
                        !BattleActionTimelineTime.IsFinite(rotation) ||
                        !BattleActionTimelineTime.IsFinite(scaleX) || scaleX < 0d ||
                        !BattleActionTimelineTime.IsFinite(scaleY) || scaleY < 0d || !validInterpolation)
                    {
                        issues.Add($"Shape TransformKeyframe {keyframe.rowId} 的时间、偏移、旋转、缩放或插值参数非法。" );
                        continue;
                    }

                    if (FindById(shapes, ownerId) == null)
                    {
                        issues.Add($"Shape TransformKeyframe {keyframe.rowId} 的 shapeId={ownerId} 断链。");
                    }

                    if (previousByOwner.TryGetValue(ownerId, out double previous) &&
                        localTime <= previous + BattleActionTimelineTime.Epsilon)
                    {
                        issues.Add(
                            $"{table.sheetName} owner={ownerId} 的 localTime 必须严格递增。" );
                    }

                    previousByOwner[ownerId] = localTime;
                }
            }
        }

        private static double ReadOptionalKeyframeNumber(BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record, double fallback, params string[] aliases) =>
            string.IsNullOrWhiteSpace(Get(table, record, aliases))
                ? fallback : GetDouble(table, record, double.NaN, aliases);
        /// <summary>
        /// 查找关键帧所有者列。
        /// </summary>

        public static int FindKeyframeOwnerColumn(BattleActionTimelineTableData table)
        {
            return FindColumn(
                table,
                "shapeId");
        }
        /// <summary>
        /// 读取变换。
        /// </summary>

        private static BattleActionTimelineTransform ReadTransform(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record)
        {
            return new BattleActionTimelineTransform(
                new Vector2(
                    (float)GetDouble(table, record, 0d, OffsetXAliases),
                    (float)GetDouble(table, record, 0d, OffsetYAliases)),
                (float)GetDouble(table, record, 0d, RotationAliases),
                new Vector2(
                    (float)GetDouble(table, record, 1d, ScaleXAliases),
                    (float)GetDouble(table, record, 1d, ScaleYAliases)));
        }
        /// <summary>
        /// 判断`Unlimited`或正数是否满足当前条件。
        /// </summary>

        private static bool UnlimitedOrPositive(int value)
        {
            return value == -1 || value > 0;
        }
        /// <summary>
        /// 解析整数。
        /// </summary>

        private static int ParseInt(string value)
        {
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result)
                ? result
                : 0;
        }
        /// <summary>
        /// 规范化当前 `in` 实例。
        /// </summary>

        private static string Normalize(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace("_", string.Empty)
                .ToLowerInvariant();
        }
    }
}
#endif
