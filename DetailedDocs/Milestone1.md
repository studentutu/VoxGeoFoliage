# Milestone 1 — MVP: Assembled Vegetation with GPU-Driven Rendering

## Goal

Deliver a working end-to-end vegetation pipeline (similar to Unreal 5.7 new foliage assembly and rendering approach): author one tree species (trunk + branches with leaves), auto-generate canopy shells (L0/L1/L2) and an impostor mesh, preview all representation tiers in the Editor, render at runtime via BRG with a single GPU compute classification pass, and validate the full chain with EditMode tests and user feedback.

**Authority**: [UnityAssembledVegetation_FULL.md](UnityAssembledVegetation_FULL.md)

---

## MVP Scope Summary

| In Scope | Out of Scope (deferred) |
|----------|------------------------|
| Full authoring data model (assemblies, manual baking) | Auto-baking of reduced geometry |
| Canopy shell generation (L0/L1/L2) from branch leaf geometry | HiZ depth pyramid occlusion |
| Impostor mesh generation (far LOD) | LOD transition dithering/cross-fade |
| Editor preview of all representation tiers | Runtime streaming / dynamic loading |
| BRG rendering with minimal vertex-lit opaque shader | Hierarchical wind system |
| RSUV-only per-instance color variation for leaves (SRP-batch safe) | Placement tools (terrain scatter, paint) |
| GPU compute: frustum cull + LOD selection + backside minimization + draw emission | Scale quantization optimization |
| Spatial partitioning (uniform grid) | Advanced shadow value scoring |
| CullingGroup API per-cell early occlusion (within sphere limits) | Cell streaming |
| ScriptableRendererFeature with depth prepass | Unity LODGroup (replaced by GPU classification) |
| Billboard vertex-lit opaque shader for impostor LOD | — |
| EditMode tests for authoring validation + shell generation | Hierarchical sub-branch canopy shells (per-cell LOD within branch, see Full End-to-End Plan §3.4) |

---

## Representation Tiers (MVP)

| Tier | What Renders | When |
|------|-------------|------|
| R0 | Trunk mesh + all branch meshes (full leaf geometry) + Shell L0 | Very Close range, high projected area |
| R1 | Trunk mesh + Shell L1 (no individual leaves and simplified branches) | Mid range |
| R2 | Trunk mesh + Shell L2 (no individual leaves and branches) | Far-mid range |
| R3 | Impostor mesh (billboard-like opaque simple mesh) | Far range, minimal projected area |

### No Unity LODGroup

This system does **not** use Unity's built-in `LODGroup` component or its default LOD culling behavior. LOD selection is fully owned by the GPU compute classification pass (`VegetationClassify.compute`), which evaluates projected area, backside penalty, and LOD profile thresholds per tree instance per frame. Unity's `LODGroup` is incompatible with BRG-driven indirect rendering and would conflict with our GPU-side tier selection.

---

## Task 1: Authoring Data Model

### 1.1 ScriptableObjects (Authoring — immutable asset data)

**`BranchPrototypeSO`** — single reusable branch module
```
- Mesh branchMesh           // full-geometry branch with leaves
- Material branchMaterial   // opaque Simple Lit
- Mesh shellL0Mesh          // generated canopy shell L0 (baked)
- Mesh shellL1Mesh          // generated canopy shell L1 (baked)
- Mesh shellL2Mesh          // generated canopy shell L2 (baked)
- Material shellMaterial    // opaque Simple Lit for shells
- Bounds localBounds        // local-space AABB
- int triangleBudgetFull    // validation: max tris for branchMesh
- int triangleBudgetShellL0 // validation: max tris for shellL0
- int triangleBudgetShellL1 // validation: max tris for shellL1
- int triangleBudgetShellL2 // validation: max tris for shellL2
```

**`TreeBlueprintSO`** — tree assembly definition
```
- Mesh trunkMesh
- Material trunkMaterial    // opaque Simple Lit
- BranchPlacement[] branches
  - BranchPrototypeSO prototype
  - Vector3 localPosition
  - Quaternion localRotation
  - float scale             // constrained: steps of 0.25 (e.g. 0.25, 0.5, 0.75, 1.0, 1.25, 1.5...)
- Mesh impostorMesh         // generated far-LOD opaque mesh (baked)
- Material impostorMaterial // billboard Simple Lit opaque
- LODProfileSO lodProfile
- Bounds treeBounds         // world-space AABB for the full assembly
```

