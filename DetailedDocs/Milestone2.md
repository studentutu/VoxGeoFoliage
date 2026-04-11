# Milestone 2 - Wind and Production Improvements

## Goal

Turn the finished Milestone 1 renderer into a production-usable vegetation package by closing the biggest practical gaps:
- hierarchical wind across `L0/L1/L2/L3/Impostor`
- runtime material extensibility so project-local custom materials can work without being replaced by package-only shaders
- editor-side canopy-shell generation from quad and alpha-masked branch inputs through `GPUVoxelizer`

**Authority**:
- architecture: [UnityAssembledVegetation_FULL.md](UnityAssembledVegetation_FULL.md)
- finished MVP baseline: [Milestone1.md](Milestone1.md)

Current milestone start state (`2026-04-11`):
- Milestone 1 is finished
- runtime rendering is GPU-resident only
- `VegetationIndirectMaterialFactory` is still a hard bottleneck for package consumers because it rebuilds runtime materials from fixed package shader names and copies only a narrow property subset
- hierarchical wind is still only a design target; it is not a shipped runtime contract yet
- canopy-shell generation still assumes the current primary source path and does not yet support the intended `GPUVoxelizer` route from quad and alpha-masked branch inputs

---

## Missing Features

1. [ ] Wind.
2. [ ] Support non-package only materials and shaders.
3. [ ] Generation of canopy-shell (primary voxelization) based on the [GPUVoxelizer](../Packages/com.voxgeofol.vegetation/Runtime/VoxelizerV2/Scripts/GPUVoxelizer.cs) from quad, alpha-masked branch material.

---

## Why Milestone 2 Exists

Milestone 1 proved the rendering architecture. It did not finish the production usability layer.

Current real blockers:
- wind is missing, so the system still looks static and unfinished in motion-heavy scenes
- custom project materials are effectively blocked because runtime rendering discards the authored shader and recreates package-owned materials through `VegetationIndirectMaterialFactory`
- the current material copy path only preserves a small fixed subset of properties (`_BaseColor`, `_BaseMap`, `_Smoothness`, `_Cull`) and therefore breaks real custom shaders, custom keywords, and project-side shader graphs
- primary canopy-shell generation still does not support the intended `GPUVoxelizer` path for quad and alpha-masked branch inputs, so the package cannot yet ingest that common foliage authoring form without manual conversion
- package-consumer setup still needs a clean contract for what "vegetation-compatible material" actually means

This milestone is about turning the MVP into something other teams can actually ship with.

---

## Scope Summary

| In Scope | Out of Scope |
|----------|--------------|
| Hierarchical wind contract for `L0/L1/L2/L3/Impostor` | Full physics-driven branch simulation |
| Stable per-tree wind phase and tier-consistent motion | Per-leaf bone rigs |
| Runtime material compatibility contract for project-local materials | "Any arbitrary shader works automatically" |
| Removal or demotion of `VegetationIndirectMaterialFactory` as the public material authority | HDRP or Built-in pipeline support |
| Validation and docs for vegetation-compatible custom materials | Runtime alpha-clipped or transparent foliage rendering |
| Editor-side canopy-shell generation from quad and alpha-masked branch materials through `GPUVoxelizer` | Automatic support for every arbitrary masked shader setup without an explicit bake contract |
| Package-consumer smoke pass in a clean URP project | Full streaming/cell-loading milestone |
| Focused lifecycle/diagnostic improvements needed to support the above | HiZ occlusion, dither transitions, DFS hierarchy migration |

---

## Broken Assumptions To Avoid

- Do not assume custom-material support means "copy a few common URP properties and hope." That already failed.
- Do not assume one shader deformation model works for `L0`, shell tiers, and `Impostor`. It will pop badly.
- Do not assume batching can stay keyed by the old source material if the final runtime material pair changes. Final runtime draw compatibility must stay explicit.
- Do not assume wind is shader-only. Stable motion needs authored ownership, runtime phase rules, and verification.
- Do not assume project-local materials can live only inside the package. The package must work when the consuming project owns the materials and shaders.
- Do not assume alpha-masked quad input means runtime alpha-clip support. The requested feature is editor-side voxelization/generation support while runtime rendering stays opaque-only.

---

## Pillar 1: Runtime Material Extensibility

### Current Problem

`VegetationIndirectMaterialFactory` currently does this:
- selects a runtime shader from hardcoded package shader names based on `VegetationRenderMaterialKind`
- creates fresh runtime materials
- copies only a small shared surface-property subset from the authored material
- forces depth rendering through one package-owned hidden shader

