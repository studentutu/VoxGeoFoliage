using System;
using System.IO;
using UnityEngine;

namespace SimpleUnityMCP.Editor
{
    public static class EditorFolders
    {
        /// <summary>
        ///   Clear directory under Assets recursively.
        /// </summary>
        public static void CleanAssetDirectoryRecursively(string pathToFolderUnderAssets)
        {
            var assetsFolder = Application.dataPath;

            if (string.IsNullOrWhiteSpace(pathToFolderUnderAssets) || Path.IsPathRooted(pathToFolderUnderAssets))
                throw new ArgumentException("Specify a child directory under Assets.", nameof(pathToFolderUnderAssets));
            var path = Path.GetFullPath(Path.Combine(assetsFolder, pathToFolderUnderAssets));
            var prefix = Path.GetFullPath(assetsFolder) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                throw new ArgumentException("Directory must remain under Assets.", nameof(pathToFolderUnderAssets));

            if (Directory.Exists(path))
                CleanDirectory(new(path));
        }

        /// <summary>
        ///   Clear directory recursively.
        /// </summary>
        public static void CleanDirectory(DirectoryInfo di)
        {
            for (var ancestor = di; ancestor != null; ancestor = ancestor.Parent)
                RejectLink(ancestor);
            ValidateTree(di);
            foreach (var file in di.EnumerateFiles())
                file.Delete();

            foreach (var dir in di.EnumerateDirectories())
                dir.Delete(recursive: true);
        }

        private static void RejectLink(FileSystemInfo entry)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Refusing to clean a linked path: {entry.FullName}");
        }

        private static void ValidateTree(DirectoryInfo directory)
        {
            // Do not traverse a link even during the read-only validation pass.
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                RejectLink(entry);
                if (entry is DirectoryInfo child)
                    ValidateTree(child);
            }
        }
    }
}
