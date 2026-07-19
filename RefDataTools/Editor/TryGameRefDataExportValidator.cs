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
        private const int ManifestFormatVersion = 1;
        private const string TransactionToolVersion = "1.1.0";
        private const string RefDataRuntimeProjectFileName = "TryGame.RefData.Runtime.csproj";

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

        public static bool ValidateManifestCopies(string phase, params string[] manifestDirectories)
        {
            try
            {
                if (manifestDirectories == null || manifestDirectories.Length != 3)
                {
                    throw new InvalidDataException(
                        $"manifest 校验必须传入三个仓库目录：actual={(manifestDirectories == null ? -1 : manifestDirectories.Length)}");
                }

                string[] paths = new string[manifestDirectories.Length];
                string[] hashes = new string[manifestDirectories.Length];
                byte[][] contents = new byte[manifestDirectories.Length][];
                for (int i = 0; i < manifestDirectories.Length; i++)
                {
                    paths[i] = Path.Combine(manifestDirectories[i], TryGameRefDataPaths.ManifestFileName);
                    EnsureFile(paths[i], $"{phase} manifest[{i}]");
                    contents[i] = File.ReadAllBytes(paths[i]);
                    hashes[i] = ComputeSha256(paths[i]);
                }

                for (int i = 1; i < contents.Length; i++)
                {
                    if (contents[0].Length != contents[i].Length ||
                        !string.Equals(hashes[0], hashes[i], StringComparison.OrdinalIgnoreCase) ||
                        !contents[0].SequenceEqual(contents[i]))
                    {
                        throw new InvalidDataException(
                            $"三仓库 manifest 内容不一致：phase={phase}, " +
                            $"referencePath={paths[0]}, referenceLength={contents[0].Length}, referenceSha256={hashes[0]}, " +
                            $"differentPath={paths[i]}, differentLength={contents[i].Length}, differentSha256={hashes[i]}");
                    }
                }

                Debug.Log(
                    $"[TryGameRefDataExportValidator] 三仓库 manifest 自动校验通过：phase={phase}, " +
                    $"length={contents[0].Length}, sha256={hashes[0]}\n" +
                    string.Join("\n", paths));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[TryGameRefDataExportValidator] 三仓库 manifest 自动校验失败：phase={phase}\n{exception}");
                return false;
            }
        }

        public static bool ValidateAndWriteManifest(
            string transactionRoot,
            IReadOnlyList<string> exportedExcelFullPaths,
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
                Directory.CreateDirectory(decodeDirectory);
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
                manifest.inputs = BuildInputManifest(exportedExcelFullPaths, expectedInputHashes);
                manifest.generatedCodeSha256 = ComputeDirectorySha256(
                    stagedGeneratedTables,
                    stagedGeneratedConfig);

                manifestJson = TryGameRefDataTextNormalizer.NormalizeLineEndings(JsonUtility.ToJson(manifest, true)) + "\n";
                ValidateManifestSerialization(manifestJson, manifest.inputs.Count, manifest.tables.Count);
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
            IReadOnlyList<string> exportedExcelFullPaths,
            IReadOnlyDictionary<string, string> expectedInputHashes)
        {
            if (exportedExcelFullPaths == null || exportedExcelFullPaths.Count == 0)
            {
                throw new InvalidDataException("manifest 没有任何本次导出的 Excel 输入。");
            }

            if (expectedInputHashes == null || expectedInputHashes.Count != exportedExcelFullPaths.Count)
            {
                throw new InvalidDataException(
                    $"导出前 Excel 哈希数量不匹配：inputs={exportedExcelFullPaths.Count}, " +
                    $"hashes={(expectedInputHashes == null ? -1 : expectedInputHashes.Count)}");
            }

            string[] files = exportedExcelFullPaths
                .Select(Path.GetFullPath)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

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
                    throw new InvalidDataException("本次导出包含重复 Excel 输入：" + path);
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
                    sha256 = currentSha256,
                });
            }

            return result;
        }

        private static void ValidateManifestSerialization(string manifestJson, int expectedInputCount, int expectedTableCount)
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
        }

        private static int GetListCount(object value)
        {
            return value is IList list ? list.Count : -1;
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
        }

        [Serializable]
        private sealed class RefDataInputManifest
        {
            public string file;
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
    }
}
