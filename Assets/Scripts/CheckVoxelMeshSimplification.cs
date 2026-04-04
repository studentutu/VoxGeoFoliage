#nullable enable

using System;
using UnityEngine;
using VoxelSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DefaultNamespace
{
    /// <summary>
    /// Editor-time demo component for comparing source mesh, raw voxel surface, reduced voxel surface, and Unity Mesh LOD output.
    /// </summary>
    public sealed class CheckVoxelMeshSimplification : MonoBehaviour
    {
        [SerializeField] private Mesh? sourceMesh;
        [SerializeField] private int voxelResolution = 24;
        [SerializeField] private MeshFilter? sourceMeshFilter;
        [SerializeField] private MeshFilter? rawVoxelMeshFilter;
        [SerializeField] private MeshFilter? reducedVoxelMeshFilter;
        [SerializeField] private MeshFilter? unityLodMeshFilter;
        [SerializeField] private int unityMeshLodLimit = 4;

        public bool Regenerate;

        private Mesh? _rawVoxelMesh;
        private Mesh? _reducedVoxelMesh;
        private Mesh? _unityLodMesh;

        private void OnValidate()
        {
            voxelResolution = Mathf.Max(2, voxelResolution);
            unityMeshLodLimit = Mathf.Clamp(unityMeshLodLimit, 0, 6);

            if (!Regenerate)
            {
                return;
            }

            Regenerate = false;
            RegenerateDemoMeshes();
        }

        private void OnDisable()
        {
            ClearGeneratedMeshes();
        }

        [ContextMenu("Regenerate Demo Meshes")]
        public void RegenerateDemoMeshes()
        {
            try
            {
                GenerateDemoMeshes();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Failed to regenerate mesh simplification demo for {name}: {exception.Message}",
                    this);
            }
        }

        private void GenerateDemoMeshes()
        {
            // Range: sourceMesh must be readable and voxelResolution must be >= 2. Condition: one shared voxel volume is reused to emit raw and reduced surfaces from identical occupancy. Output: assigned MeshFilters receive comparable demo meshes and the console logs triangle deltas.
            if (sourceMesh == null)
            {
                Debug.LogError("CheckVoxelMeshSimplification requires a source mesh.", this);
                return;
            }

            ClearGeneratedMeshes();

            CpuVoxelVolume volume = CPUVoxelizer.VoxelizeToVolume(sourceMesh, voxelResolution);
            _rawVoxelMesh = CpuVoxelSurfaceMeshBuilder.BuildSurfaceMesh(
                volume,
                null,
                $"{sourceMesh.name}_VoxelRaw",
                CpuVoxelSurfaceBuildOptions.Raw);
            _reducedVoxelMesh = CpuVoxelSurfaceMeshBuilder.BuildSurfaceMesh(
                volume,
                null,
                $"{sourceMesh.name}_VoxelReduced",
                CpuVoxelSurfaceBuildOptions.Reduced);

            if (sourceMeshFilter != null)
            {
                sourceMeshFilter.sharedMesh = sourceMesh;
            }

            if (rawVoxelMeshFilter != null)
            {
                rawVoxelMeshFilter.sharedMesh = _rawVoxelMesh;
            }

            if (reducedVoxelMeshFilter != null)
            {
                reducedVoxelMeshFilter.sharedMesh = _reducedVoxelMesh;
            }

#if UNITY_EDITOR
            if (unityLodMeshFilter != null)
            {
                _unityLodMesh = BuildUnityMeshLodMesh(sourceMesh, $"{sourceMesh.name}_UnityLod", unityMeshLodLimit);
                unityLodMeshFilter.sharedMesh = _unityLodMesh;
            }
#else
            if (unityLodMeshFilter != null)
            {
                unityLodMeshFilter.sharedMesh = null;
            }
#endif

            Debug.Log(
                $"Mesh simplification demo regenerated for {sourceMesh.name}. " +
                $"Source={GetTriangleCount(sourceMesh)} tris, " +
                $"RawVoxel({voxelResolution})={GetTriangleCount(_rawVoxelMesh)} tris, " +
                $"ReducedVoxel({voxelResolution})={GetTriangleCount(_reducedVoxelMesh)} tris, " +
                $"UnityLOD({unityMeshLodLimit})={GetTriangleCount(_unityLodMesh)} tris.",
                this);
        }

        private void ClearGeneratedMeshes()
        {
            if (rawVoxelMeshFilter != null && rawVoxelMeshFilter.sharedMesh == _rawVoxelMesh)
            {
                rawVoxelMeshFilter.sharedMesh = null;
            }

            if (reducedVoxelMeshFilter != null && reducedVoxelMeshFilter.sharedMesh == _reducedVoxelMesh)
            {
                reducedVoxelMeshFilter.sharedMesh = null;
            }

            if (unityLodMeshFilter != null && unityLodMeshFilter.sharedMesh == _unityLodMesh)
            {
                unityLodMeshFilter.sharedMesh = null;
            }

            DestroyGeneratedMesh(ref _rawVoxelMesh);
            DestroyGeneratedMesh(ref _reducedVoxelMesh);
            DestroyGeneratedMesh(ref _unityLodMesh);
        }

        private static void DestroyGeneratedMesh(ref Mesh? mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }

            mesh = null;
        }

        private static int GetTriangleCount(Mesh? mesh)
        {
            return mesh == null ? 0 : checked((int)(mesh.GetIndexCount(0) / 3L));
        }

