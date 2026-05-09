# Vegetation Shadow Redesign

Purpose: current shadow design review and proposed redesign for fast opaque foliage with shadows and LOD.

Status: design proposal. No implementation has been made from this document yet.

Primary goal:

- Opaque-only foliage rendering.
- Shadows supported without making shadows the dominant frame cost.
- Shadow caster geometry must never be larger or richer than the visible accepted representation.
- LOD must stay deterministic across color, depth, and shadow.
- Runtime must remain GPU-resident, indirect, and free of synchronous readback in URP render callbacks.

Accepted decisions:

- Offscreen vegetation may cast shadows only when the tree bounds intersect a camera-centered offscreen caster sphere.
- Default offscreen caster radius is `5 m` and must be a separate runtime setting.
- Branch/trunk same-as-color self-shadowing is limited to cascade `0` by default.
- Farther cascades use cheap tree-level casters only.

## critical flaws

1. Shadow LOD is currently independent from visible LOD.

   Current code still uses `RenderMainLightShadows` and `AllowExpandedTreePromotionInShadows` instead of the documented `ShadowMode.Off` / `ShadowMode.CheapTree` contract.

   Current path:

   ```text
   VegetationRendererFeature.DrawMainLightShadowAtlas()
   -> per cascade
   -> AuthoringContainerRuntime.PrepareViewForFrustum(... allowExpandedTreePromotion)
   -> PrepareGpuResidentView(... useExplicitFrustumPipeline = true)
   -> VegetationGpuDecisionPipeline.PrepareResidentFrame(... shadowProxyOnly = true)
   -> VegetationClassify.compute ResolveAcceptedTreeDrawSlot()
   -> L0/L1 map to shadowProxyDrawSlotL0/L1
   ```

   This is the direct correctness bug. A shadow proxy can cast a silhouette unrelated to the visible branch-expanded tree. That creates the current failure: shadow geometry can cover the visible branch as if the branch were larger than it is.

2. Shadow cost scales like another full vegetation renderer.

   The shadow pass runs classify, accept, count, pack, emit, and indirect-args finalization for every main-light cascade and every active container. That means shadow work is not a cheap derivative of the color result. It is another full decision pipeline, multiplied by cascade count.

   This violates the actual project goal. Shadows are a feature. They are not allowed to duplicate the full color LOD system unless the project explicitly accepts that cost, and it should not.

3. Shadow proxy validation does not validate the useful invariant.

   Current validation checks shadow proxy bounds against whole `treeBounds` and triangle order against lower detail tiers. That does not prove the proxy is inside the visible accepted tier silhouette.

   Required invariant:

   ```text
   shadow caster bounds <= visible accepted tier bounds + tiny bake tolerance
   shadow caster triangles <= TreeL3 triangles unless benchmark-approved
   shadow caster tier <= color accepted tier detail
   ```

4. Active-slot compaction is tied to diagnostics behavior.

   The shipped docs claim submission normally uses latest non-zero emitted-slot readback. In code, when telemetry capture is disabled, active slot indices are cleared and submission falls back to all registered slots. That means production builds can pay all registered slot submissions until diagnostics are enabled or a separate production compaction path exists.

   Shadow redesign must not depend on diagnostics to avoid dead draw slots.

5. Shadow budgets default to color-sized budgets.

   Current container defaults give shadow the same `131072` visible instances, expanded branch work items, and approximate work units as color. That is not a shadow policy. It is a memory and GPU-work multiplier.

   Cheap shadows need their own smaller defaults because the shadow path must be subordinate to color.

6. Offscreen shadows are currently under-specified.

   The runtime authority says fully correct offscreen casters are out of scope. The new target is narrower and usable: offscreen casters only within `5 m` of the camera. That must be explicitly implemented as a separate camera-near shadow caster ring, not as "all shadow-frustum vegetation."

## missing pieces

1. Public settings.

   ```csharp
   public enum VegetationShadowMode
   {
       Off = 0,
       CheapTree = 1
   }
   ```

   Required `VegetationFoliageFeatureSettings` fields:

   ```text
   ShadowMode shadowMode = CheapTree
   float offscreenCasterRadius = 5.0
   int maxSameAsColorSelfShadowCascade = 0
   bool castImpostorShadows = false
   ```

   `maxSameAsColorSelfShadowCascade = 0` means only cascade 0 can use same-as-color branch/trunk casters.

