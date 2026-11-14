# A Crack in Time: gp_prius.dat coverage, and what adding an instance takes

NPUA80966 (01.20), audited 2026-11-14 on all 22 shipped levels (space_sector_1 from its shipped
`level_cached.psarc.2026-11-12_01-20-52.bak`). Question: **is it safe to append a Moby instance to gp_prius.dat,
and exactly what must be written to do it?**

Tools (gitignored, `dev/tools/gpcoverage/`; run python without `-I`):

| Script | What |
| --- | --- |
| `coverage.py <dat>...` (`-q`, `--check`) | labels every byte; checks the layout invariants below |
| `append_moby.py --roundtrip <dat>...` | rebuilds a file from its parts; must come out byte-identical |
| `append_moby.py <dat> <out> --like <name> [--name] [--move] [--gameplay <in> <out>]` | appends one Moby (copies only) |
| `verify_append.py <orig> <new>` | decodes every instance of both files and diffs them |
| `gameplay_check.py <level dir>...` | gameplay.dat 0x25000 against its gp_prius / gp_persist |
| `secids.py`, `gpstate.py`, `callers.py`, `poolw.py` | EBOOT scans (section-id immediates, state-block users, callers) |

Extracted files: `dev/tools/gpcoverage/work/all/<level>/` (gameplay.dat, gp_prius, gp_persist, metadata, region),
`work/<level>/` (also instances.lua(.trimmed), scripts) for ss1, ss2, front_end, axiom_city, krell_canyon,
zolar_forest. Proof outputs: `work/proof/`.

"Proven" below means checked on all 22 levels (or traced in the EBOOT). "Assumed" is marked as such.

## Verdict

**Safe with conditions.** The file is understood well enough to grow:

- 100% of the bytes of all 22 files are accounted for (0 unaccounted bytes).
- The set of pointer fields equals the fix-up table, exactly, on all 22 levels.
- A rebuild from parsed parts is **byte-identical** on all 22 levels. So the layout rules are complete.
- A Moby appended at the end of its layer was built on space_sector_1 (two variants). Every existing instance of
  every kind is unchanged, including its handle. Coverage, invariants, gp.py and gameplay_check all pass.

The conditions:

1. Append only at the **end of an existing layer** that already has Mobys. Inserting anywhere else renumbers the
   handles of the later Mobys in that layer. Those handles are stored in Pod lists, settings fields,
   instances.lua and scripts.
2. Use a Moby **class already in the region**, meaning its index is in region.dat 0x1C600 and its asset is in
   mobys.dat.
3. Update **gameplay.dat 0x25000** (+0x14 gp_prius size, +0x34 Moby count).

Not proven: that the game runs with the result. Nothing was installed or tested in game.

## 1. Byte coverage

| Level | Size | Fix-ups | Records | Settings blocks | Strings | Path points | Lists | Pad | Unaccounted |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| space_sector_1 | 1,545,140 | 3,852 | 179,612 | 1,186,412 | 42,501 | 102,336 | 4,656 | 13,685 | **0** |
| front_end | 9,524 | 12 | 540 | 7,996 | 52 | 0 | 0 | 678 | **0** |
| axiom_city | 2,931,708 | 7,646 | 339,980 | 1,835,084 | 76,345 | 616,128 | 12,396 | 20,633 | **0** |
| krell_canyon | 2,890,180 | 9,360 | 430,412 | 1,791,136 | 96,441 | 489,648 | 17,664 | 26,879 | **0** |

Every other level is also at 100.0000% (`coverage.py -q`). The padding is of three kinds:

- the `0xDEADDEAD` fill of header +0x18,
- the linker's ASCII alignment words `PAD0`..`PADF` (repeating; the last word is cut off at the boundary) between
  sections and inside settings blocks,
- zero tails of settings blocks.

The first pass reported "unaccounted" ranges. All of them were `PADn` filler: between sections, after the
odd-length 0x11300 and 0x25014, and between the inner sections of settings blocks.

