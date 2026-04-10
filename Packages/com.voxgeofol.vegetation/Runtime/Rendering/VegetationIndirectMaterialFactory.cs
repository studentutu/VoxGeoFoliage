#nullable enable

using System;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    internal static class VegetationIndirectMaterialFactory
    {
        private const string CanopyShaderName = "VoxGeoFol/Vegetation/CanopyLit";
        private const string TrunkShaderName = "VoxGeoFol/Vegetation/TrunkLit";
        private const string FarMeshShaderName = "VoxGeoFol/Vegetation/FarMeshLit";
        private const string DepthShaderName = "Hidden/VoxGeoFol/Vegetation/DepthOnly";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        public static Material CreateColorMaterial(VegetationDrawSlot drawSlot)
        {
            if (drawSlot == null)
            {
                throw new ArgumentNullException(nameof(drawSlot));
            }

            Shader shader = drawSlot.MaterialKind switch
            {
                VegetationRenderMaterialKind.Trunk => ResolveShader(TrunkShaderName),
                VegetationRenderMaterialKind.CanopyFoliage => ResolveShader(CanopyShaderName),
                VegetationRenderMaterialKind.CanopyShell => ResolveShader(CanopyShaderName),
                VegetationRenderMaterialKind.FarMesh => ResolveShader(FarMeshShaderName),
                _ => throw new ArgumentOutOfRangeException()
            };

            Material runtimeMaterial = new Material(shader)
            {
                name = $"{drawSlot.DebugLabel}:Color",
                enableInstancing = true,
                hideFlags = HideFlags.HideAndDontSave
            };

            CopySharedSurfaceProperties(drawSlot, runtimeMaterial, drawSlot.MaterialKind == VegetationRenderMaterialKind.CanopyShell);
            return runtimeMaterial;
        }

        public static Material CreateDepthMaterial(VegetationDrawSlot drawSlot)
        {
            if (drawSlot == null)
            {
                throw new ArgumentNullException(nameof(drawSlot));
            }

            Material runtimeMaterial = new Material(ResolveShader(DepthShaderName))
            {
                name = $"{drawSlot.DebugLabel}:Depth",
                enableInstancing = true,
                hideFlags = HideFlags.HideAndDontSave
            };

            CopySharedSurfaceProperties(drawSlot, runtimeMaterial, true);
            return runtimeMaterial;
        }

        public static void DestroyRuntimeMaterial(Material? material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void CopySharedSurfaceProperties(VegetationDrawSlot drawSlot, Material runtimeMaterial, bool suppressBaseTexture)
        {
            Material sourceMaterial = drawSlot.Material;
            runtimeMaterial.CopyPropertiesFromMaterial(sourceMaterial);

            Color baseColor = sourceMaterial.HasProperty("_BaseColor")
                ? sourceMaterial.GetColor("_BaseColor")
                : sourceMaterial.HasProperty("_Color")
                    ? sourceMaterial.GetColor("_Color")
                    : Color.white;
            runtimeMaterial.SetColor(BaseColorId, baseColor);

            Texture? baseTexture = null;
            Vector2 textureScale = Vector2.one;
            Vector2 textureOffset = Vector2.zero;
            if (!suppressBaseTexture)
            {
                if (sourceMaterial.HasProperty("_BaseMap"))
                {
                    baseTexture = sourceMaterial.GetTexture("_BaseMap");
                    textureScale = sourceMaterial.GetTextureScale("_BaseMap");
                    textureOffset = sourceMaterial.GetTextureOffset("_BaseMap");
                }
                else if (sourceMaterial.HasProperty("_MainTex"))
                {
                    baseTexture = sourceMaterial.GetTexture("_MainTex");
                    textureScale = sourceMaterial.GetTextureScale("_MainTex");
                    textureOffset = sourceMaterial.GetTextureOffset("_MainTex");
                }
            }

            runtimeMaterial.SetTexture(BaseMapId, baseTexture != null ? baseTexture : Texture2D.whiteTexture);
            runtimeMaterial.SetTextureScale(BaseMapId, textureScale);
            runtimeMaterial.SetTextureOffset(BaseMapId, textureOffset);

            float smoothness = sourceMaterial.HasProperty("_Smoothness")
                ? sourceMaterial.GetFloat("_Smoothness")
                : sourceMaterial.HasProperty("_Glossiness")
                    ? sourceMaterial.GetFloat("_Glossiness")
                    : 0f;
            runtimeMaterial.SetFloat(SmoothnessId, smoothness);

            float cullMode = sourceMaterial.HasProperty("_Cull")
                ? sourceMaterial.GetFloat("_Cull")
                : 2f;
            runtimeMaterial.SetFloat(CullId, cullMode);
        }

        private static Shader ResolveShader(string shaderName)
        {
            Shader? shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required vegetation shader '{shaderName}' could not be found.");
            }

            return shader;
        }
    }
}
