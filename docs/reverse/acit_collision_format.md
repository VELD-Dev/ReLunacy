# A Crack in Time collision.dat: full layout, the BVH, the Tie link, adding collision

Research of 2026-11-14 on the A Crack in Time EBOOT and data (NPUA80966). It builds on `docs/reverse/collision.md`,
which covers the header, the instance record, the triangle mesh and the primitive set.

- **Scripts:** `dev/tools/colresearch/`, which is gitignored. Run them with `python3` and without `-I`.
- **Data:** the files the zones audit extracted, in `dev/tools/zonecoverage/data/<level>/default__collision.dat`.
  For space_sector_1 that is the shipped copy (the `.bak`), not the edited one.
- **Regions:** every ACiT level checked has a single region, `default`.
- **Levels used:** front_end, space_sector_1..5, axiom_city, krell_canyon, tombli and zolar_forest (10 levels).

Status labels: **PROVEN** means checked on every record of the levels named. **STRONG** means consistent
everywhere but not traced end to end. **ASSUMED** means a guess.

## 0. Summary

- **Container.** collision.dat is an IGHW 1.1 container with no fix-ups. Every offset inside section 0x2900 is
  relative to the start of that section's payload. All 10 files are 100% byte-accounted. **PROVEN**
- **The structure at header +0x30 is a BVH.** Earlier docs called it a grid; it is a bounding-volume hierarchy:
  - Nodes have a variable number of children.
  - Each node box is stored as a centre plus half extents, and every box is tight.
  - Leaves hold contiguous ranges of instances, and every instance sits in exactly one leaf.
  - The nodes are laid out in BFS order.
  - Rebuilding the file from its parsed parts gives the same bytes on all 10 levels. **PROVEN**
- **Collision belongs to the region file only.** A Tie asset in ties.dat carries no collision.
  - Each Tie placement that has collision gets N instances, one per mesh chunk, all with the placement's matrix.
  - The instance owner word holds an index into the region's table of Tie class TUIDs.
  - Nothing links a collision instance to a placement index or a placement TUID.
  - Placements of the same class share shapes. For example, moon1x and moon1y share 42 chunks. **PROVEN**
- **The game's side** (details in section 5):
  - The PPU attaches the file in place: it relocates the shape offsets, sets the BVH pointer and fills the
    TUID-to-class u16 table.
  - It then hands the tree pointer and the tree size to an SPU job, which does the queries.
  - The BVH block was 64 KB at most on every retail level. The large levels are capped at exactly 0x10000, which
    is 1819 nodes.
- **Verdicts:**
  - **(a) Add a collision instance of an existing shape: SAFE WITH CONDITIONS.** I proved it on a copy of the
    file: `mkinstance.py` plus `verify.py` pass all checks.
  - **(b) Add a new shape: SAFE WITH CONDITIONS on the format side.** The layout is fully known. It needs a mesh
    cooker, which does not exist yet.
  - **Not tested in game.**

## 1. Container and byte coverage (`coverage.py`)

IGHW 1.1 header fields:

- Header +0x10 is the file length.
- Header +0x14 is 0: there are no fix-ups in any file.
- Every space sector and front_end has only section 0x2900.
- Planet levels add section 0x8200 (little-endian, one record per Tie placement, see section 7) and section 0x8300.
  These two are counted below by their section-table size, but their contents are not decoded here.
- The bytes between sections are the usual `PAD0PAD1...` filler.

| Level | Bytes | Accounted | Biggest parts |
| --- | --- | --- | --- |
| front_end | 1,280 | 100.0000% | pad 21%, mesh vertices 20%, triangles 16%, header 10%, 1 instance 10% |
| space_sector_1 (shipped) | 904,576 | 100.0000% | instances 35.9%, vertices 27.4%, triangles 24.3%, materials 6.1%, BVH 2.2%, pad 1.65% |
| axiom_city | 8,171,744 | 100.0000% | 0x8300 55.6%, instances 23.7%, vertices 8.6%, triangles 6.5%, 0x8200 1.9% |
| krell_canyon | 7,454,100 | 100.0000% | 0x8300 51.0%, instances 22.9%, vertices 10.4%, triangles 8.5%, 0x8200 3.0% |