Byte coverage is not field understanding. Fields whose *meaning* is still unknown (they are copied, not
interpreted):

- **Moby record (0x50):**
  - +0x30 four flag bytes (`01 01 0x 0x`; +0x33 is not "has settings");
  - +0x34 flags;
  - +0x38 / +0x3C always `FFFFFFFF`;
  - +0x46 u16 (nonzero 14 times);
  - +0x48 float (1.0 on 94%);
  - +0x4C always 0.

  +0x02 equals the metadata layer on all 23,385 Mobys (proven). +0x04 / +0x08 are the display / update distances.
- **Metadata (0x10):** +0x0E is always 0.
- **Settings struct contents:** class fields, documented separately (acit_classes.md).

## 2. Section inventory (all 22 levels)

"C#" is what ReLunacy reads (ReLunacy.Engine). "Per inst." means the section must grow when an instance of that
kind is added.

| Id | Elem | Count range | Levels | Content | C# | Per inst. |
| --- | --- | --- | --- | --- | --- | --- |
| 0x25005 | blob 0x10 | 1 | 22 | root: {ptr 0x25008, layer count, ptr 0x25010, region count} | **not read** | new layer |
| 0x25006 | blob 4 | 1 | 22 | root: ptr 0x2500C | **not read** | no |
| 0x25008 | 0x80 | 1..24 | 22 | layer: name[0x40], 8 x (first record ptr, count): Moby, path, volume, Clue, controller, Area, Pod, Scent | partly: name only (GameplayGroupReader.cs:527-535) | count +1 |
| 0x2500C | 0x20 | 1..24 | 22 | layer: 8 x first-metadata ptr, same order | **not read** | new layer / first of kind |
| 0x25010 | 0x48 | 1 | 22 | region: name[0x40] ("default"), ptr 0x25014, u32 layer count | **not read** | new layer |
| 0x25014 | blob 2n | 1 | 22 | region's layer indices (u16) | **not read** | new layer |
| 0x11300 | blob | 1 | 22 | instance name strings, one per instance, never shared | via metadata +8 (GameplayGroupReader.cs:548, RegionReader.cs:242) | yes (+name) |
| 0x25048 | 0x50 | 3..2224 | 22 | Moby records | RegionReader.cs:214-286 (+0x00..+0x2C); +0x30..+0x4C not | yes |
| 0x25049 | 0x40 | 18..1518 | 13 planets | 4x4 matrices, Moby +0x40 ptr, +0x44 u16 count | **not read** | if the Moby has some |
| 0x2504C | 0x10 | 3..2224 | 22 | Moby metadata (TUID, name ptr, layer, 0) | RegionReader.cs:228-246 | yes |
| 0x25050 / 0x25054 | 0x10 / 0x10 | 35..608 | 21 | paths + metadata | GameplayGroupReader.cs:344-389 | yes |
| 0x25058 | blob | 1 | 21 | path points (0x10 each) | GameplayGroupReader.cs:363-370 | yes |
| 0x2505C / 0x25060 | 0x40 / 0x10 | 265..1533 | 21 | volumes + metadata | RegionReader.cs:314-358 | yes |
| 0x25064 / 0x25068 | 0x10 / 0x10 | 5..313 | 21 | Clues + metadata | GameplayGroupReader.cs:98-119 | yes |
| 0x2506C / 0x25070 | 0x30 / 0x10 | 112..1059 | 21 | controllers + metadata | GameplayGroupReader.cs:279-337 | yes |
| 0x25074 / 0x25078 | 0x10 / 0x10 | 15..260 | 21 | Pods + metadata | GameplayGroupReader.cs:392-430, PodWriter | yes |
| 0x2507C | blob | 1 | 21 | Pod member lists | through Pod +0 (GameplayGroupReader.cs:405-408) | if listed |
| 0x25080 / 0x25084 | 0x10 / 0x10 | 18..238 | 21 | Areas + metadata | GameplayGroupReader.cs:122-134 (volume list only) | yes |
| 0x25088 | blob | 1 | 21 | Area lists: +0 paths (u16 count +8), +4 volumes (u16 count +0xA) | volumes only; **path list not read** | if listed |
| 0x2508C / 0x25090 | 0x10 / 0x10 | 1..61 | 19 | Scents + metadata | GameplayGroupReader.cs:137-148 | yes |
| 0x25094 | blob | 1 | 19 | Scent Clue lists | via Scent +0 | if listed |
| 0x25020 | blob | 1 | 22 | **private** settings blocks, one per instance, never shared, back to back | via record pointers (ReadSettings, GameplayGroupReader.cs:216) | yes, for private settings |
| 0x25030 | blob | 1 | 22 | **class-default** settings blocks, exactly one per (kind, class), shared (one ss1 block has 256 users) | same | no (shared) |

