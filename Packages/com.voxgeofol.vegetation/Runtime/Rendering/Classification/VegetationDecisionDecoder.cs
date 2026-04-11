#nullable enable

using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Unity.Profiling;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Reconstructs the exact per-slot visible frontier from the Phase D decision contracts.
    /// </summary>
    public static class VegetationDecisionDecoder
    {
        private const int EmitSelfDecision = (int)VegetationNodeDecision.EmitSelf;
        private static readonly ProfilerMarker DecodeMarker = new ProfilerMarker("VoxGeoFol.VegetationDecisionDecoder.Decode");
        private static readonly ProfilerMarker ResetFrameOutputMarker = new ProfilerMarker("VoxGeoFol.VegetationDecisionDecoder.ResetFrameOutput");
        private static readonly ProfilerMarker DecodeTreesMarker = new ProfilerMarker("VoxGeoFol.VegetationDecisionDecoder.DecodeTrees");
        /// <summary>
        /// [INTEGRATION] Decodes the current frame decision state into exact per-slot visible-instance outputs and bounds.
        /// </summary>
        public static void Decode(
            VegetationRuntimeRegistry registry,
            VegetationFrameDecisionState decisionState,
            Plane[] frustumPlanes,
            VegetationFrameOutput frameOutput)
        {
            using (DecodeMarker.Auto())
            {
                // Range: accepts the frozen runtime registry plus one already-classified frame decision state. Condition: shell-node decisions follow the BFS contract frozen in Phase D. Output: exact per-slot visible instances, visible-data bounds, and indirect-args seed counts for Phase E.
                if (registry == null)
                {
                    throw new ArgumentNullException(nameof(registry));
                }

                if (decisionState == null)
                {
                    throw new ArgumentNullException(nameof(decisionState));
                }

                if (frustumPlanes == null)
                {
                    throw new ArgumentNullException(nameof(frustumPlanes));
                }

                if (frameOutput == null)
                {
                    throw new ArgumentNullException(nameof(frameOutput));
                }

                using (ResetFrameOutputMarker.Auto())
                {
                    frameOutput.Reset();
                }

                using (DecodeTreesMarker.Auto())
                {
                    VegetationTreeInstanceRuntime[] treeInstances = registry.TreeInstancesArray;
                    VegetationTreeBlueprintRuntime[] treeBlueprints = registry.TreeBlueprintsArray;
                    VegetationSceneBranchRuntime[] sceneBranches = registry.SceneBranchesArray;
                    VegetationBranchDecisionRecord[] branchDecisions = decisionState.BranchDecisions;
                    VegetationNodeDecisionRecord[] nodeDecisions = decisionState.NodeDecisions;
                    int[] nodeDrawSlotIndices = registry.NodeDrawSlotIndices;
                    Bounds[] nodeWorldBounds = registry.NodeWorldBounds;

                    for (int treeIndex = 0; treeIndex < treeInstances.Length; treeIndex++)
                    {
                        VegetationTreeInstanceRuntime treeInstance = treeInstances[treeIndex];
                        VegetationTreeRenderMode treeMode = decisionState.TreeModes[treeIndex];
                        if (treeMode == VegetationTreeRenderMode.Culled || !TestPlanesAabb(frustumPlanes, treeInstance.WorldBounds))
                        {
                            continue;
                        }

                        VegetationTreeBlueprintRuntime blueprint = treeBlueprints[treeInstance.BlueprintIndex];
                        switch (treeMode)
                        {
                            case VegetationTreeRenderMode.Impostor:
                                frameOutput.AddVisibleInstance(
                                    blueprint.ImpostorDrawSlot,
                                    treeIndex,
                                    in treeInstance.UploadInstanceData,
                                    in treeInstance.ImpostorWorldBounds);
                                continue;
                            case VegetationTreeRenderMode.Expanded:
                                DecodeExpandedTree(
                                    blueprint,
                                    branchDecisions,
                                    nodeDecisions,
                                    nodeDrawSlotIndices,
                                    nodeWorldBounds,
                                    sceneBranches,
                                    frustumPlanes,
                                    frameOutput,
                                    treeIndex,
                                    treeInstance);
                                continue;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
            }
        }

        private static void DecodeExpandedTree(
            VegetationTreeBlueprintRuntime blueprint,
            VegetationBranchDecisionRecord[] branchDecisions,
            VegetationNodeDecisionRecord[] nodeDecisions,
            int[] nodeDrawSlotIndices,
            Bounds[] nodeWorldBounds,
            VegetationSceneBranchRuntime[] sceneBranches,
            Plane[] frustumPlanes,
            VegetationFrameOutput frameOutput,
            int treeIndex,
            VegetationTreeInstanceRuntime treeInstance)
        {
            bool useFullTrunk = false;
            for (int branchOffset = 0; branchOffset < treeInstance.SceneBranchCount; branchOffset++)
            {
                int branchIndex = treeInstance.SceneBranchStartIndex + branchOffset;
                VegetationBranchDecisionRecord branchDecision = branchDecisions[branchIndex];
                if (!branchDecision.IsActive)
                {
                    continue;
                }

                VegetationSceneBranchRuntime sceneBranch = sceneBranches[branchIndex];
                VegetationRuntimeBranchTier runtimeTier = (VegetationRuntimeBranchTier)branchDecision.RuntimeTier;
                if (runtimeTier == VegetationRuntimeBranchTier.L0 || runtimeTier == VegetationRuntimeBranchTier.L1)
                {
                    useFullTrunk = true;
                }

                switch (runtimeTier)
                {
                    case VegetationRuntimeBranchTier.L0:
                        if (!TestPlanesAabb(frustumPlanes, sceneBranch.WorldBounds))
                        {
                            continue;
                        }

                        frameOutput.AddVisibleInstance(
                            sceneBranch.WoodDrawSlotL0,
                            treeIndex,
                            in sceneBranch.WoodUploadInstanceData,
                            in sceneBranch.WoodWorldBoundsL0);
                        frameOutput.AddVisibleInstance(
                            sceneBranch.FoliageDrawSlotL0,
                            treeIndex,
                            in sceneBranch.FoliageUploadInstanceData,
                            in sceneBranch.FoliageWorldBoundsL0);
                        break;
                    case VegetationRuntimeBranchTier.L1:
                        if (TestPlanesAabb(frustumPlanes, sceneBranch.WorldBounds))
                        {
                            frameOutput.AddVisibleInstance(
                                sceneBranch.WoodDrawSlotL1,
                                treeIndex,
                                in sceneBranch.WoodUploadInstanceData,
                                in sceneBranch.WoodWorldBoundsL1);
                        }

                        DecodeShellFrontier(nodeDecisions, nodeDrawSlotIndices, nodeWorldBounds, frustumPlanes, frameOutput, treeIndex, sceneBranch, runtimeTier);
                        break;
                    case VegetationRuntimeBranchTier.L2:
                        if (TestPlanesAabb(frustumPlanes, sceneBranch.WorldBounds))
                        {
                            frameOutput.AddVisibleInstance(
                                sceneBranch.WoodDrawSlotL2,
                                treeIndex,
                                in sceneBranch.WoodUploadInstanceData,
                                in sceneBranch.WoodWorldBoundsL2);
                        }

                        DecodeShellFrontier(nodeDecisions, nodeDrawSlotIndices, nodeWorldBounds, frustumPlanes, frameOutput, treeIndex, sceneBranch, runtimeTier);
                        break;
                    case VegetationRuntimeBranchTier.L3:
                        if (TestPlanesAabb(frustumPlanes, sceneBranch.WorldBounds))
                        {
                            frameOutput.AddVisibleInstance(
                                sceneBranch.WoodDrawSlotL3,
                                treeIndex,
                                in sceneBranch.WoodUploadInstanceData,
                                in sceneBranch.WoodWorldBoundsL3);
                        }

                        DecodeShellFrontier(nodeDecisions, nodeDrawSlotIndices, nodeWorldBounds, frustumPlanes, frameOutput, treeIndex, sceneBranch, runtimeTier);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            Bounds trunkWorldBounds = useFullTrunk ? treeInstance.TrunkFullWorldBounds : treeInstance.TrunkL3WorldBounds;
            frameOutput.AddVisibleInstance(
                useFullTrunk ? blueprint.TrunkFullDrawSlot : blueprint.TrunkL3DrawSlot,
                treeIndex,
                in treeInstance.UploadInstanceData,
                in trunkWorldBounds);
        }

        private static void DecodeShellFrontier(
            VegetationNodeDecisionRecord[] nodeDecisions,
            int[] nodeDrawSlotIndices,
            Bounds[] nodeWorldBounds,
            Plane[] frustumPlanes,
            VegetationFrameOutput frameOutput,
            int treeIndex,
            VegetationSceneBranchRuntime sceneBranch,
            VegetationRuntimeBranchTier runtimeTier)
        {
            int decisionStart;
            int decisionCount;
            Bounds shellWorldBounds;
            switch (runtimeTier)
            {
                case VegetationRuntimeBranchTier.L1:
                    decisionStart = sceneBranch.DecisionStartL1;
                    decisionCount = sceneBranch.DecisionCountL1;
                    shellWorldBounds = sceneBranch.ShellWorldBoundsL1;
                    break;
                case VegetationRuntimeBranchTier.L2:
                    decisionStart = sceneBranch.DecisionStartL2;
                    decisionCount = sceneBranch.DecisionCountL2;
                    shellWorldBounds = sceneBranch.ShellWorldBoundsL2;
                    break;
                case VegetationRuntimeBranchTier.L3:
                    decisionStart = sceneBranch.DecisionStartL3;
                    decisionCount = sceneBranch.DecisionCountL3;
                    shellWorldBounds = sceneBranch.ShellWorldBoundsL3;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(runtimeTier), runtimeTier, "Shell frontier decode is valid only for runtime tiers L1/L2/L3.");
            }

            if (decisionCount <= 0 || !TestPlanesAabb(frustumPlanes, shellWorldBounds))
            {
                return;
            }

            for (int localNodeIndex = 0; localNodeIndex < decisionCount; localNodeIndex++)
            {
                int decisionIndex = decisionStart + localNodeIndex;
                if (nodeDecisions[decisionIndex].Decision != EmitSelfDecision)
                {
                    continue;
                }

                frameOutput.AddVisibleInstance(
                    nodeDrawSlotIndices[decisionIndex],
                    treeIndex,
                    in sceneBranch.FoliageUploadInstanceData,
                    in nodeWorldBounds[decisionIndex]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TestPlanesAabb(Plane[] frustumPlanes, Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int i = 0; i < frustumPlanes.Length; i++)
            {
                Plane plane = frustumPlanes[i];
                Vector3 normal = plane.normal;
                float projectionRadius =
                    extents.x * Mathf.Abs(normal.x) +
                    extents.y * Mathf.Abs(normal.y) +
                    extents.z * Mathf.Abs(normal.z);
                if (Vector3.Dot(normal, center) + plane.distance + projectionRadius < 0f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
