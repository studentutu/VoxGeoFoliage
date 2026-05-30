#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
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
        private const int BrgSizeOfPackedMatrix = sizeof(float) * 4 * 3;
        private const int BrgSizeOfUint = sizeof(uint);
        private const int BrgSizeOfFloat4 = sizeof(float) * 4;
        private const int BrgExtraBytes = BrgSizeOfPackedMatrix * 2;
        private const uint BrgPerInstanceMetadataFlag = 0x80000000u;
        // Keep custom vegetation BRG output off SRP Core's reserved GPU Resident Drawer layers.
        private const byte BrgVegetationBatchLayer = 0;
        private const int BatchRendererRetireFrameDelay = 4;
        private const int BatchRendererRetireSlotCount = 8;
        private const int BatchRendererCullingDiagnosticLogLimit = 4;
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
        private static readonly BatchCullingViewType[] BatchRendererCameraViewTypes =
        {
            BatchCullingViewType.Camera
        };
        private static readonly BatchCullingViewType[] BatchRendererCameraAndLightViewTypes =
        {
            BatchCullingViewType.Camera,
            BatchCullingViewType.Light
        };
        private static readonly int InstanceBufferId = Shader.PropertyToID("_VegetationInstanceData");
        private static readonly int InstanceBufferBaseOffsetId = Shader.PropertyToID("_VegetationInstanceDataBaseOffset");
        private static readonly int UnityObjectToWorldId = Shader.PropertyToID("unity_ObjectToWorld");
        private static readonly int UnityWorldToObjectId = Shader.PropertyToID("unity_WorldToObject");
        private static readonly int BrgPackedLeafTintId = Shader.PropertyToID("_VegetationPackedLeafTint");
        private static readonly int BrgWindId = Shader.PropertyToID("_VegetationWind");
        private static readonly int WindStrengthId = Shader.PropertyToID("_VegetationWindStrength");
        private static readonly int WindFrequencyId = Shader.PropertyToID("_VegetationWindFrequency");
        private static readonly int WindDirectionId = Shader.PropertyToID("_VegetationWindDirection");
        private static readonly int LeafFlutterSettingsId = Shader.PropertyToID("_VegetationLeafFlutterSettings");
        [ThreadStatic] private static BatchCullingScratch? threadBatchCullingScratch;

        private readonly List<ProviderRecord> providers = new List<ProviderRecord>();
        private readonly List<PageRecord> pages = new List<PageRecord>();
        private readonly List<CellRecord> cells = new List<CellRecord>();
        private readonly List<GroupRecord> groups = new List<GroupRecord>();
        private readonly Dictionary<string, int> providerIndicesById = new Dictionary<string, int>(StringComparer.Ordinal);
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
        private int[] groupTotalInstanceCounts = Array.Empty<int>();
        private int[][] pageInstanceGroupLocalIndices = Array.Empty<int[]>();
        private int[][] pagePacketValidInstanceCounts = Array.Empty<int[]>();
        private CellCandidate[] cellCandidates = Array.Empty<CellCandidate>();
        private PacketSelection[] selectedPackets = Array.Empty<PacketSelection>();
        private int[] activeGroupIndices = Array.Empty<int>();
        private int[] batchRendererAllowedCameraViewIds = Array.Empty<int>();
        private VegetationBrgBatch[] brgBatches = Array.Empty<VegetationBrgBatch>();
        private readonly BatchRendererState?[] retiredBatchRendererStates = new BatchRendererState?[BatchRendererRetireSlotCount];
        private NativeArray<uint> argsData;
        private NativeArray<VegetationIndirectInstanceData> instanceData;
        private GraphicsBuffer? instanceBuffer;
        private GraphicsBuffer? argsBuffer;
        private BatchRendererGroup? batchRendererGroup;
        private BatchRendererState? batchRendererState;
        private VegetationFoliageFeatureSettings? batchRendererSettings;
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
        private int cellCandidateCount;
        private int selectedPacketCount;
        private int activeGroupIndexCount;
        private int batchRendererAllowedCameraViewIdCount;
        private int batchRendererAllowedCameraViewFrame = -1;
        private bool lastPrepareUsedCameraCache;
        private bool batchRendererFaulted;
        private bool batchRendererUnsupportedLogged;
        private bool invalidCompiledPacketsLogged;
        private int invalidCompiledPacketCount;
        private int retiredBatchRendererStateCursor;
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
        /// [INTEGRATION] Ensures the compiled provider graph is exposed through Unity BatchRendererGroup batches for the active URP renderer.
        /// </summary>
        public bool RefreshBatchRenderer(VegetationFoliageFeatureSettings settings, int cameraViewId)
        {
            // Range: one renderer-feature settings surface. Condition: BRG-capable graphics API and compiled providers exist. Output: BRG batches own renderer submission; unsupported APIs use the RenderGraph grouped-indirect backend.
            FlushRetiredBatchRendererResources(force: false);
            if (settings == null || providers.Count == 0)
            {
                return false;
            }

            if (batchRendererFaulted)
            {
                ReleaseBatchRendererResources();
                return false;
            }

            if (!CanUseBatchRendererGraphics())
            {
                ReleaseBatchRendererResources();
                return false;
            }

            if (BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer)
            {
                if (!batchRendererUnsupportedLogged)
                {
                    batchRendererUnsupportedLogged = true;
                    Debug.LogError(
                        $"Vegetation BatchRendererGroup disabled: graphics API requires '{BatchRendererGroup.BufferTarget}' buffers, but the vegetation BRG backend currently supports RawBuffer only.");
                }

                return false;
            }

            RegisterAllowedBatchRendererCameraView(cameraViewId);
            batchRendererSettings = settings;
            ApplyGlobalShaderSettings(settings);
            try
            {
                EnsureCompiledGraph();
                if (groups.Count == 0 || pages.Count == 0)
                {
                    return false;
                }

                EnsureBatchRendererResources();
                return batchRendererGroup != null && brgBatches.Length > 0;
            }
            catch (Exception exception)
            {
                batchRendererFaulted = true;
                ReleaseBatchRendererResources();
                Debug.LogError(
                    $"Vegetation BatchRendererGroup disabled reason={exception.GetType().Name}: {exception.Message}");
                Debug.LogException(exception);
                return false;
            }
        }

        private static bool CanUseBatchRendererGraphics()
        {
            return Application.isPlaying &&
                   !Application.isBatchMode &&
                   SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null &&
                   SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12;
        }

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
                ReleaseBatchRendererResources();
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
            ReleaseBatchRendererResources();
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

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Reset();
            FlushRetiredBatchRendererResources(force: true);
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

                // Unity 6.3/D3D12 validates this SRV on indirect draws before per-draw property-block state is always visible.
                // The shader declares it only for procedural indirect variants, so this does not affect BRG/DOTS or preview draws.
                drawWrapper.SetGlobalBuffer(InstanceBufferId, instanceBuffer);
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
            BuildBatchInstanceLookup();
            BuildPacketInstanceValidation();
            LogInvalidCompiledPacketsOnce();
            EnsureHotPathCapacity();
            groupInstanceCounts = new int[groups.Count];
            groupFrustumMasks = new int[groups.Count];
            groupStartInstances = new int[groups.Count];
            groupWriteOffsets = new int[groups.Count];
            EnsureArgsUploadCapacity(groups.Count * IndirectArgsUIntCount);
            argsGroupCapacity = 0;
            ReleaseCullingGroup();
            graphDirty = false;
        }

        private void EnsureHotPathCapacity()
        {
            if (cellCandidates.Length < cells.Count)
            {
                cellCandidates = new CellCandidate[Mathf.NextPowerOfTwo(Mathf.Max(1, cells.Count))];
            }

            if (activeGroupIndices.Length < groups.Count)
            {
                activeGroupIndices = new int[Mathf.NextPowerOfTwo(Mathf.Max(1, groups.Count))];
            }

            if (selectedPackets.Length < packetLookupIndices.Length)
            {
                selectedPackets = new PacketSelection[Mathf.NextPowerOfTwo(Mathf.Max(1, packetLookupIndices.Length))];
            }
        }

        private void AddCellCandidate(CellCandidate candidate)
        {
            if (cellCandidateCount >= cellCandidates.Length)
            {
                Array.Resize(ref cellCandidates, Mathf.NextPowerOfTwo(Mathf.Max(1, cellCandidateCount + 1)));
            }

            cellCandidates[cellCandidateCount++] = candidate;
        }

        private void AddSelectedPacket(PacketSelection selection)
        {
            if (selectedPacketCount >= selectedPackets.Length)
            {
                Array.Resize(ref selectedPackets, Mathf.NextPowerOfTwo(Mathf.Max(1, selectedPacketCount + 1)));
            }

            selectedPackets[selectedPacketCount++] = selection;
        }

        private void AddActiveGroupIndex(int groupIndex)
        {
            if (activeGroupIndexCount >= activeGroupIndices.Length)
            {
                Array.Resize(ref activeGroupIndices, Mathf.NextPowerOfTwo(Mathf.Max(1, activeGroupIndexCount + 1)));
            }

            activeGroupIndices[activeGroupIndexCount++] = groupIndex;
        }

        private void RegisterAllowedBatchRendererCameraView(int cameraViewId)
        {
            int frame = Time.renderedFrameCount;
            if (batchRendererAllowedCameraViewFrame != frame)
            {
                batchRendererAllowedCameraViewFrame = frame;
                batchRendererAllowedCameraViewIdCount = 0;
            }

            for (int i = 0; i < batchRendererAllowedCameraViewIdCount; i++)
            {
                if (batchRendererAllowedCameraViewIds[i] == cameraViewId)
                {
                    return;
                }
            }

            if (batchRendererAllowedCameraViewIdCount >= batchRendererAllowedCameraViewIds.Length)
            {
                Array.Resize(
                    ref batchRendererAllowedCameraViewIds,
                    Mathf.NextPowerOfTwo(Mathf.Max(1, batchRendererAllowedCameraViewIdCount + 1)));
            }

            batchRendererAllowedCameraViewIds[batchRendererAllowedCameraViewIdCount++] = cameraViewId;
        }

        private bool IsBatchRendererCameraViewAllowed(int cameraViewId)
        {
            for (int i = 0; i < batchRendererAllowedCameraViewIdCount; i++)
            {
                if (batchRendererAllowedCameraViewIds[i] == cameraViewId)
                {
                    return true;
                }
            }

            return false;
        }

        private static BatchCullingScratch GetBatchCullingScratch()
        {
            BatchCullingScratch? scratch = threadBatchCullingScratch;
            if (scratch == null)
            {
                scratch = new BatchCullingScratch();
                threadBatchCullingScratch = scratch;
            }

            return scratch;
        }

        private static BatchRendererState? ResolveBatchRendererState(IntPtr userContext)
        {
            if (userContext == IntPtr.Zero)
            {
                return null;
            }

            GCHandle handle = GCHandle.FromIntPtr(userContext);
            return handle.IsAllocated ? handle.Target as BatchRendererState : null;
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
            groupTotalInstanceCounts = Array.Empty<int>();
            pageInstanceGroupLocalIndices = Array.Empty<int[]>();
            pagePacketValidInstanceCounts = Array.Empty<int[]>();
            invalidCompiledPacketCount = 0;
            ReleaseBatchRendererResources();
            DisposeUploadArrays();
            selectedPacketCount = 0;
            activeGroupIndexCount = 0;
            cellCandidateCount = 0;
            batchRendererAllowedCameraViewIdCount = 0;
            batchRendererAllowedCameraViewFrame = -1;
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
                $"Vegetation compiled graph dropped invalid packet instance ranges count={invalidCompiledPacketCount}. Rebuild compiled foliage pages; runtime will skip invalid packet entries to keep BRG culling output valid.");
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

        private static int GetValidPacketInstanceCount(BatchRendererState state, int pageRecordIndex, int packetIndex)
        {
            if (pageRecordIndex < 0 ||
                pageRecordIndex >= state.PagePacketValidInstanceCounts.Length ||
                packetIndex < 0)
            {
                return 0;
            }

            int[] counts = state.PagePacketValidInstanceCounts[pageRecordIndex];
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

        private static bool TryResolvePacketLocalInstance(
            BatchRendererState state,
            int pageRecordIndex,
            BatchRendererPacketRecord packet,
            int instanceOffset,
            int expectedWorldGroupIndex,
            out int sourceInstanceIndex,
            out int groupLocalInstanceIndex)
        {
            sourceInstanceIndex = packet.FirstInstance + instanceOffset;
            groupLocalInstanceIndex = -1;
            if (instanceOffset < 0 ||
                sourceInstanceIndex < 0 ||
                pageRecordIndex < 0 ||
                pageRecordIndex >= state.PageInstanceAssetGroupIndices.Length ||
                pageRecordIndex >= state.PageInstanceGroupLocalIndices.Length)
            {
                return false;
            }

            int[] assetGroupIndices = state.PageInstanceAssetGroupIndices[pageRecordIndex];
            if (sourceInstanceIndex >= assetGroupIndices.Length ||
                assetGroupIndices[sourceInstanceIndex] != packet.AssetGroupIndex)
            {
                return false;
            }

            int[] pageLookup = state.PageInstanceGroupLocalIndices[pageRecordIndex];
            if (sourceInstanceIndex >= pageLookup.Length)
            {
                return false;
            }

            groupLocalInstanceIndex = pageLookup[sourceInstanceIndex];
            return expectedWorldGroupIndex >= 0 &&
                   expectedWorldGroupIndex < state.GroupTotalInstanceCounts.Length &&
                   groupLocalInstanceIndex >= 0 &&
                   groupLocalInstanceIndex < state.GroupTotalInstanceCounts[expectedWorldGroupIndex];
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

        private static PacketRange GetPacketRange(
            BatchRendererState state,
            int pageRecordIndex,
            FoliageRepresentationKind representationKind,
            int cellRecordIndex)
        {
            if (pageRecordIndex < 0)
            {
                return default;
            }

            int representationSlot = GetRepresentationSlot(representationKind);
            if (representationSlot < 0)
            {
                return default;
            }

            if (cellRecordIndex < 0)
            {
                int pageRangeIndex = pageRecordIndex * PacketLookupRepresentationSlotCount + representationSlot;
                return pageRangeIndex >= 0 && pageRangeIndex < state.PagePacketRanges.Length
                    ? state.PagePacketRanges[pageRangeIndex]
                    : default;
            }

            int cellRangeIndex = cellRecordIndex * PacketLookupRepresentationSlotCount + representationSlot;
            return cellRangeIndex >= 0 && cellRangeIndex < state.CellPacketRanges.Length
                ? state.CellPacketRanges[cellRangeIndex]
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

        private void EnsureBatchRendererResources()
        {
            VegetationFoliageFeatureSettings settings = batchRendererSettings
                ?? throw new InvalidOperationException("Vegetation BRG settings are not assigned.");
            BatchRendererSettingsSnapshot settingsSnapshot = BatchRendererSettingsSnapshot.From(settings);
            if (batchRendererGroup != null && batchRendererState != null && brgBatches.Length == groups.Count)
            {
                batchRendererState.Settings = settingsSnapshot;
                ApplyBatchRendererViewTypes(batchRendererGroup, settingsSnapshot);
                return;
            }

            ReleaseBatchRendererResources();
            BatchRendererState state = CreateBatchRendererState(settingsSnapshot);
            state.Handle = GCHandle.Alloc(state);
            try
            {
                batchRendererGroup = new BatchRendererGroup(
                    OnPerformBatchRendererCulling,
                    GCHandle.ToIntPtr(state.Handle));
                state.RendererGroup = batchRendererGroup;
                ApplyBatchRendererViewTypes(batchRendererGroup, settingsSnapshot);
                batchRendererGroup.SetGlobalBounds(state.CalculateGlobalBounds());

                brgBatches = new VegetationBrgBatch[groups.Count];
                state.Batches = brgBatches;
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    int instanceCount = groupIndex < groupTotalInstanceCounts.Length
                        ? groupTotalInstanceCounts[groupIndex]
                        : 0;
                    if (instanceCount <= 0)
                    {
                        continue;
                    }

                    brgBatches[groupIndex] = CreateBatchRendererBatch(groupIndex, instanceCount);
                }
                batchRendererState = state;
                LogBatchRendererRegistration(state);
            }
            catch
            {
                DisposeBatchRendererStateImmediate(state);
                brgBatches = Array.Empty<VegetationBrgBatch>();
                batchRendererGroup = null;
                batchRendererState = null;
                throw;
            }
        }

        private static void ApplyBatchRendererViewTypes(
            BatchRendererGroup rendererGroup,
            BatchRendererSettingsSnapshot settings)
        {
            rendererGroup.SetEnabledViewTypes(settings.UsesBatchRendererLightCulling
                ? BatchRendererCameraAndLightViewTypes
                : BatchRendererCameraViewTypes);
        }

        private BatchRendererState CreateBatchRendererState(BatchRendererSettingsSnapshot settings)
        {
            // Range: the compiled graph currently owned by the main thread. Condition: graph was validated before BRG registration. Output: immutable managed arrays safe for Unity's BRG worker-thread culling callback.
            int[] providerAssetGroupOffsets = providers.Count > 0 ? new int[providers.Count] : Array.Empty<int>();
            for (int i = 0; i < providers.Count; i++)
            {
                providerAssetGroupOffsets[i] = providers[i].AssetGroupOffset;
            }

            BatchRendererPageRecord[] pageRecords = pages.Count > 0
                ? new BatchRendererPageRecord[pages.Count]
                : Array.Empty<BatchRendererPageRecord>();
            BatchRendererPacketRecord[][] pagePackets = pages.Count > 0
                ? new BatchRendererPacketRecord[pages.Count][]
                : Array.Empty<BatchRendererPacketRecord[]>();
            int[][] pageInstanceAssetGroupIndices = pages.Count > 0
                ? new int[pages.Count][]
                : Array.Empty<int[]>();

            for (int pageRecordIndex = 0; pageRecordIndex < pages.Count; pageRecordIndex++)
            {
                PageRecord pageRecord = pages[pageRecordIndex];
                FoliagePageAsset page = pageRecord.Page;
                pageRecords[pageRecordIndex] = new BatchRendererPageRecord(
                    pageRecord.ProviderIndex,
                    pageRecord.PageRecordIndex,
                    page.WorldBounds,
                    pageRecord.FirstCellRecord,
                    pageRecord.CellCount);

                IReadOnlyList<FoliageRepresentationPacket> packets = page.Packets;
                BatchRendererPacketRecord[] packetRecords = packets.Count > 0
                    ? new BatchRendererPacketRecord[packets.Count]
                    : Array.Empty<BatchRendererPacketRecord>();
                for (int packetIndex = 0; packetIndex < packets.Count; packetIndex++)
                {
                    FoliageRepresentationPacket packet = packets[packetIndex];
                    packetRecords[packetIndex] = packet != null
                        ? new BatchRendererPacketRecord(packet)
                        : default;
                }

                pagePackets[pageRecordIndex] = packetRecords;

                IReadOnlyList<FoliagePacketInstance> instances = page.Instances;
                int[] assetGroupIndices = instances.Count > 0
                    ? new int[instances.Count]
                    : Array.Empty<int>();
                for (int instanceIndex = 0; instanceIndex < instances.Count; instanceIndex++)
                {
                    assetGroupIndices[instanceIndex] = instances[instanceIndex].AssetGroupIndex;
                }

                pageInstanceAssetGroupIndices[pageRecordIndex] = assetGroupIndices;
            }

            return new BatchRendererState(
                settings,
                providerAssetGroupOffsets,
                pageRecords,
                cells.Count > 0 ? cells.ToArray() : Array.Empty<CellRecord>(),
                pagePackets,
                pageInstanceAssetGroupIndices,
                pagePacketRanges.Length > 0 ? (PacketRange[])pagePacketRanges.Clone() : Array.Empty<PacketRange>(),
                cellPacketRanges.Length > 0 ? (PacketRange[])cellPacketRanges.Clone() : Array.Empty<PacketRange>(),
                packetLookupIndices.Length > 0 ? (int[])packetLookupIndices.Clone() : Array.Empty<int>(),
                groupTotalInstanceCounts.Length > 0 ? (int[])groupTotalInstanceCounts.Clone() : Array.Empty<int>(),
                CloneJagged(pageInstanceGroupLocalIndices),
                CloneJagged(pagePacketValidInstanceCounts));
        }

        private VegetationBrgBatch CreateBatchRendererBatch(int groupIndex, int instanceCount)
        {
            GroupRecord group = groups[groupIndex];
            FoliageAssetGroup assetGroup = group.AssetGroup;
            Mesh mesh = assetGroup.Mesh;
            Material material = assetGroup.Material;
            string shaderName = material.shader != null ? material.shader.name : "<missing-shader>";
            BatchMeshID meshId = batchRendererGroup!.RegisterMesh(mesh);
            BatchMaterialID materialId = batchRendererGroup.RegisterMaterial(material);

            int byteAddressObjectToWorld = BrgExtraBytes;
            int byteAddressWorldToObject = byteAddressObjectToWorld + instanceCount * BrgSizeOfPackedMatrix;
            int byteAddressPackedLeafTint = byteAddressWorldToObject + instanceCount * BrgSizeOfPackedMatrix;
            int byteAddressWind = AlignBytes(byteAddressPackedLeafTint + instanceCount * BrgSizeOfUint, BrgSizeOfFloat4);
            int totalBytes = AlignBytes(byteAddressWind + instanceCount * BrgSizeOfFloat4, sizeof(int));
            ValidateBatchRendererBufferLayout(
                byteAddressObjectToWorld,
                byteAddressWorldToObject,
                byteAddressPackedLeafTint,
                byteAddressWind);
            GraphicsBuffer instanceDataBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Raw,
                Mathf.Max(1, totalBytes / sizeof(int)),
                sizeof(int));

            UploadBatchRendererInstanceData(
                groupIndex,
                instanceCount,
                instanceDataBuffer,
                byteAddressObjectToWorld,
                byteAddressWorldToObject,
                byteAddressPackedLeafTint,
                byteAddressWind);

            NativeArray<MetadataValue> metadata = new NativeArray<MetadataValue>(
                4,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                metadata[0] = CreateBatchMetadata(UnityObjectToWorldId, byteAddressObjectToWorld);
                metadata[1] = CreateBatchMetadata(UnityWorldToObjectId, byteAddressWorldToObject);
                metadata[2] = CreateBatchMetadata(BrgPackedLeafTintId, byteAddressPackedLeafTint);
                metadata[3] = CreateBatchMetadata(BrgWindId, byteAddressWind);
                BatchID batchId = batchRendererGroup.AddBatch(metadata, instanceDataBuffer.bufferHandle);
                return new VegetationBrgBatch(
                    groupIndex,
                    assetGroup.DebugLabel,
                    mesh.name,
                    material.name,
                    shaderName,
                    assetGroup.ForwardPassIndex,
                    assetGroup.DepthPassIndex,
                    assetGroup.ShadowPassIndex,
                    byteAddressObjectToWorld,
                    byteAddressWorldToObject,
                    byteAddressPackedLeafTint,
                    byteAddressWind,
                    totalBytes,
                    batchId,
                    meshId,
                    materialId,
                    instanceDataBuffer,
                    instanceCount);
            }
            catch
            {
                batchRendererGroup.UnregisterMesh(meshId);
                batchRendererGroup.UnregisterMaterial(materialId);
                instanceDataBuffer.Release();
                throw;
            }
            finally
            {
                metadata.Dispose();
            }
        }

        private void LogBatchRendererRegistration(BatchRendererState state)
        {
            if (!state.Settings.ShouldLogBatchRendererDiagnostics)
            {
                return;
            }

            int validBatchCount = 0;
            for (int i = 0; i < state.Batches.Length; i++)
            {
                if (state.Batches[i].IsValid)
                {
                    validBatchCount++;
                }
            }

            Bounds bounds = state.CalculateGlobalBounds();
            StringBuilder builder = new StringBuilder(1024 + validBatchCount * 160);
            builder.Append("Vegetation BRG registered api=")
                .Append(state.Settings.GraphicsApi)
                .Append(" shadowMode=")
                .Append(state.Settings.ShadowMode)
                .Append(" brgLightCulling=")
                .Append(state.Settings.UsesBatchRendererLightCulling)
                .Append(" bufferTarget=")
                .Append(BatchRendererGroup.BufferTarget)
                .Append(" providers=")
                .Append(providers.Count)
                .Append(" pages=")
                .Append(state.PageCount)
                .Append(" cells=")
                .Append(state.CellCount)
                .Append(" groups=")
                .Append(state.GroupCount)
                .Append(" validBatches=")
                .Append(validBatchCount)
                .Append(" boundsCenter=")
                .Append(bounds.center)
                .Append(" boundsSize=")
                .Append(bounds.size);

            for (int i = 0; i < state.Batches.Length; i++)
            {
                VegetationBrgBatch batch = state.Batches[i];
                if (!batch.IsValid)
                {
                    continue;
                }

                builder.AppendLine()
                    .Append("  brgBatch group=")
                    .Append(batch.GroupIndex)
                    .Append(" batchId=")
                    .Append(batch.BatchId.value)
                    .Append(" meshId=")
                    .Append(batch.MeshId.value)
                    .Append(" materialId=")
                    .Append(batch.MaterialId.value)
                    .Append(" instances=")
                    .Append(batch.InstanceCount)
                    .Append(" bufferBytes=")
                    .Append(batch.BufferBytes)
                    .Append(" metadataBytes=[objectToWorld:")
                    .Append(batch.ObjectToWorldByteAddress)
                    .Append(",worldToObject:")
                    .Append(batch.WorldToObjectByteAddress)
                    .Append(",leafTint:")
                    .Append(batch.PackedLeafTintByteAddress)
                    .Append(",wind:")
                    .Append(batch.WindByteAddress)
                    .Append("] passes=[forward:")
                    .Append(batch.ForwardPassIndex)
                    .Append(",depth:")
                    .Append(batch.DepthPassIndex)
                    .Append(",shadow:")
                    .Append(batch.ShadowPassIndex)
                    .Append("] mesh='")
                    .Append(batch.MeshName)
                    .Append("' material='")
                    .Append(batch.MaterialName)
                    .Append("' shader='")
                    .Append(batch.ShaderName)
                    .Append("' label='")
                    .Append(batch.DebugLabel)
                    .Append('\'');
            }

            Debug.Log(builder.ToString());
        }

        private void UploadBatchRendererInstanceData(
            int groupIndex,
            int instanceCount,
            GraphicsBuffer instanceDataBuffer,
            int byteAddressObjectToWorld,
            int byteAddressWorldToObject,
            int byteAddressPackedLeafTint,
            int byteAddressWind)
        {
            Matrix4x4[] zeroMatrices =
            {
                Matrix4x4.zero
            };
            instanceDataBuffer.SetData(zeroMatrices, 0, 0, zeroMatrices.Length);

            NativeArray<PackedMatrix> objectToWorld = new NativeArray<PackedMatrix>(
                instanceCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<PackedMatrix> worldToObject = new NativeArray<PackedMatrix>(
                instanceCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<uint> packedLeafTint = new NativeArray<uint>(
                instanceCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<Vector4> wind = new NativeArray<Vector4>(
                instanceCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                for (int pageRecordIndex = 0; pageRecordIndex < pages.Count; pageRecordIndex++)
                {
                    PageRecord pageRecord = pages[pageRecordIndex];
                    IReadOnlyList<FoliagePacketInstance> instances = pageRecord.Page.Instances;
                    int[] pageLookup = pageInstanceGroupLocalIndices[pageRecordIndex];
                    for (int instanceIndex = 0; instanceIndex < instances.Count; instanceIndex++)
                    {
                        FoliagePacketInstance instance = instances[instanceIndex];
                        int worldGroupIndex = ResolveWorldGroupIndex(pageRecord.ProviderIndex, instance.AssetGroupIndex);
                        if (worldGroupIndex != groupIndex)
                        {
                            continue;
                        }

                        int localIndex = pageLookup[instanceIndex];
                        if (localIndex < 0 || localIndex >= instanceCount)
                        {
                            continue;
                        }

                        FoliageWindMetadata windMetadata = instance.WindMetadata;
                        objectToWorld[localIndex] = new PackedMatrix(instance.ObjectToWorld);
                        worldToObject[localIndex] = new PackedMatrix(instance.WorldToObject);
                        packedLeafTint[localIndex] = instance.PackedLeafTint;
                        wind[localIndex] = new Vector4(
                            windMetadata.Phase01,
                            windMetadata.TrunkBendWeight,
                            windMetadata.BranchFlutterWeight,
                            windMetadata.AnchorHeight);
                    }
                }

                instanceDataBuffer.SetData(
                    objectToWorld,
                    0,
                    byteAddressObjectToWorld / BrgSizeOfPackedMatrix,
                    objectToWorld.Length);
                instanceDataBuffer.SetData(
                    worldToObject,
                    0,
                    byteAddressWorldToObject / BrgSizeOfPackedMatrix,
                    worldToObject.Length);
                instanceDataBuffer.SetData(
                    packedLeafTint,
                    0,
                    byteAddressPackedLeafTint / BrgSizeOfUint,
                    packedLeafTint.Length);
                instanceDataBuffer.SetData(
                    wind,
                    0,
                    byteAddressWind / BrgSizeOfFloat4,
                    wind.Length);
            }
            finally
            {
                objectToWorld.Dispose();
                worldToObject.Dispose();
                packedLeafTint.Dispose();
                wind.Dispose();
            }
        }

        private static MetadataValue CreateBatchMetadata(int propertyId, int byteAddress)
        {
            return new MetadataValue
            {
                NameID = propertyId,
                Value = BrgPerInstanceMetadataFlag | unchecked((uint)byteAddress)
            };
        }

        private static void ValidateBatchRendererBufferLayout(
            int byteAddressObjectToWorld,
            int byteAddressWorldToObject,
            int byteAddressPackedLeafTint,
            int byteAddressWind)
        {
            if (byteAddressObjectToWorld % BrgSizeOfPackedMatrix != 0 ||
                byteAddressWorldToObject % BrgSizeOfPackedMatrix != 0 ||
                byteAddressPackedLeafTint % BrgSizeOfUint != 0 ||
                byteAddressWind % BrgSizeOfFloat4 != 0)
            {
                throw new InvalidOperationException(
                    "Vegetation BRG batch buffer layout is invalid: metadata byte addresses must match GraphicsBuffer.SetData element alignment.");
            }
        }

        private Bounds CalculateGlobalBounds()
        {
            if (pages.Count == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Bounds bounds = pages[0].Page.WorldBounds;
            for (int i = 1; i < pages.Count; i++)
            {
                bounds.Encapsulate(pages[i].Page.WorldBounds);
            }

            return bounds;
        }

        private static int AlignBytes(int value, int alignment)
        {
            int safeAlignment = Mathf.Max(1, alignment);
            return (value + safeAlignment - 1) / safeAlignment * safeAlignment;
        }

        private static int[][] CloneJagged(int[][] source)
        {
            if (source.Length == 0)
            {
                return Array.Empty<int[]>();
            }

            int[][] clone = new int[source.Length][];
            for (int i = 0; i < source.Length; i++)
            {
                int[] inner = source[i];
                clone[i] = inner.Length > 0 ? (int[])inner.Clone() : Array.Empty<int>();
            }

            return clone;
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

        private int MarkVisibleByBatchCullingContext(
            BatchCullingContext cullingContext,
            BatchCullingScratch scratch,
            BatchRendererState state)
        {
            NativeArray<CullingSplit> splits = cullingContext.cullingSplits;
            NativeArray<Plane> cullingPlanes = cullingContext.cullingPlanes;
            if (!splits.IsCreated || !cullingPlanes.IsCreated || splits.Length <= 0)
            {
                return 0;
            }

            for (int planeIndex = 0; planeIndex < cullingPlanes.Length; planeIndex++)
            {
                scratch.CullingPlanes[planeIndex] = cullingPlanes[planeIndex];
            }

            int splitCount = Mathf.Min(splits.Length, 30);
            int activeSplitMask = 0;
            for (int splitIndex = 0; splitIndex < splitCount; splitIndex++)
            {
                CullingSplit split = splits[splitIndex];
                scratch.SplitPlaneOffsets[splitIndex] = Mathf.Clamp(split.cullingPlaneOffset, 0, cullingPlanes.Length);
                scratch.SplitPlaneCounts[splitIndex] = Mathf.Clamp(
                    split.cullingPlaneCount,
                    0,
                    cullingPlanes.Length - scratch.SplitPlaneOffsets[splitIndex]);
                int splitBit = 1 << splitIndex;
                if ((cullingContext.splitExclusionMask & splitBit) == 0 && scratch.SplitPlaneCounts[splitIndex] > 0)
                {
                    activeSplitMask |= splitBit;
                }
            }

            if (activeSplitMask == 0)
            {
                return 0;
            }

            for (int pageIndex = 0; pageIndex < state.Pages.Length; pageIndex++)
            {
                BatchRendererPageRecord page = state.Pages[pageIndex];
                int pageSplitMask = TestBoundsBatchSplitMask(page.WorldBounds, activeSplitMask, splitCount, scratch);
                if (pageSplitMask == 0)
                {
                    continue;
                }

                scratch.VisiblePageFrustumMasks[pageIndex] = pageSplitMask;
                scratch.MarkPageVisible(pageIndex);
                for (int cellOffset = 0; cellOffset < page.CellCount; cellOffset++)
                {
                    int cellRecordIndex = page.FirstCellRecord + cellOffset;
                    if (cellRecordIndex < 0 || cellRecordIndex >= state.Cells.Length)
                    {
                        continue;
                    }

                    int cellSplitMask = TestBoundsBatchSplitMask(state.Cells[cellRecordIndex].WorldBounds, pageSplitMask, splitCount, scratch);
                    if (cellSplitMask == 0)
                    {
                        continue;
                    }

                    scratch.VisibleCellFrustumMasks[cellRecordIndex] = cellSplitMask;
                    scratch.MarkCellVisible(cellRecordIndex, state.Cells[cellRecordIndex].PageRecordIndex);
                }
            }

            return activeSplitMask;
        }

        private int TestBoundsBatchSplitMask(Bounds bounds, int splitMask, int splitCount, BatchCullingScratch scratch)
        {
            int visibleMask = 0;
            for (int splitIndex = 0; splitIndex < splitCount; splitIndex++)
            {
                int splitBit = 1 << splitIndex;
                if ((splitMask & splitBit) == 0)
                {
                    continue;
                }

                if (TestBoundsPlanes(
                        scratch.CullingPlanes,
                        scratch.SplitPlaneOffsets[splitIndex],
                        scratch.SplitPlaneCounts[splitIndex],
                        bounds))
                {
                    visibleMask |= splitBit;
                }
            }

            return visibleMask;
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

        private static bool TestBoundsPlanes(Plane[] planes, int planeOffset, int planeCount, Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int planeIndex = 0; planeIndex < planeCount; planeIndex++)
            {
                Plane plane = planes[planeOffset + planeIndex];
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

        private unsafe JobHandle OnPerformBatchRendererCulling(
            BatchRendererGroup rendererGroup,
            BatchCullingContext cullingContext,
            BatchCullingOutput cullingOutput,
            IntPtr userContext)
        {
            BatchRendererState? state = ResolveBatchRendererState(userContext);
            if (state == null ||
                rendererGroup != state.RendererGroup ||
                batchRendererFaulted ||
                state.GroupCount == 0 ||
                state.PageCount == 0)
            {
                WriteEmptyBatchCullingOutput(cullingOutput);
                return default;
            }

            BatchRendererSettingsSnapshot settings = state.Settings;
            VegetationRenderPassMode passMode = cullingContext.viewType == BatchCullingViewType.Light
                ? VegetationRenderPassMode.Shadow
                : VegetationRenderPassMode.Color;
            if (passMode != VegetationRenderPassMode.Shadow &&
                !IsBatchRendererCameraViewAllowed(cullingContext.viewID.GetInstanceID()))
            {
                WriteEmptyBatchCullingOutput(cullingOutput);
                return default;
            }

            if (passMode == VegetationRenderPassMode.Shadow && settings.ShadowMode == VegetationShadowMode.Off)
            {
                WriteEmptyBatchCullingOutput(cullingOutput);
                return default;
            }

            BatchCullingScratch scratch = GetBatchCullingScratch();
            scratch.ResetVisibility(
                state.PageCount,
                state.CellCount,
                state.GroupCount,
                state.PacketLookupCount,
                cullingContext.cullingSplits.IsCreated ? cullingContext.cullingSplits.Length : 0,
                cullingContext.cullingPlanes.IsCreated ? cullingContext.cullingPlanes.Length : 0);
            int activeSplitMask = MarkVisibleByBatchCullingContext(cullingContext, scratch, state);
            if (activeSplitMask == 0)
            {
                WriteEmptyBatchCullingOutput(cullingOutput);
                return default;
            }

            int activeSplitCount = CountRequiredBits(activeSplitMask);
            SelectBatchRendererPackets(scratch, cullingContext.lodParameters.cameraPosition, passMode, settings, activeSplitCount, state);
            if (!BuildSelectedGroupLayout(scratch, state))
            {
                WriteEmptyBatchCullingOutput(cullingOutput);
                return default;
            }

            WriteBatchCullingOutput(
                cullingOutput,
                scratch,
                activeSplitMask,
                passMode,
                state,
                cullingContext.viewType,
                cullingContext.viewID.GetInstanceID(),
                cullingContext.viewID.GetSliceIndex());
            return default;
        }

        private bool BuildSelectedGroupLayout(BatchCullingScratch scratch, BatchRendererState state)
        {
            scratch.PreparedInstanceCount = 0;
            scratch.PreparedActiveGroupCount = 0;
            for (int i = 0; i < scratch.ActiveGroupIndexCount; i++)
            {
                int groupIndex = scratch.ActiveGroupIndices[i];
                if (groupIndex < 0 || groupIndex >= scratch.GroupInstanceCounts.Length)
                {
                    continue;
                }

                int instanceCount = scratch.GroupInstanceCounts[groupIndex];
                if (instanceCount <= 0 || groupIndex >= state.Batches.Length || !state.Batches[groupIndex].IsValid)
                {
                    continue;
                }

                scratch.GroupStartInstances[groupIndex] = scratch.PreparedInstanceCount;
                scratch.GroupWriteOffsets[groupIndex] = 0;
                scratch.PreparedInstanceCount += instanceCount;
                scratch.PreparedActiveGroupCount++;
            }

            return scratch.PreparedInstanceCount > 0 && scratch.PreparedActiveGroupCount > 0;
        }

        private unsafe void WriteBatchCullingOutput(
            BatchCullingOutput cullingOutput,
            BatchCullingScratch scratch,
            int activeSplitMask,
            VegetationRenderPassMode passMode,
            BatchRendererState state,
            BatchCullingViewType viewType,
            int viewInstanceId,
            int viewSliceIndex)
        {
            ClearBatchCullingOutputCustomResult(cullingOutput);
            BatchCullingOutputDrawCommands* drawCommands =
                (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();
            *drawCommands = default;

            int commandCount = scratch.PreparedActiveGroupCount;
            int instanceCount = scratch.PreparedInstanceCount;
            int alignment = UnsafeUtility.AlignOf<long>();
            drawCommands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<BatchDrawCommand>() * commandCount,
                alignment,
                Allocator.TempJob);
            drawCommands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<BatchDrawRange>(),
                alignment,
                Allocator.TempJob);
            drawCommands->visibleInstances = (int*)UnsafeUtility.Malloc(
                sizeof(int) * instanceCount,
                alignment,
                Allocator.TempJob);
            drawCommands->drawCommandCount = commandCount;
            drawCommands->drawRangeCount = 1;
            drawCommands->visibleInstanceCount = instanceCount;
            drawCommands->instanceSortingPositions = null;
            drawCommands->instanceSortingPositionFloatCount = 0;

            UnsafeUtility.MemClear(drawCommands->visibleInstances, sizeof(int) * instanceCount);
            FillBatchVisibleInstances(scratch, drawCommands->visibleInstances, state);

            int commandIndex = 0;
            int compactVisibleOffset = 0;
            for (int i = 0; i < scratch.ActiveGroupIndexCount; i++)
            {
                int groupIndex = scratch.ActiveGroupIndices[i];
                if (groupIndex < 0 || groupIndex >= scratch.GroupInstanceCounts.Length || groupIndex >= state.Batches.Length)
                {
                    continue;
                }

                int writtenInstanceCount = scratch.GroupWriteOffsets[groupIndex];
                VegetationBrgBatch batch = state.Batches[groupIndex];
                if (writtenInstanceCount <= 0 || !batch.IsValid)
                {
                    continue;
                }

                int sourceVisibleOffset = scratch.GroupStartInstances[groupIndex];
                if (sourceVisibleOffset != compactVisibleOffset)
                {
                    UnsafeUtility.MemMove(
                        drawCommands->visibleInstances + compactVisibleOffset,
                        drawCommands->visibleInstances + sourceVisibleOffset,
                        sizeof(int) * writtenInstanceCount);
                }

                int groupSplitMask = groupIndex < scratch.GroupFrustumMasks.Length && scratch.GroupFrustumMasks[groupIndex] != 0
                    ? scratch.GroupFrustumMasks[groupIndex]
                    : activeSplitMask;
                drawCommands->drawCommands[commandIndex] = new BatchDrawCommand
                {
                    flags = BatchDrawCommandFlags.None,
                    visibleOffset = (uint)compactVisibleOffset,
                    visibleCount = (uint)writtenInstanceCount,
                    batchID = batch.BatchId,
                    materialID = batch.MaterialId,
                    meshID = batch.MeshId,
                    submeshIndex = 0,
                    splitVisibilityMask = (ushort)(groupSplitMask & 0xffff),
                    lightmapIndex = 0,
                    sortingPosition = 0
                };
                compactVisibleOffset += writtenInstanceCount;
                commandIndex++;
            }

            drawCommands->drawCommandCount = commandIndex;
            drawCommands->drawRangeCount = commandIndex > 0 ? 1 : 0;
            drawCommands->visibleInstanceCount = compactVisibleOffset;

            drawCommands->drawRanges[0] = new BatchDrawRange
            {
                drawCommandsBegin = 0,
                drawCommandsCount = (uint)commandIndex,
                drawCommandsType = BatchDrawCommandType.Direct,
                filterSettings = new BatchFilterSettings
                {
                    layer = 0,
                    batchLayer = BrgVegetationBatchLayer,
                    renderingLayerMask = uint.MaxValue,
                    sceneCullingMask = ulong.MaxValue,
                    motionMode = MotionVectorGenerationMode.ForceNoMotion,
                    shadowCastingMode = passMode == VegetationRenderPassMode.Shadow
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off,
                    receiveShadows = true,
                    staticShadowCaster = false,
                    allDepthSorted = false
                }
            };

            LogBatchRendererCullingOutput(
                scratch,
                activeSplitMask,
                passMode,
                state,
                viewType,
                viewInstanceId,
                viewSliceIndex,
                commandIndex,
                compactVisibleOffset);
        }

        private static void LogBatchRendererCullingOutput(
            BatchCullingScratch scratch,
            int activeSplitMask,
            VegetationRenderPassMode passMode,
            BatchRendererState state,
            BatchCullingViewType viewType,
            int viewInstanceId,
            int viewSliceIndex,
            int commandCount,
            int visibleInstanceCount)
        {
            if (!state.Settings.ShouldLogBatchRendererDiagnostics)
            {
                return;
            }

            int logIndex = Interlocked.Increment(ref state.CullingDiagnosticsLogCount);
            if (logIndex > BatchRendererCullingDiagnosticLogLimit)
            {
                return;
            }

            StringBuilder builder = new StringBuilder(1024 + commandCount * 128);
            builder.Append("Vegetation BRG culling output #")
                .Append(logIndex)
                .Append(" api=")
                .Append(state.Settings.GraphicsApi)
                .Append(" viewType=")
                .Append(viewType)
                .Append(" viewInstanceId=")
                .Append(viewInstanceId)
                .Append(" viewSlice=")
                .Append(viewSliceIndex)
                .Append(" pass=")
                .Append(passMode)
                .Append(" shadowMode=")
                .Append(state.Settings.ShadowMode)
                .Append(" activeSplitMask=0x")
                .Append(activeSplitMask.ToString("X"))
                .Append(" commands=")
                .Append(commandCount)
                .Append(" visibleInstances=")
                .Append(visibleInstanceCount)
                .Append(" selectedPackets=")
                .Append(scratch.SelectedPacketCount)
                .Append(" activeGroups=")
                .Append(scratch.ActiveGroupIndexCount);

            int compactVisibleOffset = 0;
            for (int i = 0; i < scratch.ActiveGroupIndexCount; i++)
            {
                int groupIndex = scratch.ActiveGroupIndices[i];
                if (groupIndex < 0 || groupIndex >= scratch.GroupWriteOffsets.Length || groupIndex >= state.Batches.Length)
                {
                    continue;
                }

                int writtenInstanceCount = scratch.GroupWriteOffsets[groupIndex];
                VegetationBrgBatch batch = state.Batches[groupIndex];
                if (writtenInstanceCount <= 0 || !batch.IsValid)
                {
                    continue;
                }

                int groupSplitMask = groupIndex < scratch.GroupFrustumMasks.Length && scratch.GroupFrustumMasks[groupIndex] != 0
                    ? scratch.GroupFrustumMasks[groupIndex]
                    : activeSplitMask;
                builder.AppendLine()
                    .Append("  draw group=")
                    .Append(groupIndex)
                    .Append(" batchId=")
                    .Append(batch.BatchId.value)
                    .Append(" meshId=")
                    .Append(batch.MeshId.value)
                    .Append(" materialId=")
                    .Append(batch.MaterialId.value)
                    .Append(" visibleOffset=")
                    .Append(compactVisibleOffset)
                    .Append(" visibleCount=")
                    .Append(writtenInstanceCount)
                    .Append(" batchInstanceCount=")
                    .Append(batch.InstanceCount)
                    .Append(" splitMask=0x")
                    .Append(groupSplitMask.ToString("X"))
                    .Append(" passIndices=[forward:")
                    .Append(batch.ForwardPassIndex)
                    .Append(",depth:")
                    .Append(batch.DepthPassIndex)
                    .Append(",shadow:")
                    .Append(batch.ShadowPassIndex)
                    .Append("] mesh='")
                    .Append(batch.MeshName)
                    .Append("' material='")
                    .Append(batch.MaterialName)
                    .Append("' shader='")
                    .Append(batch.ShaderName)
                    .Append("' label='")
                    .Append(batch.DebugLabel)
                    .Append('\'');
                compactVisibleOffset += writtenInstanceCount;
            }

            Debug.Log(builder.ToString());
        }

        private unsafe void FillBatchVisibleInstances(BatchCullingScratch scratch, int* visibleInstances, BatchRendererState state)
        {
            for (int selectionIndex = 0; selectionIndex < scratch.SelectedPacketCount; selectionIndex++)
            {
                PacketSelection selection = scratch.SelectedPackets[selectionIndex];
                if (selection.PageRecordIndex < 0 ||
                    selection.PageRecordIndex >= state.Pages.Length ||
                    selection.PageRecordIndex >= state.PagePackets.Length)
                {
                    continue;
                }

                BatchRendererPacketRecord[] packets = state.PagePackets[selection.PageRecordIndex];
                if (selection.PacketIndex < 0 || selection.PacketIndex >= packets.Length)
                {
                    continue;
                }

                BatchRendererPageRecord pageRecord = state.Pages[selection.PageRecordIndex];
                BatchRendererPacketRecord packet = packets[selection.PacketIndex];
                int worldGroupIndex = ResolveWorldGroupIndex(state, pageRecord.ProviderIndex, packet.AssetGroupIndex);
                if (worldGroupIndex < 0 || worldGroupIndex >= scratch.GroupWriteOffsets.Length)
                {
                    continue;
                }

                int groupStart = scratch.GroupStartInstances[worldGroupIndex];
                int groupOffset = scratch.GroupWriteOffsets[worldGroupIndex];
                int selectionWriteLimit = groupOffset + selection.InstanceCount;
                for (int instanceOffset = 0; instanceOffset < packet.InstanceCount; instanceOffset++)
                {
                    if (groupOffset >= selectionWriteLimit ||
                        groupOffset >= scratch.GroupInstanceCounts[worldGroupIndex])
                    {
                        break;
                    }

                    if (!TryResolvePacketLocalInstance(
                            state,
                            selection.PageRecordIndex,
                            packet,
                            instanceOffset,
                            worldGroupIndex,
                            out _,
                            out int groupLocalInstanceIndex))
                    {
                        continue;
                    }

                    int writeIndex = groupStart + groupOffset;
                    if (writeIndex >= 0 && writeIndex < scratch.PreparedInstanceCount)
                    {
                        visibleInstances[writeIndex] = groupLocalInstanceIndex;
                    }

                    groupOffset++;
                }

                scratch.GroupWriteOffsets[worldGroupIndex] = groupOffset;
            }
        }

        private unsafe static void WriteEmptyBatchCullingOutput(BatchCullingOutput cullingOutput)
        {
            ClearBatchCullingOutputCustomResult(cullingOutput);
            BatchCullingOutputDrawCommands* drawCommands =
                (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();
            *drawCommands = default;
        }

        private static void ClearBatchCullingOutputCustomResult(BatchCullingOutput cullingOutput)
        {
            if (cullingOutput.customCullingResult.IsCreated && cullingOutput.customCullingResult.Length > 0)
            {
                cullingOutput.customCullingResult[0] = IntPtr.Zero;
            }
        }

        private static int CountRequiredBits(int mask)
        {
            int count = 0;
            while (mask != 0)
            {
                count++;
                mask >>= 1;
            }

            return count;
        }

        private void SelectPackets(
            Vector3 cameraWorldPosition,
            VegetationRenderPassMode passMode,
            VegetationFoliageFeatureSettings settings,
            Plane[]? explicitFrustum,
            int explicitFrustumCount)
        {
            selectedPacketCount = 0;
            ClearActiveGroupSelection();
            cellCandidateCount = 0;
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
                    AddCellCandidate(new CellCandidate(cellRecordIndex, distanceSqr));
                }

                if (!hasVisibleCell)
                {
                    TrySelectHlodPackets(pageIndex, FoliageRepresentationKind.PageHLOD, -1, passMode, explicitFrustum, explicitFrustumCount, ref remainingWorkBudget, ref remainingInstanceBudget);
                }
            }

            Array.Sort(cellCandidates, 0, cellCandidateCount);
            for (int i = 0; i < cellCandidateCount; i++)
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

        private void SelectBatchRendererPackets(
            BatchCullingScratch scratch,
            Vector3 cameraWorldPosition,
            VegetationRenderPassMode passMode,
            BatchRendererSettingsSnapshot settings,
            int explicitFrustumCount,
            BatchRendererState state)
        {
            // Range: one BRG culling callback. Condition: all per-instance data is already resident in BRG-owned buffers. Output: callback-local packet and group selection without mutating streaming/runtime upload state.
            scratch.ResetSelection(state.GroupCount);
            int remainingWorkBudget = settings.GetWorkBudget(passMode);
            int remainingInstanceBudget = Mathf.Max(1, settings.MaxVisiblePacketInstances);
            float nearDistance = Mathf.Max(1f, settings.NearDetailDistance);
            float nearDistanceSqr = nearDistance * nearDistance;

            for (int pageIndex = 0; pageIndex < state.Pages.Length; pageIndex++)
            {
                if (!scratch.VisiblePageMask[pageIndex])
                {
                    continue;
                }

                BatchRendererPageRecord page = state.Pages[pageIndex];
                bool hasVisibleCell = false;
                for (int cellOffset = 0; cellOffset < page.CellCount; cellOffset++)
                {
                    int cellRecordIndex = page.FirstCellRecord + cellOffset;
                    if (cellRecordIndex < 0 ||
                        cellRecordIndex >= state.Cells.Length ||
                        !scratch.VisibleCellMask[cellRecordIndex])
                    {
                        continue;
                    }

                    hasVisibleCell = true;
                    CellRecord cell = state.Cells[cellRecordIndex];
                    float distanceSqr = CalculateCellDistanceSqr(cell, cameraWorldPosition);
                    scratch.AddCellCandidate(new CellCandidate(cellRecordIndex, distanceSqr));
                }

                if (!hasVisibleCell)
                {
                    TrySelectBatchRendererHlodPackets(
                        scratch,
                        pageIndex,
                        FoliageRepresentationKind.PageHLOD,
                        -1,
                        passMode,
                        explicitFrustumCount,
                        state,
                        ref remainingWorkBudget,
                        ref remainingInstanceBudget);
                }
            }

            Array.Sort(scratch.CellCandidates, 0, scratch.CellCandidateCount);
            for (int i = 0; i < scratch.CellCandidateCount; i++)
            {
                CellCandidate candidate = scratch.CellCandidates[i];
                if (candidate.CellRecordIndex < 0 || candidate.CellRecordIndex >= state.Cells.Length)
                {
                    continue;
                }

                CellRecord cell = state.Cells[candidate.CellRecordIndex];
                if (cell.PageRecordIndex < 0 || cell.PageRecordIndex >= state.Pages.Length)
                {
                    continue;
                }

                BatchRendererPageRecord page = state.Pages[cell.PageRecordIndex];
                if (candidate.DistanceSqr <= nearDistanceSqr)
                {
                    FoliageRepresentationKind desiredTier = ResolveTier(candidate.DistanceSqr, nearDistanceSqr);
                    if (TrySelectBatchRendererNearDetailTierCascade(
                            scratch,
                            page,
                            candidate.CellRecordIndex,
                            desiredTier,
                            passMode,
                            explicitFrustumCount,
                            state,
                            ref remainingWorkBudget,
                            ref remainingInstanceBudget))
                    {
                        continue;
                    }
                }

                TrySelectBatchRendererHlodPackets(
                    scratch,
                    page.PageRecordIndex,
                    FoliageRepresentationKind.CellHLOD,
                    candidate.CellRecordIndex,
                    passMode,
                    explicitFrustumCount,
                    state,
                    ref remainingWorkBudget,
                    ref remainingInstanceBudget);
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

        private bool TrySelectBatchRendererNearDetailTierCascade(
            BatchCullingScratch scratch,
            BatchRendererPageRecord page,
            int cellRecordIndex,
            FoliageRepresentationKind desiredTier,
            VegetationRenderPassMode passMode,
            int explicitFrustumCount,
            BatchRendererState state,
            ref int remainingWorkBudget,
            ref int remainingInstanceBudget)
        {
            if (desiredTier == FoliageRepresentationKind.TreeL0 &&
                TrySelectBatchRendererTierPackets(scratch, page, cellRecordIndex, FoliageRepresentationKind.TreeL0, passMode, explicitFrustumCount, state, ref remainingWorkBudget, ref remainingInstanceBudget))
            {
                return true;
            }

            if ((desiredTier == FoliageRepresentationKind.TreeL0 ||
                 desiredTier == FoliageRepresentationKind.TreeL1) &&
                TrySelectBatchRendererTierPackets(scratch, page, cellRecordIndex, FoliageRepresentationKind.TreeL1, passMode, explicitFrustumCount, state, ref remainingWorkBudget, ref remainingInstanceBudget))
            {
                return true;
            }

            return TrySelectBatchRendererTierPackets(scratch, page, cellRecordIndex, FoliageRepresentationKind.TreeL2, passMode, explicitFrustumCount, state, ref remainingWorkBudget, ref remainingInstanceBudget);
        }

        private bool TrySelectBatchRendererTierPackets(
            BatchCullingScratch scratch,
            BatchRendererPageRecord page,
            int cellRecordIndex,
            FoliageRepresentationKind representationKind,
            VegetationRenderPassMode passMode,
            int explicitFrustumCount,
            BatchRendererState state,
            ref int remainingWorkBudget,
            ref int remainingInstanceBudget)
        {
            return TrySelectBatchRendererPackets(
                scratch,
                page.PageRecordIndex,
                representationKind,
                cellRecordIndex,
                passMode,
                explicitFrustumCount,
                state,
                ref remainingWorkBudget,
                ref remainingInstanceBudget);
        }

        private bool TrySelectBatchRendererHlodPackets(
            BatchCullingScratch scratch,
            int pageRecordIndex,
            FoliageRepresentationKind representationKind,
            int cellRecordIndex,
            VegetationRenderPassMode passMode,
            int explicitFrustumCount,
            BatchRendererState state,
            ref int remainingWorkBudget,
            ref int remainingInstanceBudget)
        {
            return TrySelectBatchRendererPackets(
                scratch,
                pageRecordIndex,
                representationKind,
                cellRecordIndex,
                passMode,
                explicitFrustumCount,
                state,
                ref remainingWorkBudget,
                ref remainingInstanceBudget);
        }

        private bool TrySelectBatchRendererPackets(
            BatchCullingScratch scratch,
            int pageRecordIndex,
            FoliageRepresentationKind representationKind,
            int cellRecordIndex,
            VegetationRenderPassMode passMode,
            int explicitFrustumCount,
            BatchRendererState state,
            ref int remainingWorkBudget,
            ref int remainingInstanceBudget)
        {
            if (pageRecordIndex < 0 || pageRecordIndex >= state.Pages.Length)
            {
                return false;
            }

            BatchRendererPageRecord pageRecord = state.Pages[pageRecordIndex];
            PacketRange packetRange = GetPacketRange(state, pageRecordIndex, representationKind, cellRecordIndex);
            if (packetRange.Count == 0)
            {
                return false;
            }

            BatchRendererPacketRecord[] pagePackets = pageRecordIndex < state.PagePackets.Length
                ? state.PagePackets[pageRecordIndex]
                : Array.Empty<BatchRendererPacketRecord>();
            int visibilityFrustumMask = ResolveBatchRendererVisibilityFrustumMask(scratch, pageRecordIndex, cellRecordIndex, explicitFrustumCount);
            bool chargeBudget = IsNearDetailRepresentation(representationKind);
            int packetCost = 0;
            int packetInstances = 0;
            int matchingPacketCount = 0;
            for (int rangeOffset = 0; rangeOffset < packetRange.Count; rangeOffset++)
            {
                int packetIndex = state.PacketLookupIndices[packetRange.Start + rangeOffset];
                if (!TryResolveSelectablePacket(pagePackets, packetIndex, passMode, explicitFrustumCount, visibilityFrustumMask, out BatchRendererPacketRecord packet, out int resolvedPacketIndex, out int packetFrustumMask))
                {
                    continue;
                }

                int validInstanceCount = GetValidPacketInstanceCount(state, pageRecordIndex, resolvedPacketIndex);
                if (validInstanceCount <= 0)
                {
                    continue;
                }

                if (packetFrustumMask == 0)
                {
                    packetFrustumMask = visibilityFrustumMask;
                }

                _ = packetFrustumMask;
                packetCost += packet.WorkCost;
                packetInstances += validInstanceCount;
                matchingPacketCount++;
            }

            if (matchingPacketCount == 0 ||
                (chargeBudget && (packetCost > remainingWorkBudget || packetInstances > remainingInstanceBudget)))
            {
                return false;
            }

            for (int rangeOffset = 0; rangeOffset < packetRange.Count; rangeOffset++)
            {
                int packetIndex = state.PacketLookupIndices[packetRange.Start + rangeOffset];
                if (!TryResolveSelectablePacket(pagePackets, packetIndex, passMode, explicitFrustumCount, visibilityFrustumMask, out BatchRendererPacketRecord packet, out int resolvedPacketIndex, out int packetFrustumMask))
                {
                    continue;
                }

                int validInstanceCount = GetValidPacketInstanceCount(state, pageRecordIndex, resolvedPacketIndex);
                if (validInstanceCount <= 0)
                {
                    continue;
                }

                if (packetFrustumMask == 0)
                {
                    packetFrustumMask = visibilityFrustumMask;
                }

                scratch.AddSelectedPacket(new PacketSelection(pageRecordIndex, resolvedPacketIndex, validInstanceCount));
                int worldGroupIndex = ResolveWorldGroupIndex(state, pageRecord.ProviderIndex, packet.AssetGroupIndex);
                if (worldGroupIndex >= 0 && worldGroupIndex < scratch.GroupInstanceCounts.Length)
                {
                    if (scratch.GroupInstanceCounts[worldGroupIndex] == 0)
                    {
                        scratch.AddActiveGroupIndex(worldGroupIndex);
                    }

                    scratch.GroupInstanceCounts[worldGroupIndex] += validInstanceCount;
                    if (packetFrustumMask != 0 && worldGroupIndex < scratch.GroupFrustumMasks.Length)
                    {
                        scratch.GroupFrustumMasks[worldGroupIndex] |= packetFrustumMask;
                        scratch.PreparedFrustumMask |= packetFrustumMask;
                    }
                }

                if (packet.Residency == FoliagePacketResidency.NearDetail)
                {
                    scratch.PreparedNearDetailPacketCount++;
                    scratch.IncrementNearDetailTierCounter(packet.RepresentationKind);
                }
                else
                {
                    scratch.PreparedHlodPacketCount++;
                }

                if (passMode == VegetationRenderPassMode.Shadow)
                {
                    scratch.PreparedShadowPacketCount++;
                }
            }

            if (chargeBudget)
            {
                remainingWorkBudget -= packetCost;
                remainingInstanceBudget -= packetInstances;
            }

            scratch.PreparedPacketCount += matchingPacketCount;
            return true;
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

                int validInstanceCount = GetValidPacketInstanceCount(pageRecordIndex, resolvedPacketIndex);
                if (validInstanceCount <= 0)
                {
                    continue;
                }

                if (packetFrustumMask == 0)
                {
                    packetFrustumMask = visibilityFrustumMask;
                }

                _ = resolvedPacketIndex;
                _ = packetFrustumMask;
                packetCost += packet.WorkCost;
                packetInstances += validInstanceCount;
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

                int validInstanceCount = GetValidPacketInstanceCount(pageRecordIndex, resolvedPacketIndex);
                if (validInstanceCount <= 0)
                {
                    continue;
                }

                if (packetFrustumMask == 0)
                {
                    packetFrustumMask = visibilityFrustumMask;
                }

                AddSelectedPacket(new PacketSelection(pageRecordIndex, resolvedPacketIndex, validInstanceCount));
                int worldGroupIndex = ResolveWorldGroupIndex(pageRecord.ProviderIndex, packet.AssetGroupIndex);
                if (worldGroupIndex >= 0 && worldGroupIndex < groupInstanceCounts.Length)
                {
                    if (groupInstanceCounts[worldGroupIndex] == 0)
                    {
                        AddActiveGroupIndex(worldGroupIndex);
                    }

                    groupInstanceCounts[worldGroupIndex] += validInstanceCount;
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

        private static int ResolveBatchRendererVisibilityFrustumMask(
            BatchCullingScratch scratch,
            int pageRecordIndex,
            int cellRecordIndex,
            int explicitFrustumCount)
        {
            if (explicitFrustumCount <= 0)
            {
                return 0;
            }

            int validFrustumMask = AllFrustumBits(explicitFrustumCount);
            if (cellRecordIndex >= 0 && cellRecordIndex < scratch.VisibleCellFrustumMasks.Length)
            {
                return scratch.VisibleCellFrustumMasks[cellRecordIndex] & validFrustumMask;
            }

            if (pageRecordIndex >= 0 && pageRecordIndex < scratch.VisiblePageFrustumMasks.Length)
            {
                return scratch.VisiblePageFrustumMasks[pageRecordIndex] & validFrustumMask;
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
            packet = null!;
            resolvedPacketIndex = -1;
            packetFrustumMask = 0;
            if (packetIndex < 0 || packetIndex >= page.Packets.Count)
            {
                return false;
            }

            packet = page.Packets[packetIndex];
            if (packet == null)
            {
                return false;
            }

            resolvedPacketIndex = packetIndex;
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
            if (packet == null)
            {
                return false;
            }

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

        private static bool TryResolveSelectablePacket(
            BatchRendererPacketRecord[] packets,
            int packetIndex,
            VegetationRenderPassMode passMode,
            int explicitFrustumCount,
            int visibilityFrustumMask,
            out BatchRendererPacketRecord packet,
            out int resolvedPacketIndex,
            out int packetFrustumMask)
        {
            packet = default;
            resolvedPacketIndex = -1;
            packetFrustumMask = 0;
            if (packetIndex < 0 || packetIndex >= packets.Length)
            {
                return false;
            }

            packet = packets[packetIndex];
            if (!packet.IsValid)
            {
                return false;
            }

            resolvedPacketIndex = packetIndex;
            if (explicitFrustumCount > 0 && visibilityFrustumMask == 0)
            {
                return false;
            }

            if (passMode != VegetationRenderPassMode.Shadow)
            {
                packetFrustumMask = visibilityFrustumMask;
                return true;
            }

            if (packet.ShadowMode == FoliageShadowPacketMode.None)
            {
                return false;
            }

            int shadowPacketIndex = packet.ShadowPacketIndex;
            if (shadowPacketIndex < 0 || shadowPacketIndex >= packets.Length)
            {
                return false;
            }

            packet = packets[shadowPacketIndex];
            if (!packet.IsValid || packet.ShadowMode == FoliageShadowPacketMode.None)
            {
                return false;
            }

            resolvedPacketIndex = shadowPacketIndex;
            packetFrustumMask = visibilityFrustumMask;
            return true;
        }

        private int ResolveWorldGroupIndex(int providerIndex, int localGroupIndex)
        {
            if (providerIndex < 0 || providerIndex >= providers.Count || localGroupIndex < 0)
            {
                return -1;
            }

            return providers[providerIndex].AssetGroupOffset + localGroupIndex;
        }

        private static int ResolveWorldGroupIndex(BatchRendererState state, int providerIndex, int localGroupIndex)
        {
            if (providerIndex < 0 ||
                providerIndex >= state.ProviderAssetGroupOffsets.Length ||
                localGroupIndex < 0)
            {
                return -1;
            }

            return state.ProviderAssetGroupOffsets[providerIndex] + localGroupIndex;
        }

        private bool UploadPreparedFrame(VegetationFoliageFeatureSettings settings, VegetationRenderPassMode passMode)
        {
            preparedInstanceCount = 0;
            int minActiveGroupIndex = int.MaxValue;
            int maxActiveGroupIndex = -1;
            using (PrepareUploadLayoutMarker.Auto())
            {
                for (int i = 0; i < activeGroupIndexCount; i++)
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
                for (int selectionIndex = 0; selectionIndex < selectedPacketCount; selectionIndex++)
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
                    int selectionWriteLimit = groupOffset + selection.InstanceCount;
                    for (int instanceOffset = 0; instanceOffset < packet.InstanceCount; instanceOffset++)
                    {
                        if (groupOffset >= selectionWriteLimit ||
                            groupOffset >= groupInstanceCounts[worldGroupIndex])
                        {
                            break;
                        }

                        if (!TryResolvePacketLocalInstance(
                                pageRecord,
                                selection.PageRecordIndex,
                                packet,
                                instanceOffset,
                                worldGroupIndex,
                                out int sourceInstanceIndex,
                                out _))
                        {
                            continue;
                        }

                        int writeIndex = groupStart + groupOffset;
                        if (writeIndex >= 0 && writeIndex < preparedInstanceCount)
                        {
                            instanceData[writeIndex] = ConvertInstance(pageRecord.Page.Instances[sourceInstanceIndex]);
                            groupOffset++;
                        }
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

                for (int i = 0; i < activeGroupIndexCount; i++)
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

            ApplyGlobalShaderSettings(settings);
            hasPreparedFrame = true;
            return true;
        }

        private void ApplyGlobalShaderSettings(VegetationFoliageFeatureSettings settings)
        {
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

            Shader.SetGlobalFloat(WindStrengthId, preparedWindStrength);
            Shader.SetGlobalFloat(WindFrequencyId, preparedWindFrequency);
            Shader.SetGlobalVector(WindDirectionId, preparedWindDirection);
            Shader.SetGlobalVector(LeafFlutterSettingsId, preparedLeafFlutterSettings);
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
            selectedPacketCount = 0;
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
            for (int i = 0; i < activeGroupIndexCount; i++)
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

            activeGroupIndexCount = 0;
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

        private void ReleaseBatchRendererResources()
        {
            BatchRendererState? state = batchRendererState;
            BatchRendererGroup? orphanRendererGroup = batchRendererGroup;
            VegetationBrgBatch[] orphanBatches = brgBatches;
            batchRendererState = null;
            batchRendererGroup = null;
            brgBatches = Array.Empty<VegetationBrgBatch>();
            if (state == null)
            {
                DisposeBatchRendererResourcesImmediate(orphanRendererGroup, orphanBatches);
                return;
            }

            if (!Application.isPlaying)
            {
                DisposeBatchRendererStateImmediate(state);
                return;
            }

            state.RetireFrame = Time.renderedFrameCount;
            int slot = retiredBatchRendererStateCursor;
            retiredBatchRendererStateCursor = (retiredBatchRendererStateCursor + 1) % retiredBatchRendererStates.Length;
            DisposeBatchRendererStateImmediate(retiredBatchRendererStates[slot]);
            retiredBatchRendererStates[slot] = state;
        }

        private void FlushRetiredBatchRendererResources(bool force)
        {
            int frame = Time.renderedFrameCount;
            for (int i = 0; i < retiredBatchRendererStates.Length; i++)
            {
                BatchRendererState? state = retiredBatchRendererStates[i];
                if (state == null)
                {
                    continue;
                }

                if (!force && frame - state.RetireFrame < BatchRendererRetireFrameDelay)
                {
                    continue;
                }

                DisposeBatchRendererStateImmediate(state);
                retiredBatchRendererStates[i] = null;
            }
        }

        private static void DisposeBatchRendererStateImmediate(BatchRendererState? state)
        {
            if (state == null)
            {
                return;
            }

            DisposeBatchRendererResourcesImmediate(state.RendererGroup, state.Batches);
            state.RendererGroup = null;
            state.Batches = Array.Empty<VegetationBrgBatch>();
            if (state.Handle.IsAllocated)
            {
                state.Handle.Free();
            }
        }

        private static void DisposeBatchRendererResourcesImmediate(
            BatchRendererGroup? rendererGroup,
            VegetationBrgBatch[] batches)
        {
            for (int i = 0; i < batches.Length; i++)
            {
                VegetationBrgBatch batch = batches[i];
                if (!batch.IsValid)
                {
                    continue;
                }

                if (rendererGroup != null)
                {
                    rendererGroup.RemoveBatch(batch.BatchId);
                    rendererGroup.UnregisterMesh(batch.MeshId);
                    rendererGroup.UnregisterMaterial(batch.MaterialId);
                }

                batch.InstanceDataBuffer?.Release();
            }

            rendererGroup?.Dispose();
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
            batchRendererFaulted = false;
            invalidCompiledPacketsLogged = false;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct PackedMatrix
        {
            public PackedMatrix(Matrix4x4 matrix)
            {
                C0X = matrix.m00;
                C0Y = matrix.m10;
                C0Z = matrix.m20;
                C1X = matrix.m01;
                C1Y = matrix.m11;
                C1Z = matrix.m21;
                C2X = matrix.m02;
                C2Y = matrix.m12;
                C2Z = matrix.m22;
                C3X = matrix.m03;
                C3Y = matrix.m13;
                C3Z = matrix.m23;
            }

            public float C0X;
            public float C0Y;
            public float C0Z;
            public float C1X;
            public float C1Y;
            public float C1Z;
            public float C2X;
            public float C2Y;
            public float C2Z;
            public float C3X;
            public float C3Y;
            public float C3Z;
        }

        private sealed class BatchRendererState
        {
            public BatchRendererState(
                BatchRendererSettingsSnapshot settings,
                int[] providerAssetGroupOffsets,
                BatchRendererPageRecord[] pages,
                CellRecord[] cells,
                BatchRendererPacketRecord[][] pagePackets,
                int[][] pageInstanceAssetGroupIndices,
                PacketRange[] pagePacketRanges,
                PacketRange[] cellPacketRanges,
                int[] packetLookupIndices,
                int[] groupTotalInstanceCounts,
                int[][] pageInstanceGroupLocalIndices,
                int[][] pagePacketValidInstanceCounts)
            {
                Settings = settings;
                ProviderAssetGroupOffsets = providerAssetGroupOffsets;
                Pages = pages;
                Cells = cells;
                PagePackets = pagePackets;
                PageInstanceAssetGroupIndices = pageInstanceAssetGroupIndices;
                PagePacketRanges = pagePacketRanges;
                CellPacketRanges = cellPacketRanges;
                PacketLookupIndices = packetLookupIndices;
                GroupTotalInstanceCounts = groupTotalInstanceCounts;
                PageInstanceGroupLocalIndices = pageInstanceGroupLocalIndices;
                PagePacketValidInstanceCounts = pagePacketValidInstanceCounts;
            }

            public BatchRendererSettingsSnapshot Settings;
            public BatchRendererGroup? RendererGroup;
            public GCHandle Handle;
            public VegetationBrgBatch[] Batches = Array.Empty<VegetationBrgBatch>();
            public int RetireFrame;
            public int CullingDiagnosticsLogCount;
            public readonly int[] ProviderAssetGroupOffsets;
            public readonly BatchRendererPageRecord[] Pages;
            public readonly CellRecord[] Cells;
            public readonly BatchRendererPacketRecord[][] PagePackets;
            public readonly int[][] PageInstanceAssetGroupIndices;
            public readonly PacketRange[] PagePacketRanges;
            public readonly PacketRange[] CellPacketRanges;
            public readonly int[] PacketLookupIndices;
            public readonly int[] GroupTotalInstanceCounts;
            public readonly int[][] PageInstanceGroupLocalIndices;
            public readonly int[][] PagePacketValidInstanceCounts;

            public int PageCount => Pages.Length;

            public int CellCount => Cells.Length;

            public int GroupCount => GroupTotalInstanceCounts.Length;

            public int PacketLookupCount => PacketLookupIndices.Length;

            public Bounds CalculateGlobalBounds()
            {
                if (Pages.Length == 0)
                {
                    return new Bounds(Vector3.zero, Vector3.one);
                }

                Bounds bounds = Pages[0].WorldBounds;
                for (int i = 1; i < Pages.Length; i++)
                {
                    bounds.Encapsulate(Pages[i].WorldBounds);
                }

                return bounds;
            }
        }

        private readonly struct BatchRendererSettingsSnapshot
        {
            private BatchRendererSettingsSnapshot(
                VegetationShadowMode shadowMode,
                float nearDetailDistance,
                int colorWorkBudget,
                int shadowWorkBudget,
                int maxVisiblePacketInstances,
                bool enableDiagnostics,
                GraphicsDeviceType graphicsApi)
            {
                ShadowMode = shadowMode;
                NearDetailDistance = nearDetailDistance;
                ColorWorkBudget = colorWorkBudget;
                ShadowWorkBudget = shadowWorkBudget;
                MaxVisiblePacketInstances = maxVisiblePacketInstances;
                EnableDiagnostics = enableDiagnostics;
                GraphicsApi = graphicsApi;
            }

            public VegetationShadowMode ShadowMode { get; }

            public float NearDetailDistance { get; }

            public int ColorWorkBudget { get; }

            public int ShadowWorkBudget { get; }

            public int MaxVisiblePacketInstances { get; }

            public bool EnableDiagnostics { get; }

            public GraphicsDeviceType GraphicsApi { get; }

            public bool ShouldLogBatchRendererDiagnostics =>
                EnableDiagnostics;

            public bool UsesBatchRendererLightCulling =>
                ShadowMode == VegetationShadowMode.CheapTree;

            public static BatchRendererSettingsSnapshot From(VegetationFoliageFeatureSettings settings)
            {
                return new BatchRendererSettingsSnapshot(
                    settings.ShadowMode,
                    settings.NearDetailDistance,
                    settings.ColorWorkBudget,
                    settings.ShadowWorkBudget,
                    settings.MaxVisiblePacketInstances,
                    settings.EnableDiagnostics,
                    SystemInfo.graphicsDeviceType);
            }

            public int GetWorkBudget(VegetationRenderPassMode passMode)
            {
                return passMode == VegetationRenderPassMode.Shadow
                    ? Mathf.Max(1, ShadowWorkBudget)
                    : Mathf.Max(1, ColorWorkBudget);
            }
        }

        private readonly struct BatchRendererPageRecord
        {
            public BatchRendererPageRecord(
                int providerIndex,
                int pageRecordIndex,
                Bounds worldBounds,
                int firstCellRecord,
                int cellCount)
            {
                ProviderIndex = providerIndex;
                PageRecordIndex = pageRecordIndex;
                WorldBounds = worldBounds;
                FirstCellRecord = firstCellRecord;
                CellCount = cellCount;
            }

            public int ProviderIndex { get; }

            public int PageRecordIndex { get; }

            public Bounds WorldBounds { get; }

            public int FirstCellRecord { get; }

            public int CellCount { get; }
        }

        private readonly struct BatchRendererPacketRecord
        {
            public BatchRendererPacketRecord(FoliageRepresentationPacket packet)
            {
                RepresentationKind = packet.RepresentationKind;
                CellIndex = packet.CellIndex;
                AssetGroupIndex = packet.AssetGroupIndex;
                FirstInstance = packet.FirstInstance;
                InstanceCount = packet.InstanceCount;
                WorldBounds = packet.WorldBounds;
                WorkCost = packet.WorkCost;
                Residency = packet.Residency;
                ShadowMode = packet.ShadowMode;
                ShadowPacketIndex = packet.ShadowPacketIndex;
            }

            public FoliageRepresentationKind RepresentationKind { get; }

            public int CellIndex { get; }

            public int AssetGroupIndex { get; }

            public int FirstInstance { get; }

            public int InstanceCount { get; }

            public Bounds WorldBounds { get; }

            public int WorkCost { get; }

            public FoliagePacketResidency Residency { get; }

            public FoliageShadowPacketMode ShadowMode { get; }

            public int ShadowPacketIndex { get; }

            public bool IsValid => FirstInstance >= 0 && InstanceCount > 0;
        }

        private readonly struct VegetationBrgBatch
        {
            public VegetationBrgBatch(
                int groupIndex,
                string debugLabel,
                string meshName,
                string materialName,
                string shaderName,
                int forwardPassIndex,
                int depthPassIndex,
                int shadowPassIndex,
                int objectToWorldByteAddress,
                int worldToObjectByteAddress,
                int packedLeafTintByteAddress,
                int windByteAddress,
                int bufferBytes,
                BatchID batchId,
                BatchMeshID meshId,
                BatchMaterialID materialId,
                GraphicsBuffer instanceDataBuffer,
                int instanceCount)
            {
                GroupIndex = groupIndex;
                DebugLabel = debugLabel;
                MeshName = meshName;
                MaterialName = materialName;
                ShaderName = shaderName;
                ForwardPassIndex = forwardPassIndex;
                DepthPassIndex = depthPassIndex;
                ShadowPassIndex = shadowPassIndex;
                ObjectToWorldByteAddress = objectToWorldByteAddress;
                WorldToObjectByteAddress = worldToObjectByteAddress;
                PackedLeafTintByteAddress = packedLeafTintByteAddress;
                WindByteAddress = windByteAddress;
                BufferBytes = bufferBytes;
                BatchId = batchId;
                MeshId = meshId;
                MaterialId = materialId;
                InstanceDataBuffer = instanceDataBuffer;
                InstanceCount = instanceCount;
            }

            public int GroupIndex { get; }

            public string? DebugLabel { get; }

            public string? MeshName { get; }

            public string? MaterialName { get; }

            public string? ShaderName { get; }

            public int ForwardPassIndex { get; }

            public int DepthPassIndex { get; }

            public int ShadowPassIndex { get; }

            public int ObjectToWorldByteAddress { get; }

            public int WorldToObjectByteAddress { get; }

            public int PackedLeafTintByteAddress { get; }

            public int WindByteAddress { get; }

            public int BufferBytes { get; }

            public BatchID BatchId { get; }

            public BatchMeshID MeshId { get; }

            public BatchMaterialID MaterialId { get; }

            public GraphicsBuffer? InstanceDataBuffer { get; }

            public int InstanceCount { get; }

            public bool IsValid => InstanceCount > 0 && InstanceDataBuffer != null;
        }

        private sealed class BatchCullingScratch
        {
            public bool[] VisiblePageMask = Array.Empty<bool>();
            public bool[] VisibleCellMask = Array.Empty<bool>();
            public int[] VisiblePageFrustumMasks = Array.Empty<int>();
            public int[] VisibleCellFrustumMasks = Array.Empty<int>();
            public int[] GroupInstanceCounts = Array.Empty<int>();
            public int[] GroupFrustumMasks = Array.Empty<int>();
            public int[] GroupStartInstances = Array.Empty<int>();
            public int[] GroupWriteOffsets = Array.Empty<int>();
            public CellCandidate[] CellCandidates = Array.Empty<CellCandidate>();
            public PacketSelection[] SelectedPackets = Array.Empty<PacketSelection>();
            public int[] ActiveGroupIndices = Array.Empty<int>();
            public Plane[] CullingPlanes = Array.Empty<Plane>();
            public int[] SplitPlaneOffsets = Array.Empty<int>();
            public int[] SplitPlaneCounts = Array.Empty<int>();
            public int CellCandidateCount;
            public int SelectedPacketCount;
            public int ActiveGroupIndexCount;
            public int PreparedInstanceCount;
            public int PreparedPacketCount;
            public int PreparedNearDetailPacketCount;
            public int PreparedTreeL0PacketCount;
            public int PreparedTreeL1PacketCount;
            public int PreparedTreeL2PacketCount;
            public int PreparedHlodPacketCount;
            public int PreparedShadowPacketCount;
            public int PreparedActiveGroupCount;
            public int PreparedFrustumMask;
            private int pageCount;
            private int cellCount;
            private int groupCount;

            public void ResetVisibility(
                int requiredPageCount,
                int requiredCellCount,
                int requiredGroupCount,
                int requiredPacketSelectionCount,
                int requiredSplitCount,
                int requiredPlaneCount)
            {
                EnsureCapacity(
                    requiredPageCount,
                    requiredCellCount,
                    requiredGroupCount,
                    requiredPacketSelectionCount,
                    requiredSplitCount,
                    requiredPlaneCount);
                if (pageCount > 0)
                {
                    Array.Clear(VisiblePageMask, 0, pageCount);
                    Array.Clear(VisiblePageFrustumMasks, 0, pageCount);
                }

                if (cellCount > 0)
                {
                    Array.Clear(VisibleCellMask, 0, cellCount);
                    Array.Clear(VisibleCellFrustumMasks, 0, cellCount);
                }

                if (groupCount > 0)
                {
                    Array.Clear(GroupInstanceCounts, 0, groupCount);
                    Array.Clear(GroupFrustumMasks, 0, groupCount);
                    Array.Clear(GroupStartInstances, 0, groupCount);
                    Array.Clear(GroupWriteOffsets, 0, groupCount);
                }

                pageCount = requiredPageCount;
                cellCount = requiredCellCount;
                groupCount = requiredGroupCount;
                ResetSelection(requiredGroupCount);
            }

            public void ResetSelection(int requiredGroupCount)
            {
                if (groupCount > 0)
                {
                    Array.Clear(GroupInstanceCounts, 0, groupCount);
                    Array.Clear(GroupFrustumMasks, 0, groupCount);
                    Array.Clear(GroupStartInstances, 0, groupCount);
                    Array.Clear(GroupWriteOffsets, 0, groupCount);
                }

                groupCount = requiredGroupCount;
                CellCandidateCount = 0;
                SelectedPacketCount = 0;
                ActiveGroupIndexCount = 0;
                PreparedInstanceCount = 0;
                PreparedPacketCount = 0;
                PreparedNearDetailPacketCount = 0;
                PreparedTreeL0PacketCount = 0;
                PreparedTreeL1PacketCount = 0;
                PreparedTreeL2PacketCount = 0;
                PreparedHlodPacketCount = 0;
                PreparedShadowPacketCount = 0;
                PreparedActiveGroupCount = 0;
                PreparedFrustumMask = 0;
            }

            public void MarkPageVisible(int pageRecordIndex)
            {
                if (pageRecordIndex < 0 || pageRecordIndex >= pageCount || VisiblePageMask[pageRecordIndex])
                {
                    return;
                }

                VisiblePageMask[pageRecordIndex] = true;
            }

            public void MarkCellVisible(int cellRecordIndex, int pageRecordIndex)
            {
                if (cellRecordIndex < 0 || cellRecordIndex >= cellCount || VisibleCellMask[cellRecordIndex])
                {
                    return;
                }

                VisibleCellMask[cellRecordIndex] = true;
                MarkPageVisible(pageRecordIndex);
            }

            public void AddCellCandidate(CellCandidate candidate)
            {
                if (CellCandidateCount >= CellCandidates.Length)
                {
                    Array.Resize(
                        ref CellCandidates,
                        Mathf.NextPowerOfTwo(Mathf.Max(1, CellCandidateCount + 1)));
                }

                CellCandidates[CellCandidateCount++] = candidate;
            }

            public void AddSelectedPacket(PacketSelection selection)
            {
                if (SelectedPacketCount >= SelectedPackets.Length)
                {
                    Array.Resize(
                        ref SelectedPackets,
                        Mathf.NextPowerOfTwo(Mathf.Max(1, SelectedPacketCount + 1)));
                }

                SelectedPackets[SelectedPacketCount++] = selection;
            }

            public void AddActiveGroupIndex(int groupIndex)
            {
                if (ActiveGroupIndexCount >= ActiveGroupIndices.Length)
                {
                    Array.Resize(
                        ref ActiveGroupIndices,
                        Mathf.NextPowerOfTwo(Mathf.Max(1, ActiveGroupIndexCount + 1)));
                }

                ActiveGroupIndices[ActiveGroupIndexCount++] = groupIndex;
            }

            public void IncrementNearDetailTierCounter(FoliageRepresentationKind representationKind)
            {
                if (representationKind == FoliageRepresentationKind.TreeL0)
                {
                    PreparedTreeL0PacketCount++;
                    return;
                }

                if (representationKind == FoliageRepresentationKind.TreeL1)
                {
                    PreparedTreeL1PacketCount++;
                    return;
                }

                if (representationKind == FoliageRepresentationKind.TreeL2)
                {
                    PreparedTreeL2PacketCount++;
                }
            }

            private void EnsureCapacity(
                int requiredPageCount,
                int requiredCellCount,
                int requiredGroupCount,
                int requiredPacketSelectionCount,
                int requiredSplitCount,
                int requiredPlaneCount)
            {
                EnsureArray(ref VisiblePageMask, requiredPageCount);
                EnsureArray(ref VisibleCellMask, requiredCellCount);
                EnsureArray(ref VisiblePageFrustumMasks, requiredPageCount);
                EnsureArray(ref VisibleCellFrustumMasks, requiredCellCount);
                EnsureArray(ref GroupInstanceCounts, requiredGroupCount);
                EnsureArray(ref GroupFrustumMasks, requiredGroupCount);
                EnsureArray(ref GroupStartInstances, requiredGroupCount);
                EnsureArray(ref GroupWriteOffsets, requiredGroupCount);
                EnsureArray(ref CellCandidates, requiredCellCount);
                EnsureArray(ref SelectedPackets, requiredPacketSelectionCount);
                EnsureArray(ref ActiveGroupIndices, requiredGroupCount);
                EnsureArray(ref CullingPlanes, requiredPlaneCount);
                EnsureArray(ref SplitPlaneOffsets, requiredSplitCount);
                EnsureArray(ref SplitPlaneCounts, requiredSplitCount);
            }

            private static void EnsureArray<T>(ref T[] array, int requiredLength)
            {
                if (array.Length >= requiredLength)
                {
                    return;
                }

                array = new T[Mathf.NextPowerOfTwo(Mathf.Max(1, requiredLength))];
            }
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
            public PacketSelection(int pageRecordIndex, int packetIndex, int instanceCount)
            {
                PageRecordIndex = pageRecordIndex;
                PacketIndex = packetIndex;
                InstanceCount = instanceCount;
            }

            public int PageRecordIndex { get; }

            public int PacketIndex { get; }

            public int InstanceCount { get; }
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