**`LODProfileSO`** — distance/projected-area thresholds for tier selection
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

**`VegetationTreeAuthoring`** — placed on a GameObject in the scene
```
- TreeBlueprintSO blueprint  // reference to the tree assembly
- [HideInInspector] int runtimeTreeIndex  // assigned at gather time
```

No runtime data stored on the MonoBehaviour — it's a pure authoring/registration component.

### 1.3 Validation Rules (enforced in SO custom editors + tests)

- All meshes must be non-null and readable
- All materials must be opaque (no alpha clip, no transparency)
- branchMesh triangle count ≤ triangleBudgetFull
- Shell meshes must have fewer triangles than the tier above: L0 > L1 > L2
- Impostor mesh must have very low triangle count (≤ 160 suggested)
- Scale must be exactly in steps of 0.25 for MVP
- treeBounds must fully contain trunk + all branch placements
- LOD thresholds must be monotonically decreasing: r0 > r1 > shellL0 > shellL1 > shellL2 > absoluteCull

---

## Task 2: Editor Preview

### 2.1 `VegetationEditorPreview` MonoBehaviour (Editor-only, `[ExecuteInEditMode]`)

Attached to the same GameObject as `VegetationTreeAuthoring`. Toggle on/off via Inspector bool.

**Behavior when enabled:**
- Spawns child GameObjects under the tree root representing the currently selected representation tier
- Inspector enum dropdown: `PreviewTier { R0_Full, R1_ShellL1, R2_ShellL2, R3_Impostor, ShellL0_Only, ShellL1_Only, ShellL2_Only }`
- On tier change: destroy previous preview children, spawn new ones

**Preview child structure per tier:**

| Tier | Spawned Children |
|------|-----------------|
| R0_Full | Trunk GO + N × Branch GOs (full mesh) + N × ShellL0 GOs |
| R1_ShellL1 | Trunk GO + N × ShellL1 GOs (at branch positions) |
| R2_ShellL2 | Trunk GO + N × ShellL2 GOs (at branch positions) |
| R3_Impostor | Single Impostor GO |
| ShellL0_Only | N × ShellL0 GOs |
| ShellL1_Only | N × ShellL1 GOs |
| ShellL2_Only | N × ShellL2 GOs |

**Implementation notes:**
- All preview GOs are tagged/layered for easy cleanup
- Preview GOs are `HideFlags.DontSave | HideFlags.NotEditable`
- Preview is transient — never serialized into the scene
- On disable or blueprint change: clean up all preview children
- Uses `MeshFilter` + `MeshRenderer` with the materials from the SO — no special rendering

### 2.2 Custom monobehaviour Inspector

- `VegetationTreeAuthoringEditor` — custom editor for `VegetationTreeAuthoring`
- Shows: blueprint summary (branch count, total tris per tier, bounds)
- Validation warnings/errors inline
- Preview toggle + tier selector dropdown
- Button: "Regenerate Shells" (triggers shell baking, see Task 3)
- Button: "Regenerate Impostor" (triggers impostor baking, see Task 3)

---

## Task 3: Canopy Shell Generator

### 3.1 Pipeline Overview

Input: `BranchPrototypeSO.branchMesh` (full geometry branch with leaves)
Output: `shellL0Mesh`, `shellL1Mesh`, `shellL2Mesh` written back into the SO

### 3.2 Algorithm: Voxel-Based Shell Extraction

```
1. READ all vertices + triangles from branchMesh
2. COMPUTE local AABB of the branch mesh
3. VOXELIZE into a 3D grid:
   - Resolution: L0=32³, L1=16³, L2=8³ (configurable)
   - Mark voxel as occupied if any triangle intersects it
4. For each shell level:
   a. L0: FLOOD-FILL from exterior to find interior vs exterior voxels (preserves holes)
      L1/L2: DILATE occupied voxels by 1-2 steps to close gaps and collapse holes before extraction
   b. EXTRACT surface voxels (occupied voxels adjacent to empty exterior)
   c. GENERATE mesh via Marching Cubes or simple cube-face emission
   d. SIMPLIFY mesh (edge collapse) to target triangle budget — L1/L2 prioritize vertex minimization over shape fidelity
   e. RECOMPUTE normals (smooth, outward-facing)
   f. STORE as shellLX mesh
```

