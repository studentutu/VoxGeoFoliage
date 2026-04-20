# Vegetation Runtime Architecture

Purpose: exact developer-facing bake and runtime pipeline authority.

This doc is code-first. It describes what the editor/runtime path actually does today, where the ownership and memory failures are, and what the replacement runtime shape must be. Old long prose docs are not authority.

## Keep This Doc Set

- `Packages/com.voxgeofol.vegetation/README.md`
  package consumer contract and shipped terminology
- `DetailedDocs/VegetationRuntimeArchitecture.md`
  exact bake + runtime pipeline, ownership, and memory authority
- `DetailedDocs/Milestone1.md`
  shipped baseline summary only
- `DetailedDocs/Milestone2.md`
  current open work only

Everything else under `DetailedDocs/` is archive, redirect, or historical context only.

## Read This Correctly

- `Current code` means shipped editor/runtime behavior today.
- `Required target` means the design that must replace the broken ownership model.
- If this doc and an old design note disagree, trust this doc and the runtime/editor code.

## Requirements

1. Unity `6000.3` or newer.
2. URP `17.3.0` or newer-compatible project setup.
3. Compute-shader and indirect-draw support on the target hardware and graphics API.
4. `VegetationRendererFeature` added to the active URP renderer. Preview via scene view or Render graph viewer.
5. `VegetationFoliageFeatureSettings.ClassifyShader` assigned to `VegetationClassify.compute`.
6. Opaque URP SRP-compatible shaders only.
7. Runtime vegetation shaders compatible with the package indirect-instance contract. The bundled package shaders now include main-light shadow attenuation plus a `ShadowCaster` pass.

## 1. Current Bake Pipeline

### 1.1 Branch Prototype Bake

Hard separation of authoring phase and runtime path. Runtime can freely use authoring phase data.

```text
BranchPrototypeSO
  payload in:
    foliageMesh
    woodMesh
    shellBakeSettings
    canopy triangle budgets
    generatedCanopyShellsRelativeFolder
-> CanopyShellGenerator.BakeCanopyShells()
  temp payload:
    MeshVoxelizerHierarchyBuilder.BuildHierarchies(...)
      -> hierarchyL0[]
      -> hierarchyL1[]
      -> hierarchyL2[]
    each hierarchy node carries:
      localBounds
      depth
      firstChildIndex
      childMask
      shellL0Mesh / shellL1Mesh / shellL2Mesh
    shell level selections:
      selected node meshes
      leaf-frontier triangle counts
    generated wood tier candidates:
      shellL1WoodCandidate
      shellL2WoodCandidate
    generated canopy tier meshes:
      branchL1CanopyMesh
      branchL2CanopyMesh
      branchL3CanopyMesh
-> GeneratedMeshAssetUtility.PersistGeneratedMesh(...)
  payload out on BranchPrototypeSO:
    branchL1WoodMesh = source woodMesh
    branchL2WoodMesh = persisted reduced wood
    branchL3WoodMesh = persisted reduced wood
    branchL1CanopyMesh
    branchL2CanopyMesh
    branchL3CanopyMesh
```

Important current contract:

- Temporary voxel hierarchies exist only inside `CanopyShellGenerator` while selecting the best `L1/L2/L3` canopy meshes.
- `BranchPrototypeSO` persists only the runtime split-tier mesh chain: `branchL1/2/3CanopyMesh` and `branchL1/2/3WoodMesh`.
- Runtime and editor preview do not traverse per-node canopy shell hierarchies anymore.
- The sample assets were also reduced to the same single-mesh-per-tier contract instead of shipping hundreds of stale shell-node meshes.

### 1.2 Tree-Wide Bake

