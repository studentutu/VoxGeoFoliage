# Milestone 1 - MVP: Assembled Vegetation with GPU-Driven Rendering

## Goal

Deliver a working end-to-end vegetation pipeline (similar to Unreal 5.7 new foliage assembly and rendering approach): author one tree species from reusable branch prototypes, auto-generate canopy shells (L0/L1/L2) and an impostor mesh, preview all representation tiers in the Editor, render at runtime via BRG with a single GPU compute classification pass, and validate the full chain with EditMode tests and user feedback.

**Authority**: [UnityAssembledVegetation_FULL.md](UnityAssembledVegetation_FULL.md)

Package note (`2026-03-29`):
- Public package root: `Packages/com.voxgeofol.vegetation`
- Distributable demo content: `Packages/com.voxgeofol.vegetation/Samples~/Vegetation Demo`
- Repo-local demo mirror for current scenes: `Assets/Tree`

---

## MVP Scope Summary

| In Scope | Out of Scope (deferred) |
|----------|-------------------------|
| Full authoring data model with real two-mesh branch prototypes (wood + foliage) | Auto-baking of reduced geometry from arbitrary imported trees |
| Readable source branch meshes as a hard validation rule | HiZ depth pyramid occlusion |
| Canopy shell generation (L0/L1/L2) from branch foliage geometry | LOD transition dithering / cross-fade |
| Impostor mesh generation from merged tree-space shell L2 assembly | Runtime streaming / dynamic loading |
| Editor preview of all representation tiers | Hierarchical wind system |
| BRG rendering with minimal vertex-lit opaque shaders | Placement tools (terrain scatter, paint) |
| RSUV-only prototype canopy tint payload for foliage and shell draws | Random per-instance tint variation |
| GPU compute: frustum cull + LOD selection + backside minimization + draw emission | Scale quantization optimization |
| Spatial partitioning (uniform grid) | Advanced shadow value scoring |
| CullingGroup API per-cell early occlusion (within sphere limits) | Cell streaming |
| ScriptableRendererFeature with depth prepass | Unity LODGroup |
| Billboard-like opaque shader for impostor LOD | - |
| EditMode tests for authoring validation + shell generation + classification | - |

---

## Representation Tiers (MVP)

| Tier | What Renders | When |
|------|--------------|------|
| R0 | Trunk mesh + branch wood meshes + branch foliage meshes + shell L0 | Very close range, high projected area |
| R1 | Trunk mesh + simplified branch wood L1 + shell L1 | Mid range |
| R2 | Trunk mesh + simplified branch wood L2 + shell L2 | Far-mid range |
| R3 | Impostor mesh | Far range, minimal projected area |

### No Unity LODGroup

This system does not use Unity's built-in `LODGroup`. LOD selection is fully owned by the GPU compute classification pass (`VegetationClassify.compute`), which evaluates projected area, backside penalty, and LOD profile thresholds per tree instance per frame.Unity's `LODGroup` is incompatible with BRG-driven indirect rendering and would conflict with our GPU-side tier selection.

---

## Task 1: Authoring Data Model

### Implementation Status (`2026-03-28`)

- Implemented the Task 1 runtime authoring types under `Packages/com.voxgeofol.vegetation/Runtime/Authoring/`.
- Implemented explicit authoring validation via `VegetationAuthoringValidator`.
- Added demo authoring sync in `Packages/com.voxgeofol.vegetation/Editor/VegetationPhaseAAuthoringSync.cs` to rebuild branch placements/bounds from the assembled tree prefab.
- Added EditMode coverage in `Packages/com.voxgeofol.vegetation/Tests/Editor/AuthoringValidationTests.cs` and `Packages/com.voxgeofol.vegetation/Tests/Editor/AuthoringAssetSyncTests.cs`.
- Verified with `Fully Compile by Unity` and the Unity EditMode test runner; the demo `branch_leaves_fullgeo` assets now validate and the demo tree blueprint is populated.

### 1.1 ScriptableObjects (authoring, immutable asset data)

**`BranchPrototypeSO`** - single reusable branch module
```
- Mesh woodMesh             // branch wood / bark mesh used in R0
- Material woodMaterial     // opaque bark material
- Mesh foliageMesh          // leaf / needle geometry used in R0 and shell baking
- Material foliageMaterial  // opaque foliage source material
- Color leafColorTint       // prototype-authored canopy tint packed into RSUV
- BranchShellNode[] shellNodes // generated adaptive shell hierarchy; each node stores shell L0/L1/L2
- Mesh shellL1WoodMesh      // generated simplified branch wood for shell L1
- Mesh shellL2WoodMesh      // generated simplified branch wood for shell L2
- Material shellMaterial    // opaque shell material
- Bounds localBounds        // local-space AABB containing woodMesh + foliageMesh
- int triangleBudgetWood
- int triangleBudgetFoliage
- int triangleBudgetShellL0
- int triangleBudgetShellL1
- int triangleBudgetShellL2
```

