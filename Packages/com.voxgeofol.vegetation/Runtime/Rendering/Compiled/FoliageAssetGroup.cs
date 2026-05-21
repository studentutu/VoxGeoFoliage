#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// One immutable mesh/material command family emitted by the foliage compiler.
    /// </summary>
    [Serializable]
    public sealed class FoliageAssetGroup
    {
        [SerializeField] private Mesh? mesh;
        [SerializeField] private Material? material;
        [SerializeField] private VegetationRenderMaterialKind materialKind;
        [SerializeField] private int forwardPassIndex;
        [SerializeField] private int depthPassIndex;
        [SerializeField] private int shadowPassIndex;
        [SerializeField] private string debugLabel = string.Empty;

        public FoliageAssetGroup(
            Mesh mesh,
            Material material,
            VegetationRenderMaterialKind materialKind,
            int forwardPassIndex,
            int depthPassIndex,
            int shadowPassIndex,
            string debugLabel)
        {
            this.mesh = mesh != null ? mesh : throw new ArgumentNullException(nameof(mesh));
            this.material = material != null ? material : throw new ArgumentNullException(nameof(material));
            this.materialKind = materialKind;
            this.forwardPassIndex = forwardPassIndex;
            this.depthPassIndex = depthPassIndex;
            this.shadowPassIndex = shadowPassIndex;
            this.debugLabel = debugLabel ?? string.Empty;
        }

        public Mesh Mesh => mesh != null
            ? mesh
            : throw new InvalidOperationException("Compiled foliage asset group is missing its mesh reference.");

        public Material Material => material != null
            ? material
            : throw new InvalidOperationException("Compiled foliage asset group is missing its material reference.");

        public VegetationRenderMaterialKind MaterialKind => materialKind;

        public int ForwardPassIndex => forwardPassIndex;

        public int DepthPassIndex => depthPassIndex;

        public int ShadowPassIndex => shadowPassIndex;

        public string DebugLabel => debugLabel;
    }
}
