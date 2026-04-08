#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Reconstructs the exact per-slot visible frontier from the Phase D decision contracts.
    /// </summary>
    public static class VegetationDecisionDecoder
    {
        /// <summary>
        /// [INTEGRATION] Decodes the current frame decision state into exact per-slot visible-instance outputs and bounds.
        /// </summary>
        public static void Decode(
            VegetationRuntimeRegistry registry,
            VegetationFrameDecisionState decisionState,
            Plane[] frustumPlanes,
            VegetationFrameOutput frameOutput)
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

            frameOutput.Reset();
            for (int treeIndex = 0; treeIndex < registry.TreeInstances.Count; treeIndex++)
            {
                VegetationTreeInstanceRuntime treeInstance = registry.TreeInstances[treeIndex];
                VegetationTreeBlueprintRuntime blueprint = registry.TreeBlueprints[treeInstance.BlueprintIndex];
                VegetationTreeRenderMode treeMode = decisionState.TreeModes[treeIndex];
                switch (treeMode)
                {
                    case VegetationTreeRenderMode.Culled:
                        continue;
                    case VegetationTreeRenderMode.Impostor:
                        if (GeometryUtility.TestPlanesAABB(frustumPlanes, treeInstance.WorldBounds))
                        {
                            EmitVisibleInstance(frameOutput, registry.DrawSlots[blueprint.ImpostorDrawSlot], treeIndex, -1, -1, 0u, treeInstance.LocalToWorld);
                        }

                        continue;
                    case VegetationTreeRenderMode.Expanded:
                        DecodeExpandedTree(registry, decisionState, frustumPlanes, frameOutput, treeIndex, treeInstance, blueprint);
                        continue;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static void DecodeExpandedTree(
            VegetationRuntimeRegistry registry,
            VegetationFrameDecisionState decisionState,
            Plane[] frustumPlanes,
            VegetationFrameOutput frameOutput,
            int treeIndex,
            VegetationTreeInstanceRuntime treeInstance,
            VegetationTreeBlueprintRuntime blueprint)
        {
            bool useFullTrunk = false;
            for (int branchOffset = 0; branchOffset < treeInstance.SceneBranchCount; branchOffset++)
            {
                int branchIndex = treeInstance.SceneBranchStartIndex + branchOffset;
                VegetationBranchDecisionRecord branchDecision = decisionState.BranchDecisions[branchIndex];
                if (!branchDecision.IsActive)
                {
                    continue;
                }

                VegetationSceneBranchRuntime sceneBranch = registry.SceneBranches[branchIndex];
                VegetationBranchPrototypeRuntime prototype = registry.BranchPrototypes[sceneBranch.PrototypeIndex];
                VegetationRuntimeBranchTier runtimeTier = (VegetationRuntimeBranchTier)branchDecision.RuntimeTier;
                if (runtimeTier == VegetationRuntimeBranchTier.L0 || runtimeTier == VegetationRuntimeBranchTier.L1)
                {
                    useFullTrunk = true;
                }

                switch (runtimeTier)
                {
                    case VegetationRuntimeBranchTier.L0:
                        if (!GeometryUtility.TestPlanesAABB(frustumPlanes, sceneBranch.WorldBounds))
                        {
                            continue;
                        }

                        EmitVisibleInstance(frameOutput, registry.DrawSlots[prototype.WoodDrawSlotL0], treeIndex, branchIndex, -1, 0u, sceneBranch.LocalToWorld);
                        EmitVisibleInstance(frameOutput, registry.DrawSlots[prototype.FoliageDrawSlotL0], treeIndex, branchIndex, -1, prototype.PackedLeafTint, sceneBranch.LocalToWorld);
                        break;
                    case VegetationRuntimeBranchTier.L1:
                        if (GeometryUtility.TestPlanesAABB(frustumPlanes, sceneBranch.WorldBounds))
                        {
                            EmitVisibleInstance(frameOutput, registry.DrawSlots[prototype.WoodDrawSlotL1], treeIndex, branchIndex, -1, 0u, sceneBranch.LocalToWorld);
                        }

                        DecodeShellFrontier(registry, decisionState, frameOutput, treeIndex, branchIndex, sceneBranch, runtimeTier, prototype.PackedLeafTint);
                        break;
                    case VegetationRuntimeBranchTier.L2:
                        if (GeometryUtility.TestPlanesAABB(frustumPlanes, sceneBranch.WorldBounds))
                        {
                            EmitVisibleInstance(frameOutput, registry.DrawSlots[prototype.WoodDrawSlotL2], treeIndex, branchIndex, -1, 0u, sceneBranch.LocalToWorld);
                        }

                        DecodeShellFrontier(registry, decisionState, frameOutput, treeIndex, branchIndex, sceneBranch, runtimeTier, prototype.PackedLeafTint);
                        break;
                    case VegetationRuntimeBranchTier.L3:
                        if (GeometryUtility.TestPlanesAABB(frustumPlanes, sceneBranch.WorldBounds))
                        {
                            EmitVisibleInstance(frameOutput, registry.DrawSlots[prototype.WoodDrawSlotL3], treeIndex, branchIndex, -1, 0u, sceneBranch.LocalToWorld);
                        }

                        DecodeShellFrontier(registry, decisionState, frameOutput, treeIndex, branchIndex, sceneBranch, runtimeTier, prototype.PackedLeafTint);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (GeometryUtility.TestPlanesAABB(frustumPlanes, treeInstance.WorldBounds))
            {
                int trunkSlot = useFullTrunk ? blueprint.TrunkFullDrawSlot : blueprint.TrunkL3DrawSlot;
                EmitVisibleInstance(frameOutput, registry.DrawSlots[trunkSlot], treeIndex, -1, -1, 0u, treeInstance.LocalToWorld);
            }
        }

        private static void DecodeShellFrontier(
            VegetationRuntimeRegistry registry,
            VegetationFrameDecisionState decisionState,
            VegetationFrameOutput frameOutput,
            int treeIndex,
            int branchIndex,
            VegetationSceneBranchRuntime sceneBranch,
            VegetationRuntimeBranchTier runtimeTier,
            uint packedLeafTint)
        {
            registry.GetDecisionRange(sceneBranch, runtimeTier, out int decisionStart, out int decisionCount);
            if (decisionCount <= 0)
            {
                return;
            }

            VegetationBranchPrototypeRuntime prototype = registry.BranchPrototypes[sceneBranch.PrototypeIndex];
            int shellStartIndex = runtimeTier switch
            {
                VegetationRuntimeBranchTier.L1 => prototype.ShellNodeStartIndexL1,
                VegetationRuntimeBranchTier.L2 => prototype.ShellNodeStartIndexL2,
                VegetationRuntimeBranchTier.L3 => prototype.ShellNodeStartIndexL3,
                _ => throw new ArgumentOutOfRangeException(nameof(runtimeTier), runtimeTier, "Shell frontier decode is valid only for runtime tiers L1/L2/L3.")
            };

            IReadOnlyList<VegetationBranchShellNodeRuntimeBfs> shellNodes = registry.GetShellNodes(runtimeTier);
            int[] queue = new int[decisionCount];
            int queueHead = 0;
            int queueTail = 0;
            queue[queueTail++] = 0;

            while (queueHead < queueTail)
            {
                int localNodeIndex = queue[queueHead++];
                VegetationNodeDecisionRecord nodeDecision = decisionState.NodeDecisions[decisionStart + localNodeIndex];
                switch ((VegetationNodeDecision)nodeDecision.Decision)
                {
                    case VegetationNodeDecision.Reject:
                        continue;
                    case VegetationNodeDecision.EmitSelf:
                        VegetationBranchShellNodeRuntimeBfs node = shellNodes[shellStartIndex + localNodeIndex];
                        EmitVisibleInstance(frameOutput, registry.DrawSlots[node.ShellDrawSlot], treeIndex, branchIndex, localNodeIndex, packedLeafTint, sceneBranch.LocalToWorld);
                        continue;
                    case VegetationNodeDecision.ExpandChildren:
                        VegetationBranchShellNodeRuntimeBfs parentNode = shellNodes[shellStartIndex + localNodeIndex];
                        int childCount = CountBits(parentNode.ChildMask);
                        for (int childOffset = 0; childOffset < childCount; childOffset++)
                        {
                            queue[queueTail++] = parentNode.FirstChildIndex + childOffset;
                        }

                        continue;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private static void EmitVisibleInstance(
            VegetationFrameOutput frameOutput,
            VegetationDrawSlot drawSlot,
            int treeIndex,
            int branchIndex,
            int nodeIndex,
            uint packedLeafTint,
            Matrix4x4 localToWorld)
        {
            frameOutput.AddVisibleInstance(new VegetationVisibleInstance
            {
                TreeIndex = treeIndex,
                BranchInstanceIndex = branchIndex,
                NodeIndex = nodeIndex,
                DrawSlotIndex = drawSlot.SlotIndex,
                PackedLeafTint = packedLeafTint,
                LocalToWorld = localToWorld,
                WorldBounds = VegetationRuntimeMathUtility.TransformBounds(drawSlot.LocalBounds, localToWorld)
            });
        }

        private static int CountBits(uint value)
        {
            int count = 0;
            while (value != 0u)
            {
                count += (int)(value & 1u);
                value >>= 1;
            }

            return count;
        }
    }
}
