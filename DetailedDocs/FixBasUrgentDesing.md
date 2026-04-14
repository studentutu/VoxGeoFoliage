# Fix Bas Urgent Design

## Shadow Over Color Submission Leak

Problem being fixed:

- near `L0` trees intermittently render with the low-tier shadow representation on top of the main color result
- repro exists with one tree and with dense containers
- observed behavior matches prepared indirect-frame ownership corruption, not a pure shader bug

Current runtime paths involved:

- `VegetationRendererFeature.DrawMainLightShadowAtlas()`
- `VegetationRendererFeature.DrawContainers()` for depth and color
- `AuthoringContainerRuntime.PrepareFrameForCamera()`
- `AuthoringContainerRuntime.PrepareFrameForFrustum()`
- `AuthoringContainerRuntime.PrepareGpuResidentFrame()`
- `VegetationIndirectRenderer.BindGpuResidentFrame()`
- `VegetationIndirectRenderer.RenderInternal()`

Why `master` did not show this class of issue:

- `master` had one camera-oriented prepared frame per container
- current urgent redesign added shadow-frustum preparation and shadow submission, but kept one shared mutable renderer binding state
- the pipeline now has more than one prepared-frame owner, but the renderer still behaves as if only one owner exists

## critical flaws

- `VegetationIndirectRenderer` owns one mutable "currently bound" frame:
  - `gpuResidentArgsBuffer`
  - `lastBoundInstanceBuffer`
  - `lastBoundSlotPackedStartsBuffer`
  - `activeSlotIndices`
- `PrepareFrameForFrustum()` writes shadow-prepared state into that shared renderer through `BindGpuResidentFrame()`.
- `PrepareFrameForCamera()` caches by `renderFrame + cameraInstanceId` and can early-return without rebinding camera state. That is safe only if nothing else can overwrite the renderer between depth and color. That assumption is false now.
- `SlotResources.drawProperties` is mutable per-slot shared state. Buffer bindings are rewritten on every `BindGpuResidentFrame()`. That creates cross-pass and cross-consumer bleed risk on top of the shared renderer-state problem.
- The bug is architectural:
  - shadow preparation mutates shared submission state
  - depth/color cache assumes submission state is still camera-owned
  - renderer has no explicit notion of "render this prepared frame", only "render whatever was last bound"

Practical failure sequence:

1. camera depth prepares camera frame and binds it
2. shadow pass prepares frustum frame and binds it into the same renderer
3. camera color pass asks `PrepareFrameForCamera()`
4. camera prepare cache returns early because camera frame was already prepared this render frame
5. color pass renders with shadow-bound buffers or shadow-bound slot state
6. result is `TreeL3` or other shadow-tier geometry leaking into the main color output

## missing pieces

- No explicit prepared-frame handle passed from preparation to submission.
- No separation between "compute result cache" and "renderer submission binding".
- No ownership model for multiple consumers of the same container in one render cycle:
  - camera depth
  - camera color
  - shadow cascade 0..3
  - SceneView and GameView together
- No pass-safe slot binding model. Current slot property blocks are mutable shared objects.
- No frame identity in diagnostics. Logs should expose whether a draw used:
  - camera prepared frame
  - shadow prepared frame
  - which pipeline instance
  - which camera
  - which cascade

## best alternatives

### Alternative 1: Minimal patch

- Keep current pipelines.
- On every `PrepareFrameForCamera()` cache hit, rebind the camera frame into `VegetationIndirectRenderer`.
- Add separate cached camera binding payload and separate shadow binding payload.

Pros:

- small code delta
- fastest short-term fix

Cons:

- renderer still owns mutable global frame state
- shadow and camera still race through one binding surface
- mutable slot property blocks remain a bleed risk
- fragile for future multi-camera and active-slot compaction work

### Alternative 2: Stateless renderer with explicit prepared-frame handle

- `PrepareFrameForCamera()` returns a camera prepared-frame handle
- `PrepareFrameForFrustum()` returns a shadow prepared-frame handle
- `VegetationIndirectRenderer.Render(...)` receives that handle explicitly
- renderer stores only immutable slot metadata

Pros:

- correct ownership model
- fixes the current bug class, not just one manifestation
- scales to multiple cameras and shadow cascades
- no synchronous readback
- no extra compute work if camera caching remains

Cons:

- medium refactor
- touches feature, container runtime, renderer API, and diagnostics

### Alternative 3: Dedicated shadow renderer instance

- keep current binding model
- create one `VegetationIndirectRenderer` for camera and one for shadow

Pros:

- simple mental model

Cons:

- duplicates slot-side runtime state
- duplicates mutable submission surfaces
- still does not solve the broader "prepared frame should be explicit" design defect
- unnecessary memory cost for a fix that should be ownership-based

Recommended choice:

- Alternative 2

## recommended combined design

### Target rule

- preparation computes a frame result
- submission consumes an explicit frame result
- submission must never depend on "whatever frame was last bound"

### New contract

Introduce one immutable runtime record:

```text
VegetationPreparedFrameHandle
- VegetationPreparedFrameKind Kind         // Camera or ShadowFrustum
- int RenderFrame
- int CameraInstanceId
- int ShadowCascadeIndex                   // -1 for camera
- GraphicsBuffer InstanceBuffer
- GraphicsBuffer ArgsBuffer
- ComputeBuffer SlotPackedStartsBuffer
- int SourcePipelineInstanceId
- int SourcePreparedFrameSerial
- bool IsValid
```

Important:

- this handle does not own buffers
- buffers remain owned by `VegetationGpuDecisionPipeline`
- the handle is just the explicit submission contract

### Phase 1: Split compute cache from render submission

Change `AuthoringContainerRuntime`:

1. replace `HasPreparedFrame => indirectRenderer.HasUploadedFrame`
2. add camera-frame cache record:
   - cached handle
   - cached render frame
   - cached camera id
3. add transient frustum handle path for shadows
4. `PrepareFrameForCamera()`:
   - if cached and valid, return cached handle without recompute
   - do not mutate renderer state
5. `PrepareFrameForFrustum()`:
   - prepare frustum pipeline
   - return a transient explicit handle
   - do not mutate renderer state

Range-Condition-Output rule for container runtime:

- input: camera or explicit frustum
- condition: if compute result already exists for that exact owner and frame, reuse it
- output: explicit prepared-frame handle only

### Phase 2: Make `VegetationIndirectRenderer` stateless

Remove from hot-path renderer state:

- `gpuResidentArgsBuffer`
- `lastBoundInstanceBuffer`
- `lastBoundSlotPackedStartsBuffer`
- `hasGpuResidentFrame`
- `activeSlotIndices` as a global "current frame" field
- `BindGpuResidentFrame(...)` as the submission contract

Keep only immutable slot metadata:

- mesh
- material
- shader pass ids
- args buffer offset
- conservative bounds

New renderer API:

```text
Render(CommandBuffer/IRasterCommandBuffer, Camera, VegetationRenderPassMode, in VegetationPreparedFrameHandle, bool diagnostics)
```

Render behavior:

- bind frame-scoped instance buffer and slot-packed-starts buffer from the explicit handle
- issue draws using explicit handle args buffer
- never read a global "last bound" frame

### Phase 3: Remove shared mutable slot property blocks

Current `SlotResources.drawProperties` is pass-unsafe because it is mutated for every bind.

Replace it with one of these two patterns:

1. Preferred:
   - use command-buffer globals for frame-scoped buffers
   - set `_VegetationInstanceData` and `_VegetationSlotPackedStarts` once per render(handle,...)
   - set `_VegetationSlotIndex` before each draw
   - this matches the project rule better than mutable shared `MaterialPropertyBlock`

2. Acceptable fallback:
   - keep property blocks, but allocate or reuse them per prepared-frame handle, not per slot globally
   - never mutate a slot-shared property block from another pass owner

Do not keep the current hybrid:

- one shared property block per slot
- rewritten by shadow and camera prepares

### Phase 4: Wire URP passes to explicit prepared frames

Change `VegetationRendererFeature`:

1. `DrawContainers()` for depth/color:
   - call `PrepareFrameForCamera(...)`
   - receive explicit camera prepared-frame handle
   - pass handle into renderer
2. `DrawMainLightShadowAtlas()`:
   - inside each cascade loop, call `PrepareFrameForFrustum(...)`
   - receive explicit shadow prepared-frame handle
   - render immediately with that handle
   - do not store it back into shared renderer state

This preserves performance:

- camera compute still happens once per camera per frame
- color pass reuses the cached camera prepared-frame handle from depth
- shadow pass still prepares once per cascade and renders immediately
- no extra readback
- no duplicated material copies

### Phase 5: Diagnostics and guardrails

Add explicit diagnostics fields:

- `preparedFrameKind=Camera|ShadowFrustum`
- `preparedFrameSerial`
- `pipelineInstanceId`
- `cameraInstanceId`
- `shadowCascadeIndex`
- `argsBufferId`
- `instanceBufferId`

Add failure assertions in diagnostics mode:

- color pass must never consume a `ShadowFrustum` handle
- depth pass must never consume a `ShadowFrustum` handle
- shadow pass must never consume a `Camera` handle unless explicitly requested for fallback

### Phase 6: Verification matrix

Required repro checks:

1. single tree:
   - near `L0`
   - shadows on
   - confirm no `TreeL3` overlay in color
2. one container with `1000+` trees:
   - same camera path
   - confirm no frame-to-frame low-tier pop over near trees
3. SceneView + GameView both active:
   - confirm camera A and camera B do not leak prepared frames into each other
4. 4-cascade main light:
   - confirm shadow cascade preparation does not alter later color submission
5. diagnostics on:
   - color/depth must log `preparedFrameKind=Camera`
   - shadow must log `preparedFrameKind=ShadowFrustum`

### Phase 7: Follow-up performance cleanup

This bug fix should not block on these, but they should be next:

1. GPU active-slot compaction
   - renderer should stop iterating registered slots once the explicit prepared-frame contract is stable
2. actual-work-item branch dispatch sizing
   - stop dispatching branch count/emit against `visibleInstanceCapacity`
3. remove stale `HasPreparedFrame` semantics
   - replace with explicit handle validity

## implementation order

1. Add `VegetationPreparedFrameHandle`
2. Refactor `AuthoringContainerRuntime` to return handles instead of binding renderer state
3. Refactor `VegetationIndirectRenderer` to render from explicit handles
4. Replace mutable slot-shared property-block binding
5. Update `VegetationRendererFeature` depth/color/shadow paths
6. Add diagnostics and assertions
7. Validate single-tree repro
8. Validate `1000+` tree repro

## rollback rule

If the refactor is partially landed and regressions appear:

- keep shadow rendering disabled rather than shipping a renderer that can leak shadow-prepared frames into the main color pass
- do not reintroduce synchronous readback guards as a "stability" workaround
- do not duplicate full renderer instances as the long-term fix

## open questions (numbered)

1. Do we want the shadow path to keep its own transient frustum pipeline only, or do we want a small per-cascade prepared-frame pool for future parallel shadow work?
2. Do we want to restore command-buffer globals as the primary binding path now that the prepared frame will be explicit, or do we want frame-owned property-block caches?
3. Should color pass skip its own `PrepareFrameForCamera()` call and instead consume the depth-pass prepared camera handle through a pass-local context object, or should both passes independently request the same cached camera handle from `AuthoringContainerRuntime`?