```text
TreeBlueprintSO
  payload in:
    trunkMesh
    branches[] {
      prototype
      localPosition
      localRotation
      scale
    }
    impostorSettings
    generatedImpostorMeshesRelativeFolder
-> TrunkL3MeshGenerator.BakeTrunkL3Mesh()
  temp payload:
    SelectBestVoxelMeshCandidate(
      source = trunkMesh,
      clipBounds = trunkMesh.bounds)
  payload out:
    trunkL3Mesh
-> ImpostorMeshGenerator.BakeTreeL3Mesh()
  temp payload:
    CreateCombinedTreeSpaceMesh()
      = trunkMesh
      + each branch placement prototype.WoodMesh
      + each branch placement prototype.FoliageMesh
    SelectBestVoxelMeshCandidate(
      source = combined tree mesh,
      clipBounds = blueprint.TreeBounds)
  payload out:
    treeL3Mesh
-> ImpostorMeshGenerator.BakeImpostorMesh()
  temp payload:
    CreateCombinedTreeSpaceMesh()
      = same source composition as TreeL3
    SelectBestVoxelMeshCandidate(
      source = combined tree mesh,
      target triangle budget = 200)
  payload out:
    impostorMesh
-> SaveAuthoringChanges()
  side effects:
    EditorUtility.SetDirty(authoring)
    EditorUtility.SetDirty(blueprint)
    AssetDatabase.SaveAssets()
    AssetDatabase.Refresh()
```

Important current contract:

- `treeL3Mesh` and `impostorMesh` are baked from trunk + original placed branch source meshes.
- They do not consume runtime branch tier meshes.
- `treeL3Mesh` is the intended non-far whole-tree floor.
- `impostorMesh` is the far-only whole-tree coarse mesh.

### 1.3 Container Authoring Fill

```text
VegetationRuntimeContainer hierarchy
-> VegetationTreeAuthoringEditorUtility.FillRuntimeContainerAuthorings()
  payload out:
    registeredAuthorings[] = active VegetationTreeAuthoring references
  side effect:
    if runtime owner already exists -> RefreshRuntimeRegistration()
```

Important current contract:

- Container ownership is explicit and serialized.
- Nested child containers claim their own descendants.
- Runtime registration is snapshot-based. Transform or authoring changes do not live-sync until `RefreshRuntimeRegistration()`.

## 2. Current Runtime Registration Pipeline

```text
VegetationRuntimeContainer.registeredAuthorings[]
-> BuildRuntimeTreeAuthorings()
  payload out:
    VegetationTreeAuthoringRuntime[] {
      containerRuntimeHash
      treeHash
      debugName
      blueprint
      localToWorld
      isActive
      source authoring ref
    }
-> ReplaceRuntimeOwner(...)
  payload:
    new AuthoringContainerRuntime(
      containerId
      providerKind
      debugName
      diagnosticsContext
      renderLayer
      gridOrigin
      cellSize
      runtimeBudget
      authorings)
-> AuthoringContainerRuntime.Activate()
-> AuthoringContainerRuntime.RefreshRuntimeRegistration()
  side effects:
    ResetAuthoringRuntimeIndices()
    registry = VegetationRuntimeRegistryBuilder.Build(authorings, runtimeBudget.MaxRegisteredDrawSlots)
    indirectRenderer = new VegetationIndirectRenderer(registry, renderLayer)
    reset cameraGpuDecisionPipeline
    reset frustumGpuDecisionPipeline
```

### 2.1 Registry Builder Flattening

```text
VegetationRuntimeRegistryBuilder.Build(authorings)
-> RegisterLodProfile()
  payload out:
    LodProfiles[] {
      l0Distance
      l1Distance
      l2Distance
      impostorDistance
      absoluteCullDistance
    }
-> RegisterBlueprint()
  payload out:
    TreeBlueprints[] {
      lodProfileIndex
      branchPlacementStartIndex
      branchPlacementCount
      trunkFullDrawSlot
      trunkL3DrawSlot
      treeL3DrawSlot
      impostorDrawSlot
      treeL3WorkCost
      impostorWorkCost
      expandedTierCostL2
      expandedTierCostL1
      expandedTierCostL0
    }
-> RegisterPrototype()
  payload out:
    BranchPrototypes[] {
      woodDrawSlotL0
      foliageDrawSlotL0
      woodDrawSlotL1
      canopyDrawSlotL1
      woodDrawSlotL2
      canopyDrawSlotL2
      woodDrawSlotL3
      canopyDrawSlotL3
      packedLeafTint
      localBoundsCenter
      localBoundsExtents
    }
-> RegisterDrawSlot(mesh, material, materialKind)
  payload out:
    DrawSlots[] keyed by:
      Mesh + Material + MaterialKind
-> Build per-blueprint placements
  payload out:
    BlueprintBranchPlacements[] {
      localToTree
      treeToLocal
      prototypeIndex
      localBoundsCenter
      localBoundsExtents
      boundingSphereRadius
    }
-> Build per-tree instances
  payload out:
    TreeInstances[] {
      localToWorld
      worldToObject
      worldBounds
      trunkFullWorldBounds
      trunkL3WorldBounds
      treeL3WorldBounds
      impostorWorldBounds
      sphereCenterWorld
      boundingSphereRadius
      blueprintIndex
      cellIndex
      uploadInstanceData
    }
-> VegetationSpatialGrid.Build(...)
  payload out:
    SpatialGrid
-> BuildDrawSlotConservativeBounds()
  payload out:
    DrawSlotConservativeWorldBounds[]
-> VegetationRuntimeRegistry
  payload out:
    DrawSlots[]
    DrawSlotConservativeWorldBounds[]
    LodProfiles[]
    TreeBlueprints[]
    BlueprintBranchPlacements[]
    BranchPrototypes[]
    TreeInstances[]
    SpatialGrid
```

