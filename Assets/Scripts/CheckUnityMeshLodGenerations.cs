using JetBrains.Annotations;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DefaultNamespace
{
    /// <summary>
    ///     It wields vertices, but removes part of the geometry. Uv/normals are avaraged which results in incorrect lighting.
    ///     Is not suitable for voxel simplifications.
    /// </summary>
    public class CheckUnityMeshLodGenerations : MonoBehaviour
    {
        [SerializeField] private MeshFilter _meshFilter;
        [SerializeField] private Mesh sourceMesh;
        [SerializeField] private int meshLodLimit = -1;

        public bool Regenerate = false;
        [CanBeNull] private Mesh _generatedMesh;

        private void OnValidate()
        {
            if (Regenerate)
            {
                Regenerate = false;
                GeneratedMesh();
            }
        }

        private void GeneratedMesh()
        {
#if UNITY_EDITOR
            if (sourceMesh == null)
            {
                Debug.LogError("CheckUnityMeshLodGenerations requires a source mesh.", this);
                return;
            }

            if (_meshFilter == null)
            {
                Debug.LogError("CheckUnityMeshLodGenerations requires a target MeshFilter.", this);
                return;
            }

            if (!CanGenerateMeshLod(sourceMesh))
            {
                Debug.LogWarning(
                    $"Skipping MeshLodUtility generation for {sourceMesh.name}: mesh has no readable index buffer data.",
                    this);
                _meshFilter.sharedMesh = null;
                return;
            }

            _generatedMesh = CreateMeshLodInputMesh(sourceMesh, sourceMesh.name + "_MeshLOD");

            try
            {
                MeshLodUtility.GenerateMeshLods(_generatedMesh, MeshLodUtility.LodGenerationFlags.DiscardOddLevels, meshLodLimit);
                _meshFilter.sharedMesh = _generatedMesh;// generated mesh now has Lod0,1,2,3,4,5,6 submeshes(discared 1,3,5)
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    $"Skipping MeshLodUtility generation for {sourceMesh.name}: {exception.Message}",
                    this);
                DestroyImmediate(_generatedMesh);
                _generatedMesh = null;
                _meshFilter.sharedMesh = null;
            }
#endif
        }

        private static Mesh CreateMeshLodInputMesh(Mesh sourceMesh, string meshName)
        {
            Mesh lodInputMesh = new Mesh
            {
                name = meshName,
                indexFormat = sourceMesh.indexFormat,
                subMeshCount = sourceMesh.subMeshCount
            };

            lodInputMesh.vertices = sourceMesh.vertices;

            Vector3[] normals = sourceMesh.normals;
            if (normals.Length == sourceMesh.vertexCount)
            {
                lodInputMesh.normals = normals;
            }

            Vector4[] tangents = sourceMesh.tangents;
            if (tangents.Length == sourceMesh.vertexCount)
            {
                lodInputMesh.tangents = tangents;
            }

            Vector2[] uv = sourceMesh.uv;
            if (uv.Length == sourceMesh.vertexCount)
            {
                lodInputMesh.uv = uv;
            }

            Vector2[] uv2 = sourceMesh.uv2;
            if (uv2.Length == sourceMesh.vertexCount)
            {
                lodInputMesh.uv2 = uv2;
            }

            Color[] colors = sourceMesh.colors;
            if (colors.Length == sourceMesh.vertexCount)
            {
                lodInputMesh.colors = colors;
            }

            for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                lodInputMesh.SetTriangles(sourceMesh.GetTriangles(subMeshIndex), subMeshIndex, true);
            }

            lodInputMesh.RecalculateBounds();
            if (lodInputMesh.normals.Length != lodInputMesh.vertexCount)
            {
                lodInputMesh.RecalculateNormals();
            }

            return lodInputMesh;
        }

        private static bool CanGenerateMeshLod(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable || mesh.vertexCount <= 0 || mesh.subMeshCount <= 0)
            {
                return false;
            }

            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                if (mesh.GetIndexCount(subMeshIndex) > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