**Source Branch Asset Contract**
- MVP consumes the real source layout directly: one wood mesh plus one foliage mesh per branch prototype.
- Source branch meshes must be readable for validation, preview, and shell baking.
- Validation must fail explicitly if the import contract is broken. No hidden mesh-combine step is assumed.

**`TreeBlueprintSO`** - tree assembly definition
```
- Mesh trunkMesh
- Material trunkMaterial
- BranchPlacement[] branches
  - BranchPrototypeSO prototype
  - Vector3 localPosition
  - Quaternion localRotation
  - float scale             // constrained to 0.25 steps for MVP
- Mesh impostorMesh         // generated far-LOD opaque mesh
- Material impostorMaterial // billboard-like opaque material
- LODProfileSO lodProfile
- Bounds treeBounds         // local-space AABB for the assembled tree
```

**`LODProfileSO`** - projected-area thresholds for tier selection
```
- float r0MinProjectedArea
- float r1MinProjectedArea
- float shellL0MinProjectedArea
- float shellL1MinProjectedArea
- float shellL2MinProjectedArea
- float absoluteCullProjectedMin
- float backsideBiasScale
- float silhouetteKeepThreshold
```

### 1.2 Runtime MonoBehaviour

**`VegetationTreeAuthoring`** - placed on a scene GameObject
```
- TreeBlueprintSO blueprint
- [HideInInspector] int runtimeTreeIndex
```

No runtime data is stored on the MonoBehaviour. `leafColorTint` lives on `BranchPrototypeSO`, not on the scene component.

### 1.3 Validation Rules

- `trunkMesh`, `woodMesh`, and `foliageMesh` must be non-null and readable.
- All materials must be opaque. No alpha clip. No transparency.
- `woodMesh` triangle count must be within `triangleBudgetWood`.
- `foliageMesh` triangle count must be within `triangleBudgetFoliage`.
- Shell node meshes must monotonically reduce per node: `L0 >= L1 >= L2`.
- Leaf-frontier shell totals must strictly reduce: `L0 > L1 > L2`.
- `localBounds` must fully contain `woodMesh + foliageMesh`.
- `treeBounds` must fully contain trunk + all placed branches.
- Branch scale must be exactly in steps of `0.25`.
- LOD thresholds must be monotonically decreasing: `r0 > r1 > shellL0 > shellL1 > shellL2 > absoluteCull`.
- Impostor mesh should stay under `160-200` triangles.

---

## Task 2: Editor Preview

### Implementation Status (`2026-03-29`)

- Added `VegetationEditorPreview`, `VegetationTreeAuthoringEditorUtility`, `VegetationTreeAuthoringEditorPanel`, `VegetationTreeAuthoringEditor`, and `VegetationTreeAuthoringWindow` under `Packages/com.voxgeofol.vegetation/Editor/`.
- `VegetationTreeAuthoring` is back to being a clean scene binding plus validation surface; preview reconstruction and bake entry points now live in editor-only code.
- The custom inspector now shows blueprint summary (branch count, per-tier triangle totals, bounds), aggregated validation issues, preview toggle/tier controls, and shell/impostor bake buttons.
- The dedicated editor window can target the selected `VegetationTreeAuthoring` and reuses the same preview/bake controls outside the Inspector.
- Preview tiers rebuild a transient hierarchy under the configured branch root using `HideFlags.DontSave | HideFlags.NotEditable`, including the milestone-required `R0`, `R1`, `R2`, `R3`, and shell-only views.
- Updated `Packages/com.voxgeofol.vegetation/Tests/Editor/AuthoringAssetSyncTests.cs` to cover the extracted preview utility and editor utility bake entry point.
- Verified with `Fully Compile by Unity` on `2026-03-29`.

### 2.1 `VegetationEditorPreview` Editor Utility

Driven from the custom inspector and the dedicated editor window. The utility stores the last selected preview tier in editor session state and rebuilds the preview directly from `TreeBlueprintSO` plus the referenced branch prototypes.

**Behavior**
- Spawns transient child GameObjects under the configured tree branch root for the selected representation tier.
- Editor enum: `VegetationPreviewTier { R0Full, R1ShellL1, R2ShellL2, R3Impostor, ShellL0Only, ShellL1Only, ShellL2Only }`
- On tier change: destroy previous preview children, then spawn the new preview set.

**Preview child structure**

