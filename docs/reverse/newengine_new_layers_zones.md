# New-engine new layers and new zones (for "Create moon")

> Status (2026-11-16): engine API done, baked lighting copied, assetstats +0x00 raised (section 5). `GpPriusDocument.AddLayer`, `GpLayers.Add` and `Ighw02` are in
> `ReLunacy.Engine/Packing/GpPriusDocument.Layers.cs`. `TieAppender.CreateZone` is in `TieAppender.Zones.cs`. The
> headless probe `dev/tools/moonzoneprobe` passes on space_sector_1..5, including `--full` reloads, and its round
> trips pass on all 40 levels of A Crack in Time, All 4 One and Into the Nexus. Nothing has been tested in game, and
> nothing is wired into the UI or into File > Save.

This builds on `newengine_gp_prius_writer.md`, `acit_gp_prius_coverage.md`, `newengine_tie_append.md` and
`acit_zones_coverage.md`. EBOOT addresses are for A Crack in Time (NPUA80966) `EBOOT.elf`.

## 1. Naming (space_sector_1..5)

A moon `X` (for example `moon1d`) has:

- the layers `art_moonX` and `gameplay_moonX`;
- the zone `art_moonX` in region `default`.

The sector digit is the level's. Retail does not always have all three:

- ss1 has the zones `art_moon1x` and `art_moon1y` but no art layer for either;
- ss3 has the zones `art_moon3a`, `art_moon3e` and so on with only `art_moon3b` as a layer;
- shrub zones (`art_z_moon_shrubs`, `art_moon3a_shrubs`) have no layer.

The art layer holds the moon's art Mobys, if any. The zone holds its Ties. The game never ties a zone to a layer
(section 3).

Retail sorts every list by name: gp_prius layers, gameplay.dat names, metadata.dat names, and the region.dat zone
list. Nothing the game does depends on that order:

- layers are matched by a linear `strcmp` search (0x61331C, 0x612F7C);
- the zone list is sorted by TUID at load (0x5878F8).

So new entries are appended last.

## 2. Layers

### What lists a layer

Each file below was checked on all 40 levels.

| File | Where | Invariant |
| --- | --- | --- |
| gp_prius.dat (per region) | 0x25008 records, 0x2500C records, 0x25005 +4, 0x25010 +0x44, 0x25014 u16 list | 0x25014 = 0..n-1 on every one-region level |
| `<region>/metadata.dat` | 0x25098 `{0, count, ptr -> 0x25110, ptr -> 0x25104}`, 0x25100 names, 0x25110 name pointers, 0x25104 zero words | names == gp_prius names, same order, 40/40 |
| gameplay.dat (IGHW 0.2) | 0x25000 +0 name pointers, +4 count, +8 64-bit mask; region records (64-bit masks) | see below |
| instances.lua / .lc (+ .trimmed) | `handle:new(layer, index)` | unchanged: existing layer indices do not move |
| level scripts (ss1..5) | none | no script names a layer or a zone |
| level_cached.toc | sizes | rewritten for every changed file |

**gameplay.dat 0x25000.** In A Crack in Time it is 0x50 bytes, with region count / name pointers / record pointer
at +0x40 / +0x44 / +0x48. In All 4 One and Into the Nexus it is 0x68 bytes and the same three are at +0x58 / +0x5C /
+0x60. Each region record is a big-endian u64 layer mask, where bit i is gameplay.dat layer i (bit 0 is the least
significant bit).

The masks behave as follows:

- On every ACiT level, the level mask (+8) and the region mask are both 2^n - 1.
- On A4O and ITN, the level mask is the AND of the region masks on every level.
- Each layer name is its own 0.2 chunk.

**Game evidence.**

- 0x61331C maps each gp_prius layer by name to its gameplay.dat index. A missing name maps to 0xFFFF.
- The loader 0x3314BC creates layer i only when `(mask >> index) & 1` is set (`srd` on the 64-bit mask at loader
  +0x778). A shift of 0xFFFF gives 0, so **a layer gameplay.dat does not name is never created**.
- 0x332634 takes the mask from the region record of the region being loaded (`root+0x48[region]`). It loads the
  layers that are newly set and unloads the ones that were cleared.

### API

