using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TryGame.RefDataTools.Editor
{
    /// <summary>
    /// cltabtoy 导出进程封装。
    /// </summary>
    internal sealed class TryGameCLTabtoyProcess
    {
        private readonly string exePath;
        private readonly string outputPath;
        private readonly string csharpOutputPath;
        private readonly string luaOutputPath;

        public TryGameCLTabtoyProcess(string outputAssetPath, string csharpOutputAssetPath, string luaOutputAssetPath)
        {
            exePath = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.ToolBinAssetPath + "/cltabtoy.exe");
            outputPath = TryGameRefDataPaths.ToFullPath(outputAssetPath);
            csharpOutputPath = TryGameRefDataPaths.ToFullPath(csharpOutputAssetPath);
            luaOutputPath = TryGameRefDataPaths.ToFullPath(luaOutputAssetPath);
        }

        /// <summary>
        /// 批量导出选中的 Excel 文件。
        /// </summary>
        public bool Export(IReadOnlyList<string> excelFullPaths)
        {
            if (!File.Exists(exePath))
            {
                UnityEngine.Debug.LogError("cltabtoy.exe 不存在：" + exePath);
                return false;
            }

            if (excelFullPaths == null || excelFullPaths.Count == 0)
            {
                UnityEngine.Debug.LogWarning("没有选中的 Excel 配表。");
                return false;
            }

            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(csharpOutputPath);
            Directory.CreateDirectory(luaOutputPath);

            StringBuilder args = new StringBuilder();
            args.Append("-o ").Append(Quote(outputPath)).Append(' ');
            args.Append("-luaoutput ").Append(Quote(luaOutputPath)).Append(' ');
            args.Append("-csharpoutput ").Append(Quote(csharpOutputPath)).Append(' ');

            for (int i = 0; i < excelFullPaths.Count; i++)
            {
                string excelPath = excelFullPaths[i];
                if (!File.Exists(excelPath))
                {
                    UnityEngine.Debug.LogError("Excel 文件不存在：" + excelPath);
                    return false;
                }

                args.Append(Quote(excelPath)).Append(' ');
            }

            args.Append("lua");
            return RunProcess(args.ToString());
        }

        /// <summary>
        /// 执行外部进程。
        /// cltabtoy 启动时会读取 Console buffer，重定向输出会导致句柄无效。
        /// </summary>
        private bool RunProcess(string arguments)
        {
            UnityEngine.Debug.Log("开始导出配表：" + arguments);

            using (Process process = new Process())
            {
                process.StartInfo.FileName = exePath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = true;

                try
                {
                    process.Start();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        UnityEngine.Debug.LogError("cltabtoy 导出失败，ExitCode = " + process.ExitCode);
                        return false;
                    }

                    AssetDatabase.Refresh();
                    UnityEngine.Debug.Log("配表导出完成。");
                    return true;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("cltabtoy 启动失败：" + e);
                    return false;
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
