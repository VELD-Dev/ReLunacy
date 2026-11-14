# A Crack in Time: zone data coverage, and what adding Ties / UFrags takes

Audit of 2026-11-14, read-only. Game: A Crack in Time (NPUA80966), new engine. Levels measured:
space_sector_1 (the SHIPPED archive, `level_cached.psarc.2026-11-12_01-20-52.bak`), space_sector_2..5,
front_end, axiom_city, krell_canyon, zolar_forest, tombli. Every ACiT level checked has one region
(`default`).

Tools (gitignored, `dev/tools/zonecoverage/`):

| Script | What |
| --- | --- |
| `extract.py [levels]` | copies zones / lighting / region / collision / metadata / assetlookup / assetstats / ties / toc / gameplay of each level into `data/<level>/` |
| `coverage.py <level> [--zones f]` | tags every byte of zones.dat, of the zones' lighting.dat slices and of region.dat; prints % accounted, unaccounted ranges with a hex sample, and section bytes by decode status |
| `mkappend.py <level> <zone> [i] [dx dy dz]` | proof of recipe (a): writes `data/<level>/mod/{zones,assetlookup}.dat` with one duplicated Tie placement appended |
| `verify.py <level> <zone>` | re-parses the modified copy and checks it against the original |
| `ebscan.py` | finds code that materialises / compares zone section ids in the EBOOT |

"PROVEN" = checked on all data listed above or read in the EBOOT; "STRONG" = consistent everywhere
measured, not traced in code; "ASSUMED" = reasoning only.

## 1. Byte coverage