The "pad" tag is zero bytes that no field claims: alignment gaps, plus the zero tails after the u16 table, the
grid header and the BVH. The full tables are in `dev/tools/colresearch/out_coverage.txt`.

### Payload layout of section 0x2900

These rules hold on all 10 levels (**PROVEN**). All offsets are relative to the payload.

| Order | Part | Where / size |
| --- | --- | --- |
| 1 | Header | 0x00, 0x80 bytes (see the table below) |
| 2 | Tie class TUID table | at +0x6C (always 0x80): +0x68 entries of u64 |
| 3 | u16 table | at +0x70 = 0x80 + 8 x count: one u16 per TUID, 0xFFFF in every file (filled by the game) |
| 4 | Instance array | at +0x00 = align0x80(end of the u16 table); +0x04 records of 0x80 bytes |
| 5 | Shape blob | at +0x08 = the end of the instances; +0x0C bytes long. Each shape is 16-aligned and followed by its material palette |
| 6 | BVH block | at +0x30 = align0x80(end of the shape blob) |
| - | Payload end | +0x30 + 0x80 + (BVH +0x00) |

Header fields:

| Offset | Meaning |
| --- | --- |
| +0x00, +0x04 | Instance offset, instance count |
| +0x08, +0x0C | Shape blob offset, shape blob length |
| +0x20, +0x24 | 12.0 and 1/12: the runtime cell size and its reciprocal (section 5) |
| +0x30 | BVH offset |
| +0x40 / +0x50 | Bounds min / max. They equal the exact union of all instance boxes on every level (**PROVEN**). The game derives its runtime cell grid from them |
| +0x68 / +0x6C / +0x70 | TUID count / TUID table offset / u16 table offset |
| everything else | 0 in every file |

## 2. The BVH at header +0x30 (**PROVEN**)

BVH block (`bvh.py`), with offsets relative to the start of the block:

| Offset | Meaning |
| --- | --- |
| +0x00 | u32: size in bytes of the tree block below, rounded up to 0x80. The SPU job receives it as the DMA size |
| +0x04 | u32 = 0x80: offset of the tree block. The game overwrites it with an absolute pointer and hard-codes block+0x80 |
| +0x08..+0x7F | zero |
| +0x80 | Tree header: u32 node count, u32 0x10 (entries offset), u32 boxes offset (= align16(0x10 + 4 x count)), u32 0 |
| tree+0x10 | One u32 per node: u8 A, u8 B, u16 first |
| tree+boxes | One 32-byte box per node: vec4 centre (w 0), vec4 half extents (w 0) |

How to read a node:

- **Internal node.** Its children are nodes `first .. first+A+B-1`. The first A of those are internal nodes and
  the next B are leaves.
- **Leaf node.** A node is a leaf because its parent lists it in the leaf range. A leaf has A = 0, and its
  instances are `first .. first+B-1`.
- **Node 0 is the root**, and it is always an internal node.
- **Numbering is BFS.** The root is node 0, its children come next, then the children of node 1, and so on.
  Leaves in node order point to increasing instance ranges, which cover the instance array in order.

Results checked on all 10 levels:

| Level | Nodes | Internal / leaf | Depth | Max children | Max leaf | Tree block |
| --- | --- | --- | --- | --- | --- | --- |
| front_end | 2 | 1 / 1 | 1 | 1 | 1 | 0x80 |
| space_sector_1 | 561 | 64 / 497 | 5 | 144 | 36 | 0x4F00 |
| space_sector_2 | 849 | 94 / 755 | 6 | 146 | 30 | 0x7780 |
| space_sector_3 | 1335 | 181 / 1154 | 5 | 141 | 57 | 0xBC00 |
| space_sector_4 | 511 | 49 / 462 | 5 | 63 | 29 | 0x4800 |
| space_sector_5 | 259 | 28 / 231 | 5 | 45 | 29 | 0x2480 |
| axiom_city / krell_canyon / tombli / zolar_forest | 1819 each | 321/1498, 277/1542, 295/1524, 267/1552 | 5 | 128-190 | 169, 58, 111, 59 | 0x10000 each |