```csharp
GpEditResult GpPriusDocument.AddLayer(string name);    // gp_prius only; result.CreatedLayer = index (last)
GpLayerAddResult GpLayers.Add(GpPriusDocument doc, byte[] metadataDat, byte[] gameplayDat, string name);
    // -> Layer, Metadata (new bytes), Gameplay (new bytes), Changes, Problems; doc edited in place
```

`GpLayers.Add` checks all three files before it writes anything. It then:

- appends a zeroed 0x25008 record (0x80 bytes, or 0x90 on A4O / ITN) with the name, and a zeroed 0x2500C record;
- updates the counts and appends the index to 0x25014;
- in metadata.dat, appends the name, a pointer (new fix-up) and a zero word, and updates the count. The file goes
  through `GpPriusDocument.Parse` as a generic IGHW 1.1 file;
- in gameplay.dat, adds a new name chunk after the last name chunk, grows the pointer chunk with a new fix-up,
  updates the count, and sets bit n in both the level mask and the region mask.

It refuses when:

- the name is not 1-63 printable ASCII characters, or is already used;
- there are already 64 layers;
- the files list different layers;
- the masks are not all-ones;
- **the level has more than one region**.

A4O and ITN multi-region levels share layers between regions by name and give each region its own mask. Which
regions a new layer belongs to is a design decision, so those levels are refused. Result: ACiT 22/22 levels
supported, ITN 3/8 (battle_arena, clank_puzzles, swamp_wasteland), A4O 0/10.

**Ighw02** (gameplay.dat) layout:

- header 0x10;
- one 0x10-byte entry per chunk: `{id, offset, length, 0}`;
- chunks in table order on 16-byte boundaries, with PADn filler that restarts in each gap;
- the fix-up table `{count, sorted offsets}` at the next 16-byte boundary;
- 8 zero bytes.

Parse -> Serialize is byte-identical on all 40 levels.

## 3. Zones: what makes a zone load and draw

The game reads region.dat 0x1C000 / 0x1C010 at 0x57C0F0, into loader +0x241C (names), +0x2420 (TUIDs) and +0x2424
(count). It reads assetlookup 0x1DA00 / 0x1DB00 at 0x57C638, into +0x22F4 / +0x22F8 and +0x22FC / +0x2300.

Zone setup 0x5878F8 then works as follows (decompiled):

1. For each region.dat zone, it reuses the zone slot that has the same TUID, or takes a free one. There are 64
   slots of 0x80 bytes, tracked by a 64-bit bitmap at +0x2100, and the name is copied in, at most 0x27 characters.
   Note: when every slot is taken the slot pointer is 0 and is written through. **More than 64 zones in loaded
   regions crashes**, so CreateZone refuses past 64.
2. It sorts the slots by TUID.
3. It looks up each TUID linearly in assetlookup 0x1DA00, `{TUID, offset, length}`, and streams in that blob.
4. It looks up the lighting slice in 0x1DB00. An entry with length 0 is skipped, and a zone with no entry has no
   slice. 9 of ss1's 10 zones have one.
5. 0x584310 registers the zone's Ties cluster by cluster (`acit_zones_coverage.md` section 5).

**There is no other gate.** The 0x1DA01 masks are not read by this path. ReLunacy uses them for its region view.
Neither layers nor scripts switch zones on: no ss1..5 script mentions a zone or a layer, and the gameplay layer mask
covers gp_prius layers only. assetstats has no zone counter: it was checked against all 40 levels, and no field
equals the per-region zone peak.

**So a zone that is listed in its region's region.dat 0x1C000 / 0x1C010 and has a blob in 0x1DA00 is loaded with
the region. Its Ties draw through their 0x72D0 clusters**, subject to the cluster pool (assetstats +0x30), which
`TieAppender` already keeps within its rounding.

### The new zone's blob

Sections are copied from the signature of a retail Tie-only zone, in table order:

- 0x7200, 0x7240, 0x72C0, 0x72C1, 0x11300, 0x72D0;
- 0x6000, 0x6100, 0x7815 and 0x8D00, empty;
- 0x8120: 512 × 0x00FFFFFF;
- 0x8110: `{0x02000001, ptr -> 0x8120, 0…}`.

The 0x8110 / 0x8120 form is the one every one of the 59 space-sector zones has.

