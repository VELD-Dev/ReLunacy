# front_end: a minimal new-engine level, section by section

A Crack in Time (NPUA80966, 01.20), `packed/levels/front_end`. The main menu: no Ties, no shrubs, no
cinematics, an empty sky and lighting file, three Mobys. Every file a level needs is still there, so it is
the smallest example of a complete new-engine level. Each section below is compared with
`space_sector_1` (17 layers, 10 zones, 1,201 Moby instances), so a count that grows with content shows as
such.

Measured 2026-11-13 from the shipped archives. "ReLunacy" says where the editor's code reads a section;
**unknown** means nothing reads it yet. Counts are `count x element size` for array sections (IGHW entry
flag `0x10000000`) and the byte size for blocks. The level config that is not in the level folder at all -
name, rewards, segments, pools, the space sectors' `BoundsRadius` - is a block of the game's `level.csv`
(see *Outside the level folder*).

## Archives

| Archive | Files | What |
| --- | --- | --- |
| `level_cached.psarc` | 49 | everything below, read when the level loads |
| `level_uncached.psarc` | 9 | `highmips.dat` (texture mip levels streamed in), `streaming_dialogue.<lang>.dat`, `streaming_sound.dat`, and its own `.toc` |

Both carry a `.toc` (`/packed/levels/front_end/level_*.toc`): the archive's file index, the format of
`docs/reverse/tod_toc_format.md`'s new-engine variant. `level_cached.toc` names scripts, so a new script
can be added to a level archive.

Files `space_sector_1` has that front_end has none of: `ties.dat`, `shrubs.dat`, `cinematics.dat`.

## Gameplay (`default/` is the region)

### `gameplay.dat` (304 B) - an old-style IGHW (version 0.2)

The only level file still in the 0.2 container: a 16-byte header, then entries of which the first is a
section and the rest (`0xFFFFFFFF` id) are pointer fix-ups - offsets the loader relocates.

| Section | Size | ReLunacy | Notes |
| --- | --- | --- | --- |
| `0x25000` | `0x50` | `RegionReader` (regions list) | +0x00 pointer to a table of layer-name pointers, +0x04 layer count (front_end 1 `default`, ss1 `0x11`, the same 17 names as `metadata.dat`), then words that differ per level (ss1 `0x0001FFFF`, `0x2C344`, `0x1793B4`, `0x20C`, `0x6E`, `0x193A`, `0x1B`, `0x1E0`, `0xD5`, `0x14CE0`, `0x4B1`, `0x24`, `0x402`; front_end mostly 0, `0xAFD8`, `0x2534`). +0x0C is a mask of the layers (one bit each: ss1 `0x1FFFF` for 17, krell_canyon `0xFFFFFF` for 24); +0x10 and +0x14 are the region's `gp_persist.dat` and `gp_prius.dat` sizes (updated by ReLunacy when a Pod list grows gp_prius), in both levels (front_end 45,016 and 9,524; ss1 `0x2C344` and `0x1793B4`). **The rest unknown** - likely more sizes and counts the loader allocates for. |

### `default/metadata.dat` (528 B) - the layer list

| Section | front_end | ss1 | ReLunacy | Notes |
| --- | --- | --- | --- | --- |
| `0x25098` | `0x10` | same | **unknown** (layout read 2026-11-13) | {u32 0, u32 layer count, ptr to `0x25110`, ptr to `0x25104`} |
| `0x25100` | `0x8` | `0xF6` | | the layer names, NUL-separated (`default`; ss1 `ambient_asteroids` ... `sound`) |
| `0x25110` | 1 x 4 | 17 x 4 | | offset of each layer's name in `0x25100` |
| `0x25104` | 1 x 4 | 17 x 4 | | a word per layer, 0 everywhere seen |

The gameplay groups' layer names (the hierarchy's layer folders) come from here.

### `default/gp_prius.dat` (9.5 KB) - placed gameplay instances

