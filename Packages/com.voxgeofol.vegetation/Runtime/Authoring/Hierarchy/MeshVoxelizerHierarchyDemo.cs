#nullable enable

using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace MeshVoxelizerProject
{
    /// <summary>
    /// Sample component that visualizes the hierarchy builder with 80/16/6 voxel levels per node.
    /// </summary>
    public sealed class MeshVoxelizerHierarchyDemo : MonoBehaviour
    {
        [SerializeField] private ShellBakeSettings settings;
        [SerializeField] private MeshFilter? fromMesh;
        [SerializeField] private Material? previewMaterial;
        [SerializeField] private bool showShellL0 = true;
        [SerializeField] private bool showShellL1 = true;
        [SerializeField] private bool showShellL2 = true;
        [SerializeField] private bool generateHierarchy;
        [SerializeField] private bool clearGeneratedHierarchy;

        private const string GeneratedRootName = "VoxelHierarchy";
        private MeshVoxelizerHierarchyNode[] generatedNodes = System.Array.Empty<MeshVoxelizerHierarchyNode>();

        private void OnValidate()
        {
            if (clearGeneratedHierarchy)
            {
                clearGeneratedHierarchy = false;
                ClearHierarchy();
            }

            if (generateHierarchy)
            {
                generateHierarchy = false;
                GenerateHierarchy();
            }
        }

        private void OnDisable()
        {
            ClearHierarchy();
        }

        /// <summary>
        /// [INTEGRATION] Generates a nested sample hierarchy under this GameObject.
        /// </summary>
        public void GenerateHierarchy()
        {
            // Range: fromMesh or a child MeshFilter must exist. Condition: the hierarchy builder splits L0 surface voxels into octants and reuses the same node bounds for L1/L2. Output: a generated child hierarchy used for visual inspection.
            ClearHierarchy();

            MeshFilter? sourceFilter = ResolveSourceFilter();
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
            {
                Debug.LogWarning("MeshVoxelizerHierarchyDemo requires a source MeshFilter with a readable sharedMesh.", this);
                return;
            }

            generatedNodes = MeshVoxelizerHierarchyBuilder.BuildHierarchy(
                sourceFilter.sharedMesh,
                settings.VoxelResolutionL0,
                settings.VoxelResolutionL1,
                settings.VoxelResolutionL2,
                settings.MaxOctreeDepth,
                settings.MinimumSurfaceVoxelCountToSplit);

            GameObject root = new GameObject(GeneratedRootName);
            root.transform.SetParent(transform, false);

            for (int i = 0; i < generatedNodes.Length; i++)
            {
                if (generatedNodes[i].ParentIndex == -1)
                {
                    CreateNodeRecursive(root.transform, generatedNodes, i);
                }
            }
        }

        private MeshFilter? ResolveSourceFilter()
        {
            if (fromMesh != null)
            {
                return fromMesh;
            }

            MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] != null && filters[i].transform != transform)
                {
                    return filters[i];
                }
            }

            return GetComponent<MeshFilter>();
        }

        private void CreateNodeRecursive(Transform parentTransform, MeshVoxelizerHierarchyNode[] nodes, int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.Length)
            {
                return;
            }

            MeshVoxelizerHierarchyNode node = nodes[nodeIndex];
            GameObject nodeObject = new GameObject($"Node_{nodeIndex:D3}_Depth{node.Depth}");
            nodeObject.transform.SetParent(parentTransform, false);
            nodeObject.transform.localPosition = Vector3.zero;
            nodeObject.transform.localRotation = Quaternion.identity;
            nodeObject.transform.localScale = Vector3.one;

            if (showShellL0 && node.ShellL0Mesh != null)
            {
                CreateMeshChild(nodeObject.transform, $"ShellL0_{settings.VoxelResolutionL0}", node.ShellL0Mesh);
            }

            if (showShellL1 && node.ShellL1Mesh != null)
            {
                CreateMeshChild(nodeObject.transform, $"ShellL1_{settings.VoxelResolutionL1}", node.ShellL1Mesh);
            }

            if (showShellL2 && node.ShellL2Mesh != null)
            {
                CreateMeshChild(nodeObject.transform, $"ShellL2_{settings.VoxelResolutionL2}", node.ShellL2Mesh);
            }

            for (int childIndex = 0; childIndex < nodes.Length; childIndex++)
            {
                if (nodes[childIndex].ParentIndex == nodeIndex)
                {
                    CreateNodeRecursive(nodeObject.transform, nodes, childIndex);
                }
            }
        }

        private void CreateMeshChild(Transform parentTransform, string childName, Mesh mesh)
        {
            GameObject childObject = new GameObject(childName);
            childObject.transform.SetParent(parentTransform, false);
            MeshFilter filter = childObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer meshRenderer = childObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = previewMaterial;
        }

        private void ClearHierarchy()
        {
            Transform? generatedRoot = transform.Find(GeneratedRootName);
            if (generatedRoot != null)
            {
                DestroyImmediate(generatedRoot.gameObject);
            }

            DestroyGeneratedMeshes();
            generatedNodes = System.Array.Empty<MeshVoxelizerHierarchyNode>();
        }

        private void DestroyGeneratedMeshes()
        {
            for (int i = 0; i < generatedNodes.Length; i++)
            {
                DestroyGeneratedMesh(generatedNodes[i].ShellL0Mesh);
                DestroyGeneratedMesh(generatedNodes[i].ShellL1Mesh);
                DestroyGeneratedMesh(generatedNodes[i].ShellL2Mesh);
            }
        }

        private static void DestroyGeneratedMesh(Mesh? mesh)
        {
            if (mesh != null)
            {
                DestroyImmediate(mesh);
            }
        }
    }
}
