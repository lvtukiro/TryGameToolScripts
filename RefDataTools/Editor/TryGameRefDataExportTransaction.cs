using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace TryGame.RefDataTools.Editor
{
    internal enum TryGameRefDataExportMode
    {
        Incremental = 0,
        FullCleanRebuild = 1,
    }

    /// <summary>
    /// RefData 三仓库总事务：所有导出先进入 Temp staging，验证通过后才替换正式目录。
    /// 目录发布不是操作系统级跨仓库原子操作，因此使用完整目录备份和反向回滚保证最终一致性。
    /// </summary>
    internal static class TryGameRefDataExportTransaction
    {
        private const string TransactionFolderName = "TryGameRefDataTransactions";

        public static bool Execute(
            IReadOnlyList<string> excelFullPaths,
            TryGameRefDataExportMode exportMode)
        {
            if (!TryValidateInputs(excelFullPaths, exportMode))
            {
                return false;
            }

            bool cleanRebuild = exportMode == TryGameRefDataExportMode.FullCleanRebuild;
            if (cleanRebuild)
            {
                Debug.Log(
                    $"[TryGameRefDataExportTransaction] 启动全量清洁重建：" +
                    $"inputs={excelFullPaths.Count}, staging=empty, generateConfig=always");
            }
            else
            {
                Debug.LogWarning(
                    "[TryGameRefDataExportTransaction] 当前为增量导出：不会清理已删除或已改名表的旧产物；" +
                    "涉及删除/改名时请使用菜单 TryGame/RefData/导出全部配表并生成入口。");
            }

            if (!TryResolveRepositories(out string sourceRepository, out string runtimeRepository, out string generatedRepository))
            {
                return false;
            }

            SplitExportFiles(excelFullPaths, out List<string> cltabtoyFiles, out List<string> languageFiles);
            if (!TryBuildValidationInputFiles(
                excelFullPaths,
                cltabtoyFiles,
                out List<string> validationInputFiles,
                out HashSet<string> implicitDependencyFiles))
            {
                return false;
            }

            if (!TryGameRefDataExportValidator.TryCaptureInputHashes(
                validationInputFiles,
                out Dictionary<string, string> expectedInputHashes))
            {
                return false;
            }

            string transactionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N");
            string transactionParent = Path.Combine(TryGameRefDataPaths.ProjectRoot, "Temp", TransactionFolderName);
            string transactionRoot = Path.Combine(transactionParent, transactionId);
            string stagingRoot = Path.Combine(transactionRoot, "staging");
            string stagedSourceOutput = Path.Combine(stagingRoot, "SourceOutput");
            string stagedRuntimeOutput = Path.Combine(stagingRoot, "RuntimeOutput");
            string stagedGeneratedTables = Path.Combine(stagingRoot, "GeneratedTables");
            string stagedGeneratedConfig = Path.Combine(stagingRoot, "GeneratedConfig");
            string stagedLuaOutput = Path.Combine(stagingRoot, "LuaOutput");

            string sourceOutput = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.DefaultOutputAssetPath);
            string runtimeOutput = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.RuntimeOutputAssetPath);
            string generatedTables = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.DefaultGeneratedTableAssetPath);
            string generatedConfig = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.DefaultGeneratedConfigAssetPath);

            try
            {
                Directory.CreateDirectory(stagingRoot);
                if (cleanRebuild)
                {
                    Directory.CreateDirectory(stagedSourceOutput);
                    Directory.CreateDirectory(stagedRuntimeOutput);
                    Directory.CreateDirectory(stagedGeneratedTables);
                    Directory.CreateDirectory(stagedGeneratedConfig);
                }
                else
                {
                    CopyDirectorySnapshot(sourceOutput, stagedSourceOutput);
                    CopyDirectorySnapshot(runtimeOutput, stagedRuntimeOutput);
                    CopyDirectorySnapshot(generatedTables, stagedGeneratedTables);
                    CopyDirectorySnapshot(generatedConfig, stagedGeneratedConfig);
                }

                Directory.CreateDirectory(stagedLuaOutput);
                DeleteRuntimeArtifactsFromSourceSnapshot(stagedSourceOutput);

                if (cltabtoyFiles.Count > 0)
                {
                    TryGameCLTabtoyProcess process = new TryGameCLTabtoyProcess(
                        stagedSourceOutput,
                        stagedGeneratedTables,
                        stagedLuaOutput);
                    if (!process.Export(cltabtoyFiles))
                    {
                        return FailBeforePublish(transactionRoot, "cltabtoy staging 导出失败，正式目录保持不变。", null);
                    }

                    string stagedFbData = Path.Combine(stagedSourceOutput, "fb_data");
                    if (!Directory.Exists(stagedFbData) || Directory.GetFiles(stagedFbData, "*.bytes", SearchOption.TopDirectoryOnly).Length == 0)
                    {
                        return FailBeforePublish(transactionRoot, "cltabtoy 返回成功但 staging 没有任何 bytes，拒绝发布。", null);
                    }
                }

                if (languageFiles.Count > 0 && !TryGameLanguageExcelExport.Export(languageFiles, stagedSourceOutput))
                {
                    return FailBeforePublish(transactionRoot, "Language staging 导出失败，正式目录保持不变。", null);
                }

                int normalizedSourceFiles = TryGameRefDataTextNormalizer.NormalizeDirectoryAgainstBaseline(
                    stagedSourceOutput,
                    sourceOutput,
                    ".json",
                    ".fbs",
                    ".java",
                    ".txt");
                string stagedSourceLanguage = Path.Combine(stagedSourceOutput, "txt_data", "Language.bytes");
                if (TryGameRefDataTextNormalizer.NormalizeFile(stagedSourceLanguage))
                {
                    normalizedSourceFiles++;
                }

                OverlayRuntimeArtifacts(stagedSourceOutput, stagedRuntimeOutput);
                DeleteRuntimeArtifactsFromSourceSnapshot(stagedSourceOutput);

                int normalizedRuntimeFiles = TryGameRefDataTextNormalizer.NormalizeFileAgainstBaseline(
                    Path.Combine(stagedRuntimeOutput, "txt_data", "Language.bytes"),
                    Path.Combine(runtimeOutput, "txt_data", "Language.bytes")) ? 1 : 0;

                TryGameConfigGenerator.Generate(stagedGeneratedTables, stagedGeneratedConfig);

                int normalizedGeneratedFiles =
                    TryGameRefDataTextNormalizer.NormalizeCodeDirectoryAgainstBaseline(
                        stagedGeneratedTables,
                        generatedTables) +
                    TryGameRefDataTextNormalizer.NormalizeCodeDirectoryAgainstBaseline(
                        stagedGeneratedConfig,
                        generatedConfig);
                Debug.Log(
                    "[TryGameRefDataExportTransaction] staging 生成文本已规范为 LF，并保留既有 UTF-8 BOM 约定：" +
                    $"source={normalizedSourceFiles}, runtime={normalizedRuntimeFiles}, generated={normalizedGeneratedFiles}");

                if (!TryGameRefDataExportValidator.ValidateAndWriteManifest(
                    transactionRoot,
                    validationInputFiles,
                    implicitDependencyFiles,
                    expectedInputHashes,
                    stagedSourceOutput,
                    stagedRuntimeOutput,
                    stagedGeneratedTables,
                    stagedGeneratedConfig,
                    out string manifestJson))
                {
                    return FailBeforePublish(transactionRoot, "staging 完整性验证失败，正式目录保持不变。", null);
                }

                if (cleanRebuild && !TryValidateFullCleanRebuildInputs(
                    new HashSet<string>(excelFullPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase),
                    "before-publish"))
                {
                    return FailBeforePublish(
                        transactionRoot,
                        "规范源表集合在全量导出期间发生变化，拒绝发布可能漏表的清洁 staging。",
                        null);
                }

                WriteManifestCopies(
                    manifestJson,
                    stagedSourceOutput,
                    stagedRuntimeOutput,
                    stagedGeneratedTables);
                if (cleanRebuild)
                {
                    int preservedMetaCount =
                        PreserveMatchingMetaFiles(sourceOutput, stagedSourceOutput) +
                        PreserveMatchingMetaFiles(runtimeOutput, stagedRuntimeOutput) +
                        PreserveMatchingMetaFiles(generatedTables, stagedGeneratedTables) +
                        PreserveMatchingMetaFiles(generatedConfig, stagedGeneratedConfig);
                    Debug.Log(
                        $"[TryGameRefDataExportTransaction] 全量清洁重建已恢复仍存活资源的旧 meta：" +
                        $"count={preservedMetaCount}");
                }

                if (!TryGameRefDataExportValidator.ValidateManifestAndPayloadCopies(
                    "staging 发布前",
                    manifestJson,
                    stagedSourceOutput,
                    stagedRuntimeOutput,
                    stagedGeneratedTables,
                    stagedGeneratedConfig))
                {
                    return FailBeforePublish(transactionRoot, "staging manifest 或完整产物门禁失败，拒绝发布。", null);
                }

                if (cleanRebuild)
                {
                    PrintCleanRebuildRemovalPlan(
                        sourceOutput,
                        stagedSourceOutput,
                        runtimeOutput,
                        stagedRuntimeOutput,
                        generatedTables,
                        stagedGeneratedTables,
                        generatedConfig,
                        stagedGeneratedConfig);
                }

                if (!TryGameRefDataExportValidator.ValidateInputHashesUnchanged(
                    "正式发布前",
                    validationInputFiles,
                    expectedInputHashes))
                {
                    return FailBeforePublish(
                        transactionRoot,
                        "Excel 输入在 staging 验证后发生变化，拒绝发布旧产物。",
                        null);
                }

                DirectoryPublishTransaction publisher = new DirectoryPublishTransaction(transactionRoot);
                publisher.Add("源表仓库 Output", stagedSourceOutput, sourceOutput);
                publisher.Add("运行时 bytes 仓库 Output", stagedRuntimeOutput, runtimeOutput);
                publisher.Add("生成代码仓库 GeneratedTables", stagedGeneratedTables, generatedTables);
                publisher.Add("生成代码仓库 GeneratedConfig", stagedGeneratedConfig, generatedConfig);

                if (!publisher.Commit(() => TryGameRefDataExportValidator.ValidateManifestAndPayloadCopies(
                    "正式目录发布后",
                    manifestJson,
                    sourceOutput,
                    runtimeOutput,
                    generatedTables,
                    generatedConfig)))
                {
                    Debug.LogError(
                        $"[TryGameRefDataExportTransaction] 正式目录发布失败；已执行反向回滚。" +
                        $" rollbackSucceeded={publisher.RollbackSucceeded}, transaction={transactionRoot}");
                    return false;
                }
            }
            catch (Exception exception)
            {
                return FailBeforePublish(transactionRoot, "RefData staging 或验证流程发生异常，正式目录保持不变。", exception);
            }

            // Commit 返回 true 后四个正式目录已经全部切换。下面均属于发布后维护，任何失败都必须记录，
            // 但不能再把已经成功的发布伪装成“正式目录未改变”，也不能在这里触发回滚。
            TryRefreshAssetDatabase(transactionRoot);
            Debug.Log(
                $"[TryGameRefDataExportTransaction] 三仓库事务导表完成：transaction={transactionId}, " +
                $"source={sourceRepository}, runtime={runtimeRepository}, generated={generatedRepository}");
            PrintRepositoryDiffs(sourceRepository, runtimeRepository, generatedRepository);
            TryDeleteTransactionDirectory(transactionRoot, transactionParent, "成功事务目录");
            return true;
        }

        private static void TryRefreshAssetDatabase(string transactionRoot)
        {
            try
            {
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[TryGameRefDataExportTransaction] 正式目录已经发布成功，但 AssetDatabase.Refresh 失败。" +
                    $"请在 Unity 中手动刷新资源；transaction={transactionRoot}\n{exception}");
            }
        }

        private static bool TryValidateInputs(
            IReadOnlyList<string> excelFullPaths,
            TryGameRefDataExportMode exportMode)
        {
            if (exportMode != TryGameRefDataExportMode.Incremental &&
                exportMode != TryGameRefDataExportMode.FullCleanRebuild)
            {
                Debug.LogError(
                    $"[TryGameRefDataExportTransaction] 未知导出模式，事务未启动：mode={exportMode}");
                return false;
            }

            if (excelFullPaths == null || excelFullPaths.Count == 0)
            {
                Debug.LogError("[TryGameRefDataExportTransaction] 没有传入任何 Excel，事务未启动。");
                return false;
            }

            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < excelFullPaths.Count; i++)
            {
                string path = excelFullPaths[i];
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    Debug.LogError($"[TryGameRefDataExportTransaction] Excel 不存在：index={i}, path={path ?? "<null>"}");
                    return false;
                }

                string extension = Path.GetExtension(path);
                if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogError($"[TryGameRefDataExportTransaction] Excel 扩展名不受支持：index={i}, path={path}");
                    return false;
                }

                string fullPath = Path.GetFullPath(path);
                if (!seenPaths.Add(fullPath))
                {
                    Debug.LogError($"[TryGameRefDataExportTransaction] 本次导出包含重复 Excel：index={i}, path={fullPath}");
                    return false;
                }

                if (Path.GetFileName(path).StartsWith("~$", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogError($"[TryGameRefDataExportTransaction] 拒绝导出 Excel 临时锁文件：{path}");
                    return false;
                }

                if (Path.GetFileName(path).Equals(
                    TryGameRefDataPaths.CommonDefineExcelName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogError(
                        "[TryGameRefDataExportTransaction] 共用枚举结构体只能作为 cltabtoy 隐式依赖，" +
                        $"不能作为普通业务表显式导出：{fullPath}");
                    return false;
                }
            }

            if (exportMode == TryGameRefDataExportMode.FullCleanRebuild &&
                !TryValidateFullCleanRebuildInputs(seenPaths, "before-export"))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 清洁重建会把未生成文件视为已删除，因此只允许规范源目录的最新完整输入集合。
        /// </summary>
        private static bool TryValidateFullCleanRebuildInputs(
            HashSet<string> suppliedPaths,
            string phase)
        {
            string canonicalRoot = Path.GetFullPath(
                TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.DefaultExcelRootAssetPath));
            List<string> expectedFiles = TryGameRefDataPaths.FindExportableExcelFiles(canonicalRoot);
            HashSet<string> expectedPaths = new HashSet<string>(
                expectedFiles.Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);

            if (expectedPaths.Count == 0)
            {
                Debug.LogError(
                    $"[TryGameRefDataExportTransaction] 规范源目录没有可导出的 Excel，拒绝全量清洁重建：" +
                    $"phase={phase}, root={canonicalRoot}");
                return false;
            }

            if (!expectedPaths.SetEquals(suppliedPaths))
            {
                string missing = string.Join(", ", expectedPaths
                    .Except(suppliedPaths, StringComparer.OrdinalIgnoreCase)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                string unexpected = string.Join(", ", suppliedPaths
                    .Except(expectedPaths, StringComparer.OrdinalIgnoreCase)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                Debug.LogError(
                    $"[TryGameRefDataExportTransaction] 全量清洁重建输入不是规范源目录的最新完整集合，" +
                    $"拒绝把未传入表误判为删除：phase={phase}, root={canonicalRoot}, " +
                    $"expected={expectedPaths.Count}, actual={suppliedPaths.Count}, " +
                    $"missing=[{missing}], unexpected=[{unexpected}]");
                return false;
            }

            string commonDefinePath = Path.Combine(canonicalRoot, TryGameRefDataPaths.CommonDefineExcelName);
            if (!File.Exists(commonDefinePath))
            {
                Debug.LogError(
                    $"[TryGameRefDataExportTransaction] 全量清洁重建缺少共用枚举结构体源表，事务未启动：" +
                    $"phase={phase}, path={commonDefinePath}");
                return false;
            }

            Debug.Log(
                $"[TryGameRefDataExportTransaction] 全量清洁重建输入集合校验通过：" +
                $"phase={phase}, root={canonicalRoot}, inputs={expectedPaths.Count}, " +
                $"commonDefine={commonDefinePath}");
            return true;
        }

        private static bool TryResolveRepositories(
            out string sourceRepository,
            out string runtimeRepository,
            out string generatedRepository)
        {
            sourceRepository = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.SourceRepositoryAssetPath);
            runtimeRepository = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.RuntimeRepositoryAssetPath);
            generatedRepository = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.GeneratedRepositoryAssetPath);

            bool valid = ValidateGitRepository("源表", sourceRepository);
            valid &= ValidateGitRepository("运行时 bytes", runtimeRepository);
            valid &= ValidateGitRepository("生成代码", generatedRepository);
            return valid;
        }

        private static bool ValidateGitRepository(string label, string repository)
        {
            string gitMarker = Path.Combine(repository, ".git");
            if (Directory.Exists(repository) && (Directory.Exists(gitMarker) || File.Exists(gitMarker)))
            {
                return true;
            }

            Debug.LogError(
                $"[TryGameRefDataExportTransaction] {label}仓库不存在或未初始化，拒绝启动三仓库事务：" +
                $"repository={repository}, gitMarker={gitMarker}");
            return false;
        }

        private static void SplitExportFiles(
            IReadOnlyList<string> excelFullPaths,
            out List<string> cltabtoyFiles,
            out List<string> languageFiles)
        {
            cltabtoyFiles = new List<string>();
            languageFiles = new List<string>();
            for (int i = 0; i < excelFullPaths.Count; i++)
            {
                string path = excelFullPaths[i];
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.IndexOf("Language", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("语言表", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    languageFiles.Add(path);
                }
                else
                {
                    cltabtoyFiles.Add(path);
                }
            }
        }

        private static bool TryBuildValidationInputFiles(
            IReadOnlyList<string> explicitInputFiles,
            IReadOnlyList<string> cltabtoyFiles,
            out List<string> validationInputFiles,
            out HashSet<string> implicitDependencyFiles)
        {
            validationInputFiles = new List<string>();
            implicitDependencyFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < explicitInputFiles.Count; i++)
            {
                string fullPath = Path.GetFullPath(explicitInputFiles[i]);
                if (seenPaths.Add(fullPath))
                {
                    validationInputFiles.Add(fullPath);
                }
            }

            for (int i = 0; i < cltabtoyFiles.Count; i++)
            {
                string tablePath = Path.GetFullPath(cltabtoyFiles[i]);
                string tableDirectory = Path.GetDirectoryName(tablePath);
                string commonDefinePath = Path.GetFullPath(
                    Path.Combine(tableDirectory, TryGameRefDataPaths.CommonDefineExcelName));
                if (!File.Exists(commonDefinePath))
                {
                    Debug.LogError(
                        "[TryGameRefDataExportTransaction] cltabtoy 普通表缺少同目录的共用枚举结构体依赖，" +
                        $"事务未启动：table={tablePath}, dependency={commonDefinePath}");
                    validationInputFiles.Clear();
                    implicitDependencyFiles.Clear();
                    return false;
                }

                if (seenPaths.Add(commonDefinePath))
                {
                    validationInputFiles.Add(commonDefinePath);
                    implicitDependencyFiles.Add(commonDefinePath);
                }
            }

            Debug.Log(
                "[TryGameRefDataExportTransaction] 已建立导表有效输入集合：" +
                $"explicit={explicitInputFiles.Count}, implicitDependencies={implicitDependencyFiles.Count}, " +
                $"total={validationInputFiles.Count}");
            return true;
        }

        private static void CopyDirectorySnapshot(string source, string destination)
        {
            if (Directory.Exists(destination))
            {
                throw new IOException("staging 目标已存在，拒绝覆盖：" + destination);
            }

            Directory.CreateDirectory(destination);
            if (!Directory.Exists(source))
            {
                return;
            }

            string sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] directories = Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories);
            for (int i = 0; i < directories.Length; i++)
            {
                string relative = MakeRelativePath(sourceRoot, directories[i]);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }

            string[] files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string relative = MakeRelativePath(sourceRoot, files[i]);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(files[i], target, false);
            }
        }

        /// <summary>
        /// 清洁重建从空 staging 生成；这里只给仍存在的同路径文件或目录补回旧 meta。
        /// 删除/改名对象的 meta 不会被带回，新对象由 Unity 刷新时生成新 GUID。
        /// </summary>
        private static int PreserveMatchingMetaFiles(string baselineRoot, string stagedRoot)
        {
            if (!Directory.Exists(baselineRoot) || !Directory.Exists(stagedRoot))
            {
                return 0;
            }

            string[] metaFiles = Directory.GetFiles(baselineRoot, "*.meta", SearchOption.AllDirectories);
            Array.Sort(metaFiles, StringComparer.OrdinalIgnoreCase);
            int copiedCount = 0;
            for (int i = 0; i < metaFiles.Length; i++)
            {
                string relativeMetaPath = MakeRelativePath(baselineRoot, metaFiles[i]);
                string relativeAssetPath = relativeMetaPath.Substring(
                    0,
                    relativeMetaPath.Length - ".meta".Length);
                string stagedAssetPath = Path.Combine(stagedRoot, relativeAssetPath);
                if (!File.Exists(stagedAssetPath) && !Directory.Exists(stagedAssetPath))
                {
                    continue;
                }

                string stagedMetaPath = stagedAssetPath + ".meta";
                Directory.CreateDirectory(Path.GetDirectoryName(stagedMetaPath));
                File.Copy(metaFiles[i], stagedMetaPath, true);
                copiedCount++;
            }

            return copiedCount;
        }

        /// <summary>
        /// 发布前打印正式目录中存在、但清洁 staging 已不再生成的文件。
        /// 这里只报告计划，真正删除仍由后续目录发布事务统一完成并受回滚保护。
        /// </summary>
        private static void PrintCleanRebuildRemovalPlan(
            string sourceBaseline,
            string stagedSource,
            string runtimeBaseline,
            string stagedRuntime,
            string tablesBaseline,
            string stagedTables,
            string configBaseline,
            string stagedConfig)
        {
            StringBuilder details = new StringBuilder();
            int removedArtifactCount = 0;
            int removedMetaCount = 0;
            AppendRemovalPlanSection(
                details,
                "SourceOutput",
                sourceBaseline,
                stagedSource,
                ref removedArtifactCount,
                ref removedMetaCount);
            AppendRemovalPlanSection(
                details,
                "RuntimeOutput",
                runtimeBaseline,
                stagedRuntime,
                ref removedArtifactCount,
                ref removedMetaCount);
            AppendRemovalPlanSection(
                details,
                "GeneratedTables",
                tablesBaseline,
                stagedTables,
                ref removedArtifactCount,
                ref removedMetaCount);
            AppendRemovalPlanSection(
                details,
                "GeneratedConfig",
                configBaseline,
                stagedConfig,
                ref removedArtifactCount,
                ref removedMetaCount);

            if (removedArtifactCount == 0 && removedMetaCount == 0)
            {
                Debug.Log(
                    "[TryGameRefDataExportTransaction] 全量清洁重建删除计划：<none>；" +
                    "尚未发布，正式目录当前未发生变化。");
                return;
            }

            Debug.LogWarning(
                $"[TryGameRefDataExportTransaction] 全量清洁重建删除计划（尚未发布）：" +
                $"artifacts={removedArtifactCount}, metas={removedMetaCount}\n" +
                details.ToString().TrimEnd());
        }

        private static void AppendRemovalPlanSection(
            StringBuilder output,
            string label,
            string baselineRoot,
            string stagedRoot,
            ref int totalArtifactCount,
            ref int totalMetaCount)
        {
            if (!Directory.Exists(baselineRoot))
            {
                return;
            }

            string[] baselineFiles = Directory.GetFiles(baselineRoot, "*", SearchOption.AllDirectories);
            Array.Sort(baselineFiles, StringComparer.OrdinalIgnoreCase);
            StringBuilder section = new StringBuilder();
            int sectionArtifactCount = 0;
            int sectionMetaCount = 0;
            for (int i = 0; i < baselineFiles.Length; i++)
            {
                string relativePath = MakeRelativePath(baselineRoot, baselineFiles[i]);
                string stagedPath = Path.Combine(stagedRoot, relativePath);
                if (File.Exists(stagedPath))
                {
                    continue;
                }

                if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    sectionMetaCount++;
                    continue;
                }

                sectionArtifactCount++;
                section.Append("- ").AppendLine(relativePath.Replace('\\', '/'));
            }

            if (sectionArtifactCount == 0 && sectionMetaCount == 0)
            {
                return;
            }

            output.Append('[').Append(label).AppendLine("]");
            output.Append(section);
            if (sectionMetaCount > 0)
            {
                output.Append("- <paired-or-orphan-meta> count=").AppendLine(sectionMetaCount.ToString());
            }

            totalArtifactCount += sectionArtifactCount;
            totalMetaCount += sectionMetaCount;
        }

        private static void OverlayRuntimeArtifacts(string stagedSourceOutput, string stagedRuntimeOutput)
        {
            string sourceFbData = Path.Combine(stagedSourceOutput, "fb_data");
            string runtimeFbData = Path.Combine(stagedRuntimeOutput, "fb_data");
            if (Directory.Exists(sourceFbData))
            {
                Directory.CreateDirectory(runtimeFbData);
                string[] bytes = Directory.GetFiles(sourceFbData, "*.bytes", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < bytes.Length; i++)
                {
                    CopyFileAndOptionalMeta(bytes[i], Path.Combine(runtimeFbData, Path.GetFileName(bytes[i])));
                }
            }

            string sourceLanguage = Path.Combine(stagedSourceOutput, "txt_data", "Language.bytes");
            if (File.Exists(sourceLanguage))
            {
                string runtimeLanguage = Path.Combine(stagedRuntimeOutput, "txt_data", "Language.bytes");
                Directory.CreateDirectory(Path.GetDirectoryName(runtimeLanguage));
                CopyFileAndOptionalMeta(sourceLanguage, runtimeLanguage);
            }
        }

        private static void CopyFileAndOptionalMeta(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
            string sourceMeta = source + ".meta";
            string destinationMeta = destination + ".meta";
            if (File.Exists(sourceMeta) && !File.Exists(destinationMeta))
            {
                File.Copy(sourceMeta, destinationMeta, false);
            }
        }

        private static void DeleteRuntimeArtifactsFromSourceSnapshot(string sourceOutput)
        {
            string fbData = Path.Combine(sourceOutput, "fb_data");
            if (Directory.Exists(fbData))
            {
                string[] bytes = Directory.GetFiles(fbData, "*.bytes", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < bytes.Length; i++)
                {
                    File.Delete(bytes[i]);
                    DeleteIfExists(bytes[i] + ".meta");
                }

                string[] metas = Directory.GetFiles(fbData, "*.bytes.meta", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < metas.Length; i++)
                {
                    File.Delete(metas[i]);
                }
            }

            string language = Path.Combine(sourceOutput, "txt_data", "Language.bytes");
            DeleteIfExists(language);
            DeleteIfExists(language + ".meta");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void WriteManifestCopies(
            string manifestJson,
            string stagedSourceOutput,
            string stagedRuntimeOutput,
            string stagedGeneratedTables)
        {
            UTF8Encoding utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(stagedSourceOutput, TryGameRefDataPaths.ManifestFileName), manifestJson, utf8);
            File.WriteAllText(Path.Combine(stagedRuntimeOutput, TryGameRefDataPaths.ManifestFileName), manifestJson, utf8);
            File.WriteAllText(Path.Combine(stagedGeneratedTables, TryGameRefDataPaths.ManifestFileName), manifestJson, utf8);
        }

        private static bool FailBeforePublish(string transactionRoot, string message, Exception exception)
        {
            if (exception == null)
            {
                Debug.LogError($"[TryGameRefDataExportTransaction] {message} staging 已保留供检查：{transactionRoot}");
            }
            else
            {
                Debug.LogError($"[TryGameRefDataExportTransaction] {message} staging 已保留供检查：{transactionRoot}\n{exception}");
            }

            return false;
        }

        private static void PrintRepositoryDiffs(params string[] repositories)
        {
            for (int i = 0; i < repositories.Length; i++)
            {
                string repository = repositories[i];
                try
                {
                    string unstaged = RunGitForDiff(repository, "diff --name-status --no-ext-diff");
                    string staged = RunGitForDiff(repository, "diff --cached --name-status --no-ext-diff");
                    string untracked = RunGitForDiff(repository, "ls-files --others --exclude-standard");
                    StringBuilder output = new StringBuilder();
                    AppendDiffSection(output, "unstaged", unstaged, string.Empty);
                    AppendDiffSection(output, "staged", staged, string.Empty);
                    AppendDiffSection(output, "untracked", untracked, "??\t");
                    Debug.Log(
                        $"[TryGameRefDataExportTransaction] 仓库真实差异：repository={repository}\n" +
                        (output.Length == 0 ? "<clean>" : output.ToString().TrimEnd()));
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[TryGameRefDataExportTransaction] 获取仓库差异异常：repository={repository}\n{exception}");
                }
            }
        }

        private static string RunGitForDiff(string repository, string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "-C " + Quote(repository) + " " + arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"git 差异命令失败：repository={repository}, arguments={arguments}, " +
                        $"exitCode={process.ExitCode}\n{error}");
                }

                return output.TrimEnd();
            }
        }

        private static void AppendDiffSection(
            StringBuilder output,
            string label,
            string content,
            string linePrefix)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            output.Append('[').Append(label).AppendLine("]");
            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    output.Append(linePrefix).AppendLine(lines[i]);
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string MakeRelativePath(string root, string path)
        {
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Uri rootUri = new Uri(normalizedRoot);
            Uri pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static void TryDeleteTransactionDirectory(string path, string allowedParent, string label)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullParent = Path.GetFullPath(allowedParent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[TryGameRefDataExportTransaction] 拒绝删除越界{label}：path={fullPath}, allowedParent={fullParent}");
                return;
            }

            try
            {
                Directory.Delete(fullPath, true);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TryGameRefDataExportTransaction] 清理{label}失败，请手动删除：{fullPath}\n{exception}");
            }
        }

        private sealed class DirectoryPublishTransaction
        {
            private readonly string transactionRoot;
            private readonly string backupRoot;
            private readonly string failedNewRoot;
            private readonly List<PublishEntry> entries = new List<PublishEntry>();

            public bool RollbackSucceeded { get; private set; } = true;

            public DirectoryPublishTransaction(string transactionRoot)
            {
                this.transactionRoot = Path.GetFullPath(transactionRoot);
                backupRoot = Path.Combine(this.transactionRoot, "backup");
                failedNewRoot = Path.Combine(this.transactionRoot, "failed-new");
            }

            public void Add(string label, string stagedDirectory, string targetDirectory)
            {
                entries.Add(new PublishEntry
                {
                    Label = label,
                    StagedDirectory = Path.GetFullPath(stagedDirectory),
                    TargetDirectory = Path.GetFullPath(targetDirectory),
                    BackupDirectory = Path.Combine(backupRoot, entries.Count.ToString("00")),
                    FailedNewDirectory = Path.Combine(failedNewRoot, entries.Count.ToString("00")),
                });
            }

            public bool Commit(Func<bool> verifyPublished)
            {
                if (verifyPublished == null)
                {
                    throw new ArgumentNullException(nameof(verifyPublished));
                }

                Directory.CreateDirectory(backupRoot);
                for (int i = 0; i < entries.Count; i++)
                {
                    PublishEntry entry = entries[i];
                    try
                    {
                        if (!Directory.Exists(entry.StagedDirectory))
                        {
                            throw new DirectoryNotFoundException("待发布 staging 目录不存在：" + entry.StagedDirectory);
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(entry.TargetDirectory));
                        if (Directory.Exists(entry.TargetDirectory))
                        {
                            Directory.Move(entry.TargetDirectory, entry.BackupDirectory);
                            entry.HadOriginal = true;
                        }

                        Directory.Move(entry.StagedDirectory, entry.TargetDirectory);
                        entry.Published = true;
                        Debug.Log($"[TryGameRefDataExportTransaction] 已发布目录：{entry.Label}, target={entry.TargetDirectory}");
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"[TryGameRefDataExportTransaction] 发布目录失败：{entry.Label}, " +
                            $"staged={entry.StagedDirectory}, target={entry.TargetDirectory}, backup={entry.BackupDirectory}\n{exception}");
                        // 当前项可能已经把旧目录移入 backup，只是 staged -> target 失败，因此必须从 i 开始回滚。
                        RollbackSucceeded = Rollback(i);
                        return false;
                    }
                }

                try
                {
                    if (!verifyPublished())
                    {
                        throw new InvalidDataException("正式目录发布后校验返回失败。");
                    }

                    Debug.Log("[TryGameRefDataExportTransaction] 正式目录发布后校验通过，允许清理旧 backup。");
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "[TryGameRefDataExportTransaction] 正式目录已移动完成，但发布后校验失败；" +
                        $"将在保留 backup 的情况下反向回滚。transaction={transactionRoot}\n{exception}");
                    RollbackSucceeded = Rollback(entries.Count - 1);
                    return false;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    PublishEntry entry = entries[i];
                    if (Directory.Exists(entry.BackupDirectory))
                    {
                        try
                        {
                            Directory.Delete(entry.BackupDirectory, true);
                        }
                        catch (Exception exception)
                        {
                            // 正式目录已经全部发布，backup 清理失败不应把成功发布改判为失败。
                            Debug.LogError(
                                "[TryGameRefDataExportTransaction] 正式目录已经发布成功，但旧 backup 清理失败。" +
                                $"请确认差异后手动清理：label={entry.Label}, backup={entry.BackupDirectory}\n{exception}");
                        }
                    }
                }

                return true;
            }

            private bool Rollback(int lastTouchedIndex)
            {
                bool success = true;
                try
                {
                    Directory.CreateDirectory(failedNewRoot);
                }
                catch (Exception exception)
                {
                    success = false;
                    Debug.LogError(
                        "[TryGameRefDataExportTransaction] 创建 failed-new 回滚目录失败；" +
                        $"已发布的新目录可能无法移出：path={failedNewRoot}\n{exception}");
                }

                int safeLastIndex = Math.Min(lastTouchedIndex, entries.Count - 1);
                for (int i = safeLastIndex; i >= 0; i--)
                {
                    PublishEntry entry = entries[i];
                    if (!entry.Published && !entry.HadOriginal)
                    {
                        continue;
                    }

                    try
                    {
                        if (entry.Published && Directory.Exists(entry.TargetDirectory))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(entry.FailedNewDirectory));
                            if (Directory.Exists(entry.FailedNewDirectory))
                            {
                                throw new IOException("failed-new 目标已存在，拒绝覆盖：" + entry.FailedNewDirectory);
                            }

                            Directory.Move(entry.TargetDirectory, entry.FailedNewDirectory);
                        }

                        if (entry.HadOriginal)
                        {
                            if (!Directory.Exists(entry.BackupDirectory))
                            {
                                throw new DirectoryNotFoundException("回滚所需 backup 不存在：" + entry.BackupDirectory);
                            }

                            if (Directory.Exists(entry.TargetDirectory))
                            {
                                throw new IOException("回滚目标仍被占用，拒绝覆盖：" + entry.TargetDirectory);
                            }

                            Directory.Move(entry.BackupDirectory, entry.TargetDirectory);
                        }

                        entry.Published = false;
                        entry.HadOriginal = false;
                        Debug.LogWarning(
                            $"[TryGameRefDataExportTransaction] 已回滚目录：{entry.Label}, " +
                            $"target={entry.TargetDirectory}, failedNew={entry.FailedNewDirectory}");
                    }
                    catch (Exception exception)
                    {
                        success = false;
                        Debug.LogError(
                            $"[TryGameRefDataExportTransaction] 回滚目录失败，已保留 backup/failed-new 供人工恢复：" +
                            $"label={entry.Label}, backup={entry.BackupDirectory}, failedNew={entry.FailedNewDirectory}\n{exception}");
                    }
                }

                return success;
            }
        }

        private sealed class PublishEntry
        {
            public string Label;
            public string StagedDirectory;
            public string TargetDirectory;
            public string BackupDirectory;
            public string FailedNewDirectory;
            public bool HadOriginal;
            public bool Published;
        }
    }
}
