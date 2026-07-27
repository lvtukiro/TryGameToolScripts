using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TryGame.Tools.Editor
{
    /// <summary>
    /// TryGame Windows Player 的统一开发构建入口。
    /// Smoke 入口不会使用 AutoRunPlayer，因为需要显式传入窗口验证命令行参数。
    /// </summary>
    internal static class TryGameWindowsBuildMenu
    {
        private const string DevelopmentBuildMenu =
            "TryGame/Build/Windows x64/Development Build";

        private const string WindowSmokeBuildMenu =
            "TryGame/Build/Windows x64/Build And Run Window Smoke";

        private const string OutputRelativePath = "Build/Windows-x64/TryAiGameTmp.exe";
        private const string WindowSmokeArgument = "--trygame-window-smoke";

        [MenuItem(DevelopmentBuildMenu, false, 100)]
        private static void BuildDevelopmentPlayer()
        {
            BuildWindowsPlayer(runWindowSmoke: false);
        }

        [MenuItem(WindowSmokeBuildMenu, false, 101)]
        private static void BuildAndRunWindowSmoke()
        {
            BuildWindowsPlayer(runWindowSmoke: true);
        }

        [MenuItem(DevelopmentBuildMenu, true)]
        [MenuItem(WindowSmokeBuildMenu, true)]
        private static bool CanBuildWindowsPlayer()
        {
            return !EditorApplication.isCompiling &&
                   !EditorApplication.isPlayingOrWillChangePlaymode &&
                   !BuildPipeline.isBuildingPlayer;
        }

        private static void BuildWindowsPlayer(bool runWindowSmoke)
        {
            try
            {
                if (!TryPrepareBuild(out string[] scenes, out string outputPath))
                {
                    return;
                }

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.Development |
                              BuildOptions.AllowDebugging |
                              BuildOptions.CleanBuildCache,
                };

                Debug.Log(
                    $"[TryGameWindowsBuild] 开始 Windows x64 Development Build：" +
                    $"scenes={scenes.Length}, output={outputPath}, " +
                    $"smoke={runWindowSmoke}, cleanCache=True");

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (!TryValidateBuildReport(report, outputPath))
                {
                    return;
                }

                BuildSummary summary = report.summary;
                Debug.Log(
                    $"[TryGameWindowsBuild] 构建成功：output={outputPath}, " +
                    $"size={summary.totalSize} bytes, duration={summary.totalTime}, " +
                    $"warnings={summary.totalWarnings}, errors={summary.totalErrors}");

                if (runWindowSmoke)
                {
                    RunWindowSmokePlayer(outputPath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[TryGameWindowsBuild] Windows x64 Development Build 执行异常，" +
                    $"本次构建不能按成功处理：\n{exception}");
            }
        }

        private static bool TryPrepareBuild(out string[] scenes, out string outputPath)
        {
            scenes = Array.Empty<string>();
            outputPath = string.Empty;

            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError(
                    "[TryGameWindowsBuild] Unity 正在编译或进入 Play Mode，" +
                    "不能开始 Player 构建。请等待状态稳定后重试。");
                return false;
            }

            if (BuildPipeline.isBuildingPlayer)
            {
                Debug.LogError(
                    "[TryGameWindowsBuild] 已有 Player 构建正在执行，拒绝重复启动构建。");
                return false;
            }

            if (!BuildPipeline.IsBuildTargetSupported(
                    BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64))
            {
                Debug.LogError(
                    "[TryGameWindowsBuild] 当前 Unity 未安装或未启用 Windows Build Support，" +
                    "无法构建 Windows x64 Player。请通过 Unity Hub 安装对应模块。");
                return false;
            }

            if (!TryCollectEnabledScenes(out scenes))
            {
                return false;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64);
                if (!switched ||
                    EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                {
                    Debug.LogError(
                        "[TryGameWindowsBuild] 切换到 Windows x64 Build Target 失败，" +
                        "已中止本次构建。");
                    return false;
                }
            }

            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[TryGameWindowsBuild] 构建前同步刷新 AssetDatabase 失败，已中止构建：\n" +
                    exception);
                return false;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            outputPath = Path.GetFullPath(Path.Combine(projectRoot, OutputRelativePath));
            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                Debug.LogError(
                    $"[TryGameWindowsBuild] 无法解析构建输出目录：output={outputPath}");
                return false;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[TryGameWindowsBuild] 创建构建输出目录失败：directory={outputDirectory}\n" +
                    exception);
                return false;
            }

            return true;
        }

        private static bool TryCollectEnabledScenes(out string[] scenes)
        {
            List<string> enabledScenePaths = new List<string>();
            EditorBuildSettingsScene[] configuredScenes = EditorBuildSettings.scenes;
            for (int index = 0; index < configuredScenes.Length; index++)
            {
                EditorBuildSettingsScene scene = configuredScenes[index];
                if (!scene.enabled)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(scene.path))
                {
                    Debug.LogError(
                        $"[TryGameWindowsBuild] Build Settings 中第 {index + 1} 个启用场景路径为空，" +
                        "已中止构建。");
                    scenes = Array.Empty<string>();
                    return false;
                }

                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) == null)
                {
                    Debug.LogError(
                        $"[TryGameWindowsBuild] Build Settings 中的启用场景不存在或无法读取：" +
                        $"index={index}, path={scene.path}");
                    scenes = Array.Empty<string>();
                    return false;
                }

                enabledScenePaths.Add(scene.path);
            }

            if (enabledScenePaths.Count == 0)
            {
                Debug.LogError(
                    "[TryGameWindowsBuild] Build Settings 没有启用场景，无法构建 Player。");
                scenes = Array.Empty<string>();
                return false;
            }

            scenes = enabledScenePaths.ToArray();
            return true;
        }

        private static bool TryValidateBuildReport(BuildReport report, string outputPath)
        {
            if (report == null)
            {
                Debug.LogError(
                    "[TryGameWindowsBuild] BuildPipeline 未返回 BuildReport，" +
                    "本次构建不能按成功处理。");
                return false;
            }

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"[TryGameWindowsBuild] 构建未成功：result={summary.result}, " +
                    $"warnings={summary.totalWarnings}, errors={summary.totalErrors}, " +
                    $"duration={summary.totalTime}, output={outputPath}");
                return false;
            }

            if (!File.Exists(outputPath))
            {
                Debug.LogError(
                    "[TryGameWindowsBuild] BuildReport 显示成功，但目标 exe 不存在，" +
                    $"拒绝按成功处理或启动 smoke：output={outputPath}");
                return false;
            }

            return true;
        }

        private static void RunWindowSmokePlayer(string outputPath)
        {
            string workingDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                Debug.LogError(
                    $"[TryGameWindowsBuild] 无法解析 smoke Player 工作目录：output={outputPath}");
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = outputPath,
                    Arguments = WindowSmokeArgument,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Debug.LogError(
                            "[TryGameWindowsBuild] 构建成功，但启动 Window Smoke Player 失败：" +
                            $"Process.Start 未返回进程，output={outputPath}, " +
                            $"argument={WindowSmokeArgument}");
                        return;
                    }

                    Debug.Log(
                        "[TryGameWindowsBuild] Window Smoke Player 已启动：" +
                        $"pid={process.Id}, output={outputPath}, argument={WindowSmokeArgument}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[TryGameWindowsBuild] 构建成功，但启动 Window Smoke Player 异常：" +
                    $"output={outputPath}, argument={WindowSmokeArgument}\n{exception}");
            }
        }
    }
}
