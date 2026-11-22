# A Crack in Time: the Aphelion's flying height

**Answer: world Y = 780.0 in every space sector.** It is a constant in the EBOOT (an immediate in the space
manager's per-level init), not a config field and not level data. Confidence: high (code and data agree).

## Source (EBOOT, NPUA80966 decrypted EBOOT.elf)

Space manager object: global `0xE899A0` (reached through the usual second-level pointer slots, 86 functions use it).

`0x002F48A4` - space manager init (called from `0x0021703C`, once per space level):

```
bl 0x29A078 ; lfs f13,0x18(r3)          level.csv record +0x18 = BoundsRadius
  if < 0.0002: stw 0x457A0000 -> +0x18  default BoundsRadius 4000
lis r8,0 / r7,0x4443 / r27,0x4461 / r12,0x44FA
stw -> mgr+0x20990..+0x2099C            height table: [0]=0, [1]=780, [2]=900, [3]=2000
mgr+0x08 = table[1]                     current height = 780
mgr+0x04 = 780  (target), mgr+0x0C = 1  (table index)
```

Because the floats are built with `lis`, a byte search for 780.0 (`44430000`) does not find them.

Accessors:

| Address | Does |
|---|---|
| `0x002F267C` | get current height: `lfs f1, 8(r3)` |
| `0x002F2664` | get `table[i]` (callers `0x0030C82C`, `0x0031C57C` with i = 1) |
| `0x002F2684` | select index i: target = table[i], optionally snap current. **No caller found** (no `bl`, no descriptor reference, no inlined copy) |
| `0x002F40E0` | overwrite `table[i]`. **No caller found** |

So the index never leaves 1 in retail: the plane is always 780. Entries 0 / 900 / 2000 are unused leftovers.

## How the ship uses it (class 0x28F Aphelion)

- Init `0x002FC824` copies its own placed matrix translation into +0x1730..+0x173C (ship "plane position",
  +0x1734 = Y) and +0x1760..+0x176C.
- `0x00302EE8` (ship movement) each frame, unless state +0x490 == 1, eases +0x1734 toward the manager height:
  `0x00303810` `lfs f31,+0x1734` / `bl 0x2F267C` / `bl 0x624EAC` (exponential approach, rate -2 x frame time) /
  `stfs +0x1734`. A second branch (`0x00303410`) eases toward height + 5 with rate -1.
- `0x0030209C` (obstacle push-out, X/Z only) writes the Moby position with Y = +0x1734.
- Landing (`space_land_ship` -> `0x00304704`) snaps +0x1730 / +0x1760 to the landing pad beam Moby's
  position (beams sit at Y ~751); afterwards the ease above brings the ship back up to 780.
- Boundary: BoundsRadius (level.csv) around the centre vector `0xE89990` (copied to ship +0x17B0), X/Z only.

## Level data agrees (shipped files; ss1 from its pre-edit .bak)

`dev/tools/flyheight` (headless ReLunacy.Engine loader) over all five sectors:

| Sector | `aphelion` Moby (0x28F) Y | start volumes Y | most common Moby Y | landing pad beams (0x72C) Y |
|---|---|---|---|---|
| space_sector_1 | 780 | `aphelion_start_vol` 780, mission start vols 780 | 780 (73) | 750.8 - 751.6 |
| space_sector_2 | 780 | `aphelion_start_vol`, `usability_spawn_vol` 780 | 780 (199) | 751.0 - 751.2 |
| space_sector_3 | 780 | `scene_aphelion_start_vol` etc. 780 | 780 (147) | 751.1 - 751.3 |
| space_sector_4 | 780 | - | 780 (120) | 751.0 - 751.2 |
| space_sector_5 | 780 | `aphelion_start_vol` 780 | 700 (31), 780 (21) | 751.1 - 751.4 |

Cinematic ship placeholders (`aphelion_scene_*`, class 0x24C), NPC ships (smuggler, Agorian champions), the
`ss#_planetclue_vorselonsship` Clue and several paths (`[780..780]`) also sit at exactly 780. The junk trader's
landing pad is at 760.3 in every sector. No config CSV (space.csv, level.csv, ...) contains a height/altitude
field or the value 780.

## For the editor

Draw the flight plane at a fixed **Y = 780** for ACiT space sectors (constant, same in all five; not read from
the level). Landing pad beams belong ~29 units below it (~751); the landing snaps the ship to the beam, then it
rises back to 780. The plane's horizontal extent is the BoundsRadius circle around the origin.