2. A color-result dependency surface.

   The color prepare must expose enough GPU state for shadow prepare to consume accepted color tiers without rerunning tree promotion:

   ```text
   ColorAcceptedTreeState
     tree visible flag
     accepted color tier
     accepted color tier cost
     tree distance or priority ring
   ```

   Shadow prepare should read this state. It must not run independent promotion.

3. A shadow-specific cheap emission path.

   Required shadow path:

   ```text
   Prepare color view once
   -> keep accepted tier state
   -> per shadow cascade
      -> classify cascade frustum against visible color trees
      -> add offscreen 5 m ring trees
      -> map accepted tier to shadow caster tier
      -> count shadow slots
      -> build slot starts
      -> emit shadow instances
      -> finalize shadow indirect args
      -> submit active shadow slots
   ```

   This still prepares per cascade, but it does not rerun color LOD admission or branch promotion. It only filters and emits cheaper caster instances.

4. Explicit offscreen-ring classification.

   Offscreen caster eligibility:

   ```text
   treeIntersectsCameraSphere =
       distance(cameraPosition, tree.sphereCenterWorld) <= offscreenCasterRadius + tree.boundingSphereRadius

   offscreenShadowEligible =
       treeIntersectsCameraSphere
       && tree is not color-visible
       && tree intersects current shadow cascade frustum
   ```

   Default offscreen caster representation:

   ```text
   offscreen tree -> TreeL3 or validated TreeShadowLod
   ```

   Do not emit branch-expanded offscreen casters by default. The camera cannot see the tree, so branch-accurate unseen caster detail is the wrong default.

5. Shadow tier matrix.

   ```text
   Color accepted tier L0:
     cascade 0 -> same trunk + same L0 branch/foliage/canopy draw slots as color
     cascade 1+ -> TreeL3 or validated TreeShadowLod

   Color accepted tier L1:
     cascade 0 -> same trunk + same L1 branch/wood/canopy draw slots as color
     cascade 1+ -> TreeL3 or validated TreeShadowLod

   Color accepted tier L2:
     all cascades -> TreeL3 or validated TreeShadowLod

   Color accepted tier TreeL3:
     all cascades -> TreeL3 or validated TreeShadowLod

   Color accepted tier Impostor:
     default -> no shadow caster

   Offscreen 5 m ring:
     all cascades where cascade frustum intersects tree -> TreeL3 or validated TreeShadowLod
   ```

6. Validation and migration.

   Production should remove `ShadowProxyMeshL0/L1` from runtime registration and compute payloads.

   If an optional `TreeShadowLod` is introduced later, validation must be:

   ```text
   TreeShadowLod readable
   TreeShadowLod bounds inside TreeL3 bounds + tolerance
   TreeShadowLod triangles <= TreeL3 triangles
   TreeShadowLod material supports ShadowCaster pass
   ```

   Existing shadow proxy bake buttons should be removed or marked obsolete and disconnected from production runtime.

7. Telemetry.

   Shadow diagnostics must report separately:

   ```text
   cascade index
   color-visible shadow tree count
   offscreen 5 m ring tree count
   same-as-color caster count
   cheap tree caster count
   skipped impostor caster count
   shadow visible instance count
   active shadow slot count
   shadow cap hits
   shadow prepare GPU marker
   shadow submit GPU marker
   ```

## best alternatives

1. Current independent shadow proxy path.

   Rejected. It is already proving brittle. It can cast larger silhouettes than visible geometry and it duplicates too much GPU work.

2. No vegetation shadows.

   Useful as `ShadowMode.Off`, but not enough for the project. The stated goal requires shadows.

3. Same-as-color shadows for all accepted tiers and all cascades.

   Correct but too expensive. It preserves visual consistency, but it pays branch-expanded shadow casters across cascades. That fights the fast foliage goal.

4. Cheap tree-only shadows for all tiers.

   Fast and stable, but loses near self-shadowing. This can be acceptable for far cascades and offscreen casters, not for close branches where self-shadowing sells volume.

