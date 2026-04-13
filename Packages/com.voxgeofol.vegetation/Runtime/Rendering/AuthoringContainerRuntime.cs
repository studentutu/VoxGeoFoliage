#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Single authoritative runtime owner for vegetation registration, GPU classification, and indirect rendering.
    /// </summary>
    public sealed class AuthoringContainerRuntime : IDisposable
    {
        private static readonly ProfilerMarker RefreshRuntimeRegistrationMarker =
            new ProfilerMarker("VoxGeoFol.AuthoringContainerRuntime.RefreshRuntimeRegistration");

        private static readonly ProfilerMarker PrepareFrameForCameraMarker =
            new ProfilerMarker("VoxGeoFol.AuthoringContainerRuntime.PrepareFrameForCamera");

        private static readonly ProfilerMarker PrepareFrameForFrustumMarker =
            new ProfilerMarker("VoxGeoFol.AuthoringContainerRuntime.PrepareFrameForFrustum");

        private static readonly ProfilerMarker PrepareGpuResidentFrameMarker =
            new ProfilerMarker("VoxGeoFol.AuthoringContainerRuntime.PrepareGpuResidentFrame");

        private readonly IReadOnlyList<VegetationTreeAuthoringRuntime> authorings;
        private readonly Plane[] reusableFrustumPlanes = new Plane[6];
        private readonly string containerId;
        private readonly string debugName;
        private readonly UnityEngine.Object? diagnosticsContext;
        private readonly VegetationRuntimeProviderKind providerKind;
        private readonly Vector3 gridOrigin;
        private readonly Vector3 cellSize;
        private readonly int renderLayer;
        private readonly int maxVisibleInstanceCapacity;
        private VegetationRuntimeRegistry? registry;
        private VegetationGpuDecisionPipeline? gpuDecisionPipeline;
        private VegetationIndirectRenderer? indirectRenderer;
        private int gpuPipelineShaderInstanceId = -1;
        private int gpuPipelineVisibleInstanceCapacity = -1;
        private int lastPreparedCameraInstanceId = -1;
        private int lastPreparedRenderFrame = -1;
        private bool registrationDiagnosticsDirty = true;
        private int lastPreparationDiagnosticsCameraInstanceId = -1;
        private int lastPreparationDiagnosticsDrawSlotCount = -1;
        private bool lastPreparationDiagnosticsUploadedFrame;
        private int lastPreparationMissingStateCameraInstanceId = -1;
        private int lastPreparationWarningCameraInstanceId = -1;
        private int lastPreparationWarningDrawSlotCount = -1;
        private bool lastPreparationWarningUploadedFrame;
        private bool gpuPipelineTelemetryDiagnosticsDirty = true;
        private bool preparedFrameTelemetryDiagnosticsDirty = true;
        private bool isRegistered;
        private bool isDisposed;
        private bool renderRuntimeFaulted;
        private string? renderRuntimeFaultStage;
        private bool renderRuntimeCleanupPending;

        public AuthoringContainerRuntime(
            string containerId,
            VegetationRuntimeProviderKind providerKind,
            string debugName,
            UnityEngine.Object? diagnosticsContext,
            int renderLayer,
            Vector3 gridOrigin,
            Vector3 cellSize,
            int maxVisibleInstanceCapacity,
            IReadOnlyList<VegetationTreeAuthoringRuntime> authorings)
        {
            if (string.IsNullOrWhiteSpace(containerId))
            {
                throw new ArgumentException("Container id must be present.", nameof(containerId));
            }

            this.authorings = authorings ?? new List<VegetationTreeAuthoringRuntime>();
            this.containerId = containerId;
            this.providerKind = providerKind;
            this.debugName = debugName;
            this.diagnosticsContext = diagnosticsContext;
            this.renderLayer = renderLayer;
            this.gridOrigin = gridOrigin;
            this.cellSize = cellSize;
            this.maxVisibleInstanceCapacity = Mathf.Max(1, maxVisibleInstanceCapacity);
        }

        public string ContainerId => containerId;

        public VegetationRuntimeProviderKind ProviderKind => providerKind;

        public string DebugName => debugName;

        public VegetationRuntimeRegistry? Registry => registry;

        public VegetationIndirectRenderer? IndirectRenderer => indirectRenderer;

        public bool HasPreparedFrame => indirectRenderer != null && indirectRenderer.HasUploadedFrame;

        public bool IsRenderRuntimeFaulted => renderRuntimeFaulted;

        /// <summary>
        /// [INTEGRATION] Activates this runtime owner in the shared renderer discovery registry.
        /// </summary>
        public bool Activate()
        {
            if (isDisposed)
            {
                return false;
            }
            if (isRegistered)
            {
                return true;
            }

            isRegistered = VegetationActiveAuthoringContainerRuntimes.Register(this);
            return isRegistered;
        }

        /// <summary>
        /// [INTEGRATION] Rebuilds the runtime registration snapshot from runtime-safe tree records.
        /// </summary>
        public void RefreshRuntimeRegistration()
        {
            if (isDisposed)
            {
                return;
            }

            if (!isRegistered)
            {
                return;
            }

            using (RefreshRuntimeRegistrationMarker.Auto())
            {
                ResetAuthoringRuntimeIndices();

                VegetationRuntimeRegistryBuilder builder = new VegetationRuntimeRegistryBuilder(gridOrigin, cellSize);
                registry = builder.Build(authorings);
                indirectRenderer?.Dispose();
                indirectRenderer = new VegetationIndirectRenderer(registry, renderLayer);

                for (int treeIndex = 0; treeIndex < registry.TreeInstances.Count; treeIndex++)
                {
                    registry.TreeInstances[treeIndex].Authoring.RefreshRuntimeTreeIndex(treeIndex);
                }

                ResetGpuDecisionPipeline();
                registrationDiagnosticsDirty = true;
                gpuPipelineTelemetryDiagnosticsDirty = true;
                preparedFrameTelemetryDiagnosticsDirty = true;
                lastPreparedCameraInstanceId = -1;
                lastPreparedRenderFrame = -1;
                lastPreparationMissingStateCameraInstanceId = -1;
                lastPreparationWarningCameraInstanceId = -1;
                lastPreparationWarningDrawSlotCount = -1;
                ClearRenderRuntimeFault();
            }
        }

        /// <summary>
        /// [INTEGRATION] Clears runtime-only state and releases GPU resources.
        /// </summary>
        public void ResetRuntimeState()
        {
            ResetAuthoringRuntimeIndices();
            ResetGpuDecisionPipeline();
            indirectRenderer?.Dispose();
            indirectRenderer = null;
            registry = null;
            registrationDiagnosticsDirty = true;
            gpuPipelineTelemetryDiagnosticsDirty = true;
            preparedFrameTelemetryDiagnosticsDirty = true;
            lastPreparedCameraInstanceId = -1;
            lastPreparedRenderFrame = -1;
            lastPreparationDiagnosticsCameraInstanceId = -1;
            lastPreparationDiagnosticsDrawSlotCount = -1;
            lastPreparationMissingStateCameraInstanceId = -1;
            lastPreparationWarningCameraInstanceId = -1;
            lastPreparationWarningDrawSlotCount = -1;
            ClearRenderRuntimeFault();
        }

        /// <summary>
        /// [INTEGRATION] Prepares exactly one camera-visible GPU-resident frame snapshot for indirect rendering.
        /// </summary>
        public bool PrepareFrameForCamera(Camera camera, ComputeShader? classifyShader, bool diagnosticsEnabled)
        {
            using (PrepareFrameForCameraMarker.Auto())
            {
                if (isDisposed)
                {
                    return false;
                }

                if (camera == null)
                {
                    return false;
                }

                if (!isRegistered)
                {
                    return false;
                }

                if (renderRuntimeFaulted)
                {
                    CleanupFaultedRuntimeResourcesIfPending();
                    return false;
                }

                if (!SystemInfo.supportsComputeShaders)
                {
                    LogRenderRuntimeSkip("prepare", "compute-shaders-unsupported");
                    return false;
                }

                if (!SystemInfo.supportsInstancing)
                {
                    LogRenderRuntimeSkip("prepare", "gpu-instancing-unsupported");
                    return false;
                }

                try
                {
                    EnsureRuntimeRegistration();
                    if (registry == null || indirectRenderer == null)
                    {
                        return false;
                    }

                    LogRegistrationDiagnostics(diagnosticsEnabled);
                    int renderFrame = Time.renderedFrameCount;
                    int cameraInstanceId = camera.GetInstanceID();
                    if (lastPreparedRenderFrame == renderFrame && lastPreparedCameraInstanceId == cameraInstanceId)
                    {
                        return HasPreparedFrame;
                    }

                    GeometryUtility.CalculateFrustumPlanes(camera, reusableFrustumPlanes);
                    Vector3 cameraWorldPosition = camera.transform.position;
                    bool uploadedFrame =
                        PrepareGpuResidentFrame(
                            cameraWorldPosition,
                            reusableFrustumPlanes,
                            classifyShader,
                            diagnosticsEnabled,
                            true);

                    lastPreparedCameraInstanceId = cameraInstanceId;
                    lastPreparedRenderFrame = renderFrame;
                    LogPreparationDiagnostics(camera, uploadedFrame, diagnosticsEnabled);
                    return uploadedFrame;
                }
                catch (Exception exception)
                {
                    MarkRenderRuntimeFault("prepare", exception);
                    return false;
                }
            }
        }

        /// <summary>
        /// [INTEGRATION] Prepares a transient GPU-resident frame snapshot for an explicit world-space frustum without touching camera-frame cache state.
        /// </summary>
        internal bool PrepareFrameForFrustum(
            Vector3 observerWorldPosition,
            Plane[] frustumPlanes,
            ComputeShader? classifyShader,
            bool diagnosticsEnabled,
            bool allowExpandedTreePromotion)
        {
            using (PrepareFrameForFrustumMarker.Auto())
            {
                if (isDisposed)
                {
                    return false;
                }

                if (!isRegistered)
                {
                    return false;
                }

                if (renderRuntimeFaulted)
                {
                    CleanupFaultedRuntimeResourcesIfPending();
                    return false;
                }

                if (frustumPlanes == null || frustumPlanes.Length != reusableFrustumPlanes.Length)
                {
                    return false;
                }

                if (!SystemInfo.supportsComputeShaders)
                {
                    LogRenderRuntimeSkip("prepare-frustum", "compute-shaders-unsupported");
                    return false;
                }

                if (!SystemInfo.supportsInstancing)
                {
                    LogRenderRuntimeSkip("prepare-frustum", "gpu-instancing-unsupported");
                    return false;
                }

                try
                {
                    EnsureRuntimeRegistration();
                    if (registry == null || indirectRenderer == null)
                    {
                        return false;
                    }

                    LogRegistrationDiagnostics(diagnosticsEnabled);
                    return PrepareGpuResidentFrame(
                        observerWorldPosition,
                        frustumPlanes,
                        classifyShader,
                        diagnosticsEnabled,
                        allowExpandedTreePromotion);
                }
                catch (Exception exception)
                {
                    MarkRenderRuntimeFault("prepare-frustum", exception);
                    return false;
                }
            }
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            if (isRegistered)
            {
                VegetationActiveAuthoringContainerRuntimes.Unregister(this);
                isRegistered = false;
            }

            ResetRuntimeState();
            isDisposed = true;
        }

        internal void HandleRegistrySuperseded()
        {
            isRegistered = false;
            ResetRuntimeState();
        }

        internal void MarkRenderRuntimeFault(string stage, Exception exception)
        {
            if (renderRuntimeFaulted)
            {
                return;
            }

            renderRuntimeFaulted = true;
            renderRuntimeFaultStage = stage;
            renderRuntimeCleanupPending = true;
            Debug.LogError(
                $"Vegetation runtime disabled container={debugName} provider={providerKind} stage={stage} reason={exception.GetType().Name}: {exception.Message}",
                diagnosticsContext);
            Debug.LogException(exception, diagnosticsContext);
        }

        private void EnsureRuntimeRegistration()
        {
            if (!isRegistered || (registry != null && indirectRenderer != null))
            {
                return;
            }

            RefreshRuntimeRegistration();
        }

        private void CleanupFaultedRuntimeResourcesIfPending()
        {
            if (!renderRuntimeCleanupPending)
            {
                return;
            }

            ResetGpuDecisionPipeline();
            indirectRenderer?.Dispose();
            indirectRenderer = null;
            lastPreparedCameraInstanceId = -1;
            lastPreparedRenderFrame = -1;
            lastPreparationDiagnosticsCameraInstanceId = -1;
            lastPreparationDiagnosticsDrawSlotCount = -1;
            lastPreparationMissingStateCameraInstanceId = -1;
            lastPreparationWarningCameraInstanceId = -1;
            lastPreparationWarningDrawSlotCount = -1;
            renderRuntimeCleanupPending = false;
        }

        private bool PrepareGpuResidentFrame(
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            ComputeShader? classifyShader,
            bool diagnosticsEnabled,
            bool allowExpandedTreePromotion)
        {
            using (PrepareGpuResidentFrameMarker.Auto())
            {
                if (!TryEnsureGpuDecisionPipeline(classifyShader))
                {
                    return false;
                }

                VegetationGpuDecisionPipeline pipeline = gpuDecisionPipeline!;
                LogGpuPipelineTelemetry(diagnosticsEnabled, pipeline);
                pipeline.PrepareResidentFrame(cameraWorldPosition, frustumPlanes, allowExpandedTreePromotion);
                indirectRenderer!.BindGpuResidentFrame(
                    pipeline.ResidentInstanceBuffer,
                    pipeline.ResidentArgsBuffer,
                    pipeline.ResidentSlotPackedStartsBuffer);
                LogPreparedFrameTelemetry(diagnosticsEnabled, pipeline);
                return indirectRenderer.HasUploadedFrame;
            }
        }

        private bool TryEnsureGpuDecisionPipeline(ComputeShader? classifyShader)
        {
            if (registry == null)
            {
                LogRenderRuntimeSkip("pipeline", "missing-runtime-registration");
                return false;
            }

            if (classifyShader == null)
            {
                LogRenderRuntimeSkip("pipeline", "missing-classify-shader");
                return false;
            }

            int shaderInstanceId = classifyShader.GetInstanceID();
            if (gpuDecisionPipeline != null &&
                shaderInstanceId == gpuPipelineShaderInstanceId &&
                maxVisibleInstanceCapacity == gpuPipelineVisibleInstanceCapacity)
            {
                return true;
            }

            ResetGpuDecisionPipeline();
            gpuDecisionPipeline = new VegetationGpuDecisionPipeline(
                classifyShader,
                registry,
                maxVisibleInstanceCapacity);
            gpuPipelineShaderInstanceId = shaderInstanceId;
            gpuPipelineVisibleInstanceCapacity = maxVisibleInstanceCapacity;
            gpuPipelineTelemetryDiagnosticsDirty = true;
            preparedFrameTelemetryDiagnosticsDirty = true;
            return true;
        }

        private void ResetGpuDecisionPipeline()
        {
            gpuDecisionPipeline?.Dispose();
            gpuDecisionPipeline = null;
            gpuPipelineShaderInstanceId = -1;
            gpuPipelineVisibleInstanceCapacity = -1;
            gpuPipelineTelemetryDiagnosticsDirty = true;
            preparedFrameTelemetryDiagnosticsDirty = true;
        }

        private void ResetAuthoringRuntimeIndices()
        {
            for (int i = 0; i < authorings.Count; i++)
            {
                VegetationTreeAuthoringRuntime? authoring = authorings[i];
                authoring?.ResetRuntimeTreeIndex();
            }
        }

        private void LogRegistrationDiagnostics(bool diagnosticsEnabled)
        {
            if (!diagnosticsEnabled || registry == null || !registrationDiagnosticsDirty)
            {
                return;
            }

            int activeAuthorings = CountActiveAuthorings();
            StringBuilder builder = new StringBuilder(256);
            builder.Append("AuthoringContainerRuntime registration");
            builder.Append(" container=").Append(debugName);
            builder.Append(" provider=").Append(providerKind);
            builder.Append(" configuredAuthorings=").Append(authorings.Count);
            builder.Append(" activeAuthorings=").Append(activeAuthorings);
            builder.Append(" trees=").Append(registry.TreeInstances.Count);
            builder.Append(" blueprints=").Append(registry.TreeBlueprints.Count);
            builder.Append(" blueprintPlacements=").Append(registry.BlueprintBranchPlacements.Count);
            builder.Append(" branchPrototypes=").Append(registry.BranchPrototypes.Count);
            builder.Append(" drawSlots=").Append(registry.DrawSlots.Count);
            builder.Append(" cells=").Append(registry.SpatialGrid.Cells.Count);

            if (activeAuthorings > 0)
            {
                builder.Append(" authoringNames=[");
                AppendActiveAuthoringNames(builder, 4);
                builder.Append(']');
            }

            registrationDiagnosticsDirty = false;
            Debug.Log(builder.ToString(), diagnosticsContext);
        }

        private void LogGpuPipelineTelemetry(bool diagnosticsEnabled, VegetationGpuDecisionPipeline pipeline)
        {
            if (!diagnosticsEnabled || !gpuPipelineTelemetryDiagnosticsDirty || registry == null)
            {
                return;
            }

            StringBuilder builder = new StringBuilder(512);
            builder.Append("AuthoringContainerRuntime telemetry");
            builder.Append(" container=").Append(debugName);
            builder.Append(" provider=").Append(providerKind);
            builder.Append(" cells=").Append(pipeline.CellCount);
            builder.Append(" cellBufferElements=").Append(pipeline.AllocatedCellBufferElementCount);
            builder.Append(" cellBufferBytes=").Append(pipeline.CellBufferBytes);
            builder.Append(" lodProfiles=").Append(pipeline.LodProfileCount);
            builder.Append(" lodProfileBufferElements=").Append(pipeline.AllocatedLodProfileBufferElementCount);
            builder.Append(" lodProfileBufferBytes=").Append(pipeline.LodProfileBufferBytes);
            builder.Append(" blueprints=").Append(pipeline.BlueprintCount);
            builder.Append(" blueprintBufferElements=").Append(pipeline.AllocatedBlueprintBufferElementCount);
            builder.Append(" blueprintBufferBytes=").Append(pipeline.BlueprintBufferBytes);
            builder.Append(" blueprintPlacements=").Append(pipeline.BlueprintPlacementCount);
            builder.Append(" blueprintPlacementBufferElements=").Append(pipeline.AllocatedBlueprintPlacementBufferElementCount);
            builder.Append(" blueprintPlacementBufferBytes=").Append(pipeline.BlueprintPlacementBufferBytes);
            builder.Append(" branchPrototypes=").Append(pipeline.BranchPrototypeCount);
            builder.Append(" prototypeBufferElements=").Append(pipeline.AllocatedPrototypeBufferElementCount);
            builder.Append(" prototypeBufferBytes=").Append(pipeline.PrototypeBufferBytes);
            builder.Append(" trees=").Append(pipeline.TreeCount);
            builder.Append(" treeBufferElements=").Append(pipeline.AllocatedTreeBufferElementCount);
            builder.Append(" treeBufferBytes=").Append(pipeline.TreeBufferBytes);
            builder.Append(" treeVisibilityBufferElements=").Append(pipeline.AllocatedTreeVisibilityBufferElementCount);
            builder.Append(" treeVisibilityBufferBytes=").Append(pipeline.TreeVisibilityBufferBytes);
            builder.Append(" expandedBranchWorkItemCapacity=").Append(pipeline.ExpandedBranchWorkItemCapacity);
            builder.Append(" expandedBranchWorkItemBufferBytes=").Append(pipeline.ExpandedBranchWorkItemBufferBytes);
            builder.Append(" expandedBranchWorkItemCountBufferBytes=").Append(pipeline.ExpandedBranchWorkItemCountBufferBytes);
            builder.Append(" frameStatsBufferBytes=").Append(pipeline.FrameStatsBufferBytes);
            builder.Append(" drawSlots=").Append(pipeline.DrawSlotCount);
            builder.Append(" slotMetadataBufferBytes=").Append(pipeline.SlotMetadataBufferBytes);
            builder.Append(" slotRequestedCountBufferBytes=").Append(pipeline.SlotRequestedInstanceCountBufferBytes);
            builder.Append(" slotEmittedCountBufferBytes=").Append(pipeline.SlotEmittedInstanceCountBufferBytes);
            builder.Append(" slotPackedStartsBufferBytes=").Append(pipeline.SlotPackedStartsBufferBytes);
            builder.Append(" cellVisibilityBufferBytes=").Append(pipeline.CellVisibilityBufferBytes);
            builder.Append(" totalBranchTelemetryBufferBytes=").Append(pipeline.TotalBranchTelemetryBufferBytes);
            builder.Append(" visibleInstanceCapacity=").Append(maxVisibleInstanceCapacity);
            builder.Append(" visibleInstanceStrideBytes=").Append(pipeline.VisibleInstanceStrideBytes);
            builder.Append(" visibleInstanceCapacityBytes=").Append(pipeline.VisibleInstanceCapacityBytes);
            builder.Append(" indirectArgsBufferBytes=").Append(pipeline.IndirectArgsBufferBytes);
            builder.Append(" totalComputeBufferBytes=").Append(pipeline.TotalComputeBufferBytes);
            builder.Append(" totalGraphicsBufferBytes=").Append(pipeline.TotalGraphicsBufferBytes);
            builder.Append(" totalGpuBufferBytes=").Append(pipeline.TotalGpuBufferBytes);
            if (indirectRenderer != null)
            {
                builder.Append(" registeredDrawSlots=").Append(indirectRenderer.RegisteredDrawSlotCount);
                builder.Append(" runtimeMaterialCopies=").Append(indirectRenderer.RuntimeMaterialCopyCount);
            }

            Debug.Log(builder.ToString(), diagnosticsContext);
            gpuPipelineTelemetryDiagnosticsDirty = false;
        }

        private void LogPreparedFrameTelemetry(bool diagnosticsEnabled, VegetationGpuDecisionPipeline pipeline)
        {
            if (!diagnosticsEnabled || !preparedFrameTelemetryDiagnosticsDirty || registry == null || indirectRenderer == null)
            {
                return;
            }

            StringBuilder builder = new StringBuilder(256);
            builder.Append("AuthoringContainerRuntime preparedFrameTelemetry");
            builder.Append(" container=").Append(debugName);
            builder.Append(" provider=").Append(providerKind);
            builder.Append(" blueprints=").Append(pipeline.BlueprintCount);
            builder.Append(" registeredDrawSlots=").Append(indirectRenderer.RegisteredDrawSlotCount);
            builder.Append(" runtimeMaterialCopies=").Append(indirectRenderer.RuntimeMaterialCopyCount);
            builder.Append(" visibleInstanceCapacity=").Append(maxVisibleInstanceCapacity);
            builder.Append(" visibleInstanceCapacityBytes=").Append(pipeline.VisibleInstanceCapacityBytes);
            builder.Append(" syncReadbackTelemetry=disabled");
            builder.Append(" reason=avoid-gpu-fence-on-render-thread");

            Debug.Log(builder.ToString(), diagnosticsContext);
            preparedFrameTelemetryDiagnosticsDirty = false;
        }

        private void LogPreparationDiagnostics(Camera camera, bool uploadedFrame, bool diagnosticsEnabled)
        {
            if (!diagnosticsEnabled)
            {
                return;
            }

            int cameraInstanceId = camera.GetInstanceID();
            if (registry == null || indirectRenderer == null)
            {
                if (cameraInstanceId != lastPreparationMissingStateCameraInstanceId)
                {
                    lastPreparationMissingStateCameraInstanceId = cameraInstanceId;
                    Debug.LogWarning(
                        $"AuthoringContainerRuntime prepare failed container={debugName} provider={providerKind} camera={camera.name} reason=missing-runtime-state",
                        diagnosticsContext);
                }

                return;
            }

            lastPreparationMissingStateCameraInstanceId = -1;
            int drawSlotCount = indirectRenderer.ActiveSlotIndices.Count;
            if (uploadedFrame && drawSlotCount > 0)
            {
                if (cameraInstanceId == lastPreparationDiagnosticsCameraInstanceId &&
                    uploadedFrame == lastPreparationDiagnosticsUploadedFrame &&
                    drawSlotCount == lastPreparationDiagnosticsDrawSlotCount)
                {
                    return;
                }

                lastPreparationDiagnosticsCameraInstanceId = cameraInstanceId;
                lastPreparationDiagnosticsUploadedFrame = uploadedFrame;
                lastPreparationDiagnosticsDrawSlotCount = drawSlotCount;
                lastPreparationWarningCameraInstanceId = -1;
                lastPreparationWarningDrawSlotCount = -1;
                Debug.Log(
                    string.Format(
                        "AuthoringContainerRuntime prepare container={0} provider={1} camera={2} uploaded={3} source=GpuResident drawSlots={4}",
                        debugName,
                        providerKind,
                        camera.name,
                        uploadedFrame,
                        drawSlotCount),
                    diagnosticsContext);
                return;
            }

            if (cameraInstanceId == lastPreparationWarningCameraInstanceId &&
                uploadedFrame == lastPreparationWarningUploadedFrame &&
                drawSlotCount == lastPreparationWarningDrawSlotCount)
            {
                return;
            }

            lastPreparationWarningCameraInstanceId = cameraInstanceId;
            lastPreparationWarningUploadedFrame = uploadedFrame;
            lastPreparationWarningDrawSlotCount = drawSlotCount;
            Debug.LogWarning(
                string.Format(
                    "AuthoringContainerRuntime prepare container={0} provider={1} camera={2} uploaded={3} source=GpuResident drawSlots={4} reason=no-bound-gpu-resident-draw-slots",
                    debugName,
                    providerKind,
                    camera.name,
                    uploadedFrame,
                    drawSlotCount),
                diagnosticsContext);
        }

        private int CountActiveAuthorings()
        {
            int activeAuthoringCount = 0;
            for (int i = 0; i < authorings.Count; i++)
            {
                if (authorings[i].IsActive)
                {
                    activeAuthoringCount++;
                }
            }

            return activeAuthoringCount;
        }

        private void AppendActiveAuthoringNames(StringBuilder builder, int maxNames)
        {
            int namesWritten = 0;
            for (int i = 0; i < authorings.Count && namesWritten < maxNames; i++)
            {
                VegetationTreeAuthoringRuntime authoring = authorings[i];
                if (!authoring.IsActive)
                {
                    continue;
                }

                if (namesWritten > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(authoring.DebugName);
                namesWritten++;
            }

            if (CountActiveAuthorings() > namesWritten)
            {
                builder.Append(", ...");
            }
        }

        private void ClearRenderRuntimeFault()
        {
            renderRuntimeFaulted = false;
            renderRuntimeFaultStage = null;
            renderRuntimeCleanupPending = false;
        }

        private void LogRenderRuntimeSkip(string stage, string reason)
        {
            if (renderRuntimeFaultStage == reason)
            {
                return;
            }

            renderRuntimeFaultStage = reason;
            Debug.LogWarning(
                $"Vegetation runtime skipped container={debugName} provider={providerKind} stage={stage} reason={reason}",
                diagnosticsContext);
        }
    }
}
