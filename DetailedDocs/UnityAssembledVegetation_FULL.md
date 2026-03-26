# Unity Assembled Vegetation System — Full End-to-End Plan (Branch-Shell Architecture)

## 0. Purpose

This document defines a production-ready architecture for a **high-performance vegetation system in Unity 6.3 (URP)** inspired by Unreal Engine 5.7 foliage innovations (Assemblies, voxelized LOD, and hierarchical wind animation).

The system is designed to:
- Maximize **geometry reuse** (branch-based assembly)
- Minimize **CPU overhead** via GPU-driven rendering
- Preserve **canopy mass and silhouette at distance**
- Scale to **millions of instances**
- Integrate cleanly with **URP Forward+**
- remove usage of Masked transparent material on foliage (only opaque rendering)

With following constraints:
- No Nanite (currently no Unity equivalent)
- Uses **fully reduced geometry at all stages**
- Uses **branch assembly reuse**
- Uses **multi-level canopy shells**
- canopy shell levels are first-class runtime representation tiers
- Uses **single-pass GPU classification (LOD + Occlusion + Draw emission)**
- All runtime geometry is reduced geometry
- No transparency
- No masked / alpha-tested materials
- Opaque rendering only
- Impostors are not billboards or cards, very simple mesh
- Far-distance fallback is a simplified plain opaque mesh (front facing only)
- Wind is hierarchical and representation-dependent
- LOD + Occlusion + Draw emission are fused into a single GPU compute dispatch per frame

This system is intended to reproduce the performance behavior of modern large-scale foliage renderers inside Unity’s actual rendering constraints, rather than copying UE/Nanite literally.

Core principles:
- Opaque-only rendering (no transparency, no alpha test), transparency will break tile-based rendering on mobiles/handheld devices.
- Reduced geometry at all levels (no Nanite equivalent)
- Branch-based assembly with reusable modules
- Branch-attached canopy shells (L0/L1/L2)
- Single GPU compute pass for LOD + occlusion + draw emission
- SRP Batcher–friendly via scale control and shared assets
- For color variation prefer (BRG) to keep SRP-friendly batching.  
- do not use MaterialPropertyBlock! For per instance variation prefer to use new Unity API Renderer Shader User Value (RSUV) with a single 'uint' as a packed custom 32-bit integer per renderer for all variation data (required DOTS Instancing shader support). This preserves SRP-friendly batching.

---

# 1. System Architecture

## 1.1 High-Level Pipeline

Authoring → Baking → Runtime (Gather/Switch LOD) → GPU Classification → Indirect Rendering

## 1.2 Core Rules

- No transparency or masked materials
- No transparency billboards (onlu opque very simplified front-sided geometry)
- Far representation = simplified opaque mesh
- Geometry always reduced
- Single compute dispatch per frame
- Branch reuse everywhere

---

# 2. Representation Model

## 2.1 Hierarchy

| Tier | Representation |
|------|----------------|
| R0 | trunk + branch shells L0 |
| R1 | trunk + branch shells L1/L2 |
| R2 | shells only |
| R3 | far opaque mesh |

## 2.2 Tree Composition

Tree = Trunk + Σ (Branch → Shell L0/L1/L2)

---

## 2.3 Rendering Stack

- Unity 6.3
- URP Forward+
- Opaque materials only
- Compute shader classification
- BatchRendererGroup and/or RenderMeshIndirect
- StructuredBuffer / RWStructuredBuffer / AppendStructuredBuffer
- Indirect draw arguments

---

## 2.4 URP Configuration

Required:
- Forward+
- SRP Batcher ON
- Opaque-only simple lit shaders
- Compute shaders enabled
- Depth texture and depth pyramid support for occlusion
- Avoid material features that force expensive divergence

Recommended:
- no per-instance material variants unless packed into instance data
- minimize keyword explosion
- avoid unnecessary passes
- keep simple lit shaders tightly specialized (remove normals from canopy shell material)

## 2.5 Performance Strategy

The core performance strategy is:

1. Opaque-only everything
   - maximize depth rejection
   - stabilize occlusion
   - reduce overdraw
   - limit branch scale variability to pre-defined progressively smaller number of scale factors (SRP batching explicitly requires the same scaling)

2. Reduce geometry early
   - especially backside and interior mass

3. Use shells as primary budget representations
   - not just distant fallbacks

4. Use a very cheap far opaque mesh
   - not a textured billboard

5. Single GPU classification dispatch
   - unify LOD + occlusion + draw emission

6. Hierarchical wind
   - cost decreases with representation simplification

# 3. Canopy Shell System

## 3.1 Definition

Branch-local opaque volumetric mesh representing foliage mass.

## 3.2 Generation

Per branch prototype:
1. Reduced geometry input
2. Voxel/SDF density
3. Extract L0/L1/L2
4. Simplify
5. Store

## 3.3 Material

- Fully opaque
- Soft diffuse shading
- Stable normals

## 3.4 Hierarchical Sub-Branch Canopy Shells (Full Version)

