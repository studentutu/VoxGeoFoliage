#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

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
        private static readonly VegetationRenderWorld SharedInstance = new VegetationRenderWorld();
        private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare");
        private static readonly ProfilerMarker PrepareEnsureGraphMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.EnsureGraph");
        private static readonly ProfilerMarker PrepareBroadPhaseMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.BroadPhase");
        private static readonly ProfilerMarker PrepareSelectPacketsMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.SelectPackets");
        private static readonly ProfilerMarker PrepareUploadMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.Upload");
        private static readonly ProfilerMarker PrepareUploadLayoutMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.Upload.Layout");
        private static readonly ProfilerMarker PrepareUploadCopyInstancesMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.Upload.CopyInstances");
        private static readonly ProfilerMarker PrepareUploadArgsMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.Upload.Args");
        private static readonly ProfilerMarker PrepareUploadSetDataMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Prepare.Upload.SetData");
        private static readonly ProfilerMarker RenderMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Render");
        private static readonly ProfilerMarker RenderDrawGroupsMarker = new ProfilerMarker("VoxGeoFol.VegetationRenderWorld.Render.DrawGroups");
        private static readonly int InstanceBufferId = Shader.PropertyToID("_VegetationInstanceData");
        private static readonly int InstanceBufferBaseOffsetId = Shader.PropertyToID("_VegetationInstanceDataBaseOffset");
        private static readonly int WindStrengthId = Shader.PropertyToID("_VegetationWindStrength");
        private static readonly int WindFrequencyId = Shader.PropertyToID("_VegetationWindFrequency");
        private static readonly int WindDirectionId = Shader.PropertyToID("_VegetationWindDirection");
        private static readonly int LeafFlutterSettingsId = Shader.PropertyToID("_VegetationLeafFlutterSettings");
        private const string IndirectRenderingKeyword = "_VOXGEOFOL_INDIRECT_RENDERING";

        private readonly List<ProviderRecord> providers = new List<ProviderRecord>();
        private readonly List<PageRecord> pages = new List<PageRecord>();
        private readonly List<CellRecord> cells = new List<CellRecord>();
        private readonly List<GroupRecord> groups = new List<GroupRecord>();
        private readonly Dictionary<string, int> providerIndicesById = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<CellCandidate> cellCandidates = new List<CellCandidate>();
        private readonly List<PacketSelection> selectedPackets = new List<PacketSelection>();
        private readonly List<int> activeGroupIndices = new List<int>();
        private readonly VegetationCommandBufferIndirectDrawWrapper commandBufferDrawWrapper = new VegetationCommandBufferIndirectDrawWrapper();
        private readonly VegetationRasterCommandBufferIndirectDrawWrapper rasterCommandBufferDrawWrapper = new VegetationRasterCommandBufferIndirectDrawWrapper();
        private readonly MaterialPropertyBlock sharedPropertyBlock = new MaterialPropertyBlock();
        private readonly Plane[] cameraFrustumPlanes = new Plane[6];

        private BoundingSphere[] boundingSpheres = Array.Empty<BoundingSphere>();
        private SphereRecord[] sphereRecords = Array.Empty<SphereRecord>();
        private int[] visibleSphereIndices = Array.Empty<int>();
        private bool[] visiblePageMask = Array.Empty<bool>();
        private bool[] visibleCellMask = Array.Empty<bool>();
        private int[] visiblePageFrustumMasks = Array.Empty<int>();
        private int[] visibleCellFrustumMasks = Array.Empty<int>();
        private bool[] nearDetailResidentCellMask = Array.Empty<bool>();
        private bool[] nearDetailRequestedCellMask = Array.Empty<bool>();
        private long[] nearDetailCellBytes = Array.Empty<long>();
        private int[] nearDetailLastUsedCellFrame = Array.Empty<int>();
        private PacketRange[] pagePacketRanges = Array.Empty<PacketRange>();
        private PacketRange[] cellPacketRanges = Array.Empty<PacketRange>();
        private int[] packetLookupIndices = Array.Empty<int>();
        private int[] groupInstanceCounts = Array.Empty<int>();
        private int[] groupFrustumMasks = Array.Empty<int>();
        private int[] groupStartInstances = Array.Empty<int>();
        private int[] groupWriteOffsets = Array.Empty<int>();
        private NativeArray<uint> argsData;
        private NativeArray<VegetationIndirectInstanceData> instanceData;
        private GraphicsBuffer? instanceBuffer;
        private GraphicsBuffer? argsBuffer;
        private CullingGroup? cullingGroup;
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
        private int cachedCameraFrame = -1;
        private int cachedCameraId = -1;
        private int cachedCameraSettingsHash;
        private bool lastPrepareUsedCameraCache;
        private bool disposed;

        private VegetationRenderWorld()
        {
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
                ReleaseCullingGroup();
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
            ReleaseCullingGroup();
            MarkGraphDirty();
        }

        /// <summary>
        /// [INTEGRATION] Prepares grouped indirect buffers for one camera-visible color or depth pass using page/cell CullingGroup broad phase.
        /// </summary>
        public bool PrepareForCamera(Camera camera, VegetationRenderPassMode passMode, VegetationFoliageFeatureSettings settings)
        {
            // Range: one camera and one non-shadow pass. Condition: CullingGroup selects visible page/cell spheres, then active budget chooses HLOD or near-detail packets. Output: instance and args buffers are ready for grouped indirect submission.
            lastPrepareUsedCameraCache = false;
            if (camera == null || passMode == VegetationRenderPassMode.Shadow)
            {
                return false;
            }

            int frame = Time.renderedFrameCount;
            int cameraId = camera.GetInstanceID();
            int settingsHash = ComputeCameraPrepareSettingsHash(settings);
            if (!graphDirty &&
                hasPreparedFrame &&
                cachedCameraFrame == frame &&
                cachedCameraId == cameraId &&
                cachedCameraSettingsHash == settingsHash)
            {
                lastPrepareUsedCameraCache = true;
                return true;
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

                using (PrepareBroadPhaseMarker.Auto())
                {
                    EnsureCullingGroup(camera);
                    ClearVisibilityMasks();
                    int visibleCount = cullingGroup!.QueryIndices(true, visibleSphereIndices, 0);
                    MarkVisibleSpheres(visibleCount);
                    if (preparedVisiblePageCount == 0 || preparedVisibleCellCount == 0)
                    {
                        // CullingGroup results can be one camera cull late; first-frame fallback stays conservative.
                        GeometryUtility.CalculateFrustumPlanes(camera, cameraFrustumPlanes);
                        MarkVisibleByFrustum(cameraFrustumPlanes);
                    }
                }

                using (PrepareSelectPacketsMarker.Auto())
                {
                    SelectPackets(camera.transform.position, passMode, settings, null, 0);
                }

                bool prepared;
                using (PrepareUploadMarker.Auto())
                {
                    prepared = UploadPreparedFrame(settings, passMode);
                }

                if (prepared)
                {
                    cachedCameraFrame = frame;
                    cachedCameraId = cameraId;
                    cachedCameraSettingsHash = settingsHash;
                }

                return prepared;
            }
        }

        /// <summary>
        /// [INTEGRATION] Prepares grouped indirect buffers for one explicit frustum, used by main-light shadow cascades.
        /// </summary>
        public bool PrepareForFrustum(
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            VegetationFoliageFeatureSettings settings)
        {
            return PrepareForFrustums(cameraWorldPosition, frustumPlanes, 1, settings);
        }

        /// <summary>
        /// [INTEGRATION] Prepares one grouped shadow frame for a batch of main-light cascade frustums.
        /// </summary>
        public bool PrepareForFrustums(
            Vector3 cameraWorldPosition,
            Plane[] frustumPlanes,
            int frustumCount,
            VegetationFoliageFeatureSettings settings)
        {
            // Range: one or more main-light shadow cascade frustums. Condition: compiled packet shadow modes decide the submitted caster set; no runtime shadow-proxy promotion exists. Output: one shared shadow instance/args frame is ready for all cascade submissions.
            int validatedFrustumCount = ResolveFrustumCount(frustumPlanes, frustumCount);
            if (validatedFrustumCount <= 0 || settings.ShadowMode == VegetationShadowMode.Off)
            {
                return false;
            }

            InvalidatePreparedCameraCache();
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
                    ClearVisibilityMasks();
                    MarkVisibleByFrustums(frustumPlanes, validatedFrustumCount);
                }

                using (PrepareSelectPacketsMarker.Auto())
                {
                    SelectPackets(cameraWorldPosition, VegetationRenderPassMode.Shadow, settings, frustumPlanes, validatedFrustumCount);
                }

                using (PrepareUploadMarker.Auto())
                {
                    return UploadPreparedFrame(settings, VegetationRenderPassMode.Shadow);
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
            commandBuffer.EnableShaderKeyword(IndirectRenderingKeyword);
            try
            {
                RenderInternal(camera, passMode, rasterCommandBufferDrawWrapper, diagnosticsEnabled, shadowFrustumIndex);
            }
            finally
            {
                commandBuffer.DisableShaderKeyword(IndirectRenderingKeyword);
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
            commandBuffer.EnableShaderKeyword(IndirectRenderingKeyword);
            try
            {
                RenderInternal(camera, passMode, commandBufferDrawWrapper, diagnosticsEnabled, shadowFrustumIndex);
            }
            finally
            {
                commandBuffer.DisableShaderKeyword(IndirectRenderingKeyword);
                commandBufferDrawWrapper.ClearCommandBuffer();
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
                    for (int i = 0; i < activeGroupIndices.Count; i++)
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

                        sharedPropertyBlock.SetInt(InstanceBufferBaseOffsetId, groupStartInstances[groupIndex]);
                        drawWrapper.DrawMeshInstancedIndirect(
                            group.AssetGroup.Mesh,
                            group.AssetGroup.Material,
                            argsBuffer,
                            group.ArgsBufferOffset,
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

        private void EnsureCompiledGraph()
        {
            if (!graphDirty)
            {
                return;
            }

            ClearCompiledGraph();
            int sphereCount = 0;
            for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                ProviderRecord provider = providers[providerIndex];
                provider.AssetGroupOffset = groups.Count;
                providers[providerIndex] = provider;

                for (int groupIndex = 0; groupIndex < provider.AssetGroups.Length; groupIndex++)
                {
                    groups.Add(new GroupRecord(provider.AssetGroups[groupIndex], groups.Count));
                }

                for (int pageIndex = 0; pageIndex < provider.Pages.Length; pageIndex++)
                {
                    FoliagePageAsset page = provider.Pages[pageIndex];
                    int pageRecordIndex = pages.Count;
                    int firstCellRecord = cells.Count;
                    PageRecord pageRecord = new PageRecord(pageRecordIndex, providerIndex, pageIndex, page, sphereCount, firstCellRecord, page.Cells.Count);
                    pages.Add(pageRecord);
                    sphereCount++;

                    for (int cellOffset = 0; cellOffset < page.Cells.Count; cellOffset++)
                    {
                        FoliagePageCell cell = page.Cells[cellOffset];
                        cells.Add(new CellRecord(pageRecordIndex, cell.CellIndex, sphereCount, cell.WorldBounds));
                        sphereCount++;
                    }
                }
            }

            boundingSpheres = new BoundingSphere[Mathf.Max(0, sphereCount)];
            sphereRecords = new SphereRecord[Mathf.Max(0, sphereCount)];
            visibleSphereIndices = new int[Mathf.Max(1, sphereCount)];
            visiblePageMask = new bool[pages.Count];
            visibleCellMask = new bool[cells.Count];
            visiblePageFrustumMasks = new int[pages.Count];
            visibleCellFrustumMasks = new int[cells.Count];
            nearDetailResidentCellMask = new bool[cells.Count];
            nearDetailRequestedCellMask = new bool[cells.Count];
            nearDetailCellBytes = new long[cells.Count];
            nearDetailLastUsedCellFrame = new int[cells.Count];
            nearDetailResidentBytes = 0L;
            nearDetailResidentCellCount = 0;
            int writeSphere = 0;
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                PageRecord page = pages[pageIndex];
                boundingSpheres[writeSphere] = new BoundingSphere(
                    page.Page.WorldBounds.center,
                    Mathf.Max(0.01f, page.Page.WorldBounds.extents.magnitude));
                sphereRecords[writeSphere] = SphereRecord.Page(pageIndex);
                page.PageSphereIndex = writeSphere;
                pages[pageIndex] = page;
                writeSphere++;

                for (int cellOffset = 0; cellOffset < page.CellCount; cellOffset++)
                {
                    int cellRecordIndex = page.FirstCellRecord + cellOffset;
                    CellRecord cell = cells[cellRecordIndex];
                    boundingSpheres[writeSphere] = new BoundingSphere(
                        cell.WorldBounds.center,
                        Mathf.Max(0.01f, cell.WorldBounds.extents.magnitude));
                    sphereRecords[writeSphere] = SphereRecord.Cell(cellRecordIndex);
                    cell.SphereIndex = writeSphere;
                    cells[cellRecordIndex] = cell;
                    writeSphere++;
                }
            }

            BuildPacketLookup();
            BuildNearDetailCellBytes();
            EnsureHotListCapacity();
            groupInstanceCounts = new int[groups.Count];
            groupFrustumMasks = new int[groups.Count];
            groupStartInstances = new int[groups.Count];
            groupWriteOffsets = new int[groups.Count];
            EnsureArgsUploadCapacity(groups.Count * IndirectArgsUIntCount);
            argsGroupCapacity = 0;
            ReleaseCullingGroup();
            graphDirty = false;
        }

        private void EnsureHotListCapacity()
        {
            if (cellCandidates.Capacity < cells.Count)
            {
                cellCandidates.Capacity = cells.Count;
            }

            if (activeGroupIndices.Capacity < groups.Count)
            {
                activeGroupIndices.Capacity = groups.Count;
            }

            if (selectedPackets.Capacity < packetLookupIndices.Length)
            {
                selectedPackets.Capacity = packetLookupIndices.Length;
            }
        }

        private void ClearCompiledGraph()
        {
            pages.Clear();
            cells.Clear();
            groups.Clear();
            boundingSpheres = Array.Empty<BoundingSphere>();
            sphereRecords = Array.Empty<SphereRecord>();
            visibleSphereIndices = Array.Empty<int>();
            visiblePageMask = Array.Empty<bool>();
            visibleCellMask = Array.Empty<bool>();
            visiblePageFrustumMasks = Array.Empty<int>();
            visibleCellFrustumMasks = Array.Empty<int>();
            nearDetailResidentCellMask = Array.Empty<bool>();
            nearDetailRequestedCellMask = Array.Empty<bool>();
            nearDetailCellBytes = Array.Empty<long>();
            nearDetailLastUsedCellFrame = Array.Empty<int>();
            pagePacketRanges = Array.Empty<PacketRange>();
            cellPacketRanges = Array.Empty<PacketRange>();
            packetLookupIndices = Array.Empty<int>();
            nearDetailResidentBytes = 0L;
            nearDetailResidentCellCount = 0;
            groupInstanceCounts = Array.Empty<int>();
            groupFrustumMasks = Array.Empty<int>();
            groupStartInstances = Array.Empty<int>();
            groupWriteOffsets = Array.Empty<int>();
            DisposeUploadArrays();
            selectedPackets.Clear();
            activeGroupIndices.Clear();
            cellCandidates.Clear();
            hasPreparedFrame = false;
            InvalidatePreparedCameraCache();
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

        private void EnsureCullingGroup(Camera camera)
        {
            if (cullingGroup == null)
            {
                cullingGroup = new CullingGroup();
                cullingGroup.SetBoundingSpheres(boundingSpheres);
                cullingGroup.SetBoundingSphereCount(boundingSpheres.Length);
            }

            if (cullingGroup.targetCamera != camera)
            {
                cullingGroup.targetCamera = camera;
            }
        }

        private void ClearVisibilityMasks()
        {
            if (visiblePageMask.Length > 0)
            {
                Array.Clear(visiblePageMask, 0, visiblePageMask.Length);
            }

            if (visibleCellMask.Length > 0)
            {
                Array.Clear(visibleCellMask, 0, visibleCellMask.Length);
            }

            if (visiblePageFrustumMasks.Length > 0)
            {
                Array.Clear(visiblePageFrustumMasks, 0, visiblePageFrustumMasks.Length);
            }

            if (visibleCellFrustumMasks.Length > 0)
            {
                Array.Clear(visibleCellFrustumMasks, 0, visibleCellFrustumMasks.Length);
            }

            preparedVisiblePageCount = 0;
            preparedVisibleCellCount = 0;
        }

        private void MarkVisibleSpheres(int visibleCount)
        {
            int count = Mathf.Min(visibleCount, visibleSphereIndices.Length);
            for (int i = 0; i < count; i++)
            {
                int sphereIndex = visibleSphereIndices[i];
                if (sphereIndex < 0 || sphereIndex >= sphereRecords.Length)
                {
                    continue;
                }

                SphereRecord record = sphereRecords[sphereIndex];
                if (record.Kind == SphereKind.Page)
                {
                    MarkPageVisible(record.RecordIndex);
                }
                else
                {
                    MarkCellVisible(record.RecordIndex);
                }
            }
        }

        private void MarkVisibleByFrustum(Plane[] frustumPlanes)
        {
            MarkVisibleByFrustums(frustumPlanes, 1);
        }

        private void MarkVisibleByFrustums(Plane[] frustumPlanes, int frustumCount)
        {
            int validatedFrustumCount = ResolveFrustumCount(frustumPlanes, frustumCount);
            if (validatedFrustumCount <= 0)
            {
                return;
            }

            int allFrustumMask = AllFrustumBits(validatedFrustumCount);
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                PageRecord page = pages[pageIndex];
                int pageFrustumMask = TestBoundsFrustumMask(
                    frustumPlanes,
                    allFrustumMask,
                    validatedFrustumCount,
                    page.Page.WorldBounds);
                if (pageFrustumMask == 0)
                {
                    continue;
                }

                visiblePageFrustumMasks[pageIndex] = pageFrustumMask;
                MarkPageVisible(pageIndex);
                for (int cellOffset = 0; cellOffset < page.CellCount; cellOffset++)
                {
                    int cellRecordIndex = page.FirstCellRecord + cellOffset;
                    int cellFrustumMask = TestBoundsFrustumMask(
                        frustumPlanes,
                        pageFrustumMask,
                        validatedFrustumCount,
                        cells[cellRecordIndex].WorldBounds);
                    if (cellFrustumMask != 0)
                    {
                        visibleCellFrustumMasks[cellRecordIndex] = cellFrustumMask;
                        MarkCellVisible(cellRecordIndex);
                    }
                }
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

        private static int TestBoundsFrustumMask(Plane[] frustumPlanes, int frustumMask, int frustumCount, Bounds bounds)
        {
            int visibleMask = 0;
            for (int frustumIndex = 0; frustumIndex < frustumCount; frustumIndex++)
            {
                int frustumBit = 1 << frustumIndex;
                if ((frustumMask & frustumBit) == 0)
                {
                    continue;
                }

                if (TestBoundsFrustum(frustumPlanes, frustumIndex * 6, bounds))
                {
                    visibleMask |= frustumBit;
                }
            }

            return visibleMask;
        }

        private static int AllFrustumBits(int frustumCount)
        {
            int clampedCount = Mathf.Clamp(frustumCount, 0, 30);
            return clampedCount == 0 ? 0 : (1 << clampedCount) - 1;
        }

        private static bool TestBoundsFrustum(Plane[] frustumPlanes, int planeOffset, Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int planeIndex = 0; planeIndex < 6; planeIndex++)
            {
                Plane plane = frustumPlanes[planeOffset + planeIndex];
                Vector3 normal = plane.normal;
                float radius =
                    extents.x * Mathf.Abs(normal.x) +
                    extents.y * Mathf.Abs(normal.y) +
                    extents.z * Mathf.Abs(normal.z);
                if (Vector3.Dot(normal, center) + plane.distance + radius < 0f)
                {
                    return false;
                }
            }

            return true;
        }

        private void MarkPageVisible(int pageRecordIndex)
        {
            if (pageRecordIndex < 0 || pageRecordIndex >= visiblePageMask.Length || visiblePageMask[pageRecordIndex])
            {
                return;
            }

            visiblePageMask[pageRecordIndex] = true;
            preparedVisiblePageCount++;
        }

        private void MarkCellVisible(int cellRecordIndex)
        {
            if (cellRecordIndex < 0 || cellRecordIndex >= visibleCellMask.Length || visibleCellMask[cellRecordIndex])
            {
                return;
            }

            visibleCellMask[cellRecordIndex] = true;
            preparedVisibleCellCount++;
            int pageRecordIndex = cells[cellRecordIndex].PageRecordIndex;
            MarkPageVisible(pageRecordIndex);
        }

        private void SelectPackets(
            Vector3 cameraWorldPosition,
            VegetationRenderPassMode passMode,
            VegetationFoliageFeatureSettings settings,
            Plane[]? explicitFrustum,
            int explicitFrustumCount)
        {
            selectedPackets.Clear();
            ClearActiveGroupSelection();
            cellCandidates.Clear();
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
            InvalidatePreparedCameraCache();
            int remainingWorkBudget = settings.GetWorkBudget(passMode);
            int remainingInstanceBudget = Mathf.Max(1, settings.MaxVisiblePacketInstances);
            long residentByteBudget = settings.GetNearDetailResidentByteBudget();
            long remainingUploadByteBudget = settings.GetNearDetailUploadByteBudget();
            float nearDistance = Mathf.Max(1f, settings.NearDetailDistance);
            float nearDistanceSqr = nearDistance * nearDistance;
            BeginResidencyPrepare(residentByteBudget);

            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                if (!visiblePageMask[pageIndex])
                {
                    continue;
                }

                PageRecord page = pages[pageIndex];
                bool hasVisibleCell = false;
                for (int cellOffset = 0; cellOffset < page.CellCount; cellOffset++)
                {
                    int cellRecordIndex = page.FirstCellRecord + cellOffset;
                    if (!visibleCellMask[cellRecordIndex])
                    {
                        continue;
                    }

                    hasVisibleCell = true;
                    CellRecord cell = cells[cellRecordIndex];
                    float distanceSqr = CalculateCellDistanceSqr(cell, cameraWorldPosition);
                    cellCandidates.Add(new CellCandidate(cellRecordIndex, distanceSqr));
                }

                if (!hasVisibleCell)
                {
                    TrySelectHlodPackets(pageIndex, FoliageRepresentationKind.PageHLOD, -1, passMode, explicitFrustum, explicitFrustumCount, ref remainingWorkBudget, ref remainingInstanceBudget);
                }
            }

            cellCandidates.Sort();
            for (int i = 0; i < cellCandidates.Count; i++)
            {
                CellCandidate candidate = cellCandidates[i];
                CellRecord cell = cells[candidate.CellRecordIndex];
                PageRecord page = pages[cell.PageRecordIndex];
                if (candidate.DistanceSqr <= nearDistanceSqr)
                {
                    if (TryEnsureNearDetailCellResident(candidate.CellRecordIndex, residentByteBudget, ref remainingUploadByteBudget))
                    {
                        FoliageRepresentationKind desiredTier = ResolveTier(candidate.DistanceSqr, nearDistanceSqr);
                        if (TrySelectNearDetailTierCascade(page, candidate.CellRecordIndex, desiredTier, passMode, explicitFrustum, explicitFrustumCount, ref remainingWorkBudget, ref remainingInstanceBudget))
                        {
                            continue;
                        }
                    }
                }

                TrySelectHlodPackets(page.PageRecordIndex, FoliageRepresentationKind.CellHLOD, candidate.CellRecordIndex, passMode, explicitFrustum, explicitFrustumCount, ref remainingWorkBudget, ref remainingInstanceBudget);
            }
        }

        private void BeginResidencyPrepare(long residentByteBudget)
        {
            if (residencyFrameIndex == int.MaxValue)
            {
                Array.Clear(nearDetailLastUsedCellFrame, 0, nearDetailLastUsedCellFrame.Length);
                residencyFrameIndex = 1;
            }
            else
            {
                residencyFrameIndex++;
            }

            if (nearDetailRequestedCellMask.Length > 0)
            {
                Array.Clear(nearDetailRequestedCellMask, 0, nearDetailRequestedCellMask.Length);
            }

            EvictNearDetailCellsToBudget(residentByteBudget, 0L);
        }

        private bool TryEnsureNearDetailCellResident(
            int cellRecordIndex,
            long residentByteBudget,
            ref long remainingUploadByteBudget)
        {
            if (cellRecordIndex < 0 || cellRecordIndex >= nearDetailCellBytes.Length)
            {
                return false;
            }

            long cellBytes = nearDetailCellBytes[cellRecordIndex];
            if (cellBytes <= 0L)
            {
                return true;
            }

            if (!nearDetailRequestedCellMask[cellRecordIndex])
            {
                nearDetailRequestedCellMask[cellRecordIndex] = true;
                preparedNearDetailLoadRequestCount++;
            }

            if (cellBytes > residentByteBudget)
            {
                UnloadNearDetailCell(cellRecordIndex, countEviction: nearDetailResidentCellMask[cellRecordIndex]);
                return false;
            }

            if (nearDetailResidentCellMask[cellRecordIndex])
            {
                nearDetailLastUsedCellFrame[cellRecordIndex] = residencyFrameIndex;
                return true;
            }

            if (cellBytes > remainingUploadByteBudget)
            {
                return false;
            }

            EvictNearDetailCellsToBudget(residentByteBudget, cellBytes);
            if (nearDetailResidentBytes + cellBytes > residentByteBudget)
            {
                return false;
            }

            nearDetailResidentCellMask[cellRecordIndex] = true;
            nearDetailLastUsedCellFrame[cellRecordIndex] = residencyFrameIndex;
            nearDetailResidentBytes += cellBytes;
            nearDetailResidentCellCount++;
            remainingUploadByteBudget -= cellBytes;
            preparedNearDetailLoadedBytes += cellBytes;
            return true;
        }

        private void EvictNearDetailCellsToBudget(long residentByteBudget, long incomingBytes)
        {
            while (nearDetailResidentBytes + incomingBytes > residentByteBudget)
            {
                int candidateCellIndex = -1;
                int oldestFrame = int.MaxValue;
                for (int cellIndex = 0; cellIndex < nearDetailResidentCellMask.Length; cellIndex++)
                {
                    if (!nearDetailResidentCellMask[cellIndex] || nearDetailRequestedCellMask[cellIndex])
                    {
                        continue;
                    }

                    int lastUsedFrame = nearDetailLastUsedCellFrame[cellIndex];
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
            if (cellRecordIndex < 0 ||
                cellRecordIndex >= nearDetailResidentCellMask.Length ||
                !nearDetailResidentCellMask[cellRecordIndex])
            {
                return;
            }

            nearDetailResidentCellMask[cellRecordIndex] = false;
            nearDetailLastUsedCellFrame[cellRecordIndex] = 0;
            nearDetailResidentBytes = Math.Max(0L, nearDetailResidentBytes - nearDetailCellBytes[cellRecordIndex]);
            nearDetailResidentCellCount = Mathf.Max(0, nearDetailResidentCellCount - 1);
            if (countEviction)
            {
                preparedNearDetailEvictedCellCount++;
            }
        }

        private static FoliageRepresentationKind ResolveTier(float distanceSqr, float nearDistanceSqr)
        {
            float nearThird = nearDistanceSqr * 0.11111111f;
            if (distanceSqr <= nearThird)
            {
                return FoliageRepresentationKind.TreeL0;
            }

            if (distanceSqr <= nearDistanceSqr * 0.44444444f)
            {
                return FoliageRepresentationKind.TreeL1;
            }

            return FoliageRepresentationKind.TreeL2;
        }

        private static float CalculateCellDistanceSqr(CellRecord cell, Vector3 cameraWorldPosition)
        {
            return cell.WorldBounds.SqrDistance(cameraWorldPosition);
        }

        private bool TrySelectNearDetailTierCascade(
            PageRecord page,
            int cellRecordIndex,
            FoliageRepresentationKind desiredTier,
            VegetationRenderPassMode passMode,
            Plane[]? explicitFrustum,
            int explicitFrustumCount,
            ref int remainingWorkBudget,
            ref int remainingInstanceBudget)
        {
            // Range: one visible near cell. Condition: try the best distance tier first, then cheaper tiers before HLOD. Output: selects the first affordable near-detail tier.
            if (desiredTier == FoliageRepresentationKind.TreeL0 &&
                TrySelectTierPackets(page, cellRecordIndex, FoliageRepresentationKind.TreeL0, passMode, explicitFrustum, explicitFrustumCount, ref remainingWorkBudget, ref remainingInstanceBudget))
            {
                return true;
            }

            if ((desiredTier == FoliageRepresentationKind.TreeL0 ||
                 desiredTier == FoliageRepresentationKind.TreeL1) &&
                TrySelectTierPackets(page, cellRecordIndex, FoliageRepresentationKind.TreeL1, passMode, explicitFrustum, explicitFrustumCount, ref remainingWorkBudget, ref remainingInstanceBudget))
            {
                return true;
            }

            return TrySelectTierPackets(page, cellRecordIndex, FoliageRepresentationKind.TreeL2, passMode, explicitFrustum, explicitFrustumCount, ref remainingWorkBudget, ref remainingInstanceBudget);
        }

        private bool TrySelectTierPackets(
            PageRecord page,
            int cellRecordIndex,
            FoliageRepresentationKind representationKind,
            VegetationRenderPassMode passMode,
            Plane[]? explicitFrustum,
            int explicitFrustumCount,
            ref int remainingWorkBudget,
            ref int remainingInstanceBudget)
        {
            return TrySelectPackets(
                page.PageRecordIndex,
                representationKind,
                cellRecordIndex,
                passMode,
                explicitFrustum,
                explicitFrustumCount,
                ref remainingWorkBudget,
                ref remainingInstanceBudget);
        }

        private bool TrySelectHlodPackets(
            int pageRecordIndex,
            FoliageRepresentationKind representationKind,
            int cellRecordIndex,
            VegetationRenderPassMode passMode,
            Plane[]? explicitFrustum,
            int explicitFrustumCount,
            ref int remainingWorkBudget,
            ref int remainingInstanceBudget)
        {
            return TrySelectPackets(
                pageRecordIndex,
                representationKind,
                cellRecordIndex,
                passMode,
                explicitFrustum,
                explicitFrustumCount,
                ref remainingWorkBudget,
                ref remainingInstanceBudget);
        }

        private bool TrySelectPackets(
            int pageRecordIndex,
            FoliageRepresentationKind representationKind,
            int cellRecordIndex,
            VegetationRenderPassMode passMode,
            Plane[]? explicitFrustum,
            int explicitFrustumCount,
            ref int remainingWorkBudget,
            ref int remainingInstanceBudget)
        {
            if (pageRecordIndex < 0 || pageRecordIndex >= pages.Count)
            {
                return false;
            }

            PageRecord pageRecord = pages[pageRecordIndex];
            if (IsNearDetailRepresentation(representationKind) && !IsNearDetailCellResident(cellRecordIndex))
            {
                return false;
            }

            PacketRange packetRange = GetPacketRange(pageRecordIndex, representationKind, cellRecordIndex);
            if (packetRange.Count == 0)
            {
                return false;
            }

            int visibilityFrustumMask = ResolveVisibilityFrustumMask(pageRecordIndex, cellRecordIndex, explicitFrustumCount);
            bool chargeBudget = IsNearDetailRepresentation(representationKind);
            int packetCost = 0;
            int packetInstances = 0;
            int matchingPacketCount = 0;
            for (int rangeOffset = 0; rangeOffset < packetRange.Count; rangeOffset++)
            {
                int packetIndex = packetLookupIndices[packetRange.Start + rangeOffset];
                if (!TryResolveSelectablePacket(pageRecord.Page, packetIndex, passMode, explicitFrustum, explicitFrustumCount, visibilityFrustumMask, out FoliageRepresentationPacket packet, out int resolvedPacketIndex, out int packetFrustumMask))
                {
                    continue;
                }

                _ = resolvedPacketIndex;
                _ = packetFrustumMask;
                packetCost += packet.WorkCost;
                packetInstances += packet.InstanceCount;
                matchingPacketCount++;
            }

            if (matchingPacketCount == 0 ||
                (chargeBudget && (packetCost > remainingWorkBudget || packetInstances > remainingInstanceBudget)))
            {
                return false;
            }

            for (int rangeOffset = 0; rangeOffset < packetRange.Count; rangeOffset++)
            {
                int packetIndex = packetLookupIndices[packetRange.Start + rangeOffset];
                if (!TryResolveSelectablePacket(pageRecord.Page, packetIndex, passMode, explicitFrustum, explicitFrustumCount, visibilityFrustumMask, out FoliageRepresentationPacket packet, out int resolvedPacketIndex, out int packetFrustumMask))
                {
                    continue;
                }

                selectedPackets.Add(new PacketSelection(pageRecordIndex, resolvedPacketIndex));
                int worldGroupIndex = ResolveWorldGroupIndex(pageRecord.ProviderIndex, packet.AssetGroupIndex);
                if (worldGroupIndex >= 0 && worldGroupIndex < groupInstanceCounts.Length)
                {
                    if (groupInstanceCounts[worldGroupIndex] == 0)
                    {
                        activeGroupIndices.Add(worldGroupIndex);
                    }

                    groupInstanceCounts[worldGroupIndex] += packet.InstanceCount;
                    if (packetFrustumMask != 0 && worldGroupIndex < groupFrustumMasks.Length)
                    {
                        groupFrustumMasks[worldGroupIndex] |= packetFrustumMask;
                        preparedFrustumMask |= packetFrustumMask;
                    }
                }

                if (packet.Residency == FoliagePacketResidency.NearDetail)
                {
                    nearDetailLastUsedCellFrame[cellRecordIndex] = residencyFrameIndex;
                    preparedNearDetailPacketCount++;
                    IncrementNearDetailTierCounter(packet.RepresentationKind);
                }
                else
                {
                    preparedHlodPacketCount++;
                }

                if (passMode == VegetationRenderPassMode.Shadow)
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

        private int ResolveVisibilityFrustumMask(int pageRecordIndex, int cellRecordIndex, int explicitFrustumCount)
        {
            if (explicitFrustumCount <= 0)
            {
                return 0;
            }

            int validFrustumMask = AllFrustumBits(explicitFrustumCount);
            if (cellRecordIndex >= 0 && cellRecordIndex < visibleCellFrustumMasks.Length)
            {
                return visibleCellFrustumMasks[cellRecordIndex] & validFrustumMask;
            }

            if (pageRecordIndex >= 0 && pageRecordIndex < visiblePageFrustumMasks.Length)
            {
                return visiblePageFrustumMasks[pageRecordIndex] & validFrustumMask;
            }

            return validFrustumMask;
        }

        private void IncrementNearDetailTierCounter(FoliageRepresentationKind representationKind)
        {
            if (representationKind == FoliageRepresentationKind.TreeL0)
            {
                preparedTreeL0PacketCount++;
                return;
            }

            if (representationKind == FoliageRepresentationKind.TreeL1)
            {
                preparedTreeL1PacketCount++;
                return;
            }

            if (representationKind == FoliageRepresentationKind.TreeL2)
            {
                preparedTreeL2PacketCount++;
            }
        }

        private bool IsNearDetailCellResident(int cellRecordIndex)
        {
            return cellRecordIndex >= 0 &&
                   cellRecordIndex < nearDetailResidentCellMask.Length &&
                   nearDetailResidentCellMask[cellRecordIndex];
        }

        private static bool IsNearDetailRepresentation(FoliageRepresentationKind representationKind)
        {
            return representationKind == FoliageRepresentationKind.TreeL0 ||
                   representationKind == FoliageRepresentationKind.TreeL1 ||
                   representationKind == FoliageRepresentationKind.TreeL2;
        }

        private bool TryResolveSelectablePacket(
            FoliagePageAsset page,
            int packetIndex,
            VegetationRenderPassMode passMode,
            Plane[]? explicitFrustum,
            int explicitFrustumCount,
            int visibilityFrustumMask,
            out FoliageRepresentationPacket packet,
            out int resolvedPacketIndex,
            out int packetFrustumMask)
        {
            packet = page.Packets[packetIndex];
            resolvedPacketIndex = packetIndex;
            packetFrustumMask = 0;
            if (explicitFrustum != null && visibilityFrustumMask == 0)
            {
                return false;
            }

            if (passMode != VegetationRenderPassMode.Shadow)
            {
                if (explicitFrustum == null)
                {
                    return true;
                }

                packetFrustumMask = TestBoundsFrustumMask(
                    explicitFrustum,
                    visibilityFrustumMask,
                    explicitFrustumCount,
                    packet.WorldBounds);
                return packetFrustumMask != 0;
            }

            if (packet.ShadowMode == FoliageShadowPacketMode.None)
            {
                return false;
            }

            int shadowPacketIndex = packet.ShadowPacketIndex;
            if (shadowPacketIndex < 0 || shadowPacketIndex >= page.Packets.Count)
            {
                return false;
            }

            packet = page.Packets[shadowPacketIndex];
            resolvedPacketIndex = shadowPacketIndex;
            if (packet.ShadowMode == FoliageShadowPacketMode.None)
            {
                return false;
            }

            if (explicitFrustum == null)
            {
                return true;
            }

            packetFrustumMask = TestBoundsFrustumMask(
                explicitFrustum,
                visibilityFrustumMask,
                explicitFrustumCount,
                packet.WorldBounds);
            return packetFrustumMask != 0;
        }

        private int ResolveWorldGroupIndex(int providerIndex, int localGroupIndex)
        {
            if (providerIndex < 0 || providerIndex >= providers.Count || localGroupIndex < 0)
            {
                return -1;
            }

            return providers[providerIndex].AssetGroupOffset + localGroupIndex;
        }

        private bool UploadPreparedFrame(VegetationFoliageFeatureSettings settings, VegetationRenderPassMode passMode)
        {
            preparedInstanceCount = 0;
            int minActiveGroupIndex = int.MaxValue;
            int maxActiveGroupIndex = -1;
            using (PrepareUploadLayoutMarker.Auto())
            {
                for (int i = 0; i < activeGroupIndices.Count; i++)
                {
                    int groupIndex = activeGroupIndices[i];
                    if (groupIndex < 0 || groupIndex >= groupInstanceCounts.Length)
                    {
                        continue;
                    }

                    int groupInstanceCount = groupInstanceCounts[groupIndex];
                    if (groupInstanceCount <= 0)
                    {
                        continue;
                    }

                    groupStartInstances[groupIndex] = preparedInstanceCount;
                    groupWriteOffsets[groupIndex] = 0;
                    preparedInstanceCount += groupInstanceCount;
                    preparedActiveGroupCount++;
                    minActiveGroupIndex = Mathf.Min(minActiveGroupIndex, groupIndex);
                    maxActiveGroupIndex = Mathf.Max(maxActiveGroupIndex, groupIndex);
                }

                if (preparedInstanceCount <= 0 || minActiveGroupIndex == int.MaxValue || maxActiveGroupIndex < minActiveGroupIndex)
                {
                    ClearPreparedFrame();
                    return false;
                }

                EnsureGpuBuffers(preparedInstanceCount, groups.Count);
                EnsureInstanceUploadCapacity(preparedInstanceCount);
                EnsureArgsUploadCapacity(groups.Count * IndirectArgsUIntCount);
            }

            using (PrepareUploadCopyInstancesMarker.Auto())
            {
                for (int selectionIndex = 0; selectionIndex < selectedPackets.Count; selectionIndex++)
                {
                    PacketSelection selection = selectedPackets[selectionIndex];
                    PageRecord pageRecord = pages[selection.PageRecordIndex];
                    FoliageRepresentationPacket packet = pageRecord.Page.Packets[selection.PacketIndex];
                    int worldGroupIndex = ResolveWorldGroupIndex(pageRecord.ProviderIndex, packet.AssetGroupIndex);
                    if (worldGroupIndex < 0 || worldGroupIndex >= groupWriteOffsets.Length)
                    {
                        continue;
                    }

                    int groupStart = groupStartInstances[worldGroupIndex];
                    int groupOffset = groupWriteOffsets[worldGroupIndex];
                    for (int instanceOffset = 0; instanceOffset < packet.InstanceCount; instanceOffset++)
                    {
                        int sourceInstanceIndex = packet.FirstInstance + instanceOffset;
                        if (sourceInstanceIndex < 0 || sourceInstanceIndex >= pageRecord.Page.Instances.Count)
                        {
                            continue;
                        }

                        int writeIndex = groupStart + groupOffset;
                        instanceData[writeIndex] = ConvertInstance(pageRecord.Page.Instances[sourceInstanceIndex]);
                        groupOffset++;
                    }

                    groupWriteOffsets[worldGroupIndex] = groupOffset;
                }
            }

            int argsStart = minActiveGroupIndex * IndirectArgsUIntCount;
            int argsCount = (maxActiveGroupIndex - minActiveGroupIndex + 1) * IndirectArgsUIntCount;
            using (PrepareUploadArgsMarker.Auto())
            {
                for (int groupIndex = minActiveGroupIndex; groupIndex <= maxActiveGroupIndex; groupIndex++)
                {
                    WriteArgs(argsData, groupIndex * IndirectArgsUIntCount, groupIndex, 0);
                }

                for (int i = 0; i < activeGroupIndices.Count; i++)
                {
                    int groupIndex = activeGroupIndices[i];
                    if (groupIndex < minActiveGroupIndex || groupIndex > maxActiveGroupIndex)
                    {
                        continue;
                    }

                    WriteArgs(
                        argsData,
                        groupIndex * IndirectArgsUIntCount,
                        groupIndex,
                        groupInstanceCounts[groupIndex]);
                }
            }

            using (PrepareUploadSetDataMarker.Auto())
            {
                instanceBuffer!.SetData(instanceData, 0, 0, preparedInstanceCount);
                argsBuffer!.SetData(argsData, argsStart, argsStart, argsCount);
            }

            preparedWindStrength = Mathf.Max(0f, settings.WindStrength);
            preparedWindFrequency = Mathf.Max(0f, settings.WindFrequency);
            Vector3 windDirection = settings.WindDirection.sqrMagnitude > 0.0001f
                ? settings.WindDirection.normalized
                : Vector3.right;
            preparedWindDirection = new Vector4(windDirection.x, windDirection.y, windDirection.z, 0f);
            preparedLeafFlutterSettings = new Vector4(
                Mathf.Max(0f, settings.LeafFlutterStrength),
                Mathf.Max(0f, settings.LeafFlutterFrequencyMultiplier),
                Mathf.Max(0f, settings.LeafFlutterSpatialScale),
                Mathf.Max(0f, settings.LeafFlutterSecondaryStrength));
            hasPreparedFrame = true;
            return true;
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

        private void WriteArgs(NativeArray<uint> targetArgs, int baseOffset, int groupIndex, int instanceCount)
        {
            GroupRecord group = groups[groupIndex];
            targetArgs[baseOffset] = group.IndexCount;
            targetArgs[baseOffset + 1] = (uint)Mathf.Max(0, instanceCount);
            targetArgs[baseOffset + 2] = group.IndexStart;
            targetArgs[baseOffset + 3] = group.BaseVertex;
            // Keep backend instance IDs local to the draw; the shader applies the grouped instance-buffer offset explicitly.
            targetArgs[baseOffset + 4] = 0u;
        }

        private void EnsureInstanceUploadCapacity(int requiredCount)
        {
            if (instanceData.IsCreated && instanceData.Length >= requiredCount)
            {
                return;
            }

            if (instanceData.IsCreated)
            {
                instanceData.Dispose();
            }

            instanceData = new NativeArray<VegetationIndirectInstanceData>(
                Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCount)),
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        private void EnsureArgsUploadCapacity(int requiredCount)
        {
            if (argsData.IsCreated && argsData.Length >= requiredCount)
            {
                return;
            }

            if (argsData.IsCreated)
            {
                argsData.Dispose();
            }

            argsData = new NativeArray<uint>(
                Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCount)),
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
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
            selectedPackets.Clear();
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
            InvalidatePreparedCameraCache();
        }

        private void ClearActiveGroupSelection()
        {
            for (int i = 0; i < activeGroupIndices.Count; i++)
            {
                int groupIndex = activeGroupIndices[i];
                if (groupIndex >= 0 && groupIndex < groupInstanceCounts.Length)
                {
                    groupInstanceCounts[groupIndex] = 0;
                }

                if (groupIndex >= 0 && groupIndex < groupFrustumMasks.Length)
                {
                    groupFrustumMasks[groupIndex] = 0;
                }
            }

            activeGroupIndices.Clear();
        }

        private void DisposeUploadArrays()
        {
            if (instanceData.IsCreated)
            {
                instanceData.Dispose();
            }

            if (argsData.IsCreated)
            {
                argsData.Dispose();
            }
        }

        private void ReleaseGpuBuffers()
        {
            ReleaseGraphicsBuffer(ref instanceBuffer);
            ReleaseGraphicsBuffer(ref argsBuffer);
            instanceCapacity = 0;
            argsGroupCapacity = 0;
        }

        private void ReleaseCullingGroup()
        {
            cullingGroup?.Dispose();
            cullingGroup = null;
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
            unchecked
            {
                graphVersion++;
            }

            InvalidatePreparedCameraCache();
        }

        private void InvalidatePreparedCameraCache()
        {
            cachedCameraFrame = -1;
            cachedCameraId = -1;
            cachedCameraSettingsHash = 0;
            lastPrepareUsedCameraCache = false;
        }

        private int ComputeCameraPrepareSettingsHash(VegetationFoliageFeatureSettings settings)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + graphVersion;
                hash = hash * 31 + settings.NearDetailDistance.GetHashCode();
                hash = hash * 31 + settings.ColorWorkBudget;
                hash = hash * 31 + settings.MaxVisiblePacketInstances;
                hash = hash * 31 + settings.NearDetailResidentByteBudget;
                hash = hash * 31 + settings.NearDetailUploadByteBudget;
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
                int pageSphereIndex,
                int firstCellRecord,
                int cellCount)
            {
                ProviderIndex = providerIndex;
                ProviderPageIndex = providerPageIndex;
                Page = page;
                PageSphereIndex = pageSphereIndex;
                FirstCellRecord = firstCellRecord;
                CellCount = cellCount;
                PageRecordIndex = pageRecordIndex;
            }

            public int ProviderIndex;
            public int ProviderPageIndex;
            public FoliagePageAsset Page;
            public int PageSphereIndex;
            public int FirstCellRecord;
            public int CellCount;
            public int PageRecordIndex;
        }

        private struct CellRecord
        {
            public CellRecord(int pageRecordIndex, int cellIndex, int sphereIndex, Bounds worldBounds)
            {
                PageRecordIndex = pageRecordIndex;
                CellIndex = cellIndex;
                SphereIndex = sphereIndex;
                WorldBounds = worldBounds;
            }

            public int PageRecordIndex;
            public int CellIndex;
            public int SphereIndex;
            public Bounds WorldBounds;
        }

        private struct GroupRecord
        {
            public GroupRecord(FoliageAssetGroup assetGroup, int groupIndex)
            {
                AssetGroup = assetGroup;
                ArgsBufferOffset = checked(groupIndex * GraphicsBuffer.IndirectDrawIndexedArgs.size);
                Mesh mesh = assetGroup.Mesh;
                IndexCount = (uint)mesh.GetIndexCount(0);
                IndexStart = (uint)mesh.GetIndexStart(0);
                BaseVertex = unchecked((uint)mesh.GetBaseVertex(0));
            }

            public FoliageAssetGroup AssetGroup;
            public int ArgsBufferOffset;
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

        private readonly struct SphereRecord
        {
            private SphereRecord(SphereKind kind, int recordIndex)
            {
                Kind = kind;
                RecordIndex = recordIndex;
            }

            public SphereKind Kind { get; }

            public int RecordIndex { get; }

            public static SphereRecord Page(int pageRecordIndex)
            {
                return new SphereRecord(SphereKind.Page, pageRecordIndex);
            }

            public static SphereRecord Cell(int cellRecordIndex)
            {
                return new SphereRecord(SphereKind.Cell, cellRecordIndex);
            }
        }

        private readonly struct CellCandidate : IComparable<CellCandidate>
        {
            public CellCandidate(int cellRecordIndex, float distanceSqr)
            {
                CellRecordIndex = cellRecordIndex;
                DistanceSqr = distanceSqr;
            }

            public int CellRecordIndex { get; }

            public float DistanceSqr { get; }

            public int CompareTo(CellCandidate other)
            {
                return DistanceSqr.CompareTo(other.DistanceSqr);
            }
        }

        private readonly struct PacketSelection
        {
            public PacketSelection(int pageRecordIndex, int packetIndex)
            {
                PageRecordIndex = pageRecordIndex;
                PacketIndex = packetIndex;
            }

            public int PageRecordIndex { get; }

            public int PacketIndex { get; }
        }

        private struct PacketRange
        {
            public int Start;
            public int Count;
        }

        private enum SphereKind
        {
            Page = 0,
            Cell = 1
        }
    }
}
