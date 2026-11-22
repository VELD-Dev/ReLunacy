# New-engine Tie assets: what writing one takes

Status 2026-11-16. Research only. Nothing here has been tested in game. The probe is
`dev/tools/tieasset` (section 9). Game files were opened read-only; space_sector_1 uses the shipped
`level_cached.psarc.2026-11-12_01-20-52.bak`.

The three planned features are:

1. **Curve to moon.** Copy a Tie and bend it onto a sphere.
2. **Import.** Build a Tie from a model file, using existing shaders only.
3. **Bake.** Prelight new Ties with the region's lights and bake-time point lights.

All three need a written Tie **asset** (the class geometry), not just a placement. Placements are
already handled (`newengine_tie_append.md`, `newengine_new_layers_zones.md`).

Labels: **PROVEN** means exact over the stated corpus or read back through the engine. **STRONG**
means a structural or statistical match with the consumer not traced. **OPEN** means not settled.

---

## 0. Verdicts

| Feature | Verdict | Effort |
| --- | --- | --- |
| 1. Curve to moon, in place (the class has one placement, or every placement should bend) | **Feasible now.** The probe bends geometry and collision in place and reads both back. | 1–2 days for the engine API and UI, plus an in-game test |
| 1. Curve to moon, as a bent copy (new class, one placement re-pointed) | **Feasible now for the render.** The probe registers the copy and reads it back with LevelReader. The copy's collision needs one more writer. | About 1 week: copy and register (2 days), collision class entry and shapes (2–3 days), UI |
| 2. Import from a model file | **Feasible, with a few fields copied from a template** (section 7). This needs a container writer, a collision cooker and a lighting choice. | 2–3 weeks |
| 3. Bake | **QForce-style prelight is decoded, but A Crack in Time's is only partly decoded.** On ACiT the prelight carries the key light, and its intensity bits are OPEN. Lightmap pairs need an unwrap and a DXT writer. | Prelight 1–2 weeks once the ACiT word is closed (about 3 days of research). Lightmaps: 2+ weeks more |

Recommended order (section 8): curve to moon in place, then the bent copy, then the ACiT prelight
research and the prelight baker, then import, then lightmaps.

---

## 1. Where a Tie asset lives

| Piece | Where | ReLunacy reads it? |
| --- | --- | --- |
| The class container | `ties.dat`, one IGHW 1.1 container per class, laid end to end on a 0x80 grid (2450/2450 ACiT payloads 0x80-aligned) | yes, `TieReader` / `Objects.Tie` |
| Pointer + length | `assetlookup.dat` 0x1D300 `{u64 TUID, u32 offset, u32 length}`. Sorted by TUID, with ascending offsets, on 21/21 ACiT, 9/9 A4O and 7/7 ITN levels | yes |
| Region mask | `assetlookup.dat` 0x1D301, one u32 per 0x1D300 row | yes |
| Region list | `<region>/region.dat` 0x1C300, the region's sorted Tie TUIDs (not 0x1C600, which holds Mobys). Shaders are in 0x1C100 | read by the writers |
| Zone class table | zone blob 0x7200 (u64 TUIDs). Placement record +0x50 (u32) indexes it | yes |
| Collision | `<region>/collision.dat` 0x2900. The class TUID table is at payload +0x68 (count) and +0x6C (table). Instances owned by `0x20000000 \| class index`; shapes are in class-local space | yes (`LevelCollision`); written by `CollisionWriter`, `TieAppender` and `CollisionBvh` |
| Prelight / lightmaps | per **placement**, in the zone's lighting.dat slice: placement +0x54 (prelight words), +0x5A (0x5400/0x5410 pair) | yes |
| Budgets | `assetstats.dat` 0x9800: +0x2C Tie classes (region peak of 0x1C300), +0x00 resident pool, +0x08 region.dat buffer | written by `TieAppender` |