#if UNITY_EDITOR
        private static Mesh? BuildUnityMeshLodMesh(Mesh sourceMesh, string meshName, int meshLodLimit)
        {
            Mesh lodMesh = Instantiate(sourceMesh);
            lodMesh.name = $"{meshName}_MeshLod{meshLodLimit}";
            MeshLodUtility.GenerateMeshLods(
                lodMesh,
                MeshLodUtility.LodGenerationFlags.DiscardOddLevels,
                meshLodLimit);

            int subMeshIndex = GetLastNonEmptySubMeshIndex(lodMesh);
            if (subMeshIndex < 0)
            {
                DestroyImmediate(lodMesh);
                return null;
            }

            Mesh extractedMesh = ExtractSubMesh(lodMesh, subMeshIndex, meshName);
            DestroyImmediate(lodMesh);
            return extractedMesh;
        }

        private static int GetLastNonEmptySubMeshIndex(Mesh mesh)
        {
            for (int subMeshIndex = mesh.subMeshCount - 1; subMeshIndex >= 0; subMeshIndex--)
            {
                if (mesh.GetIndexCount(subMeshIndex) > 0)
                {
                    return subMeshIndex;
                }
            }

            return -1;
        }

        private static Mesh ExtractSubMesh(Mesh sourceMesh, int subMeshIndex, string meshName)
        {
            Mesh extractedMesh = new Mesh
            {
                name = meshName,
                indexFormat = sourceMesh.indexFormat
            };

            extractedMesh.vertices = sourceMesh.vertices;

            Vector3[] normals = sourceMesh.normals;
            if (normals.Length == sourceMesh.vertexCount)
            {
                extractedMesh.normals = normals;
            }

            Vector2[] uv = sourceMesh.uv;
            if (uv.Length == sourceMesh.vertexCount)
            {
                extractedMesh.uv = uv;
            }

            Vector2[] uv2 = sourceMesh.uv2;
            if (uv2.Length == sourceMesh.vertexCount)
            {
                extractedMesh.uv2 = uv2;
            }

            Color[] colors = sourceMesh.colors;
            if (colors.Length == sourceMesh.vertexCount)
            {
                extractedMesh.colors = colors;
            }

            extractedMesh.SetTriangles(sourceMesh.GetTriangles(subMeshIndex), 0, true);
            extractedMesh.RecalculateBounds();
            if (extractedMesh.GetIndexCount(0) > 0 && extractedMesh.normals.Length != extractedMesh.vertexCount)
            {
                extractedMesh.RecalculateNormals();
            }

            return extractedMesh;
        }
#endif
    }
}