| Tier | Spawned Children |
|------|------------------|
| R0_Full | Trunk GO + N x Branch Wood GOs + N x Branch Foliage GOs + N x ShellL0 GOs |
| R1_ShellL1 | Trunk GO + N x WoodL1 GOs + N x ShellL1 GOs |
| R2_ShellL2 | Trunk GO + N x WoodL2 GOs + N x ShellL2 GOs |
| R3_Impostor | Single impostor GO |
| ShellL0_Only | N x Wood GOs + N x ShellL0 GOs |
| ShellL1_Only | N x WoodL1 GOs + N x ShellL1 GOs |
| ShellL2_Only | N x WoodL2 GOs + N x ShellL2 GOs |

**Implementation notes**
- All preview GOs are transient: `HideFlags.DontSave | HideFlags.NotEditable`.
- On disable or blueprint change: clean up all preview children.
- Use the source materials from the SOs. No special runtime path is required for preview.

### 2.2 Custom Inspector + Editor Window

`VegetationTreeAuthoringEditor`
- Shows blueprint summary: branch count, total tris per tier, bounds.
- Shows validation warnings and errors inline.
- Exposes preview toggle + tier selector.
- Button: `Regenerate Shells`
- Button: `Regenerate Impostor`
- Button: `Regenerate Shells And Impostor`
- Button: `Open Editor Window`

`VegetationTreeAuthoringWindow`
- Targets a selected `VegetationTreeAuthoring` component.
- Reuses the same preview, validation, summary, and bake controls when the Inspector is not convenient.

---

## Task 3: Canopy Shell Generator

### Implementation Status (`2026-03-30`)

- Implemented `ShellBakeSettings` and `ImpostorBakeSettings` under `Packages/com.voxgeofol.vegetation/Runtime/Authoring/`.
- Implemented `BranchShellNode`, `BranchShellNodeUtility`, `GeneratedMeshAssetUtility`, `CanopyShellGenerator`, `ImpostorMeshGenerator`, and the shared `Runtime/MeshVoxelizerV1/` hierarchy builder under the package runtime/editor folders.
- Replaced the MVP single-branch shell chain with hierarchical `BranchPrototypeSO.shellNodes`.
- `CanopyShellGenerator` now bakes `L0/L1/L2` shell meshes on every occupied shell node via `MeshVoxelizerHierarchyBuilder` and still bakes branch-level `shellL1WoodMesh` and `shellL2WoodMesh`.
- `ImpostorMeshGenerator` now merges `trunkMesh` plus transformed leaf-frontier `shellNodes[].shellL2Mesh` and `shellL2WoodMesh` instances in tree local space, re-voxelizes that aggregate with `MeshVoxelizerHierarchyBuilder`, and writes `impostorMesh` onto `TreeBlueprintSO`.
- Generated shell and impostor meshes are persisted as explicit `.mesh` assets under owner-local `GeneratedMeshes/` folders in `Assets/` so they survive editor restarts and stay compatible with public package installs.
- Rewrote EditMode coverage in `Packages/com.voxgeofol.vegetation/Tests/Editor/CanopyShellGenerationTests.cs`, `AuthoringValidationTests.cs`, and `AuthoringAssetSyncTests.cs` for the hierarchy model.
- Removed the obsolete editor-only `Voxelizer`, `VoxelGrid`, and `MarchingTetrahedraMesher` files after the rewrite landed.
- Verified with `Fully Compile by Unity` on `2026-03-30`; Unity EditMode tests were rewritten but not rerun in this pass.

### 3.1 Pipeline Overview

Input: `BranchPrototypeSO.foliageMesh`  
Output: `shellNodes`, `shellL1WoodMesh`, `shellL2WoodMesh`

`woodMesh` is explicitly excluded from canopy voxelization. `L0` preview reconstruction reuses the source `woodMesh`, while `L1/L2` bake simplified branch wood attachments alongside the hierarchical canopy shells.
Generated outputs are saved as standalone `.mesh` assets beside the owning authoring asset under `GeneratedMeshes/` when that asset lives in `Assets/`. Package-root assets fall back to `Assets/VoxGeoFol.Generated/Vegetation/Meshes/`.

### 3.2 Algorithm: MeshVoxelizer-Based Hierarchical Shell Extraction

```
1. READ all vertices + triangles from `foliageMesh`
2. COMPUTE the source local AABB from `foliageMesh.bounds`
3. BUILD three MeshVoxelizer occupancy levels for the same source mesh:
   - `L0 = 16`
   - `L1 = 12`
   - `L2 = 8`
4. COUNT `L0` surface voxels inside the current node bounds
5. SPLIT the node into octants only when:
   - `depth < maxOctreeDepth`
   - `surfaceVoxelCount >= minimumSurfaceVoxelCountToSplit`
   - at least two child octants contain owned `L0` surface voxels
6. For each occupied node:
   - emit `shellL0Mesh`, `shellL1Mesh`, and `shellL2Mesh` from the owned occupied voxels at that level
   - persist those meshes into owner-local `GeneratedMeshes/`
7. CLONE source branch wood into `shellL1WoodMesh` and `shellL2WoodMesh` so shell preview tiers keep wood attached while the hierarchy rewrite is stabilized
```

