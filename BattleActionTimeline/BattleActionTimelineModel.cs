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

        public BattleActionTimelineDocument Clone()
        {
            BattleActionTimelineDocument clone = JsonUtility.FromJson<
                BattleActionTimelineDocument>(JsonUtility.ToJson(this));
            clone ??= new BattleActionTimelineDocument();
            clone.EnsureLists();
            return clone;
        }

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

        public BattleActionTimelineTableData FindTable(string sheetName)
        {
            return tables.FirstOrDefault(table => table != null && string.Equals(
                table.sheetName,
                sheetName,
                StringComparison.Ordinal));
        }

        public BattleActionTimelineRecordData FindRecord(string sheetName, int rowId)
        {
            return FindTable(sheetName)?.records.FirstOrDefault(
                record => record != null && record.rowId == rowId);
        }

        public IEnumerable<BattleActionTimelineTableData> ActionTables()
        {
            return tables.Where(BattleActionTimelineSchema.IsActiveSingleTable);
        }

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
            return result;
        }

        private void AddInlineProjections()
        {
            List<BattleActionTimelineTableData> sourceTables = tables
                .Where(table => table != null && !table.isInlineProjection)
                .ToList();
            bool hasExecutionStepSheet = sourceTables.Any(
                BattleActionTimelineSchema.IsExecutionStepTable);
            bool hasShapeSheet = sourceTables.Any(
                BattleActionTimelineSchema.IsShapeTable);
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
                        InlineField("stepConfigId", "StepConfigId"));
                }

                if (!hasShapeSheet &&
                    BattleActionTimelineSchema.IsAttackBodyTable(source))
                {
                    AddInlineProjection(
                        source,
                        new[] { "shapes", "attackShapes" },
                        "AttackBodyShape",
                        "attackBodyId",
                        InlineField("shapeType", "ShapeType"),
                        InlineField("offsetX", "OffsetX"),
                        InlineField("offsetY", "OffsetY"),
                        InlineField("rotationDegrees", "RotationDegrees"),
                        InlineField("sizeX", "SizeX"),
                        InlineField("sizeY", "SizeY"),
                        InlineField("radius", "Radius"),
                        InlineField("capsuleDirection", "CapsuleDirection"));
                }

                if (!hasKeyframeSheet &&
                    BattleActionTimelineSchema.IsMeleeSpawnTable(source))
                {
                    AddInlineProjection(
                        source,
                        new[] { "transformKeyframes" },
                        "MeleeTransformKeyframe",
                        "meleeAttackSpawnId",
                        TransformInlineFields());
                }

                if (!hasKeyframeSheet &&
                    BattleActionTimelineSchema.IsProjectileTable(source))
                {
                    AddInlineProjection(
                        source,
                        new[] { "attackBodyTransformKeyframes", "transformKeyframes" },
                        "ProjectileTransformKeyframe",
                        "projectileId",
                        TransformInlineFields());
                }
            }
        }

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
                    if (string.Equals(
                            currentSignature,
                            owner.baselineSignature,
                            StringComparison.Ordinal))
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
                            projection.records.Where(record =>
                                InlineOwnerId(projection, record) ==
                                owner.ownerRowId));
                }
            }

            return result;
        }

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

        private static BattleActionTimelineInlineFieldData CloneInlineField(
            BattleActionTimelineInlineFieldData source)
        {
            return InlineField(source.headerName, source.serializedName);
        }

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
        public bool isInlineProjection;
        public string inlineSourceSheetName = string.Empty;
        public string inlineSourceColumnName = string.Empty;
        public List<BattleActionTimelineInlineFieldData> inlineFields =
            new List<BattleActionTimelineInlineFieldData>();
        public List<BattleActionTimelineInlineOwnerData> inlineOwners =
            new List<BattleActionTimelineInlineOwnerData>();

        public int AllocateRowId()
        {
            int maximum = 0;
            for (int index = 0; index < records.Count; index++)
            {
                maximum = Math.Max(maximum, records[index]?.rowId ?? 0);
            }

            if (maximum == int.MaxValue)
            {
                throw new InvalidOperationException(
                    sheetName + " 的稳定 rowId 已耗尽。");
            }

            return Math.Max(1, maximum + 1);
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
        public BattleActionTimelineTransform(
            Vector2 offset,
            float rotationDegrees,
            Vector2 scale)
        {
            Offset = offset;
            RotationDegrees = rotationDegrees;
            Scale = scale;
        }

        public static BattleActionTimelineTransform Identity =>
            new BattleActionTimelineTransform(Vector2.zero, 0f, Vector2.one);

        public Vector2 Offset { get; }
        public float RotationDegrees { get; }
        public Vector2 Scale { get; }
    }

    internal readonly struct BattleActionTimelinePhaseTimes
    {
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

        public double StartupEnd { get; }
        public double SwitchWindowStart { get; }
        public double RecoveryStart { get; }
        public double Duration { get; }
    }

    internal static class BattleActionTimelineTime
    {
        public const double Epsilon = 0.0000001d;

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

        public static double FrameToSeconds(int frame, double frameRate)
        {
            ValidateFrameRate(frameRate);
            return frame / frameRate;
        }

        public static double SnapSeconds(double seconds, double frameRate)
        {
            return FrameToSeconds(SecondsToFrame(seconds, frameRate), frameRate);
        }

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

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void ValidateFrameRate(double frameRate)
        {
            if (!IsFinite(frameRate) || frameRate <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate));
            }
        }
    }

    internal static class BattleActionTimelineInlineStructCodec
    {
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
                        continue;
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

            public Parser(string text)
            {
                this.text = text;
            }

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

            private string ReadToken(char terminator)
            {
                int start = index;
                while (index < text.Length && text[index] != terminator)
                {
                    index++;
                }

                return text.Substring(start, index - start).Trim();
            }

            private void SkipWhitespace()
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }
            }

            private char Peek()
            {
                if (index >= text.Length)
                {
                    throw Error("意外到达文本末尾");
                }

                return text[index];
            }

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

            private void Expect(char expected)
            {
                SkipWhitespace();
                if (index >= text.Length || text[index] != expected)
                {
                    throw Error("应为 '" + expected + "'");
                }

                index++;
            }

            private void EnsureEnd()
            {
                SkipWhitespace();
                if (index != text.Length)
                {
                    throw Error("数组结束后仍有多余内容");
                }
            }

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
        internal static readonly string[] StepConfigAliases =
            { "stepConfigId", "configId", "targetId" };
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
            "id\tcodeName\tshapes\tclashStrength\tclashResistance\tmaxHitsPerTarget\tsameTargetHitInterval\tmaxTotalHitCount\n" +
            "[BattleMeleeAttackSpawn]\n" +
            "id\tcodeName\tattackBodyId\tactiveDuration\ttransformKeyframes\n" +
            "[BattleProjectileLaunch]\n" +
            "id\tcodeName\tprojectileId\tspawnOffsetX\tspawnOffsetY\tdirectionMode\tangleOffsetDegrees\n" +
            "[BattleProjectile]\n" +
            "id\tcodeName\tpresentationResourceId\tattackBodyId\tmovementType\tmovementConfigId\tmaxLifetime\tworldCollisionMode\toneWayPlatformCollisionMode\tdestroyWhenAttackBodyExhausted\tattackBodyTransformKeyframes\n" +
            "[BattleProjectileLinearMovement]\n" +
            "id\tcodeName\tspeed\n" +
            "[BattleProjectileBallisticMovement]\n" +
            "id\tcodeName\tinitialSpeed\tgravityScale\n\n" +
            "executionSteps / shapes / transformKeyframes 为 repeated struct 单元格；" +
            "工具也兼容已拆成独立子 Sheet 的等价结构。";

        public static bool IsActiveSingleTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, StartupEndAliases) >= 0 &&
                FindColumn(table, SwitchStartAliases) >= 0 &&
                FindColumn(table, RecoveryStartAliases) >= 0 &&
                FindColumn(table, DurationAliases) >= 0;
        }

        public static bool IsExecutionStepTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, TriggerTimeAliases) >= 0 &&
                FindColumn(table, StepTypeAliases) >= 0 &&
                FindColumn(table, StepConfigAliases) >= 0 &&
                FindColumn(table, StepOwnerAliases) >= 0;
        }

        public static bool IsAttackBodyTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, "clashStrength") >= 0 &&
                FindColumn(table, "clashResistance") >= 0;
        }

        public static bool IsShapeTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, "shapeType") >= 0 &&
                FindColumn(table, AttackBodyIdAliases) >= 0;
        }

        public static bool IsMeleeSpawnTable(BattleActionTimelineTableData table)
        {
            return table != null && !IsActiveSingleTable(table) &&
                FindColumn(table, "activeDuration") >= 0 &&
                FindColumn(table, AttackBodyIdAliases) >= 0;
        }

        public static bool IsProjectileLaunchTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, "projectileId") >= 0 &&
                FindColumn(table, "spawnOffsetX", "offsetX") >= 0 &&
                FindColumn(table, "spawnOffsetY", "offsetY") >= 0;
        }

        public static bool IsProjectileTable(BattleActionTimelineTableData table)
        {
            return table != null && !IsProjectileLaunchTable(table) &&
                FindColumn(table, "movementConfigId") >= 0 &&
                FindColumn(table, "maxLifetime") >= 0 &&
                FindColumn(table, AttackBodyIdAliases) >= 0;
        }

        public static bool IsKeyframeTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, LocalTimeAliases) >= 0 &&
                FindColumn(table, ScaleXAliases) >= 0 &&
                FindColumn(table, ScaleYAliases) >= 0;
        }

        public static bool IsLinearMovementTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, "speed") >= 0 &&
                FindColumn(table, "initialSpeed") < 0;
        }

        public static bool IsBallisticMovementTable(BattleActionTimelineTableData table)
        {
            return table != null &&
                FindColumn(table, "initialSpeed") >= 0 &&
                FindColumn(table, "gravityScale") >= 0;
        }

        public static int FindColumn(
            BattleActionTimelineTableData table,
            params string[] aliases)
        {
            return table == null ? -1 : FindColumn(table.headers, aliases);
        }

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

        public static BattleActionTimelineTableData TableContaining(
            BattleActionTimelineDocument document,
            BattleActionTimelineRecordData record)
        {
            return document.tables.FirstOrDefault(
                table => table?.records != null && table.records.Contains(record));
        }

        public static string DisplayName(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData record)
        {
            string name = Get(table, record, "codeName", "name", "displayName");
            return string.IsNullOrWhiteSpace(name)
                ? record.rowId.ToString(CultureInfo.InvariantCulture)
                : record.rowId.ToString(CultureInfo.InvariantCulture) + " · " + name;
        }

        public static bool StepIsProjectile(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData step)
        {
            string value = Get(table, step, StepTypeAliases).Trim();
            return value.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Launch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value == "1";
        }

        public static bool StepIsMelee(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData step)
        {
            string value = Get(table, step, StepTypeAliases).Trim();
            return value.IndexOf("Melee", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value == "0";
        }

        public static bool StepIsSelfEffect(
            BattleActionTimelineTableData table,
            BattleActionTimelineRecordData step)
        {
            string value = Get(table, step, StepTypeAliases).Trim();
            return value.Equals(
                    "ApplySelfEffect",
                    StringComparison.OrdinalIgnoreCase) ||
                value == "2";
        }

        public static BattleActionTimelineTableData FirstTable(
            BattleActionTimelineDocument document,
            Func<BattleActionTimelineTableData, bool> predicate)
        {
            return document?.tables.FirstOrDefault(predicate);
        }

        public static BattleActionTimelineRecordData FindById(
            BattleActionTimelineTableData table,
            int id)
        {
            return table?.records.FirstOrDefault(record => record != null && record.rowId == id);
        }

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

                            int configId = GetInt(
                                stepTable,
                                step,
                                0,
                                StepConfigAliases);
                            if (configId <= 0)
                            {
                                issues.Add($"ExecutionStep {step.rowId} 缺少正的 stepConfigId。" );
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
                        issues.Add($"共享 AttackBody {body.rowId} 的抵消或命中限制非法。" );
                    }

                    if (shapeTable != null && !shapeTable.records.Any(shape =>
                        GetInt(shapeTable, shape, 0, AttackBodyIdAliases) == body.rowId))
                    {
                        issues.Add($"共享 AttackBody {body.rowId} 至少需要一个 Shape。" );
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
                    issues.Add($"共享 Shape {shape.rowId} 的尺寸或 attackBodyId 非法。" );
                }
            }
        }

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

        private static void ValidateKeyframes(
            BattleActionTimelineDocument document,
            IList<string> issues)
        {
            foreach (BattleActionTimelineTableData table in document.tables
                .Where(IsKeyframeTable))
            {
                int ownerColumn = FindKeyframeOwnerColumn(table);
                string ownerHeader = ownerColumn >= 0 && ownerColumn < table.headers.Length
                    ? table.headers[ownerColumn] ?? string.Empty
                    : string.Empty;
                bool projectileOwner = ownerHeader.IndexOf(
                        "projectile",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    table.sheetName.IndexOf(
                        "projectile",
                        StringComparison.OrdinalIgnoreCase) >= 0;
                bool meleeOwner = ownerHeader.IndexOf(
                        "melee",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    table.sheetName.IndexOf(
                        "melee",
                        StringComparison.OrdinalIgnoreCase) >= 0;
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
                    double scaleX = GetDouble(table, keyframe, 0d, ScaleXAliases);
                    double scaleY = GetDouble(table, keyframe, 0d, ScaleYAliases);
                    if (ownerId <= 0 ||
                        !BattleActionTimelineTime.IsFinite(localTime) || localTime < 0d ||
                        scaleX <= 0d || scaleY <= 0d)
                    {
                        issues.Add($"共享 TransformKeyframe {keyframe.rowId} 的参数非法。" );
                        continue;
                    }

                    if (projectileOwner || meleeOwner)
                    {
                        BattleActionTimelineTableData ownerTable =
                            document.tables.FirstOrDefault(candidate =>
                                (projectileOwner
                                    ? IsProjectileTable(candidate)
                                    : IsMeleeSpawnTable(candidate)) &&
                                FindById(candidate, ownerId) != null);
                        BattleActionTimelineRecordData owner =
                            FindById(ownerTable, ownerId);
                        double lifetime = owner == null
                            ? double.NaN
                            : GetDouble(
                                ownerTable,
                                owner,
                                double.NaN,
                                projectileOwner ? "maxLifetime" : "activeDuration");
                        if (owner == null ||
                            !BattleActionTimelineTime.IsFinite(lifetime) ||
                            localTime > lifetime + BattleActionTimelineTime.Epsilon)
                        {
                            issues.Add(
                                $"共享 TransformKeyframe {keyframe.rowId} 的 owner 断链或 localTime 超出寿命。" );
                        }
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

        public static int FindKeyframeOwnerColumn(BattleActionTimelineTableData table)
        {
            return FindColumn(
                table,
                "meleeAttackSpawnId",
                "meleeSpawnId",
                "spawnId",
                "projectileId",
                "ownerId",
                "attackOwnerId");
        }

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

        private static bool UnlimitedOrPositive(int value)
        {
            return value == -1 || value > 0;
        }

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
