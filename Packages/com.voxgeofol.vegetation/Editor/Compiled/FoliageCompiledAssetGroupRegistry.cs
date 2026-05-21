#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Deterministic compiler-side registry for render asset groups.
    /// </summary>
    internal sealed class FoliageCompiledAssetGroupRegistry
    {
        private readonly int maxAssetGroups;
        private readonly List<FoliageAssetGroup> assetGroups = new List<FoliageAssetGroup>();
        private readonly Dictionary<string, int> assetGroupIndices = new Dictionary<string, int>(StringComparer.Ordinal);

        public FoliageCompiledAssetGroupRegistry(int maxAssetGroups)
        {
            this.maxAssetGroups = Mathf.Max(1, maxAssetGroups);
        }

        public int Count => assetGroups.Count;

        public int Register(Mesh mesh, Material material, VegetationRenderMaterialKind materialKind, string debugLabel)
        {
            string key = $"{mesh.GetInstanceID()}|{material.GetInstanceID()}|{(int)materialKind}";
            if (assetGroupIndices.TryGetValue(key, out int existingIndex))
            {
                return existingIndex;
            }

            if (assetGroups.Count >= maxAssetGroups)
            {
                throw new InvalidOperationException(
                    $"Foliage compiler exceeded max asset groups ({maxAssetGroups}) while adding '{debugLabel}'.");
            }

            int groupIndex = assetGroups.Count;
            assetGroups.Add(new FoliageAssetGroup(
                mesh,
                material,
                materialKind,
                FindPass(material, "UniversalForward", "UniversalForwardOnly", "SRPDefaultUnlit"),
                FindPass(material, "DepthOnly", "DepthNormals"),
                FindPass(material, "ShadowCaster"),
                debugLabel));
            assetGroupIndices.Add(key, groupIndex);
            return groupIndex;
        }

        public FoliageAssetGroup[] ToArray()
        {
            return assetGroups.ToArray();
        }

        private static int FindPass(Material material, params string[] passNames)
        {
            for (int i = 0; i < passNames.Length; i++)
            {
                int passIndex = material.FindPass(passNames[i]);
                if (passIndex >= 0)
                {
                    return passIndex;
                }
            }

            return 0;
        }
    }
}
