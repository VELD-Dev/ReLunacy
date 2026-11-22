# A Crack in Time: every placed class, field by field

A Crack in Time (NPUA80966, 01.20): every class a Moby, a Clue or a controller of the game's 22 levels is an
instance of - 290 placed classes, 320 with the bases they derive from. The method, the evidence letters, how
a settings block works, the prompt component, gravity and the spawn rule are explained in
`acit_space_sector_classes.md`, which is this same table cut down to the space sectors; read that first.

What is different here:

- **Scope.** Statistics are over all 22 levels, and a class's line says which levels place it. front_end
  places three Mobys; insomniac_museum reuses the moon and space classes next to its own.
- **Classes nothing has looked at** are still listed, under "Other Clues / controllers / Mobys" at the end,
  with a name from All 4 One's class table when the number is below 0x6A0 (the older classes keep their
  numbers) or else from what their instances are called. Their rows come from three automatic sources: the
  values in the files, the references the class's fix-up resolves, and the script layer below.
- **Script layer.** The game registers 1,102 Lua functions (name, function descriptor pairs in the EBOOT's
  data). Each binding calls an implementation that fetches the instance's class object, tests its class with
  IsA and reads or writes fields. `dev/tools/classfields/natfields.py` follows that and records, per class and
  offset, which functions touch the field; a row that says "Script: written by `enemy_set_patrol_path`" is
  that. A field with exactly one setter takes its name from it. This is evidence E S: the write is read in
  the EBOOT, the meaning is the function's name.
- A row with no meaning is still an open field. Most are on classes used by one or two levels.

Nothing here is tested in game unless the row carries U.

<!-- generated: dev/tools/classfields/gendoc.py -->

## Shared bases

### 0x6 IGG_GameObjectUpdate

own fields +0x4..+0x8; not placed itself; values below are over its 29827 placed descendants; own methods: fix-up 0x0091D528.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4 | run time |  | Class number at run time (the loader swaps the file's +0x00 class number for the vtable; code tests `+4 == class`) | E |

### 0x47 MobyBaseUpdate

Parent 0x6 IGG_GameObjectUpdate; own fields +0x8..+0x5C; not placed itself; values below are over its 23145 placed descendants.

Every Moby class starts with this. The registry says 0x60, but derived classes put their first field at +0x5C, so the base really ends at 0x5C. Nothing here is level design except the two bytes below: the rest is filled by Init (0x00528CF4): +0x14 the Moby instance pointer, +0x1C = 0x2FFFF, +0x20, +0x38, +0x3A, +0x50, +0x5A.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x10 | u8 | 1 on all 23145 | Base flag +0x10: 1 on every placed instance (role open) | D |
| +0x14 | run time |  | Pointer to the Moby instance (position, model...) | E |
| +0x3C | s32 | -1 on all 23145 | -1 in every file: a run-time slot | D |
| +0x54 | u32 | 65535 on all 23145 | 0xFFFF in every file: a run-time slot | D |
| +0x59 | u8 | 0 x20201, 1 x2944 | Starts deactivated: Init takes the deactivate path (0x005238B4: no update, hidden, collision off) instead of the activate one (0x00523B64); a script turns the object on later (asteroid_radioactive, the second pad of a pair, mission NPCs) | E D |
| +0x5A | run time u8 |  | Initialised: set to 1 by Init | E |

### 0xCF ControllerBaseUpdate

Parent 0x6 IGG_GameObjectUpdate; own fields +0x8..+0x14; not placed itself; values below are over its 6682 placed descendants.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC | f32 | 256 x5732, 15000 x136, 5000 x135, 1000 x129, 4000 x92 ... (23 values) | Range. 256 by default, thousands on effects and spawners seen from far (FX 15000, car spawners 4000). Not read by the base methods; role open (QForce has the same slot) | Q D |

## Clues

### 0x11D ClueBase

own fields +0x4..+0x14; 90 placed in the game (19 levels); e.g. `ClueBase#`, `CLUE_HEATMAP_UPPER_RIGHT`, `CLUE_HEATMAP_LOWER_LEFT`; own methods: fix-up 0x00935420, Init 0x00935424.

Clue vtables have 5 slots: 0/1 destructors, 2 fix-up of references, 3 Init(this, handle), 4. The Clue's volume is not a settings field: it is the gp_prius Clue record's +0x00.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x8 | run time |  | The Clue's volume (pointer), set when the Clue is bound | E |
| +0xC | u8 | 1 x1156, 0 x14 | Enabled at start: 1 on every Clue of the space sectors (0 only on script-driven Clues elsewhere) | D |
| +0x10 | run time |  | The Clue's own handle, stored by Init | E |

### 0x11E ClueGravity

Parent 0x11D ClueBase; own fields +0x14..+0x24; 2 placed in the game (galacton_ship); e.g. `ClueGravity#`; own methods: fix-up 0x0015B434.

A gravity region, and SpaceMoon's parent - so every moon Clue is one. At level start 0x004F06AC lists every enabled Clue that is a ClueGravity (list at 0x00F1E534, count at 0x00F1E530). 0x004F076C finds the first one a position is in: its Area (+0x18) when set, else its volume reference (+0x14) when set, else the Clue's own volume. 0x004F0128 then gives the "up" direction there. All four own fields are zero on every SpaceMoon, so a moon's gravity region is the Clue's own sphere and its gravity points at the sphere's centre. See *Gravity*.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | unset on all 37 | Volume to test instead of the Clue's own (0x00920D60); unset on moons | E |
| +0x18 | reference | unset on all 37 | Area to test instead of both (0x0091D7A8); unset on moons | E |
| +0x1C | f32 | 0 on all 37 | With +0x20 set: how much the direction bends with the position along the volume's axis; 0 = straight | E |
| +0x20 | u8 | 0 on all 37 | Directional: gravity follows the volume's own up axis. 0 = by the volume's shape (a sphere pulls to its centre) | E |
| +0x21 | u8 | 0 on all 37 | Inverted: the shape's direction is flipped (walking on the inside) | E |
| +0x22 | u8 | 0 on all 37 | Applies on any ground. 0 = on the ground only the collision surface decides; the Clue acts in the air | E |

### 0x129 ClueDeath

Parent 0x11D ClueBase; own fields +0x14..+0x18; 22 placed in the game (10 levels); e.g. `magboot_hack_death_clue#`, `ClueDeath#`, `junktrader_death_clue`.

The junk trader station's death volume (junktrader_death_clue), one per sector. Its only own word (+0x14) is zero on every instance of the game.

No instance sets anything in the class's own range.

### 0x4BC SpaceMoon

Parent 0x11E ClueGravity > 0x11D ClueBase; own fields +0x24..+0xF8; 35 placed in the game (6 levels); e.g. `moon#b_moonclue`, `moon#a_moonclue`, `moon#x_moonclue`; own methods: fix-up 0x002F30BC, Init 0x002F4BAC.

A moon, or the junk trader's station: the ship's landing target. Fix-up 0x002F30BC (+0xC0, +0xC4, +0xD0, +0xD8 and the prompt's volume), Init 0x002F4BAC, Update 0x002F5190, landing 0x00304704 (space_land_ship).

A moon owns the Mobys of its Clue's editor layer (gameplay_moon1a...). The game starts a layer with 0x0061C1D0, which makes it the current one (0x00F5C760) and calls Init on each of its Clues; Init stores the Clue handle, then walks the current layer's Moby list (0x0061AEA0) and builds a run-time Pod from it (0x00617D54, kept at +0xD8). The landing pad beam (class 0x72C) is left out; the first Zoni (class 0x5D3) and gold bolt (class 0x210) are remembered at +0xC8 / +0xCC; every other member is deactivated and hidden until the moon wakes up. Update runs a small state machine (+0xEC) that fades the members in (Moby +0x14C) and, while the Aphelion is within the moon area's radius of the Clue, tells the ship which moon it is at (0x002FA6E0) - which is what puts the name and the stats on screen.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x28 | component |  | Prompt component (+0x28..+0xBF), see *Prompt component* | E |
| +0x80 | u32 text id | 925 `SS4_PROMPT_SPACE_LNDONMOON4B` x2, 902 `SS1_PROMPT_SPACE_LNDONJUNKSHPDPT1` x1, 910 `SS1_PROMPT_SPACE_LNDONMOON1A` x1 ... (34 values) | Land prompt text: SSn_PROMPT_SPACE_LNDONMOONnX, ..._LNDONJUNKSHPDPTn on the station. Init replaces it with GEN_PROMPT_GPLAY_LAND (0x53F), so this id is not what the game shows | E T |
| +0x84 | reference | unset on all 35 | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0x8C | f32 | 3 on all 35 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0xA0 | u32 | 2 on all 35 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0xB0 | f32 | -1 on all 35 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0xBC | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0xBD | run time u8 |  | Prompt enabled | E |
| +0xBE | run time u8 |  | Prompt showing | E |
| +0xBF | run time u8 |  | Prompt button state | E |
| +0xC0 | reference | Moby, set on 34 of 35: `space_junktrader_landingpad`, `moon#y_landing_pad_beam`, `moon#a_landing_pad_beam` | Landing pad beam: the ship is put at this Moby's position and orientation (0x00304704) | E U |
| +0xC4 | reference | unset on all 35 | A reference, fixed up with the others; 0 on every retail moon | E |
| +0xC8 | run time |  | The moon's Zoni: the Pod member of class 0x5D3, found by Init. Authored values are overwritten. (0x003029FC tests it before use) | E |
| +0xCC | run time |  | The moon's gold bolt: the Pod member of class 0x210 (PickupGoldBolt), found by Init | E |
| +0xD0 | reference | Volume, set on 35 of 35: `moon#b_vol`, `junktrader_vol`, `moon#a_vol` | Moon area (moonXx_vol, a sphere): the scripts run the moon's script while Ratchet is inside it, and the ship counts as "at this moon" within its radius of the Clue. Not the gravity range | E S U |
| +0xD4 | u32 text id | 2426 `SS4_TXT_SPACE_MN2MC` x2, 2358 `SS1_TXT_SPACE_DPT1MC` x1, 2362 `SS1_TXT_SPACE_MN1MC` x1 ... (34 values) | Moon name text: SSn_TXT_SPACE_MNxMC, ..._DPT1MC on the junk depot | T D |
| +0xD8 | run time |  | Run-time Pod of the moon's objects: every Moby of the Clue's layer but the beam, the Zoni and the gold bolt, built by Init. 0 in every file | E D |
| +0xDC | run time f32 |  | Fade timer of the moon's objects (Init zeroes it, Update moves it towards 1) | E |
| +0xE0 | f32 | 50 x19, 25 x14, 20 x2 | Moon radius: 50 on the large moons, 25 on the small ones, 20 on the station. Also the moon map camera's distance | D U |
| +0xE4 | u8 | 1 x31, 0 x4 | Landing message: landing sends message 0xD2 to the beam and to the ship (0x00304790). 1 on moons; 0 on three stations and the museum's moon | E |
| +0xE8 | run time |  | Pointer set by Init | E |
| +0xEC | run time u8 |  | State of the fade (Init sets 3; 0 -> 1, 2 -> 3 in Update) | E |
| +0xED | run time u8 |  | Active, set to 1 by Init: Update tests the ship against the moon area only while set | E |
| +0xEE | u8 | 0 x30, 1 x5 | Station flag: 1 on the five junk trader stations only, 0 on moons. No instruction in the EBOOT loads a byte at +0xEE of anything, so the game most likely never reads it: a level tool flag (the scripts use `depot = true` in their own table) | E D |

### 0x4C2 SpacePlanet

Parent 0x11D ClueBase; own fields +0x14..+0x100; 13 placed in the game (5 levels); e.g. `ClueSpacePlanet_Vorselon`, `ss#_planetclue_zolarforest`, `ss#_planetclue_vorselonsship`; own methods: fix-up 0x002F304C, Init 0x002F61D4.

A destination the ship can land on to load another level: a planet, Vorselon's ship, the arena, the museum. Fix-up 0x002F304C (+0xD4, +0xD8, +0xDC), Init 0x002F61D4, Update 0x002F5670 (the ship being inside the location volume, tested with 0x0061A124, drives the prompt). With a barrier type set, Init spawns the barrier around the planet (objects of class 0x694 and 0x809, art classes 0xEE / 0xEF / 0xF0 for types 1 / 2 / 3, piece spacing 500 / 200 / 500) and Update runs it against the two barrier volumes.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x30 | component |  | Prompt component (+0x30..+0xC7), see *Prompt component* | E |
| +0x88 | u32 text id | 909 `SS1_PROMPT_SPACE_LNDONVORS` x3, 908 `SS1_PROMPT_SPACE_LNDONPLNT1` x1, 888 `SS2_PROMPT_SPACE_LNDONPLNT1` x1 ... (11 values) | Land prompt text (SSn_PROMPT_SPACE_LNDONPLNTn, ..._LNDONVORS, SS0_PROMPT_GPLAY_LAND_INSMU). Init replaces it with GEN_PROMPT_GPLAY_LAND (0x53F) | E T |
| +0x8C | reference | Volume, set on 1 of 13: `ss#_terachnos_barrier_inner` | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0x94 | f32 | 3 on all 13 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0xA8 | u32 | 2 on all 13 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0xB8 | f32 | -1 on all 13 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0xC4 | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0xC5 | run time u8 |  | Prompt enabled | E |
| +0xC6 | run time u8 |  | Prompt showing | E |
| +0xC7 | run time u8 |  | Prompt button state | E |
| +0xC8 | u32 | 4 x3, 2 x1, 7 x1, 8 x1, 13 x1 ... (11 values) | Destination level: the game's LEVEL enum: 2 Zolar Forest, 4 Vorselon's ship (galacton_ship), 7 Molonoth, 8 Axiom City, 11 Krell Canyon, 12 Agorian Arena, 13 Tombli, 16 Valkyrie Fleet (Vapedia), 17 Nefarious Station, 19 Gimlick Valley, 21 Insomniac Museum | T D |
| +0xCC | u32 text id | 2356 `SS1_TXT_SPACE_SHIP1MC` x3, 2338 `GEN_TXT_SPACE_PLNT1MC` x1, 2340 `GEN_TXT_SPACE_PLNT2MC` x1 ... (11 values) | Planet name text (GEN_TXT_SPACE_PLNTnMC, SS1_TXT_SPACE_SHIP1MC...) | T D |
| +0xD0 | u32 text id | 0 x10, 2394 `SS2_TXT_SPACE_BARR1MC` x1, 2418 `SS3_TXT_SPACE_BARR1MC` x1 ... (4 values) | Barrier name text (SSn_TXT_SPACE_BARR1MC); 0 without a barrier | T D |
| +0xD4 | reference | Volume, set on 10 of 13: `ss#_zolar_location_vol`, `ss#_torreniv_planet`, `ss#_terachnos_planet` | Location volume: the ship inside it can land (Update, 0x0061A124). Init also gives it to the prompt | E D |
| +0xD8 | reference | Volume, set on 3 of 13: `ss#_terachnos_barrier_outer`, `ss#_tombli_outer_vol`, `ss#_nefarious_outer_vol` | Barrier outer volume | E D |
| +0xDC | reference | Volume, set on 3 of 13: `ss#_terachnos_barrier_inner`, `ss#_tombli_outer_vol`, `ss#_nefarious_outer_vol` | Barrier inner volume | E D |
| +0xE8 | run time f32 |  | Barrier piece spacing, set by Init from the barrier type | E |
| +0xEC | run time |  | Barrier timer / state (Init zeroes it) | E |
| +0xF0 | u16 | 0 x10, 1 x1, 2 x1, 3 x1 | Barrier type: 0 none, 1 (Terachnos, sector 2), 2 (Tombli, sector 3), 3 (Nefarious station, sector 4) | E D |
| +0xF2 | u8 | 1 on all 13 | Unread flag: 1 on every instance. No instruction in the EBOOT loads a byte at +0xF2 or +0xF3 of anything: most likely never read | E D |
| +0xF3 | u8 | 0 x9, 1 x4 | Not-a-planet flag: 1 on Vorselon's ship and the Agorian arena - the destinations that are not planets. Not read, as +0xF2 | E D |
| +0xF4 | run time |  | Barrier flags and piece count (+0xF4..+0xF8), set by Init and Update | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x18 u32.

## Space objects

### 0x238 space object base

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x4B0; not placed itself; values below are over its 1148 placed descendants; own methods: Init 0x003139E4, Update 0x003138F4.

Base of everything the ship can shoot or hit in space: asteroids, mines, derelicts, satellites, bosses, billboards. Init 0x003139E4, Update 0x003138F4. Almost all of its 0x450 bytes are run-time state (health, physics, three effect slots); their tuning comes from `space.csv` in global_cached.psarc, not from the instance.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | f32 | 1 on all 1148 | 1.0, 1.0, 1.0 at +0x60 / +0x64 / +0x68 on every instance | D |
| +0x470 | u8 | 0 on all 1148 | Script: written by `space_make_attacker` | E S |
| +0x490 | u32 | 1 on all 1148 | 1 on every instance | D |
| +0x4A0 | u32 | 65535 on all 1148 | 0xFFFF on every instance: a run-time slot | D |
| +0x4A4 | run time |  | Run-time pointer and scale (+0x4A4, +0x4A8 = 1.0), set by Init | E |
| +0x4AC | u8 | 1 x984, 0 x164 | 1 on everything except the lava asteroids (asteroid_lava, sector 3). Read when the object is destroyed (0x00313670: when set, 0x00299354(moby, 0) runs - the reward / debris path) | E D |
| +0x4AD | u8 | 1 on all 1148 | 1 on every instance; gates the second half of the same function | E D |
| +0x4AE | run time u16 |  | State flags (+0x4AE, +0x4AF), zeroed by Init. Script: written by `space_set_indestructible` | E S |

Same on every instance: +0x64 = 1, +0x68 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x74 u32, +0x78 u32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0x150 u32, +0x168 u32, +0x180 f32, +0x184 f32, +0x188 f32, +0x18C f32, +0x190 u32, +0x194 u32, +0x198 u32, +0x220 u8, +0x2B0 u8, +0x340 u8, +0x3D0 u8, +0x460 u8, +0x480 u32, +0x484 f32, +0x48C f32, +0x494 u32, +0x498 f32, +0x49C u32.

### 0x24C PathFollower

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x560; 436 placed in the game (13 levels); e.g. `space_junk_tetherable_#_#`, `moon_floating_platform_#_#`, `moon_floating_platform#`; own methods: fix-up 0x0039FEDC, Init 0x003A09F4, Update 0x003A161C.

Anything that rides a path: tetherable space junk, the moons' floating platforms, ring asteroids, skiffs. Fix-up 0x0039FEDC, Init 0x003A09F4, motion setup 0x003A07C4. The motion itself is an embedded component of class 0x247 at +0xB0 (GetInterface 0x0039FCD8), which the script functions `set_pf_speed`, `set_pf_stop`, `set_pf_reverse`, `set_pf_position` and `is_pf_moving` drive; the fields from +0x4C0 are what Init feeds it.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | run time u16 |  | State: 0 stopped, 1 moving, 2 no path; +0x5E the previous state, +0x60 / +0x64 its timing | E |
| +0xB0 | component |  | Path-follow component, class 0x247 (+0xB0..): +0x10 the path, +0x1C flags (bit 3 = reversed), +0x20 the start fraction, +0x34 / +0x38 / +0x3C the speeds `set_pf_speed` sets. Script: read by `taxi_warp` | E S |
| +0x4C0 | reference | Path, set on 434 of 442: `moon#a_spacejunk_ring#`, `moon#a_asteroidcurve#`, `moon#c_asteroid_path_outer` | The path followed. Script: read by `taxi_warp` | E D S |
| +0x4E0 | f32 | 10 x173, 5 x80, 14 x75, 20 x41, 30 x15 ... (19 values) | Speed: given to the path-follow component with +0x4E4 (0x004DC788). 10 by default | E D |
| +0x4E4 | f32 | 10 x166, 28 x59, 20 x54, 30 x27, 35 x18 ... (26 values) | Second speed value given with it (10 by default; most likely the acceleration) | E D |
| +0x4E8 | f32 | 0 x434, 1 x7, 0.5 x1 | Start position along the path, 0-1 (clamped by 0x004DC3F8) | E |
| +0x4EC | f32 | 1 x437, 2 x4, 3 x1 | A factor given to the component at setup (1 on nearly all, 2 on four) | E D |
| +0x4F0 | f32 | 0.5 x350, 0 x51, 0.1 x19, 0.3 x15, 1 x3 ... (7 values) | With +0x4F4: two values given to the component (0x004DC3C4); the second is clamped to 0.1-60. 0.5 and 5 by default | E |
| +0x4F4 | f32 | 5 x388, 0 x45, 4 x6, 3 x2, 2 x1 | | |
| +0x4F8 | run time |  | Zeroed by Init; +0x4FC a handle Init looks up from the Moby's position | E |
| +0x504 | u32 | 0 x392, 8 x50 | | |
| +0x50C | reference | Pod, set on 10 of 442: `BD_TRAFFIC_SKIFF#_MOVE#`, `FUEL_SHIP#_MOVE_POD`, `BD_SKIFF#_MOVE` | Pod of Mobys carried along (skiffs, fuel ships) | E D |
| +0x510 | reference | Area, set on 3 of 442: `move_area_skiffs_#`, `AZI_SHIP_ESCAPE_MOVE_AREA`, `AZI_SHIP_PICKUP_MOVE_AREA` | Area of stop positions | E D |
| +0x514 | f32 | -1 x398, 20 x28, 70 x13, 65 x3 | -1 by default; 20-70 on some | D |
| +0x518 | run time |  | Zeroed by Init; +0x51C..+0x528 the four stop distances along the path, from the stop volumes | E |
| +0x52C | reference | Volume, set on 8 of 442: `volume#`, `graveyard_stop#`, `VOL_PLAT#_PICKUP_POS` | Stop volume 1: Init turns each into a distance along the path. Script: read by `taxi_warp` | E D S |
| +0x530 | reference | Volume, set on 7 of 442: `volume#`, `graveyard_stop#`, `BOSS_TRAV_TAXI_WARP` | Stop volume 2. Script: read by `taxi_warp` | E D S |
| +0x534 | reference | Volume, set on 1 of 442: `graveyard_stop#` | Stop volume 3 | E D |
| +0x538 | reference | Volume, set on 1 of 442: `graveyard_stop#` | Stop volume 4 | E D |
| +0x540 | u8 | 1 x300, 0 x142 | A flag given to the component at setup (1 on two thirds) | E D |
| +0x541 | u8 | 1 x361, 0 x81 | Starts moving: Init sets the state to 1 (moving) instead of 0. Script: written by `taxi_warp` | E S |
| +0x542 | u8 | 0 x402, 1 x40 | Init changes the Moby's collision mode (0x00523690(moby, 2)) | E |
| +0x543 | u8 | 0 x436, 1 x6 | Init sends the object message 0x183 | E |
| +0x547 | u8 | 0 on all 442 | Init sets bit 8 of the Moby's flags | E |
| +0x548 | u8 | 0 on all 442 | Enables the component of class 0x4C at +0x1C0 | E |
| +0x549 | u8 | 0 x401, 1 x41 | Starts hidden | E |
| +0x54A | u8 | 0 x434, 1 x8 | Picks the other of two draw distances | E |
| +0x54C | f32 | 0 x383, 15 x59 | | |
| +0x550 | f32 | 0 x383, 50 x59 | | |
| +0x554 | u32 | 0 x436, 1 x6 | | |

Same on every instance: +0x53C = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x150 f32, +0x154 f32, +0x158 f32, +0x15C f32, +0x160 f32, +0x164 f32, +0x168 f32, +0x16C f32, +0x170 f32, +0x174 f32, +0x178 f32, +0x17C f32, +0x180 f32, +0x184 f32, +0x188 f32, +0x18C f32, +0x1B0 u32, +0x1B4 u32, +0x1B8 u32, +0x1C0 f32, +0x1C4 f32, +0x1C8 f32, +0x1CC f32, +0x1D0 u32, +0x1D4 u32, +0x1D8 u32, +0x260 u8, +0x2F0 u8, +0x380 u8, +0x410 u8, +0x4A0 u8, +0x4B0 u8, +0x500 u32.

### 0x28F Aphelion (ship hero)

Parent 0xE5 HERO_HeroBase > 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x70..+0x1AC0; 5 placed in the game (5 levels); e.g. `aphelion`; own methods: fix-up 0x002FB3D4, Init 0x002FC824, Update 0x00303AB0.

The ship. 0x1AC0 bytes of run-time state; its tuning is `space.csv` (Aphelion block). Init 0x002FC824, Update 0x00303AB0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | component |  | Prompt component (+0x70..+0x107), see *Prompt component* | E |
| +0xC8 | u32 text id | 442 `GEN_TRIPROMPT_GPLAY_TELEPORT` on all 5 | Prompt text (442, the default) | E T |
| +0xCC | reference | unset on all 5 | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0xD4 | f32 | 3 on all 5 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0xE8 | u32 | 2 on all 5 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0xF8 | f32 | -1 on all 5 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0x104 | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0x105 | run time u8 |  | Prompt enabled | E |
| +0x106 | run time u8 |  | Prompt showing | E |
| +0x107 | run time u8 |  | Prompt button state | E |
| +0x420 | reference | Moby, set on 5 of 5: `ratchet` | The pilot: ratchet | E D |
| +0x424 | reference | Controller, set on 2 of 5: `scene_sat_cam` | A camera controller (scene_sat_cam, sectors with satellites) | E D |
| +0x1690 | u32 | 0 on all 5 | Script: read by `space_land_ship` | E S |
| +0x16D0 | component |  | CAM_CameraShakeEffect (class 221) embedded | T |
| +0x1710 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1714 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1718 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x171C | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1730 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1734 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1738 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x173C | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1760 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1764 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1768 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x176C | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x17C0 | u32 | 0 on all 5 | Script: read by `space_land_ship` | E S |
| +0x1830 | u32 | 0 on all 5 | Script: read by `space_land_ship` | E S |
| +0x1840 | u32 | 1065353216 on all 5 | Script: written by `space_land_ship` | E S |
| +0x186C | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1938 | u32 | 0 on all 5 | Script: read by `space_land_ship` | E S |
| +0x1988 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1990 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1994 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1998 | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x199C | f32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x19BC | u32 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x19FE | u8 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1A02 | u8 | 0 on all 5 | Script: written by `space_land_ship` | E S |
| +0x1A04 | u8 | 0 on all 5 | Script: written by `space_land_ship` | E S |

Same on every instance: +0x1844 = 1, +0x1848 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x110 f32, +0x114 f32, +0x118 f32, +0x11C f32, +0x120 u32, +0x124 u32, +0x128 u32, +0x1B0 u8, +0x240 u8, +0x2D0 u8, +0x360 u8, +0x3F0 u8, +0x400 u8, +0x42C u32, +0x43C u32, +0x450 u16, +0x460 u16, +0x4F4 u32, +0x538 u32, +0x53C f32, +0x15F0 u32, +0x15F4 u32, +0x1680 u8, +0x1681 u8, +0x1682 u8, +0x1683 u8, +0x1684 u8, +0x1685 u8, +0x1686 u8, +0x1687 u8, +0x16C0 u32, +0x16F4 u32, +0x1720 f32, +0x1724 f32, +0x1728 f32, +0x1740 f32, +0x1744 f32, +0x1748 f32, +0x174C f32, +0x1750 f32, +0x1754 f32, +0x1758 f32, +0x175C f32, +0x1770 f32, +0x1774 f32, +0x1778 f32, +0x177C f32, +0x17B0 f32, +0x17B4 f32, +0x17B8 f32, +0x17BC f32, +0x17D0 f32, +0x17D4 f32, +0x17D8 f32, +0x17DC f32, +0x1834 f32, +0x1838 u32, +0x183C u32, +0x1870 f32, +0x1874 f32, +0x18CC u32, +0x1920 u32, +0x1930 u32, +0x1934 u32, +0x193C u32, +0x1940 f32, +0x1944 f32, +0x1948 f32, +0x194C f32, +0x1950 f32, +0x1958 f32, +0x195C f32, +0x1968 f32, +0x196C f32, +0x1970 f32, +0x197C f32, +0x1980 f32, +0x1984 f32, +0x198C u32, +0x19A0 f32, +0x19A4 f32, +0x19A8 u32, +0x19AC f32, +0x19B0 f32, +0x19B4 f32, +0x19B8 f32, +0x19C0 f32, +0x19C4 f32, +0x19C8 f32, +0x19CC f32, +0x19D0 u32, +0x19D4 f32, +0x19D8 u32, +0x19DC u32, +0x19E0 u32, +0x19E4 u32, +0x19E8 u32, +0x19EC u16, +0x19EE u8, +0x19F5 u8, +0x19F6 u8, +0x19F7 u8, +0x19F8 u8, +0x19F9 u8, +0x19FB u8.

### 0x337 SimplePathFollower

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x160; 133 placed in the game (18 levels); e.g. `destructible_asteroid#`, `ja_mf_conveyor_bucket#`, `asteroid_habitable_round#`; own methods: fix-up 0x003A2120, Init 0x003A2288.

Round asteroids of the belts, transports. Fix-up 0x003A2120.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x110 | reference | Path, set on 15 of 133: `asteroid_belt_path_#`, `Fish_bridge_C`, `Fish_bridge_A` | The path followed | E D |
| +0x130 | f32 | 10 x103, 150 x18, 40 x6, 100 x3, 5 x3 | Speed-like value (10 by default, 150 on the asteroid belts) | D |
| +0x134 | f32 | 0 x126, 20 x7 | | |
| +0x13C | f32 | 0 x127, 0.05 x6 | | |
| +0x140 | u32 | 0 x118, 8 x11, 1 x4 | | |
| +0x144 | 4 bytes | 0x00000000 x75, 0x00000001 x23, 0x00000101 x19, 0x01000000 x13, 0x00000100 x3 | | |
| +0x148 | u32 | 0 x91, 3 x37, 2 x5 | | |
| +0x14C | f32 | 360 x96, 90 x19, 5 x4, 0 x3, 60 x1 ... (15 values) | Angle-like value (360 by default) | D |
| +0x150 | f32 | -1 x116, 35 x6, 20 x5, 55 x3, 70 x2 ... (6 values) | | |

Same on every instance: +0x138 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x7C u32, +0x8C f32, +0xE4 f32, +0x10C f32, +0x154 u32.

### 0x4C4 space NPC

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x188; 13 placed in the game (space_sector_1, space_sector_3, space_sector_4, space_sector_5); e.g. `smuggler`, `smugglership`, `carina`; own methods: fix-up 0x0031A1E8, Init 0x0031A214, Update 0x0031A328.

A character or ship the Aphelion can hail: the smuggler and his ship, Cassiopeia, Libra, Carina, Azimuth, mission givers. Init 0x0031A214, Update 0x0031A328.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | component |  | Prompt component (+0x70..+0x107), see *Prompt component* | E |
| +0xC8 | u32 text id | 442 `GEN_TRIPROMPT_GPLAY_TELEPORT` x11, 1105 `SS0_PROMPT_GPLAY_HAIL_SHIP` x3 | Prompt text: 442 (GEN_TRIPROMPT_GPLAY_TELEPORT, the default every prompt is saved with) or SS0_PROMPT_GPLAY_HAIL_SHIP (1105) | E T |
| +0xCC | reference | Volume, set on 6 of 14: `cassy_cam_vol`, `ss#_mission_junk_vol`, `smuggler_prompt_vol` | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0xD4 | f32 | 3 x9, 0 x5 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0xE8 | u32 | 2 on all 14 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0xF8 | f32 | -1 on all 14 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0x104 | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0x105 | run time u8 |  | Prompt enabled | E |
| +0x106 | run time u8 |  | Prompt showing | E |
| +0x107 | run time u8 |  | Prompt button state | E |
| +0x108 | u8 | 1 x11, 0 x3 | 1 on characters, 0 on the two smuggler ships | D |
| +0x10C | u32 | 0 x6, 4 x5, 2 x2, 1 x1 | 0, 1, 2 or 4 by character (4 on the Valkyries, 2 on the smuggler): role open | D |
| +0x114 | reference | Volume, set on 11 of 14: `smuggler_scene_cam_vol`, `ss#_mission_tow_terachnoid_camvol`, `cassy_cam_vol` | Camera volume (..._cam_vol): where the conversation camera is taken from | D |
| +0x118 | reference | Volume, set on 10 of 14: `smuggler_lightlink`, `carina_lightlink`, `terac_lightlink` | "Light link" volume (smuggler_lightlink, carina_lightlink...) | D |
| +0x124 | f32 | -2 on all 14 | -2 on every instance | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x110 u32, +0x144 u8.

### 0x556 SpaceAsteroid

Parent 0x67D space drifting object base > 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x510..+0x530; 889 placed in the game (space_sector_1, space_sector_2, space_sector_3, space_sector_4); e.g. `asteroid_habitable#`, `asteroid_lava#`, `asteroid_ice#`; own methods: Init 0x0030C7E4.

889 asteroids, the most common Moby of the sectors. Init 0x0030C7E4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x528 | u8 | 0 x887, 1 x15 | Irradiated asteroid (the game's SpaceIrradiatedAsteroid): 1 on the 15 asteroid_radioactive of sector 1. Init wakes it with a message unless it starts deactivated; the update then tests the hero's distance | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x510 f32, +0x514 f32, +0x518 f32, +0x524 u32.

### 0x67D space drifting object base

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x510; not placed itself; values below are over its 915 placed descendants; own methods: Init 0x0031C2D8, Update 0x0031C370.

Adds drift / tumble (floats +0x4D0..+0x4F8, +0x508) to the space object base. Init 0x0031C2D8, Update 0x0031C370. All run time: no instance sets any of it.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4E0 | u32 | 0 on all 915 | Script: read by `space_make_attacker` | E S |
| +0x50E | u8 | 0 x902, 1 x13 | 1 on the space flotsam (class 0x842), 0 elsewhere. Read by the base (role open) | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4B0 u16, +0x4C0 u32, +0x4D0 f32, +0x4D4 f32, +0x4D8 f32, +0x4DC f32, +0x4E4 f32, +0x4E8 f32, +0x4EC f32, +0x4F0 f32, +0x4F4 f32, +0x4F8 f32, +0x500 u32, +0x504 u32, +0x508 f32.

### 0x72C landing pad beam

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 34 placed in the game (5 levels); e.g. `space_junktrader_landingpad`, `moon#y_landing_pad_beam`, `moon#a_landing_pad_beam`; own methods: fix-up 0x002F3004, Init 0x002F2F64.

The beam a SpaceMoon lands the ship on (SpaceMoon +0xC0). Init 0x002F2F64 sets the Moby's fade distances and zeroes the reward component; fix-up 0x002F3004.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | component |  | ECON_Reward component (+0x5C..+0x7F), zeroed by Init (0x002983F8): no reward | E T |
| +0x80 | reference | Volume, set on 8 of 34: `moon#b_takeoff_vol`, `moon#y_takeoff_vol`, `moon#c_landing_pad_takeoff_vol` | Take-off volume (moonXx_takeoff_vol); set on a quarter of the moons | E D |
| +0x84 | reference | Volume, set on 5 of 34: `junktrader_checkpoint_respawn_vol` | Respawn / checkpoint volume: only on the junk trader stations (junktrader_checkpoint_respawn_vol) | E D |

### 0x757 DerelictShip

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x4C0; 124 placed in the game (space_sector_2); e.g. `agorian_heavy_fighter_derelict#`, `ja_dp_junk_hauler_#_derelict#`, `drudge_hauler_ship_derelict#`; own methods: Init 0x002F6970.

Sector 2's wrecks. Init 0x002F6970. No instance sets anything.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4B0 f32, +0x4B4 f32, +0x4B8 f32.

### 0x75F SpaceDefenseSatellite

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x4F0; 15 placed in the game (space_sector_1, space_sector_2, space_sector_4, space_sector_5); e.g. `nefarious_camera_satellite#`, `satellite#`; own methods: fix-up 0x00311B50, Init 0x00312A34, Update 0x003133F4.

Nefarious camera satellites and the plain satellites. Fix-up 0x00311B50, Init 0x00312A34.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4D0 | reference | Controller, set on 15 of 15: `nefarious_camera_satellite_controller#`, `vorselon_satellite_controller`, `satellite_attack_controller` | Attack controller (SpaceAttackController): what answers when the satellite spots the ship | E D |
| +0x4D4 | reference | Volume, set on 15 of 15: `nefarious_camera_satellite#_inner_vol`, `satellite#_find`, `satellite#_inner_vol` | Inner volume (satelliteN_find): the ship inside it is spotted (0x003130A0) | E D |
| +0x4D8 | reference | Volume, set on 15 of 15: `nefarious_camera_satellite#_outer_vol`, `satellite#_keep`, `satellite#_outer_vol` | Outer volume (satelliteN_keep): the ship stays tracked until it leaves it | E D |
| +0x4DC | u32 | 20 x6, 30 x6, 25 x3 | 20, 25 or 30 by sector (20 in sectors 1-2, 30 in 4, 25 in 5). Not read by the class's own methods | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4C0 u32, +0x4C4 u32, +0x4C8 f32, +0x4CC u32, +0x4E0 u32, +0x4E4 f32, +0x4E8 u16, +0x4EA u8.

### 0x765 Vorselon's ship

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xE0; 2 placed in the game (space_sector_1, space_sector_3); e.g. `vorselons_ship`, `level_vorselons_ship`; own methods: Init 0x004EB8E4, Update 0x004EBAD0.

Embeds a whole attack controller: +0x60 holds class number 1886 (0x75E, the Nefarious attack controller) and the controller's fields follow at the same relative offsets (+0x60 + x).

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | component |  | Embedded SpaceAttackController, class 0x75E, at +0x60..+0xBF (see that class: field x is at +0x60 + x) | D T |
| +0xD4 | reference | Volume, set on 1 of 2: `vorselon_satellite_inner_vol` | Inner volume (vorselon_satellite_inner_vol) | D |
| +0xD8 | reference | Volume, set on 1 of 2: `vorselon_satellite_outer_vol` | Outer volume (vorselon_satellite_outer_vol) | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u8, +0x5D u8.

### 0x76D SpaceShockwaveMine

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x500; 82 placed in the game (space_sector_2, space_sector_3, space_sector_4); e.g. `space_shockwave_mine#`; own methods: Init 0x0031B204, Update 0x0031B4B0.

Init 0x0031B204, Update 0x0031B4B0. No instance sets anything.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4C0 f32, +0x4C4 f32, +0x4C8 f32, +0x4CC f32, +0x4D0 u32, +0x4E4 f32, +0x4E8 f32, +0x4EC f32, +0x4F0 u8.

### 0x796 space mission ship

Parent 0x67D space drifting object base > 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x510..+0x520; 13 placed in the game (5 levels); e.g. `ss#_adwars#_sign#`, `space_junk_trader_billboard#`, `ss#_mission_tow_npc`; own methods: fix-up 0x00313DCC, Init 0x00313D3C, Update 0x00314028.

Ad-war signs, the tow mission's NPC, haulers. Fix-up 0x00313DCC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x510 | u32 | 0 on all 13 | Script: written by `space_make_attacker` | E S |
| +0x514 | reference | Controller, set on 6 of 13: `vorselon_satellite_controller`, `ss#_mission_lost_attack_controller`, `SpaceAttackController_Agorian#` | Attack controller that ambushes around it (set on 6 of 13). Script: read by `space_make_attacker` | E D S |
| +0x518 | u32 | 0 on all 13 | Script: written by `space_make_attacker` | E S |
| +0x51C | u8 | 0 on all 13 | Script: written by `space_make_attacker` | E S |

### 0x7A5 SpaceJumpingFireball

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xB0; 26 placed in the game (space_sector_1, space_sector_3); e.g. `moon#c_sawblade#`, `space_jumping_fireball#`; own methods: Init 0x00319008, Update 0x00319114.

Also the moon1c saw blades. Same six values on all 26 instances.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x84 | f32 | 20 on all 26 | 20, 10, 2, 1, 5 at +0x84..+0x94 and 1 at +0x98 on every instance (jump tuning; roles open) | D |

Same on every instance: +0x88 = 10, +0x8C = 2, +0x90 = 1, +0x94 = 5, +0x98 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0xA4 f32.

### 0x7BB SpaceComet

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x90; 1 placed in the game (space_sector_2); e.g. `ss#_comet`; own methods: fix-up 0x00310A44, Init 0x00310814, Update 0x00310E78.

Sector 2's comet. Init 0x00310814, Update 0x00310E78.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | reference | Path, set on 1 of 1: `ss#_comet_path` | The comet's path | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x84 u32, +0x88 u32.

### 0x842 SpaceFlotsam

Parent 0x556 SpaceAsteroid > 0x67D space drifting object base > 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x530..+0x530; 13 placed in the game (space_sector_2); e.g. `ss#_junk#`; own methods: Init 0x00318F94.

Sector 2's ss2_junk. A SpaceAsteroid with its own Init (0x00318F94); no own field.

No instance sets anything in the class's own range.

### 0x899 Agorian satellite / champion ship

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x510; 7 placed in the game (space_sector_3, space_sector_5); e.g. `ss#_satellite#`, `agorian_champion_ship#`; own methods: fix-up 0x0030ABCC, Init 0x0030B930, Update 0x0030C700.

Same layout as the defense satellite, 0x10 further. Fix-up 0x0030ABCC, Init 0x0030B930.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4E0 | reference | Controller, set on 7 of 7: `SpaceAttackController_Agorian#`, `agorian_camera_satellite_controller` | Attack controller | E D |
| +0x4E4 | reference | Volume, set on 7 of 7: `ss#_satellite#_inner_vol`, `nefarious_camera_satellite#_inner_vol` | Inner volume | E D |
| +0x4E8 | reference | Volume, set on 7 of 7: `ss#_satellite#_outer_vol`, `nefarious_camera_satellite#_outer_vol` | Outer volume | E D |
| +0x4EC | u32 | 25 x4, 30 x3 | 25 (satellites) or 30 (champion ships) | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4B0 u16, +0x4C0 u16, +0x4C2 u16, +0x4C4 f32, +0x4C8 f32, +0x4CC u16, +0x4D0 u32, +0x4D4 u32, +0x4D8 f32, +0x4DC u32, +0x4F0 f32, +0x4F4 f32, +0x4F8 u32, +0x4FC f32, +0x500 u32, +0x504 u16.

### 0x89A SpaceValkyrieBossLibra

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x530; 1 placed in the game (space_sector_3); e.g. `valkyrie_boss`; own methods: fix-up 0x003204E8, Init 0x003215E4, Update 0x00322600.

Sector 3's valkyrie_boss. Fix-up 0x003204E8.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4CC | reference | Controller, set on 1 of 1: `SpaceAttackController_Valkyrie#` | Attack controller (SpaceAttackController_Valkyrie) | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4C0 u32, +0x4C4 u32, +0x4C8 u32, +0x4D0 u32, +0x4D4 u32, +0x4D8 u32, +0x4E4 u32, +0x4E8 u32, +0x4EC u32, +0x4F0 u32, +0x4F4 u8, +0x4F5 u8, +0x4F6 u8, +0x4F8 f32, +0x4FC f32, +0x500 f32, +0x504 f32, +0x508 u32, +0x50C u32, +0x510 u32, +0x514 f32, +0x518 f32, +0x51C u8, +0x51D u8, +0x51E u8, +0x51F u8, +0x520 u8, +0x521 u8, +0x522 u8.

### 0x89B SpaceValkyrieBossCarina

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x540; 1 placed in the game (space_sector_4); e.g. `valkyrie_boss`; own methods: fix-up 0x0031D504, Init 0x0031E58C, Update 0x0031F7BC.

Sector 4's valkyrie_boss. Fix-up 0x0031D504.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4CC | reference | Controller, set on 1 of 1: `valkyrie_boss_controller` | Attack controller (valkyrie_boss_controller) | E D |
| +0x51C | u8 | 0 on all 1 | Script: read by `space_is_aware` | E S |
| +0x530 | u8 | 0 on all 1 | Script: read by `space_is_aware` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4C0 u32, +0x4C4 u32, +0x4C8 u32, +0x4D0 u32, +0x4D4 u32, +0x4D8 u32, +0x4E4 u32, +0x4EC u32, +0x4F0 u32, +0x4F4 u32, +0x4F8 u32, +0x4FC u8, +0x4FD u8, +0x4FE u8, +0x504 u32, +0x508 f32, +0x50C f32, +0x510 u32, +0x514 u32, +0x518 u32, +0x524 f32, +0x528 f32, +0x52C u32, +0x535 u8.

### 0x8A8 SpaceBillboard

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x4B0; 3 placed in the game (space_sector_3); e.g. `space_junk_trader_billboard#`; own methods: Init 0x0030F9EC.

The junk trader billboards of sector 3. No own field.

No instance sets anything in the class's own range.

### 0x8BE space mission NPC

Parent 0x4C4 space NPC > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x188..+0x1F0; 1 placed in the game (space_sector_2); e.g. `ss#_mission_junk_npc`; own methods: Init 0x0031954C, Update 0x003195E8.

A space NPC with 0x68 more bytes, all zero on its one instance (ss2_mission_junk_npc).

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x1E0 | u32 | 0 on all 1 | Script: written by `space_grab_junk` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x190 f32, +0x194 f32, +0x198 f32, +0x19C f32, +0x1A0 f32, +0x1A4 f32, +0x1A8 f32, +0x1AC f32, +0x1B0 f32, +0x1B4 f32, +0x1B8 f32, +0x1BC f32, +0x1C0 f32, +0x1C4 u32, +0x1E4 u32.

## Controllers

### 0xD1 CamArg

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x60; not placed itself; values below are over its 976 placed descendants.

Base of the camera controllers: two CAM_CameraBlendParam blocks.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 3 x557, 0 x237, 4 x59, 5 x52, 6 x49 ... (7 values) | Blend in: type (0 = cut; 3, 6... = blend curves). First CAM_CameraBlendParam block, +0x14..+0x33 | Q D |
| +0x18 | u32 | 0 x958, 1 x17, 2 x1 | | |
| +0x1C | u32 | 0 x862, 1 x114 | | |
| +0x24 | u32 | 0 x970, 1 x6 | | |
| +0x2C | f32 | 0 x926, 2 x20, 1 x14, 5 x5, 3 x3 ... (11 values) | Blend in: seconds | Q D |
| +0x34 | u32 | 3 x689, 0 x138, 6 x82, 5 x25, 4 x23 ... (7 values) | Blend out: type. Second CAM_CameraBlendParam block, +0x34..+0x53 | Q D |
| +0x38 | u32 | 0 x964, 1 x12 | | |
| +0x3C | u32 | 0 x838, 1 x138 | | |
| +0x4C | f32 | 0 x888, 1 x49, 2 x28, 4 x2, 2.5 x2 ... (11 values) | Blend out: seconds | Q D |
| +0x50 | reference | Volume, set on 3 of 976: `orvus_staff_collect_cam_follow_forward`, `CUTSCENE_SUBEND_EXITCAM_VOL`, `CUTSCENE_MQWARK_EXITCAM_VOL` | | |
| +0x58 | u32 | 1 x975, 0 x1 | | |

Same on every instance: +0x20 = 1, +0x28 = 1, +0x40 = 1, +0x48 = 1.

### 0xD2 CamArgFollowPath

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0xA8; 238 placed in the game (21 levels); e.g. `td_scene#_cam#`, `moon#a_goal_cam`, `moon#b_goal_cam`; own methods: fix-up 0x0004AC88, Init 0x0004AC7C.

The moons' goal cameras, arrival and exit scenes.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | reference | Path, set on 238 of 238: `curve#`, `moon#a_goal_cam_curve`, `moon#b_goal_cam_curve` | Camera path | E Q D |
| +0x64 | reference | Path, set on 125 of 238: `curve#`, `scene_arrival_lookat_curve`, `scene_exit_lookat_curve` | Look-at path | E Q D |
| +0x68 | reference | Volume, set on 59 of 238: `arena_spawn_vol_center`, `cam_lookat_back`, `shuttle_launch_lookat_vol` | Look-at volume (used without a look-at path) | E Q D |
| +0x6C | reference | Moby, set on 54 of 238: `aphelion_zoni#`, `moon#b_zoni`, `moon#a_reward` | Look-at Moby (a moon's Zoni or reward) | E Q D |
| +0x70 | f32 | -1 x46, 6 x23, 8 x22, 4 x20, 10 x20 ... (35 values) | Duration in seconds over the whole path (-1 = use the speed) | Q D |
| +0x74 | f32 | -1 x159, 1 x21, 2 x16, 3 x7, 5 x7 ... (21 values) | Speed along the path (-1 = use the duration) | Q D |
| +0x78 | f32 | 0 x185, 1 x33, 2 x6, 0.5 x4, 5 x3 ... (10 values) | Not read by the QForce camera (role open) | Q |
| +0x7C | f32 | -1 x168, 1 x24, 2 x13, 4 x5, 5 x5 ... (17 values) | Ease in (-1 = constant speed) | Q D |
| +0x80 | f32 | -1 x206, 1 x9, 0.5 x6, 5 x4, 10 x3 ... (14 values) | Ease out | Q D |
| +0x84 | f32 | 0 x193, 1 x21, 0.5 x6, 1.5 x5, 4 x3 ... (13 values) | Delay before moving, seconds | Q D |
| +0x88 | f32 | 0 x132, 99 x14, 10 x14, 1 x13, 2 x12 ... (20 values) | Hold at the end, seconds (99 = until the script switches) | Q D |
| +0x8C | reference | Controller, set on 12 of 238: `CAM_SCENE_GRAVRAMP_B`, `CAM_SCENE_SMUGGLER#A`, `cam_azimuth_jump_cam#` | Next camera | E Q D |
| +0x90 | f32 | 0 x236, 80 x2 | Field of view when > 0 | Q D |
| +0x94 | u32 | 0 x232, 1 x6 | | |
| +0xA0 | u32 | 0 x235, 1 x3 | | |
| +0xA4 | f32 | -1 x236, 50 x1, 100 x1 | -1 on nearly all | Q D |

### 0x171 EnemyBattlefieldController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0xAC; 77 placed in the game (16 levels); e.g. `BattlefieldControllerA#_Ranged`, `BattlefieldControllerA#_Melee`, `EnemyBattlefieldController#`; own methods: Init 0x000962A0, Update 0x0009654C.

Referenced by enemies (EnemyBase +0x888). No instance sets anything but the range.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x14 u32, +0x18 u32, +0x1C u32, +0x20 u32, +0x24 u32, +0x28 u32, +0xA4 u8, +0xA8 f32.

### 0x173 EnemyFirepointController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x620; 322 placed in the game (14 levels); e.g. `EnemyFirepointController#`, `moon#x_group#_firepoint_controller`, `moon#b_ap#_firepoint_controller`; own methods: fix-up 0x00097350, Init 0x00097388, Update 0x00096ECC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | Area, set on 311 of 322: `moon#y_enemies_group#_firepoints`, `moon#b_ap#_firepoints`, `moon#x_enemies_group#_firepoints` | Area of the firepoints it hands out | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x220 u32, +0x618 u8, +0x619 u8.

### 0x202 SoundEnvironment

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x80; 348 placed in the game (21 levels); e.g. `AUD_ENV_IslandFan#`, `AUD_Reverb_Master`, `AUD_ENV_FanEmit#`; own methods: fix-up 0x004EC9D0, Init 0x004ED1C4, Update 0x004ECC14.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 0 x230, 10062 x24, 10061 x8, 11028 x7, 10007 x5 ... (62 values) | Sound id (10000-range) | D |
| +0x18 | f32 | 1 x245, 0.1 x44, 2 x20, 0.5 x13, 4 x7 ... (13 values) | | |
| +0x1C | f32 | 1 x239, 3 x52, 2 x23, 4 x20, 5 x7 ... (8 values) | | |
| +0x20 | s32 | -1 x347, 3 x1 | | |
| +0x28 | u32 | 0 x256, 196 x77, 180 x15 | | |
| +0x2C | u32 | 0 x243, 212 x44, 180 x28, 228 x19, 244 x7 ... (8 values) | | |
| +0x38 | u32 | 0 x268, 1 x80 | | |
| +0x44 | u32 | 10 x277, 7 x13, 13 x12, 11 x11, 17 x11 ... (14 values) | | |

Same on every instance: +0x24 = -1, +0x40 = -1, +0x48 = 1, +0x4C = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x30 u32, +0x34 u32, +0x50 f32, +0x54 f32, +0x58 f32, +0x5C u32, +0x60 f32, +0x64 f32, +0x68 f32, +0x6C u32, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C u32.

### 0x260 CamArgIndexedPath

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x9C; 65 placed in the game (16 levels); e.g. `moon#c_asteroid#_cam`, `gravroom_#_fixed_follow_#`, `GCD_GUN_PLATFORM#_CAM`; own methods: fix-up 0x0004AD70, Init 0x0004AD64.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | reference | Path, set on 61 of 65: `path_bossvator_hero_index`, `moon#f_asteroid#_cam_lookat_path`, `gun_platform#_TT_path` | Index path: the hero's progress along it drives the camera | E D |
| +0x64 | reference | Path, set on 61 of 65: `moon#f_asteroid#_cam_lookat_path`, `magroom#_look_at_path_#`, `gun_platform#_look_at_path` | Look-at path | E D |
| +0x68 | reference | Path, set on 60 of 65: `moon#f_asteroid#_cam_follow_path`, `magroom#_index_cam_path_#`, `gun_platform#_cam_path` | Camera path | E D |
| +0x6C | f32 | -1 x64, 6 x1 | | |
| +0x70 | f32 | 0 x60, 85 x3, 120 x1, 80 x1 | | |
| +0x7C | f32 | 0 x64, 60 x1 | | |
| +0x84 | f32 | 0 x61, 30 x4 | | |
| +0x88 | f32 | 0 x61, 30 x4 | | |
| +0x8C | f32 | 0 x61, 30 x3, 20 x1 | | |
| +0x90 | f32 | 0 x61, 30 x3, 20 x1 | | |
| +0x94 | 4 bytes | 0x00000000 x26, 0x00010000 x19, 0x00000001 x6, 0x00010001 x6, 0x01010000 x5 ... (6 values) | | |
| +0x98 | 4 bytes | 0x00000000 x57, 0x00010000 x6, 0x01010000 x1, 0x02000000 x1 | | |

Same on every instance: +0x80 = 20.

### 0x333 CamArgOpeningView

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x68; 37 placed in the game (15 levels); e.g. `CamArgOpeningView#`, `moon#b_opening_view_cam`, `moon#d_opening_view_cam`; own methods: Init 0x0004ADD4.

A moon's opening view.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | f32 | -1 x33, 75.9 x2, 75 x1, 85 x1 | -1, or 75-85: most likely a field of view | D |
| +0x64 | u8 | 1 x27, 0 x10 | 1 on three quarters | D |

### 0x35E CarSpawner

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x60; 645 placed in the game (18 levels); e.g. `aCarSpawner#`, `CarSpawner#`, `carspawner_inner_path#`; own methods: fix-up 0x0034ED48, Init 0x0034FCB8, Update 0x0034FC14.

Ambient traffic: space transports, asteroid streams, the Great Clock's cars. Init 0x0034FCB8 deactivates every Moby of the Pod and finds where on the path the stream starts; Update 0x0034FC14 sends the next one when its timer falls due.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC | run time f32 |  | Range: Init overwrites it with a constant | E |
| +0x10 | run time u8 |  | Active: set from +0x54 by Init | E |
| +0x14 | reference | Path, set on 645 of 645: `curve#`, `carpath#`, `asteroid_path_#` | Path the spawned objects fly | E D |
| +0x18 | reference | Pod, set on 645 of 645: `time_energy_cars`, `ambient_space_transports`, `ambient_clock_cars` | Pod of the Mobys to send (ambient_space_transports...). Init deactivates them all | E D |
| +0x1C | reference | unset on all 645 | Start volume: the stream starts at the point of the path nearest to it; unset = the path's start | E |
| +0x20 | f32 | 0 x514, 30 x64, 10 x39, 1 x28 | | |
| +0x24 | f32 | 0 x575, 10 x39, 1 x28, 20 x3 | | |
| +0x28 | f32 | 0 x578, 10 x39, 1 x28 | | |
| +0x2C | f32 | 0 x578, 10 x39, 1 x28 | | |
| +0x30 | f32 | 1 x114, 5 x44, 10 x33, 15 x28, 3 x23 ... (77 values) | Spawn interval, minimum seconds | E |
| +0x34 | f32 | 20 x95, 2 x43, 10 x42, 4 x37, 60 x24 ... (78 values) | Spawn interval, maximum seconds: the next one leaves after a random time between the two | E |
| +0x38 | run time f32 |  | When the next one leaves | E |
| +0x3C | f32 | 250 x67, 20 x58, 71 x41, 70 x36, 60 x29 ... (67 values) | Most likely the speed | D |
| +0x40 | f32 | 0 x622, 0.6 x18, 0.3 x3, 0.5 x2 | | |
| +0x44 | f32 | 0 x154, 10 x102, 12 x68, 13 x41, 2 x32 ... (25 values) | | |
| +0x48 | run time f32 |  | Distance along the path where the stream starts | E |
| +0x4C | u32 | 0 x517, 8 x117, 1 x11 | | |
| +0x53 | u8 | 0 x399, 1 x246 | When set, Update runs an extra pass every frame (0x0034FA4C; role open) | E |
| +0x54 | u8 | 0 x412, 1 x233 | Start active | E |
| +0x58 | f32 | 60 x566, 0 x50, 10 x12, -100 x11, 5 x6 | | |
| +0x5C | f32 | 180 x566, 10 x38, 0 x12, 20 x12, -300 x11 ... (6 values) | | |

### 0x360 CamArgFixed

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x6C; 464 placed in the game (21 levels); e.g. `CamArgFixed#`, `goldbolt#cam`, `moon#a_kickpad#_cam`; own methods: Init 0x0004AC14.

The view sits on the controller and looks along its local +Z.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | f32 | 0 x462, 90 x1, 100 x1 | Field of view in degrees when > 0 (0 keeps the game's) | Q D |
| +0x64 | u32 | 0 x448, 1 x16 | 0 / 1 (role open) | D |

### 0x369 SoundEmitter

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x28; 707 placed in the game (21 levels); e.g. `SoundEmitter#`, `AUD_E_IslandFan#`, `AUD_E_FanEmit#`; own methods: fix-up 0x004EC384, Init 0x004EC314, Update 0x004EC408.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4 | reference | unset on all 707 | Reference slot: resolved by the class's fix-up | E |
| +0x14 | u32 | 10020 x37, 10078 x36, 10246 x36, 10787 x35, 10067 x33 ... (102 values) | Sound id (10000-range) | D |
| +0x18 | u32 | 100 x526, 0 x181 | 100 or 0 | D |

Same on every instance: +0x24 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1C u32, +0x20 u32.

### 0x3D0 FxController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x1B0; 3235 placed in the game (21 levels); e.g. `FX_moon#a_lava_fireburst#`, `FxController#`, `Mortar_Floor_#`; own methods: fix-up 0x00345864, Init 0x003466FC, Update 0x00346850.

Plays an effect at the controller. The fields up to +0x9C are QForce's FxController four bytes earlier (its controller base is four bytes longer) and the retail values agree; the run-time fields from +0xB0 are at the same offsets in both games.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 10108 x301, 10128 x281, 10231 x232, 10172 x205, 10106 x204 ... (128 values) | Effect key: the fxconduit record (class FxController, event ACTIVE) that gives the effect | Q D |
| +0x18 | u32 | 0 x3228, 10109 x7 | Effect key (alternate): used when the start call asks for the alternate effect | Q D |
| +0x1C | u32 | 0 x1877, 1 x1358 | Start active | Q D |
| +0x20 | u32 | 0 x2851, 1 x384 | Tracked: the controller keeps the effect alive and can stop it | Q D |
| +0x24 | f32 | 0 x2514, 1.5 x393, 1 x151, 0.25 x92, 3 x32 ... (9 values) | Retrigger delay min: with the max > 0, the effect plays again after a random delay between the two | Q D |
| +0x28 | f32 | 0 x2514, 3 x521, 0.75 x92, 6 x53, 10 x31 ... (8 values) | Retrigger delay max | Q D |
| +0x2C | f32 | 1 x3207, 0.8 x28 | Retrigger chance, 0-1: a retrigger that falls due only plays this often | Q D |
| +0x30 | f32 | 0 x3229, 4 x6 | Timer B min: with the max > 0, an impulse / camera shake fires that long after the spawn | Q D |
| +0x34 | f32 | 0 x3229, 4 x6 | Timer B max | Q D |
| +0x38 | f32 | 0 on all 3235 | Timer C min: with the max > 0, the tracked effect is stopped that long after the spawn | Q |
| +0x3C | f32 | 0 on all 3235 | Timer C max | Q |
| +0x40 | u32 | 0 on all 3235 | Trigger source A (mode 2); +0x44 B (mode 1), +0x48 C (mode 3); 0 on every retail controller | Q |
| +0x54 | reference | Volume, set on 104 of 3235: `FX_rainVolume_sneaky_#`, `FX_rainDrops_vol#`, `FX_ambFireballs_#` | Volume the effect fills (rain volumes) | E D |
| +0x60 | u32 | 0 x3131, 1 x104 | | |
| +0x64 | u32 | 0 x3139, 1 x96 | | |
| +0x68 | u32 | 0 x3231, 1 x4 | | |
| +0x6C | reference | unset on all 3235 | Reference slot: resolved by the class's fix-up | E |
| +0x70 | f32 | 0 x3206, 1 x29 | Impulse amount, fired at the controller when Timer B falls due | Q D |
| +0x74 | f32 | 2 x3208, 0.6 x14, 3 x10, 3.15 x1, 1 x1 ... (6 values) | Impulse radius (2 on nearly all) | Q D |
| +0x78 | u32 | 1 x3211, 0 x14, 2 x10 | Impulse value passed with the amount (1 on nearly all) | Q D |
| +0x7C | u32 | 0 x3220, 1 x15 | | |
| +0x80 | u32 | 0 x3210, 512 x25 | | |
| +0x8C | u32 | 0 x3190, 3 x45 | | |
| +0x90 | f32 | 0 x2781, 6 x265, 3 x99, 1 x52, 12 x27 ... (6 values) | Camera shake A: with shake D > 0, Timer B starts the embedded camera shake with A, B, C, 20, D | Q D |
| +0x94 | f32 | 0 x2663, 22 x138, 12 x127, 10 x96, 50 x92 ... (11 values) | Camera shake B | Q D |
| +0x98 | f32 | 0 x2653, 0.6 x333, 0.3 x126, 3 x80, 1 x22 ... (10 values) | Camera shake C | Q D |
| +0x9C | f32 | 0 x2653, 1 x297, 0.7 x117, 0.3 x107, 0.8 x45 ... (8 values) | Camera shake D: non-zero enables the shake | Q D |
| +0xB0 | run time f32 |  | Activated at: the game clock when the controller became active (-1 in every file) | Q D |
| +0xBC | run time f32 |  | Next retrigger (-1 in every file) | Q D |
| +0xC0 | run time f32 |  | Timer C due (-1 in every file) | Q D |
| +0x150 | component |  | CAM_CameraShakeEffect (class 221) embedded at +0x150..+0x187 | T Q |
| +0x1A0 | 4 bytes | 0x00000000 x3212, 0x00010000 x23 | | |

Same on every instance: +0x84 = 2, +0x88 = 3.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x44 u32, +0x48 u32, +0x4C u32, +0x5C f32, +0xA0 u32, +0xB4 u32, +0xB8 u8, +0xC4 f32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0xF0 f32, +0xF4 f32, +0xF8 f32, +0xFC f32, +0x100 f32, +0x104 f32, +0x108 f32, +0x10C f32, +0x110 f32, +0x114 f32, +0x118 f32, +0x11C f32, +0x120 f32, +0x124 f32, +0x128 f32, +0x12C f32, +0x130 u8, +0x131 u8, +0x132 u8, +0x134 f32, +0x138 u32, +0x13C u32, +0x140 u32.

### 0x402 CamArgFixedFollow

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x74; 105 placed in the game (18 levels); e.g. `ss#_junkdepot_armor_cam`, `CamArgFixedFollow#`, `scene_aphelion_cam#`; own methods: fix-up 0x0004AC2C, Init 0x0004AC20.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x64 | reference | Moby, set on 95 of 105: `ratchet`, `APHELION`, `aphelion_zoni#` | The Moby the fixed camera turns to follow (ratchet, the Aphelion, a Zoni, the armor pad) | E D |
| +0x68 | f32 | 540 x98, 2400 x2, 1080 x1, 720 x1, 1800 x1 ... (7 values) | 540 on nearly all, with +0x6C: turn speed limits | D |
| +0x6C | f32 | 540 x98, 2400 x2, 1080 x1, 720 x1, 1800 x1 ... (7 values) | | |
| +0x70 | f32 | 90 x93, 250 x2, 2400 x2, 500 x1, 275 x1 ... (11 values) | 90 on nearly all | D |

### 0x626 HazardSwampGasJet

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x20; 23 placed in the game (space_sector_2); e.g. `HazardSwampGasJet#`; own methods: Init 0x0016781C, Update 0x001679E4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | f32 | 4 x20, 2 x3 | 4 or 2; with +0x18 (2 or 4) and +0x1C (1): jet timing | D |
| +0x18 | f32 | 2 x13, 4 x10 | | |

Same on every instance: +0x1C = 1.

### 0x6E3 SpaceAsteroidField

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x50; 4 placed in the game (space_sector_3); e.g. `SpaceAsteroidField#`; own methods: fix-up 0x0030CF38, Init 0x0030D494, Update 0x0030D738.

Sector 3. Init 0x0030D494, Update 0x0030D738.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x30 | reference | Pod, set on 4 of 4: `bill_puts_too_many_asteroids_#` | Pod of the field's asteroids | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x20 f32, +0x24 f32, +0x28 f32, +0x2C f32, +0x34 u32, +0x48 f32.

### 0x714 SpaceAttackController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x5C; not placed itself; values below are over its 13 placed descendants.

Base of the three attack controllers: sends enemy ships at the Aphelion when a satellite, a mission ship or a boss that references it calls. The wave size is picked by 0x0030E2B0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 6 x7, 3 x6 | | |
| +0x18 | f32 | 0 x7, 1 x6 | | |
| +0x1C | f32 | 3 x7, 1 x6 | | |
| +0x38 | u32 | 4 x10, 5 x3 | Enemy ships to keep alive, minimum | E |
| +0x3C | u32 | 4 x10, 5 x3 | Enemy ships to keep alive, maximum: when fewer than a random count between the two are flying, the caller (0x00313E04 on a mission ship) sends a new wave | E |
| +0x40 | u32 | 3 x12, 2 x1 | Ships per wave, minimum | E |
| +0x44 | u32 | 5 x8, 4 x3, 3 x2 | Ships per wave, maximum (a random count between the two, 0x006669F4) | E |
| +0x48 | f32 | 60 x6, 125 x2, 65 x2, 50 x2, 100 x1 | Health of the wave's carrier, the class 0x764 object spawned to launch it (stored to its +0x350 / +0x354) | E |
| +0x4C | s32 | -1 on all 13 | Ships left to send in total: each wave is capped by it and takes from it; -1 = no limit (every retail controller) | E |
| +0x50 | u32 | 8 x10, 0 x3 | Second ship type: most alive at once (a random cap from 0 to this, less the ones already flying, 0x00314DA0) | E |
| +0x54 | u32 | 50 x10, 0 x3 | Chance in percent that a wave includes the second ship type (the bombers) | E |
| +0x58 | u32 | 2 x10, 0 x3 | Second ship type: most per wave. They take the place of first-type ships, so the wave size holds | E |

Same on every instance: +0x20 = 150, +0x24 = 2, +0x28 = 3, +0x2C = 6, +0x30 = 1, +0x34 = 1.

### 0x75D SpaceAttackController (Agorian)

Parent 0x714 SpaceAttackController > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 4 placed in the game (space_sector_3, space_sector_5); e.g. `SpaceAttackController_Agorian#`, `agorian_camera_satellite_controller#`, `agorian_camera_satellite_controller`.

Spawn method 0x0030E810: picks the wave, shuffles it and hands each ship to the carrier (0x00315E00).

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u8 | 0 x2, 1 x2 | Fighters are the advanced kind: 0 = class 0x756 (art 0xBA), 1 = class 0x82C (art 0xBB) | E |
| +0x5D | u8 | 0 x2, 1 x2 | Bombers are the advanced kind: 0 = class 0x755 (art 0xB8), 1 = class 0x839 (art 0xB9) | E |

### 0x75E SpaceAttackController (Nefarious)

Parent 0x714 SpaceAttackController > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 6 placed in the game (space_sector_1, space_sector_2, space_sector_4, space_sector_5); e.g. `nefarious_camera_satellite_controller#`, `vorselon_satellite_controller`, `ss#_mission_lost_attack_controller`.

Spawn method 0x0030E5B4, the same shape as the Agorian one with art classes 0xBE / 0xBF.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u8 | 0 x3, 1 x3 | Fighters are the advanced kind | E |
| +0x5D | u8 | 0 x3, 1 x3 | Bombers are the advanced kind | E |

### 0x81D SpaceAttackController (Valkyrie)

Parent 0x714 SpaceAttackController > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 3 placed in the game (space_sector_3, space_sector_4); e.g. `SpaceAttackController_Valkyrie#`, `valkyrie_boss_controller#`, `valkyrie_boss_controller`.

Spawn method 0x0030E450.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u8 | 1 x2, 0 x1 | Selects the ship kind, as on the other two | E |

## Moon gameplay objects

### 0x4D PlatformGrappleBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x470; not placed itself; values below are over its 35 placed descendants; own methods: fix-up 0x00375608, Init 0x00375634, Update 0x003754F4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x3F8 | u32 | 0 x33, 2 x1, 3 x1 | | |
| +0x414 | reference | Path, set on 1 of 35: `platformcurve` | | |
| +0x418 | u32 | 2 x34, 1 x1 | | |
| +0x464 | 4 bytes | 0x00000000 x32, 0x01000000 x3 | | |

Same on every instance: +0x428 = -1, +0x43C = 0x01000000, +0x450 = 3, +0x45C = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xD0 u32, +0xD4 u32, +0xD8 u32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0xF0 u32, +0xF4 u32, +0xF8 u32, +0x180 u8, +0x210 u8, +0x2A0 u8, +0x330 u8, +0x3C0 u8, +0x3D0 u8, +0x40C u32, +0x410 u32, +0x440 u32, +0x44C u8, +0x44D u8, +0x44E u8, +0x454 f32, +0x458 f32, +0x460 f32.

### 0x4F PlatformGrappleCatapult

Parent 0x4D PlatformGrappleBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x470..+0x4B0; 14 placed in the game (8 levels); e.g. `wrench_catapult_arm#`, `CATAPULT#`, `moon#b_catapult#_arm`; own methods: fix-up 0x00375A84, Init 0x0037610C, Update 0x00375948.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x468 | reference | Volume, set on 14 of 14: `volume#`, `VOL_CATAPULT#_TARGET`, `moon#b_catapult#_arm_vol` | Target volume | E D |
| +0x46C | reference | Path, set on 14 of 14: `curve#`, `CATAPULT#_PATH`, `moon#c_catapult#_curve` | Flight path | E D |
| +0x470 | f32 | 20 x8, 25 x2, 15 x1, 50 x1, 10 x1 ... (6 values) | Flight tuning: +0x470 20, +0x474 5, +0x478 5 by default | D |
| +0x474 | f32 | 5 x8, 10 x2, 12 x1, 20 x1, 8 x1 ... (6 values) | | |
| +0x478 | f32 | 5 x13, 2.5 x1 | | |
| +0x48C | reference | Controller, set on 1 of 14: `catapult_cam` | Camera (one instance) | E D |
| +0x498 | 4 bytes | 0x00000100 x8, 0x01010100 x3, 0x01000100 x1, 0x00000000 x1, 0x01010000 x1 | | |
| +0x49C | 4 bytes | 0x00000000 x5, 0x01000000 x5, 0x00010000 x4 | | |

Same on every instance: +0x47C = 20, +0x490 = 2, +0x494 = 3.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x480 f32, +0x484 f32, +0x488 f32, +0x4A0 f32.

### 0xD7 PickupBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xE0; not placed itself; values below are over its 430 placed descendants; own methods: Init 0x004B9074, Update 0x004B97D4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x64 | f32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0x68 | u32 | 5 x429, 0 x1 | | |
| +0x6C | f32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0x70 | f32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0x74 | u32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0x78 | u32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0x90 | u32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0x94 | u32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0xA0 | f32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0xA4 | f32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0xA8 | f32 | 0 on all 430 | Script: written by `reset_flares` | E S |
| +0xAC | f32 | 0 on all 430 | Script: written by `reset_flares` | E S |

Same on every instance: +0x5C = 4096, +0x60 = 16.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x8C u32, +0xC0 f32, +0xC4 f32, +0xC8 f32.

### 0xD8 PickupSpecialBase

Parent 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xE0..+0xF0; not placed itself; values below are over its 69 placed descendants; own methods: fix-up 0x004BF030, Init 0x004BEC34.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xE0 | reference | Controller, set on 47 of 69: `goldbolt#cam`, `CamArgFixed#`, `goldboltcam#` | Reveal camera | E D |
| +0xE4 | reference | Volume, set on 47 of 69: `goldbolt#vol`, `volume#`, `VOL_GOLDBOLT#` | Volume the camera is triggered from | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xE8 u8.

### 0x15C SimpleAnimated

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 366 placed in the game (14 levels); e.g. `yy_moon_light#`, `tj_val_propeller#`, `a_ice_moon#`; own methods: Init 0x003A563C, Update 0x003A5608.

Moon lights, the ice moons' animated parts.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x6C | u32 | 0 x329, 3 x21, 43 x13, 31 x2, 8 x1 | Animation-like index (0 by default) | D |
| +0x70 | u32 | 1 x312, 0 x39, 5 x4, 3 x3, 6 x2 ... (9 values) | | |
| +0x74 | u32 | 1 x281, 0 x85 | | |
| +0x78 | u32 | 1 x334, 0 x32 | | |
| +0x7C | 4 bytes | 0x00000000 x193, 0x01000000 x173 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x80 u32.

### 0x201 PlatformBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x120; 119 placed in the game (6 levels); e.g. `moon#d_floatingplat_#m#`, `arena_platform_square_#`, `arena_platform_triangle_#`; own methods: Init 0x00367624, Update 0x003675DC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x118 | 4 bytes | 0x00000100 x744, 0x00000000 x35, 0x00010000 x14, 0x00010100 x7, 0x01010100 x4 | | |
| +0x11C | f32 | 0 x746, 180 x20, 200 x10, -200 x6, 25 x5 ... (17 values) | Speed: Script: written by `gear_hazard_set_speed` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xC0 u32, +0xC4 u32, +0xC8 u32, +0xD0 u32, +0xD4 u32, +0xD8 u32, +0xDC u32, +0xE0 f32, +0xE4 u32, +0xF4 u32, +0x116 u8.

### 0x210 PickupGoldBolt

Parent 0xD8 PickupSpecialBase > 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xF0..+0x110; 41 placed in the game (16 levels); e.g. `goldbolt#`, `moon#a_goldbolt`, `moon#x_reward`; own methods: Init 0x004BD430.

A SpaceMoon finds its gold bolt by this class.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x104 | f32 | 3 x29, 2 x9, 4 x1, 1 x1, 0.1 x1 | 3 by default | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xF0 u32, +0xFC u32, +0x100 u32, +0x108 u32.

### 0x220 VendorBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x2C0; not placed itself; values below are over its 43 placed descendants; own methods: fix-up 0x003851B4, Init 0x00385204.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x1C0 | component |  | Prompt component (+0x1C0..+0x257), see *Prompt component* | E |
| +0x218 | u32 text id | 442 `GEN_TRIPROMPT_GPLAY_TELEPORT` on all 43 | Prompt text | E T |
| +0x21C | reference | Volume, set on 1 of 43: `volume#` | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0x224 | f32 | 3 x42, 2 x1 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0x238 | u32 | 2 on all 43 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0x248 | f32 | -1 on all 43 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0x254 | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0x255 | run time u8 |  | Prompt enabled | E |
| +0x256 | run time u8 |  | Prompt showing | E |
| +0x257 | run time u8 |  | Prompt button state | E |
| +0x270 | reference | Volume, set on 3 of 43: `armorVendorExitVol`, `vol_armor_vendor`, `ArmorExitVol` | Exit volume | E D |

Same on every instance: +0xC0 = 1, +0xC4 = 1, +0xC8 = 1, +0x258 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xB0 u32, +0xB4 u8, +0xCC u32, +0xD4 u32, +0xD8 u32, +0xFC f32, +0x100 f32, +0x104 f32, +0x1B0 u32, +0x25C u32, +0x268 u32, +0x26C u32, +0x280 f32, +0x284 f32, +0x288 f32, +0x28C f32, +0x290 f32, +0x294 f32, +0x298 f32, +0x29C f32, +0x2A0 f32, +0x2A4 f32, +0x2A8 u32, +0x2AC f32, +0x2B0 u32, +0x2B4 u8, +0x2B5 u8.

### 0x221 VendorArmor

Parent 0x220 VendorBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x2C0..+0x2D0; 12 placed in the game (11 levels); e.g. `armorVendor#`, `ss#_armor_vendor`, `ARMOR_VENDOR`; own methods: fix-up 0x00384508, Init 0x0038455C.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x2B8 | reference | Controller, set on 12 of 12: `ss#_junkdepot_armor_cam`, `armor_pad_camera`, `CamArgFixed#` | Camera | E D |
| +0x2BC | reference | Moby, set on 12 of 12: `armor_pad#`, `ss#_armor_pad`, `ARMOR_PAD` | The armor pad (class 0x706) | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x2C0 u32, +0x2C4 u8, +0x2C8 u32, +0x2CC u8.

### 0x245 ShotTargetBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xA0; not placed itself; values below are over its 78 placed descendants; own methods: fix-up 0x0037C788, Init 0x0037C7E0, Update 0x0037CA80.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | f32 | -1 x66, 30 x3, 20 x2, 15 x2, 25 x2 ... (8 values) | Range (-1 = default) | D |
| +0x74 | reference | Area, set on 2 of 78: `AREA_DISALLOW_SWINGSHOT_TARGET#`, `BOSS_NO_SWING_AREA` | Area where the target cannot be used | E D |
| +0x78 | reference | Area, set on 4 of 78: `FUEL_SHIP#_STICK_AREA`, `force_hang_tow_to_boss`, `force_hang_escape_boss` | Reference slot: resolved by the class's fix-up | E |
| +0x7C | reference | Moby, set on 5 of 78: `wrench_platform_mag_twoaxis#`, `AZI_SHIP_PICKUP`, `AZI_SHIP_ESCAPE` | Moby the target is attached to | E D |
| +0x80 | s32 | -1 x76, 14 x2 | | |
| +0x88 | 4 bytes | 0x00000000 x67, 0x01000000 x11 | | |
| +0x94 | f32 | 8 x41, 0 x16, 6 x5, 11 x4, 7 x4 ... (10 values) | 8 by default | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x84 f32, +0x8C u32, +0x90 u32.

### 0x246 ShotTargetSwing

Parent 0x245 ShotTargetBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xA0..+0xA0; 58 placed in the game (10 levels); e.g. `swingshotTargetGreen#`, `moon#a_platform_#m#`, `swingshotGreen#`.

No instance sets anything in the class's own range.

### 0x290 stationary laser

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x100; 250 placed in the game (axiom_city, galacton_ship, insomniac_museum); e.g. `laser_stationary#`, `mellis_laser#`, `udown_room_hurdle_#`; own methods: fix-up 0x00161E64, Init 0x00162014, Update 0x00161EAC.

Laser beams, fixed or moving (the Great Clock's laser snakes and fans). Init 0x00162014.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | run time u16 |  | State (Init sets 1, then 0 or 4 by the motion mode); +0x5E the previous state, +0x60 / +0x64 its timing | E |
| +0x6C | f32 | 0 x232, 1.5 x5, 3 x5, 0.5 x4, 1 x2 ... (7 values) | First part of the cycle, seconds; Init adds +0x78 to it for the cycle length (doubled when the laser travels) | E |
| +0x74 | u32 | 0 x177, 1 x71, 2 x2 | Motion: 0 fixed, 1 travels between the start and end volumes, 2 the third mode (takes its own pose as the base) | E |
| +0x78 | f32 | 2 x136, 0 x81, 4 x11, 0.3 x5, 1 x5 ... (12 values) | Second part of the cycle, seconds | E |
| +0x7C | f32 | 0 x205, 1 x6, 3 x4, 0.6 x3, 0.4 x3 ... (21 values) | | |
| +0x80 | f32 | -1 x191, 0 x39, 1 x9, 3 x6, 1.5 x5 | With +0x84: two durations Init adds together (-1 on most) | E |
| +0x84 | f32 | -1 x191, 0 x39, 3 x12, 1.5 x5, 1 x3 | | |
| +0x88 | reference | Volume, set on 77 of 250: `lasersnake_right_start_vol#`, `lasersnake_left_start_vol#`, `lasercol_trip#_longhall_laser_fan_start_#` | Start volume of a travelling laser | E D |
| +0x8C | reference | Volume, set on 77 of 250: `lasersnake_right_end_vol#`, `lasersnake_left_end_vol#`, `lasercol_trip#_longhall_laser_fan_end_#` | End volume of a travelling laser | E D |
| +0x90 | 4 bytes | 0x00000000 x182, 0x00010000 x32, 0x01000000 x20, 0x01010000 x16 | | |
| +0x94 | run time f32 |  | Cycle length; +0x98 the second sum; +0xA0.. the start and end poses | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 u8, +0xF4 u32.

### 0x291 BreakableCrateBase

Parent 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x250..+0x590; not placed itself; values below are over its 4932 placed descendants; own methods: Init 0x004B674C, Update 0x004B65BC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x580 | u32 | 0 x4880, 1 x52 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x250 f32, +0x254 f32, +0x258 f32, +0x25C f32, +0x260 u32, +0x264 u32, +0x268 u32, +0x2F0 u8, +0x380 u8, +0x410 u8, +0x4A0 u8, +0x530 u8, +0x540 u8, +0x550 u32, +0x554 f32, +0x558 f32, +0x55C f32, +0x560 u32, +0x570 f32, +0x574 f32, +0x578 f32, +0x57C f32, +0x584 f32.

### 0x292 BreakableBombCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x5C0; 312 placed in the game (15 levels); e.g. `explosive_crate#`, `Camo_Crate#`, `z_moon#a_mods_#`; own methods: Init 0x004B5100.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x590 f32, +0x594 u16, +0x5A0 f32, +0x5A4 f32, +0x5A8 f32, +0x5AC f32, +0x5B0 f32, +0x5B4 u32.

### 0x2B6 PadBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xC0; not placed itself; values below are over its 447 placed descendants; own methods: Init 0x00362FBC, Update 0x00362F90.

Same on every instance: +0xA4 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x60 u32, +0x64 u32, +0x68 u32, +0x6C u32, +0x70 u32, +0x80 u32, +0xA2 u8.

### 0x2C5 ShotTargetHook

Parent 0x245 ShotTargetBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xA0..+0xA0; 16 placed in the game (8 levels); e.g. `swingshotTargetBlue#`, `moon#b_hookshot`, `moon#e_swingshot_#m#`.

No instance sets anything in the class's own range.

### 0x2D4 BreakableMetalCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 131 placed in the game (11 levels); e.g. `indestructible_crate#`; own methods: Init 0x004B7420.

No instance sets anything in the class's own range.

### 0x2E7 BreakableAmmoCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 626 placed in the game (15 levels); e.g. `ammo_crate#`, `g#ammo_crate#`, `g#breakable_crate#`; own methods: Init 0x004B4DDC.

No instance sets anything in the class's own range.

### 0x2EC machine burner

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 113 placed in the game (agorian_arena, insomniac_museum, molonoth, space_sector_2); e.g. `dc_mf_machine_burner#`, `dc_mf_machine_burner_flat#`, `arena_floor_burner_#`; own methods: Init 0x001664D0, Update 0x0016655C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | f32 | 2 x80, 100 x14, 4 x10, 1 x9 | Timing: +0x70 2, +0x74 3, +0x78 0 by default | D |
| +0x74 | f32 | 3 x59, 0 x45, 4 x9 | | |
| +0x78 | f32 | 0 x104, 2 x6, 1 x3 | | |
| +0x82 | u8 | 0 on all 113 | Script: written by `flame_jet_on`, `flame_jet_off`, `flame_jet_restart` | E S |
| +0x83 | u8 | 0 on all 113 | Script: written by `flame_jet_on`, `flame_jet_off`, `flame_jet_restart` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x7C f32.

### 0x384 PlatformBoltCrank

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 6 placed in the game (5 levels); e.g. `t#_bolt_crank_plat`, `moon#a_bolt_platform`, `moon#a_boltcrank#_platform`; own methods: fix-up 0x00367980, Init 0x003679C0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | reference | Moby, set on 6 of 6: `moon#a_bolt_crank`, `azimuth_bridge_winch`, `rot_bridge_crank` | The bolt crank that drives the platform | E D |

### 0x3A7 PromptTeleporter

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x140; 65 placed in the game (11 levels); e.g. `moon#a_#b_teleporter`, `moon#b_teleporter_#a`, `moon#b_teleporter_#b`; own methods: fix-up 0x003A4864, Init 0x003A5240.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x78 | reference | Volume, set on 64 of 65: `moon#a_#a_volume`, `moon#b_teleporter_#b_vol`, `moon#b_teleporter_#a_vol` | Destination volume | E D |
| +0x80 | component |  | Prompt component (+0x80..+0x117), see *Prompt component* | E |
| +0xD8 | u32 text id | 442 `GEN_TRIPROMPT_GPLAY_TELEPORT` x58, 1099 `GEN_PROMPT_GPLAY_RETURN_APHELION` x5, 2208 `GEN_PROMPT_GPLAY_TELE_PLYX` x1 ... (4 values) | Prompt text (442 default; GEN_PROMPT_GPLAY_RETURN_APHELION 1099 on the pads back to the ship) | E T |
| +0xDC | reference | unset on all 65 | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0xE4 | f32 | 3 x51, 1 x14 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0xF8 | u32 | 2 on all 65 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0x108 | f32 | -1 on all 65 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0x114 | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0x115 | run time u8 |  | Prompt enabled | E |
| +0x116 | run time u8 |  | Prompt showing | E |
| +0x117 | run time u8 |  | Prompt button state | E |
| +0x138 | u8 | 0 on all 65 | Script: written by `has_teleported` | E S |

Same on every instance: +0x118 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x11C u32, +0x128 u32, +0x12C u32.

### 0x3B0 PadFling

Parent 0x2B6 PadBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xC0..+0xE8; 27 placed in the game (6 levels); e.g. `moon#c_flingpad#`, `moon#d_flingpad#`, `clank_jump_pad#`; own methods: fix-up 0x003633E8, Init 0x00363390.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xBC | u32 text id | 0 x22, 1274 `GC2_PROMPT_GPLAY_USEJUMPPADCLANK` x5 | Prompt text (GC2_PROMPT_GPLAY_USEJUMPPADCLANK on Clank's pads, 0 elsewhere) | T D |
| +0xC0 | reference | Volume, set on 27 of 27: `moon#e_flingpad_vol_#`, `moon#d_flingpad#_vol`, `volume#` | Trigger volume | E D |
| +0xC4 | reference | Path, set on 27 of 27: `moon#e_flingpad_curve_#`, `moon#d_flingpad#_curve`, `curve#` | Flight path | E D |
| +0xC8 | reference | Controller, set on 5 of 27: `clank_jump_pad_cam#`, `LAUNCHPAD#_LAND_CAM`, `CHAMBER_START_CAM` | Camera during the flight | E D |
| +0xCC | f32 | 20 x22, 24 x3, 35 x2 | Flight tuning: +0xCC 20, +0xD0 5, +0xDC 2, +0xE0 3 by default | D |
| +0xD0 | f32 | 5 x22, 12 x3, 10 x2 | | |
| +0xDC | f32 | 2 x22, 0.8 x3, 1 x2 | | |
| +0xE0 | f32 | 3 x22, 20 x3, 10 x2 | | |

Same on every instance: +0xD4 = 5, +0xD8 = 20, +0xE4 = 0x00010000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xE4 u8.

### 0x3B1 SpawnerBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x90; 36 placed in the game (galacton_ship, nefarious_station, space_sector_4, space_sector_5); e.g. `monster_closet_teleportation_pad#`, `moon#b_ap#_teleport_pad#`; own methods: fix-up 0x0037D984, Init 0x0037D9BC.

The monster closet teleport pads. No instance sets anything.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x88 | reference | unset on all 36 | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32.

### 0x3B2 VendorWeapon

Parent 0x220 VendorBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x2C0..+0x2C0; 31 placed in the game (16 levels); e.g. `weapon_vendor#`, `GCE_WEAPON_VENDOR`, `weapon_vendor_outside`; own methods: Init 0x003863E0.

No instance sets anything in the class's own range.

### 0x3ED PlatformMoving

Parent 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x120..+0x1B0; 257 placed in the game (8 levels); e.g. `moon#e_floatingplatform_#m#`, `moon#c_floatingplatform_#m#`, `moon#d_floatingplat_#m#`; own methods: fix-up 0x0036AF18, Init 0x0036B318, Update 0x0036AA9C.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x180 | reference | Volume, set on 2 of 323: `VOL_OIL_PLATFORM`, `VOL_MOVING_PLAT_END` | End volume (rare) | E D |
| +0x184 | reference | Volume, set on 2 of 323: `volume#`, `VOL_MOVING_PLAT_START` | Start volume (rare) | E D |
| +0x188 | reference | Path, set on 68 of 323: `curve#`, `moon#c_movingplatform_#m#_curve`, `moon#b_#m_moving_plat#_curve` | Path the platform moves along | E D |
| +0x198 | f32 | 2 x260, 6 x16, 5 x16, 8 x11, 4 x9 ... (9 values) | Speed-like value (2 by default) | D |
| +0x19C | f32 | 5 x257, 2 x44, 0 x10, 1 x8, 6 x2 ... (6 values) | Wait-like value (5 by default) | D |
| +0x1A0 | 4 bytes | 0x01000100 x256, 0x00000100 x35, 0x00000000 x16, 0x00020100 x15, 0x01010100 x1 | | |
| +0x1A4 | 4 bytes | 0x00000000 x310, 0x01000000 x13 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x120 f32, +0x124 f32, +0x128 u16, +0x130 f32, +0x134 f32, +0x138 f32, +0x13C f32, +0x170 f32, +0x174 f32, +0x178 f32, +0x17C f32, +0x190 u32, +0x1A8 u32, +0x1AE u8, +0x1AF u8.

### 0x404 SimpleRotated

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xB0; 632 placed in the game (11 levels); e.g. `sr_great_clock_gear_#_#`, `tb_gc_a_gear_#_#`, `tb_gc_a_wallgear#_#`; own methods: Init 0x003A6354.

Turns a Moby, or one of its joints, about one axis: fans, gears, radar dishes. Init 0x003A6354, Update 0x003A64C4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 1 x310, 2 x182, 0 x140 | Axis: 0, 1 or 2 (three rotation builders, 0x0062B758 / 0x0062B6A4 / 0x0062B5F0) | E |
| +0x60 | s32 | -1 x570, 1 x62 | Joint to turn; -1 = the whole Moby. Init resets it to -1 when the model has no such joint | E |
| +0x64 | f32 | 10 x246, 15 x73, 28 x57, 18 x44, 5 x42 ... (35 values) | Speed, degrees per second | E |
| +0x68 | f32 | 0 x606, 180 x18, 6 x4, 10 x2, -90 x1 ... (6 values) | Swing angle, degrees: when > 0 the part swings back and forth by this much; 0 = turns all the way round | E |
| +0x6C | f32 | 0 x606, 5 x6, 4 x6, 2.5 x6, 2 x4 ... (9 values) | Pause at each end of a swing, seconds | E |
| +0x70 | f32 | 0 x618, 1 x4, 0.5 x3, 2 x2, 3 x1 ... (9 values) | Time offset, seconds: shifts this instance against the others | E |
| +0x74 | u8 | 0 x398, 1 x234 | Keeps turning when not visible (otherwise only while the Moby is drawn) | E |
| +0x78 | run time f32 |  | Position in the cycle; +0x7C the cycle length, +0x80 the rest pose | E |

### 0x425 BoltCrankBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3D0; 6 placed in the game (5 levels); e.g. `moon#a_bolt_crank`, `azimuth_bridge_winch`, `rot_bridge_crank`; own methods: Init 0x00353EE0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x37C | 4 bytes | 0x00000000 x17, 0xE4000000 x10, 0xD8000000 x7, 0xDC000000 x4, 0xAC000000 x3 ... (6 values) | | |
| +0x380 | f32 | 0 on all 43 | Script: written by `crank_reset` | E S |
| +0x384 | f32 | 0 on all 43 | Script: written by `crank_reset` | E S |
| +0x388 | f32 | 0 on all 43 | Script: written by `crank_reset` | E S |
| +0x38C | f32 | 0 on all 43 | Script: written by `crank_reset` | E S |
| +0x390 | f32 | 5 x20, 3 x13, 2 x7, 4 x2, 20 x1 | | |
| +0x394 | u32 | 0 on all 43 | Script: written by `crank_reset` | E S |
| +0x398 | f32 | 5 x38, 3 x4, 10 x1 | | |
| +0x39C | f32 | 0 x35, 3 x6, 1 x1, 5 x1 | | |
| +0x3A4 | u32 | 0 on all 43 | Script: written by `crank_reset` | E S |
| +0x3B8 | u32 | 0 x38, 1 x5 | | |
| +0x3C0 | u32 | 0 x39, 1 x4 | | |
| +0x3C4 | u32 | 0 x42, 1 x1 | | |
| +0x3C8 | u8 | 0 on all 43 | Script: written by `crank_reset` | E S |
| +0x3C9 | u8 | 0 on all 43 | Script: written by `crank_reset` | E S |
| +0x3CA | u8 | 0 on all 43 | Script: written by `crank_reset` | E S |
| +0x3CC | 4 bytes | 0x00140000 x9, 0x00D00000 x4, 0x00D40000 x4, 0x00840000 x4, 0x00000000 x3 ... (20 values) | | |

Same on every instance: +0x360 = 2, +0x370 = -1, +0x3B0 = -1, +0x3B4 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 u32, +0x74 u32, +0x78 u32, +0x100 u8, +0x190 u8, +0x220 u8, +0x2B0 u8, +0x340 u8, +0x350 u8, +0x378 f32, +0x3A0 f32, +0x3A8 u32, +0x3AC f32, +0x3BC u32.

### 0x43B BreakableBoltCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 3164 placed in the game (21 levels); e.g. `breakable_crate#`, `morph_crate#`, `Camo_Crate#`; own methods: Init 0x004B4EB4.

No instance sets anything in the class's own range.

### 0x46B BreakableHealthCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 321 placed in the game (20 levels); e.g. `health_crate#`, `CONDITIONAL_HEALTH_#`, `g#health_crate#`; own methods: Init 0x004B6DF8.

No instance sets anything in the class's own range.

### 0x470 PlatformGrappleSpring

Parent 0x4D PlatformGrappleBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x470..+0x4A0; 13 placed in the game (space_sector_2, space_sector_3, space_sector_4); e.g. `wrench_spring_pad#`, `moon#b_springpad`; own methods: Init 0x0037A34C, Update 0x00379F60.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x488 | f32 | 10 x3, 35 x3, 16 x2, 25 x1, 20 x1 ... (8 values) | Launch strength (10-35) | D |
| +0x48C | 4 bytes | 0x01000100 x8, 0x00000100 x4, 0x01010100 x1 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x478 u32, +0x47C f32, +0x480 f32, +0x484 f32, +0x490 f32.

### 0x548 Door

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x110; 1 placed in the game (gimlick_valley); e.g. `FONGOID_DOOR_PRESENT`; own methods: fix-up 0x0035AD14, Init 0x0035AE08, Update 0x0035B814.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | reference | unset on all 403 | Reference slot: resolved by the class's fix-up | E |
| +0xD0 | f32 | 0 x329, -1.65 x70, -10 x2, 1 x2 | | |
| +0xE0 | f32 | 0 x373, 15 x30 | | |
| +0xE4 | f32 | 0 x373, 18 x30 | | |
| +0xE8 | reference | Volume, set on 100 of 403: `door_open_hall_east#`, `auto_door_#_open_vol`, `build_elevator_#` | Open volume | E D |
| +0xEC | reference | Volume, set on 103 of 403: `door_close_hall_east#`, `auto_door_#_close_vol`, `elevator_doors_close` | Close volume | E D |
| +0xF0 | reference | Pod, set on 45 of 403: `gcc_subcon_puzzle#_p#_cham#_button`, `GCBpuzzle#buttonpod#`, `gcb_subcon_puzzle#_button_chamber#` | Pod of the buttons that open it | E D |
| +0xF4 | reference | Volume, set on 8 of 403: `PUZZLE#_FDOOR_SQUISH_VOL`, `vol_subcon_puzzle_exit_squish_vol`, `PUZZLE#_SQUASH_VOL` | Reference slot: resolved by the class's fix-up | E |
| +0xFC | f32 | 0 on all 403 | Script: written by `door_force_open`, `door_force_closed`, `door_clear_force` | E S |
| +0x101 | u8 | 0 on all 403 | Script: written by `door_force_open`, `door_force_closed`, `door_clear_force` | E S |
| +0x108 | f32 | 0 x230, 1 x154, 7 x9, 8 x3, 0.5 x3 ... (9 values) | Speed-like value (1 by default) | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x68 u16, +0x80 f32, +0x84 f32, +0x88 f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xC0 f32, +0xC4 u8, +0xC5 u8, +0xF8 u32, +0x104 u8, +0x105 u8.

### 0x54A DoorAnimated

Parent 0x548 Door > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x110..+0x110; 143 placed in the game (17 levels); e.g. `door_hall_east#`, `sr_great_clock_pressure_button_chamber_doors#`, `kb_great_clock_plain_door#`; own methods: Init 0x0035C130.

The bunker doors, opened by the moon scripts (door_open).

No instance sets anything in the class's own range.

### 0x569 Damageable

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x200; not placed itself; values below are over its 8272 placed descendants; own methods: fix-up 0x004B2B10, Init 0x004B24A4, Update 0x004B2660.

Base of everything breakable. GetInterface (0x004B2418) hands out its embedded components by class number: 0x4BD SEGMENT_Spawn at +0x5C, 0x137 ECON_Reward at +0x60, 0x51 Health at +0x84, 0x113 ColorFlash at +0x124.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 0 x5343, 3 x1654, 1 x1236, 5 x18, 4 x18 ... (6 values) | SEGMENT_Spawn rule, 0-7 (see *Segments and the spawn rule*): 0 always there, 1 first visit only, 3 stays gone once broken | E T |
| +0x60 | component |  | ECON_Reward (+0x60..+0x83): what breaking it pays. Filled by each class's Init (0x002983F8), not by the instance | E T |
| +0x1B4 | reference | unset on all 8272 | Reference slot: resolved by the class's fix-up | E |
| +0x1B8 | reference | unset on all 8272 | Reference slot: resolved by the class's fix-up | E |
| +0x1BC | 4 bytes | 0x01000000 x8236, 0x00000000 x36 | | |
| +0x1C0 | f32 | 0 x8179, 2 x93 | | |
| +0x1C4 | f32 | 3 x8271, 0 x1 | | |
| +0x1C8 | f32 | 3 x8271, 1 x1 | | |
| +0x1E0 | u32 | 0 x7966, 2 x282, 7 x19, 1 x4, 6 x1 | Index into a table at 0x00D374B4, read by Init next to the health; Init sets 6 in one case | E |
| +0x1E4 | f32 | 1 x8003, 100 x52, 1000 x38, 75 x33, 500 x25 ... (24 values) | Health: given to the Health component by Init (0x0008D3C8). 1 on nearly everything | E |
| +0x1F0 | 4 bytes | 0x00000000 x7134, 0x00010000 x649, 0x00000001 x374, 0x01000000 x72, 0x00010001 x41 ... (6 values) | | |
| +0x1F4 | 4 bytes | 0x00000000 x6181, 0x01000000 x1969, 0x00000100 x93, 0x00010000 x26, 0x01010000 x2 ... (6 values) | | |
| +0x1F8 | f32 | 0 x8039, 0.8 x141, 0.5 x41, 0.85 x29, 0.6 x22 | | |
| +0x1FC | u16 | 0 on all 8272 | Script: read by `fixable_is_fixed`, `fixable_is_broken` | E S |

Same on every instance: +0xA0 = 1, +0xA4 = 1, +0xA8 = 1, +0x1B0 = 1, +0x1CC = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x8C u32, +0xAC u32, +0xB0 u32, +0xB4 u32, +0xB8 u32, +0xDC f32, +0xE0 f32, +0xE4 f32, +0x190 u32, +0x1D0 u32, +0x1D4 f32, +0x1D8 f32, +0x1DC u8, +0x1DD u8, +0x1E8 u32, +0x1EC f32.

### 0x56A Breakable

Parent 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x200..+0x250; 2874 placed in the game (19 levels); e.g. `yy_moon_breakable_#_#`, `tv_mf_breakable_plantb#`, `td_val_mushroomgroup_breakable#`; own methods: Init 0x004AFC68, Update 0x004AFF18.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x208 | u32 | 0 x5704, 1 x2225 | 0 / 1: with 1 the break impulse below is x1.5 for some hits (0x004B0540) | E |
| +0x20C | u32 | 0 x7494, 1 x435 | | |
| +0x230 | f32 | 1 x5450, 3 x857, 10 x578, 4 x434, 5 x361 ... (14 values) | Break impulse: how hard the pieces fly (0x004B0540) | E |
| +0x234 | f32 | -1 x6241, 3 x1186, 5 x310, 2 x96, 0.5 x38 ... (9 values) | Seconds given to the piece effect; negative = 10 (0x004B092C) | E |
| +0x23C | 4 bytes | 0x00000100 x6033, 0x01000100 x1719, 0x01000101 x107, 0x01000000 x58, 0x00010100 x6 ... (8 values) | | |
| +0x240 | 4 bytes | 0x00000000 x7264, 0x00010000 x665 | | |
| +0x244 | u32 | 0 x7928, 1 x1 | | |
| +0x24C | u32 | 0 x7920, 2 x9 | | |

Same on every instance: +0x238 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x210 u32, +0x218 u32, +0x21C f32, +0x220 u32, +0x224 u32, +0x22C u32.

### 0x56E Destructible

Parent 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x250..+0x270; not placed itself; values below are over its 9 placed descendants; own methods: Init 0x004B36B4.

Same on every instance: +0x268 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x26C u8, +0x26D u8, +0x26E u8, +0x26F u8.

### 0x570 hydro plant

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3E0; 12 placed in the game (gimlick_valley, krell_canyon, space_sector_5, tombli); e.g. `hydro_plant#`, `test_hydroplant#`, `KrlFongoidHydroPlant_#`; own methods: Init 0x00429F6C, Update 0x0042A480.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 0 x8, 3 x3, 1 x1 | | |
| +0x370 | f32 | 30 x8, 25 x2, 15 x1, 0 x1 | | |
| +0x3C8 | f32 | 8 x10, 10 x1, 11 x1 | | |
| +0x3D8 | u32 | 0 x8, 1 x4 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 u16, +0x62 u16, +0x64 f32, +0x68 f32, +0x6C u16, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0x84 u32, +0x88 u32, +0x110 u8, +0x1A0 u8, +0x230 u8, +0x2C0 u8, +0x350 u8, +0x360 u8, +0x374 u32, +0x378 f32, +0x37E u16, +0x380 u32, +0x384 u32, +0x388 u32, +0x38C u32, +0x390 f32, +0x394 u32, +0x3A4 f32, +0x3C5 u8, +0x3C6 u8, +0x3CC u32, +0x3D0 u32, +0x3D4 f32.

### 0x577 hoverboot ramp / kick pad

Parent 0x2B6 PadBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xC0..+0x120; 396 placed in the game (14 levels); e.g. `dm_hovboot_arrow_ramp#`, `dm_hovboot_arrow_holo_ground#`, `wall_kick_pad#`; own methods: fix-up 0x00363950, Init 0x00363BBC, Update 0x00363F80.

Also what a moon hill spawns around itself. Init 0x00363BBC: when the pad has a destination pad, the flight path's first point is moved onto this pad and its last point onto the destination, so the path's ends in the file do not matter.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC8 | reference | Path, set on 52 of 396: `curve#`, `moon#a_kickpad#_curve`, `CURVE_KICKPAD_#` | Flight path; Init snaps its ends to the two pads | E D |
| +0xCC | reference | Volume, set on 143 of 396: `vol_ring_bn#_ramp#`, `volume#`, `HOVERBOOT_JUMP_VOL#` | Trigger volume | E D |
| +0xD0 | reference | Controller, set on 52 of 396: `CamArgFixed#`, `kickpad#cam`, `moon#a_kickpad#_cam` | Camera | E D |
| +0xD4 | reference | Controller, set on 1 of 396: `moon#a_kickpad#_fake_cam` | Second camera (one instance) | E D |
| +0xD8 | reference | Moby, set on 33 of 396: `wall_kick_pad#`, `ghost_pad_#`, `moon#a_kickpad#` | Destination pad (another pad of this class) | E D |
| +0xDC | reference | unset on all 396 | Reference slot: resolved by the class's fix-up. Script: written by `set_jump_duncan_hack` | E S |
| +0xE0 | f32 | 35 x117, 40 x46, 20 x42, 25 x39, 18 x30 ... (32 values) | Launch tuning (+0xE0..+0xEC): 35, 2.5, 0.3, 1 by default | D |
| +0xE4 | f32 | 2.5 x143, 0.25 x65, 2 x57, 1.5 x41, 0.5 x32 ... (13 values) | | |
| +0xE8 | f32 | 0.3 x324, 0.6 x34, 0.8 x12, 1 x10, 0.45 x7 ... (8 values) | | |
| +0xEC | f32 | 1 x362, 0.25 x19, 0.5 x10, 2 x3, 0 x2 | | |
| +0xF0 | f32 | -25 x268, -20 x38, -15 x17, -22 x13, -18 x12 ... (21 values) | First of two angles (-25 by default) | E D |
| +0xF4 | f32 | -25 x294, -15 x20, -20 x20, -22 x13, -27 x8 ... (19 values) | Second angle; Init copies the first over it when it holds the "unset" value | E |
| +0xF8 | f32 | 0 on all 396 | Jump duncan hack: Script: written by `set_jump_duncan_hack` | E S |
| +0xFC | f32 | 0 on all 396 | Jump duncan hack: Script: written by `set_jump_duncan_hack` | E S |
| +0x100 | run time |  | Zeroed by Init (+0x100, +0x104) | E |
| +0x108 | u32 | 0 x380, 2 x10, 4 x6 | Mode: set by Init from the flags below (5; or 2 / 4), otherwise as authored | E |
| +0x10C | u32 | 0 x384, 2 x7, 3 x5 | Second mode: 2 or 3 from the flags | E |
| +0x111 | u8 | 0 x344, 1 x52 | Flag: lets +0x112 and +0x113 pick the two modes | E |
| +0x112 | u8 | 0 x373, 1 x23 | Flag: mode 4 instead of 2 | E |
| +0x113 | u8 | 0 x375, 1 x21 | Flag: second mode 3 instead of 2, and only this pad's end of the path is snapped | E |
| +0x114 | u8 | 0 x335, 1 x61 | Flag: mode 5 | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xC0 u32, +0xC4 u32, +0x118 u32.

### 0x591 GenericPrompt

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x160; 20 placed in the game (9 levels); e.g. `statuePlaque#`, `ss#_starcrossed_fakeprompt#`, `ss#_mission_tow_fakeprompt`; own methods: fix-up 0x00424028, Init 0x00424054.

An invisible prompt (the missions' "fake prompts"). Init 0x00424054.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | component |  | Prompt component (+0x60..+0xF7), see *Prompt component* | E |
| +0xB8 | u32 text id | 1105 `SS0_PROMPT_GPLAY_HAIL_SHIP` x6, 442 `GEN_TRIPROMPT_GPLAY_TELEPORT` x5, 1343 `GEN_PROMPT_GPLAY_LAND` x2 ... (10 values) | Prompt text (SS0_PROMPT_GPLAY_HAIL_SHIP on the mission prompts) | E T |
| +0xBC | reference | Volume, set on 15 of 20: `plaqueVols#`, `ss#_starcrossed_fakeprompt#_vol`, `FREAKY_WARP_THINGIE_GOES_HERE` | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0xC4 | f32 | 3 x13, 0 x5, 1 x1, 5 x1 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0xD8 | u32 | 2 x19, 0 x1 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0xE8 | f32 | -1 x19, 10 x1 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0xF4 | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0xF5 | run time u8 |  | Prompt enabled | E |
| +0xF6 | run time u8 |  | Prompt showing | E |
| +0xF7 | run time u8 |  | Prompt button state | E |
| +0x148 | u32 | 0 x18, 231 x1, 229 x1 | 0; 229 / 231 on two Great Clock prompts | D |
| +0x154 | f32 | 10 x18, 5 x2 | 10 (5 on two) | D |
| +0x15C | u8 | 1 x17, 0 x3 | 1 on most | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xF8 u32, +0xFC u32, +0x104 u32, +0x140 u32, +0x144 f32, +0x14C u32, +0x150 u32, +0x158 u8, +0x159 u8, +0x15A u8, +0x15B u8.

### 0x5A0 PlatformGrappleAutoSpring

own fields +0x4..+0x4A0; 45 placed in the game (7 levels); e.g. `spring_pad#`, `moon#d_springpad#`, `fountain_springpad`; own methods: fix-up 0x00374988.

The registry gives this class no parent.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x58 | 4 bytes | 0x00000000 x44, 0x00010000 x1 | | |
| +0x44C | 4 bytes | 0x00000000 x44, 0x01000000 x1 | | |
| +0x468 | reference | Moby, set on 2 of 45: `hoverboot_boltcrank_test`, `hoverboot_bolt_crank#` | A bolt crank (two instances) | E D |
| +0x488 | f32 | 10 x16, 30 x8, 5 x4, 15 x3, 20 x3 ... (15 values) | Launch strength (5-30) | D |
| +0x48C | 4 bytes | 0x01010100 x19, 0x00000000 x10, 0x01000100 x8, 0x01010101 x6, 0x01010000 x1 ... (6 values) | | |
| +0x494 | u32 | 0 x36, 1 x9 | | |

Same on every instance: +0x10 = 0x01000000, +0x3C = -1, +0x54 = 65535, +0x418 = 2, +0x428 = -1, +0x43C = 0x01000000, +0x450 = 3, +0x45C = -1, +0x46C = 30.

### 0x5AC battery bot socket

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3B0; 46 placed in the game (9 levels); e.g. `battery_bot_socket#`, `iris_bot_socket_#`, `moon#e_batterybotsocket#`; own methods: Init 0x0041BF3C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 0 on all 46 | Script: read by `get_attached_battery_bot` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 u32, +0x74 u32, +0x78 u32, +0x100 u8, +0x190 u8, +0x220 u8, +0x2B0 u8, +0x340 u8, +0x350 u8, +0x360 u32, +0x364 u32, +0x368 u32, +0x3A0 u8.

### 0x61C destructible junk pile

Parent 0x56E Destructible > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x270..+0x570; 9 placed in the game (molonoth, space_sector_3); e.g. `JUNK_PILE#`, `tv_mf_destruct_junkpile_#_#`; own methods: Init 0x0041C488.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x270 f32, +0x274 f32, +0x278 f32, +0x27C f32, +0x280 u32, +0x284 u32, +0x288 u32, +0x310 u8, +0x3A0 u8, +0x430 u8, +0x4C0 u8, +0x550 u8, +0x560 u8.

### 0x623 DamageDistributer

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x68; 1300 placed in the game (21 levels); e.g. `yy_moon_platform_antennae#`, `kb_gc_power_cord_junction_a#`, `tj_ago_track_trench_cover#`; own methods: Init 0x003A60A0, Update 0x003A6158.

All 4 One's name for this number; here the platform antennae and force field lasers.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | 4 bytes | 0x01010000 x1296, 0x01010001 x4 | | |

Same on every instance: +0x5C = 64.

### 0x63D prized junk pickup

Parent 0xD8 PickupSpecialBase > 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xF0..+0xF0; 1 placed in the game (space_sector_3); e.g. `moon#b_prized_junk`; own methods: Init 0x004BE96C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x65B crank-driven platform

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x1A0; 80 placed in the game (10 levels); e.g. `touch_pad_switch_activation_indicator#`, `moon#e_winchcrank#_plat#`, `gal_swingshot_arm#`; own methods: fix-up 0x0039105C, Init 0x003910C4.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x68 | 4 bytes | 0x00000000 x74, 0x01010000 x6 | | |
| +0x6C | f32 | 3 x74, 10 x3, 8 x3 | | |
| +0x78 | u32 | 0 x30, 1 x28, 2 x15, 3 x7 | | |
| +0x7C | f32 | 0 x65, 90 x8, -90 x6, -360 x1 | | |
| +0x80 | u32 | 1 x76, 4 x3, 0 x1 | | |
| +0x84 | reference | Volume, set on 28 of 80: `moon#e_winchcrank#_plat#_startvol`, `moon#b_floatingplat_#m#_vol#`, `moon#c_floatingplatform_#m#_vol#` | Start volume | E D |
| +0x88 | reference | Volume, set on 28 of 80: `moon#e_winchcrank#_plat#_endvol`, `moon#b_floatingplat_#m#_vol#`, `moon#c_floatingplatform_#m#_vol#` | End volume | E D |
| +0x8C | reference | Moby, set on 51 of 80: `winch_crank#`, `moon#c_winchcrank#`, `moon#b_winchcrank#` | The winch crank (class 0x665) or bolt crank that drives it | E D |
| +0x90 | reference | Moby, set on 12 of 80: `swingshotTargetGreen#` | A swingshot target that drives it instead | E D |
| +0x94 | s32 | -1 x68, 0 x12 | | |
| +0x9C | s32 | -1 x73, 0 x7 | | |
| +0xA0 | u32 | 3 x79, 0 x1 | | |
| +0xA4 | f32 | 0 x77, 1 x3 | | |
| +0xA8 | f32 | 1 x74, 0 x3, 0.8 x2, 0.5 x1 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x70 f32, +0x74 f32, +0x98 u8, +0xBC f32, +0xC0 u8, +0xC1 u8, +0xD0 f32, +0xD4 f32, +0xD8 f32, +0xDC f32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0xF0 f32, +0xF4 f32, +0xF8 f32, +0xFC f32, +0x100 f32, +0x104 f32, +0x108 f32, +0x10C f32, +0x130 u32, +0x134 u32, +0x138 u32, +0x160 f32, +0x164 f32, +0x168 f32, +0x16C f32, +0x170 f32, +0x174 f32, +0x178 f32, +0x17C f32, +0x190 f32.

### 0x665 winch crank

Parent 0x425 BoltCrankBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x3D0..+0x410; 29 placed in the game (10 levels); e.g. `winch_crank#`, `moon#c_winchcrank#`, `moon#b_winchcrank#`; own methods: Init 0x00356C10, Update 0x00356DB4.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x408 = 101.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x400 f32.

### 0x6A4 camo crate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 337 placed in the game (11 levels); e.g. `camo_crate#`, `Camo_Crate#`, `Morph_Crate#`; own methods: Init 0x004B5904.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x6D4 timed floating platform

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x390; 39 placed in the game (galacton_ship, nefarious_station, space_sector_3); e.g. `moon#e_floatingplatform_#m#`, `za_nss_jump_satellite_arm_heated_surface#`, `tj_gal_platform_boilerroom#`; own methods: Init 0x00429BB8, Update 0x00429B64.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x68 | 4 bytes | 0x01010000 x25, 0x01000000 x12, 0x00000000 x2 | | |
| +0x6C | f32 | 15 x8, 7 x6, 12 x6, 3 x6, 9 x5 ... (7 values) | Per-instance time (3-15) | D |
| +0x380 | s32 | -1 x31, 0 x8 | | |

Same on every instance: +0x384 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x70 f32, +0x74 f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 u32, +0x94 u32, +0x98 u32, +0x120 u8, +0x1B0 u8, +0x240 u8, +0x2D0 u8, +0x360 u8, +0x370 u8.

### 0x6D5 soaker refill tank

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3D0; 17 placed in the game (9 levels); e.g. `soaker_refill_tank#`, `moon#e_soaker_refill_tank`, `lava_tank`; own methods: Init 0x0043C6C8, Update 0x0043CAA8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x374 | u32 | 3 x10, 1 x5, 2 x2 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0x84 u32, +0x88 u32, +0x110 u8, +0x1A0 u8, +0x230 u8, +0x2C0 u8, +0x350 u8, +0x360 u8, +0x370 u32, +0x3C4 u16.

### 0x6D7 vacuum pad / transport tube

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x110; 22 placed in the game (axiom_city, space_sector_2); e.g. `vacuum_transport_pad#`, `TUBE_SP_DUMMY#_A`, `TUBE_SP_DUMMY#_B`; own methods: fix-up 0x0035FC2C, Init 0x0035FB5C, Update 0x0035FC84.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | reference | Moby, set on 12 of 22: `TUBE_SP_DUMMY#_A`, `TUBE_SP_DUMMY#_B`, `moon#x_vacuum_pad#` | The pad at the other end | E D |
| +0x60 | reference | Controller, set on 12 of 22: `Cam_TUBE_SP_DUMMY#_A`, `Cam_TUBE_SP_DUMMY#_B`, `Cam_TUBE_SP_TRANS_A` | Camera | E D |
| +0x64 | 4 bytes | 0x00000000 x15, 0x01000000 x7 | | |
| +0x68 | component |  | Prompt component (+0x68..+0xFF), see *Prompt component* | E |
| +0xC0 | u32 text id | 1108 `AXM_PROMPT_GPLAY_TRANSPORT_TUBE` x17, 442 `GEN_TRIPROMPT_GPLAY_TELEPORT` x5 | Prompt text (AXM_PROMPT_GPLAY_TRANSPORT_TUBE 1108) | E T |
| +0xC4 | reference | unset on all 22 | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0xCC | f32 | 3 on all 22 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0xE0 | u32 | 2 on all 22 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0xF0 | f32 | -1 on all 22 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0xFC | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0xFD | run time u8 |  | Prompt enabled | E |
| +0xFE | run time u8 |  | Prompt showing | E |
| +0xFF | run time u8 |  | Prompt button state | E |

### 0x6FC versa bolt

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3F0; 29 placed in the game (5 levels); e.g. `pull_bolt#`, `versa_fuse_#`, `moon#b_versabolt#`; own methods: Init 0x00437858, Update 0x0043797C.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x3B8 = 2, +0x3C8 = -1, +0x3DC = 0x01000000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x68 u16, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 u32, +0x94 u32, +0x98 u32, +0x120 u8, +0x1B0 u8, +0x240 u8, +0x2D0 u8, +0x360 u8, +0x370 u8, +0x398 u32, +0x39C u32, +0x3A0 u32, +0x3A4 u32, +0x3D0 u32, +0x3E0 f32, +0x3E5 u8, +0x3E6 u8.

### 0x706 armor pad

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 12 placed in the game (11 levels); e.g. `armor_pad#`, `ss#_armor_pad`, `ARMOR_PAD`; own methods: Init 0x00384D2C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x719 custom weapon part pickup

Parent 0xD8 PickupSpecialBase > 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xF0..+0x110; 17 placed in the game (12 levels); e.g. `custom_part#`, `moon#y_reward`, `moon#x_reward`; own methods: Init 0x004BC9D0.

The moons' "reward" on x / y / z moons.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xEC | u32 | 5 x1, 11 x1, 13 x1, 20 x1, 19 x1 ... (17 values) | Which part: different on each of the 17 instances | D |

Same on every instance: +0xF0 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xF4 u32, +0x100 u32, +0x104 u32, +0x108 u32.

### 0x810 large breakable crate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 41 placed in the game (10 levels); e.g. `large_breakable_crate#`; own methods: Init 0x004B4E48.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x813 combat modifier / exploding barrel

Parent 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x250..+0x550; 93 placed in the game (axiom_city, galacton_ship, nefarious_station, space_sector_1); e.g. `z_combat_modifiers_#`, `z_moon#x_exploding_barrel_#`, `z_moon#c_mods_and_crates#`; own methods: Init 0x00421904, Update 0x004218B8.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x250 f32, +0x254 f32, +0x258 f32, +0x25C f32, +0x260 u32, +0x264 u32, +0x268 u32, +0x2F0 u8, +0x380 u8, +0x410 u8, +0x4A0 u8, +0x530 u8, +0x540 u8.

### 0x814 red mist tank

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x80; 5 placed in the game (gimlick_valley, krell_canyon, space_sector_3); e.g. `agorian_red_mist_tank#`; own methods: Init 0x0042E38C, Update 0x0042E608.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x6C | u32 | 1 x3, 0 x2 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x78 u32.

### 0x863 burner platform

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 25 placed in the game (space_sector_2); e.g. `moon#d_floatingplatform_#m#`, `moon#d_burner#`, `yy_machine_burner_flat_quad#`; own methods: Init 0x00166B50, Update 0x00166BF0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | f32 | 4 x18, 2 x7 | Timing: +0x70 4 or 2, +0x78 a per-instance offset (0-10) | D |
| +0x78 | f32 | 0 x6, 2 x3, 1 x3, 6 x2, 8 x2 ... (12 values) | | |

Same on every instance: +0x74 = 4.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x7C f32, +0x82 u16.

### 0x882 moon hill / crater

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 35 placed in the game (insomniac_museum, space_sector_1, space_sector_2, space_sector_3); e.g. `habitable_moon_hill#`, `moon#a_hill#`, `moon#a_crater#`; own methods: Init 0x003117F8, Update 0x00311118.

A hill or crater on a moon surface. Init 0x003117F8 -> 0x0031111C spawns a ring of hoverboot ramps (class 0x577) around it and keeps their handles.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 3 x11, 2 x8, 5 x8, 0 x8 | Variant: 0-2 spawn 4 ramps, 3 and up spawn 8, with different radii and heights. Retail: 0, 2 (craters), 3, 5 (hills) | E D |
| +0x60 | run time |  | Handles of the spawned ramps (+0x60..+0x7F) and their count (+0x80) | E |

### 0x897 simple idle character

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 55 placed in the game (6 levels); e.g. `agorian_warrior_heckler#`, `junk_trader_simpleidle#`, `junk_trader#`; own methods: Init 0x003A57F8, Update 0x003A5940.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | u32 | 4 x50, 3 x5 | | |
| +0x74 | u32 | 2 x11, 6 x10, 1 x9, 7 x7, 0 x7 ... (7 values) | | |
| +0x78 | f32 | 1 x33, 4 x10, 2 x10, 6 x2 | | |
| +0x7C | f32 | 3 x32, 8 x10, 6 x5, 4 x4, 5 x2 ... (7 values) | | |

Same on every instance: +0x84 = 0x00010000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C f32, +0x80 u32, +0x84 u8, +0x86 u8, +0x87 u8.

## Characters and enemies

### 0xE5 HERO_HeroBase

Parent 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x70; not placed itself; values below are over its 29 placed descendants; own methods: Init 0x00169E68, Update 0x00169EE0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x64 | 4 bytes | 0x00010000 x24, 0x00000000 x5 | | |
| +0x6C | u32 | 0 on all 29 | Script: read by `is_hoverboot_thrusting` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x68 u32.

### 0xF6 HERO_HeroCharacter

Parent 0xE5 HERO_HeroBase > 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x70..+0x1AB0; not placed itself; values below are over its 24 placed descendants; own methods: Init 0x001E0B88, Update 0x001DF714.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x11BC | u32 | 0 on all 24 | Script: read by `ratchet_arm_nefarious_disguise` | E S |
| +0x11FC | u8 | 0 on all 24 | Script: written by `ratchet_arm_nefarious_disguise` | E S |
| +0x11FD | u8 | 0 on all 24 | Script: written by `ratchet_arm_nefarious_disguise` | E S |
| +0x11FE | u8 | 0 on all 24 | Script: written by `ratchet_arm_nefarious_disguise` | E S |
| +0x11FF | u8 | 0 on all 24 | Script: written by `ratchet_arm_nefarious_disguise` | E S |
| +0x1200 | u8 | 0 on all 24 | Script: written by `ratchet_arm_nefarious_disguise` | E S |
| +0x1201 | u8 | 0 on all 24 | Script: written by `ratchet_arm_nefarious_disguise` | E S |
| +0x1202 | u8 | 0 on all 24 | Script: written by `ratchet_arm_nefarious_disguise` | E S |
| +0x1203 | u8 | 0 on all 24 | Script: written by `ratchet_arm_nefarious_disguise` | E S |
| +0x17E0 | u8 | 0 on all 24 | Script: written by `ratchet_deactivate_nefarious_disguise` | E S |
| +0x17F0 | u32 | 1065353216 on all 24 | Script: read by `ratchet_deactivate_nefarious_disguise` | E S |
| +0x1800 | u32 | 0 on all 24 | Script: written by `ratchet_deactivate_nefarious_disguise` | E S |
| +0x1908 | u32 | 0 on all 24 | Script: read by `hero_has_object`, `hero_add_object`, `hero_get_num_objects`, `hero_set_num_objects` ... | E S |

Same on every instance: +0x17F4 = 1, +0x17F8 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x11B4 u32, +0x120C u32, +0x1210 u16, +0x1212 u16, +0x1214 u16, +0x1216 u16, +0x1218 u32, +0x121C u32, +0x12AC u32, +0x1300 u32, +0x1304 u32, +0x1308 u32, +0x1508 u32, +0x1590 u8, +0x182C f32, +0x18F8 u32, +0x1904 u32, +0x1910 u8, +0x1911 u8, +0x1912 u8, +0x1920 u32, +0x1980 f32, +0x1984 f32, +0x1988 f32, +0x198C f32, +0x19C0 u8, +0x19C1 u8, +0x19C2 u8, +0x1A10 f32, +0x1A14 f32, +0x1A18 f32, +0x1A1C f32, +0x1A20 f32, +0x1A24 f32, +0x1A28 f32, +0x1A2C f32, +0x1A30 f32, +0x1A34 f32, +0x1A38 f32, +0x1A40 f32, +0x1A44 f32, +0x1A48 f32, +0x1A4C u32, +0x1A80 f32, +0x1A84 f32, +0x1A88 f32, +0x1A8C f32, +0x1A90 f32, +0x1A98 f32, +0x1AA0 u32, +0x1AA4 u32, +0x1AA8 u8, +0x1AA9 u8, +0x1AAB u8.

### 0xF7 Ratchet

Parent 0xF6 HERO_HeroCharacter > 0xE5 HERO_HeroBase > 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x1AB0..+0x2B50; 18 placed in the game (17 levels); e.g. `ratchet`, `ratchet_rcf#_#`, `RATCHET`; own methods: fix-up 0x00936E04, Init 0x00181910, Update 0x00181280.

0x2B50 bytes, all run time: no instance sets anything but one byte at +0x65.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x2510 | u32 | 242 on all 18 | Script: read by `is_hoverbooting`, `is_hoverboot_thrusting`, `ratchet_deactivate_nefarious_disguise` | E S |
| +0x2631 | u8 | 0 on all 18 | Script: written by `ratchet_deactivate_nefarious_disguise` | E S |
| +0x2754 | f32 | 0 on all 18 | Glide pitch: Script: written by `set_glide_pitch`, `reset_glide_pitch` | E S |
| +0x2761 | u8 | 0 on all 18 | Script: written by `hero_force_underwater_fx` | E S |
| +0x291C | u32 | 0 on all 18 | Script: written by `set_force_thrust`, `set_hoverboots`, `hero_force_idle_on_rail`, `ratchet_deactivate_nefarious_disguise` ... | E S |
| +0x2920 | u32 | 0 on all 18 | Script: written by `ratchet_deactivate_nefarious_disguise`, `ratchet_arm_nefarious_disguise` | E S |
| +0x2938 | u32 | 0 on all 18 | Lawrence hologram pod: Script: written by `ratchet_set_lawrence_hologram_pod` | E S |

Same on every instance: +0x1AE0 = 310, +0x1D90 = 12, +0x1D94 = 12, +0x1D98 = 6, +0x1D9C = 12.5664, +0x1DA0 = 12.5664, +0x1DA4 = 6.28319, +0x1DA8 = 0.5, +0x1DAC = 0.15, +0x1DB0 = -0.5, +0x1DB4 = 0.872665, +0x1EF4 = -25, +0x1EF8 = -50, +0x1EFC = 1, +0x223C = 15, +0x2240 = 0.35, +0x2244 = 0.7, +0x2248 = 0.7, +0x27EC = 10.

### 0x160 CharacterBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x840; not placed itself; values below are over its 3279 placed descendants; own methods: fix-up 0x0048FD30, Init 0x0048FFA8, Update 0x00491884.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | u16 | 0 on all 3279 | Script: read by `set_anim_modifier`, `npc_set_speed`, `ally_set_speed`, `azimuth_start_grind` ... | E S |
| +0x9C | u32 | 0 on all 3279 | Script: read by `arena_hydra_tag_in`, `hydra_tank_kill`, `set_anim_modifier`, `npc_set_speed` ... | E S |
| +0xD4 | u32 | 0 on all 3279 | Inner awareness: Script: written by `enemy_set_inner_awareness` | E S |
| +0xD8 | u32 | 0 on all 3279 | Outer awareness: Script: written by `enemy_set_outer_awareness` | E S |
| +0x13C | u32 | 0 on all 3279 | Script: read by `zoni_path_follow`, `ally_followpathhover`, `ally_gotopointhover`, `ally_setcollisionignore` | E S |
| +0x148 | 4 bytes | 0x01010100 x3256, 0x00000000 x15, 0x00000100 x8 | | |
| +0x1A0 | f32 | 1 on all 3279 | Script: written by `hydra_tank_kill` | E S |
| +0x1A4 | f32 | 1 on all 3279 | Script: read by `hydra_tank_kill` | E S |
| +0x29C | u32 | 0 on all 3279 | Script: read by `set_anim_modifier` | E S |
| +0x2A0 | u32 | 0 on all 3279 | Script: read by `hydra_tank_kill` | E S |
| +0x2B8 | u32 | 0 on all 3279 | Script: read by `hydra_tank_kill` | E S |
| +0x3E0 | u32 | 0 on all 3279 | Script: read by `hydra_tank_kill` | E S |
| +0x470 | u32 | 0 on all 3279 | Script: read by `hydra_tank_kill` | E S |
| +0x4F0 | u8 | 0 on all 3279 | Script: written by `hydra_tank_kill` | E S |
| +0x5F8 | u32 | 0 on all 3279 | Script: read by `nefarious_laser_create` | E S |
| +0x5FC | u32 | 0 on all 3279 | Script: read by `nefarious_laser_create` | E S |
| +0x760 | u32 | 0 on all 3279 | Script: read by `npc_interact`, `zoni_path_follow`, `qwark_start_smashers`, `qwark_startled_goto` | E S |
| +0x7F0 | u32 | 0 on all 3279 | Script: written by `nefarious_resume_behavior` | E S |
| +0x7F8 | u32 | 0 on all 3279 | Script: written by `nefarious_resume_behavior` | E S |
| +0x808 | reference | Path, set on 614 of 3279: `agorian_warrior#_curve`, `agorian_battlebot#_curve`, `blob_small#_curve` | The character's path (<name>_curve) | D |
| +0x80C | reference | Path, set on 785 of 3279: `curve#`, `group#_intro_curve#`, `INTRO_SHOOTER#` | Intro path. Script: written by `enemy_set_intro_path` | Q D S |
| +0x810 | reference | Path, set on 1184 of 3279: `agorian_warrior#_curve`, `agorian_battlebot#_curve`, `curve#` | Patrol path. Script: written by `npc_patrol`, `npc_stop`, `enemy_set_patrol_path` | Q D S |
| +0x814 | reference | Area, set on 396 of 3279: `moon#e_ap#_homearea`, `moon#b_ap#_homearea`, `moon#a_ap#_homearea` | Home Area (moonXx_apNN_homearea) | D |
| +0x818 | reference | Path, set on 1 of 3279: `PATH_LAST_ZONI` | A further path (one Zoni) | E D |

Same on every instance: +0x170 = 1, +0x180 = 65535, +0x1A8 = 1, +0x820 = 1, +0x83C = 0x01000000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x74 u32, +0x78 u32, +0x7C u32, +0x98 u32, +0xA0 u16, +0xA2 u16, +0xA4 u16, +0xA6 u16, +0xA8 u32, +0xAC u32, +0x10C u16, +0x134 u32, +0x138 u32, +0x140 u32, +0x150 f32, +0x154 f32, +0x158 f32, +0x15C f32, +0x18C u32, +0x1B0 u32, +0x234 u32, +0x250 u32, +0x2A4 f32, +0x2A8 f32, +0x2AC f32, +0x2B0 u32, +0x2B4 u32, +0x340 u8, +0x3D0 u8, +0x460 u8, +0x580 u8, +0x590 u8, +0x5A0 u32, +0x5A4 u32, +0x5A8 u32, +0x5AC u32, +0x5B0 u32, +0x5B4 u32, +0x5B8 u32, +0x5BC u32, +0x5C0 u32, +0x5C4 u32, +0x5C8 u32, +0x5CC u32, +0x5D0 u32, +0x5D4 u32, +0x5D8 u32, +0x5DC u32, +0x5E0 u32, +0x5E4 u32, +0x5E8 u32, +0x5EC u32, +0x5F0 u32, +0x5F4 u32, +0x600 u32, +0x618 u32, +0x620 u32, +0x630 f32, +0x634 f32, +0x63C u32, +0x640 u32, +0x644 u32, +0x648 u32, +0x658 f32, +0x6A4 u32, +0x6A8 u8, +0x6B4 u32, +0x800 u32, +0x804 f32, +0x81C u32, +0x824 f32, +0x828 f32, +0x82C u32, +0x830 u8, +0x834 u32, +0x838 u32, +0x83D u8, +0x83E u8.

### 0x181 EnemyBase

Parent 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x840..+0x920; not placed itself; values below are over its 2996 placed descendants; own methods: fix-up 0x0008EF68, Init 0x00090B48, Update 0x00090914.

The two awareness blocks have QForce's ClueEnemy shape: (Volume, Area, distance, flag). A moon's enemy group is its moon_apNN set: _homearea, _inner_vol, _outer_vol and the _enemies Pod.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x840 | u32 | 0 x2611, 1 x328, 2 x57 | 1 = Init deactivates the enemy (0x005238B4): it waits for a script or a spawner. 2 on a few (role open) | E D |
| +0x844 | u32 | 0 x1811, 1 x1093, 3 x59, 2 x33 | SEGMENT_Spawn rule, the same component as Damageable +0x5C (GetInterface 0x0008EB38 returns it for class 0x4BD) | E T |
| +0x848 | u32 | 0 x1335, 1 x553, 2 x380, 9 x151, 6 x130 ... (16 values) | Variant index, read by each enemy class's Init (the Agorian warrior switches on 0-9) | E D |
| +0x864 | reference | Volume, set on 1823 of 2996: `hangar_int_inner_vol`, `bat_bot_home_vol#`, `VOL_TRIGGER_ENTRANCE_BATTLE` | Inner awareness volume: the hero inside it wakes the enemy. Script: written by `enemy_set_inner_awareness` | Q D S |
| +0x868 | reference | Area, set on 276 of 2996: `cargo_near_area_inner_#`, `vorselon_inner`, `moon#e_ap#_awarenessarea` | Inner awareness Area (several volumes). Script: written by `enemy_set_inner_awareness` | Q D S |
| +0x86C | f32 | 30 x2334, 15 x299, 20 x112, 0 x41, 35 x36 ... (22 values) | Inner awareness distance (30 by default). Script: written by `enemy_set_inner_awareness` | Q D S |
| +0x870 | u32 | 0 x2974, 1 x22 | Inner awareness flag (0 / 1) | Q D |
| +0x874 | reference | Volume, set on 1814 of 2996: `hangar_int_outer_vol`, `volume#`, `bat_bot_home_vol#` | Outer awareness volume: the enemy gives up when the hero leaves it. Script: written by `enemy_set_outer_awareness` | Q D S |
| +0x878 | reference | Area, set on 309 of 2996: `cargo_near_area_outter_#`, `vorselon_inner`, `moon#e_ap#_awarenessarea` | Outer awareness Area. Script: written by `enemy_set_outer_awareness` | Q D S |
| +0x87C | f32 | 30 x2679, 20 x78, 0 x43, 35 x36, 0.5 x30 ... (20 values) | Outer awareness distance (30 by default). Script: written by `enemy_set_outer_awareness` | Q D S |
| +0x880 | u32 | 0 x2953, 1 x43 | Outer awareness flag (0 / 1) | Q D |
| +0x884 | reference | Area, set on 450 of 2996: `FIREPOINTS_BLOB_ALLEY_BLOBS#`, `firepoints_control_wave#_set#`, `firepoints_entrance_wave#_set#` | Firepoints Area. Script: written by `enemy_clear_firepoints`, `enemy_set_firepoints` | D S |
| +0x888 | reference | Controller, set on 744 of 2996: `moon#x_battlefield_controller`, `moon#y_battlefield_controller`, `td_battlefieldcontroller_#` | EnemyBattlefieldController | D |
| +0x88C | reference | Controller, set on 599 of 2996: `EnemyFirepointController#`, `moon#x_group#_firepoint_controller`, `wasps#_firepoint_controller` | EnemyFirepointController | D |
| +0x890 | reference | Scent, set on 132 of 2996: `AMBIENT_A#_Targets`, `AREA_AMBIENT_DAM`, `AMBIENT_A#_Targets_wtower` | Scent of ambient targets. Script: written by `enemy_set_ambient_attack_scent` | E D S |
| +0x8A0 | reference | Area, set on 14 of 2996: `A#_BEAST_NOGO`, `tower_#_dont_go_area`, `batterybot_cant_go` | No-go Area | E D |
| +0x8A4 | reference | unset on all 2996 | Reference slot: resolved by the class's fix-up | E |
| +0x8A8 | reference | Pod, set on 1314 of 2996: `BLOB_SPAWNER_BATBOT#_#`, `AGORIAN_A#_PART_OF_POD`, `moon#x_group#_enemies` | The enemy's Pod (its group: moonXx_apNN_enemies) | E D |
| +0x8B0 | u32 | 0 on all 2996 | Volume target: Script: written by `enemy_set_volume_target`, `enemy_clear_volume_target` | E S |
| +0x8B4 | 4 bytes | 0x00000000 x2780, 0x01000000 x216 | | |
| +0x8B8 | component |  | Component of class 0x835 (+0x8B8..), returned by GetInterface; holds the two fields below | E |
| +0x8C4 | u8 | 0 x2804, 1 x192 | 1 on a tenth of the enemies (in the 0x835 component; role open) | D |
| +0x8C8 | f32 | -1 x2955, 2 x9, 35 x9, 50 x8, 5 x4 ... (10 values) | A distance of the 0x835 component: Init squares it when positive; -1 = none. 35-80 where set | E D |
| +0x918 | u32 | 0 x2959, 4 x21, 2 x6, 1 x5, 3 x5 | | |
| +0x91C | u32 | 0 on all 2996 | Script: written by `hydra_tank_kill` | E S |

Same on every instance: +0x8AC = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x84C u32, +0x8BC u32, +0x8C0 u32, +0x8D4 u32, +0x8DC u32, +0x8E4 u32.

### 0x182 EnemyBurrowerBeetle

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC90; 410 placed in the game (10 levels); e.g. `dust_creeper#`, `BEETLE#`, `dust_creeper_gcc_#_#`; own methods: Init 0x00118F34, Update 0x00118464.

The dust creepers.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC70 | u32 | 1 x367, 0 x43 | | |
| +0xC74 | f32 | 0 x308, 3 x64, 1 x18, 4 x7, 5 x4 ... (8 values) | | |
| +0xC78 | f32 | 6 x383, 4 x23, 3.5 x3, 5 x1 | | |
| +0xC7C | u32 | 0 x382, 1 x28 | | |

Same on every instance: +0x9A0 = 322, +0xBF4 = 0.5, +0xBF8 = -0.5, +0xBFC = 0.872665, +0xC00 = 7, +0xC04 = 18, +0xC08 = 0.2, +0xC0C = 0.5, +0xC14 = -25, +0xC18 = -50, +0xC60 = 4, +0xC64 = 4, +0xC68 = 8, +0xC6C = 5.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x9A4 f32, +0x9A8 u32, +0xA70 u32, +0xA74 u32, +0xA78 u32, +0xA9C u32, +0xAF0 u32, +0xAF4 f32, +0xC40 u32, +0xC80 u8.

### 0x187 NPCBase

Parent 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x840..+0xD30; 10 placed in the game (8 levels); e.g. `vorselon`, `NEFARIOUS_HOLOVID_#`, `lilman`; own methods: fix-up 0x002ADB78, Init 0x002AD8A8, Update 0x002AD1CC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x910 | component |  | Prompt component (+0x910..+0x9A7), see *Prompt component* | E |
| +0x968 | u32 text id | 442 `GEN_TRIPROMPT_GPLAY_TELEPORT` x256, 907 `GEN_PROMPT_SPACE_TLKTOJUNK` x6, 1768 `GEN_PROMPT_GPLAY_TLKTOMAC` x1 ... (7 values) | Talk prompt text (442 default; GEN_PROMPT_SPACE_TLKTOJUNK 907 on junk traders) | E T |
| +0x96C | reference | Volume, set on 2 of 267: `mackReturnConvoVol`, `ss#_vorselon_scene_pip_cam` | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0x974 | f32 | 3 x263, 0 x4 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0x988 | u32 | 2 on all 267 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0x998 | f32 | -1 on all 267 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0x9A4 | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0x9A5 | run time u8 |  | Prompt enabled | E |
| +0x9A6 | run time u8 |  | Prompt showing | E |
| +0x9A7 | run time u8 |  | Prompt button state | E |
| +0xC70 | u32 | 1 on all 267 | Script: written by `npc_icon_off` | E S |
| +0xC74 | u32 | 0 on all 267 | Script: written by `npc_icon_on` | E S |
| +0xC88 | u32 | 0 x209, 2 x42, 3 x13, 1 x3 | 0-3 (role open). Script: written by `npc_patrol` | D S |
| +0xC8C | reference | Area, set on 18 of 267: `TRADER_STOP_VOLS#`, `TERACH_#_STOP_POINTS`, `AREA_PRISON_STOP#` | Area of stop points. Script: written by `npc_patrol`, `npc_stop` | E D S |
| +0xC90 | u32 | 0 on all 267 | Face moby: Script: written by `npc_set_face_moby`, `npc_clear_face_moby` | E S |
| +0xC94 | u32 | 0 on all 267 | Face hero: Script: written by `npc_set_face_hero` | E S |
| +0xCA0 | f32 | 0 on all 267 | Script: written by `npc_start_attack` | E S |
| +0xCA4 | f32 | 0 on all 267 | Script: written by `npc_start_attack` | E S |
| +0xCA8 | f32 | 0 on all 267 | Script: written by `npc_start_attack` | E S |
| +0xCAC | f32 | 0 on all 267 | Script: written by `npc_start_attack` | E S |
| +0xCB4 | reference | Volume, set on 8 of 267: `VOL_POLLYX_TRAITOR_POS`, `terrachnoid_escape_vol` | Reference slot: resolved by the class's fix-up | E |
| +0xCB8 | reference | Volume, set on 10 of 267: `VOL_CTRL_TERACHNOID#_POS`, `terach_#_stop_point_#`, `terrachnoid_escape_vol` | Reference slot: resolved by the class's fix-up | E |
| +0xCBC | f32 | 10.5 x266, 10 x1 | | |
| +0xCC0 | 4 bytes | 0x00000000 x254, 0x01000000 x13 | | |
| +0xCC4 | u32 | 0 x257, 5 x5, 3 x5 | 0, 3 or 5 | D |
| +0xCCC | reference | Volume, set on 13 of 267: `vorselon_cam`, `ss#_mission_transport_npc_cam_vol`, `ss#_mission_junk_cam_vol` | Camera volume for the conversation / picture-in-picture | D |
| +0xCD0 | reference | Volume, set on 13 of 267: `junk_trader_lightlink`, `vorselon_lightlink`, `terachnoid_lightlink` | "Light link" volume | D |
| +0xCD8 | f32 | 0 x265, 1.175 x2 | | |
| +0xCDC | f32 | -2 x265, -3 x2 | | |
| +0xCF8 | u32 | 0 on all 267 | Target: Script: written by `npc_set_target`, `npc_clear_targets` | E S |
| +0xCFC | u32 | 0 on all 267 | Script: written by `npc_clear_targets` | E S |
| +0xD00 | u32 | 0 on all 267 | Script: written by `npc_clear_targets` | E S |
| +0xD05 | u8 | 1 on all 267 | Script: written by `npc_idle_dialogue_enable` | E S |
| +0xD06 | u8 | 0 on all 267 | Script: written by `npc_holo_down`, `npc_holo_up` | E S |
| +0xD08 | reference | Controller, set on 66 of 267: `CTRL_TERA#_AMBIENT_DIALOGUE`, `DIALOGUE_FONGOID_PAST#`, `AmbientFongoidDialog#` | Ambient dialogue controller | E D |
| +0xD14 | u32 | 1065353216 on all 267 | Script: written by `npc_enable_damage`, `npc_disable_damage`, `npc_scale_damage` | E S |
| +0xD18 | u32 | 0 on all 267 | Script: written by `npc_get_attention` | E S |
| +0xD1C | u32 | 0 on all 267 | Script: written by `npc_get_attention` | E S |
| +0xD20 | u32 | 0 on all 267 | Script: written by `npc_goto`, `npc_patrol`, `npc_stop`, `npc_interact` ... | E S |
| +0xD24 | u32 | 0 x235, 1 x9, 2 x8, 3 x8, 403632143 x1 ... (11 values) | Visible: Script: written by `npc_vorselon_set_visible` | E S |
| +0xD28 | u8 | 0 x266, 1 x1 | Visible: Script: written by `npc_vorselon_set_visible` | E S |

Same on every instance: +0x9B0 = 322, +0xC04 = 0.5, +0xC08 = -0.5, +0xC0C = 0.872665, +0xC10 = 7, +0xC14 = 18, +0xC18 = 0.2, +0xC1C = 0.5, +0xC24 = -25, +0xC28 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x900 u32, +0x904 u32, +0x908 u32, +0x9B4 f32, +0x9B8 f32, +0xA80 u32, +0xA84 u32, +0xA88 f32, +0xAAC u32, +0xAC0 u32, +0xAC4 u32, +0xAC8 u32, +0xACC u32, +0xB00 u32, +0xB04 f32, +0xC50 u32, +0xCB0 u32, +0xCC8 u32, +0xD0C u32, +0xD10 u32.

### 0x19A swamp flyer / wasp

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xCD0; 87 placed in the game (9 levels); e.g. `swamp_flyer#`, `wasp_gcc_C#_#`, `moon#e_wasp#`; own methods: Init 0x001289A0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC84 | 4 bytes | 0x00020002 x77, 0x00010002 x10 | | |
| +0xC9C | u32 | 0 x84, 1 x3 | | |
| +0xCA0 | f32 | 2 x76, 5 x8, 6 x2, 1 x1 | | |

Same on every instance: +0x920 = 409, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50, +0xBE4 = 0.5, +0xBE8 = 0.1, +0xBEC = 6.28319, +0xC78 = 4, +0xC7C = 16, +0xC80 = 16, +0xC88 = 1, +0xC8C = 5, +0xC90 = 0.5, +0xC94 = 5, +0xC98 = 5.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 u32, +0x9F4 u32, +0x9F8 u32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBE0 f32, +0xCC0 u8, +0xCC1 u8, +0xCC2 u8, +0xCC3 u8.

### 0x4BF spinner bot

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC10; 117 placed in the game (space_sector_1, space_sector_2, space_sector_3); e.g. `spinner_bot#`, `moon#e_spinnerbot#`, `moon#e_enemy_spinnerbot#`; own methods: Init 0x0011CEB4.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC04 | f32 | 2 x73, 0 x28, 1 x16 | | |

Same on every instance: +0x920 = 409, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50, +0xBE4 = 0.5, +0xBE8 = 0.1, +0xBEC = 6.28319, +0xBF8 = 4, +0xBFC = 4, +0xC00 = 8.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 u32, +0x9F4 u32, +0x9F8 u32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBE0 u32, +0xBF0 u32, +0xC08 u8.

### 0x4E1 Agorian warrior

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xD30; 306 placed in the game (5 levels); e.g. `agorian_warrior#`, `AGORIAN_SHIELDED#`, `Moon_warrior#`; own methods: fix-up 0x0010BEE8, Init 0x001102D4, Update 0x0010DDBC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC88 | u32 | 0 x189, 3 x49, 1 x42, 2 x26 | | |
| +0xCF4 | f32 | 0 on all 306 | Rush dists: Script: written by `agorian_set_rush_dists` | E S |
| +0xCF8 | f32 | 0 on all 306 | Rush dists: Script: written by `agorian_set_rush_dists` | E S |
| +0xD0C | u32 | 0 on all 306 | Shots per volley: Script: written by `agorian_set_shots_per_volley` | E S |
| +0xD10 | 4 bytes | 0x00000000 x304, 0x00010000 x2 | | |
| +0xD1C | u32 | 0 x299, 256 x7 | | |

Same on every instance: +0x930 = 322, +0xB84 = 0.5, +0xB88 = -0.5, +0xB8C = 0.872665, +0xB90 = 7, +0xB94 = 18, +0xB98 = 0.2, +0xB9C = 0.5, +0xBA4 = -25, +0xBA8 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x934 u32, +0x938 u32, +0xA00 u32, +0xA04 u32, +0xA08 u32, +0xA2C u32, +0xA80 u32, +0xA84 f32, +0xBD0 u32, +0xBD8 u32, +0xBDC f32, +0xBF4 u32, +0xBFC f32, +0xC00 f32, +0xC08 f32, +0xC0C f32, +0xC10 f32, +0xC14 f32, +0xC50 u32, +0xC5C u32, +0xC60 f32, +0xC64 f32, +0xC68 f32, +0xC6C f32, +0xC8C u32, +0xCD0 u32, +0xCD4 u32, +0xCD8 u32, +0xCDC u32, +0xCE0 u32, +0xCE4 u32, +0xCE8 u32, +0xCEC f32, +0xCFC f32, +0xD00 f32, +0xD08 f32, +0xD14 u8, +0xD15 u8, +0xD18 u8, +0xD19 u8, +0xD1A u8, +0xD1B u8, +0xD20 f32, +0xD24 f32.

### 0x52E PlayerBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; not placed itself; values below are over its 29 placed descendants.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 0 on all 29 | Script: read by `default_follow_cam_pos`, `get_follow_cam_dist`, `get_follow_cam_pitch`, `get_follow_cam_fov` ... | E S |

### 0x54E battery bot

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC90; 34 placed in the game (7 levels); e.g. `iris_battery_bot#`, `battery_bot#`, `Battery_Bot#`; own methods: fix-up 0x00107034, Init 0x00107D6C, Update 0x001072B8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x940 | reference | unset on all 34 | Reference slot: resolved by the class's fix-up | E |
| +0xC80 | reference | Area, set on 32 of 34: `paths_iris_bot`, `PATHS_BATTERY_BOTS`, `junktownBatteryBot_FleePaths` | Area of flee paths | E D |
| +0xC84 | reference | Volume, set on 8 of 34: `vol_burner_nolock` | A no-lock volume | E D |

Same on every instance: +0x960 = 322, +0xBB4 = 0.5, +0xBB8 = -0.5, +0xBBC = 0.872665, +0xBC0 = 7, +0xBC4 = 18, +0xBC8 = 0.2, +0xBCC = 0.5, +0xBD4 = -25, +0xBD8 = -50, +0xC60 = 2, +0xC70 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x920 f32, +0x924 f32, +0x928 f32, +0x92C f32, +0x934 u32, +0x964 f32, +0x968 f32, +0xA30 f32, +0xA34 u32, +0xA38 f32, +0xA5C u32, +0xAB0 u32, +0xAB4 f32, +0xC00 u32, +0xC20 u16, +0xC22 u16, +0xC24 f32, +0xC28 f32, +0xC2C u16, +0xC30 u32, +0xC34 u32, +0xC38 u32, +0xC3C u32, +0xC40 u32, +0xC4C u32, +0xC50 u32, +0xC54 u32, +0xC58 u32, +0xC78 u8, +0xC79 u8, +0xC7A u8, +0xC7B u8, +0xC7C u8, +0xC7D u8, +0xC7E u8, +0xC7F u8, +0xC88 u8, +0xC89 u8, +0xC8A u8, +0xC8B u8.

### 0x590 Agorian battlebot

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xBF0; 186 placed in the game (6 levels); e.g. `agorian_battlebot#`, `JUNK_BATTLEBOT#`, `moon#e_battlebot#`; own methods: Init 0x00109DE8, Update 0x00108BF4.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x930 = 322, +0xB84 = 0.5, +0xB88 = -0.5, +0xB8C = 0.872665, +0xB90 = 7, +0xB94 = 18, +0xB98 = 0.2, +0xB9C = 0.5, +0xBA4 = -25, +0xBA8 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x920 u8, +0x921 u8, +0x922 u8, +0x923 u8, +0x924 u8, +0x925 u8, +0x928 u32, +0x934 u32, +0x938 u32, +0xA00 u32, +0xA04 u32, +0xA08 u32, +0xA2C u32, +0xA80 u32, +0xA84 f32, +0xBD0 u32, +0xBD8 u32, +0xBDC f32.

### 0x5B0 junk trader

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD30; 36 placed in the game (7 levels); e.g. `junk_trader#`, `junk_trader`, `junk_trader_#_#_celeb`; own methods: Init 0x002B7DE4.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x5D3 Zoni

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xCD0; 36 placed in the game (13 levels); e.g. `zoni#`, `moon#b_zoni`, `moon#a_reward`; own methods: fix-up 0x00133F40, Init 0x00135140, Update 0x00133EA8.

A SpaceMoon finds its Zoni by this class.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x920 | u32 | 409 on all 36 | Script: read by `zoni_set_forced_idle` | E S |
| +0xB90 | u32 | 0 on all 36 | Forced idle: Script: written by `zoni_set_forced_idle` | E S |
| +0xB94 | u32 | 3251109888 on all 36 | Forced idle: Script: written by `zoni_set_forced_idle` | E S |
| +0xB98 | u32 | 3259498496 on all 36 | Forced idle: Script: written by `zoni_set_forced_idle` | E S |
| +0xC90 | reference | Area, set on 14 of 36: `moon#b_zoni_path`, `moon#c_zoni_chase_paths`, `moon#a_zoni_chase_paths` | Area of chase paths | E D |
| +0xC94 | reference | Volume, set on 8 of 36: `moon#b_zoni_homevol`, `zoni_chase#_inner_vol`, `zoni#_pre_wander_vol` | Volume: start of the chase / home | E D |
| +0xC98 | reference | Volume, set on 10 of 36: `moon#b_zoni_homevol`, `zoni_chase_end_inner_vol`, `zoni#_post_wander_vol` | Volume: end of the chase | E D |
| +0xCA0 | u32 | 0 on all 36 | Stop: Script: written by `zoni_path_follow`, `zoni_set_stop`, `zoni_clear_stop` | E S |
| +0xCB8 | f32 | 0 x35, 1.25 x1 | | |
| +0xCBD | u8 | 0 x35, 1 x1 | Forced idle: Script: written by `zoni_set_forced_idle` | E S |
| +0xCC0 | u32 | 0 on all 36 | Script: read by `zoni_resurrect` | E S |

Same on every instance: +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xBE4 = 0.5, +0xBE8 = 0.1, +0xBEC = 6.28319, +0xC30 = 2, +0xC40 = -1, +0xC50 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 f32, +0x928 f32, +0x9F0 f32, +0x9F4 f32, +0x9F8 f32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBE0 u32, +0xBF0 u16, +0xBF2 u16, +0xBF4 f32, +0xBF8 f32, +0xBFC u16, +0xC00 u32, +0xC04 u32, +0xC08 u32, +0xC0C u32, +0xC10 u32, +0xC20 u32, +0xC24 u32, +0xC28 u32, +0xC48 u8, +0xC49 u8, +0xC4A u8, +0xC4B u8, +0xC4C u8, +0xC4D u8, +0xC4E u8, +0xC4F u8, +0xC54 u32, +0xC60 u32, +0xC64 u32, +0xC80 f32, +0xC84 f32, +0xC88 f32, +0xC8C f32, +0xC9C u32, +0xCA4 f32, +0xCA8 f32, +0xCAC f32, +0xCB0 f32.

### 0x5E0 Fongoid

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD60; 127 placed in the game (6 levels); e.g. `FONGOID_PRISONER#`, `FONGOID_BG_CONVO#`, `fongoid#`; own methods: fix-up 0x002B6D54, Init 0x002B6DE0, Update 0x002B6948.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xD40 | u32 | 0 x63, 1 x21, 2 x15, 3 x6, 8 x6 ... (13 values) | | |
| +0xD44 | reference | Pod, set on 9 of 127: `ENTRANCE_BAND`, `DIRT_BAND` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xD48 u8, +0xD49 u8, +0xD4C f32, +0xD50 u8, +0xD51 u8.

### 0x66A blob

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC80; 249 placed in the game (space_sector_2, tombli, valkyrie_fleet, zolar_forest); e.g. `blob_small#`, `islands_blob_s#_#`, `BLOB_ALLEY_BLOB_#`; own methods: fix-up 0x00116178, Init 0x00116590, Update 0x00117384.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC10 | f32 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC14 | f32 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC18 | f32 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC1C | f32 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC20 | u32 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC30 | reference | Pod, set on 39 of 249: `BLOB_ALLEY_#`, `POD_FOREST_BLOBS`, `POD_WATERFALL_BLOBS` | Pod of the blob's group | E D |
| +0xC35 | u8 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC36 | u8 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC37 | u8 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC6C | f32 | 0 x174, 2 x41, 1 x13, 2.5 x13, 1.5 x8 | | |
| +0xC70 | u32 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC74 | u32 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |
| +0xC78 | u8 | 0 on all 249 | Script: written by `blob_scripted_combine` | E S |

Same on every instance: +0x920 = 322, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 f32, +0x9F4 f32, +0x9F8 f32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBF0 u32, +0xBF4 u32, +0xBF8 u32, +0xC00 f32, +0xC04 f32, +0xC38 u32, +0xC3C u32, +0xC54 u32, +0xC5C u32, +0xC64 u32, +0xC68 f32.

### 0x699 Nefarious swarmer

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC10; 571 placed in the game (9 levels); e.g. `nefarious_swarmer#`, `iris_room_nef_swarmer#`, `SWARMER#`; own methods: Init 0x00127270.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xBFC | u32 | 2 x285, 1 x234, 0 x52 | | |
| +0xC00 | f32 | 0 x457, 2 x51, 5 x20, 1 x20, 0.5 x15 ... (8 values) | | |

Same on every instance: +0x920 = 322, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 f32, +0x9F4 f32, +0x9F8 f32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBF0 u32, +0xBFA u16.

### 0x69A Nefarious trooper

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xF00; 676 placed in the game (10 levels); e.g. `nefarious_medium_trooper#`, `moon#x_nef_med_trooper#`, `Nef_wave#_#`; own methods: Init 0x00125514.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xEC4 | u32 | 0 x657, 10 x6, 1 x6, 2 x5, 3 x2 | | |
| +0xEE0 | u32 | 1 x541, 2 x85, 0 x50 | | |
| +0xEE4 | u32 | 1 x424, 0 x252 | | |
| +0xEE8 | u32 | 0 x551, 1 x125 | | |

Same on every instance: +0x920 = 409, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50, +0xBE4 = 0.5, +0xBE8 = 0.1, +0xBEC = 6.28319, +0xBF0 = 322, +0xE44 = 0.5, +0xE48 = -0.5, +0xE4C = 0.872665, +0xE50 = 7, +0xE54 = 18, +0xE58 = 0.2, +0xE5C = 0.5, +0xE64 = -25, +0xE68 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xCC0 u32, +0xCC4 u32, +0xCC8 u32, +0xCEC u32, +0xD40 u32, +0xD44 f32, +0xEB0 u32, +0xEB4 u32, +0xEB8 u32, +0xEBC u32, +0xEC0 u32, +0xEC8 u32, +0xECC u32, +0xED0 u32, +0xED4 u32, +0xED8 u32, +0xEDC u32, +0xEEC u32, +0xEF0 u32, +0xEF4 f32, +0xEF8 u16, +0xEFA u16, +0xEFC u8.

### 0x6B2 Terachnoid

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD40; 55 placed in the game (5 levels); e.g. `terachnoid#`, `CTRL_TERACHNOID#`, `TERACHNOID_GAMER#`; own methods: fix-up 0x002B9994, Init 0x002B9FF8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xD24 | reference | Area, set on 7 of 55: `movepoints_control_terach#`, `movepoints_control_pollyx` | Reference slot: resolved by the class's fix-up | E |
| +0xD34 | 4 bytes | 0x00000000 x54, 0x00010000 x1 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xD30 u32.

## Other Clues

### 0x120 ClueGrindRail

Parent 0x11D ClueBase; own fields +0x14..+0x20; 81 placed in the game (6 levels); e.g. `chase_grindrail#`, `grindclue#`, `ClueGrindRail#`; own methods: fix-up 0x0015B3A4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | Path, set on 81 of 81: `curve#`, `GrindRailCurve#`, `grindrail#_path` | Reference slot: resolved by the class's fix-up | E |
| +0x18 | reference | Area, set on 21 of 81: `grindrail#_area`, `ClueGrindRail#aAREA`, `grindrail_animation_support` | Reference slot: resolved by the class's fix-up | E |
| +0x1C | u32 | 0 x71, 1 x10 | | |

### 0x121 ClueWind

Parent 0x11D ClueBase; own fields +0x14..+0x24; 4 placed in the game (gimlick_valley, krell_canyon, molonoth); e.g. `ClueWind#`; own methods: fix-up 0x0015B35C.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | f32 | 2.5 x1, 4 x1, 3 x1, 0.5 x1 | | |
| +0x18 | f32 | 6 x2, 3.5 x1, 5 x1 | | |
| +0x1C | reference | unset on all 4 | Reference slot: resolved by the class's fix-up | E |
| +0x20 | reference | unset on all 4 | Reference slot: resolved by the class's fix-up | E |

### 0x122 ClueLedgeGrab

Parent 0x11D ClueBase; own fields +0x14..+0x24; 19 placed in the game (axiom_city, krell_canyon, molonoth, valkyrie_fleet); e.g. `clue_azi_jump_island_#c#`, `ClueLedgeGrab#_#`, `ClueLedgeGrab#`; own methods: fix-up 0x0015B314.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | Path, set on 6 of 19: `LedgeGrabPath#_#`, `curve#` | Reference slot: resolved by the class's fix-up | E |
| +0x18 | reference | Area, set on 5 of 19: `ledgegrab#_layer`, `ledge_grab_paths_#`, `krelloutsideledgegrab#` | Reference slot: resolved by the class's fix-up | E |
| +0x1C | u32 | 0 x18, 1 x1 | | |
| +0x20 | f32 | 0.75 x15, 1.25 x2, 1.5 x1, 0.5 x1 | | |

### 0x123 ClueWater

Parent 0x11D ClueBase; own fields +0x14..+0x1C; 1 placed in the game (gimlick_valley); e.g. `ClueWater#`.

No instance sets anything in the class's own range.

### 0x125 ClueQuicksand

Parent 0x11D ClueBase; own fields +0x14..+0x40; 2 placed in the game (gimlick_valley); e.g. `CLUE_WATER_PAST`, `ClueWaterPresent`; own methods: fix-up 0x0015B264.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x30 | reference | unset on all 2 | Reference slot: resolved by the class's fix-up | E |

### 0x126 ClueLava

Parent 0x11D ClueBase; own fields +0x14..+0x18; 1 placed in the game (agorian_arena); e.g. `ClueLava#`.

No instance sets anything in the class's own range.

### 0x127 ClueFallToDeath

Parent 0x11D ClueBase; own fields +0x14..+0x14; 146 placed in the game (15 levels); e.g. `ClueFallToDeath#`, `CLUE_DEATH_GENERAL`, `ClueFallToDeath#_second_gear`.

No instance sets anything in the class's own range.

### 0x12E ClueSwingshotEquip

Parent 0x11D ClueBase; own fields +0x14..+0x14; 5 placed in the game (axiom_city, tombli, valkyrie_fleet); e.g. `ClueSwingshotEquip#`, `clue_swingshot_equip_#`.

No instance sets anything in the class's own range.

### 0x130 ClueHudMap

Parent 0x11D ClueBase; own fields +0x14..+0x204; 22 placed in the game (15 levels); e.g. `ClueHudMap#`; own methods: fix-up 0x0015B14C.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 0 x15, 1 x5, 2 x2 | | |
| +0x18 | reference | Volume, set on 3 of 22: `HUDMAP_COLL_VOL` | Reference slot: resolved by the class's fix-up | E |
| +0x1C | reference | Area, set on 1 of 22: `mol_clue_#` | | |
| +0x20 | u32 | 0 x21, 128 x1 | | |
| +0x24 | u32 | 0 x21, 208 x1 | | |
| +0x28 | u32 | 0 x21, 64 x1 | | |
| +0x2C | u32 | 0 x21, 64 x1 | | |
| +0x30 | u32 | 0 x21, 20 x1 | | |
| +0x34 | reference | Area, set on 22 of 22: `gal_#_clue_#`, `map_clue_#`, `axiom_city_clue_#` | | |
| +0x38 | u32 | 272 x3, 256 x3, 96 x2, 128 x2, 144 x2 ... (15 values) | | |
| +0x3C | u32 | 384 x3, 256 x3, 336 x2, 176 x2, 16 x1 ... (16 values) | | |
| +0x40 | u32 | 80 x4, 128 x3, 112 x2, 144 x2, 96 x2 ... (13 values) | | |
| +0x44 | u32 | 128 x4, 64 x2, 176 x2, 112 x2, 96 x2 ... (13 values) | | |
| +0x4C | reference | Area, set on 20 of 22: `gal_#_clue_#`, `axiom_city_clue_#`, `axiom_tech_center_clue_#` | | |
| +0x50 | u32 | 0 x2, 192 x2, 256 x2, 304 x2, 80 x2 ... (17 values) | | |
| +0x54 | u32 | 240 x3, 256 x3, 0 x2, 320 x2, 272 x2 ... (15 values) | | |
| +0x58 | u32 | 64 x7, 112 x4, 144 x3, 0 x2, 48 x2 ... (9 values) | | |
| +0x5C | u32 | 64 x4, 96 x3, 112 x3, 0 x2, 80 x2 ... (12 values) | | |
| +0x60 | u32 | 2 x20, 0 x2 | | |
| +0x64 | reference | Area, set on 20 of 22: `gal_#_clue_#`, `axiom_city_clue_#`, `axiom_tech_center_clue_#` | | |
| +0x68 | u32 | 112 x3, 96 x3, 208 x3, 384 x3, 0 x2 ... (13 values) | | |
| +0x6C | u32 | 240 x3, 352 x3, 0 x2, 128 x2, 208 x2 ... (13 values) | | |
| +0x70 | u32 | 64 x6, 0 x2, 128 x2, 112 x2, 96 x1 ... (14 values) | | |
| +0x74 | u32 | 48 x4, 112 x4, 176 x3, 0 x2, 64 x2 ... (12 values) | | |
| +0x78 | u32 | 3 x20, 0 x2 | | |
| +0x7C | reference | Area, set on 19 of 22: `gal_#_clue_#`, `axiom_city_clue_#`, `axiom_tech_center_clue_#` | | |
| +0x80 | u32 | 0 x4, 112 x4, 128 x2, 208 x1, 144 x1 ... (15 values) | | |
| +0x84 | u32 | 0 x3, 112 x2, 240 x2, 216 x2, 320 x1 ... (17 values) | | |
| +0x88 | u32 | 80 x5, 64 x4, 0 x3, 112 x3, 72 x2 ... (9 values) | | |
| +0x8C | u32 | 80 x6, 0 x3, 112 x3, 64 x2, 144 x2 ... (11 values) | | |
| +0x90 | u32 | 4 x19, 0 x3 | | |
| +0x94 | reference | Area, set on 19 of 22: `gal_#_clue_#`, `axiom_city_clue_#`, `axiom_tech_center_clue_#` | | |
| +0x98 | u32 | 0 x3, 112 x3, 272 x3, 208 x2, 32 x2 ... (14 values) | | |
| +0x9C | u32 | 0 x5, 304 x3, 256 x3, 80 x2, 64 x2 ... (12 values) | | |
| +0xA0 | u32 | 112 x4, 80 x4, 0 x3, 64 x3, 96 x3 ... (8 values) | | |
| +0xA4 | u32 | 128 x5, 0 x3, 80 x3, 144 x3, 96 x3 ... (10 values) | | |
| +0xA8 | u32 | 5 x19, 0 x3 | | |
| +0xAC | reference | Area, set on 16 of 22: `axiom_city_clue_#`, `gal_#_clue_#`, `gimlick_valley_present_clue_#` | | |
| +0xB0 | u32 | 0 x6, 272 x3, 144 x2, 336 x2, 240 x2 ... (12 values) | | |
| +0xB4 | u32 | 0 x6, 304 x3, 112 x2, 352 x2, 80 x2 ... (12 values) | | |
| +0xB8 | u32 | 0 x6, 32 x3, 112 x2, 48 x2, 160 x2 ... (11 values) | | |
| +0xBC | u32 | 0 x6, 48 x3, 80 x2, 112 x2, 32 x2 ... (12 values) | | |
| +0xC0 | u32 | 6 x16, 0 x6 | | |
| +0xC4 | reference | Area, set on 15 of 22: `axiom_city_clue_#`, `gal_#_clue_#`, `gimlick_valley_present_clue_#` | | |
| +0xC8 | u32 | 0 x8, 208 x3, 304 x2, 224 x2, 96 x1 ... (11 values) | | |
| +0xCC | u32 | 0 x7, 256 x3, 352 x3, 128 x2, 336 x2 ... (10 values) | | |
| +0xD0 | u32 | 0 x7, 64 x4, 112 x3, 80 x2, 32 x2 ... (8 values) | | |
| +0xD4 | u32 | 0 x7, 96 x3, 64 x2, 128 x2, 80 x2 ... (10 values) | | |
| +0xD8 | u32 | 7 x15, 0 x7 | | |
| +0xDC | reference | Area, set on 14 of 22: `axiom_city_clue_#`, `gal_#_clue_#`, `gimlick_valley_past_clue_#` | | |
| +0xE0 | u32 | 0 x8, 112 x2, 368 x2, 384 x1, 80 x1 ... (13 values) | | |
| +0xE4 | u32 | 0 x7, 160 x2, 48 x2, 304 x2, 320 x1 ... (13 values) | | |
| +0xE8 | u32 | 0 x7, 64 x3, 80 x2, 144 x2, 224 x2 ... (10 values) | | |
| +0xEC | u32 | 0 x7, 80 x5, 160 x2, 64 x2, 32 x1 ... (10 values) | | |
| +0xF0 | u32 | 8 x15, 0 x7 | | |
| +0xF4 | reference | Area, set on 12 of 22: `axiom_city_clue_#`, `gal_#_clue_#`, `cg_a_clue_#` | | |
| +0xF8 | u32 | 0 x9, 288 x2, 80 x1, 144 x1, 344 x1 ... (13 values) | | |
| +0xFC | u32 | 0 x9, 192 x2, 320 x2, 272 x2, 208 x1 ... (11 values) | | |
| +0x100 | u32 | 0 x9, 64 x4, 48 x3, 80 x2, 144 x1 ... (8 values) | | |
| +0x104 | u32 | 0 x9, 64 x6, 112 x1, 56 x1, 80 x1 ... (9 values) | | |
| +0x108 | u32 | 9 x13, 0 x9 | | |
| +0x10C | reference | Area, set on 10 of 22: `axiom_city_clue_#`, `gal_#_clue_#`, `cg_a_clue_#` | | |
| +0x110 | u32 | 0 x11, 176 x2, 352 x2, 96 x2, 16 x1 ... (9 values) | | |
| +0x114 | u32 | 0 x11, 208 x3, 64 x1, 120 x1, 112 x1 ... (10 values) | | |
| +0x118 | u32 | 0 x11, 112 x3, 64 x3, 72 x2, 96 x1 ... (7 values) | | |
| +0x11C | u32 | 0 x11, 144 x2, 48 x2, 176 x1, 96 x1 ... (10 values) | | |
| +0x120 | u32 | 0 x11, 10 x11 | | |
| +0x124 | reference | Area, set on 9 of 22: `axiom_city_clue_#`, `gal_#_clue_#`, `cg_a_clue_#` | | |
| +0x128 | u32 | 0 x13, 80 x2, 128 x1, 400 x1, 96 x1 ... (9 values) | | |
| +0x12C | u32 | 0 x13, 304 x2, 176 x1, 240 x1, 88 x1 ... (9 values) | | |
| +0x130 | u32 | 0 x13, 48 x3, 80 x2, 64 x1, 40 x1 ... (7 values) | | |
| +0x134 | u32 | 0 x13, 80 x4, 48 x1, 56 x1, 49 x1 ... (7 values) | | |
| +0x138 | u32 | 0 x13, 11 x9 | | |
| +0x13C | reference | Area, set on 7 of 22: `axiom_city_clue_#`, `gal_#_clue_#`, `cg_b_clue_#` | | |
| +0x140 | u32 | 0 x15, 128 x2, 64 x1, 96 x1, 320 x1 ... (7 values) | | |
| +0x144 | u32 | 0 x15, 176 x2, 256 x2, 304 x1, 352 x1 ... (6 values) | | |
| +0x148 | u32 | 0 x15, 96 x3, 80 x2, 64 x1, 128 x1 | | |
| +0x14C | u32 | 0 x15, 64 x2, 112 x2, 128 x1, 80 x1 ... (6 values) | | |
| +0x150 | u32 | 0 x15, 12 x7 | | |
| +0x154 | reference | Area, set on 5 of 22: `axiom_city_clue_#`, `cg_b_clue_#`, `krl_clue_#` | | |
| +0x158 | u32 | 0 x17, 64 x1, 128 x1, 80 x1, 368 x1 ... (6 values) | | |
| +0x15C | u32 | 0 x16, 64 x1, 320 x1, 288 x1, 304 x1 ... (7 values) | | |
| +0x160 | u32 | 0 x16, 48 x2, 96 x1, 16 x1, 128 x1 ... (6 values) | | |
| +0x164 | u32 | 0 x16, 112 x1, 48 x1, 16 x1, 80 x1 ... (7 values) | | |
| +0x168 | u32 | 0 x16, 13 x6 | | |
| +0x16C | reference | Area, set on 4 of 22: `axiom_city_clue_#`, `cg_b_clue_#`, `krl_clue_#` | | |
| +0x170 | u32 | 0 x18, 128 x2, 256 x1, 352 x1 | | |
| +0x174 | u32 | 0 x18, 128 x1, 320 x1, 336 x1, 32 x1 | | |
| +0x178 | u32 | 0 x18, 64 x1, 112 x1, 48 x1, 160 x1 | | |
| +0x17C | u32 | 0 x18, 64 x1, 136 x1, 48 x1, 160 x1 | | |
| +0x180 | u32 | 0 x18, 14 x4 | | |
| +0x184 | reference | Area, set on 3 of 22: `axiom_city_clue_#`, `krl_clue_#`, `mol_clue_#` | | |
| +0x188 | u32 | 0 x19, 128 x1, 336 x1, 320 x1 | | |
| +0x18C | u32 | 0 x19, 64 x1, 288 x1, 144 x1 | | |
| +0x190 | u32 | 0 x19, 64 x2, 80 x1 | | |
| +0x194 | u32 | 0 x19, 80 x2, 64 x1 | | |
| +0x198 | u32 | 0 x19, 15 x3 | | |
| +0x19C | reference | Area, set on 3 of 22: `axiom_city_clue_#`, `krl_clue_#`, `mol_clue_#` | | |
| +0x1A0 | u32 | 0 x19, 144 x1, 176 x1, 224 x1 | | |
| +0x1A4 | u32 | 0 x19, 80 x1, 208 x1, 128 x1 | | |
| +0x1A8 | u32 | 0 x19, 48 x1, 64 x1, 112 x1 | | |
| +0x1AC | u32 | 0 x19, 128 x1, 48 x1, 112 x1 | | |
| +0x1B0 | u32 | 0 x19, 16 x3 | | |
| +0x1B4 | reference | Area, set on 1 of 22: `mol_clue_#` | | |
| +0x1B8 | u32 | 0 x21, 208 x1 | | |
| +0x1BC | u32 | 0 x21, 160 x1 | | |
| +0x1C0 | u32 | 0 x21, 80 x1 | | |
| +0x1C4 | u32 | 0 x21, 80 x1 | | |
| +0x1C8 | u32 | 0 x21, 17 x1 | | |
| +0x1CC | reference | Area, set on 1 of 22: `mol_clue_#` | | |
| +0x1D0 | u32 | 0 x21, 144 x1 | | |
| +0x1D4 | u32 | 0 x21, 64 x1 | | |
| +0x1D8 | u32 | 0 x21, 160 x1 | | |
| +0x1DC | u32 | 0 x21, 160 x1 | | |
| +0x1E0 | u32 | 0 x21, 18 x1 | | |
| +0x1E4 | reference | Area, set on 1 of 22: `mol_clue_#` | | |
| +0x1E8 | u32 | 0 x21, 224 x1 | | |
| +0x1EC | u32 | 0 x21, 160 x1 | | |
| +0x1F0 | u32 | 0 x21, 128 x1 | | |
| +0x1F4 | u32 | 0 x21, 128 x1 | | |
| +0x1F8 | u32 | 0 x21, 19 x1 | | |

Same on every instance: +0x48 = 1.

### 0x4D3 NavClue_NavPoint

Parent 0x11D ClueBase; own fields +0x14..+0x38; 193 placed in the game (valkyrie_fleet); e.g. `tower_one_#dnav_point_far_#`, `tumbler_#_welcome_commitee_nav_point_#`, `boss_entry_#dnp_#`.

No instance sets anything in the class's own range.

### 0x4D4 NavClue_NavNetwork

Parent 0x11D ClueBase; own fields +0x14..+0x124; 33 placed in the game (valkyrie_fleet); e.g. `tumbler_#_welcome_commitee_nav_network_L_#`, `tumbler_#_welcome_commitee_nav_network_R_#`, `tower_#_center_#dnn_#`; own methods: fix-up 0x0015AFAC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | Scent, set on 33 of 33: `tumbler_#_welcome_commitee_nav_point_layer_L_#`, `tumbler_#_welcome_commitee_nav_point_layer_R_#`, `tower_#_center_#dnn_scent_#` | Reference slot: resolved by the class's fix-up | E |
| +0x18 | reference | unset on all 33 | Reference slot: resolved by the class's fix-up | E |
| +0x1C | f32 | 0.5 x29, 0.2 x4 | | |

### 0x4D6 cluesupersoakerfillup

Parent 0x11D ClueBase; own fields +0x14..+0x40; 32 placed in the game (5 levels); e.g. `ClueSuperSoakerFillUp#`, `waterfill#`, `water_soaker_clue_#`; own methods: fix-up 0x0015B074.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x18 | reference | Path, set on 32 of 32: `krell_water_soakup#`, `water_soaker_clue_path_#`, `watercurve#` | Reference slot: resolved by the class's fix-up | E |
| +0x34 | f32 | 4 x10, 2.5 x9, 2 x6, 3 x4, 5 x3 | | |

Same on every instance: +0x14 = 1.

### 0x4D9 ClueFirePoint

Parent 0x11D ClueBase; own fields +0x14..+0x14; 25 placed in the game (galacton_ship, krell_canyon); e.g. `ClueFirePoint#`, `magboot_cell#_fp#`.

No instance sets anything in the class's own range.

### 0x59D ClueSwarmBridge

Parent 0x11D ClueBase; own fields +0x14..+0x1C; 2 placed in the game (krell_canyon); e.g. `ClueSwarmBridge#`; own methods: fix-up 0x0015B02C.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | unset on all 2 | Reference slot: resolved by the class's fix-up | E |
| +0x18 | reference | Path, set on 2 of 2: `swarm_bridge_hindge_#` | Reference slot: resolved by the class's fix-up | E |

### 0x5DC clue_grindvine_patch

Parent 0x11D ClueBase; own fields +0x14..+0x14; 6 placed in the game (gimlick_valley, tombli); e.g. `CLUE_GRINDVINE_PATCH#`, `GRINDVINE_SEED_MUD#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x622 DamageDistributerVolleyInfo

Parent 0x11D ClueBase; own fields +0x14..+0x34; 120 placed in the game (gimlick_valley, krell_canyon); e.g. `Clue_Strafer#a`, `clue_strafe_run_#_fire`, `Clue_Strafer#b`; own methods: fix-up 0x004509F0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 3 x86, 4 x32, 0 x2 | | |
| +0x18 | u32 | 0 x90, 1 x18, 3 x12 | | |
| +0x1C | f32 | 1 x68, 3 x8, 2.5 x5, 2 x5, 2.2 x5 ... (22 values) | | |
| +0x20 | f32 | 45 x40, 20 x39, 30 x24, 60 x11, 65 x2 ... (8 values) | | |
| +0x24 | reference | Moby, set on 15 of 120: `ratchet`, `JunkShip_victim#` | Reference slot: resolved by the class's fix-up | E |
| +0x28 | reference | Volume, set on 63 of 120: `vol_strafe_run_#_target`, `RouteC#_#TargetVol`, `RouteD#_#TargetVol` | Reference slot: resolved by the class's fix-up | E |
| +0x2C | reference | Area, set on 4 of 120: `AMBIENT_TARGET_AREA_CENTER_PATH_ARGOS`, `AMBIENT_TARGET_AREA_LEFT_PATH_ARGOS`, `AMBIENT_TARGET_AREA_RIGHT_PATH_ARGOS` | Reference slot: resolved by the class's fix-up | E |
| +0x30 | reference | Volume, set on 18 of 120: `vol_strafe_run_#_start`, `shiptarget_right#_#`, `shiptarget_dam#_#` | Reference slot: resolved by the class's fix-up | E |

### 0x69F clueazimuthship

Parent 0x11D ClueBase; own fields +0x14..+0x2C; 33 placed in the game (axiom_city, krell_canyon); e.g. `ClueAzimuthShip#`, `clue_azimuth_tankB#strafe#_fire`, `clue_azimuth_tankB#strafe#_altfire`; own methods: fix-up 0x00453C94.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 2 x17, 3 x7, 0 x7, 1 x2 | | |
| +0x18 | f32 | 1.5 x8, 2 x6, 0.5 x5, 1.4 x4, 1.8 x3 ... (11 values) | | |
| +0x1C | f32 | 10 x12, 5 x9, 1 x3, 0 x3, 0.1 x3 ... (7 values) | | |
| +0x20 | f32 | 20 x23, 45 x4, 35 x2, 40 x2, 30 x1 ... (6 values) | | |
| +0x24 | reference | Moby, set on 10 of 33: `VX#`, `agorian_turret_victim#`, `DROPSHIP_AZIMUTH_TARGET` | Reference slot: resolved by the class's fix-up | E |
| +0x28 | reference | Volume, set on 19 of 33: `azimuth_dome_fire_vol#`, `vol_azimuth_tankB#strafe#_target`, `vol_azimuth_tankB#strafe#_alttarget` | Reference slot: resolved by the class's fix-up | E |

### 0x6CA clueallyjumpspot

Parent 0x11D ClueBase; own fields +0x14..+0x1C; 30 placed in the game (5 levels); e.g. `ClueAllyJumpSpot#`, `clue_azi_jump_island_#`, `azi_jump_spot_clue_#_#`; own methods: fix-up 0x0015AFF4.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | Volume, set on 30 of 30: `azimuth_grind_jump_landing#`, `allyjumpvol#`, `vol_azi_jump_target_#` | Reference slot: resolved by the class's fix-up | E |
| +0x18 | u32 | 0 x26, 1 x3, 2 x1 | | |

### 0x730 a_target

Parent 0x11D ClueBase; own fields +0x14..+0x18; 163 placed in the game (5 levels); e.g. `A#_Target#`, `ClueAmbientTarget#`, `ambient_target_#_#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x773 cluehbsambientmovement

Parent 0x11D ClueBase; own fields +0x14..+0x18; 10 placed in the game (great_clock_a, great_clock_b, insomniac_museum); e.g. `ClueHBSAmbientMovement#_#`, `ClueHBSAmbOpenCam#_#`, `ClueHBSbounce#_#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | f32 | 10 x3, 30 x3, 25 x2, 20 x1, 2 x1 | | |

### 0x774 cluehbsbounce

Parent 0x11D ClueBase; own fields +0x14..+0x20; 11 placed in the game (great_clock_a, great_clock_b); e.g. `ClueHBSAmbientAction#_#`, `ClueHBSdrop#_#`, `ClueHBSbounce#_#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 0 x8, 2 x1, 4 x1, 3 x1 | | |

Same on every instance: +0x18 = 1, +0x1C = 1.

### 0x85B cluenoledgegrab

Parent 0x11D ClueBase; own fields +0x14..+0x14; 37 placed in the game (8 levels); e.g. `ClueNoLedgeGrab#`, `swingshot_room_noledgegrab#`, `azimuth_cage_noledgegrab`.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x860 clueduncanhoverboothack

Parent 0x11D ClueBase; own fields +0x14..+0x1C; 3 placed in the game (axiom_city); e.g. `ClueDuncanHoverBootHack#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | f32 | -13 x1, -14 x1, -40 x1 | | |
| +0x18 | f32 | 35 x1, 30 x1, 15 x1 | | |

### 0x8A3 navclueazimuthbackstab

Parent 0x11D ClueBase; own fields +0x14..+0x20; 6 placed in the game (great_clock_e); e.g. `NavClueAzimuthBackstab#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 2 x4, 1 x1, 0 x1 | | |
| +0x18 | f32 | 1 x5, 5 x1 | | |
| +0x1C | f32 | 3 x2, 2 x2, 1 x1, 10 x1 | | |

### 0x8B6 cluenowater

Parent 0x11D ClueBase; own fields +0x14..+0x14; 23 placed in the game (axiom_city, gimlick_valley, great_clock_b, krell_canyon); e.g. `ClueNoWater#`, `NO_COMBAT_CAM_HACK`.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

## Other controllers

### 0x13A AlarmController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x2C; 4 placed in the game (axiom_city, galacton_ship, nefarious_station); e.g. `tc_alarm_controller`, `alarmcontroller_noflash`, `AlarmController#`; own methods: fix-up 0x0034B444, Init 0x0034B48C, Update 0x0034B160.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | unset on all 4 | Reference slot: resolved by the class's fix-up | E |
| +0x18 | reference | unset on all 4 | Reference slot: resolved by the class's fix-up | E |
| +0x1C | f32 | 1.5 x3, 10000 x1 | | |
| +0x20 | f32 | 0.2 x3, 0.15 x1 | | |

Same on every instance: +0x24 = 0.05.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x28 u8.

### 0x211 MoveGameObjectController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x80; 1 placed in the game (molonoth); e.g. `MoveGameObjectController#`; own methods: Init 0x0034AD50, Update 0x0034AF0C.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x68 f32, +0x6C f32, +0x70 f32.

### 0x22E CamArgFixedZoom

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x68; 1 placed in the game (molonoth); e.g. `camera_hiddenBattleBots`; own methods: Init 0x0004AC70.

Same on every instance: +0x60 = 5, +0x64 = -1.

### 0x2D0 CameraSwayController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x48; 4 placed in the game (galacton_ship); e.g. `SWING_CAM_SWAY`, `ELEVATOR_CAM_SWAY`, `BOSS_CAM_SWAY`; own methods: fix-up 0x00344768, Init 0x003447A0, Update 0x003445EC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x34 | reference | Area, set on 4 of 4: `SWINGSHOT_VOL#`, `ELEVATOR_VOL#`, `BOSS_CHAMBER_VOL#` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x14 = 719, +0x38 = 3, +0x3C = 1, +0x40 = 16.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x18 u32, +0x1C f32, +0x20 f32, +0x24 f32, +0x28 f32, +0x2C u32, +0x30 u8, +0x44 u8.

### 0x2E2 camarggravtube

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x60; 2 placed in the game (galacton_ship); e.g. `CamArgGravTube#`; own methods: Init 0x0004AD00.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x371 EnemyGroupController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0xA0; 2 placed in the game (axiom_city); e.g. `EnemyGroupController#`; own methods: fix-up 0x00097B10, Init 0x00097B48.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x98 | u32 | 2 x1, 3 x1 | | |
| +0x9C | reference | Pod, set on 2 of 2: `TC_ENTRANCE_ENEMIES_WAVE#`, `TD_NW_ENEMIES` | Reference slot: resolved by the class's fix-up | E |

### 0x3CC swingcam

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x60; 1 placed in the game (insomniac_museum); e.g. `swingcam#`; own methods: Init 0x0004AE58.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x3EF WeatherController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0xA0; 5 placed in the game (gimlick_valley, tombli, valkyrie_fleet, zolar_forest); e.g. `WeatherController#`, `WeatherControllerIntro`, `WeatherController`; own methods: fix-up 0x0034A690, Init 0x0034A6C8, Update 0x0034AA44.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x20 | f32 | 5 x3, 0 x2 | | |
| +0x40 | f32 | 25 x2, 15 x1, 2 x1, 30 x1 | | |
| +0x44 | f32 | 1 x4, 0.5 x1 | | |
| +0x48 | f32 | 15 x2, 10 x1, 2 x1, 25 x1 | | |
| +0x4C | f32 | 1 x4, 0.75 x1 | | |
| +0x50 | f32 | 0.01 x4, 0.02 x1 | | |
| +0x54 | f32 | 0 x3, 15 x1, 3 x1 | | |
| +0x5C | u32 | 27 x4, 85 x1 | | |
| +0x60 | reference | Area, set on 2 of 5: `AREA_NO_RAIN`, `AREA_NOSNOW` | Reference slot: resolved by the class's fix-up | E |
| +0x70 | 4 bytes | 0x00010100 x3, 0x00000000 x2 | | |
| +0x80 | f32 | 0.25 x3, 0 x2 | | |
| +0x84 | f32 | 0.75 x3, 0 x2 | | |
| +0x88 | f32 | 0.75 x3, 0 x2 | | |
| +0x8C | f32 | 0.75 x3, 0 x2 | | |
| +0x90 | f32 | 0 x3, 0.4 x2 | | |
| +0x94 | f32 | 1 x3, 0.8 x2 | | |

Same on every instance: +0x30 = 0.5, +0x34 = 0.6, +0x38 = 0.7, +0x3C = 4, +0x58 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x64 u32, +0x68 f32, +0x6C u32, +0x98 u32.

### 0x472 VerticalFogController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x90; 30 placed in the game (9 levels); e.g. `VerticalFogController#`; own methods: fix-up 0x00349C60, Init 0x00349D14, Update 0x00349C04.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | Volume, set on 28 of 30: `volume#`, `w#`, `w` | Reference slot: resolved by the class's fix-up | E |
| +0x18 | 4 bytes | 0x00FFE100 x11, 0x00FFFF00 x5, 0x5AEBFF00 x3, 0x08131800 x2, 0x4EB2D000 x1 ... (13 values) | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1C f32, +0x20 f32, +0x30 f32, +0x34 f32, +0x38 f32, +0x40 f32, +0x44 f32, +0x48 f32, +0x50 f32, +0x54 f32, +0x58 f32, +0x60 f32, +0x64 f32, +0x68 f32, +0x80 u32.

### 0x713 zapper

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x28; 5 placed in the game (zolar_forest); e.g. `ZAPPER#`; own methods: fix-up 0x00423288, Init 0x00423264, Update 0x004232C0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | Pod, set on 5 of 5: `POD_ZAP#` | Reference slot: resolved by the class's fix-up | E |
| +0x18 | f32 | 0 x3, 2 x1, 3 x1 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1C f32, +0x20 u32, +0x24 u8.

### 0x72B poison_fog

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x50; 2 placed in the game (agorian_arena, valkyrie_fleet); e.g. `poison_fog`; own methods: Init 0x00344818, Update 0x00344858.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | f32 | 0 x1, 2.5 x1 | | |
| +0x18 | f32 | 0 x1, 5 x1 | | |
| +0x1C | f32 | 5 x1, 7 x1 | | |
| +0x24 | f32 | 0.6 x1, 0.3 x1 | | |

Same on every instance: +0x20 = 50, +0x2C = 0.6, +0x30 = 0.1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x28 f32, +0x34 f32, +0x38 f32, +0x3C f32, +0x40 f32, +0x44 f32, +0x48 f32, +0x4C f32.

### 0x73B electric_arc_master

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x8BC; 41 placed in the game (7 levels); e.g. `Electric_Arc_Master#`, `explosion_master#`, `dirt_ledge_mortar_barage_#second_Master_Ctrlr`; own methods: Init 0x0034B9C0, Update 0x0034BD50.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | f32 | 3 x21, 10 x7, 5 x4, 15 x3, 1 x3 ... (8 values) | | |
| +0x18 | f32 | 5000 x9, 50 x7, 1000 x5, 100 x4, 40 x2 ... (16 values) | | |
| +0x1C | f32 | 0 x16, 0.25 x5, 0.5 x3, 10 x3, 1.5 x2 ... (13 values) | | |
| +0x20 | f32 | 0.5 x9, 0.25 x6, 3 x4, 0.1 x4, 20 x3 ... (15 values) | | |
| +0x24 | f32 | 0 x8, 0.3 x6, 0.1 x4, 0.7 x4, 4 x3 ... (15 values) | | |
| +0x3C | f32 | -1 x36, 10 x1, 30 x1, 250 x1, 25 x1 ... (6 values) | | |
| +0x40 | f32 | -1 x36, 50 x2, 1000 x2, 200 x1 | | |
| +0x44 | f32 | 0 x37, 5 x3, 0.4 x1 | | |
| +0x48 | f32 | 0 x36, 10 x2, 0.75 x1, 15 x1, 2 x1 | | |
| +0x64 | f32 | -1 x37, 10 x1, 50 x1, 100 x1, 1 x1 | | |
| +0x68 | f32 | -1 x37, 50 x1, 100 x1, 500 x1, 1000 x1 | | |
| +0x6C | f32 | 0 x38, 0.4 x1, 7 x1, 1 x1 | | |
| +0x70 | f32 | 0 x37, 0.75 x1, 15 x1, 2 x1, 2.5 x1 | | |
| +0x8C | f32 | -1 x40, 10 x1 | | |
| +0x90 | f32 | -1 x40, 50 x1 | | |
| +0x94 | f32 | 0 x40, 0.4 x1 | | |
| +0x98 | f32 | 0 x40, 0.75 x1 | | |
| +0x8B8 | u32 | 0 x35, 1 x6 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x28 u32, +0x2C u32, +0x30 u32, +0x8B4 u32.

### 0x789 cam_gcb_subcon_puzzle_cham_button

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x78; 63 placed in the game (5 levels); e.g. `CAM_GCB_SUBCON_PUZZLE#_CHAM#_BUTTON`, `CAM_GCB_SUBCON_PUZZLE#_CHAM#`, `e#_cam_#`; own methods: Init 0x0004AC64.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x64 | f32 | 10 x62, 30 x1 | | |
| +0x68 | f32 | 10 x62, 30 x1 | | |
| +0x6C | f32 | 10 x62, 30 x1 | | |
| +0x70 | f32 | 10 x62, 30 x1 | | |

### 0x79E dialogue_fongoid_past

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0xD8; 42 placed in the game (7 levels); e.g. `DIALOGUE_FONGOID_PAST#`, `DIALOGUE_PRISONER#`, `CTRL_TERA#_AMBIENT_DIALOGUE`; own methods: Init 0x0034CEC8.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x94 u32.

### 0x7A3 hangar_chunk

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0xC8; 118 placed in the game (galacton_ship, great_clock_a, great_clock_e); e.g. `HANGAR_CHUNK#`, `CheapChunk#`, `SlowCheapChunk#`; own methods: Init 0x0034FEE8, Update 0x003506A0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x1C | u32 | 307 x63, 292 x54, 293 x1 | | |
| +0x28 | f32 | 0.3 x70, 1 x22, 0.2 x8, 0.4 x8, 0.5 x7 ... (6 values) | | |
| +0x2C | f32 | 0.15 x63, 0.05 x44, 0.1 x8, 0 x3 | | |
| +0x38 | u32 | 2 x110, 0 x8 | | |
| +0x3C | f32 | 30 x110, 60 x8 | | |
| +0x54 | f32 | 5 x79, 1 x22, 6 x7, 7 x5, 8 x5 | | |
| +0x58 | f32 | 0 x96, 1.5 x22 | | |
| +0x60 | f32 | 60 x107, 180 x8, 0 x3 | | |
| +0xB0 | 4 bytes | 0x00010000 x115, 0x00000100 x3 | | |

Same on every instance: +0x20 = 1, +0x24 = 2, +0x30 = 1, +0x40 = 1, +0x44 = 1, +0x48 = 1, +0x4C = 3, +0x5C = 360, +0x64 = 1, +0x68 = 1, +0x70 = 1, +0x74 = 1, +0x78 = 1, +0x7C = 1, +0x80 = 1, +0x84 = 1, +0x88 = 1, +0x8C = 1, +0x90 = 1, +0x94 = 1, +0x98 = 1, +0x9C = 1, +0xA0 = 1, +0xA4 = 1, +0xA8 = 1, +0xAC = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x14 f32, +0x18 f32, +0x34 u32, +0xB8 u32, +0xBC u8, +0xC0 f32, +0xC4 u8, +0xC5 u8.

### 0x7F8 timeslowcontroller

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x40; 13 placed in the game (6 levels); e.g. `TimeSlowController#`, `TIME_TINT#`, `TIME_SLOW_CONTROLLER`; own methods: fix-up 0x0034D5AC, Init 0x0034D544, Update 0x0034D34C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x18 | reference | Volume, set on 13 of 13: `TIME_TINT#`, `VOL_TIME_TINT_POS#`, `BRIDGE_BREAK_TIMESLOW_VOL` | Reference slot: resolved by the class's fix-up | E |
| +0x20 | 4 bytes | 0x00010000 x8, 0x00000000 x5 | | |
| +0x24 | f32 | 1 x10, 0.25 x2, 0.1 x1 | | |
| +0x28 | f32 | 10 x6, 2048 x5, 8 x1, 25 x1 | | |
| +0x2C | f32 | 2048 x5, 20 x3, 25 x3, 15 x1, 65 x1 | | |

Same on every instance: +0x30 = 0.01, +0x34 = 1.15, +0x38 = 1.6, +0x3C = 0.6.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x14 u32, +0x1C u32.

### 0x858 crash_fire

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x14; 1 placed in the game (zolar_forest); e.g. `CRASH_FIRE`; own methods: Init 0x00160A58, Update 0x00160768.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x8A9 infocontroller

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0xB8; 63 placed in the game (agorian_arena, insomniac_museum, tombli); e.g. `InfoController#`, `t#_ps#ratchet`, `t#_swimming`; own methods: fix-up 0x0034CCE4, Init 0x0034CD10, Update 0x0034CC3C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | u32 | 1934 x58, 2045 x1, 2043 x1, 2041 x1, 2039 x1 ... (6 values) | | |
| +0x74 | reference | Volume, set on 54 of 63: `ITEM_#_vol`, `ITEM_ps#ratchet_vol`, `ITEM_dicecrate_vol` | | |
| +0x7C | f32 | 3 x56, 2 x6, 10 x1 | | |
| +0xB0 | u32 | 2217 x1, 2216 x1, 2215 x1, 2214 x1, 1891 x1 ... (63 values) | | |
| +0xB4 | u32 | 2046 x1, 2044 x1, 2042 x1, 2040 x1, 1483 x1 ... (63 values) | | |

Same on every instance: +0x90 = 2, +0xA0 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x18 u32, +0x1C u32, +0x24 u32, +0x60 u32, +0x64 f32, +0x78 f32, +0x80 f32, +0x84 u32, +0x88 u32, +0xA8 u32, +0xAC u8, +0xAD u8, +0xAE u8, +0xAF u8.

### 0x8B8 controller_nefarious_disguise

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x1DC; 1 placed in the game (nefarious_station); e.g. `controller_nefarious_disguise`; own methods: Init 0x001C3A1C, Update 0x001C4038.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x14 u32, +0x18 u16, +0x20 f32, +0x70 u32, +0x74 u32, +0x1D4 u32, +0x1DA u8.

## Other Mobys

### 0x141

Parent 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x840..+0x1150; not placed itself; values below are over its 16 placed descendants; own methods: fix-up 0x00011470, Init 0x000114B4, Update 0x000119A8.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xB70 | u32 | 0 on all 16 | Script: written by `ally_setawareness` | E S |
| +0xB78 | u32 | 0 on all 16 | Script: written by `ally_addautotargetvolume`, `ally_clearautotargetvolume` | E S |
| +0xB80 | u32 | 0 on all 16 | Script: written by `ally_settargetvolume`, `azimuth_throw_bomb` | E S |
| +0xB90 | u32 | 0 on all 16 | Script: written by `ally_setfirepoints` | E S |
| +0xC68 | u32 | 0 on all 16 | Script: written by `ally_setattitude` | E S |
| +0xC70 | u32 | 0 on all 16 | Home area: Script: written by `ally_set_home_area`, `ally_clear_home_area` | E S |
| +0xC76 | u8 | 0 on all 16 | Script: written by `ally_followpath`, `ally_followpathhover`, `ally_gotopoint`, `ally_gotopointhover` ... | E S |
| +0x1120 | u16 | 0 on all 16 | Script: read by `queue_ally_followpath`, `queue_ally_followpathhover` | E S |
| +0x1122 | u16 | 0 on all 16 | Script: written by `queue_ally_followpath`, `queue_ally_followpathhover` | E S |
| +0x1124 | u32 | 0 on all 16 | Script: written by `ally_followpathhover`, `ally_gotopointhover` | E S |
| +0x1128 | u32 | 0 on all 16 | Script: written by `ally_followpath`, `ally_followpathhover`, `ally_cancel_script_command` | E S |
| +0x112C | u32 | 0 on all 16 | Script: written by `ally_gotopoint`, `ally_gotopointhover`, `qwark_startled_goto` | E S |
| +0x1130 | u32 | 0 on all 16 | Script: written by `ally_followpathhover`, `ally_gotopointhover` | E S |
| +0x1138 | u32 | 0 on all 16 | Script: written by `ally_followpath`, `ally_followpathhover` | E S |
| +0x113C | u16 | 0 on all 16 | Script: written by `ally_followpath`, `ally_followpathhover`, `ally_cancel_script_command` | E S |
| +0x113E | u16 | 0 on all 16 | Script: written by `ally_followpath`, `ally_followpathhover` | E S |
| +0x1140 | u16 | 0 on all 16 | Script: written by `ally_ranged_only` | E S |
| +0x1142 | u16 | 0 on all 16 | Script: written by `ally_settargetvolume` | E S |
| +0x1144 | f32 | 0 on all 16 | Hover speed scale: Script: written by `ally_set_hover_speed_scale` | E S |
| +0x1148 | u32 | 0 on all 16 | Face moby: Script: written by `ally_set_face_moby`, `ally_clear_face_moby` | E S |

Same on every instance: +0xBB4 = 1, +0xC28 = 442, +0xC34 = 3, +0xC48 = 2, +0xC58 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x890 f32, +0x894 f32, +0x898 f32, +0x8A0 f32, +0x8A4 f32, +0x8A8 f32, +0x8B0 f32, +0x8B4 f32, +0x8B8 f32, +0x8C0 u32, +0x8C4 u32, +0x8C8 u16, +0x8CA u16, +0x8CC u16, +0x8CE u16, +0x8D0 f32, +0x8D4 f32, +0x8D8 f32, +0x8DC u32, +0x8E0 u32, +0x8FC u32, +0x900 u32, +0x908 u32, +0xAE0 u32, +0xAFC f32, +0xB00 u32, +0xB04 u32, +0xB5C u32, +0xB74 u32, +0xB7C u32, +0xB84 u32, +0xB88 u32, +0xB8C u16, +0xB8E u16, +0xBD0 u32, +0xBD4 u32, +0xBDC u32, +0xC18 u32, +0xC1C f32, +0xC2C u32, +0xC30 f32, +0xC38 f32, +0xC3C u32, +0xC40 u32, +0xC60 u32, +0xC64 u8, +0xC65 u8, +0xC66 u8, +0xC67 u8, +0xC78 u32, +0x1134 f32.

### 0x154 WeaponBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x170; not placed itself; values below are over its 2 placed descendants; own methods: Init 0x0046C988, Update 0x0046D8C0.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x70 u32, +0x74 u32, +0x90 f32, +0x9C f32, +0xA0 u32, +0xA4 u32, +0xB0 f32, +0xB4 f32, +0xB8 f32, +0xBC f32, +0xC0 f32, +0xC4 f32, +0xC8 f32, +0xCC f32, +0xD0 f32, +0xD4 f32, +0xD8 f32, +0xDC f32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0xF0 f32, +0xF4 f32, +0xF8 f32, +0xFC f32, +0x108 u32, +0x10C f32, +0x158 u32, +0x160 u8, +0x164 u32.

### 0x185 RotaterBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x68; not placed itself; values below are over its 1 placed descendants; own methods: Init 0x0037BE98.

Same on every instance: +0x5C = 1, +0x60 = -1, +0x64 = 20.

### 0x192 clank

Parent 0xF6 HERO_HeroCharacter > 0xE5 HERO_HeroBase > 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x1AB0..+0xF7A40; 5 placed in the game (5 levels); e.g. `clank`; own methods: Init 0x001C6744, Update 0x001C5EA8.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x1AC0 = 310, +0x1D70 = 12, +0x1D74 = 12, +0x1D78 = 6, +0x1D7C = 12.5664, +0x1D80 = 12.5664, +0x1D84 = 6.28319, +0x1D88 = 0.5, +0x1D8C = 0.15, +0x1D90 = -0.5, +0x1D94 = 0.872665, +0x1ED4 = -25, +0x1ED8 = -50, +0x1EDC = 1, +0xF7570 = 1526, +0xF76B0 = 221.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1AB0 u8, +0x1B54 u16, +0x1B56 u8, +0x1B57 u8, +0x1D58 f32, +0x1F60 f32, +0x1F90 u32, +0x1FB0 f32, +0x1FF4 u32, +0x1FF8 u16, +0x1FFA u16, +0x2030 u32, +0x203C u16, +0x2040 u32, +0x2044 u32, +0x2048 u32, +0x204C u32, +0x2050 u32, +0x2054 u32, +0x2060 f32, +0x2064 f32, +0x2068 f32, +0x206C f32, +0x2070 u32, +0x2074 u32, +0x2078 u32, +0x207C f32, +0x2080 f32, +0x2084 u32, +0x2088 f32, +0x208C u32, +0x2090 u32, +0x2094 f32, +0x2098 u32, +0x209C u32, +0x20A0 u32, +0x20A4 f32, +0x20A8 f32, +0x20AC f32, +0x20C0 u32, +0x20C4 f32, +0x20C8 f32, +0x20CC u32, +0x20D0 u32, +0x20D4 u32, +0x20D8 f32, +0x20DC f32, +0x20E0 f32, +0x20E4 f32, +0x20EC u16, +0x20EE u16.

### 0x198 smuggler

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD30; 1 placed in the game (axiom_city); e.g. `SMUGGLER`; own methods: fix-up 0x002B952C, Init 0x002B9570.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xD24 | reference | unset on all 1 | Reference slot: resolved by the class's fix-up | E |

### 0x19F MonitorBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; not placed itself; values below are over its 27 placed descendants; own methods: Init 0x0039F304, Update 0x0039F2D0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | 4 bytes | 0x00010000 x12, 0x00020000 x12, 0x00020001 x3 | | |

### 0x213 ButtonPressure

Parent 0x541 Button > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x78..+0x180; 6 placed in the game (insomniac_museum, zolar_forest); e.g. `vic_zahntile_start#`, `boom_button`; own methods: fix-up 0x00359898, Init 0x00359634, Update 0x003598D0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x138 | reference | Moby, set on 1 of 70: `gcc_puzzle#_floatingplat` | Reference slot: resolved by the class's fix-up | E |
| +0x160 | s32 | 0 x64, -1 x3, 2 x2, 1 x1 | | |
| +0x164 | f32 | 0.75 x64, 1 x6 | | |
| +0x168 | f32 | 0.08 x64, 0.07 x3, 0.2 x2, 0.071 x1 | | |
| +0x16C | f32 | 0.25 x64, 0.7 x4, 1 x2 | | |
| +0x170 | f32 | 0.2 x64, 0 x3, 5 x2, 0.5 x1 | | |
| +0x178 | 4 bytes | 0x00000000 x65, 0x01000000 x5 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xB0 f32, +0xB4 f32, +0xB8 f32, +0xBC f32, +0xC0 f32, +0xC4 f32, +0xC8 f32, +0xD0 f32, +0xD4 f32, +0xD8 f32, +0xDC f32, +0xE0 u32, +0xE4 u32, +0xE8 u32, +0xF0 u32, +0xF4 u32, +0xF8 u32, +0xFC u32, +0x104 u32, +0x114 u32, +0x135 u8, +0x136 u8, +0x140 f32, +0x144 f32, +0x148 f32, +0x14C f32, +0x150 f32, +0x154 f32, +0x158 f32, +0x15C f32, +0x174 f32.

### 0x21B PickupBolt

Parent 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xE0..+0xF0; 305 placed in the game (11 levels); e.g. `gearBolt#`, `bolt#`, `crumb#`; own methods: Init 0x004BBC84.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xE4 | f32 | 3 x160, 2 x107, 1.5 x20, 1 x18 | | |

Same on every instance: +0xE0 = 14.

### 0x21D PlatformBuoyant

Parent 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x120..+0x1E0; 5 placed in the game (gimlick_valley); e.g. `tb_gimlick_floating_debri_#_#`; own methods: fix-up 0x00367C70, Init 0x00367B2C, Update 0x00367CA8.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x180 | reference | Clue, set on 5 of 5: `ClueWaterPresent` | Reference slot: resolved by the class's fix-up. Script: written by `platform_set_water` | E S |
| +0x1B0 | f32 | 1.5 x4, 6 x1 | | |
| +0x1BC | f32 | 0.025 x4, 0.5 x1 | | |

Same on every instance: +0x158 = 0.5, +0x15C = 18, +0x160 = 0.5, +0x164 = 3, +0x170 = -1.5, +0x174 = -1, +0x1B4 = 3, +0x1B8 = 2.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x120 f32, +0x124 f32, +0x128 f32, +0x12C f32, +0x130 f32, +0x134 f32, +0x138 f32, +0x13C f32, +0x140 f32, +0x144 f32, +0x148 f32, +0x14C f32, +0x150 f32, +0x154 f32, +0x168 f32, +0x16C f32, +0x1A0 f32, +0x1A4 f32, +0x1A8 f32, +0x1AC f32, +0x1C0 f32, +0x1C4 f32, +0x1C8 f32, +0x1CC f32, +0x1D0 f32, +0x1D4 u32.

### 0x225 BridgeBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x78; 2 placed in the game (axiom_city); e.g. `BRIDGE_TC#`; own methods: fix-up 0x003577B4, Init 0x003577FC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u16 | 0 on all 2 | Script: read by `bridge_is_extended` | E S |
| +0x6C | reference | unset on all 2 | Reference slot: resolved by the class's fix-up | E |
| +0x70 | reference | unset on all 2 | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x74 = 256.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x74 u8, +0x75 u8, +0x77 u8.

### 0x24D Taxi

Parent 0x24C PathFollower > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x560..+0x5E0; 6 placed in the game (axiom_city, nefarious_station, tombli); e.g. `taxi_to_fuelship#`, `taxi_theatre_district`, `taxi_spaceport`; own methods: Init 0x00464B80, Update 0x00464D14.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5BC | u32 | 450 x4, 2206 x1, 1099 x1 | | |
| +0x5C0 | u32 | 450 x5, 1099 x1 | | |

Same on every instance: +0x5C4 = 450, +0x5C8 = 450.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x564 u32, +0x568 u32, +0x56C u32, +0x578 u32, +0x5CC u32, +0x5D0 u32, +0x5D4 f32, +0x5D8 u8, +0x5D9 u8, +0x5DA u8.

### 0x25B PlatformGrappleExtending

Parent 0x4D PlatformGrappleBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x470..+0x490; 1 placed in the game (galacton_ship); e.g. `tether_pull_bridge#`; own methods: fix-up 0x00376B34, Init 0x00376B88.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x468 | reference | Path, set on 1 of 1: `wrench_pull_plat_path` | Reference slot: resolved by the class's fix-up | E |
| +0x46C | reference | unset on all 1 | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x474 = 1, +0x478 = 4.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x470 u32, +0x47C u32, +0x480 f32.

### 0x269 ArenaCrateSpawner

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xA0; 54 placed in the game (9 levels); e.g. `ammo_spawner#`, `ammo_spawnerA#_#`, `arena_ammoSpawner#`; own methods: Init 0x0033A304.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x80 | u32 | 0 x49, 1 x5 | | |
| +0x84 | f32 | 5 x31, 10 x20, 4 x2, 120 x1 | | |
| +0x88 | f32 | 8 x32, 10 x19, 15 x2, 180 x1 | | |
| +0x8C | f32 | 12 x30, 10 x18, 20 x3, 16 x2, 300 x1 | | |
| +0x9C | 4 bytes | 0x01000000 x50, 0x00000000 x4 | | |

Same on every instance: +0x6C = 1, +0x7C = 65535.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x60 u32, +0x64 u32, +0x68 u32, +0x70 u32, +0x74 u32, +0x78 u32, +0x7C u8, +0x94 u32, +0x98 f32.

### 0x26E SimpleElevator

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x190; 8 placed in the game (agorian_arena, galacton_ship, nefarious_station, tombli); e.g. `elevator_#`, `tj_gal_elevator#`, `elevator_platform`; own methods: fix-up 0x003618EC, Init 0x00361D0C, Update 0x00362788.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u16 | 0 on all 18 | Script: read by `elevator_is_moving` | E S |
| +0x6C | u32 | 0 x15, 3 x3 | | |
| +0x70 | u32 | 0 x13, 1 x4, 2 x1 | | |
| +0x74 | reference | Volume, set on 18 of 18: `elevator_#_top_node_vol`, `VOL_GCD_PUZZLE#_ELEV#_UPPER`, `elevator_#_bottom_node_vol` | Reference slot: resolved by the class's fix-up. Script: read by `elevator_warp_to_first_node` | E S |
| +0x78 | reference | Volume, set on 4 of 18: `lobby_enter_detect_vol`, `auto_door_#_open_vol`, `elevator_#_bottom_call_vol` | Reference slot: resolved by the class's fix-up | E |
| +0x7C | f32 | 3 x14, 0 x4 | | |
| +0x80 | u32 | 0 x15, 3 x2, 2 x1 | | |
| +0x84 | u32 | 0 x13, 2 x4, 1 x1 | | |
| +0x88 | reference | Volume, set on 18 of 18: `elevator_#_bottom_node_vol`, `VOL_GCD_PUZZLE#_ELEV#_LOWER`, `elevator_#_top_node_vol` | Reference slot: resolved by the class's fix-up. Script: read by `elevator_warp_to_second_node` | E S |
| +0x8C | reference | Volume, set on 4 of 18: `elevator_#_bottom_call_vol`, `lobby_exit_detect_vol`, `elevator_#_top_call_vol` | Reference slot: resolved by the class's fix-up | E |
| +0x90 | f32 | 3 x13, 0 x4, 10 x1 | | |
| +0x158 | u32 | 0 on all 18 | Script: written by `elevator_warp_to_first_node`, `elevator_warp_to_second_node` | E S |
| +0x15C | f32 | 0 on all 18 | Script: written by `elevator_move`, `elevator_move_to_first_node`, `elevator_move_to_second_node`, `elevator_warp_to_first_node` ... | E S |
| +0x160 | f32 | 75 x10, 4 x5, 8 x1, 3 x1, 1.5 x1 | | |
| +0x164 | f32 | 75 x8, 2 x6, 150 x2, 4 x1, 1 x1 | | |
| +0x168 | f32 | 0.5 x14, 1.5 x4 | | |
| +0x16C | u32 | 0 on all 18 | Script: written by `elevator_warp_to_first_node`, `elevator_warp_to_second_node` | E S |
| +0x178 | reference | Volume, set on 1 of 18: `lobby_enter_detect_vol` | Reference slot: resolved by the class's fix-up | E |
| +0x17C | u32 | 0 on all 18 | Script: written by `elevator_warp_to_first_node`, `elevator_warp_to_second_node` | E S |
| +0x180 | 4 bytes | 0x01000001 x10, 0x00000001 x4, 0x00010001 x2, 0x01000101 x1, 0x00010000 x1 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xB0 f32, +0xB4 f32, +0xB8 f32, +0xBC f32, +0xC0 f32, +0xC4 f32, +0xC8 f32, +0xCC f32, +0xD0 f32, +0xD4 f32, +0xD8 f32, +0xDC f32, +0x100 u32, +0x104 u32, +0x108 u32, +0x110 u32, +0x114 u32, +0x118 u32, +0x11C u32, +0x120 u32, +0x124 u32, +0x134 u32, +0x156 u8, +0x170 f32, +0x174 f32.

### 0x296 ActorBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x4A0; 4 placed in the game (great_clock_a); e.g. `NEFARIOUS_HOLOVID_#`, `LAWRENCE_HOLOVID_#`; own methods: Init 0x002E9D94, Update 0x002EA0CC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x6C | u32 | 0 on all 4 | Color: Script: written by `actor_set_color` | E S |
| +0x70 | u32 | 0 on all 4 | Idle face hero: Script: written by `actor_set_idle_face_hero` | E S |
| +0x74 | u32 | 0 on all 4 | Script: read by `actor_move` | E S |
| +0x8C | u32 | 0 on all 4 | Stance: Script: written by `actor_set_stance` | E S |
| +0x90 | u32 | 0 on all 4 | Stance: Script: written by `actor_set_stance` | E S |
| +0x94 | u32 | 0 on all 4 | Gesture: Script: written by `actor_set_gesture` | E S |
| +0xB0 | u32 | 0 on all 4 | Gesture: Script: written by `actor_set_gesture` | E S |
| +0xF0 | u32 | 0 on all 4 | Script: written by `actor_play_anim` | E S |
| +0x29C | u32 | 0 on all 4 | Script: written by `actor_move`, `actor_stop_moving`, `actor_play_anim` | E S |
| +0x3A0 | u32 | 1355 on all 4 | Script: read by `actor_move` | E S |
| +0x3E0 | u32 | 0 on all 4 | Script: written by `actor_move` | E S |
| +0x3F0 | u32 | 0 on all 4 | Script: read by `actor_move` | E S |
| +0x470 | f32 | 0 on all 4 | Script: written by `actor_move` | E S |
| +0x474 | f32 | 0 on all 4 | Script: written by `actor_move` | E S |
| +0x478 | f32 | 0 on all 4 | Script: written by `actor_move` | E S |
| +0x480 | u32 | 0 on all 4 | Script: written by `actor_move` | E S |

Same on every instance: +0x2A0 = 521.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x88 f32, +0x98 u32, +0x9C u32, +0xA0 u32, +0xA4 u32, +0xA8 u32, +0xAC u32, +0xB4 u32, +0xB8 u32, +0xBC u32, +0xC0 u32, +0xC4 u32, +0xC8 u32, +0xF4 u32, +0xF8 u32, +0xFC u32, +0x100 u32, +0x104 u32, +0x108 f32, +0x10C u32, +0x110 u32, +0x114 u32, +0x118 u32, +0x2A0 u16, +0x2A4 f32, +0x2A8 f32, +0x2AC u16, +0x3A4 f32, +0x3A8 f32, +0x3AC u16.

### 0x2C9 aphelion_zoni

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x540; 15 placed in the game (11 levels); e.g. `aphelion_zoni#`, `APHELION`, `aphelion_zoni_trip#_vorselon_defeated`; own methods: fix-up 0x00450FEC, Init 0x00451E0C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xB8 | u32 | 1098 x13, 324 x2 | | |
| +0xC4 | f32 | 3 x8, 6 x5, 0 x1, 5 x1 | | |
| +0xF8 | u16 | 0 on all 15 | Script: written by `hero_ship_fly_in`, `hero_ship_fly_out` | E S |
| +0xFA | u16 | 0 on all 15 | Script: written by `hero_ship_fly_in`, `hero_ship_fly_out` | E S |
| +0xFC | f32 | 0 on all 15 | Script: written by `hero_ship_fly_in`, `hero_ship_fly_out` | E S |
| +0x100 | f32 | 0 on all 15 | Script: written by `hero_ship_fly_in`, `hero_ship_fly_out` | E S |
| +0x104 | u16 | 0 on all 15 | Script: written by `hero_ship_fly_in`, `hero_ship_fly_out` | E S |
| +0x320 | u32 | 0 on all 15 | Script: read by `hero_ship_add_enter_cine`, `hero_ship_add_enter_movie` | E S |
| +0x350 | u32 | 0 on all 15 | Script: read by `hero_ship_add_enter_cine`, `hero_ship_add_enter_movie` | E S |
| +0x388 | u32 | 0 on all 15 | Script: read by `hero_ship_add_enter_cine`, `hero_ship_add_enter_movie` | E S |
| +0x400 | u32 | 0 on all 15 | Script: read by `hero_ship_add_exit_cine`, `hero_ship_add_exit_movie` | E S |
| +0x430 | u32 | 0 on all 15 | Script: read by `hero_ship_add_exit_cine`, `hero_ship_add_exit_movie` | E S |
| +0x468 | u32 | 0 on all 15 | Script: read by `hero_ship_add_exit_cine`, `hero_ship_add_exit_movie` | E S |
| +0x4E0 | u32 | 10 x4, 18 x3, 3 x3, 6 x2, 15 x2 ... (6 values) | | |
| +0x4E4 | u32 | 0 x11, 18 x1, 15 x1, 10 x1, 3 x1 | | |
| +0x4E8 | reference | unset on all 15 | Reference slot: resolved by the class's fix-up | E |
| +0x4EC | reference | unset on all 15 | Reference slot: resolved by the class's fix-up | E |
| +0x4F0 | f32 | 40 x4, 75 x3, 55 x2, 35 x2, 80 x1 ... (8 values) | | |
| +0x4F4 | f32 | 60 x6, 50 x3, 40 x3, 45 x2, 30 x1 | | |
| +0x4F8 | f32 | 0 x9, 0.2 x5, 0.25 x1 | | |
| +0x4FC | reference | Path, set on 12 of 15: `PATH_APHELION_INTRO`, `trip_#_intro_path`, `aphelion_intro_path` | Reference slot: resolved by the class's fix-up | E |
| +0x500 | reference | Controller, set on 12 of 15: `CAM_APHELION`, `trip_#_intro_cam`, `intro_swing_cam` | Reference slot: resolved by the class's fix-up | E |
| +0x504 | reference | Path, set on 14 of 15: `trip_#_outro_path#`, `PATH_APHELION_OUTRO`, `aphelion_outro_path` | Reference slot: resolved by the class's fix-up | E |
| +0x508 | reference | Controller, set on 14 of 15: `CAM_APHELION#`, `trip_#_outro_cam#`, `aphelion_outro_cam` | Reference slot: resolved by the class's fix-up | E |
| +0x50C | reference | Controller, set on 1 of 15: `trip_#_aphelion_zoni_cam` | Reference slot: resolved by the class's fix-up | E |
| +0x510 | u32 | 0 on all 15 | Script: written by `show_upgrade_screen` | E S |
| +0x524 | f32 | 0 x13, 0.2 x1, 0.5 x1 | | |
| +0x528 | f32 | 0 x14, -5 x1 | | |
| +0x531 | u8 | 0 on all 15 | Script: written by `hero_ship_fly_in`, `hero_ship_fly_out` | E S |
| +0x532 | u8 | 1 on all 15 | Script: written by `hero_ship_fly_in`, `hero_ship_fly_out` | E S |
| +0x534 | u32 | 0 x11, 256 x4 | | |

Same on every instance: +0xD8 = 2, +0xE8 = -1, +0x1B0 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 u32, +0x64 u32, +0x6C u32, +0xA8 u32, +0xAC f32, +0xBC u32, +0xC0 f32, +0xC8 f32, +0xCC u32, +0xD0 u32, +0xF0 u32, +0xF4 u8, +0xF5 u8, +0xF6 u8, +0xF7 u8, +0x120 u32, +0x12C u32, +0x140 f32, +0x198 f32, +0x1B4 u32, +0x1C0 u32, +0x1C4 u32, +0x1E0 f32, +0x1E4 f32, +0x1E8 f32, +0x1EC f32, +0x200 f32, +0x204 f32, +0x208 f32, +0x20C f32, +0x230 u32, +0x234 u32, +0x238 u32, +0x250 u16, +0x252 u16, +0x2C0 u32, +0x2C4 f32, +0x2CC u32, +0x2D0 f32, +0x2D4 u8, +0x2E0 f32, +0x2E4 f32, +0x2E8 f32, +0x2EC f32.

### 0x2D3 PlatformFloating

Parent 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x120..+0x210; 22 placed in the game (7 levels); e.g. `tb_gc_floating_bridge#`, `azimuths_ship#`, `azimuths_ship`; own methods: Init 0x0036941C, Update 0x003691B4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x130 | s32 | -1 x17, 3 x4, 5 x1 | | |
| +0x134 | f32 | 50 x17, 4 x4, 10 x1 | | |
| +0x138 | f32 | 3 x17, 0.2 x5 | | |
| +0x13C | s32 | -1 x17, 4 x4, 6 x1 | | |
| +0x140 | f32 | 50 x17, 4 x4, 10 x1 | | |
| +0x144 | f32 | 3 x17, 0.2 x5 | | |
| +0x148 | s32 | -1 x17, 5 x4, 7 x1 | | |
| +0x14C | f32 | 50 x17, 4 x4, 10 x1 | | |
| +0x150 | f32 | 3 x17, 0.2 x5 | | |
| +0x154 | f32 | 0.075 x17, 0.125 x5 | | |
| +0x158 | f32 | 0.22 x17, 0.275 x5 | | |
| +0x164 | u32 | 1 x21, 0 x1 | | |

Same on every instance: +0x160 = 0.5.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x120 f32, +0x124 f32, +0x128 f32, +0x12C f32, +0x15C f32, +0x168 u32, +0x180 u16, +0x182 u16, +0x184 f32, +0x188 f32, +0x1F0 u32, +0x1F4 f32, +0x1F8 f32, +0x1FC f32, +0x200 f32, +0x204 u8.

### 0x316 HazardFlameColumn

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x160; 99 placed in the game (galacton_ship, insomniac_museum); e.g. `fire_emitter#`, `fireL#`, `fireR#`; own methods: fix-up 0x0015F594, Init 0x0015F850.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x150 | reference | Path, set on 26 of 99: `fireR#path#`, `fireL#path`, `fireR#path` | Reference slot: resolved by the class's fix-up | E |
| +0x154 | 4 bytes | 0x01000000 x68, 0x00000000 x28, 0x00000100 x2, 0x01000100 x1 | | |
| +0x158 | f32 | 4 x62, 2 x36, 1.5 x1 | | |
| +0x15C | f32 | 1.8 x62, 0.5 x35, 2 x2 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x64 f32, +0x70 u32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xB0 u32, +0xB4 u32, +0xB8 u32, +0xBC u32, +0xC0 f32, +0xC4 f32, +0xC8 u32, +0xCC u32, +0xD0 f32, +0xD4 f32, +0xD8 f32, +0xDC f32, +0x120 f32, +0x124 f32, +0x128 f32, +0x12C u32, +0x130 f32, +0x134 f32, +0x140 u8, +0x141 u8, +0x144 f32, +0x148 u32, +0x14C f32.

### 0x319 GrindRailElectroRing

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 6 placed in the game (molonoth); e.g. `TEMP_AZIMUTH_BOMB#`, `electro_ring#`; own methods: Init 0x00425C28.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C f32.

### 0x358 tj_ago_arena_crowd

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x68; 562 placed in the game (agorian_arena); e.g. `tj_ago_arena_crowd_#_#`; own methods: Init 0x0033A674, Update 0x0033A7E8.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C f32, +0x60 f32, +0x64 u8, +0x65 u8.

### 0x366 TurretBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x440; not placed itself; values below are over its 6 placed descendants; own methods: Init 0x0037EC54, Update 0x0037EC00.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x3F4 | f32 | 30 x5, 25 x1 | | |
| +0x3F8 | f32 | -30 x3, -45 x2, -25 x1 | | |
| +0x400 | f32 | 180 x2, 90 x1, 60 x1, 10000 x1, 55 x1 | | |
| +0x420 | f32 | 0.6 x4, 0.25 x2 | | |
| +0x424 | f32 | 0.8 x4, 0.375 x2 | | |
| +0x428 | f32 | 1 x4, 0.5 x2 | | |
| +0x434 | u32 | 0 x3, 1 x3 | | |

Same on every instance: +0x404 = 0.92, +0x408 = 0.88.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xD0 u32, +0xD4 u32, +0xD8 u32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0xF0 u32, +0xF4 u32, +0xF8 u32, +0x180 u8, +0x210 u8, +0x2A0 u8, +0x330 u8, +0x3C0 u8, +0x3D0 u8, +0x3E8 f32, +0x3EC f32, +0x3F0 f32, +0x3FC f32, +0x40C f32, +0x410 f32, +0x414 u32, +0x418 u32, +0x41C f32, +0x42C u8, +0x42D u8, +0x42E u8, +0x42F u8, +0x430 u8.

### 0x36F PlatformGrappleTwoAxis

Parent 0x4D PlatformGrappleBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x470..+0x510; 6 placed in the game (insomniac_museum, krell_canyon); e.g. `wrench_platform_mag_twoaxis#`; own methods: fix-up 0x0037AD14, Init 0x0037B05C, Update 0x0037AFCC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4E0 | reference | unset on all 6 | Reference slot: resolved by the class's fix-up | E |
| +0x4E4 | reference | Volume, set on 1 of 6: `platform_gate` | Reference slot: resolved by the class's fix-up | E |
| +0x4E8 | f32 | 3 x5, 5 x1 | | |
| +0x500 | 4 bytes | 0x00010000 x3, 0x00000000 x3 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x470 u32, +0x474 u32, +0x478 f32, +0x47C u32, +0x48C u32, +0x4AE u8, +0x4B0 f32, +0x4B4 f32, +0x4B8 f32, +0x4BC f32, +0x4C0 f32, +0x4C4 f32, +0x4C8 f32, +0x4CC f32, +0x4D0 u32, +0x4D4 u32, +0x4D8 u32, +0x4DC u32, +0x4EC u32, +0x4F0 f32, +0x4F4 f32, +0x4FC f32.

### 0x373 TempCollisionDisable

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 5 placed in the game (insomniac_museum); e.g. `tj_ago_track_hoverboot_ramp#`; own methods: Init 0x0043E7E4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | f32 | 5000 x3, 10 x2 | | |

### 0x388 ShakingPlant

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xC0; 2207 placed in the game (10 levels); e.g. `tv_mf_agave_wiggle#`, `ml_zolar_agave_moby#`, `blueplant#`; own methods: Init 0x00439C94, Update 0x00439F88.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xA8 | f32 | 1.5 x1885, 0.6 x322 | | |
| +0xAC | f32 | 1 x1885, 0.35 x322 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 u16, +0xA0 u32, +0xA4 u32, +0xB0 u32, +0xB4 u32, +0xB8 f32.

### 0x391 SimpleMotion

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x80; 7 placed in the game (nefarious_station); e.g. `TRASH_WALL_L#`, `TRASH_WALL_R#`, `TRASH_DEBRIS_PILE`; own methods: Init 0x003A5CC4, Update 0x003A5BFC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | f32 | 0 on all 7 | Script: written by `simplemotion_set_dest`, `simplemotion_set_offset` | E S |
| +0x64 | f32 | 0 on all 7 | Script: written by `simplemotion_set_dest`, `simplemotion_set_offset` | E S |
| +0x68 | f32 | 0 on all 7 | Script: written by `simplemotion_set_dest`, `simplemotion_set_offset` | E S |
| +0x6C | f32 | 0 on all 7 | Script: written by `simplemotion_set_dest`, `simplemotion_set_offset` | E S |
| +0x70 | f32 | 0.5 x6, 0.2 x1 | Speed: Script: written by `simplemotion_set_speed` | E S |

### 0x3BB DoorSlide

Parent 0x548 Door > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x110..+0x160; 200 placed in the game (5 levels); e.g. `td_val_doorext#`, `mcd_door_L_#`, `mcd_door_R_#`; own methods: fix-up 0x0035DA80, Init 0x0035DAC4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x150 | reference | Volume, set on 136 of 200: `mcd_dest_L_#`, `auto_door_#_right_vol`, `auto_door_#_left_vol` | Reference slot: resolved by the class's fix-up | E |
| +0x158 | f32 | 1 x146, 0.5 x44, 3 x4, 0.75 x4, 20 x2 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x110 f32, +0x114 f32, +0x118 f32, +0x11C f32, +0x120 f32, +0x124 f32, +0x128 f32, +0x12C f32, +0x130 f32, +0x134 f32, +0x138 f32, +0x13C f32, +0x140 f32, +0x144 f32, +0x148 f32, +0x14C f32, +0x154 f32.

### 0x3C8 DropShipBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x120; not placed itself; values below are over its 11 placed descendants; own methods: fix-up 0x0045548C, Init 0x00455550, Update 0x004559F8.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC4 | f32 | 0.25 x6, 0.4 x4, 0.15 x1 | | |
| +0xC8 | f32 | 28 x6, 20 x3, 25 x2 | | |
| +0xF8 | reference | Path, set on 11 of 11: `PATH_INTRO_DROPSHIP#`, `drop_intro_A#_dropship#`, `drop_intro_A#_dropship` | Reference slot: resolved by the class's fix-up | E |
| +0xFC | f32 | 30 x6, 50 x2, 75 x1, 25 x1, 10 x1 | | |
| +0x100 | f32 | 1 x6, 25 x3, 10 x1, 2 x1 | | |
| +0x104 | reference | Volume, set on 11 of 11: `ORIENT_VOLUME_DROPSHIP#`, `spawn_vol_A#_dropship#`, `spawn_vol_A#_dropship` | Reference slot: resolved by the class's fix-up | E |
| +0x108 | reference | Path, set on 11 of 11: `PATH_OUTRO_DROPSHIP#`, `outro_A#_dropship#`, `outro_A#_dropship` | Reference slot: resolved by the class's fix-up | E |
| +0x10C | f32 | 25 x4, 1 x3, 5 x3, 2 x1 | | |
| +0x110 | f32 | 75 x4, 40 x4, 50 x2, 20 x1 | | |

Same on every instance: +0x114 = 0.6, +0x11C = 0x01000000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xB0 f32, +0xB4 f32, +0xB8 f32, +0xBC f32, +0xC0 f32, +0xCC f32, +0xD0 f32, +0xD4 u32, +0xD8 f32, +0xDC u32, +0xE0 u32, +0xE4 u32, +0xEC u32, +0xF0 f32, +0x11D u8, +0x11E u8.

### 0x3CD ShotTargetHang

Parent 0x245 ShotTargetBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xA0..+0xA0; 4 placed in the game (axiom_city, nefarious_station); e.g. `swingshotTargetGreen#`, `AZI_SHIP_HANGSHOT`, `AZI_SHIP_HANGSHOT#`.

No instance sets anything in the class's own range.

### 0x3E7 PhysicsObject

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x68; 134 placed in the game (7 levels); e.g. `lv_zf_fongoid_lamp#`, `tv_mf_hanging_light#`, `lv_fongoidlamp_red#`; own methods: Init 0x003A2720.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 0 x129, 164 x5 | | |

Same on every instance: +0x60 = 1.

### 0x400 PlatformGrappleSliding

Parent 0x4D PlatformGrappleBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x470..+0x490; 1 placed in the game (insomniac_museum); e.g. `wrench_platform_mag_twoaxis#`; own methods: fix-up 0x00378DB0, Init 0x00378EA8.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x468 | reference | Path, set on 1 of 1: `platformcurve` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x470 u32, +0x474 f32, +0x478 f32, +0x47C f32, +0x480 u8.

### 0x474 RotaterConstant

Parent 0x185 RotaterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x68..+0x68; 1 placed in the game (axiom_city); e.g. `rotating_pullshot_carousel#`; own methods: Init 0x0037C00C.

No instance sets anything in the class's own range.

### 0x4C7 azimuth

Parent 0x141 > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x1150..+0x14A0; 10 placed in the game (7 levels); e.g. `azimuth`, `AZIMUTH`, `azimuth_midpoint_scene`; own methods: Init 0x0001FF64, Update 0x00022798.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x142C | f32 | 0 on all 10 | Chase distance: Script: written by `azimuth_set_chase_distance` | E S |
| +0x1430 | u32 | 0 on all 10 | Chase distance: Script: written by `azimuth_set_chase_distance` | E S |
| +0x1474 | u32 | 0 on all 10 | Script: written by `azimuth_throw_flare` | E S |
| +0x1478 | f32 | 0 on all 10 | Script: written by `azimuth_throw_flare` | E S |
| +0x1484 | u32 | 0 on all 10 | Script: written by `azimuth_backstab` | E S |
| +0x148C | f32 | 0 on all 10 | Script: written by `azimuth_setup_deathblows` | E S |
| +0x1490 | f32 | 0 on all 10 | Script: written by `azimuth_setup_deathblows` | E S |
| +0x1494 | u32 | 0 on all 10 | Script: written by `azimuth_setup_deathblows` | E S |
| +0x1498 | f32 | 0 on all 10 | Script: written by `azimuth_setup_deathblows` | E S |

Same on every instance: +0x1150 = 322, +0x13A4 = 0.5, +0x13A8 = -0.5, +0x13AC = 0.872665, +0x13B0 = 7, +0x13B4 = 18, +0x13B8 = 0.2, +0x13BC = 0.5, +0x13C4 = -25, +0x13C8 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1154 f32, +0x1158 f32, +0x1220 f32, +0x1224 f32, +0x1228 u32, +0x124C u32, +0x1260 u32, +0x1264 u32, +0x1268 u32, +0x126C u32, +0x12A0 u32, +0x12A4 f32, +0x13F0 u32, +0x13F8 f32, +0x13FC f32, +0x1410 f32, +0x1414 f32, +0x1418 f32, +0x141C f32, +0x1420 u32, +0x1424 u8, +0x1428 u32, +0x1434 u32, +0x1438 u32, +0x143C u32, +0x1440 u32, +0x1444 u32, +0x1448 u32, +0x1470 u32, +0x147C f32, +0x1480 u32, +0x1488 u32.

### 0x51E kb_great_clock_time_record_pad

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x100; 42 placed in the game (great_clock_b, great_clock_c, great_clock_d); e.g. `kb_great_clock_time_record_pad#`, `kb_great_clock_time_record_padD#`, `kb_great_clock_time_record_padB#`; own methods: fix-up 0x0038A5D0, Init 0x0038DAD0, Update 0x0038E23C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x8C | reference | Pod, set on 14 of 42: `GCBpuzzle#padpod`, `GCCpuzzle#padpod`, `GCD_Puzzle#_TimePads` | Reference slot: resolved by the class's fix-up. Script: read by `clone_pad_delete_all` | E S |
| +0x90 | reference | Volume, set on 14 of 42: `GCBpuzzle#vicvol`, `GCCvictory#vol`, `VOL_GCD_PUZZLE#_VICTORY` | Reference slot: resolved by the class's fix-up | E |
| +0x94 | reference | unset on all 42 | Reference slot: resolved by the class's fix-up | E |
| +0x98 | reference | Pod, set on 14 of 42: `GCBpuzzle#resetpod`, `GCCpuzzle#resetpod`, `GCD_Puzzle#_ResetPod` | Reference slot: resolved by the class's fix-up. Script: written by `clone_pad_set_reset_pod` | E S |
| +0x9C | u32 | 1 x14, 0 x14, 2 x8, 3 x6 | | |
| +0xA0 | u32 | 0 on all 42 | Script: read by `clone_pad_has_clone` | E S |
| +0xAC | reference | Volume, set on 11 of 42: `warpvol_bypass_timepuzzle_#`, `warpvol_bypass_puzzle#` | Reference slot: resolved by the class's fix-up | E |
| +0xB0 | reference | Volume, set on 11 of 42: `GCBpuzzle#vicvol`, `GCCvictory#vol`, `VOL_GCD_PUZZLE#_VICTORY` | Reference slot: resolved by the class's fix-up | E |
| +0xB4 | reference | Moby, set on 10 of 42: `PUZZLE#_FANCYDOOR`, `GCC_PUZZLE_DOOR_#`, `sr_great_clock_door_combi_lock#` | Reference slot: resolved by the class's fix-up | E |
| +0xB8 | reference | Moby, set on 11 of 42: `bucket_of_win_timepuzzle_#` | Reference slot: resolved by the class's fix-up | E |
| +0xE4 | u32 | 0 on all 42 | Script: written by `clone_pad_disable_sigmund` | E S |
| +0xEC | u32 | 0 on all 42 | Script: read by `clone_pad_get_count` | E S |
| +0xF8 | u32 | 0 on all 42 | Script: written by `clone_pad_disable_sigmund` | E S |
| +0xFC | u32 | 0 on all 42 | Sigmund path: Script: written by `clone_pad_set_sigmund_path` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0xA4 u32, +0xA8 u32, +0xE8 u32, +0xF0 u32.

### 0x541 Button

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x78; not placed itself; values below are over its 70 placed descendants; own methods: Init 0x00358EE0, Update 0x00358E20.

Same on every instance: +0x74 = 0x00010000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x6C f32, +0x70 u8, +0x71 u8, +0x74 u8, +0x76 u8, +0x77 u8.

### 0x544 BurningTorch

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x68; 14 placed in the game (insomniac_museum); e.g. `torch#`; own methods: Init 0x0041ED58, Update 0x0041EBE4.

Same on every instance: +0x60 = 3.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u8, +0x64 f32.

### 0x545 ConveyorBelt

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xE0; 29 placed in the game (molonoth); e.g. `conveyor_belt_medium#`, `conveyor_belt_large#`, `conveyor_belt_small#`; own methods: Init 0x0041EF7C, Update 0x0041EE4C.

Same on every instance: +0xD0 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xC0 u32, +0xC4 u32, +0xC8 u32.

### 0x54D PlatformRotating

Parent 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x120..+0x130; not placed itself; values below are over its 58 placed descendants; own methods: Init 0x0036D1B8, Update 0x0036D040.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x120 | u32 | 0 x45, 2 x10, 1 x3 | | |

Same on every instance: +0x124 = 0x01000000.

### 0x553 PlatformProcedural

Parent 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x120..+0x130; not placed itself; values below are over its 2 placed descendants; own methods: fix-up 0x0036C0DC, Init 0x0036C078, Update 0x0036C134.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x11C | reference | Volume, set on 2 of 2: `vol_#_#_bridge_start_pos`, `vol_#_#_t_bridge_anim_start` | Reference slot: resolved by the class's fix-up | E |
| +0x120 | reference | Volume, set on 2 of 2: `vol_#_#_bridge_end_pos`, `vol_#_#_t_bridge_anim_end` | Reference slot: resolved by the class's fix-up | E |
| +0x124 | reference | Moby, set on 2 of 2: `boltcrankTbridge`, `second_t_bridge_bolt_crank` | Reference slot: resolved by the class's fix-up | E |

### 0x55B ant_swarm

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x290; 10 placed in the game (agorian_arena, insomniac_museum, krell_canyon, tombli); e.g. `ant_swarm_#_#`, `ant_swarm`, `audio_swarm`; own methods: fix-up 0x00141404, Init 0x00140F34, Update 0x00145664.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x190 | u32 | 0 on all 10 | Script: read by `swarm_is_targeting_ratchet`, `swarm_is_targeting_ambrosia` | E S |
| +0x198 | u32 | 0 on all 10 | Override target: Script: written by `swarm_set_override_target` | E S |
| +0x19C | u32 | 0 on all 10 | Override target: Script: written by `swarm_set_override_target` | E S |
| +0x1A0 | f32 | 0 on all 10 | Override target: Script: written by `swarm_set_override_target` | E S |
| +0x1A4 | f32 | 0 on all 10 | Override target: Script: written by `swarm_set_override_target` | E S |
| +0x1A8 | reference | Moby, set on 10 of 10: `lv_ant_swarm_spawner#_#`, `swarm_spawner_#`, `lv_ant_swarm_spawner#` | Reference slot: resolved by the class's fix-up. Script: read by `swarm_is_hiding` | E S |
| +0x1AC | reference | Area, set on 10 of 10: `swarm_#_attack`, `ant_swarm_#_attack`, `BATTLEDOME_BOSS_ARENA_OUTER` | Reference slot: resolved by the class's fix-up | E |
| +0x1B0 | reference | Area, set on 9 of 10: `swarm_#_retreat`, `ant_swarm_#_retreat`, `grindrail#_area` | Reference slot: resolved by the class's fix-up | E |
| +0x1B4 | reference | Moby, set on 4 of 10: `ant_swarm_#_#` | Reference slot: resolved by the class's fix-up | E |
| +0x1B8 | reference | Moby, set on 2 of 10: `webtacle_wall_secret`, `webtacle_wall_#_#` | Reference slot: resolved by the class's fix-up | E |
| +0x1BC | reference | Moby, set on 1 of 10: `webtacle_wall_#_#` | Reference slot: resolved by the class's fix-up | E |
| +0x1C0 | u16 | 0 on all 10 | Script: read by `swarm_is_retreating` | E S |
| +0x1D7 | u8 | 0 on all 10 | Script: read by `swarm_targeted_ambrosia`, `swarm_took_non_ambrosia_damage` | E S |
| +0x27C | f32 | 0 on all 10 | Script: written by `swarm_force_retreat` | E S |

Same on every instance: +0x78 = 4.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x60 u32, +0x64 u32, +0x68 u32, +0x6C u32, +0x70 u32, +0x74 u32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 u32, +0x110 f32, +0x114 f32, +0x118 f32, +0x11C f32, +0x128 u32, +0x12C u16, +0x12E u16, +0x130 u16, +0x132 u16, +0x140 f32, +0x144 f32, +0x148 f32, +0x14C f32, +0x150 f32, +0x154 f32, +0x158 f32, +0x15C f32, +0x160 u32, +0x164 u32, +0x168 u32, +0x170 f32, +0x174 f32, +0x178 f32, +0x17C f32, +0x180 f32, +0x184 f32, +0x188 f32, +0x18C f32, +0x194 u32, +0x1D0 f32, +0x1D8 f32, +0x1DC f32, +0x1E0 u32, +0x1E4 u32, +0x268 u32, +0x26C f32, +0x270 u32, +0x274 u32, +0x278 u32, +0x280 u32, +0x284 u32, +0x288 u32.

### 0x55F ConveyorJunk

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x100; 2 placed in the game (molonoth); e.g. `ja_mf_trasha#`; own methods: fix-up 0x0041F594, Init 0x0041F984, Update 0x0041F5CC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xDC | reference | Volume, set on 2 of 2: `junk_death_out`, `junk_death_in` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0xD0 = 0.75, +0xD4 = 1.75, +0xD8 = 2, +0xE0 = 15, +0xE4 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 f32, +0x74 f32, +0x78 f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x94 u32, +0x9C u32, +0xA8 u32, +0xB0 f32, +0xB4 f32, +0xB8 f32, +0xBC f32, +0xC0 u32, +0xE8 f32, +0xEC f32, +0xF4 u8.

### 0x573 ambrosia_plant

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x380; 7 placed in the game (insomniac_museum, krell_canyon, tombli); e.g. `ambrosia_plant#`, `ambrosia_plant_#_#`; own methods: Init 0x00419790, Update 0x00419D7C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0x84 u32, +0x88 u32, +0x110 u8, +0x1A0 u8, +0x230 u8, +0x2C0 u8, +0x350 u8, +0x360 u8, +0x370 f32, +0x374 f32, +0x378 u32, +0x37C u16, +0x37E u16.

### 0x589 PlatformGrappleWinding

own fields +0x4..+0x490; 4 placed in the game (galacton_ship, molonoth); e.g. `tj_gal_wing_pullfin#`, `tv_mf_winding_platform#`, `ROTATING_PLATFORM_START`; own methods: fix-up 0x00375608.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x468 | f32 | 60 x2, 100 x2 | | |
| +0x46C | f32 | -79.5 x1, -78 x1, 20 x1, -74.3754 x1 | | |
| +0x470 | f32 | 50000 x2, 2.5 x1, 4 x1 | | |
| +0x480 | u32 | 2 x3, 1 x1 | | |
| +0x484 | 4 bytes | 0x01000000 x2, 0x00000000 x2 | | |

Same on every instance: +0x10 = 0x01000000, +0x3C = -1, +0x54 = 65535, +0x418 = 2, +0x428 = -1, +0x43C = 0x01000000, +0x450 = 3, +0x45C = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x40C u32, +0x410 u32, +0x414 u32.

### 0x598 lv_ant_swarm_spawner

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x130; 10 placed in the game (agorian_arena, insomniac_museum, krell_canyon, tombli); e.g. `lv_ant_swarm_spawner#_#`, `swarm_spawner_#`, `lv_ant_swarm_spawner#`; own methods: Init 0x0043D008, Update 0x0043D5F0.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x70 u32, +0x74 f32, +0x78 u32, +0x7C u32, +0x80 f32.

### 0x5A3 custom_pistol

Parent 0x154 WeaponBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x170..+0x180; 1 placed in the game (front_end); e.g. `custom_pistol#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x170 u32, +0x174 f32, +0x178 u32.

### 0x5A4 custom_shotgun

Parent 0x154 WeaponBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x170..+0x290; 1 placed in the game (front_end); e.g. `custom_shotgun#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x268 f32, +0x26C f32, +0x270 f32, +0x274 u32, +0x278 u16, +0x27C f32, +0x280 f32, +0x284 f32.

### 0x5AF ConveyorSmasher

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x80; 25 placed in the game (insomniac_museum, molonoth); e.g. `GRIND_PISTON#`, `EXIT_PISTON#`, `SMASHER_PISTON#`; own methods: Init 0x0041FEEC, Update 0x0041FC3C.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | f32 | 1 x14, 2 x11 | | |
| +0x60 | f32 | 2 x19, 1.5 x6 | | |
| +0x68 | f32 | 0 x17, 1 x4, 2.1 x1, 1.4 x1, 0.5 x1 ... (6 values) | | |
| +0x70 | u32 | 0 on all 25 | Script: written by `open_smasher`, `close_smasher`, `restart_smasher` | E S |
| +0x74 | u16 | 0 on all 25 | Script: written by `open_smasher`, `close_smasher`, `restart_smasher` | E S |
| +0x76 | u16 | 0 on all 25 | Script: written by `open_smasher`, `close_smasher`, `restart_smasher` | E S |

Same on every instance: +0x64 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C f32.

### 0x5B7 webtacle_wall

Parent 0x54A DoorAnimated > 0x548 Door > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x110..+0x430; 6 placed in the game (insomniac_museum, krell_canyon, tombli); e.g. `webtacle_wall#`, `webtacle_wall_#_#`, `webtacle_wall_secret`; own methods: fix-up 0x00360324, Init 0x0036027C, Update 0x003606D8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x10C | reference | unset on all 6 | Reference slot: resolved by the class's fix-up | E |
| +0x11C | f32 | 0 on all 6 | Script: read by `is_webtacle_thrashing` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x110 u32, +0x114 u32, +0x118 u32, +0x120 u32, +0x130 f32, +0x134 f32, +0x138 f32, +0x13C f32, +0x140 u32, +0x144 u32, +0x148 u32, +0x1D0 u8, +0x260 u8, +0x2F0 u8, +0x380 u8, +0x410 u8, +0x420 u8.

### 0x5BE hydra

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0x1160; 4 placed in the game (gimlick_valley, krell_canyon); e.g. `Hydra#`, `agorian_hydra_tankB#HT_#`; own methods: Init 0x001479B8, Update 0x00148758.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC40 | u32 | 0 on all 4 | Script: read by `hydra_tank_kill` | E S |
| +0xDB0 | u32 | 0 on all 4 | Script: read by `hydra_tank_kill` | E S |
| +0xE48 | u32 | 0 on all 4 | Script: written by `hydra_tank_kill` | E S |
| +0xF20 | u32 | 0 on all 4 | Script: read by `hydra_tank_kill` | E S |
| +0xFB8 | u32 | 0 on all 4 | Script: written by `hydra_tank_kill` | E S |
| +0x1090 | u32 | 0 on all 4 | Script: read by `hydra_tank_kill` | E S |
| +0x1144 | f32 | 20 x2, 30 x2 | | |
| +0x114C | f32 | 0 x3, 3.5 x1 | | |
| +0x1154 | u32 | 0 on all 4 | Script: written by `arena_hydra_tag_out` | E S |
| +0x1159 | u8 | 0 on all 4 | Script: written by `arena_hydra_tag_out` | E S |

Same on every instance: +0x980 = 322, +0xBD4 = 0.5, +0xBD8 = -0.5, +0xBDC = 0.872665, +0xBE0 = 7, +0xBE4 = 18, +0xBE8 = 0.2, +0xBEC = 0.5, +0xBF4 = -25, +0xBF8 = -50, +0x1140 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x920 u32, +0x930 f32, +0x934 f32, +0x938 f32, +0x93C f32, +0x940 f32, +0x944 f32, +0x948 f32, +0x94C f32, +0x950 f32, +0x954 f32, +0x958 f32, +0x95C f32, +0x960 u32, +0x964 u32, +0x968 u16, +0x96A u16, +0x96C u16, +0x96E u16, +0x970 u32, +0x974 f32, +0x978 f32, +0x97C u8, +0x984 u32, +0x988 u32, +0xA50 u32, +0xA54 u32, +0xA58 u32, +0xA7C u32, +0xA84 u32, +0xA88 u32, +0xAC8 u8, +0xAD0 u32, +0xAD4 f32, +0xC20 u32, +0xC44 f32, +0xC48 f32, +0xC4C u16, +0xC50 u16, +0xC52 u16, +0xC54 f32, +0xC58 f32, +0xC5C u16, +0xC60 u16, +0xC74 u32, +0xC78 u32, +0xC88 f32, +0xC8C f32, +0xCC0 u32, +0xCC4 u32, +0xCC8 u16, +0xCCA u16, +0xCCC u16, +0xCCE u16, +0xCD0 u32, +0xCD4 u32, +0xCD8 u32, +0xCDC u32, +0xCE0 u32, +0xCE4 u32, +0xCF8 u32, +0xD50 u32, +0xD7C u32, +0xD99 u8, +0xD9A u8, +0xD9B u8, +0xD9C u8, +0xD9E u8, +0xDA0 u8, +0xDB4 f32, +0xDB8 f32, +0xDBC u16, +0xDE4 u32, +0xDE8 u32, +0xDF8 f32, +0xDFC f32, +0xE30 u32, +0xE34 u32, +0xE38 u16, +0xE3A u16, +0xE3C u16, +0xE3E u16, +0xE40 u32, +0xE44 u32, +0xE4C u32, +0xE50 u32, +0xE54 u32, +0xE68 u32, +0xEC0 u32, +0xEEC u32, +0xF09 u8, +0xF0A u8, +0xF0B u8, +0xF0E u8, +0xF10 u8, +0xF24 f32, +0xF28 f32, +0xF2C u16, +0xF54 u32, +0xF58 u32, +0xF68 f32, +0xF6C f32, +0xFA0 u32, +0xFA4 u32, +0xFA8 u16, +0xFAA u16, +0xFAC u16, +0xFAE u16, +0xFB0 u32, +0xFB4 u32, +0xFBC u32, +0xFC0 u32, +0xFC4 u32, +0xFD8 u32, +0x1030 u32, +0x105C u32, +0x1079 u8, +0x107A u8, +0x107B u8, +0x107E u8, +0x1080 u8, +0x1094 u32, +0x1098 u32, +0x10B0 u32, +0x10B4 u32, +0x1130 u32, +0x113C f32, +0x1150 u32.

### 0x5C6 PlatformGrappleJunkDoor

own fields +0x4..+0x480; 5 placed in the game (insomniac_museum); e.g. `junk_panel_door#`; own methods: fix-up 0x00375608.

Same on every instance: +0x10 = 0x01000000, +0x3C = -1, +0x54 = 65535, +0x418 = 2, +0x428 = -1, +0x43C = 0x01000000, +0x450 = 3, +0x45C = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x40C u32, +0x410 u32, +0x414 u32.

### 0x5D2 hoverboot_bolt_crank

Parent 0x425 BoltCrankBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x3D0..+0x420; 8 placed in the game (great_clock_e, molonoth, tombli, valkyrie_fleet); e.g. `hoverboot_bolt_crank#`, `hoverboot_boltcrank_chasezoni`, `hoverboot_boltcrank_test`; own methods: fix-up 0x003546C4, Init 0x003545B0, Update 0x00354434.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x3D0 | reference | Controller, set on 8 of 8: `cam_spinbolt_#`, `hovboltcam_test_cam#`, `hoverboot_bolt_crank_cam#` | Reference slot: resolved by the class's fix-up | E |
| +0x3D4 | reference | Volume, set on 1 of 8: `volume#` | Reference slot: resolved by the class's fix-up | E |
| +0x3D8 | reference | Volume, set on 1 of 8: `volume#` | Reference slot: resolved by the class's fix-up | E |
| +0x3E4 | f32 | 7 x4, 3 x3, 12 x1 | | |
| +0x3E8 | f32 | 2.25 x7, 1.5 x1 | | |
| +0x3EC | f32 | 32.5 x3, 40 x2, 28 x1, 30 x1, 45 x1 | | |
| +0x3F0 | f32 | 35 x5, 40 x2, 20 x1 | | |

Same on every instance: +0x3F4 = 1, +0x414 = 2.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x3DC u32, +0x3E0 u32, +0x404 u32, +0x408 u32, +0x410 f32, +0x418 u8.

### 0x5D6 panel_hydroid

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x740; 3 placed in the game (insomniac_museum); e.g. `panel_hydroid`, `display_hydroid`, `ledge_hydroid`; own methods: Init 0x0013234C, Update 0x00132AEC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | u32 | 1 x2, 0 x1 | | |
| +0x690 | u32 | 2 x2, 1 x1 | | |

Same on every instance: +0x80 = 322, +0x2D4 = 0.5, +0x2D8 = -0.5, +0x2DC = 0.872665, +0x2E0 = 7, +0x2E4 = 18, +0x2E8 = 0.2, +0x2EC = 0.5, +0x2F4 = -25, +0x2F8 = -50, +0x640 = 30, +0x6A0 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x6C u32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0x150 u32, +0x154 u32, +0x158 f32, +0x17C u32, +0x1D0 u32, +0x1D4 f32, +0x340 f32, +0x344 f32, +0x348 f32, +0x34C f32, +0x350 u32, +0x354 u32, +0x358 u32, +0x3E0 u8, +0x470 u8, +0x500 u8, +0x590 u8, +0x620 u8, +0x630 u8, +0x648 f32, +0x64E u16, +0x650 u16, +0x652 u16, +0x654 f32, +0x658 f32, +0x65C u16, +0x660 u32, +0x664 u32, +0x668 u32, +0x66C u32, +0x670 u32, +0x680 u32, +0x684 u32, +0x688 u32, +0x6A8 u8, +0x6A9 u8, +0x6AA u8, +0x6AB u8, +0x6AC u8, +0x6AD u8, +0x6AE u8, +0x6AF u8, +0x6B0 f32, +0x6B4 f32, +0x6B8 f32, +0x6BC f32, +0x6C0 u32, +0x6D4 f32, +0x6D8 u32, +0x6DC f32, +0x6E0 f32, +0x6E8 u32, +0x720 f32, +0x724 f32, +0x728 f32, +0x72C f32, +0x731 u8, +0x732 u8.

### 0x5DA grind_vine_seed

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x710; 12 placed in the game (gimlick_valley); e.g. `grind_vine_seed#`; own methods: Init 0x00426BD0, Update 0x0042756C.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x60 = 322, +0x2B4 = 0.5, +0x2B8 = -0.5, +0x2BC = 0.872665, +0x2C0 = 7, +0x2C4 = 18, +0x2C8 = 0.2, +0x2CC = 0.5, +0x2D4 = -25, +0x2D8 = -50, +0x660 = 2, +0x670 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x130 f32, +0x134 f32, +0x138 f32, +0x15C u32, +0x1B0 u32, +0x1B4 f32, +0x214 f32, +0x2B0 f32, +0x320 f32, +0x324 f32, +0x328 f32, +0x32C f32, +0x330 u32, +0x334 u32, +0x338 u32, +0x3C0 u8, +0x450 u8, +0x4E0 u8, +0x570 u8, +0x600 u8, +0x610 u8, +0x620 u16, +0x622 u16, +0x624 f32, +0x628 f32, +0x62C u16, +0x630 u32, +0x634 u32, +0x638 u32, +0x63C u32, +0x640 u32, +0x650 u32, +0x654 u32, +0x658 u32, +0x678 u8, +0x679 u8, +0x67A u8, +0x67B u8, +0x67C u8, +0x67D u8, +0x67E u8, +0x67F u8, +0x680 u16, +0x682 u16, +0x684 f32, +0x688 f32, +0x68C u16, +0x6F0 u32, +0x6F4 u32.

### 0x5E4 dropship_a_agorian

Parent 0x3C8 DropShipBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x120..+0x5D0; 11 placed in the game (gimlick_valley, krell_canyon); e.g. `DROPSHIP#`, `dropship_A#_agorian#`, `dropship_A#_agorian`; own methods: fix-up 0x0044D240, Init 0x0044DDA0, Update 0x0044D524.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x570 | u32 | 0 x7, 1 x4 | | |
| +0x578 | reference | Pod, set on 11 of 11: `AGORIAN_A#_BWAVE#`, `DROPSHIP_SHIELDED_BRIDGE`, `DROPSHIP_SHIELD_WAVE` | Reference slot: resolved by the class's fix-up | E |
| +0x57C | reference | Pod, set on 7 of 11: `AGORIAN_A#_BWAVE#`, `AGORIAN_A#_RWAVE#`, `AGORIAN_A#_LWAVE#` | Reference slot: resolved by the class's fix-up | E |
| +0x580 | reference | Pod, set on 5 of 11: `AGORIAN_A#_RWAVE#`, `AGORIAN_A#_LWAVE#`, `AGORIAN_A#_TOP_WAVE#` | Reference slot: resolved by the class's fix-up | E |
| +0x584 | reference | Pod, set on 2 of 11: `AGORIAN_A#_TOP_WAVE#`, `AGORIAN_A#_BOTTOM_WAVE#` | Reference slot: resolved by the class's fix-up | E |
| +0x588 | reference | unset on all 11 | Reference slot: resolved by the class's fix-up | E |
| +0x5B4 | f32 | 30 x7, 20 x2, 25 x2 | | |
| +0x5B8 | f32 | 10 x6, 5 x5 | | |
| +0x5BC | 4 bytes | 0x00000000 x7, 0x00010000 x4 | | |

Same on every instance: +0x120 = 1, +0x124 = 1, +0x128 = 1, +0x230 = 1, +0x240 = 65535, +0x594 = 2, +0x5B0 = 1, +0x5C0 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x12C u32, +0x130 u32, +0x134 u32, +0x138 u32, +0x15C f32, +0x160 f32, +0x164 f32, +0x1B4 u32, +0x210 u32, +0x220 u32, +0x224 f32, +0x22C f32, +0x234 u32, +0x238 f32, +0x23C u32, +0x240 u8, +0x250 f32, +0x254 f32, +0x258 f32, +0x25C f32, +0x260 u32, +0x264 u32, +0x268 u32, +0x2F0 u8, +0x380 u8, +0x410 u8, +0x4A0 u8, +0x530 u8, +0x540 u8, +0x550 u32, +0x554 u32, +0x558 u32, +0x560 f32, +0x564 f32, +0x568 f32, +0x56C f32, +0x574 u32, +0x58C u32, +0x590 u32, +0x598 f32, +0x59C u32, +0x5A0 u32, +0x5A4 f32, +0x5B0 u8, +0x5B1 u8, +0x5B2 u8, +0x5C0 u8, +0x5C1 u8, +0x5C2 u8, +0x5C4 u8, +0x5C5 u8, +0x5C6 u8.

### 0x5E8 spawner_cannon

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x410; 4 placed in the game (agorian_arena); e.g. `spawner_cannon_#`; own methods: fix-up 0x00338908, Init 0x00338960.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x6C | reference | Path, set on 4 of 4: `spawn_cannon_path_#` | Reference slot: resolved by the class's fix-up | E |
| +0x70 | reference | Volume, set on 4 of 4: `home_center_#` | Reference slot: resolved by the class's fix-up | E |
| +0x74 | reference | Area, set on 4 of 4: `CANNON_#_ALT_VOLS` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x400 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 u32, +0x94 u32, +0x98 u32, +0x9C u32, +0xA0 f32, +0xA4 f32, +0xA8 u32, +0xAC u32, +0xB0 f32, +0xB4 f32, +0xB8 f32, +0xBC f32, +0x100 f32, +0x104 f32, +0x108 f32, +0x10C f32, +0x110 f32, +0x114 f32, +0x120 f32, +0x124 f32, +0x128 f32, +0x12C f32, +0x130 f32, +0x134 f32, +0x138 f32, +0x13C f32, +0x140 f32, +0x144 f32, +0x148 f32, +0x14C f32, +0x150 u32, +0x154 u32, +0x158 u16, +0x15A u16, +0x15C u16, +0x15E u16, +0x160 u8, +0x164 u32, +0x168 u32, +0x16C u32, +0x170 u32, +0x174 u32, +0x178 u32, +0x17C u32, +0x180 u32, +0x184 u32, +0x188 u32, +0x18C u32, +0x190 u32, +0x3FC f32, +0x404 u32.

### 0x5EE zoni_vessel

Parent 0xD8 PickupSpecialBase > 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xF0..+0xF0; 1 placed in the game (zolar_forest); e.g. `ZONI_VESSEL`; own methods: Init 0x0044403C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x5F5 general_ship

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x800; 2 placed in the game (gimlick_valley); e.g. `GENERAL_SHIP`, `GENERAL_SHIP_STATUE`; own methods: fix-up 0x0045781C, Init 0x004590AC, Update 0x0045E380.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4B8 | u32 | 442 x1, 324 x1 | | |
| +0x4C4 | f32 | 3 x1, 6 x1 | | |
| +0x5C8 | 4 bytes | 0x00000000 x1, 0x01000000 x1 | | |
| +0x5CC | reference | Area, set on 1 of 2: `PATHS_GENERAL_RUN` | Reference slot: resolved by the class's fix-up | E |
| +0x5D8 | reference | Volume, set on 1 of 2: `VOL_TRIGGER_GENERAL` | Reference slot: resolved by the class's fix-up | E |
| +0x5E0 | reference | Path, set on 1 of 2: `PATH_GENERAL_INTRO_#` | Reference slot: resolved by the class's fix-up | E |
| +0x5E4 | reference | Path, set on 1 of 2: `PATH_GENERAL_FIGHT` | Reference slot: resolved by the class's fix-up | E |
| +0x5E8 | reference | Volume, set on 1 of 2: `VOL_DAM_LEFT` | | |
| +0x5EC | reference | Volume, set on 1 of 2: `VOL_GENERAL_RECHARGE_LEFT` | | |
| +0x5F4 | reference | Volume, set on 1 of 2: `VOL_DAM_CENTER` | | |
| +0x5F8 | reference | Volume, set on 1 of 2: `VOL_GENERAL_RECHARGE_CENTER` | | |
| +0x600 | reference | Volume, set on 1 of 2: `VOL_DAM_RIGHT` | | |
| +0x604 | reference | Volume, set on 1 of 2: `VOL_GENERAL_RECHARGE_RIGHT` | | |
| +0x614 | reference | Path, set on 1 of 2: `GENERAL_VERT_PATH_LEFT` | | |
| +0x618 | reference | Area, set on 1 of 2: `GENERAL_MINE_TARGETS_LEFT` | | |
| +0x61C | reference | Volume, set on 1 of 2: `VOL_GENERAL_RECHARGE_LEFT` | | |
| +0x628 | reference | Path, set on 1 of 2: `GENERAL_VERT_PATH_CENTER` | | |
| +0x62C | reference | Area, set on 1 of 2: `GENERAL_MINE_TARGETS_CENTER` | | |
| +0x630 | reference | Volume, set on 1 of 2: `VOL_GENERAL_RECHARGE_CENTER` | | |
| +0x63C | reference | Path, set on 1 of 2: `GENERAL_VERT_PATH_RIGHT` | | |
| +0x640 | reference | Area, set on 1 of 2: `GENERAL_MINE_TARGETS_RIGHT` | | |
| +0x644 | reference | Volume, set on 1 of 2: `VOL_GENERAL_RECHARGE_RIGHT` | | |
| +0x650 | reference | Volume, set on 1 of 2: `VOL_GENERAL_SPAWN_FROM` | Reference slot: resolved by the class's fix-up | E |
| +0x654 | reference | Pod, set on 1 of 2: `POD_GENERAL_PASSENGERS` | Reference slot: resolved by the class's fix-up | E |
| +0x658 | reference | Area, set on 1 of 2: `GENERAL_DROPOFF_AREA` | Reference slot: resolved by the class's fix-up | E |
| +0x65C | reference | Path, set on 1 of 2: `PATH_GENERAL_LEAVE` | Reference slot: resolved by the class's fix-up | E |
| +0x660 | reference | Path, set on 1 of 2: `PATH_GENERAL_INTRO_#` | Reference slot: resolved by the class's fix-up | E |
| +0x664 | reference | Path, set on 1 of 2: `PATH_GENERAL_FIGHT` | Reference slot: resolved by the class's fix-up | E |
| +0x66C | reference | Volume, set on 1 of 2: `VOL_GENERAL_DEATH` | Reference slot: resolved by the class's fix-up | E |
| +0x670 | reference | Area, set on 1 of 2: `GENERAL_MINE_TARGETS_LEFT` | | |
| +0x674 | reference | Volume, set on 1 of 2: `VOL_GENERAL_RECHARGE_LEFT` | | |
| +0x678 | reference | Area, set on 1 of 2: `GENERAL_MINE_TARGETS_RIGHT` | | |
| +0x67C | reference | Volume, set on 1 of 2: `VOL_GENERAL_RECHARGE_RIGHT` | | |

Same on every instance: +0x60 = 1, +0x64 = 1, +0x68 = 1, +0x4D8 = 2, +0x4E8 = -1, +0x584 = 1, +0x594 = 65535, +0x5A8 = 2, +0x5B8 = -1, +0x5D0 = 70, +0x5D4 = 8, +0x5DC = 70, +0x60C = 4, +0x610 = 15, +0x620 = 3, +0x624 = 2, +0x634 = 3, +0x638 = 2, +0x648 = 3, +0x64C = 2, +0x668 = 2.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x70 u32, +0x74 u32, +0x78 u32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0x150 u32, +0x170 u32, +0x174 u32, +0x178 u32, +0x200 u8, +0x290 u8, +0x320 u8, +0x3B0 u8, +0x440 u8, +0x450 u8, +0x460 u32, +0x464 u32, +0x4A8 u32, +0x4AC u32, +0x4C0 u32, +0x4C8 u32, +0x4CC u32, +0x4D0 u32, +0x4F0 u32, +0x4F5 u8, +0x4F6 u8, +0x4F7 u8, +0x4F8 u16, +0x550 u32, +0x554 u32, +0x558 u32, +0x560 u32, +0x564 u32, +0x568 u32, +0x570 u8, +0x574 u32, +0x578 f32, +0x580 f32, +0x588 u32, +0x58C f32, +0x590 u32, +0x594 u8, +0x598 u32, +0x59C u32, +0x5A0 u32, +0x5A4 u32, +0x5C0 u32, +0x5C4 u32, +0x68C u32, +0x690 u32, +0x698 u32, +0x69C f32, +0x6B4 u32, +0x6B8 u32, +0x6BC u32, +0x6C0 u32, +0x6C4 u32, +0x6C8 u32, +0x720 u32, +0x760 u32, +0x790 f32, +0x794 f32, +0x798 f32, +0x79C f32, +0x7A0 u32, +0x7A4 u32, +0x7A8 u32, +0x7AC u32, +0x7B0 f32, +0x7B4 f32, +0x7B8 f32, +0x7BC f32, +0x7E0 u32, +0x7E8 u32, +0x7FC u8, +0x7FD u8.

### 0x60A first_t_bridge

Parent 0x553 PlatformProcedural > 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x130..+0x140; 2 placed in the game (krell_canyon); e.g. `first_t_bridge`, `second_t_bridge`; own methods: Init 0x0036A510, Update 0x0036A5C8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x138 | f32 | 1 x1, 0.1 x1 | | |

### 0x60B CollisionSticker

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 3 placed in the game (tombli); e.g. `SPROUT#`; own methods: Init 0x00428CFC, Update 0x00428CA8.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32.

### 0x60E hangar_turret

Parent 0x366 TurretBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x440..+0x660; 5 placed in the game (agorian_arena, galacton_ship, krell_canyon, valkyrie_fleet); e.g. `arena_turret_#`, `hangar_turret#`, `hangar_turret`; own methods: Init 0x003800E0, Update 0x00381CF8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x644 | u32 | 16 x2, 10 x2, 15 x1 | | |

Same on every instance: +0x470 = 221, +0x4C0 = 221, +0x61C = 300.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4A4 u32, +0x4D0 f32, +0x4D4 f32, +0x4D8 f32, +0x4DC f32, +0x4E0 f32, +0x4E4 f32, +0x4E8 f32, +0x4EC f32, +0x4F0 f32, +0x4F4 u32, +0x4F8 f32, +0x4FC f32, +0x500 f32, +0x504 f32, +0x508 f32, +0x510 u32, +0x514 u32, +0x540 f32, +0x544 f32, +0x548 f32, +0x54C f32, +0x5D0 f32, +0x5D4 f32, +0x5D8 f32, +0x5DC f32, +0x5E0 u32, +0x5E4 f32, +0x5E8 u32, +0x5EC u32, +0x5F0 u32, +0x5F4 u32, +0x614 u32, +0x618 f32, +0x620 f32, +0x628 f32, +0x62C f32, +0x630 f32, +0x634 f32, +0x638 u32, +0x63C u16, +0x63E u16, +0x648 u32, +0x64C f32, +0x650 f32, +0x654 f32, +0x65A u16.

### 0x613 agorian_strafer

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x610; 147 placed in the game (gimlick_valley, krell_canyon); e.g. `agorian_strafer#`, `GROUP_E#_#`, `GROUP_B#_#`; own methods: fix-up 0x0044EEA8, Init 0x0044EEF0, Update 0x004505F8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x570 | reference | Path, set on 146 of 147: `Ambient_Strafer#`, `routeE#`, `routeB#` | Reference slot: resolved by the class's fix-up | E |
| +0x574 | reference | Area, set on 1 of 147: `PATHS_GENERAL_RUN` | Reference slot: resolved by the class's fix-up | E |
| +0x578 | f32 | 1 x87, 3 x21, 0.5 x14, 0.01 x8, 2 x7 ... (8 values) | | |
| +0x57C | f32 | 30 x71, 70 x18, 95 x15, 45 x15, 50 x10 ... (8 values) | | |
| +0x580 | f32 | 0 x143, 0.3 x2, 0.2 x1, 0.5 x1 | | |
| +0x5FC | u32 | 0 x104, 256 x42, 1 x1 | | |
| +0x600 | 4 bytes | 0x01000000 x140, 0x00000000 x7 | | |

Same on every instance: +0x60 = 1, +0x64 = 1, +0x68 = 1, +0x470 = 1, +0x480 = 65535, +0x5C8 = 1570, +0x5D4 = 0x01000000, +0x5DC = 3, +0x5E4 = 3, +0x5E8 = 20.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x70 u32, +0x74 u32, +0x78 u32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xF4 u32, +0x150 u32, +0x160 f32, +0x164 f32, +0x168 f32, +0x16C f32, +0x170 u32, +0x174 u32, +0x178 u32, +0x200 u8, +0x290 u8, +0x320 u8, +0x3B0 u8, +0x440 u8, +0x450 u8, +0x460 u32, +0x464 f32, +0x46C f32, +0x474 u32, +0x478 f32, +0x47C u32, +0x480 u8, +0x484 u32, +0x488 u16, +0x48A u16, +0x48C f32, +0x490 f32, +0x494 u16, +0x4B0 f32, +0x4B4 f32, +0x4B8 f32, +0x4BC f32, +0x4D0 f32, +0x4D4 f32, +0x4D8 f32, +0x4DC f32, +0x4E0 u32, +0x4E4 u32, +0x4E8 u32, +0x4EC u32, +0x4F0 f32, +0x4F4 f32, +0x4F8 u32, +0x4FC u32, +0x500 f32, +0x504 f32, +0x508 f32, +0x50C f32, +0x550 f32, +0x554 f32, +0x558 f32, +0x55C u32, +0x560 f32, +0x564 f32, +0x598 f32, +0x5A4 f32, +0x5B8 u32, +0x5BC u32, +0x5C0 u32, +0x5C4 u32.

### 0x616 parking_arm

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x260; 1 placed in the game (agorian_arena); e.g. `Parking_Arm`; own methods: fix-up 0x0033ADEC, Init 0x0033ABC8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x6C | reference | Moby, set on 1 of 1: `RCF_Car` | Reference slot: resolved by the class's fix-up | E |
| +0x70 | reference | Moby, set on 1 of 1: `aphelion_zoni#` | Reference slot: resolved by the class's fix-up | E |
| +0x74 | reference | Moby, set on 1 of 1: `Parking_Arm#` | Reference slot: resolved by the class's fix-up | E |
| +0x78 | reference | Moby, set on 1 of 1: `Parking_Arm#` | Reference slot: resolved by the class's fix-up | E |
| +0x7C | reference | Path, set on 1 of 1: `Arm_Curve` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 u32, +0x94 u32, +0x98 u32, +0x9C u32, +0xA0 f32, +0xA4 f32, +0xA8 u32, +0xAC u32, +0xB0 f32, +0xB4 f32, +0xB8 f32, +0xBC f32, +0x100 f32, +0x104 f32, +0x108 f32, +0x10C u32, +0x110 f32, +0x114 f32, +0x120 f32, +0x124 f32, +0x128 f32, +0x12C f32, +0x130 u32, +0x134 u32, +0x138 u32, +0x13C u32, +0x140 f32, +0x144 f32, +0x148 u32, +0x14C u32, +0x150 f32, +0x154 f32, +0x158 f32, +0x15C f32, +0x1A0 f32, +0x1A4 f32, +0x1A8 f32, +0x1AC u32, +0x1B0 f32, +0x1B4 f32, +0x1C0 f32, +0x1C4 f32, +0x1C8 f32, +0x1CC f32, +0x1D0 u32, +0x1D4 u32, +0x1D8 u32, +0x1DC u32, +0x1E0 f32, +0x1E4 f32, +0x1E8 u32, +0x1EC u32, +0x1F0 f32, +0x1F4 f32, +0x1F8 f32, +0x1FC f32, +0x240 f32, +0x244 f32, +0x248 f32, +0x24C u32, +0x250 f32, +0x254 f32.

### 0x62D CamArgPathTraveler

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x70; 43 placed in the game (great_clock_e, krell_canyon, molonoth); e.g. `grindrail_obstacle#`; own methods: Init 0x003A56E0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x68 | f32 | 1 x26, 3 x9, 10 x8 | | |

Same on every instance: +0x60 = -1, +0x64 = 0.5.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32.

### 0x630 tb_gc_clanksub_tile_platform

Parent 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x120..+0x2C0; 267 placed in the game (great_clock_b, great_clock_c, great_clock_d, insomniac_museum); e.g. `tb_gc_clanksub_tile_platform#_#`; own methods: fix-up 0x00373BE4, Init 0x00373A04, Update 0x003739C4.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x2B0 | reference | Pod, set on 222 of 267: `gcd_subcon_puzzle#_flooroccupants`, `bonus_puzzle_platform_occupants`, `bonus_puzzle_tile_floor_occupants` | Reference slot: resolved by the class's fix-up | E |
| +0x2B4 | 4 bytes | 0x00000000 x45, 0x00010100 x31, 0x01000001 x31, 0x01000100 x30, 0x00010001 x30 ... (14 values) | | |

### 0x63F machine_sawblade_hazard

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x80; 2 placed in the game (molonoth); e.g. `machine_sawblade_hazard#`; own methods: Init 0x00165DE0, Update 0x0016606C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x78 | f32 | 0 x1, 2.25 x1 | | |
| +0x7C | u16 | 0 on all 2 | Script: written by `open_saw`, `close_saw`, `restart_saw` | E S |
| +0x7E | u16 | 0 on all 2 | Script: written by `open_saw`, `close_saw`, `restart_saw` | E S |

Same on every instance: +0x6C = 2, +0x70 = 2, +0x74 = 1.5.

### 0x643 flare

Parent 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xE0..+0x130; 56 placed in the game (molonoth); e.g. `flare#`; own methods: Init 0x004BD0F4, Update 0x004BD020.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x110 | u32 | 0 on all 56 | Script: read by `reset_flares` | E S |
| +0x120 | u32 | 0 on all 56 | Script: written by `reset_flares` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x114 f32, +0x118 f32, +0x11C f32.

### 0x645 ConfirmPrompt

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x178; 9 placed in the game (great_clock_b, great_clock_c); e.g. `kb_great_clock_time_record_pad#`, `kb_great_clock_time_record_padB#`, `kb_great_clock_time_record_padD#`; own methods: fix-up 0x0038E574, Init 0x0038E530, Update 0x0038E520.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x168 | reference | Pod, set on 9 of 9: `GCBpuzzle#padpod`, `GCCpuzzle#padpod` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0xB8 = 10, +0x118 = 442, +0x124 = 3, +0x138 = 2, +0x148 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x11C u32.

### 0x646 qwark

Parent 0x141 > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x1150..+0x14A0; 6 placed in the game (agorian_arena, galacton_ship, nefarious_station, zolar_forest); e.g. `qwark`, `QWARK`, `QWARK_ON_ASTEROID`; own methods: Init 0x000238D0, Update 0x00023DE4.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x146C | f32 | 0 on all 6 | Roam radius: Script: written by `qwark_set_roam_radius` | E S |
| +0x1490 | f32 | 0 on all 6 | Script: written by `qwark_setup_deathblows` | E S |
| +0x1494 | f32 | 0 on all 6 | Script: written by `qwark_setup_deathblows` | E S |
| +0x1498 | u32 | 0 on all 6 | Script: written by `qwark_setup_deathblows` | E S |
| +0x149C | f32 | 0 on all 6 | Script: written by `qwark_setup_deathblows` | E S |

Same on every instance: +0x1150 = 322, +0x13A4 = 0.5, +0x13A8 = -0.5, +0x13AC = 0.872665, +0x13B0 = 7, +0x13B4 = 18, +0x13B8 = 0.2, +0x13BC = 0.5, +0x13C4 = -25, +0x13C8 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1154 f32, +0x1158 f32, +0x1220 u32, +0x1224 u32, +0x1228 f32, +0x124C u32, +0x1260 u32, +0x1264 u32, +0x1268 u32, +0x126C u32, +0x12A0 u32, +0x12A4 f32, +0x13F0 u32, +0x1410 f32, +0x1414 f32, +0x1418 f32, +0x141C f32, +0x1420 f32, +0x1424 f32, +0x1428 f32, +0x142C f32, +0x1430 f32, +0x1434 f32, +0x1438 f32, +0x143C f32, +0x1440 u32, +0x1444 u32, +0x1448 u16, +0x144A u16, +0x144C u16, +0x144E u16, +0x1450 f32, +0x1454 u32, +0x1458 u32, +0x145C u32, +0x1460 u32, +0x1464 u32, +0x1468 u32, +0x1470 f32, +0x1474 f32, +0x1478 f32, +0x147C f32, +0x1480 u32, +0x1484 u32, +0x1488 u32, +0x148C u32.

### 0x647 death_beam_asteroid

Parent 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x250..+0x640; 13 placed in the game (agorian_arena, axiom_city, nefarious_station); e.g. `DEATH_BEAM_ASTEROID#`, `agorian_ship_lobby_meandering_screen#`, `a_boss_policecar#`; own methods: fix-up 0x004B0E00, Init 0x004B0F6C, Update 0x004B0B7C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5F0 | reference | Path, set on 11 of 13: `path_asteroid#`, `meandering_screen_curve_#` | Reference slot: resolved by the class's fix-up | E |
| +0x610 | f32 | 12 x9, 2 x2, 4 x2 | | |
| +0x614 | f32 | 0.2 x9, 0 x4 | | |
| +0x620 | 4 bytes | 0x00000001 x9, 0x01000000 x2, 0x00010000 x1, 0x00000000 x1 | | |
| +0x624 | f32 | 0 x12, 2 x1 | | |
| +0x628 | f32 | 5 x12, 50 x1 | | |

Same on every instance: +0x618 = 1, +0x61C = 1, +0x62C = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x250 f32, +0x254 f32, +0x258 f32, +0x25C f32, +0x260 u32, +0x264 u32, +0x268 u32, +0x2F0 u8, +0x380 u8, +0x410 u8, +0x4A0 u8, +0x530 u8, +0x540 u8, +0x550 f32, +0x554 f32, +0x558 f32, +0x55C f32, +0x560 u32, +0x564 u32, +0x568 u32, +0x56C u32, +0x570 f32, +0x574 f32, +0x578 f32, +0x57C f32, +0x580 f32, +0x584 f32, +0x588 f32, +0x58C f32, +0x5D0 f32, +0x5D4 f32, +0x5D8 f32, +0x5DC u32, +0x5E0 f32, +0x5E4 f32, +0x630 u32.

### 0x648 PlatformAnimated

Parent 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x120..+0x170; 8 placed in the game (zolar_forest); e.g. `TEMPLE#_PILLAR#`, `lv_breaking_pillar_#m#`, `vic_coliseumwalkway_d#`; own methods: fix-up 0x003664AC, Init 0x00366510, Update 0x00366754.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x12C | reference | Pod, set on 8 of 8: `POD_TEMPLE#_PILLARS_SYNC` | Reference slot: resolved by the class's fix-up | E |
| +0x130 | u32 | 4 x3, 8 x1, 10 x1, 6 x1, 14 x1 ... (6 values) | | |
| +0x15C | 4 bytes | 0x01000000 x6, 0x01010000 x2 | | |
| +0x164 | u32 | 5 x3, 9 x1, 11 x1, 7 x1, 15 x1 ... (6 values) | | |

Same on every instance: +0x134 = -1, +0x138 = -1, +0x13C = 1, +0x144 = 1, +0x148 = 2, +0x160 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x150 u32, +0x154 u32, +0x158 f32.

### 0x65C planetgamec

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x1F0; 6 placed in the game (great_clock_a, great_clock_c, great_clock_d); e.g. `planetgameC#_#`, `planetgametestGCA`, `planetgametestGCD`; own methods: fix-up 0x00399F24, Init 0x0039B888, Update 0x0039CA34.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 0 on all 6 | Incomplete: Script: written by `ftg_set_incomplete` | E S |
| +0x80 | u32 | 0 on all 6 | Script: read by `ftg_set_incomplete` | E S |
| +0x88 | u32 | 3 x1, 6 x1, 4 x1, 5 x1, 1 x1 ... (6 values) | Script: read by `ftg_set_incomplete` | E S |
| +0x8C | reference | Moby, set on 6 of 6: `kb_gc_c_pedestal_a#`, `GCA_TIMEFIX_PEDESTAL`, `planetgameC#_#ped` | Reference slot: resolved by the class's fix-up | E |
| +0x90 | reference | Moby, set on 5 of 6: `kb_gc_c_planet_room_projectors#` | Reference slot: resolved by the class's fix-up | E |
| +0x94 | reference | Moby, set on 6 of 6: `pa_speaker`, `GCA_PA_SPEAKER` | Reference slot: resolved by the class's fix-up | E |
| +0xA0 | u32 | 0 on all 6 | Incomplete: Script: written by `ftg_set_incomplete` | E S |
| +0x190 | reference | Controller, set on 6 of 6: `planetgameC#_#Cam`, `POST_PLANET_GAME_CAM`, `cam_planet_minigame` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x194 = 2.5.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 u16, +0x7C u32, +0x84 u8, +0x85 u8, +0x98 u32, +0xAC f32, +0xB0 f32, +0xB8 u32, +0xC0 u32, +0xCC f32, +0xD0 f32, +0xD4 f32, +0xD8 f32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0xF0 u32, +0x100 f32, +0x104 f32, +0x108 f32, +0x10C f32, +0x110 f32, +0x114 f32, +0x118 f32, +0x11C f32, +0x120 f32, +0x124 f32, +0x128 f32, +0x12C u32, +0x130 f32, +0x134 f32, +0x138 f32, +0x13C f32, +0x140 u32, +0x144 u32, +0x148 u32, +0x14C u32, +0x150 u32, +0x154 u32, +0x158 u32, +0x15C u32, +0x160 u32, +0x164 f32, +0x168 f32, +0x16C u32, +0x170 f32, +0x174 f32, +0x178 f32, +0x17C f32, +0x184 f32, +0x198 u32, +0x19C u32, +0x1E0 u32, +0x1E4 f32.

### 0x668 blaster_game

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x158; 10 placed in the game (agorian_arena); e.g. `blaster_game#`; own methods: fix-up 0x003A35FC, Init 0x003A31F4.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xDC | reference | Volume, set on 10 of 10: `blaster_game_vol_#` | | |
| +0x118 | u32 | 0 on all 10 | Script: read by `set_arcade_game` | E S |
| +0x157 | u8 | 0 on all 10 | Arcade game: Script: written by `set_arcade_game` | E S |

Same on every instance: +0xD8 = 1110, +0xF8 = 2, +0x108 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x80 u32, +0x84 u32, +0x8C u32, +0xC8 u32, +0xCC f32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC u32, +0xF0 u32, +0x110 u32, +0x114 u8, +0x115 u8, +0x116 u8, +0x117 u8.

### 0x669 qwark_door

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x78; 7 placed in the game (galacton_ship, nefarious_station, zolar_forest); e.g. `QWARK_DOOR#`, `qwark_operated_door#`, `QWARK_TRASH_DOOR`; own methods: Init 0x0035F778, Update 0x0035F89C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x68 | u32 | 0 on all 7 | Broken: Script: written by `qwark_door_set_broken`, `qwark_door_manual_break` | E S |
| +0x6C | u32 | 1 on all 7 | Script: written by `qwark_door_enable_break` | E S |
| +0x70 | u32 | 0 x5, 1 x2 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C f32, +0x60 f32, +0x74 f32.

### 0x678 kb_great_clock_pressure_pad

Parent 0x213 ButtonPressure > 0x541 Button > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x180..+0x190; 64 placed in the game (great_clock_b, great_clock_c, great_clock_d); e.g. `kb_great_clock_pressure_pad#`, `gcd_puzzle#_button_elev#`, `kb_great_clock_chamber_button#`; own methods: Init 0x0035A244.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x180 | 4 bytes | 0x00000000 x34, 0x00010000 x19, 0x00010001 x7, 0x00000001 x4 | | |

### 0x683 NPCStateGotoPoint

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x440; 1 placed in the game (axiom_city); e.g. `VX#`; own methods: fix-up 0x0002A3F8, Init 0x0002B390, Update 0x0002C778.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 0 on all 1 | Script: read by `vx99_open_flaps`, `vx99_start_top_attack` | E S |
| +0x98 | u32 | 0 on all 1 | Script: read by `vx99_attack_path` | E S |
| +0x9C | u32 | 0 on all 1 | Script: read by `vx99_attack_path` | E S |
| +0xA4 | u32 | 0 on all 1 | Script: read by `vx99_lower_turret`, `vx99_start_top_attack`, `vx99_start_turret_attack` | E S |
| +0xA8 | u32 | 0 on all 1 | Script: written by `vx99_pull_dome` | E S |
| +0x400 | u32 | 0 on all 1 | Script: written by `vx99_go_to_volume` | E S |
| +0x404 | u32 | 0 on all 1 | Script: written by `vx99_follow_path` | E S |
| +0x410 | f32 | 0 on all 1 | Script: written by `vx99_follow_path`, `vx99_go_to_volume` | E S |
| +0x414 | f32 | 0 on all 1 | Script: written by `vx99_follow_path`, `vx99_go_to_volume` | E S |
| +0x41C | u32 | 0 on all 1 | Fling data: Script: written by `vx99_set_fling_data` | E S |
| +0x420 | u32 | 0 on all 1 | Fling data: Script: written by `vx99_set_fling_data` | E S |
| +0x424 | reference | Pod, set on 1 of 1: `BB_BOSS_DRAG_POD` | Reference slot: resolved by the class's fix-up | E |
| +0x428 | reference | Area, set on 1 of 1: `BB_BOSS_DRAG_AREA` | Reference slot: resolved by the class's fix-up | E |
| +0x43E | u8 | 0 on all 1 | Bob active: Script: written by `vx99_set_bob_active` | E S |

Same on every instance: +0x140 = 1, +0x144 = 1, +0x148 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 u16, +0x62 u16, +0x64 u16, +0x66 u16, +0x68 u32, +0x6C u32, +0x70 u32, +0x74 u32, +0x80 u32, +0x8C u32, +0xAC u32, +0xB1 u8, +0xD0 f32, +0xD4 f32, +0xD8 f32, +0xDC f32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0xF0 f32, +0xF4 f32, +0xF8 f32, +0xFC f32, +0x100 f32, +0x104 f32, +0x108 f32, +0x10C f32, +0x130 u32, +0x134 u32, +0x138 u32, +0x14C u32, +0x150 u32, +0x154 u32, +0x158 u32, +0x17C f32, +0x180 f32, +0x184 f32, +0x230 u32, +0x240 u32, +0x244 u32, +0x248 u32, +0x418 f32, +0x42C f32, +0x430 f32, +0x434 u32, +0x438 u32.

### 0x69D EnemyStateFollowTarget_DebugData

Parent 0x3ED PlatformMoving > 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x1B0..+0x200; 49 placed in the game (5 levels); e.g. `kb_gc_c_platform_circular_b#`, `kb_gc_clanksub_self_building_platform#`, `SELF_PLAT_SUB_B#`; own methods: fix-up 0x0036EE14, Init 0x0036EE84, Update 0x0036F4D4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x1C0 | reference | Moby, set on 23 of 49: `kb_great_clock_pressure_pad#`, `gcd_subcon_puzzle#_assistpath#_button_#`, `planetgameC#_#` | Reference slot: resolved by the class's fix-up | E |
| +0x1C4 | reference | Moby, set on 1 of 49: `planetgameC#_#` | Reference slot: resolved by the class's fix-up | E |
| +0x1C8 | reference | Volume, set on 22 of 49: `SELF_TRIG_PLAT_SUB_B#`, `SELF_SUBCONC#_#`, `vol_kb_gc_clanksub_self_building_platform#` | Reference slot: resolved by the class's fix-up | E |
| +0x1CC | reference | unset on all 49 | Reference slot: resolved by the class's fix-up | E |
| +0x1D4 | f32 | 0 x36, 1 x7, 0.5 x6 | | |
| +0x1D8 | f32 | 0 x45, 0.25 x2, 0.5 x2 | | |

Same on every instance: +0x1E8 = 0.1, +0x1EC = 0.25.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1B0 u16, +0x1D0 f32, +0x1DC u32, +0x1E0 u32, +0x1E4 f32, +0x1F0 f32, +0x1F4 f32.

### 0x6A0 azimuth_tankbstrafe

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x130; 28 placed in the game (axiom_city, galacton_ship, krell_canyon); e.g. `azimuth_tankB#strafe#`, `Azimuth_vs_fighter_B#`, `azimuth_turret_victim#`; own methods: fix-up 0x004536F4, Init 0x00453CDC, Update 0x0045435C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x110 | reference | Path, set on 28 of 28: `azimuth_tankB#strafepath#`, `agorian_turretazimuth#_path`, `AvF_B#_azimuth_agro` | Reference slot: resolved by the class's fix-up | E |
| +0x120 | f32 | 50 x8, 65 x8, 60 x4, 45 x3, 70 x2 ... (8 values) | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0x84 u32, +0x88 u32, +0x8C u32, +0x90 f32, +0x94 f32, +0x98 u32, +0x9C u32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xF0 f32, +0xF4 f32, +0xF8 f32, +0xFC f32, +0x100 f32, +0x104 f32, +0x114 u32, +0x118 f32, +0x11C u32, +0x124 u8.

### 0x6A3 dice_crate

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3B0; 3 placed in the game (insomniac_museum); e.g. `dice_crate#`; own methods: Init 0x00420460.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x7C = 1, +0x8C = 65535.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x6C u32, +0x70 f32, +0x78 f32, +0x80 u32, +0x84 f32, +0x88 u32, +0x8C u8, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xB0 u32, +0xB4 u32, +0xB8 u32, +0x140 u8, +0x1D0 u8, +0x260 u8, +0x2F0 u8, +0x380 u8, +0x390 u8, +0x3A0 f32, +0x3A4 u32.

### 0x6A9 orvus

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0x1050; 3 placed in the game (great_clock_b, great_clock_c, great_clock_d); e.g. `orvus`; own methods: Init 0x002B80C8, Update 0x002B8298.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0xD80 = 409, +0xFD4 = 0.5, +0xFD8 = -0.5, +0xFDC = 0.872665, +0xFE0 = 7, +0xFE4 = 18, +0xFE8 = 0.2, +0xFEC = 0.5, +0xFF4 = -25, +0xFF8 = -50, +0x1044 = 0.5, +0x1048 = 0.1, +0x104C = 6.28319.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xD78 u32, +0xD7C u32, +0xD84 f32, +0xD88 f32, +0xE50 u32, +0xE54 u32, +0xE58 f32, +0xE7C u32, +0xE90 u32, +0xE94 u32, +0xE98 u32, +0xE9C u32, +0xED0 u32, +0xED4 f32, +0xF34 f32, +0xFF0 u32, +0x1020 u32, +0x1034 u32, +0x1040 f32.

### 0x6AF gas_room_pad

Parent 0x2B6 PadBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xC0..+0x1A0; 24 placed in the game (axiom_city, galacton_ship, insomniac_museum, valkyrie_fleet); e.g. `gas_room_pad#`, `TRIPAD_NW#`, `TRIPAD_NE#`; own methods: fix-up 0x00364B00, Init 0x003649F0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x118 | reference | Pod, set on 18 of 24: `pads_gas_room#`, `TD_TRIPADS#_NW`, `TD_TRIPADS#_NE` | Reference slot: resolved by the class's fix-up. Script: read by `multipad_set` | E S |
| +0x11C | f32 | -1 x15, 30 x6, 10 x3 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xC0 u16, +0xD0 u32, +0xD4 u32, +0xD8 u32, +0xDC u32, +0xE0 u32, +0xE4 u32, +0xF4 u32, +0x115 u8, +0x116 u8, +0x124 u32, +0x128 f32, +0x12D u8, +0x130 f32, +0x134 f32, +0x138 f32, +0x13C f32, +0x140 f32, +0x144 f32, +0x148 f32, +0x14C f32, +0x150 f32, +0x154 f32, +0x158 f32, +0x15C f32, +0x160 f32, +0x164 f32, +0x168 f32, +0x16C f32, +0x190 u32, +0x194 u32, +0x198 u32.

### 0x6BD blob_spawner

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x570; 10 placed in the game (tombli, valkyrie_fleet, zolar_forest); e.g. `blob_spawner_kp#_#`, `BLOB_SPAWNER_#`, `blob_spawner#`; own methods: fix-up 0x000AF7D0, Init 0x000AF878, Update 0x000AFF84.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x494 | u32 | 0 x6, 1 x4 | | |
| +0x508 | u32 | 1 x7, 0 x3 | | |
| +0x50C | reference | Pod, set on 10 of 10: `BLOB_SPAWNER_BATBOT#_#`, `GRIND_VINE_BLOBS`, `POD_FOREST_BLOBS` | Reference slot: resolved by the class's fix-up | E |
| +0x510 | reference | Pod, set on 5 of 10: `GRIND_VINE_BLOBS`, `POD_FOREST_BLOBS`, `POD_WATERFALL_BLOBS` | Reference slot: resolved by the class's fix-up | E |
| +0x514 | reference | Area, set on 8 of 10: `bbot_#_fps_#`, `FIREPOINT_GRINDVINE_BLOBS`, `FIREPOINTS_BLOB_ALLEY_BLOBS#` | | |
| +0x518 | reference | Controller, set on 2 of 10: `BATTLEFIELD_BLOB_FOREST` | | |
| +0x51C | reference | Controller, set on 9 of 10: `bbot_#_fpc_#`, `FIREPOINT_CONTROLLER_GRINDVINE_BLOBS`, `FIREPOINT_CONTROLLER_BLOB_ALLEY_#` | | |
| +0x520 | reference | Volume, set on 5 of 10: `INNER_GRIND_VINE_BLOBS_#`, `INNER_BLOB_ALLEY_#`, `INNER_BLOB_ALLEY_BLOBS#` | | |
| +0x530 | reference | Volume, set on 5 of 10: `OUTER_GRIND_VINE_BLOBS`, `OUTER_BLOB_ALLEY_#`, `OUTER_BLOB_ALLEY_BLOBS#` | | |
| +0x550 | s32 | -1 x7, 30 x1, 25 x1, 12 x1 | | |
| +0x554 | u32 | 2 x6, 3 x3, 1 x1 | | |
| +0x564 | 4 bytes | 0x00010400 x5, 0x00010600 x2, 0x00000300 x1, 0x00010A00 x1, 0x00010300 x1 | | |

Same on every instance: +0x70 = 1, +0x74 = 1, +0x78 = 1, +0x480 = 1, +0x490 = 65535, +0x528 = 30, +0x538 = 30.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x7C u32, +0x80 u32, +0x84 u32, +0x88 u32, +0xAC f32, +0xB0 f32, +0xB4 f32, +0x108 u32, +0x160 u32, +0x170 f32, +0x174 f32, +0x178 f32, +0x17C f32, +0x180 u32, +0x184 u32, +0x188 u32, +0x210 u8, +0x2A0 u8, +0x330 u8, +0x3C0 u8, +0x450 u8, +0x460 u8, +0x470 u32, +0x474 f32, +0x47C f32, +0x484 u32, +0x488 f32, +0x48C u32, +0x490 u8, +0x4A0 u32, +0x524 u32, +0x534 u32, +0x540 u32, +0x544 u32, +0x548 u32, +0x54C u32, +0x558 u32, +0x55C u32, +0x560 u32.

### 0x6C7 subcon_mini_qwark

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xBF0; 28 placed in the game (great_clock_b, great_clock_c); e.g. `SUBCON_MINI_QWARK_#`, `mquark_wave#_dude#`, `CutsceneMiniQwark#`; own methods: Init 0x00120EC0, Update 0x00120E9C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xBE0 | 4 bytes | 0x00000000 x22, 0x00000001 x3, 0x00010000 x3 | | |

Same on every instance: +0x920 = 322, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 u32, +0x9F4 u32, +0x9F8 f32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32.

### 0x6CD nefarious_large_trooper

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC50; 113 placed in the game (6 levels); e.g. `nefarious_large_trooper#`, `hangar_lrg_far_L#`, `hangar_lrg_near_R#`; own methods: fix-up 0x00121FD4, Init 0x001222F0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xBF4 | reference | Volume, set on 19 of 113: `volume#`, `bt_swarmers_spawner#_spawn_vol#`, `lt_#_swarmer_spawn_vol` | Reference slot: resolved by the class's fix-up | E |
| +0xBF8 | reference | Area, set on 8 of 113: `SECU_ENEMIES_SPAWN_AREA`, `TD_NW_SPAWNSWARMER_AREA`, `setup#_lt_swarmer_spawnvols` | Reference slot: resolved by the class's fix-up | E |
| +0xBFC | u32 | 0 x109, 5 x3, 100 x1 | | |
| +0xC0C | u32 | 0 on all 113 | Script: written by `nef_trooper_spawn_swarmers` | E S |
| +0xC10 | u32 | 0 on all 113 | Script: written by `nef_trooper_spawn_swarmers` | E S |
| +0xC14 | u32 | 0 on all 113 | Script: written by `nef_trooper_spawn_swarmers` | E S |
| +0xC18 | reference | Moby, set on 27 of 113: `entrance_swarmer_template`, `security_swarmer_template`, `control_swarmer_template` | Reference slot: resolved by the class's fix-up | E |
| +0xC1C | u16 | 0 on all 113 | Script: written by `nef_trooper_spawn_swarmers` | E S |
| +0xC24 | u32 | 1 x80, 0 x33 | | |
| +0xC28 | u32 | 1 x58, 0 x55 | | |

Same on every instance: +0x920 = 322, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 u32, +0x9F4 u32, +0x9F8 u32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBF0 u32, +0xC00 u32, +0xC04 u32, +0xC20 f32, +0xC2C u32, +0xC30 f32, +0xC34 u32, +0xC38 u32, +0xC3C f32, +0xC40 f32, +0xC44 u16, +0xC46 u16, +0xC48 u32.

### 0x6CE fongoid_staff_panel

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x80; 10 placed in the game (zolar_forest); e.g. `fongoid_staff_panel#`, `STAFF_PANEL#`, `DEATH_TEMPLE_STAFF_PANEL`; own methods: Init 0x0042B2E8, Update 0x0042B1F4.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x68 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 u8, +0x75 u8.

### 0x6D0 stellar_leviathan

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xAC0; 1 placed in the game (insomniac_museum); e.g. `stellar_leviathan#`; own methods: fix-up 0x001295AC, Init 0x001293A0, Update 0x001298AC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xA90 | reference | Path, set on 1 of 1: `leviathan_path` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x920 f32, +0x924 f32, +0x928 f32, +0x92C f32, +0xA24 f32, +0xA60 f32, +0xA64 f32, +0xA68 f32, +0xA6C f32, +0xA80 f32, +0xA84 f32, +0xA88 f32, +0xA8C f32, +0xA94 u32, +0xA98 u32, +0xA9C f32, +0xAA0 f32, +0xAA4 f32, +0xAA8 f32, +0xAAC f32, +0xAB0 f32.

### 0x6DC zolar_bridge_chunks

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x80; 1 placed in the game (zolar_forest); e.g. `ZOLAR_BRIDGE_CHUNKS`; own methods: Init 0x0041E1F0, Update 0x0041E314.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x70 u32, +0x74 u32, +0x78 u32.

### 0x6E0 nefarious_dropship

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0x1380; 69 placed in the game (5 levels); e.g. `nefarious_dropship#`, `dropship_nw_#`, `DROPSHIP_BB_PLATFORM#_WAVE#`; own methods: fix-up 0x0045F79C, Init 0x0046062C, Update 0x004609BC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x1218 | u32 | 5044 x65, 0 x4 | | |

Same on every instance: +0xF40 = 409, +0x1194 = 0.5, +0x1198 = -0.5, +0x119C = 0.872665, +0x11A0 = 7, +0x11A4 = 18, +0x11A8 = 0.2, +0x11AC = 0.5, +0x11B4 = -25, +0x11B8 = -50, +0x1204 = 0.5, +0x1208 = 0.1, +0x120C = 6.28319.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xF20 u16, +0xF44 f32, +0xF48 f32, +0x1010 f32, +0x1014 f32, +0x1018 u32, +0x103C u32, +0x1090 u32, +0x1094 f32, +0x11E0 u32, +0x1200 f32, +0x1210 u32, +0x1214 f32, +0x121C u32, +0x1220 u32, +0x124C u32, +0x1310 u32, +0x1318 u32, +0x1320 f32, +0x1324 f32, +0x1328 u32, +0x132C u32, +0x1330 f32, +0x1334 u32, +0x1338 u32, +0x136C u32, +0x1370 u32, +0x1374 u8, +0x1375 u8.

### 0x6E7 sigmund

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0x1020; 24 placed in the game (5 levels); e.g. `sigmund`, `sigmund_squawk_boxC#`, `squawk_box_helipack_#`; own methods: fix-up 0x002B8808, Init 0x002B8958, Update 0x002B9028.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x1000 | f32 | 0 on all 24 | Script: written by `sigmund_harrass` | E S |
| +0x1004 | f32 | 0 on all 24 | Script: written by `sigmund_harrass` | E S |
| +0x1008 | f32 | 0 on all 24 | Script: written by `sigmund_harrass` | E S |
| +0x100C | f32 | 0 on all 24 | Script: written by `sigmund_harrass` | E S |
| +0x1010 | reference | unset on all 24 | Reference slot: resolved by the class's fix-up | E |
| +0x1014 | reference | Moby, set on 17 of 24: `sigmund_hologram_projector_#`, `squawk_box_helipack_#_proj`, `gcb_sigmund_projector_#` | Reference slot: resolved by the class's fix-up. Script: read by `sigmund_holo_intro`, `sigmund_holo_outro` | E S |
| +0x101C | u8 | 0 on all 24 | Grounded: Script: written by `sigmund_set_grounded` | E S |
| +0x101F | u8 | 0 on all 24 | Script: written by `sigmund_holo_intro`, `sigmund_holo_outro` | E S |

Same on every instance: +0xD30 = 409, +0xF84 = 0.5, +0xF88 = -0.5, +0xF8C = 0.872665, +0xF90 = 7, +0xF94 = 18, +0xF98 = 0.2, +0xF9C = 0.5, +0xFA4 = -25, +0xFA8 = -50, +0xFF4 = 0.5, +0xFF8 = 0.1, +0xFFC = 6.28319.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xD34 f32, +0xD38 f32, +0xE00 u32, +0xE04 u32, +0xE08 f32, +0xE2C u32, +0xE40 u32, +0xE44 u32, +0xE48 u32, +0xE4C u32, +0xE80 u32, +0xE84 f32, +0xEE4 f32, +0xF50 f32, +0xFA0 u32, +0xFD0 u32, +0xFE4 u32, +0xFF0 f32.

### 0x6E8 hbs_boss

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC30; 1 placed in the game (great_clock_b); e.g. `hbs_boss`; own methods: fix-up 0x0011E724, Init 0x0011E640, Update 0x0011F9F0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xBF0 | reference | Volume, set on 1 of 1: `hbs_boss_anchor` | Reference slot: resolved by the class's fix-up | E |
| +0xBF4 | reference | Pod, set on 1 of 1: `HBS_StageTwo_Beetles` | Reference slot: resolved by the class's fix-up | E |
| +0xBF8 | reference | Pod, set on 1 of 1: `HBS_StageThree_Beetles` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x920 = 409, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50, +0xBE4 = 0.5, +0xBE8 = 0.1, +0xBEC = 6.28319.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 u32, +0x9F4 u32, +0x9F8 f32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBE0 f32, +0xBFC f32, +0xC04 f32, +0xC0C u32, +0xC10 u32, +0xC14 u32, +0xC18 u32, +0xC1C u32, +0xC20 u32, +0xC24 u32, +0xC28 u8, +0xC29 u8, +0xC2A u8, +0xC2B u8, +0xC2C u8, +0xC2D u8, +0xC2E u8, +0xC2F u8.

### 0x6ED galacton_first

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xD80; 3 placed in the game (galacton_ship); e.g. `galacton_first`, `galacton_third`, `galacton_second`; own methods: fix-up 0x0012E590, Init 0x00130350, Update 0x0012E96C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xD38 | reference | Area, set on 3 of 3: `vorselon_cryo_effects` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0xA50 = 322, +0xCA4 = 0.5, +0xCA8 = -0.5, +0xCAC = 0.872665, +0xCB0 = 7, +0xCB4 = 18, +0xCB8 = 0.2, +0xCBC = 0.5, +0xCC4 = -25, +0xCC8 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x920 f32, +0x924 f32, +0x928 f32, +0x92C f32, +0x930 f32, +0x934 f32, +0x938 f32, +0x93C f32, +0x9B0 u32, +0x9B4 u32, +0x9B8 u32, +0x9BC f32, +0x9C0 u32, +0x9C4 u32, +0x9C8 f32, +0x9CC u32, +0x9D4 u32, +0x9DC f32, +0x9E0 f32, +0x9E8 f32, +0x9EC f32, +0x9F0 f32, +0x9F4 f32, +0xA20 u32, +0xA24 u32, +0xA28 u16, +0xA2A u16, +0xA2C u16, +0xA2E u16, +0xA30 u32, +0xA3C u32, +0xA40 f32, +0xA44 f32, +0xA48 f32, +0xA4C f32, +0xA54 u32, +0xA58 u32, +0xB20 u32, +0xB24 u32, +0xB28 f32, +0xB4C u32, +0xB60 u32, +0xB64 u32, +0xB68 u32, +0xB6C u32, +0xBA0 u32, +0xBA4 f32, +0xCF0 u32, +0xCF4 u8, +0xD10 u16, +0xD18 f32, +0xD1C u16, +0xD30 u32, +0xD3C u32, +0xD40 u32, +0xD44 u32, +0xD48 u32, +0xD4C u32, +0xD50 u32, +0xD54 u32, +0xD58 f32, +0xD5C f32, +0xD60 f32, +0xD64 f32, +0xD68 f32, +0xD6C u32, +0xD70 u32, +0xD74 u8, +0xD75 u8, +0xD76 u8, +0xD77 u8, +0xD78 u8.

### 0x708 tb_gc_ringa

Parent 0x54D PlatformRotating > 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x130..+0x440; 55 placed in the game (5 levels); e.g. `tb_gc_ring#a#`, `tb_gc_ring#b#`, `kb_clanksub_spingear_large#`; own methods: Init 0x0036FDAC, Update 0x00370154.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x430 | u32 | 1 x37, 0 x18 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x140 u32, +0x148 u32, +0x190 f32, +0x194 f32, +0x198 f32, +0x19C f32, +0x1C0 u32, +0x1D0 u8, +0x420 u8, +0x434 f32, +0x43C f32.

### 0x70D axiom_cargo

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xF0; 8 placed in the game (axiom_city); e.g. `axiom_cargo#`; own methods: fix-up 0x00447DF4, Init 0x00447D64, Update 0x00447B7C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | reference | Moby, set on 8 of 8: `traversable_carousel#` | Reference slot: resolved by the class's fix-up | E |
| +0x64 | reference | Pod, set on 8 of 8: `CAROUSEL#_SYNC_POD` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xD0 u32, +0xD4 u32, +0xD8 u32, +0xE0 f32.

### 0x71A rotating_magboot_laser_emitters

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x90; 18 placed in the game (galacton_ship); e.g. `rotating_magboot_laser_emitters#`; own methods: fix-up 0x00163ACC, Init 0x001639B4, Update 0x001634DC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | f32 | 25 x5, 0 x5, 180 x3, 90 x2, 45 x1 ... (7 values) | | |
| +0x60 | reference | Moby, set on 7 of 18: `winch_crank#`, `magtube_end_winch`, `grav_tunnel_#_crank` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x64 f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32.

### 0x720 sr_great_clock_elevator_standard

Parent 0x26E SimpleElevator > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x190..+0x190; 10 placed in the game (great_clock_b, great_clock_c, great_clock_d); e.g. `sr_great_clock_elevator_standard#`; own methods: fix-up 0x00361178, Init 0x00361138.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x184 | reference | Moby, set on 10 of 10: `gcd_puzzle#_button_elev#`, `kb_great_clock_pressure_pad#`, `kb_great_clock_pressure_padD#` | Reference slot: resolved by the class's fix-up | E |

### 0x739 occular_scanner

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xC8; 93 placed in the game (5 levels); e.g. `occular_scanner#`, `SCANNER_RIGHT_NORMAL#`, `SCANNER_LEFT_NORMAL#`; own methods: Init 0x00431C84, Update 0x004321F0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | f32 | 8 x28, 25 x27, 12 x18, 14 x4, 9 x3 ... (12 values) | | |
| +0x60 | f32 | 12 x41, 30 x27, 16 x12, 20 x5, 15 x4 ... (6 values) | | |
| +0x90 | u32 | 0 x86, 1 x7 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x64 f32, +0x68 f32, +0x70 f32, +0x78 f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x94 u16, +0xB4 u32, +0xB8 u32, +0xBC f32, +0xC0 f32.

### 0x73D water_tower_a

Parent 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x250..+0x250; 6 placed in the game (krell_canyon); e.g. `water_tower_A#`, `water_tower_A#R`, `water_tower_A#L`; own methods: Init 0x004B1640.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x740 ping_pong_gears_machine

own fields +0x4..+0x550; 1 placed in the game (great_clock_a); e.g. `PING_PONG_GEARS_MACHINE`; own methods: fix-up 0x0091D528.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x10 = 0x01000000, +0x3C = -1, +0x54 = 65535, +0x118 = 256, +0x530 = 1, +0x534 = 1, +0x538 = 1, +0x53C = 7.

### 0x741 kb_gc_power_cord_straight_m

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 186 placed in the game (great_clock_a, great_clock_b, great_clock_c, great_clock_d); e.g. `kb_gc_power_cord_straight_#m#`, `kb_gc_power_cord_turn_#m#`, `kb_gc_power_cord_angle_#m#`; own methods: fix-up 0x0043E6F0, Init 0x0043E6CC, Update 0x0043E5B0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | reference | Moby, set on 135 of 186: `kb_great_clock_pressure_pad#`, `kb_great_clock_pressure_padD#`, `gcd_puzzle#_button_elev#` | Reference slot: resolved by the class's fix-up | E |

### 0x744 puzzle_fancydoor

Parent 0x54A DoorAnimated > 0x548 Door > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x110..+0x130; 13 placed in the game (great_clock_b, great_clock_c, great_clock_d); e.g. `PUZZLE#_FANCYDOOR`, `sr_great_clock_door_combi_lock#`, `GCC_PUZZLE_DOOR_#`; own methods: Init 0x0035D11C, Update 0x0035D90C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x745 lawrence_holo

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD30; 5 placed in the game (great_clock_a, nefarious_station); e.g. `LAWRENCE_HOLO#`, `LAWRENCE_HOLOVID_#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x749 boss_trooper

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC00; 87 placed in the game (valkyrie_fleet); e.g. `boss_trooper_#`, `gondola_trooper_d_#`, `tumbler_#_welcome_ham_#`; own methods: fix-up 0x0012C334, Init 0x0012D2D4, Update 0x0012C5D8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xBE8 | reference | Clue, set on 39 of 87: `nav_network_boss`, `tumbler_#_welcome_commitee_nav_network_R_#`, `tumbler_#_welcome_commitee_nav_network_L_#` | Reference slot: resolved by the class's fix-up | E |
| +0xBF0 | reference | unset on all 87 | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x920 = 322, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 u32, +0x9F4 u32, +0x9F8 u32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBC8 f32, +0xBCC f32, +0xBE0 u32, +0xBE4 u8, +0xBEC u32, +0xBF4 u8, +0xBF8 f32.

### 0x74C tumbler_puzzle

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x440; 3 placed in the game (valkyrie_fleet); e.g. `tumbler_puzzle_#`; own methods: fix-up 0x00355988, Init 0x003559C0, Update 0x00355938.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x3EC | u32 | 1 x2, 2 x1 | | |
| +0x3F0 | u32 | 0 x1, 2 x1, 1 x1 | | |
| +0x3FC | u32 | 1 x2, 2 x1 | | |
| +0x404 | u32 | 0 x2, 2 x1 | | |
| +0x408 | u32 | 2 x2, 0 x1 | | |
| +0x410 | u32 | 2 x2, 0 x1 | | |
| +0x424 | u32 | 0 x2, 1 x1 | | |
| +0x428 | u32 | 0 x1, 2 x1, 1 x1 | | |
| +0x42C | f32 | 180 x2, 90 x1 | | |
| +0x430 | reference | unset on all 3 | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x398 = 2, +0x3A8 = -1, +0x3BC = 0x01000000, +0x40C = 2, +0x41C = 2.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 u32, +0x74 u32, +0x78 u32, +0x100 u8, +0x190 u8, +0x220 u8, +0x2B0 u8, +0x340 u8, +0x350 u8, +0x378 u32, +0x37C u32, +0x380 u32, +0x384 u32, +0x3B0 u32, +0x3C0 f32, +0x3C4 f32, +0x3C8 f32, +0x3CC f32, +0x3D0 f32, +0x3D4 f32, +0x3D8 f32, +0x3DC f32, +0x3E0 f32, +0x3E4 u32, +0x3E8 u8, +0x434 u8, +0x43C u8, +0x43D u8.

### 0x74D swarm_block

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x80; 1 placed in the game (tombli); e.g. `SWARM_BLOCK`; own methods: Init 0x00390F54.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x5C = 2, +0x60 = 2, +0x64 = 1, +0x68 = 1, +0x70 = 1, +0x74 = 10, +0x78 = 10.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C f32, +0x7C u8.

### 0x762 jn_axm_planter_breakable

Parent 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x200..+0x230; 233 placed in the game (9 levels); e.g. `jn_axm_planter_breakable#`, `cc_nss_small_cover#`, `tj_gal_breakable_cover#`; own methods: Init 0x004B1490, Update 0x004B14F8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x200 | f32 | 0.5 x124, 0.2 x58, 0.15 x29, 0 x22 | | |
| +0x204 | u32 | 11 x61, 20 x52, 12 x39, 9 x26, 7 x23 ... (7 values) | | |
| +0x208 | f32 | 0 x200, 0.2 x33 | | |
| +0x20C | s32 | 14 x49, 35 x41, -1 x39, 15 x33, 29 x18 ... (9 values) | | |
| +0x218 | f32 | 10 x215, 6 x18 | | |
| +0x21C | f32 | 5 x202, 4 x18, 3.5 x13 | | |
| +0x220 | s32 | 0 x110, 2 x55, -1 x39, 1 x29 | | |

Same on every instance: +0x214 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x224 u32.

### 0x775 hsb_ambient

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xBF0; 13 placed in the game (great_clock_a, great_clock_b, insomniac_museum); e.g. `HSB_ambient_#_#`, `HBS_AMBIENT_#_#`, `OPENING_CAM_HBS_#_#`; own methods: fix-up 0x0012056C, Init 0x001204B0, Update 0x0012047C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xBE0 | reference | Scent, set on 11 of 13: `scent_layer_HBS_AMB_gca_#`, `HBS_AMBIENT_#_#_SCENT_LAYER`, `scent_layer_HBS_AMB_gca_OCAM_#_#` | Reference slot: resolved by the class's fix-up | E |
| +0xBE4 | reference | Path, set on 13 of 13: `hbs_ambient_path_#_#`, `hbs_#_#ambient_curve`, `OPENING_CAM_HBS#_#_PATH` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x920 = 322, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 u32, +0x9F4 u32, +0x9F8 f32, +0xA1C u32, +0xA30 u32, +0xA34 u32, +0xA38 u32, +0xA3C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBE8 u32, +0xBEC u32.

### 0x77B boss_azimuth

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xF20; 1 placed in the game (great_clock_e); e.g. `BOSS_AZIMUTH`; own methods: fix-up 0x00112518, Init 0x00113760, Update 0x0011238C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x9A0 | reference | Volume, set on 1 of 1: `VOL_GCD_AZIMUTH_BOSS_ANCHOR` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x950 = 221, +0xAB0 = 409, +0xD04 = 0.5, +0xD08 = -0.5, +0xD0C = 0.872665, +0xD10 = 7, +0xD14 = 18, +0xD18 = 0.2, +0xD1C = 0.5, +0xD24 = -25, +0xD28 = -50, +0xD74 = 0.5, +0xD78 = 0.1, +0xD7C = 6.28319.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x934 u32, +0x938 u32, +0x93C u32, +0x940 u32, +0x944 u32, +0x948 u32, +0x984 u32, +0x9A4 u32, +0xAB4 u32, +0xAB8 u32, +0xB80 u32, +0xB84 u32, +0xB88 u32, +0xBAC u32, +0xC00 u32, +0xC04 f32, +0xD50 u32, +0xD70 u32, +0xD80 u32, +0xD84 u16, +0xD86 u16, +0xD88 u16, +0xD8A u16, +0xD8C u32, +0xD90 u32, +0xD94 u32, +0xE30 u32, +0xE34 f32, +0xE38 f32, +0xE8C u32, +0xE90 f32, +0xE94 f32, +0xE98 f32, +0xE9C f32, +0xEA0 f32, +0xEA4 f32, +0xEA8 f32, +0xEAC f32, +0xEB0 f32, +0xEB4 f32, +0xEB8 u32, +0xEBC u32, +0xEC4 u32, +0xED0 u32, +0xED4 u32, +0xED8 f32, +0xEDC f32, +0xEE0 u32, +0xEEC u32, +0xEF0 f32, +0xEF4 u32, +0xEF8 u32, +0xEFC f32, +0xF00 f32, +0xF04 u32, +0xF08 u32, +0xF0C u32, +0xF10 u32.

### 0x788 boss_cassy

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xA50; 2 placed in the game (valkyrie_fleet); e.g. `boss_cassy`, `boss_cassy#`; own methods: fix-up 0x00119F6C, Init 0x0011A170, Update 0x0011AFD8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x93C | reference | Volume, set on 2 of 2: `vol_cassy_turret` | Reference slot: resolved by the class's fix-up | E |
| +0x940 | reference | Path, set on 2 of 2: `path_cassy_fly` | Reference slot: resolved by the class's fix-up | E |
| +0x944 | reference | Path, set on 2 of 2: `path_cassy_target` | Reference slot: resolved by the class's fix-up | E |
| +0x948 | reference | Volume, set on 2 of 2: `vol_boss_plat_#` | | |
| +0x94C | reference | Volume, set on 2 of 2: `vol_boss_plat_#` | | |
| +0x950 | reference | Volume, set on 2 of 2: `vol_boss_plat_#` | | |
| +0x954 | reference | Volume, set on 2 of 2: `vol_boss_plat_#` | | |
| +0x958 | reference | Volume, set on 2 of 2: `vol_boss_plat_fp_#` | | |
| +0x95C | reference | Volume, set on 2 of 2: `vol_boss_plat_fp_#` | | |
| +0x960 | reference | Volume, set on 2 of 2: `vol_boss_plat_fp_#` | | |
| +0x964 | reference | Volume, set on 2 of 2: `vol_boss_plat_fp_#` | | |
| +0x968 | u32 | 0 on all 2 | Script: written by `cassy_exit_arena` | E S |
| +0x990 | u32 | 0 on all 2 | Script: written by `cassy_go_to_volume` | E S |
| +0x994 | u32 | 0 on all 2 | Script: written by `cassy_follow_path` | E S |
| +0x998 | u32 | 0 on all 2 | Script: written by `cassy_follow_path` | E S |
| +0xA3D | u8 | 0 on all 2 | Script: written by `cassy_exit_arena` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x92C u16, +0x99C u32, +0x9A0 f32, +0x9D0 f32, +0x9D4 f32, +0x9D8 f32, +0xA10 f32, +0xA14 u32, +0xA18 u32, +0xA38 u8, +0xA39 u8, +0xA3A u8, +0xA40 u8.

### 0x78F tb_gc_clanksub_zoniscreen

Parent 0x19F MonitorBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x90; 27 placed in the game (great_clock_a, great_clock_b, great_clock_c, great_clock_d); e.g. `tb_gc_clanksub_zoniscreen#`, `HOLOVID_AFTER_OCCULAR`, `HOLOVID_UTURN`; own methods: fix-up 0x0039F684, Init 0x0039F638, Update 0x0039F5F0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | reference | Moby, set on 3 of 27: `NEFARIOUS_HOLOVID_#` | | |
| +0x64 | reference | Moby, set on 3 of 27: `LAWRENCE_HOLOVID_#` | | |
| +0x70 | reference | Volume, set on 3 of 27: `HOLOVID_#_VOL_LINK` | Reference slot: resolved by the class's fix-up | E |
| +0x74 | f32 | 45 x24, 90 x3 | | |
| +0x78 | f32 | 1 x24, 2 x3 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x7C u32, +0x88 u32.

### 0x790 gcb_subcon_tube

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x130; 4 placed in the game (great_clock_b, great_clock_c, great_clock_d); e.g. `gcb_subcon_tube`, `gcb_subcon_tube_return`, `gcc_subcon_tube`; own methods: fix-up 0x0042EEAC, Init 0x0042F6DC, Update 0x0042F878.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x108 | reference | Moby, set on 4 of 4: `sr_gc_mnemonic_chamber_upper_arm#` | Reference slot: resolved by the class's fix-up | E |
| +0x10C | reference | Moby, set on 4 of 4: `sr_gc_mnemonic_chamber_lower_arm#` | Reference slot: resolved by the class's fix-up | E |
| +0x110 | reference | Moby, set on 1 of 4: `sr_gc_mnemonic_chamber_lower_arm_broken#` | Reference slot: resolved by the class's fix-up | E |
| +0x114 | reference | Moby, set on 1 of 4: `sr_gc_mnemonic_chamber_upper_arm_broken#` | Reference slot: resolved by the class's fix-up | E |
| +0x118 | reference | Moby, set on 1 of 4: `sr_gc_mnemonic_chamber_tube_broken#` | Reference slot: resolved by the class's fix-up | E |
| +0x11C | reference | Moby, set on 4 of 4: `gcb_mstation_ring_upper`, `gcc_mstation_ring_upper`, `gcd_mstation_ring_upper` | Reference slot: resolved by the class's fix-up | E |
| +0x120 | reference | Moby, set on 4 of 4: `gcb_mstation_ring_lower`, `gcc_mstation_ring_lower`, `gcd_mstation_ring_lower` | Reference slot: resolved by the class's fix-up | E |
| +0x128 | 4 bytes | 0x00000000 x3, 0x01000000 x1 | | |

Same on every instance: +0xC8 = 442, +0xD4 = 3, +0xE8 = 2, +0xF8 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x70 u32, +0x74 u32, +0xB8 u32, +0xBC u32, +0xD0 u32, +0xD8 u32, +0xDC u32, +0xE0 u32, +0x100 u32, +0x104 u8, +0x105 u8, +0x106 u8, +0x107 u8.

### 0x79B kb_gc_fixable_sconce_b

Parent 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x200..+0x230; 110 placed in the game (great_clock_a, great_clock_b, great_clock_c, great_clock_d); e.g. `kb_gc_fixable_sconce_b#`, `sr_gc_fixable_pipe_big#`, `kb_gc_fixable_sconce_a#`; own methods: Init 0x004B4BDC, Update 0x004B453C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x218 | u32 | 0 on all 110 | Script: written by `fixable_fix`, `fixable_break` | E S |
| +0x21C | u32 | 256 x69, 257 x41 | | |
| +0x220 | 4 bytes | 0x00000000 x96, 0x01000000 x13, 0x00010000 x1 | | |

Same on every instance: +0x20C = 1.2, +0x214 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x210 f32.

### 0x79C sigmund_squawk_box

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x78; 1 placed in the game (insomniac_museum); e.g. `sigmund_squawk_box#`; own methods: Init 0x0043A40C, Update 0x0043A484.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C f32.

### 0x7B0 kb_gc_platform_gun_hazard

Parent 0x3ED PlatformMoving > 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x1B0..+0x240; 17 placed in the game (great_clock_b, great_clock_c, great_clock_d, great_clock_e); e.g. `kb_gc_platform_gun_hazard#`, `kb_clanksub_platform_gun_hazard#`, `GCC_SUBCON_TURRET_PLAT#`; own methods: fix-up 0x00372578, Init 0x003725B8, Update 0x00372FC0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x1C0 | reference | Volume, set on 17 of 17: `volume_test_gun_platform#`, `TURRET_PLAT_TARGET_#`, `vol_target_gun_hazard#` | Reference slot: resolved by the class's fix-up | E |
| +0x210 | f32 | 5 x12, 24 x3, 16 x2 | | |
| +0x230 | u32 | 0 x14, 1 x3 | | |

Same on every instance: +0x204 = 2, +0x208 = 1, +0x220 = 0.1, +0x224 = 0.25.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1B0 u16, +0x1D0 f32, +0x1D4 f32, +0x1D8 f32, +0x1DC u32, +0x1E0 f32, +0x1E4 f32, +0x1E8 f32, +0x1EC f32, +0x1F0 f32, +0x1F4 f32, +0x1F8 f32, +0x1FC f32, +0x200 u32, +0x20C f32, +0x218 f32, +0x21C f32, +0x228 f32, +0x22C f32.

### 0x7BE boss_ring_seg_c

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xD0; 10 placed in the game (nefarious_station); e.g. `BOSS_RING_SEG_C`, `BOSS_RING_SEG_B`, `BOSS_RING_SEG_G`; own methods: fix-up 0x0013E8D0, Init 0x0013E0EC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | reference | Volume, set on 10 of 10: `vol_ring_bn#_pos#` | | |
| +0x60 | reference | Volume, set on 9 of 10: `vol_ring_bn#_pos#` | | |
| +0x64 | reference | Volume, set on 10 of 10: `vol_ring_bn#_pos#` | | |
| +0x68 | reference | Volume, set on 10 of 10: `vol_ring_bn#_pos#` | | |
| +0x6C | reference | Volume, set on 10 of 10: `vol_ring_bn#_pos#` | | |
| +0x70 | reference | Volume, set on 9 of 10: `vol_ring_bn#_pos#` | | |
| +0x74 | reference | Volume, set on 10 of 10: `vol_ring_bn#_pos#` | | |
| +0x78 | reference | Volume, set on 10 of 10: `vol_ring_bn#_pos#` | | |
| +0x7C | 4 bytes | 0x01000101 x5, 0x01010101 x2, 0x00010101 x1, 0x00000101 x1, 0x01010001 x1 | | |
| +0x80 | 4 bytes | 0x01010101 x3, 0x01000101 x2, 0x01010100 x2, 0x00010101 x1, 0x00000100 x1 ... (6 values) | | |
| +0xBC | reference | unset on all 10 | Reference slot: resolved by the class's fix-up | E |
| +0xC0 | reference | Pod, set on 10 of 10: `BOSS_RING` | Reference slot: resolved by the class's fix-up | E |
| +0xC4 | f32 | 100 x9, 10 x1 | | |
| +0xCC | reference | Moby, set on 10 of 10: `BOSS_NEFARIOUS` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xC8 u32.

### 0x7BF boss_nefarious

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xA50; 2 placed in the game (nefarious_station); e.g. `BOSS_NEFARIOUS#`, `BOSS_NEFARIOUS`; own methods: fix-up 0x00137BEC, Init 0x00138D04, Update 0x00139FBC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x958 | reference | Volume, set on 2 of 2: `VOL_left_eye` | Reference slot: resolved by the class's fix-up. Script: read by `nefarious_laser_create` | E S |
| +0x95C | reference | Volume, set on 2 of 2: `VOL_laser_pen_left` | Reference slot: resolved by the class's fix-up. Script: read by `nefarious_laser_create` | E S |
| +0x960 | reference | Volume, set on 2 of 2: `VOL_right_eye` | Reference slot: resolved by the class's fix-up. Script: read by `nefarious_laser_create` | E S |
| +0x964 | reference | Volume, set on 2 of 2: `VOL_laser_pen_right` | Reference slot: resolved by the class's fix-up. Script: read by `nefarious_laser_create` | E S |
| +0x968 | reference | Volume, set on 2 of 2: `VOL_eye_laser_reflect` | Reference slot: resolved by the class's fix-up. Script: read by `nefarious_laser_create` | E S |
| +0x96C | reference | Pod, set on 2 of 2: `BOSS_RING` | Reference slot: resolved by the class's fix-up | E |
| +0x970 | u32 | 0 on all 2 | Script: read by `nefarious_laser_create` | E S |
| +0x9D0 | u32 | 0 on all 2 | Script: written by `nefarious_follow_path` | E S |
| +0x9D4 | u32 | 0 on all 2 | Script: written by `nefarious_go_to_volume` | E S |
| +0xA08 | f32 | 0 on all 2 | Speed: Script: written by `nefarious_set_speed` | E S |
| +0xA0C | f32 | 0 on all 2 | Speed: Script: written by `nefarious_set_speed` | E S |
| +0xA10 | u32 | 0 on all 2 | Script: written by `nefarious_laser_create` | E S |
| +0xA14 | f32 | 0 on all 2 | Script: read by `nefarious_script_done` | E S |
| +0xA18 | u32 | 0 on all 2 | Script: written by `nefarious_follow_path` | E S |
| +0xA45 | u8 | 0 on all 2 | Script: written by `nefarious_laser_stop_chase` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x928 u16, +0x948 u32, +0x954 u32, +0x974 u32, +0x978 u32, +0x97C u32, +0x980 f32, +0x984 f32, +0x988 f32, +0x98C f32, +0x990 f32, +0x994 f32, +0x998 f32, +0x99C f32, +0x9A0 f32, +0x9A4 f32, +0x9A8 f32, +0x9AC f32, +0x9B0 f32, +0x9B4 f32, +0x9B8 f32, +0x9BC f32, +0x9C0 f32, +0x9C4 f32, +0x9C8 f32, +0x9CC u32, +0x9F0 f32, +0x9F4 f32, +0x9F8 f32, +0x9FC f32, +0xA00 f32, +0xA04 f32, +0xA1C f32, +0xA20 u32, +0xA24 u32, +0xA28 u32, +0xA2C u32, +0xA30 u32, +0xA34 u32, +0xA38 u32, +0xA3C u32, +0xA40 u8, +0xA41 u8, +0xA42 u8.

### 0x7C0 boss_ship_in_space

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x1D0; 2 placed in the game (nefarious_station); e.g. `BOSS_SHIP_IN_SPACE`, `BOSS_SHIP`; own methods: Init 0x001355E0, Update 0x00136EE0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x1A8 | u32 | 0 on all 2 | Script: written by `lawrence_go_to_volume` | E S |
| +0x1AC | u32 | 0 on all 2 | Script: written by `lawrence_follow_path` | E S |
| +0x1B0 | f32 | 0 on all 2 | Speed: Script: written by `lawrence_set_speed` | E S |
| +0x1B4 | f32 | 0 on all 2 | Speed: Script: written by `lawrence_set_speed` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x60 f32, +0x64 u32, +0x6C u32, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xD0 u32, +0xD4 u32, +0xD8 u32, +0x13C u16, +0x148 u16, +0x180 f32, +0x184 f32, +0x188 f32, +0x18C f32, +0x190 f32, +0x194 f32, +0x198 f32, +0x19C f32, +0x1A0 f32, +0x1A4 f32, +0x1C5 u8, +0x1C6 u8, +0x1C7 u8.

### 0x7E3 time_portal

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xA0; 4 placed in the game (gimlick_valley, tombli); e.g. `time_portal#`, `PORTAL_PAST`, `PORTAL_SAD_PRESENT`; own methods: fix-up 0x00440FE0, Init 0x00441028, Update 0x00440AE0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x84 | u32 | 2 x1, 1 x1, 5 x1, 4 x1 | Id: Script: written by `time_portal_set_id` | E S |
| +0x88 | reference | Moby, set on 4 of 4: `time_portal#`, `PORTAL_SAD_PRESENT`, `PORTAL_PAST` | Reference slot: resolved by the class's fix-up | E |
| +0x8C | reference | Volume, set on 4 of 4: `VOL_RESPAWN#`, `VOL_WARP_PRESENT`, `VOL_RATCHET_PAST_WARP` | Reference slot: resolved by the class's fix-up. Script: written by `time_portal_set_target_volume`, `time_portal_clear_target_volume` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0x90 u32, +0x94 f32, +0x98 u8.

### 0x7E9 bomb_emitter

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 1 placed in the game (valkyrie_fleet); e.g. `bomb_emitter`; own methods: Init 0x00449460, Update 0x0044982C.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x5C = 2.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 u8, +0x65 u8, +0x66 u8, +0x67 u8, +0x68 u8, +0x70 f32, +0x74 u32, +0x78 u32, +0x7C u32, +0x80 u8, +0x81 u8.

### 0x7EB tj_val_bomb_plate_c

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xE0; 36 placed in the game (valkyrie_fleet); e.g. `tj_val_bomb_plate_c#`, `tj_val_bomb_plate_b#`, `tj_val_bomb_plate_a#`; own methods: Init 0x0044A418, Update 0x0044A3D4.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xD4 | u32 | 9 x12, 7 x12, 5 x12 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xC0 u32, +0xC4 u32, +0xC8 u32, +0xD0 u32.

### 0x7ED ml_tombli_force_field_emitter

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 10 placed in the game (tombli); e.g. `ml_tombli_force_field_emitter#`, `FORCE_FIELD_GUARD`, `DERRICK_FIELD`; own methods: Init 0x0042379C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x7F3 tj_val_floor_ring_c

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xF0; 3 placed in the game (valkyrie_fleet); e.g. `tj_val_floor_ring_c#`, `tj_val_floor_ring_a#`, `tj_val_floor_ring_b#`; own methods: fix-up 0x0044A7AC, Init 0x0044A5D0, Update 0x0044A7F4.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | f32 | 1 x1, 1.5 x1, -1 x1 | Gas speed: Script: written by `vap_set_gas_speed` | E S |
| +0x74 | reference | Moby, set on 3 of 3: `gas_room_pad#` | Reference slot: resolved by the class's fix-up | E |
| +0x78 | reference | Moby, set on 3 of 3: `gas_room_pad#` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xB0 f32, +0xB4 f32, +0xB8 f32, +0xBC f32, +0xE0 u32, +0xE4 u32, +0xE8 u32.

### 0x7F4 breaking_floor

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xF90; 1 placed in the game (valkyrie_fleet); e.g. `breaking_floor`; own methods: Init 0x0044AF98, Update 0x0044B350.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x70 = 120.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x74 f32, +0x78 u32, +0x7C u32, +0xF80 u8.

### 0x800 security_module

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x580; 4 placed in the game (insomniac_museum); e.g. `security_module_#`, `spinning_security_module#`; own methods: fix-up 0x004DD274, Init 0x004DD2AC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x544 | u32 | 0 on all 4 | Script: written by `setup_security_module` | E S |
| +0x548 | u32 | 0 on all 4 | Script: written by `setup_security_module` | E S |
| +0x54C | f32 | 0 on all 4 | Script: written by `setup_security_module` | E S |
| +0x550 | u32 | 0 on all 4 | Script: written by `add_security_module_pattern` | E S |
| +0x554 | u32 | 0 on all 4 | Script: written by `add_security_module_pattern` | E S |
| +0x558 | u32 | 0 on all 4 | Script: written by `setup_security_module` | E S |
| +0x55C | f32 | 0 on all 4 | Script: written by `setup_security_module` | E S |
| +0x560 | f32 | 0 on all 4 | Script: written by `setup_security_module` | E S |
| +0x564 | f32 | 0 on all 4 | Script: written by `add_security_module_pattern` | E S |
| +0x570 | u32 | 0 on all 4 | Script: written by `security_module_bypass` | E S |
| +0x574 | reference | Volume, set on 3 of 4: `sec#_dropoff` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 u32, +0x74 u32, +0x78 u32, +0x100 u8, +0x190 u8, +0x220 u8, +0x2B0 u8, +0x340 u8, +0x350 u8, +0x360 u32, +0x540 f32, +0x568 u32, +0x56C u32.

### 0x804 orvus_staff_chamber

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x70; 1 placed in the game (great_clock_b); e.g. `orvus_staff_chamber`; own methods: Init 0x00432C48, Update 0x00432E1C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x5E u16, +0x60 f32, +0x64 f32, +0x68 u16.

### 0x806 orvus_staff_pickup

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xC8; 1 placed in the game (great_clock_b); e.g. `orvus_staff_pickup`; own methods: fix-up 0x004340B8, Init 0x00434050, Update 0x00434018.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC0 | reference | Controller, set on 1 of 1: `orvus_staff_collect_cam` | Reference slot: resolved by the class's fix-up | E |
| +0xC4 | reference | Volume, set on 1 of 1: `orvus_staff_collect_vol` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xB8 f32, +0xBC f32.

### 0x80B tb_gimlick_grindvine_spike_moby

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x180; 15 placed in the game (gimlick_valley, tombli); e.g. `tb_gimlick_grindvine_spike_moby#`; own methods: Init 0x0042894C, Update 0x00428A20.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x60 = 1, +0x64 = 1, +0x68 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x70 u32, +0x74 u32, +0x78 u32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0x150 u32, +0x168 u32, +0x16C f32, +0x170 f32.

### 0x811 pollen_plant

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3A0; 15 placed in the game (gimlick_valley, tombli, valkyrie_fleet); e.g. `pollen_plant#`; own methods: Init 0x00436258, Update 0x004366C4.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x70 u32, +0x74 u32, +0x78 f32, +0x7C u32, +0x80 f32, +0x84 u8, +0x85 u8, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 u32, +0xA4 u32, +0xA8 u32, +0x130 u8, +0x1C0 u8, +0x250 u8, +0x2E0 u8, +0x370 u8, +0x380 u8, +0x394 f32.

### 0x815 death_beam_turret

Parent 0x366 TurretBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x440..+0x5D0; 1 placed in the game (nefarious_station); e.g. `DEATH_BEAM_TURRET`; own methods: fix-up 0x003831DC, Init 0x00382D50, Update 0x00384084.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x58C | reference | Volume, set on 1 of 1: `VOL_DEATH_BEAM_PROMPT` | Reference slot: resolved by the class's fix-up | E |
| +0x59C | u32 | 0 on all 1 | Script: written by `turret_lock`, `turret_unlock` | E S |

Same on every instance: +0x440 = 221, +0x490 = 221.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x474 u32, +0x4C4 u32, +0x4E0 u32, +0x4E4 u32, +0x510 f32, +0x514 f32, +0x518 f32, +0x51C f32, +0x560 f32, +0x564 f32, +0x568 f32, +0x56C f32, +0x570 f32, +0x574 f32, +0x578 f32, +0x57C f32, +0x580 u32, +0x584 f32, +0x588 u32, +0x590 f32, +0x594 f32, +0x5A0 u32.

### 0x816 blueprint

Parent 0xD8 PickupSpecialBase > 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xF0..+0x110; 8 placed in the game (8 levels); e.g. `blueprint#`, `RYNO_PART`, `RYNO`; own methods: Init 0x004BB84C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xF0 u32, +0xFC u32, +0x100 u32, +0x104 u32.

### 0x818 lagoon_bird

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xA0; 65 placed in the game (axiom_city, zolar_forest); e.g. `Lagoon_bird#`, `Bird_#_#`, `ambientBird#_#`; own methods: fix-up 0x00106534, Init 0x0010657C, Update 0x00106E98.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x7C | reference | Path, set on 56 of 65: `Lagoon_Bird_Path_#`, `bird_#_#_path`, `curve#` | Reference slot: resolved by the class's fix-up | E |
| +0x80 | reference | Moby, set on 4 of 65: `TEMPLE#_PILLAR#`, `lv_breaking_pillar_#m#` | Reference slot: resolved by the class's fix-up | E |
| +0x90 | u32 | 2 x52, 0 x9, 1 x4 | | |
| +0x98 | f32 | 0.2 x32, 0.13 x17, 0.18 x5, 0.1 x3, 0.3 x3 ... (9 values) | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x5E u16, +0x60 f32, +0x64 f32, +0x68 u16, +0x84 u32, +0x88 f32, +0x94 u8.

### 0x824 orvus_door

Parent 0x54A DoorAnimated > 0x548 Door > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x110..+0x2A0; 9 placed in the game (insomniac_museum, zolar_forest); e.g. `ORVUS_DOOR_#`, `RETURN_TRIP_ORVUS_DOOR_#`, `corn#`; own methods: fix-up 0x0035F37C, Init 0x0035EB0C, Update 0x0035E6BC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x1C0 | reference | Path, set on 9 of 9: `RIGHT_EYE_RETURN_TRIP_ORVUS_DOOR_#`, `RIGHT_EYE_ORVUS_DOOR_#`, `corn_lefteye#` | | |
| +0x1C4 | f32 | 6 x2, 2 x2, 10 x1, 8 x1, 8.5 x1 ... (7 values) | | |
| +0x280 | reference | Path, set on 9 of 9: `LEFT_EYE_RETURN_TRIP_ORVUS_DOOR_#`, `LEFT_EYE_ORVUS_DOOR_#`, `corn_righteye#` | | |
| +0x284 | f32 | 6 x2, 2 x2, 10 x1, 8 x1, 8.5 x1 ... (7 values) | | |
| +0x294 | reference | Pod, set on 6 of 9: `EXIT_ORVUS_DOORS`, `ENTRANCE_ORVUS_DOORS` | Reference slot: resolved by the class's fix-up | E |

Same on every instance: +0x298 = 0x00010000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1C8 u32, +0x1CC u32, +0x288 u32, +0x28C u32, +0x290 u32, +0x298 u8, +0x29A u8.

### 0x829 zf_gear_hazard

Parent 0x54D PlatformRotating > 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x130..+0x170; 3 placed in the game (zolar_forest); e.g. `zf_gear_hazard#`; own methods: Init 0x00369528, Update 0x003695AC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x160 | f32 | 45 x1, -30 x1, -60 x1 | Speed: Script: written by `gear_hazard_set_speed` | E S |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x150 f32, +0x154 f32, +0x158 f32.

### 0x834 lawrence

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x70; 9 placed in the game (great_clock_b, great_clock_c, great_clock_d); e.g. `lawrence#`, `LAWRENCE_#`; own methods: Init 0x0042DB20, Update 0x0042DA08.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x68 | 4 bytes | 0x00010000 x6, 0x00000000 x3 | | |

Same on every instance: +0x5C = 3, +0x60 = 5.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x64 f32.

### 0x836 gcc_clankbooth

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x110; 10 placed in the game (5 levels); e.g. `GCC_CLANKBOOTH#`, `GCA_CLANKBOOTH_#ND`, `GCA_CLANKBOOTH`; own methods: Init 0x0042560C, Update 0x0042569C.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0xB8 = 442, +0xC4 = 3, +0xD8 = 2, +0xE8 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 u32, +0x64 u32, +0x6C u32, +0xA8 u32, +0xAC f32, +0xB4 u32, +0xBC u32, +0xC0 f32, +0xC8 f32, +0xCC u32, +0xD0 u32, +0xF0 u32, +0xF4 u8, +0xF5 u8, +0xF6 u8, +0xF7 u8, +0x108 u32.

### 0x837 emitter

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x78; 57 placed in the game (great_clock_a, great_clock_d, great_clock_e); e.g. `EMITTER_#_#`, `kb_gc_emitter_b#`, `PING_PONG_TIME_RIB_EMITTER_#_#`; own methods: fix-up 0x00441A28, Init 0x00441AA8, Update 0x00441BCC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x6C | reference | Moby, set on 22 of 57: `EMITTER_#_#`, `PING_PONG_TIME_RIB_EMITTER_#_#`, `TIME_STOP_BRIDGE_EMITTER_#_#` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 u32.

### 0x83F grindvine_tree

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xA0; 5 placed in the game (gimlick_valley, tombli); e.g. `GRINDVINE_TREE#`; own methods: Init 0x004291C0, Update 0x00429294.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x6C u32, +0x8C u32, +0x90 u32, +0x94 u32, +0x98 u32, +0x9C u8.

### 0x845 sr_gc_boss_glass_outer

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x70; 20 placed in the game (great_clock_e); e.g. `sr_gc_boss_glass_outer#`, `sr_gc_boss_glass_inner#`; own methods: Init 0x0041A5B0, Update 0x0041A74C.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x6C = 0x00010000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6E u8, +0x6F u8.

### 0x84B test_az_monitor

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xD0; 16 placed in the game (great_clock_e); e.g. `test_az_monitor#`; own methods: Init 0x0041AAA4, Update 0x0041B488.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x74 u32, +0x7C u32, +0xC4 u32, +0xC8 u32.

### 0x85D kb_gc_c_pedestal_a

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 6 placed in the game (great_clock_a, great_clock_c, great_clock_d); e.g. `kb_gc_c_pedestal_a#`, `GCA_TIMEFIX_PEDESTAL`, `planetgameC#_#ped`; own methods: Init 0x00394264.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x85E lower_tower

Parent 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x250..+0x250; 2 placed in the game (zolar_forest); e.g. `LOWER_TOWER`, `UPPER_TOWER`; own methods: fix-up 0x004B195C, Init 0x004B19A0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x248 | reference | Pod, set on 1 of 2: `POD_WALL` | Reference slot: resolved by the class's fix-up | E |

### 0x869 ratchet_rcf_simple

Parent 0xF6 HERO_HeroCharacter > 0xE5 HERO_HeroBase > 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x1AB0..+0x1AC0; 1 placed in the game (front_end); e.g. `ratchet_rcf#_simple#`; own methods: Init 0x0016A3B0, Update 0x0016A224.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1AB0 u32.

### 0x86B dcs_galacton_tease_galacton

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD30; 2 placed in the game (galacton_ship); e.g. `dcs_galacton_tease_galacton_#`, `dcs_glass_vorselon_actor_trip_#`; own methods: Init 0x002BA774, Update 0x002BA6CC.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x86E sr_gc_mnemonic_chamber_lower_arm_broken

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 3 placed in the game (great_clock_d); e.g. `sr_gc_mnemonic_chamber_lower_arm_broken#`, `sr_gc_mnemonic_chamber_tube_broken#`, `sr_gc_mnemonic_chamber_upper_arm_broken#`; own methods: Init 0x004201A8.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32.

### 0x86F nefarious_security_card

Parent 0xD8 PickupSpecialBase > 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xF0..+0xF0; 1 placed in the game (agorian_arena); e.g. `nefarious_security_card`; own methods: Init 0x004B8C20.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x874 cassy_npc_trap

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD30; 4 placed in the game (valkyrie_fleet); e.g. `cassy_NPC_trap_#`, `cassy_NPC_intro`; own methods: Init 0x002B5FD0.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x87B nef_trooper_pingpong

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x90; 7 placed in the game (great_clock_a); e.g. `NEF_TROOPER_PINGPONG_#`; own methods: fix-up 0x00434458, Init 0x004348AC, Update 0x0043462C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | u32 | 1 x5, 2 x1, 0 x1 | | |
| +0x74 | reference | Moby, set on 7 of 7: `PING_PONG_GEARS_MACHINE` | Reference slot: resolved by the class's fix-up | E |
| +0x78 | reference | Volume, set on 7 of 7: `PING_PONG_TROOPER_START_#` | Reference slot: resolved by the class's fix-up | E |
| +0x7C | reference | Volume, set on 7 of 7: `PING_PONG_TROOPER_END_#` | Reference slot: resolved by the class's fix-up | E |
| +0x80 | f32 | 25 x4, 33 x3 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x84 f32, +0x89 u8.

### 0x883 planter

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x370; 6 placed in the game (gimlick_valley, tombli); e.g. `PLANTER#`; own methods: Init 0x00425CC8, Update 0x00425F08.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x68 u16, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0x84 u32, +0x88 u32, +0x110 u8, +0x1A0 u8, +0x230 u8, +0x2C0 u8, +0x350 u8, +0x360 u8.

### 0x885 fishnet

Parent 0x54A DoorAnimated > 0x548 Door > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x110..+0x110; 2 placed in the game (zolar_forest); e.g. `FISHNET#`; own methods: Init 0x00443D10, Update 0x00443CEC.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x88A forcefield_doors

Parent 0x548 Door > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x110..+0x110; 29 placed in the game (axiom_city, nefarious_station); e.g. `forcefield_doors#`, `FORCEFIELD_SP#`, `FORCEFIELD_TC_HALL#`.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x893 security_platform

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x160; 1 placed in the game (tombli); e.g. `SECURITY_PLATFORM`; own methods: fix-up 0x00430BDC, Init 0x00430C08, Update 0x00430D34.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x110 = 1598, +0x11C = 3, +0x130 = 2, +0x140 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x70 u32, +0x74 u32, +0x78 u32, +0x80 u32, +0x90 u32, +0xB2 u8, +0xB8 u32, +0xBC u32, +0x100 u32, +0x104 u32, +0x114 u32, +0x118 u32, +0x120 u32, +0x124 u32, +0x128 u32, +0x148 u32, +0x14D u8, +0x14E u8, +0x14F u8, +0x150 f32, +0x154 u8, +0x155 u8.

### 0x8A2 pa_speaker

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x68; 6 placed in the game (5 levels); e.g. `pa_speaker`, `GCA_PA_SPEAKER`, `GCB_PASPEAKER`; own methods: Init 0x003A6980, Update 0x003A68CC.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x60 u32, +0x64 u32.

### 0x8A5 bucket_of_win_timepuzzle

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 17 placed in the game (great_clock_a, great_clock_b, great_clock_c, great_clock_d); e.g. `bucket_of_win_timepuzzle_#`, `bucket_of_win_C#_#`, `bucket_of_win#`; own methods: Init 0x0042519C, Update 0x00425240.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x6C = 1, +0x7C = 65535.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x60 f32, +0x68 f32, +0x70 u32, +0x74 f32, +0x78 u32, +0x7C u8, +0x80 f32, +0x84 u32.

### 0x8B9 lightning_pillar

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x70; 1 placed in the game (zolar_forest); e.g. `LIGHTNING_PILLAR#`; own methods: Init 0x0041E7C4, Update 0x0041E89C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.