`coverage.py` tags: header, section table, fix-up table, records of each section, data reached through
an offset / pointer (vertex + index ranges, strings, texture mips, prelight words, pointer-addressed blobs),
pad (zeros and the builder's `PAD0PAD1...` filler), UNACCOUNTED.

| Level | File | Size | Accounted |
| --- | --- | --- | --- |
| space_sector_1 (shipped) | zones.dat | 1,230,976 | 100.000% |
| | lighting.dat | 11,745,280 | 100.000% |
| | region.dat | 45,120 | 100.000% |
| front_end | zones.dat | 5,632 | 100.000% |
| | lighting.dat | 0 (empty file) | - |
| | region.dat | 5,912 | 100.000% |
| axiom_city | zones.dat | 7,338,496 | 100.000% |
| | lighting.dat | 59,208,960 | 100.000% |
| | region.dat | 379,204 | 100.000% |
| krell_canyon | zones.dat | 10,400,000 | 100.000% |
| | lighting.dat | 57,811,712 | 100.000% |
| | region.dat | 447,552 | 100.000% |

No unaccounted range is left. Three things had to be found to get there (all new):

- **Gaps are `PAD0PAD1...` ASCII filler**, not zeros: every section starts on a 0x80 boundary and the
  gap before it is filled with that text. Between zone blobs (and at file end) the filler is zeros.
- **Two-layer UFrags carry a second vertex stream**: a UFrag whose record +0x52 (second material) is not
  0xFFFF has `vertexCount x 4` more bytes right after its `vertexCount x 0x18` vertices in 0x6000
  (1,681 of the 1,703 tails of that length; the rest of the gaps are zero alignment). ReLunacy does not
  read it (UFrag.cs reads 0x18-byte vertices only). Likely the blend weight; not decoded.
- **Shrub placements have prelight in lighting.dat**: Shrub record (0x7540) +0x2C is an offset into the
  zone's lighting.dat slice, like a Tie placement's +0x54 (space_sector_1: 12,862 Shrub placements,
  8.5% of lighting.dat). Block size is not known (coverage infers it as "up to the next block start");
  ReLunacy's ShrubInstance does not name the field.

Note: "100%" is structural - every byte is inside something whose extent is known. How much of the
*meaning* is known is the decode status of each section (table in section 2). Bytes in record sections,
by status (whole data sections like vertex buffers excluded):

| Level | read by ReLunacy | partly read | decoded, not read | undecoded |
| --- | --- | --- | --- | --- |
| space_sector_1 | 874,888 | 240,512 | 8,112 | 26,112 |
| front_end | 16 | 128 | 80 | 656 |
| axiom_city | 353,024 | 1,259,712 | 34,472 | 123,496 |
| krell_canyon | 1,070,192 | 1,671,296 | 44,368 | 177,296 |

lighting.dat, how it splits (space_sector_1 / krell_canyon): Tie prelight 34.5% / 18.1%, 0x5400 colour
lightmaps 28.2% / 52.8%, 0x5410 direction maps 28.2% / 17.6%, Shrub prelight 8.5% / 0.6%, 0x5700 blend
masks 0 / 7.7%, 0x5200 textures (via 0x6641) 0 / 0.8%, pad 0.5% / 2.4%.

region.dat: every section's extent is known; zone-related part decoded below. Sections 0x19xxx / 0x1Axxx
(navigation / gameplay volumes on planet levels) and 0x81B0 / 0x81C0 / 0x81E0 / 0x8B00 / 0x8100 / 0x11400
/ 0x11000 (region lighting and sky) are not zone geometry and were not decoded here.

## 2. Section inventory (zones.dat)

Counts are totals over 105 zones of the 10 levels. "ReLunacy" cites the reader. "Game" is the EBOOT
consumer (section 5).

| Id | Stride | Zones / records | What | Scope | ReLunacy | Game |
| --- | --- | --- | --- | --- | --- | --- |
| 0x7200 | 8 | 102 / 1,646 | Tie class TUIDs; placement +0x50 indexes it | zone | read, ZoneReader.cs:575 | 0x5878F8 (count kept) |
| 0x7240 | 0x80 | 102 / 36,149 | Tie placements | per Tie | partial, TieInstance.cs | 0x584310 (pointer only, NO count) |
| 0x72C0 | 0x10 | 102 / 36,149 | {TUID, char* -> 0x11300, 0} per placement | per Tie | read, ZoneReader.cs:590 | 0x584310 (pointer only) |
| 0x72C1 | 8 | 102 / 139 | Tie cubemap palette (+0x5D indexes) | zone | read, ZoneReader.cs:596 | 0x5878F8 |
| 0x72D0 | 0x20 | 102 / 3,895 | **Tie clusters** {sphere, u32 first, u32 first, 0, u16 count, 0} | per Tie cluster | NOT read | 0x584310 -> 0x5FA618 (count kept) |
| 0x6000 | whole | 105 | UFrag vertices, 0x18/vertex (+4/vertex tail on two-layer) | zone | read, ZoneReader.cs:383 | 0x5878F8 (RSX offset) |
| 0x6100 | whole | 105 | UFrag indices, u16 | zone | read | 0x5878F8 |
| 0x6200 | 0x80 | 23 / 10,699 | UFrag records | per UFrag | partial, UFragMetadata.cs | 0x5878F8 (count kept) and 0x584310 |
| 0x6240 | 0x20 | 23 / 1,068 | **UFrag clusters** {sphere, u32 first, 0, 0, u16 count, 0} | per UFrag cluster | NOT read | 0x584310 -> 0x60587C (count kept) |
| 0x6280 | 0x10 | 23 / 1,068 | parallel to 0x6240, two TUID-like u64 (high halves 0x0776xxxx) | per UFrag cluster | NOT read | copied verbatim into the cluster pool (0x60587C) |
| 0x6400 | 0x10 | 23 / 10,699 | parallel to 0x6200: {u32 index (a permutation), f32, f32, 0} | per UFrag | NOT read | 0x5878F8 (pointer) |
| 0x6641 | 0x50 | 12 / 42 | records with a fixed-up pointer (+0x38) to 0x5200 | zone | NOT read | 0x5878F8 (count kept) |
| 0x5200 | 0x20 | 6 / 19 | RSX texture descriptors, payload in lighting.dat slice | per 0x6641 | NOT read | 0x5878F8 |
| 0x5A00 | 8 | 6 / 19 | {u32 id, 0x00008000} parallel to 0x5200 | | NOT read | - |
| 0x5400 / 0x5410 | 0x20 | 82 / 23,889 | baked colour / direction lightmap descriptors, payload in lighting.dat | per Tie placement or UFrag atlas | read, NewEngineZoneLightingReader.cs:109 | 0x5878F8 (0x5400 count kept) |
| 0x5700 / 0x5900 | 0x20 / 8 | 9 / 39 | blend masks + ids | zone | read, NewEngineZoneLightingReader.cs:312 | 0x5878F8 |
| 0x5C00 / 0x5C01 | whole | 9 | UFrag UV animation / companion | | 0x5C00 read (ZoneReader.cs:303), 0x5C01 not | 0x5878F8 |
| 0x71A0 | 8 | 23 / 303 | UFrag shader TUIDs | zone | read, ZoneReader.cs:321 | 0x5878F8 |
| 0x71C0 | 8 | 23 / 42 | UFrag cubemap palette | zone | read, ZoneReader.cs:393 | 0x5878F8 |
| 0x7400 / 0x7440 / 0x74C0 / 0x74D0 | 8 / 0x80 / 0x10 / 0x20 | 10 / 14, 197, 197, 27 | **foliage** classes (all in assetlookup 0x1D400) / placements / names / clusters - the Tie pattern again | per foliage | only 0x74C0 named (NewEngineZoneNames.cs) | 0x5878F8 (classes), 0x584310 |
| 0x7500 / 0x7540 / 0x75D0 | 8 / 0x40 / 0x20 | 29 / 61, 88,576, 9,558 | Shrub classes / placements / groups | per Shrub | read (0x75D0 not), ZoneReader.cs:206 | 0x5878F8, 0x584310 |
| 0x7800 | 0x80 | 31 / 457 | dynamic lights (names `_DYN_hutLight1`, `spotLight2`...) | per light | NOT read | 0x584310 (count kept) |
| 0x7810 | 0x10 | 31 / 457 | light names {TUID, char*, 0} | per light | NOT read | 0x584310 |
| 0x7820 | 0x20 | 31 / 457 | per-light record | per light | NOT read | 0x584310 (count kept) |
| 0x7805 / 0x7815 | 0x10 / whole | 4 / 156 | light projector refs (+4 fixed-up -> 0x7815) | per light texture | NOT read | 0x584310 |
| 0x8C00 / 0x8C01 / 0x8D00 | 0x100 / 0x10 / whole | 21 / 908 | volumes (`naVolForCubemap3`, `lightingVolume1`, `*_pip_cam`); +0xA8 fixed-up -> 0x8D00 | per volume | NOT read on the new engine (LightingEnvironmentReader is ToD main.dat) | 0x584310 (count kept) |
| 0x8110 / 0x8120 | 0x30 / 0x800 | 105 | {0x02000001, ptr -> 0x8120, 0...} and 512 x 0x00FFFFFF; identical in every zone and in region.dat | zone | NOT read | 0x5878F8 (0x8110 pointer) |
| 0x11300 | whole | 104 | string pool for every name pointer | zone | read through the pointers | - |

**0x07810 / 0x07820 answered**: they index neither Ties nor UFrags. They are parallel to 0x7800, the
zone's dynamic lights (0x7810 = the lights' names). The two per-zone tables that DO index Ties and UFrags
are **0x72D0** and **0x6240**: contiguous partitions of 0x7240 / 0x6200 into clusters with a bounding sphere
(every zone of every level: clusters start at 0, have no gaps, end at the record count; 99.8% of member
spheres are inside the cluster sphere, the rest by float noise). 0x72D0 +0x10 == +0x14 on all 3,895.

