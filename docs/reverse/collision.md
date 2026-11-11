# Level collision (both engines)

Static level collision for Tools of Destruction (NPUA80965), Quest for Booty (NPEA00088), QForce
(NPEA00378), All 4 One (NPUA80695), Into the Nexus (NPUA80908) and A Crack in Time (NPUA80966).

Parser: `ReLunacy.Engine/Assets/Collision/LevelCollision.cs`, read by `Loading/Readers/CollisionReader.cs`.
Drawn and edited from the 3D viewport toolbar's collision panel (`Rendering/CollisionGeometry.cs`, `Rendering/Vulkan/VulkanRenderer.Collision.cs`, `ReLunacy/Core/Collision/CollisionEditor.cs`).

The ToD layout was first decoded by @sowelilili in
[ReLunacy-ToD-tools](https://github.com/sowelilili/ReLunacy-ToD-tools) (`docs/reverse/tod_collision.md`
there, against the EU EBOOT NPEA00452). This document re-checks it on the US release, extends it to
Quest for Booty, and decodes the new engine's format.

Measured 2026-11-09 over every level of all six games: 25 old-engine levels, 152 new-engine region
files.

## Where it lives

| Engine | File | Container |
| --- | --- | --- |
| ToD, QfB | `collision.dat` beside `main.dat`, one per level | none: a raw big-endian blob the EBOOT relocates in place (`"/app_home/%s/collision.dat"`) |
| new engine | `<region>/collision.dat` beside `region.dat`, one per region | IGHW; the blob is section `0x2900` |

The new-engine file also holds `0x8200` and `0x8300` on 126 of 152 regions (see *Not decoded*). Four
lobby regions (QForce `lobby/outfitter`, A4O `lobby/default` and `lobby/static_scene`, ITN
`lobby/default`) ship a 512-byte file whose `0x2900` has zero instances.

A Tie that several regions list is in each of those regions' files, with the same matrix and box.
On A4O's rootworm the nine regions hold 6.9M placed triangles between them, 3.7M once repeats are
removed. The overlay keeps each instance once, remembering every region it came from.

## Header (0x80 bytes, both engines)

| Offset | Type | Meaning |
| --- | --- | --- |
| `0x00` | u32 | Instance array offset (0x80 on ToD/QfB; 0x280 on the new engine, after the table at `+0x6C`) |
| `0x04` | u32 | Instance count |
| `0x08` | u32 | Shape blob offset (each instance's shape offset is relative to it) |
| `0x0C` | u32 | Shape blob length |
| `0x20` | f32 | Grid cell size: 10 on ToD/QfB, 12 on the new engine |
| `0x24` | f32 | Its reciprocal (0.1, 0.08333) |
| `0x30` | u32 | Spatial grid offset (the two engines' grids differ, see *Not decoded*) |
| `0x40` | vec3 | Bounds min |
| `0x50` | vec3 | Bounds max |
| `0x68` | u32 | New engine: entry count of the table at `+0x6C` (0x2D on agorian_arena) |
| `0x6C` | u32 | New engine: offset of a table of 8-byte records, before the instances |

## Instance (0x80 bytes)

The same fields in both engines, in a different order:

| Field | ToD / QfB | new engine |
| --- | --- | --- |
| Shape to world, mat4, row-vector (translation in the 4th row) | `+0x00` | `+0x30` |
| World bounding sphere | `+0x40` | `+0x70` |
| World box min | `+0x50` | `+0x00` |
| World box max | `+0x60` | `+0x10` |
| Flags and id (bit 29 = Tie; low 28 bits = id) | `+0x70` | `+0x28` |
| Shape offset in the blob | `+0x74` | `+0x20` |
| Shape length (u16) | `+0x78` low half | `+0x24` high half |
| Shape flags (u16) | `+0x78` high half | `+0x24` low half |

- **Tie vs world.** Bit 29 set: the matrix is a Tie placement's (fork: exact on 3852 of 3858 Ties on
  cobalia). Clear: a world chunk (terrain, shells), identity matrix, shape already in world space.
  Over ToD+QfB: 123929 Tie instances with a non-identity matrix, 739 with identity, 9114 world, all
  identity.
- **Tie link.** A Tie instance's matrix is its Tie placement's, bit for bit: matched on the file matrices,
  3852 of 3858 cobalia Ties and 7974 of 9312 krell_canyon Ties find collision, and every Tie collision
  instance of both levels (5073, 11502) finds a Tie. The unmatched Ties have no collision nearby
  (decoration). The editor uses this to move collision with its Tie.
- **Shape flags.** `0x400` = the shape is a primitive set (new engine only); it matches the shape's own
  header on every instance of all four games (e.g. 20488 of 20488 on QForce). `0x02` and `0x08` are set
  on every instance of all six games, `0x40` on all but 0.2%. `0x01`, `0x04`, `0x20` vary; the new
  engine adds `0x100`, set on almost all. The values seen are `0x2E 0x4A 0x4E 0x4F 0x6A 0x6E 0x6F` on
  ToD/QfB (world chunks only ever `0x4A`/`0x6A`) and the same with `0x100` or `0x400` on top on the new
  engine. Meaning not closed.
- **Box.** Tight on every new-engine instance; on ToD about 2% have one side trimmed short.

Checked: every vertex of every triangle-mesh instance, transformed by its matrix, lies inside its
instance's stored box, on all 25 old-engine levels and all 152 new-engine regions (0 outside; 31.4M
new-engine vertices). That holds the matrix, the box, the vertex decoding and the vertex order
together.

## Triangle mesh

