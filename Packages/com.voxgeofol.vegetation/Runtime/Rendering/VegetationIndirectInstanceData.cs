#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    internal struct VegetationIndirectInstanceData
    {
        public Matrix4x4 ObjectToWorld;
        public Matrix4x4 WorldToObject;
        public uint PackedLeafTint;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
    }
}
