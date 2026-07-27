using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TryGame.Tools.Editor
{
    /// <summary>
    /// 构建前校验会进入 Player 的 TryGameBuildRes Prefab。
    /// 必须从磁盘枚举文件：导入失败的 Prefab 可能不会出现在 AssetDatabase.FindAssets 结果中。
    /// </summary>
    internal sealed class TryGamePlayerPrefabBuildValidator : IPreprocessBuildWithReport
    {
        private const string PlayerResourceRoot = "Assets/Resources/TryGameBuildRes";
        private const string ValidationMenu =
            "TryGame/Validation/Validate Player Prefabs";

        private static readonly Regex PrefabDocumentHeaderRegex = new Regex(
            @"^--- !u!\d+ &(?<id>-?\d+)(?: stripped)?\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex PrefabFileIdReferenceRegex = new Regex(
            @"\bfileID:\s*(?<id>-?\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (ValidateForBuild(logSuccess: true))
            {
                return;
            }

            throw new BuildFailedException(
                "TryGame Player Prefab validation failed. " +
                "See TryGamePlayerPrefabBuildValidator errors in Console.");
        }

        [MenuItem(ValidationMenu, false, 230)]
        private static void ValidateFromMenu()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[TryGamePlayerPrefabBuildValidator] 同步刷新 AssetDatabase 失败，" +
                    $"无法执行可信的 Player Prefab 校验：\n{exception}");
                return;
            }

            ValidateForBuild(logSuccess: true);
        }

        internal static bool ValidateForBuild(bool logSuccess)
        {
            List<string> failures = new List<string>();
            List<string> prefabPaths = CollectPrefabPaths(failures);

            for (int index = 0; index < prefabPaths.Count; index++)
            {
                string assetPath = prefabPaths[index];
                ValidatePrefabYaml(assetPath, failures);
                ValidateImportedPrefab(assetPath, failures);
            }

            for (int index = 0; index < failures.Count; index++)
            {
                Debug.LogError(
                    $"[TryGamePlayerPrefabBuildValidator] {failures[index]}");
            }

            if (failures.Count > 0)
            {
                Debug.LogError(
                    "[TryGamePlayerPrefabBuildValidator] Player Prefab 构建前校验失败，" +
                    $"prefabs={prefabPaths.Count}, errors={failures.Count}；已阻止本次构建。");
                return false;
            }

            if (logSuccess)
            {
                Debug.Log(
                    "[TryGamePlayerPrefabBuildValidator] Player Prefab 构建前校验通过：" +
                    $"prefabs={prefabPaths.Count}。");
            }

            return true;
        }

        private static List<string> CollectPrefabPaths(List<string> failures)
        {
            List<string> result = new List<string>();
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                failures.Add(
                    $"无法从 Application.dataPath 解析项目根目录：{Application.dataPath}");
                return result;
            }

            string absoluteRoot = Path.GetFullPath(
                Path.Combine(projectRoot, PlayerResourceRoot));
            if (!Directory.Exists(absoluteRoot))
            {
                failures.Add($"Player 资源根目录不存在：{PlayerResourceRoot}");
                return result;
            }

            try
            {
                string[] absolutePaths = Directory.GetFiles(
                    absoluteRoot,
                    "*.prefab",
                    SearchOption.AllDirectories);
                Array.Sort(absolutePaths, StringComparer.OrdinalIgnoreCase);

                for (int index = 0; index < absolutePaths.Length; index++)
                {
                    string normalizedAbsolutePath = Path.GetFullPath(absolutePaths[index])
                        .Replace('\\', '/');
                    string normalizedRoot = absoluteRoot.Replace('\\', '/').TrimEnd('/');
                    string relativePath = normalizedAbsolutePath
                        .Substring(normalizedRoot.Length)
                        .TrimStart('/');
                    result.Add(PlayerResourceRoot + "/" + relativePath);
                }
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"枚举 Player Prefab 失败：root={PlayerResourceRoot}\n{exception}");
                return result;
            }

            if (result.Count == 0)
            {
                failures.Add($"Player 资源目录中没有任何 Prefab：{PlayerResourceRoot}");
            }

            return result;
        }

        private static void ValidatePrefabYaml(
            string assetPath,
            List<string> failures)
        {
            string absolutePath = AssetPathToAbsolute(assetPath);
            Dictionary<long, int> firstLineByFileId = new Dictionary<long, int>();
            int documentCount = 0;
            int lineNumber = 0;

            try
            {
                foreach (string line in File.ReadLines(absolutePath))
                {
                    lineNumber++;
                    MatchCollection referenceMatches =
                        PrefabFileIdReferenceRegex.Matches(line);
                    for (int index = 0; index < referenceMatches.Count; index++)
                    {
                        string rawReferenceId =
                            referenceMatches[index].Groups["id"].Value;
                        if (!long.TryParse(
                                rawReferenceId,
                                NumberStyles.AllowLeadingSign,
                                CultureInfo.InvariantCulture,
                                out _))
                        {
                            failures.Add(
                                $"Prefab fileID 引用超出 Int64 范围：" +
                                $"{assetPath}:{lineNumber}, fileID={rawReferenceId}");
                        }
                    }

                    Match match = PrefabDocumentHeaderRegex.Match(line);
                    if (!match.Success)
                    {
                        continue;
                    }

                    documentCount++;
                    string rawFileId = match.Groups["id"].Value;
                    if (!long.TryParse(
                            rawFileId,
                            NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture,
                            out long fileId))
                    {
                        failures.Add(
                            $"Prefab fileID 超出 Int64 范围：{assetPath}:{lineNumber}, " +
                            $"fileID={rawFileId}");
                        continue;
                    }

                    if (firstLineByFileId.TryGetValue(fileId, out int firstLine))
                    {
                        failures.Add(
                            $"Prefab document fileID 重复：{assetPath}:{lineNumber}, " +
                            $"fileID={fileId}, firstLine={firstLine}");
                        continue;
                    }

                    firstLineByFileId.Add(fileId, lineNumber);
                }
            }
            catch (Exception exception)
            {
                failures.Add($"读取 Prefab YAML 失败：{assetPath}\n{exception}");
                return;
            }

            if (documentCount == 0)
            {
                failures.Add(
                    $"Prefab 没有可识别的 YAML document header：{assetPath}。" +
                    "项目要求 Prefab 使用 Force Text 序列化。");
            }
        }

        private static void ValidateImportedPrefab(
            string assetPath,
            List<string> failures)
        {
            GameObject prefab;
            try
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            }
            catch (Exception exception)
            {
                failures.Add($"AssetDatabase 加载 Prefab 异常：{assetPath}\n{exception}");
                return;
            }

            if (prefab == null)
            {
                failures.Add(
                    $"AssetDatabase 无法加载 Prefab：{assetPath}。" +
                    "资源可能导入失败，不能继续构建 Player。");
                return;
            }

            try
            {
                Transform[] nodes =
                    prefab.GetComponentsInChildren<Transform>(includeInactive: true);
                for (int index = 0; index < nodes.Length; index++)
                {
                    Transform node = nodes[index];
                    int missingScriptCount =
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            node.gameObject);
                    if (missingScriptCount <= 0)
                    {
                        continue;
                    }

                    failures.Add(
                        $"Prefab 存在 Missing Script：{assetPath}, " +
                        $"node={GetHierarchyPath(node)}, count={missingScriptCount}");
                }
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"检查 Prefab 导入内容异常：{assetPath}\n{exception}");
            }
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty, assetPath));
        }

        private static string GetHierarchyPath(Transform node)
        {
            List<string> names = new List<string>();
            Transform current = node;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }
    }
}