5. Recommended hybrid: same-as-color only for visible near `L0/L1` in cascade 0; cheap tree-level casters everywhere else.

   This keeps the only expensive shadow work where it is visible and correctness-sensitive. It also prevents larger unrelated shadow silhouettes because near casters reuse the accepted visible representation.

Relevant production guidance:

- Unity shadow optimization focuses on limiting shadow distance, caster count, cascades, and soft-shadow cost: https://docs.unity.cn/6000.2/Documentation/Manual/shadows-optimization.html
- Unity recommends `RenderMeshIndirect` over older indirect APIs: https://docs.unity.cn/6000.2/Documentation/ScriptReference/Graphics.RenderMeshIndirect.html
- Unreal Nanite foliage direction supports the project premise: opaque real geometry is preferable to masked foliage cards for performance-sensitive foliage: https://dev.epicgames.com/documentation/unreal-engine/nanite-virtualized-geometry-in-unreal-engine
- Godot MultiMesh guidance reinforces the batching/chunking tradeoff: batch many instances, but spatial chunking is still required for culling: https://docs.godotengine.org/en/stable/classes/class_multimeshinstance3d.html

## recommended combined design

### 1. Replace public shadow toggles

Remove production usage of:

```text
RenderMainLightShadows
AllowExpandedTreePromotionInShadows
ShadowProxyMeshL0
ShadowProxyMeshL1
ShadowProxyDrawSlotL0
ShadowProxyDrawSlotL1
ShadowProxyWorkCostL0
ShadowProxyWorkCostL1
_ShadowProxyOnly
```

Replace with:

```text
ShadowMode
OffscreenCasterRadius
MaxSameAsColorSelfShadowCascade
CastImpostorShadows
```

Default:

```text
ShadowMode = CheapTree
OffscreenCasterRadius = 5.0
MaxSameAsColorSelfShadowCascade = 0
CastImpostorShadows = false
```

### 2. Keep color prepare authoritative

Color prepare remains the only owner of LOD admission:

```text
ClassifyCells
BuildVisibleTreeList
ClassifyTrees
AcceptTreeTiers
GenerateExpandedBranchWorkItems
Count/pack/emit color
Finalize color args
```

Shadow prepare must not run `AcceptTreeTiers`.

Shadow prepare consumes accepted color tiers and maps them to legal shadow casters.

### 3. Add a shadow emission pipeline

New compute path:

```text
ResetShadowFrameState
ClassifyShadowCascadeTrees
BuildShadowCasterList
ResetShadowSlotCounts
CountShadowCasters
BuildShadowSlotStarts
EmitShadowCasters
FinalizeShadowIndirectArgs
```

Rules:

- Visible `L0/L1` trees can emit branch/trunk casters only when `cascadeIndex <= MaxSameAsColorSelfShadowCascade`.
- Visible `L2/TreeL3` emit cheap tree caster.
- Visible `Impostor` emits nothing by default.
- Offscreen 5 m ring emits cheap tree caster only.
- No shadow path can promote a tree above its color accepted tier.

### 4. Define caster mapping in one function

Centralize shadow tier decisions. Do not scatter tier logic across renderer feature, registry builder, and compute shader.

Pseudo-code:

```text
ResolveShadowCaster(colorTier, cascadeIndex, isOffscreenNearRing):
  if colorTier == Culled and !isOffscreenNearRing:
    None

  if isOffscreenNearRing:
    CheapTree

  if colorTier == Impostor and !CastImpostorShadows:
    None

  if cascadeIndex <= MaxSameAsColorSelfShadowCascade:
    if colorTier == L0:
      SameAsColorL0
    if colorTier == L1:
      SameAsColorL1

  if colorTier == L2 or colorTier == TreeL3 or colorTier == L0 or colorTier == L1:
    CheapTree

  return None
```

### 5. Keep branch self-shadowing narrow

For cascade 0 same-as-color:

```text
L0 -> trunk full + L0 branch wood + L0 foliage
L1 -> trunk full + L1 branch wood + L1 canopy
```

For cascades 1+:

```text
L0/L1/L2/TreeL3 -> TreeL3 or validated TreeShadowLod
```

