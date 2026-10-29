#if UNITY_EDITOR
using System;
using System.Collections.Generic;

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
    /// 新版非破坏性切割方案。只存原图、裁框、逐帧锚点和显式输出配置，
    /// 不再记录旧工具的自动抠图、内容高度归一化或临时标准化图片。
    /// </summary>
    [Serializable]
    public sealed class SpriteSheetAnimationDocument
    {
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
        public string outputFolder = "Assets/Resources/TryGameBuildRes/clip_sprite/sequence_animation";
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
            ValidateGrid(width, height, settings);
            if (settings.cellWidth <= 0 || settings.cellHeight <= 0)
                throw new ArgumentException("单格宽高必须大于 0。");
            int columns = (int)((width - settings.marginLeft - settings.marginRight
                + (long)settings.gapX) / (settings.cellWidth + (long)settings.gapX));
            int rows = (int)((height - settings.marginTop - settings.marginBottom
                + (long)settings.gapY) / (settings.cellHeight + (long)settings.gapY));
            if (columns <= 0 || rows <= 0 || (long)columns * rows > MaximumFrames)
                throw new ArgumentException("没有可容纳的完整格子，或总帧数超过 4096。");
            return CreateGrid(columns, rows, settings.cellWidth, settings.cellHeight, settings);
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
