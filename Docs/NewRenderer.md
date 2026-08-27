# ReLunacy renderer - architecture (branch `new-renderer`)

## What we're optimising for (measured, not assumed)

- Levels are **static geometry**: the same meshes/materials/transforms every frame. Only the **camera** moves.
- The frame is **CPU-submission bound with an idle GPU** (profiled). The cost is the *number of command-list calls* x Veldrith's managed per-call tax (~2-4 us). A dense view records ~40k calls/frame (~16k `DrawIndexed`, ~24k binds) => ~110 ms in `Draw Record` alone, even after pipeline-cache + instancing + sort fixes.
- This is an **editor**, not a game: free-fly camera (no gameplay draw-distance guarantee), gizmo editing, per-entity selection/picking.

A senior take: on PS3 this was cheap because libgcm draw calls were nearly free and the game pre-built display lists with LODs + portal culling. We don't have cheap calls (managed Vulkan wrapper), so the equivalent move is **build the command stream once and replay it** - stop paying the per-call tax every frame.

## Core principle: retained command stream, recorded once, replayed per frame

The scene's draw commands are identical frame to frame. So:

1. Build a **retained render list** from the entities once (on level load / scene change).
2. **Record it into a dedicated CommandList once** (Begin...End), and keep that recorded list.
3. Each frame: update the small per-frame uniforms (view/projection, light), **submit the pre-recorded list**, present. The 40k calls happen **once**, not 60x/second.
4. Per-frame CPU cost collapses to: a couple of uniform updates + one `SubmitCommands` + present.

Everything that legitimately changes per frame is tiny and goes in a **separate per-frame CommandList**: selection outline, gizmo, axis widget, bounding spheres, overlays. That's a handful of draws.

### Why this works with an editor

- **Camera**: view/projection live in a uniform buffer the shaders already read. Moving the camera updates that buffer - **no re-record**.
- **Editing a transform** (gizmo drag): transforms live in the storage buffer (already implemented); the recorded list reads them by `gl_InstanceIndex`. Updating one SSBO slot changes the object's position **without re-recording**.
- **Selection**: highlight is a separate per-frame pass over the one selected entity - never touches the retained list.

### When we DO re-record (set a `dirty` flag, rebuild+re-record next frame)

- Level load / unload.
- Show/hide toggles (mobys/ties/ufrags/foliage/volumes), lighting on/off (changes the effect), MSAA/resize (new `OutputDescription` / pipelines).
- Adding/removing an instance. (Moving one does **not** - SSBO update only.)

## Culling decision