This prevents cascade multiplication of branch work.

### 6. Treat offscreen shadows as a bounded local ring

Offscreen ring is not "everything in the shadow frustum."

It is only:

```text
tree bounds intersects camera sphere radius OffscreenCasterRadius
tree not already color-visible
tree intersects current cascade frustum
```

Default caster is cheap tree only.

This covers nearby trees just outside the camera that can cast into the visible foreground without exploding the forest-wide shadow caster set.

### 7. Split shadow defaults lower than color

The exact values need profiling, but defaults should express intent:

```text
ColorMaxVisibleInstances = current production value
ColorMaxExpandedBranchWorkItems = current production value
ColorMaxApproxWorkUnits = current production value

ShadowMaxVisibleInstances = much lower than color
ShadowMaxExpandedBranchWorkItems = only enough for cascade-0 visible L0/L1
ShadowMaxApproxWorkUnits = based on cheap tree casters, not color equivalence
```

Do not make shadow budgets equal to color by default.

### 8. Make active-slot filtering production behavior

Submission should not need async CPU readback to skip empty slots.

Best next step:

```text
GPU builds active shadow/color draw command list
RenderMeshIndirect submits command-count based batches
```

If staying with one args record per draw slot for now, active slot readback may remain a transitional path, but it must not be gated by diagnostics.

### 9. Migration order

1. Add `fixShadows.md` and route it from `FeatureRouter`.
2. Add `VegetationShadowMode`.
3. Replace renderer feature toggles with `ShadowMode`.
4. Remove `_ShadowProxyOnly` from production compute path.
5. Add shadow caster mapping function.
6. Add shadow cascade emission kernels that consume accepted color state.
7. Add offscreen 5 m ring classification.
8. Remove production registration of `ShadowProxyL0/L1` draw slots.
9. Update validator to reject runtime shadow proxies and validate optional `TreeShadowLod` only if introduced.
10. Update README and `VegetationRuntimeArchitecture.md`.
11. Add EditMode tests for tier mapping and validation.
12. Profile with shadows off, cheap tree cascade 0, cheap tree cascades 0-1, and current baseline for comparison.

### 10. Verification criteria

Correctness:

- A visible `L0` branch cannot receive a shadow from a larger `ShadowProxyL0`.
- A visible `L1` branch cannot receive a shadow from a larger `ShadowProxyL1`.
- Cascade 0 can emit same-as-color branch/trunk casters.
- Cascades 1+ emit cheap tree casters only.
- Offscreen casters appear only inside the configured 5 m camera radius.
- Impostor shadows are skipped by default.

Performance:

- Shadow prepare must not run full `AcceptTreeTiers` per cascade.
- Shadow branch work must be zero for cascades 1+ by default.
- Production active-slot submission must not require diagnostics.
- Shadow GPU memory must not duplicate color-sized expanded branch capacity by default.

Operability:

- Diagnostics must separate color and shadow counts.
- Validation must catch illegal shadow caster bounds and cost.
- Missing shadow-compatible material pass must skip that slot with diagnostics, not throw in render graph callbacks.

## open questions (numbered)

1. Should `OffscreenCasterRadius` be measured from camera position against tree sphere, or from near-plane corners against tree bounds? Recommendation: camera sphere against tree sphere first because it is cheaper and deterministic.
2. Should cascade 0 same-as-color self-shadowing include source `L0` foliage mesh, or should `L0` shadow use `L1` canopy to avoid source foliage triangle spikes? Recommendation: start with true same-as-color for correctness, then add a profiled quality setting only if needed.
3. Should optional `TreeShadowLod` exist now, or should `TreeL3` be the only cheap caster for the first redesign pass? Recommendation: use `TreeL3` only first. Add `TreeShadowLod` only after profiling proves it is worth another asset and validation path.
4. Should SceneView use the same 5 m offscreen radius? Recommendation: yes by default, with diagnostics visible, because SceneView should expose production behavior rather than a special editor-only shadow model.
5. Should `RenderMeshIndirect` migration happen in the same change? Recommendation: no. Fix shadow correctness and cost first. Migrate indirect backend after shadow behavior is stable.
