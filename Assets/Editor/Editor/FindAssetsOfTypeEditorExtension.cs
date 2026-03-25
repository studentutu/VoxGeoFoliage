using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Scripts.Editor
{
    public class FindAssetsOfTypeEditorExtension
    {
        /// <summary>
        /// This method is used to get all assets from a type.
        /// </summary>
        /// <param name="additionOption">For example add name to find with name</param>
        /// <param name="path">Find within path.</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IEnumerable<T> FindAssetsWithType<T>(string additionOption = "", string path = "")
            where T : Object
        {
            List<T> assets = new();
            string[] paths = string.IsNullOrEmpty(path) ? new string[0] : new[] {path};

            foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name} {additionOption}", paths))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var assetsAtPath = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                assets.Add(assetsAtPath);
            }

            return assets;
        }
    }
}