### 3.3 Shell Quality Constraints

- L0 preserves major canopy lobes and silhouette detail across the branch bounds.
- L1 and L2 reduce triangle counts monotonically on the leaf frontier.
- Shell nodes must remain spatially valid: children stay inside the parent bounds and child ranges stay contiguous in the persisted node array.
- All shells must have stable outward normals and no degenerate triangles.

### 3.4 Impostor Generation

Input: temporary tree-space mesh assembled from `trunkMesh` + transformed leaf-frontier `BranchShellNode.shellL2Mesh`  
Output: `TreeBlueprintSO.impostorMesh` in tree local space

```
1. Assemble a temporary tree-space mesh from `trunkMesh` + all placed leaf-frontier `shellL2` meshes + branch `shellL2WoodMesh`
2. Approximate a voxel resolution from the requested target triangle count
3. Run `MeshVoxelizerHierarchyBuilder.BuildHierarchy` with `maxDepth = 0` so only the root node is produced
4. Use the root `shellL0Mesh` as the persisted impostor mesh
5. Store the result as `impostorMesh`
```

### 3.5 Hierarchy Status

Hierarchical sub-branch canopy shells are now implemented in the editor authoring path. `BranchPrototypeSO.shellNodes` is the authoritative canopy shell representation for preview, validation, triangle accounting, and impostor baking.

Follow-up note (`2026-03-30`):
- The production editor baker now uses `Runtime/MeshVoxelizerV1/MeshVoxelizerHierarchyBuilder`.
- `MeshVoxelizerHierarchyDemo` remains the manual validation tool for inspecting the hierarchy split on real branches.

### 3.6 API

```csharp
/// Bakes canopy shells for a single branch prototype.
/// [INTEGRATION] Called from editor tooling.
/// Range: foliageMesh must be readable and non-null.
/// Output: populates shellNodes plus branch-level shellL1WoodMesh/shellL2WoodMesh.
static void BakeCanopyShells(BranchPrototypeSO prototype, ShellBakeSettings settings)

/// Bakes the far-LOD impostor mesh for a full tree blueprint.
/// [INTEGRATION] Called from editor tooling.
/// Range: blueprint must have valid leaf-frontier shellL2 meshes on all referenced branch prototypes.
/// Output: populates impostorMesh.
static void BakeImpostorMesh(TreeBlueprintSO blueprint, ImpostorBakeSettings settings)
```

### 3.7 Settings

```
ShellBakeSettings:
  - int maxOctreeDepth = 4
  - int voxelResolutionL0 = 16
  - int voxelResolutionL1 = 12
  - int voxelResolutionL2 = 8
  - int minimumSurfaceVoxelCountToSplit = 4

ImpostorBakeSettings:
  - int targetTriangles = 200
  - float weldThreshold = 0.01f
```

---

## Task 4: BRG Rendering + Shaders

### 4.1 `VegetationBRGManager`

Responsibilities
- Create and own `BatchRendererGroup`.
- Register mesh/material pairs for every draw slot:
  - trunk draw slot per tree blueprint
  - branch wood draw slot per branch prototype
  - branch foliage draw slot per branch prototype
  - shell L0/L1/L2 draw slots per shell node mesh on the leaf frontier
  - impostor draw slot per tree blueprint
- Own the draw-slot registry used by GPU classification.
- Feed visible instance data from GPU classification results.
- Handle GPU buffer lifecycle.

### 4.2 Draw Groups

| Group | Mesh Source | Shader | Instance Data |
|-------|-------------|--------|---------------|
| Trunk | `TreeBlueprintSO.trunkMesh` | `VegetationTrunkLit` | transform only |
| BranchWood | `BranchPrototypeSO.woodMesh` | `VegetationTrunkLit` | transform only |
| BranchFoliage | `BranchPrototypeSO.foliageMesh` | `VegetationCanopyLit` | transform + packed leaf tint |
| ShellL0 | `BranchShellNode.shellL0Mesh` (leaf frontier) | `VegetationCanopyLit` | transform + packed leaf tint |
| ShellL1 | `BranchShellNode.shellL1Mesh` (leaf frontier) | `VegetationCanopyLit` | transform + packed leaf tint |
| ShellL2 | `BranchShellNode.shellL2Mesh` (leaf frontier) | `VegetationCanopyLit` | transform + packed leaf tint |
| Impostor | `TreeBlueprintSO.impostorMesh` | `VegetationImpostorLit` | transform only |

