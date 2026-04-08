#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Deterministic CPU reference mirror for Phase D tree classification, branch decisions, and BFS shell-node decisions.
    /// </summary>
    public sealed class VegetationCpuReferenceEvaluator
    {
        private readonly List<int> visibleCellIndices = new List<int>();

        /// <summary>
        /// [INTEGRATION] Evaluates the authoritative CPU reference path and rebuilds the stable Phase E handoff outputs.
        /// </summary>
        public void EvaluateFrame(
            VegetationRuntimeRegistry registry,
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            VegetationFrameDecisionState decisionState,
            VegetationFrameOutput frameOutput)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (frustumPlanes == null)
            {
                throw new ArgumentNullException(nameof(frustumPlanes));
            }

            if (decisionState == null)
            {
                throw new ArgumentNullException(nameof(decisionState));
            }

            if (frameOutput == null)
            {
                throw new ArgumentNullException(nameof(frameOutput));
            }

            decisionState.Reset(registry);
            registry.SpatialGrid.BuildVisibleCellMask(frustumPlanes, decisionState.CellVisibilityMask, visibleCellIndices);
            decisionState.RefreshVisibleCellIndices(visibleCellIndices);

            ClassifyTreesAndBranches(registry, cameraWorldPosition, frustumPlanes, decisionState);
            VegetationDecisionDecoder.Decode(registry, decisionState, frustumPlanes, frameOutput);
        }

        private static void ClassifyTreesAndBranches(
            VegetationRuntimeRegistry registry,
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            VegetationFrameDecisionState decisionState)
        {
            for (int treeIndex = 0; treeIndex < registry.TreeInstances.Count; treeIndex++)
            {
                VegetationTreeInstanceRuntime treeInstance = registry.TreeInstances[treeIndex];
                VegetationTreeBlueprintRuntime blueprint = registry.TreeBlueprints[treeInstance.BlueprintIndex];
                VegetationLodProfileRuntime lodProfile = registry.LodProfiles[blueprint.LodProfileIndex];

                if (treeInstance.CellIndex < 0 || decisionState.CellVisibilityMask[treeInstance.CellIndex] == 0u)
                {
                    decisionState.TreeModes[treeIndex] = VegetationTreeRenderMode.Culled;
                    continue;
                }

                float treeDistance = VegetationRuntimeMathUtility.ComputeSphereSurfaceDistance(
                    cameraWorldPosition,
                    treeInstance.SphereCenterWorld,
                    treeInstance.BoundingSphereRadius);

                if (treeDistance >= lodProfile.AbsoluteCullDistance)
                {
                    decisionState.TreeModes[treeIndex] = VegetationTreeRenderMode.Culled;
                    continue;
                }

                if (treeDistance >= lodProfile.ImpostorDistance)
                {
                    decisionState.TreeModes[treeIndex] = VegetationTreeRenderMode.Impostor;
                    continue;
                }

                decisionState.TreeModes[treeIndex] = VegetationTreeRenderMode.Expanded;
                for (int branchOffset = 0; branchOffset < treeInstance.SceneBranchCount; branchOffset++)
                {
                    int branchIndex = treeInstance.SceneBranchStartIndex + branchOffset;
                    VegetationSceneBranchRuntime sceneBranch = registry.SceneBranches[branchIndex];
                    float branchDistance = VegetationRuntimeMathUtility.ComputeSphereSurfaceDistance(
                        cameraWorldPosition,
                        sceneBranch.SphereCenterWorld,
                        sceneBranch.BoundingSphereRadius);

                    VegetationRuntimeBranchTier runtimeTier = branchDistance < lodProfile.L0Distance
                        ? VegetationRuntimeBranchTier.L0
                        : branchDistance < lodProfile.L1Distance
                            ? VegetationRuntimeBranchTier.L1
                            : branchDistance < lodProfile.L2Distance
                                ? VegetationRuntimeBranchTier.L2
                                : VegetationRuntimeBranchTier.L3;

                    VegetationBranchDecisionRecord branchDecision = decisionState.BranchDecisions[branchIndex];
                    branchDecision.RuntimeTier = (int)runtimeTier;
                    decisionState.BranchDecisions[branchIndex] = branchDecision;

                    if (runtimeTier == VegetationRuntimeBranchTier.L0)
                    {
                        continue;
                    }

                    WriteNodeDecisions(registry, decisionState, frustumPlanes, sceneBranch, runtimeTier);
                }
            }
        }

        private static void WriteNodeDecisions(
            VegetationRuntimeRegistry registry,
            VegetationFrameDecisionState decisionState,
            Plane[] frustumPlanes,
            VegetationSceneBranchRuntime sceneBranch,
            VegetationRuntimeBranchTier runtimeTier)
        {
            registry.GetDecisionRange(sceneBranch, runtimeTier, out int decisionStart, out int decisionCount);
            IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> shellNodes = registry.GetShellNodes(runtimeTier);
            VegetationBranchPrototypeRuntime prototype = registry.BranchPrototypes[sceneBranch.PrototypeIndex];
            int shellNodeStart = runtimeTier switch
            {
                VegetationRuntimeBranchTier.L1 => prototype.ShellNodeStartIndexL1,
                VegetationRuntimeBranchTier.L2 => prototype.ShellNodeStartIndexL2,
                VegetationRuntimeBranchTier.L3 => prototype.ShellNodeStartIndexL3,
                _ => throw new ArgumentOutOfRangeException(nameof(runtimeTier), runtimeTier, "Node decisions exist only for runtime shell tiers L1/L2/L3.")
            };

            for (int nodeLocalIndex = 0; nodeLocalIndex < decisionCount; nodeLocalIndex++)
            {
                VegetationBranchShellNodeRuntimeBfs shellNode = shellNodes[shellNodeStart + nodeLocalIndex];
                Bounds nodeWorldBounds = VegetationRuntimeMathUtility.TransformBounds(
                    new Bounds(shellNode.LocalCenter, shellNode.LocalExtents * 2f),
                    sceneBranch.LocalToWorld);

                VegetationNodeDecision decision;
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, nodeWorldBounds))
                {
                    decision = VegetationNodeDecision.Reject;
                }
                else if (shellNode.ChildMask != 0u && shellNode.FirstChildIndex >= 0)
                {
                    // MVP assumption: Phase D keeps intra-tier simplification deterministic by always expanding visible internal nodes and only emitting visible leaves.
                    decision = VegetationNodeDecision.ExpandChildren;
                }
                else
                {
                    decision = VegetationNodeDecision.EmitSelf;
                }

                VegetationNodeDecisionRecord record = decisionState.NodeDecisions[decisionStart + nodeLocalIndex];
                record.Decision = (int)decision;
                decisionState.NodeDecisions[decisionStart + nodeLocalIndex] = record;
            }
        }
    }
}
