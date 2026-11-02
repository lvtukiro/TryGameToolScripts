#if SPRITESHEET_SLICING_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Game.EditorTools.SpriteSheetAnimation;

// 独立 .NET 回归，不启动或关闭 Unity，不依赖 NuGet 测试包。
internal static class Program
{
    private static int passed;
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--next-action-id")
        {
            Console.WriteLine("Next unused action ID: " + SpriteSheetActionIdFinder.FindNext(args[1]));
            return;
        }
        Test("默认帧图目录与固定 Clip 目录分开", SeparateExportFolders);
        Test("旧默认目录只升级内存参数，自定义目录不变", LegacyOutputFolderUpgrade);
        Test("新框模板默认512方框、中线256底线512，每个模板独立", GenerationDefaults);
        Test("任意行列生成统一尺寸与锚点，保留网格位置和原有帧", GeneratedFrameSettings);
        Test("生成模板只裁剪不缩放，RGBA逐字节保持", GeneratedFramePixels);
        Test("按单格尺寸使用本次模板，不修改保存方案的参数", GeneratedCellSize);
        Test("透明区域生成也应用统一模板，不猜测或移动内容", GeneratedTransparentFrames);
        Test("生成模板非法或任一框越界时拒绝，输入帧不变", InvalidGenerationSettings);
        Test("动作取号检查未导出源表、角色引用和技能动作，不混用状态/技能 ID", NextActionIdSourceTables);
        Test("动作取号包含未登记 Clip、动画清单和自定义输出目录，只读且不占号", NextActionIdAssets);
        Test("动作取号包含尚未刷新的旧导出配置", NextActionIdExportedTables);
        Test("动作取号源表缺失/损坏及编号耗尽时明确失败", NextActionIdFailures);
        Test("任意行列/横条/竖条/非方格，逐字节裁剪", ArbitraryGrids);
        Test("边距、间隔与固定尺寸网格", MarginsAndSpacing);
        Test("非整除、负参数、超限明确报错", InvalidGrid);
        Test("任意手动矩形与透明 RGB 均保留", ManualCrop);
        Test("纯裁剪忽略全部对齐和缩放参数", RawModeIsRaw);
        Test("100% 对齐只有整数平移", IntegerAlignment);
        Test("两帧独立锚点将同一脚底标记放到相同位置", IndependentAnchors);
        Test("全部帧使用同一显式缩放，不按内容高度缩放", UniformScale);
        Test("最近邻放大不混色", NearestNeighbour);
        Test("画布不足拒绝，原数据不变", RejectClipping);
        Test("自动适配容纳全部帧且不改变逐帧参数", FitCanvas);
        Test("未启用帧不影响画布适配", ExcludedFrame);
        Test("透明区域八连通识别与排序", AlphaRegions);
        Test("小图块筛选不删除原像素", AlphaDoesNotErase);
        Test("不透明背景拒绝自动识别", OpaqueRejected);
        Test("透明识别合并断开的图块", AlphaMerge);
        Test("空图、错误字节数和非法锚点明确报错", InvalidPixelsAndAnchor);
        Test("随机矩形裁剪 250 次逐字节比对", RandomCrops);
        Console.WriteLine("PASS: " + passed + " groups; source pixels unchanged; no Unity process modified.");
    }

    private static void Test(string name, Action test)
    { test(); passed++; Console.WriteLine("PASS " + name); }
    private static void Check(bool condition, string message = "assertion failed")
    { if (!condition) throw new Exception(message); }
    private static void Throws(Action test)
    {
        bool thrown = false;
        try { test(); } catch (ArgumentException) { thrown = true; }
        Check(thrown, "expected a validation error");
    }

    private static readonly XNamespace SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static XDocument ActionSheet(string[] headers, params string[][] data)
    {
        var rows = new List<XElement>();
        var values = new List<string[]> { headers, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>() };
        values.AddRange(data);
        for (int i = 0; i < values.Count; i++)
            rows.Add(new XElement(SheetNs + "row", new XAttribute("r", i + 1),
                values[i].Select((value, column) => new XElement(SheetNs + "c",
                    new XAttribute("r", ((char)('A' + column)).ToString() + (i + 1)), new XAttribute("t", "inlineStr"),
                    new XElement(SheetNs + "is", new XElement(SheetNs + "t", value))))));
        return new XDocument(new XElement(SheetNs + "worksheet", new XElement(SheetNs + "sheetData", rows)));
    }

    private static void WriteActionWorkbook(string path, params (string name, XDocument sheet)[] sheets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var workbook = new XDocument(new XElement(SheetNs + "workbook", new XElement(SheetNs + "sheets",
                sheets.Select((sheet, i) => new XElement(SheetNs + "sheet", new XAttribute("name", sheet.name),
                    new XAttribute(RelNs + "id", "rId" + (i + 1)))))));
            XNamespace package = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relationships = new XDocument(new XElement(package + "Relationships", sheets.Select((sheet, i) =>
                new XElement(package + "Relationship", new XAttribute("Id", "rId" + (i + 1)),
                    // 同时覆盖绝对和相对 XLSX 关系路径。
                    new XAttribute("Target", (i % 2 == 0 ? "/xl/" : "") + "worksheets/sheet" + (i + 1) + ".xml")))));
            using (var output = archive.CreateEntry("xl/workbook.xml").Open()) workbook.Save(output);
            using (var output = archive.CreateEntry("xl/_rels/workbook.xml.rels").Open()) relationships.Save(output);
            for (int i = 0; i < sheets.Length; i++)
                using (var output = archive.CreateEntry("xl/worksheets/sheet" + (i + 1) + ".xml").Open()) sheets[i].sheet.Save(output);
        }
    }

    private static void WithActionProject(Action<string> test, int resourceId = 12, int characterAction = 9, int skillAction = 11)
    {
        string parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ActionIdFixtures"));
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        string source = Path.Combine(root, "RefDataSource/TryGameRefdataRes/v2");
        try
        {
            WriteActionWorkbook(Path.Combine(source, "z.资源相关表.xlsx"),
                ("Unrelated", ActionSheet(new[] { "id" }, new[] { "999999" })),
                ("SequenceFrameActionResource", ActionSheet(new[] { "id", "ruleId" }, new[] { resourceId.ToString(), "10" })));
            WriteActionWorkbook(Path.Combine(source, "r.机器人表.xlsx"),
                ("BattleCharacterAction", ActionSheet(new[] { "id", "entries" },
                    new[] { "999999", "[{ActionId:" + characterAction + ",CharacterId:99999}]" })),
                ("BattleActiveSingleSkill", ActionSheet(new[] { "id", "actionId" }, new[] { "888888", skillAction.ToString() })));
            test(root);
        }
        finally
        {
            // 只清理由本次测试生成、位于固定测试产物目录内的唯一夹具。
            Check(Path.GetFullPath(root).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            Directory.Delete(root, true);
        }
    }

    private static void NextActionIdSourceTables()
    {
        WithActionProject(root => Check(SpriteSheetActionIdFinder.FindNext(root) == 13));
        WithActionProject(root => Check(SpriteSheetActionIdFinder.FindNext(root) == 31), characterAction: 30);
        WithActionProject(root => Check(SpriteSheetActionIdFinder.FindNext(root) == 42), skillAction: 41);
        WithActionProject(root => Check(SpriteSheetActionIdFinder.FindNext(root) == 1), 0, 0, 0);
    }

    private static void NextActionIdAssets()
    {
        WithActionProject(root =>
        {
            string assets = Path.Combine(root, "Assets");
            File.WriteAllText(Path.Combine(assets, "clip_15_1.asset"), "actionId: 16\n");
            File.WriteAllText(Path.Combine(assets, "clip_999_1.asset.meta"), "not an animation");
            Check(SpriteSheetActionIdFinder.FindNext(root) == 17);
            File.WriteAllText(Path.Combine(assets, "test.sequence.json"), "{\"actionId\":18}");
            File.WriteAllText(Path.Combine(assets, "test.slices.json"), "{\"actionId\":20}");
            Check(SpriteSheetActionIdFinder.FindNext(root) == 21);
            string custom = Path.Combine(root, "Frames");
            Directory.CreateDirectory(custom);
            File.WriteAllText(Path.Combine(custom, "test.sequence.json"), "{\"actionId\":25}");
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, path => File.ReadAllBytes(path));
            Check(SpriteSheetActionIdFinder.FindNext(root, "Frames") == 26);
            Check(SpriteSheetActionIdFinder.FindNext(root, "Frames") == 26, "只取号不应创建占号记录");
            Check(before.Count == Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            foreach (var pair in before) Check(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)), "取号修改了文件");
        });
    }

    private static void NextActionIdExportedTables()
    {
        WithActionProject(root =>
        {
            string json = Path.Combine(root, "RefDataSource/TryGameRefdataRes/v2/Output/Json/client");
            Directory.CreateDirectory(json);
            File.WriteAllText(Path.Combine(json, "SequenceFrameActionResource.json"), "{\"Rows\":[{\"id\":40}]}");
            File.WriteAllText(Path.Combine(json, "BattleCharacterAction.json"), "{\"Rows\":[{\"id\":999999,\"entries\":[{\"ActionId\":50}]}]}");
            File.WriteAllText(Path.Combine(json, "BattleActiveSingleSkill.json"), "{\"Rows\":[{\"id\":888888,\"actionId\":60}]}");
            Check(SpriteSheetActionIdFinder.FindNext(root) == 61);
        });
    }

    private static void NextActionIdFailures()
    {
        WithActionProject(root => ExpectActionIdFailure(() => SpriteSheetActionIdFinder.FindNext(root)), int.MaxValue);
        WithActionProject(root =>
        {
            File.Delete(Path.Combine(root, "RefDataSource/TryGameRefdataRes/v2/z.资源相关表.xlsx"));
            ExpectActionIdFailure(() => SpriteSheetActionIdFinder.FindNext(root));
        });
        WithActionProject(root =>
        {
            File.WriteAllText(Path.Combine(root, "RefDataSource/TryGameRefdataRes/v2/r.机器人表.xlsx"), "broken xlsx");
            ExpectActionIdFailure(() => SpriteSheetActionIdFinder.FindNext(root));
        });
        WithActionProject(root =>
        {
            File.WriteAllText(Path.Combine(root, "Assets/broken.slices.json"), "{}");
            ExpectActionIdFailure(() => SpriteSheetActionIdFinder.FindNext(root));
        });
    }

    private static void ExpectActionIdFailure(Action action)
    {
        bool thrown = false;
        try { action(); } catch (Exception) { thrown = true; }
        Check(thrown, "取号数据不完整时必须失败，不能静默返回可能冲突的编号");
    }
    private static SpriteSheetPixels Image(int width, int height)
    {
        var data = new byte[width * height * 4];
        new Random(width * 73 + height).NextBytes(data);
        return new SpriteSheetPixels(width, height, data);
    }
    private static SpriteSheetPixels Transparent(int width, int height) => new SpriteSheetPixels(width, height, new byte[width * height * 4]);
    private static void Set(SpriteSheetPixels image, int x, int y, byte r = 77, byte alpha = 255)
    { int i = (y * image.width + x) * 4; image.rgba[i] = r; image.rgba[i + 3] = alpha; }
    private static void AssertCrop(SpriteSheetPixels source, SpriteSheetFrameData frame, SpriteSheetPixels cut)
    {
        Check(cut.width == frame.rect.width && cut.height == frame.rect.height);
        for (int y = 0; y < cut.height; y++)
            for (int x = 0; x < cut.width; x++)
                for (int c = 0; c < 4; c++)
                    Check(cut.rgba[(y * cut.width + x) * 4 + c]
                        == source.rgba[((frame.rect.y + y) * source.width + frame.rect.x + x) * 4 + c]);
    }

    private static void SeparateExportFolders()
    {
        var document = new SpriteSheetAnimationDocument();
        Check(document.outputFolder == "Assets/Resources/TryGameBuildRes/clip_sprite");
        Check(SpriteSheetAnimationDocument.ClipOutputFolder == document.outputFolder + "/sequence_animation");
        Check(!document.UpgradeLegacyOutputFolder());
    }

    private static void LegacyOutputFolderUpgrade()
    {
        var frame = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 512, 512), "idle");
        foreach (string path in new[] { SpriteSheetAnimationDocument.ClipOutputFolder,
            SpriteSheetAnimationDocument.ClipOutputFolder.Replace('/', '\\') + "\\",
            SpriteSheetAnimationDocument.ClipOutputFolder.ToUpperInvariant() + "/" })
        {
            var document = new SpriteSheetAnimationDocument { outputFolder = path, actionId = 9,
                animationId = "Warrior_Unarmed_Idle", frameRate = 8, defaultFacingLeft = false };
            document.frames.Add(frame);
            Check(document.UpgradeLegacyOutputFolder());
            Check(document.outputFolder == SpriteSheetAnimationDocument.DefaultFrameOutputFolder);
            Check(!document.UpgradeLegacyOutputFolder());
            Check(document.actionId == 9 && document.animationId == "Warrior_Unarmed_Idle"
                && document.frameRate == 8 && !document.defaultFacingLeft && document.frames[0] == frame);
        }
        foreach (string path in new[] { "Assets/CustomFrames", "Assets/CustomFrames/sequence_animation", "", null })
        {
            var document = new SpriteSheetAnimationDocument { outputFolder = path };
            Check(!document.UpgradeLegacyOutputFolder() && document.outputFolder == path);
        }
    }

    private static void GenerationDefaults()
    {
        var first = new SpriteSheetFrameGenerationSettings();
        Check(first.width == 512 && first.height == 512 && first.anchorX == 256 && first.anchorY == 512);
        first.width = 200; first.height = 300; first.anchorX = 10; first.anchorY = 20;
        var reopened = new SpriteSheetFrameGenerationSettings();
        Check(reopened.width == 512 && reopened.height == 512 && reopened.anchorX == 256 && reopened.anchorY == 512);
    }

    private static void GeneratedFrameSettings()
    {
        foreach (var dimensions in new[] { (3, 2), (3, 3), (4, 1), (1, 4) })
        {
            int width = dimensions.Item1 * 512, height = dimensions.Item2 * 512;
            var document = new SpriteSheetAnimationDocument { columns = dimensions.Item1, rows = dimensions.Item2 };
            var originals = SpriteSheetSlicing.GridByCount(width, height, document);
            originals[0].included = false;
            var template = new SpriteSheetFrameGenerationSettings { width = 256, height = 384, anchorX = 80, anchorY = 360 };
            var generated = SpriteSheetSlicing.ApplyGenerationSettings(originals, template, width, height);
            Check(generated.Count == dimensions.Item1 * dimensions.Item2);
            for (int i = 0; i < generated.Count; i++)
            {
                var frame = generated[i];
                Check(!ReferenceEquals(frame, originals[i]));
                Check(frame.rect.x == originals[i].rect.x && frame.rect.y == originals[i].rect.y);
                Check(frame.rect.width == 256 && frame.rect.height == 384 && frame.anchorX == 80 && frame.anchorY == 360);
                Check(frame.name == originals[i].name && frame.included == originals[i].included);
                Check(originals[i].rect.width == 512 && originals[i].rect.height == 512
                    && originals[i].anchorX == 256 && originals[i].anchorY == 512);
            }
            template.width = 1;
            generated[0].anchorX = 2;
            Check(generated[1].rect.width == 256 && generated[1].anchorX == 80);
        }
    }

    private static void GeneratedFramePixels()
    {
        var image = Image(18, 12);
        var before = (byte[])image.rgba.Clone();
        var document = new SpriteSheetAnimationDocument { columns = 3, rows = 2 };
        var template = new SpriteSheetFrameGenerationSettings { width = 4, height = 5, anchorX = 1, anchorY = 4 };
        var frames = SpriteSheetSlicing.ApplyGenerationSettings(
            SpriteSheetSlicing.GridByCount(18, 12, document), template, 18, 12);
        foreach (var frame in frames) AssertCrop(image, frame, SpriteSheetSlicing.Render(image, frame, document));
        Check(before.SequenceEqual(image.rgba));
    }

    private static void GeneratedCellSize()
    {
        var document = new SpriteSheetAnimationDocument { cellWidth = 999, cellHeight = 777,
            marginLeft = 1, marginTop = 2, marginRight = 1, marginBottom = 3, gapX = 2, gapY = 1 };
        var template = new SpriteSheetFrameGenerationSettings { width = 4, height = 5, anchorX = 2, anchorY = 5 };
        var frames = SpriteSheetSlicing.ApplyGenerationSettings(
            SpriteSheetSlicing.GridBySize(20, 20, document, template.width, template.height), template, 20, 20);
        Check(frames.Count == 6);
        Check(frames[0].rect.x == 1 && frames[0].rect.y == 2);
        Check(frames[5].rect.x == 13 && frames[5].rect.y == 8);
        Check(frames.All(frame => frame.rect.width == 4 && frame.rect.height == 5));
        Check(document.cellWidth == 999 && document.cellHeight == 777);
    }

    private static void GeneratedTransparentFrames()
    {
        var image = Transparent(32, 16);
        Set(image, 2, 3); Set(image, 20, 3);
        var before = (byte[])image.rgba.Clone();
        var originals = SpriteSheetSlicing.DetectTransparentRegions(image, 8, 1, 0);
        var template = new SpriteSheetFrameGenerationSettings { width = 6, height = 8, anchorX = 3, anchorY = 8 };
        var frames = SpriteSheetSlicing.ApplyGenerationSettings(originals, template, 32, 16);
        Check(frames.Count == 2 && frames[0].rect.x == 2 && frames[1].rect.x == 20);
        Check(frames.All(frame => frame.rect.y == 3 && frame.rect.width == 6 && frame.rect.height == 8
            && frame.anchorX == 3 && frame.anchorY == 8));
        Check(originals.All(frame => frame.rect.width == 1 && frame.rect.height == 1));
        foreach (var frame in frames) AssertCrop(image, frame, SpriteSheetSlicing.Crop(image, frame));
        Check(before.SequenceEqual(image.rgba));
    }

    private static void InvalidGenerationSettings()
    {
        var frames = new List<SpriteSheetFrameData>
        {
            new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 2, 2), "first"),
            new SpriteSheetFrameData(new SpriteSheetSliceRect(8, 0, 2, 2), "last"),
        };
        foreach (var template in new[]
        {
            new SpriteSheetFrameGenerationSettings { width = 0 },
            new SpriteSheetFrameGenerationSettings { width = 2, height = 2, anchorX = -1, anchorY = 2 },
            new SpriteSheetFrameGenerationSettings { width = 2, height = 2, anchorX = 1, anchorY = 3 },
            new SpriteSheetFrameGenerationSettings { width = 3, height = 3, anchorX = 1, anchorY = 3 },
        }) Throws(() => SpriteSheetSlicing.ApplyGenerationSettings(frames, template, 10, 10));
        Check(frames.Count == 2 && frames[0].rect.width == 2 && frames[1].rect.x == 8
            && frames[1].rect.width == 2 && frames.All(frame => frame.anchorX == 1 && frame.anchorY == 2));
    }

    private static void ArbitraryGrids()
    {
        for (int columns = 1; columns <= 9; columns++)
            for (int rows = 1; rows <= 9; rows++)
            {
                var image = Image(columns * 7, rows * 11);
                var before = (byte[])image.rgba.Clone();
                var settings = new SpriteSheetAnimationDocument { columns = columns, rows = rows };
                var frames = SpriteSheetSlicing.GridByCount(image.width, image.height, settings);
                Check(frames.Count == columns * rows);
                for (int i = 0; i < frames.Count; i++)
                {
                    Check(frames[i].rect.x == i % columns * 7 && frames[i].rect.y == i / columns * 11);
                    AssertCrop(image, frames[i], SpriteSheetSlicing.Crop(image, frames[i]));
                }
                Check(before.SequenceEqual(image.rgba));
            }
    }
    private static void MarginsAndSpacing()
    {
        var s = new SpriteSheetAnimationDocument { columns = 4, rows = 3, marginLeft = 2, marginTop = 3,
            marginRight = 4, marginBottom = 5, gapX = 2, gapY = 1, cellWidth = 7, cellHeight = 9 };
        var frames = SpriteSheetSlicing.GridByCount(40, 37, s);
        Check(frames.Count == 12);
        Check(frames[0].rect.x == 2 && frames[0].rect.y == 3);
        Check(frames[11].rect.x == 29 && frames[11].rect.y == 23);
        Check(frames.All(f => f.rect.width == 7 && f.rect.height == 9));
        var sized = SpriteSheetSlicing.GridBySize(43, 40, s);
        Check(sized.Count == 12 && sized[11].rect.x == 29 && sized[11].rect.y == 23);
    }
    private static void InvalidGrid()
    {
        Throws(() => SpriteSheetSlicing.GridByCount(10, 10, new SpriteSheetAnimationDocument { columns = 3 }));
        Throws(() => SpriteSheetSlicing.GridByCount(10, 10, new SpriteSheetAnimationDocument { rows = 0 }));
        Throws(() => SpriteSheetSlicing.GridByCount(100, 100, new SpriteSheetAnimationDocument { columns = 100, rows = 100 }));
        Throws(() => SpriteSheetSlicing.GridByCount(10, 10, new SpriteSheetAnimationDocument { marginLeft = -1 }));
        Throws(() => SpriteSheetSlicing.GridByCount(10, 10, new SpriteSheetAnimationDocument { gapX = int.MaxValue, columns = 9 }));
        Throws(() => SpriteSheetSlicing.GridBySize(10, 10, new SpriteSheetAnimationDocument { cellWidth = 11 }));
    }
    private static void ManualCrop()
    {
        var image = Image(31, 19);
        var f = new SpriteSheetFrameData(new SpriteSheetSliceRect(3, 7, 17, 6), "manual");
        AssertCrop(image, f, SpriteSheetSlicing.Crop(image, f));
    }
    private static void RawModeIsRaw()
    {
        var image = Image(13, 11);
        var f = new SpriteSheetFrameData(new SpriteSheetSliceRect(1, 2, 7, 5), "raw");
        var s = new SpriteSheetAnimationDocument { alignAnchors = false, scalePercent = 400, canvasWidth = 1, canvasHeight = 1 };
        AssertCrop(image, f, SpriteSheetSlicing.Render(image, f, s));
    }
    private static void IntegerAlignment()
    {
        var image = Image(10, 8);
        var f = new SpriteSheetFrameData(new SpriteSheetSliceRect(2, 1, 5, 4), "aligned") { anchorX = 2, anchorY = 3 };
        var s = new SpriteSheetAnimationDocument { alignAnchors = true, canvasWidth = 13, canvasHeight = 12, targetAnchorX = 6, targetAnchorY = 8 };
        var result = SpriteSheetSlicing.Render(image, f, s);
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 13; x++)
                for (int c = 0; c < 4; c++)
                    Check(result.rgba[(y * 13 + x) * 4 + c] == (x >= 4 && x < 9 && y >= 5 && y < 9
                        ? image.rgba[((y - 5 + 1) * 10 + x - 4 + 2) * 4 + c] : 0));
    }
    private static void IndependentAnchors()
    {
        var image = Transparent(20, 10); Set(image, 2, 7, 99); Set(image, 17, 5, 99);
        var a = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 10, 10), "a") { anchorX = 2, anchorY = 7 };
        var b = new SpriteSheetFrameData(new SpriteSheetSliceRect(10, 0, 10, 10), "b") { anchorX = 7, anchorY = 5 };
        var s = new SpriteSheetAnimationDocument { alignAnchors = true, canvasWidth = 30, canvasHeight = 25, targetAnchorX = 12, targetAnchorY = 15 };
        var ra = SpriteSheetSlicing.Render(image, a, s); var rb = SpriteSheetSlicing.Render(image, b, s);
        Check(ra.rgba[(15 * 30 + 12) * 4] == 99 && rb.rgba[(15 * 30 + 12) * 4] == 99);
    }
    private static void UniformScale()
    {
        var a = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 10, 20), "a");
        var b = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 10, 10), "b");
        var s = new SpriteSheetAnimationDocument { scalePercent = 150 };
        SpriteSheetSlicing.GetPlacement(a, s, out int aw, out int ah, out _, out _);
        SpriteSheetSlicing.GetPlacement(b, s, out int bw, out int bh, out _, out _);
        Check(aw == 15 && bw == 15 && ah == 30 && bh == 15);
    }
    private static void NearestNeighbour()
    {
        var image = Image(4, 3); var f = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 4, 3), "scale");
        var s = new SpriteSheetAnimationDocument { alignAnchors = true, canvasWidth = 8, canvasHeight = 6,
            targetAnchorX = 4, targetAnchorY = 6, scalePercent = 200 };
        var result = SpriteSheetSlicing.Render(image, f, s);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++)
                for (int c = 0; c < 4; c++)
                    Check(result.rgba[(y * 8 + x) * 4 + c] == image.rgba[((y / 2) * 4 + x / 2) * 4 + c]);
    }
    private static void RejectClipping()
    {
        var image = Image(10, 10); var before = (byte[])image.rgba.Clone();
        var f = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 10, 10), "large");
        var s = new SpriteSheetAnimationDocument { alignAnchors = true, canvasWidth = 5, canvasHeight = 5 };
        Throws(() => SpriteSheetSlicing.Render(image, f, s)); Check(before.SequenceEqual(image.rgba));
    }
    private static void FitCanvas()
    {
        var image = Image(20, 20);
        var a = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 8, 10), "a") { anchorX = 3, anchorY = 7 };
        var b = new SpriteSheetFrameData(new SpriteSheetSliceRect(3, 4, 12, 6), "b") { anchorX = 9, anchorY = 2 };
        var s = new SpriteSheetAnimationDocument { alignAnchors = true, frames = new List<SpriteSheetFrameData> { a, b } };
        SpriteSheetSlicing.FitCanvas(s);
        Check(s.canvasWidth == 14 && s.canvasHeight == 11 && s.targetAnchorX == 9 && s.targetAnchorY == 7);
        SpriteSheetSlicing.Render(image, a, s); SpriteSheetSlicing.Render(image, b, s);
        Check(a.rect.width == 8 && a.anchorX == 3 && b.rect.y == 4 && b.anchorY == 2);
    }
    private static void ExcludedFrame()
    {
        var a = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 4, 5), "a");
        var b = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 1000, 1000), "b") { included = false };
        var s = new SpriteSheetAnimationDocument { frames = new List<SpriteSheetFrameData> { a, b } };
        SpriteSheetSlicing.FitCanvas(s); Check(s.canvasWidth == 4 && s.canvasHeight == 5);
    }
    private static void AlphaRegions()
    {
        var image = Transparent(12, 10);
        Set(image, 1, 1); Set(image, 2, 2); // 斜向相连仍是一块。
        Set(image, 8, 1); Set(image, 8, 2); Set(image, 2, 8);
        var frames = SpriteSheetSlicing.DetectTransparentRegions(image, 8, 1, 0);
        Check(frames.Count == 3 && frames[0].rect.x == 1 && frames[1].rect.x == 8 && frames[2].rect.y == 8);
        Check(frames[0].rect.width == 2 && frames[0].rect.height == 2);
    }
    private static void AlphaDoesNotErase()
    {
        var image = Transparent(12, 10); Set(image, 1, 1); Set(image, 2, 2); Set(image, 9, 8);
        var before = (byte[])image.rgba.Clone();
        var frames = SpriteSheetSlicing.DetectTransparentRegions(image, 8, 2, 0);
        Check(frames.Count == 1 && before.SequenceEqual(image.rgba));
    }
    private static void OpaqueRejected()
    {
        var image = Image(4, 4); for (int i = 3; i < image.rgba.Length; i += 4) image.rgba[i] = 255;
        Throws(() => SpriteSheetSlicing.DetectTransparentRegions(image, 8, 1, 0));
    }
    private static void AlphaMerge()
    {
        var image = Transparent(10, 5); Set(image, 1, 1); Set(image, 4, 1);
        Check(SpriteSheetSlicing.DetectTransparentRegions(image, 8, 1, 0).Count == 2);
        Check(SpriteSheetSlicing.DetectTransparentRegions(image, 8, 1, 2).Count == 1);
        Check(SpriteSheetSlicing.DetectTransparentRegions(image, 8, 1, 3).Count == 1);
    }
    private static void InvalidPixelsAndAnchor()
    {
        Throws(() => new SpriteSheetPixels(0, 3, new byte[0]));
        Throws(() => new SpriteSheetPixels(10, 10, new byte[4]));
        Throws(() => SpriteSheetSlicing.ValidatePixels(int.MaxValue, int.MaxValue));
        var f = new SpriteSheetFrameData(new SpriteSheetSliceRect(0, 0, 3, 3), "invalid") { anchorX = 4 };
        Throws(() => SpriteSheetSlicing.Crop(Image(4, 4), f));
        Throws(() => SpriteSheetSlicing.Crop(Image(4, 4), new SpriteSheetFrameData(new SpriteSheetSliceRect(3, 3, 2, 2), "bad")));
    }
    private static void RandomCrops()
    {
        var random = new Random(37); var image = Image(71, 53); var before = (byte[])image.rgba.Clone();
        for (int i = 0; i < 250; i++)
        {
            int x = random.Next(71), y = random.Next(53);
            var f = new SpriteSheetFrameData(new SpriteSheetSliceRect(x, y, random.Next(1, 72 - x), random.Next(1, 54 - y)), "random");
            AssertCrop(image, f, SpriteSheetSlicing.Crop(image, f));
        }
        Check(before.SequenceEqual(image.rgba));
    }
}
#endif