### 4.3 Shaders

**`VegetationCanopyLit.shader`** - for branch foliage full geometry and canopy shells
- Opaque only
- No albedo texture for shells
- Use vertex color when present; otherwise use a simple material base color multiplied by packed tint
- **No normal map** — vertex normals only
- **No emission**, no bump/height maps
- Lambert lighting from vertex normals
- RSUV-only packed tint
- No `MaterialPropertyBlock`

**`VegetationTrunkLit.shader`** - for trunk and branch wood
- Opaque only
- Albedo texture allowed
- No normal map
- **No normal map** — vertex normals only
- **No emission**, no bump/height maps
- Lambert lighting from vertex normals

**`VegetationImpostorLit.shader`** - for far LOD
- Opaque only
- No albedo textures(only vertex color driven)
- Billboard-like Y rotation toward camera
- **No normal map** — vertex normals only
- **No emission**, no bump/height maps
- Color baked into the impostor mesh / material for MVP
- No RSUV tint required for MVP

### 4.4 Prototype Canopy Tint - RSUV Only

- `BranchPrototypeSO` owns `leafColorTint`
- At gather time, that color is packed into `packedLeafTint`
- `BranchPrototypeGPU` carries the packed tint for foliage + shell draw emission
- Shader reads RSUV and applies HSB adjustments
- No `MaterialPropertyBlock`
- No DOTS instanced color property

```
Packing: uint rsuv = hueShift | (satAdj << 8) | (briAdj << 16) | (reserved << 24)
```

---

## Task 5: GPU Compute Classification

### 5.1 `VegetationClassify.compute`

Single dispatch per frame. One thread per all foliage assembly instances (all _TreeBlueprints types).

```hlsl
Input buffers:
  - _TreeInstances       (StructuredBuffer<TreeInstanceGPU>)
  - _TreeBlueprints      (StructuredBuffer<TreeBlueprintGPU>)
  - _BranchPlacements    (StructuredBuffer<BranchPlacementGPU>)
  - _BranchPrototypes    (StructuredBuffer<BranchPrototypeGPU>)
  - _BranchShellNodes    (StructuredBuffer<BranchShellNodeGPU>)
  - _LODProfiles         (StructuredBuffer<LODProfileGPU>)
  - _CellVisibility      (StructuredBuffer<uint>)

Output buffers:
  - _VisibleRecords      (RWStructuredBuffer<VisibleVegetationRecord>)
  - _DrawArgs            (RWStructuredBuffer<uint>)
  - _VisibleCount        (RWStructuredBuffer<uint>)
```

### 5.2 Classification Steps

```
1. LOAD TreeInstanceGPU + TreeBlueprintGPU + LODProfileGPU
2. CELL CHECK
3. FRUSTUM CULL
4. PROJECTED AREA
5. ABSOLUTE CULL
6. BACKSIDE PENALTY
7. LOD SELECTION (R0 / R1 / R2 / R3)
8. EMIT tree-level VisibleVegetationRecord
9. INCREMENT DRAW ARGS
   - R0: trunk + each branch placement's wood + foliage + leaf-frontier shellL0 node slots
   - R1: trunk + each branch placement's leaf-frontier shellL1 node slots
   - R2: trunk + each branch placement's leaf-frontier shellL2 node slots
   - R3: impostor slot only
```

### 5.3 Runtime Data Structs

```hlsl
struct TreeInstanceGPU
{
    float3 position;
    float  uniformScale;
    uint   blueprintIndex;
    uint   cellIndex;
    float  boundingSphereRadius;
};

struct TreeBlueprintGPU
{
    uint lodProfileIndex;
    uint branchPlacementStartIndex;
    uint branchPlacementCount;
    uint trunkDrawSlot;
    uint impostorDrawSlot;
};

struct BranchPlacementGPU
{
    float3 localPosition;
    float  uniformScale;
    float4 localRotation;
    uint   prototypeIndex;
};

struct BranchPrototypeGPU
{
    uint woodDrawSlot;
    uint foliageDrawSlot;
    uint shellNodeStartIndex;
    uint shellNodeCount;
    uint packedLeafTint;
};

struct BranchShellNodeGPU
{
    float3 localCenter;
    float3 localExtents;
    uint   firstChildIndex;
    uint   childMask;
    uint   shellL0DrawSlot;
    uint   shellL1DrawSlot;
    uint   shellL2DrawSlot;
};

struct LODProfileGPU
{
    float r0MinProjectedArea;
    float r1MinProjectedArea;
    float shellL0MinProjectedArea;
    float shellL1MinProjectedArea;
    float shellL2MinProjectedArea;
    float absoluteCullProjectedMin;
    float backsideBiasScale;
    float silhouetteKeepThreshold;
};

struct VisibleVegetationRecord
{
    uint  treeIndex;
    uint  blueprintIndex;
    uint  representationType;
    uint  shellLevel;
    float4x4 objectToWorld;
};
```

