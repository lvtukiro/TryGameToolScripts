using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TryGame.RefDataTools.Editor
{
    internal sealed class TryGameCLTabtoyProcess
    {
        private const int ExportTimeoutMilliseconds = 300000;
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

            // 共用枚举/结构体表只作为 cltabtoy 的隐式依赖，不能再次作为普通输入传入。
            // 否则 cltabtoy 会在每个源表处理时重复注册 StructCommonResource 等定义。
            List<string> normalizedExcelPaths = new List<string>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < excelFullPaths.Count; i++)
            {
                string excelPath = excelFullPaths[i];
                if (string.IsNullOrWhiteSpace(excelPath))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(excelPath);
                string fileName = Path.GetFileName(fullPath);
                if (fileName.Equals(
                        TryGameRefDataPaths.CommonDefineExcelName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Debug.LogWarning(
                        "跳过作为 cltabtoy 直接输入的共用枚举结构体表；该表只作为隐式依赖：" +
                        fullPath);
                    continue;
                }

                if (seenPaths.Add(fullPath))
                {
                    normalizedExcelPaths.Add(fullPath);
                }
            }

            if (normalizedExcelPaths.Count == 0)
            {
                UnityEngine.Debug.LogWarning("过滤共用定义和重复路径后没有可导出的 Excel 配表。");
                return false;
            }

            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(csharpOutputPath);
            Directory.CreateDirectory(luaOutputPath);

            StringBuilder args = new StringBuilder();
            args.Append("-o ").Append(Quote(outputPath)).Append(' ');
            args.Append("-luaoutput ").Append(Quote(luaOutputPath)).Append(' ');
            args.Append("-csharpoutput ").Append(Quote(csharpOutputPath)).Append(' ');

            for (int i = 0; i < normalizedExcelPaths.Count; i++)
            {
                string excelPath = normalizedExcelPaths[i];
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

        private bool RunProcess(string arguments)
        {
            UnityEngine.Debug.Log("开始导出配表：" + arguments);

            using (Process process = new Process())
            {
                process.StartInfo.FileName = exePath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                process.StartInfo.CreateNoWindow = false;
                process.StartInfo.UseShellExecute = true;

                try
                {
                    process.Start();
                    if (!process.WaitForExit(ExportTimeoutMilliseconds))
                    {
                        UnityEngine.Debug.LogError($"cltabtoy 导出超时，已终止进程：timeoutMs={ExportTimeoutMilliseconds}, arguments={arguments}");
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception killException)
                        {
                            UnityEngine.Debug.LogError("cltabtoy 超时后终止进程也失败：" + killException);
                        }

                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        if (process.ExitCode == -1073741510)
                        {
                            UnityEngine.Debug.LogError("cltabtoy 导出进程被中断。通常是导出结束后直接关闭了控制台窗口，请在控制台里按任意键退出。");
                            return false;
                        }

                        UnityEngine.Debug.LogError("cltabtoy 导出失败，ExitCode = " + process.ExitCode);
                        return false;
                    }

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