### 2.2 Submission Surface Build

```text
VegetationRuntimeRegistry
-> VegetationIndirectRenderer(...)
  payload out:
    SlotResources[] {
      drawSlot
      conservativeWorldBounds
      sharedArgsBufferOffset
      forward pass index
      depth pass index
      shadow pass index
      MaterialPropertyBlock {
        _VegetationSlotIndex
        shared buffers bound later
      }
    }
```

Important current contract:

- One registered draw slot is one exact `Mesh + Material + MaterialKind`.
- Submission surface is `PreparedViewHandle.ActiveSlotIndices`, not raw registry slot metadata.
- Current shipped active-slot source is the latest completed async non-zero emitted-slot subset, with registered-slot fallback until the emitted-slot readback warms.
- `VegetationIndirectRenderer` does not own its own GPU instance/args buffers. It binds explicit prepared-view buffers and does not let camera/shadow submission overwrite each other.

## 3. Current Camera Color/Depth Pipeline

### 3.1 URP Setup

```text
Camera
-> VegetationRendererFeature.AddRenderPasses()
  payload in:
    camera
    classifyShader
    diagnostics flag
    shadow settings
-> DepthPass.Setup()
-> ColorPass.Setup()
  payload stored per pass:
    camera
    classifyShader
    diagnosticsEnabled
    containerSnapshot[]
```

Important current contract:

- Depth and color both call `PrepareViewForCamera(camera, classifyShader, diagnosticsEnabled)`.
- The second call for the same camera and render frame reuses the cached prepared-view handle.

### 3.2 Per-Container Prepare

```text
AuthoringContainerRuntime.PrepareViewForCamera(camera, classifyShader, diagnostics)
  cache key:
    lastPreparedRenderFrame + lastPreparedCameraInstanceId
-> EnsureRuntimeRegistration()
-> GeometryUtility.CalculateFrustumPlanes(camera)
  payload out:
    frustumPlanes[6]
-> TryEnsureGpuDecisionPipeline(useExplicitFrustumPipeline = false)
  payload out:
    cameraGpuDecisionPipeline
-> PrepareGpuResidentView(
     observerWorldPosition = camera.transform.position,
     frustumPlanes,
     classifyShader,
     diagnosticsEnabled,
      allowExpandedTreePromotion = true,
      useExplicitFrustumPipeline = false)
  payload out:
    VegetationIndirectRenderer.PreparedViewHandle
```

### 3.3 GPU Decision Chain