### 5.4 Backside Minimization

```hlsl
float backsidePenalty = saturate(dot(normalize(tree.position - _CameraPosition), _CameraForward));
// backsidePenalty ≈ 1.0 when tree is directly behind camera dir (backside)
// backsidePenalty ≈ 0.0 when tree faces camera (frontside)
float effectiveArea = projectedArea * (1.0 - backsidePenalty * lod.backsideBiasScale);
// Use effectiveArea for LOD thresholds instead of raw projectedArea
```

---

## Task 6: Spatial Partitioning

### 6.1 Uniform Grid

`VegetationSpatialGrid`— CPU-side spatial structure
- Gather all `VegetationTreeAuthoring` positions at startup
- Compute world AABB of all trees
- Divide into uniform cells
- Assign each tree a `cellIndex`
- Each frame: frustum-test cell AABBs on CPU and upload `cellVisibility`

### 6.2 Cell Visibility

- Cheap CPU frustum test reduces full GPU classification work
- 50m default cell size is acceptable for MVP

### 6.3 `CullingGroup` API

Optional early occlusion layer on top of frustum testing
- One `BoundingSphere` per cluster of cells (cluster is 8*8*8 cells)
- Main camera only for MVP
- Must remain a pure optimization layer; frustum-only fallback must work

**Limitations:**
- 1024 sphere limit per CullingGroup instance
- Sphere approximation (not tight AABB) may be slightly conservative
- Only works with Unity's built-in occlusion data (requires baking)
- For MVP: optional enhancement — if Unity occlusion data isn't baked, CullingGroup still provides frustum-equivalent results
**Implementation note:** CullingGroup is an optimization layer on top of the existing frustum test. The system must work correctly without it (frustum-only fallback).

---

## Task 7: ScriptableRendererFeature + Depth Prepass

### 7.1 `VegetationRendererFeature`

Add three ordered render stages:

**Pass 1: Clear + Classify**
- Cache view position and rotation (skip clearing/calculating if we haven't moved and haven't rotated main camera)
- Clear visible counters and draw-arg counters
- Upload per-frame cell visibility
- Dispatch `VegetationClassify.compute`

**Pass 2: Vegetation Depth Prepass**
- Render visible vegetation with `VegetationDepthOnly.shader`
- Uses draw args produced by Pass 1

**Pass 3: Vegetation Color Pass**
- Render visible vegetation with the lit shaders
- Uses the same draw args

### 7.2 Execution Order

```
URP Frame:
  1. Standard URP setup
  2. CPU updates cell visibility
  3. GPU clear + classify
  4. Standard URP opaque depth pre-pass and GPU vegetation depth prepass
  5. Standard URP opaques and GPU vegetation color pass
  6. Standard URP remaining  transparents / post-processing 
```

### 7.3 Buffer Lifecycle

```
Scene Load / Gather:
  1. Collect all VegetationTreeAuthoring instances
  2. Build VegetationSpatialGrid
  3. Build TreeInstanceGPU / TreeBlueprintGPU / BranchPlacementGPU / BranchPrototypeGPU / BranchShellNodeGPU buffers
  4. Upload LOD profiles and static draw-slot data
  5. Register meshes and materials with BRG

Each Frame:
  1. CPU roughly updates cell visibility (rely on Occlusion spheres and cell clusters)
  2. GPU clears counters
  3. GPU dispatches classification
  4. GPU draws depth
  5. GPU draws color

Scene Unload:
  1. Dispose buffers
  2. Unregister BRG
```

---

## Task 8: Impostor Billboard Shader

### 8.1 `VegetationImpostorLit.shader`

- Opaque only
- no albedo texture
- Minimal Lambert lighting
- No normal map
- Billboard-like Y rotation toward camera
- Color baked into impostor vertex color or base material
- No RSUV tint for MVP
- No wind for MVP
- **No normal map**
- **No emission**, no bump/height maps, no specular

### 8.2 Billboard Transform

```hlsl
float3 viewDir = normalize(_CameraPosition - objectOrigin);
float angle = atan2(viewDir.x, viewDir.z);
float3x3 billboardRotation = RotateY(angle);
```

---

## Task 9: Runtime Manager (Orchestration)

### 9.1 `VegetationRuntimeManager`— MonoBehaviour singleton

