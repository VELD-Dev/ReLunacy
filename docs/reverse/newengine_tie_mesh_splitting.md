# New-engine Tie meshes and collision: how retail splits them, and an importer that does the same

Status 2026-11-17. Research only; nothing here was tested in game. It extends `newengine_tie_asset_writing.md` (container layout,
section 7 import feasibility), `acit_collision_format.md` (collision.dat) and `newengine_tie_append.md`.

Corpus (every level with a `ties.dat`, deduplicated by class content hash): **A Crack in Time 1854 Tie classes / 28,016 primitives**
(21 levels), **All 4 One 1696 / 19,203** (9 levels, 51 collision files), **Into the Nexus 1093 / 9,694** (7 levels). Collision: 10,378 / 7,675 /
3,504 unique triangle-mesh chunks plus 65 / 354 / 416 primitive sets. Old-engine ToD (3 levels, 551 Ties, 6,259 meshes) only for the cross-check
in section 4.

Probe: `dev/tools/tiesplit` (gitignored): `Program.cs` dumps `out/{prims,classes,shapes,cclasses}.tsv` for the whole corpus in about 7 s,
`a1..a8.py` and `c1.py`/`c2.py` are the analyses. Re-run: `dotnet build -c Release && dotnet bin/Release/net10.0/tiesplit.dll <game root> out`;
`--tod <levels dir> <outdir> <level>...` dumps ToD.

Labels: **PROVEN** = holds on every record of the stated corpus. **STRONG** = holds on at least 95% with a natural explanation.
**OPEN** = not determined. "Slot" is the index into the class's 0x5600 shader list (ACiT: primitive +0x2A, dup +0x21; A4O/ITN: +0x26).

---

## 1. Render meshes

### 1.1 What a "mesh" is

A primitive (0x40 record) is one draw: **one shader slot, one lightmap state, one feature mask, one index list over its own
vertex window**. A class holds at most 64 of them. There is no "one mesh per material" rule in either direction: a class has the
same number of primitives as distinct (slot, gate, mask, feature mask, fades) keys in only 984/1854 ACiT classes, and
many keys hold several primitives.

| Game | classes | classes with 1 primitive | one primitive per slot | median primitives |
| --- | --- | --- | --- | --- |
| ACiT | 1854 | 289 | 780 | 6 |
| A4O | 1696 | 295 | 798 | 4 |
| ITN | 1093 | 250 | 639 | 4 |

(Distribution, ACiT, primitives per class: 1:289 2:175 3:156 4:166 5:118 6:98 7:73 8:75 ... 63:1 **64:186**.)

### 1.2 Hard limits

| Rule | Evidence | Label |
| --- | --- | --- |
| **At most 64 primitives per class.** | max 64 in all three games and ToD. The tail is not smooth: ACiT 60:4 61:3 62:3 63:1 then **64:186**; A4O 64:103 against 63:0; ITN 64:36 against 63:1. So the tool *forces* a class to exactly 64 when it would exceed it. How (merge, truncate) is unknown. | PROVEN limit; mechanism OPEN |
| **At most 2048 triangles (6144 indices) per primitive.** | 0 of 57,000 primitives exceed it: max 2048 (ACiT, 4 prims), 2042 A4O, 2032 ITN, 2026 ToD. The number of indices is `+0x12`, always a multiple of 3. | PROVEN ceiling; exact constant inferred from 484/484 groups below |
| Vertices per primitive | max 3017 (ACiT), 2662 A4O, 2970 ITN, 2452 ToD. Local u16 indices; no clustering at any power of two (`<=255`: 3288, `256`: 19, `<=512`: 2355, `<=1024`: 1613, `<=2048`: 1179, `<=3072`: 111). The vertex count follows the triangle cap (vertices/triangle 0.5..1.67). **No vertex cap is visible.** | PROVEN max; no cap OPEN |
| Vertices per class | max 63,794 (ACiT), 28,146 A4O, 32,444 ITN. The vertex start `+0x04` is u16. | PROVEN (u16) |
| Index values | local, `< verts`, first index of each prim is 0; vertex block is 20 B/vertex | PROVEN (28,016/28,016) |

Vertex counts per primitive, ACiT (log-ish bins): <=16: 4817, <=32: 4642, <=64: 5389, <=128: 4603, <=255: 3288, 256: 19, <=512: 2355, <=1024:
1613, <=2048: 1179, <=3072: 111. Index counts: <=48: 6283, <=96: 4095, <=192: 5037, <=384: 4366, <=768: 3197, <=1536: 2197, <=3072: 1513,
<=6144: 1328.

### 1.3 Why a class gets several primitives of the same material

1. **Separate source objects** (the common case). Most same-key siblings are disjoint boxes (ACiT: 131,017 disjoint pairs, 20,460 under 10%
   overlap, 6,780 over); sums of vertices of multi-primitive groups under 2048 triangles have median 245 vertices. 8,982 of 21,501 primitives
   in multi-primitive groups have an identical-size sibling (repeated modules: four legs, six rungs). So one authored mesh node = one
   primitive, nothing merges small ones (2-triangle quads exist as primitives; the 64 classes at the cap are mostly such cards). STRONG.
