using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TryGame.HomeDebugTools.Editor
{
    /// <summary>
    /// HomeArea 调试工具共用的 id 文本解析器；只服务编辑器运行时测试按钮，不参与正式剧情解锁流程。
    /// </summary>
    internal static class HomeAreaDebugUnlocks
    {
        public const string DefaultAreaIdsText = "10002";

        private static readonly Regex SplitRegex = new Regex(@"[,\s;，；]+", RegexOptions.Compiled);

        public static bool ParseAreaIds(string text, List<int> result, bool logErrors = true)
        {
            if (result == null || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            bool hasAny = false;
            string[] parts = SplitRegex.Split(text.Trim());
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int areaId) || areaId <= 0)
                {
                    if (logErrors)
                    {
                        Debug.LogError($"[HomeAreaDebugUnlocks] 测试 HomeAreaId 非法：{part}");
                    }

                    continue;
                }

                if (!result.Contains(areaId))
                {
                    result.Add(areaId);
                }

                hasAny = true;
            }

            return hasAny;
        }
    }
}