## 3. How a zone is structured

- zones.dat is a **concatenation of independent IGHW 1.1 blobs**, one per zone, each at a 0x80-aligned
  offset with zero fill between them. There is no table inside zones.dat: `assetlookup.dat` 0x1DA00 gives
  {zone TUID, offset, length} per zone and 0x1DA01 the region mask. region.dat 0x1C010 lists a region's
  zone TUIDs and 0x1C000 their names (`art_moon1a`...). The game finds zones through 0x1DA00
  (0x57C638 materialises 0x1DA00 / 0x1DB00).
- lighting.dat is the same scheme: 0x1DB00 {zone TUID, offset, length}. A zone with no baked light has no
  slice (space_sector_1: 10 zones, 9 slices).
- Blob layout (PROVEN on all 105 zones): header 0x20 (`IGHW`, 1.1, +0x08 section count, +0x0C header
  length = 0x20 + 16 x count, +0x10 fileLength = end of the last section, +0x14 fix-up count, +0x18
  `DEADDEADDEADDEAD`), section table, sections in table order each at a 0x80 boundary (PAD filler
  between), then the fix-up table (u32 offsets) at fileLength; blob length = fileLength + 4 x fix-ups.
  Array sections have bit 0x10000000 in the count and the stride at +0x0C; others the byte length.