2. **Median bisection when over 2048 triangles** (the only size-driven split). Group the primitives by key; look at the group's triangle
   total T over classes with fewer than 60 primitives:

   | T (group triangle total) | group size histogram, ACiT (A4O and ITN have the same shape) |
   | --- | --- |
   | <= 2048 | 1: 5863, 2: 866, 3: 195, 4: 281 ... (separate objects, case 1) |
   | 2049..4096 | **2: 146**, 3: 19, 4: 29, 5: 5, 7: 7, 8: 7 ... |
   | 4097..6144 | **4: 39**, 5: 6, 6: 4, 8: 6 (3: 0) |
   | 6145..8192 | **4: 15**, 5: 1, 6: 2, 8: 2 |
   | 8193..10240 | **8: 8**, 13: 1 |

   Over all three games every group with T > 2048 has at least 2 primitives (484/484 in ACiT) and no primitive exceeds 2048 triangles.

   - A 2-way split puts the halves at **min/total = 0.5** in 140/146 (ACiT), 80/82 (A4O), 56/57 (ITN); the triangle imbalance has median
     0.38% and 111/146 are within 1%.
   - Powers of two (1, 2, 4, 8) dominate, so the tool does **recursive median bisection** until each leaf has at most 2048 triangles.
     That is why a lot of retail primitives sit at 1000..1100 and 1900..2048 triangles (the whole mesh just over 2048 or 4096).
   - The cut plane is perpendicular to the **longest axis of the group's box** in 129/146 ACiT, 71/82 A4O, 48/57 ITN (88%); the halves
     overlap by a median 2.4% of the extent (triangles that straddle are assigned whole to one side). The split is by triangle (median of
     centroids), vertices on the cut are duplicated (the siblings' vertex windows are independent).
   - STRONG for the algorithm (counts, axis, balance all fit); the centroid choice is OPEN.
3. **Per-primitive state changes**: lightmap gate (`+0x0E` bit 0), blend mask (`+0x0A`), feature mask, and the fades start a new
   primitive. These are part of the key.

There is no spatial grid, no maximum extent and no per-primitive size cap: the longest axis of a primitive is smooth from 0.1 to 900
units (median 16, p99 399 ACiT), with a mode near 12.

### 1.4 Primitive order, batch, and index/vertex order

| Rule | Result | Label |
| --- | --- | --- |
| Primitive order | sorted by **slot ascending, then lightmapped (gate 1) first, then mask**: (slot, -gate) holds on 1852/1854 ACiT, 1692/1696 A4O, 1089/1093 ITN classes; slot alone 1853/1854. | STRONG |
| `+0x0D` batch | **min(number of immediately following primitives with the same (slot, gate, mask, feature mask), 16)**: ACiT 28,015/28,016; A4O 19,203/19,203; ITN 9,694/9,694 (A4O/ITN key uses +0x26 and +0x20). | PROVEN |
| Vertex block order | primitive vertex windows are laid out in primitive order in 1292/1854 ACiT classes (1620/1696 A4O, 1069/1093 ITN); the rest are permuted (blocks need not be contiguous). Anything is valid; write contiguous in primitive order. | PROVEN (free) |
| Index starts | strictly increasing in primitive order, 1854/1854; the primitive's own index run is contiguous | PROVEN |
| **Vertex order inside a primitive = first-use order of the index list** (the n-th new vertex met in the index list is vertex n) | 28,011/28,016 ACiT, 19,187/19,203 A4O, 9,694/9,694 ITN | PROVEN-ish (99.9%) |
| Triangle order | cache-optimised but not optimal: FIFO-16 ACMR median 1.08 (p10 0.82, p90 1.6) for >=20-triangle prims. | STRONG: any cache-friendly order is valid |
| Degenerate triangles | 0 in the corpus | PROVEN |
| Winding / indices | triangle lists only (indices %3 == 0 on all); all indices below the primitive's vertex count | PROVEN |
| Vertex weld | vertices are unique on (position, normal, UV, tangent); 0.35 duplicate positions per vertex (hard edges and UV seams) | STRONG |
| Unreferenced vertices | 1227 of 28,016 ACiT primitives have some (cause not found); write none | free |

So a baked primitive is: take a connected set of triangles, run a vertex-cache optimiser, renumber vertices by first use, emit.

### 1.5 LODs

- `+0x0F` = 1 on **26,434/28,016 (94%)** ACiT primitives (A4O 95%, ITN 99%), 2 on 151, 3 on 239, 4 on 1192. A4O/ITN almost never use 2.
- LOD ranges are `{u16 offset, u16 count}` index runs **inside the primitive's own index block, contiguous, LOD0 first, tiling it exactly** (1582/1582
  ACiT multi-LOD, 934/934 A4O, 92/92 ITN) over the same vertex window. Triangle counts decrease monotonically (ACiT 1562/1582). Mean tri ratio LOD1/LOD2/LOD3 vs
  LOD0: ACiT 4-LOD 0.66 / 0.42 / 0.24 (A4O 0.63 / 0.38 / 0.21).
- Multi-LOD primitives are bigger ones (median LOD0 triangles 218 for 4-LOD ACiT) but 8,781 single-LOD primitives have >= 100 triangles too.
- Distances: for multi-LOD primitives `+0x2E` is a distance (ACiT: 750 x192, 256 x109, 80, 5000, 30, 60, 70, 150 ...) and `+0x2C` 0;
  single-LOD primitives have `+0x2E` = 0 and, on 1295 ACiT ones, `+0x2C` = 30..200. A4O/ITN: `+0x2C` = 0 everywhere, `+0x2E` = 0 on single-LOD.
  (Meaning of the distances: STRONG LOD switch / cull distance; not traced.)
- **Importer: write one LOD** (`+0x0F` = 1, the range repeated four times, `+0x2C` = `+0x2E` = 0), exactly the retail majority.
  Optional later: LOD1..3 as index lists from a mesh simplifier on the same vertices, ratios 0.66 / 0.42 / 0.24, `+0x2E` = a distance
  (start from 5000/750), shared vertices only (a lower LOD may only reuse vertices of LOD0).

### 1.6 Quantisation and class-level data

| Rule | Evidence | Label |
| --- | --- | --- |
| **Per-axis scale = max(1.0, max abs coordinate on that axis) / 32767; no offset.** | 5562/5562 ACiT axes (4771 saturate to 32767; 791 have all coordinates inside +-1.0 and scale exactly 1/32767), 5088/5088 A4O, 3279/3279 ITN; `(|centre| + half) / (scale x 32767)` = 1.0 on every saturated axis | PROVEN (new rule: the floor is 1.0 unit) |
| Position = q x scale (s16) | `newengine_tie_asset_writing.md` | PROVEN |
| Primitive box 0x3F00, class AABB/sphere | exact (previous doc) | PROVEN |
| Lightmap UV window `+0x06` | unlit primitives hold the next lightmapped start | PROVEN (previous doc) |
| `+0x0A` blend mask, `+0x22/+0x28` second layer | 593 ACiT primitives (417 in the deduplicated set) only; **do not generate** | |
| Feature mask (ACiT `+0x24`, A4O/ITN `+0x20`) | 0x1D on 27,108 of 28,016 ACiT; **a property of the shader**: the majority value per shader TUID matches 27,337/28,016 (A4O 17,850/19,203, ITN 9,385/9,694); single-valued for 1576/1669 shaders | STRONG: copy from an existing primitive that uses the same shader, else 0x1D |
| Fades `+0x30..0x36` | (35,120,8,1) on 20,494/28,016 (73%); depends on shader only 82% | copy the template's, default (35,120,8,1) |
| 0x3400 `+0x0E` | 0 on 1852/1854 (ITN 68 x 4) | copy 0 |
| 0x3400 `+0x1C` | 4096 on 1794/1854, 10000 x43 | copy 4096 (A4O/ITN: 0) |
| 0x3400 `+0x78` | 01A0000C x112, 01800002 x94, 00800000 x63 ... (no relation found to slot, lightmap or primitive counts) | OPEN: copy from the template |
| Sections per class | 9 (1679), 11 (135, UV animation), 12 (40, embedded texture) | |

