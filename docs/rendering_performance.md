# Rendering performance: volumes and effects

Measured headless with `dev/tools/perfprobe` (RX 7600, 1280x720, pipelined frames, one level per run).

## Volumes
- Before: every wireframe edge was its own push-constant update plus draw. Metropolis: 10,880 edges per frame;
  ACiT axiom_city: 14,848. The per-frame list also composed each edge matrix from a Transform and allocated
  (102 KiB/frame, about 0.7 ms).
- Now: edges are instance data (96 bytes) in one host-mapped buffer per frame slot, drawn with one instanced draw
  (`VulkanRenderer.VolumeEdges.cs`), frustum-culled by edge bounding sphere while copying. Picking draws the same
  buffer. `EntityVolume.WorldEdgeMatrices` caches the matrices; the view copies them without allocating.
- Result (metropolis, scene 1.49 ms/frame): volumes cost 0.83 ms (2.32 vs 1.49) before, 0.03 ms after. List build
  0.7 ms / 102 KiB to about 0.05 ms / 3 KiB. Pick with volumes 4.5 to 3.5 ms.
- Fills (push constants per volume, 902 on metropolis with fill All) cost about 0.05 ms CPU; left as is, since
  grouping by shape would change the blend order.

## Effects
- `SetEffectSprites` waited for the GPU every frame (serialised CPU and GPU); the sprite buffer is now per frame slot.
- `EffectEmitter.Scalar/Color` used LINQ (closure + enumerator) and ran per particle per step: 300 to 900 KiB/frame
  of garbage. Now a plain loop, and the spin track is hoisted out of the particle loop: 0 to 13 KiB/frame.
- Sprite sort uses `Array.Sort` on float keys instead of a delegate sort of tuples.
- Metropolis worst spot (3,622 sprites): sim 0.65 to 0.48 ms, batch 0.45 to 0.36 ms.

## Remaining
- Scene GPU time dominates (metropolis 13 to 18 ms for 34k draws), not volumes or effects.
- Particles are still heap objects (pool them); sprites are CPU-built quads; no soft-particle depth fade.
- VolumeFilterPanel recomputes its LINQ aggregates every frame while open.
