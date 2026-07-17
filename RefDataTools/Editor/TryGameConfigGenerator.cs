using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace TryGame.RefDataTools.Editor
{
    /// <summary>
    /// 根据 cltabtoy 生成的 XxxTable.cs 自动生成 PetConfig / CommonConfig 等读取入口。
    /// </summary>
    internal static class TryGameConfigGenerator
    {
        private static readonly Regex TableRegex = new Regex(
            @"public\s+class\s+(?<table>[A-Za-z_][A-Za-z0-9_]*)Table\s*:\s*IRefData",
            RegexOptions.Compiled);

        private static readonly Regex ByKeyRegex = new Regex(
            @"(?:static\s+public|public\s+static)\s+(?<row>[A-Za-z_][A-Za-z0-9_]*)\s+(?<method>[A-Za-z_][A-Za-z0-9_]*ByKey)\s*\(\s*int\s+_?key",
            RegexOptions.Compiled);

        private static readonly Regex ContainsKeyRegex = new Regex(
            @"(?:static\s+public|public\s+static)\s+bool\s+ContainsKey\s*\(\s*int\s+_?key",
            RegexOptions.Compiled);

        private static readonly Regex GeneralInstanceRegex = new Regex(
            @"public\s+static\s+General\s+Instance",
            RegexOptions.Compiled);

        /// <summary>
        /// 生成默认目录下的 Config 入口代码。
        /// </summary>
        [MenuItem("TryGame/RefData/生成 Config 读取入口")]
        public static void GenerateDefault()
        {
            Generate(
                TryGameRefDataPaths.DefaultGeneratedTableAssetPath,
                TryGameRefDataPaths.DefaultGeneratedConfigAssetPath);
        }

        /// <summary>
        /// 扫描生成表代码，并按“第一个单词为模块名”的规则输出 Config 文件。
        /// </summary>
        public static void Generate(string generatedTableAssetPath, string generatedConfigAssetPath)
        {
            string tableDir = TryGameRefDataPaths.ToFullPath(generatedTableAssetPath);
            string configDir = TryGameRefDataPaths.ToFullPath(generatedConfigAssetPath);

            Directory.CreateDirectory(tableDir);
            Directory.CreateDirectory(configDir);

            Dictionary<string, List<ConfigAccessor>> accessorsByModule = new Dictionary<string, List<ConfigAccessor>>();
            GeneralAccessor generalAccessor = null;
            HashSet<string> expectedTables = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> generatedTables = new HashSet<string>(StringComparer.Ordinal);

            string[] files = Directory.GetFiles(tableDir, "*.cs", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string content = File.ReadAllText(files[i], Encoding.UTF8);
                Match tableMatch = TableRegex.Match(content);
                if (!tableMatch.Success)
                {
                    string fileName = Path.GetFileNameWithoutExtension(files[i]);
                    if (fileName.EndsWith("_define", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    throw new InvalidDataException("生成表代码无法解析 Table 定义，拒绝静默跳过：" + files[i]);
                }

                string tableName = tableMatch.Groups["table"].Value;
                if (IsLanguageTable(tableName))
                {
                    continue;
                }

                expectedTables.Add(tableName);

                if (tableName == "General" && GeneralInstanceRegex.IsMatch(content))
                {
                    generalAccessor = new GeneralAccessor(tableName + "Table");
                    generatedTables.Add(tableName);
                    continue;
                }

                Match byKeyMatch = ByKeyRegex.Match(content);
                if (!byKeyMatch.Success)
                {
                    throw new InvalidDataException("生成表缺少可解析的 int ByKey 入口：table=" + tableName + ", file=" + files[i]);
                }

                string rowType = byKeyMatch.Groups["row"].Value;
                if (IsLanguageTable(rowType))
                {
                    continue;
                }

                if (rowType == "General")
                {
                    generalAccessor = new GeneralAccessor(tableName + "Table");
                    generatedTables.Add(tableName);
                    continue;
                }

                ConfigName configName;
                if (!TryBuildConfigName(rowType, out configName))
                {
                    throw new InvalidDataException("无法拆分配表模块名，拒绝静默跳过：rowType=" + rowType + ", table=" + tableName);
                }

                ConfigAccessor accessor = new ConfigAccessor(
                    configName.ModuleName,
                    configName.MethodName,
                    rowType,
                    tableName + "Table",
                    byKeyMatch.Groups["method"].Value,
                    ContainsKeyRegex.IsMatch(content));

                List<ConfigAccessor> moduleAccessors;
                if (!accessorsByModule.TryGetValue(accessor.ModuleName, out moduleAccessors))
                {
                    moduleAccessors = new List<ConfigAccessor>();
                    accessorsByModule.Add(accessor.ModuleName, moduleAccessors);
                }

                moduleAccessors.Add(accessor);
                generatedTables.Add(tableName);
            }

            if (!expectedTables.SetEquals(generatedTables))
            {
                List<string> missing = new List<string>();
                foreach (string tableName in expectedTables)
                {
                    if (!generatedTables.Contains(tableName))
                    {
                        missing.Add(tableName);
                    }
                }

                throw new InvalidDataException("提交前校验失败，以下预期表没有生成 Config 入口：" + string.Join(", ", missing));
            }

            GenerateAndCommitConfigs(configDir, accessorsByModule, generalAccessor);

            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("Config 读取入口生成完成：" + generatedConfigAssetPath);
        }

        private static void GenerateAndCommitConfigs(string configDir, Dictionary<string, List<ConfigAccessor>> accessorsByModule, GeneralAccessor generalAccessor)
        {
            string parentDir = Directory.GetParent(configDir)?.FullName ?? configDir;
            string transactionId = Guid.NewGuid().ToString("N");
            string stagingDir = Path.Combine(parentDir, ".TryGameConfigGenerator.staging." + transactionId);
            string backupDir = Path.Combine(parentDir, ".TryGameConfigGenerator.backup." + transactionId);
            bool transactionSucceeded = false;
            try
            {
                Directory.CreateDirectory(stagingDir);
                WriteModuleConfigs(stagingDir, accessorsByModule);
                if (generalAccessor != null)
                {
                    WriteGeneralConfig(stagingDir, generalAccessor);
                }

                if (Directory.GetFiles(stagingDir, "*.Generated.cs", SearchOption.TopDirectoryOnly).Length == 0)
                {
                    throw new InvalidOperationException("暂存目录没有生成任何 .Generated.cs 文件，拒绝覆盖旧配置入口。");
                }

                Directory.CreateDirectory(backupDir);
                CopyGeneratedFiles(configDir, backupDir, false);
                CommitGeneratedFiles(configDir, stagingDir, backupDir);
                transactionSucceeded = true;
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError("[TryGameConfigGenerator] Config 入口事务生成失败，旧文件应保持或已尝试恢复。 staging=" + stagingDir + ", backup=" + backupDir + "\n" + exception);
                throw;
            }
            finally
            {
                TryDeleteTransactionDirectory(stagingDir, "暂存目录");
                if (transactionSucceeded)
                {
                    TryDeleteTransactionDirectory(backupDir, "备份目录");
                }
                else if (Directory.Exists(backupDir))
                {
                    UnityEngine.Debug.LogWarning("[TryGameConfigGenerator] 事务失败，已保留备份目录供人工核对：" + backupDir);
                }
            }
        }

        private static void CommitGeneratedFiles(string configDir, string stagingDir, string backupDir)
        {
            try
            {
                string[] stagedFiles = Directory.GetFiles(stagingDir, "*.Generated.cs", SearchOption.TopDirectoryOnly);
                HashSet<string> stagedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < stagedFiles.Length; i++)
                {
                    string fileName = Path.GetFileName(stagedFiles[i]);
                    stagedNames.Add(fileName);
                    File.Copy(stagedFiles[i], Path.Combine(configDir, fileName), true);
                }

                string[] currentFiles = Directory.GetFiles(configDir, "*.Generated.cs", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < currentFiles.Length; i++)
                {
                    if (!stagedNames.Contains(Path.GetFileName(currentFiles[i])))
                    {
                        File.Delete(currentFiles[i]);
                        DeleteMetaIfExists(currentFiles[i]);
                    }
                }
            }
            catch (Exception commitException)
            {
                UnityEngine.Debug.LogError("[TryGameConfigGenerator] 提交新生成文件失败，开始恢复旧文件：configDir=" + configDir + "\n" + commitException);
                if (!TryRestoreGeneratedFiles(configDir, backupDir))
                {
                    throw new IOException("提交失败，且旧生成文件恢复不完整。请从版本控制恢复 Config 目录。", commitException);
                }

                throw;
            }
        }

        private static bool TryRestoreGeneratedFiles(string configDir, string backupDir)
        {
            bool success = true;
            try
            {
                CleanGeneratedConfigs(configDir);
            }
            catch (Exception cleanException)
            {
                success = false;
                UnityEngine.Debug.LogError("[TryGameConfigGenerator] 恢复前清理部分新文件失败：configDir=" + configDir + "\n" + cleanException);
            }

            try
            {
                CopyGeneratedFiles(backupDir, configDir, true);
            }
            catch (Exception restoreException)
            {
                success = false;
                UnityEngine.Debug.LogError("[TryGameConfigGenerator] 恢复旧生成文件失败：backupDir=" + backupDir + ", configDir=" + configDir + "\n" + restoreException);
            }

            if (success)
            {
                UnityEngine.Debug.LogWarning("[TryGameConfigGenerator] 新文件提交失败后已恢复旧 Config 生成文件。");
            }

            return success;
        }

        private static void CopyGeneratedFiles(string sourceDir, string destinationDir, bool overwrite)
        {
            string[] files = Directory.GetFiles(sourceDir, "*.Generated.cs", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string destinationFile = Path.Combine(destinationDir, Path.GetFileName(files[i]));
                File.Copy(files[i], destinationFile, overwrite);
                string sourceMeta = files[i] + ".meta";
                if (File.Exists(sourceMeta))
                {
                    File.Copy(sourceMeta, destinationFile + ".meta", overwrite);
                }
            }
        }

        private static void TryDeleteTransactionDirectory(string path, string label)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception cleanupException)
            {
                UnityEngine.Debug.LogError("[TryGameConfigGenerator] 清理" + label + "失败，请手动删除：" + path + "\n" + cleanupException);
            }
        }

        private static void WriteModuleConfigs(string configDir, Dictionary<string, List<ConfigAccessor>> accessorsByModule)
        {
            foreach (KeyValuePair<string, List<ConfigAccessor>> pair in accessorsByModule)
            {
                pair.Value.Sort((a, b) => string.CompareOrdinal(a.MethodName, b.MethodName));

                string className = pair.Key + "Config";
                string path = Path.Combine(configDir, className + ".Generated.cs");
                StringBuilder sb = new StringBuilder();
                HashSet<string> usedMethods = new HashSet<string>();

                AppendGeneratedHeader(sb);
                sb.AppendLine("namespace RefData");
                sb.AppendLine("{");
                sb.AppendLine("    /// <summary>");
                sb.AppendLine("    /// 自动生成的 " + pair.Key + " 模块配表读取入口。");
                sb.AppendLine("    /// </summary>");
                sb.AppendLine("    public static partial class " + className);
                sb.AppendLine("    {");

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    ConfigAccessor accessor = pair.Value[i];
                    if (!usedMethods.Add(accessor.MethodName))
                    {
                        throw new InvalidDataException(className + " 中存在重复方法名，拒绝生成不完整入口：" + accessor.MethodName);
                    }

                    sb.AppendLine("        /// <summary>");
                    sb.AppendLine("        /// 按 id 读取 " + accessor.RowType + "。");
                    sb.AppendLine("        /// </summary>");
                    sb.AppendLine("        public static " + accessor.RowType + " Get" + accessor.MethodName + "(int id)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            return " + accessor.TableClass + "." + accessor.ByKeyMethod + "(id);");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                    if (accessor.HasContainsKey)
                    {
                        sb.AppendLine("        /// <summary>");
                        sb.AppendLine("        /// 判断 " + accessor.RowType + " 是否存在。");
                        sb.AppendLine("        /// </summary>");
                        sb.AppendLine("        public static bool Has" + accessor.MethodName + "(int id)");
                        sb.AppendLine("        {");
                        sb.AppendLine("            return " + accessor.TableClass + ".ContainsKey(id);");
                        sb.AppendLine("        }");
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("    }");
                sb.AppendLine("}");

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
        }

        private static void WriteGeneralConfig(string configDir, GeneralAccessor accessor)
        {
            string path = Path.Combine(configDir, "GeneralConfig.Generated.cs");
            StringBuilder sb = new StringBuilder();

            AppendGeneratedHeader(sb);
            sb.AppendLine("namespace RefData");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// General 表是单例全局参数表，统一从 Data 读取。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static partial class GeneralConfig");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 当前 General 配置数据。");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static General Data");
            sb.AppendLine("        {");
            sb.AppendLine("            get { return " + accessor.TableClass + ".Instance; }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void CleanGeneratedConfigs(string configDir)
        {
            string[] files = Directory.GetFiles(configDir, "*.Generated.cs", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                File.Delete(files[i]);
                DeleteMetaIfExists(files[i]);
            }
        }

        private static void DeleteMetaIfExists(string assetFilePath)
        {
            string metaPath = assetFilePath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        private static void AppendGeneratedHeader(StringBuilder sb)
        {
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//  This file is generated by TryGameConfigGenerator. Do not modify by hand.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
        }

        private static bool TryBuildConfigName(string rowType, out ConfigName configName)
        {
            string[] words = SplitPascalWords(rowType);
            if (words.Length == 0)
            {
                configName = default(ConfigName);
                return false;
            }

            string moduleName = words[0];
            string methodName = words.Length == 1 ? words[0] : JoinWords(words, 1, words.Length - 1);
            configName = new ConfigName(moduleName, methodName);
            return true;
        }

        private static string JoinWords(string[] words, int startIndex, int count)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                sb.Append(words[startIndex + i]);
            }

            return sb.ToString();
        }

        private static string[] SplitPascalWords(string value)
        {
            MatchCollection matches = Regex.Matches(value, @"[A-Z]+(?=[A-Z][a-z]|$)|[A-Z]?[a-z]+|\d+");
            List<string> words = new List<string>(matches.Count);
            for (int i = 0; i < matches.Count; i++)
            {
                words.Add(matches[i].Value);
            }

            return words.ToArray();
        }

        private static bool IsLanguageTable(string name)
        {
            return name.StartsWith("Language", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("HotLanguage", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("PLanguage", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ConfigName
        {
            public readonly string ModuleName;
            public readonly string MethodName;

            public ConfigName(string moduleName, string methodName)
            {
                ModuleName = moduleName;
                MethodName = methodName;
            }
        }

        private sealed class ConfigAccessor
        {
            public readonly string ModuleName;
            public readonly string MethodName;
            public readonly string RowType;
            public readonly string TableClass;
            public readonly string ByKeyMethod;
            public readonly bool HasContainsKey;

            public ConfigAccessor(
                string moduleName,
                string methodName,
                string rowType,
                string tableClass,
                string byKeyMethod,
                bool hasContainsKey)
            {
                ModuleName = moduleName;
                MethodName = methodName;
                RowType = rowType;
                TableClass = tableClass;
                ByKeyMethod = byKeyMethod;
                HasContainsKey = hasContainsKey;
            }
        }

        private sealed class GeneralAccessor
        {
            public readonly string TableClass;

            public GeneralAccessor(string tableClass)
            {
                TableClass = tableClass;
            }
        }
    }
}