Two-layer blend and animated UV (`5C00/5C01`) are shader-driven extras the importer does not create.

### 1.7 Materials the importer can pick

A "material" in the writer is a shader TUID in the class's 0x5600. A class has up to 42 slots (ACiT). The same shader appears across
primitives with different gates/features; a primitive is keyed by (shader, lightmapped, feature mask).

---

## 2. Collision

### 2.1 Independence from the render mesh

Collision lives in collision.dat only (`acit_collision_format.md`). It is **a different mesh**:

- collision triangles / render LOD0 triangles: median 0.10 (ACiT), 0.10 (A4O), 0.18 (ITN); 184/1738, 210/1246, 133/720 are equal.
- Collision vertices that coincide (0.02) with a render vertex: >=99% in only 367/1738 ACiT classes; <10% in 806.
- Chunk count vs primitive count agree in 87/184 of the equal-triangle classes. The moon: 79 chunks / 3072 triangles (a 16x16 cube-sphere)
  against 63 render patches / 3078 triangles.

So the chunker is **independent of the primitives**; the importer builds collision from its own mesh (separate file or `COL_` meshes), or from the
render triangles if none is given.

### 2.2 Chunk (triangle mesh) rules, all on the unique chunk sets

| Rule | ACiT | A4O | ITN | Label |
| --- | --- | --- | --- | --- |
| vertices per chunk | max **512** (2 chunks), 36 in 480..500 | max 512 (1) | max 504 | PROVEN (9-bit index) |
| triangles per chunk | max 882 | max 940 | max 938 | no cap visible (u16 size is the limit) |
| size field | max 0x1D10 | 0x1DF0 | 0x1E50 | PROVEN |
| span of one axis | max 65529 / 1024 = **63.99 units** | 63.98 | 63.92 | PROVEN (u16 offsets) |
| **origin = chunk minimum** | min offset is 0 on every axis for 10,378/10,378 | 7,675/7,675 | 3,504/3,504 | PROVEN |
| unreferenced vertices | 0 | 0 | 0 | PROVEN |
| degenerate triangles | 0 | | | PROVEN |
| max index == vertex count - 1 | 10,378/10,378 | all | all | PROVEN |
| palette entries (shape trailer) | 1: 9612, 2: 693, 3: 65, 4: 8 | 1: 7537, 2: 137, 3: 1 | 1: 3343, 2: 156, 3: 5 | max seen 4 |
| triangle flags | 0 (9313), 1 (488), 0,2 (164), 0,1 (141), 2 (90) | | | bit 0 = no wall jump |

Layout, on all 50,007 chunks: vertex offset `+0x02` = 0x10; triangle offset = 0x20 + 0x30 x groups (groups = ceil(vc/8)); material bytes at the
triangle end **rounded up to 16** (the gap 0/4/8/12 appears in 30,302 / 1,606 / 16,609 / 1,490 chunks); size = align16(material offset + triangles); header `+0x04` = 0x100, `+0x0A` = `+0x0E` = 0;
the padding of the origin block is 0. The lanes of the last 8-vertex group beyond the vertex count are **not** a copy of the last/first vertex and
mostly non-zero (15,582 of 170,703 zero): OPEN, write zeros (never read, the vertex count bounds the indices).
Vertex order is arbitrary (sorted by x in 78 chunks) and not first-use ordered (0/10,378).

### 2.3 How retail chooses chunks

- 1123 of 1738 ACiT classes (65%) are **one chunk**; 615 have 2..750.
- Chunk count is not a power of two (1:1123 2:134 3:66 4:59 5:42 6:36 7:26 8:30 ... ), so it is not a bisection.
- Chunks from one class **share boundary vertex positions** (451/616 ACiT multi-chunk classes; 2.4% of vertices): the mesh is cut along triangle
  edges and the seam vertices duplicated (like the render split).
- Components per chunk: median 1.5 (ACiT), 1.0 (A4O): a chunk can hold several disconnected pieces.
- Not a world-aligned grid (852/10,378 chunks sit inside an aligned 32-unit cell). Typical chunk: median 57 vertices, 20..40 units
  across; the tail of 512-vertex, 880-triangle chunks has spans of only 4..13 units, and sparse floors (16 triangles) use the full 56..64.
- 289 ACiT classes (A4O 226, ITN 115) are split although all their chunks would fit one (<= 512 vertices, < 64 units): separate colliders or
  an unrecovered density rule. **The leaf criterion beyond the hard limits is OPEN.**

Importer rule: **split only when forced** (a retail majority is a single chunk): recursive median bisection (triangle centroid, longest
axis) until a chunk has <= 480 vertices **and** every axis span <= 60 units **and** <= ~800 triangles. This always satisfies the hard limits
(a bisected chunk can only shrink in vertices once seam vertices are duplicated: re-count after each cut). To keep the BVH
(<= 0x10000 bytes, 1819 nodes) and the number of instances low, do not split further.

### 2.4 Palette and materials

A chunk's palette is `u32 n` + n `u32` words, 1..4 used. Words are the opaque `00000000` (6,523 on ten levels), `0009120e`, `000a1b0f` ...; meaning
not decoded. Each triangle has a palette index byte. The importer maps `COL_` mesh material names (or a default) to a palette word through a
small table; default `00000000` (the junk-trader platforms and most moon surfaces use only that). Triangle flags 0.

