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
        private bool isRegistered;
        private bool isDisposed;

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

            if (string.IsNullOrWhiteSpace(debugName))
            {
                throw new ArgumentException("Debug name must be present.", nameof(debugName));
            }

            this.authorings = authorings ?? throw new ArgumentNullException(nameof(authorings));
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

        /// <summary>
        /// [INTEGRATION] Activates this runtime owner in the shared renderer discovery registry.
        /// </summary>
        public bool Activate()
        {
            ThrowIfDisposed();
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
            ThrowIfDisposed();
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
                lastPreparedCameraInstanceId = -1;
                lastPreparedRenderFrame = -1;
                lastPreparationMissingStateCameraInstanceId = -1;
                lastPreparationWarningCameraInstanceId = -1;
                lastPreparationWarningDrawSlotCount = -1;
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
            lastPreparedCameraInstanceId = -1;
            lastPreparedRenderFrame = -1;
            lastPreparationDiagnosticsCameraInstanceId = -1;
            lastPreparationDiagnosticsDrawSlotCount = -1;
            lastPreparationMissingStateCameraInstanceId = -1;
            lastPreparationWarningCameraInstanceId = -1;
            lastPreparationWarningDrawSlotCount = -1;
        }

        /// <summary>
        /// [INTEGRATION] Prepares exactly one camera-visible GPU-resident frame snapshot for indirect rendering.
        /// </summary>
        public bool PrepareFrameForCamera(Camera camera, ComputeShader? classifyShader, bool diagnosticsEnabled)
        {
            using (PrepareFrameForCameraMarker.Auto())
            {
                ThrowIfDisposed();
                if (camera == null)
                {
                    throw new ArgumentNullException(nameof(camera));
                }

                if (!isRegistered)
                {
                    return false;
                }

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
                    PrepareGpuResidentFrame(cameraWorldPosition, reusableFrustumPlanes, classifyShader);

                lastPreparedCameraInstanceId = cameraInstanceId;
                lastPreparedRenderFrame = renderFrame;
                LogPreparationDiagnostics(camera, uploadedFrame, diagnosticsEnabled);
                return uploadedFrame;
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

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(AuthoringContainerRuntime));
            }
        }

        private void EnsureRuntimeRegistration()
        {
            if (!isRegistered || (registry != null && indirectRenderer != null))
            {
                return;
            }

            RefreshRuntimeRegistration();
        }

        private bool PrepareGpuResidentFrame(Vector3 cameraWorldPosition, Plane[] frustumPlanes, ComputeShader? classifyShader)
        {
            using (PrepareGpuResidentFrameMarker.Auto())
            {
                EnsureGpuDecisionPipeline(classifyShader);
                VegetationGpuDecisionPipeline pipeline = gpuDecisionPipeline!;
                pipeline.PrepareResidentFrame(cameraWorldPosition, frustumPlanes);
                indirectRenderer!.BindGpuResidentFrame(
                    pipeline.ResidentInstanceBuffer,
                    pipeline.ResidentArgsBuffer,
                    pipeline.ResidentSlotPackedStartsBuffer);
                return indirectRenderer.HasUploadedFrame;
            }
        }

        private void EnsureGpuDecisionPipeline(ComputeShader? classifyShader)
        {
            if (registry == null)
            {
                Debug.LogError(
                    $"AuthoringContainerRuntime cannot build the GPU pipeline before runtime registration is available. container={debugName}",
                    diagnosticsContext);
                throw new InvalidOperationException(
                    $"AuthoringContainerRuntime cannot build the GPU pipeline before runtime registration is available. container={debugName}");
            }

            if (classifyShader == null)
            {
                Debug.LogError(
                    "VegetationRendererFeature requires VegetationFoliageFeatureSettings.ClassifyShader to be assigned. Legacy per-container compute-shader wiring was removed.",
                    diagnosticsContext);

                throw new InvalidOperationException(
                    "VegetationRendererFeature requires VegetationFoliageFeatureSettings.ClassifyShader to be assigned. Legacy per-container compute-shader wiring was removed.");
            }

            int shaderInstanceId = classifyShader.GetInstanceID();
            if (gpuDecisionPipeline != null &&
                shaderInstanceId == gpuPipelineShaderInstanceId &&
                maxVisibleInstanceCapacity == gpuPipelineVisibleInstanceCapacity)
            {
                return;
            }

            ResetGpuDecisionPipeline();
            gpuDecisionPipeline = new VegetationGpuDecisionPipeline(
                classifyShader,
                registry,
                maxVisibleInstanceCapacity);
            gpuPipelineShaderInstanceId = shaderInstanceId;
            gpuPipelineVisibleInstanceCapacity = maxVisibleInstanceCapacity;
        }

        private void ResetGpuDecisionPipeline()
        {
            gpuDecisionPipeline?.Dispose();
            gpuDecisionPipeline = null;
            gpuPipelineShaderInstanceId = -1;
            gpuPipelineVisibleInstanceCapacity = -1;
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
            builder.Append(" branches=").Append(registry.SceneBranches.Count);
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
    }
}