**The container is self-contained** (PROVEN). Its only pointers are 3 fix-ups, all internal:
0x3400 +0x00 → 0x3300, +0x04 → 0x3F00 and +0x64 → 0x3410. Measured on both accounted classes.
`IghwRelayout.Relayout` with nothing changed reproduces **2450/2450 ACiT, 2025/2025 A4O and
1219/1219 ITN** containers byte for byte. So a grown or new container can be laid out by the
existing code.

Section sets (PROVEN, whole corpus):

| Game | Sets |
| --- | --- |
| ACiT | `5600 5B00 3000 3200 3F00 3300 3500 3410 3400` ×2185. Plus `5C00 5C01` (UV animation) ×210. Plus `5800 5700 5900` (embedded texture) ×55 |
| A4O / ITN | the same, plus `3900` (material sets) on every class |

The file order of the sections is as listed. Every section starts on a 0x80 boundary and the gaps
are `PAD0..PADF`.

---

## 2. Byte accounting

### 2.1 space_sector_1, moon1a surface: `volcanic_moon` (TUID 437A81EB01581048)

The class is `shared/gameplay_objects/volcanic_moon`. It has one placement in `art_moon1a`. Its
largest coordinate is 50.15, so it is the 50-radius moon sphere.

- ties.dat +0x2BDA00, length 0x9720C (619,020 B).
- 0x1D300 row 13; mask 0x00000001.
- region.dat 0x1C300 index 13 of 52 (sorted); its 3 shaders are all in 0x1C100.
- collision.dat class index 18 of 46, with **79 instances** owned (79 chunks).

| Offset | Section | Size | Content |
| --- | --- | --- | --- |
| 0x00000 | header + table | 0x20 + 12×16 = 224 | `IGHW` 1.1, 12 sections, header 0xE0, fileLength 0x97200, 3 fix-ups, `DEADDEADDEADDEAD` |
| 0x00100 | 0x5600 | 3 × 8 = 24 | shader TUIDs 38A184B706998F73, 19ECCFD6FFE54E65, 60C89AB006999F3A |
| 0x00180 | 0x5B00 | 3 × 1 | `00 00 00` |
| 0x00200 | 0x5800 | 524,288 | embedded texels: one 1024×1024 DXT1 (0x5700 descriptor `…0400 0400…`) |
| 0x80200 | 0x5700 | 1 × 0x20 | RSX descriptor |
| 0x80280 | 0x5900 | 1 × 8 | id `9804F1DB 00080000` |
| 0x80300 | 0x3000 | 68,780 | 2461 vertices × 20 B (49,220) + 4890 Half2 (19,560) |
| 0x91000 | 0x3200 | 18,468 | 9234 u16 indices |
| 0x95880 | 0x3F00 | 64 × 0x20 | per-primitive AABB |
| 0x96080 | 0x3300 | 64 × 0x40 | primitives |
| 0x97080 | 0x3500 | 3 × 0x10 | per material slot |
| 0x97100 | 0x3410 | 63 | name |
| 0x97180 | 0x3400 | 0x80 | definition |
| 0x97200 | fix-ups | 3 × 4 | +0x97180, +0x97184, +0x971E4 |

Total: header and table 224, sections 617,986, PAD filler 798, fix-ups 12, tail 0. That is
**619,020 of 619,020**.

0x3400 fields:

- scale (0.001531, 0.001531, 0.001531) = 50.15172 / 32767.
- sphere (0.34, 1.38, 0.47) r 51.95.
- AABB half (50.15, 50.15, 50.15), centre 0.
- +0x1C 4096.0; +0x74 0x0000E678 (no fix-up); +0x78 B4E00C00.
- max |q| = 32767 on all three axes.

The primitives:

- **63 lightmapped primitives** (+0x0E = 01) on shader 0. All have mask +0x0A = 0000, fades
  (50, 4000, 8, 1) and +0x24 = 0x1D.