- **Instances.** Every instance is in exactly one leaf on every level. No instance is missing from the tree and
  none appears twice.
- **Leaf boxes.** On every leaf, the box equals the union of its instances' boxes to within 0.001.
- **Internal boxes.** On every internal node, the box equals the union of its children's boxes to within 0.001.
- **Root box.** It equals the header bounds.
- **Re-serialising.** Writing the parsed tree back in BFS order (`colwrite.py`) gives the same bytes on all 10
  levels.

### How the old "grid" questions answer

- It is not a uniform grid and it is not split by zone. It is one BVH per region.
- Empty space simply has no node. No "empty cell" record exists.
- The fan-out varies and is limited to 255 internal plus 255 leaf children per node.
- There is no overflow scheme.
- The cell size of 12 at header +0x20 does not describe the file. It feeds a grid the game builds at run time
  (section 5).

## 3. Instance record (0x80 bytes)

The field names come from the EBOOT's own debug dump (`0x562F64`, HTML, and `0x56337C`, text, both printing
"obj %5d"). Statistics are over all 10 levels: 74,619 instances in total.

| Offset | EBOOT name | Type | Meaning |
| --- | --- | --- | --- |
| +0x00 | aabb_min | vec4 | World box min, w = 0. Tight (see collision.md) |
| +0x10 | aabb_max | vec4 | World box max, w = 0 |
| +0x20 | packet | u32 | Shape offset in the shape blob. The game rewrites it in place to an absolute pointer (0x564114) |
| +0x24 | size | u16 | Shape length in bytes. The material palette that follows the shape is not counted |
| +0x26 | type_data | u8 | 1 = triangle mesh (74,264 instances), 4 = primitive set (355). The old "shape flag 0x400" is this byte = 4 |
| +0x27 | flags | u8 | Values seen: 0x6A, 0x6E, 0x6F, 0x4A, 0x4E, 0x4F, 0x2A. **bit 0x01 = mirrored** (determinant < 0): PROVEN on 74,619/74,619. **bit 0x04 = scaled**: STRONG. It is set on every instance whose scale differs from 1 by more than 4e-4, and also on all mirrored instances and on some unit-scale ones. Bits 0x02 and 0x08 are always set. 0x20 and 0x40 are open |
| +0x28 | owner_data | u32 | Top nibble 2 = Tie (bit 29), with the low 28 bits = **index into the TUID table**. Top nibble 0 = world chunk (UFrag terrain, planets only), index 0 |
| +0x2C | anim_mats | u32 | 0 in every file. It is a run-time pointer (animated matrices) |
| +0x30 | matrix | mat4 | The placement's matrix, row-vector form, bit-identical to the Tie placement's. On primitive sets, **w (+0x6C) = the uniform scale** (the length of the 3x3 rows) instead of 1.0: PROVEN on 355/355 |
| +0x70 | bsphere | vec4 | A bounding sphere of the transformed shape. It is not the box's sphere: the centre differs on about half the instances and the radius is sometimes larger than the box's half-diagonal. It always contains the box's vertices |

There is no inverse matrix. Nothing in the record points back to a zone, a placement index or a placement TUID.

## 4. Shapes, the material palette and the Tie link

**Triangle mesh.** The layout is as in collision.md:

- Header: +0x00 vertex count, +0x02 = 0x10, +0x04 = 0x100, +0x06 triangle count, +0x08 triangle offset,
  +0x0C material offset. +0x0A and +0x0E are always 0.
- The mesh ends at align16(material offset + triangle count), which equals the size field on all 10,519 meshes.

**Material palette (new, PROVEN).**

- Each shape is followed by `u32 n` and then n u32 material words, padded to 16 bytes. This is 10,519/10,519
  meshes plus the primitive sets.
- **The per-triangle "material" byte is an index into this palette.** On every mesh, the largest index is below n,
  and every palette entry is used.
- So a material is a 32-bit word that belongs to the shape, not a global id.
- Palettes hold 1 to 5 entries.
- Common words: `00000000` (6,523), `00102314`, `000a1b0f`, `0009120e`, `00002314`, `000a1a0f`, `0009121b` and
  others. The meaning of each byte is not decoded.

**Tie link (PROVEN):**

