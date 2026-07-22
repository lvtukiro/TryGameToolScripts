using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TryGame.RefDataTools.Editor
{
    /// <summary>
    /// 在 staging 内完成发布前验证并生成三仓库共享的确定性 manifest。
    /// </summary>
    internal static class TryGameRefDataExportValidator
    {
        private const int ProcessTimeoutMilliseconds = 300000;
        private const int ManifestFormatVersion = 3;
        private const string TransactionToolVersion = "1.5.0";
        private const string RefDataRuntimeProjectFileName = "TryGame.RefData.Runtime.csproj";
        private const string SourceOutputRoot = "SourceOutput";
        private const string RuntimeOutputRoot = "RuntimeOutput";
        private const string GeneratedTablesRoot = "GeneratedTables";
        private const string GeneratedConfigRoot = "GeneratedConfig";
        private const string CanonicalSourceInputRole = "canonicalSource";
        private const string ImplicitDependencyInputRole = "implicitDependency";

        public static bool TryCaptureInputHashes(
            IReadOnlyList<string> excelFullPaths,
            out Dictionary<string, string> inputHashes)
        {
            inputHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (excelFullPaths == null || excelFullPaths.Count == 0)
                {
                    throw new InvalidDataException("没有可记录的 Excel 输入。");
                }

                for (int i = 0; i < excelFullPaths.Count; i++)
                {
                    string path = Path.GetFullPath(excelFullPaths[i]);
                    EnsureFile(path, "Excel 输入");
                    if (inputHashes.ContainsKey(path))
                    {
                        throw new InvalidDataException("本次导出包含重复 Excel 输入：" + path);
                    }

                    inputHashes.Add(path, ComputeSha256(path));
                }

                Debug.Log($"[TryGameRefDataExportValidator] 已记录导出前 Excel 哈希：count={inputHashes.Count}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TryGameRefDataExportValidator] 记录导出前 Excel 哈希失败，事务未启动：\n{exception}");
                inputHashes.Clear();
                return false;
            }
        }

        public static bool ValidateInputHashesUnchanged(
            string phase,
            IReadOnlyList<string> inputFullPaths,
            IReadOnlyDictionary<string, string> expectedInputHashes)
        {
            try
            {
                if (inputFullPaths == null || inputFullPaths.Count == 0)
                {
                    throw new InvalidDataException("没有可执行最终哈希门禁的 Excel 输入。");
                }

                if (expectedInputHashes == null || expectedInputHashes.Count != inputFullPaths.Count)
                {
                    throw new InvalidDataException(
                        $"最终哈希门禁输入数量不匹配：inputs={inputFullPaths.Count}, " +
                        $"hashes={(expectedInputHashes == null ? -1 : expectedInputHashes.Count)}");
                }

                HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < inputFullPaths.Count; i++)
                {
                    string path = Path.GetFullPath(inputFullPaths[i]);
                    EnsureFile(path, $"{phase} Excel 输入");
                    if (!seenPaths.Add(path))
                    {
                        throw new InvalidDataException($"{phase} 包含重复 Excel 输入：{path}");
                    }

                    string actualSha256 = ComputeSha256(path);
                    if (!expectedInputHashes.TryGetValue(path, out string expectedSha256) ||
                        !string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"Excel 输入在导出期间发生变化：phase={phase}, path={path}, " +
                            $"expected={expectedSha256 ?? "<missing>"}, actual={actualSha256}");
                    }
                }

                Debug.Log(
                    $"[TryGameRefDataExportValidator] Excel 输入最终哈希门禁通过：phase={phase}, " +
                    $"count={seenPaths.Count}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[TryGameRefDataExportValidator] Excel 输入最终哈希门禁失败：phase={phase}\n{exception}");
                return false;
            }
        }

        /// <summary>
        /// 增量导出必须建立在一份完整、可复核的正式快照上。
        /// 未选择但已经变化的源表不能把新哈希写入 manifest 并继续保留旧产物；
        /// 源表集合或公共定义变化时必须执行全量清洁重建。
        /// </summary>
        public static bool ValidateIncrementalSourceSnapshot(
            string sourceOutput,
            string runtimeOutput,
            string generatedTables,
            string generatedConfig,
            IReadOnlyList<string> selectedInputFullPaths,
            IReadOnlyList<string> completeInputFullPaths,
            IReadOnlyCollection<string> implicitDependencyFullPaths,
            IReadOnlyDictionary<string, string> currentInputHashes)
        {
            const string Phase = "增量导出正式基线";
            try
            {
                string manifestPath = Path.Combine(sourceOutput, TryGameRefDataPaths.ManifestFileName);
                EnsureFile(manifestPath, Phase + " manifest");
                string manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
                if (!ValidateManifestAndPayloadCopies(
                    Phase,
                    manifestJson,
                    sourceOutput,
                    runtimeOutput,
                    generatedTables,
                    generatedConfig))
                {
                    Debug.LogError(
                        "[TryGameRefDataExportValidator] 增量导出基线的三份 manifest 或 payload 已不一致；" +
                        "请先执行“导出全部配表并生成入口”建立新的完整基线。");
                    return false;
                }

                RefDataManifest publishedManifest = DeserializeManifest(manifestJson, Phase);
                if (publishedManifest.inputs == null || publishedManifest.inputs.Count == 0)
                {
                    throw new InvalidDataException("正式 manifest 没有任何源表快照输入。");
                }

                if (selectedInputFullPaths == null || selectedInputFullPaths.Count == 0)
                {
                    throw new InvalidDataException("增量导出没有任何本次选中输入。");
                }

                if (completeInputFullPaths == null || completeInputFullPaths.Count == 0)
                {
                    throw new InvalidDataException("增量导出无法建立完整规范源表快照。");
                }

                if (implicitDependencyFullPaths == null)
                {
                    throw new InvalidDataException("完整源表快照的隐式依赖集合为 null。");
                }

                if (currentInputHashes == null || currentInputHashes.Count != completeInputFullPaths.Count)
                {
                    throw new InvalidDataException(
                        $"完整源表快照哈希数量不匹配：inputs={completeInputFullPaths.Count}, " +
                        $"hashes={(currentInputHashes == null ? -1 : currentInputHashes.Count)}");
                }

                HashSet<string> completePaths = new HashSet<string>(
                    completeInputFullPaths.Select(Path.GetFullPath),
                    StringComparer.OrdinalIgnoreCase);
                if (completePaths.Count != completeInputFullPaths.Count)
                {
                    throw new InvalidDataException("完整规范源表快照包含重复路径。");
                }

                HashSet<string> implicitPaths = new HashSet<string>(
                    implicitDependencyFullPaths.Select(Path.GetFullPath),
                    StringComparer.OrdinalIgnoreCase);
                if (!implicitPaths.IsSubsetOf(completePaths))
                {
                    throw new InvalidDataException("隐式依赖不属于完整规范源表快照。");
                }

                HashSet<string> selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selectedInputFullPaths.Count; i++)
                {
                    string selectedPath = Path.GetFullPath(selectedInputFullPaths[i]);
                    if (!selectedPaths.Add(selectedPath))
                    {
                        throw new InvalidDataException("增量导出包含重复选中源表：" + selectedPath);
                    }

                    if (!completePaths.Contains(selectedPath) || implicitPaths.Contains(selectedPath))
                    {
                        throw new InvalidDataException("增量导出选中了规范源表集合之外的文件：" + selectedPath);
                    }
                }

                Dictionary<string, RefDataInputManifest> publishedInputs =
                    new Dictionary<string, RefDataInputManifest>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < publishedManifest.inputs.Count; i++)
                {
                    RefDataInputManifest input = publishedManifest.inputs[i];
                    if (input == null)
                    {
                        throw new InvalidDataException($"正式 manifest inputs 包含空记录：index={i}");
                    }

                    string fullPath = ResolveManifestInputFullPath(input.path);
                    if (publishedInputs.ContainsKey(fullPath))
                    {
                        throw new InvalidDataException("正式 manifest inputs 包含重复路径：" + fullPath);
                    }

                    publishedInputs.Add(fullPath, input);

                    if (!string.Equals(input.file, Path.GetFileName(fullPath), StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(input.sha256))
                    {
                        throw new InvalidDataException(
                            $"正式 manifest 输入记录不完整：index={i}, file={input.file}, path={input.path}, sha256={input.sha256}");
                    }
                }

                HashSet<string> publishedPaths = new HashSet<string>(
                    publishedInputs.Keys,
                    StringComparer.OrdinalIgnoreCase);
                if (!publishedPaths.SetEquals(completePaths))
                {
                    string missing = string.Join(", ", completePaths
                        .Except(publishedPaths, StringComparer.OrdinalIgnoreCase)
                        .Select(Path.GetFileName)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                    string obsolete = string.Join(", ", publishedPaths
                        .Except(completePaths, StringComparer.OrdinalIgnoreCase)
                        .Select(Path.GetFileName)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                    throw new InvalidDataException(
                        "正式 manifest 不是当前规范源目录的完整输入快照；新增、删除、改名或旧版单项 manifest " +
                        "必须先执行全量清洁重建：" +
                        $"current={completePaths.Count}, published={publishedPaths.Count}, " +
                        $"missingInManifest=[{missing}], obsoleteInManifest=[{obsolete}]");
                }

                List<string> changedSelected = new List<string>();
                List<string> changedNotSelected = new List<string>();
                foreach (string path in completePaths.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    RefDataInputManifest publishedInput = publishedInputs[path];
                    bool isImplicit = implicitPaths.Contains(path);
                    string expectedRole = isImplicit ? ImplicitDependencyInputRole : CanonicalSourceInputRole;
                    if (!string.Equals(publishedInput.role, expectedRole, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"正式 manifest 输入角色不符合完整快照约定：path={path}, " +
                            $"expectedRole={expectedRole}, actualRole={publishedInput.role}");
                    }

                    if (!currentInputHashes.TryGetValue(path, out string currentSha256) ||
                        string.IsNullOrWhiteSpace(currentSha256))
                    {
                        throw new InvalidDataException("当前完整源表快照缺少哈希：" + path);
                    }

                    if (string.Equals(currentSha256, publishedInput.sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (isImplicit)
                    {
                        throw new InvalidDataException(
                            "共用枚举结构体已变化，可能影响全部普通表；拒绝只重导部分表。" +
                            $"请执行全量清洁重建：path={path}, published={publishedInput.sha256}, current={currentSha256}");
                    }

                    if (selectedPaths.Contains(path))
                    {
                        changedSelected.Add(Path.GetFileName(path));
                    }
                    else
                    {
                        changedNotSelected.Add(Path.GetFileName(path));
                    }
                }

                if (changedNotSelected.Count > 0)
                {
                    throw new InvalidDataException(
                        "存在已修改但本次未选中的 Excel，拒绝把新源表哈希与旧产物写成同一份 manifest。" +
                        "请把这些表一并选中，或执行全量清洁重建：" +
                        $"changedNotSelected=[{string.Join(", ", changedNotSelected)}]");
                }

                Debug.Log(
                    "[TryGameRefDataExportValidator] 增量导出完整源表基线通过：" +
                    $"snapshotInputs={completePaths.Count}, selected={selectedPaths.Count}, " +
                    $"changedSelected=[{string.Join(", ", changedSelected)}]");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[TryGameRefDataExportValidator] 增量导出完整源表基线校验失败，正式目录保持不变。" +
                    "请按日志补选已修改表，或执行“导出全部配表并生成入口”：\n" + exception);
                return false;
            }
        }

        public static bool ValidateManifestAndPayloadCopies(
            string phase,
            string expectedManifestJson,
            string sourceOutput,
            string runtimeOutput,
            string generatedTables,
            string generatedConfig)
        {
            try
            {
                ValidateManifestCopiesAgainstExpected(
                    phase,
                    expectedManifestJson,
                    sourceOutput,
                    runtimeOutput,
                    generatedTables);

                RefDataManifest expectedManifest = DeserializeManifest(expectedManifestJson, phase);
                List<RefDataPayloadFileManifest> actualPayloadFiles = BuildPayloadManifest(
                    sourceOutput,
                    runtimeOutput,
                    generatedTables,
                    generatedConfig);
                ValidatePayloadSnapshot(phase, expectedManifest.payloadFiles, actualPayloadFiles);

                Debug.Log(
                    $"[TryGameRefDataExportValidator] manifest 与完整产物门禁通过：phase={phase}, " +
                    $"payloadFiles={actualPayloadFiles.Count}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[TryGameRefDataExportValidator] manifest 或完整产物门禁失败：phase={phase}\n{exception}");
                return false;
            }
        }

        public static bool ValidateAndWriteManifest(
            string transactionRoot,
            IReadOnlyList<string> validationInputFullPaths,
            IReadOnlyCollection<string> implicitDependencyFullPaths,
            IReadOnlyDictionary<string, string> expectedInputHashes,
            string stagedSourceOutput,
            string stagedRuntimeOutput,
            string stagedGeneratedTables,
            string stagedGeneratedConfig,
            out string manifestJson)
        {
            manifestJson = string.Empty;
            try
            {
                string jsonDirectory = Path.Combine(stagedSourceOutput, "Json", "client");
                string fbsDirectory = Path.Combine(stagedSourceOutput, "fb_idl");
                string bytesDirectory = Path.Combine(stagedRuntimeOutput, "fb_data");
                EnsureDirectory(jsonDirectory, "客户端 JSON");
                EnsureDirectory(fbsDirectory, "FBS");
                EnsureDirectory(bytesDirectory, "运行时 bytes");
                EnsureDirectory(stagedGeneratedTables, "GeneratedTables");
                EnsureDirectory(stagedGeneratedConfig, "GeneratedConfig");

                SortedSet<string> jsonTables = CollectFileNames(jsonDirectory, "*.json");
                SortedSet<string> bytesTables = CollectFileNames(bytesDirectory, "*.bytes");
                if (jsonTables.Count == 0)
                {
                    throw new InvalidDataException("staging 没有任何客户端 JSON 表，拒绝发布。");
                }

                if (!jsonTables.SetEquals(bytesTables))
                {
                    throw new InvalidDataException(
                        "JSON 与 bytes 表集合不一致：" +
                        DescribeSetDifference("missingBytes", jsonTables, bytesTables) + ", " +
                        DescribeSetDifference("orphanBytes", bytesTables, jsonTables));
                }

                string flatcPath = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.ToolBinAssetPath + "/flatc.exe");
                if (!File.Exists(flatcPath))
                {
                    throw new FileNotFoundException("flatc.exe 不存在，无法验证 bytes。", flatcPath);
                }

                string decodeDirectory = Path.Combine(transactionRoot, "validation", "decoded");
                string expectedBytesDirectory = Path.Combine(transactionRoot, "validation", "expected-bytes");
                Directory.CreateDirectory(decodeDirectory);
                Directory.CreateDirectory(expectedBytesDirectory);
                RefDataManifest manifest = new RefDataManifest
                {
                    formatVersion = ManifestFormatVersion,
                    transactionToolVersion = TransactionToolVersion,
                    cltabtoyVersion = ReadFileVersion(
                        TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.ToolBinAssetPath + "/cltabtoy.exe")),
                    flatcVersion = ReadFileVersion(flatcPath),
                };

                foreach (string tableName in jsonTables)
                {
                    string jsonPath = Path.Combine(jsonDirectory, tableName + ".json");
                    string fbsPath = Path.Combine(fbsDirectory, tableName + ".fbs");
                    string bytesPath = Path.Combine(bytesDirectory, tableName + ".bytes");
                    string generatedPath = Path.Combine(stagedGeneratedTables, tableName + ".cs");
                    EnsureFile(fbsPath, "表 FBS");
                    EnsureFile(bytesPath, "表 bytes");
                    EnsureFile(generatedPath, "生成 C#");

                    int rowCount = ValidateJsonRowsAndUniqueIds(tableName, jsonPath);
                    ValidateBytesWithFlatc(flatcPath, fbsDirectory, fbsPath, bytesPath, decodeDirectory);
                    ValidateBytesMatchesJson(
                        flatcPath,
                        fbsDirectory,
                        fbsPath,
                        jsonPath,
                        bytesPath,
                        expectedBytesDirectory,
                        tableName);
                    manifest.tables.Add(new RefDataTableManifest
                    {
                        name = tableName,
                        rowCount = rowCount,
                        jsonSha256 = ComputeSha256(jsonPath),
                        fbsSha256 = ComputeSha256(fbsPath),
                        bytesSha256 = ComputeSha256(bytesPath),
                        generatedCSharpSha256 = ComputeSha256(generatedPath),
                    });
                }

                manifest.language = ValidateLanguage(stagedRuntimeOutput);
                ValidateGeneratedCSharpCompiles(
                    transactionRoot,
                    stagedGeneratedTables,
                    stagedGeneratedConfig);
                manifest.inputs = BuildInputManifest(
                    validationInputFullPaths,
                    implicitDependencyFullPaths,
                    expectedInputHashes);
                manifest.generatedCodeSha256 = ComputeDirectorySha256(
                    stagedGeneratedTables,
                    stagedGeneratedConfig);
                manifest.payloadFiles = BuildPayloadManifest(
                    stagedSourceOutput,
                    stagedRuntimeOutput,
                    stagedGeneratedTables,
                    stagedGeneratedConfig);

                manifestJson = TryGameRefDataTextNormalizer.NormalizeLineEndings(JsonUtility.ToJson(manifest, true)) + "\n";
                ValidateManifestSerialization(
                    manifestJson,
                    manifest.inputs.Count,
                    manifest.tables.Count,
                    manifest.payloadFiles.Count);
                string previewPath = Path.Combine(transactionRoot, "manifest.preview.json");
                File.WriteAllText(previewPath, manifestJson, new UTF8Encoding(false));
                Debug.Log(
                    $"[TryGameRefDataExportValidator] staging 验证通过：tables={manifest.tables.Count}, " +
                    $"languageRows={manifest.language.rowCount}, manifest={previewPath}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TryGameRefDataExportValidator] staging 验证失败，禁止发布：\n{exception}");
                manifestJson = string.Empty;
                return false;
            }
        }

        private static List<RefDataInputManifest> BuildInputManifest(
            IReadOnlyList<string> validationInputFullPaths,
            IReadOnlyCollection<string> implicitDependencyFullPaths,
            IReadOnlyDictionary<string, string> expectedInputHashes)
        {
            if (validationInputFullPaths == null || validationInputFullPaths.Count == 0)
            {
                throw new InvalidDataException("manifest 没有任何完整规范源表快照输入。");
            }

            if (implicitDependencyFullPaths == null)
            {
                throw new InvalidDataException("manifest 的隐式 Excel 依赖集合为 null。");
            }

            if (expectedInputHashes == null || expectedInputHashes.Count != validationInputFullPaths.Count)
            {
                throw new InvalidDataException(
                    $"导出前 Excel 哈希数量不匹配：inputs={validationInputFullPaths.Count}, " +
                    $"hashes={(expectedInputHashes == null ? -1 : expectedInputHashes.Count)}");
            }

            string[] files = validationInputFullPaths
                .Select(Path.GetFullPath)
                .OrderBy(GetManifestInputPath, StringComparer.Ordinal)
                .ToArray();
            HashSet<string> implicitDependencies = new HashSet<string>(
                implicitDependencyFullPaths.Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);

            List<RefDataInputManifest> result = new List<RefDataInputManifest>(files.Length);
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                EnsureFile(path, "Excel 输入");
                string extension = Path.GetExtension(path);
                if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("manifest 输入扩展名不受支持：" + path);
                }

                if (!seenPaths.Add(path))
                {
                    throw new InvalidDataException("完整规范源表快照包含重复 Excel 输入：" + path);
                }

                string currentSha256 = ComputeSha256(path);
                if (!expectedInputHashes.TryGetValue(path, out string expectedSha256) ||
                    !string.Equals(currentSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Excel 在导出期间发生变化，拒绝发布旧输出：" +
                        $"path={path}, before={expectedSha256 ?? "<missing>"}, after={currentSha256}");
                }

                result.Add(new RefDataInputManifest
                {
                    file = Path.GetFileName(path),
                    path = GetManifestInputPath(path),
                    role = implicitDependencies.Contains(path) ? ImplicitDependencyInputRole : CanonicalSourceInputRole,
                    sha256 = currentSha256,
                });
            }

            int recordedDependencyCount = result.Count(input =>
                string.Equals(input.role, ImplicitDependencyInputRole, StringComparison.Ordinal));
            if (recordedDependencyCount != implicitDependencies.Count)
            {
                string missingDependencies = string.Join(", ", implicitDependencies
                    .Where(path => !seenPaths.Contains(path))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
                throw new InvalidDataException(
                    "manifest 的隐式 Excel 依赖不属于有效输入集合：" +
                    $"expected={implicitDependencies.Count}, actual={recordedDependencyCount}, " +
                    $"missing=[{missingDependencies}]");
            }

            return result;
        }

        private static string GetManifestInputPath(string inputPath)
        {
            string fullPath = Path.GetFullPath(inputPath).Replace("\\", "/");
            string projectRoot = Path.GetFullPath(TryGameRefDataPaths.ProjectRoot)
                .Replace("\\", "/")
                .TrimEnd('/') + "/";
            return fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(projectRoot.Length)
                : fullPath;
        }

        private static string ResolveManifestInputFullPath(string manifestInputPath)
        {
            if (string.IsNullOrWhiteSpace(manifestInputPath))
            {
                throw new InvalidDataException("manifest 输入路径为空。");
            }

            string normalized = manifestInputPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(
                Path.IsPathRooted(normalized)
                    ? normalized
                    : Path.Combine(TryGameRefDataPaths.ProjectRoot, normalized));
        }

        private static void ValidateManifestSerialization(
            string manifestJson,
            int expectedInputCount,
            int expectedTableCount,
            int expectedPayloadFileCount)
        {
            object root = MiniJsonParser.Deserialize(manifestJson);
            if (!(root is IDictionary<string, object> manifest))
            {
                throw new InvalidDataException("JsonUtility 生成的 manifest 根结构不是对象。");
            }

            if (!manifest.TryGetValue("inputs", out object inputsValue) || !(inputsValue is IList inputs) ||
                inputs.Count != expectedInputCount)
            {
                throw new InvalidDataException(
                    $"JsonUtility 生成的 manifest inputs 不完整：expected={expectedInputCount}, actual={GetListCount(inputsValue)}");
            }

            if (!manifest.TryGetValue("tables", out object tablesValue) || !(tablesValue is IList tables) ||
                tables.Count != expectedTableCount)
            {
                throw new InvalidDataException(
                    $"JsonUtility 生成的 manifest tables 不完整：expected={expectedTableCount}, actual={GetListCount(tablesValue)}");
            }

            if (!manifest.TryGetValue("language", out object languageValue) ||
                !(languageValue is IDictionary<string, object>))
            {
                throw new InvalidDataException("JsonUtility 生成的 manifest 缺少 language 对象。");
            }

            if (!manifest.TryGetValue("payloadFiles", out object payloadFilesValue) ||
                !(payloadFilesValue is IList payloadFiles) ||
                payloadFiles.Count != expectedPayloadFileCount)
            {
                throw new InvalidDataException(
                    $"JsonUtility 生成的 manifest payloadFiles 不完整：" +
                    $"expected={expectedPayloadFileCount}, actual={GetListCount(payloadFilesValue)}");
            }
        }

        private static int GetListCount(object value)
        {
            return value is IList list ? list.Count : -1;
        }

        private static void ValidateManifestCopiesAgainstExpected(
            string phase,
            string expectedManifestJson,
            params string[] manifestDirectories)
        {
            if (string.IsNullOrEmpty(expectedManifestJson))
            {
                throw new InvalidDataException($"{phase} 没有本次事务生成的 expected manifest。");
            }

            if (manifestDirectories == null || manifestDirectories.Length != 3)
            {
                throw new InvalidDataException(
                    $"manifest 校验必须传入三个仓库目录：actual={(manifestDirectories == null ? -1 : manifestDirectories.Length)}");
            }

            byte[] expectedContent = new UTF8Encoding(false).GetBytes(expectedManifestJson);
            string expectedSha256 = ComputeSha256(expectedContent);
            for (int i = 0; i < manifestDirectories.Length; i++)
            {
                string path = Path.Combine(manifestDirectories[i], TryGameRefDataPaths.ManifestFileName);
                EnsureFile(path, $"{phase} manifest[{i}]");
                byte[] actualContent = File.ReadAllBytes(path);
                string actualSha256 = ComputeSha256(actualContent);
                if (actualContent.Length != expectedContent.Length ||
                    !string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase) ||
                    !actualContent.SequenceEqual(expectedContent))
                {
                    throw new InvalidDataException(
                        $"manifest 与本次事务生成内容不一致：phase={phase}, path={path}, " +
                        $"expectedLength={expectedContent.Length}, actualLength={actualContent.Length}, " +
                        $"expectedSha256={expectedSha256}, actualSha256={actualSha256}");
                }
            }
        }

        private static RefDataManifest DeserializeManifest(string manifestJson, string phase)
        {
            RefDataManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<RefDataManifest>(manifestJson);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException($"{phase} expected manifest 无法反序列化。", exception);
            }

            if (manifest == null)
            {
                throw new InvalidDataException($"{phase} expected manifest 反序列化结果为 null。");
            }

            if (manifest.formatVersion != ManifestFormatVersion)
            {
                throw new InvalidDataException(
                    $"{phase} manifest 版本不受支持：expected={ManifestFormatVersion}, actual={manifest.formatVersion}");
            }

            if (manifest.payloadFiles == null || manifest.payloadFiles.Count == 0)
            {
                throw new InvalidDataException($"{phase} manifest 没有任何 payloadFiles，拒绝视为空产物发布。");
            }

            return manifest;
        }

        private static List<RefDataPayloadFileManifest> BuildPayloadManifest(
            string sourceOutput,
            string runtimeOutput,
            string generatedTables,
            string generatedConfig)
        {
            PayloadRootDefinition[] roots =
            {
                new PayloadRootDefinition(SourceOutputRoot, sourceOutput),
                new PayloadRootDefinition(RuntimeOutputRoot, runtimeOutput),
                new PayloadRootDefinition(GeneratedTablesRoot, generatedTables),
                new PayloadRootDefinition(GeneratedConfigRoot, generatedConfig),
            };

            List<RefDataPayloadFileManifest> result = new List<RefDataPayloadFileManifest>();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                PayloadRootDefinition root = roots[rootIndex];
                EnsureDirectory(root.Directory, root.LogicalRoot + " payload 根目录");
                string[] files = Directory.GetFiles(root.Directory, "*", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
                {
                    string file = files[fileIndex];
                    if (IsExcludedPayloadFile(root.Directory, file))
                    {
                        continue;
                    }

                    result.Add(new RefDataPayloadFileManifest
                    {
                        logicalRoot = root.LogicalRoot,
                        relativePath = GetPayloadRelativePath(root.Directory, file),
                        sha256 = ComputeSha256(file),
                    });
                }
            }

            return result
                .OrderBy(file => file.logicalRoot, StringComparer.Ordinal)
                .ThenBy(file => file.relativePath, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsExcludedPayloadFile(string rootDirectory, string path)
        {
            if (Path.GetExtension(path).Equals(".meta", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string rootManifestPath = Path.GetFullPath(
                Path.Combine(rootDirectory, TryGameRefDataPaths.ManifestFileName));
            return Path.GetFullPath(path).Equals(rootManifestPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPayloadRelativePath(string rootDirectory, string filePath)
        {
            string root = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = root + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"payload 文件不在声明的根目录内：root={root}, file={fullPath}");
            }

            return fullPath.Substring(prefix.Length).Replace('\\', '/');
        }

        private static void ValidatePayloadSnapshot(
            string phase,
            IReadOnlyList<RefDataPayloadFileManifest> expectedFiles,
            IReadOnlyList<RefDataPayloadFileManifest> actualFiles)
        {
            Dictionary<string, string> expected = BuildPayloadIndex(expectedFiles, phase + " expected");
            Dictionary<string, string> actual = BuildPayloadIndex(actualFiles, phase + " actual");

            List<string> missing = expected.Keys
                .Except(actual.Keys, StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
            List<string> unexpected = actual.Keys
                .Except(expected.Keys, StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
            if (missing.Count > 0 || unexpected.Count > 0)
            {
                throw new InvalidDataException(
                    $"payload 文件集合与 manifest 不一致：phase={phase}, " +
                    $"expected={expected.Count}, actual={actual.Count}, " +
                    $"missing=[{FormatPayloadKeys(missing)}], unexpected=[{FormatPayloadKeys(unexpected)}]");
            }

            foreach (KeyValuePair<string, string> pair in expected.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                string actualSha256 = actual[pair.Key];
                if (!string.Equals(pair.Value, actualSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"payload 文件哈希与 manifest 不一致：phase={phase}, file={pair.Key}, " +
                        $"expected={pair.Value}, actual={actualSha256}");
                }
            }
        }

        private static Dictionary<string, string> BuildPayloadIndex(
            IReadOnlyList<RefDataPayloadFileManifest> files,
            string label)
        {
            if (files == null || files.Count == 0)
            {
                throw new InvalidDataException($"{label} payload 文件清单为空。");
            }

            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < files.Count; i++)
            {
                RefDataPayloadFileManifest file = files[i];
                if (file == null || !IsKnownPayloadRoot(file.logicalRoot))
                {
                    throw new InvalidDataException(
                        $"{label} payload logicalRoot 无效：index={i}, root={file?.logicalRoot ?? "<null>"}");
                }

                if (string.IsNullOrWhiteSpace(file.relativePath) ||
                    file.relativePath.IndexOf('\\') >= 0 ||
                    Path.IsPathRooted(file.relativePath) ||
                    file.relativePath.Equals("..", StringComparison.Ordinal) ||
                    file.relativePath.StartsWith("../", StringComparison.Ordinal) ||
                    file.relativePath.IndexOf("/../", StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidDataException(
                        $"{label} payload relativePath 无效：index={i}, path={file.relativePath ?? "<null>"}");
                }

                if (!IsSha256(file.sha256))
                {
                    throw new InvalidDataException(
                        $"{label} payload SHA256 无效：index={i}, root={file.logicalRoot}, " +
                        $"path={file.relativePath}, sha256={file.sha256 ?? "<null>"}");
                }

                string key = file.logicalRoot + "/" + file.relativePath;
                if (result.ContainsKey(key))
                {
                    throw new InvalidDataException($"{label} payload 包含重复文件：{key}");
                }

                result.Add(key, file.sha256);
            }

            return result;
        }

        private static bool IsKnownPayloadRoot(string logicalRoot)
        {
            return string.Equals(logicalRoot, SourceOutputRoot, StringComparison.Ordinal) ||
                string.Equals(logicalRoot, RuntimeOutputRoot, StringComparison.Ordinal) ||
                string.Equals(logicalRoot, GeneratedTablesRoot, StringComparison.Ordinal) ||
                string.Equals(logicalRoot, GeneratedConfigRoot, StringComparison.Ordinal);
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (!Uri.IsHexDigit(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string FormatPayloadKeys(IReadOnlyList<string> keys)
        {
            const int maxPrintedKeys = 20;
            if (keys == null || keys.Count == 0)
            {
                return string.Empty;
            }

            string value = string.Join(", ", keys.Take(maxPrintedKeys));
            return keys.Count > maxPrintedKeys
                ? value + $", ... ({keys.Count - maxPrintedKeys} more)"
                : value;
        }

        private static int ValidateJsonRowsAndUniqueIds(string tableName, string jsonPath)
        {
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            object root;
            try
            {
                root = MiniJsonParser.Deserialize(json);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException($"客户端 JSON 无法解析：table={tableName}, path={jsonPath}", exception);
            }

            List<IList> rowArrays = new List<IList>();
            if (root is IList rootArray)
            {
                rowArrays.Add(rootArray);
            }
            else if (root is IDictionary<string, object> rootObject)
            {
                foreach (KeyValuePair<string, object> property in rootObject)
                {
                    if (property.Value is IList array && IsObjectRowArray(array))
                    {
                        rowArrays.Add(array);
                    }
                }
            }

            if (rowArrays.Count == 0)
            {
                if (!(root is IDictionary<string, object>))
                {
                    throw new InvalidDataException($"JSON 根结构不是表对象：table={tableName}, path={jsonPath}");
                }

                return 1;
            }

            int rowCount = 0;
            Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int arrayIndex = 0; arrayIndex < rowArrays.Count; arrayIndex++)
            {
                IList rows = rowArrays[arrayIndex];
                rowCount += rows.Count;
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    IDictionary<string, object> row = (IDictionary<string, object>)rows[rowIndex];
                    string idKey = row.Keys.FirstOrDefault(
                        key => string.Equals(key, "id", StringComparison.OrdinalIgnoreCase));
                    if (idKey == null)
                    {
                        continue;
                    }

                    object idValue = row[idKey];
                    string id = Convert.ToString(idValue, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        throw new InvalidDataException($"表存在空 ID：table={tableName}, row={rowIndex + 1}");
                    }

                    if (ids.TryGetValue(id, out int previousRow))
                    {
                        throw new InvalidDataException(
                            $"表存在重复 ID：table={tableName}, id={id}, firstRow={previousRow}, " +
                            $"duplicateRow={rowCount - rows.Count + rowIndex + 1}");
                    }

                    ids.Add(id, rowCount - rows.Count + rowIndex + 1);
                }
            }

            if (rowCount <= 0)
            {
                throw new InvalidDataException($"表没有任何数据行：table={tableName}, path={jsonPath}");
            }

            return rowCount;
        }

        private static bool IsObjectRowArray(IList values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (!(values[i] is IDictionary<string, object>))
                {
                    return false;
                }
            }

            return true;
        }

        private static RefDataLanguageManifest ValidateLanguage(string stagedRuntimeOutput)
        {
            string languagePath = Path.Combine(stagedRuntimeOutput, "txt_data", "Language.bytes");
            EnsureFile(languagePath, "Language.bytes");
            string[] lines = File.ReadAllLines(languagePath, Encoding.UTF8);
            if (lines.Length < 2)
            {
                throw new InvalidDataException("Language.bytes 缺少表头或数据：" + languagePath);
            }

            string[] headers = lines[0].Split('\t');
            int idColumn = FindColumn(headers, "id");
            int zhColumn = FindColumn(headers, "zh_cn");
            int enColumn = FindColumn(headers, "en_US");
            if (idColumn < 0 || zhColumn < 0 || enColumn < 0)
            {
                throw new InvalidDataException(
                    $"Language.bytes 缺少必要列：id={idColumn}, zh_cn={zhColumn}, en_US={enColumn}, path={languagePath}");
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            int rowCount = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                string[] values = lines[i].Split('\t');
                string id = GetValue(values, idColumn).Trim();
                string zh = GetValue(values, zhColumn).Trim();
                string en = GetValue(values, enColumn).Trim();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(zh) || string.IsNullOrEmpty(en))
                {
                    throw new InvalidDataException(
                        $"Language.bytes 存在必要字段为空：line={i + 1}, id={id}, zhEmpty={string.IsNullOrEmpty(zh)}, enEmpty={string.IsNullOrEmpty(en)}");
                }

                if (!keys.Add(id))
                {
                    throw new InvalidDataException($"Language key 重复：line={i + 1}, key={id}");
                }

                rowCount++;
            }

            return new RefDataLanguageManifest
            {
                rowCount = rowCount,
                sha256 = ComputeSha256(languagePath),
            };
        }

        private static int FindColumn(string[] values, string name)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string GetValue(string[] values, int index)
        {
            return index >= 0 && index < values.Length ? values[index] ?? string.Empty : string.Empty;
        }

        private static void ValidateBytesWithFlatc(
            string flatcPath,
            string fbsDirectory,
            string fbsPath,
            string bytesPath,
            string decodeDirectory)
        {
            string arguments =
                "-t --strict-json --defaults-json --raw-binary -o " + Quote(decodeDirectory) + " " +
                Quote(fbsPath) + " -- " + Quote(bytesPath);
            ProcessResult result = RunProcess(flatcPath, arguments, fbsDirectory, ProcessTimeoutMilliseconds);
            if (result.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"bytes 无法按 FBS 反序列化：bytes={bytesPath}, fbs={fbsPath}, exitCode={result.ExitCode}\n" +
                    result.CombinedOutput);
            }
        }

        private static void ValidateBytesMatchesJson(
            string flatcPath,
            string fbsDirectory,
            string fbsPath,
            string jsonPath,
            string actualBytesPath,
            string expectedBytesDirectory,
            string tableName)
        {
            string expectedBytesPath = Path.Combine(expectedBytesDirectory, tableName + ".bytes");
            if (File.Exists(expectedBytesPath))
            {
                File.Delete(expectedBytesPath);
            }

            string arguments =
                "-b --strict-json -o " + Quote(expectedBytesDirectory) + " " +
                Quote(fbsPath) + " " + Quote(jsonPath);
            ProcessResult result = RunProcess(flatcPath, arguments, fbsDirectory, ProcessTimeoutMilliseconds);
            if (result.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"客户端 JSON 无法按 FBS 重新编码用于 bytes 一致性验证：" +
                    $"table={tableName}, json={jsonPath}, fbs={fbsPath}, exitCode={result.ExitCode}\n" +
                    result.CombinedOutput);
            }

            EnsureFile(expectedBytesPath, "JSON 重新编码 bytes");
            long expectedLength = new FileInfo(expectedBytesPath).Length;
            long actualLength = new FileInfo(actualBytesPath).Length;
            string expectedSha256 = ComputeSha256(expectedBytesPath);
            string actualSha256 = ComputeSha256(actualBytesPath);
            long firstDifferentOffset = FindFirstDifferentByteOffset(expectedBytesPath, actualBytesPath);
            if (expectedLength != actualLength
                || !string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase)
                || firstDifferentOffset >= 0)
            {
                throw new InvalidDataException(
                    $"客户端 JSON 与运行时 bytes 内容不一致，拒绝发布：" +
                    $"table={tableName}, json={jsonPath}, actualBytes={actualBytesPath}, " +
                    $"expectedBytes={expectedBytesPath}, expectedLength={expectedLength}, " +
                    $"actualLength={actualLength}, expectedSha256={expectedSha256}, " +
                    $"actualSha256={actualSha256}, firstDifferentByteOffset={firstDifferentOffset}");
            }
        }

        private static long FindFirstDifferentByteOffset(string expectedPath, string actualPath)
        {
            const int bufferSize = 81920;
            byte[] expectedBuffer = new byte[bufferSize];
            byte[] actualBuffer = new byte[bufferSize];
            long offset = 0L;
            using (FileStream expected = File.OpenRead(expectedPath))
            using (FileStream actual = File.OpenRead(actualPath))
            {
                while (true)
                {
                    int expectedRead = expected.Read(expectedBuffer, 0, expectedBuffer.Length);
                    int actualRead = actual.Read(actualBuffer, 0, actualBuffer.Length);
                    int commonLength = Math.Min(expectedRead, actualRead);
                    for (int i = 0; i < commonLength; i++)
                    {
                        if (expectedBuffer[i] != actualBuffer[i])
                        {
                            return offset + i;
                        }
                    }

                    if (expectedRead != actualRead)
                    {
                        return offset + commonLength;
                    }

                    if (expectedRead == 0)
                    {
                        return -1L;
                    }

                    offset += expectedRead;
                }
            }
        }

        private static void ValidateGeneratedCSharpCompiles(
            string transactionRoot,
            string stagedGeneratedTables,
            string stagedGeneratedConfig)
        {
            string sourceProject = Path.Combine(TryGameRefDataPaths.ProjectRoot, RefDataRuntimeProjectFileName);
            if (!File.Exists(sourceProject))
            {
                throw new FileNotFoundException(
                    $"RefData Runtime 工程文件不存在，无法准确验证 staging 生成代码。" +
                    $"请先让 Unity 重新生成 {RefDataRuntimeProjectFileName}；" +
                    "验证器不会回退到 Assembly-CSharp.csproj，以免掩盖程序集分层后的真实编译失败：" +
                    sourceProject,
                    sourceProject);
            }

            string validationDirectory = Path.Combine(transactionRoot, "validation", "compile");
            Directory.CreateDirectory(validationDirectory);
            string projectPath = Path.Combine(validationDirectory, "RefDataStagingValidation.csproj");

            Debug.Log($"[TryGameRefDataExportValidator] staging 编译验证基于程序集工程：{sourceProject}");
            XDocument project = XDocument.Load(sourceProject, LoadOptions.PreserveWhitespace);
            string formalTables = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.DefaultGeneratedTableAssetPath);
            string formalConfig = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.DefaultGeneratedConfigAssetPath);
            List<XElement> compileElements = project.Descendants("Compile").ToList();
            HashSet<string> includedStagedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = compileElements.Count - 1; i >= 0; i--)
            {
                XElement compile = compileElements[i];
                XAttribute includeAttribute = compile.Attribute("Include");
                if (includeAttribute == null || string.IsNullOrWhiteSpace(includeAttribute.Value))
                {
                    continue;
                }

                string originalFullPath = ResolveProjectPath(includeAttribute.Value);
                if (IsPathUnder(originalFullPath, formalTables))
                {
                    ReplaceCompilePathOrRemove(compile, stagedGeneratedTables, originalFullPath, includedStagedFiles);
                }
                else if (IsPathUnder(originalFullPath, formalConfig))
                {
                    ReplaceCompilePathOrRemove(compile, stagedGeneratedConfig, originalFullPath, includedStagedFiles);
                }
                else
                {
                    includeAttribute.Value = originalFullPath;
                }
            }

            AddMissingCompileFiles(project, stagedGeneratedTables, includedStagedFiles);
            AddMissingCompileFiles(project, stagedGeneratedConfig, includedStagedFiles);
            MakeProjectReferencesAbsolute(project);
            SetAllElementValues(project, "AssemblyName", "RefDataStagingValidation");
            SetAllElementValues(project, "BaseDirectory", TryGameRefDataPaths.ProjectRoot);
            SetAllElementValues(project, "BaseIntermediateOutputPath", Path.Combine(validationDirectory, "obj") + Path.DirectorySeparatorChar);
            SetAllElementValues(project, "IntermediateOutputPath", Path.Combine(validationDirectory, "obj") + Path.DirectorySeparatorChar);
            SetAllElementValues(project, "OutputPath", Path.Combine(validationDirectory, "bin") + Path.DirectorySeparatorChar);
            project.Save(projectPath);

            string arguments = "build " + Quote(projectPath) + " --nologo --verbosity:minimal /p:RestoreIgnoreFailedSources=true";
            ProcessResult result = RunProcess("dotnet", arguments, TryGameRefDataPaths.ProjectRoot, ProcessTimeoutMilliseconds);
            if (result.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"staging 生成 C# 编译失败：exitCode={result.ExitCode}, project={projectPath}\n" +
                    result.CombinedOutput);
            }

            Debug.Log("[TryGameRefDataExportValidator] staging GeneratedTables/GeneratedConfig 编译通过。\n" + result.CombinedOutput.Trim());
        }

        private static void MakeProjectReferencesAbsolute(XDocument project)
        {
            foreach (XElement hintPath in project.Descendants("HintPath"))
            {
                hintPath.Value = ResolvePortableProjectPath(hintPath.Value);
            }

            string[] includeElementNames =
            {
                "AdditionalFiles",
                "Analyzer",
                "EmbeddedResource",
                "ProjectReference",
            };
            for (int nameIndex = 0; nameIndex < includeElementNames.Length; nameIndex++)
            {
                foreach (XElement element in project.Descendants(includeElementNames[nameIndex]))
                {
                    XAttribute include = element.Attribute("Include");
                    if (include != null)
                    {
                        include.Value = ResolvePortableProjectPath(include.Value);
                    }
                }
            }
        }

        private static string ResolvePortableProjectPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.IndexOf("$(", StringComparison.Ordinal) >= 0)
            {
                return value;
            }

            return ResolveProjectPath(value);
        }

        private static void ReplaceCompilePathOrRemove(
            XElement compile,
            string stagedDirectory,
            string originalFullPath,
            HashSet<string> includedStagedFiles)
        {
            string stagedPath = Path.Combine(stagedDirectory, Path.GetFileName(originalFullPath));
            if (!File.Exists(stagedPath))
            {
                compile.Remove();
                return;
            }

            stagedPath = Path.GetFullPath(stagedPath);
            compile.SetAttributeValue("Include", stagedPath);
            includedStagedFiles.Add(stagedPath);
        }

        private static void AddMissingCompileFiles(
            XDocument project,
            string stagedDirectory,
            HashSet<string> includedStagedFiles)
        {
            XElement itemGroup = project.Descendants("ItemGroup").FirstOrDefault(group => group.Elements("Compile").Any());
            if (itemGroup == null)
            {
                itemGroup = new XElement("ItemGroup");
                project.Root.Add(itemGroup);
            }

            string[] files = Directory.GetFiles(stagedDirectory, "*.cs", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string fullPath = Path.GetFullPath(files[i]);
                if (includedStagedFiles.Add(fullPath))
                {
                    itemGroup.Add(new XElement("Compile", new XAttribute("Include", fullPath)));
                }
            }
        }

        private static void SetAllElementValues(XDocument document, string elementName, string value)
        {
            List<XElement> elements = document.Descendants(elementName).ToList();
            if (elements.Count == 0)
            {
                XElement propertyGroup = document.Descendants("PropertyGroup").First();
                propertyGroup.Add(new XElement(elementName, value));
                return;
            }

            for (int i = 0; i < elements.Count; i++)
            {
                elements[i].Value = value;
            }
        }

        private static string ResolveProjectPath(string path)
        {
            return Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(TryGameRefDataPaths.ProjectRoot, path));
        }

        private static bool IsPathUnder(string path, string directory)
        {
            string fullPath = Path.GetFullPath(path);
            string fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static SortedSet<string> CollectFileNames(string directory, string pattern)
        {
            SortedSet<string> result = new SortedSet<string>(StringComparer.Ordinal);
            string[] files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                result.Add(Path.GetFileNameWithoutExtension(files[i]));
            }

            return result;
        }

        private static string DescribeSetDifference(
            string label,
            IEnumerable<string> expected,
            ISet<string> actual)
        {
            return label + "=[" + string.Join(", ", expected.Where(item => !actual.Contains(item))) + "]";
        }

        private static string ComputeDirectorySha256(params string[] directories)
        {
            List<string> entries = new List<string>();
            for (int directoryIndex = 0; directoryIndex < directories.Length; directoryIndex++)
            {
                string directory = directories[directoryIndex];
                string[] files = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    entries.Add(directoryIndex + "/" + Path.GetFileName(files[i]) + ":" + ComputeSha256(files[i]));
                }
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(string.Join("\n", entries));
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] content)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(content));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }

            return sb.ToString();
        }

        private static string ReadFileVersion(string path)
        {
            EnsureFile(path, "工具文件");
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? "unknown" : info.FileVersion;
        }

        private static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMilliseconds)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            StringBuilder output = new StringBuilder();
            object outputLock = new object();
            using (Process process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                    {
                        lock (outputLock)
                        {
                            output.AppendLine(args.Data);
                        }
                    }
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                    {
                        lock (outputLock)
                        {
                            output.AppendLine(args.Data);
                        }
                    }
                };

                try
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception killException)
                        {
                            lock (outputLock)
                            {
                                output.AppendLine("进程超时后终止失败：" + killException);
                            }
                        }

                        throw new TimeoutException(
                            $"外部验证进程超时：file={fileName}, timeoutMs={timeoutMilliseconds}, arguments={arguments}\n" +
                            GetProcessOutput(output, outputLock));
                    }

                    process.WaitForExit();
                    return new ProcessResult(process.ExitCode, GetProcessOutput(output, outputLock));
                }
                catch (Exception exception) when (!(exception is TimeoutException))
                {
                    throw new InvalidOperationException(
                        $"无法启动外部验证进程：file={fileName}, arguments={arguments}", exception);
                }
            }
        }

        private static string GetProcessOutput(StringBuilder output, object outputLock)
        {
            lock (outputLock)
            {
                return output.ToString();
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void EnsureDirectory(string path, string label)
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException(label + "目录不存在：" + path);
            }
        }

        private static void EnsureFile(string path, string label)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(label + "不存在：" + path, path);
            }
        }

        private sealed class ProcessResult
        {
            public readonly int ExitCode;
            public readonly string CombinedOutput;

            public ProcessResult(int exitCode, string combinedOutput)
            {
                ExitCode = exitCode;
                CombinedOutput = combinedOutput ?? string.Empty;
            }
        }

        /// <summary>
        /// 只用于验证 cltabtoy 生成 JSON 的轻量解析器，避免给编辑器工具新增第三方 JSON 依赖。
        /// </summary>
        private sealed class MiniJsonParser : IDisposable
        {
            private const string WordBreak = "{}[],:\"";
            private readonly StringReader reader;

            private MiniJsonParser(string json)
            {
                reader = new StringReader(json ?? string.Empty);
            }

            public static object Deserialize(string json)
            {
                using (MiniJsonParser parser = new MiniJsonParser(json))
                {
                    object value = parser.ParseValue();
                    parser.EatWhitespace();
                    if (parser.reader.Peek() != -1)
                    {
                        throw new FormatException("JSON 根值之后仍有未解析内容。");
                    }

                    return value;
                }
            }

            public void Dispose()
            {
                reader.Dispose();
            }

            private object ParseValue()
            {
                switch (NextToken)
                {
                    case JsonToken.String:
                        return ParseString();
                    case JsonToken.Number:
                        return ParseNumber();
                    case JsonToken.ObjectStart:
                        return ParseObject();
                    case JsonToken.ArrayStart:
                        return ParseArray();
                    case JsonToken.True:
                        return true;
                    case JsonToken.False:
                        return false;
                    case JsonToken.Null:
                        return null;
                    default:
                        throw new FormatException("JSON 值格式非法。");
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
                reader.Read();
                while (true)
                {
                    JsonToken token = NextToken;
                    if (token == JsonToken.ObjectEnd)
                    {
                        reader.Read();
                        return result;
                    }

                    if (token != JsonToken.String)
                    {
                        throw new FormatException("JSON 对象属性名必须是字符串。");
                    }

                    string key = ParseString();
                    if (NextToken != JsonToken.Colon)
                    {
                        throw new FormatException("JSON 对象属性缺少冒号：" + key);
                    }

                    reader.Read();
                    result[key] = ParseValue();
                    token = NextToken;
                    if (token == JsonToken.Comma)
                    {
                        reader.Read();
                        continue;
                    }

                    if (token == JsonToken.ObjectEnd)
                    {
                        reader.Read();
                        return result;
                    }

                    throw new FormatException("JSON 对象属性之间缺少逗号。");
                }
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                reader.Read();
                while (true)
                {
                    JsonToken token = NextToken;
                    if (token == JsonToken.ArrayEnd)
                    {
                        reader.Read();
                        return result;
                    }

                    result.Add(ParseValue());
                    token = NextToken;
                    if (token == JsonToken.Comma)
                    {
                        reader.Read();
                        continue;
                    }

                    if (token == JsonToken.ArrayEnd)
                    {
                        reader.Read();
                        return result;
                    }

                    throw new FormatException("JSON 数组元素之间缺少逗号。");
                }
            }

            private string ParseString()
            {
                StringBuilder value = new StringBuilder();
                if (reader.Read() != '"')
                {
                    throw new FormatException("JSON 字符串缺少开始引号。");
                }

                while (true)
                {
                    int next = reader.Read();
                    if (next == -1)
                    {
                        throw new FormatException("JSON 字符串没有结束引号。");
                    }

                    char c = (char)next;
                    if (c == '"')
                    {
                        return value.ToString();
                    }

                    if (c != '\\')
                    {
                        value.Append(c);
                        continue;
                    }

                    int escaped = reader.Read();
                    if (escaped == -1)
                    {
                        throw new FormatException("JSON 转义字符不完整。");
                    }

                    switch ((char)escaped)
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u':
                            char[] hex = new char[4];
                            for (int i = 0; i < hex.Length; i++)
                            {
                                int hexValue = reader.Read();
                                if (hexValue == -1)
                                {
                                    throw new FormatException("JSON Unicode 转义不完整。");
                                }

                                hex[i] = (char)hexValue;
                            }

                            value.Append((char)Convert.ToInt32(new string(hex), 16));
                            break;
                        default:
                            throw new FormatException("JSON 包含未知转义字符：" + (char)escaped);
                    }
                }
            }

            private object ParseNumber()
            {
                string number = ReadWord();
                if (number.IndexOf('.') < 0 && number.IndexOf('e') < 0 && number.IndexOf('E') < 0 &&
                    long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
                {
                    return integer;
                }

                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double floating))
                {
                    return floating;
                }

                throw new FormatException("JSON 数值格式非法：" + number);
            }

            private string ReadWord()
            {
                StringBuilder word = new StringBuilder();
                while (reader.Peek() != -1)
                {
                    char c = (char)reader.Peek();
                    if (char.IsWhiteSpace(c) || WordBreak.IndexOf(c) >= 0)
                    {
                        break;
                    }

                    word.Append((char)reader.Read());
                }

                return word.ToString();
            }

            private void EatWhitespace()
            {
                while (reader.Peek() != -1 && char.IsWhiteSpace((char)reader.Peek()))
                {
                    reader.Read();
                }
            }

            private JsonToken NextToken
            {
                get
                {
                    EatWhitespace();
                    int next = reader.Peek();
                    if (next == -1)
                    {
                        return JsonToken.None;
                    }

                    switch ((char)next)
                    {
                        case '{': return JsonToken.ObjectStart;
                        case '}': return JsonToken.ObjectEnd;
                        case '[': return JsonToken.ArrayStart;
                        case ']': return JsonToken.ArrayEnd;
                        case ',': return JsonToken.Comma;
                        case '"': return JsonToken.String;
                        case ':': return JsonToken.Colon;
                        case '-':
                        case '0':
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                        case '8':
                        case '9':
                            return JsonToken.Number;
                    }

                    string word = ReadWord();
                    if (word == "true") return JsonToken.True;
                    if (word == "false") return JsonToken.False;
                    if (word == "null") return JsonToken.Null;
                    throw new FormatException("JSON 包含未知标记：" + word);
                }
            }

            private enum JsonToken
            {
                None,
                ObjectStart,
                ObjectEnd,
                ArrayStart,
                ArrayEnd,
                Colon,
                Comma,
                String,
                Number,
                True,
                False,
                Null,
            }
        }

        [Serializable]
        private sealed class RefDataManifest
        {
            public int formatVersion;
            public string transactionToolVersion;
            public string cltabtoyVersion;
            public string flatcVersion;
            public string generatedCodeSha256;
            public List<RefDataInputManifest> inputs = new List<RefDataInputManifest>();
            public List<RefDataTableManifest> tables = new List<RefDataTableManifest>();
            public RefDataLanguageManifest language;
            public List<RefDataPayloadFileManifest> payloadFiles = new List<RefDataPayloadFileManifest>();
        }

        [Serializable]
        private sealed class RefDataInputManifest
        {
            public string file;
            public string path;
            public string role;
            public string sha256;
        }

        [Serializable]
        private sealed class RefDataPayloadFileManifest
        {
            public string logicalRoot;
            public string relativePath;
            public string sha256;
        }

        [Serializable]
        private sealed class RefDataTableManifest
        {
            public string name;
            public int rowCount;
            public string jsonSha256;
            public string fbsSha256;
            public string bytesSha256;
            public string generatedCSharpSha256;
        }

        [Serializable]
        private sealed class RefDataLanguageManifest
        {
            public int rowCount;
            public string sha256;
        }

        private sealed class PayloadRootDefinition
        {
            public PayloadRootDefinition(string logicalRoot, string directory)
            {
                LogicalRoot = logicalRoot;
                Directory = directory;
            }

            public string LogicalRoot { get; }
            public string Directory { get; }
        }
    }
}
