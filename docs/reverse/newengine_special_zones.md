# New-engine special zones

Not every zone is art. Once zones are named from `region.dat` (see `NewEngineZoneNames`), a handful
of names turn up that carry no geometry and exist only to hold data: `navmesh`, `navigation`,
`screen_cheats`, `lighting*`, `fog*`, `base_defense_grid`. This is what is in them.

Measured 2026-11-06 over every level of all four new-engine games — All 4 One (NPUA80695), Into the
Nexus (NPUA80908), QForce/Full Frontal Assault (NPEA00378) and A Crack in Time (NPUA80966): 55
levels, 1010 zones. Fields whose meaning is not established are listed as observations, not guesses.

## Census

| name pattern | zones | what it is |
|---|---:|---|
| `art_*` | 638 | ordinary geometry |
| `other` (level-specific names) | 189 | mostly geometry |
| `gp_*`, `gameplay*` | 130 | gameplay; some carry a navmesh |
| `screen_cheats` | 23 | screen-filter cheat definitions |
| `lighting*` | 11 | the lighting and atmosphere environment |
| `navmesh` | 10 | AI navigation mesh |
| `fog*` | 5 | fog-only, no geometry sections at all |
| `navigation` | 3 | same as `navmesh` (A4O's name for it) |
| `base_defense_grid` | 1 | eight test barricade points |

A zone's name does not define its contents — `0x19000`/`0x19200` also appear in some `gp_*` and
level-specific zones, 25 zones in all. The sections are what matter; the name is a hint.

## Navigation mesh: `0x19000` + `0x19200`

These two sections **never appear in a single `art_*` zone**, on any level of any of the four games.

`0x19000` is one 0x40-byte record per navmesh chunk, 1062 chunks over the 25 zones:

| offset | type | meaning |
|---|---|---|
| `+0x00` | u16 | 0 on every chunk measured |
| `+0x02` | u16 | vertex count V |
| `+0x04` | u16 | V again (identical on all 1062) |
| `+0x06` | u16 | edge count E |
| `+0x08` | u16 | V + E (exact on 1062/1062) |
| `+0x0A` | u16 | face count F |
| `+0x0C` | u16 | unknown; grows with V and F |
| `+0x0E` | u16 | F again (identical on all 1062) |
| `+0x10` | u16 | unknown |
| `+0x12` | u16 | unknown; near F |
| `+0x14`..`+0x1F` | — | zero |
| `+0x20` | u32 | byte offset of this chunk's data in `0x19200` |
| `+0x24`..`+0x2F` | — | zero but for a copy of V at `+0x26` |
| `+0x30` | 4x f32 | bounding sphere: centre x, y, z, radius |

`0x19200` is one blob holding every chunk's data back to back. At a chunk's `+0x20` offset sit its V
vertices as 16 bytes each — `float x, y, z` and a zero word — in world space.

Evidence for that reading:

- **All 21011 vertices of all 1062 chunks lie inside their own chunk's bounding sphere.** This is the
  test that settles it: the offset, the vertex count, the 16-byte stride and the sphere all have to be
  right together for it to pass, and nothing else in the blob passes it.
- `+0x20` is a multiple of 128 on 1062/1062 chunks, and strictly ascending within a zone, so chunk
  blocks are 128-byte aligned and laid out in record order.
- V, E and F satisfy Euler's relation for a planar subdivision, `V - E + F = c - h` for c connected
  components and h holes: `E = V + F - 1` exactly on 787/1062, and `E - V - F + 1` is within ±4 on
  1061/1062 (negative = several components, positive = holes). A navmesh is a planar subdivision of
  walkable floor, so this is the expected signature and it is not something arbitrary counts produce.
- Vertex Y values cluster a metre or two above the floor, and the x/z spans match the level's extent.

**Not decoded:** everything in a chunk's block past its vertices. A block is far larger than
`16 * V`, so the edges, faces and whatever adjacency the pathfinder walks are in there, but the
layout past the vertex array has not been established. `+0x0C`, `+0x10` and `+0x12` are unidentified
and are the obvious place to look for their counts.

## Screen-filter cheats: `screen_cheats` and `0x34005`

`0x11300`, the string table, names the effects. Nine entries, identical across 23 zones of three
games (Into the Nexus writes the sixth as `f` rather than `f_seattle`):

```
a_blackandwhite   b_bloom   c_western   f_seattle   g_northpole   h_psychadelic
aaa__this_is_a_hack   aab_this_is_another_hack   i_this_is_also_a_hack
```

The `d_` and `e_` slots are missing from the sequence, so two were removed during development, and
three of the nine are developer placeholders by their own admission.

**The names are positionally parallel to the `0x34001` atmosphere records**: `count(0x34001)` equals
the number of strings on 23/23 zones. So each named cheat is a complete environment override - its
own atmosphere record (`0x34001`), parameters (`0x34002`), and a share of the inscatter (`0x34003`)
and fog (`0x34004`) tables the renderer already reads per region.

`0x34005` is **exclusive to these zones** - 7 records of 0x800 bytes, i.e. 512 ARGB8888 words. Record
1 of `acid_refinery_pre` is an exact identity ramp, `FF000000, FF010101, FF010202, ... FFFEFEFE,
FFFFFFFF`, stepping one level every two entries, which identifies it as a **512-entry 1D colour
lookup table** (a tone curve at 8.8 fixed-point input) rather than image or parameter data. Record 0
is saturated white across almost its whole range, consistent with a blown-out filter.

So a screen cheat is an atmosphere swap plus a colour-grading curve. Seven of the nine have a curve;
the other two presumably only swap the atmosphere.

## `lighting*` zones

These hold the environment the renderer already consumes per region, with no geometry sections at
all: directional and point lights (`0x7800` at 0x80 bytes each, `0x7810`, `0x7820`), `0x7910` and a
large `0x7912` blob, `0x8C00`/`0x8C01`, and on some levels the atmosphere set `0x34001`-`0x34004`.
`lighting_jr` (QForce jungle_ruins) is the exception that also carries geometry, plus `0x5202` and
`0x5A00` which no other `lighting*` zone has.

Worth knowing when hunting a per-region constant: a level may keep its atmosphere in a *zone* named
`lighting*` rather than in `region.dat`.

## `fog*` zones

`fog_no_lighting_01..07` on Into the Nexus's haunted_city. Ordinary zone sections minus `0x5400`,
`0x5410` and `0x5460`, so they have Tie placements but no UFrag geometry of their own. Blob sizes
around 0x500, so they carry almost nothing - a fog volume plus a few placements.

## `base_defense_grid`

One zone, QForce jungle_ruins_pre. No special sections whatsoever: 8 Tie placements and 8 UFrags,
with a completely ordinary art-zone signature. Its `0x11300` names them
`test_barricade_grid_point1` through `test_barricade_grid_point8` - eight anchor points for the
barricades the base-defense mode puts up, left under their test names. There is nothing else in it.
