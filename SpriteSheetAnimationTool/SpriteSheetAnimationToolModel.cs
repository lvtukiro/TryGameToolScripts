#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Game.EditorTools.SpriteSheetAnimation
{
    public enum SpriteSheetSliceMode { CellCount, CellSize, TransparentRegions, Manual }

    /// <summary>所有源图坐标均从左上角开始，右边界和下边界不包含在矩形内。</summary>
    [Serializable]
    public struct SpriteSheetSliceRect
    {
        public int x, y, width, height;
        public int Right => x + width;
        public int Bottom => y + height;
        public SpriteSheetSliceRect(int x, int y, int width, int height)
        { this.x = x; this.y = y; this.width = width; this.height = height; }
        public bool IsInside(int imageWidth, int imageHeight) => x >= 0 && y >= 0
            && width > 0 && height > 0 && (long)x + width <= imageWidth
            && (long)y + height <= imageHeight;
    }

    [Serializable]
    public sealed class SpriteSheetFrameData
    {
        public string name = "frame";
        public bool included = true;
        public SpriteSheetSliceRect rect;
        // 锚点是裁框内部的像素边界坐标，不是透明内容边界。
        // (width/2, height) 表示底部中点；每一帧独立保存，不自动猜测角色身体位置。
        public int anchorX, anchorY;
        public SpriteSheetFrameData() { }
        public SpriteSheetFrameData(SpriteSheetSliceRect rect, string name)
        {
            this.rect = rect; this.name = name;
            anchorX = rect.width / 2; anchorY = rect.height;
        }
    }

    /// <summary>
    /// 本次生成裁框的统一模板，不属于已保存的逐帧数据。
    /// 新开窗口恢复固定默认值；手工修改现有帧不反向改变该模板。
    /// </summary>
    public sealed class SpriteSheetFrameGenerationSettings
    {
        public int width = 512, height = 512;
        public int anchorX = 256, anchorY = 512;
    }

    /// <summary>
    /// 新版非破坏性切割方案。只存原图、裁框、逐帧锚点和显式输出配置，
    /// 不再记录旧工具的自动抠图、内容高度归一化或临时标准化图片。
    /// </summary>
    [Serializable]
    public sealed class SpriteSheetAnimationDocument
    {
        public const string DefaultFrameOutputFolder = "Assets/Resources/TryGameBuildRes/clip_sprite";
        public const string ClipOutputFolder = DefaultFrameOutputFolder + "/sequence_animation";

        public int formatVersion = 2;
        public string sourceImagePath = "", sourceSha256 = "";
        public int sourceWidth, sourceHeight;
        public SpriteSheetSliceMode sliceMode;
        public int columns = 1, rows = 1, cellWidth = 256, cellHeight = 256;
        public int marginLeft, marginTop, marginRight, marginBottom, gapX, gapY;
        public int alphaThreshold = 8, minimumRegionPixels = 32, mergeDistance;
        public List<SpriteSheetFrameData> frames = new List<SpriteSheetFrameData>();
        public bool alignAnchors;
        public int canvasWidth = 512, canvasHeight = 512, targetAnchorX = 256, targetAnchorY = 512;
        // 只有显式启用对齐输出才使用此缩放值，全帧共用同一比例。
        public float scalePercent = 100f;
        public string animationId = "new_sprite_animation";
        public int actionId;
        public float frameRate = 12f;
        public bool loop = true, defaultFacingLeft = true;
        public string outputFolder = DefaultFrameOutputFolder;

        /// <summary>只纠正旧窗口/方案中的错误默认路径，不移动资源，也不改动其它自定义目录。</summary>
        public bool UpgradeLegacyOutputFolder()
        {
            if (!string.Equals(outputFolder?.Replace('\\', '/').TrimEnd('/'),
                ClipOutputFolder, StringComparison.OrdinalIgnoreCase)) return false;
            outputFolder = DefaultFrameOutputFolder;
            return true;
        }
    }

    /// <summary>只读取号：合并已保存源表、现有 Clip 与清单，避免尚未导表的动作被重复分配。</summary>
    public static class SpriteSheetActionIdFinder
    {
        private static readonly XNamespace SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly Regex ActionIdField = new Regex(@"\bactionId""?\s*:\s*(-?\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex ClipName = new Regex(@"^clip_(\d+)_\d+\.asset$", RegexOptions.IgnoreCase);
        private static readonly Regex ClipActionId = new Regex(@"^\s*actionId:\s*(-?\d+)\s*$", RegexOptions.Multiline);

        /// <summary>取最大已用编号 + 1；0 是空动作，跳过。不写文件、不加载或重新初始化运行时 RefData。</summary>
        public static int FindNext(string projectRoot, string additionalOutputFolder = null)
        {
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("缺少项目根目录。");
            projectRoot = Path.GetFullPath(projectRoot);
            string assets = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assets)) throw new DirectoryNotFoundException("找不到项目 Assets：" + assets);
            string source = Path.Combine(projectRoot, "RefDataSource/TryGameRefdataRes/v2");
            int maximum = 0;
            ReadWorkbook(Path.Combine(source, "z.资源相关表.xlsx"), true, ref maximum);
            ReadWorkbook(Path.Combine(source, "r.机器人表.xlsx"), false, ref maximum);
            ScanAssets(assets, ref maximum);
            if (!string.IsNullOrWhiteSpace(additionalOutputFolder))
            {
                string extra = Path.GetFullPath(Path.Combine(projectRoot, additionalOutputFolder));
                string assetsPrefix = assets.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!string.Equals(extra, assets, StringComparison.OrdinalIgnoreCase)
                    && !extra.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(extra)) ScanAssets(extra, ref maximum);
            }

            // 已导出的旧表仍可能引用源表后来移除的动作；同样保留这些编号。
            string jsonFolder = Path.Combine(source, "Output/Json/client");
            foreach (string table in new[] { "SequenceFrameActionResource", "BattleCharacterAction", "BattleActiveSingleSkill" })
            {
                string path = Path.Combine(jsonFolder, table + ".json");
                if (!File.Exists(path)) continue;
                string text = File.ReadAllText(path);
                Regex fields = table == "SequenceFrameActionResource"
                    ? new Regex(@"""id""\s*:\s*(-?\d+)") : ActionIdField;
                CollectFields(text, fields, path, ref maximum);
            }
            if (maximum == int.MaxValue) throw new InvalidOperationException("动作 ID 已达到 int.MaxValue，无法继续顺延。");
            return maximum + 1;
        }

        private static void ScanAssets(string folder, ref int maximum)
        {
            foreach (string path in Directory.EnumerateFiles(folder, "clip_*_*.asset", SearchOption.AllDirectories))
            {
                Match name = ClipName.Match(Path.GetFileName(path));
                if (!name.Success) continue;
                CollectId(name.Groups[1].Value, path, ref maximum);
                CollectFields(File.ReadAllText(path), ClipActionId, path, ref maximum);
            }
            foreach (string pattern in new[] { "*.sequence.json", "*.slices.json" })
                foreach (string path in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(path);
                    if (!ActionIdField.IsMatch(text)) throw new InvalidDataException("动画清单缺少有效动作 ID：" + path);
                    CollectFields(text, ActionIdField, path, ref maximum);
                }
        }

        private static void CollectFields(string text, Regex fields, string label, ref int maximum)
        {
            foreach (Match match in fields.Matches(text)) CollectId(match.Groups[1].Value, label, ref maximum);
        }

        private static void CollectId(string text, string label, ref int maximum)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0)
                throw new InvalidDataException("动作 ID 必须是非负整数：" + label + "，值=" + text);
            maximum = Math.Max(maximum, id);
        }

        private static XDocument ReadXml(ZipArchive archive, string part)
        {
            var entry = archive.GetEntry(part);
            if (entry == null) throw new InvalidDataException("源表缺少 XLSX 成员：" + part);
            using (var stream = entry.Open()) return XDocument.Load(stream);
        }

        private static string CellValue(XElement cell, List<string> shared)
        {
            string type = (string)cell.Attribute("t");
            if (type == "inlineStr") return string.Concat(cell.Descendants(SheetNs + "t").Select(t => t.Value));
            string value = (string)cell.Element(SheetNs + "v") ?? "";
            if (type != "s") return value;
            if (!int.TryParse(value, out int index) || index < 0 || index >= shared.Count)
                throw new InvalidDataException("源表共享字符串索引无效：" + value);
            return shared[index];
        }

        private static string Column(XElement cell) =>
            new string(((string)cell.Attribute("r") ?? "").TakeWhile(char.IsLetter).ToArray());

        private static void ReadWorkbook(string path, bool resourceTable, ref int maximum)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("取号需要读取已保存的动作源表：" + path, path);
            // 允许 Excel 已打开，但只读取磁盘上已保存的数据；读取失败时保留窗口原 ID。
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var shared = new List<string>();
                if (archive.GetEntry("xl/sharedStrings.xml") != null)
                    shared.AddRange(ReadXml(archive, "xl/sharedStrings.xml").Descendants(SheetNs + "si")
                        .Select(si => string.Concat(si.Descendants(SheetNs + "t").Select(t => t.Value))));
                var rels = ReadXml(archive, "xl/_rels/workbook.xml.rels").Root.Elements()
                    .ToDictionary(e => (string)e.Attribute("Id"), e => (string)e.Attribute("Target"));
                bool foundRequiredSheet = false;
                foreach (var sheet in ReadXml(archive, "xl/workbook.xml").Descendants(SheetNs + "sheet"))
                {
                    string name = (string)sheet.Attribute("name");
                    bool resource = name == "SequenceFrameActionResource";
                    bool character = name == "BattleCharacterAction";
                    if (resourceTable && !resource) continue;
                    foundRequiredSheet |= resourceTable ? resource : character;
                    string target = rels[(string)sheet.Attribute(RelNs + "id")];
                    string part = new Uri(new Uri("http://xlsx/xl/workbook.xml"), target).AbsolutePath.TrimStart('/');
                    var rows = ReadXml(archive, part).Descendants(SheetNs + "row").ToList();
                    var header = rows.FirstOrDefault(row => (string)row.Attribute("r") == "1");
                    if (header == null) continue;
                    var columns = header.Elements(SheetNs + "c").ToDictionary(Column, cell => CellValue(cell, shared));
                    var idColumns = columns.Where(pair => string.Equals(pair.Value,
                        resource ? "id" : "actionId", StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray();
                    string entriesColumn = character ? columns.FirstOrDefault(pair => pair.Value == "entries").Key : null;
                    if ((resource || character) && (resource ? idColumns.Length == 0 : entriesColumn == null))
                        throw new InvalidDataException("源表缺少动作 ID/entries 列：" + path + " / " + name);
                    if (idColumns.Length == 0 && entriesColumn == null) continue;
                    foreach (var row in rows)
                    {
                        if (!int.TryParse((string)row.Attribute("r"), out int number) || number < 5) continue;
                        foreach (var cell in row.Elements(SheetNs + "c"))
                        {
                            string column = Column(cell);
                            if (!idColumns.Contains(column) && column != entriesColumn) continue;
                            if (cell.Element(SheetNs + "f") != null)
                                throw new InvalidDataException("动作 ID 不支持未计算的公式，请改为固定值：" + path + " / " + name);
                            string value = CellValue(cell, shared);
                            if (string.IsNullOrWhiteSpace(value)) continue;
                            if (column == entriesColumn) CollectFields(value, ActionIdField, path + " / " + name, ref maximum);
                            else CollectId(value, path + " / " + name, ref maximum);
                        }
                    }
                }
                if (!foundRequiredSheet) throw new InvalidDataException("找不到动作配置工作表：" + path);
            }
        }
    }

    public sealed class SpriteSheetPixels
    {
        public readonly int width, height;
        // RGBA 字节，按从上到下、从左到右排列。算法不依赖 Unity 编辑器，便于像素级回归。
        public readonly byte[] rgba;
        public SpriteSheetPixels(int width, int height, byte[] rgba)
        {
            SpriteSheetSlicing.ValidatePixels(width, height, rgba);
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            this.width = width; this.height = height; this.rgba = rgba;
        }
    }

    /// <summary>
    /// 可独立测试的切割内核。裁框生成、锚点放置与像素复制互相独立。
    /// 不写源文件，不抠图，不按每帧内容边界重新缩放，也不静默裁掉超出画布的内容。
    /// </summary>
    public static class SpriteSheetSlicing
    {
        public const int MaximumFrames = 4096;
        public const int MaximumPixels = 32 * 1024 * 1024;

        public static void ValidatePixels(int width, int height, byte[] rgba = null)
        {
            if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels)
                throw new ArgumentException("图片尺寸无效或超过 3200 万像素处理上限。");
            if (rgba != null && rgba.LongLength != (long)width * height * 4)
                throw new ArgumentException("RGBA 像素数量与图片尺寸不一致。");
        }

        private static void ValidateGrid(int width, int height, SpriteSheetAnimationDocument settings)
        {
            ValidatePixels(width, height);
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.marginLeft < 0 || settings.marginTop < 0 || settings.marginRight < 0
                || settings.marginBottom < 0 || settings.gapX < 0 || settings.gapY < 0)
                throw new ArgumentException("边距和格间距不能为负数。");
            if ((long)settings.marginLeft + settings.marginRight >= width
                || (long)settings.marginTop + settings.marginBottom >= height)
                throw new ArgumentException("边距已占满整张图片。");
        }

        /// <summary>任意行列数；尺寸无法整除时明确拒绝，不丢弃最后几列像素。</summary>
        public static List<SpriteSheetFrameData> GridByCount(int width, int height,
            SpriteSheetAnimationDocument settings)
        {
            ValidateGrid(width, height, settings);
            int columns = settings.columns, rows = settings.rows;
            if (columns <= 0 || rows <= 0 || (long)columns * rows > MaximumFrames)
                throw new ArgumentException("行列数必须大于 0，总帧数不能超过 4096。");
            long usableWidth = width - settings.marginLeft - settings.marginRight
                - (long)(columns - 1) * settings.gapX;
            long usableHeight = height - settings.marginTop - settings.marginBottom
                - (long)(rows - 1) * settings.gapY;
            if (usableWidth <= 0 || usableHeight <= 0 || usableWidth % columns != 0
                || usableHeight % rows != 0)
                throw new ArgumentException("扣除边距、间距后的尺寸不能被行列整除。请调整参数或改用手动裁框。");
            return CreateGrid(columns, rows, (int)(usableWidth / columns),
                (int)(usableHeight / rows), settings);
        }

        /// <summary>按固定单格尺寸取完整帧，末尾不足一格的区域不生成裁框。</summary>
        public static List<SpriteSheetFrameData> GridBySize(int width, int height,
            SpriteSheetAnimationDocument settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return GridBySize(width, height, settings, settings.cellWidth, settings.cellHeight);
        }

        /// <summary>使用窗口当前裁框宽高计算格数，不覆盖已保存方案或旧裁框。</summary>
        public static List<SpriteSheetFrameData> GridBySize(int width, int height,
            SpriteSheetAnimationDocument settings, int cellWidth, int cellHeight)
        {
            ValidateGrid(width, height, settings);
            if (cellWidth <= 0 || cellHeight <= 0)
                throw new ArgumentException("单格宽高必须大于 0。");
            int columns = (int)((width - settings.marginLeft - settings.marginRight
                + (long)settings.gapX) / (cellWidth + (long)settings.gapX));
            int rows = (int)((height - settings.marginTop - settings.marginBottom
                + (long)settings.gapY) / (cellHeight + (long)settings.gapY));
            if (columns <= 0 || rows <= 0 || (long)columns * rows > MaximumFrames)
                throw new ArgumentException("没有可容纳的完整格子，或总帧数超过 4096。");
            return CreateGrid(columns, rows, cellWidth, cellHeight, settings);
        }

        /// <summary>
        /// 保留生成位置和顺序，统一新框的大小与框内锚点。先校验全部新框再返回，
        /// 不修改输入列表，不静默夹紧越界裁框，也不处理或缩放图片像素。
        /// </summary>
        public static List<SpriteSheetFrameData> ApplyGenerationSettings(
            IList<SpriteSheetFrameData> frames, SpriteSheetFrameGenerationSettings settings,
            int imageWidth, int imageHeight)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ValidatePixels(imageWidth, imageHeight);
            ValidatePixels(settings.width, settings.height);
            if (settings.anchorX < 0 || settings.anchorX > settings.width ||
                settings.anchorY < 0 || settings.anchorY > settings.height)
                throw new ArgumentException("生成裁框的中线/底线必须在框内，允许位于边界。");
            if (frames.Count > MaximumFrames) throw new ArgumentException("裁框总数不能超过4096。");
            var result = new List<SpriteSheetFrameData>(frames.Count);
            for (int index = 0; index < frames.Count; index++)
            {
                var frame = frames[index];
                if (frame == null) throw new ArgumentException("待生成裁框不能为空。");
                var rect = new SpriteSheetSliceRect(frame.rect.x, frame.rect.y, settings.width, settings.height);
                if (!rect.IsInside(imageWidth, imageHeight))
                    throw new ArgumentException("第 " + (index + 1) + " 个裁框（X=" + rect.x +
                        "，Y=" + rect.y + "，" + rect.width + "×" + rect.height +
                        "）超出原图。请调整生成宽高、行列或边距；原有裁框未替换。");
                result.Add(new SpriteSheetFrameData(rect, frame.name)
                {
                    included = frame.included,
                    anchorX = settings.anchorX,
                    anchorY = settings.anchorY,
                });
            }
            return result;
        }

        private static List<SpriteSheetFrameData> CreateGrid(int columns, int rows,
            int cellWidth, int cellHeight, SpriteSheetAnimationDocument settings)
        {
            var frames = new List<SpriteSheetFrameData>();
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                    frames.Add(new SpriteSheetFrameData(new SpriteSheetSliceRect(
                        settings.marginLeft + column * (cellWidth + settings.gapX),
                        settings.marginTop + row * (cellHeight + settings.gapY),
                        cellWidth, cellHeight), "frame_" + frames.Count.ToString("D4")));
            return frames;
        }

        /// <summary>
        /// 按 Alpha 的八连通区域生成裁框。小区域只是不独立生成框，不会从原图删除。
        /// 披风断开时可增大合并距离或手动调整；不把不透明背景猜成角色轮廓。
        /// </summary>
        public static List<SpriteSheetFrameData> DetectTransparentRegions(SpriteSheetPixels source,
            int alphaThreshold, int minimumPixels, int mergeDistance)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (alphaThreshold < 0 || alphaThreshold > 254 || minimumPixels < 1
                || mergeDistance < 0 || mergeDistance > Math.Max(source.width, source.height))
                throw new ArgumentException("透明识别阈值、最小区域或合并距离无效。");
            bool hasTransparent = false;
            for (int i = 3; i < source.rgba.Length; i += 4)
                if (source.rgba[i] <= alphaThreshold) { hasTransparent = true; break; }
            if (!hasTransparent)
                throw new ArgumentException("原图没有可用于分隔的透明像素。请用手动裁框或网格；本工具不会猜测渐变背景。");

            int width = source.width, height = source.height;
            var visited = new bool[width * height];
            var queue = new Queue<int>();
            var rectangles = new List<SpriteSheetSliceRect>();
            for (int start = 0; start < visited.Length; start++)
            {
                if (visited[start] || source.rgba[start * 4 + 3] <= alphaThreshold) continue;
                visited[start] = true; queue.Enqueue(start);
                int left = width, top = height, right = -1, bottom = -1, count = 0;
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue(), x = index % width, y = index / width;
                    count++; left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx == 0 && dy == 0) || nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                            int next = ny * width + nx;
                            if (visited[next] || source.rgba[next * 4 + 3] <= alphaThreshold) continue;
                            visited[next] = true; queue.Enqueue(next);
                        }
                }
                if (count < minimumPixels) continue;
                rectangles.Add(new SpriteSheetSliceRect(left, top, right - left + 1, bottom - top + 1));
                if (rectangles.Count > MaximumFrames)
                    throw new ArgumentException("检测到的图块过多。请提高最小区域像素数后重试。");
            }

            // 0 只合并相交框，正值允许合并指定像素距离内的断开图块。
            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < rectangles.Count && !merged; i++)
                    for (int j = i + 1; j < rectangles.Count; j++)
                    {
                        var a = rectangles[i]; var b = rectangles[j];
                        bool separated = mergeDistance == 0
                            ? a.Right <= b.x || b.Right <= a.x || a.Bottom <= b.y || b.Bottom <= a.y
                            : (long)a.Right + mergeDistance < b.x || (long)b.Right + mergeDistance < a.x
                                || (long)a.Bottom + mergeDistance < b.y || (long)b.Bottom + mergeDistance < a.y;
                        if (separated) continue;
                        int left = Math.Min(a.x, b.x), top = Math.Min(a.y, b.y);
                        rectangles[i] = new SpriteSheetSliceRect(left, top,
                            Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
                        rectangles.RemoveAt(j); merged = true; break;
                    }
            } while (merged);

            // 先形成行，再按 X 排序，避免高度不同的图块被普通 Y/X 排序打乱。
            rectangles.Sort((a, b) => a.y.CompareTo(b.y));
            var result = new List<SpriteSheetFrameData>();
            while (rectangles.Count > 0)
            {
                var row = new List<SpriteSheetSliceRect> { rectangles[0] };
                int rowCenter = rectangles[0].y + rectangles[0].height / 2;
                rectangles.RemoveAt(0);
                for (int i = 0; i < rectangles.Count;)
                {
                    var rect = rectangles[i];
                    if (rect.y <= rowCenter && rect.Bottom > rowCenter)
                    { row.Add(rect); rectangles.RemoveAt(i); }
                    else i++;
                }
                row.Sort((a, b) => a.x.CompareTo(b.x));
                foreach (var rect in row)
                    result.Add(new SpriteSheetFrameData(rect, "frame_" + result.Count.ToString("D4")));
            }
            return result;
        }

        public static void ValidateFrame(SpriteSheetPixels source, SpriteSheetFrameData frame)
        {
            if (source == null || frame == null || !frame.rect.IsInside(source.width, source.height))
                throw new ArgumentException("裁框越过原图边界，或裁框宽高无效。");
            if (frame.anchorX < 0 || frame.anchorX > frame.rect.width
                || frame.anchorY < 0 || frame.anchorY > frame.rect.height)
                throw new ArgumentException("逐帧锚点必须位于裁框内部或边缘。");
        }

        /// <summary>纯裁剪按行复制全部 RGBA 字节，包含透明像素中的颜色，不改动源数据。</summary>
        public static SpriteSheetPixels Crop(SpriteSheetPixels source, SpriteSheetFrameData frame)
        {
            ValidateFrame(source, frame);
            var rect = frame.rect;
            var output = new byte[rect.width * rect.height * 4];
            for (int y = 0; y < rect.height; y++)
                Buffer.BlockCopy(source.rgba, ((rect.y + y) * source.width + rect.x) * 4,
                    output, y * rect.width * 4, rect.width * 4);
            return new SpriteSheetPixels(rect.width, rect.height, output);
        }

        private static int ScaleCoordinate(int value, double scale) =>
            (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);

        public static void GetPlacement(SpriteSheetFrameData frame, SpriteSheetAnimationDocument settings,
            out int width, out int height, out int offsetX, out int offsetY)
        {
            if (settings == null || float.IsNaN(settings.scalePercent)
                || settings.scalePercent < 1f || settings.scalePercent > 400f)
                throw new ArgumentException("全帧统一缩放必须在 1% 到 400% 之间。");
            double scale = settings.scalePercent / 100.0;
            width = Math.Max(1, ScaleCoordinate(frame.rect.width, scale));
            height = Math.Max(1, ScaleCoordinate(frame.rect.height, scale));
            offsetX = settings.targetAnchorX - ScaleCoordinate(frame.anchorX, scale);
            offsetY = settings.targetAnchorY - ScaleCoordinate(frame.anchorY, scale);
        }

        /// <summary>显式对齐输出；100% 时只有整数平移，其它比例使用统一最近邻采样。</summary>
        public static SpriteSheetPixels Render(SpriteSheetPixels source, SpriteSheetFrameData frame,
            SpriteSheetAnimationDocument settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ValidateFrame(source, frame);
            if (!settings.alignAnchors) return Crop(source, frame);
            ValidatePixels(settings.canvasWidth, settings.canvasHeight);
            GetPlacement(frame, settings, out int width, out int height, out int offsetX, out int offsetY);
            if (settings.targetAnchorX < 0 || settings.targetAnchorX > settings.canvasWidth
                || settings.targetAnchorY < 0 || settings.targetAnchorY > settings.canvasHeight)
                throw new ArgumentException("输出锚点必须位于输出画布内部或边缘。");
            if (offsetX < 0 || offsetY < 0 || (long)offsetX + width > settings.canvasWidth
                || (long)offsetY + height > settings.canvasHeight)
                throw new ArgumentException("“" + frame.name + "”的完整裁框超出输出画布。请点击自动适配画布，或调整尺寸、锚点；不会自动裁掉像素。");
            var output = new byte[settings.canvasWidth * settings.canvasHeight * 4];
            for (int y = 0; y < height; y++)
            {
                int sourceY = frame.rect.y + Math.Min(frame.rect.height - 1,
                    (int)((y + 0.5) * frame.rect.height / height));
                for (int x = 0; x < width; x++)
                {
                    int sourceX = frame.rect.x + Math.Min(frame.rect.width - 1,
                        (int)((x + 0.5) * frame.rect.width / width));
                    int from = (sourceY * source.width + sourceX) * 4;
                    int to = ((offsetY + y) * settings.canvasWidth + offsetX + x) * 4;
                    Buffer.BlockCopy(source.rgba, from, output, to, 4);
                }
            }
            return new SpriteSheetPixels(settings.canvasWidth, settings.canvasHeight, output);
        }

        /// <summary>只增加所需画布空间，不修改任何帧的裁框、锚点或缩放。</summary>
        public static void FitCanvas(SpriteSheetAnimationDocument settings)
        {
            int left = 0, right = 0, top = 0, bottom = 0, count = 0;
            foreach (var frame in settings.frames)
            {
                if (frame == null || !frame.included) continue;
                GetPlacement(frame, settings, out int width, out int height, out int x, out int y);
                int ax = settings.targetAnchorX - x, ay = settings.targetAnchorY - y;
                left = Math.Max(left, ax); right = Math.Max(right, width - ax);
                top = Math.Max(top, ay); bottom = Math.Max(bottom, height - ay); count++;
            }
            if (count == 0) throw new ArgumentException("没有启用的帧。");
            ValidatePixels(Math.Max(1, left + right), Math.Max(1, top + bottom));
            settings.canvasWidth = Math.Max(1, left + right);
            settings.canvasHeight = Math.Max(1, top + bottom);
            settings.targetAnchorX = left; settings.targetAnchorY = top;
        }
    }
}
#endif