- The TUID table at header +0x6C lists the region's **Tie class TUIDs**. All 46/46 to 215/215 entries are classes
  in assetlookup 0x1D300.
- An instance's owner index selects the class. Every Tie instance whose matrix matches a placement matches one of
  that class: ss1 2465/2465, axiom 14236/14236, krell 11532/11532.
- The ss1-5 instances that match no placement are exactly the 71 primitive-set instances of the
  `ja_mf_xmas_bulb*` lights. Their only difference is the scale stored in matrix w.
- A placement whose class has collision has a set of instances: the class's chunks, each with the placement's
  matrix.
- Placements of the same class **share the shapes**. Each placement gets its own instance records, which point at
  the same shape offsets. Example: moon1x and moon1y are 2 placements, 84 instances and 42 shapes.
- 0 to 2 shapes per level are shared between two different classes (identical content, de-duplicated).
- **The Tie asset (ties.dat) has no collision section.** A moon1x class blob has sections 0x3000, 0x3200, 0x3300,
  0x3400, 0x3410, 0x3500, 0x3F00, 0x5600 and 0x5B00. So collision.dat is self-contained, and a Tie class without
  entries in it is never solid. For example, `a_moon_1c1` is a second class placed at the same matrix as `a_moon_1c`, and it has no collision.
- **Mobys have no records here.** Their collision is registered at run time in the dynamic object pool
  (section 5).

**Mesh chunk limits seen in retail.** A new shape must respect them:

- At most 512 vertices: the indices are 9 bits. The largest seen is 499.
- Vertices are s32 origin + u16 offsets in 1/1024 units, so a chunk spans **less than 64 units per axis**.
  Values up to 65528 (64.0 units) were seen.
- At most 735 triangles were seen.
- A shape is at most 0x18A0 bytes, and the size field is a u16.

## 5. What the game does (EBOOT, address and role)

`G` = the collision world global at 0xF3D080. It is reached through TOC -0x51A8, then -0x8000.

| Address | Role |
| --- | --- |
| 0x583BDC | Region loader. Finds section 0x2900 (`0x5D5140`, sites 0x584004/0x584214/0x584244), calls 0x564114(payload, 0). Then, for each TUID in the table: `0x5F72A8(tuid)` (Tie class lookup); u16[i] = (class record - class table) >> 7 (0x80 per record), or 0xFFFF if not found |
| 0x564114 | Attach. G+0x7C/0x80 = header +0x20/+0x24. Returns early if the count is 0. G+0x20 = count, G+0x24 = instances, G+0x2C = shape blob, G+0x114 = BVH block, G+0x11C/0x120/0x124 = TUID count, table and u16 table. **Rewrites every instance +0x20 to a pointer.** Writes BVH +0x04 = block + 0x80. Computes a run-time cell grid from header bounds x 1/12: min cell - 1 aligned down to 16 and max cell + 17 aligned down to 16, per axis. The results go to G+0x60..0x78 (spans, nx*ny, origin) and G+0xBC..0xCC |
| 0x5639D4 | System init (once). Carves fixed buffers: G+0xB0 0x80 B, G+0xB4 0x800 B (set to -1), G+0xB8 0x2000 B (set to -1), G+0x28 **0x80000 B = 4,096 dynamic "obj" records of 0x80** (Mobys and other moving collision; G+0x40..0x54 pool state), then G+0xD0..0x110 (several 0x10000-0x50000 B work buffers) |
| 0x563F28 | Per-level reset: clears the same buffers and the dynamic pool |
| 0x553A44 | Builds the SPU collision context: +0x80 = BVH tree pointer (BVH +0x04), +0x84 = **tree size (BVH +0x00)**, +0xA0 = dynamic pool, +0xA4 = static instance array, plus G+0xB4 and G+0x104..0x110. **No instance count is passed**: the SPU reaches instances only through the leaves |
| 0x554780 | Reads the BVH +0x00 / +0x04 pair again (job setup) |
| 0x562F64, 0x56337C | Debug dumps of every obj record (the source of the field names) |
| 0x553F1C, 0x553CC4, 0x55D910... | Dynamic object management (G+0x28 pool, G+0xB8) |

