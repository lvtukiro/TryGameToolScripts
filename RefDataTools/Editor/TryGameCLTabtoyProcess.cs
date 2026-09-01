using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace TryGame.RefDataTools.Editor
{
    internal sealed class TryGameCLTabtoyProcess
    {
        private const int ExportTimeoutMilliseconds = 300000;
        private readonly string exePath;
        private readonly string outputPath;
        private readonly string csharpOutputPath;
        private readonly string luaOutputPath;

        public TryGameCLTabtoyProcess(string outputAssetPath, string csharpOutputAssetPath, string luaOutputAssetPath)
        {
            exePath = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.ToolBinAssetPath + "/cltabtoy.exe");
            outputPath = TryGameRefDataPaths.ToFullPath(outputAssetPath);
            csharpOutputPath = TryGameRefDataPaths.ToFullPath(csharpOutputAssetPath);
            luaOutputPath = TryGameRefDataPaths.ToFullPath(luaOutputAssetPath);
        }

        public bool Export(IReadOnlyList<string> excelFullPaths)
        {
            if (!File.Exists(exePath))
            {
                UnityEngine.Debug.LogError("cltabtoy.exe 不存在：" + exePath);
                return false;
            }

            if (excelFullPaths == null || excelFullPaths.Count == 0)
            {
                UnityEngine.Debug.LogWarning("没有选中的 Excel 配表。");
                return false;
            }

            // 共用枚举/结构体表只作为 cltabtoy 的隐式依赖，不能再次作为普通输入传入。
            // 否则 cltabtoy 会在每个源表处理时重复注册 StructCommonResource 等定义。
            List<string> normalizedExcelPaths = new List<string>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < excelFullPaths.Count; i++)
            {
                string excelPath = excelFullPaths[i];
                if (string.IsNullOrWhiteSpace(excelPath))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(excelPath);
                string fileName = Path.GetFileName(fullPath);
                if (fileName.Equals(
                        TryGameRefDataPaths.CommonDefineExcelName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Debug.LogWarning(
                        "跳过作为 cltabtoy 直接输入的共用枚举结构体表；该表只作为隐式依赖：" +
                        fullPath);
                    continue;
                }

                if (seenPaths.Add(fullPath))
                {
                    normalizedExcelPaths.Add(fullPath);
                }
            }

            if (normalizedExcelPaths.Count == 0)
            {
                UnityEngine.Debug.LogWarning("过滤共用定义和重复路径后没有可导出的 Excel 配表。");
                return false;
            }

            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(csharpOutputPath);
            Directory.CreateDirectory(luaOutputPath);

            for (int i = 0; i < normalizedExcelPaths.Count; i++)
            {
                string excelPath = normalizedExcelPaths[i];
                if (!File.Exists(excelPath))
                {
                    UnityEngine.Debug.LogError("Excel 文件不存在：" + excelPath);
                    return false;
                }
            }

            string compatibilityDirectory = null;
            try
            {
                IReadOnlyList<string> exportExcelPaths =
                    TryGameCLTabtoyExcelCompatibility.CreateExportCopies(
                        normalizedExcelPaths,
                        out compatibilityDirectory);

                StringBuilder args = new StringBuilder();
                args.Append("-o ").Append(Quote(outputPath)).Append(' ');
                args.Append("-luaoutput ").Append(Quote(luaOutputPath)).Append(' ');
                args.Append("-csharpoutput ").Append(Quote(csharpOutputPath)).Append(' ');

                for (int i = 0; i < exportExcelPaths.Count; i++)
                {
                    args.Append(Quote(exportExcelPaths[i])).Append(' ');
                }

                args.Append("lua");
                return RunProcess(args.ToString());
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("创建 cltabtoy Excel 兼容副本失败：" + e);
                return false;
            }
            finally
            {
                TryGameCLTabtoyExcelCompatibility.Cleanup(compatibilityDirectory);
            }
        }

        private bool RunProcess(string arguments)
        {
            UnityEngine.Debug.Log("开始导出配表：" + arguments);

            using (Process process = new Process())
            {
                process.StartInfo.FileName = exePath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                process.StartInfo.CreateNoWindow = false;
                process.StartInfo.UseShellExecute = true;

                try
                {
                    process.Start();
                    if (!process.WaitForExit(ExportTimeoutMilliseconds))
                    {
                        UnityEngine.Debug.LogError($"cltabtoy 导出超时，已终止进程：timeoutMs={ExportTimeoutMilliseconds}, arguments={arguments}");
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception killException)
                        {
                            UnityEngine.Debug.LogError("cltabtoy 超时后终止进程也失败：" + killException);
                        }

                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        if (process.ExitCode == -1073741510)
                        {
                            UnityEngine.Debug.LogError("cltabtoy 导出进程被中断。通常是导出结束后直接关闭了控制台窗口，请在控制台里按任意键退出。");
                            return false;
                        }

                        UnityEngine.Debug.LogError("cltabtoy 导出失败，ExitCode = " + process.ExitCode);
                        return false;
                    }

                    UnityEngine.Debug.Log("配表导出完成。");
                    return true;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("cltabtoy 启动失败：" + e);
                    return false;
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    /// <summary>
    /// Excel 的工作表标签最多只能有 31 个字符，而 cltabtoy 0.3.0.0 又直接把标签当作
    /// 逻辑表名。正式源表使用可由 Excel 正常打开的短标签；仅在导出临时副本中恢复
    /// 完整逻辑名，避免生成类名和资源名发生变化。
    /// </summary>
    internal static class TryGameCLTabtoyExcelCompatibility
    {
        private const string WorkbookEntryPath = "xl/workbook.xml";
        private const string CompatibilityRootName = "TryGameCLTabtoyExcelCompatibility";

        private static readonly IReadOnlyDictionary<string, string> LogicalSheetNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "BattleStageDifficultyPointEffec", "BattleStageDifficultyPointEffect" },
                { "BattleDifficultyRestrictionAffi", "BattleDifficultyRestrictionAffix" },
                { "BattleLootSpawnChanceModifierEf", "BattleLootSpawnChanceModifierEffect" },
                { "BattleEnemyGroupSpawnChanceModi", "BattleEnemyGroupSpawnChanceModifierEffect" },
                { "BattleSmallAreaExtractionSpawnP", "BattleSmallAreaExtractionSpawnPoint" },
                { "BattleProjectileBallisticMoveme", "BattleProjectileBallisticMovement" },
                { "RobotLowHealthDamageReductionEf", "RobotLowHealthDamageReductionEffect" }
            };

        public static IReadOnlyList<string> CreateExportCopies(
            IReadOnlyList<string> sourceExcelPaths,
            out string compatibilityDirectory)
        {
            if (sourceExcelPaths == null || sourceExcelPaths.Count == 0)
            {
                throw new ArgumentException("没有可复制的 Excel 配表。", nameof(sourceExcelPaths));
            }

            string compatibilityRoot = Path.Combine(
                Path.GetTempPath(),
                CompatibilityRootName);
            compatibilityDirectory = Path.Combine(
                compatibilityRoot,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(compatibilityDirectory);

            List<string> copiedPaths = new List<string>(sourceExcelPaths.Count);
            HashSet<string> copiedFileNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sourceExcelPaths.Count; i++)
            {
                string sourcePath = sourceExcelPaths[i];
                string fileName = Path.GetFileName(sourcePath);
                if (!copiedFileNames.Add(fileName))
                {
                    throw new InvalidOperationException(
                        "cltabtoy 导出输入存在同名 Excel，无法建立兼容副本：" + fileName);
                }

                string copiedPath = Path.Combine(compatibilityDirectory, fileName);
                File.Copy(sourcePath, copiedPath, true);
                RestoreLogicalSheetNames(copiedPath);
                copiedPaths.Add(copiedPath);
            }

            string commonSourcePath = Path.Combine(
                Path.GetDirectoryName(sourceExcelPaths[0]) ?? string.Empty,
                TryGameRefDataPaths.CommonDefineExcelName);
            if (!File.Exists(commonSourcePath))
            {
                throw new FileNotFoundException(
                    "cltabtoy 的共用枚举结构体表不存在。",
                    commonSourcePath);
            }

            string commonFileName = Path.GetFileName(commonSourcePath);
            if (copiedFileNames.Add(commonFileName))
            {
                string commonCopiedPath = Path.Combine(
                    compatibilityDirectory,
                    commonFileName);
                File.Copy(commonSourcePath, commonCopiedPath, true);
                RestoreLogicalSheetNames(commonCopiedPath);
            }

            return copiedPaths;
        }

        public static void Cleanup(string compatibilityDirectory)
        {
            if (string.IsNullOrWhiteSpace(compatibilityDirectory) ||
                !Directory.Exists(compatibilityDirectory))
            {
                return;
            }

            string expectedRoot = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                CompatibilityRootName));
            string target = Path.GetFullPath(compatibilityDirectory);
            string expectedPrefix = expectedRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Debug.LogError(
                    "拒绝清理不在 cltabtoy 兼容临时目录中的路径：" + target);
                return;
            }

            try
            {
                Directory.Delete(target, true);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(
                    "清理 cltabtoy Excel 兼容副本失败，可稍后手动删除：" +
                    target + "\n" + e.Message);
            }
        }

        internal static void RestoreLogicalSheetNames(string excelPath)
        {
            using (FileStream stream = new FileStream(
                       excelPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            using (ZipArchive archive = new ZipArchive(
                       stream,
                       ZipArchiveMode.Update,
                       false))
            {
                ZipArchiveEntry workbookEntry = archive.GetEntry(WorkbookEntryPath);
                if (workbookEntry == null)
                {
                    throw new InvalidDataException(
                        "Excel 缺少 " + WorkbookEntryPath + "：" + excelPath);
                }

                XDocument workbook;
                using (Stream entryStream = workbookEntry.Open())
                {
                    workbook = XDocument.Load(
                        entryStream,
                        LoadOptions.PreserveWhitespace);
                }

                XNamespace spreadsheetNamespace =
                    "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                bool changed = false;
                foreach (XElement sheet in workbook.Descendants(
                             spreadsheetNamespace + "sheet"))
                {
                    XAttribute nameAttribute = sheet.Attribute("name");
                    if (nameAttribute == null ||
                        !LogicalSheetNames.TryGetValue(
                            nameAttribute.Value,
                            out string logicalName))
                    {
                        continue;
                    }

                    nameAttribute.Value = logicalName;
                    changed = true;
                }

                if (!changed)
                {
                    return;
                }

                workbookEntry.Delete();
                ZipArchiveEntry replacement = archive.CreateEntry(
                    WorkbookEntryPath,
                    CompressionLevel.Optimal);
                using (Stream replacementStream = replacement.Open())
                {
                    workbook.Save(
                        replacementStream,
                        SaveOptions.DisableFormatting);
                }
            }
        }
    }
}