That design makes package consumers second-class:
- project-owned custom shaders are ignored
- shader-specific properties and keywords are dropped
- author intent in the original material is only partially preserved
- "same look in preview and runtime" is not guaranteed

### Required Contract

Milestone 2 must replace the current implicit material rewrite with one explicit compatibility contract.

Required rules:
- runtime rendering must accept project-local materials outside `Packages/com.voxgeofol.vegetation`
- every runtime draw slot must resolve to an explicit color/depth material contract, not a hidden hardcoded shader-name switch
- the compatibility path must be validation-driven; unsupported materials must fail clearly instead of silently rendering wrong
- batching identity must be based on the final runtime-compatible material pair plus mesh/material-kind, not on a stale pre-conversion assumption
- authored preview materials and runtime indirect materials must stop diverging silently

### Required Compatibility Paths

Milestone 2 should support exactly these paths:

1. Direct compatible material
- the authored material is already vegetation-runtime compatible
- it exposes the required indirect instance contract and depth path
- runtime uses it directly instead of rebuilding it

2. Explicit binding/adapter path
- the authored material is not directly compatible
- authoring or renderer settings provide an explicit runtime-compatible color/depth binding
- runtime uses the bound materials, not an implicit property-copy approximation

3. Hard failure
- if neither direct compatibility nor an explicit binding exists, registration/validation fails with a clear reason

Non-goal:
- automatically support every arbitrary shader graph or third-party shader without an explicit compatibility path

### Material Milestone Tasks

1. Freeze the public material compatibility contract:
- required shader properties/buffers
- required instancing/indirect input contract
- required depth-pass support
- exact validation errors

2. Refactor runtime material ownership:
- remove `VegetationIndirectMaterialFactory` as the public authority
- keep it only as a temporary fallback utility if still needed internally
- move the real decision to an explicit compatibility resolver/binding contract (material is already placed on the scriptable objects of the branch and tree itself)
- create a supportVegetation.hlsl that should support our indirect rendering pipeline.
- update package shaders to use supportVegetation.hlsl as well as SRP compatible.

3. Update registration and draw-slot identity:
- resolve runtime-compatible material pairs before draw-slot registration
- ensure slot grouping keys the final compatible runtime materials, not the old authored source alone

4. Add package-consumer documentation and validation:
- explain how to author a compatible custom vegetation shader
- explain how to bind a non-compatible authored material to compatible runtime materials
- fail fast on missing depth support, missing instance buffer support, or unsupported shader setup

### Material Done Criteria

- a clean URP consumer project can use project-local vegetation materials without modifying package code
- runtime no longer silently replaces every material with package-only shaders
- validation catches unsupported custom materials before runtime registration
- preview/runtime material intent is explicit instead of accidental

---

## Pillar 2: Hierarchical Wind

### Required Wind Rules

Wind must be representation-dependent and tier-stable.

Required behavior:
- every tree gets a stable wind phase seed
- wind stays visually continuous when a branch or tree changes between `L0/L1/L2/L3/Impostor`
- trunk, branch wood, foliage shells, and far mesh do not all move with the same deformation profile
- the cheapest tiers still preserve the same large-scale motion read as the higher-detail tiers

### First-Pass Wind Ownership

Milestone 2 should keep ownership simple.

Preferred ownership:
- one species-level wind profile for the tree blueprint
- one stable per-tree runtime phase seed derived from instance identity
- optional per-prototype response multiplier only if the first pass proves species-level control is insufficient

Do not start with per-node authored wind data. That is cost-heavy and not justified yet.

### Tier Rules

`L0`
- source foliage gets the richest motion
- source wood gets lower-frequency sway

`L1`
- shell canopy and any retained source wood must still read as the same tree as `L0`
- motion amplitude can be reduced, but phase continuity is mandatory

`L2` and `L3`
- shell/frontier motion must become cheaper and coarser
- simplified trunk and simplified wood must still share the same large-scale sway direction and timing

`Impostor`
- coarse whole-tree sway only
- no fake fine leaf flutter that cannot match the near tiers

### Wind Milestone Tasks

1. Freeze the runtime wind contract:
- global wind inputs
- per-species profile inputs
- per-tree phase seed
- exact instance/shader inputs needed by runtime materials

2. Implement tier-aware deformation rules:
- near tiers prioritize readability and continuity
- far tiers prioritize stable coarse motion and low cost

3. Extend shader/material compatibility rules:
- custom compatible materials must be able to consume the same wind contract
- wind support cannot be package-shader-only if custom materials are a real goal

