using System;
using System.IO;
using UnityEngine;

namespace Scripts.Editor
{
    public static class EditorFolders
    {
        public const string AddressableAssetDataFolder = "AddressableAssetsData";

        // From official addressables ChangeLog.md:
        // The addressables_content_state.bin is built into a platform specific folder within Assets/AddressableAssetsData/.
        // We recommend deleting the addressables_content_state.bin in Assets/AddressableAssetsData to avoid future confusion.
        public static void ClearAddressableAssetFolder()
        {
            CleanFileUnderDirectory(AddressableAssetDataFolder, "addressables_content_state.bin");
        }

        private static void CleanFileUnderDirectory(string pathToFolderUnderAssets, string addressablesContentStateBin)
        {
            var assetsFolder = Application.dataPath;

            var path = Path.Combine(assetsFolder, pathToFolderUnderAssets);
            path = path.Replace("\\", "/");

            if (Directory.Exists(path))
            {
                var directoryInfo = new DirectoryInfo(path);
                foreach (var file in directoryInfo.EnumerateFiles())
                    if (file.Name.StartsWith(addressablesContentStateBin, StringComparison.InvariantCultureIgnoreCase))
                        file.Delete();
            }
        }

        /// <summary>
        ///   Clear directory under Assets recursively.
        /// </summary>
        public static void CleanAssetDirectoryRecursively(string pathToFolderUnderAssets)
        {
            var assetsFolder = Application.dataPath;

            var path = Path.Combine(assetsFolder, pathToFolderUnderAssets);
            path = path.Replace("\\", "/");

            if (Directory.Exists(path))
                CleanDirectory(new(path));
        }

        /// <summary>
        ///   Clear directory recursively.
        /// </summary>
        public static void CleanDirectory(DirectoryInfo di)
        {
            foreach (var file in di.EnumerateFiles())
                file.Delete();

            foreach (var dir in di.EnumerateDirectories())
                dir.Delete(recursive: true);
        }
    }
}