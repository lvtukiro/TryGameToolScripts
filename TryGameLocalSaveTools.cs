using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TryGame.Tools.Editor
{
    /// 实现说明：该注释描述当前模块的边界条件和运行时处理。
    /// 开发期本地存档工具。
    /// 只处理 Editor/Player 共用的 Application.persistentDataPath/saves 目录；
    /// 清档采用移动到备份目录的方式，避免测试档被直接物理删除。
    /// 实现说明：该注释描述当前模块的边界条件和运行时处理。
    internal static class TryGameLocalSaveTools
    {
        private const string SaveFolderName = "saves";
        private const string OpenSaveFolderMenu = "TryGame/存档/打开本地存档文件夹";
        private const string ArchiveLocalSavesMenu = "TryGame/存档/归档本地存档";
        /// <summary>
        /// OpenLocalSaveFolder：执行当前模块的OpenLocalSaveFolder逻辑。
        /// </summary>

        [MenuItem(OpenSaveFolderMenu, false, 300)]
        private static void OpenLocalSaveFolder()
        {
            string saveFolder = EnsureSaveFolder();
            EditorUtility.RevealInFinder(saveFolder);
            Debug.Log($"[TryGameLocalSaveTools] 已打开本地存档目录：{saveFolder}");
        }
        /// <summary>
        /// ArchiveLocalSaves：执行当前模块的ArchiveLocalSaves逻辑。
        /// </summary>

        [MenuItem(ArchiveLocalSavesMenu, false, 301)]
        private static void ArchiveLocalSaves()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[TryGameLocalSaveTools] PlayMode 运行中不能清档，请先退出 PlayMode。");
                return;
            }

            string saveFolder = EnsureSaveFolder();
            string[] candidateFiles = CollectRootSaveFiles(saveFolder);
            if (candidateFiles.Length == 0)
            {
                Debug.Log($"[TryGameLocalSaveTools] 当前没有可归档的本地存档文件：{saveFolder}");
                EditorUtility.DisplayDialog("TryGame 清档", "当前没有可清理的本地存档。", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "TryGame 清档",
                $"将把当前存档目录根部的 {candidateFiles.Length} 个存档文件移动到备份目录。\n\n" +
                $"{saveFolder}\n\n" +
                "不会递归处理已有备份目录，也不会直接删除文件。",
                "归档清档",
                "取消");
            if (!confirmed)
            {
                Debug.Log("[TryGameLocalSaveTools] 已取消本地存档归档。");
                return;
            }

            string archiveFolder = ResolveUniqueArchiveFolder(saveFolder);
            try
            {
                Directory.CreateDirectory(archiveFolder);
                int movedCount = 0;
                for (int index = 0; index < candidateFiles.Length; index++)
                {
                    string sourcePath = candidateFiles[index];
                    string fileName = Path.GetFileName(sourcePath);
                    string targetPath = ResolveUniqueFilePath(archiveFolder, fileName);
                    File.Move(sourcePath, targetPath);
                    movedCount++;
                }

                Debug.Log(
                    $"[TryGameLocalSaveTools] 本地存档已归档清理：moved={movedCount}, " +
                    $"saveFolder={saveFolder}, archiveFolder={archiveFolder}");
                EditorUtility.DisplayDialog(
                    "TryGame 清档完成",
                    $"已移动 {movedCount} 个存档文件到备份目录：\n\n{archiveFolder}",
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[TryGameLocalSaveTools] 本地存档归档失败：saveFolder={saveFolder}, " +
                    $"archiveFolder={archiveFolder}\n{exception}");
                EditorUtility.DisplayDialog(
                    "TryGame 清档失败",
                    $"存档归档失败，详情请看 Console。\n\n{exception.Message}",
                    "OK");
            }
        }
        /// <summary>
        /// CanArchiveLocalSaves：执行当前模块的CanArchiveLocalSaves逻辑。
        /// </summary>

        [MenuItem(ArchiveLocalSavesMenu, true)]
        private static bool CanArchiveLocalSaves()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }
        /// <summary>
        /// EnsureSaveFolder：执行当前模块的EnsureSaveFolder逻辑。
        /// </summary>

        private static string EnsureSaveFolder()
        {
            string saveFolder = Path.GetFullPath(
                Path.Combine(Application.persistentDataPath, SaveFolderName));
            Directory.CreateDirectory(saveFolder);
            return saveFolder;
        }
        /// <summary>
        /// CollectRootSaveFiles：执行当前模块的CollectRootSaveFiles逻辑。
        /// </summary>

        private static string[] CollectRootSaveFiles(string saveFolder)
        {
            if (!Directory.Exists(saveFolder))
            {
                return Array.Empty<string>();
            }

            string[] files = Directory.GetFiles(saveFolder, "*", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            int count = 0;
            for (int index = 0; index < files.Length; index++)
            {
                if (IsSaveFileName(Path.GetFileName(files[index])))
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return Array.Empty<string>();
            }

            string[] result = new string[count];
            int writeIndex = 0;
            for (int index = 0; index < files.Length; index++)
            {
                if (IsSaveFileName(Path.GetFileName(files[index])))
                {
                    result[writeIndex] = files[index];
                    writeIndex++;
                }
            }

            return result;
        }
        /// <summary>
        /// IsSaveFileName：执行当前模块的IsSaveFileName逻辑。
        /// </summary>

        private static bool IsSaveFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".json.tmp", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase);
        }
        /// <summary>
        /// ResolveUniqueArchiveFolder：执行当前模块的ResolveUniqueArchiveFolder逻辑。
        /// </summary>

        private static string ResolveUniqueArchiveFolder(string saveFolder)
        {
            string basePath = Path.Combine(
                saveFolder,
                "archive_manual_clear_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string path = basePath;
            int suffix = 1;
            while (Directory.Exists(path))
            {
                path = $"{basePath}_{suffix}";
                suffix++;
            }

            return path;
        }
        /// <summary>
        /// ResolveUniqueFilePath：执行当前模块的ResolveUniqueFilePath逻辑。
        /// </summary>

        private static string ResolveUniqueFilePath(string archiveFolder, string fileName)
        {
            string path = Path.Combine(archiveFolder, fileName);
            if (!File.Exists(path))
            {
                return path;
            }

            string name = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            int suffix = 1;
            do
            {
                path = Path.Combine(archiveFolder, $"{name}_{suffix}{extension}");
                suffix++;
            }
            while (File.Exists(path));

            return path;
        }
    }
}