Correction to the brief: 0x25020 is not "Clue property blobs". It holds the private settings of Mobys,
controllers and Clues alike. Clue blocks are flat; Moby and controller blocks are inner IGHW 0.2.

The const `PropertySection` (GameplayGroupReader.cs:40) is declared but never used. The A4O/ITN-only sections
0x25022 and 0x250A0/A4/A8 never occur in ACiT.

**Read by nobody in ReLunacy:** 0x25005, 0x25006, 0x2500C, 0x25010, 0x25014, 0x25049 and the path half of 0x25088.
The Moby record's +0x30..+0x4C are not read either. All of these are now decoded structurally.

**Settings block layout** (Moby and controller; flat for Clues): an inner IGHW of version 0.2.

- Header: magic, `00000002`, section count, 0. Then 0x10-byte entries: (0x2501C, off, len, 0) for the class
  struct, plus `0xFFFFFFFF` entries for extra data.
- `PADn` alignment words.
- The block's own fix-up table: u32 count, then u32 offsets **relative to the block**. All 793 of these point
  into the block's own extra data.
- Zero padding to 16.

So a settings block is self-contained and can be copied anywhere unchanged. 185 Mobys have no settings (offset 0,
size 0).

## 3. Fix-up audit

- Header: +0x0C header end (0x20 + 16 x sections), +0x10 data end, +0x14 fix-up count. The table follows the
  data directly; file length = data end + 4 x count (all 22).
- The EBOOT's IGHW loader 0x5D5308 requires version `0x00010001`. It walks the table: the top 2 bits of each entry
  are the type (0 = 32-bit add base, 0x40000000 = 64-bit), the low 30 bits are the offset. Every ACiT entry is
  type 0. The pointers are relocated **in place by adding the load base**, so each location must appear exactly once.
- **Pointer fields == fix-up locations, exactly, on all 22 levels.** No unlisted pointer, no unexplained fix-up.
  Sorted, unique. Every target lies inside the data and inside an accounted range.

| Location | Target |
| --- | --- |
| 0x25005 +0 / +8 | 0x25008 / 0x25010 |
| 0x25006 +0 | 0x2500C |
| 0x25010 +0x40 | 0x25014 |
| 0x25008 +0x40..+0x78 (non-empty kinds only) | first record of the layer in that kind's record table |
| 0x2500C +0..+0x1C (non-empty only) | first metadata of the layer |
| every metadata +0x08 | 0x11300 string |
| Moby +0x0C | 0x25020 or 0x25030; +0x40 0x25049 (when count > 0) |
| Clue +0x04, controller +0x00 | 0x25020 or 0x25030 |
| path +0x00 | 0x25058 |
| Pod +0x00 | 0x2507C |
| Area +0x00 / +0x04 | 0x25088 |
| Scent +0x00 | 0x25094 |

ss1 example: 1,201 Moby settings (742 to 0x25020 + 459 to 0x25030), 1,201 Moby names, 110 path points, 36 Pod lists.

- **Null pointers are not fix-ups.** A layer with no instance of a kind has (0, 0), and an Area with no paths has
  ptr 0. Adding the first instance of a kind to a layer therefore needs two new fix-ups (layer +0x40+8k and
  0x2500C +4k).
