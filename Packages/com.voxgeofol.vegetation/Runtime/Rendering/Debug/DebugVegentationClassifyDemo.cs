#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Editor Scene-view gizmo demo for the real VegetationClassify.compute GPU decision path and decoded visible frontier.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugVegentationClassifyDemo : MonoBehaviour
    {
        private const string ClassifyShaderAssetPath = "Packages/com.voxgeofol.vegetation/Runtime/Shaders/VegetationClassify.compute";
        private static readonly Color VisibleCellColor = new Color(0.18f, 0.55f, 1f, 0.18f);
        private static readonly Color VisibleInstanceColor = new Color(0.1f, 0.95f, 1f, 1f);
        private static readonly Color InactiveBranchColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color L0BranchColor = new Color(0.96f, 0.42f, 0.18f, 1f);
        private static readonly Color L1BranchColor = new Color(1f, 0.78f, 0.12f, 1f);
        private static readonly Color L2BranchColor = new Color(0.1f, 0.86f, 1f, 1f);
        private static readonly Color L3BranchColor = new Color(0.82f, 0.28f, 1f, 1f);
        private static readonly Color RejectNodeColor = new Color(0.9f, 0.18f, 0.18f, 1f);
        private static readonly Color EmitNodeColor = new Color(0.16f, 0.95f, 0.26f, 1f);
        private static readonly Color ExpandNodeColor = new Color(1f, 0.82f, 0.18f, 1f);
        private static readonly Color CulledTreeColor = new Color(0.92f, 0.22f, 0.22f, 1f);
        private static readonly Color ExpandedTreeColor = new Color(0.16f, 0.92f, 0.4f, 1f);
        private static readonly Color ImpostorTreeColor = new Color(1f, 0.75f, 0.2f, 1f);

        [SerializeField] private VegetationTreeAuthoring? targetTree;
        [SerializeField] private ComputeShader? vegetationClassifyShader;
        [SerializeField] private Camera? previewCamera;
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;
        [SerializeField] private Vector3 cellSize = new Vector3(32f, 32f, 32f);
        [SerializeField] private bool drawOnlyWhenSelected = false;
        [SerializeField] private bool showVisibleCells = true;
        [SerializeField] private bool showTreeBounds = true;
        [SerializeField] private bool showBranchBounds = true;
        [SerializeField] private bool showShellNodeDecisions = true;
        [SerializeField] private bool showDecodedVisibleInstances = true;
        [SerializeField] private bool showLabels = false;

        private readonly List<VegetationTreeAuthoring> orderedAuthorings = new List<VegetationTreeAuthoring>();
        private VegetationRuntimeRegistry? registry;
        private VegetationGpuDecisionPipeline? gpuDecisionPipeline;
        private VegetationFrameDecisionState? lastDecisionState;
        private VegetationFrameOutput? lastFrameOutput;
        private int cachedRegistrationHash = int.MinValue;
        private int cachedTargetTreeIndex = -1;
        private string? lastErrorMessage;

        private void Reset()
        {
            ResolveDefaultReferences();
        }

        private void OnEnable()
        {
            ResolveDefaultReferences();
            InvalidateDebugCache();
        }

        private void OnDisable()
        {
            ReleaseRuntimeResources();
        }

        private void OnValidate()
        {
            ReleaseDebugCache();
            ResolveDefaultReferences();
            InvalidateDebugCache();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawOnlyWhenSelected)
            {
                return;
            }
            DrawDebugGizmos();
        }

        /// <summary>
        /// [INTEGRATION] Forces a fresh registration rebuild and GPU classification pass for the current editor camera.
        /// </summary>
        [ContextMenu("Refresh Debug Snapshot")]
        public void RefreshDebugSnapshot()
        {
            InvalidateDebugCache();
            DrawDebugGizmos();
        }

        /// <summary>
        /// [INTEGRATION] Releases cached runtime buffers so the next Scene-view pass rebuilds from scratch.
        /// </summary>
        [ContextMenu("Release Debug Cache")]
        public void ReleaseDebugCache()
        {
            ReleaseRuntimeResources();
            lastErrorMessage = null;
        }

        private void DrawDebugGizmos()
        {
            Camera? activeCamera = ResolveActiveCamera();
            if (!TryRefreshDebugSnapshot(activeCamera))
            {
                DrawErrorGizmo();
                return;
            }

            if (registry == null || lastDecisionState == null || lastFrameOutput == null)
            {
                return;
            }

            DrawVisibleCellGizmos();
            DrawTreeGizmos();
            DrawBranchAndNodeGizmos();
            DrawVisibleInstanceGizmos();
        }

        private bool TryRefreshDebugSnapshot(Camera? activeCamera)
        {
            if (vegetationClassifyShader == null)
            {
                lastErrorMessage = "Missing VegetationClassify.compute reference.";
                return false;
            }

            if (activeCamera == null)
            {
                lastErrorMessage = "No active camera available for frustum classification.";
                return false;
            }

            try
            {
                CollectOrderedAuthorings();
                if (orderedAuthorings.Count == 0)
                {
                    lastErrorMessage = "No active VegetationTreeAuthoring instances found in the scene.";
                    return false;
                }

                int registrationHash = BuildRegistrationHash();
                if (registrationHash != cachedRegistrationHash || registry == null || gpuDecisionPipeline == null)
                {
                    RebuildRegistrationCache();
                    cachedRegistrationHash = registrationHash;
                }

                if (registry == null || gpuDecisionPipeline == null)
                {
                    lastErrorMessage = "Debug registration cache failed to initialize.";
                    return false;
                }

                ResolveTargetTreeIndex();
                if (targetTree != null && cachedTargetTreeIndex < 0)
                {
                    return false;
                }

                Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(activeCamera);
                lastDecisionState = gpuDecisionPipeline.EvaluateFrameImmediate(activeCamera.transform.position, frustumPlanes);
                if (lastFrameOutput == null)
                {
                    lastFrameOutput = registry.CreateFrameOutput();
                }

                VegetationDecisionDecoder.Decode(registry, lastDecisionState, frustumPlanes, lastFrameOutput);
                lastErrorMessage = null;
                return true;
            }
            catch (Exception exception)
            {
                lastErrorMessage = exception.Message;
                return false;
            }
        }

        private void CollectOrderedAuthorings()
        {
            orderedAuthorings.Clear();
            VegetationTreeAuthoring[] authorings = FindObjectsByType<VegetationTreeAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < authorings.Length; i++)
            {
                if (authorings[i] != null)
                {
                    orderedAuthorings.Add(authorings[i]);
                }
            }

            orderedAuthorings.Sort(CompareAuthorings);
        }

        private int BuildRegistrationHash()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + gridOrigin.GetHashCode();
                hash = (hash * 31) + cellSize.GetHashCode();
                hash = (hash * 31) + vegetationClassifyShader!.GetInstanceID();

                for (int i = 0; i < orderedAuthorings.Count; i++)
                {
                    VegetationTreeAuthoring authoring = orderedAuthorings[i];
                    Transform transformToHash = authoring.transform;
                    hash = (hash * 31) + authoring.GetInstanceID();
                    hash = (hash * 31) + (authoring.Blueprint != null ? authoring.Blueprint.GetInstanceID() : 0);
                    hash = (hash * 31) + (authoring.gameObject.scene.path?.GetHashCode() ?? 0);
                    hash = (hash * 31) + BuildHierarchyPath(transformToHash).GetHashCode();
                    hash = (hash * 31) + transformToHash.position.GetHashCode();
                    hash = (hash * 31) + transformToHash.rotation.GetHashCode();
                    hash = (hash * 31) + transformToHash.lossyScale.GetHashCode();
                }

                return hash;
            }
        }

        private void RebuildRegistrationCache()
        {
            ReleaseRuntimeResources();

            VegetationRuntimeRegistryBuilder builder = new VegetationRuntimeRegistryBuilder(gridOrigin, cellSize);
            registry = builder.Build(orderedAuthorings);
            gpuDecisionPipeline = new VegetationGpuDecisionPipeline(vegetationClassifyShader!, registry);
            lastFrameOutput = registry.CreateFrameOutput();
            lastDecisionState = null;
            cachedTargetTreeIndex = -1;
        }

        private void ResolveTargetTreeIndex()
        {
            cachedTargetTreeIndex = -1;

            if (registry == null || targetTree == null)
            {
                return;
            }

            for (int treeIndex = 0; treeIndex < registry.TreeInstances.Count; treeIndex++)
            {
                if (registry.TreeInstances[treeIndex].Authoring == targetTree)
                {
                    cachedTargetTreeIndex = treeIndex;
                    return;
                }
            }

            lastErrorMessage = $"Target tree '{targetTree.name}' is not part of the active runtime registration.";
        }

        private void DrawVisibleCellGizmos()
        {
            if (!showVisibleCells || registry == null || lastDecisionState == null)
            {
                return;
            }

            Gizmos.color = VisibleCellColor;
            for (int i = 0; i < lastDecisionState.VisibleCellIndices.Count; i++)
            {
                int cellIndex = lastDecisionState.VisibleCellIndices[i];
                VegetationSpatialGrid.CellData cell = registry.SpatialGrid.Cells[cellIndex];
                Bounds bounds = cell.AuthoritativeBounds;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
                DrawLabel(bounds.center, $"Cell {cell.CellIndex}");
            }
        }

        private void DrawTreeGizmos()
        {
            if (!showTreeBounds || registry == null || lastDecisionState == null)
            {
                return;
            }

            for (int treeIndex = 0; treeIndex < registry.TreeInstances.Count; treeIndex++)
            {
                if (!ShouldDrawTree(treeIndex))
                {
                    continue;
                }

                VegetationTreeInstanceRuntime tree = registry.TreeInstances[treeIndex];
                VegetationTreeRenderMode treeMode = lastDecisionState.TreeModes[treeIndex];
                Gizmos.color = GetTreeColor(treeMode);
                Gizmos.DrawWireCube(tree.WorldBounds.center, tree.WorldBounds.size);

                int visibleInstanceCount = CountTreeVisibleInstances(treeIndex);
                DrawLabel(tree.WorldBounds.center, $"Tree {treeIndex} {treeMode} vis:{visibleInstanceCount}");
            }
        }

        private void DrawBranchAndNodeGizmos()
        {
            if (registry == null || lastDecisionState == null)
            {
                return;
            }

            for (int branchIndex = 0; branchIndex < registry.SceneBranches.Count; branchIndex++)
            {
                VegetationSceneBranchRuntime sceneBranch = registry.SceneBranches[branchIndex];
                if (!ShouldDrawTree(sceneBranch.TreeIndex))
                {
                    continue;
                }

                VegetationBranchDecisionRecord branchDecision = lastDecisionState.BranchDecisions[branchIndex];
                if (showBranchBounds)
                {
                    Gizmos.color = GetBranchColor(branchDecision.RuntimeTier);
                    Gizmos.DrawWireCube(sceneBranch.WorldBounds.center, sceneBranch.WorldBounds.size);
                    DrawLabel(sceneBranch.WorldBounds.center, $"B{branchIndex} {GetTierLabel(branchDecision.RuntimeTier)}");
                }

                if (!showShellNodeDecisions || !branchDecision.IsActive || branchDecision.RuntimeTier == (int)VegetationRuntimeBranchTier.L0)
                {
                    continue;
                }

                DrawShellNodeDecisionGizmos(branchIndex, sceneBranch, (VegetationRuntimeBranchTier)branchDecision.RuntimeTier);
            }
        }

        private void DrawShellNodeDecisionGizmos(int branchIndex, VegetationSceneBranchRuntime sceneBranch, VegetationRuntimeBranchTier runtimeTier)
        {
            if (registry == null || lastDecisionState == null)
            {
                return;
            }

            registry.GetDecisionRange(sceneBranch, runtimeTier, out int decisionStart, out int decisionCount);
            if (decisionCount <= 0)
            {
                return;
            }

            VegetationBranchPrototypeRuntime prototype = registry.BranchPrototypes[sceneBranch.PrototypeIndex];
            IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> shellNodes = registry.GetShellNodes(runtimeTier);
            int shellStartIndex = GetShellStartIndex(prototype, runtimeTier);

            for (int nodeIndex = 0; nodeIndex < decisionCount; nodeIndex++)
            {
                VegetationBranchShellNodeRuntimeBfs node = shellNodes[shellStartIndex + nodeIndex];
                VegetationNodeDecision nodeDecision = (VegetationNodeDecision)lastDecisionState.NodeDecisions[decisionStart + nodeIndex].Decision;
                Bounds worldBounds = VegetationRuntimeMathUtility.TransformBounds(
                    new Bounds(node.LocalCenter, node.LocalExtents * 2f),
                    sceneBranch.LocalToWorld);

                Gizmos.color = GetNodeColor(nodeDecision);
                Gizmos.DrawWireCube(worldBounds.center, worldBounds.size);

                if (nodeDecision == VegetationNodeDecision.EmitSelf)
                {
                    Gizmos.DrawSphere(worldBounds.center, Mathf.Max(0.02f, worldBounds.extents.magnitude * 0.08f));
                }

                DrawLabel(worldBounds.center, $"B{branchIndex} N{nodeIndex} {nodeDecision}");
            }
        }

        private void DrawVisibleInstanceGizmos()
        {
            if (!showDecodedVisibleInstances || lastFrameOutput == null)
            {
                return;
            }

            Gizmos.color = VisibleInstanceColor;
            for (int activeSlotOffset = 0; activeSlotOffset < lastFrameOutput.ActiveSlotIndices.Count; activeSlotOffset++)
            {
                int slotIndex = lastFrameOutput.ActiveSlotIndices[activeSlotOffset];
                VegetationVisibleSlotOutput slotOutput = lastFrameOutput.SlotOutputs[slotIndex];
                for (int instanceIndex = 0; instanceIndex < slotOutput.Instances.Count; instanceIndex++)
                {
                    VegetationVisibleInstance instance = slotOutput.Instances[instanceIndex];
                    if (!ShouldDrawTree(instance.TreeIndex))
                    {
                        continue;
                    }

                    Gizmos.DrawWireCube(instance.WorldBounds.center, instance.WorldBounds.size);
                    DrawLabel(instance.WorldBounds.center, slotOutput.DrawSlot.DebugLabel);
                }
            }
        }

        private bool ShouldDrawTree(int treeIndex)
        {
            return targetTree == null || treeIndex == cachedTargetTreeIndex;
        }

        private int CountTreeVisibleInstances(int treeIndex)
        {
            if (lastFrameOutput == null)
            {
                return 0;
            }

            int count = 0;
            for (int activeSlotOffset = 0; activeSlotOffset < lastFrameOutput.ActiveSlotIndices.Count; activeSlotOffset++)
            {
                int slotIndex = lastFrameOutput.ActiveSlotIndices[activeSlotOffset];
                VegetationVisibleSlotOutput slotOutput = lastFrameOutput.SlotOutputs[slotIndex];
                for (int instanceIndex = 0; instanceIndex < slotOutput.Instances.Count; instanceIndex++)
                {
                    if (slotOutput.Instances[instanceIndex].TreeIndex == treeIndex)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private Camera? ResolveActiveCamera()
        {
            if (previewCamera != null)
            {
                return previewCamera;
            }

            if (Camera.current != null)
            {
                return Camera.current;
            }

            return Camera.main;
        }

        private void ResolveDefaultReferences()
        {
            if (targetTree == null)
            {
                targetTree = GetComponent<VegetationTreeAuthoring>();
            }

#if UNITY_EDITOR
            if (vegetationClassifyShader == null)
            {
                vegetationClassifyShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ClassifyShaderAssetPath);
            }
#endif
        }

        private void InvalidateDebugCache()
        {
            cachedRegistrationHash = int.MinValue;
            cachedTargetTreeIndex = -1;
        }

        private void ReleaseRuntimeResources()
        {
            gpuDecisionPipeline?.Dispose();
            gpuDecisionPipeline = null;
            registry = null;
            lastDecisionState = null;
            lastFrameOutput = null;
            cachedRegistrationHash = int.MinValue;
            cachedTargetTreeIndex = -1;
        }

        private void DrawErrorGizmo()
        {
            if (string.IsNullOrWhiteSpace(lastErrorMessage))
            {
                return;
            }

            Vector3 errorPosition = targetTree != null ? targetTree.transform.position : transform.position;
            Gizmos.color = CulledTreeColor;
            Gizmos.DrawWireSphere(errorPosition, 0.35f);
            DrawLabel(errorPosition, lastErrorMessage!);
        }

        private static int GetShellStartIndex(VegetationBranchPrototypeRuntime prototype, VegetationRuntimeBranchTier runtimeTier)
        {
            switch (runtimeTier)
            {
                case VegetationRuntimeBranchTier.L1:
                    return prototype.ShellNodeStartIndexL1;
                case VegetationRuntimeBranchTier.L2:
                    return prototype.ShellNodeStartIndexL2;
                case VegetationRuntimeBranchTier.L3:
                    return prototype.ShellNodeStartIndexL3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(runtimeTier), runtimeTier, "Shell-node debug drawing is valid only for runtime tiers L1/L2/L3.");
            }
        }

        private static Color GetTreeColor(VegetationTreeRenderMode treeMode)
        {
            switch (treeMode)
            {
                case VegetationTreeRenderMode.Culled:
                    return CulledTreeColor;
                case VegetationTreeRenderMode.Expanded:
                    return ExpandedTreeColor;
                case VegetationTreeRenderMode.Impostor:
                    return ImpostorTreeColor;
                default:
                    throw new ArgumentOutOfRangeException(nameof(treeMode), treeMode, "Unexpected tree render mode.");
            }
        }

        private static Color GetBranchColor(int runtimeTier)
        {
            switch (runtimeTier)
            {
                case VegetationBranchDecisionRecord.InactiveRuntimeTier:
                    return InactiveBranchColor;
                case (int)VegetationRuntimeBranchTier.L0:
                    return L0BranchColor;
                case (int)VegetationRuntimeBranchTier.L1:
                    return L1BranchColor;
                case (int)VegetationRuntimeBranchTier.L2:
                    return L2BranchColor;
                case (int)VegetationRuntimeBranchTier.L3:
                    return L3BranchColor;
                default:
                    return CulledTreeColor;
            }
        }

        private static Color GetNodeColor(VegetationNodeDecision nodeDecision)
        {
            switch (nodeDecision)
            {
                case VegetationNodeDecision.Reject:
                    return RejectNodeColor;
                case VegetationNodeDecision.EmitSelf:
                    return EmitNodeColor;
                case VegetationNodeDecision.ExpandChildren:
                    return ExpandNodeColor;
                default:
                    throw new ArgumentOutOfRangeException(nameof(nodeDecision), nodeDecision, "Unexpected node decision.");
            }
        }

        private static string GetTierLabel(int runtimeTier)
        {
            return runtimeTier switch
            {
                VegetationBranchDecisionRecord.InactiveRuntimeTier => "Inactive",
                (int)VegetationRuntimeBranchTier.L0 => "L0",
                (int)VegetationRuntimeBranchTier.L1 => "L1",
                (int)VegetationRuntimeBranchTier.L2 => "L2",
                (int)VegetationRuntimeBranchTier.L3 => "L3",
                _ => "Invalid"
            };
        }

        private static int CompareAuthorings(VegetationTreeAuthoring left, VegetationTreeAuthoring right)
        {
            string leftScenePath = left.gameObject.scene.path ?? string.Empty;
            string rightScenePath = right.gameObject.scene.path ?? string.Empty;
            int compareScenePath = string.CompareOrdinal(leftScenePath, rightScenePath);
            if (compareScenePath != 0)
            {
                return compareScenePath;
            }

            string leftHierarchyPath = BuildHierarchyPath(left.transform);
            string rightHierarchyPath = BuildHierarchyPath(right.transform);
            int compareHierarchyPath = string.CompareOrdinal(leftHierarchyPath, rightHierarchyPath);
            if (compareHierarchyPath != 0)
            {
                return compareHierarchyPath;
            }

            return left.GetInstanceID().CompareTo(right.GetInstanceID());
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            Stack<string> pathParts = new Stack<string>();
            Transform? current = transform;
            while (current != null)
            {
                pathParts.Push($"{current.GetSiblingIndex():D4}:{current.name}");
                current = current.parent;
            }

            return string.Join("/", pathParts.ToArray());
        }

        private void DrawLabel(Vector3 position, string text)
        {
            if (!showLabels)
            {
                return;
            }

#if UNITY_EDITOR
            Handles.Label(position + (Vector3.up * 0.12f), text);
#endif
        }
    }
}