What this means for adding instances:

- **Queries run on SPU.** The string `.../igPhysics/SPU/igPhysicsSpuC...` is in the EBOOT. The PPU passes the
  tree pointer and the tree size, and the SPU fetches the tree, then the leaves' instances, then the shapes.
  Neither the triangle decode nor the BVH walk is on the PPU, so the per-query buffer sizes are not known.
- **No PPU pool is sized by the static instance, shape or node count.** Static data is used in place, from the
  loaded file.
- **The tree block must stay at or below 0x10000.** This is STRONG: all four big planet levels stop at exactly
  1,819 nodes (0xFFE0 bytes), the largest count that fits 64 KB, and the SPU gets the size as a DMA length.
  The space sectors are far below it: 0x2480-0xBC00.
- **The u16 table is filled from the TUID table by the game.** A TUID that is not loaded maps to 0xFFFF.
- **Mirrored sizes:**
  - `assetstats.dat` +0x8C equals align0x80(the collision.dat size) on all 5 levels checked. This is
    assetstats offset 0x8C: section 0x9800 +0x0C. Neither assetstats reader found (0x32C7C0, 0x580028) reads it
    (STRONG that it is a statistic), but update it anyway.
  - `level_cached.toc` holds the file size, like every file.
  - No other file holds the instance count or the node count. I searched region.dat, metadata.dat, gameplay.dat
    and assetlookup.dat.
- **Moving a Tie in ReLunacy.** `CollisionWriter` rewrites the instance matrix, box and sphere but not the BVH.
  A Tie moved outside its leaf's box will not be found by queries. The fix is a refit: recompute the leaf and
  ancestor boxes and the header bounds. The topology stays the same.

## 6. Space sectors

| Sector | File | Instances | Shapes | Mesh triangles | Classes | Nodes | Header bounds (x / y / z) | Corner radius |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 904,576 | 2,536 | 853 | 54,929 | 46 | 561 | -2445..1752 / -3..1144 / -2174..2480 | 3,482 |
| 2 | 968,832 | 3,795 | 721 | 44,094 | 63 | 849 | -3450..4398 / -3611..4518 / -4073..3391 | 5,995 |
| 3 | 1,703,680 | 6,484 | 1,254 | 78,480 | 58 | 1,335 | -3091..3676 / -3..1144 / -3545..3036 | 5,107 |
| 4 | 482,688 | 1,620 | 366 | 26,223 | 53 | 511 | -2230..3205 / -1..1479 / -3128..6218 | 6,995 |
| 5 | 403,200 | 949 | 375 | 27,541 | 41 | 259 | -1552..1225 / -1..1144 / -1725..2920 | 3,307 |

How a moon's collision is stored, using space_sector_1:

- **moon1a:** one placement, its own class, 79 chunks (79 shapes, 79 instances).
- **a_moon_1c:** a different class, also 79 chunks (it has its own 79 shapes).
- **moon1x and moon1y:** one class, two placements, 42 shared shapes and 84 instances.
- **habitable_moon_death1:** 79 chunks with a 2-entry palette.
- Each placed moon is 3,072 triangles in about 42-79 chunks.
- The moon's spires, bunker, landing pads and so on are separate classes with their own instances.
- Identical moons share shapes only when they are placements of the same Tie class.

The header bounds sit inside the play radius (about 2650-3500) on sectors 1 and 5. On sectors 2-4 they reach
further out, to a corner radius of 5000-7000.

**Material words of moon surfaces against junk traders** (the full table is in `shapes.py` output; space sectors 1-5):

| Surface | Palette words |
| --- | --- |
| moon1a, moon1x/moon1y, most moon platforms, landing pads, **junk-trader platforms** (`junk_trader_pip_plat`, `yy_space_junk_trader_platform`, `yy_moon_junk_shop_platform`) | `00000000` |
| a_moon_1c, a_moon2e, aa_moon3c, a_moon5b | `0009120e` |
| habitable_moon_death1, moon2d, a_moon4b, a_moon5a | `00000000` + `0009120e` (two materials per chunk) |
| ice_moon_death1, moon3e, a_moon4a | `0009121b` |
| ja_mf_barrel_huta1 / ml_krell_grindrail_support / tv_mf_junkpile_04, nb_mf_trash_ibeam | `000b1a0f` / `000a1a0f` / `00001a0f` |