```text
VegetationGpuDecisionPipeline.PrepareResidentFrame(
  cameraWorldPosition,
  frustumPlanes,
  allowExpandedTreePromotion,
  captureTelemetry)
-> UploadDynamicFrameData
  payload in constants:
    _CameraWorldPosition
    _FrustumPlanes[6]
    _CellCount
    _TreeCount
    _LodProfileCount
    _BlueprintCount
    _PlacementCount
    _PrototypeCount
    _DrawSlotCount
    _VisibleInstanceCapacity
    _ExpandedBranchWorkItemCapacity
    _ApproxWorkUnitCapacity
    _AllowExpandedTierPromotion
    _PriorityRingCount
-> ResetFrameState
  payload out:
    ExpandedBranchWorkItemCount = 0
    ExpandedBranchDispatchArgs = {0, 1, 1}
    FrameStats[0..12] = 0
-> ClassifyCells
  payload in:
    Cells[]
  payload out:
    CellVisibility[]
-> ClassifyTrees
  payload in:
    Trees[]
    Blueprints[]
    LodProfiles[]
    CellVisibility[]
  payload out:
    TreeVisibility[] {
      treeDistance
      priorityRing
      desiredTier
      acceptedTier = culled
      acceptedTierCost = 0
      visible
    }
-> AcceptTreeTiers
  payload in:
    TreeVisibility[]
    Trees[]
    Blueprints[]
  temp payload:
    PriorityRingTreeCounts[]
    PriorityRingOffsets[]
    PriorityOrderedVisibleTreeIndices[]
  payload out:
    TreeVisibility[].acceptedTier
    TreeVisibility[].acceptedTierCost
    FrameStats[] {
      visibleTrees
      acceptedTreeL3
      promotedL2
      promotedL1
      promotedL0
      rejectedPromotions
      expandedTrees
      expandedBranchWorkItems
      acceptedTierCostUsage
      baselineTreeL3Failures
      visibleInstanceCapHits
      expandedBranchWorkItemCapHits
      emittedVisibleInstances
    }
  actual current order:
    1. camera/color path pre-validates visible non-far `TreeL3` baseline fit and fault-disables the container explicitly if it cannot fit
    2. nearest-first promotion TreeL3 -> L2 -> L1 -> L0 if enabled
    3. far trees try Impostor last
-> ResetSlotCounts
  payload out:
    SlotRequestedInstanceCounts[]
    SlotEmittedInstanceCounts[]
    SlotPackedStarts[]
-> CountTrees
  payload out:
    SlotRequestedInstanceCounts[accepted tree draw slot]++
-> GenerateExpandedBranchWorkItems
  only when expanded promotion is enabled
  payload out:
    ExpandedBranchWorkItems[] {
      treeIndex
      branchInstanceIndex
      branchPlacementIndex
      runtimeTier
    }
    ExpandedBranchWorkItemCount
-> CountExpandedBranches
  dispatch item count:
    actual generated expanded-branch work-item count
  payload out:
    SlotRequestedInstanceCounts[branch wood slot]++
    SlotRequestedInstanceCounts[branch canopy slot]++
-> ClampRequestedSlotCounts
  payload out:
    SlotRequestedInstanceCounts[] clamped in slot order        <-- current slot-order bias
-> BuildSlotStarts
  payload out:
    SlotPackedStarts[]
-> EmitTrees
  payload out:
    VisibleInstances[]
    SlotEmittedInstanceCounts[]
-> EmitExpandedBranches
  dispatch item count:
    actual generated expanded-branch work-item count
  payload out:
    VisibleInstances[]
    SlotEmittedInstanceCounts[]
-> FinalizeIndirectArgs
  payload out per slot:
    IndirectArgs {
      indexCountPerInstance
      instanceCount
      startIndexLocation
      baseVertexLocation
      startInstanceLocation = 0
    }
-> SchedulePreparedFrameReadbacks
  payload out:
    latest prepared-frame telemetry cache
    latest active-slot index cache
```

### 3.4 Bind And Submit

```text
PrepareResidentFrame(...)
-> VegetationIndirectRenderer.BindGpuResidentFrame(...)
  payload out:
    PreparedViewHandle {
      instanceBuffer
      argsBuffer
      slotPackedStartsBuffer
      activeSlotIndices = latest completed non-zero emitted slots
      fallback = all registered slots until async emitted-slot readback warms
    }
-> VegetationIndirectRenderer.Render(preparedViewHandle, passMode = Depth | Color)
  for each active slot:
    resolve material + pass index
    DrawMeshInstancedIndirect(
      mesh,
      material,
      argsBuffer,
      sharedArgsBufferOffset,
      shaderPass,
      drawProperties)
```

Important current contract:

- Submission iterates the prepared-view handle's active-slot indices, not the raw registered slot table.
- Current shipped submission uses the latest completed async non-zero emitted-slot subset, with registered-slot fallback during async warm-up.
- Zero-instance draws are only tolerated during registered-slot fallback.
- The renderer renders explicit prepared-view handles instead of one mutable "currently bound prepared frame" surface.

## 4. Current Shadow Pipeline