### 2.5 Primitive sets (`type_data` 4) instead of meshes

65 ACiT shapes (47 with one primitive), 354 A4O, 416 ITN: up to 16 boxes, spheres, capsules, cylinders per set (counts 1..16). Used for round props
(light bulbs, poles). **Feasible but not worth it** for the first importer; hints (`UCX_`/`UBX_`/`USP_`) are the natural extension, see 3.2.

---

## 3. Importer design

### 3.1 Input conventions

- Format: glTF 2.0 or GLB through SharpGLTF (`SharpGLTF.Toolkit` 1.0.6 is already referenced by `ReLunacy.Engine`). Coordinates are used as exported by
  `LevelExporter` (no axis swap, UV as stored; verify the V direction against one round trip).
- One import = one **new Tie class**. Class name: the file name or the user's.
- **Render geometry**: every glTF mesh primitive reached through the scene (node world matrix baked in) whose node name does **not** start with `COL_`
  (case-insensitive; also accept `UCX_`/`UBX_`/`USP_`/`UCP_`/`UCY_`). Each glTF primitive is a *source part*; its material is the slot key.
- **Collision geometry**: either (a) the meshes named `COL_*` in the same file, (b) a second glTF given by the user, (c) nothing (no collision, the
  placement is not solid), or (d) "use the render mesh" (every triangle, one palette). Meshes named `UCX_*` could later feed a convex
  hull; for now treat them as `COL_`.
- `COL_` meshes never render and are excluded from the class bounds (the class bounds come from the render vertices only).
- Required attributes: POSITION, NORMAL, TEXCOORD_0; TANGENT optional (computed from UVs if absent, see 3.5); COLOR_0 ignored in v1 (prelight comes from the baker); TEXCOORD_1 ignored.
- Scale: units are game units (1 = 1 unit); offer a uniform scale factor in the dialog.

### 3.2 glTF material to game shader

The game's material is a **shader TUID** in the class's 0x5600 list; textures live inside the shader, so the glTF's texture is not used.

1. Candidate list = shaders that are in 0x1D100 (assetlookup) and in the target region's 0x1C100, ranked by "used by a Tie of this level" first
   (Tie-proven), with an albedo thumbnail from the loaded material.
2. Automatic match, in order: material name is a hex TUID; material name contains a game shader/material name the loader knows; same
   base-colour texture name as a loaded material; else the user's first choice.
3. The import dialog lists each distinct glTF material with a combo box (and a "same for all" button) and shows the shader's tiling hint (UV
   density from 0x3500). Unmatched materials block the import.
4. For each chosen shader, find a **template primitive** (any existing Tie primitive in the level using that TUID, prefer the one with the same
   feature mask majority) and copy: feature mask, fades, and the 0x3500 densities when the class has no better source.