- The only pointers relocated another way are the inner settings-block fix-ups. They are block-relative and
  listed in each block's own tail.
- At load the game also **rewrites some file fields in place**. 0x61B0E4 replaces Moby +0x00 with the loaded-class
  index, and writes Moby metadata +0x08. Irrelevant for writing the file.

## 4. Ordering and layout invariants (all proven on 22 levels unless marked)

- **File layout.** Sections in section-table order. Each section starts at the next 0x80 boundary (the first one
  after the section table). Gaps are `PADn` filler. Data end = end of the last section, with no tail padding.
  The rebuild that follows this rule is byte-identical on all 22 levels.
- **Records grouped by layer.** For every kind, records and metadata are contiguous per layer, layers ascending.
  0x25008's (ptr, count) and 0x2500C's ptr match the first instance and the count of every layer and kind.
- **Records and metadata are parallel.** Same count, same order (record i belongs to metadata i).
- **0x2500C** holds, per layer, 8 pointers to the first *metadata* entry of each kind (0 when none). Order:
  Moby 0x2504C, path 0x25054, volume 0x25060, Clue 0x25068, controller 0x25070, Area 0x25084, Pod 0x25078,
  Scent 0x25090. 0x25008 uses the same order. The **reference kind numbers use another order**: 0 Moby, 1 path,
  2 volume, 3 Clue, 4 controller, 5 Scent, 6 Area, 7 Pod.
- **TUIDs.** Not sorted anywhere. Moby TUIDs are unique per level (random 64-bit). Volume TUIDs are NOT unique:
  the volumes generated for Clues carry TUID 0 or 1 (8 to 251 duplicates per level). Pod/Area/Scent share TUIDs
  across kinds (e.g. SEGMENT_SS1_JUNKTRADER is both Pod and Scent).
- **Names.** Not unique: 3,490 duplicated Moby names game-wide. One string per instance, never shared.
- **Order within a layer.** Not sorted by TUID, name or class. It is the editor's order.
- **No checksum, hash, sorted lookup or count field inside gp_prius** besides the section table counts, 0x25008
  counts, 0x25005 / 0x25010 counts and the header.
- **Counts outside the file:** gameplay.dat 0x25000 (section 6). Their **use** by the game was not located
  (assumed: allocation budgets).
- **Settings sharing.** Only 0x25030 blocks are shared (one per class). 0x25020 blocks are never shared, even when
  their bytes are identical (e.g. ss1: 727 duplicate contents).
- **Layer limits** (EBOOT). The runtime layer pool has 64 slots (bitmap at state 0xF5C6C0 +0x44). The loader's
  layer-enable mask is 64-bit. The handle index is 12 bits, so at most 4,096 instances of one kind per layer.
  The handle layer field is 10 bits.

## 5. What the game reads (EBOOT)

Section ids built in code (`lis r,2` + `ori/addi 0x50xx`), all occurrences, via `secids.py`:

| Id | Where | Role |
| --- | --- | --- |
| 0x25005 | 0x32A7F0 (+0x32A854), 0x3314BC, 0x3321E4 | gp_prius root tree. 0x61331C stores it in the gameplay state (0xF5C398 +8) and maps each gp_prius layer **by name** to gameplay.dat's layer list (u16 map at 0xF5C3D4; 0xFFFF if absent) |
| 0x25006 | same three loaders | metadata tree. 0x612F18 stores it at state +0x10 |
| 0x11300 | same | string table bounds, state +0x14 / +0x18 (0x612F30) |
| 0x25000 | 0x32C07C (gameplay.dat) | stored at state +4 (0x612EE8). Readers found: +0 / +4 layer names (0x612F7C, 0x61331C), +0x40 / +0x44 region names (0x61325C). +0x18..+0x3C **no reader found** |
| 0x25007 | 0x32F770, 0x332634 | gp_persist root (state +0). 0x612CE0 = class -> default-settings lookup |
| 0x2501C | 0x524368 (Moby settings), 0x61291C | settings struct, found inside each block's inner IGHW |
| 0x25018 / 0x25098 | 0x1E7B30 / 0x217E94 | other files (not gp_prius) |