### Problem with Fixed L0/L1/L2

MVP uses 3 fixed shell meshes per branch prototype (L0, L1, L2). This works for small-to-medium branches but becomes a bottleneck for large, complex branches:

- A large branch may have foliage clusters at very different distances from the camera
- Fixed shells treat the entire branch as one unit — either all detailed or all simplified
- Unreal 5.7 solves this by subdividing branches into spatial cells and generating shell variants per cell, allowing the GPU classifier to choose shell detail **per sub-region** of a branch based on each region's own AABB/projected area

### Hierarchical Shell Architecture

In the full version, each branch prototype is spatially subdivided:

```
Branch = Σ (BranchCell[i] → ShellChain[i])

Where:
  BranchCell = spatial subdivision of the branch volume (uniform grid or octree)
  ShellChain = { shellL0, shellL1, shellL2 } per cell
```

**Per-cell shell generation:**
1. Subdivide branch local AABB into cells (e.g. 2×2×2 or 3×3×3 grid)
2. For each cell that contains geometry:
   a. Voxelize only the triangles within that cell
   b. Generate L0/L1/L2 shells from that cell's volume
   c. Store as separate meshes with cell-local bounds
3. Each cell gets its own AABB for GPU classification

**GPU classification change:**
- Instead of selecting one shell level for the entire branch, the classifier evaluates **each cell's AABB** against projected area thresholds
- Cells close to camera → L0 (detailed shell)
- Cells at medium distance → L1
- Cells far away → L2 or culled entirely
- This means a single branch instance can render a mix of L0/L1/L2 cells simultaneously

### Data Model Extension

```
BranchPrototypeGPU (full version):
  - uint cellCount
  - uint cellDataStartIndex  // into BranchCellData buffer

BranchCellData:
  - float3 cellLocalCenter
  - float3 cellLocalExtents   // half-size of cell AABB
  - uint shellL0MeshIndex
  - uint shellL1MeshIndex
  - uint shellL2MeshIndex
```

### Benefits

- **Gradual detail reduction**: nearby cells stay detailed while distant cells simplify
- **Better silhouette preservation**: important crown lobes keep L0 while interior mass drops to L2
- **Reduced overdraw**: cells fully behind other cells can be culled individually
- **Matches UE 5.7 behavior**: per-region LOD within a single foliage assembly

### MVP Simplification

MVP treats each branch as a single cell (cell count = 1). The full hierarchical system is a superset — MVP's 3-mesh-per-branch model is equivalent to the hierarchical model with cell grid size = 1×1×1.

This ensures the MVP data model is **forward-compatible**: upgrading to hierarchical shells requires no architectural changes, only:
1. Sub-branch cell generation in the baking pipeline
2. Per-cell AABB evaluation in the GPU classifier
3. Per-cell indirect draw emission

## 3.5 Wind

- Low frequency
- Mass motion only

---

# 4. Authoring Workflow

## 4.1 Tree Assembly/ Branch Creation
- Modular opaque foliage chunks
- Reuse branches with transforms

Trees are not authored as single final meshes.

They are authored as:

```text
Tree = trunk + reusable branch modules + reduced branch sets + shell chain + far opaque mesh
```

Every authored asset must preserve the system constraints:
- opaque-only
- reduced-only
- no billboard dependence
- no alpha-leaf assumptions
- minimized number of unique scaled branches (approximate same branch to the nearest usable scale)

---

## 4.2 Baking
- R0/R1 reduction
- Shell L0/L1/L2 per branch
- Far mesh

## 4.3 Canopy shell chain baking

Bake:
- shell L0
- shell L1
- shell L2

### Baking pipeline
1. Build aggregate canopy occupancy from branch set
2. Voxelize or field-sample canopy mass
3. Extract shell candidates at different density thresholds
4. Simplify while preserving silhouette and dominant crown lobes
5. Compute bounds and metadata
6. Export levels

## 4.4  Far opaque mesh baking

This is critical.

Generate a plain opaque far mesh from the shell / reduced canopy volume:
- no cards
- no billboards
- no alpha-based impostor

The far mesh should:
- preserve gross crown silhouette
- preserve trunk anchor if visible
- minimize triangle count aggressively
- be stable under lighting and wind

Recommended generation:
1. Start from shell L2
2. simplify further
3. remove cavities that no longer matter at far distance
4. fuse into one or very few connected volumes
5. ensure robust normals for stable shading


## 4.5 Validation
- No alpha
- Proper silhouette
- Shell continuity

---

## 4.6 Editor preview modes

The editor must support previewing each runtime representation:
- R0 + shell L0
- R1 + shell L1-L2
- shell L0
- shell L1
- shell L2
- far opaque mesh

And each wind mode:
- R0 branch wind
- R1 reduced wind
- shell sway
- far mesh motion

# 5. Runtime System

## 5.1 Data

- Tree instances
- Branch instances
- Prototypes
- Shell levels
- Visible records

---

# 6. GPU Pipeline

Single compute pass:

- Frustum culling
- Occlusion
- LOD selection
- Shell selection
- Emit visible records
- Build indirect args

## 6.1 Example classification pseudocode

```hlsl
[numthreads(64,1,1)]
void ClassifyVegetation(uint id : SV_DispatchThreadID)
{
    TreeInstanceGPU tree = _TreeInstances[id];
    TreeRuntimeStaticGPU treeStatic = _TreeStatic[id];
    LODProfileGPU lod = _LODProfiles[tree.lodProfileIndex];

    if (!CellVisible(tree.cellIndex))
        return;

    float projectedArea = ComputeProjectedArea(tree, treeStatic);
    if (projectedArea < _AbsoluteCullProjectedMin)
        return;

    float occlusionConfidence = EstimateOcclusion(tree, treeStatic);
    if (occlusionConfidence <= _HardOcclusionReject)
        return;

    float silhouetteImportance = EvaluateSilhouetteImportance(tree, treeStatic);
    float backsidePenalty = EvaluateBacksidePenalty(tree, treeStatic);
    float shadowValue = EvaluateShadowValue(tree, treeStatic);

    uint representationType;
    uint shellLevel;
    float lodFade;

    SelectRepresentation(
        projectedArea,
        occlusionConfidence,
        silhouetteImportance,
        backsidePenalty,
        shadowValue,
        lod,
        representationType,
        shellLevel,
        lodFade);

    VisibleVegetationRecord record =
        BuildVisibleRecord(tree, treeStatic, representationType, shellLevel, lodFade, occlusionConfidence);

    uint visibleIndex = WriteVisibleRecord(record);

    EmitToCompactedList(representationType, shellLevel, visibleIndex);
    IncrementIndirectCounter(representationType, shellLevel, treeStatic, record.meshVariantIndex);
}
```

## 6.2Representation selector example

```hlsl
void SelectRepresentation(
    float projectedArea,
    float occlusionConfidence,
    float silhouetteImportance,
    float backsidePenalty,
    float shadowValue,
    LODProfileGPU lod,
    out uint representationType,
    out uint shellLevel,
    out float lodFade)
{
    float r0Score =
        projectedArea * 0.45 +
        silhouetteImportance * 0.35 +
        shadowValue * 0.20 -
        backsidePenalty * lod.backsideBiasScale;

    if (r0Score > lod.silhouetteKeepThreshold &&
        projectedArea > lod.r0MinProjectedArea)
    {
        representationType = 0; // R0
        shellLevel = 0;
        lodFade = 0;
        return;
    }

    if (projectedArea > lod.r1MinProjectedArea)
    {
        representationType = 1; // R1
        shellLevel = 0;
        lodFade = 0;
        return;
    }

    representationType = 2; // shell
    lodFade = 0;

    if (projectedArea > lod.shellL0MinProjectedArea)
    {
        shellLevel = 0;
        return;
    }

    if (projectedArea > lod.shellL1MinProjectedArea)
    {
        shellLevel = 1;
        return;
    }

    if (projectedArea > lod.shellL2MinProjectedArea)
    {
        shellLevel = 2;
        return;
    }

    representationType = 3; // far mesh
    shellLevel = 0;
}
```

---

# 7. GPU Buffer Layout

## 7.1 Buffers

- TreeInstances
- BranchInstances
- BranchPrototypes
- ShellLevels
- VisibleRecords
- IndirectArgs

## 7.2 Flow

Tree → Branch → Prototype → Shell → Mesh

---

# 8. Rendering

## 8.1 Draw Groups

- R0
- R1
- Shell L0/L1/L2
- Far mesh

## 8.2 Shaders

- Opaque only
- Stable lighting
- Wind tiers

### Branch shader
- opaque only
- reduced geometry only
- limited wind
- branch/trunk shading

### Shell shader
- opaque only
- density-preserving canopy shading
- low-frequency wind
- stable normals

### Far mesh shader
- opaque only
- very stable shading
- minimal wind
- imposter camera-facing

---

# 9. Wind System

| Tier | Wind |
|------|------|
| R0 | branch motion |
| R1 | reduced |
| Shell | sway |
| Far | minimal |

---

# 10. Deferred Optimization: Scale Quantization

## Rule

Scale must be discrete or fixed per branch (to generate shell levels with 1 scale per branch).

Preferred:
scale = 1.0

Alternative:
{0.75, 1.0, 1.25}

---

# 11. Tasks Breakdown

## Phase 1
- Data structures
- Core buffers

## Phase 2
- Authoring tools
- Baking tools

## Phase 3
- GPU classification

## Phase 4
- Rendering integration

## Phase 5
- Optimization + validation

---

# 12. Validation

Must ensure:
- No transparency
- Stable LOD transitions
- Proper batching
- Controlled triangle count

---

# 13. Conclusion

This system achieves scalable vegetation rendering in Unity by:

- replacing leaf geometry with opaque volumes
- using branch-attached shells
- enforcing batching constraints
- using a single GPU pass

---

END
