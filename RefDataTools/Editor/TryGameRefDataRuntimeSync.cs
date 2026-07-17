using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TryGame.RefDataTools.Editor
{
    /// <summary>
    /// 把源仓库正式导出的运行时 bytes 同步到 Resources 子仓库。
    /// Excel、JSON、FBS、Java 和普通 txt 不会进入运行时仓库。
    /// </summary>
    internal static class TryGameRefDataRuntimeSync
    {
        private const string RuntimeRepositoryAssetPath = "Assets/Resources/TryGameRefdataRes";

        public static bool PrepareSourceOutputForExport()
        {
            if (!TryResolveRuntimeOutput(out _))
            {
                return false;
            }

            string sourceOutput = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.DefaultOutputAssetPath);
            try
            {
                int removedCount = DeleteSourceRuntimeArtifacts(sourceOutput);
                if (removedCount > 0)
                {
                    Debug.LogWarning($"[TryGameRefDataRuntimeSync] 导出前已清理上次残留的源 bytes：count={removedCount}, source={sourceOutput}");
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TryGameRefDataRuntimeSync] 导出前清理源 bytes 失败，已中止导表：source={sourceOutput}\n{exception}");
                return false;
            }
        }

        public static bool SyncFromSourceOutput()
        {
            string sourceOutput = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.DefaultOutputAssetPath);
            if (!TryResolveRuntimeOutput(out string runtimeOutput))
            {
                return false;
            }

            string sourceFbData = Path.Combine(sourceOutput, "fb_data");
            string runtimeFbData = Path.Combine(runtimeOutput, "fb_data");
            string sourceLanguage = Path.Combine(sourceOutput, "txt_data", "Language.bytes");
            string runtimeLanguage = Path.Combine(runtimeOutput, "txt_data", "Language.bytes");

            string[] sourceBytes = Directory.Exists(sourceFbData)
                ? Directory.GetFiles(sourceFbData, "*.bytes", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            bool hasLanguage = File.Exists(sourceLanguage);
            if (sourceBytes.Length == 0 && !hasLanguage)
            {
                Debug.LogError($"[TryGameRefDataRuntimeSync] 运行时 RefData 同步失败：本次导出没有生成任何 bytes。fbData={sourceFbData}, language={sourceLanguage}");
                return false;
            }

            try
            {
                Directory.CreateDirectory(runtimeFbData);
                Directory.CreateDirectory(Path.GetDirectoryName(runtimeLanguage));

                for (int i = 0; i < sourceBytes.Length; i++)
                {
                    string fileName = Path.GetFileName(sourceBytes[i]);
                    string destination = Path.Combine(runtimeFbData, fileName);
                    CopyAndVerify(sourceBytes[i], destination);
                    CopyMetaIfMissing(sourceBytes[i], destination);
                }

                if (hasLanguage)
                {
                    CopyAndVerify(sourceLanguage, runtimeLanguage);
                    CopyMetaIfMissing(sourceLanguage, runtimeLanguage);
                }

                int removedCount = DeleteSourceRuntimeArtifacts(sourceOutput);
                AssetDatabase.Refresh();
                Debug.Log($"[TryGameRefDataRuntimeSync] 运行时 RefData 增量同步完成：tables={sourceBytes.Length}, language={(hasLanguage ? 1 : 0)}, sourceArtifactsRemoved={removedCount}, target={runtimeOutput}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TryGameRefDataRuntimeSync] 运行时 RefData 同步失败，不能报告完整导出成功：target={runtimeOutput}\n{exception}");
                return false;
            }
        }

        private static bool TryResolveRuntimeOutput(out string runtimeOutput)
        {
            string runtimeRepository = TryGameRefDataPaths.ToFullPath(RuntimeRepositoryAssetPath);
            runtimeOutput = TryGameRefDataPaths.ToFullPath(TryGameRefDataPaths.RuntimeOutputAssetPath);
            string gitMarker = Path.Combine(runtimeRepository, ".git");
            if (Directory.Exists(runtimeRepository) &&
                (Directory.Exists(gitMarker) || File.Exists(gitMarker)))
            {
                return true;
            }

            Debug.LogError(
                $"[TryGameRefDataRuntimeSync] 运行时 RefData 子仓库不存在或未初始化，已中止导表，避免把 bytes 写入普通目录。" +
                $" 请先初始化子模块：repository={runtimeRepository}, gitMarker={gitMarker}, target={runtimeOutput}");
            return false;
        }

        private static int DeleteSourceRuntimeArtifacts(string sourceOutput)
        {
            int removedCount = 0;
            string sourceFbData = Path.Combine(sourceOutput, "fb_data");
            if (Directory.Exists(sourceFbData))
            {
                string[] sourceBytes = Directory.GetFiles(sourceFbData, "*.bytes", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < sourceBytes.Length; i++)
                {
                    removedCount += DeleteIfExists(sourceBytes[i]);
                    removedCount += DeleteIfExists(sourceBytes[i] + ".meta");
                }

                string[] orphanMetas = Directory.GetFiles(sourceFbData, "*.bytes.meta", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < orphanMetas.Length; i++)
                {
                    removedCount += DeleteIfExists(orphanMetas[i]);
                }
            }

            string sourceLanguage = Path.Combine(sourceOutput, "txt_data", "Language.bytes");
            removedCount += DeleteIfExists(sourceLanguage);
            removedCount += DeleteIfExists(sourceLanguage + ".meta");
            return removedCount;
        }

        private static int DeleteIfExists(string path)
        {
            if (!File.Exists(path))
            {
                return 0;
            }

            File.Delete(path);
            return 1;
        }

        private static void CopyAndVerify(string source, string destination)
        {
            string tempPath = destination + ".sync-tmp";
            string backupPath = destination + ".sync-bak";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            File.Copy(source, tempPath, false);
            if (!FilesEqual(source, tempPath))
            {
                throw new IOException("同步临时文件逐字节校验失败：source=" + source + ", temp=" + tempPath);
            }

            if (!File.Exists(destination))
            {
                File.Move(tempPath, destination);
            }
            else
            {
                ReplaceWithRollback(tempPath, destination, backupPath);
            }

            if (!FilesEqual(source, destination))
            {
                throw new IOException("运行时文件提交后逐字节校验失败：source=" + source + ", destination=" + destination);
            }
        }

        private static void ReplaceWithRollback(string tempPath, string destination, string backupPath)
        {
            try
            {
                File.Replace(tempPath, destination, backupPath, true);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceByRename(tempPath, destination, backupPath);
            }
            catch (NotSupportedException)
            {
                ReplaceByRename(tempPath, destination, backupPath);
            }
        }

        private static void ReplaceByRename(string tempPath, string destination, string backupPath)
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(destination, backupPath);
            try
            {
                File.Move(tempPath, destination);
                File.Delete(backupPath);
            }
            catch
            {
                if (!File.Exists(destination) && File.Exists(backupPath))
                {
                    File.Move(backupPath, destination);
                    Debug.LogWarning("[TryGameRefDataRuntimeSync] 新文件提交失败后已恢复旧运行时文件：" + destination);
                }

                throw;
            }
        }

        private static void CopyMetaIfMissing(string source, string destination)
        {
            string sourceMeta = source + ".meta";
            string destinationMeta = destination + ".meta";
            if (!File.Exists(destinationMeta) && File.Exists(sourceMeta))
            {
                File.Copy(sourceMeta, destinationMeta, false);
            }
        }

        private static bool FilesEqual(string leftPath, string rightPath)
        {
            FileInfo leftInfo = new FileInfo(leftPath);
            FileInfo rightInfo = new FileInfo(rightPath);
            if (!leftInfo.Exists || !rightInfo.Exists || leftInfo.Length != rightInfo.Length)
            {
                return false;
            }

            const int bufferSize = 81920;
            byte[] leftBuffer = new byte[bufferSize];
            byte[] rightBuffer = new byte[bufferSize];
            using (FileStream left = File.OpenRead(leftPath))
            using (FileStream right = File.OpenRead(rightPath))
            {
                while (true)
                {
                    int leftRead = left.Read(leftBuffer, 0, leftBuffer.Length);
                    int rightRead = right.Read(rightBuffer, 0, rightBuffer.Length);
                    if (leftRead != rightRead)
                    {
                        return false;
                    }

                    if (leftRead == 0)
                    {
                        return true;
                    }

                    for (int i = 0; i < leftRead; i++)
                    {
                        if (leftBuffer[i] != rightBuffer[i])
                        {
                            return false;
                        }
                    }
                }
            }
        }
    }
}