- **Fix-ups**: sorted ascending in all 105 zones; all type 0 (top two bits 0); the game's IGHW opener
  0x5D5308 checks `IGHW` 1.1 and adds the blob base to each u32 listed (type 0x40000000 = 64-bit, unused
  here). The only pointers in zone blobs are: 0x72C0 / 0x74C0 / 0x7810 / 0x8C01 +8 (names -> 0x11300),
  0x8110 +4 (-> 0x8120), 0x7805 +4 (-> 0x7815), 0x8C00 +0xA8 (-> 0x8D00, 732 of 908, the rest 0),
  0x6641 +0x38 (-> 0x5200). Everything else is a relative offset: UFrag +0x40 / +0x44 into 0x6100 / 0x6000;
  Tie +0x54 and Shrub +0x2C and every 0x5x00 descriptor +0x00 into the zone's lighting.dat slice; indices
  (Tie +0x50 -> 0x7200, +0x5A -> 0x5400/0x5410, +0x5D -> 0x72C1; UFrag +0x4E, +0x50 -> 0x71A0, +0x59 ->
  0x71C0; cluster first/count -> records).
- A Tie placement is tied to: its class (0x7200 entry), its name record (0x72C0, same index), its cluster
  (0x72D0 range containing its index), its prelight block (lighting slice, +0x54), its lightmap pair
  (0x5400/0x5410 entry +0x5A, payloads in the lighting slice), its cube palette entry (+0x5D), and -
  outside the zone - possibly a collision instance in region `collision.dat` (matched by matrix only).

## 4. Hidden dependencies

| Dependency | Finding | Status |
| --- | --- | --- |
| Tie clusters 0x72D0 | The game walks Tie placements ONLY through clusters (section 5). A placement outside every cluster is never registered. | PROVEN (EBOOT) |
| Cluster pool size | `assetstats.dat` 0x9800 +0x30 = total Tie clusters of the level (space_sector_1 248, krell 835: exact); the loader reserves `roundup16(+0x30)` slots (0x580028 -> 0x5F87E8). UFrag clusters +0x28 (krell 391 exact), Shrub groups +0x40 (725 / 721), Shrub classes +0x3C, Tie classes +0x2C, lightmaps (0x5400 total) +0x14 (328 / 7,795), 0x6641 records +0x44. The cluster allocator 0x960C34 returns -1 when full and the register function 0x5FA618 then uses slot 0. | field meaning PROVEN by match, sizing call PROVEN, overflow behaviour PROVEN in code |
| Per-instance lighting | Tie +0x54 = prelight words in the zone slice; size = 4 x (max first+count over all primitives) unbaked, 4 x (first - secondary + count over non-lightmapped primitives) baked; Shrub +0x2C likewise (size not derived). Retail shares non-empty prelight blocks between placements (2-18 placements on one block in every level) and gives 0-word placements an offset. 0xFFFFFFFF = none, shipped on unbaked placements with +0x64/+0x66 = 0. | PROVEN in data |
| Lightmaps | One 0x5400/0x5410 pair per baked Tie placement; no two Tie placements share a pair in any level. A pair's payload is in the zone's slice. Count feeds assetstats +0x14. | PROVEN in data; sharing a pair untested |
| Cubemaps | +0x5D indexes the zone's 0x72C1; reusing an index the zone already has needs nothing. | PROVEN (earlier doc) |
| Lightmap atlases (UFrags) | UFrag +0x4E indexes a zone 0x5400 pair shared by many UFrags (atlas); UV2 in the vertex. | known |
| Collision | collision.dat instances link to Ties by matrix only; nothing in a zone points at collision. A Tie with no collision instance is non-solid decoration (many retail Ties are). The undecoded grid at header +0x30 would be needed to ADD collision. | STRONG |
| Region lists | region.dat 0x1C300 = sorted Tie TUIDs of the region (= assetlookup 0x1D300 set), 0x1C100 = sorted shader TUIDs, 0x1C700 = sorted animset TUIDs, 0x1C200 cubemaps, 0x1C500 Shrubs, 0x1C600 Mobys (unsorted), 0x1CA00 cinematics. None depends on placements; a new Tie class would need them. | PROVEN (set equality, 3 levels) |
| Visibility | No per-placement visibility table in the zone besides the clusters. 0x6280 (UFrag clusters) is copied into the runtime pool, role unknown. | partial |
| LOD / draw distance | per Tie class / primitive (ties.dat) and UFrag record +0x60; nothing per placement found. | ASSUMED |
| Bounding volumes | cluster spheres (0x72D0 / 0x6240) must enclose members (retail always does). No zone-level bounding box found. | STRONG |
| metadata.dat | 0x25100 = layer names (`art_moon1a`...), no counts. | read |
| assetstats +0x00 | read by 0x32C7C0 and used as an allocation size (level resident heap?); its relation to file sizes was not found. | open |
| level_cached.toc / PSARC | file sizes change when zones.dat / lighting.dat grow; PsarcPatcher + GameToc handle that. | known |

