# Vegetation Shadows

Purpose: current shadow contract for the compiled vegetation renderer.

Status: active. Shadow rendering is owned by `VegetationRenderWorld` and scheduled by `VegetationRendererFeature`.

## Contract

Vegetation supports two public shadow modes:

1. `VegetationShadowMode.Off`
2. `VegetationShadowMode.CheapTree`

`Off` skips vegetation shadow-caster submission.

`CheapTree` submits compiled shadow packets under `ShadowWorkBudget`. It does not use a separate runtime renderer or an independent proxy promotion path.

## Correctness Rule

Shadow packets are subordinate to compiled packet metadata. A shadow caster must not be richer or materially larger than the selected packet policy permits.

This fixes the original class of bugs where an enabled shadow path could cast from a different shape than the visible vegetation and cover the visible branch with an impossible silhouette.

## Runtime Flow

```text
main light cascade frustum set
-> VegetationRenderWorld.PrepareForFrustums()
   -> page/cell cascade-frustum mask tests
   -> compiled shadow packet mapping
   -> shadow work budget
   -> one grouped instance payload shared by all cascades
   -> one grouped indirect args surface shared by all cascades
-> grouped indirect shadow draws only for cascades/groups that have selected shadow packets
```

The shadow path uses explicit frustum/bounds tests for cascades. Camera `CullingGroup` broad phase remains camera-owned; shadow cascades are prepared directly from compiled page/cell bounds, but cascades are batched into one page/cell selection pass and must not repack static instances per cascade. Cascades and groups with no selected packets are skipped at submission time.

The vegetation shadow pass augments URP's main-light shadow atlas. It must run as a render-graph raster pass with `resourceData.mainShadowsTexture` bound through `SetRenderAttachmentDepth(..., AccessFlags.ReadWrite)`. Do not use an unsafe pass plus manual `SetRenderTarget` for the shadow atlas; DirectX backends can treat that as invalid render target state and leak shadow writes into the color path while Vulkan may appear to tolerate it.

Indirect vegetation shadow draws do not go through URP renderer lists, so each cascade draw must explicitly bind the cascade shadow-slice view/projection shader globals before submitting `DrawMeshInstancedIndirect`. Do not rely on the game camera state left by URP after the main shadow pass; that makes caster projection camera-relative and causes vegetation shadows in Game View/builds to drift or rotate with camera motion. Shader globals must use URP's GPU-adjusted projection convention, not the raw projection matrix, or geometry can flip. Restore the camera view/projection globals after the vegetation shadow atlas append pass.

## Budgeting

`ShadowWorkBudget` is independent from `ColorWorkBudget`, but both are global render-world budgets.

Budget pressure must degrade by packet quality:

```text
near detail
-> cell HLOD
-> page HLOD
-> culled
```

It must not create independent high-detail shadow work after color already degraded.

## Limits

1. Main directional light only.
2. No additional-light vegetation shadow atlases.
3. Offscreen caster policy is conservative and still needs production validation.
4. Dense cascade validation with wind enabled remains a required production test.

## Verification

Shadow cutover is intact when:

1. active renderer settings expose only `Off` and `CheapTree`
2. shadow submission runs through `VegetationRenderWorld`
3. no active package compute dispatch drives vegetation shadow selection
4. grouped shadow args are emitted by compiled packet groups
5. focused render-world tests cover HLOD fallback and `CheapTree` packet selection