No retail zone has an array section with count 0. **So a zone is made with its first Tie:** the empty tables exist
only inside the call. They get the first Tie's record, name, class, cube entry and its own cluster before anything
is returned. If the first Tie is refused, nothing is made.

### API

```csharp
sealed record ZoneCreateRequest(string Name, string Region = "default");
TieAppendResult TieAppender.CreateZone(Func<string, byte[]?> read, ZoneCreateRequest zone, IReadOnlyList<TieAppendRequest> ties);
    // ties[i].ZoneTuid / Region are replaced; result.CreatedZone = the new zone's TUID
```

Files touched:

| File | Change |
| --- | --- |
| zones.dat | Blob inserted in TUID order (retail keeps 0x1DA00 sorted by TUID with ascending offsets); later blobs move by its 0x80-aligned size |
| assetlookup.dat | 0x1DA00 entry + 0x1DA01 mask, inserted in TUID order. The mask is the region's bit in gameplay.dat region order. A 0x1DB00 / 0x1DB01 entry of length 0 is inserted, and is removed again if no Tie got prelight. The file is re-laid with `IghwRelayout` (identical on 40/40) |
| lighting.dat | The slice the Ties' copied prelight fills |
| `<region>/region.dat` | Name appended to 0x1CF00, pointer (new fix-up) to 0x1C000, TUID to 0x1C010. Re-laid; identical on 40/40 |
| assetstats.dat | +0x08 = the largest region.dat size rounded to 0x80 (holds on 40/40 levels). +0x30 Tie clusters, +0x0C collision, as for any append |
| `<region>/collision.dat` | Each Tie's collision, copied from its source placement (`newengine_tie_append.md`) |
| level_cached.toc | Sizes |

The TUID is FNV-1a of `zone/<region>/<name>/<salt>`, and is unique among every assetlookup TUID and the region's
zone TUIDs. `TieAppender.Splice` now moves a blob that starts exactly where an empty (length 0) blob is being
filled, which is what the new slice needs.

## 4. Probe (`dev/tools/moonzoneprobe`)

Usage: `moonzoneprobe <ACiT levels dir> [--full] [--all] level...`. It reads the archives read-only. For ss1 it uses
the shipped `.bak`. `--full` writes patched archives to `./work`, which was deleted after the run.

- **`--all`**: gameplay.dat (Ighw02), every metadata.dat, region.dat and assetlookup.dat are byte-identical after a
  round trip on 22 + 10 + 8 levels. `GpLayers.Add` is applied on 22 ACiT and 3 ITN levels, and the result validates
  and round-trips. It is refused on the 15 multi-region levels, with the document untouched.
- **Per sector**: it adds `art_moon<N><x>` and `gameplay_moon<N><x>`, where x is the next free letter (ss1 moon1d,
  ss2 moon2e, ss3 moon3c, ss4 moon4e, ss5 moon5c). It then makes zone `art_moon<N><x>` with 3 Ties: the class of the
  sector's first moon's largest Tie (its surface), and two more classes of that moon, 4 radii away. Checks:
  - gp_prius validates;
  - every other gp_prius section is identical (pointer words compared by target);
  - old layer records name the same runs, and the new layers are empty;
  - metadata.dat and gameplay.dat list the layers, the masks have their bits, and the budgets are unchanged;
  - a missing first Tie and a duplicate zone name are refused;
  - region.dat lists the zone, and its other sections are identical;
  - 0x1DA00 is sorted and aligned with mask 1;
  - every old zone blob and lighting slice is byte-identical at its new offset;
  - the Ties have the right names, classes and matrices, the clusters partition them, and the fix-ups and cubes are
    in range;
  - `Objects.Zone` reads the zone;
  - prelight is inside the slice;
  - assetstats +0x08 and +0x30, and the toc sizes, are right;
  - the old collision instances are unchanged and the BVH is valid.
- **`--full`**: `LevelReader` loads the patched archive. The zone is found by TUID with its name and 3 Ties with
  class assets and the right matrices, the zone count is +1, the region's layers end with the two new ones, and
  `CollisionReader` reads the file.