**0x1A as the "walk on any side" material is REFUTED for moons.** No moon surface in any space sector has 0x1A
in byte 2 of its material word. Byte 2 is 0x00 or 0x12, and the junk traders also have 0x00. Only small props
carry 0x1A: barrels, grind-rail supports, a junk pile and an i-beam.

This assumes the hit's +0x60 word is the palette word. That is ASSUMED: the SPU fills the hit, which is not
traced. If the assumption holds, a moon's "up = towards the centre" comes from the SpaceMoon gravity Clue, not
from the collision material. A new moon needs no special material: copying an existing moon's chunks copies its
palette.

## 7. Verdicts and recipes

### (a) Collision for a new placement of a class that already has collision in the region: SAFE WITH CONDITIONS

I proved this mechanically on a scratch copy; it was never installed:

- Command: `python3 mkinstance.py space_sector_1 moon1x 200 725.4 -1500`, then `python3 verify.py`.
- **Result:** 42 instances appended (moon1z at (200, 725.4, -1500)). The file grew from 904,576 to 910,080
  bytes, the node count from 561 to 562 and the tree block from 0x4F00 to 0x4F80.
- **All `verify.py` checks pass:**
  - The original records are unchanged and at the same indices.
  - The shape blob and the TUID tables are identical.
  - Every instance is in exactly one leaf.
  - Every box equals its union.
  - The header bounds equal the union of the instance boxes.
  - The transformed vertices of the new instances lie in their boxes.
  - A point query at a surface vertex returns each new instance (42/42).
  - 200 random old instances are still found.
  - Byte coverage is 100%.
- The outputs are `out/space_sector_1/default__collision.dat` and `out/space_sector_1/assetstats.dat`.

Recipe, in order (all offsets relative to the payload unless stated otherwise):

1. Find the source placement's instances: Tie bit set, owner index = the class's TUID index, matrix equal to the
   source placement's.
2. For each one, copy the 0x80-byte record and apply the new transform:
   - The matrix becomes the new placement matrix. On primitive sets keep w = the uniform scale.
   - Box and sphere: for a pure translation, shift them. Otherwise recompute the box from the shape's
     transformed vertices (or primitives) and transform the sphere centre, scaling the radius by the largest
     scale.
   - Flags +0x27: set bit 0x01 if mirrored and bit 0x04 if scaled; otherwise copy the source's.
   - Keep +0x20 (shape offset), +0x24/+0x26 (size, type) and +0x28 (owner index). Leave +0x2C at 0.
3. Append the copies at the end of the instance array. Instance count +0x04 goes up by k.
4. BVH: add one leaf {A=0, B=k (at most 255; split if larger), first=old count, box = union of the new boxes}.
   Make it an extra leaf child of the root (or of any internal node whose box you then enlarge). Root B goes up
   by 1, which must stay at or below 255. Enlarge the boxes up to the root. Re-serialise in BFS order. Keep the
   tree block at or below 0x10000.
5. Header +0x40/+0x50: the union of all boxes.
6. Re-lay out:
   - The shape blob offset +0x08 moves by 0x80 x k.
   - The BVH offset +0x30 becomes align0x80(the shape end).
   - BVH +0x00 = the new tree size and +0x04 = 0x80.
   - Section 0x2900 length and IGHW header +0x10 = the new file length.
   - On planet levels with 0x8200/0x8300, those sections move by the 0x80-aligned growth (`colwrite.wrap` does
     not do this yet).
7. `assetstats.dat` +0x8C = align0x80(the new collision.dat size).
8. Repack the PSARC entry and update `level_cached.toc`'s size for `default/collision.dat`. Make a timestamped
   backup first.
9. Zones side, from the zones audit: append the visible Tie placement (zones.dat 0x7240 / 0x72C0 / 0x72D0,
   assetlookup and so on). The collision does not reference it, but the two should share the same matrix so the
   editor pairs them.

**Conditions:**

