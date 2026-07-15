using System.IO;
using UnityEngine;

namespace TryGame.RefDataTools.Editor
{
    /// <summary>
    /// TryGame 配表工具的统一路径配置。
    /// </summary>
    internal static class TryGameRefDataPaths
    {
        public const string DefaultExcelRootAssetPath = "Assets/Resources/TryGameRefdataRes/v2";
        public const string DefaultOutputAssetPath = "Assets/Resources/TryGameRefdataRes/v2/Output";
        public const string DefaultGeneratedTableAssetPath = "Assets/TryGameRefdataScripts/GeneratedTables";
        public const string DefaultGeneratedConfigAssetPath = "Assets/TryGameRefdataScripts/GeneratedConfig";
        public const string DefaultLuaOutputAssetPath = "Assets/TryGameToolScripts/RefDataTools/LuaOutput";
        public const string ToolBinAssetPath = "Assets/TryGameToolScripts/RefDataTools/Bin";

        public static string ProjectRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace("\\", "/"); }
        }

        public static string ToFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            return Path.GetFullPath(Path.Combine(ProjectRoot, assetPath)).Replace("\\", "/");
        }

        public static string ToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return string.Empty;
            }

            string normalized = Path.GetFullPath(fullPath).Replace("\\", "/");
            string root = ProjectRoot.TrimEnd('/') + "/";
            return normalized.StartsWith(root) ? normalized.Substring(root.Length) : normalized;
        }
    }
}