```text
VegetationRendererFeature.RecordShadowRenderGraph()
-> DrawMainLightShadowAtlas()
  payload in:
    main directional light
    culling results
    cascade count
    main shadow texture
-> for each cascade:
  -> ShadowUtils.ExtractDirectionalLightMatrix(...)
    payload out:
      ShadowSliceData {
        viewMatrix
        projectionMatrix
        resolution
        offsetX
        offsetY
      }
  -> GeometryUtility.CalculateFrustumPlanes(
       shadowSliceData.projectionMatrix * shadowSliceData.viewMatrix)
    payload out:
      shadowFrustumPlanes[6]
  -> for each container:
    -> AuthoringContainerRuntime.PrepareViewForFrustum(
         observerWorldPosition = cameraData.worldSpaceCameraPos,
         frustumPlanes = shadowFrustumPlanes,
         classifyShader,
         diagnosticsEnabled,
         allowExpandedTreePromotion = shadow setting)
      -> TryEnsureGpuDecisionPipeline(useExplicitFrustumPipeline = true)
        payload out:
          frustumGpuDecisionPipeline
      -> same PrepareResidentFrame compute chain as camera path
      -> VegetationIndirectRenderer.BindGpuResidentFrame(...)
         payload out:
           PreparedViewHandle
    -> VegetationIndirectRenderer.Render(preparedViewHandle, passMode = Shadow)
      for each active slot:
        resolve ShadowCaster pass
        DrawMeshInstancedIndirect(...)
```

Important current contract:

- Shadow preparation owns a second full `VegetationGpuDecisionPipeline` per container once used.
- Shadow and camera still share one `VegetationIndirectRenderer`, but render calls no longer share one renderer-global mutable bound frame.
- Camera, depth, and shadow now pass explicit prepared-view handles through submission.

## 5. Current Resident Memory Surfaces

### 5.1 CPU Resident Per Container

```text
AuthoringContainerRuntime
  -> VegetationRuntimeRegistry CPU arrays
  -> VegetationIndirectRenderer SlotResources[]
  -> camera/frustum pipeline references
```

### 5.2 GPU Resident Per VegetationGpuDecisionPipeline

```text
cellBuffer[max(1, cellCount)]
lodBuffer[max(1, lodProfileCount)]
blueprintBuffer[max(1, blueprintCount)]
placementBuffer[max(1, placementCount)]
prototypeBuffer[max(1, prototypeCount)]
treeBuffer[max(1, treeCount)]
treeVisibilityBuffer[max(1, treeCount)]
expandedBranchWorkItemBuffer[expandedBranchWorkItemCapacity]
expandedBranchWorkItemCountBuffer[1]
expandedBranchDispatchArgsBuffer[3]
frameStatsBuffer[13]
priorityRingTreeCountBuffer[priorityRingCount]
priorityRingOffsetsBuffer[priorityRingCount]
priorityOrderedVisibleTreeIndicesBuffer[max(1, treeCount)]
slotMetadataBuffer[max(1, drawSlotCount)]
slotRequestedInstanceCountBuffer[max(1, drawSlotCount)]
slotEmittedInstanceCountBuffer[max(1, drawSlotCount)]
slotPackedStartsBuffer[max(1, drawSlotCount)]
cellVisibilityBuffer[max(1, cellCount)]
residentInstanceBuffer[visibleInstanceCapacity]
residentArgsBuffer[max(1, drawSlotCount)]
```

### 5.3 Current Multipliers

```text
one active container
  -> one registry
  -> one indirect renderer
  -> one cameraGpuDecisionPipeline
  -> one frustumGpuDecisionPipeline once shadow/explicit frustum is used

worst current steady state
  -> 2 x full VegetationGpuDecisionPipeline GPU residency per container
  -> 1 x shared indirect renderer per container with explicit prepared-view handles
```

### 5.4 Shipped Budget Split

```text
old alias:
  maxVisibleInstanceCapacity drove:
    visible instance buffer size
    expanded branch work-item capacity
    accepted tier work budget
    branch count dispatch size
    branch emit dispatch size

current shipped split:
  color/shadow visible-instance budgets drive resident instance memory
  color/shadow expanded-work-item budgets drive branch queue capacity
  color/shadow approx-work-unit budgets drive accepted-content cost
  registered draw-slot cap bounds registry slot metadata
  branch count/emit dispatch now uses GPU-built indirect dispatch args from actual generated work count
```

The alias is gone. The remaining problem is duplicated per-view residency, not one integer pretending to mean five different things.

## 6. Current Runtime Weak Points