- The class must already be in this region's TUID table.
- To use a class that is loaded in the level but has no collision entry yet, append its TUID to the table:
  count +0x68 goes up by 1, the u16 table moves (it gets a 0xFFFF entry) and the instance array may move by
  0x80. The writer recomputes all of that.
- The class can only get collision if some region already has its shapes, or you cook new ones (b).

### (b) A new shape (custom moon mesh): SAFE WITH CONDITIONS on the format side; tooling NOT YET

Steps:

1. Cook the mesh into chunks that respect the limits in section 4: at most 512 vertices, less than 64 units per
   axis, ideally at most about 700 triangles, and size u16.
2. Per chunk:
   - The 0x10-byte header.
   - The vertex block: s32 origin, 4 bytes of padding, then the 8-vertex SoA groups in slot order
     0,4,1,5,2,6,3,7.
   - The u32 triangles: indices in bits 18-26 / 9-17 / 0-8 and flags in 27-31 (bit 0 = no wall jump).
   - The material bytes, padded to 16.
   - Then the palette: `u32 n, n x u32`, padded to 16. Copy the words from a retail moon, for example
     `00000000`.
3. Append the chunks to the shape blob. Shape blob length +0x0C grows, and so does everything after it.
4. Add one instance per chunk:
   - type 1.
   - flags 0x6A, or 0x6E when scaled.
   - The owner index of the moon's class TUID, which needs to be in the table.
5. BVH and the remaining steps as in (a).

Re-serialising the whole file this way is byte-exact on retail, so the risk is in the cooker and the game, not the
layout.

### Risks, ranked

1. **The SPU budgets are unknown.** The tree must stay at or below 64 KB (STRONG). Leaf size has a retail maximum
   of 169 and per-query candidate buffers are unknown, so keep leaves small (42 is fine: ss3 has 57). The
   triangle and vertex limits per chunk are only observed. Adding a moon to a space sector is far from all of
   these.
2. **Header bounds that grow change the run-time cell grid.** Its fixed buffers (0x800 / 0x2000) are not
   decoded. Keep new collision inside the existing header bounds; the proof did, and `mkinstance` warns
   otherwise.
3. **Not tested in game.** The BVH walk order or the instance order may matter to the SPU in ways a data check
   cannot show. Retail instances are in leaf order, while the proof appends at the end, out of that order.
4. **Material word meaning is open.** Copying a retail moon's palette avoids the question.
5. **Moved Ties.** ReLunacy's in-place writer still skips the BVH refit (section 5).
6. **Planet levels only.** The 0x8200 record count equals the number of Tie placements (axiom 5,897, krell
   9,312) and its u16 index is 0..count-1, so a new Tie placement there likely needs a 0x8200 record. Space
   sectors have no 0x8200.

## 8. Not determined

- The SPU query code: the traversal, the per-query buffers, and what the hit +0x60 word is.
- Flags bits 0x02, 0x08, 0x20 and 0x40, and the exact rule for bit 0x04.
- The meaning of the bytes in the material words.
- Primitive descriptor fields (see collision.md).
- The contents of 0x8200 and 0x8300 beyond the record count.
- The meaning of the run-time grid buffers G+0xB4 / G+0xB8 and whether large bounds can overflow them.
- Who reads assetstats +0x8C, if anything does.

## Scripts (`dev/tools/colresearch/`)

| Script | What it does |
| --- | --- |
| `colfmt.py <level>` | Parser: header, TUID tables, instances, BVH nodes |
| `bvh.py <level>...` | Walks the BVH and checks coverage, tightness, depth and fan-out (section 2 table) |
| `link.py <level>...` | Collision instance to Tie placement / class matching |
| `shapes.py <level> [name]` | Per-class palettes, materials and triangle flags; the mesh decoder |
| `coverage.py <level>... / --file p` | Byte coverage (`out_coverage.txt`) |
| `colwrite.py <level>...` | Rebuild from parts; byte-identical on all 10 levels |
| `mkinstance.py [level name x y z]` | Proof of (a); writes `out/<level>/` |
| `verify.py [level path]` | Re-parses the output, re-derives the BVH, runs queries and coverage |
| `gref.py <addr>`, `gfields.py` | EBOOT: functions using the collision global and the G-relative fields they touch |
