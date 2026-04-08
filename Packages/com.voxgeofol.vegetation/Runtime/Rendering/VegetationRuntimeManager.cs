#nullable enable

using System;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Runtime orchestration hub for Phase D registration, CPU reference evaluation, and GPU decision-path parity hooks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VegetationRuntimeManager : MonoBehaviour
    {
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;
        [SerializeField] private Vector3 cellSize = new Vector3(32f, 32f, 32f);
        [SerializeField] private ComputeShader? vegetationClassifyShader;

        private readonly VegetationCpuReferenceEvaluator cpuReferenceEvaluator = new VegetationCpuReferenceEvaluator();
        private VegetationRuntimeRegistry? registry;
        private VegetationFrameDecisionState? lastDecisionState;
        private VegetationFrameOutput? lastFrameOutput;
        private VegetationGpuDecisionPipeline? gpuDecisionPipeline;

        public VegetationRuntimeRegistry? Registry => registry;

        public VegetationFrameDecisionState? LastDecisionState => lastDecisionState;

        public VegetationFrameOutput? LastFrameOutput => lastFrameOutput;

        /// <summary>
        /// [INTEGRATION] Rebuilds the full Phase D runtime registration snapshot from the current scene vegetation authorings.
        /// </summary>
        public void RefreshRuntimeRegistration()
        {
            ResetAuthoringRuntimeIndices();

            VegetationTreeAuthoring[] authorings = FindObjectsByType<VegetationTreeAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            VegetationRuntimeRegistryBuilder builder = new VegetationRuntimeRegistryBuilder(gridOrigin, cellSize);
            registry = builder.Build(authorings);
            lastDecisionState = new VegetationFrameDecisionState(registry);
            lastFrameOutput = registry.CreateFrameOutput();

            for (int treeIndex = 0; treeIndex < registry.TreeInstances.Count; treeIndex++)
            {
                registry.TreeInstances[treeIndex].Authoring.RefreshRuntimeTreeIndex(treeIndex);
            }

            gpuDecisionPipeline?.Dispose();
            gpuDecisionPipeline = vegetationClassifyShader == null
                ? null
                : new VegetationGpuDecisionPipeline(vegetationClassifyShader, registry);
        }

        /// <summary>
        /// [INTEGRATION] Clears runtime-only registration state and releases GPU resources without touching authoring assets.
        /// </summary>
        public void ResetRuntimeState()
        {
            ResetAuthoringRuntimeIndices();
            gpuDecisionPipeline?.Dispose();
            gpuDecisionPipeline = null;
            registry = null;
            lastDecisionState = null;
            lastFrameOutput = null;
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

            SimulateReferenceFrame(camera.transform.position, GeometryUtility.CalculateFrustumPlanes(camera));
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

        private void OnDisable()
        {
            ResetRuntimeState();
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
    }
}