- Camera and shadow no longer mutate one shared submission surface, but they still pay for separate full GPU pipelines.
- Slot clamping is slot-order biased.
- Prepared-frame telemetry and active-slot submission are latest async readback snapshots, so the first prepared frames can fall back to registered slots and reported counts can lag the frame being rendered.
- Memory scales with container count and with camera plus frustum pipeline duplication.
- Shadow still defaults to the same budget shape as color unless explicitly overridden.

## 7. Required Target Design

### 7.1 Ownership Split

```text
RefreshRuntimeRegistration()
-> VegetationContainerStaticState
  owns:
    immutable CPU registry
    immutable GPU static buffers
    slot metadata
    conservative slot bounds

PrepareView(camera | shadow cascade)
-> VegetationPreparedViewPool.Acquire()
-> VegetationPreparedViewScratch
  owns:
    cell visibility
    tree visibility
    priority ordering
    branch work queue
    slot requested counts
    slot packed starts
    visible instances
    indirect args
-> VegetationPreparedViewHandle
  carries:
    owner
    version
    instance buffer
    args buffer
    slot starts buffer
    pass/view kind

Render(handle)
-> bind handle-owned buffers
-> submit without touching unrelated prepared views
-> release handle after use
```

Required rule:

- `VegetationIndirectRenderer` must stop owning one mutable bound frame for all consumers.
- Camera and shadow need explicit prepared-view handles, not "whatever was bound last".

### 7.2 Budget Split

```text
VegetationRuntimeBudget
  ColorMaxVisibleInstances
  ColorMaxExpandedBranchWorkItems
  ColorMaxApproxWorkUnits
  ShadowMaxVisibleInstances
  ShadowMaxExpandedBranchWorkItems
  ShadowMaxApproxWorkUnits
  MaxRegisteredDrawSlots
```

Meaning:

- visible instances = memory cap
- expanded branch work items = queue cap
- approx work units = quality/perf cap
- registered draw slots = submission surface cap

### 7.3 Required Prepare Pipeline

```text
PrepareView(staticState, observer, frustum, budget)
-> Reset transient view scratch
-> ClassifyCells
-> ClassifyTrees
-> AcceptBaselineTreeL3OrFail()
  rule:
    visible non-far color trees must fit their baseline or the container is invalid
-> PromoteNearest(TreeL3 -> L2 -> L1 -> L0)
  rule:
    uses work-unit budget, not visible-instance count
-> GenerateExpandedBranchWorkItems
  rule:
    bounded by expanded-work-item budget
-> CountTrees
-> CountExpandedBranches
  dispatch count:
    actual generated work-item count
-> BuildSlotStarts
-> EmitTrees
-> EmitExpandedBranches
  dispatch count:
    actual generated work-item count
-> FinalizeIndirectArgs
-> return prepared-view handle
```

### 7.4 Required Shadow Policy

```text
default shadow policy
  far      -> Impostor
  non-far  -> TreeL3
  branch promotion -> off

first optional upgrade
  allow only L1/L0 in shadows

do not default to
  L2 in shadows
  full color-equivalent promotion in shadows
```

Reason:

- Shadow is secondary.
- Current runtime already overpays for it.
- Cheap shadow policy only works if shadow and color no longer fight over one shared submission state.

## 8. Non-Negotiable Invariants

- Visible non-far color trees must never disappear silently. Minimum accepted representation is `TreeL3`.
- `Impostor` stays far-only.
- Promotion is nearest-first.
- Branch work exists only for trees already accepted above `TreeL3`.
- Slot order must not decide survival.
- Shadow can be cheaper than color, but it must have explicit ownership.

## 9. Immediate Implementation Order

Completed:

1. Split persistent container state from prepared-view scratch state.
2. Introduce explicit prepared-view handles.
3. Remove renderer-global bound-frame ownership.
4. Split budgets into instances, work items, work units, and slot cap.
5. Dispatch branch count and emit from actual generated work count.
6. Enforce baseline-fit failure for visible non-far color trees.
7. Add actual visible-instance count, actual generated branch-work count, and budget-cap-hit telemetry through async prepared-frame readbacks.
8. Replace registered-slot submission with the live active-slot surface, using latest async emitted-slot readback with registered-slot fallback during warm-up.

Remaining:

1. Keep shadow cheap by default and tune shadow budgets separately from color residency.
2. Remove slot-order bias from visible-instance clamping.
3. Collapse duplicated camera/frustum GPU residency into the pooled prepared-view ownership target.
