using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TryGame.Tools.Editor
{
    /// <summary>
    /// 防止 Gameplay 代码重新绕过统一帧入口或直接读取 Unity 时间。
    /// 这是源码结构门禁，不代替 Unity 编译和 Play Mode 行为验证。
    /// </summary>
    [InitializeOnLoad]
    internal static class TryGameGameplayArchitectureValidator
    {
        private const string MenuPath = "TryGame/Validation/校验 Gameplay 帧入口与时间";
        private const string ScriptsRoot = "Assets/TryGameScripts";

        private static readonly string[] TimeGuardRoots =
        {
            "Assets/TryGameScripts/ai_runtime",
            "Assets/TryGameScripts/camera_runtime",
            "Assets/TryGameScripts/furniture_runtime",
            "Assets/TryGameScripts/home_runtime",
            "Assets/TryGameScripts/home_view_runtime/Input",
            "Assets/TryGameScripts/pet_runtime",
            "Assets/TryGameScripts/shop_runtime",
            "Assets/TryGameScripts/world_runtime",
        };

        private static readonly Regex ForbiddenUnityTimeRegex = new Regex(
            @"(?:\b(?:UnityEngine\s*\.\s*)?Time\s*\.\s*" +
            @"(?:timeAsDouble|time|deltaTime|fixedDeltaTime|unscaledTimeAsDouble|unscaledTime|" +
            @"unscaledDeltaTime|realtimeSinceStartupAsDouble|realtimeSinceStartup|timeScale)\b" +
            @"|\bWaitForSeconds(?:Realtime)?\b" +
            @"|\busing\s+(?:static\s+UnityEngine\s*\.\s*Time|" +
            @"[A-Za-z_][A-Za-z0-9_]*\s*=\s*UnityEngine\s*\.\s*Time)\s*;)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex HomeCameraSystemLateUpdateRegex = new Regex(
            @"\bvoid\s+LateUpdate\s*\(\s*\)\s*\{\s*ClampCameraToArea\s*\(\s*\)\s*;\s*\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex FaceCameraLateUpdateDeclarationRegex = new Regex(
            @"\bprivate\s+void\s+LateUpdate\s*\(\s*\)\s*\{",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const string ExpectedFaceCameraLateUpdateNormalized =
            "privatevoidLateUpdate(){" +
            "Cameracamera=targetCamera!=null?targetCamera:CameraMgr.instance.TargetMainCamera;" +
            "if(camera==null){return;}" +
            "Vector3forward=transform.position-camera.transform.position;" +
            "if(lockX){forward.x=0f;}" +
            "if(lockZ){forward.z=0f;}" +
            "if(forward.sqrMagnitude>0.0001f){" +
            "transform.rotation=Quaternion.LookRotation(forward.normalized,camera.transform.up);" +
            "}}";

        static TryGameGameplayArchitectureValidator()
        {
            EditorApplication.delayCall += RunAutomaticValidation;
        }

        [MenuItem(MenuPath)]
        private static void RunFromMenu()
        {
            RunValidation(true);
        }

        private static void RunAutomaticValidation()
        {
            RunValidation(false);
        }

        internal static bool ValidateForBuild()
        {
            return RunValidation(false);
        }

        private static bool RunValidation(bool requestedByUser)
        {
            try
            {
                Dictionary<Type, string> scriptPaths = BuildRuntimeScriptPathMap();
                int frameTypeCount = 0;
                int scannedFileCount = 0;
                int violationCount = ValidateFrameEntrypoints(scriptPaths, ref frameTypeCount);
                violationCount += ValidateUnityTimeUsage(scriptPaths, ref scannedFileCount);

                if (violationCount > 0)
                {
                    Debug.LogError(
                        $"[TryGameGameplayArchitectureValidator] Gameplay 架构校验失败：" +
                        $"violations={violationCount}, gameplayTypes={frameTypeCount}, " +
                        $"timeGuardFiles={scannedFileCount}。请先修复全部 TG-GP 规则再继续验收。");
                    return false;
                }

                if (requestedByUser)
                {
                    Debug.Log(
                        $"[TryGameGameplayArchitectureValidator] Gameplay 架构校验通过：" +
                        $"gameplayTypes={frameTypeCount}, timeGuardFiles={scannedFileCount}。");
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[TryGameGameplayArchitectureValidator] 校验器自身执行失败，" +
                    $"本次结果无效，不能按通过处理：\n{exception}");
                return false;
            }
        }

        private static int ValidateFrameEntrypoints(
            IReadOnlyDictionary<Type, string> scriptPaths,
            ref int checkedTypeCount)
        {
            int violations = 0;
            HashSet<Type> checkedTypes = new HashSet<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<GameplayBehaviour>())
            {
                if (type == null || !checkedTypes.Add(type))
                {
                    continue;
                }

                checkedTypeCount++;
                violations += ValidateDeclaredFrameMethods(type, scriptPaths, false);
            }

            foreach (Type type in TypeCache.GetTypesDerivedFrom<GameplayLateBehaviour>())
            {
                if (type == null || !checkedTypes.Add(type))
                {
                    continue;
                }

                checkedTypeCount++;
                violations += ValidateDeclaredFrameMethods(type, scriptPaths, true);
            }

            foreach (KeyValuePair<Type, string> pair in scriptPaths)
            {
                Type type = pair.Key;
                if (type == null
                    || !typeof(MonoBehaviour).IsAssignableFrom(type)
                    || typeof(GameplayBehaviour).IsAssignableFrom(type)
                    || typeof(GameplayLateBehaviour).IsAssignableFrom(type)
                    || !IsUnderTimeGuardRoot(pair.Value))
                {
                    continue;
                }

                checkedTypeCount++;
                violations += ValidateStandaloneFrameMethods(type, pair.Value);
            }

            return violations;
        }

        private static int ValidateDeclaredFrameMethods(
            Type type,
            IReadOnlyDictionary<Type, string> scriptPaths,
            bool usesLateDriver)
        {
            int violations = 0;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                string methodName = methods[i].Name;
                scriptPaths.TryGetValue(type, out string assetPath);
                if (!IsUnityFrameMethod(methodName)
                    || methods[i].ReturnType != typeof(void)
                    || methods[i].GetParameters().Length != 0
                    || IsExplicitSystemFrameException(
                        type,
                        methodName,
                        usesLateDriver,
                        assetPath))
                {
                    continue;
                }

                int line = FindFrameMethodLine(assetPath, methodName);
                Debug.LogError(
                    $"[TryGameGameplayArchitectureValidator][TG-GP001] Gameplay 类型声明了绕过统一门禁的 " +
                    $"Unity 帧入口：type={type.FullName}, method={methodName}, " +
                    $"file={FormatLocation(assetPath, line)}。" +
                    $"请改写为 OnGameplayUpdate/OnGameplayLateUpdate；清理入口继续放 OnDisable 等系统回调。");
                violations++;
            }

            return violations;
        }

        private static int ValidateStandaloneFrameMethods(Type type, string assetPath)
        {
            int violations = 0;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!IsUnityFrameMethod(method.Name)
                    || method.ReturnType != typeof(void)
                    || method.GetParameters().Length != 0
                    || IsStandaloneSystemFrameException(type, method.Name, assetPath))
                {
                    continue;
                }

                int line = FindFrameMethodLine(assetPath, method.Name);
                Debug.LogError(
                    $"[TryGameGameplayArchitectureValidator][TG-GP001] Gameplay 目录中的普通 " +
                    $"MonoBehaviour 声明了未分类的 Unity 帧入口：type={type.FullName}, " +
                    $"method={method.Name}, file={FormatLocation(assetPath, line)}。" +
                    $"Gameplay 推进请继承统一 Behaviour；纯 System/表现入口需增加带理由的精确白名单。");
                violations++;
            }

            return violations;
        }

        private static bool IsExplicitSystemFrameException(
            Type type,
            string methodName,
            bool usesLateDriver,
            string assetPath)
        {
            // HomeCameraDragController 的 Update 由 GameplayBehaviour 托管；LateUpdate 只做
            // 幂等的边界钳制，暂停期间仍需修正外部/场景切换造成的越界，不推进玩家输入。
            return !usesLateDriver
                && methodName == "LateUpdate"
                && string.Equals(
                    type.FullName,
                    "Game.HomeCameraDragController",
                    StringComparison.Ordinal)
                && HasExpectedHomeCameraSystemLateUpdate(assetPath);
        }

        private static bool IsStandaloneSystemFrameException(
            Type type,
            string methodName,
            string assetPath)
        {
            // FaceCamera 只同步世界 HUD 朝向，不读取输入、不修改业务状态，也不推进计时。
            return methodName == "LateUpdate"
                && string.Equals(type.FullName, "Game.FaceCamera", StringComparison.Ordinal)
                && HasExpectedFaceCameraSystemLateUpdate(assetPath);
        }

        private static bool HasExpectedHomeCameraSystemLateUpdate(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string absolutePath = AssetPathToAbsolute(assetPath);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            string source = StripCommentsAndLiterals(File.ReadAllText(absolutePath));
            return HomeCameraSystemLateUpdateRegex.IsMatch(source);
        }

        private static bool HasExpectedFaceCameraSystemLateUpdate(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string absolutePath = AssetPathToAbsolute(assetPath);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            string source = StripCommentsAndLiterals(File.ReadAllText(absolutePath));
            MatchCollection declarations = FaceCameraLateUpdateDeclarationRegex.Matches(source);
            if (declarations.Count != 1)
            {
                return false;
            }

            Match declaration = declarations[0];
            int openingBrace = source.IndexOf('{', declaration.Index + declaration.Length - 1);
            if (openingBrace < 0
                || !TryFindMatchingBrace(source, openingBrace, out int closingBrace))
            {
                return false;
            }

            string methodSource = source.Substring(
                declaration.Index,
                closingBrace - declaration.Index + 1);
            string normalized = RemoveWhitespace(methodSource);
            return string.Equals(
                normalized,
                ExpectedFaceCameraLateUpdateNormalized,
                StringComparison.Ordinal);
        }

        private static bool TryFindMatchingBrace(
            string source,
            int openingBrace,
            out int closingBrace)
        {
            closingBrace = -1;
            int depth = 0;
            for (int i = openingBrace; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closingBrace = i;
                        return true;
                    }

                    if (depth < 0)
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static string RemoveWhitespace(string source)
        {
            StringBuilder result = new StringBuilder(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                if (!char.IsWhiteSpace(source[i]))
                {
                    result.Append(source[i]);
                }
            }

            return result.ToString();
        }

        private static int ValidateUnityTimeUsage(
            IReadOnlyDictionary<Type, string> scriptPaths,
            ref int scannedFileCount)
        {
            int violations = 0;
            HashSet<string> filesToScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int rootIndex = 0; rootIndex < TimeGuardRoots.Length; rootIndex++)
            {
                string assetRoot = TimeGuardRoots[rootIndex];
                string absoluteRoot = AssetPathToAbsolute(assetRoot);
                if (!Directory.Exists(absoluteRoot))
                {
                    Debug.LogError(
                        $"[TryGameGameplayArchitectureValidator][TG-GP002] 时间门禁目录不存在，" +
                        $"本次扫描不完整：root={assetRoot}");
                    violations++;
                    continue;
                }

                string[] rootFiles = Directory.GetFiles(
                    absoluteRoot,
                    "*.cs",
                    SearchOption.AllDirectories);
                for (int fileIndex = 0; fileIndex < rootFiles.Length; fileIndex++)
                {
                    filesToScan.Add(Path.GetFullPath(rootFiles[fileIndex]));
                }
            }

            // GameplayBehaviour 若以后移动到新目录，仍必须进入时间门禁；不能依赖手写目录
            // 恰好覆盖当前结构。MonoScript 一文件多主类的极端情况仍交给编译/代码审查兜底。
            foreach (KeyValuePair<Type, string> pair in scriptPaths)
            {
                Type type = pair.Key;
                if (type != null
                    && (typeof(GameplayBehaviour).IsAssignableFrom(type)
                        || typeof(GameplayLateBehaviour).IsAssignableFrom(type))
                    && !string.IsNullOrEmpty(pair.Value))
                {
                    filesToScan.Add(AssetPathToAbsolute(pair.Value));
                }
            }

            string[] files = new string[filesToScan.Count];
            filesToScan.CopyTo(files);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                string absolutePath = files[fileIndex];
                scannedFileCount++;
                string source;
                try
                {
                    source = File.ReadAllText(absolutePath);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[TryGameGameplayArchitectureValidator][TG-GP002] 无法读取 Gameplay 源码，" +
                        $"本次扫描不完整：file={AbsoluteToAssetPath(absolutePath)}\n{exception}");
                    violations++;
                    continue;
                }

                string searchable = StripCommentsAndLiterals(source);
                MatchCollection matches = ForbiddenUnityTimeRegex.Matches(searchable);
                for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
                {
                    Match match = matches[matchIndex];
                    int line = GetLineNumber(searchable, match.Index);
                    string assetPath = AbsoluteToAssetPath(absolutePath);
                    Debug.LogError(
                        $"[TryGameGameplayArchitectureValidator][TG-GP002] Gameplay 源码绕过统一时间或暂停入口：" +
                        $"expression={match.Value}, file={FormatLocation(assetPath, line)}。" +
                        $"请显式选择 GameplayTime 的 ScaledGameplay/ActiveGameplay；" +
                        $"System、UI、Loading 或媒体真实时间应留在各自底层来源中。");
                    violations++;
                }
            }

            return violations;
        }

        private static Dictionary<Type, string> BuildRuntimeScriptPathMap()
        {
            Dictionary<Type, string> paths = new Dictionary<Type, string>();
            string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { ScriptsRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                Type type = script != null ? script.GetClass() : null;
                if (type != null && !paths.ContainsKey(type))
                {
                    paths.Add(type, path);
                }
            }

            return paths;
        }

        private static bool IsUnityFrameMethod(string methodName)
        {
            return methodName == "Update"
                || methodName == "LateUpdate"
                || methodName == "FixedUpdate";
        }

        private static bool IsUnderTimeGuardRoot(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string normalized = assetPath.Replace('\\', '/');
            for (int i = 0; i < TimeGuardRoots.Length; i++)
            {
                string root = TimeGuardRoots[i];
                if (normalized.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindFrameMethodLine(string assetPath, string methodName)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return 0;
            }

            string absolutePath = AssetPathToAbsolute(assetPath);
            if (!File.Exists(absolutePath))
            {
                return 0;
            }

            string[] lines = File.ReadAllLines(absolutePath);
            Regex exactMethodRegex = new Regex(
                $@"\b{Regex.Escape(methodName)}\s*\(",
                RegexOptions.CultureInvariant);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(methodName, StringComparison.Ordinal) >= 0
                    && exactMethodRegex.IsMatch(lines[i]))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private static string StripCommentsAndLiterals(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source ?? string.Empty;
            }

            // 先保留换行、将其它位置遮蔽成空格，使诊断行号始终与原文一致。
            // 扫描普通代码时再把代码字符写回；插值字符串只写回 { }
            // 里的表达式，字面量和格式段继续保持遮蔽。
            char[] searchable = new char[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                searchable[i] = source[i] == '\r' || source[i] == '\n'
                    ? source[i]
                    : ' ';
            }

            int index = 0;
            ScanCode(source, searchable, ref index);
            return new string(searchable);
        }

        private static void ScanCode(string source, char[] searchable, ref int index)
        {
            while (index < source.Length)
            {
                if (TrySkipComment(source, ref index)
                    || TrySkipLiteral(source, searchable, ref index))
                {
                    continue;
                }

                searchable[index] = source[index];
                index++;
            }
        }

        private static bool TrySkipComment(string source, ref int index)
        {
            if (index + 1 >= source.Length || source[index] != '/')
            {
                return false;
            }

            char next = source[index + 1];
            if (next == '/')
            {
                index += 2;
                while (index < source.Length
                    && source[index] != '\r'
                    && source[index] != '\n')
                {
                    index++;
                }

                return true;
            }

            if (next != '*')
            {
                return false;
            }

            index += 2;
            while (index < source.Length)
            {
                if (index + 1 < source.Length
                    && source[index] == '*'
                    && source[index + 1] == '/')
                {
                    index += 2;
                    return true;
                }

                index++;
            }

            return true;
        }

        private static bool TrySkipLiteral(
            string source,
            char[] searchable,
            ref int index)
        {
            if (TryGetInterpolatedStringPrefix(
                    source,
                    index,
                    out int prefixLength,
                    out bool verbatim))
            {
                ScanInterpolatedString(
                    source,
                    searchable,
                    ref index,
                    prefixLength,
                    verbatim);
                return true;
            }

            if (source[index] == '@'
                && index + 1 < source.Length
                && source[index + 1] == '"')
            {
                SkipVerbatimString(source, ref index);
                return true;
            }

            if (source[index] == '"')
            {
                SkipEscapedQuotedLiteral(source, ref index, '"');
                return true;
            }

            if (source[index] == '\'')
            {
                SkipEscapedQuotedLiteral(source, ref index, '\'');
                return true;
            }

            return false;
        }

        private static bool TryGetInterpolatedStringPrefix(
            string source,
            int index,
            out int prefixLength,
            out bool verbatim)
        {
            prefixLength = 0;
            verbatim = false;
            if (source[index] == '$')
            {
                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    prefixLength = 2;
                    return true;
                }

                if (index + 2 < source.Length
                    && source[index + 1] == '@'
                    && source[index + 2] == '"')
                {
                    prefixLength = 3;
                    verbatim = true;
                    return true;
                }
            }
            else if (source[index] == '@'
                && index + 2 < source.Length
                && source[index + 1] == '$'
                && source[index + 2] == '"')
            {
                prefixLength = 3;
                verbatim = true;
                return true;
            }

            return false;
        }

        private static void ScanInterpolatedString(
            string source,
            char[] searchable,
            ref int index,
            int prefixLength,
            bool verbatim)
        {
            index += prefixLength;
            while (index < source.Length)
            {
                char current = source[index];
                char next = index + 1 < source.Length ? source[index + 1] : '\0';
                if (!verbatim && current == '\\')
                {
                    index += next == '\0' ? 1 : 2;
                    continue;
                }

                if (current == '"')
                {
                    if (verbatim && next == '"')
                    {
                        index += 2;
                        continue;
                    }

                    index++;
                    return;
                }

                if (current == '{')
                {
                    if (next == '{')
                    {
                        index += 2;
                        continue;
                    }

                    index++;
                    ScanInterpolationExpression(source, searchable, ref index);
                    continue;
                }

                if (current == '}' && next == '}')
                {
                    index += 2;
                    continue;
                }

                index++;
            }
        }

        private static void ScanInterpolationExpression(
            string source,
            char[] searchable,
            ref int index)
        {
            int braceDepth = 1;
            int parenthesisDepth = 0;
            int bracketDepth = 0;
            int conditionalDepth = 0;
            while (index < source.Length)
            {
                if (TrySkipComment(source, ref index)
                    || TrySkipLiteral(source, searchable, ref index))
                {
                    continue;
                }

                char current = source[index];
                char previous = index > 0 ? source[index - 1] : '\0';
                char next = index + 1 < source.Length ? source[index + 1] : '\0';
                if (current == '{')
                {
                    searchable[index] = current;
                    braceDepth++;
                    index++;
                    continue;
                }

                if (current == '}')
                {
                    braceDepth--;
                    if (braceDepth == 0)
                    {
                        index++;
                        return;
                    }

                    searchable[index] = current;
                    index++;
                    continue;
                }

                bool topLevel = braceDepth == 1
                    && parenthesisDepth == 0
                    && bracketDepth == 0;
                if (topLevel && current == '?')
                {
                    bool nullOperator = next == '?' || next == '.' || next == '[' || previous == '?';
                    if (!nullOperator)
                    {
                        conditionalDepth++;
                    }
                }
                else if (topLevel && current == ':')
                {
                    bool aliasQualifier = previous == ':' || next == ':';
                    if (!aliasQualifier && conditionalDepth > 0)
                    {
                        conditionalDepth--;
                    }
                    else if (!aliasQualifier)
                    {
                        // 顶层非三目的冒号开始插值格式段。格式段是文本，
                        // 例如 $"{value:Time.deltaTime}" 不能被当成 Unity 时间读取。
                        index++;
                        SkipInterpolationFormat(source, ref index);
                        return;
                    }
                }

                if (current == '(')
                {
                    parenthesisDepth++;
                }
                else if (current == ')' && parenthesisDepth > 0)
                {
                    parenthesisDepth--;
                }
                else if (current == '[')
                {
                    bracketDepth++;
                }
                else if (current == ']' && bracketDepth > 0)
                {
                    bracketDepth--;
                }

                searchable[index] = current;
                index++;
            }
        }

        private static void SkipInterpolationFormat(string source, ref int index)
        {
            while (index < source.Length)
            {
                if (source[index] == '}')
                {
                    index++;
                    return;
                }

                index++;
            }
        }

        private static void SkipEscapedQuotedLiteral(
            string source,
            ref int index,
            char quote)
        {
            index++;
            while (index < source.Length)
            {
                if (source[index] == '\\' && index + 1 < source.Length)
                {
                    index += 2;
                    continue;
                }

                if (source[index] == quote)
                {
                    index++;
                    return;
                }

                index++;
            }
        }

        private static void SkipVerbatimString(string source, ref int index)
        {
            index += 2;
            while (index < source.Length)
            {
                if (source[index] != '"')
                {
                    index++;
                    continue;
                }

                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                index++;
                return;
            }
        }

        private static int GetLineNumber(string text, int index)
        {
            int line = 1;
            int limit = Math.Min(index, text.Length);
            for (int i = 0; i < limit; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException(
                    $"Cannot resolve project root from Application.dataPath={Application.dataPath}.");
            }

            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static string AbsoluteToAssetPath(string absolutePath)
        {
            string normalizedDataPath = Path.GetFullPath(Application.dataPath)
                .Replace('\\', '/');
            string normalizedPath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            if (!normalizedPath.StartsWith(normalizedDataPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath;
            }

            return "Assets/" + normalizedPath.Substring(normalizedDataPath.Length + 1);
        }

        private static string FormatLocation(string assetPath, int line)
        {
            string path = string.IsNullOrEmpty(assetPath) ? "<unresolved>" : assetPath;
            return line > 0 ? $"{path}:{line}" : path;
        }
    }

    internal sealed class TryGameGameplayArchitectureBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!TryGameGameplayArchitectureValidator.ValidateForBuild())
            {
                throw new BuildFailedException(
                    "TryGame Gameplay architecture validation failed. See TG-GP errors in Console.");
            }
        }
    }
}
