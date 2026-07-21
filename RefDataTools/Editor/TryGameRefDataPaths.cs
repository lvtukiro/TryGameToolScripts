using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TryGame.RefDataTools.Editor
{
    /// <summary>
    /// TryGame 配表工具的统一路径配置。
    /// </summary>
    internal static class TryGameRefDataPaths
    {
        public const string DefaultExcelRootAssetPath = "RefDataSource/TryGameRefdataRes/v2";
        public const string SourceRepositoryAssetPath = "RefDataSource/TryGameRefdataRes";
        public const string DefaultOutputAssetPath = "RefDataSource/TryGameRefdataRes/v2/Output";
        public const string RuntimeRepositoryAssetPath = "Assets/Resources/TryGameRefdataRes";
        public const string RuntimeOutputAssetPath = "Assets/Resources/TryGameRefdataRes/v2/Output";
        public const string GeneratedRepositoryAssetPath = "Assets/TryGameRefdataScripts";
        public const string DefaultGeneratedTableAssetPath = "Assets/TryGameRefdataScripts/GeneratedTables";
        public const string DefaultGeneratedConfigAssetPath = "Assets/TryGameRefdataScripts/GeneratedConfig";
        public const string DefaultLuaOutputAssetPath = "Assets/TryGameToolScripts/RefDataTools/LuaOutput";
        public const string ToolBinAssetPath = "Assets/TryGameToolScripts/RefDataTools/Bin";
        public const string ManifestFileName = "RefDataManifest.json";
        public const string CommonDefineExcelName = "共用枚举结构体.xlsx";

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

        /// <summary>
        /// 按统一规则收集指定根目录下可直接导出的 Excel。
        /// 共用枚举结构体由其它表引用，不作为独立导出输入。
        /// </summary>
        public static List<string> FindExportableExcelFiles(string rootFullPath)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(rootFullPath) || !Directory.Exists(rootFullPath))
            {
                return result;
            }

            string[] files = Directory.GetFiles(rootFullPath, "*.*", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string extension = Path.GetExtension(file);
                string name = Path.GetFileName(file);
                if ((extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)) &&
                    !name.StartsWith("~$", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals(CommonDefineExcelName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(Path.GetFullPath(file).Replace("\\", "/"));
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }
}