Responsibilities
- Gather all `VegetationTreeAuthoring` instances
- Build `VegetationSpatialGrid`
- Flatten tree blueprint data into GPU static buffers
- Initialize `VegetationBRGManager`
- Drive per-frame buffer updates through the renderer feature
- Clean up on destroy

### 9.2 Explicit Reset

Per project rules, all static runtime services must expose `Reset()` and be wired into `EditorPlayModeStaticServicesReset`.

---

## Task 10: Testing Strategy

### 10.1 Authoring Validation Tests (`Packages/com.voxgeofol.vegetation/Tests/Editor/`)

```
AuthoringValidationTests:
  - BranchPrototype_NullWoodMesh_FailsValidation
  - BranchPrototype_NullFoliageMesh_FailsValidation
  - BranchPrototype_SourceMeshesMustBeReadable
  - BranchPrototype_TransparentMaterial_FailsValidation
  - BranchPrototype_TriangleBudget_FailsValidation
  - BranchPrototype_LocalBoundsContainWoodAndFoliage
  - BranchPrototype_ShellTriangleOrder_L0GreaterThanL1GreaterThanL2
  - TreeBlueprint_NullTrunk_FailsValidation
  - TreeBlueprint_EmptyBranches_FailsValidation
  - TreeBlueprint_LODThresholds_MonotonicallyDecreasing
  - TreeBlueprint_BoundsContainAllBranches
  - TreeBlueprint_ImpostorTriangleBudget_Under200
  - TreeBlueprint_ScaleConstraint_OnlyAllowedValues

AuthoringAssetSyncTests:
  - RefreshBranchPrototypeLocalBounds_EncapsulatesWoodAndFoliageMeshes
  - RefreshBlueprintFromAssemblyAsset_RebuildsPlacementsAssignsLodProfileAndProducesValidBlueprint
  - ReconstructFromDataAndOriginalBranch_RebuildsBranchHierarchyFromBlueprint
  - ReconstructShellL1FromData_RebuildsBranchHierarchyFromBlueprintShells
  - DeleteOriginals_RemovesAllChildrenFromBranchRoot
```

### 10.2 Canopy Shell Generation Tests (`Packages/com.voxgeofol.vegetation/Tests/Editor/`)

```
CanopyShellGenerationTests:
  - BuildHierarchy_SeparatedClusters_CreateHierarchyNodes
  - BakeCanopyShells_SeparatedClusters_CreateHierarchyNodes
  - BakeCanopyShells_LeafFrontierTriangleCountsDecreaseAcrossLevels
  - BakeCanopyShells_NodeMeshesHaveValidNormalsAndNoDegenerateTriangles
  - BakeCanopyShells_PersistedChildRangesStayValid
  - ImpostorGenerate_FromLeafFrontierShellL2_CreatesReadableMesh
  - ImpostorGenerate_MergesHierarchyShellsInTreeSpace
```

### 10.3 Spatial Grid Tests

```
SpatialGridTests:
  - Grid_SingleTree_AssignedToCorrectCell
  - Grid_TreesAcrossCells_UniqueIndices
  - Grid_FrustumTest_VisibleCellsMarked
  - Grid_FrustumTest_OccludedCellsNotMarked
```

### 10.4 Classification Logic Tests

```
ClassificationTests:
  - Classify_InsideFrustum_LargeArea_SelectsR0
  - Classify_InsideFrustum_MediumArea_SelectsR1
  - Classify_InsideFrustum_SmallArea_SelectsR2
  - Classify_InsideFrustum_TinyArea_SelectsR3
  - Classify_OutsideFrustum_NotVisible
  - Classify_BelowAbsoluteCull_NotVisible
  - Classify_BacksidePenalty_ReducesEffectiveLOD
  - Classify_CellNotVisible_SkipsTree
  - Classify_ExpandsBlueprintBranchSlots_ForSelectedTier
```

---

## Folder Structure

