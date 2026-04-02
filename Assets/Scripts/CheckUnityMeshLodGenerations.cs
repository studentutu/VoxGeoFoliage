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
            _generatedMesh = Instantiate(sourceMesh);
            _generatedMesh.name = sourceMesh.name + "_MeshLOD";
            
            MeshLodUtility.GenerateMeshLods(_generatedMesh, MeshLodUtility.LodGenerationFlags.DiscardOddLevels, meshLodLimit);
            
            _meshFilter.sharedMesh = _generatedMesh;// generated mesh now has Lod0,1,2,3,4,5,6 submeshes(discared 1,3,5)
#endif
        }
    }
}