| Offset | Type | Meaning |
| --- | --- | --- |
| `0x00` | u16 | Vertex count (max 512, indices are 9 bits) |
| `0x02` | u16 | Vertex block offset: 0x60 on ToD/QfB, 0x10 on the new engine |
| `0x04` | u16 | 1 on ToD/QfB, 0x100 on the new engine |
| `0x06` | u16 | Triangle count |
| `0x08` | u16 | Triangle offset |
| `0x0C` | u16 | Material offset (one byte per triangle) |
| `0x14` | 3 x vec4 | ToD/QfB only: oriented box, axis xyz + half extent |
| `0x44` | vec3 | ToD/QfB only: oriented box centre |

The new engine drops the oriented box and moves the vertex block up; everything else is the same.

Vertex block: `s32 x, y, z` origin, 4 bytes padding, then groups of 8 vertices as three lines of
8 x u16 (x line, y line, z line). Position = (origin + value) / 1024. Inside each line the slots are
ordered **0, 4, 1, 5, 2, 6, 3, 7** (fork: flat quads 96.5% planar on metropolis in this order against
46% in plain order).

Triangle: one u32, indices in bits 18-26, 9-17, 0-8, flags in 27-31. No index is out of range on any
mesh of any of the six games. Winding is not consistent (cobalia's world floors face up and down
about equally), so the overlay draws both sides.

### Triangle flags (bits 27-31)

- **Bit 0: no wall jump.** The fork traced ToD's wall-jump entry (`Hero_TryEnterWallJumpState`, EU
  `0x0020822C`): it requires `(hit triangle flags & 1) == 0`. Bit-0 faces are 51% walls by area
  against 39% for unflagged ones.
- Values seen: 0-7 on ToD/QfB, 0-15 on the new engine (bit 4 never set). Value 7 is 89% walls,
  value 4 72%. Not otherwise closed.

### Materials

One byte per triangle, ids 0..67 over ToD/QfB. Some are almost purely horizontal (49: 100% floor or
ceiling, 48: 95%, 50: 94%, 5: 84%, 46: 83%), which suggests water, lava or death planes, but that is a
guess. No name table exists on the PPU side of either EBOOT: the queries that read the triangle run on
SPU (no PPU code extracts the 9-bit indices), so naming the materials needs the SPU job, or live
memory as the fork did for the wall jump. The overlay colours by id.

## Primitive set (new engine)

Flagged by `0x400` in the instance's shape flags. 20-50% of instances on QForce, A4O and ITN use one;
1% on ACiT. Up to 16 primitives.

| Offset | Type | Meaning |
| --- | --- | --- |
| `0x00` | u16 | Offset of the first block (0x20) |
| `0x02` | u16 | Primitive count |
| `0x10` | 4 x u8 | Count per kind: box, sphere, capsule, cylinder (they sum to the total) |

Then for each kind in that order, for each group of four primitives of that kind:

1. four 16-byte descriptors,
2. the group's data as SoA rows: one vec4 per value, lane k = primitive k of the group, unused lanes
   padded with 1.0.

| Kind | Rows per group | Data |
| --- | --- | --- |
| 0 box | 15 | centre xyz, 3x3 rotation (rows = the box's axes), half extents xyz |
| 1 sphere | 4 | centre xyz, radius |
| 2 capsule | 7 | end A xyz, end B xyz, radius |
| 3 cylinder | 7 | end A xyz, end B xyz, radius |

Descriptor: `+0x00` kind code (4 box, 5 sphere, 6 capsule, 8 cylinder), `+0x01` 0..2, `+0x03` 0xFF
(rarely 0, 10, 16), `+0x08` u16 index of the primitive in the set, `+0x0A` a byte that looks like a
mask (0x1F on most, 0x17 on most cylinders, also 0x0F/0x27/0x2F/0x37/0x3F), `+0x0C` f32 1.0 (0.0 on
most ACiT sets).

Checked over every primitive set of all four games:

- The length this layout gives equals the instance's recorded shape length on all of them.
- The union of the primitives' world boxes equals the instance's stored box (to 0.02) on 131082 sets
  and lies inside it on the other 51; none stick out.
- Capsule and cylinder have the same data, so the box decides: kind 2 is tight on all 45 kind-2-only
  sets only when read as a capsule (as a cylinder it falls short), and kind 3 is tight on 1277 and
  inside on 46 as a cylinder, but sticks out on 1323 as a capsule.

## Not decoded

- Triangle flag bits 1-4, the material ids, the shape-flag bits other than `0x400`, and the primitive
  descriptor's `+0x01`, `+0x03`, `+0x0A`, `+0x0C`.
- The instance id (low 28 bits of the flags-and-id word). On ToD it counts up across the chunks of one
  Tie class.
- The spatial grid at header `+0x30`. ToD's starts with an origin and the inverse cell size (cobalia:
  `(-440, -40, -410)`, 0.1), and the fork notes cell words of the form `count << 20 | first`. The new
  engine's has a different header (agorian_arena: `0x1280, 0x80, 0...`). Only needed for queries.
- The new-engine table at header `+0x6C` (8-byte records; the second word, `0xD2xxxxxx`, recurs in
  `0x8200`).
- `0x8200`: little-endian, unlike the rest of the file. Header `+0x04` = section length, `+0x08` =
  0x30 (header size), `+0x14` = record count (439 on agorian_arena, 0x30 + 439 x 16 = the length).
  Records are 16 bytes: u16 index, u16 value (every one a multiple of 60: 180, 300, 600, 1260, 3600...,
  i.e. likely times in frames), then 12 bytes whose last word is again `0xD2xxxxxx`.
- `0x8300`: a grid of some kind (`+0x10` = 3.0, then signed 16-bit cell bounds). Not geometry.