Every other gp_prius section (0x25048 ... 0x25094, 0x25020 / 0x25030, 0x25049) is **never looked up by id**.
The game reaches them only through 0x25005 -> 0x25008 layer records -> (ptr, count), and through the pointers in
the records. So the section table entries of those sections matter to tools (ReLunacy, gp.py), not to the game.
The pointers and counts are what the game consumes.

Load path (0x3314BC; 0x32A7F0 is the simpler variant):

1. Open the IGHW with fix-ups (0x5D5308).
2. Find 0x25005 / 0x25006 / 0x11300 (0x5D5140).
3. Map layers by name (0x61331C).
4. For each layer whose gameplay.dat index has its bit set in the 64-bit mask at loader +0x778: **0x61B0E4**
   (layer record, metadata record) creates the layer:
   - **Mobys:** for i < layer +0x44 it reads record +0x00 and looks up region class table entry (region.dat
     0x1C600 TUID). A linear search 0x669180 finds it in the loaded class table, and the index is written back
     to +0x00. Then the Moby create callback 0x5247A0 -> 0x5264C0 (manager vtable +0x14 spawn; position from
     +0x14...) runs, and the handle goes into the runtime layer list.
   - Then paths (0x615924), volumes (0x61ACEC), Clues, ... in turn.
   - Per-layer handle lists are sized from the counts (0x61AFA8).
5. 0x61BAD0 and 0x61C1D0 (start) for each layer.

The callbacks are registered at 0x32A7C0 -> 0x61AE64:

| Slot | Callback |
| --- | --- |
| +0 | 0x5247A0 |
| +4 | 0x524748 |
| +8 | 0x4F13FC |
| +0xC | 0x4EB41C |
| +0x10 | 0x4EB290 |
| +0x14 | 0x4EB2AC |
| +0x18 | 0x4EB2C8 |
| +0x1C | 0x2186C8 |
| +0x20 | 0x2186C0 |

Consequence: the instance (layer, index) handle is the position in the runtime layer list, which is file order
within the layer. This matches instances.lua `handle:new(layer, index)`.

## 6. Cross-file dependencies for a Moby

Effect of appending one Moby at the end of an existing layer, with an existing class:

| File | Ties to instances | Existing layer | New layer |
| --- | --- | --- | --- |
| **gameplay.dat 0x25000** | +0x14 gp_prius size; +0x34 Moby count (= 0x25048 count on 22/22). Also volumes +0x18, paths +0x1C, path points +0x20 (each path rounded up to even), Areas +0x24, Area list budget +0x28 (16 x sum ceil((paths+volumes)/16)), controllers +0x2C, controller settings bytes +0x30, Pods +0x38, Pod entries +0x3C. All verified on 22/22 | **yes: +0x14, +0x34** | also: layer name string + pointer (new 0.2 chunk + fix-up), +0x04 count, +0x0C mask, region mask (+0x48 record +4) |
| region.dat 0x1C600 | Moby +0x00 indexes it (class TUID). gp_persist 0x2502E has the same count (180 on ss1): per class | no (existing class) | no |
| gp_persist.dat | class-indexed default settings (0x612CE0); size in gameplay +0x10 | no | no |
| metadata.dat | layer list (0x25098 count, 0x25100 names, 0x25110 offsets, 0x25104 words) | no | yes (assumed; equals the gp_prius layer names on all levels checked) |
| instances.lua / .lc (+ .trimmed) | generated `moby_handle:new(layer, index) -- TUID`; .trimmed lists only the script-used names (ss1: 43 of 1,201 Mobys) | no; add a line only if a script must name it (the .lc is rebuilt from the text) | yes if scripts use it |
| level scripts | refer to instances by these names | no | no |
| level.csv (global_cached) | per-segment counts = Pods `SEGMENT_<LVL>_<X>` (NumZoni / NumGoldBolts / NumParts, user-checked) | only if the Moby joins a segment Pod as a counted collectible | same |
| Pod / Area / Scent lists, settings references | (kind, layer, index) words | no: existing indices do not change | no |

