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
    public sealed class VegetationRuntimeManager : MonoBehaviour
    {
        private static readonly ProfilerMarker RefreshRuntimeRegistrationMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeManager.RefreshRuntimeRegistration");
        private static readonly ProfilerMarker PrepareFrameForCameraMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeManager.PrepareFrameForCamera");
        private static readonly ProfilerMarker PrepareCpuReferenceFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeManager.PrepareCpuReferenceFrame");
        private static readonly ProfilerMarker PrepareGpuDecisionFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeManager.PrepareGpuDecisionFrame");
        private static readonly ProfilerMarker PrepareGpuResidentFrameMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeManager.PrepareGpuResidentFrame");
        private static readonly ProfilerMarker TryConsumeGpuReadbackMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeManager.TryConsumeGpuReadback");
        private static readonly ProfilerMarker DecodeGpuReadbackMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeManager.DecodeGpuReadback");
        private static readonly ProfilerMarker UploadGpuReadbackMarker = new ProfilerMarker("VoxGeoFol.VegetationRuntimeManager.UploadGpuReadback");
        private static readonly List<VegetationRuntimeManager> ActiveManagersInternal = new List<VegetationRuntimeManager>();

        [SerializeField] private Vector3 gridOrigin = Vector3.zero;
        [SerializeField] private Vector3 cellSize = new Vector3(32f, 32f, 32f);
        [SerializeField] private ComputeShader? vegetationClassifyShader;
        [SerializeField] private VegetationRuntimeFrameSource frameSource = VegetationRuntimeFrameSource.GpuResident;
        [SerializeField] private bool bootstrapCpuReferenceWhileGpuReadbackPending = true;
        [SerializeField] private bool enableDiagnostics;

        private readonly VegetationCpuReferenceEvaluator cpuReferenceEvaluator = new VegetationCpuReferenceEvaluator();
        private readonly Plane[] reusableFrustumPlanes = new Plane[6];
        private readonly Plane[] pendingGpuReadbackFrustumPlanes = new Plane[6];
        private VegetationRuntimeRegistry? registry;
        private VegetationFrameDecisionState? lastDecisionState;
        private VegetationFrameOutput? lastFrameOutput;
        private VegetationGpuDecisionPipeline? gpuDecisionPipeline;
        private VegetationIndirectRenderer? indirectRenderer;
        private VegetationRuntimeFrameSource lastPreparedFrameSource;
        private int lastPreparedCameraInstanceId = -1;
        private int lastPreparedRenderFrame = -1;
        private string lastRegistrationDiagnostics = string.Empty;
        private string lastPreparationDiagnostics = string.Empty;
        private string lastPreparationWarning = string.Empty;

        public VegetationRuntimeRegistry? Registry => registry;

        public VegetationFrameDecisionState? LastDecisionState => lastDecisionState;

        public VegetationFrameOutput? LastFrameOutput => lastFrameOutput;

        public VegetationIndirectRenderer? IndirectRenderer => indirectRenderer;

        public VegetationRuntimeFrameSource LastPreparedFrameSource => lastPreparedFrameSource;

        public bool HasPreparedFrame => indirectRenderer != null && indirectRenderer.HasUploadedFrame;

        internal bool DiagnosticsEnabled => enableDiagnostics;

        public static void GetActiveManagers(List<VegetationRuntimeManager> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Clear();
            for (int i = 0; i < ActiveManagersInternal.Count; i++)
            {
                VegetationRuntimeManager? manager = ActiveManagersInternal[i];
                if (manager != null && manager.isActiveAndEnabled)
                {
                    target.Add(manager);
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

                VegetationTreeAuthoring[] authorings = FindObjectsByType<VegetationTreeAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                VegetationRuntimeRegistryBuilder builder = new VegetationRuntimeRegistryBuilder(gridOrigin, cellSize);
                registry = builder.Build(authorings);
                lastDecisionState = new VegetationFrameDecisionState(registry);
                lastFrameOutput = registry.CreateFrameOutput(enableDiagnostics);
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

                LogRegistrationDiagnostics(authorings);
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
            lastDecisionState = null;
            lastFrameOutput = null;
            lastPreparedCameraInstanceId = -1;
            lastPreparedRenderFrame = -1;
        }

        /// <summary>
        /// [INTEGRATION] Runs the deterministic CPU reference mirror against the provided camera contract.
        /// </summary>
        public void SimulateReferenceFrame(Camera camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            GeometryUtility.CalculateFrustumPlanes(camera, reusableFrustumPlanes);
            SimulateReferenceFrame(camera.transform.position, reusableFrustumPlanes);
        }

        /// <summary>
        /// [INTEGRATION] Runs the deterministic CPU reference mirror against explicit camera data for tests and tooling.
        /// </summary>
        public void SimulateReferenceFrame(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
        {
            if (registry == null || lastDecisionState == null || lastFrameOutput == null)
            {
                throw new InvalidOperationException("VegetationRuntimeManager requires RefreshRuntimeRegistration() before Phase D simulation.");
            }

            cpuReferenceEvaluator.EvaluateFrame(registry, cameraWorldPosition, frustumPlanes, lastDecisionState, lastFrameOutput);
        }

        /// <summary>
        /// [INTEGRATION] Runs the immediate GPU decision-path mirror used by Phase D parity checks.
        /// </summary>
        public VegetationFrameDecisionState SimulateGpuFrameImmediate(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
        {
            if (gpuDecisionPipeline == null)
            {
                throw new InvalidOperationException("VegetationRuntimeManager is missing vegetationClassifyShader or registration has not been refreshed.");
            }

            return gpuDecisionPipeline.EvaluateFrameImmediate(cameraWorldPosition, frustumPlanes);
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
                if (registry == null || lastDecisionState == null || lastFrameOutput == null || indirectRenderer == null)
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

                bool uploadedFrame = frameSource switch
                {
                    VegetationRuntimeFrameSource.GpuDecisionReadback => PrepareGpuDecisionFrame(cameraWorldPosition, reusableFrustumPlanes),
                    VegetationRuntimeFrameSource.GpuResident => PrepareGpuResidentFrame(cameraWorldPosition, reusableFrustumPlanes),
                    _ => PrepareCpuReferenceFrame(cameraWorldPosition, reusableFrustumPlanes)
                };

                lastPreparedCameraInstanceId = cameraInstanceId;
                lastPreparedRenderFrame = renderFrame;
                LogPreparationDiagnostics(camera, uploadedFrame);
                return uploadedFrame;
            }
        }

        private void OnEnable()
        {
            if (!ActiveManagersInternal.Contains(this))
            {
                ActiveManagersInternal.Add(this);
            }

            RefreshRuntimeRegistration();
        }

        private void OnDisable()
        {
            ActiveManagersInternal.Remove(this);
            ResetRuntimeState();
        }

        private void EnsureRuntimeRegistration()
        {
            if (registry != null && lastDecisionState != null && lastFrameOutput != null && indirectRenderer != null)
            {
                return;
            }

            RefreshRuntimeRegistration();
        }

        private bool PrepareCpuReferenceFrame(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
        {
            using (PrepareCpuReferenceFrameMarker.Auto())
            {
                cpuReferenceEvaluator.EvaluateFrame(registry!, cameraWorldPosition, frustumPlanes, lastDecisionState!, lastFrameOutput!);
                indirectRenderer!.UploadFrameOutput(lastFrameOutput!);
                lastPreparedFrameSource = VegetationRuntimeFrameSource.CpuReference;
                return indirectRenderer!.HasUploadedFrame;
            }
        }

        private bool PrepareGpuDecisionFrame(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
        {
            using (PrepareGpuDecisionFrameMarker.Auto())
            {
                if (gpuDecisionPipeline == null)
                {
                    return PrepareCpuReferenceFrame(cameraWorldPosition, frustumPlanes);
                }

                bool uploadedGpuReadback = false;
                VegetationFrameDecisionState? completedState;
                using (TryConsumeGpuReadbackMarker.Auto())
                {
                    completedState = gpuDecisionPipeline.TryConsumeCompletedReadback(out VegetationFrameDecisionState? consumedState)
                        ? consumedState
                        : null;
                }

                if (completedState != null)
                {
                    lastDecisionState = completedState;
                    using (DecodeGpuReadbackMarker.Auto())
                    {
                        VegetationDecisionDecoder.Decode(registry!, lastDecisionState!, pendingGpuReadbackFrustumPlanes, lastFrameOutput!);
                    }

                    using (UploadGpuReadbackMarker.Auto())
                    {
                        indirectRenderer!.UploadFrameOutput(lastFrameOutput!);
                    }

                    lastPreparedFrameSource = VegetationRuntimeFrameSource.GpuDecisionReadback;
                    uploadedGpuReadback = true;
                }

                gpuDecisionPipeline.ScheduleFrameReadback(cameraWorldPosition, frustumPlanes);
                CopyFrustumPlanes(frustumPlanes, pendingGpuReadbackFrustumPlanes);

                if (uploadedGpuReadback)
                {
                    return true;
                }

                // TODO: Changed by user, as we primary will use only GPU-async readback!
                // if (bootstrapCpuReferenceWhileGpuReadbackPending || !indirectRenderer!.HasUploadedFrame)
                // {
                //     return PrepareCpuReferenceFrame(cameraWorldPosition, frustumPlanes);
                // }

                return indirectRenderer!.HasUploadedFrame;
            }
        }

        private bool PrepareGpuResidentFrame(Vector3 cameraWorldPosition, Plane[] frustumPlanes)
        {
            using (PrepareGpuResidentFrameMarker.Auto())
            {
                if (gpuDecisionPipeline == null)
                {
                    return PrepareCpuReferenceFrame(cameraWorldPosition, frustumPlanes);
                }

                gpuDecisionPipeline.PrepareResidentFrame(cameraWorldPosition, frustumPlanes);
                indirectRenderer!.BindGpuResidentFrame(
                    gpuDecisionPipeline.ResidentInstanceBuffer,
                    gpuDecisionPipeline.ResidentArgsBuffer);
                lastPreparedFrameSource = VegetationRuntimeFrameSource.GpuResident;
                return indirectRenderer.HasUploadedFrame;
            }
        }

        private static void CopyFrustumPlanes(IReadOnlyList<Plane> source, Plane[] destination)
        {
            int count = Mathf.Min(source.Count, destination.Length);
            for (int i = 0; i < count; i++)
            {
                destination[i] = source[i];
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
            builder.Append("VegetationRuntimeManager registration");
            builder.Append(" manager=").Append(name);
            builder.Append(" authorings=").Append(authorings.Count);
            builder.Append(" trees=").Append(registry.TreeInstances.Count);
            builder.Append(" branches=").Append(registry.SceneBranches.Count);
            builder.Append(" drawSlots=").Append(registry.DrawSlots.Count);
            builder.Append(" cells=").Append(registry.SpatialGrid.Cells.Count);
            builder.Append(" frameSource=").Append(frameSource);
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
                    $"VegetationRuntimeManager prepare failed manager={name} camera={camera.name} reason=missing-runtime-state";
                if (missingStateSummary != lastPreparationWarning)
                {
                    lastPreparationWarning = missingStateSummary;
                    UnityEngine.Debug.LogWarning(missingStateSummary, this);
                }

                return;
            }

            if (lastPreparedFrameSource == VegetationRuntimeFrameSource.GpuResident)
            {
                string residentSummary = string.Format(
                    "VegetationRuntimeManager prepare manager={0} camera={1} uploaded={2} source={3} drawSlots={4} residentOutput=gpu-only",
                    name,
                    camera.name,
                    uploadedFrame,
                    lastPreparedFrameSource,
                    indirectRenderer.ActiveSlotIndices.Count);

                if (uploadedFrame && indirectRenderer.ActiveSlotIndices.Count > 0)
                {
                    if (residentSummary != lastPreparationDiagnostics)
                    {
                        lastPreparationDiagnostics = residentSummary;
                        UnityEngine.Debug.Log(residentSummary, this);
                    }

                    lastPreparationWarning = string.Empty;
                    return;
                }

                string residentWarning = residentSummary + " reason=no-bound-gpu-resident-draw-slots";
                if (residentWarning != lastPreparationWarning)
                {
                    lastPreparationWarning = residentWarning;
                    UnityEngine.Debug.LogWarning(residentWarning, this);
                }

                return;
            }

            if (lastDecisionState == null || lastFrameOutput == null)
            {
                string missingCpuSummary =
                    $"VegetationRuntimeManager prepare failed manager={name} camera={camera.name} reason=missing-decode-state";
                if (missingCpuSummary != lastPreparationWarning)
                {
                    lastPreparationWarning = missingCpuSummary;
                    UnityEngine.Debug.LogWarning(missingCpuSummary, this);
                }

                return;
            }

            int culledTreeCount = 0;
            int expandedTreeCount = 0;
            int impostorTreeCount = 0;
            for (int treeIndex = 0; treeIndex < lastDecisionState.TreeModes.Length; treeIndex++)
            {
                switch (lastDecisionState.TreeModes[treeIndex])
                {
                    case VegetationTreeRenderMode.Culled:
                        culledTreeCount++;
                        break;
                    case VegetationTreeRenderMode.Expanded:
                        expandedTreeCount++;
                        break;
                    case VegetationTreeRenderMode.Impostor:
                        impostorTreeCount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            int inactiveBranchCount = 0;
            int l0Count = 0;
            int l1Count = 0;
            int l2Count = 0;
            int l3Count = 0;
            for (int branchIndex = 0; branchIndex < lastDecisionState.BranchDecisions.Length; branchIndex++)
            {
                VegetationBranchDecisionRecord branchDecision = lastDecisionState.BranchDecisions[branchIndex];
                if (!branchDecision.IsActive)
                {
                    inactiveBranchCount++;
                    continue;
                }

                switch ((VegetationRuntimeBranchTier)branchDecision.RuntimeTier)
                {
                    case VegetationRuntimeBranchTier.L0:
                        l0Count++;
                        break;
                    case VegetationRuntimeBranchTier.L1:
                        l1Count++;
                        break;
                    case VegetationRuntimeBranchTier.L2:
                        l2Count++;
                        break;
                    case VegetationRuntimeBranchTier.L3:
                        l3Count++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            int visibleInstanceCount = 0;
            StringBuilder slotBuilder = new StringBuilder(192);
            int slotsToLog = Mathf.Min(lastFrameOutput.ActiveSlotIndices.Count, 6);
            for (int slotOffset = 0; slotOffset < lastFrameOutput.ActiveSlotIndices.Count; slotOffset++)
            {
                int slotIndex = lastFrameOutput.ActiveSlotIndices[slotOffset];
                VegetationVisibleSlotOutput slotOutput = lastFrameOutput.SlotOutputs[slotIndex];
                visibleInstanceCount += slotOutput.InstanceCount;

                if (slotOffset < slotsToLog)
                {
                    if (slotBuilder.Length > 0)
                    {
                        slotBuilder.Append(", ");
                    }

                    slotBuilder.Append(slotOutput.DrawSlot.DebugLabel);
                    slotBuilder.Append('x');
                    slotBuilder.Append(slotOutput.InstanceCount);
                }
            }

            string summary = string.Format(
                "VegetationRuntimeManager prepare manager={0} camera={1} uploaded={2} source={3} visibleCells={4}/{5} trees(culled={6},expanded={7},impostor={8}) branches(inactive={9},l0={10},l1={11},l2={12},l3={13}) activeSlots={14} visibleInstances={15} slots=[{16}]",
                name,
                camera.name,
                uploadedFrame,
                lastPreparedFrameSource,
                lastDecisionState.VisibleCellIndices.Count,
                registry.SpatialGrid.Cells.Count,
                culledTreeCount,
                expandedTreeCount,
                impostorTreeCount,
                inactiveBranchCount,
                l0Count,
                l1Count,
                l2Count,
                l3Count,
                lastFrameOutput.ActiveSlotIndices.Count,
                visibleInstanceCount,
                slotBuilder.ToString());

            if (uploadedFrame && lastFrameOutput.ActiveSlotIndices.Count > 0)
            {
                if (summary != lastPreparationDiagnostics)
                {
                    lastPreparationDiagnostics = summary;
                    UnityEngine.Debug.Log(summary, this);
                }

                lastPreparationWarning = string.Empty;
                return;
            }

            string warningSummary = summary + " reason=no-uploaded-visible-slots";
            if (warningSummary != lastPreparationWarning)
            {
                lastPreparationWarning = warningSummary;
                UnityEngine.Debug.LogWarning(warningSummary, this);
            }
        }
    }
}