```
Packages/com.voxgeofol.vegetation/
|-- Runtime/
|   |-- Authoring/
|   |   |-- BranchShellNode.cs
|   |   |-- BranchShellNodeUtility.cs
|   |   |-- BranchPrototypeSO.cs
|   |   |-- TreeBlueprintSO.cs
|   |   |-- LODProfileSO.cs
|   |   |-- VegetationTreeAuthoring.cs
|   |   |-- BranchPlacement.cs
|   |   |-- ImpostorBakeSettings.cs
|   |   |-- ShellBakeSettings.cs
|   |   `-- VegetationAuthoringValidator.cs
|   |-- MeshVoxelizerV1/
|   |   |-- Box3.cs
|   |   |-- DrawLines.cs
|   |   |-- MeshRayTracer.cs
|   |   |-- MeshVoxelizer.cs
|   |   |-- MeshVoxelizerHierarchyBuilder.cs
|   |   |-- MeshVoxelizerHierarchyDemo.cs
|   |   |-- MeshVoxelizerHierarchyNode.cs
|   |   `-- VoxelizerDemo.cs
|   |-- Shaders/
|   |   `-- planned GPU pipeline assets
|   |-- Rendering/
|   |   `-- planned URP integration runtime code
|   `-- Vegetation.asmdef
|-- Editor/
|   |-- VegetationPhaseAAuthoringSync.cs
|   |-- VegetationPreviewTier.cs
|   |-- VegetationAuthoringSummary.cs
|   |-- VegetationEditorPreview.cs
|   |-- VegetationTreeAuthoringEditor.cs
|   |-- VegetationTreeAuthoringEditorPanel.cs
|   |-- VegetationTreeAuthoringEditorUtility.cs
|   |-- VegetationTreeAuthoringWindow.cs
|   |-- CanopyShellGenerator.cs
|   |-- GeneratedMeshAssetUtility.cs
|   |-- ImpostorMeshGenerator.cs
|   `-- supporting editor tools
|-- Tests/
|   `-- Editor/
|       |-- AuthoringAssetSyncTests.cs
|       |-- AuthoringValidationTests.cs
|       |-- CanopyShellGenerationTests.cs
|       |-- SpatialGridTests.cs
|       `-- ClassificationTests.cs
`-- Samples~/
    `-- Vegetation Demo/
```

---

## Implementation Order

### Phase A: Foundation (Tasks 1 + 10.1)
1. Create folder structure + asmdef
2. Implement `BranchPrototypeSO`, `TreeBlueprintSO`, `LODProfileSO`, `BranchPlacement`
3. Match the real source asset contract directly: `woodMesh + foliageMesh + leafColorTint`
4. Author first playable source assets from `branch_leaves_fullgeo` and `pine_branch_dense_needles`
5. Create one demo `TreeBlueprintSO` and one `LODProfileSO`
6. Implement `VegetationTreeAuthoring`
7. Implement authoring validation logic
8. Write authoring validation EditMode tests
9. Compile check + run tests

### Phase B: Shell Generation (Tasks 3 + 10.2)
1. Implement MeshVoxelizer-based hierarchy generation on foliage geometry
2. Implement shell extraction by splitting L0 surface voxels into octant nodes
3. Implement `CanopyShellGenerator`
4. Implement `ImpostorMeshGenerator` from merged tree-space shell L2 assembly
5. Wire shell / impostor baking into SOs
6. Write shell generation EditMode tests
7. Compile check + run tests

### Phase C: Editor Preview (Task 2)
1. Implement `VegetationEditorPreview`
2. Implement `VegetationTreeAuthoringEditor`
3. Wire `Regenerate Shells` / `Regenerate Impostor`
4. Manual visual verification
5. Compile check

### Phase D: Spatial Grid + CPU Classification (Tasks 6 + 5 CPU + 10.3 + 10.4)
1. Implement `VegetationSpatialGrid`
2. Implement `VegetationClassifier` as CPU mirror
3. Mirror tree-blueprint expansion into branch draw-slot selection
4. Write spatial grid EditMode tests
5. Write classification EditMode tests
6. Compile check + run tests

### Phase E: GPU Pipeline (Tasks 4 + 5 + 7 + 8 + 9)
1. Implement `VegetationClassify.compute`
2. Implement `VegetationCanopyLit.shader`
3. Implement `VegetationTrunkLit.shader`
4. Implement `VegetationImpostorLit.shader`
5. Implement `VegetationDepthOnly.shader`
6. Implement `VegetationBRGManager` with per-blueprint / per-prototype draw-slot registry
7. Implement `VegetationRendererFeature` + `VegetationRenderPass`
8. Implement `VegetationRuntimeManager` with GPU static-buffer flattening
9. End-to-end manual test in a demo scene
10. Compile check + manual verification

---

## Done Criteria

- One tree species is authored as a `TreeBlueprintSO` using reusable two-mesh branch prototypes
- Source branch meshes validate as readable
- Shell L0 / L1 / L2 are auto-generated from branch foliage geometry
- Impostor mesh is auto-generated from merged tree-space shell L2 assembly
- Editor preview shows all 7 representation views correctly
- Runtime GPU classification selects the correct tier by distance / projected area
- BRG renders trunk, branch wood, branch foliage, shell tiers, and impostor with correct batching
- Canopy draws use prototype-authored `leafColorTint`
- Impostor billboards face the camera
- Classification runs before depth, and depth runs before color
- Spatial grid reduces unnecessary GPU work
- All authoring validation tests pass
- All shell generation tests pass
- All spatial grid tests pass
- All classification tests pass

---

END
