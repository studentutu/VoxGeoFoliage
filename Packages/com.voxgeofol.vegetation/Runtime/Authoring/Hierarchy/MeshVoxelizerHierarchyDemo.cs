#nullable enable

using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace MeshVoxelizerProject
{
    /// <summary>
    /// Sample component that visualizes canonical L0 plus compact L1/L2 shell hierarchies.
    /// </summary>
    public sealed class MeshVoxelizerHierarchyDemo : MonoBehaviour
    {
        [SerializeField] private ShellBakeSettings settings = new ShellBakeSettings();
        [SerializeField] private MeshFilter? fromMesh;
        [SerializeField] private Material? previewMaterial;
        [SerializeField] private bool showShellL0 = true;
        [SerializeField] private bool showShellL1 = true;
        [SerializeField] private bool showShellL2 = true;
        [SerializeField] private bool generateHierarchy;
        [SerializeField] private bool clearGeneratedHierarchy;

        private const string GeneratedRootName = "VoxelHierarchy";
        private MeshVoxelizerHierarchyNode[] generatedL0Nodes = System.Array.Empty<MeshVoxelizerHierarchyNode>();
        private MeshVoxelizerHierarchyNode[] generatedL1Nodes = System.Array.Empty<MeshVoxelizerHierarchyNode>();
        private MeshVoxelizerHierarchyNode[] generatedL2Nodes = System.Array.Empty<MeshVoxelizerHierarchyNode>();

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
            // Range: fromMesh or a child MeshFilter must exist. Condition: canonical L0 drives ownership and compact L1/L2 are rebuilt from that occupancy into their own hierarchies. Output: a generated child hierarchy used for visual inspection.
            ClearHierarchy();

            MeshFilter? sourceFilter = ResolveSourceFilter();
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
            {
                Debug.LogWarning("MeshVoxelizerHierarchyDemo requires a source MeshFilter with a readable sharedMesh.", this);
                return;
            }

            MeshVoxelizerHierarchyBuilder.BuildHierarchies(
                sourceFilter.sharedMesh,
                settings.VoxelResolutionL0,
                settings.VoxelResolutionL1,
                settings.VoxelResolutionL2,
                settings.MaxOctreeDepth,
                settings.MinimumSurfaceVoxelCountToSplit,
                out generatedL0Nodes,
                out generatedL1Nodes,
                out generatedL2Nodes);

            GameObject root = new GameObject(GeneratedRootName);
            root.transform.SetParent(transform, false);

            if (showShellL0)
            {
                CreateHierarchyTierRoot(root.transform, generatedL0Nodes, 0);
            }

            if (showShellL1)
            {
                CreateHierarchyTierRoot(root.transform, generatedL1Nodes, 1);
            }

            if (showShellL2)
            {
                CreateHierarchyTierRoot(root.transform, generatedL2Nodes, 2);
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

        private void CreateHierarchyTierRoot(Transform parentTransform, MeshVoxelizerHierarchyNode[] nodes, int shellLevel)
        {
            if (nodes.Length == 0)
            {
                return;
            }

            GameObject tierRoot = new GameObject($"ShellL{shellLevel}_Hierarchy");
            tierRoot.transform.SetParent(parentTransform, false);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].ParentIndex == -1)
                {
                    CreateNodeRecursive(tierRoot.transform, nodes, shellLevel, i);
                }
            }
        }

        private void CreateNodeRecursive(
            Transform parentTransform,
            MeshVoxelizerHierarchyNode[] nodes,
            int shellLevel,
            int nodeIndex)
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

            Mesh? shellMesh = shellLevel switch
            {
                0 => node.ShellL0Mesh,
                1 => node.ShellL1Mesh,
                2 => node.ShellL2Mesh,
                _ => null
            };
            if (shellMesh != null)
            {
                int resolution = shellLevel switch
                {
                    0 => settings.VoxelResolutionL0,
                    1 => settings.VoxelResolutionL1,
                    2 => settings.VoxelResolutionL2,
                    _ => 0
                };
                CreateMeshChild(nodeObject.transform, $"ShellL{shellLevel}_{resolution}", shellMesh);
            }

            for (int childIndex = 0; childIndex < nodes.Length; childIndex++)
            {
                if (nodes[childIndex].ParentIndex == nodeIndex)
                {
                    CreateNodeRecursive(nodeObject.transform, nodes, shellLevel, childIndex);
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
            generatedL0Nodes = System.Array.Empty<MeshVoxelizerHierarchyNode>();
            generatedL1Nodes = System.Array.Empty<MeshVoxelizerHierarchyNode>();
            generatedL2Nodes = System.Array.Empty<MeshVoxelizerHierarchyNode>();
        }

        private void DestroyGeneratedMeshes()
        {
            for (int i = 0; i < generatedL0Nodes.Length; i++)
            {
                DestroyGeneratedMesh(generatedL0Nodes[i].ShellL0Mesh);
            }

            for (int i = 0; i < generatedL1Nodes.Length; i++)
            {
                DestroyGeneratedMesh(generatedL1Nodes[i].ShellL1Mesh);
            }

            for (int i = 0; i < generatedL2Nodes.Length; i++)
            {
                DestroyGeneratedMesh(generatedL2Nodes[i].ShellL2Mesh);
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