- **One 16-vertex unlit primitive** on shader 1. Fades (35, 120, 8, 1).

The UV array has **2 × 2445** entries:

- The first 2445 are the lightmap unwrap (range 0.0076..0.9946).
- The second 2445 equal each vertex's own UV.
- These are ACiT two-layer primitives (section 6.4).

Vertex 0: `529D 35AD 529D 8080 3C00 3900 588BDA7A AA009AF5`. That is q (21149, 13741, 21149),
w 0x8080, uv (1, 0.625), n (0.62, 0.37, 0.69), t (0.74, 0.02, −0.67). Every w on this class is in
the 0x80xx band.

### 2.2 krell_canyon, ground rock: `lv_webtacle_barnacle` (TUID 04BCE228D2C511C5)

- ties.dat +0x51500, 73,916 B. 11 sections, fileLength 0x120B0, 3 fix-ups. Region list index 3 of
  200; collision class index 2 of 189 with 23 instances.
- Layout: 0x5600 (3 shaders), 0x5B00, 0x3000 (2882 × 20 + 205 × 4 = 58,460), 0x3200 (6270 indices),
  0x3F00 ×5, 0x3300 ×5, 0x3500 ×3, 0x3410, 0x3400, 0x5C00 (80 B), 0x5C01 (1200 B).
- Total: 208 + 73,050 + PAD 646 + fix-ups 12 = **73,916 of 73,916**.
- Scale (0.000031, 0.000031, 0.000031), max |q| (23692, 31733, 25871). This is a small object that
  does not fill the range.
- Primitive vertex starts are out of order (208, 0, 508, 47, 1300). Only primitives 1 and 3 are
  lightmapped, so the UV array holds 47 + 158 = 205 entries.
- The unlit primitives point +0x06 at the array end (208).
- Vertex w values: 7F80 ×394, 80xx ×2485.

---

## 3. Record layouts (ACiT unless noted)

### 3.1 0x3400 definition (0x80; 0x90 on A4O/ITN)

| Offset | Field | Status |
| --- | --- | --- |
| +0x00 / +0x04 | → 0x3300 / → 0x3F00 (fix-ups) | PROVEN |
| +0x0E | flags: 00 on 2447/2450 ACiT (02 on 3) | OPEN, copy 0 |
| +0x0F | primitive count (max 64 in all three games) | PROVEN |
| +0x18 | vertex block bytes (20 B/vertex) | PROVEN |
| +0x1C | f32 distance: 4096 on 2340/2450; 10000, 5000… | OPEN (copy 4096) |
| +0x20 | per-axis scale; position = q × scale, **no offset** | PROVEN (AABB matches decode within one quantum on 5694/5694) |
| +0x2C | 1.0 | PROVEN |
| +0x30 | sphere: radius = farthest vertex from its centre (5694/5694). The centre is **not** the AABB centre (81/2450) | radius rule PROVEN; centre rule OPEN, any enclosing sphere is valid |
| +0x40 / +0x50 | AABB half extents / centre of the decoded vertices (within one quantum) | PROVEN |
| +0x64 | → 0x3410 name (fix-up) | PROVEN |
| +0x68 | u64 TUID = the 0x1D300 key | PROVEN |
| +0x74 | FFFFFFFF, or an offset into 0x5800 on the 55 embedded-texture classes (not fixed up) | STRONG |
| +0x78 | packed bytes (78800B12, B4E00C00, 02B00022…) | **OPEN** (copy from a template) |

### 3.2 0x3300 primitive (0x40)