| Section | front_end | ss1 | ReLunacy | Notes |
| --- | --- | --- | --- | --- |
| `0x25048` | 3 x `0x50` | 1,201 x `0x50` | `GameplayGroupReader`, saved | Moby instances: +0x0C settings offset, +0x10 size, +0x14 position, +0x20 Euler ZYX, +0x2C scale |
| `0x2504C` | 3 x `0x10` | 1,201 x `0x10` | read | instance metadata: TUID and name |
| `0x25008` | 1 x `0x80` | 17 x `0x80` | read | the layers: a name at +0x00, indexed by an instance's metadata +0x0C |
| `0x2500C` | 1 x `0x20` | 17 x `0x20` | **partly** | one per layer |
| `0x25010` | `0x48` | | **unknown** | |
| `0x25014` | `0x2` | | **unknown** | |
| `0x25020` | empty | | read | the settings pool (inner IGHW blocks, A4O and ACiT Moby settings) |
| `0x25030` | `0x1F60` | | read | settings data |
| `0x25005`, `0x25006` | `0x10`, `0x4` | | **unknown** | |
| `0x11300` | `0x34` | | | a name string table; the same id is in `zones.dat`, `animsets.dat`, `shaders.dat` |

Volumes (`0x2505C`), Clues (`0x25064`), controllers (`0x2506C`) and paths (`0x25050`) are absent: front_end
places none.

### `default/gp_persist.dat` (44 KB) - **not read by ReLunacy**

| Section | front_end | ss1 | Notes |
| --- | --- | --- | --- |
| `0x25038` | `0x4D0` | | |
| `0x2502E` | 25 x 4 | 180 x 4 | the same counts as `region.dat`'s `0x1C600` (25, 180): one per persistent instance? |
| `0x25030` | `0x8318` | | the same id as `gp_prius.dat`'s settings data |
| `0x25024` | `0x22FC` | | |
| `0x2502C`, `0x25028` | `0xA4` each | | same size: a pair of tables |
| `0x25007` | `0x20` | | |

Persistent gameplay data (state that survives the region unloading); the next file to decode.

### `default/instances.lua` / `.lc` (+ `.trimmed`)

The generated handle declarations (`x = moby_handle:new( a, b )`), source and Lua 5.1 chunk.

### `default/collision.dat` (1.3 KB)

`0x2900`, one block (`0x480`): read and saved (`docs/reverse/collision.md`).

### `default/region.dat` (5.9 KB)

| Section | front_end | ss1 | ReLunacy | Notes |
| --- | --- | --- | --- | --- |
| `0x1C000` | 1 x 4 | 10 x 4 | read | zone ids |
| `0x1C010` | 1 x 8 | 10 x 8 | read | zone names |
| `0x1C100` | 201 x 8 | 693 x 8 | **unknown** | grows with content |
| `0x1C200` | 1 x 8 | 5 x 8 | read (`CubemapReader`) | the region's cubemaps |
| `0x1C600` | 25 x 8 | 180 x 8 | read (`RegionReader`) | 25 / 180, as `gp_persist.dat`'s `0x2502E` |
| `0x1C700` | 11 x 8 | 69 x 8 | **unknown** | 11, as `0x081E0` |
| `0x081E0` | 11 x 4 | | read (`SkyReader`) | sky |
| `0x08100`, `0x08110`, `0x08120` | `0x100`, `0x30`, `0x800` | | read (`LightingEnvironmentReader`) | the region's lighting environment (`newengine-region-environment`: per region, not per level) |
| `0x08B00` | `0xA0` | | read | lights |
| `0x11400` | `0x20` | | **unknown** | |
| `0x11000` | `0x10` | | **unknown** | |
| `0x1CF00` | `0x8` | | **unknown** | |
| `0x19600`, `0x19700`, `0x19A00`, `0x1A600`, `0x1A700`, `0x1AA00` | empty | | **unknown** | always present, empty here |

### `default/sky.dat`, `lighting.dat` - 0 bytes

Empty files stand in for a level without a sky or baked lighting: the loader accepts them.

## Zones: `zones.dat` (5.6 KB)

Several IGHW blocks back to back, one per zone, each at its offset in `assetlookup.dat`'s `0x1DA00`;
front_end has one. (Comparisons with ss1 read only its first block, so they are left out here.)

| Section | front_end | ReLunacy | Notes |
| --- | --- | --- | --- |
| `0x06000` | `0x360` | read | UFrag vertices |
| `0x06100` | `0x12C` | read | UFrag indices |
| `0x06200` | 1 x `0x80` | read, saved | UFrag records (anchor x256 at +0x70, sphere at +0x30) |
| `0x06240`, `0x06280`, `0x06400` | 1 x `0x20`, `0x10`, `0x10` | partly | per-UFrag tables |
| `0x071A0`, `0x071C0` | 1 x 8 each | read (`ZoneReader`) | |
| `0x07800` | 2 x `0x80` | read, saved | Tie placements (matrix, sphere at +0x40, mirrored byte at +0x5C) |
| `0x07810`, `0x07820` | 2 x `0x10`, 2 x `0x20` | **unknown** | per Tie placement |
| `0x07815` | empty | **unknown** | |
| `0x08C00`, `0x08C01` | 1 x `0x100`, 1 x `0x10` | read | zone lighting |
| `0x08D00` | empty | read | |
| `0x08110`, `0x08120` | | read | lighting, as in `region.dat` |
| `0x11300` | `0x26` | | names |