Results (2026-11-16): ss1..5 176 checks ALL PASS, and with `--full` 201 checks ALL PASS. `--all` passes. The
tieappendprobe regression (ss1, ss2, krell_canyon, 160 checks) passes.

## 5. Baked lighting and the resident size (added 2026-11-16)

**Baked copies keep their lighting** (`TieAppender.Lighting.cs`). Every moon surface placement is baked. When a source
placement is baked and the target zone has a slice and 0x5400 / 0x5410 tables, `BakedLighting` copies the source's
lighting into the target:

- **The lightmap pair.** Record +0x5A indexes the source zone's 0x5400 / 0x5410 descriptors. Both DXT1 payloads
  (every retail lightmap is DXT1 and 0x80-aligned, 10,767 of 10,767) go to the end of the target slice. Two
  descriptors are appended with the new offsets, and +0x5A names them. The pair is copied even within one zone,
  because retail never shares a pair.
- **The prelight block** (+0x54).
- **Unchanged:** +0x60..+0x6C, the baked-light constant.
- **assetstats +0x14** becomes the per-region total of lightmap pairs. On ss1, 328 is the sum of its zones' 0x5400
  counts.

A new zone carries empty 0x5400 / 0x5410 tables for this, and drops them again when no Tie is baked
(`IghwRelayout.WithoutSections`). The source is now picked in this order:

1. same zone, unbaked, with prelight;
2. another zone, unbaked, with prelight;
3. same zone, unbaked;
4. same zone, any;
5. any baked placement (when the zone can take a pair);
6. any unbaked placement;
7. anything.

When the pair can not be copied, the copy is still made unbaked, as before.

**Probe.** Every baked copy owns a pair whose descriptor and payload bytes equal one of the source zone's pairs. With
`--full`, `LevelReader` resolves the new placement's `LightmapIndex` to colour and direction textures whose decoded
pixels equal its source placement's, and `NewEngineBakedLight` is equal too. Results: moon1d's surface is a copy of
moon1a, ss4's of moon4d, ss5's of a_moon5a. space_sector_1..5 `--full`: 240 checks pass. tieappendprobe on 9 ACiT
levels: 484 checks pass, and its case-a baked copies now get their own pairs.

**assetstats +0x00 is the main streaming pool.** It goes from 0x32CE90 to 0x57F368, which carves it into 0x80-byte
blocks after subtracting what 0x580508 already took. Zones stream into this pool (0x5878F8 allocates from loader
+0x20). Lighting slices stream into the pool at +0x40, whose size is the heap's free remainder, not an assetstats
value.

On 22 of 22 ACiT levels, +0x00 equals the sum of these 0x80-aligned sizes:

- cubemaps.dat, ties.dat, shrubs.dat, mobys.dat, animsets.dat, cinematics.dat;
- zones.dat;
- collision.dat;
- plus a level-specific 0 to 0xB300 bytes, always a multiple of 0x80. It is exactly 0 on front_end. The extra was not
  identified, and does not track any asset count.

`TieAppender` therefore raises +0x00 by the 0x80-aligned growth of zones.dat and every collision.dat. A new moon
with 3 Ties adds about 0x1000 of zone blob plus its collision.

## 6. Open risks

- **Not tested in game.**
- **Lighting pool**: each copied surface pair adds about 350 KB to lighting.dat. Its pool is sized from free memory,
  so a large moon could exhaust it. The headroom is not known.
- **Prelight block length** is an upper bound and copies up to 38 KB extra per Tie.
- **Layer order**: the new layers are last, not sorted by name as in retail. No game path depends on the order.
- **Zone list order**: region.dat's zone list is no longer sorted by name. The game sorts by TUID itself.
- **Cluster pool**: each new zone needs at least one Tie cluster. When assetstats +0x30 is at its rounded-up 16 the
  zone is refused. Free slots were ss1 8, ss2 5, ss3 4, ss4 10, ss5 4.
- **Zone slots**: 64 per loaded set of regions. ACiT sectors use 8-17.
- **Gameplay**: an empty `gameplay_moonX` layer is created at load. A moon also needs its SpaceMoon Clue, volumes
  and Pods in that layer; see `acit_space_sector_classes.md`.
- **Multi-region levels** (A4O, most of ITN) are refused for layers. `CreateZone` handles them by region bit, but
  that was only probed on ACiT.