4. Add verification and tooling:
- manual scene validation across all tiers
- side-by-side validation for tier transitions under wind
- diagnostics proving stable per-tree phase and no obvious tier pop

### Wind Done Criteria

- vegetation visibly moves in runtime across `L0/L1/L2/L3/Impostor`
- the same tree keeps consistent large-scale motion as its representation changes
- wind support is part of the public compatible-material contract, not a private package-only shader feature

---

## Pillar 3: Canopy-Shell Generation From Quad and Alpha-Masked Branch Inputs

### Current Problem

We use CPU voxelizer for primary voxelization hierarchy, bu we also need to support GPU voxelizer.
The current canopy-shell bake path does not yet treat quad and alpha-masked branch materials as a first-class primary source for shell generation. Developer should be able to build hierarchy from both sources (full geometry or quad-alpha-masked).

That blocks a common real workflow:
- foliage often starts as quad cards with alpha-masked textures
- package users already have those assets
- forcing manual conversion to dense source geometry before shell generation is not a credible production contract

### Required Contract

Milestone 2 must add an explicit editor-side bake path that uses [GPUVoxelizer](../Packages/com.voxgeofol.vegetation/Runtime/VoxelizerV2/Scripts/GPUVoxelizer.cs) to derive primary canopy occupancy from quad and alpha-masked branch inputs.

Required rules:
- this is an editor/bake feature, not runtime alpha-clip rendering support
- the produced shell, wood, trunk, and impostor outputs still obey the existing opaque-only runtime contract
- the bake contract must define exactly which source meshes/material data are sampled from the masked input
- unsupported masked-shader setups must fail clearly instead of silently generating garbage occupancy

### GPUVoxelizer Tasks

1. Freeze the source-input contract:
- supported quad/mesh topology assumptions
- supported alpha source and channel rules
- required material/texture readability requirements
- exact failure cases

2. Implement additional voxelization route:
- feed quad and alpha-masked branch inputs through `GPUVoxelizer`
- derive authoritative canopy occupancy for shell generation from that result
- keep the rest of the canopy-shell pipeline bounded and validation-driven

3. Integrate with authoring and docs:
- expose the source mode clearly in authoring/bake tooling
- document when to use dense geometry input versus masked-quad input
- add verification assets or a smoke path proving the generated shell matches the intended canopy silhouette

### GPUVoxelizer Done Criteria

- a branch prototype can be authored from quad cards with alpha-masked branch material which should generate canopy shells through the editor bake flow
- the bake path is explicit about supported input assumptions and failure cases
- runtime rendering remains opaque-only while still consuming the generated outputs from that bake path

---

## Secondary Improvements

These are allowed only when they directly support the three main pillars:

- package-consumer smoke pass and docs cleanup
- runtime lifecycle hardening around `RefreshRuntimeRegistration()`
- diagnostics needed to inspect resolved runtime materials and wind inputs

These are not the milestone focus:
- DFS hierarchy migration
- deep occlusion work
- new placement tools

---

## Implementation Order

### Phase A: Contract Freeze

1. Freeze the custom-material compatibility contract
2. Freeze the first-pass wind ownership model
3. Freeze the `GPUVoxelizer` masked-quad bake input contract
4. Define exact validation failures and runtime authoring requirements

### Phase B: Material Path Refactor

1. Replace hardcoded shader-name switching with explicit compatibility resolution
2. Resolve final runtime color/depth materials before draw-slot registration
3. Add validation and package-consumer documentation

### Phase C: Wind Integration

1. Add the shared wind runtime contract
2. Implement tier-aware deformation in the compatible shader path
3. Verify phase continuity across `L0/L1/L2/L3/Impostor`

### Phase D: `GPUVoxelizer` Canopy Integration

1. Add additional primary (L0) shell-generation path for quad and alpha-masked branch inputs
2. Integrate bake/validation/tooling for the new source mode
3. Verify generated shell output against the intended masked silhouette

### Phase E: Verification and Hardening

1. Clean URP consumer-project smoke pass
2. Manual scene validation for wind, material compatibility, and masked-quad shell generation
3. Compile validation and targeted EditMode coverage for the new compatibility rules

---

## Done Criteria

- Milestone 1 remains intact and unchanged as the runtime baseline
- a package consumer can use project-local compatible materials without patching package source
- runtime material compatibility is explicit, validated, and documented
- hierarchical wind works across all runtime tiers and does not introduce obvious representation popping
- canopy-shell generation supports the primary `GPUVoxelizer` path for quad and alpha-masked branch inputs without weakening the opaque-only runtime contract
- the package README and milestone routing reflect the new consumer-facing contract

---

END