5. The model's UVs must already suit that material's texture (the user's responsibility); the UV density formula of the doc (UV units per world unit)
   is only for the 0x3500 record.

### 3.3 The splitting algorithm (reproduces retail)

```
parts = glTF mesh primitives (render only), each: triangles, material -> slot (user choice), world-transformed
weld vertices per part on (pos, normal, uv0, tangent) so the vertex count is the real one
for each part:
    if triangles <= 2048: leaf = part
    else: recursive median bisection on the triangle centroid along the longest axis of the part's box
          until every leaf has <= 2048 triangles      (retail: 2 / 4 / 8 leaves for T <= 4096 / 8192 / 16384)
          duplicate the vertices on the cut                   [OPTIONAL: also stop at 3000 vertices]
leaves are the primitives. Do NOT merge parts of the same slot.
if leaves > 64:  (retail forces exactly 64; mechanism unknown)
    merge same-slot leaves, smallest first, nearest-box first, while the merged leaf keeps <= 2048 triangles
    if still > 64: refuse the import (or ask the user to split into two classes)
order primitives by (slot ascending, lightmapped first [always unlit in v1, so no-op], mask ascending), stable
per primitive:
    triangle order : vertex-cache optimisation (any, e.g. meshoptimizer's tipsify equivalent; ACMR ~1.1)
    vertex order   : renumber by first use in the index list; indices local u16
    box            : exact min/max of the quantised vertices (0x3F00)
    batch (+0x0D)  : min(count of following primitives with same slot/gate/mask/feature mask, 16)
    +0x06          : 0 on all (no lightmap array; "next lightmapped start" = the array size = 0)
    +0x0E          : 0, +0x0F: 1, ranges: (0, n) x 4, +0x2C = +0x2E = 0
    +0x24 (ACiT) / +0x20 (A4O, ITN): the template's feature mask; fades from the template
vertex block: all primitives contiguous in primitive order; vertex starts accumulate
class: scale_axis = max(1.0, max |coord|) / 32767; q = round(v / scale); bounds from the quantised positions; sphere radius = farthest vertex
```

Checks the writer must run (all PROVEN rules above): <= 64 primitives, <= 2048 triangles per primitive, indices < primitive vertex count,
total vertices < 65,536 (u16 vertex starts), vertex block bytes = 20 x vertices, all |q| <= 32767.

Tri-based priorities: nothing else drives retail splitting, so the importer does **not** split by a spatial grid, by material count, or at powers
of two of vertices (no retail cluster exists).

### 3.4 What is written, and what is copied from a template

Generated: the 0x5600 shader list, 0x5B00 (zeros), 0x3000 vertex block (20 B vertices, then **no Half2 lightmap array**), 0x3200 indices,
0x3F00 boxes, 0x3300 primitives, 0x3500 slot records (`{slot, u density, v density, 0}`), 0x3410 name, 0x3400 definition, fix-ups (3 internal).
Copied from a template class (a retail class with no lightmap: any unlit prop): 0x3400 `+0x0E`, `+0x1C`, `+0x74`, `+0x78`; the section set; the container header
words. No 0x5800/0x5700/0x5900 (no embedded texture), no 0x5C00/0x5C01.

Quantised `w` (vertex `+0x06`): `0x8000 | alpha` / `0x7F80 | alpha` by UV handedness (see the other doc); the default alpha = max.
Normals/tangents: `PackedNormal.EncodeForImport` (11:11:10) in object space.

### 3.5 Tangents

If the glTF has no TANGENT: per-vertex tangent from UV derivatives (the probe's `bit[]` accumulation), orthonormalised against the normal; handedness
sets the sign of `w`.

### 3.6 Collision cooking

1. Input triangles: `COL_` meshes (world matrix baked), positions in class space (the same space as the render geometry).
2. Weld by position at 1/1024; drop degenerate triangles.
3. Chunk by 2.3. Quantise: origin = integer chunk minimum (floor(min x 1024)), offsets = round(p x 1024) - origin (must be <= 65535).
4. Emit shapes (16-aligned, followed by the palette), then **one instance per chunk per placement**. Instances use the placement matrix, the owner word
   `0x20000000 | class index`, flags 0x6A (0x6E if scaled), type 1. This is the existing `CollisionBvh.BuildPayload`/`TieAppender` route; the class TUID
   must be in the collision TUID table.
5. Keep new collision inside the existing header bounds if possible (the run-time cell grid is derived from them).

### 3.7 Lighting

Imported classes have gate 0 on every primitive: lit by per-placement prelight words in the zone slice (`newengine_tie_asset_writing.md` 6.5.1). Until
the ACiT prelight word is closed the placements need neutral prelight (copy a plain placement's block pattern or write mid grey).

---

## 4. Old engine and other games

- **A4O / ITN**: same container sets plus 0x3900; the definition is 0x90 bytes; slot is `+0x26` (not `+0x2A`), feature mask `+0x20`
  (`+0x24` = 0), `+0x1C` = 0, `+0x2C` = 0. Every rule in 1.2..1.6 holds (the batch rule and the sort key use these fields). The class-level
  caps are the same.
- **ToD** (3 levels, 551 Ties, 6,259 meshes, via the engine reader): max 64 meshes per Tie (hit by classes), max 2026 triangles and 2452
  vertices per mesh, 305 of 551 Ties have one mesh per distinct material, the rest more. Same 64 / ~2048 caps; old engine fields
  (+0x28 u16 shader, +0x30/+0x34 floats) differ and the vertex format is old. The same bisection algorithm should apply but was not measured at primitive level.
- Collision limits are the same across all three new-engine games (512 vertices, 64 units, owner index into the class table).

---

## 5. Implementation steps (each sized for one agent)

1. **TieClassBuilder (engine, pure)**: `ReLunacy.Engine/Packing/TieBuilder.cs`. In: `ImportedMesh` (parts with positions/normals/uv/tangents/indices/material key),
   shader assignment map, template class bytes. Out: the full IGHW container bytes (all sections above) by composing sections and calling
   `IghwRelayout`. Includes quantisation, boxes, sphere, `0x3500`, `PackedNormal`, primitive records. Test: re-parse with `TieReader` and compare
   positions to 1 quantum; round-trip a retail unlit class (rebuild it from its own decoded triangles and diff the record fields).
2. **Splitter**: `TieSplitter`. Weld, median bisection > 2048 triangles, >64 merge/refuse, vertex-cache optimise (port a small tipsify), first-use renumbering, batch. Test:
   run it on decoded retail classes and check it reproduces the primitive count for the single-object classes (target >= 90%) and that every rule
   in 3.3 holds.
3. **glTF loader and material mapping**: `GltfTieImporter` (SharpGLTF): scene traversal, node transforms, `COL_` classification, weld, tangent
   generation, material list, UV and winding conventions (verify a round trip with `LevelExporter` output). Test: export a retail Tie, re-import, compare.
4. **Shader picker and template finder**: engine API `ShaderCandidates(region)` (0x1D100 with 0x1C100, Tie-proven first), template primitive lookup, feature-mask/fades majority table. Test: every shader of
   a level resolves to a template; majority agreement as in 1.6.
5. **Collision cooker**: `CollisionCooker` (weld, bisection by 480 vertices / 60 units, quantisation, shape bytes with palettes). Test: re-read with `CollisionReader`
   and check the limits; run on decoded retail chunks and compare triangle/vertex counts.
6. **Registration (existing writers)**: new class via the `--copy` route of `newengine_tie_asset_writing.md` section 5 (`ties.dat`, 0x1D300/0x1D301, 0x1C300, zone 0x7200, assetstats, toc) plus
   the collision class entry and instances (`CollisionBvh.BuildPayload`). Test: load the patched archive with `LevelReader` and `CollisionReader`.
7. **UI**: import dialog (file, scale, material combo list, collision source, class name), progress, validation messages, project overlay integration (`.lunaproj`).
8. **Optional**: LODs (simplifier), `UCX_` primitive sets, prelight bake for imported classes (depends on the ACiT prelight word).

Dependencies: 1 needs 2 only for the splitter's output type (define the types first); 3 and 4 are independent; 5 independent; 6 needs 1 and 5;
7 needs 3, 4 and 6.

---

## 6. Open items

1. How the 64-primitive limit is enforced (186 ACiT classes at exactly 64): merge, truncate or a class split.
2. Whether 2048 triangles is a hard engine limit or an exporter policy (nothing exceeds it; the bisection is the tool's).
3. The collision chunk leaf criterion (65% of classes are one chunk; 289 split classes fit in one).
4. The collision shape's pad lanes, the palette word meaning, and the SPU buffer limits.
5. 0x3400 `+0x78`, `+0x74`; primitive `+0x2C/+0x2E` meaning and the LOD distances.
6. Retail vertex-cache optimiser identity (ACMR median 1.08).
7. Untested in game: no generated Tie has been loaded by the console.

---

## 7. Collision cooker status (step 5)

Done 2026-11-17: `ReLunacy.Engine/Importing/CollisionInput.cs`, `CollisionCooker.cs`, `CollisionMaterials.cs`. Nothing is written to collision.dat yet (step 6).

- `CollisionCooker.Cook(CollisionInput)` welds on the 1/1024 grid, drops degenerate triangles and unreferenced vertices, cuts a triangle wider than 63.9
  units at its longest edge's midpoint, and bisects (triangle centroid median, longest axis, input order kept) only when a chunk breaks
  <= 480 vertices, <= 60 units per axis (a one-triangle chunk may use 63.9), <= 800 triangles or <= 4 palette words. Each chunk is written in the retail
  layout with origin = chunk minimum, the palette in first-use order, and zero pad lanes.
- `CookedCollisionChunk` holds the shape plus palette bytes, the shape size (instance +0x24), the quantised positions, the class-space box and sphere.
  `BuildInstanceRecord(chunk, shapeOffset, ownerIndex, placement)` makes the 0x80 record (world box from the transformed vertices, flags 0x6A | 1 mirrored |
  4 scaled, owner `0x20000000 | index`, matrix, sphere grown to hold the box corners).
- `CollisionMaterials.Common` lists the 20 most used palette words, named "Material 0x...".
- Probe `dev/tools/colcookprobe` (retail ACiT, flatten each class, cook, read back with `CollisionMesh.Read`): space_sector_1 43 classes and krell_canyon 189
  classes, 0 triangle-set mismatches (positions within one 1/1024 step, material words via palette, flags), 0 limit violations. Of 182 single-chunk retail classes,
  75 come back byte-identical, 105 identical except the pad lanes of the last vertex group (retail's are non-zero), 2 are split by the 480/60 margins, 0 differ
  otherwise. Retail palettes are in first-use order (120/120 multi-word shapes). Instance records: box exact, size/type/owner equal, matrix equal.
  Synthetic: 200 x 200 terrain (80,000 triangles) gives 128 chunks (max 363 vertices, 625 triangles, span 25); a 5000-triangle sphere of radius 40 with six materials gives 8 chunks.
- Open: instance flags differ from the 0x6A/0x6E rule on 14 of 180 retail records (6E on unit scale x10, 0x4E with 0x20 clear x3, 0x4F x1); our sphere radius is
  about 1.2 x retail's; pad lane content; the leaf criterion for classes that fit in one chunk but are split in retail.

---

## 7. Implementation status (steps 1 and 2)

Code: `ReLunacy.Engine/Importing/` (`ImportModel.cs`, `TieSplitter.cs`, `TieClassBuilder.cs`). Probe (gitignored): `dev/tools/tieimportprobe`
(`dotnet bin/Release/net10.0/tieimportprobe.dll <levels dir> <level>...` and `--synthetic <levels dir> <level>`).

- **`ImportModel`** (parts in class-local space, material slots with feature mask, fades, blend mask, UV density), **`TieSplitter.Split`** (weld,
  median bisection over 2048 triangles, merge only above 64 primitives, order, batch, Forsyth cache optimisation with cache 32, first-use renumbering,
  ACMR) and **`TieClassBuilder.Build(model, tuid, template)`** (container from `IghwRelayout.Relayout` over a template class, per-axis scale, boxes,
  0x3500, name, unlit primitives, one LOD) are done. The result (`TieClassResult`) carries the bytes, TUID, the 0x5600 shader TUIDs (must be in the
  region's 0x1C100), the vertex count (prelight words per placement) and the bounds, for step 6.
- **Template**: only plain classes (the nine standard sections, `+0x74` = FFFFFFFF); `CheckTemplate` says why not. Of 252 classes of space_sector_1
  and krell_canyon, 225 qualify (the rest have UV animation or an embedded texture).
- **Not written yet**: lightmapped primitives (a true `Lightmapped` is a problem), two-layer blend masks, LODs, prelight (a placement's words are the
  baker's job), UV1.
- **Correction to 3.3**: the 0x3F00 record's last word (`+0x1C`) is not zero; it is the box half-diagonal `|half extents|` (the builder writes it).
- **Retail rebuild**: every plain retail class of space_sector_1 (39) and krell_canyon (186), rebuilt from its own decoded geometry with itself as
  template and read back through `Objects.Tie`: same primitive count 225/225; same scale 224/225; triangle multiset (position q, UV) identical on
  223/224 (the one miss is 2 triangles of 208,073); normals within 0.063 degrees, no handedness mismatch; 0x5600, 0x5B00, 0x3500 and 0x3410 byte-identical on
  all; boxes within 2 quanta on 218/225 (the rest lose unreferenced vertices); the 0x3400 record equals the template's outside count, vertex bytes,
  bounds and TUID. Vertices 344,324 -> 328,912 (unreferenced ones dropped). ACMR (FIFO-16, primitives of 20+ triangles): retail median 0.84 / 1.00,
  rebuilt 0.78 / 0.96.
- **Bisection check**: feeding all triangles of a slot as one part gives retail's primitive count in 11 of 23 groups over 2048 triangles; the others
  mix separate objects (case 1.3.1), so no single bisection can match them.
- **Sphere centre** (0x3400 +0x30) is not the AABB centre, the vertex mean, the area-weighted centroid or the mean of box centres in retail; the builder
  writes the AABB centre with the farthest-vertex radius (a tighter, valid sphere). OPEN.
- **Synthetic**: a 10,520-triangle sphere (slot 0) plus 70 cards (slots 1, 2) gives 78 leaves (7 bisections), 14 merges to 64 primitives, all at most 1238
  triangles, surface area preserved, batch/order/first-use rules hold; 70 same-slot parts of 1500 triangles are refused (no pair fits 2048).

---

## 8. Shader catalog status (step 4)

Done 2026-11-17: `ReLunacy.Engine/Importing/TieShaderCatalog.cs`. Probe: `dev/tools/shadercatprobe` (gitignored).

- `TieShaderCatalog.Build(FileManager, LevelData, regionName)` reads region.dat 0x1C100 (shaders) and 0x1C300 (Tie classes), scans
  the Tie classes' primitives (ACiT layout) and returns `Candidates` (Tie-proven first, most used first), `Template`, `Find(tuid)`,
  `AutoMatch(material, baseColourTexture)` (null under 0.6) and `Rank(...)`. A second overload takes the class bytes and name lookups directly.
- `TieShaderCandidate`: TUID, display name (last segment of the shader name; the engine's names lose their first characters, so
  `FullName` is cut), six texture names, tie primitive / class counts (region), level primitive count, lightmapped count, two-layer flag,
  majority feature mask and fades (mask and fades vote separately), unanimous flag, template class + primitive index (majority values,
  unlit and single layer preferred) and the template's 0x3500 densities. `ToSlot()` makes an `ImportMaterialSlot`.
- Template class: among the region's classes that pass `TieClassBuilder.CheckTemplate`, are unlit, single-LOD, single-layer, have
  0x3400 +0x0E = 0 and +0x1C = 4096, take the most common +0x78 and the smallest class of that group. `Template.Reason` is the log text.
- Region membership: counts and majority come from the region's Ties; a shader no region Tie uses falls back to the level's Ties
  (not Tie-proven, no template when no Tie has it).
- Measured (ACiT, one region each): space_sector_1 693 shaders, 90 Tie-proven (all with a template), 31 mostly lightmapped, 2 two-layer;
  all 1057 primitives' shaders are in the list; feature mask equals the catalog's on **100.0%**, fades on 92.8%. krell_canyon 828 shaders,
  262 proven (258 with a template), 154 lightmapped, 3 two-layer; mask **94.6%**, fades 68.9% (both 66.3%) of 1317 primitives. Template
  found on both (14 and 23 plain classes).
- Auto-match with retail names as fake material names (proven shaders with an albedo; hit = same shader or same albedo texture): albedo
  tail 98.9% / 96.2%, texture only 98.9% / 98.9%, Blender style `NAME.001` 96-99%, shader short name 100%, junk name "Material"
  returns null 200/200. Misses are shaders that share one texture under different names.
- Open: fades are the weak value on krell (use the template's per shader anyway); only the ACiT layout is scanned; the thumbnail is the
  albedo texture of the loaded shader (`LevelData.Shaders[tuid].Albedo`), not part of the catalog.

---

## 8. glTF importer status (step 3)

Done 2026-11-17: `ReLunacy.Engine/Importing/GltfTieImporter.cs`. Probe (gitignored): `dev/tools/gltfimportprobe` (`dotnet bin/Release/net10.0/gltfimportprobe.dll <levels dir> <level> <scratch dir> [classCount]`).

- **API**: `GltfTieImporter.Import(path, GltfImportOptions?, collisionPath?)` returns `GltfImportResult { Model, Collision?, Warnings, Materials, counters, BoundsMin/Max }`. Options: `Name`, `Scale` (metres to game units, default 1), `FlipV` (default off), `CollisionMaterialWord(nodeName, glTF material name)` (default `CollisionMaterials.Default`), `CollisionPrefixes` (`COL_`, `UCX_`).
- **Scene**: default scene (else the first), node world matrices baked in, root = class origin. A node or mesh named `COL_`/`UCX_` (case-insensitive) makes itself and its descendants collision: triangles go to `CollisionInput`, nothing to the render parts or the bounds. The optional second file is all collision.
- **Conventions**: no axis change (Y-up, as the exporter writes); **no V flip**: the exporter writes the game's UVs unchanged and `TieClassBuilder` writes them unchanged, so game V = glTF V. Tangent w follows the engine (`GeometryMath`: B = dP/dV, w = sign(dot(cross(N, T), B))); a glTF TANGENT is used as given (xyz by the node matrix, w negated on a mirror). Mirrored nodes (det < 0) swap the winding. Strips and fans are triangulated (degenerates dropped); points/lines are skipped with a warning. Missing normals give flat normals (corners unwelded), missing TANGENT is generated, missing TEXCOORD_0 gives zeros with a warning.
- **Materials**: one `ImportMaterialSlot` per used glTF material (ShaderTuid 0; the primitive without a material gets "Default"); `GltfMaterialInfo` carries name, slot, base-colour texture name/uri/factor, alpha mode, double-sided, part and triangle counts.
- **Round trip** (retail space_sector_1, 14 plain classes exported with `GltfExporter`, imported, rebuilt with `TieSplitter` + `TieClassBuilder` using the class as template): 73,350 triangles, 73,350 matched on (position quantum, UV bits), 14 of 14 classes identical in triangle multiset and scale; normals within 0.056 degrees; handedness differs on 62 of about 220,000 corners (retail w disagrees with the UV-derived w the exporter writes; 0.03%). UV orientation is therefore correct with no flip.
- **Hand-made glTF**: 2 materials, a mirrored node, a strip, `COL_box` and a `UCX_ramp` under a translated parent: 5 render parts (strip = 2 triangles, winding +Z after the mirror), 15 collision triangles excluded from the bounds, cooked into 1 chunk and read back (15 triangles, both material words).
- **Open**: glTF TANGENT from foreign tools may use the opposite handedness (not measured); no LOD, skin or morph handling; UV1 is read but the builder does not write it; collision material mapping per glTF material needs UI.

---

## 9. Registration status (step 6)

Done 2026-11-17: `ReLunacy.Engine/Packing/TieClassRegistrar.cs` (types, `FreshTuid`, `Register`), `TieAppender.Classes.cs` (the writer and the placement path
for a class with no placement), `GameplayWriter.Classes.cs` (save chain), `Scene/EntityRegion.Imported.cs` (pending list). Probes (gitignored):
`dev/tools/tieregprobe` (bytes and readers) and `dev/tools/tieregsaveprobe` (File > Save through the editor's regions).

- **`TieClassRegistrar.Register(read, IReadOnlyList<PendingTieClass>)`** writes, per class and lowest TUID first: the container at the end of ties.dat (0x80 aligned);
  assetlookup 0x1D300 `{TUID, offset, length}` and 0x1D301 (the bits of the target regions, `RegionBit`); each target region.dat's 0x1C300 (sorted); assetstats
  0x9800 +0x2C (Tie classes), +0x08 (region.dat), +0x00 (resident pool, plus the 0x80-aligned growth of ties.dat and the collision files), +0x0C (collision.dat); the toc
  sizes. The zones are not touched. **Refused** (nothing changes): TUID in use anywhere in assetlookup, TUID not above the largest 0x1D300 TUID (keeps it sorted with
  ascending offsets; `FreshTuid` gives one), a shader of the class not in a target region's 0x1C100 or in 0x1D100, a build problem, a bad own TUID (0x3400 +0x68).
- **Collision class entry** (per target region with a collision.dat): the TUID appended to the table (+0x68 count, +0x6C), 0xFFFF appended to the u16 table (+0x70, re-laid by
  `CollisionBvh.BuildPayload` over a widened "old" payload), the cooked chunks appended to the shape blob, 16-aligned, each with its palette (`CookedCollisionChunk.Data`);
  the owner index of the class is the old count. Instances, tree and header bounds are untouched until a placement exists (no placement uses the shapes yet).
- **First placement of a class with no placement** (`TieAppender.Append(read, requests, imported)`, `CreateZone(..., imported)`): the record is a copy of a plain placement of
  the zone (same preference as the normal source pick: unbaked with prelight first), its sphere is the class sphere carried by the template's matrix (then moved as usual),
  no lightmap pair, and a new prelight block of `VertexCount` words at the end of the zone's lighting slice, each the template block's most common word (a neutral
  placeholder; a fixed word when the template has none). Collision instances come from `CollisionCooker.BuildInstanceRecord` (owner `0x20000000 | index`), one leaf
  per 255, `AddLeaf` / 64 KB `ExtendLastLeaf` fallback and the header-bounds union exactly as `AppendCollision`. Further placements in the same call, or later,
  use the normal copy path (the first placement is their source).
- **Save chain**: `GameplayWriter.Build` registers the regions' `EntityRegion.PendingTieClasses` before deletions and appends. A class loads in the region it was imported into and in
  every region that loads a zone one of its placements is in. The save lists "Tie class <name> imported (...)", is structural (reload), and clears the pending list after a
  real save. `LevelReader.AddPendingTie(pending)` reads the class bytes into the level's Tie library (`AssetLibrary.AddPending`: listed, named, survives `Unload`), which gives the
  UI an `ITie` for `EntityRegion.CreateTie` and the asset explorer.
- **Verified** (copies, read-only on the game; `tieregprobe`, 138 checks each on space_sector_1 and krell_canyon; `tieregsaveprobe` on space_sector_1):
  two classes (a synthetic sphere plus card; the largest plain retail class of the region rebuilt from its own geometry under a new TUID, 64 primitives, 27,921 / 23,161
  vertices) with cooked collision (4 and 1417 / 39 chunks), four placements (two rotated and scaled 1.7 / 0.6 / -1.1 rad yaw). The bytes at the new 0x1D300 entries equal the built
  bytes; `LevelReader` loads both classes (primitive count, indices, vertices within 1e-4), the placements (class, matrix, prelight window of at least `VertexCount` words,
  no lightmap pair); `LevelCollision` lists the TUIDs and the instances (4 / 1417 / 39 per placement, owner index, boxes), and every world vertex equals the cooked
  chunk transformed by the placement within 1/512 (worst 0 on the quantised vertices), indices equal; 0x1D300 sorted with ascending offsets, region 0x1C300 sorted, 0x1D100 / 0x1C100
  unchanged; every old class byte-identical, every other zone identical, the target zone's old placements identical, old collision instances / shape blob / TUID table
  prefix identical, BVH valid (every instance in one leaf, boxes hold, <= 255 per leaf, bounds union, block <= 64 KB); toc sizes match; a TUID twice and a missing shader are
  refused with no change. Through the editor: `tieregsaveprobe` queues a class, creates two Ties, saves to a copy: the save lists the class, the copy reads back with the
  class, both placements and 4/4 collision instances.
- **Numbers**: space_sector_1 collision.dat 904,576 -> 1,829,888 bytes with the 1417-chunk class twice (shape blob 558,736 -> 1,119,824; bounds grew on X); krell_canyon (a planet
  region, tree at 64 KB: `ExtendLastLeaf` was used, 0x8200 entries copied) 7,454,100 -> 7,666,452. assetstats +0x2C 52 -> 54 (ss1), 200 -> 202 (krell).
- **Open**: nothing loaded in game (the memory arena and the prelight placeholder are the risks, as for any Tie append); cooking the render mesh as collision gives
  thousands of chunks for a big class (a `COL_` mesh is the intended input); the 0x8200 planet entry of a first placement copies another class's record (the u16
  at +0x02 is per class: unknown); a pending class that is placed in a region the registrar did not list is refused by the zone-mask check; only the shader check
  guards the "textures" (they live in the shader, as the `--copy` route found); no undo of a registered class (delete the Ties; the class stays in the files).

---

## 10. Import UI status (step 7)

Done 2026-11-17: `ReLunacy/Core/Frames/Modals/ImportModelModal.cs` (File > Import > Model..., Asset Explorer Ties tab), logic in `ReLunacy.Engine/Importing/ModelImportSession.cs`,
project record in `ImportRecords.cs`. Probe (gitignored): `dev/tools/modelimportprobe <level_cached.psarc> <scratch dir>`.

- **Session**: `Parse()` (glTF, auto-match, collision words) and `Build()` (split, class, cook) run on a background task from the modal (one at a time, the UI is disabled meanwhile);
  both fill new lists and assign them at the end. Collision materials are read with placeholder words, replaced at build time, so changing a surface type does not reparse.
  `Commit(reader, assets)` = `AddPendingTieClass` + `LevelReader.AddPendingTie` + `AssetManager.GetOrBuildTie`; `Remove` refuses while a placed Tie uses the class.
- **Collision source**: COL_/UCX_ nodes (default when the file has some), a second file, none, or the render mesh (each part's glTF material gives the word).
- **Project**: the model (a .gltf with its buffers and images) is copied to `<project>.d/imports/<name>/`; `editor.importedAssets` gets `{kind: tieClass, name, tuid, region, model, scale, flipV,
  materials[{material, shader, collisionWord}], collisionMaterials[], collisionSource}`. With no project open the record waits for the first save (`ProjectSession.MarkSaved`).
- **Not done**: shader thumbnails in the picker; lightmapped and two-layer shaders are offered but written unlit (a warning is listed).
- **Verified** (retail space_sector_1 copy): a retail class exported with `GltfExporter` and re-imported: parse 47 ms, auto-match 1 of 1 materials (the shader the class uses), build 55 ms
  (4 primitives against 5 retail); render mesh as collision gives 16 chunks; an unmapped material blocks the build. A hand-made cube with a COL_ node: 12 collision triangles, 1 chunk, the chosen word
  is in the palette. Both committed, two Ties created, the third (removed) class left no trace, a class with a placed Tie is refused; save to a copy lists both classes, both read back, the
  collision class entry and instance of the solid one are present; the project holds both records and the model copies.