## 7. Recipe

### Append one Moby to an existing layer L (proven mechanically)

The layer must already have at least one Moby; the class must be in region.dat 0x1C600.

1. **Template.** Copy an existing Moby's 0x50 record. Set position +0x14 (+ rotation +0x20, scale +0x2C).
   Keep +0x00 class index and +0x02 = L.
2. **Settings.**
   - If the template's +0x0C points into **0x25030** (class default): keep it, shared.
   - If it points into **0x25020**: append a copy of the block (offset +0x0C, size +0x10, copied as is: it is
     self-relocating) at the end of 0x25020 (8-aligned), and point the record at it.
   - If size is 0: keep 0 / 0.
   - Never share a 0x25020 block; edits through GameplayWriter would change both instances.
3. **Name.** Append `name\0` to 0x11300.
4. **Metadata.** New 0x10 entry: fresh random TUID (unique in the level), +8 the new string, +0xC L, +0xE 0.
5. **Insert.** Put the record into 0x25048 and the metadata into 0x2504C at file index first(L) + count(L), the
   end of L's run. **The new handle is (0 << 26 | L << 16 | 0xF000 | count(L)).**
6. **Layer count.** 0x25008[L] +0x44 += 1. Its pointer is unchanged.
7. **Re-layout everything after the insert points.** Every section at the next 0x80 boundary, `PADn` gaps, section
   table offsets / counts (0x25048, 0x2504C count + 1, keeping the 0x10000000 flag) / lengths (0x11300, 0x25020).
   Translate **every** fix-up location and every pointer value through the moves. This covers the later layers'
   0x25008 / 0x2500C pointers, all record pointers, 0x25005 / 0x25006 and all names.
8. **Fix-ups.** Add the new record's +0x0C (if settings) and the new metadata +0x08 (and +0x40 if it has 0x25049
   matrices). Sort. Header +0x0C, +0x10 data end, +0x14 count.
9. **gameplay.dat 0x25000:** +0x14 = new gp_prius length; +0x34 += 1. Write the archive with PsarcPatcher; RPCS3
   FIOS cache caveat as usual.

Proof (`work/proof/`, space_sector_1):

| Test | Result |
| --- | --- |
| `moon1a_hill6` -> `moon1a_hill6_relunacy` (layer 9 `gameplay_moon1a`, private 0x25020 block, +25 x) | file +264 B, handle 0x9F0ED (layer 9, index 237) |
| `ss1_mission_tow_npc` copy (layer 15, last layer, shared 0x25030 block, +5 y) | file +136 B |
| `verify_append.py` | 2,126 -> 2,127 decoded entries; only the new Moby is added, only layer L's Moby count changes; every other instance's record, resolved pointers, layer and **handle** identical |
| coverage | 100%, 0 unaccounted; invariants pass (only the two shipped TUID observations) |
| `gameplay_check.py` | modified directory OK |
| gp.py | 1,202 Mobys; the new one resolves by handle; settings bytes equal the template's; all Clue references unchanged |
| `--roundtrip` | 22/22 identical |

### Append to a brand-new layer (not built; recipe from the decoded structure)

**gp_prius:**

- Insert a 0x25008 record (name, 8 x (0, 0)) and a 0x2500C record (8 x 0). Put it **last**, so the
  layer-ascending order holds.
- 0x25005 +4 += 1.
- 0x25014: append the u16 index. 0x25010 +0x44 += 1.
- For each kind it gets: set (ptr, count) and the 0x2500C ptr, and add both fix-ups (they were null). Its records
  go at the end of each table.

**gameplay.dat** (the game maps layers by name and enables them by its own mask):