| Offset | Field | Status |
| --- | --- | --- |
| +0x00 | u32 first index in 0x3200 | PROVEN |
| +0x04 | u16 first vertex (vertex blocks may be out of order or padded: contiguous on 1598/2450) | PROVEN |
| +0x06 | u16 first lightmap UV entry. Unlit primitives hold the next lightmapped start (13424/13745) | PROVEN |
| +0x08 | u16 vertex count. Indices are **local** u16 (36622/36622) | PROVEN |
| +0x0A | u16 blend-mask index (FFFF = single layer) | PROVEN |
| +0x0C | 00 on 36622/36622 | PROVEN |
| +0x0D | batch look-ahead: following primitives with the same material and gate, capped at 16 (moon: 16…16, 15, 14 … 0) | STRONG |
| +0x0E | bit 0 = lightmapped (the placement's pair is bound) | PROVEN (QForce EBOOT) |
| +0x0F | LOD range count 1..4. 34415 of 36622 primitives have 1, with that range repeated in all four slots | PROVEN |
| +0x10..+0x1F | four `{u16 index offset, u16 count}` triangle-list ranges. Each LOD is its own index list over the **same vertices** | PROVEN |
| +0x21 / +0x2A | material slot (into 0x5600), stored twice (36622/36622) | PROVEN |
| +0x22 / +0x28 / +0x2B | two-layer: second material slot, second-UV window start, slot copy. FFFF/FFFF/FF on 36029 | STRONG |
| +0x24 | **ACiT feature mask**: 0x1D on 35582/36622, then 0x19, 0x14, 0x10, 0x0D… This is QForce's +0x20 value set. On A4O/ITN +0x24 is 0 and +0x20 holds it | STRONG. ReLunacy forces 0x1F on ACiT; it should read +0x24 |
| +0x2C / +0x2E | u16 pair, non-zero on every multi-LOD primitive of ss1 and krell (`(0, 25)`, `(0, 750)`, `(0, 95)`) | STRONG: LOD distance |
| +0x30..+0x36 | s16 fades: normal map, reflection, detail, 1 (moon 50/4000/8/1; props 35/120/8/1) | PROVEN (QForce) |
| +0x38..+0x3F | zero (36622/36622) | PROVEN |

### 3.3 Other sections

| Section | Content | Status |
| --- | --- | --- |
| 0x3F00 (0x20 per primitive) | `{half extents, 0, centre, 0}` of the primitive's vertices (within one quantum) | PROVEN, 36622/36622 |
| 0x3500 (0x10 per slot) | `{u32 slot index, f32 u density, f32 v density, 0}` | slot and zero PROVEN (12059/12059). The densities are **UV units per world unit**: log-correlation −0.97 with √(UV area / 3D area) over krell's slots, and exactly 1/16 and 1/8 on tiling materials. STRONG |
| 0x5B00 (1 per slot) | 00 on 12016/12059 | OPEN, write 0 |
| 0x3000 | vertex block, then a packed Half2 array: one entry per vertex of each lightmapped primitive. The two-layer classes add a second run (section 6.4) | PROVEN |

### 3.4 Vertex (20 bytes, VertexFormat0)

| Offset | Field | Status |
| --- | --- | --- |
| +0x00 | s16 × 3 position, × the 0x3400 scale | PROVEN |
| +0x06 | s16 w | see below |
| +0x08 | Half2 UV | PROVEN |
| +0x0C | normal | PROVEN |
| +0x10 | tangent | PROVEN |

- **Normal and tangent** are signed 11:11:10 in **object space**. They are unit length on
  7,232,742/7,232,800 ACiT vertices, and the normal is perpendicular to the tangent within 0.05 on
  96%.
- **w sign = −(UV handedness).** The sign of w disagrees with `sign((N × T) · ∂P/∂v)` on
  6,984,502/6,994,478 ACiT vertices (99.86%), on 99.7% of A4O and on 99.4% of ITN. The magnitude
  sits in 0x7F80..0x807F (|w| ≈ 0x7F80 + 7 bits). In ACiT, 0x8000 is 73% and 0x7F80 27%; the other
  bands are rare.
- A writer keeps w from the source vertex (curve to moon), or writes ±0x7F80 with the sign from
  handedness (import). The low 7 bits are the reported vertex alpha (VertexFormat0 bands).

**Reader bug found.** `TieMesh.GetBuffers` reads the packed normal as if it were in quantised space
(n / scale) and the tangent as t × scale. On classes whose scale anisotropy is 2 or more, the raw
normal is the closer one to the geometric normal: mean 15.4° against 28.0° on ss1 (84,725 of
110,623 vertices), and 11.9° against 21.9° on krell. So the editor shades anisotropic Ties with
bent normals. This is reported only; it was not fixed here.

---

## 4. Quantisation and the bend

- The scale is per axis and there is no offset. **At least 90% of classes saturate an axis at
  32767** (p10 of max|q|/32767 is 1.0 in all three games), and 2299/2450 ACiT scales are
  non-uniform. So the range is fitted to max |coordinate| per axis.
- Any vertex that the bend pushes past the old extent on an axis needs a larger scale on that axis.
  That is one float at +0x20 plus a requantisation of every vertex of the class, in place, with no
  size change.
- The cost is a coarser step of max|coord| / 32767. That is 0.0015 units at 50 and 0.002 units at
  64, which is harmless.
- There is no hard limit, but off-centre geometry wastes range because there is no offset. The ss1
  solar array spans −14..64 on X.
- **Normals and tangents.** The probe maps them through the bend's Jacobian (n by J⁻ᵀ, t by J).
  There is no need to recompute them from topology, so hard edges and normal-map frames survive.
  The bend keeps orientation, so the handedness bit of w stays valid.
- **What must also change:**
  - the 0x3F00 boxes;
  - the 0x3400 AABB and sphere;
  - 0x3500 only if the UV-to-world ratio changes (arc length is preserved, so it barely does).
- **Unchanged and still valid:**
  - indices, the LOD ranges, UVs and lightmap UVs (same vertices, same order);
  - the placement's prelight window and lightmap pair (vertex-indexed).
  The baked values are stale, but they bind correctly.

---

## 5. Adding a new class (TUID) and its budgets

What the probe's `--copy` mode wrote (PROVEN loadable in ReLunacy; not tested in game):

1. **ties.dat**: the container is appended at the next 0x80 boundary. +0x68 = the new TUID, which
   is the largest 0x1D300 TUID + 1, unused by every lookup table. That keeps 0x1D300 sorted with
   ascending offsets, as retail is on every level.
2. **assetlookup**: 0x1D300 gets `{TUID, offset, length}` and 0x1D301 gets the source's mask. The
   0x1DA00 zone offsets are updated, and the file is re-laid with `IghwRelayout`.
3. **region.dat 0x1C300**: the TUID is inserted sorted. No 0x1C100 or shader change is needed
   because the shaders are already in the region. Textures are bound inside the shader resources,
   so they need nothing either.
4. **zone**: 0x7200 + TUID, and one placement's +0x50 = the new index. The zone is re-laid and
   spliced into zones.dat, and later zones move.
5. **assetstats 0x9800**:
   - +0x2C Tie classes = the largest 0x1C300 count, 52 → 53 on ss1. The loader rounds it up to 16.
   - +0x08 = roundUp128(largest region.dat).
   - +0x00 resident pool += the 0x80-aligned growth of ties.dat and zones.dat (TieAppender's rule:
     22/22 ACiT levels).
6. **level_cached.toc**: the new sizes.

The copy's placement keeps its lightmap pair and prelight window, and the other placements of the
source class are unchanged (section 9).

Precedent: the Ressurgence project's cross-game sends added Tie classes to QForce levels this way,
with a full closure. One rung, M1L2, **loaded on console** after its collision tree was fixed
(`dev/ReLunacy_Ressurgence/Docs/reverse/NEW_ENGINE_ZONE_SEND_OUT_OF_MEMORY_2026-09-23.md`). The
risk it found is the memory arena: allocations return NULL silently once +0x00 is exceeded, so
large additions need the budget raised.

**Not done: the copy's collision.** collision.dat's class TUID table (+0x68 count, +0x6C table,
followed by a u16 table) would need one more entry. Then the copied, bent chunks get instances
owned by `0x20000000 | new index`. Growing the table moves everything after it, so the payload must
be rebuilt with `CollisionBvh.BuildPayload`, which is byte-exact on retail. The probe's re-pointed
placement keeps the source's unbent collision.

---

## 6. Lighting

### 6.1 How each placement is lit (measured)

| Level | Tie placements | Lightmap pair | of which also prelight (unlit primitives) | Prelight only | Neither |
| --- | --- | --- | --- | --- | --- |
| space_sector_1 | 1879 | 328 | 217 | 1543 | 8 |
| krell_canyon | 9312 | 7736 | 2429 | 1573 | 3 |

- **Prelight** is one u32 per vertex per placement, in the zone's lighting.dat slice at +0x54.
- Its word index is the primitive's +0x04 + k on an unbaked placement. On a baked placement it is
  (+0x04 − +0x06) + k, for the unlit primitives only.
- ACiT's scale is the f32 at record +0x68 (ReLunacy `CrackInTimeTieBake`).

### 6.2 The prelight word on A Crack in Time

The QForce decode is in `NEW_ENGINE_SHADER_SECTIONS_EBOOT_2026-09-27.md` §6. Tested on ACiT with the
probe's `--prelight` mode:

- **Bytes 2–3 = chroma a, b** with a² + b² ≤ 1 on **1,650,970/1,650,970** words (random bytes would
  pass 78.5%). STRONG: same as QForce.
- **Bit 0 is not sun visibility on ACiT.** It agrees with N·L for either 0x8B00 light only 47–53%
  of the time. QForce's 99% does not hold here.
- **Byte 1 carries the key light.** On krell, binned by N·L₀:
  - its mean is 20–24 for every N·L₀ < 0;
  - it **jumps to 46 at 0** and rises to 72 at N·L₀ ≈ 1;
  - byte 0 stays at 130 ± 1;
  - ss1 shows the same.

  So ACiT bakes the directional key light into the prelight. The 16-bit intensity layout of bytes
  0–1 is **OPEN**: the QForce three-basis decode is unproven on ACiT, and ACiT's own Tie programs
  have not been traced.

### 6.3 What ReLunacy decodes of the region light

region.dat 0x8B00 is 0xA0 on ACiT (`LightingEnvironmentReader.ReadNewEngineShiftedRoot`):

| Offset | Field |
| --- | --- |
| +0x40 | ambient |
| +0x50 / +0x60 | the two light colours |
| +0x70 / +0x80 | their unit directions |

For example:

- ss1: ambient (0.15, 0.22, 0.75); light 0 (3.93, 3.26, 0.38) dir (−0.81, 0.40, −0.43); light 1 dim
  blue.
- krell: ambient 2, light 0 (1.98, 1.61, 0.98) dir (−0.37, 0.82, 0.44).
- A4O/QForce use 0x1DB03 instead.

**The direction sign convention is OPEN**: the bit-0 test was run both ways. Byte 1's step at
N·L₀ = 0 suggests that +0x70 points **towards** the light, which is STRONG.

### 6.4 Lightmap pairs and two-layer primitives

- **A baked placement** has one 0x5400 (colour) and one 0x5410 descriptor in the zone, with texels
  in the slice and per-placement scales at +0x64/+0x66. No two placements share a pair in retail.
- On ACiT, 0x5410 is a light direction, not RNM weights (ZoneReader note; INFERRED).
- assetstats +0x14 counts lightmaps.
- **ACiT two-layer primitives** (+0x0A ≠ FFFF on 593 primitives; the 55 classes with
  0x5700/0x5800/0x5900 carry the mask texture) are different from A4O, which uses 0x3900:
  - the mask texture is in the class;
  - +0x22 is the second slot and +0x28 the window into a second Half2 run after the lightmap UVs;
  - on the moon, that run is a copy of the vertex UV.

  ReLunacy does not read this yet. A bent copy carries all of it unchanged.

### 6.5 What a CPU baker must output

1. **Unbaked new class** (the simplest):
   - Write the gate bit 0 clear on every primitive.
   - Give each placement a prelight block of vertexCount words in the zone slice. TieAppender
     already grows the slices.
   - Set +0x54 to the block and +0x68 to the scale.
   - The word itself needs §6.2 closed first. Bytes 2–3 can be written now; bytes 0–1 need a
     decode. Cheapest research: fit byte 1 against the analytic key light × N·L over krell's
     946,144 words, then compare the editor's rendering with retail screenshots.
2. **Baked**:
   - an unwrap (the class's lightmap UV array, with gate bit 0);
   - per placement, a DXT colour texture (alpha = key-light visibility on QForce, PROVEN there) and
     a direction texture;
   - the 0x5400/0x5410 descriptors;
   - +0x5A, +0x64/+0x66, and assetstats +0x14.

   The colour excludes the sun on QForce (STRONG). ACiT is OPEN, like its prelight.
3. **Bake-time point lights** are an editor-side concept only. Nothing in the files stores them; they
   only change the numbers the baker writes.

---

## 7. Import: what a new mesh can use

- **Vertex format**: VertexFormat0 only, 20 B. Every Tie uses it; no other Tie format exists.
- **Primitive limits**:
  - u16 vertex start, count and local indices: a hard ceiling of 65,535 vertices per class;
  - at most 64 primitives per class in all three games (+0x0F is a byte; treat 64 as the limit);
  - the largest retail primitive has 3017 vertices and 6144 indices. Split meshes to stay near that.
- **Materials**:
  - one material slot per primitive (+0x2A, duplicated at +0x21), indexing the class's 0x5600 list;
  - several primitives may share a slot;
  - "one material per mesh" maps to one or more primitives per slot.
- **Picking a shader**: any TUID that is in 0x1D100 **and** in the region's 0x1C100. Prefer one
  already used by a Tie of the level, since its pipeline class is known to work on Ties. Of ACiT's
  Tie shaders, 1116/3472 are also used by a Moby, so shader resources are not family-specific, but
  Tie-proven is safer.
- **Textures**: bound inside the shader resource (0x5A00), so there is nothing to write. The model's
  UVs must suit that material's textures.
- **Fields to write**, each rule PROVEN above unless noted:
  - scale, bounds, sphere, 0x3F00;
  - 0x3500 densities (STRONG formula);
  - w (sign from handedness, magnitude 0x7F80);
  - normals and tangents (encoder: `PackedNormal.EncodeForImport`, internal);
  - one LOD range repeated four times, with +0x0F = 1;
  - batch look-ahead (STRONG rule).
- **Fields to copy from a retail template**: 0x3400 +0x0E, +0x1C (4096), +0x78; primitive +0x24
  feature mask (0x1D, or the template's), +0x2C/+0x2E (0 for one LOD), fades (35, 120, 8, 1);
  0x5B00 = 0.
- **Collision**: a cooker is needed: chunks of at most 512 vertices, under 64 units per axis, u16
  size, with a material palette (`acit_collision_format.md` §7b). The format is known; there is no
  C# writer yet, only the Python proof for instances.
- **Model loading**: SharpGLTF is already an engine dependency (it is used by `LevelExporter`), so
  glTF import needs no new package.

---

## 8. Recommended order

1. **Curve to moon, in place** (engine API + UI). Reuse the probe's bend, Jacobian, requantise and
   bounds code, plus the collision-in-place code (`CollisionBvh.Refit`). Restrict it to classes
   whose placements all bend. Test in game on ss1.
2. **Bent copy as a new class**: the probe's `--copy` registration, plus a collision class entry
   (`CollisionBvh.BuildPayload`). This is the general curve-to-moon.
3. **Close ACiT's prelight word** (§6.2). Then write a prelight baker for unbaked placements:
   ambient, the 0x8B00 lights × N·L × shadow rays, and point lights. This also re-lights bent
   copies.
4. **Import** on top of 2 and 3: container writer, glTF split by material, shader picker, collision
   cooker.
5. **Lightmap pairs**: unwrap, DXT writer, descriptors. This is the largest piece and is optional
   while prelight works.

---

## 9. Probe: `dev/tools/tieasset`

```
dotnet run -- ~/.var/app/net.rpcs3.RPCS3/config/rpcs3/dev_hdd0/game [--account] [--corpus]
              [--bend|--copy] [--class NAME] [--radius R] [--lightstats L] [--prelight L] [--nspace L] [--slot L]
```

Patched archives go to `dev/tools/tieasset/work/<level>_patched/` and are never installed.

**`--bend`** (space_sector_1, radius 50):

- It bends a class in local space: Y up, the base laid on the sphere, horizontal distance turned
  into arc length, nothing rescaled.
- It re-encodes positions, normals and tangents, and rewrites the scale, the 0x3F00 boxes and the
  0x3400 bounds **in place**.
- It bends the class's collision chunks in place too: new s32 origin, u16 offsets, then the
  instance boxes and spheres, then `CollisionBvh.Refit`.
- It patches a copy of the archive and reloads it with `LevelReader` and `CollisionReader`.

| Class | Result |
| --- | --- |
| `yy_moon_platform_hab_rockbase_4` (auto pick, 2997 vertices) | **17/17 pass**. Y scale 0.000068 → 0.000125; positions within 0.5 quanta; raw normals 0.066°; edges dropped 1.88 units (= the sphere sagitta); 1 collision chunk bent; header bounds unchanged |
| `moon_landing_pad_blue` (1844 vertices, 4 placements, lightmapped) | **17/17 pass**. 1121 lightmap UVs and every placement's pair and prelight window unchanged; collision 196 vertices exact |
| `za_nss_solar_array1`, radius 25 (stress case: off-centre, vertical) | passes. The read-back normal reaches 1.08° only through the reader's n / scale with anisotropy 21; the raw words are within 0.06° |

**`--copy`** (`moon_landing_pad_blue`) registers the bent geometry as class FFAEBF3F06854D12 and
re-points the `art_junk_trader` placement. **10/10 pass**:

- both classes read;
- the copy is bent and the source is unchanged;
- the zone still has 102 placements, 1 of them on the copy;
- the copy's placement keeps its pair and prelight window;
- the 3 other placements are on the source;
- 0x1C300 and 0x1D300 are sorted.

**`--corpus`** gives the section 1–4 statistics over 21 ACiT, 9 A4O and 7 ITN levels (every level with a ties.dat).

---

## 10. Unknowns, ranked for these features

1. **Nothing has been tested in game.** For the in-place bend, the risk is low because only bytes of
   the same size changed. For the copy, it is the memory arena and the new class's collision.
2. **ACiT's prelight intensity bits** (§6.2) and the 0x8B00 direction convention. Both block
   feature 3 on ACiT.
3. **The copy's collision class entry.** The layout is known and the writer is not written.
4. **0x3400 +0x78 and +0x0E; primitive +0x2C/+0x2E** (the LOD distance is STRONG, the exact use is
   OPEN). Copy them from a template.
5. **ACiT two-layer second UV run.** A bent copy keeps it; an import should avoid two-layer
   materials.
6. **Collision chunks over 64 units after a bend.** None in the tests. Those would need re-chunking,
   which means new shapes and the cooker.
7. **Stale bake after a bend.** It binds correctly but is lit for the flat shape until rebaked.
