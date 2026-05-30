# Vegetation Shadows

Purpose: current shadow contract for the compiled vegetation renderer.

Status: active. Shadow rendering is owned by `VegetationRenderWorld` through the BRG light culling callback on supported raw-buffer BRG APIs. On Direct3D12 Unity `6000.3.15f1`, vegetation uses the RenderGraph grouped-indirect shadow pass because native `InjectShadowDrawCommands` crashes when custom vegetation BRG batches are registered, before vegetation BRG light-culling output exists.

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
-> Unity BRG light culling callback
   -> page/cell split-frustum mask tests from BatchCullingContext
   -> compiled shadow packet mapping
   -> shadow work budget
   -> visible instance index output
   -> split visibility masks on BRG draw commands
-> URP submits BRG shadow caster draws only for visible splits/groups
```

The production shadow path uses BRG split frustums for page/cell bounds tests on supported raw-buffer BRG APIs. The grouped-indirect RenderGraph path uses explicit cascade-frustum masks and one shared selected packet payload when BRG cannot initialize, fault-disables, or runs on Direct3D12. Cascades and groups with no selected packets are skipped through split visibility masks or fallback cascade masks.

The fallback vegetation shadow pass augments URP's main-light shadow atlas. It must run as a render-graph raster pass with `resourceData.mainShadowsTexture` bound through `SetRenderAttachmentDepth(..., AccessFlags.ReadWrite)`. Do not use an unsafe pass plus manual `SetRenderTarget` for the shadow atlas; DirectX backends can treat that as invalid render target state and leak shadow writes into the color path while Vulkan may appear to tolerate it. The BRG path must not manually bind the atlas at all.

Fallback indirect vegetation shadow draws do not go through URP renderer lists, so each cascade draw must explicitly bind the cascade shadow-slice view/projection shader globals before submitting `DrawMeshInstancedIndirect`. Do not rely on the game camera state left by URP after the main shadow pass; that makes caster projection camera-relative and causes vegetation shadows in Game View/builds to drift or rotate with camera motion. Shader globals must use URP's GPU-adjusted projection convention, not the raw projection matrix, or geometry can flip. Restore the camera view/projection globals after the vegetation shadow atlas append pass.

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
4. BRG shadow commands are emitted by compiled packet groups on supported raw-buffer BRG APIs, while Direct3D12 and unsupported/faulted BRG setup use grouped RenderGraph shadow args
5. focused render-world tests cover HLOD fallback and `CheapTree` packet selection
