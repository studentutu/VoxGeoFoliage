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
    /// [INTEGRATION] Scene-view gizmo demo for the Phase E uploaded indirect batches and decoded visible instances.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugVegetationDemo : MonoBehaviour
    {
        private static readonly Color VisibleInstanceColor = new Color(0.14f, 0.95f, 0.28f, 1f);
        private static readonly Color UploadedBatchColor = new Color(0.1f, 0.82f, 1f, 0.9f);
        private static readonly Color ErrorColor = new Color(0.95f, 0.22f, 0.22f, 1f);

        [SerializeField] private VegetationRuntimeManager? runtimeManager;
        [SerializeField] private VegetationTreeAuthoring? targetTree;
        [SerializeField] private Camera? previewCamera;
        [SerializeField] private bool drawOnlyWhenSelected = false;
        [SerializeField] private bool drawOnGismo = false;
        [SerializeField] private bool showVisibleInstances = true;
        [SerializeField] private bool showUploadedBatches = true;
        [SerializeField] private bool showLabels = true;

        private readonly List<VegetationIndirectDrawBatchSnapshot> uploadedBatches = new List<VegetationIndirectDrawBatchSnapshot>();
        private int cachedTargetTreeIndex = -1;
        private string? lastErrorMessage;

        private void Reset()
        {
            ResolveDefaults();
        }

        private void OnValidate()
        {
            ResolveDefaults();
            cachedTargetTreeIndex = -1;
        }

        private void OnDrawGizmos()
        {
            if(!drawOnGismo)
                return;
            if (drawOnlyWhenSelected)
            {
                return;
            }

            DrawDebugGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            if(!drawOnGismo)
                return;
            
            if (!drawOnlyWhenSelected)
            {
                return;
            }

            DrawDebugGizmos();
        }

        private void DrawDebugGizmos()
        {
            ResolveDefaults();

            Camera? activeCamera = ResolveCamera();
            if (runtimeManager == null)
            {
                DrawError("Missing VegetationRuntimeManager.");
                return;
            }

            if (activeCamera == null)
            {
                DrawError("Missing preview camera.");
                return;
            }

            try
            {
                if (!runtimeManager.PrepareFrameForCamera(activeCamera))
                {
                    DrawError("Runtime manager did not prepare a visible frame.");
                    return;
                }

                VegetationRuntimeRegistry? registry = runtimeManager.Registry;
                VegetationFrameOutput? frameOutput = runtimeManager.LastFrameOutput;
                VegetationIndirectRenderer? indirectRenderer = runtimeManager.IndirectRenderer;
                if (registry == null || frameOutput == null || indirectRenderer == null)
                {
                    DrawError("Phase E runtime state is incomplete.");
                    return;
                }

                ResolveTargetTreeIndex(registry);
                lastErrorMessage = null;

                if (showVisibleInstances)
                {
                    DrawVisibleInstances(frameOutput);
                }

                if (showUploadedBatches)
                {
                    DrawUploadedBatches(indirectRenderer);
                }

                DrawManagerLabel(indirectRenderer.ActiveSlotIndices.Count);
            }
            catch (Exception exception)
            {
                DrawError(exception.Message);
            }
        }

        private void DrawVisibleInstances(VegetationFrameOutput frameOutput)
        {
            Gizmos.color = VisibleInstanceColor;
            for (int activeSlotOffset = 0; activeSlotOffset < frameOutput.ActiveSlotIndices.Count; activeSlotOffset++)
            {
                int slotIndex = frameOutput.ActiveSlotIndices[activeSlotOffset];
                VegetationVisibleSlotOutput slotOutput = frameOutput.SlotOutputs[slotIndex];
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

        private void DrawUploadedBatches(VegetationIndirectRenderer indirectRenderer)
        {
            if (targetTree != null)
            {
                return;
            }

            indirectRenderer.GetDebugSnapshots(uploadedBatches);
            Gizmos.color = UploadedBatchColor;
            for (int batchIndex = 0; batchIndex < uploadedBatches.Count; batchIndex++)
            {
                VegetationIndirectDrawBatchSnapshot batch = uploadedBatches[batchIndex];
                Gizmos.DrawWireCube(batch.WorldBounds.center, batch.WorldBounds.size);
                string countLabel = batch.HasExactInstanceCount ? batch.InstanceCount.ToString() : "?";
                DrawLabel(batch.WorldBounds.center, $"{batch.DebugLabel} x{countLabel}");
            }
        }

        private void DrawManagerLabel(int activeSlotCount)
        {
            string label = $"PhaseE {runtimeManager!.LastPreparedFrameSource} slots:{activeSlotCount}";
            DrawLabel(transform.position + (Vector3.up * 0.5f), label);
        }

        private void ResolveDefaults()
        {
            if (runtimeManager == null)
            {
                runtimeManager = GetComponent<VegetationRuntimeManager>();
                if (runtimeManager == null)
                {
                    runtimeManager = FindFirstObjectByType<VegetationRuntimeManager>(FindObjectsInactive.Exclude);
                }
            }

            if (targetTree == null)
            {
                targetTree = GetComponent<VegetationTreeAuthoring>();
            }
        }

        private void ResolveTargetTreeIndex(VegetationRuntimeRegistry registry)
        {
            cachedTargetTreeIndex = -1;
            if (targetTree == null)
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

            throw new InvalidOperationException($"Target tree '{targetTree.name}' is not part of the active runtime registration.");
        }

        private bool ShouldDrawTree(int treeIndex)
        {
            return targetTree == null || treeIndex == cachedTargetTreeIndex;
        }

        private Camera? ResolveCamera()
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

        private void DrawError(string message)
        {
            lastErrorMessage = message;
            Gizmos.color = ErrorColor;
            Gizmos.DrawWireSphere(transform.position, 0.35f);
            DrawLabel(transform.position, lastErrorMessage);
        }

        private void DrawLabel(Vector3 position, string? text)
        {
            if (!showLabels || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

#if UNITY_EDITOR
            Handles.Label(position + (Vector3.up * 0.12f), text);
#endif
        }
    }
}
