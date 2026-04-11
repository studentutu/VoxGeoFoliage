#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.Profiling;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Runtime orchestration hub for Phase D registration plus Phase E frame preparation and renderer ownership.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VegetationRuntimeContainer : MonoBehaviour
    {
        private static readonly ProfilerMarker RefreshRuntimeRegistrationMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeContainer.RefreshRuntimeRegistration");
        private static readonly ProfilerMarker PrepareFrameForCameraMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeContainer.PrepareFrameForCamera");
        private static readonly ProfilerMarker PrepareGpuResidentFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeContainer.PrepareGpuResidentFrame");
        private static readonly List<VegetationRuntimeContainer> ActiveContainersInternal = new List<VegetationRuntimeContainer>();

        [SerializeField] private Vector3 gridOrigin = Vector3.zero;
        [SerializeField] private Vector3 cellSize = new Vector3(32f, 32f, 32f);
        [SerializeField] private ComputeShader? vegetationClassifyShader;
        [SerializeField] private bool enableDiagnostics;

        private readonly List<VegetationTreeAuthoring> containedAuthorings = new List<VegetationTreeAuthoring>();
        private readonly Plane[] reusableFrustumPlanes = new Plane[6];
        private VegetationRuntimeRegistry? registry;
        private VegetationGpuDecisionPipeline? gpuDecisionPipeline;
        private VegetationIndirectRenderer? indirectRenderer;
        private int lastPreparedCameraInstanceId = -1;
        private int lastPreparedRenderFrame = -1;
        private string lastRegistrationDiagnostics = string.Empty;
        private string lastPreparationDiagnostics = string.Empty;
        private string lastPreparationWarning = string.Empty;

        public VegetationRuntimeRegistry? Registry => registry;

        public VegetationIndirectRenderer? IndirectRenderer => indirectRenderer;

        public bool HasPreparedFrame => indirectRenderer != null && indirectRenderer.HasUploadedFrame;

        internal bool DiagnosticsEnabled => enableDiagnostics;

        public static void GetActiveContainers(List<VegetationRuntimeContainer> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Clear();
            for (int i = 0; i < ActiveContainersInternal.Count; i++)
            {
                VegetationRuntimeContainer? container = ActiveContainersInternal[i];
                if (container != null && container.isActiveAndEnabled)
                {
                    target.Add(container);
                }
            }
        }

        /// <summary>
        /// [INTEGRATION] Rebuilds the full Phase D runtime registration snapshot from the current scene vegetation authorings.
        /// </summary>
        public void RefreshRuntimeRegistration()
        {
            using (RefreshRuntimeRegistrationMarker.Auto())
            {
                ResetAuthoringRuntimeIndices();

                CollectContainedAuthorings(containedAuthorings);
                VegetationRuntimeRegistryBuilder builder = new VegetationRuntimeRegistryBuilder(gridOrigin, cellSize);
                registry = builder.Build(containedAuthorings);
                indirectRenderer?.Dispose();
                indirectRenderer = new VegetationIndirectRenderer(registry, gameObject.layer, enableDiagnostics);

                for (int treeIndex = 0; treeIndex < registry.TreeInstances.Count; treeIndex++)
                {
                    registry.TreeInstances[treeIndex].Authoring.RefreshRuntimeTreeIndex(treeIndex);
                }

                gpuDecisionPipeline?.Dispose();
                gpuDecisionPipeline = vegetationClassifyShader == null
                    ? null
                    : new VegetationGpuDecisionPipeline(vegetationClassifyShader, registry);
                lastPreparedCameraInstanceId = -1;
                lastPreparedRenderFrame = -1;

                LogRegistrationDiagnostics(containedAuthorings);
            }
        }

        /// <summary>
        /// [INTEGRATION] Clears runtime-only registration state and releases GPU resources without touching authoring assets.
        /// </summary>
        public void ResetRuntimeState()
        {
            ResetAuthoringRuntimeIndices();
            gpuDecisionPipeline?.Dispose();
            gpuDecisionPipeline = null;
            indirectRenderer?.Dispose();
            indirectRenderer = null;
            registry = null;
            lastPreparedCameraInstanceId = -1;
            lastPreparedRenderFrame = -1;
        }

        /// <summary>
        /// [INTEGRATION] Prepares exactly one camera-visible frame snapshot and uploads it into the Phase E indirect renderer.
        /// </summary>
        public bool PrepareFrameForCamera(Camera camera)
        {
            using (PrepareFrameForCameraMarker.Auto())
            {
                if (camera == null)
                {
                    throw new ArgumentNullException(nameof(camera));
                }

                EnsureRuntimeRegistration();
                if (registry == null || indirectRenderer == null)
                {
                    return false;
                }

                int renderFrame = Time.renderedFrameCount;
                int cameraInstanceId = camera.GetInstanceID();
                if (lastPreparedRenderFrame == renderFrame && lastPreparedCameraInstanceId == cameraInstanceId)
                {
                    return HasPreparedFrame;
                }

                GeometryUtility.CalculateFrustumPlanes(camera, reusableFrustumPlanes);
                Vector3 cameraWorldPosition = camera.transform.position;
                bool uploadedFrame = PrepareGpuResidentFrame(cameraWorldPosition, reusableFrustumPlanes);

                lastPreparedCameraInstanceId = cameraInstanceId;
                lastPreparedRenderFrame = renderFrame;
                LogPreparationDiagnostics(camera, uploadedFrame);
                return uploadedFrame;
            }
        }

        private void OnEnable()
        {
            if (!ActiveContainersInternal.Contains(this))
            {
                ActiveContainersInternal.Add(this);
            }

            RefreshRuntimeRegistration();
        }

        private void OnDisable()
        {
            ActiveContainersInternal.Remove(this);
            ResetRuntimeState();
        }

        private void EnsureRuntimeRegistration()
        {
            if (registry != null && indirectRenderer != null && gpuDecisionPipeline != null)
            {
                return;
            }

            RefreshRuntimeRegistration();
        }

        private bool PrepareGpuResidentFrame(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
        {
            using (PrepareGpuResidentFrameMarker.Auto())
            {
                if (gpuDecisionPipeline == null)
                {
                    throw new InvalidOperationException(
                        "VegetationRuntimeContainer requires vegetationClassifyShader and a valid GPU pipeline. Legacy CPU/readback runtime paths were removed.");
                }

                gpuDecisionPipeline.PrepareResidentFrame(cameraWorldPosition, frustumPlanes);
                indirectRenderer!.BindGpuResidentFrame(
                    gpuDecisionPipeline.ResidentInstanceBuffer,
                    gpuDecisionPipeline.ResidentArgsBuffer);
                return indirectRenderer.HasUploadedFrame;
            }
        }

        private void CollectContainedAuthorings(List<VegetationTreeAuthoring> target)
        {
            target.Clear();
            GetComponentsInChildren(true, target);
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < target.Count; readIndex++)
            {
                VegetationTreeAuthoring authoring = target[readIndex];
                if (authoring == null || !authoring.gameObject.activeInHierarchy)
                {
                    continue;
                }

                VegetationRuntimeContainer? owningContainer = authoring.GetComponentInParent<VegetationRuntimeContainer>();
                if (owningContainer != this)
                {
                    continue;
                }

                target[writeIndex] = authoring;
                writeIndex++;
            }

            if (writeIndex < target.Count)
            {
                target.RemoveRange(writeIndex, target.Count - writeIndex);
            }
        }

        private void ResetAuthoringRuntimeIndices()
        {
            if (registry == null)
            {
                return;
            }

            for (int i = 0; i < registry.TreeInstances.Count; i++)
            {
                VegetationTreeAuthoring authoring = registry.TreeInstances[i].Authoring;
                if (authoring != null)
                {
                    authoring.ResetRuntimeTreeIndex();
                }
            }
        }

        private void LogRegistrationDiagnostics(IReadOnlyList<VegetationTreeAuthoring> authorings)
        {
            if (!enableDiagnostics || registry == null)
            {
                return;
            }

            StringBuilder builder = new StringBuilder(256);
            builder.Append("VegetationRuntimeContainer registration");
            builder.Append(" container=").Append(name);
            builder.Append(" authorings=").Append(authorings.Count);
            builder.Append(" trees=").Append(registry.TreeInstances.Count);
            builder.Append(" branches=").Append(registry.SceneBranches.Count);
            builder.Append(" drawSlots=").Append(registry.DrawSlots.Count);
            builder.Append(" cells=").Append(registry.SpatialGrid.Cells.Count);
            builder.Append(" classifyShader=").Append(vegetationClassifyShader != null ? vegetationClassifyShader.name : "<none>");

            if (authorings.Count > 0)
            {
                builder.Append(" authoringNames=[");
                int namesToLog = Mathf.Min(authorings.Count, 4);
                for (int i = 0; i < namesToLog; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(authorings[i] != null ? authorings[i].name : "<null>");
                }

                if (authorings.Count > namesToLog)
                {
                    builder.Append(", ...");
                }

                builder.Append(']');
            }

            string summary = builder.ToString();
            if (summary == lastRegistrationDiagnostics)
            {
                return;
            }

            lastRegistrationDiagnostics = summary;
            UnityEngine.Debug.Log(summary, this);
        }

        private void LogPreparationDiagnostics(Camera camera, bool uploadedFrame)
        {
            if (!enableDiagnostics)
            {
                return;
            }

            if (registry == null || indirectRenderer == null)
            {
                string missingStateSummary =
                    $"VegetationRuntimeContainer prepare failed container={name} camera={camera.name} reason=missing-runtime-state";
                if (missingStateSummary != lastPreparationWarning)
                {
                    lastPreparationWarning = missingStateSummary;
                    UnityEngine.Debug.LogWarning(missingStateSummary, this);
                }

                return;
            }

            string summary = string.Format(
                "VegetationRuntimeContainer prepare container={0} camera={1} uploaded={2} source=GpuResident drawSlots={3}",
                name,
                camera.name,
                uploadedFrame,
                indirectRenderer.ActiveSlotIndices.Count);

            if (uploadedFrame && indirectRenderer.ActiveSlotIndices.Count > 0)
            {
                if (summary != lastPreparationDiagnostics)
                {
                    lastPreparationDiagnostics = summary;
                    UnityEngine.Debug.Log(summary, this);
                }

                lastPreparationWarning = string.Empty;
                return;
            }

            string warningSummary = summary + " reason=no-bound-gpu-resident-draw-slots";
            if (warningSummary != lastPreparationWarning)
            {
                lastPreparationWarning = warningSummary;
                UnityEngine.Debug.LogWarning(warningSummary, this);
            }
        }
    }
}