Drop **per-frame** frustum culling for the retained list - record *all* opaque/translucent geometry once and let the idle GPU draw it. GPU headroom is real (that's why GPU usage is low). If a level ever turns GPU-bound, add **coarse, infrequent culling** that only re-records when the visible set materially changes (e.g. crossing zone boundaries), never every frame. This trades a little GPU for ~all of the per-frame CPU.

## Data model

```
RenderItem            // one draw
  pipeline            // resolved SimplePipeline (from our reference-keyed cache)
  materialSets        // MaterialBuffer + the textures that differ (deduped at build)
  mesh                // vertex/index buffers
  firstInstance,count // range into the transform SSBO (instanced draw)

RenderPassList        // opaque list + translucent list, each: RenderItem[] sorted by
                      // (pipeline, material, mesh) so recording binds each state once

TransformStore        // one StructuredBufferReadOnly of all instance world matrices,
                      // indexed by gl_InstanceIndex; updated in place on edit
MaterialStore         // (future) material params in one buffer indexed per-draw, to kill
                      // the per-material MaterialBuffer bind
```

The retained list is built by walking `EntityManager` once, exactly like `DecalAwareForwardRenderer.Draw` does today (sort -> batch by mat+mesh -> instanced ranges), but the *output is data* (RenderItems), not immediate command-list calls.

## Passes (frame) - AS BUILT

One command buffer, re-recorded each frame with only the visible draws, one submit. Three render passes over a shared depth-stencil buffer:

1. **Opaque** -> colour + depth. Depth-writing work first (Opaque/Cutout, Soft-Edge's alpha-tested depth prepass, foliage billboards), then Additive (depth-tested, no write), then the editor's volume wireframes.
2. **Accumulate** -> RGBA16F accum + R16F reveal. Overlay/Scunge/Blended and Soft-Edge's colour pass, blended commutatively (weighted-blended OIT), depth-tested but not writing. No sorting, no re-record.
3. **Resolve** -> a fullscreen triangle composites accum/reveal over the opaque colour, then the selection outline draws on top of the finished image (stencil mask-and-inflate).

Every non-opaque draw carries the game's polygon offset (`depthBias -87`, `slopeScaled -0.33972`, from a RenderDoc capture). ImGui gizmos still render over the resulting texture on the ImGui side.

## Reuse, don't rewrite-from-scratch

The hard-won correct pieces stay and become the *builder* for the retained list:
- Instancing via `InstanceTransforms` SSBO + `gl_InstanceIndex` (works; `AssetManager.BuildLitModelEffect` declares it as `StructuredReadOnly`).
- Reference-keyed pipeline cache.
- (material, mesh) coherence sort with the packed primitive key.
- Per-slot texture-bind dedup.
- The lit-effect material/texture layout, lighting, cubemap, baked-light plumbing.

The genuinely *new* part is small and surgical: (a) emit RenderItems instead of calling the CommandList directly; (b) own a retained CommandList recorded on `dirty`; (c) replay it each frame; (d) move selection/gizmo into a per-frame list; (e) the `dirty` triggers.

## Migration stages (each independently testable)

1. **Retained record + replay** - record the current scene draws into a renderer-owned CommandList once, replay each frame; re-record on a coarse `dirty` flag (any level/visibility change). Verify visuals identical; watch `Draw Record`/frame time collapse. *(Biggest win; do first.)*
2. **Move camera/light updates out of the recorded list** into per-frame uniform writes, so camera motion needs no re-record.
3. **SSBO-update-on-edit** so gizmo drags don't re-record.
4. **Split selection/gizmo/overlay** into a separate per-frame list.
5. **(Optional) MaterialStore** - collapse the per-material `MaterialBuffer` bind into one indexed buffer, cutting the remaining `Binds` cost.
6. **(Optional) submesh/mesh-buffer merge** at load - fewer meshes => fewer draws even in the retained list.

Stages 1-4 remove the per-frame floor entirely for a static scene. 5-6 shrink the one-time record cost (matters on re-record).

## RESOLVED: Veldrith blocks record-once (measured from the DLL)

`Veldrith.Vk.VkCommandList.Begin()` acquires a fresh/recycled command buffer (`GetNextCommandBuffer()`) and calls `vkBeginCommandBuffer` with `VkCommandBufferUsageFlags = 1` = **ONE_TIME_SUBMIT_BIT**. `SIMULTANEOUS_USE_BIT` (0x4) is never set anywhere. So a recorded Veldrith CommandList is, by contract, submit-once-then-re-record. Vulkan validation forbids resubmitting it across frames. **Record-once/replay is impossible through Veldrith's CommandList.**

Veldrith is built on **Vortice.Vulkan** (raw bindings), which is therefore already a transitive dependency. Two ways to get record-once:
- **Hybrid**: keep Veldrith for device/resources (buffers, textures, pipelines, descriptor sets, swapchain, ImGui) but record the SCENE into our own `SIMULTANEOUS_USE` VkCommandBuffer via raw Vortice.Vulkan, submitted each frame. Needs Veldrith to expose the raw `VkPipeline`/`VkDescriptorSet`/`VkBuffer` handles (unverified) and render-pass compatibility.
- **Full raw-Vulkan renderer**: own the whole pipeline on Vortice.Vulkan / Silk.NET.Vulkan. Maximum control (record-once, secondary buffers, bindless, custom allocator) and the cleanest home for animations/particles/splines - but a multi-session, hardware-tested build that also re-ports the material/lighting/effect system (the game-faithful shader port).

## Two independent levers (decide per goal)

1. **Fewer command-list calls** - backend-agnostic, reuses everything, low risk. Merge mesh buffers (kill per-mesh vertex/index binds), consolidate/atlas materials, merge same-material submeshes at load. Realistic target ~5-8k calls => ~15-20 ms even on Veldrith's per-call tax. **Fastest route out of "critical".**
2. **Eliminate per-frame recording** (record-once) - the structural end-state, but needs raw Vulkan per above. The right long-term answer, especially with animations (bone SSBO), particles/splines (per-frame dynamic pass) incoming.

## LOCKED DECISION (2026-08-06)

**Build a from-scratch raw-Vulkan renderer, sharing Veldrith's device during the staged migration.**

- **Binding: Vortice.Vulkan 3.2.3** - forced: it's the exact version Veldrith uses, so the handles it hands back are Vortice types. Added as a direct dependency of ReLunacy.Engine.
- **Device sharing:** `GraphicsDevice.GetVulkanInfo()` -> `Veldrith.BackendInfoVulkan` exposes `Instance / Device / PhysicalDevice / GraphicsQueue` as raw `nint` + `GraphicsQueueFamilyIndex` (uint). The new renderer wraps these into Vortice `Vk*` handles - NO second device/instance. The window, swapchain, ImGui, present, and resource creation stay on Veldrith throughout the migration; only the SCENE 3D pass moves to raw Vulkan.
- **Record-once:** our own command pool + command buffers recorded with `VK_COMMAND_BUFFER_USAGE_SIMULTANEOUS_USE_BIT`, recorded on scene-change and re-submitted every frame to the shared queue (fenced). This is the whole point - the ONE_TIME_SUBMIT limit was Veldrith's, not Vulkan's.
- **Render target:** the scene renders into a `VkImage` (its own, or Veldrith's `RenderTexture2D` image via the exposed handle) that ImGui already displays - so View3D's `ImGui.Image(...)` path is unchanged.

### Stage roadmap (each = a build-and-run milestone you verify on hardware)

0. **Acquire** - `VulkanContext` wraps the shared Instance/PhysicalDevice/Device/Queue from `GetVulkanInfo`, creates a command pool + fences. Dormant; app unchanged. *(compiles / no-op)*
1. **Clear** - render a solid colour into a `VkImage` via a raw `SIMULTANEOUS_USE` command buffer, recorded once, submitted each frame; display it (prove device-share + record-once end to end).
2. **One triangle / one mesh** - a pipeline (SPIR-V from our existing GLSL), a vertex/index buffer, a descriptor set (camera UBO), one draw. Prove pipelines + descriptors + the shared render pass.
3. **Scene build** - walk EntityManager once into a retained RenderItem list (reuse the sort/batch/instance logic), record it once, replay. This is where `Draw Record` per-frame goes to ~0.
4. **Materials/textures/lighting** - port the lit effect's descriptor layout (transforms SSBO, material buffer, the 7 textures, light UBO, cubemap). Reuse the GLSL + the AssetManager texture/material data (share Veldrith `VkImage`/`VkBuffer` handles rather than re-uploading).
5. **Dynamic pass** - per-frame command buffer for selection/gizmo/overlay; camera/light UBO writes (no re-record on camera move); SSBO transform writes on gizmo edit (no re-record).
6. **Invalidation + culling** - `dirty` re-record triggers; optional coarse culling. Then animations (bone SSBO), particles/splines (dynamic pass).

Swap the new scene pass in behind a flag; keep DecalAwareForwardRenderer working until stage 4 is verified, then retire it.

## Dynamic features fit the retained/dynamic split cleanly

- **Skeletal animation**: bone matrices in a per-object SSBO region, updated per frame; the recorded draw reads them by index - no re-record, just a buffer write.
- **Particles**: dynamic geometry => a small **per-frame** dynamic pass (instanced quads), never in the retained list.
- **Splines**: static level splines => retained; editor path-editing => per-frame debug pass.
- **Selection/gizmo/overlay**: always the per-frame pass.