- name string + pointer-table entry (both 0.2 chunks with fix-ups),
- +0x04 += 1,
- +0x0C mask bit,
- region record (+0x48 -> +4) mask bit.

**Other files:**

- metadata.dat: the layer list.
- instances.lua if scripts are to use it.

Limits: 64 runtime layers, 10-bit layer field, 32-bit mask words in gameplay.dat (krell_canyon uses 24 bits).
**Recommendation: avoid. Moons can live in an existing layer** (ss1 scripts never mention layers).

### Other kinds a moon needs (same mechanism; not built)

The re-layout engine is generic. Per kind, append record + metadata + name at the end of layer L, bump 0x25008[L]
(that kind's pair), add the pointer fix-ups, and update gameplay.dat:

| Kind | Record | gameplay.dat | Notes |
| --- | --- | --- | --- |
| Volume | 0x2505C 0x40 matrix, last word = shape << 24 | +0x18 += 1 | |
| Clue | 0x25064: +0 reference to its volume (the volume must exist), +4 / +8 settings (flat block; 0x25020 copy or 0x25030 default), +0xC class | none found | **Clue and Scent counts are not in gameplay.dat** |
| Controller / camera | 0x2506C: settings ptr / size, position, rotation, scale | +0x2C += 1, +0x30 += settings size | |
| Path | 0x25050 + points appended to 0x25058 | +0x1C += 1, +0x20 += count rounded up to even | |
| Pod | 0x25074 + list in 0x2507C | +0x38, +0x3C | |
| Area | 0x25080 + lists in 0x25088 | +0x24, +0x28 (recompute the 16-rounded budget) | |
| Scent | 0x2508C + list in 0x25094 | none found | |

A SpaceMoon Clue's settings name its volumes, beam Moby and Pod by (kind, layer, index). Create those first and
write the references from their new handles.

## Risks, most likely to break the game first

1. **Runtime pool capacity.** The Moby object pool (runtime array 0xF5CE00, 0x180 per object, bound at +0x18) and
   the manager spawn (vtable +0x14) were not traced to their capacity. If a pool is sized from gameplay.dat
   (+0x34 etc.), the gameplay.dat update covers it. If it is fixed, shipped levels go up to 2,224 Mobys
   (galacton_ship) against 1,201 on ss1. Unknown, not measured.
2. **gameplay.dat counts not updated.** They match on 22/22 levels and almost certainly size allocations, but no
   reader was found (+0x18..+0x3C). Always update them; a stale count could overflow a buffer silently.
3. **Settings content.** A copied block keeps the template's per-instance ids and references. For example
   SpaceMoon +0x80 / +0xD4 ids, or an enemy's home Area / volumes. Two Mobys may then claim the same thing. Pick
   templates without references, or rewrite them.
4. **Persistent / save state by TUID** (assumed, unverified). A new TUID must not collide. A random 64-bit TUID
   is used and checked against the whole file.
5. **Insert position.** Anything but the end of a layer renumbers handles: Pod lists, settings references,
   instances.lua, scripts.
6. **Moby record +0x30..+0x4C semantics unknown.** Copying from a same-class template is safe; zeroing them is not.
7. **instances.lc vs .lc.trimmed.** Which one the game loads was not determined. Irrelevant unless the new Moby
   needs a Lua name.
8. **ReLunacy's GameplayWriter.** It patches in place and finds records by TUID / order. It must reload after a
   grown file; it would also write through shared 0x25030 blocks.

## Not determined

- Where (if anywhere) the game reads gameplay.dat 0x25000 +0x18..+0x3C.
- The Moby pool's capacity.
- The meaning of Moby +0x30..+0x4C, 0x25049's role (matrices per Moby: attachment / joint overrides?), and
  metadata.dat's 0x25104 words.
- Which instances chunk the game loads.
- In-game behaviour of a grown gp_prius. Next step: install `work/proof/gp_prius_hill6.dat` +
  `gameplay_hill6.dat` into a COPY of the ss1 archive (with backup) and look for `moon1a_hill6` twice on moon1a.