### 3.3 Shell Quality Constraints

- L0: highest fidelity, preserves crown lobes and major concavities
- L1: simplified silhouette, major lobes only. **Holes and small concavities are collapsed** — vertex budget takes priority over volumetric accuracy. Interior cavities are filled.
- L2: blobby mass, minimal detail, just gross shape. **Aggressively collapse all holes and concavities** — produce the cheapest possible closed volume that preserves only the gross crown silhouette. Vertex/triangle count minimization is the primary goal, even if the mesh becomes a single convex-ish blob.
- All shells: stable outward-facing normals, no degenerate triangles
- L1/L2 generation should skip the flood-fill interior detection step (or invert it) and instead **dilate** the voxel field to close gaps before surface extraction, ensuring holes are filled rather than preserved

### 3.4 Impostor Generation

Input: `shellL2Mesh` (or full tree assembly baked shells)
Output: `impostorMesh`

```
1. Start from Shell L2 (coarsest shell)
2. Further simplify: target ≤ 200 triangles
3. Remove internal cavities
4. Ensure front-facing normals only
5. Weld vertices aggressively
6. Store as impostorMesh on the TreeBlueprintSO
```

### 3.5 Forward Compatibility: Hierarchical Sub-Branch Shells

MVP treats each branch as a single unit with 3 fixed shell meshes (L0/L1/L2). The full version (see [FULL §3.4](UnityAssembledVegetation_FULL.md#34-hierarchical-sub-branch-canopy-shells-full-version)) will subdivide each branch into spatial cells, each with its own shell chain, enabling per-sub-region LOD within a single branch.

**MVP is forward-compatible by design:** the current 3-mesh-per-branch model is equivalent to a hierarchical model with cell grid = 1×1×1. Upgrading later requires only:
1. Sub-branch cell generation in the baking pipeline
2. Per-cell AABB evaluation in the GPU classifier (additional inner loop)
3. Per-cell indirect draw emission

No authoring data model changes needed — `BranchPrototypeSO` will gain an optional cell array, defaulting to the current single-cell behavior.

### 3.6 API

```csharp
/// Bakes canopy shells for a single branch prototype.
/// [INTEGRATION] Called from editor tooling (inspector button, batch bake).
/// Range: branchMesh must be readable, non-null.
/// Condition: voxelResolutions must be > 0.
/// Output: populates shellL0Mesh, shellL1Mesh, shellL2Mesh on the prototype.
static void BakeCanopyShells(BranchPrototypeSO prototype, ShellBakeSettings settings)

/// Bakes the far-LOD impostor mesh for a full tree blueprint.
/// [INTEGRATION] Called from editor tooling.
/// Range: blueprint must have valid shells baked on all branch prototypes.
/// Output: populates impostorMesh on the blueprint.
static void BakeImpostorMesh(TreeBlueprintSO blueprint, ImpostorBakeSettings settings)
```

### 3.7 `ShellBakeSettings` / `ImpostorBakeSettings`

```
ShellBakeSettings:
  - int voxelResolutionL0 = 32
  - int voxelResolutionL1 = 16
  - int voxelResolutionL2 = 8
  - int targetTrianglesL0 = 2000
  - int targetTrianglesL1 = 500
  - int targetTrianglesL2 = 150
  - float smoothNormalAngle = 60f

ImpostorBakeSettings:
  - int targetTriangles = 200
  - float weldThreshold = 0.01f
```

---

## Task 4: BRG Rendering + Simple Lit Shader

### 4.1 BatchRendererGroup Setup

**`VegetationBRGManager`** — runtime singleton, owns the BRG instance

```
Responsibilities:
- Create and own BatchRendererGroup
- Register mesh/material pairs for each draw group:
  - Trunk draw group
  - Branch draw group (full geometry)
  - Shell L0/L1/L2 draw groups
  - Impostor draw group
- Provide the OnPerformCulling callback
- Feed visible instance data from GPU classification results
- Handle buffer lifecycle (create on gather, dispose on destroy)
```

### 4.2 Draw Groups (MVP)

| Group | Mesh Source | Shader | Instance Data |
|-------|-----------|--------|---------------|
| Trunk | TreeBlueprintSO.trunkMesh | VegetationTrunkLit | transform only |
| Branch | BranchPrototypeSO.branchMesh | VegetationTrunkLit | transform only |
| ShellL0 | BranchPrototypeSO.shellL0Mesh | VegetationCanopyLit | transform + RSUV |
| ShellL1 | BranchPrototypeSO.shellL1Mesh | VegetationCanopyLit | transform + RSUV |
| ShellL2 | BranchPrototypeSO.shellL2Mesh | VegetationCanopyLit | transform + RSUV |
| Impostor | TreeBlueprintSO.impostorMesh | VegetationImpostorLit | transform + RSUV |

### 4.3 Vegetation Shaders — Minimal Vertex-Lit, No Textures

Design principle: **as cheap as possible**. No texture sampling for canopy/impostor. All shading data comes from vertex attributes and per-instance RSUV. SRP Batcher compatibility preserved.

Two shaders needed for MVP:

**`VegetationCanopyLit.shader`** — for canopy shells (L0/L1/L2)
- Custom minimal URP-compatible shader (NOT full SimpleLit — stripped down)
- **Opaque only**, front-face culling (`Cull Back`)
- **No albedo texture** — base color from vertex color channel
- **No normal map texture** — normals from vertex data (outward-pointing, baked at shell generation)
- **No emission**, no bump/height maps, no specular map
- Lighting: single diffuse Lambert from vertex normal + URP main light
- DOTS Instancing support for BRG transforms
- Per-instance color variation via **RSUV only** (`unity_RenderingLayer` or custom RSUV uint)
  - RSUV packing (32 bits):
    - Bits [0-7]: hue shift (0-255 → mapped to 0°-30° range)
    - Bits [8-15]: saturation adjust
    - Bits [16-23]: brightness adjust
    - Bits [24-31]: reserved (future wind params, etc.)
  - Shader reads RSUV, unpacks, applies tint to vertex color
- No wind for MVP
- **No MaterialPropertyBlock** — all variation through RSUV only

**`VegetationTrunkLit.shader`** — for trunk and branch full-geometry
- Custom minimal URP-compatible shader
- **Opaque only**, front-face culling (`Cull Back`)
- Albedo from texture (trunk/bark needs texture detail)
- **No normal map** — vertex normals only
- **No emission**, no bump/height maps
- Lighting: single diffuse Lambert
- DOTS Instancing support for BRG transforms
- No RSUV color variation (trunk color is material-fixed)
- No wind for MVP

**`VegetationImpostorLit.shader`** — for far-LOD impostor (see Task 8)

### 4.4 Color Variation — RSUV Only

**Single variation mechanism**: Renderer Shader User Value (RSUV).

- No `MaterialPropertyBlock` (breaks SRP batching)
- No `UNITY_DOTS_INSTANCED_PROP` for color (adds per-instance buffer overhead)
- RSUV provides a single `uint` per renderer that preserves SRP batching
- `VegetationTreeAuthoring` exposes a `Color leafColorTint` in Inspector
- At gather time, the color tint is packed into the RSUV uint
- Shader reads RSUV via `asuint(unity_RenderingLayer)` or equivalent RSUV accessor
- Unpacks and applies as HSB adjustment to vertex color

```
Packing: uint rsuv = (hueShift) | (satAdj << 8) | (briAdj << 16) | (reserved << 24)
```

---

## Task 5: GPU Compute Classification

### 5.1 Compute Shader: `VegetationClassify.compute`

Single dispatch per frame. One thread per tree instance.

```hlsl
Kernel: ClassifyVegetation

Input buffers:
  - _TreeInstances       (StructuredBuffer<TreeInstanceGPU>)
  - _LODProfiles         (StructuredBuffer<LODProfileGPU>)
  - _CellVisibility      (StructuredBuffer<uint>, 1 bit per cell)

Output buffers:
  - _VisibleRecords      (RWStructuredBuffer<VisibleVegetationRecord>)
  - _DrawArgs            (RWStructuredBuffer<uint>, indirect args per draw group)
  - _VisibleCount        (RWStructuredBuffer<uint>, atomic counter)

Uniforms:
  - _ViewProjectionMatrix
  - _CameraPosition
  - _CameraForward
  - _FrustumPlanes[6]
  - _AbsoluteCullProjectedMin
```

### 5.2 Classification Steps (per thread)

```
1. LOAD tree instance data
2. CELL CHECK: if cell not visible → return (CPU pre-marks cells via frustum vs grid)
3. FRUSTUM CULL: test tree AABB against 6 frustum planes → return if outside
4. PROJECTED AREA: compute screen-space projected area from bounds + distance
5. ABSOLUTE CULL: if projected area < absoluteCullProjectedMin → return
6. BACKSIDE PENALTY: dot(cameraForward, treeToCamera) → penalize trees mostly showing backside
7. LOD SELECTION: compare projected area against LOD profile thresholds → select tier (R0/R1/R2/R3)
8. EMIT: atomically increment visible count, write VisibleVegetationRecord
9. INCREMENT DRAW ARGS: atomic add to indirect draw arg counters for the selected draw group(s)
```

### 5.3 Runtime Data Structs (GPU side)

```hlsl
struct TreeInstanceGPU
{
    float3 position;       // world position
    float  uniformScale;   // always 1.0 for MVP
    uint   blueprintIndex; // index into prototype data
    uint   cellIndex;      // spatial cell assignment
    uint   packedColor;    // per-instance color variation (32-bit packed)
    float  boundingSphereRadius;
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
    uint  representationType; // 0=R0, 1=R1, 2=R2, 3=R3
    uint  shellLevel;         // 0/1/2 (meaningful for R0, R1, R2)
    uint  packedColor;
    float4x4 objectToWorld;   // full transform for rendering
};
```

### 5.4 Backside Minimization

Trees where the camera is viewing primarily the "back" (interior/underside) get penalized in LOD selection, pushing them to cheaper representations faster:

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

**`VegetationSpatialGrid`** — CPU-side spatial structure

```
- Gather all VegetationTreeAuthoring positions at startup
- Compute world AABB of all trees
- Divide into uniform cells (cellSize configurable, default 50m)
- Assign each tree a cellIndex
- Each frame: frustum-test cell AABBs on CPU → write cellVisibility bitfield
- Upload cellVisibility to GPU before compute dispatch
- early reject by cellVisibility (when directly behind camera)
```

### 6.2 Cell Visibility

- CPU frustum test is cheap (just AABB vs 6 planes per cell)
- Reduces GPU threads that need full classification
- Cell count for 1km² at 50m cells = 400 cells (trivial CPU cost)

### 6.3 CullingGroup API — Per-Cell Early Occlusion

Unity's `CullingGroup` API can provide **free** visibility and distance-band callbacks from the engine's internal culling (tied to the camera). We use it at the **cell level** (not per-tree) to get early occlusion beyond simple frustum testing.

**Setup:**
- Create one `CullingGroup` per camera (main camera for MVP)
- Register one `BoundingSphere` per cell (center = cell center, radius = cell diagonal / 2)
- `CullingGroup` limit: **1024 bounding spheres per instance**
  - At 50m cells, 1024 spheres covers ~2.56 km² — sufficient for MVP
  - If more cells needed later: use multiple CullingGroup instances or coarser cells
- Set distance reference point to camera position

**Per-frame flow:**
1. `CullingGroup.onStateChanged` fires when cell visibility changes
2. Alternatively, poll `CullingGroup.IsVisible(int index)` per cell each frame
3. Merge CullingGroup visibility with CPU frustum test → write combined `cellVisibility` bitfield
4. Upload to GPU

**Benefits:**
- CullingGroup uses Unity's internal occlusion (including static occluders if baked)
- Zero GPU cost — it's CPU-side engine culling
- Catches cells hidden behind large buildings/terrain that frustum test alone would miss

**Limitations:**
- 1024 sphere limit per CullingGroup instance
- Sphere approximation (not tight AABB) may be slightly conservative
- Only works with Unity's built-in occlusion data (requires baking or runtime occluders)
- For MVP: optional enhancement — if Unity occlusion data isn't baked, CullingGroup still provides frustum-equivalent results

**Implementation note:** CullingGroup is an optimization layer on top of the existing frustum test. The system must work correctly without it (frustum-only fallback).

---

## Task 7: ScriptableRendererFeature + Depth Prepass

### 7.1 `VegetationRendererFeature` — URP ScriptableRendererFeature

Adds two render passes:

**Pass 1: Vegetation Depth Prepass** (`RenderPassEvent.BeforeRenderingOpaques`)
- Renders all visible vegetation with a depth-only shader
- Populates depth buffer early so subsequent opaque passes benefit from early-Z rejection
- Uses the same indirect draw args from classification

**Pass 2: Vegetation Classify + Draw** (`RenderPassEvent.BeforeRenderingOpaques`, after depth)
- Step A: Dispatch `VegetationClassify.compute`
- Step B: Issue indirect draw calls per draw group using the populated draw args
- All draws are opaque Simple Lit — benefits from the depth prepass

### 7.2 Execution Order

```
URP Frame:
  1. [Standard URP] Setup, shadows, etc.
  2. [VegetationRendererFeature] CPU: update cell visibility, upload buffers
  3. [VegetationRendererFeature] GPU: dispatch ClassifyVegetation compute
  4. [VegetationRendererFeature] GPU: vegetation depth prepass (indirect draws, depth-only)
  5. [VegetationRendererFeature] GPU: vegetation color pass (indirect draws, Simple Lit)
  6. [Standard URP] Other opaques, transparents, post-processing
```

### 7.3 Buffer Lifecycle

```
Scene Load / Gather:
  1. Collect all VegetationTreeAuthoring in scene
  2. Build spatial grid, assign cell indices
  3. Create GPU buffers (TreeInstances, LODProfiles, CellVisibility, VisibleRecords, DrawArgs)
  4. Upload static data (tree instances, LOD profiles)
  5. Register meshes/materials with BRG

Each Frame:
  1. CPU: frustum-test cells, update CellVisibility buffer
  2. GPU: clear VisibleRecords counter + DrawArgs counters
  3. GPU: dispatch ClassifyVegetation
  4. GPU: read-back not needed — indirect args drive draws directly

Scene Unload:
  1. Dispose all GPU buffers
  2. Unregister BRG
```

---

## Task 8: Impostor Billboard Shader

### 8.1 `VegetationImpostorLit.shader`

Same minimal design philosophy as `VegetationCanopyLit.shader`:

- Custom minimal URP-compatible shader
- **Opaque only**, front-face culling (`Cull Back`)
- **No albedo texture** — base color from vertex color (baked at impostor generation)
- **No normal map** — vertex normals only (outward-pointing, baked)
- **No emission**, no bump/height maps, no specular
- Lighting: single diffuse Lambert from vertex normal + URP main light
- DOTS Instancing support for BRG transforms
- Per-instance color variation via **RSUV only** (same packing as canopy shader)
- Vertex shader: cylindrical billboard rotation around Y-axis toward camera
- Minimal vertex count (impostor mesh is ≤ 200 triangles)
- No wind for MVP

### 8.2 Billboard Transform

```hlsl
float3 viewDir = normalize(_CameraPosition - objectOrigin);
float angle = atan2(viewDir.x, viewDir.z);
float3x3 billboardRotation = RotateY(angle);
// Apply to vertex positions in vertex shader before standard transform
```

---

## Task 9: Runtime Manager (Orchestration)

### 9.1 `VegetationRuntimeManager` — MonoBehaviour singleton

**Responsibilities:**
- On scene start: gather all `VegetationTreeAuthoring` instances
- Build `VegetationSpatialGrid`
- Create and upload GPU buffers
- Initialize `VegetationBRGManager`
- Each frame: update cell visibility, trigger compute dispatch via renderer feature
  - use Main Camera as a cell visiblity/culling author 
- On destroy: clean up everything

### 9.2 Explicit Reset

Per project rules, must have `static void Reset()` registered in `EditorPlayModeStaticServicesReset`.

---

## Task 10: Testing Strategy

### 10.1 Authoring Validation Tests (`Assets/EditorTests/Vegetation/`)

```
AuthoringValidationTests:
  - BranchPrototype_NullMesh_FailsValidation
  - BranchPrototype_TransparentMaterial_FailsValidation
  - BranchPrototype_TriangleOverBudget_FailsValidation
  - BranchPrototype_ShellTriangleOrder_L0GreaterThanL1GreaterThanL2
  - TreeBlueprint_NullTrunk_FailsValidation
  - TreeBlueprint_EmptyBranches_FailsValidation
  - TreeBlueprint_LODThresholds_MonotonicallyDecreasing
  - TreeBlueprint_BoundsContainAllBranches
  - TreeBlueprint_ImpostorTriangleBudget_Under200
  - TreeBlueprint_ScaleConstraint_OnlyAllowedValues
```

### 10.2 Canopy Shell Generation Tests (`Assets/EditorTests/Vegetation/`)

```
CanopyShellGenerationTests:
  - Voxelize_SimpleCube_AllVoxelsOccupied
  - Voxelize_EmptyMesh_NoVoxelsOccupied
  - Voxelize_SphereMesh_ApproximateVolumeCorrect
  - ShellExtract_L0Resolution_HigherTrisThanL1
  - ShellExtract_L1Resolution_HigherTrisThanL2
  - ShellExtract_OutputMesh_HasValidNormals
  - ShellExtract_OutputMesh_NoDegenTriangles
  - ShellExtract_OutputMesh_WithinBudget
  - ShellExtract_OutputMesh_BoundsContainedInInput
  - ImpostorGenerate_FromShellL2_UnderTriangleBudget
  - ImpostorGenerate_OutputMesh_HasOutwardNormals
  - ImpostorGenerate_OutputMesh_NoInternalCavities
  - ShellExtract_L1_DilationClosesSmallHoles
  - ShellExtract_L2_DilationCollapsesAllCavities
  - ShellExtract_L2_VertexCountMinimized_OverShapeFidelity
```

### 10.3 Spatial Grid Tests

```
SpatialGridTests:
  - Grid_SingleTree_AssignedToCorrectCell
  - Grid_TreesAcrossCells_UniqueIndices
  - Grid_FrustumTest_VisibleCellsMarked
  - Grid_FrustumTest_OccludedCellsNotMarked
```

### 10.4 Classification Logic Tests (CPU mirror)

```
ClassificationTests:
  - Classify_InsideFrustum_LargeArea_SelectsR0
  - Classify_InsideFrustum_MediumArea_SelectsR1
  - Classify_InsideFrustum_SmallArea_SelectsShell
  - Classify_InsideFrustum_TinyArea_SelectsImpostor
  - Classify_OutsideFrustum_NotVisible
  - Classify_BelowAbsoluteCull_NotVisible
  - Classify_BacksidePenalty_ReducesEffectiveLOD
  - Classify_CellNotVisible_SkipsTree
```

---

## Folder Structure

```
Assets/Scripts/Features/Vegetation/
├── Authoring/
│   ├── BranchPrototypeSO.cs
│   ├── TreeBlueprintSO.cs
│   ├── LODProfileSO.cs
│   ├── VegetationTreeAuthoring.cs
│   ├── BranchPlacement.cs          (struct)
│   └── ShellBakeSettings.cs        (struct)
├── Runtime/
│   ├── VegetationRuntimeManager.cs
│   ├── VegetationBRGManager.cs
│   ├── VegetationSpatialGrid.cs
│   ├── TreeInstanceGPU.cs          (struct)
│   ├── LODProfileGPU.cs            (struct)
│   ├── VisibleVegetationRecord.cs  (struct)
│   └── VegetationClassifier.cs     (CPU mirror for testing)
├── Editor/
│   ├── VegetationEditorPreview.cs
│   ├── VegetationTreeAuthoringEditor.cs
│   ├── CanopyShellGenerator.cs
│   ├── ImpostorMeshGenerator.cs
│   ├── Voxelizer.cs
│   └── MeshSimplifier.cs
├── Shaders/
│   ├── VegetationCanopyLit.shader
│   ├── VegetationTrunkLit.shader
│   ├── VegetationImpostorLit.shader
│   ├── VegetationDepthOnly.shader
│   └── VegetationClassify.compute
├── Rendering/
│   ├── VegetationRendererFeature.cs
│   └── VegetationRenderPass.cs
└── Vegetation.asmdef

Assets/EditorTests/Vegetation/
├── AuthoringValidationTests.cs
├── CanopyShellGenerationTests.cs
├── SpatialGridTests.cs
└── ClassificationTests.cs
```

---

## Implementation Order

### Phase A: Foundation (Tasks 1 + 10.1)
1. Create folder structure + asmdef
2. Implement authoring SOs: `BranchPrototypeSO`, `TreeBlueprintSO`, `LODProfileSO`, `BranchPlacement`
3. Implement `VegetationTreeAuthoring` MonoBehaviour
4. Implement authoring validation logic (static validator class)
5. Write authoring validation EditMode tests
6. **Compile check + run tests**

### Phase B: Shell Generation (Tasks 3 + 10.2)
1. Implement `Voxelizer` (triangle-voxel intersection on 3D grid)
2. Implement shell extraction (surface voxel → mesh)
3. Implement `MeshSimplifier` (edge-collapse to budget)
4. Implement `CanopyShellGenerator` (orchestrates voxelize → extract → simplify per level)
5. Implement `ImpostorMeshGenerator` (from L2 → further simplified)
6. Wire into `BranchPrototypeSO` and `TreeBlueprintSO`
7. Write shell generation EditMode tests
8. **Compile check + run tests**

### Phase C: Editor Preview (Task 2)
1. Implement `VegetationEditorPreview` (spawn/destroy child GOs per tier)
2. Implement `VegetationTreeAuthoringEditor` (custom inspector with preview controls)
3. Wire "Regenerate Shells" / "Regenerate Impostor" buttons
4. Manual visual verification in Editor
5. **Compile check**

### Phase D: Spatial Grid + CPU Classification (Tasks 6 + 5-CPU + 10.3 + 10.4)
1. Implement `VegetationSpatialGrid` (uniform grid, cell assignment, frustum test)
2. Implement `VegetationClassifier` (CPU-side classification mirror)
3. Write spatial grid EditMode tests
4. Write classification EditMode tests
5. **Compile check + run tests**

### Phase E: GPU Pipeline (Tasks 4 + 5 + 7 + 8 + 9)
1. Implement `VegetationClassify.compute` shader
2. Implement `VegetationCanopyLit.shader` (vertex-lit, RSUV color variation)
3. Implement `VegetationTrunkLit.shader` (texture-lit trunk/branches)
4. Implement `VegetationImpostorLit.shader` (billboard, vertex-lit, RSUV)
6. Implement `VegetationDepthOnly.shader`
7. Implement `VegetationBRGManager` (BRG lifecycle, mesh/material registration)
8. Implement `VegetationRendererFeature` + `VegetationRenderPass`
9. Implement `VegetationRuntimeManager` (orchestration, gather, frame loop)
10. End-to-end manual test: place tree in scene → run → observe LOD transitions at distance
11. **Compile check + manual verification**

---

## Done Criteria

- [ ] Place a single tree (trunk + branches) in a scene
- [ ] Shell L0/L1/L2 auto-generated from branch geometry
- [ ] Impostor mesh auto-generated from shell L2
- [ ] Editor preview shows all 7 representation views correctly
- [ ] At runtime: GPU classification selects correct LOD based on distance
- [ ] BRG renders all tiers with correct batching
- [ ] Leaf shells show per-instance color variation
- [ ] Impostor billboards face camera
- [ ] Depth prepass renders before color pass
- [ ] Spatial grid reduces unnecessary GPU work
- [ ] All authoring validation tests pass
- [ ] All shell generation tests pass
- [ ] All spatial grid tests pass
- [ ] All classification tests pass

---

END
