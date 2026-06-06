#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// [INTEGRATION] Global compiled-page foliage renderer owner for page/cell culling, active budgets, packet residency, wind payload upload, and grouped indirect submission.
    /// </summary>
    public sealed class VegetationRenderWorld : IDisposable
    {
        private const int IndirectArgsUIntCount = 5;
        private const int PacketLookupRepresentationSlotCount = 5;
        private const long EstimatedNearDetailPacketBytes = 80L;
        private const long EstimatedNearDetailInstanceBytes = 144L;
        private const int PreparationSlotCount = 6;
        private const int RecordedPreparationSlotHoldFrames = 1;
        private const int PreparationCounterPreparedInstanceCount = 0;
        private const int PreparationCounterPreparedPacketCount = 1;
        private const int PreparationCounterPreparedNearDetailPacketCount = 2;
        private const int PreparationCounterPreparedTreeL0PacketCount = 3;
        private const int PreparationCounterPreparedTreeL1PacketCount = 4;
        private const int PreparationCounterPreparedTreeL2PacketCount = 5;
        private const int PreparationCounterPreparedHlodPacketCount = 6;
        private const int PreparationCounterPreparedShadowPacketCount = 7;
        private const int PreparationCounterPreparedActiveGroupCount = 8;
        private const int PreparationCounterPreparedFrustumMask = 9;
        private const int PreparationCounterVisiblePageCount = 10;
        private const int PreparationCounterVisibleCellCount = 11;
        private const int PreparationCounterNearDetailLoadRequestCount = 12;
        private const int PreparationCounterNearDetailEvictedCellCount = 13;
        private const int PreparationCounterNearDetailResidentCellCount = 14;
        private const int PreparationCounterCount = 15;
        private const int PreparationLongCounterNearDetailResidentBytes = 0;
        private const int PreparationLongCounterNearDetailLoadedBytes = 1;
        private const int PreparationLongCounterCount = 2;
        private static readonly VegetationRenderWorld SharedInstance = new VegetationRenderWorld();
        private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare");
        private static readonly ProfilerMarker PrepareEnsureGraphMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.EnsureGraph");
        private static readonly ProfilerMarker PrepareBroadPhaseMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.BroadPhase");
        private static readonly ProfilerMarker PrepareSelectPacketsMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.SelectPackets");
        private static readonly ProfilerMarker RenderMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Render");
        private static readonly ProfilerMarker RenderDrawGroupsMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Render.DrawGroups");
        private static readonly int InstanceBufferId = Shader.PropertyToID("_VegetationInstanceData");
        private static readonly int InstanceBufferBaseOffsetId = Shader.PropertyToID("_VegetationInstanceDataBaseOffset");
        private static readonly int WindStrengthId = Shader.PropertyToID("_VegetationWindStrength");
        private static readonly int WindFrequencyId = Shader.PropertyToID("_VegetationWindFrequency");
        private static readonly int WindDirectionId = Shader.PropertyToID("_VegetationWindDirection");
        private static readonly int LeafFlutterSettingsId = Shader.PropertyToID("_VegetationLeafFlutterSettings");

        private readonly List<ProviderRecord> providers = new List<ProviderRecord>();
        private readonly List<PageRecord> pages = new List<PageRecord>();
        private readonly List<CellRecord> cells = new List<CellRecord>();
        private readonly List<GroupRecord> groups = new List<GroupRecord>();
        private readonly Dictionary<string, int> providerIndicesById = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly VegetationCommandBufferIndirectDrawWrapper commandBufferDrawWrapper = new VegetationCommandBufferIndirectDrawWrapper();
        private readonly VegetationRasterCommandBufferIndirectDrawWrapper rasterCommandBufferDrawWrapper = new VegetationRasterCommandBufferIndirectDrawWrapper();
        private readonly MaterialPropertyBlock sharedPropertyBlock = new MaterialPropertyBlock();
        private readonly Plane[] cameraFrustumPlanes = new Plane[6];

        private long[] nearDetailCellBytes = Array.Empty<long>();
        private PacketRange[] pagePacketRanges = Array.Empty<PacketRange>();
        private PacketRange[] cellPacketRanges = Array.Empty<PacketRange>();
        private int[] packetLookupIndices = Array.Empty<int>();
        private int[] groupInstanceCounts = Array.Empty<int>();
        private int[] groupFrustumMasks = Array.Empty<int>();
        private int[] groupStartInstances = Array.Empty<int>();
        private int[] groupTotalInstanceCounts = Array.Empty<int>();
        private int[][] pageInstanceGroupLocalIndices = Array.Empty<int[]>();
        private int[][] pagePacketValidInstanceCounts = Array.Empty<int[]>();
        private int[] activeGroupIndices = Array.Empty<int>();
        private NativeArray<PreparationPageRecord> preparationPages;
        private NativeArray<PreparationCellRecord> preparationCells;
        private NativeArray<PreparationPacketRecord> preparationPackets;
        private NativeArray<int> preparationPacketLookupIndices;
        private NativeArray<PreparationGroupRecord> preparationGroups;
        private NativeArray<VegetationIndirectInstanceData> preparationStaticInstances;
        private NativeArray<int> preparationNearDetailResidentCellMask;
        private NativeArray<int> preparationNearDetailRequestedCellMask;
        private NativeArray<int> preparationNearDetailLastUsedCellFrame;
        private readonly PreparationSlot[] preparationSlots = new PreparationSlot[PreparationSlotCount];
        private GraphicsBuffer? instanceBuffer;
        private GraphicsBuffer? argsBuffer;
        private int instanceCapacity;
        private int argsGroupCapacity;
        private int preparedInstanceCount;
        private int preparedPacketCount;
        private int preparedNearDetailPacketCount;
        private int preparedTreeL0PacketCount;
        private int preparedTreeL1PacketCount;
        private int preparedTreeL2PacketCount;
        private int preparedHlodPacketCount;
        private int preparedShadowPacketCount;
        private int preparedActiveGroupCount;
        private int preparedFrustumMask;
        private int lastRenderedGroupCount;
        private int lastSkippedGroupCount;
        private int preparedVisiblePageCount;
        private int preparedVisibleCellCount;
        private int preparedNearDetailLoadRequestCount;
        private int preparedNearDetailEvictedCellCount;
        private int nearDetailResidentCellCount;
        private int residencyFrameIndex;
        private long nearDetailResidentBytes;
        private long preparedNearDetailLoadedBytes;
        private float preparedWindStrength;
        private float preparedWindFrequency;
        private Vector4 preparedWindDirection;
        private Vector4 preparedLeafFlutterSettings;
        private bool graphDirty = true;
        private bool hasPreparedFrame;
        private int graphVersion;
        private int renderGraphCameraFrame = -1;
        private int renderGraphCameraId = -1;
        private int renderGraphCameraSettingsHash;
        private int renderGraphCameraPreparationSlot = -1;
        private int renderGraphCameraPreparationVersion;
        private int pendingCameraFrame = -1;
        private int pendingCameraId = -1;
        private int pendingCameraSettingsHash;
        private int renderGraphShadowFrame = -1;
        private int renderGraphShadowSettingsHash;
        private int renderGraphShadowPreparationSlot = -1;
        private int renderGraphShadowPreparationVersion;
        private int completedCameraPreparationSlot = -1;
        private int completedCameraPreparationVersion;
        private int completedShadowPreparationSlot = -1;
        private int completedShadowPreparationVersion;
        private int activeGroupIndexCount;
        private int preparationSlotCursor;
        private int preparationVersion;
        private int maxPreparationInstanceCount;
        private JobHandle preparationDependency;
        private bool lastPrepareUsedCameraCache;
        private bool invalidCompiledPacketsLogged;
        private int invalidCompiledPacketCount;
        private bool disposed;

        private VegetationRenderWorld()
        {
            for (int i = 0; i < preparationSlots.Length; i++)
            {
                preparationSlots[i] = new PreparationSlot(i);
            }
        }

        public static VegetationRenderWorld Shared => SharedInstance;

        public bool HasProviders => providers.Count > 0;

        public int ProviderCount => providers.Count;

        public int PageCount => pages.Count;

        public int GroupCount => groups.Count;

        public int LastPreparedInstanceCount => preparedInstanceCount;

        public int LastPreparedPacketCount => preparedPacketCount;

        public int LastPreparedNearDetailPacketCount => preparedNearDetailPacketCount;

        public int LastPreparedTreeL0PacketCount => preparedTreeL0PacketCount;

        public int LastPreparedTreeL1PacketCount => preparedTreeL1PacketCount;

        public int LastPreparedTreeL2PacketCount => preparedTreeL2PacketCount;

        public int LastPreparedHlodPacketCount => preparedHlodPacketCount;

        public int LastPreparedShadowPacketCount => preparedShadowPacketCount;

        public int LastPreparedActiveGroupCount => preparedActiveGroupCount;

        public int LastPreparedFrustumMask => preparedFrustumMask;

        public int LastRenderedGroupCount => lastRenderedGroupCount;

        public int LastSkippedGroupCount => lastSkippedGroupCount;

        public int LastPreparedVisiblePageCount => preparedVisiblePageCount;

        public int LastPreparedVisibleCellCount => preparedVisibleCellCount;

        public int LastPreparedNearDetailLoadRequestCount => preparedNearDetailLoadRequestCount;

        public int LastPreparedNearDetailEvictedPageCount => preparedNearDetailEvictedCellCount;

        public int LastPreparedNearDetailEvictedCellCount => preparedNearDetailEvictedCellCount;

        public long LastPreparedNearDetailLoadedBytes => preparedNearDetailLoadedBytes;

        public int NearDetailResidentPageCount => nearDetailResidentCellCount;

        public int NearDetailResidentCellCount => nearDetailResidentCellCount;

        public long NearDetailResidentBytes => nearDetailResidentBytes;

        public bool LastPrepareUsedCameraCache => lastPrepareUsedCameraCache;

        /// <summary>
        /// [INTEGRATION] Registers or replaces one compiled page provider. Source authoring is not consumed at runtime.
        /// </summary>
        public void RegisterProvider(
            string providerId,
            string debugName,
            FoliageAssemblyAsset assemblyAsset,
            IReadOnlyList<FoliagePageAsset> pageAssets)
        {
            // Range: compiled assembly plus generated pages for one provider. Condition: invalid or empty payload removes the provider. Output: global packet graph is rebuilt before the next prepare.
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("Provider id is required.", nameof(providerId));
            }

            if (assemblyAsset == null || pageAssets == null || pageAssets.Count == 0)
            {
                UnregisterProvider(providerId);
                return;
            }

            List<FoliagePageAsset> validPages = new List<FoliagePageAsset>(pageAssets.Count);
            for (int i = 0; i < pageAssets.Count; i++)
            {
                FoliagePageAsset pageAsset = pageAssets[i];
                if (pageAsset != null)
                {
                    validPages.Add(pageAsset);
                }
            }

            if (validPages.Count == 0)
            {
                UnregisterProvider(providerId);
                return;
            }

            ProviderRecord provider = new ProviderRecord(
                providerId,
                debugName ?? string.Empty,
                assemblyAsset,
                CopyAssetGroups(assemblyAsset.AssetGroups),
                validPages.ToArray());

            if (providerIndicesById.TryGetValue(providerId, out int existingIndex))
            {
                providers[existingIndex] = provider;
            }
            else
            {
                providerIndicesById.Add(providerId, providers.Count);
                providers.Add(provider);
            }

            MarkGraphDirty();
        }

        /// <summary>
        /// [INTEGRATION] Unregisters one compiled page provider and releases global render resources if the world becomes empty.
        /// </summary>
        public void UnregisterProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId) ||
                !providerIndicesById.TryGetValue(providerId, out int providerIndex))
            {
                return;
            }

            providers.RemoveAt(providerIndex);
            RebuildProviderIndexMap();
            MarkGraphDirty();
            if (providers.Count == 0)
            {
                ClearCompiledGraph();
                ReleaseGpuBuffers();
            }
        }

        /// <summary>
        /// [INTEGRATION] Releases all compiled providers and GPU-side world resources.
        /// </summary>
        public void Reset()
        {
            providers.Clear();
            providerIndicesById.Clear();
            ClearCompiledGraph();
            ReleaseGpuBuffers();
            MarkGraphDirty();
        }

        /// <summary>
        /// [INTEGRATION] Schedules one RenderGraph-owned camera preparation job without completing it on the caller thread.
        /// </summary>
        internal bool ScheduleRenderGraphPrepareForCamera(
            Camera camera,
            VegetationRenderPassMode passMode,
            VegetationFoliageFeatureSettings settings,
            out VegetationRenderGraphFrame frame)
        {
            // Range: one camera and one non-shadow pass. Condition: records only an already-completed preparation frame, then schedules the next frame without waiting. Output: a renderable preparation frame imported by RenderGraph.
            frame = default;
            lastPrepareUsedCameraCache = false;
            if (camera == null || passMode == VegetationRenderPassMode.Shadow)
            {
                return false;
            }

            int renderFrame = Time.renderedFrameCount;
            int cameraId = camera.GetInstanceID();
            int settingsHash = ComputePrepareSettingsHash(settings, passMode);

            using (PrepareMarker.Auto())
            {
                using (PrepareEnsureGraphMarker.Auto())
                {
                    EnsureCompiledGraph();
                    RefreshCompletedPreparationSlots();
                    if (groups.Count == 0 || pages.Count == 0)
                    {
                        ClearPreparedFrame();
                        return false;
                    }
                }

                if (TryCreateRecordedCameraFrame(renderFrame, cameraId, settingsHash, out frame))
                {
                    lastPrepareUsedCameraCache = true;
                    return true;
                }

                bool hasCompletedFrame = TryCreateCompletedCameraFrame(cameraId, settingsHash, out frame);
                if (hasCompletedFrame)
                {
                    CacheRecordedCameraFrame(renderFrame, cameraId, settingsHash, frame);
                }

                using (PrepareBroadPhaseMarker.Auto())
                {
                    GeometryUtility.CalculateFrustumPlanes(camera, cameraFrustumPlanes);
                }

                using (PrepareSelectPacketsMarker.Auto())
                {
                    TryScheduleCameraPreparation(
                        renderFrame,
                        cameraId,
                        settingsHash,
                        camera.transform.position,
                        passMode,
                        settings,
                        cameraFrustumPlanes);
                    return hasCompletedFrame;
                }
            }
        }

        /// <summary>
        /// [INTEGRATION] Schedules and immediately completes one camera preparation frame for EditMode tests that cannot execute RenderGraph.
        /// </summary>
        internal bool PrepareRenderGraphCameraImmediateForTests(
            Camera camera,
            VegetationRenderPassMode passMode,
            VegetationFoliageFeatureSettings settings)
        {
            // Range: EditMode validation only. Condition: uses the same scheduling path as RenderGraph, then completes the job and CPU buffer upload synchronously. Output: diagnostics counters match the production scheduled frame.
            lastPrepareUsedCameraCache = false;
            if (camera == null || passMode == VegetationRenderPassMode.Shadow)
            {
                return false;
            }

            int renderFrame = Time.renderedFrameCount;
            int cameraId = camera.GetInstanceID();
            int settingsHash = ComputePrepareSettingsHash(settings, passMode);
            if (TryCreateRecordedCameraFrame(renderFrame, cameraId, settingsHash, out VegetationRenderGraphFrame frame))
            {
                lastPrepareUsedCameraCache = true;
                CompletePreparationImmediate(frame);
                return hasPreparedFrame;
            }

            CompletePreparationSlots();
            using (PrepareMarker.Auto())
            {
                using (PrepareEnsureGraphMarker.Auto())
                {
                    EnsureCompiledGraph();
                    if (groups.Count == 0 || pages.Count == 0)
                    {
                        ClearPreparedFrame();
                        return false;
                    }
                }

                using (PrepareBroadPhaseMarker.Auto())
                {
                    GeometryUtility.CalculateFrustumPlanes(camera, cameraFrustumPlanes);
                }

                if (!TrySchedulePreparationJob(
                        camera.transform.position,
                        passMode,
                        settings,
                        cameraFrustumPlanes,
                        1,
                        renderFrame,
                        cameraId,
                        settingsHash,
                        out PreparationSlot slot))
                {
                    return false;
                }

                TryFinalizePreparationSlot(slot, allowBlocking: true);
                frame = CreateRenderGraphFrame(slot);
                CacheRecordedCameraFrame(renderFrame, cameraId, settingsHash, frame);
            }

            CompletePreparationImmediate(frame);
            return hasPreparedFrame;
        }

        /// <summary>
        /// [INTEGRATION] Schedules and immediately completes one explicit-frustum preparation frame for EditMode tests that cannot execute RenderGraph.
        /// </summary>
        internal bool PrepareRenderGraphFrustumImmediateForTests(
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            VegetationFoliageFeatureSettings settings)
        {
            // Range: EditMode validation only. Condition: uses the same shadow scheduling path as RenderGraph with one frustum. Output: diagnostics counters match the production scheduled shadow frame.
            return PrepareRenderGraphFrustumsImmediateForTests(cameraWorldPosition, frustumPlanes, 1, settings);
        }

        /// <summary>
        /// [INTEGRATION] Schedules and immediately completes one batched-frustum preparation frame for EditMode tests that cannot execute RenderGraph.
        /// </summary>
        internal bool PrepareRenderGraphFrustumsImmediateForTests(
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            int frustumCount,
            VegetationFoliageFeatureSettings settings)
        {
            // Range: EditMode validation only. Condition: uses the same shadow scheduling path as RenderGraph, then completes the job and CPU buffer upload synchronously. Output: diagnostics counters match the production scheduled shadow frame.
            CompletePreparationSlots();
            int validatedFrustumCount = ResolveFrustumCount(frustumPlanes, frustumCount);
            if (validatedFrustumCount <= 0 || settings.ShadowMode == VegetationShadowMode.Off)
            {
                return false;
            }

            using (PrepareMarker.Auto())
            {
                using (PrepareEnsureGraphMarker.Auto())
                {
                    EnsureCompiledGraph();
                    if (groups.Count == 0 || pages.Count == 0)
                    {
                        ClearPreparedFrame();
                        return false;
                    }
                }

                int settingsHash = ComputePrepareSettingsHash(settings, VegetationRenderPassMode.Shadow);
                if (!TrySchedulePreparationJob(
                    cameraWorldPosition,
                    VegetationRenderPassMode.Shadow,
                    settings,
                    frustumPlanes,
                    validatedFrustumCount,
                    Time.renderedFrameCount,
                    0,
                    settingsHash,
                    out PreparationSlot slot))
                {
                    return false;
                }

                TryFinalizePreparationSlot(slot, allowBlocking: true);
                VegetationRenderGraphFrame frame = CreateRenderGraphFrame(slot);
                CompletePreparationImmediate(frame);
                return hasPreparedFrame;
            }
        }

        /// <summary>
        /// [INTEGRATION] Schedules one RenderGraph-owned shadow-frustum preparation job without completing it on the caller thread.
        /// </summary>
        internal bool ScheduleRenderGraphPrepareForFrustums(
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            int frustumCount,
            VegetationFoliageFeatureSettings settings,
            out VegetationRenderGraphFrame frame)
        {
            // Range: one or more main-light shadow cascade frustums. Condition: records only an already-completed shadow preparation frame, then schedules the next shadow frame without waiting. Output: a renderable preparation frame imported by RenderGraph.
            frame = default;
            int validatedFrustumCount = ResolveFrustumCount(frustumPlanes, frustumCount);
            if (validatedFrustumCount <= 0 || settings.ShadowMode == VegetationShadowMode.Off)
            {
                return false;
            }

            int renderFrame = Time.renderedFrameCount;
            int settingsHash = ComputePrepareSettingsHash(settings, VegetationRenderPassMode.Shadow);
            using (PrepareMarker.Auto())
            {
                using (PrepareEnsureGraphMarker.Auto())
                {
                    EnsureCompiledGraph();
                    RefreshCompletedPreparationSlots();
                    if (groups.Count == 0 || pages.Count == 0)
                    {
                        ClearPreparedFrame();
                        return false;
                    }
                }

                bool hasCompletedFrame = TryCreateRecordedShadowFrame(renderFrame, settingsHash, out frame);
                if (!hasCompletedFrame)
                {
                    hasCompletedFrame = TryCreateCompletedShadowFrame(settingsHash, out frame);
                    if (hasCompletedFrame)
                    {
                        CacheRecordedShadowFrame(renderFrame, settingsHash, frame);
                    }
                }

                using (PrepareBroadPhaseMarker.Auto())
                {
                    TrySchedulePreparationJob(
                        cameraWorldPosition,
                        VegetationRenderPassMode.Shadow,
                        settings,
                        frustumPlanes,
                        validatedFrustumCount,
                        renderFrame,
                        0,
                        settingsHash,
                        out _);
                    return hasCompletedFrame;
                }
            }
        }

        /// <summary>
        /// [INTEGRATION] Renders the last prepared grouped packet frame through a render-graph raster command buffer.
        /// </summary>
        internal void Render(
            IRasterCommandBuffer commandBuffer,
            Camera camera,
            VegetationRenderPassMode passMode,
            bool diagnosticsEnabled,
            int shadowFrustumIndex = -1)
        {
            rasterCommandBufferDrawWrapper.RefreshCommandBuffer(commandBuffer);
            try
            {
                RenderInternal(camera, passMode, rasterCommandBufferDrawWrapper, diagnosticsEnabled, shadowFrustumIndex);
            }
            finally
            {
                rasterCommandBufferDrawWrapper.ClearCommandBuffer();
            }
        }

        /// <summary>
        /// [INTEGRATION] Renders the last prepared grouped packet frame through a compatibility command buffer.
        /// </summary>
        internal void Render(
            CommandBuffer commandBuffer,
            Camera camera,
            VegetationRenderPassMode passMode,
            bool diagnosticsEnabled,
            int shadowFrustumIndex = -1)
        {
            commandBufferDrawWrapper.RefreshCommandBuffer(commandBuffer);
            try
            {
                RenderInternal(camera, passMode, commandBufferDrawWrapper, diagnosticsEnabled, shadowFrustumIndex);
            }
            finally
            {
                commandBufferDrawWrapper.ClearCommandBuffer();
            }
        }

        internal void CompleteRenderGraphPreparation(
            in VegetationRenderGraphFrame frame,
            ComputeCommandBuffer commandBuffer,
            BufferHandle instanceBufferHandle,
            BufferHandle argsBufferHandle)
        {
            if (!TryResolveCompletedPreparationSlot(frame, out PreparationSlot slot))
            {
                return;
            }

            if (slot.UploadedVersion == slot.Version)
            {
                return;
            }

            int instanceUploadCount = Math.Max(1, slot.Counters[PreparationCounterPreparedInstanceCount]);
            int argsUploadCount = Math.Max(1, slot.ArgsEntryCount * IndirectArgsUIntCount);
            commandBuffer.SetBufferData(instanceBufferHandle, slot.InstanceData, 0, 0, instanceUploadCount);
            commandBuffer.SetBufferData(argsBufferHandle, slot.ArgsData, 0, 0, argsUploadCount);
            slot.UploadedVersion = slot.Version;
        }

        internal bool ActivateRenderGraphPreparedFrame(in VegetationRenderGraphFrame frame)
        {
            if (!TryResolveCompletedPreparationSlot(frame, out PreparationSlot slot))
            {
                return false;
            }

            return ActivatePreparationSlot(slot);
        }

        private void CompletePreparationImmediate(in VegetationRenderGraphFrame frame)
        {
            if (!TryResolvePreparationSlot(frame, out PreparationSlot slot))
            {
                ClearPreparedFrame();
                return;
            }

            if (!TryFinalizePreparationSlot(slot, allowBlocking: true) ||
                !ActivatePreparationSlot(slot))
            {
                ClearPreparedFrame();
                return;
            }

            if (slot.UploadedVersion == slot.Version)
            {
                return;
            }

            int instanceUploadCount = Math.Max(1, slot.Counters[PreparationCounterPreparedInstanceCount]);
            int argsUploadCount = Math.Max(1, slot.ArgsEntryCount * IndirectArgsUIntCount);
            instanceBuffer!.SetData(slot.InstanceData, 0, 0, instanceUploadCount);
            argsBuffer!.SetData(slot.ArgsData, 0, 0, argsUploadCount);
            slot.UploadedVersion = slot.Version;
        }

        private bool TryResolvePreparationSlot(in VegetationRenderGraphFrame frame, out PreparationSlot slot)
        {
            slot = null!;
            int slotIndex = frame.PreparationSlotIndex;
            if (slotIndex < 0 || slotIndex >= preparationSlots.Length)
            {
                return false;
            }

            PreparationSlot candidate = preparationSlots[slotIndex];
            if (candidate.Version != frame.PreparationVersion)
            {
                return false;
            }

            slot = candidate;
            return true;
        }

        private bool TryResolveCompletedPreparationSlot(in VegetationRenderGraphFrame frame, out PreparationSlot slot)
        {
            if (!TryResolvePreparationSlot(frame, out slot))
            {
                return false;
            }

            return slot.CompletedVersion == slot.Version;
        }

        private VegetationRenderGraphFrame CreateRenderGraphFrame(PreparationSlot slot)
        {
            return new VegetationRenderGraphFrame(
                instanceBuffer!,
                argsBuffer!,
                slot.Counters[PreparationCounterPreparedInstanceCount],
                slot.Counters[PreparationCounterPreparedPacketCount],
                slot.Counters[PreparationCounterPreparedActiveGroupCount],
                slot.Counters[PreparationCounterPreparedFrustumMask],
                graphVersion,
                slot.SlotIndex,
                slot.Version);
        }

        private int ResolvePreparationArgsEntryCount(VegetationRenderPassMode passMode, int frustumCount)
        {
            int groupCount = Math.Max(1, groups.Count);
            return passMode == VegetationRenderPassMode.Shadow
                ? groupCount * Math.Max(1, frustumCount)
                : groupCount;
        }

        private int ResolvePreparationInstanceCapacity(VegetationRenderPassMode passMode, int frustumCount)
        {
            int baseCount = Math.Max(1, maxPreparationInstanceCount);
            return passMode == VegetationRenderPassMode.Shadow
                ? baseCount * Math.Max(1, frustumCount)
                : baseCount;
        }

        private void RefreshCompletedPreparationSlots()
        {
            for (int i = 0; i < preparationSlots.Length; i++)
            {
                TryFinalizePreparationSlot(preparationSlots[i], allowBlocking: false);
            }
        }

        private bool TryCreateRecordedCameraFrame(
            int renderFrame,
            int cameraId,
            int settingsHash,
            out VegetationRenderGraphFrame frame)
        {
            frame = default;
            if (renderGraphCameraFrame != renderFrame ||
                renderGraphCameraId != cameraId ||
                renderGraphCameraSettingsHash != settingsHash ||
                renderGraphCameraPreparationSlot < 0)
            {
                return false;
            }

            return TryCreateFrameFromCompletedSlot(
                renderGraphCameraPreparationSlot,
                renderGraphCameraPreparationVersion,
                cameraId,
                settingsHash,
                VegetationRenderPassMode.Color,
                out frame);
        }

        private void CacheRecordedCameraFrame(
            int renderFrame,
            int cameraId,
            int settingsHash,
            in VegetationRenderGraphFrame frame)
        {
            renderGraphCameraFrame = renderFrame;
            renderGraphCameraId = cameraId;
            renderGraphCameraSettingsHash = settingsHash;
            renderGraphCameraPreparationSlot = frame.PreparationSlotIndex;
            renderGraphCameraPreparationVersion = frame.PreparationVersion;
            if (TryResolveCompletedPreparationSlot(frame, out PreparationSlot slot))
            {
                slot.LastRecordedFrame = renderFrame;
            }
        }

        private bool TryCreateCompletedCameraFrame(
            int cameraId,
            int settingsHash,
            out VegetationRenderGraphFrame frame)
        {
            return TryCreateFrameFromCompletedSlot(
                completedCameraPreparationSlot,
                completedCameraPreparationVersion,
                cameraId,
                settingsHash,
                VegetationRenderPassMode.Color,
                out frame);
        }

        private bool TryCreateRecordedShadowFrame(int renderFrame, int settingsHash, out VegetationRenderGraphFrame frame)
        {
            frame = default;
            if (renderGraphShadowFrame != renderFrame ||
                renderGraphShadowSettingsHash != settingsHash ||
                renderGraphShadowPreparationSlot < 0)
            {
                return false;
            }

            return TryCreateFrameFromCompletedSlot(
                renderGraphShadowPreparationSlot,
                renderGraphShadowPreparationVersion,
                cameraId: 0,
                settingsHash,
                VegetationRenderPassMode.Shadow,
                out frame);
        }

        private void CacheRecordedShadowFrame(int renderFrame, int settingsHash, in VegetationRenderGraphFrame frame)
        {
            renderGraphShadowFrame = renderFrame;
            renderGraphShadowSettingsHash = settingsHash;
            renderGraphShadowPreparationSlot = frame.PreparationSlotIndex;
            renderGraphShadowPreparationVersion = frame.PreparationVersion;
            if (TryResolveCompletedPreparationSlot(frame, out PreparationSlot slot))
            {
                slot.LastRecordedFrame = renderFrame;
            }
        }

        private bool TryCreateCompletedShadowFrame(int settingsHash, out VegetationRenderGraphFrame frame)
        {
            return TryCreateFrameFromCompletedSlot(
                completedShadowPreparationSlot,
                completedShadowPreparationVersion,
                cameraId: 0,
                settingsHash,
                VegetationRenderPassMode.Shadow,
                out frame);
        }

        private bool TryCreateFrameFromCompletedSlot(
            int slotIndex,
            int slotVersion,
            int cameraId,
            int settingsHash,
            VegetationRenderPassMode passMode,
            out VegetationRenderGraphFrame frame,
            bool requireSettingsMatch = true)
        {
            frame = default;
            if (slotIndex < 0 || slotIndex >= preparationSlots.Length)
            {
                return false;
            }

            PreparationSlot slot = preparationSlots[slotIndex];
            if (slot.Version != slotVersion ||
                slot.CompletedVersion != slotVersion ||
                slot.GraphVersion != graphVersion ||
                (slot.PassMode != passMode &&
                 !(passMode != VegetationRenderPassMode.Shadow && slot.PassMode != VegetationRenderPassMode.Shadow)))
            {
                return false;
            }

            if (requireSettingsMatch &&
                (slot.CameraId != cameraId || slot.SettingsHash != settingsHash))
            {
                return false;
            }

            frame = CreateRenderGraphFrame(slot);
            return frame.InstanceCount > 0 && frame.ActiveGroupCount > 0;
        }

        private void TryScheduleCameraPreparation(
            int renderFrame,
            int cameraId,
            int settingsHash,
            Vector3 cameraWorldPosition,
            VegetationRenderPassMode passMode,
            VegetationFoliageFeatureSettings settings,
            Plane[] frustumPlanes)
        {
            if (pendingCameraFrame == renderFrame &&
                pendingCameraId == cameraId &&
                pendingCameraSettingsHash == settingsHash)
            {
                return;
            }

            if (TrySchedulePreparationJob(
                    cameraWorldPosition,
                    passMode,
                    settings,
                    frustumPlanes,
                    1,
                    renderFrame,
                    cameraId,
                    settingsHash,
                    out _))
            {
                pendingCameraFrame = renderFrame;
                pendingCameraId = cameraId;
                pendingCameraSettingsHash = settingsHash;
            }
        }

        private void CacheCompletedPreparationSlot(PreparationSlot slot)
        {
            if (slot.GraphVersion != graphVersion ||
                slot.CompletedVersion != slot.Version ||
                !slot.Counters.IsCreated ||
                slot.Counters.Length <= PreparationCounterPreparedActiveGroupCount ||
                slot.Counters[PreparationCounterPreparedInstanceCount] <= 0 ||
                slot.Counters[PreparationCounterPreparedActiveGroupCount] <= 0)
            {
                return;
            }

            if (slot.PassMode == VegetationRenderPassMode.Shadow)
            {
                completedShadowPreparationSlot = slot.SlotIndex;
                completedShadowPreparationVersion = slot.Version;
                return;
            }

            completedCameraPreparationSlot = slot.SlotIndex;
            completedCameraPreparationVersion = slot.Version;
        }

        private bool IsPreparationSlotPinnedForCurrentRenderGraph(PreparationSlot slot)
        {
            int renderFrame = Time.renderedFrameCount;
            if (slot.LastRecordedFrame >= 0)
            {
                int recordedAge = renderFrame - slot.LastRecordedFrame;
                if (recordedAge >= 0 && recordedAge <= RecordedPreparationSlotHoldFrames)
                {
                    return true;
                }
            }

            return (renderGraphCameraFrame == renderFrame &&
                    renderGraphCameraPreparationSlot == slot.SlotIndex &&
                    renderGraphCameraPreparationVersion == slot.Version) ||
                   (renderGraphShadowFrame == renderFrame &&
                    renderGraphShadowPreparationSlot == slot.SlotIndex &&
                    renderGraphShadowPreparationVersion == slot.Version);
        }

        private void InvalidatePreparationReferences(PreparationSlot slot)
        {
            if (completedCameraPreparationSlot == slot.SlotIndex &&
                completedCameraPreparationVersion == slot.Version)
            {
                completedCameraPreparationSlot = -1;
                completedCameraPreparationVersion = 0;
            }

            if (completedShadowPreparationSlot == slot.SlotIndex &&
                completedShadowPreparationVersion == slot.Version)
            {
                completedShadowPreparationSlot = -1;
                completedShadowPreparationVersion = 0;
            }

            if (renderGraphCameraPreparationSlot == slot.SlotIndex &&
                renderGraphCameraPreparationVersion == slot.Version)
            {
                renderGraphCameraFrame = -1;
                renderGraphCameraId = -1;
                renderGraphCameraSettingsHash = 0;
                renderGraphCameraPreparationSlot = -1;
                renderGraphCameraPreparationVersion = 0;
            }

            if (renderGraphShadowPreparationSlot == slot.SlotIndex &&
                renderGraphShadowPreparationVersion == slot.Version)
            {
                renderGraphShadowFrame = -1;
                renderGraphShadowSettingsHash = 0;
                renderGraphShadowPreparationSlot = -1;
                renderGraphShadowPreparationVersion = 0;
            }
        }

        private bool TrySchedulePreparationJob(
            Vector3 cameraWorldPosition,
            VegetationRenderPassMode passMode,
            VegetationFoliageFeatureSettings settings,
            Plane[] frustumPlanes,
            int frustumCount,
            int renderFrame,
            int cameraId,
            int settingsHash,
            out PreparationSlot slot)
        {
            slot = null!;
            int validatedFrustumCount = ResolveFrustumCount(frustumPlanes, frustumCount);
            if (!TryAcquirePreparationSlot(out slot))
            {
                return false;
            }

            int argsEntryCount = ResolvePreparationArgsEntryCount(passMode, validatedFrustumCount);
            int instanceCount = ResolvePreparationInstanceCapacity(passMode, validatedFrustumCount);
            EnsureGpuBuffers(instanceCount, argsEntryCount);
            EnsurePreparationSlotCapacity(
                slot,
                instanceCount,
                Math.Max(1, groups.Count),
                argsEntryCount,
                Math.Max(1, cells.Count),
                Math.Max(6, validatedFrustumCount * 6));
            CopyFrustumPlanesToSlot(slot, frustumPlanes, validatedFrustumCount);
            BeginScheduledResidencyFrame();
            ApplyPreparationWindSettings(slot, settings);

            unchecked
            {
                preparationVersion++;
                if (preparationVersion == 0)
                {
                    preparationVersion = 1;
                }
            }

            slot.Version = preparationVersion;
            slot.UploadedVersion = 0;
            slot.CompletedVersion = 0;
            slot.PassMode = passMode;
            slot.RenderFrame = renderFrame;
            slot.CameraId = cameraId;
            slot.SettingsHash = settingsHash;
            slot.GraphVersion = graphVersion;
            slot.LastRecordedFrame = -1;
            slot.GroupCount = groups.Count;
            slot.ArgsEntryCount = argsEntryCount;
            slot.FrustumCount = validatedFrustumCount;
            slot.JobHandle = new PrepareFrameJob
            {
                Pages = preparationPages,
                Cells = preparationCells,
                Packets = preparationPackets,
                PacketLookupIndices = preparationPacketLookupIndices,
                Groups = preparationGroups,
                StaticInstances = preparationStaticInstances,
                NearDetailResidentCellMask = preparationNearDetailResidentCellMask,
                NearDetailRequestedCellMask = preparationNearDetailRequestedCellMask,
                NearDetailLastUsedCellFrame = preparationNearDetailLastUsedCellFrame,
                FrustumPlanes = slot.FrustumPlanes,
                CellCandidates = slot.CellCandidates,
                SelectedPackets = slot.SelectedPackets,
                PageVisibleMask = slot.PageVisibleMask,
                InstanceData = slot.InstanceData,
                ArgsData = slot.ArgsData,
                GroupInstanceCounts = slot.GroupInstanceCounts,
                GroupStartInstances = slot.GroupStartInstances,
                GroupWriteOffsets = slot.GroupWriteOffsets,
                GroupFrustumMasks = slot.GroupFrustumMasks,
                Counters = slot.Counters,
                LongCounters = slot.LongCounters,
                CameraWorldPosition = cameraWorldPosition,
                PassMode = (int)passMode,
                FrustumCount = validatedFrustumCount,
                AllFrustumMask = AllFrustumBits(validatedFrustumCount),
                GroupCount = groups.Count,
                ArgsEntryCount = argsEntryCount,
                CellCount = cells.Count,
                ResidencyFrameIndex = residencyFrameIndex,
                WorkBudget = settings.GetWorkBudget(passMode),
                InstanceBudget = Mathf.Max(1, settings.MaxVisiblePacketInstances),
                ResidentByteBudget = settings.GetNearDetailResidentByteBudget(),
                UploadByteBudget = settings.GetNearDetailUploadByteBudget(),
                NearDistanceSqr = Mathf.Max(1f, settings.NearDetailDistance) * Mathf.Max(1f, settings.NearDetailDistance)
            }.Schedule(preparationDependency);
            preparationDependency = slot.JobHandle;
            JobHandle.ScheduleBatchedJobs();
            return true;
        }

        private bool TryAcquirePreparationSlot(out PreparationSlot slot)
        {
            slot = null!;
            for (int attempt = 0; attempt < preparationSlots.Length; attempt++)
            {
                int slotIndex = (preparationSlotCursor + attempt) % preparationSlots.Length;
                PreparationSlot candidate = preparationSlots[slotIndex];
                if (IsPreparationSlotPinnedForCurrentRenderGraph(candidate))
                {
                    continue;
                }

                if (candidate.Version != 0 &&
                    candidate.CompletedVersion != candidate.Version &&
                    !TryFinalizePreparationSlot(candidate, allowBlocking: false))
                {
                    continue;
                }

                InvalidatePreparationReferences(candidate);
                preparationSlotCursor = (slotIndex + 1) % preparationSlots.Length;
                slot = candidate;
                return true;
            }

            return false;
        }

        private bool TryFinalizePreparationSlot(PreparationSlot slot, bool allowBlocking)
        {
            if (slot.Version == 0)
            {
                return false;
            }

            if (slot.CompletedVersion == slot.Version)
            {
                return true;
            }

            if (!allowBlocking && !slot.JobHandle.IsCompleted)
            {
                return false;
            }

            slot.JobHandle.Complete();
            slot.CompletedVersion = slot.Version;
            CacheCompletedPreparationSlot(slot);
            return true;
        }

        private bool ActivatePreparationSlot(PreparationSlot slot)
        {
            if (slot.CompletedVersion != slot.Version)
            {
                return false;
            }

            activeGroupIndexCount = 0;
            EnsureActiveGroupIndexCapacity(groups.Count);
            EnsurePreparedGroupStateCapacity(Math.Max(groups.Count, slot.ArgsEntryCount));
            int groupEntryCount = Math.Min(slot.ArgsEntryCount, groupInstanceCounts.Length);
            for (int entryIndex = 0; entryIndex < groupEntryCount; entryIndex++)
            {
                groupStartInstances[entryIndex] = slot.GroupStartInstances[entryIndex];
                groupInstanceCounts[entryIndex] = slot.GroupInstanceCounts[entryIndex];
            }

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                int groupFrustumMask = groupIndex < slot.GroupFrustumMasks.Length
                    ? slot.GroupFrustumMasks[groupIndex]
                    : 0;
                if (groupFrustumMask != 0)
                {
                    activeGroupIndices[activeGroupIndexCount++] = groupIndex;
                }

                groupFrustumMasks[groupIndex] = groupFrustumMask;
            }

            preparedInstanceCount = slot.Counters[PreparationCounterPreparedInstanceCount];
            preparedPacketCount = slot.Counters[PreparationCounterPreparedPacketCount];
            preparedNearDetailPacketCount = slot.Counters[PreparationCounterPreparedNearDetailPacketCount];
            preparedTreeL0PacketCount = slot.Counters[PreparationCounterPreparedTreeL0PacketCount];
            preparedTreeL1PacketCount = slot.Counters[PreparationCounterPreparedTreeL1PacketCount];
            preparedTreeL2PacketCount = slot.Counters[PreparationCounterPreparedTreeL2PacketCount];
            preparedHlodPacketCount = slot.Counters[PreparationCounterPreparedHlodPacketCount];
            preparedShadowPacketCount = slot.Counters[PreparationCounterPreparedShadowPacketCount];
            preparedActiveGroupCount = slot.Counters[PreparationCounterPreparedActiveGroupCount];
            preparedFrustumMask = slot.Counters[PreparationCounterPreparedFrustumMask];
            preparedVisiblePageCount = slot.Counters[PreparationCounterVisiblePageCount];
            preparedVisibleCellCount = slot.Counters[PreparationCounterVisibleCellCount];
            preparedNearDetailLoadRequestCount = slot.Counters[PreparationCounterNearDetailLoadRequestCount];
            preparedNearDetailEvictedCellCount = slot.Counters[PreparationCounterNearDetailEvictedCellCount];
            nearDetailResidentCellCount = slot.Counters[PreparationCounterNearDetailResidentCellCount];
            nearDetailResidentBytes = slot.LongCounters[PreparationLongCounterNearDetailResidentBytes];
            preparedNearDetailLoadedBytes = slot.LongCounters[PreparationLongCounterNearDetailLoadedBytes];
            preparedWindStrength = slot.WindStrength;
            preparedWindFrequency = slot.WindFrequency;
            preparedWindDirection = slot.WindDirection;
            preparedLeafFlutterSettings = slot.LeafFlutterSettings;
            lastRenderedGroupCount = 0;
            lastSkippedGroupCount = 0;
            hasPreparedFrame = preparedInstanceCount > 0 && preparedActiveGroupCount > 0;
            return hasPreparedFrame;
        }

        private void EnsureActiveGroupIndexCapacity(int requiredCount)
        {
            if (activeGroupIndices.Length >= requiredCount)
            {
                return;
            }

            activeGroupIndices = new int[Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCount))];
        }

        private void EnsurePreparedGroupStateCapacity(int requiredCount)
        {
            if (groupInstanceCounts.Length < requiredCount)
            {
                int capacity = Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCount));
                groupInstanceCounts = new int[capacity];
                groupStartInstances = new int[capacity];
            }

            if (groupFrustumMasks.Length < groups.Count)
            {
                groupFrustumMasks = new int[Mathf.NextPowerOfTwo(Mathf.Max(1, groups.Count))];
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Reset();
        }

        private void RenderInternal(
            Camera camera,
            VegetationRenderPassMode passMode,
            IVegetationIndirectDrawWrapper drawWrapper,
            bool diagnosticsEnabled,
            int shadowFrustumIndex)
        {
            using (RenderMarker.Auto())
            {
                lastRenderedGroupCount = 0;
                lastSkippedGroupCount = 0;
                if (!hasPreparedFrame || camera == null || drawWrapper == null || instanceBuffer == null || argsBuffer == null)
                {
                    return;
                }

                sharedPropertyBlock.Clear();
                sharedPropertyBlock.SetBuffer(InstanceBufferId, instanceBuffer);
                sharedPropertyBlock.SetInt(InstanceBufferBaseOffsetId, 0);
                sharedPropertyBlock.SetFloat(WindStrengthId, preparedWindStrength);
                sharedPropertyBlock.SetFloat(WindFrequencyId, preparedWindFrequency);
                sharedPropertyBlock.SetVector(WindDirectionId, preparedWindDirection);
                sharedPropertyBlock.SetVector(LeafFlutterSettingsId, preparedLeafFlutterSettings);

                int requiredFrustumBit = ResolveRenderFrustumBit(passMode, shadowFrustumIndex);
                int renderedGroupCount = 0;
                int skippedGroupCount = 0;
                using (RenderDrawGroupsMarker.Auto())
                {
                    for (int i = 0; i < activeGroupIndexCount; i++)
                    {
                        int groupIndex = activeGroupIndices[i];
                        if (groupIndex < 0 || groupIndex >= groups.Count)
                        {
                            continue;
                        }

                        if (requiredFrustumBit != 0 &&
                            (groupIndex >= groupFrustumMasks.Length ||
                             (groupFrustumMasks[groupIndex] & requiredFrustumBit) == 0))
                        {
                            skippedGroupCount++;
                            continue;
                        }

                        GroupRecord group = groups[groupIndex];
                        if (!group.TryGetPass(passMode, out int passIndex))
                        {
                            continue;
                        }

                        int groupEntryIndex = ResolveRenderGroupEntryIndex(passMode, shadowFrustumIndex, groupIndex);
                        if (groupEntryIndex < 0 ||
                            groupEntryIndex >= groupInstanceCounts.Length ||
                            groupInstanceCounts[groupEntryIndex] <= 0)
                        {
                            skippedGroupCount++;
                            continue;
                        }

                        sharedPropertyBlock.SetInt(InstanceBufferBaseOffsetId, groupStartInstances[groupEntryIndex]);
                        drawWrapper.DrawMeshInstancedIndirect(
                            group.AssetGroup.Mesh,
                            group.AssetGroup.Material,
                            argsBuffer,
                            ResolveArgsBufferOffset(groupEntryIndex),
                            passIndex,
                            sharedPropertyBlock);
                        renderedGroupCount++;
                    }
                }

                lastRenderedGroupCount = renderedGroupCount;
                lastSkippedGroupCount = skippedGroupCount;
                if (diagnosticsEnabled)
                {
                    Debug.Log(
                        $"VegetationRenderWorld render camera={camera.name} pass={passMode} groups={renderedGroupCount} skippedGroups={skippedGroupCount} activeGroups={preparedActiveGroupCount} frustumMask=0x{preparedFrustumMask:X} packets={preparedPacketCount} instances={preparedInstanceCount} nearPackets={preparedNearDetailPacketCount} treeL0Packets={preparedTreeL0PacketCount} treeL1Packets={preparedTreeL1PacketCount} treeL2Packets={preparedTreeL2PacketCount} hlodPackets={preparedHlodPacketCount} shadowPackets={preparedShadowPacketCount} nearResidentCells={nearDetailResidentCellCount} nearResidentBytes={nearDetailResidentBytes} nearLoadedBytes={preparedNearDetailLoadedBytes} nearEvictedCells={preparedNearDetailEvictedCellCount}");
                }
            }
        }

        internal bool HasPreparedShadowFrustum(int frustumIndex)
        {
            int frustumBit = ResolveRenderFrustumBit(VegetationRenderPassMode.Shadow, frustumIndex);
            return hasPreparedFrame && frustumBit != 0 && (preparedFrustumMask & frustumBit) != 0;
        }

        private static int ResolveRenderFrustumBit(VegetationRenderPassMode passMode, int shadowFrustumIndex)
        {
            if (passMode != VegetationRenderPassMode.Shadow ||
                shadowFrustumIndex < 0 ||
                shadowFrustumIndex >= 30)
            {
                return 0;
            }

            return 1 << shadowFrustumIndex;
        }

        private int ResolveRenderGroupEntryIndex(
            VegetationRenderPassMode passMode,
            int shadowFrustumIndex,
            int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= groups.Count)
            {
                return -1;
            }

            if (passMode != VegetationRenderPassMode.Shadow)
            {
                return groupIndex;
            }

            if (shadowFrustumIndex < 0)
            {
                return -1;
            }

            return groupIndex + shadowFrustumIndex * groups.Count;
        }

        private static int ResolveArgsBufferOffset(int groupEntryIndex)
        {
            return checked(groupEntryIndex * GraphicsBuffer.IndirectDrawIndexedArgs.size);
        }

        private void EnsureCompiledGraph()
        {
            if (!graphDirty)
            {
                return;
            }

            ClearCompiledGraph();
            for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                ProviderRecord provider = providers[providerIndex];
                provider.AssetGroupOffset = groups.Count;
                providers[providerIndex] = provider;

                for (int groupIndex = 0; groupIndex < provider.AssetGroups.Length; groupIndex++)
                {
                    groups.Add(new GroupRecord(provider.AssetGroups[groupIndex]));
                }

                for (int pageIndex = 0; pageIndex < provider.Pages.Length; pageIndex++)
                {
                    FoliagePageAsset page = provider.Pages[pageIndex];
                    int pageRecordIndex = pages.Count;
                    int firstCellRecord = cells.Count;
                    PageRecord pageRecord = new PageRecord(pageRecordIndex, providerIndex, pageIndex, page, firstCellRecord, page.Cells.Count);
                    pages.Add(pageRecord);

                    for (int cellOffset = 0; cellOffset < page.Cells.Count; cellOffset++)
                    {
                        FoliagePageCell cell = page.Cells[cellOffset];
                        cells.Add(new CellRecord(pageRecordIndex, cell.CellIndex, cell.WorldBounds));
                    }
                }
            }

            nearDetailCellBytes = new long[cells.Count];
            nearDetailResidentBytes = 0L;
            nearDetailResidentCellCount = 0;

            BuildPacketLookup();
            BuildNearDetailCellBytes();
            BuildBatchInstanceLookup();
            BuildPacketInstanceValidation();
            LogInvalidCompiledPacketsOnce();
            BuildPreparationGraph();
            EnsureHotPathCapacity();
            groupInstanceCounts = new int[groups.Count];
            groupFrustumMasks = new int[groups.Count];
            groupStartInstances = new int[groups.Count];
            argsGroupCapacity = 0;
            graphDirty = false;
        }

        private void EnsureHotPathCapacity()
        {
            if (activeGroupIndices.Length < groups.Count)
            {
                activeGroupIndices = new int[Mathf.NextPowerOfTwo(Mathf.Max(1, groups.Count))];
            }
        }

        private void BuildPreparationGraph()
        {
            CompletePreparationSlots();
            DisposePreparationGraph();

            int pageCount = pages.Count;
            int cellCount = cells.Count;
            int groupCount = groups.Count;
            int packetCount = 0;
            int validPacketInstanceCount = 0;
            int[] pagePacketOffsets = pageCount > 0 ? new int[pageCount] : Array.Empty<int>();
            for (int pageRecordIndex = 0; pageRecordIndex < pageCount; pageRecordIndex++)
            {
                pagePacketOffsets[pageRecordIndex] = packetCount;
                IReadOnlyList<FoliageRepresentationPacket> packets = pages[pageRecordIndex].Page.Packets;
                packetCount += packets.Count;
                for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
                {
                    validPacketInstanceCount += GetValidPacketInstanceCount(pageRecordIndex, packetIndex);
                }
            }

            preparationPages = new NativeArray<PreparationPageRecord>(
                Mathf.Max(1, pageCount),
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            preparationCells = new NativeArray<PreparationCellRecord>(
                Mathf.Max(1, cellCount),
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            preparationPackets = new NativeArray<PreparationPacketRecord>(
                Mathf.Max(1, packetCount),
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            preparationPacketLookupIndices = new NativeArray<int>(
                Mathf.Max(1, packetLookupIndices.Length),
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            preparationGroups = new NativeArray<PreparationGroupRecord>(
                Mathf.Max(1, groupCount),
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            preparationStaticInstances = new NativeArray<VegetationIndirectInstanceData>(
                Mathf.Max(1, validPacketInstanceCount),
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            preparationNearDetailResidentCellMask = new NativeArray<int>(
                Mathf.Max(1, cellCount),
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            preparationNearDetailRequestedCellMask = new NativeArray<int>(
                Mathf.Max(1, cellCount),
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            preparationNearDetailLastUsedCellFrame = new NativeArray<int>(
                Mathf.Max(1, cellCount),
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            int staticInstanceWrite = 0;
            int[] pageWriteOffsets = pagePacketRanges.Length > 0 ? new int[pagePacketRanges.Length] : Array.Empty<int>();
            int[] cellWriteOffsets = cellPacketRanges.Length > 0 ? new int[cellPacketRanges.Length] : Array.Empty<int>();
            for (int pageRecordIndex = 0; pageRecordIndex < pageCount; pageRecordIndex++)
            {
                PageRecord pageRecord = pages[pageRecordIndex];
                FoliagePageAsset pageAsset = pageRecord.Page;
                PacketRange pageHlodRange = GetPacketRange(pageRecordIndex, FoliageRepresentationKind.PageHLOD, -1);
                Bounds pageBounds = pageAsset.WorldBounds;
                preparationPages[pageRecordIndex] = new PreparationPageRecord
                {
                    BoundsCenter = pageBounds.center,
                    BoundsExtents = pageBounds.extents,
                    FirstCellRecord = pageRecord.FirstCellRecord,
                    CellCount = pageRecord.CellCount,
                    PageHlodStart = pageHlodRange.Start,
                    PageHlodCount = pageHlodRange.Count
                };

                for (int cellOffset = 0; cellOffset < pageRecord.CellCount; cellOffset++)
                {
                    int cellRecordIndex = pageRecord.FirstCellRecord + cellOffset;
                    CellRecord cell = cells[cellRecordIndex];
                    PacketRange cellHlodRange = GetPacketRange(pageRecordIndex, FoliageRepresentationKind.CellHLOD, cellRecordIndex);
                    PacketRange treeL0Range = GetPacketRange(pageRecordIndex, FoliageRepresentationKind.TreeL0, cellRecordIndex);
                    PacketRange treeL1Range = GetPacketRange(pageRecordIndex, FoliageRepresentationKind.TreeL1, cellRecordIndex);
                    PacketRange treeL2Range = GetPacketRange(pageRecordIndex, FoliageRepresentationKind.TreeL2, cellRecordIndex);
                    preparationCells[cellRecordIndex] = new PreparationCellRecord
                    {
                        BoundsCenter = cell.WorldBounds.center,
                        BoundsExtents = cell.WorldBounds.extents,
                        PageRecordIndex = cell.PageRecordIndex,
                        CellHlodStart = cellHlodRange.Start,
                        CellHlodCount = cellHlodRange.Count,
                        TreeL0Start = treeL0Range.Start,
                        TreeL0Count = treeL0Range.Count,
                        TreeL1Start = treeL1Range.Start,
                        TreeL1Count = treeL1Range.Count,
                        TreeL2Start = treeL2Range.Start,
                        TreeL2Count = treeL2Range.Count,
                        NearDetailBytes = cellRecordIndex < nearDetailCellBytes.Length ? nearDetailCellBytes[cellRecordIndex] : 0L
                    };
                }

                IReadOnlyList<FoliageRepresentationPacket> packets = pageAsset.Packets;
                for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
                {
                    FoliageRepresentationPacket packet = packets[packetIndex];
                    int firstStaticInstance = staticInstanceWrite;
                    int worldGroupIndex = -1;
                    int shadowPacketIndex = -1;
                    Bounds packetBounds = default;
                    int workCost = 1;
                    int residency = (int)FoliagePacketResidency.AlwaysResident;
                    int representationKind = (int)FoliageRepresentationKind.PageHLOD;
                    int shadowMode = (int)FoliageShadowPacketMode.None;
                    if (packet != null)
                    {
                        worldGroupIndex = ResolveWorldGroupIndex(pageRecord.ProviderIndex, packet.AssetGroupIndex);
                        packetBounds = packet.WorldBounds;
                        workCost = packet.WorkCost;
                        residency = (int)packet.Residency;
                        representationKind = (int)packet.RepresentationKind;
                        shadowMode = (int)packet.ShadowMode;
                        if (packet.ShadowPacketIndex >= 0 && packet.ShadowPacketIndex < packets.Count)
                        {
                            shadowPacketIndex = pagePacketOffsets[pageRecordIndex] + packet.ShadowPacketIndex;
                        }

                        for (int instanceOffset = 0; instanceOffset < packet.InstanceCount; instanceOffset++)
                        {
                            if (!TryResolvePacketLocalInstance(
                                    pageRecord,
                                    pageRecordIndex,
                                    packet,
                                    instanceOffset,
                                    worldGroupIndex,
                                    out int sourceInstanceIndex,
                                    out _))
                            {
                                continue;
                            }

                            preparationStaticInstances[staticInstanceWrite++] =
                                ConvertInstance(pageAsset.Instances[sourceInstanceIndex]);
                        }
                    }

                    preparationPackets[pagePacketOffsets[pageRecordIndex] + packetIndex] =
                        new PreparationPacketRecord
                        {
                            BoundsCenter = packetBounds.center,
                            BoundsExtents = packetBounds.extents,
                            WorldGroupIndex = worldGroupIndex,
                            FirstInstance = firstStaticInstance,
                            InstanceCount = staticInstanceWrite - firstStaticInstance,
                            WorkCost = workCost,
                            Residency = residency,
                            RepresentationKind = representationKind,
                            ShadowMode = shadowMode,
                            ShadowPacketIndex = shadowPacketIndex
                        };

                    if (packet == null ||
                        !TryResolvePacketRangeIndex(pageRecord, packet, out bool pageRange, out int rangeIndex))
                    {
                        continue;
                    }

                    PacketRange range = pageRange ? pagePacketRanges[rangeIndex] : cellPacketRanges[rangeIndex];
                    int[] writeOffsets = pageRange ? pageWriteOffsets : cellWriteOffsets;
                    int writeIndex = range.Start + writeOffsets[rangeIndex];
                    if (writeIndex >= 0 && writeIndex < preparationPacketLookupIndices.Length)
                    {
                        preparationPacketLookupIndices[writeIndex] = pagePacketOffsets[pageRecordIndex] + packetIndex;
                    }

                    writeOffsets[rangeIndex]++;
                }
            }

            int worstPreparedInstanceCount = 0;
            for (int pageRecordIndex = 0; pageRecordIndex < pageCount; pageRecordIndex++)
            {
                PageRecord pageRecord = pages[pageRecordIndex];
                int pageHlodCount = Math.Max(
                    CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.PageHLOD, -1), shadow: false),
                    CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.PageHLOD, -1), shadow: true));
                int pageCellWorstCount = 0;
                for (int cellOffset = 0; cellOffset < pageRecord.CellCount; cellOffset++)
                {
                    int cellRecordIndex = pageRecord.FirstCellRecord + cellOffset;
                    int cellWorstCount = 0;
                    cellWorstCount = Math.Max(cellWorstCount, CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.CellHLOD, cellRecordIndex), shadow: false));
                    cellWorstCount = Math.Max(cellWorstCount, CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.TreeL0, cellRecordIndex), shadow: false));
                    cellWorstCount = Math.Max(cellWorstCount, CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.TreeL1, cellRecordIndex), shadow: false));
                    cellWorstCount = Math.Max(cellWorstCount, CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.TreeL2, cellRecordIndex), shadow: false));
                    cellWorstCount = Math.Max(cellWorstCount, CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.CellHLOD, cellRecordIndex), shadow: true));
                    cellWorstCount = Math.Max(cellWorstCount, CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.TreeL0, cellRecordIndex), shadow: true));
                    cellWorstCount = Math.Max(cellWorstCount, CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.TreeL1, cellRecordIndex), shadow: true));
                    cellWorstCount = Math.Max(cellWorstCount, CountPacketRangeValidInstances(pageRecordIndex, GetPacketRange(pageRecordIndex, FoliageRepresentationKind.TreeL2, cellRecordIndex), shadow: true));
                    pageCellWorstCount += cellWorstCount;
                }

                worstPreparedInstanceCount += Math.Max(pageHlodCount, pageCellWorstCount);
            }

            maxPreparationInstanceCount = Mathf.Max(1, worstPreparedInstanceCount);
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                GroupRecord group = groups[groupIndex];
                preparationGroups[groupIndex] = new PreparationGroupRecord
                {
                    IndexCount = group.IndexCount,
                    IndexStart = group.IndexStart,
                    BaseVertex = group.BaseVertex
                };
            }
        }

        private int CountPacketRangeValidInstances(int pageRecordIndex, PacketRange range, bool shadow)
        {
            if (pageRecordIndex < 0 || pageRecordIndex >= pages.Count || range.Count <= 0)
            {
                return 0;
            }

            FoliagePageAsset page = pages[pageRecordIndex].Page;
            int count = 0;
            for (int rangeOffset = 0; rangeOffset < range.Count; rangeOffset++)
            {
                int packetIndex = packetLookupIndices[range.Start + rangeOffset];
                if (packetIndex < 0 || packetIndex >= page.Packets.Count)
                {
                    continue;
                }

                FoliageRepresentationPacket packet = page.Packets[packetIndex];
                if (packet == null)
                {
                    continue;
                }

                if (!shadow)
                {
                    count += GetValidPacketInstanceCount(pageRecordIndex, packetIndex);
                    continue;
                }

                if (packet.ShadowMode == FoliageShadowPacketMode.None ||
                    packet.ShadowPacketIndex < 0 ||
                    packet.ShadowPacketIndex >= page.Packets.Count)
                {
                    continue;
                }

                count += GetValidPacketInstanceCount(pageRecordIndex, packet.ShadowPacketIndex);
            }

            return count;
        }

        private void EnsurePreparationSlotCapacity(
            PreparationSlot slot,
            int instanceCount,
            int groupCount,
            int argsEntryCount,
            int cellCount,
            int frustumPlaneCount)
        {
            slot.JobHandle.Complete();
            EnsureNativeArrayCapacity(ref slot.FrustumPlanes, frustumPlaneCount, NativeArrayOptions.UninitializedMemory);
            EnsureNativeArrayCapacity(ref slot.CellCandidates, cellCount, NativeArrayOptions.UninitializedMemory);
            EnsureNativeArrayCapacity(ref slot.SelectedPackets, Math.Max(1, preparationPacketLookupIndices.Length), NativeArrayOptions.UninitializedMemory);
            EnsureNativeArrayCapacity(ref slot.PageVisibleMask, Math.Max(1, pages.Count), NativeArrayOptions.ClearMemory);
            EnsureNativeArrayCapacity(ref slot.InstanceData, instanceCount, NativeArrayOptions.UninitializedMemory);
            EnsureNativeArrayCapacity(ref slot.ArgsData, argsEntryCount * IndirectArgsUIntCount, NativeArrayOptions.ClearMemory);
            EnsureNativeArrayCapacity(ref slot.GroupInstanceCounts, argsEntryCount, NativeArrayOptions.ClearMemory);
            EnsureNativeArrayCapacity(ref slot.GroupStartInstances, argsEntryCount, NativeArrayOptions.ClearMemory);
            EnsureNativeArrayCapacity(ref slot.GroupWriteOffsets, argsEntryCount, NativeArrayOptions.ClearMemory);
            EnsureNativeArrayCapacity(ref slot.GroupFrustumMasks, groupCount, NativeArrayOptions.ClearMemory);
            EnsureNativeArrayCapacity(ref slot.Counters, PreparationCounterCount, NativeArrayOptions.ClearMemory);
            EnsureNativeArrayCapacity(ref slot.LongCounters, PreparationLongCounterCount, NativeArrayOptions.ClearMemory);
        }

        private static void CopyFrustumPlanesToSlot(PreparationSlot slot, Plane[] frustumPlanes, int frustumCount)
        {
            int planeCount = Mathf.Min(slot.FrustumPlanes.Length, Mathf.Max(0, frustumCount) * 6);
            for (int i = 0; i < planeCount; i++)
            {
                Plane plane = frustumPlanes[i];
                Vector3 normal = plane.normal;
                slot.FrustumPlanes[i] = new Vector4(normal.x, normal.y, normal.z, plane.distance);
            }
        }

        private void BeginScheduledResidencyFrame()
        {
            if (residencyFrameIndex == int.MaxValue)
            {
                if (preparationNearDetailLastUsedCellFrame.IsCreated)
                {
                    for (int i = 0; i < preparationNearDetailLastUsedCellFrame.Length; i++)
                    {
                        preparationNearDetailLastUsedCellFrame[i] = 0;
                    }
                }

                residencyFrameIndex = 1;
                return;
            }

            residencyFrameIndex++;
        }

        private static void ApplyPreparationWindSettings(PreparationSlot slot, VegetationFoliageFeatureSettings settings)
        {
            slot.WindStrength = Mathf.Max(0f, settings.WindStrength);
            slot.WindFrequency = Mathf.Max(0f, settings.WindFrequency);
            Vector3 windDirection = settings.WindDirection.sqrMagnitude > 0.0001f
                ? settings.WindDirection.normalized
                : Vector3.right;
            slot.WindDirection = new Vector4(windDirection.x, windDirection.y, windDirection.z, 0f);
            slot.LeafFlutterSettings = new Vector4(
                Mathf.Max(0f, settings.LeafFlutterStrength),
                Mathf.Max(0f, settings.LeafFlutterFrequencyMultiplier),
                Mathf.Max(0f, settings.LeafFlutterSpatialScale),
                Mathf.Max(0f, settings.LeafFlutterSecondaryStrength));
        }

        private void CompletePreparationSlots()
        {
            preparationDependency.Complete();
            preparationDependency = default;
            for (int i = 0; i < preparationSlots.Length; i++)
            {
                PreparationSlot slot = preparationSlots[i];
                if (slot.Version == 0)
                {
                    continue;
                }

                if (slot.CompletedVersion != slot.Version)
                {
                    slot.JobHandle.Complete();
                    slot.CompletedVersion = slot.Version;
                    CacheCompletedPreparationSlot(slot);
                }
            }
        }

        private void DisposePreparationGraph()
        {
            DisposeNativeArray(ref preparationPages);
            DisposeNativeArray(ref preparationCells);
            DisposeNativeArray(ref preparationPackets);
            DisposeNativeArray(ref preparationPacketLookupIndices);
            DisposeNativeArray(ref preparationGroups);
            DisposeNativeArray(ref preparationStaticInstances);
            DisposeNativeArray(ref preparationNearDetailResidentCellMask);
            DisposeNativeArray(ref preparationNearDetailRequestedCellMask);
            DisposeNativeArray(ref preparationNearDetailLastUsedCellFrame);
            maxPreparationInstanceCount = 0;
        }

        private void DisposePreparationSlots()
        {
            preparationDependency.Complete();
            for (int i = 0; i < preparationSlots.Length; i++)
            {
                preparationSlots[i].Dispose();
            }
        }

        private static void EnsureNativeArrayCapacity<T>(
            ref NativeArray<T> array,
            int requiredCount,
            NativeArrayOptions options)
            where T : struct
        {
            if (array.IsCreated && array.Length >= requiredCount)
            {
                return;
            }

            DisposeNativeArray(ref array);
            array = new NativeArray<T>(
                Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCount)),
                Allocator.Persistent,
                options);
        }

        private static void DisposeNativeArray<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        private void ClearCompiledGraph()
        {
            CompletePreparationSlots();
            DisposePreparationGraph();
            DisposePreparationSlots();
            pages.Clear();
            cells.Clear();
            groups.Clear();
            nearDetailCellBytes = Array.Empty<long>();
            pagePacketRanges = Array.Empty<PacketRange>();
            cellPacketRanges = Array.Empty<PacketRange>();
            packetLookupIndices = Array.Empty<int>();
            nearDetailResidentBytes = 0L;
            nearDetailResidentCellCount = 0;
            groupInstanceCounts = Array.Empty<int>();
            groupFrustumMasks = Array.Empty<int>();
            groupStartInstances = Array.Empty<int>();
            groupTotalInstanceCounts = Array.Empty<int>();
            pageInstanceGroupLocalIndices = Array.Empty<int[]>();
            pagePacketValidInstanceCounts = Array.Empty<int[]>();
            invalidCompiledPacketCount = 0;
            activeGroupIndexCount = 0;
            hasPreparedFrame = false;
            InvalidatePreparationFrameCaches();
        }

        private void BuildPacketLookup()
        {
            int pageRangeCount = pages.Count * PacketLookupRepresentationSlotCount;
            int cellRangeCount = cells.Count * PacketLookupRepresentationSlotCount;
            pagePacketRanges = new PacketRange[pageRangeCount];
            cellPacketRanges = new PacketRange[cellRangeCount];

            int lookupCount = 0;
            for (int pageRecordIndex = 0; pageRecordIndex < pages.Count; pageRecordIndex++)
            {
                PageRecord pageRecord = pages[pageRecordIndex];
                IReadOnlyList<FoliageRepresentationPacket> packets = pageRecord.Page.Packets;
                for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
                {
                    if (!TryResolvePacketRangeIndex(pageRecord, packets[packetIndex], out bool pageRange, out int rangeIndex))
                    {
                        continue;
                    }

                    IncrementPacketRangeCount(pageRange ? pagePacketRanges : cellPacketRanges, rangeIndex);
                    lookupCount++;
                }
            }

            int start = 0;
            PrefixPacketRanges(pagePacketRanges, ref start);
            PrefixPacketRanges(cellPacketRanges, ref start);
            packetLookupIndices = new int[Mathf.Max(0, lookupCount)];

            int[] pageWriteOffsets = pageRangeCount > 0 ? new int[pageRangeCount] : Array.Empty<int>();
            int[] cellWriteOffsets = cellRangeCount > 0 ? new int[cellRangeCount] : Array.Empty<int>();
            for (int pageRecordIndex = 0; pageRecordIndex < pages.Count; pageRecordIndex++)
            {
                PageRecord pageRecord = pages[pageRecordIndex];
                IReadOnlyList<FoliageRepresentationPacket> packets = pageRecord.Page.Packets;
                for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
                {
                    if (!TryResolvePacketRangeIndex(pageRecord, packets[packetIndex], out bool pageRange, out int rangeIndex))
                    {
                        continue;
                    }

                    PacketRange range = pageRange ? pagePacketRanges[rangeIndex] : cellPacketRanges[rangeIndex];
                    int[] writeOffsets = pageRange ? pageWriteOffsets : cellWriteOffsets;
                    int writeIndex = range.Start + writeOffsets[rangeIndex];
                    if (writeIndex >= 0 && writeIndex < packetLookupIndices.Length)
                    {
                        packetLookupIndices[writeIndex] = packetIndex;
                    }

                    writeOffsets[rangeIndex]++;
                }
            }
        }

        private void BuildNearDetailCellBytes()
        {
            if (nearDetailCellBytes.Length > 0)
            {
                Array.Clear(nearDetailCellBytes, 0, nearDetailCellBytes.Length);
            }

            for (int cellRecordIndex = 0; cellRecordIndex < cells.Count; cellRecordIndex++)
            {
                CellRecord cell = cells[cellRecordIndex];
                PageRecord pageRecord = pages[cell.PageRecordIndex];
                long bytes = 0L;
                bytes += EstimatePacketRangeBytes(pageRecord.Page, GetPacketRange(pageRecord.PageRecordIndex, FoliageRepresentationKind.TreeL0, cellRecordIndex));
                bytes += EstimatePacketRangeBytes(pageRecord.Page, GetPacketRange(pageRecord.PageRecordIndex, FoliageRepresentationKind.TreeL1, cellRecordIndex));
                bytes += EstimatePacketRangeBytes(pageRecord.Page, GetPacketRange(pageRecord.PageRecordIndex, FoliageRepresentationKind.TreeL2, cellRecordIndex));
                nearDetailCellBytes[cellRecordIndex] = bytes;
            }
        }

        private void BuildBatchInstanceLookup()
        {
            groupTotalInstanceCounts = new int[groups.Count];
            pageInstanceGroupLocalIndices = new int[pages.Count][];
            for (int pageRecordIndex = 0; pageRecordIndex < pages.Count; pageRecordIndex++)
            {
                PageRecord pageRecord = pages[pageRecordIndex];
                IReadOnlyList<FoliagePacketInstance> instances = pageRecord.Page.Instances;
                int[] pageLookup = instances.Count > 0 ? new int[instances.Count] : Array.Empty<int>();
                for (int instanceIndex = 0; instanceIndex < instances.Count; instanceIndex++)
                {
                    int worldGroupIndex = ResolveWorldGroupIndex(pageRecord.ProviderIndex, instances[instanceIndex].AssetGroupIndex);
                    if (worldGroupIndex < 0 || worldGroupIndex >= groupTotalInstanceCounts.Length)
                    {
                        pageLookup[instanceIndex] = -1;
                        continue;
                    }

                    pageLookup[instanceIndex] = groupTotalInstanceCounts[worldGroupIndex];
                    groupTotalInstanceCounts[worldGroupIndex]++;
                }

                pageInstanceGroupLocalIndices[pageRecordIndex] = pageLookup;
            }
        }

        private void BuildPacketInstanceValidation()
        {
            pagePacketValidInstanceCounts = new int[pages.Count][];
            invalidCompiledPacketCount = 0;
            for (int pageRecordIndex = 0; pageRecordIndex < pages.Count; pageRecordIndex++)
            {
                PageRecord pageRecord = pages[pageRecordIndex];
                IReadOnlyList<FoliageRepresentationPacket> packets = pageRecord.Page.Packets;
                int[] validCounts = packets.Count > 0 ? new int[packets.Count] : Array.Empty<int>();
                for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
                {
                    FoliageRepresentationPacket packet = packets[packetIndex];
                    if (packet == null)
                    {
                        invalidCompiledPacketCount++;
                        continue;
                    }

                    int validCount = CountValidPacketInstances(pageRecord, pageRecordIndex, packet);
                    validCounts[packetIndex] = validCount;
                    if (validCount != packet.InstanceCount)
                    {
                        invalidCompiledPacketCount++;
                    }
                }

                pagePacketValidInstanceCounts[pageRecordIndex] = validCounts;
            }
        }

        private void LogInvalidCompiledPacketsOnce()
        {
            if (invalidCompiledPacketCount <= 0 || invalidCompiledPacketsLogged)
            {
                return;
            }

            invalidCompiledPacketsLogged = true;
            Debug.LogWarning(
                $"Vegetation compiled graph dropped invalid packet instance ranges count={invalidCompiledPacketCount}. Rebuild compiled foliage pages; runtime will skip invalid packet entries to keep grouped draw output valid.");
        }

        private int CountValidPacketInstances(
            PageRecord pageRecord,
            int pageRecordIndex,
            FoliageRepresentationPacket packet)
        {
            int worldGroupIndex = ResolveWorldGroupIndex(pageRecord.ProviderIndex, packet.AssetGroupIndex);
            if (worldGroupIndex < 0 || worldGroupIndex >= groupTotalInstanceCounts.Length)
            {
                return 0;
            }

            int firstInstance = packet.FirstInstance;
            int instanceCount = packet.InstanceCount;
            if (firstInstance < 0 || instanceCount <= 0)
            {
                return 0;
            }

            IReadOnlyList<FoliagePacketInstance> instances = pageRecord.Page.Instances;
            if ((long)firstInstance + instanceCount > instances.Count)
            {
                return 0;
            }

            int validCount = 0;
            for (int instanceOffset = 0; instanceOffset < instanceCount; instanceOffset++)
            {
                if (TryResolvePacketLocalInstance(
                        pageRecord,
                        pageRecordIndex,
                        packet,
                        instanceOffset,
                        worldGroupIndex,
                        out _,
                        out _))
                {
                    validCount++;
                }
            }

            return validCount;
        }

        private int GetValidPacketInstanceCount(int pageRecordIndex, int packetIndex)
        {
            if (pageRecordIndex < 0 ||
                pageRecordIndex >= pagePacketValidInstanceCounts.Length ||
                packetIndex < 0)
            {
                return 0;
            }

            int[] counts = pagePacketValidInstanceCounts[pageRecordIndex];
            return packetIndex < counts.Length ? counts[packetIndex] : 0;
        }

        private bool TryResolvePacketLocalInstance(
            PageRecord pageRecord,
            int pageRecordIndex,
            FoliageRepresentationPacket packet,
            int instanceOffset,
            int expectedWorldGroupIndex,
            out int sourceInstanceIndex,
            out int groupLocalInstanceIndex)
        {
            sourceInstanceIndex = packet.FirstInstance + instanceOffset;
            groupLocalInstanceIndex = -1;
            if (instanceOffset < 0 ||
                sourceInstanceIndex < 0 ||
                sourceInstanceIndex >= pageRecord.Page.Instances.Count ||
                pageRecordIndex < 0 ||
                pageRecordIndex >= pageInstanceGroupLocalIndices.Length)
            {
                return false;
            }

            FoliagePacketInstance instance = pageRecord.Page.Instances[sourceInstanceIndex];
            if (instance.AssetGroupIndex != packet.AssetGroupIndex)
            {
                return false;
            }

            int[] pageLookup = pageInstanceGroupLocalIndices[pageRecordIndex];
            if (sourceInstanceIndex >= pageLookup.Length)
            {
                return false;
            }

            groupLocalInstanceIndex = pageLookup[sourceInstanceIndex];
            return expectedWorldGroupIndex >= 0 &&
                   expectedWorldGroupIndex < groupTotalInstanceCounts.Length &&
                   groupLocalInstanceIndex >= 0 &&
                   groupLocalInstanceIndex < groupTotalInstanceCounts[expectedWorldGroupIndex];
        }

        private long EstimatePacketRangeBytes(FoliagePageAsset page, PacketRange range)
        {
            long bytes = 0L;
            for (int rangeOffset = 0; rangeOffset < range.Count; rangeOffset++)
            {
                int packetIndex = packetLookupIndices[range.Start + rangeOffset];
                if (packetIndex < 0 || packetIndex >= page.Packets.Count)
                {
                    continue;
                }

                FoliageRepresentationPacket packet = page.Packets[packetIndex];
                bytes += EstimatedNearDetailPacketBytes + packet.InstanceCount * EstimatedNearDetailInstanceBytes;
            }

            return bytes;
        }

        private bool TryResolvePacketRangeIndex(
            PageRecord pageRecord,
            FoliageRepresentationPacket packet,
            out bool pageRange,
            out int rangeIndex)
        {
            pageRange = false;
            rangeIndex = -1;
            int representationSlot = GetRepresentationSlot(packet.RepresentationKind);
            if (representationSlot < 0)
            {
                return false;
            }

            if (packet.CellIndex < 0)
            {
                pageRange = true;
                rangeIndex = pageRecord.PageRecordIndex * PacketLookupRepresentationSlotCount + representationSlot;
                return rangeIndex >= 0 && rangeIndex < pagePacketRanges.Length;
            }

            int cellRecordIndex = ResolveCellRecordIndex(pageRecord, packet.CellIndex);
            if (cellRecordIndex < 0)
            {
                return false;
            }

            rangeIndex = cellRecordIndex * PacketLookupRepresentationSlotCount + representationSlot;
            return rangeIndex >= 0 && rangeIndex < cellPacketRanges.Length;
        }

        private PacketRange GetPacketRange(
            int pageRecordIndex,
            FoliageRepresentationKind representationKind,
            int cellRecordIndex)
        {
            int representationSlot = GetRepresentationSlot(representationKind);
            if (representationSlot < 0)
            {
                return default;
            }

            if (cellRecordIndex < 0)
            {
                int rangeIndex = pageRecordIndex * PacketLookupRepresentationSlotCount + representationSlot;
                return rangeIndex >= 0 && rangeIndex < pagePacketRanges.Length
                    ? pagePacketRanges[rangeIndex]
                    : default;
            }

            if (cellRecordIndex >= cells.Count || cells[cellRecordIndex].PageRecordIndex != pageRecordIndex)
            {
                return default;
            }

            int cellRangeIndex = cellRecordIndex * PacketLookupRepresentationSlotCount + representationSlot;
            return cellRangeIndex >= 0 && cellRangeIndex < cellPacketRanges.Length
                ? cellPacketRanges[cellRangeIndex]
                : default;
        }

        private int ResolveCellRecordIndex(PageRecord pageRecord, int pageCellIndex)
        {
            for (int cellOffset = 0; cellOffset < pageRecord.CellCount; cellOffset++)
            {
                int cellRecordIndex = pageRecord.FirstCellRecord + cellOffset;
                if (cellRecordIndex >= 0 &&
                    cellRecordIndex < cells.Count &&
                    cells[cellRecordIndex].CellIndex == pageCellIndex)
                {
                    return cellRecordIndex;
                }
            }

            return -1;
        }

        private static int GetRepresentationSlot(FoliageRepresentationKind representationKind)
        {
            switch (representationKind)
            {
                case FoliageRepresentationKind.PageHLOD:
                    return 0;
                case FoliageRepresentationKind.CellHLOD:
                    return 1;
                case FoliageRepresentationKind.TreeL0:
                    return 2;
                case FoliageRepresentationKind.TreeL1:
                    return 3;
                case FoliageRepresentationKind.TreeL2:
                    return 4;
                default:
                    return -1;
            }
        }

        private static void IncrementPacketRangeCount(PacketRange[] ranges, int rangeIndex)
        {
            PacketRange range = ranges[rangeIndex];
            range.Count++;
            ranges[rangeIndex] = range;
        }

        private static void PrefixPacketRanges(PacketRange[] ranges, ref int start)
        {
            for (int i = 0; i < ranges.Length; i++)
            {
                PacketRange range = ranges[i];
                int count = range.Count;
                range.Start = start;
                ranges[i] = range;
                start += count;
            }
        }

        private static int ResolveFrustumCount(Plane[]? frustumPlanes, int requestedFrustumCount)
        {
            if (frustumPlanes == null || requestedFrustumCount <= 0)
            {
                return 0;
            }

            return Mathf.Min(Mathf.Min(requestedFrustumCount, frustumPlanes.Length / 6), 30);
        }

        private static int AllFrustumBits(int frustumCount)
        {
            int clampedCount = Mathf.Clamp(frustumCount, 0, 30);
            return clampedCount == 0 ? 0 : (1 << clampedCount) - 1;
        }
        private int ResolveWorldGroupIndex(int providerIndex, int localGroupIndex)
        {
            if (providerIndex < 0 || providerIndex >= providers.Count || localGroupIndex < 0)
            {
                return -1;
            }

            return providers[providerIndex].AssetGroupOffset + localGroupIndex;
        }

        private static VegetationIndirectInstanceData ConvertInstance(FoliagePacketInstance instance)
        {
            FoliageWindMetadata wind = instance.WindMetadata;
            return new VegetationIndirectInstanceData
            {
                ObjectToWorld = instance.ObjectToWorld,
                WorldToObject = instance.WorldToObject,
                PackedLeafTint = instance.PackedLeafTint,
                Padding0 = 0u,
                Padding1 = 0u,
                Padding2 = 0u,
                Wind = new Vector4(wind.Phase01, wind.TrunkBendWeight, wind.BranchFlutterWeight, wind.AnchorHeight)
            };
        }

        private void EnsureGpuBuffers(int requiredInstanceCount, int requiredGroupCount)
        {
            if (instanceBuffer == null || instanceCapacity < requiredInstanceCount)
            {
                ReleaseGraphicsBuffer(ref instanceBuffer);
                instanceCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, requiredInstanceCount));
                instanceBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    instanceCapacity,
                    Marshal.SizeOf<VegetationIndirectInstanceData>());
            }

            if (argsBuffer == null || argsGroupCapacity < requiredGroupCount)
            {
                ReleaseGraphicsBuffer(ref argsBuffer);
                argsGroupCapacity = Mathf.NextPowerOfTwo(Mathf.Max(1, requiredGroupCount));
                argsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.IndirectArguments,
                    argsGroupCapacity * IndirectArgsUIntCount,
                    sizeof(uint));
            }
        }

        private void ClearPreparedFrame()
        {
            ClearActiveGroupSelection();
            preparedInstanceCount = 0;
            preparedPacketCount = 0;
            preparedNearDetailPacketCount = 0;
            preparedTreeL0PacketCount = 0;
            preparedTreeL1PacketCount = 0;
            preparedTreeL2PacketCount = 0;
            preparedHlodPacketCount = 0;
            preparedShadowPacketCount = 0;
            preparedActiveGroupCount = 0;
            preparedFrustumMask = 0;
            lastRenderedGroupCount = 0;
            lastSkippedGroupCount = 0;
            preparedNearDetailLoadRequestCount = 0;
            preparedNearDetailEvictedCellCount = 0;
            preparedNearDetailLoadedBytes = 0L;
            hasPreparedFrame = false;
            InvalidatePreparationFrameCaches();
        }

        private void ClearActiveGroupSelection()
        {
            if (groupInstanceCounts.Length > 0)
            {
                Array.Clear(groupInstanceCounts, 0, groupInstanceCounts.Length);
            }

            if (groupStartInstances.Length > 0)
            {
                Array.Clear(groupStartInstances, 0, groupStartInstances.Length);
            }

            if (groupFrustumMasks.Length > 0)
            {
                Array.Clear(groupFrustumMasks, 0, groupFrustumMasks.Length);
            }

            activeGroupIndexCount = 0;
        }

        private void ReleaseGpuBuffers()
        {
            CompletePreparationSlots();
            ReleaseGraphicsBuffer(ref instanceBuffer);
            ReleaseGraphicsBuffer(ref argsBuffer);
            instanceCapacity = 0;
            argsGroupCapacity = 0;
        }

        private static void ReleaseGraphicsBuffer(ref GraphicsBuffer? buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Release();
            buffer = null;
        }

        private void RebuildProviderIndexMap()
        {
            providerIndicesById.Clear();
            for (int i = 0; i < providers.Count; i++)
            {
                providerIndicesById.Add(providers[i].ProviderId, i);
            }
        }

        private void MarkGraphDirty()
        {
            graphDirty = true;
            invalidCompiledPacketsLogged = false;
            unchecked
            {
                graphVersion++;
            }

            InvalidatePreparationFrameCaches();
        }

        private void InvalidatePreparationFrameCaches()
        {
            renderGraphCameraFrame = -1;
            renderGraphCameraId = -1;
            renderGraphCameraSettingsHash = 0;
            renderGraphCameraPreparationSlot = -1;
            renderGraphCameraPreparationVersion = 0;
            pendingCameraFrame = -1;
            pendingCameraId = -1;
            pendingCameraSettingsHash = 0;
            renderGraphShadowFrame = -1;
            renderGraphShadowSettingsHash = 0;
            renderGraphShadowPreparationSlot = -1;
            renderGraphShadowPreparationVersion = 0;
            completedCameraPreparationSlot = -1;
            completedCameraPreparationVersion = 0;
            completedShadowPreparationSlot = -1;
            completedShadowPreparationVersion = 0;
            lastPrepareUsedCameraCache = false;
        }

        private int ComputePrepareSettingsHash(VegetationFoliageFeatureSettings settings, VegetationRenderPassMode passMode)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + graphVersion;
                hash = hash * 31 + settings.NearDetailDistance.GetHashCode();
                hash = hash * 31 + settings.GetWorkBudget(passMode);
                hash = hash * 31 + settings.MaxVisiblePacketInstances;
                hash = hash * 31 + settings.NearDetailResidentByteBudget;
                hash = hash * 31 + settings.NearDetailUploadByteBudget;
                hash = hash * 31 + (passMode == VegetationRenderPassMode.Shadow ? (int)settings.ShadowMode : 0);
                hash = hash * 31 + settings.WindStrength.GetHashCode();
                hash = hash * 31 + settings.WindFrequency.GetHashCode();
                hash = hash * 31 + settings.WindDirection.x.GetHashCode();
                hash = hash * 31 + settings.WindDirection.y.GetHashCode();
                hash = hash * 31 + settings.WindDirection.z.GetHashCode();
                hash = hash * 31 + settings.LeafFlutterStrength.GetHashCode();
                hash = hash * 31 + settings.LeafFlutterFrequencyMultiplier.GetHashCode();
                hash = hash * 31 + settings.LeafFlutterSpatialScale.GetHashCode();
                hash = hash * 31 + settings.LeafFlutterSecondaryStrength.GetHashCode();
                return hash;
            }
        }

        private static FoliageAssetGroup[] CopyAssetGroups(IReadOnlyList<FoliageAssetGroup> source)
        {
            FoliageAssetGroup[] result = new FoliageAssetGroup[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                FoliageAssetGroup group = source[i];
                if (!group.Material.enableInstancing)
                {
                    group.Material.enableInstancing = true;
                }

                result[i] = group;
            }

            return result;
        }

        private struct ProviderRecord
        {
            public ProviderRecord(
                string providerId,
                string debugName,
                FoliageAssemblyAsset assembly,
                FoliageAssetGroup[] assetGroups,
                FoliagePageAsset[] pages)
            {
                ProviderId = providerId;
                DebugName = debugName;
                Assembly = assembly;
                AssetGroups = assetGroups;
                Pages = pages;
                AssetGroupOffset = 0;
            }

            public string ProviderId;
            public string DebugName;
            public FoliageAssemblyAsset Assembly;
            public FoliageAssetGroup[] AssetGroups;
            public FoliagePageAsset[] Pages;
            public int AssetGroupOffset;
        }

        private struct PageRecord
        {
            public PageRecord(
                int pageRecordIndex,
                int providerIndex,
                int providerPageIndex,
                FoliagePageAsset page,
                int firstCellRecord,
                int cellCount)
            {
                ProviderIndex = providerIndex;
                ProviderPageIndex = providerPageIndex;
                Page = page;
                FirstCellRecord = firstCellRecord;
                CellCount = cellCount;
                PageRecordIndex = pageRecordIndex;
            }

            public int ProviderIndex;
            public int ProviderPageIndex;
            public FoliagePageAsset Page;
            public int FirstCellRecord;
            public int CellCount;
            public int PageRecordIndex;
        }

        private struct CellRecord
        {
            public CellRecord(int pageRecordIndex, int cellIndex, Bounds worldBounds)
            {
                PageRecordIndex = pageRecordIndex;
                CellIndex = cellIndex;
                WorldBounds = worldBounds;
            }

            public int PageRecordIndex;
            public int CellIndex;
            public Bounds WorldBounds;
        }

        private struct GroupRecord
        {
            public GroupRecord(FoliageAssetGroup assetGroup)
            {
                AssetGroup = assetGroup;
                Mesh mesh = assetGroup.Mesh;
                IndexCount = (uint)mesh.GetIndexCount(0);
                IndexStart = (uint)mesh.GetIndexStart(0);
                BaseVertex = unchecked((uint)mesh.GetBaseVertex(0));
            }

            public FoliageAssetGroup AssetGroup;
            public uint IndexCount;
            public uint IndexStart;
            public uint BaseVertex;

            public bool TryGetPass(VegetationRenderPassMode passMode, out int passIndex)
            {
                if (passMode == VegetationRenderPassMode.Depth)
                {
                    passIndex = AssetGroup.DepthPassIndex;
                    return passIndex >= 0;
                }

                if (passMode == VegetationRenderPassMode.Shadow)
                {
                    passIndex = AssetGroup.ShadowPassIndex;
                    return passIndex >= 0;
                }

                passIndex = AssetGroup.ForwardPassIndex >= 0 ? AssetGroup.ForwardPassIndex : 0;
                return true;
            }
        }

        private sealed class PreparationSlot : IDisposable
        {
            public PreparationSlot(int slotIndex)
            {
                SlotIndex = slotIndex;
            }

            public int SlotIndex { get; }

            public JobHandle JobHandle;
            public int Version;
            public int UploadedVersion;
            public int CompletedVersion;
            public VegetationRenderPassMode PassMode;
            public int RenderFrame;
            public int CameraId;
            public int SettingsHash;
            public int GraphVersion;
            public int LastRecordedFrame = -1;
            public int GroupCount;
            public int ArgsEntryCount;
            public int FrustumCount;
            public NativeArray<Vector4> FrustumPlanes;
            public NativeArray<PreparationCellCandidate> CellCandidates;
            public NativeArray<PreparationPacketSelection> SelectedPackets;
            public NativeArray<int> PageVisibleMask;
            public NativeArray<VegetationIndirectInstanceData> InstanceData;
            public NativeArray<uint> ArgsData;
            public NativeArray<int> GroupInstanceCounts;
            public NativeArray<int> GroupStartInstances;
            public NativeArray<int> GroupWriteOffsets;
            public NativeArray<int> GroupFrustumMasks;
            public NativeArray<int> Counters;
            public NativeArray<long> LongCounters;
            public float WindStrength;
            public float WindFrequency;
            public Vector4 WindDirection;
            public Vector4 LeafFlutterSettings;

            public void Dispose()
            {
                JobHandle.Complete();
                DisposeNativeArray(ref FrustumPlanes);
                DisposeNativeArray(ref CellCandidates);
                DisposeNativeArray(ref SelectedPackets);
                DisposeNativeArray(ref PageVisibleMask);
                DisposeNativeArray(ref InstanceData);
                DisposeNativeArray(ref ArgsData);
                DisposeNativeArray(ref GroupInstanceCounts);
                DisposeNativeArray(ref GroupStartInstances);
                DisposeNativeArray(ref GroupWriteOffsets);
                DisposeNativeArray(ref GroupFrustumMasks);
                DisposeNativeArray(ref Counters);
                DisposeNativeArray(ref LongCounters);
                Version = 0;
                UploadedVersion = 0;
                CompletedVersion = 0;
                PassMode = default;
                RenderFrame = 0;
                CameraId = 0;
                SettingsHash = 0;
                GraphVersion = 0;
                LastRecordedFrame = -1;
                GroupCount = 0;
                ArgsEntryCount = 0;
                FrustumCount = 0;
            }
        }

        private struct PreparationPageRecord
        {
            public Vector3 BoundsCenter;
            public Vector3 BoundsExtents;
            public int FirstCellRecord;
            public int CellCount;
            public int PageHlodStart;
            public int PageHlodCount;
        }

        private struct PreparationCellRecord
        {
            public Vector3 BoundsCenter;
            public Vector3 BoundsExtents;
            public int PageRecordIndex;
            public int CellHlodStart;
            public int CellHlodCount;
            public int TreeL0Start;
            public int TreeL0Count;
            public int TreeL1Start;
            public int TreeL1Count;
            public int TreeL2Start;
            public int TreeL2Count;
            public long NearDetailBytes;
        }

        private struct PreparationPacketRecord
        {
            public Vector3 BoundsCenter;
            public Vector3 BoundsExtents;
            public int WorldGroupIndex;
            public int FirstInstance;
            public int InstanceCount;
            public int WorkCost;
            public int Residency;
            public int RepresentationKind;
            public int ShadowMode;
            public int ShadowPacketIndex;
        }

        private struct PreparationGroupRecord
        {
            public uint IndexCount;
            public uint IndexStart;
            public uint BaseVertex;
        }

        private readonly struct PreparationCellCandidate : IComparable<PreparationCellCandidate>
        {
            public PreparationCellCandidate(int cellRecordIndex, float distanceSqr, int frustumMask)
            {
                CellRecordIndex = cellRecordIndex;
                DistanceSqr = distanceSqr;
                FrustumMask = frustumMask;
            }

            public int CellRecordIndex { get; }

            public float DistanceSqr { get; }

            public int FrustumMask { get; }

            public int CompareTo(PreparationCellCandidate other)
            {
                return DistanceSqr.CompareTo(other.DistanceSqr);
            }
        }

        private readonly struct PreparationPacketSelection
        {
            public PreparationPacketSelection(int packetRecordIndex, int frustumMask)
            {
                PacketRecordIndex = packetRecordIndex;
                FrustumMask = frustumMask;
            }

            public int PacketRecordIndex { get; }

            public int FrustumMask { get; }
        }

        private struct PrepareFrameJob : IJob
        {
            public NativeArray<PreparationPageRecord> Pages;
            public NativeArray<PreparationCellRecord> Cells;
            public NativeArray<PreparationPacketRecord> Packets;
            public NativeArray<int> PacketLookupIndices;
            public NativeArray<PreparationGroupRecord> Groups;
            public NativeArray<VegetationIndirectInstanceData> StaticInstances;
            public NativeArray<int> NearDetailResidentCellMask;
            public NativeArray<int> NearDetailRequestedCellMask;
            public NativeArray<int> NearDetailLastUsedCellFrame;
            public NativeArray<Vector4> FrustumPlanes;
            public NativeArray<PreparationCellCandidate> CellCandidates;
            public NativeArray<PreparationPacketSelection> SelectedPackets;
            public NativeArray<int> PageVisibleMask;
            public NativeArray<VegetationIndirectInstanceData> InstanceData;
            public NativeArray<uint> ArgsData;
            public NativeArray<int> GroupInstanceCounts;
            public NativeArray<int> GroupStartInstances;
            public NativeArray<int> GroupWriteOffsets;
            public NativeArray<int> GroupFrustumMasks;
            public NativeArray<int> Counters;
            public NativeArray<long> LongCounters;
            public Vector3 CameraWorldPosition;
            public int PassMode;
            public int FrustumCount;
            public int AllFrustumMask;
            public int GroupCount;
            public int ArgsEntryCount;
            public int CellCount;
            public int ResidencyFrameIndex;
            public int WorkBudget;
            public int InstanceBudget;
            public long ResidentByteBudget;
            public long UploadByteBudget;
            public float NearDistanceSqr;

            private int remainingWorkBudget;
            private int remainingInstanceBudget;
            private long remainingUploadByteBudget;
            private long residentBytes;
            private int residentCellCount;
            private int visiblePageCount;
            private int visibleCellCount;
            private int cellCandidateCount;
            private int selectedPacketCount;
            private int preparedPacketCount;
            private int preparedNearDetailPacketCount;
            private int preparedTreeL0PacketCount;
            private int preparedTreeL1PacketCount;
            private int preparedTreeL2PacketCount;
            private int preparedHlodPacketCount;
            private int preparedShadowPacketCount;
            private int preparedFrustumMask;
            private int preparedNearDetailLoadRequestCount;
            private int preparedNearDetailEvictedCellCount;
            private long preparedNearDetailLoadedBytes;

            public void Execute()
            {
                remainingWorkBudget = WorkBudget;
                remainingInstanceBudget = Math.Max(1, InstanceBudget);
                remainingUploadByteBudget = Math.Max(0L, UploadByteBudget);
                RefreshResidentStateFromMask();
                visiblePageCount = 0;
                visibleCellCount = 0;
                cellCandidateCount = 0;
                selectedPacketCount = 0;
                preparedPacketCount = 0;
                preparedNearDetailPacketCount = 0;
                preparedTreeL0PacketCount = 0;
                preparedTreeL1PacketCount = 0;
                preparedTreeL2PacketCount = 0;
                preparedHlodPacketCount = 0;
                preparedShadowPacketCount = 0;
                preparedFrustumMask = 0;
                preparedNearDetailLoadRequestCount = 0;
                preparedNearDetailEvictedCellCount = 0;
                preparedNearDetailLoadedBytes = 0L;

                ClearFrameOutputs();
                EvictNearDetailCellsToBudget(ResidentByteBudget, 0L);
                SelectVisiblePackets();
                SortCellCandidates();
                SelectCellCandidates();
                CompactSelectedPackets();
                WriteCounters();
            }

            private void RefreshResidentStateFromMask()
            {
                residentBytes = 0L;
                residentCellCount = 0;
                int count = Math.Min(CellCount, NearDetailResidentCellMask.Length);
                for (int cellIndex = 0; cellIndex < count; cellIndex++)
                {
                    if (NearDetailResidentCellMask[cellIndex] == 0)
                    {
                        continue;
                    }

                    residentCellCount++;
                    if (cellIndex < Cells.Length)
                    {
                        residentBytes += Math.Max(0L, Cells[cellIndex].NearDetailBytes);
                    }
                }
            }

            private void ClearFrameOutputs()
            {
                for (int i = 0; i < Counters.Length; i++)
                {
                    Counters[i] = 0;
                }

                for (int i = 0; i < LongCounters.Length; i++)
                {
                    LongCounters[i] = 0L;
                }

                for (int i = 0; i < PageVisibleMask.Length; i++)
                {
                    PageVisibleMask[i] = 0;
                }

                for (int i = 0; i < NearDetailRequestedCellMask.Length; i++)
                {
                    NearDetailRequestedCellMask[i] = 0;
                }

                int safeGroupEntryCount = ResolveSafeGroupEntryCount();
                for (int entryIndex = 0; entryIndex < safeGroupEntryCount; entryIndex++)
                {
                    GroupInstanceCounts[entryIndex] = 0;
                    GroupStartInstances[entryIndex] = 0;
                    GroupWriteOffsets[entryIndex] = 0;
                    WriteArgs(entryIndex, ResolveGroupIndexFromEntry(entryIndex), 0);
                }

                int safeGroupCount = Math.Min(GroupCount, GroupFrustumMasks.Length);
                for (int groupIndex = 0; groupIndex < safeGroupCount; groupIndex++)
                {
                    GroupFrustumMasks[groupIndex] = 0;
                }
            }

            private void SelectVisiblePackets()
            {
                int pageCount = Pages.Length;
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    PreparationPageRecord page = Pages[pageIndex];
                    int pageFrustumMask = TestBoundsFrustumMask(page.BoundsCenter, page.BoundsExtents, AllFrustumMask);
                    if (pageFrustumMask == 0)
                    {
                        continue;
                    }

                    MarkPageVisible(pageIndex);
                    bool hasVisibleCell = false;
                    for (int cellOffset = 0; cellOffset < page.CellCount; cellOffset++)
                    {
                        int cellRecordIndex = page.FirstCellRecord + cellOffset;
                        if (cellRecordIndex < 0 || cellRecordIndex >= CellCount || cellRecordIndex >= Cells.Length)
                        {
                            continue;
                        }

                        PreparationCellRecord cell = Cells[cellRecordIndex];
                        int cellFrustumMask = TestBoundsFrustumMask(cell.BoundsCenter, cell.BoundsExtents, pageFrustumMask);
                        if (cellFrustumMask == 0)
                        {
                            continue;
                        }

                        hasVisibleCell = true;
                        visibleCellCount++;
                        AddCellCandidate(new PreparationCellCandidate(
                            cellRecordIndex,
                            CalculateBoundsDistanceSqr(cell.BoundsCenter, cell.BoundsExtents, CameraWorldPosition),
                            cellFrustumMask));
                    }

                    if (!hasVisibleCell)
                    {
                        TrySelectRange(page.PageHlodStart, page.PageHlodCount, pageFrustumMask, chargeBudget: false, cellRecordIndex: -1);
                    }
                }
            }

            private void SortCellCandidates()
            {
                if (cellCandidateCount <= 1)
                {
                    return;
                }

                for (int i = 1; i < cellCandidateCount; i++)
                {
                    PreparationCellCandidate value = CellCandidates[i];
                    int j = i - 1;
                    while (j >= 0 && CellCandidates[j].CompareTo(value) > 0)
                    {
                        CellCandidates[j + 1] = CellCandidates[j];
                        j--;
                    }

                    CellCandidates[j + 1] = value;
                }
            }

            private void SelectCellCandidates()
            {
                for (int i = 0; i < cellCandidateCount; i++)
                {
                    PreparationCellCandidate candidate = CellCandidates[i];
                    if (candidate.CellRecordIndex < 0 || candidate.CellRecordIndex >= Cells.Length)
                    {
                        continue;
                    }

                    PreparationCellRecord cell = Cells[candidate.CellRecordIndex];
                    if (candidate.DistanceSqr <= NearDistanceSqr)
                    {
                        if (TryEnsureNearDetailCellResident(candidate.CellRecordIndex))
                        {
                            int desiredTier = ResolveTier(candidate.DistanceSqr, NearDistanceSqr);
                            if (TrySelectNearDetailTierCascade(cell, desiredTier, candidate.FrustumMask, candidate.CellRecordIndex))
                            {
                                continue;
                            }
                        }
                    }

                    TrySelectRange(cell.CellHlodStart, cell.CellHlodCount, candidate.FrustumMask, chargeBudget: false, candidate.CellRecordIndex);
                }
            }

            private bool TrySelectNearDetailTierCascade(
                PreparationCellRecord cell,
                int desiredTier,
                int frustumMask,
                int cellRecordIndex)
            {
                if (desiredTier == (int)FoliageRepresentationKind.TreeL0 &&
                    TrySelectRange(cell.TreeL0Start, cell.TreeL0Count, frustumMask, chargeBudget: true, cellRecordIndex))
                {
                    return true;
                }

                if ((desiredTier == (int)FoliageRepresentationKind.TreeL0 ||
                     desiredTier == (int)FoliageRepresentationKind.TreeL1) &&
                    TrySelectRange(cell.TreeL1Start, cell.TreeL1Count, frustumMask, chargeBudget: true, cellRecordIndex))
                {
                    return true;
                }

                return TrySelectRange(cell.TreeL2Start, cell.TreeL2Count, frustumMask, chargeBudget: true, cellRecordIndex);
            }

            private bool TrySelectRange(
                int rangeStart,
                int rangeCount,
                int visibilityFrustumMask,
                bool chargeBudget,
                int cellRecordIndex)
            {
                if (rangeCount <= 0 || rangeStart < 0 || visibilityFrustumMask == 0)
                {
                    return false;
                }

                int packetCost = 0;
                int packetInstances = 0;
                int matchingPacketCount = 0;
                for (int rangeOffset = 0; rangeOffset < rangeCount; rangeOffset++)
                {
                    int lookupIndex = rangeStart + rangeOffset;
                    if (lookupIndex < 0 || lookupIndex >= PacketLookupIndices.Length)
                    {
                        continue;
                    }

                    if (!TryResolveSelectablePacket(
                            PacketLookupIndices[lookupIndex],
                            visibilityFrustumMask,
                            out int resolvedPacketIndex,
                            out _))
                    {
                        continue;
                    }

                    PreparationPacketRecord packet = Packets[resolvedPacketIndex];
                    if (packet.InstanceCount <= 0)
                    {
                        continue;
                    }

                    packetCost += Math.Max(1, packet.WorkCost);
                    packetInstances += packet.InstanceCount;
                    matchingPacketCount++;
                }

                if (matchingPacketCount == 0 ||
                    (chargeBudget && (packetCost > remainingWorkBudget || packetInstances > remainingInstanceBudget)))
                {
                    return false;
                }

                for (int rangeOffset = 0; rangeOffset < rangeCount; rangeOffset++)
                {
                    int lookupIndex = rangeStart + rangeOffset;
                    if (lookupIndex < 0 || lookupIndex >= PacketLookupIndices.Length)
                    {
                        continue;
                    }

                    if (!TryResolveSelectablePacket(
                            PacketLookupIndices[lookupIndex],
                            visibilityFrustumMask,
                            out int resolvedPacketIndex,
                            out int packetFrustumMask))
                    {
                        continue;
                    }

                    PreparationPacketRecord packet = Packets[resolvedPacketIndex];
                    if (packet.InstanceCount <= 0)
                    {
                        continue;
                    }

                    AddSelectedPacket(new PreparationPacketSelection(resolvedPacketIndex, packetFrustumMask));
                    AddGroupInstanceCounts(packet.WorldGroupIndex, packet.InstanceCount, packetFrustumMask);

                    preparedFrustumMask |= packetFrustumMask;
                    if (packet.Residency == (int)FoliagePacketResidency.NearDetail)
                    {
                        if (cellRecordIndex >= 0 && cellRecordIndex < NearDetailLastUsedCellFrame.Length)
                        {
                            NearDetailLastUsedCellFrame[cellRecordIndex] = ResidencyFrameIndex;
                        }

                        preparedNearDetailPacketCount++;
                        IncrementNearDetailTierCounter(packet.RepresentationKind);
                    }
                    else
                    {
                        preparedHlodPacketCount++;
                    }

                    if (PassMode == (int)VegetationRenderPassMode.Shadow)
                    {
                        preparedShadowPacketCount++;
                    }
                }

                if (chargeBudget)
                {
                    remainingWorkBudget -= packetCost;
                    remainingInstanceBudget -= packetInstances;
                }

                preparedPacketCount += matchingPacketCount;
                return true;
            }

            private bool TryResolveSelectablePacket(
                int packetIndex,
                int visibilityFrustumMask,
                out int resolvedPacketIndex,
                out int packetFrustumMask)
            {
                resolvedPacketIndex = -1;
                packetFrustumMask = 0;
                if (packetIndex < 0 || packetIndex >= Packets.Length || visibilityFrustumMask == 0)
                {
                    return false;
                }

                PreparationPacketRecord source = Packets[packetIndex];
                if (PassMode == (int)VegetationRenderPassMode.Shadow)
                {
                    if (source.ShadowMode == (int)FoliageShadowPacketMode.None ||
                        !IsCheapShadowPacketMode(source.ShadowMode) ||
                        source.ShadowPacketIndex < 0 ||
                        source.ShadowPacketIndex >= Packets.Length)
                    {
                        return false;
                    }

                    PreparationPacketRecord shadowPacket = Packets[source.ShadowPacketIndex];
                    if (shadowPacket.ShadowMode == (int)FoliageShadowPacketMode.None ||
                        shadowPacket.WorldGroupIndex < 0)
                    {
                        return false;
                    }

                    packetFrustumMask = TestBoundsFrustumMask(
                        shadowPacket.BoundsCenter,
                        shadowPacket.BoundsExtents,
                        visibilityFrustumMask);
                    resolvedPacketIndex = source.ShadowPacketIndex;
                    return packetFrustumMask != 0;
                }

                if (source.WorldGroupIndex < 0)
                {
                    return false;
                }

                packetFrustumMask = TestBoundsFrustumMask(source.BoundsCenter, source.BoundsExtents, visibilityFrustumMask);
                resolvedPacketIndex = packetIndex;
                return packetFrustumMask != 0;
            }

            private static bool IsCheapShadowPacketMode(int shadowMode)
            {
                return shadowMode == (int)FoliageShadowPacketMode.CheapTree ||
                       shadowMode == (int)FoliageShadowPacketMode.Hlod;
            }

            private bool TryEnsureNearDetailCellResident(int cellRecordIndex)
            {
                if (cellRecordIndex < 0 || cellRecordIndex >= Cells.Length)
                {
                    return false;
                }

                long cellBytes = Cells[cellRecordIndex].NearDetailBytes;
                if (cellBytes <= 0L)
                {
                    return true;
                }

                if (cellRecordIndex < NearDetailRequestedCellMask.Length &&
                    NearDetailRequestedCellMask[cellRecordIndex] == 0)
                {
                    NearDetailRequestedCellMask[cellRecordIndex] = 1;
                    preparedNearDetailLoadRequestCount++;
                }

                if (cellBytes > ResidentByteBudget)
                {
                    UnloadNearDetailCell(cellRecordIndex, countEviction: IsNearDetailCellResident(cellRecordIndex));
                    return false;
                }

                if (IsNearDetailCellResident(cellRecordIndex))
                {
                    NearDetailLastUsedCellFrame[cellRecordIndex] = ResidencyFrameIndex;
                    return true;
                }

                if (cellBytes > remainingUploadByteBudget)
                {
                    return false;
                }

                EvictNearDetailCellsToBudget(ResidentByteBudget, cellBytes);
                if (residentBytes + cellBytes > ResidentByteBudget)
                {
                    return false;
                }

                NearDetailResidentCellMask[cellRecordIndex] = 1;
                NearDetailLastUsedCellFrame[cellRecordIndex] = ResidencyFrameIndex;
                residentBytes += cellBytes;
                residentCellCount++;
                remainingUploadByteBudget -= cellBytes;
                preparedNearDetailLoadedBytes += cellBytes;
                return true;
            }

            private void EvictNearDetailCellsToBudget(long residentByteBudget, long incomingBytes)
            {
                while (residentBytes + incomingBytes > residentByteBudget)
                {
                    int candidateCellIndex = -1;
                    int oldestFrame = int.MaxValue;
                    int count = Math.Min(CellCount, NearDetailResidentCellMask.Length);
                    for (int cellIndex = 0; cellIndex < count; cellIndex++)
                    {
                        if (NearDetailResidentCellMask[cellIndex] == 0 ||
                            NearDetailRequestedCellMask[cellIndex] != 0)
                        {
                            continue;
                        }

                        int lastUsedFrame = NearDetailLastUsedCellFrame[cellIndex];
                        if (lastUsedFrame < oldestFrame)
                        {
                            oldestFrame = lastUsedFrame;
                            candidateCellIndex = cellIndex;
                        }
                    }

                    if (candidateCellIndex < 0)
                    {
                        return;
                    }

                    UnloadNearDetailCell(candidateCellIndex, countEviction: true);
                }
            }

            private void UnloadNearDetailCell(int cellRecordIndex, bool countEviction)
            {
                if (!IsNearDetailCellResident(cellRecordIndex))
                {
                    return;
                }

                NearDetailResidentCellMask[cellRecordIndex] = 0;
                NearDetailLastUsedCellFrame[cellRecordIndex] = 0;
                long cellBytes = cellRecordIndex < Cells.Length ? Cells[cellRecordIndex].NearDetailBytes : 0L;
                residentBytes = Math.Max(0L, residentBytes - cellBytes);
                residentCellCount = Math.Max(0, residentCellCount - 1);
                if (countEviction)
                {
                    preparedNearDetailEvictedCellCount++;
                }
            }

            private bool IsNearDetailCellResident(int cellRecordIndex)
            {
                return cellRecordIndex >= 0 &&
                       cellRecordIndex < NearDetailResidentCellMask.Length &&
                       NearDetailResidentCellMask[cellRecordIndex] != 0;
            }

            private void CompactSelectedPackets()
            {
                int preparedInstanceCount = 0;
                int preparedActiveGroupCount = 0;
                int safeGroupEntryCount = ResolveSafeGroupEntryCount();
                for (int entryIndex = 0; entryIndex < safeGroupEntryCount; entryIndex++)
                {
                    int groupIndex = ResolveGroupIndexFromEntry(entryIndex);
                    int groupInstanceCount = GroupInstanceCounts[entryIndex];
                    if (groupInstanceCount <= 0)
                    {
                        WriteArgs(entryIndex, groupIndex, 0);
                        continue;
                    }

                    if (preparedInstanceCount >= InstanceData.Length)
                    {
                        GroupInstanceCounts[entryIndex] = 0;
                        GroupStartInstances[entryIndex] = 0;
                        WriteArgs(entryIndex, groupIndex, 0);
                        continue;
                    }

                    int clampedCount = Math.Min(groupInstanceCount, InstanceData.Length - preparedInstanceCount);
                    GroupInstanceCounts[entryIndex] = clampedCount;
                    GroupStartInstances[entryIndex] = preparedInstanceCount;
                    GroupWriteOffsets[entryIndex] = 0;
                    preparedInstanceCount += clampedCount;
                    preparedActiveGroupCount++;
                    WriteArgs(entryIndex, groupIndex, clampedCount);
                }

                for (int selectionIndex = 0; selectionIndex < selectedPacketCount; selectionIndex++)
                {
                    PreparationPacketSelection selection = SelectedPackets[selectionIndex];
                    if (selection.PacketRecordIndex < 0 || selection.PacketRecordIndex >= Packets.Length)
                    {
                        continue;
                    }

                    PreparationPacketRecord packet = Packets[selection.PacketRecordIndex];
                    int groupIndex = packet.WorldGroupIndex;
                    if (groupIndex < 0 || groupIndex >= GroupCount)
                    {
                        continue;
                    }

                    if (PassMode == (int)VegetationRenderPassMode.Shadow)
                    {
                        CopyPacketInstancesToShadowEntries(packet, groupIndex, selection.FrustumMask);
                        continue;
                    }

                    CopyPacketInstancesToEntry(packet, groupIndex);
                }

                Counters[PreparationCounterPreparedInstanceCount] = preparedInstanceCount;
                Counters[PreparationCounterPreparedActiveGroupCount] = preparedActiveGroupCount;
            }

            private void AddGroupInstanceCounts(int groupIndex, int instanceCount, int frustumMask)
            {
                if (groupIndex < 0 || groupIndex >= GroupCount || instanceCount <= 0)
                {
                    return;
                }

                if (groupIndex < GroupFrustumMasks.Length)
                {
                    GroupFrustumMasks[groupIndex] |= frustumMask;
                }

                if (PassMode != (int)VegetationRenderPassMode.Shadow)
                {
                    if (groupIndex < GroupInstanceCounts.Length)
                    {
                        GroupInstanceCounts[groupIndex] += instanceCount;
                    }

                    return;
                }

                int frustumLimit = Math.Min(FrustumCount, 30);
                for (int frustumIndex = 0; frustumIndex < frustumLimit; frustumIndex++)
                {
                    int frustumBit = 1 << frustumIndex;
                    if ((frustumMask & frustumBit) == 0)
                    {
                        continue;
                    }

                    int entryIndex = groupIndex + frustumIndex * GroupCount;
                    if (entryIndex >= 0 && entryIndex < GroupInstanceCounts.Length)
                    {
                        GroupInstanceCounts[entryIndex] += instanceCount;
                    }
                }
            }

            private void CopyPacketInstancesToShadowEntries(PreparationPacketRecord packet, int groupIndex, int frustumMask)
            {
                int frustumLimit = Math.Min(FrustumCount, 30);
                for (int frustumIndex = 0; frustumIndex < frustumLimit; frustumIndex++)
                {
                    int frustumBit = 1 << frustumIndex;
                    if ((frustumMask & frustumBit) == 0)
                    {
                        continue;
                    }

                    CopyPacketInstancesToEntry(packet, groupIndex + frustumIndex * GroupCount);
                }
            }

            private void CopyPacketInstancesToEntry(PreparationPacketRecord packet, int entryIndex)
            {
                if (entryIndex < 0 || entryIndex >= GroupInstanceCounts.Length)
                {
                    return;
                }

                int groupOffset = GroupWriteOffsets[entryIndex];
                int groupLimit = GroupInstanceCounts[entryIndex];
                int groupStart = GroupStartInstances[entryIndex];
                for (int instanceOffset = 0; instanceOffset < packet.InstanceCount; instanceOffset++)
                {
                    if (groupOffset >= groupLimit)
                    {
                        break;
                    }

                    int sourceIndex = packet.FirstInstance + instanceOffset;
                    int writeIndex = groupStart + groupOffset;
                    if (sourceIndex >= 0 &&
                        sourceIndex < StaticInstances.Length &&
                        writeIndex >= 0 &&
                        writeIndex < InstanceData.Length)
                    {
                        InstanceData[writeIndex] = StaticInstances[sourceIndex];
                        groupOffset++;
                    }
                }

                GroupWriteOffsets[entryIndex] = groupOffset;
            }

            private void WriteCounters()
            {
                Counters[PreparationCounterPreparedPacketCount] = preparedPacketCount;
                Counters[PreparationCounterPreparedNearDetailPacketCount] = preparedNearDetailPacketCount;
                Counters[PreparationCounterPreparedTreeL0PacketCount] = preparedTreeL0PacketCount;
                Counters[PreparationCounterPreparedTreeL1PacketCount] = preparedTreeL1PacketCount;
                Counters[PreparationCounterPreparedTreeL2PacketCount] = preparedTreeL2PacketCount;
                Counters[PreparationCounterPreparedHlodPacketCount] = preparedHlodPacketCount;
                Counters[PreparationCounterPreparedShadowPacketCount] = preparedShadowPacketCount;
                Counters[PreparationCounterPreparedFrustumMask] = preparedFrustumMask;
                Counters[PreparationCounterVisiblePageCount] = visiblePageCount;
                Counters[PreparationCounterVisibleCellCount] = visibleCellCount;
                Counters[PreparationCounterNearDetailLoadRequestCount] = preparedNearDetailLoadRequestCount;
                Counters[PreparationCounterNearDetailEvictedCellCount] = preparedNearDetailEvictedCellCount;
                Counters[PreparationCounterNearDetailResidentCellCount] = residentCellCount;
                LongCounters[PreparationLongCounterNearDetailResidentBytes] = residentBytes;
                LongCounters[PreparationLongCounterNearDetailLoadedBytes] = preparedNearDetailLoadedBytes;
            }

            private void AddCellCandidate(PreparationCellCandidate candidate)
            {
                if (cellCandidateCount >= CellCandidates.Length)
                {
                    return;
                }

                CellCandidates[cellCandidateCount++] = candidate;
            }

            private void AddSelectedPacket(PreparationPacketSelection selection)
            {
                if (selectedPacketCount >= SelectedPackets.Length)
                {
                    return;
                }

                SelectedPackets[selectedPacketCount++] = selection;
            }

            private void MarkPageVisible(int pageIndex)
            {
                if (pageIndex < 0 || pageIndex >= PageVisibleMask.Length || PageVisibleMask[pageIndex] != 0)
                {
                    return;
                }

                PageVisibleMask[pageIndex] = 1;
                visiblePageCount++;
            }

            private void IncrementNearDetailTierCounter(int representationKind)
            {
                if (representationKind == (int)FoliageRepresentationKind.TreeL0)
                {
                    preparedTreeL0PacketCount++;
                    return;
                }

                if (representationKind == (int)FoliageRepresentationKind.TreeL1)
                {
                    preparedTreeL1PacketCount++;
                    return;
                }

                if (representationKind == (int)FoliageRepresentationKind.TreeL2)
                {
                    preparedTreeL2PacketCount++;
                }
            }

            private int ResolveSafeGroupEntryCount()
            {
                int requestedCount = ArgsEntryCount > 0 ? ArgsEntryCount : GroupCount;
                return Math.Min(Math.Min(requestedCount, GroupInstanceCounts.Length), ArgsData.Length / IndirectArgsUIntCount);
            }

            private int ResolveGroupIndexFromEntry(int entryIndex)
            {
                if (GroupCount <= 0)
                {
                    return -1;
                }

                return entryIndex % GroupCount;
            }

            private void WriteArgs(int entryIndex, int groupIndex, int instanceCount)
            {
                int argsOffset = entryIndex * IndirectArgsUIntCount;
                if (entryIndex < 0 ||
                    groupIndex < 0 ||
                    groupIndex >= Groups.Length ||
                    argsOffset < 0 ||
                    argsOffset + 4 >= ArgsData.Length)
                {
                    return;
                }

                PreparationGroupRecord group = Groups[groupIndex];
                ArgsData[argsOffset] = group.IndexCount;
                ArgsData[argsOffset + 1] = (uint)Math.Max(0, instanceCount);
                ArgsData[argsOffset + 2] = group.IndexStart;
                ArgsData[argsOffset + 3] = group.BaseVertex;
                ArgsData[argsOffset + 4] = 0u;
            }

            private int TestBoundsFrustumMask(Vector3 center, Vector3 extents, int frustumMask)
            {
                int visibleMask = 0;
                for (int frustumIndex = 0; frustumIndex < FrustumCount; frustumIndex++)
                {
                    int frustumBit = 1 << frustumIndex;
                    if ((frustumMask & frustumBit) == 0)
                    {
                        continue;
                    }

                    if (TestBoundsFrustum(center, extents, frustumIndex * 6))
                    {
                        visibleMask |= frustumBit;
                    }
                }

                return visibleMask;
            }

            private bool TestBoundsFrustum(Vector3 center, Vector3 extents, int planeOffset)
            {
                for (int planeIndex = 0; planeIndex < 6; planeIndex++)
                {
                    int index = planeOffset + planeIndex;
                    if (index < 0 || index >= FrustumPlanes.Length)
                    {
                        return false;
                    }

                    Vector4 plane = FrustumPlanes[index];
                    float radius =
                        extents.x * Abs(plane.x) +
                        extents.y * Abs(plane.y) +
                        extents.z * Abs(plane.z);
                    if (plane.x * center.x + plane.y * center.y + plane.z * center.z + plane.w + radius < 0f)
                    {
                        return false;
                    }
                }

                return true;
            }

            private static float CalculateBoundsDistanceSqr(Vector3 center, Vector3 extents, Vector3 point)
            {
                float dx = Math.Max(Abs(point.x - center.x) - extents.x, 0f);
                float dy = Math.Max(Abs(point.y - center.y) - extents.y, 0f);
                float dz = Math.Max(Abs(point.z - center.z) - extents.z, 0f);
                return dx * dx + dy * dy + dz * dz;
            }

            private static int ResolveTier(float distanceSqr, float nearDistanceSqr)
            {
                float nearThird = nearDistanceSqr * 0.11111111f;
                if (distanceSqr <= nearThird)
                {
                    return (int)FoliageRepresentationKind.TreeL0;
                }

                if (distanceSqr <= nearDistanceSqr * 0.44444444f)
                {
                    return (int)FoliageRepresentationKind.TreeL1;
                }

                return (int)FoliageRepresentationKind.TreeL2;
            }

            private static float Abs(float value)
            {
                return value < 0f ? -value : value;
            }
        }

        private struct PacketRange
        {
            public int Start;
            public int Count;
        }

    }
}
