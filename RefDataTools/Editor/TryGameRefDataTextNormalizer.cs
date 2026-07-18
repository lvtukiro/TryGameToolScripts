using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TryGame.RefDataTools.Editor
{
    /// <summary>
    /// 把导表生成的文本统一为 LF，同时保留文件既有 UTF-8 BOM 约定，避免制造格式型 Git 差异。
    /// </summary>
    internal static class TryGameRefDataTextNormalizer
    {
        private static readonly UTF8Encoding Utf8NoBomStrict = new UTF8Encoding(false, true);

        public static int NormalizeDirectory(string root, params string[] extensions)
        {
            if (!Directory.Exists(root))
            {
                return 0;
            }

            HashSet<string> allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < extensions.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(extensions[i]))
                {
                    string extension = extensions[i].StartsWith(".", StringComparison.Ordinal)
                        ? extensions[i]
                        : "." + extensions[i];
                    allowedExtensions.Add(extension);
                }
            }

            int changedCount = 0;
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                if (allowedExtensions.Contains(Path.GetExtension(files[i])) && NormalizeFile(files[i]))
                {
                    changedCount++;
                }
            }

            return changedCount;
        }

        public static int NormalizeDirectoryAgainstBaseline(
            string generatedRoot,
            string baselineRoot,
            params string[] extensions)
        {
            return NormalizeDirectoryAgainstBaselineInternal(
                generatedRoot,
                baselineRoot,
                false,
                extensions);
        }

        public static int NormalizeCodeDirectoryAgainstBaseline(
            string generatedRoot,
            string baselineRoot)
        {
            return NormalizeDirectoryAgainstBaselineInternal(
                generatedRoot,
                baselineRoot,
                true,
                new[] { ".cs" });
        }

        private static int NormalizeDirectoryAgainstBaselineInternal(
            string generatedRoot,
            string baselineRoot,
            bool ignoreBlankLineDifferences,
            params string[] extensions)
        {
            if (!Directory.Exists(generatedRoot))
            {
                return 0;
            }

            HashSet<string> allowedExtensions = BuildExtensionSet(extensions);
            string fullGeneratedRoot = Path.GetFullPath(generatedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullBaselineRoot = Path.GetFullPath(baselineRoot);
            int changedCount = 0;
            string[] files = Directory.GetFiles(fullGeneratedRoot, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                if (!allowedExtensions.Contains(Path.GetExtension(files[i])))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(files[i]);
                string relativePath = fullPath.Substring(fullGeneratedRoot.Length);
                string baselinePath = Path.Combine(fullBaselineRoot, relativePath);
                if (NormalizeFileAgainstBaseline(fullPath, baselinePath, ignoreBlankLineDifferences))
                {
                    changedCount++;
                }
            }

            return changedCount;
        }

        public static bool NormalizeFile(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            byte[] originalBytes = File.ReadAllBytes(path);
            byte[] normalizedBytes = BuildNormalizedBytes(originalBytes);
            if (BytesEqual(originalBytes, normalizedBytes))
            {
                return false;
            }

            File.WriteAllBytes(path, normalizedBytes);
            return true;
        }

        public static bool NormalizeFileAgainstBaseline(string generatedPath, string baselinePath)
        {
            return NormalizeFileAgainstBaseline(generatedPath, baselinePath, false);
        }

        private static bool NormalizeFileAgainstBaseline(
            string generatedPath,
            string baselinePath,
            bool ignoreBlankLineDifferences)
        {
            if (!File.Exists(generatedPath))
            {
                return false;
            }

            byte[] originalGenerated = File.ReadAllBytes(generatedPath);
            byte[] normalizedGenerated = BuildNormalizedBytes(originalGenerated);
            byte[] desired = normalizedGenerated;
            if (File.Exists(baselinePath))
            {
                byte[] normalizedBaseline = BuildNormalizedBytes(File.ReadAllBytes(baselinePath));
                if (EquivalentIgnoringTrailingWhitespace(
                    DecodeUtf8(normalizedGenerated),
                    DecodeUtf8(normalizedBaseline),
                    ignoreBlankLineDifferences))
                {
                    desired = normalizedBaseline;
                }
            }

            if (BytesEqual(originalGenerated, desired))
            {
                return false;
            }

            File.WriteAllBytes(generatedPath, desired);
            return true;
        }

        public static string NormalizeLineEndings(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder(content.Length);
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c != '\r')
                {
                    result.Append(c);
                    continue;
                }

                while (i + 1 < content.Length && content[i + 1] == '\r')
                {
                    i++;
                }

                if (i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                result.Append('\n');
            }

            return result.ToString();
        }

        private static HashSet<string> BuildExtensionSet(string[] extensions)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < extensions.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(extensions[i]))
                {
                    result.Add(extensions[i].StartsWith(".", StringComparison.Ordinal)
                        ? extensions[i]
                        : "." + extensions[i]);
                }
            }

            return result;
        }

        private static byte[] BuildNormalizedBytes(byte[] originalBytes)
        {
            bool hasBom = HasUtf8Bom(originalBytes);
            int offset = hasBom ? 3 : 0;
            string content = Utf8NoBomStrict.GetString(originalBytes, offset, originalBytes.Length - offset);
            byte[] body = Utf8NoBomStrict.GetBytes(NormalizeLineEndings(content));
            if (!hasBom)
            {
                return body;
            }

            byte[] result = new byte[body.Length + 3];
            result[0] = 0xEF;
            result[1] = 0xBB;
            result[2] = 0xBF;
            Buffer.BlockCopy(body, 0, result, 3, body.Length);
            return result;
        }

        private static string DecodeUtf8(byte[] bytes)
        {
            int offset = HasUtf8Bom(bytes) ? 3 : 0;
            return Utf8NoBomStrict.GetString(bytes, offset, bytes.Length - offset);
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        private static bool EquivalentIgnoringTrailingWhitespace(
            string left,
            string right,
            bool ignoreBlankLineDifferences)
        {
            List<string> leftLines = BuildComparisonLines(left, ignoreBlankLineDifferences);
            List<string> rightLines = BuildComparisonLines(right, ignoreBlankLineDifferences);
            if (leftLines.Count != rightLines.Count)
            {
                return false;
            }

            for (int i = 0; i < leftLines.Count; i++)
            {
                if (!string.Equals(leftLines[i], rightLines[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> BuildComparisonLines(string content, bool ignoreBlankLines)
        {
            string[] lines = NormalizeLineEndings(content).Split('\n');
            List<string> result = new List<string>(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd(' ', '\t');
                if (!ignoreBlankLines || line.Length > 0)
                {
                    result.Add(line);
                }
            }

            return result;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