## 5. What the game reads (EBOOT)

Zone section ids are not looked up one by one: two loops walk the blob's whole section table and switch on
the id (found by `ebscan.py`, compare immediates).

| Function | Role |
| --- | --- |
| 0x5D5308 | IGHW open: magic / version check, applies the fix-up table (header +0x10 / +0x14). |
| 0x5878F8 | Zone resident setup, two passes over the section table. Pass 1 (0x58887C): 0x7200, 0x71A0, 0x71C0, 0x72C1, 0x7400, 0x7500 - class / shader / cube tables, count from the section header, one runtime slot each. Pass 2 (0x588E64): 0x5200, 0x5400 (count kept), 0x5410, 0x5700 (count), 0x5C00, 0x5C01, 0x6000 / 0x6100 (turned into RSX main-memory offsets), 0x6200 (pointer + count), 0x6400, 0x6641 (count), 0x7900 (absent in ACiT), 0x8110. |
| 0x584310 | Zone instance registration. Section switch: 0x6200, 0x6240 (+count), 0x6280, 0x7240 (pointer only), 0x72C0, 0x72D0 (+count), 0x7440, 0x74C0, 0x74D0 (+count), 0x7540, 0x75D0 (+count), 0x7800 (+count), 0x7805 (+count), 0x7810, 0x7820 (+count), 0x8C00 (+count), 0x8C01. Then, per UFrag cluster: 0x60587C(UFrag manager 0xF5BE80, cluster, its 0x6280 entry); per Tie cluster: 0x5FA618(Tie manager 0xF5B680, cluster, 0x7240 base, 0x72C0 base); and it writes 0x7FFF into each member's runtime field (UFrag +0x4C, Tie +0x7C). |
| 0x5FA618 | Tie cluster register: takes a pool slot (bitmap allocator 0x960C34, capacity manager +0x58), copies the 0x20 cluster record, turns +0x10 into `placements + first x 0x80` and +0x14 into `names + first x 0x10`, adds the cluster count to the manager's placement total (+0x64). |
| 0x60587C | UFrag cluster register, same pattern; also copies the 16-byte 0x6280 entry. |
| 0x580028 | Sizes the managers from assetstats.dat 0x9800 (read by 0x32C7C0 through section lookup 0x5D5140). |

Consequences:
- The section header count of **0x7240 is never used**: the Tie placement table is walked cluster by
  cluster. A longer 0x7240 "just works" only if a 0x72D0 cluster covers the new rows.
- Every zone section in the inventory is consumed by the game except 0x5A00, 0x11300 (reached through
  pointers), 0x7815 / 0x8D00 / 0x8120 (reached through pointers). So "undecoded" sections are live data.
- Gaps not resolved: the cluster-pool capacity is set elsewhere than 0x5FA714 (which zeroes it); the
  0x6280 values' use after copying; 0x5F87E8's exact pool layout.

## 6. Verdicts and recipes

Context for the moons: in every space sector the moons are zones of Ties only - `art_moon1a`..`art_moon1y`
on space_sector_1 (21-64 placements each, 4-13 clusters, most placements baked: one 0x5400/0x5410 pair
each), plus a Shrub zone (`art_z_moon_shrubs`, 11,282 Shrubs). **No space-sector zone has a single
UFrag** (0x6200 absent, 0x6000 / 0x6100 empty). A user moon is therefore Ties (+ Shrubs + gameplay).