Two Tie placements although `ties.dat` is absent: their classes come from another level or global data -
to check.

## Assets

| File | Sections | ReLunacy |
| --- | --- | --- |
| `assetlookup.dat` (24 KB) | `0x1D000`, then pairs `0x1D100/0x1D101`, `0x1D140`, `0x1D180/0x1D181`, `0x1D1C0`, `0x1D200/0x1D201`, `0x1D600/0x1D601`, `0x1D700/0x1D701`, `0x1DA00/0x1DA01`, `0x1DB00/0x1DB01` | read (`NewEngineAssetLookup`): where each asset sits in its file (shaders, textures, cubemaps, Mobys, animsets, zones, zone lighting) |
| `assetstats.dat` (256 B) | `0x09800` | **unknown** (asset counts or memory budgets?) |
| `mobys.dat` (5.3 MB) | 19: `0x0D000`, `0x0D100`-`0x0E900`, `0x10200`-`0x10900`, `0x27000`, `0x27100`, `0x05600`, `0x05B00` | read (Moby classes, meshes, skeletons) |
| `animsets.dat` (629 KB) | 14: `0x0F000`-`0x0FE02`, `0x30000`-`0x30760`, `0x11300` | partly (animation metadata) |
| `shaders.dat` (744 KB) | `0x05000`, `0x05200`, `0x05A00`, `0x05D00`, `0x11300` | read |
| `textures.dat` (13 MB) | not IGHW: raw texture data at `assetlookup.dat` offsets | read |
| `cubemaps.dat` (48 KB) | `0x05910`, `0x05920`, `0x05940`, `0x05A00` | read |
| `vfx_system_header.dat`, `vfx_system_texel.dat` | `0x05200` (53 x `0x20`), `0x05300`, `0x05A00` | partly |
| `effect.dat` (652 KB) | IGHW 0.2, 564 entries (`0x00100`, `0x00300`, `0x00400`/`0x00600` pairs, `0x00700`) | **unknown**: particle effects |
| `fxconduit_packed.dat` | IGHW 0.2: `0x12000`, `0x12200`, `0x12400` | **unknown** |

## Sound and dialogue - **not read by ReLunacy**

| File | Sections |
| --- | --- |
| `resident_sound.dat` (8.9 MB) | `0x21000` (the sample data), `0x21100`, `0x21200`, `0x21300` |
| `resident_dialogue.<lang>.dat` | `0x21100`, `0x21200`, `0x21300` (the same family) |
| `dialogue.<lang>.pkg` | IGHW 0.2, `0x26000` |
| `lipsync.<lang>.dat` | `0x18000` (index), `0x18100` (data) |

Seven languages each: de, es, fr, gb, it, nl, us.

## Scripts

`/levels/front_end/scripts/dummy.lua` (37 B) and its chunk: the menu runs no level script of its own.

## Outside the level folder

The game's configuration tables, `packed/game/global_cached.psarc` `/data/configs/*.csv`, loaded as
`CONFIG_TABLE` and parsed by the matching `.lua`. `level.csv` has a block per level - `LEVEL_<NAME>`
(A Crack in Time, Into the Nexus) or `LEVEL_INDEX_<NAME>` (All 4 One) - with its name text, rewards,
Zoni count, segments (pods) and their bolt / XP pools, and for space sectors `BoundsRadius`: the radius of
the play area (2650 in space_sector_1; 3300, 3500, 3000, 2800 for the other sectors) - editing it moves the boundary in game (user test, 2026-11-13). front_end has no
block. Shown in Level Data, edited in View > Config Tables.

## What to decode next

1. `gameplay.dat`'s `0x25000` words: the loader's sizes and counts (+0x10 / +0x14 are the region's gp file sizes).
2. `gp_persist.dat`, all of it - its `0x2502E` count matches `region.dat`'s `0x1C600`.
3. `region.dat`'s `0x1C100` and `0x1C700`, which grow with content.
4. `0x07810` / `0x07820` (per Tie placement) and the UFrag side tables.
5. `assetstats.dat`.