Ordering facts the writers must keep (PROVEN on all 10 levels): assetlookup 0x1DA00 / 0x1DB00 / 0x1D300
are sorted by TUID and their offsets ascend; zone blobs and sections are 0x80-aligned; fix-ups sorted.

### (a) Append a placement of a Tie class the zone already uses - SAFE WITH CONDITIONS

Proven mechanically (scratch copies only, never installed): `mkappend.py` + `verify.py` on
space_sector_1 `art_moon1b` (0x8B144C5505794C90, a middle zone, so later zones move) and on krell_canyon
zone 0xFAB0C7FF0619941E (lights, volumes, 0x6641 and UFrags: every pointer kind gets relocated). Both:
all other zones byte-identical at their new offsets, every original section payload kept, fix-ups sorted
and all landing in sections, all names resolve, clusters partition all placements, coverage 100%, no
problems, lighting.dat untouched and still fully accounted. NOT proven: that the game draws it (needs an
in-game test).

Order of work:
1. Pick the source placement: same class; prefer an UNBAKED one (+0x5A = 0xFFFF). Copy its 0x80 record.
   Set the matrix and the sphere (+0x40 centre = matrix-moved centre, radius scaled with the matrix).
   Keep +0x50 (class), +0x54 (prelight offset: share the source's block, retail does), +0x5A, +0x5C
   mirrored flag (recompute from the determinant), +0x5D cube index, +0x64..+0x7F. +0x7C is a runtime
   field (written 0x7FFF at load) - keep it 0.
2. 0x7240: append the record (index N = old count). Count field +1.
3. 0x72C0: append {unique TUID, char*, 0}; count +1. The char* needs a new fix-up entry.
4. 0x11300: append the name string (or point at an existing string; names repeat in retail).
5. 0x72D0: append a cluster {sphere = the placement's sphere, N, N, 0, 1 << 16} (count +1), OR grow the
   last cluster (count+1 at +0x1C, enlarge its sphere to enclose the new one) - no new pool slot then.
6. Re-lay the blob (sections in table order, 0x80 boundaries, PAD filler, fix-up table at the end);
   move every section offset, every fix-up offset and every fixed-up pointer value with its section; add
   the new fix-up; sort; header +0x10 = new fileLength, +0x14 = fix-up count.
7. zones.dat: put the blob back, shift later blobs to the next 0x80 boundary; assetlookup 0x1DA00: new
   length for this zone, new offsets for the later ones.
8. assetstats.dat 0x9800 +0x30 (Tie clusters): only if a NEW cluster pushes the level total past the
   next multiple of 16. Free slots (Tie clusters / lightmaps): space_sector_1 8 / 8, space_sector_2 5 / 2,
   space_sector_3 4 / 12, space_sector_4 10 / 2, space_sector_5 4 / 12. Growing the last cluster avoids
   this; a new lightmap pair would consume +0x14 the same way (lightmap sizing ASSUMED from the match).
9. Repack: PSARC entry (zlib like the original) and level_cached.toc size (PsarcPatcher / GameToc).
10. Collision: nothing. The new Tie is not solid (as many retail Ties).
If the source must be a BAKED placement: either share its lightmap pair (+0x5A unchanged: no retail
precedent, untested) or turn it into an unbaked one (+0x5A = 0xFFFF, +0x64/+0x66 = 0) - but then its
prelight block (sized for the baked case: only non-lightmapped primitives) is too short; point +0x54 at a
block of an unbaked placement of the same class, or 0xFFFFFFFF (shipped on 4..33 placements per level,
look unknown).

### (b) Append a placement of a Tie class used elsewhere in the level but not in this zone - SAFE WITH CONDITIONS (one more step than (a), untested)

As (a), plus:
1. 0x7200: append the class TUID (count +1); +0x50 = its index. The game sizes the per-class table from
   this count (0x5878F8 pass 1).
2. Cube palette: +0x5D must index this zone's 0x72C1; add the cube TUID there if missing.
3. Prelight: the block must be in THIS zone's lighting slice. Either copy a block of the class from the
   other zone's slice into this zone's slice (lighting.dat grows: re-lay lighting.dat, 0x1DB00 length /
   later offsets; every Tie +0x54, Shrub +0x2C and descriptor +0x00 is slice-relative, so nothing inside
   other slices moves) or use 0xFFFFFFFF. A zone with no slice (one of space_sector_1's ten) needs a new
   0x1DB00 entry (sorted by TUID) to get prelight.
4. Region lists: nothing - the class is already in region.dat 0x1C300 and its shaders in 0x1C100
   (ACiT levels have one region). assetstats +0x2C (Tie classes) is per level, unchanged.

### (c) Add a UFrag / enlarge one - NOT YET

- Enlarging an existing UFrag on a planet level (vertex / index data grows inside 0x6000 / 0x6100; the
  record's +0x40 / +0x44 offsets are section-relative, so a re-layout handles it; update +0x48 / +0x4A
  counts, +0x30 sphere, +0x70 anchor, the 0x6240 cluster sphere) is mechanically like (a). Blockers: the
  two-layer 4-byte stream, new lightmap UV2 islands with no baked texels (re-bake or 0xFFFF lightmap),
  0x6400 (index + two floats, read by the game, not decoded), collision (UFrag collision is a world
  instance in collision.dat whose grid is not decoded).
- A new UFrag in a space-sector zone means adding the whole UFrag family to a zone that has none:
  0x6200, 0x6240, 0x6280, 0x6400, 0x71A0, 0x71C0 (section table grows), plus assetstats +0x28 (UFrag
  clusters: 0 on space sectors, so 0 pool slots - must be raised). 0x6280's two u64 per cluster are copied
  into the runtime pool with no known meaning. Not needed for moons: retail moons have no UFrags.

### (d) Add a whole new zone - NOT YET (feasible, more files)

New blob (copy an existing Tie-only moon zone's structure) appended to zones.dat; assetlookup 0x1DA00
entry inserted in TUID order + 0x1DA01 region mask (assetlookup is an IGHW: its own re-layout, it has no
fix-ups); optional lighting slice + 0x1DB00 / 0x1DB01 entry; region.dat 0x1C010 TUID + 0x1C000 name
pointer + string (region.dat has 12 fix-ups: re-layout); metadata.dat layer name (probably editor-only);
assetstats: +0x30 Tie clusters, +0x14 lightmaps if baked, budgets for anything else added; whether the
game also limits the zone count per region, or needs the zone in gameplay data, was not checked.

### Risks, most likely first

1. **Cluster pool overflow** (new cluster without raising assetstats +0x30 past the rounded slack): the
   allocator fails and the register function writes the cluster into slot 0 - silent corruption of the
   first cluster. Mitigation: grow an existing cluster, or raise +0x30.
2. **Lighting of the copy**: a shared baked lightmap pair is untested; an unbaked copy with a too-short
   prelight block reads past its block (wrong colours, or another placement's words).
3. **Resident memory**: zones.dat and lighting.dat blobs grow; assetstats +0x00 is used as an allocation
   size and was not matched to file sizes. A few hundred bytes per placement is likely within slack; a
   whole moon may not be.
4. **Visibility outside a cluster sphere**: the cluster sphere must enclose the new placement or it is
   culled when the cluster is (retail always encloses).
5. **Non-solid Ties**: no collision instance, and adding one needs the collision grid (undecoded).
6. Duplicate placement TUIDs: keep them unique; gameplay may reference Tie placements by TUID (not checked).

### Could not determine

- Where the Tie / UFrag cluster pool capacity is finally written (0x5F87E8 was not traced; the sizing
  call from assetstats is proven).
- What 0x6280, 0x6400, 0x5A00, 0x7820, 0x8110/0x8120 and Tie record +0x58 / +0x5E / +0x60 / +0x6C..+0x78
  mean (all consumed by the game, all copied unchanged by the recipe).
- Shrub prelight block size (needs shrubs.dat vertex counts).
- assetstats +0x00, +0x18 (256 everywhere), +0x50, +0x54.
- In-game behaviour of anything above.

## 7. Reproduce

```
cd dev/tools/zonecoverage
python3 extract.py                       # once
python3 coverage.py space_sector_1       # also front_end axiom_city krell_canyon
python3 mkappend.py space_sector_1 8b144c5505794c90
python3 verify.py   space_sector_1 8b144c5505794c90   # prints ALL OK
python3 ebscan.py                        # immediate / compare sites of zone section ids
```
