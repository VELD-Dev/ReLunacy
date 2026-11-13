# A Crack in Time: the classes of the space sectors, field by field

A Crack in Time (NPUA80966, 01.20), `packed/levels/space_sector_1` to `space_sector_5`. Every class a Moby, a
Clue or a controller of the five sectors is an instance of - 100 placed classes, 123 with the bases they
derive from - with what each settings field is, as far as the decrypted EBOOT and the retail files tell.

Measured 2026-11-13. Sector 1 was read from its shipped archive (the 134,875,469 byte backup), not the
edited one. Value statistics are over all 22 levels of the game, so a class also used on the planets shows
the planets' values too.

**Where it stands.** 519 fields are listed; 315 have a meaning, 204 are listed with their values only. The
space-only classes (moons, planets, beams, asteroids, satellites, attack controllers) are the ones read in
the EBOOT; most of the general gameplay classes (enemies, platforms, pads, crates) are named from the data
alone and say so. Nothing here was tested in game except the fields marked U.

## How to read the tables

| Evidence | Means |
| --- | --- |
| E | Read in the EBOOT's code; the address is in the text where it matters |
| T | From the EBOOT's own name tables: text ids, the level enum, class numbers shared with All 4 One |
| D | A pattern in the retail files: the values, the names of the instances, the names of what a reference points at |
| Q | Ported from the QForce analysis in ReLunacy Resurgence (`Docs/reverse/NEW_ENGINE_CAMERAS`, `QFORCE_CLUE_ENEMY_CLASS`): same layout, shifted, and it fits the retail values here |
| S | The level scripts (`scripts/gameplay/*.lua` in the level archive) |
| U | Tested in game by the user |

A row without a meaning is a field some instance sets and nothing explains yet. "Same on every instance"
lines list words that never vary: defaults, or run-time state the level tool saved along.

Types: `reference` is the four-byte instance reference of gp_prius (`type << 10 | layer`, `0xF000 | index`);
`u32 text id` is an entry of the game's localised text table; `run time` means the game overwrites the field
when the level starts, so what the file holds does not matter; `component` is an embedded object (below).
`#` in an instance name stands for a number.

## How a class's settings work

**The settings block is the object.** A Moby, Clue or controller record of `gp_prius.dat` points at a settings
block that starts with the class number. The game copies the block, replaces the class number at +0x00 by
the class's vtable (0x003DF504) and uses the block as the C++ object from then on. So a field offset in the
file is the offset the class's code uses on `this`, and most of a large class is not level design at all: it
is the object's run-time state, saved as zeros or defaults. A space asteroid is 0x530 bytes and the level
sets three of them.

**The registry.** One block of tables at 0x00EDD73C (TOC-0x6C50), filled at start-up by 23 registration
functions (0x003DF404): the size of each class at +0, its parent at +0x2300, its vtable at +0x4600, for 0x8BF
classes. `IsA(class, ancestor)` is 0x003A8CE0; the factory that builds an empty object of a class is the
switch at 0x003DF5AC (jump table at 0x003DF60C). Moby class sizes are rounded up, so a derived class's first
field can sit below its parent's size: every Moby class starts its own fields at +0x5C although the Moby
base says 0x60, and VendorArmor's two references are at +0x2B8 under a VendorBase of 0x2C0. The tables give
such a field to the class whose fix-up or code uses it.

**Vtable slots.** The same in every class of a family, which is what makes a class readable:

| Family | Slots |
| --- | --- |
| Clue (base 0x11D, 5 slots) | 0, 1 destructors; 2 fix-up of references; 3 Init(this, handle) |
| Moby (base 0x47, 58 slots) | 0 fix-up; 3, 4 destructors; 5 Init; 6 Update; 19 GetInterface(class number). Likely, not checked: 8 message handler; 50, 51 on / off |
| Controller (base 0xCF, 23 slots) | 0 fix-up; 5 Init; 7 Update. Likely, not checked: 14, 15 on / off |

**Fix-up.** A reference in the file becomes a run-time handle through 0x006134F4. Each class's fix-up method
calls it on every reference field it owns, which gives the list of a class's references exactly - including
the ones no retail instance sets. Those are the rows that read "Reference slot: resolved by the class's
fix-up".

**Components.** Classes embed smaller objects, and those are registered classes too: `GetInterface` (Moby
slot 19) returns the embedded object for a class number. The ones met here, named by All 4 One's class
table (the numbers are the same in both games for the older classes): 0x137 ECON_Reward (what an object
pays out), 0x4BD SEGMENT_Spawn, 0x51 Health, 0x113 ColorFlash, 0xDD CAM_CameraShakeEffect. An embedded
object shows in the file as its class number at its first word - 221 at FxController +0x150, 1886 (0x75E, an
attack controller) at +0x60 of Vorselon's ship.

**Prompt component.** The "press triangle" prompt is one embedded object used by moons, planets, NPCs,
vendors, teleporters and tubes (Init 0x004DED88, hero-in-range test 0x004DEDF0, fix-up 0x004DF274):

| Offset in the component | Type | Meaning |
| --- | --- | --- |
| +0x58 | u32 text id | The prompt's text (set with 0x004DF184) |
| +0x5C | reference | Volume: used when the range is <= 0 (containment, 0x0061A124) |
| +0x64 | f32 | Range: the prompt shows within this distance of the object, x1.5 once it is showing |
| +0x78 | u32 | 2 on nearly every instance (role open) |
| +0x88 | f32 | -1 on nearly every instance (role open) |
| +0x94 | u8 | Distance in 3D instead of on the ground plane (run time) |
| +0x95..+0x97 | u8 | Enabled, showing, button state (run time) |

It sits at +0x28 in SpaceMoon, +0x30 in SpacePlanet, +0x60 in GenericPrompt, +0x70 in the space NPC and the
Aphelion, +0x80 in PromptTeleporter, +0x68 in the transport tube, +0x1C0 in VendorBase, +0x910 in NPCBase.

**Names in the EBOOT.** Text ids and other enums are tables of (name pointer, value as a float, 0) in the
data segment; `dev/tools/classfields/enums.py` reads them. Text id 442, the default prompt of most
objects, is not in the table. Levels are the `LEVEL_` enum: 0 NONE, 1 GREAT_CLOCK_A, 2 ZOLAR_FOREST,
3 SPACE_SECTOR_1, 4 GALACTON_SHIP, 5 GREAT_CLOCK_B, 6 SPACE_SECTOR_2, 7 MOLONOTH, 8 AXIOM_CITY,
9 GREAT_CLOCK_C, 10 SPACE_SECTOR_3, 11 KRELL_CANYON, 12 AGORIAN_ARENA, 13 TOMBLI, 14 GREAT_CLOCK_D,
15 SPACE_SECTOR_4, 16 VALKYRIE_FLEET, 17 NEFARIOUS_STATION, 18 SPACE_SECTOR_5, 19 GIMLICK,
20 GREAT_CLOCK_E, 21 MUSEUM, 22 FRONTEND.

**Class names.** A Crack in Time's EBOOT has no class name table. A class is named from All 4 One's table
when the number is the same and the instances agree (true for the classes below about 0x6A0; later numbers
differ between the games), from the EBOOT's own lists where one fits (SpaceAsteroid, DerelictShip,
SpaceShockwaveMine...), and otherwise described from its instances, which the section says.

## What is not in the instances

- **Tuning of everything in space** is `space.csv` in `packed/game/global_cached.psarc` (`/data/configs`):
  the Aphelion's speeds and weapons, each enemy ship, mines, capital ships. An asteroid or a mine has no
  per-instance health or damage.
- **The play area** is `BoundsRadius` in `level.csv`; the level's segments, their Pods and reward pools are
  there too.
- **Which moon is which** in the scripts is `scripts/gameplay/tables.lua`, below.

## A moon, end to end

What sector 1's moon1a is made of, as the files and the EBOOT have it. This is the list a new moon has to
reproduce.

| Piece | Where | Notes |
| --- | --- | --- |
| The moon's art | Ties (`volcanic_moon`...), its surface 50 (large) or 25 (small) from the centre | SpaceMoon +0xE0 is that radius |
| `moon1a_moonclue` | Clue, class 0x4BC SpaceMoon, on a sphere volume | The landing target; fields in its section |
| `moon1a_vol` | Volume, a sphere of scale 400 | SpaceMoon +0xD0, and `vol` in the script table |
| `moon1a_landing_pad_beam` | Moby, class 0x72C | SpaceMoon +0xC0: where the ship is put. Optional take-off volume at beam +0x80 |
| Name and prompt | Text ids at SpaceMoon +0xD4 and +0x80 | The prompt id is overwritten at Init; the name is read by the HUD |
| Layer `gameplay_moon1a` | The editor layer of the Clue | **What the moon owns.** SpaceMoon's Init takes every Moby of its own layer, hides and deactivates them until the moon wakes up, and remembers the Zoni (class 0x5D3) and the gold bolt (class 0x210). A new moon needs its Clue and its objects in a layer of their own; the art is in `art_moon1a` |
| `SEGMENT_SS1_MOON1A` | Pod, a level segment of `level.csv` | Lists the moon's crates, enemies, breakables, Zoni, gold bolt: what the spawn rule and the reward pools apply to (below) |
| `moon1a_reward`, `moon1a_goldbolt` | Mobys | Found by class, not by reference |
| `moon1a_goal_cam` | Controller, class 0xD2 CamArgFollowPath | Played by the moon's script (`play_goal_cam`) |
| `moon1a_checkpoint1_vol` | Volume | `checkpoint(ratchet, segment, checkpoint_vol, ...)` after landing |
| Enemy groups `moon1a_apNN` | `_homearea` Area, `_inner_vol`, `_outer_vol`, `_enemies` Pod | EnemyBase +0x814, +0x864, +0x874, +0x8A8 on each enemy; firepoint and battlefield controllers at +0x88C, +0x888 |
| Hills and craters | Mobys, class 0x882 | Each spawns its own ring of hoverboot ramps |
| Script table | `tables.lua`: `vol`, `checkpoint_vol`, `segment`, `clue`, `script`, `beam`, `antennae`, `reward`, `zoni`, `goal_cam`, `collectibles`; optional Pods `deleteme`, `turnmeoff`, `hideme`, `blue` | `main.lua` runs `moons_init` (`space_land_ship(aphelion, clue)`) then the moon's own script while Ratchet is inside `vol` |

The junk trader's station is a SpaceMoon too, with +0xEE = 1, a radius of 20, `depot = true` in its table
and a respawn volume on its landing pad.

## Gravity

Read in the hero's ground code (0x004EF2D4, 0x004F08C0) and the three ClueGravity functions named in that
class's section. Not tested in game.

- **A moon's gravity region is the SpaceMoon Clue's own volume** - the sphere the Clue sits on, not
  `moonXx_vol` and not the radius field. SpaceMoon derives from ClueGravity, so it is in the game's list of
  gravity Clues, and with ClueGravity's own fields at zero the region is the Clue's volume and "up" is the
  line from the sphere's centre to the hero (volume shape 2 at volume +0x7C; the centre is the volume's
  position).
- **On the ground the collision decides first.** The hero's ground probe takes the surface it hit: when byte 2
  of the hit's +0x60 word is 0x1A - most likely the collision material - "up" is the surface normal and the
  Clue is not asked. Otherwise the Clue is used only if its +0x22 is set, which it is not on moons. So walking
  round a moon needs the moon's collision to carry that value.
- **In the air** (no ground under the probe) the gravity Clue the hero is inside gives the direction. Outside
  every gravity Clue, gravity is the world's down, which is the fall into the void seen when leaving a moon.

What this predicts for a test: moving or shrinking the SpaceMoon Clue's own sphere changes where a jump stops
curving back to the moon; changing `moonXx_vol` or the radius does not. The earlier tests on the Clue's size
were made before the stale disk cache was found, so they may not have been loaded at all.

## Segments and the spawn rule

Read in 0x00298B74, which runs once when the level starts (from 0x0021703C, right after the layers are
started). For each segment of the level (`level.csv`), it takes the segment's Pod and, for every Moby in it:

- writes the segment's index into the Moby's ECON_Reward component (+0x22), which is how a crate knows whose
  reward pool it pays from;
- asks 0x0029B390 whether the Moby should exist, and removes it if not.

The segment has a saved state (save data 0x00E249E0 + level x 0x6C8 + segment x 0x2C + 0x600, at +0x20):
0 becomes 1 on the first visit; 2 is a segment already done; 2 becomes 3 when the game-mode flag at
0x00E20E54 is 1 (most likely challenge mode). Each Moby also has one saved "gone" bit per level
(0x002E605C tests it, 0x002E60EC clears it). The rule is the SEGMENT_Spawn component's value - Damageable
+0x5C, EnemyBase +0x844:

| Rule | The Moby is there |
| --- | --- |
| 0 | Always (also any Moby without the component, or outside every segment Pod) |
| 1 | Only while the segment's state is 1: the first visit |
| 2 | Only while the state is 3: the replay in the flagged mode |
| 3 | Unless its gone bit is set: once broken or collected, it stays gone |
| 4 | As 3, but the flagged mode clears the bit first: it comes back there |
| 5 | Never |
| 6 | As 4, and only while the state is 1 |
| 7 | As 4, and only while the state is 3 |

Retail crates: 0 on about 60%, 3 on 20%, 1 on 15%.

## Still open

- Gravity: the value 0x1A in the ground hit is assumed to be the collision material; not checked against the
  moons' collision files, and nothing of the Gravity section is tested in game.
- SpaceAttackController +0x14..+0x34: no reader found. The ships are handed their carrier, not the controller.
- What the game-mode flag at 0x00E20E54 is (challenge mode is a guess), and what sets a Moby's gone bit.
- The second table SpaceMoon's Init looks its layer's name up in (0x00586174 on 0x00F46E00).
- The four blend flags of each CAM_CameraBlendParam block, and every row below without a meaning.

## Reproducing

`dev/tools/classfields/` (local, not in git): `ex.py` / `exall.py` extract the gp_prius files, `survey.py
<class>` prints a class's values over the game, `who.py <class> <offset>` groups instances by a field's
value, `ev.py <address>` lists a function with its field accesses and constants, `ctx.py <class> <offset>`
shows the code around every access to a field in the class's own methods, and `gendoc.py <this file>`
rewrites everything below this line from `annotations.py`.

<!-- generated: dev/tools/classfields/gendoc.py -->

## Shared bases

### 0x6 IGG_GameObjectUpdate

own fields +0x4..+0x8; not placed itself; values below are over its 22371 placed descendants; own methods: fix-up 0x0091D528.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4 | run time |  | Class number at run time (the loader swaps the file's +0x00 class number for the vtable; code tests `+4 == class`) | E |

### 0x47 MobyBaseUpdate

Parent 0x6 IGG_GameObjectUpdate; own fields +0x8..+0x5C; not placed itself; values below are over its 16088 placed descendants.

Every Moby class starts with this. The registry says 0x60, but derived classes put their first field at +0x5C, so the base really ends at 0x5C. Nothing here is level design except the two bytes below: the rest is filled by Init (0x00528CF4): +0x14 the Moby instance pointer, +0x1C = 0x2FFFF, +0x20, +0x38, +0x3A, +0x50, +0x5A.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x10 | u8 | 1 on all 16088 | Base flag +0x10: 1 on every placed instance (role open) | D |
| +0x14 | run time |  | Pointer to the Moby instance (position, model...) | E |
| +0x3C | s32 | -1 on all 16088 | -1 in every file: a run-time slot | D |
| +0x54 | u32 | 65535 on all 16088 | 0xFFFF in every file: a run-time slot | D |
| +0x59 | u8 | 0 x14050, 1 x2038 | Starts deactivated: Init takes the deactivate path (0x005238B4: no update, hidden, collision off) instead of the activate one (0x00523B64); a script turns the object on later (asteroid_radioactive, the second pad of a pair, mission NPCs) | E D |
| +0x5A | run time u8 |  | Initialised: set to 1 by Init | E |

### 0xCF ControllerBaseUpdate

Parent 0x6 IGG_GameObjectUpdate; own fields +0x8..+0x14; not placed itself; values below are over its 6283 placed descendants.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC | f32 | 256 x5366, 15000 x136, 5000 x126, 1000 x122, 4000 x90 ... (22 values) | Range. 256 by default, thousands on effects and spawners seen from far (FX 15000, car spawners 4000). Not read by the base methods; role open (QForce has the same slot) | Q D |

## Clues

### 0x11D ClueBase

own fields +0x4..+0x14; 32 placed in the space sectors, 90 in the game; e.g. `mines#_clue`, `CLUE_HEATMAP_UPPER_RIGHT`, `CLUE_HEATMAP_LOWER_LEFT`; own methods: fix-up 0x00935420, Init 0x00935424.

Clue vtables have 5 slots: 0/1 destructors, 2 fix-up of references, 3 Init(this, handle), 4. The Clue's volume is not a settings field: it is the gp_prius Clue record's +0x00.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x8 | run time |  | The Clue's volume (pointer), set when the Clue is bound | E |
| +0xC | u8 | 1 on all 160 | Enabled at start: 1 on every Clue of the space sectors (0 only on script-driven Clues elsewhere) | D |
| +0x10 | run time |  | The Clue's own handle, stored by Init | E |

### 0x11E ClueGravity

Parent 0x11D ClueBase; own fields +0x14..+0x24; not placed itself; values below are over its 35 placed descendants; own methods: fix-up 0x0015B434.

A gravity region, and SpaceMoon's parent - so every moon Clue is one. At level start 0x004F06AC lists every enabled Clue that is a ClueGravity (list at 0x00F1E534, count at 0x00F1E530). 0x004F076C finds the first one a position is in: its Area (+0x18) when set, else its volume reference (+0x14) when set, else the Clue's own volume. 0x004F0128 then gives the "up" direction there. All four own fields are zero on every SpaceMoon, so a moon's gravity region is the Clue's own sphere and its gravity points at the sphere's centre. See *Gravity*.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | unset on all 35 | Volume to test instead of the Clue's own (0x00920D60); unset on moons | E |
| +0x18 | reference | unset on all 35 | Area to test instead of both (0x0091D7A8); unset on moons | E |
| +0x1C | f32 | 0 on all 35 | With +0x20 set: how much the direction bends with the position along the volume's axis; 0 = straight | E |
| +0x20 | u8 | 0 on all 35 | Directional: gravity follows the volume's own up axis. 0 = by the volume's shape (a sphere pulls to its centre) | E |
| +0x21 | u8 | 0 on all 35 | Inverted: the shape's direction is flipped (walking on the inside) | E |
| +0x22 | u8 | 0 on all 35 | Applies on any ground. 0 = on the ground only the collision surface decides; the Clue acts in the air | E |

### 0x129 ClueDeath

Parent 0x11D ClueBase; own fields +0x14..+0x18; 5 placed in the space sectors, 22 in the game; e.g. `junktrader_death_clue`.

The junk trader station's death volume (junktrader_death_clue), one per sector. Its only own word (+0x14) is zero on every instance of the game.

No instance sets anything in the class's own range.

### 0x4BC SpaceMoon

Parent 0x11E ClueGravity > 0x11D ClueBase; own fields +0x24..+0xF8; 34 placed in the space sectors, 35 in the game; e.g. `moon#a_moonclue`, `moon#b_moonclue`, `moon#x_moonclue`; own methods: fix-up 0x002F30BC, Init 0x002F4BAC.

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

Parent 0x11D ClueBase; own fields +0x14..+0x100; 13 placed in the space sectors, 13 in the game; e.g. `ClueSpacePlanet_Vorselon`, `ss#_planetclue_zolarforest`, `ss#_planetclue_vorselonsship`; own methods: fix-up 0x002F304C, Init 0x002F61D4.

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
| +0x490 | u32 | 1 on all 1148 | 1 on every instance | D |
| +0x4A0 | u32 | 65535 on all 1148 | 0xFFFF on every instance: a run-time slot | D |
| +0x4A4 | run time |  | Run-time pointer and scale (+0x4A4, +0x4A8 = 1.0), set by Init | E |
| +0x4AC | u8 | 1 x984, 0 x164 | 1 on everything except the lava asteroids (asteroid_lava, sector 3). Read when the object is destroyed (0x00313670: when set, 0x00299354(moby, 0) runs - the reward / debris path) | E D |
| +0x4AD | u8 | 1 on all 1148 | 1 on every instance; gates the second half of the same function | E D |
| +0x4AE | run time u16 |  | State flags (+0x4AE, +0x4AF), zeroed by Init | E |

Same on every instance: +0x64 = 1, +0x68 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x74 u32, +0x78 u32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0x150 u32, +0x168 u32, +0x180 f32, +0x184 f32, +0x188 f32, +0x18C f32, +0x190 u32, +0x194 u32, +0x198 u32, +0x220 u8, +0x2B0 u8, +0x340 u8, +0x3D0 u8, +0x460 u8, +0x470 u8, +0x480 u32, +0x484 f32, +0x48C f32, +0x494 u32, +0x498 f32, +0x49C u32.

### 0x24C PathFollower

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x560; 354 placed in the space sectors, 436 in the game; e.g. `space_junk_tetherable_#_#`, `moon_floating_platform_#_#`, `moon_floating_platform#`; own methods: fix-up 0x0039FEDC, Init 0x003A09F4, Update 0x003A161C.

Anything that rides a path: tetherable space junk, the moons' floating platforms, ring asteroids. Fix-up 0x0039FEDC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4C0 | reference | Path, set on 428 of 436: `moon#a_spacejunk_ring#`, `moon#a_asteroidcurve#`, `moon#c_asteroid_path_outer` | The path followed | E D |
| +0x4E0 | f32 | 10 x168, 5 x79, 14 x75, 20 x41, 30 x15 ... (19 values) | Speed-like value (10 by default) | D |
| +0x4E4 | f32 | 10 x162, 28 x59, 20 x53, 30 x27, 35 x18 ... (26 values) | Second speed-like value (10 by default) | D |
| +0x4E8 | f32 | 0 x434, 0.5 x1, 1 x1 | | |
| +0x4EC | f32 | 1 x432, 2 x4 | | |
| +0x4F0 | f32 | 0.5 x347, 0 x51, 0.1 x19, 0.3 x15, 1 x3 ... (6 values) | | |
| +0x4F4 | f32 | 5 x383, 0 x45, 4 x6, 3 x2 | | |
| +0x504 | u32 | 0 x386, 8 x50 | | |
| +0x50C | reference | Pod, set on 10 of 436: `BD_TRAFFIC_SKIFF#_MOVE#`, `FUEL_SHIP#_MOVE_POD`, `BD_SKIFF#_MOVE` | Pod of Mobys carried along (skiffs, fuel ships) | E D |
| +0x510 | reference | Area, set on 3 of 436: `move_area_skiffs_#`, `AZI_SHIP_ESCAPE_MOVE_AREA`, `AZI_SHIP_PICKUP_MOVE_AREA` | Area of stop positions | E D |
| +0x514 | f32 | -1 x392, 20 x28, 70 x13, 65 x3 | -1 by default; 20-70 on some | D |
| +0x52C | reference | Volume, set on 2 of 436: `graveyard_stop#`, `VOL_PLAT#_PICKUP_POS` | Stop volumes (+0x52C..+0x538) | E D |
| +0x530 | reference | Volume, set on 1 of 436: `graveyard_stop#` | | |
| +0x534 | reference | Volume, set on 1 of 436: `graveyard_stop#` | | |
| +0x538 | reference | Volume, set on 1 of 436: `graveyard_stop#` | | |
| +0x540 | 4 bytes | 0x01010000 x259, 0x00000000 x80, 0x00010000 x51, 0x01010100 x40, 0x00010001 x5 ... (6 values) | | |
| +0x544 | 4 bytes | 0x00010100 x241, 0x00000100 x117, 0x00000000 x60, 0x00010000 x10, 0x01010100 x6 ... (7 values) | | |
| +0x548 | 4 bytes | 0x00000000 x328, 0x00000001 x59, 0x00010000 x41, 0x00000100 x8 | | |
| +0x54C | f32 | 0 x377, 15 x59 | | |
| +0x550 | f32 | 0 x377, 50 x59 | | |

Same on every instance: +0x53C = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x5E u16, +0x60 f32, +0x64 f32, +0x68 u16, +0xB0 f32, +0xB4 f32, +0xB8 f32, +0xBC f32, +0xC0 u32, +0xC4 u32, +0xC8 u32, +0xCC u32, +0xD0 f32, +0xD4 f32, +0xD8 f32, +0xDC f32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0x130 f32, +0x134 f32, +0x138 f32, +0x13C f32, +0x140 f32, +0x144 f32, +0x150 f32, +0x154 f32, +0x158 f32, +0x15C f32, +0x160 f32, +0x164 f32, +0x168 f32, +0x16C f32, +0x170 f32, +0x174 f32, +0x178 f32, +0x17C f32, +0x180 f32, +0x184 f32, +0x188 f32, +0x18C f32, +0x1B0 u32, +0x1B4 u32, +0x1B8 u32, +0x1C0 f32, +0x1C4 f32, +0x1C8 f32, +0x1CC f32, +0x1D0 u32, +0x1D4 u32, +0x1D8 u32, +0x260 u8, +0x2F0 u8, +0x380 u8, +0x410 u8, +0x4A0 u8, +0x4B0 u8, +0x4F8 f32, +0x4FC u32, +0x500 u32, +0x518 u32.

### 0x28F Aphelion (ship hero)

Parent 0xE5 HERO_HeroBase > 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x70..+0x1AC0; 5 placed in the space sectors, 5 in the game; e.g. `aphelion`; own methods: fix-up 0x002FB3D4, Init 0x002FC824, Update 0x00303AB0.

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
| +0x16D0 | component |  | CAM_CameraShakeEffect (class 221) embedded | T |

Same on every instance: +0x1840 = 1, +0x1844 = 1, +0x1848 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x110 f32, +0x114 f32, +0x118 f32, +0x11C f32, +0x120 u32, +0x124 u32, +0x128 u32, +0x1B0 u8, +0x240 u8, +0x2D0 u8, +0x360 u8, +0x3F0 u8, +0x400 u8, +0x42C u32, +0x43C u32, +0x450 u16, +0x460 u16, +0x4F4 u32, +0x538 u32, +0x53C f32, +0x15F0 u32, +0x15F4 u32, +0x1680 u8, +0x1681 u8, +0x1682 u8, +0x1683 u8, +0x1684 u8, +0x1685 u8, +0x1686 u8, +0x1687 u8, +0x16C0 u32, +0x16F4 u32, +0x1710 f32, +0x1714 f32, +0x1718 f32, +0x171C f32, +0x1720 f32, +0x1724 f32, +0x1728 f32, +0x1730 f32, +0x1734 f32, +0x1738 f32, +0x173C f32, +0x1740 f32, +0x1744 f32, +0x1748 f32, +0x174C f32, +0x1750 f32, +0x1754 f32, +0x1758 f32, +0x175C f32, +0x1760 f32, +0x1764 f32, +0x1768 f32, +0x176C f32, +0x1770 f32, +0x1774 f32, +0x1778 f32, +0x177C f32, +0x17B0 f32, +0x17B4 f32, +0x17B8 f32, +0x17BC f32, +0x17D0 f32, +0x17D4 f32, +0x17D8 f32, +0x17DC f32, +0x1830 f32, +0x1834 f32, +0x1838 u32, +0x183C u32, +0x186C f32, +0x1870 f32, +0x1874 f32, +0x18CC u32, +0x1920 u32, +0x1930 u32, +0x1934 u32, +0x1938 u32, +0x193C u32, +0x1940 f32, +0x1944 f32, +0x1948 f32, +0x194C f32, +0x1950 f32, +0x1958 f32, +0x195C f32, +0x1968 f32, +0x196C f32, +0x1970 f32, +0x197C f32, +0x1980 f32, +0x1984 f32, +0x1988 f32, +0x198C u32, +0x1990 f32, +0x1994 f32, +0x1998 f32, +0x199C f32, +0x19A0 f32, +0x19A4 f32, +0x19A8 u32, +0x19AC f32, +0x19B0 f32, +0x19B4 f32, +0x19B8 f32, +0x19BC f32, +0x19C0 f32, +0x19C4 f32, +0x19C8 f32, +0x19CC f32, +0x19D0 u32, +0x19D4 f32, +0x19D8 u32, +0x19DC u32, +0x19E0 u32, +0x19E4 u32, +0x19E8 u32, +0x19EC u16, +0x19EE u8, +0x19F5 u8, +0x19F6 u8, +0x19F7 u8, +0x19F8 u8, +0x19F9 u8, +0x19FB u8, +0x19FC u8, +0x19FD u8, +0x19FE u8, +0x19FF u8, +0x1A00 u8, +0x1A01 u8, +0x1A02 u8, +0x1A04 u8, +0x1A05 u8.

### 0x337 SimplePathFollower

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x160; 22 placed in the space sectors, 133 in the game; e.g. `asteroid_habitable_round#`, `transportB#`, `transportC#`; own methods: fix-up 0x003A2120, Init 0x003A2288.

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

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x188; 13 placed in the space sectors, 13 in the game; e.g. `smuggler`, `smugglership`, `carina`; own methods: fix-up 0x0031A1E8, Init 0x0031A214, Update 0x0031A328.

A character or ship the Aphelion can hail: the smuggler and his ship, Cassiopeia, Libra, Carina, Azimuth, mission givers. Init 0x0031A214, Update 0x0031A328.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | component |  | Prompt component (+0x70..+0x107), see *Prompt component* | E |
| +0xC8 | u32 text id | 442 `GEN_TRIPROMPT_GPLAY_TELEPORT` x11, 1105 `SS0_PROMPT_GPLAY_HAIL_SHIP` x3 | Prompt text: 442 (the default, not in the EBOOT's name table) or SS0_PROMPT_GPLAY_HAIL_SHIP (1105) | E T |
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

Parent 0x67D space drifting object base > 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x510..+0x530; 889 placed in the space sectors, 889 in the game; e.g. `asteroid_habitable#`, `asteroid_lava#`, `asteroid_ice#`; own methods: Init 0x0030C7E4.

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
| +0x50E | u8 | 0 x902, 1 x13 | 1 on the space flotsam (class 0x842), 0 elsewhere. Read by the base (role open) | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4B0 u16, +0x4C0 u32, +0x4D0 f32, +0x4D4 f32, +0x4D8 f32, +0x4DC f32, +0x4E0 f32, +0x4E4 f32, +0x4E8 f32, +0x4EC f32, +0x4F0 f32, +0x4F4 f32, +0x4F8 f32, +0x500 u32, +0x504 u32, +0x508 f32.

### 0x72C landing pad beam

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 34 placed in the space sectors, 34 in the game; e.g. `space_junktrader_landingpad`, `moon#y_landing_pad_beam`, `moon#a_landing_pad_beam`; own methods: fix-up 0x002F3004, Init 0x002F2F64.

The beam a SpaceMoon lands the ship on (SpaceMoon +0xC0). Init 0x002F2F64 sets the Moby's fade distances and zeroes the reward component; fix-up 0x002F3004.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | component |  | ECON_Reward component (+0x5C..+0x7F), zeroed by Init (0x002983F8): no reward | E T |
| +0x80 | reference | Volume, set on 8 of 34: `moon#b_takeoff_vol`, `moon#y_takeoff_vol`, `moon#c_landing_pad_takeoff_vol` | Take-off volume (moonXx_takeoff_vol); set on a quarter of the moons | E D |
| +0x84 | reference | Volume, set on 5 of 34: `junktrader_checkpoint_respawn_vol` | Respawn / checkpoint volume: only on the junk trader stations (junktrader_checkpoint_respawn_vol) | E D |

### 0x757 DerelictShip

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x4C0; 124 placed in the space sectors, 124 in the game; e.g. `agorian_heavy_fighter_derelict#`, `ja_dp_junk_hauler_#_derelict#`, `drudge_hauler_ship_derelict#`; own methods: Init 0x002F6970.

Sector 2's wrecks. Init 0x002F6970. No instance sets anything.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4B0 f32, +0x4B4 f32, +0x4B8 f32.

### 0x75F SpaceDefenseSatellite

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x4F0; 15 placed in the space sectors, 15 in the game; e.g. `nefarious_camera_satellite#`, `satellite#`; own methods: fix-up 0x00311B50, Init 0x00312A34, Update 0x003133F4.

Nefarious camera satellites and the plain satellites. Fix-up 0x00311B50, Init 0x00312A34.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4D0 | reference | Controller, set on 15 of 15: `nefarious_camera_satellite_controller#`, `vorselon_satellite_controller`, `satellite_attack_controller` | Attack controller (SpaceAttackController): what answers when the satellite spots the ship | E D |
| +0x4D4 | reference | Volume, set on 15 of 15: `nefarious_camera_satellite#_inner_vol`, `satellite#_find`, `satellite#_inner_vol` | Inner volume (satelliteN_find): the ship inside it is spotted (0x003130A0) | E D |
| +0x4D8 | reference | Volume, set on 15 of 15: `nefarious_camera_satellite#_outer_vol`, `satellite#_keep`, `satellite#_outer_vol` | Outer volume (satelliteN_keep): the ship stays tracked until it leaves it | E D |
| +0x4DC | u32 | 20 x6, 30 x6, 25 x3 | 20, 25 or 30 by sector (20 in sectors 1-2, 30 in 4, 25 in 5). Not read by the class's own methods | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4C0 u32, +0x4C4 u32, +0x4C8 f32, +0x4CC u32, +0x4E0 u32, +0x4E4 f32, +0x4E8 u16, +0x4EA u8.

### 0x765 Vorselon's ship

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xE0; 2 placed in the space sectors, 2 in the game; e.g. `vorselons_ship`, `level_vorselons_ship`; own methods: Init 0x004EB8E4, Update 0x004EBAD0.

Embeds a whole attack controller: +0x60 holds class number 1886 (0x75E, the Nefarious attack controller) and the controller's fields follow at the same relative offsets (+0x60 + x).

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | component |  | Embedded SpaceAttackController, class 0x75E, at +0x60..+0xBF (see that class: field x is at +0x60 + x) | D T |
| +0xD4 | reference | Volume, set on 1 of 2: `vorselon_satellite_inner_vol` | Inner volume (vorselon_satellite_inner_vol) | D |
| +0xD8 | reference | Volume, set on 1 of 2: `vorselon_satellite_outer_vol` | Outer volume (vorselon_satellite_outer_vol) | D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u8, +0x5D u8.

### 0x76D SpaceShockwaveMine

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x500; 82 placed in the space sectors, 82 in the game; e.g. `space_shockwave_mine#`; own methods: Init 0x0031B204, Update 0x0031B4B0.

Init 0x0031B204, Update 0x0031B4B0. No instance sets anything.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4C0 f32, +0x4C4 f32, +0x4C8 f32, +0x4CC f32, +0x4D0 u32, +0x4E4 f32, +0x4E8 f32, +0x4EC f32, +0x4F0 u8.

### 0x796 space mission ship

Parent 0x67D space drifting object base > 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x510..+0x520; 13 placed in the space sectors, 13 in the game; e.g. `ss#_adwars#_sign#`, `space_junk_trader_billboard#`, `ss#_mission_tow_npc`; own methods: fix-up 0x00313DCC, Init 0x00313D3C, Update 0x00314028.

Ad-war signs, the tow mission's NPC, haulers. Fix-up 0x00313DCC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x514 | reference | Controller, set on 6 of 13: `vorselon_satellite_controller`, `ss#_mission_lost_attack_controller`, `SpaceAttackController_Agorian#` | Attack controller that ambushes around it (set on 6 of 13) | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x510 u32, +0x518 u32, +0x51C u8.

### 0x7A5 SpaceJumpingFireball

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xB0; 26 placed in the space sectors, 26 in the game; e.g. `moon#c_sawblade#`, `space_jumping_fireball#`; own methods: Init 0x00319008, Update 0x00319114.

Also the moon1c saw blades. Same six values on all 26 instances.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x84 | f32 | 20 on all 26 | 20, 10, 2, 1, 5 at +0x84..+0x94 and 1 at +0x98 on every instance (jump tuning; roles open) | D |

Same on every instance: +0x88 = 10, +0x8C = 2, +0x90 = 1, +0x94 = 5, +0x98 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0xA4 f32.

### 0x7BB SpaceComet

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x90; 1 placed in the space sectors, 1 in the game; e.g. `ss#_comet`; own methods: fix-up 0x00310A44, Init 0x00310814, Update 0x00310E78.

Sector 2's comet. Init 0x00310814, Update 0x00310E78.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | reference | Path, set on 1 of 1: `ss#_comet_path` | The comet's path | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x84 u32, +0x88 u32.

### 0x842 SpaceFlotsam

Parent 0x556 SpaceAsteroid > 0x67D space drifting object base > 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x530..+0x530; 13 placed in the space sectors, 13 in the game; e.g. `ss#_junk#`; own methods: Init 0x00318F94.

Sector 2's ss2_junk. A SpaceAsteroid with its own Init (0x00318F94); no own field.

No instance sets anything in the class's own range.

### 0x899 Agorian satellite / champion ship

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x510; 7 placed in the space sectors, 7 in the game; e.g. `ss#_satellite#`, `agorian_champion_ship#`; own methods: fix-up 0x0030ABCC, Init 0x0030B930, Update 0x0030C700.

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

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x530; 1 placed in the space sectors, 1 in the game; e.g. `valkyrie_boss`; own methods: fix-up 0x003204E8, Init 0x003215E4, Update 0x00322600.

Sector 3's valkyrie_boss. Fix-up 0x003204E8.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4CC | reference | Controller, set on 1 of 1: `SpaceAttackController_Valkyrie#` | Attack controller (SpaceAttackController_Valkyrie) | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4C0 u32, +0x4C4 u32, +0x4C8 u32, +0x4D0 u32, +0x4D4 u32, +0x4D8 u32, +0x4E4 u32, +0x4E8 u32, +0x4EC u32, +0x4F0 u32, +0x4F4 u8, +0x4F5 u8, +0x4F6 u8, +0x4F8 f32, +0x4FC f32, +0x500 f32, +0x504 f32, +0x508 u32, +0x50C u32, +0x510 u32, +0x514 f32, +0x518 f32, +0x51C u8, +0x51D u8, +0x51E u8, +0x51F u8, +0x520 u8, +0x521 u8, +0x522 u8.

### 0x89B SpaceValkyrieBossCarina

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x540; 1 placed in the space sectors, 1 in the game; e.g. `valkyrie_boss`; own methods: fix-up 0x0031D504, Init 0x0031E58C, Update 0x0031F7BC.

Sector 4's valkyrie_boss. Fix-up 0x0031D504.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4CC | reference | Controller, set on 1 of 1: `valkyrie_boss_controller` | Attack controller (valkyrie_boss_controller) | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x4C0 u32, +0x4C4 u32, +0x4C8 u32, +0x4D0 u32, +0x4D4 u32, +0x4D8 u32, +0x4E4 u32, +0x4EC u32, +0x4F0 u32, +0x4F4 u32, +0x4F8 u32, +0x4FC u8, +0x4FD u8, +0x4FE u8, +0x504 u32, +0x508 f32, +0x50C f32, +0x510 u32, +0x514 u32, +0x518 u32, +0x51C u32, +0x524 f32, +0x528 f32, +0x52C u32, +0x530 u8, +0x531 u8, +0x532 u8, +0x533 u8, +0x535 u8.

### 0x8A8 SpaceBillboard

Parent 0x238 space object base > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x4B0..+0x4B0; 3 placed in the space sectors, 3 in the game; e.g. `space_junk_trader_billboard#`; own methods: Init 0x0030F9EC.

The junk trader billboards of sector 3. No own field.

No instance sets anything in the class's own range.

### 0x8BE space mission NPC

Parent 0x4C4 space NPC > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x188..+0x1F0; 1 placed in the space sectors, 1 in the game; e.g. `ss#_mission_junk_npc`; own methods: Init 0x0031954C, Update 0x003195E8.

A space NPC with 0x68 more bytes, all zero on its one instance (ss2_mission_junk_npc).

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x190 f32, +0x194 f32, +0x198 f32, +0x19C f32, +0x1A0 f32, +0x1A4 f32, +0x1A8 f32, +0x1AC f32, +0x1B0 f32, +0x1B4 f32, +0x1B8 f32, +0x1BC f32, +0x1C0 f32, +0x1C4 u32, +0x1E4 u32.

## Controllers

### 0xD1 CamArg

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x60; not placed itself; values below are over its 909 placed descendants.

Base of the camera controllers: two CAM_CameraBlendParam blocks.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 3 x553, 0 x231, 6 x49, 4 x31, 5 x23 ... (7 values) | Blend in: type (0 = cut; 3, 6... = blend curves). First CAM_CameraBlendParam block, +0x14..+0x33 | Q D |
| +0x18 | u32 | 0 x891, 1 x17, 2 x1 | | |
| +0x1C | u32 | 0 x850, 1 x59 | | |
| +0x24 | u32 | 0 x903, 1 x6 | | |
| +0x2C | f32 | 0 x859, 2 x20, 1 x14, 5 x5, 3 x3 ... (11 values) | Blend in: seconds | Q D |
| +0x34 | u32 | 3 x630, 0 x132, 6 x82, 4 x23, 5 x23 ... (7 values) | Blend out: type. Second CAM_CameraBlendParam block, +0x34..+0x53 | Q D |
| +0x38 | u32 | 0 x897, 1 x12 | | |
| +0x3C | u32 | 0 x823, 1 x86 | | |
| +0x4C | f32 | 0 x821, 1 x49, 2 x28, 4 x2, 2.5 x2 ... (11 values) | Blend out: seconds | Q D |
| +0x50 | reference | Volume, set on 3 of 909: `orvus_staff_collect_cam_follow_forward`, `CUTSCENE_SUBEND_EXITCAM_VOL`, `CUTSCENE_MQWARK_EXITCAM_VOL` | | |
| +0x58 | u32 | 1 x908, 0 x1 | | |

Same on every instance: +0x20 = 1, +0x28 = 1, +0x40 = 1, +0x48 = 1.

### 0xD2 CamArgFollowPath

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0xA8; 53 placed in the space sectors, 238 in the game; e.g. `moon#a_goal_cam`, `moon#b_goal_cam`, `scene_arrival_cam`; own methods: fix-up 0x0004AC88, Init 0x0004AC7C.

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

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0xAC; 10 placed in the space sectors, 77 in the game; e.g. `moon#x_battlefield_controller`, `moon#y_battlefield_controller`, `moon#c_battlefield_controller`; own methods: Init 0x000962A0, Update 0x0009654C.

Referenced by enemies (EnemyBase +0x888). No instance sets anything but the range.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x14 u32, +0x18 u32, +0x1C u32, +0x20 u32, +0x24 u32, +0x28 u32, +0xA4 u8, +0xA8 f32.

### 0x173 EnemyFirepointController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x620; 105 placed in the space sectors, 322 in the game; e.g. `EnemyFirepointController#`, `moon#x_group#_firepoint_controller`, `moon#b_ap#_firepoint_controller`; own methods: fix-up 0x00097350, Init 0x00097388, Update 0x00096ECC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | Area, set on 311 of 322: `moon#y_enemies_group#_firepoints`, `moon#b_ap#_firepoints`, `moon#x_enemies_group#_firepoints` | Area of the firepoints it hands out | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x220 u32, +0x618 u8, +0x619 u8.

### 0x202 SoundEnvironment

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x80; 79 placed in the space sectors, 348 in the game; e.g. `aud_moon#b_osc_parent`, `aud_moon#b_drone`, `aud_moon#a_osc_parent`; own methods: fix-up 0x004EC9D0, Init 0x004ED1C4, Update 0x004ECC14.

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

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x9C; 7 placed in the space sectors, 65 in the game; e.g. `moon#c_asteroid#_cam`, `moon#c_catapult#_cam`, `moon#a_pad_cam`; own methods: fix-up 0x0004AD70, Init 0x0004AD64.

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

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x68; 9 placed in the space sectors, 37 in the game; e.g. `moon#b_opening_view_cam`, `moon#c_opening_view_cam`, `moon#d_opening_view_cam`; own methods: Init 0x0004ADD4.

A moon's opening view.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | f32 | -1 x33, 75.9 x2, 75 x1, 85 x1 | -1, or 75-85: most likely a field of view | D |
| +0x64 | u8 | 1 x27, 0 x10 | 1 on three quarters | D |

### 0x35E CarSpawner

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x60; 123 placed in the space sectors, 645 in the game; e.g. `CarSpawner#`, `car_controller_tombli_to_arena#`, `car_controller_arena#`; own methods: fix-up 0x0034ED48, Init 0x0034FCB8, Update 0x0034FC14.

Ambient traffic: space transports, asteroid streams. Init 0x0034FCB8.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | reference | Path, set on 645 of 645: `curve#`, `carpath#`, `asteroid_path_#` | Path the spawned objects fly | E D |
| +0x18 | reference | Pod, set on 645 of 645: `time_energy_cars`, `ambient_space_transports`, `ambient_clock_cars` | Pod of the Mobys to spawn from (ambient_space_transports...) | E D |
| +0x1C | reference | unset on all 645 | Reference slot: resolved by the class's fix-up | E |
| +0x20 | f32 | 0 x514, 30 x64, 10 x39, 1 x28 | | |
| +0x24 | f32 | 0 x575, 10 x39, 1 x28, 20 x3 | | |
| +0x28 | f32 | 0 x578, 10 x39, 1 x28 | | |
| +0x2C | f32 | 0 x578, 10 x39, 1 x28 | | |
| +0x30 | f32 | 1 x114, 5 x44, 10 x33, 15 x28, 3 x23 ... (77 values) | With +0x34: most likely the spawn interval range | D |
| +0x34 | f32 | 20 x95, 2 x43, 10 x42, 4 x37, 60 x24 ... (78 values) | | |
| +0x3C | f32 | 250 x67, 20 x58, 71 x41, 70 x36, 60 x29 ... (67 values) | Most likely the speed | D |
| +0x40 | f32 | 0 x622, 0.6 x18, 0.3 x3, 0.5 x2 | | |
| +0x44 | f32 | 0 x154, 10 x102, 12 x68, 13 x41, 2 x32 ... (25 values) | | |
| +0x4C | u32 | 0 x517, 8 x117, 1 x11 | | |
| +0x50 | 4 bytes | 0x00000000 x372, 0x00000001 x195, 0x00000101 x44, 0x01000000 x26, 0x01000001 x4 ... (7 values) | | |
| +0x54 | 4 bytes | 0x00000000 x337, 0x01000000 x201, 0x00010000 x75, 0x01010000 x32 | | |
| +0x58 | f32 | 60 x566, 0 x50, 10 x12, -100 x11, 5 x6 | | |
| +0x5C | f32 | 180 x566, 10 x38, 0 x12, 20 x12, -300 x11 ... (6 values) | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x38 f32, +0x48 f32.

### 0x360 CamArgFixed

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x6C; 70 placed in the space sectors, 464 in the game; e.g. `moon#a_kickpad#_cam`, `junktrader_scene_cam#`, `ss#_vorselonchase_cam#`; own methods: Init 0x0004AC14.

The view sits on the controller and looks along its local +Z.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | f32 | 0 x462, 90 x1, 100 x1 | Field of view in degrees when > 0 (0 keeps the game's) | Q D |
| +0x64 | u32 | 0 x448, 1 x16 | 0 / 1 (role open) | D |

### 0x369 SoundEmitter

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x28; 74 placed in the space sectors, 707 in the game; e.g. `aud_moon#b_osc_emitter`, `aud_moon#a_osc_emitter`, `aud_moon#y_osc_emitter`; own methods: fix-up 0x004EC384, Init 0x004EC314, Update 0x004EC408.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x4 | reference | unset on all 707 | Reference slot: resolved by the class's fix-up | E |
| +0x14 | u32 | 10020 x37, 10078 x36, 10246 x36, 10787 x35, 10067 x33 ... (102 values) | Sound id (10000-range) | D |
| +0x18 | u32 | 100 x526, 0 x181 | 100 or 0 | D |

Same on every instance: +0x24 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x1C u32, +0x20 u32.

### 0x3D0 FxController

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x1B0; 583 placed in the space sectors, 3235 in the game; e.g. `FX_moon#a_lava_fireburst#`, `FX_moonDust_#`, `FxController#`; own methods: fix-up 0x00345864, Init 0x003466FC, Update 0x00346850.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | u32 | 10108 x301, 10128 x281, 10231 x232, 10172 x205, 10106 x204 ... (128 values) | Effect id (10000-range) | D |
| +0x18 | u32 | 0 x3228, 10109 x7 | Second effect id (rare) | D |
| +0x1C | u32 | 0 x1877, 1 x1358 | | |
| +0x20 | u32 | 0 x2851, 1 x384 | | |
| +0x24 | f32 | 0 x2514, 1.5 x393, 1 x151, 0.25 x92, 3 x32 ... (9 values) | | |
| +0x28 | f32 | 0 x2514, 3 x521, 0.75 x92, 6 x53, 10 x31 ... (8 values) | | |
| +0x2C | f32 | 1 x3207, 0.8 x28 | | |
| +0x30 | f32 | 0 x3229, 4 x6 | | |
| +0x34 | f32 | 0 x3229, 4 x6 | | |
| +0x54 | reference | Volume, set on 104 of 3235: `FX_rainVolume_sneaky_#`, `FX_rainDrops_vol#`, `FX_ambFireballs_#` | Volume the effect fills (rain volumes) | E D |
| +0x60 | u32 | 0 x3131, 1 x104 | | |
| +0x64 | u32 | 0 x3139, 1 x96 | | |
| +0x68 | u32 | 0 x3231, 1 x4 | | |
| +0x6C | reference | unset on all 3235 | Reference slot: resolved by the class's fix-up | E |
| +0x70 | f32 | 0 x3206, 1 x29 | | |
| +0x74 | f32 | 2 x3208, 0.6 x14, 3 x10, 3.15 x1, 1 x1 ... (6 values) | | |
| +0x78 | u32 | 1 x3211, 0 x14, 2 x10 | | |
| +0x7C | u32 | 0 x3220, 1 x15 | | |
| +0x80 | u32 | 0 x3210, 512 x25 | | |
| +0x8C | u32 | 0 x3190, 3 x45 | | |
| +0x90 | f32 | 0 x2781, 6 x265, 3 x99, 1 x52, 12 x27 ... (6 values) | | |
| +0x94 | f32 | 0 x2663, 22 x138, 12 x127, 10 x96, 50 x92 ... (11 values) | | |
| +0x98 | f32 | 0 x2653, 0.6 x333, 0.3 x126, 3 x80, 1 x22 ... (10 values) | | |
| +0x9C | f32 | 0 x2653, 1 x297, 0.7 x117, 0.3 x107, 0.8 x45 ... (8 values) | | |
| +0x150 | component |  | CAM_CameraShakeEffect (class 221) embedded at +0x150..+0x187 | T Q |
| +0x1A0 | 4 bytes | 0x00000000 x3212, 0x00010000 x23 | | |

Same on every instance: +0x84 = 2, +0x88 = 3, +0xB0 = -1, +0xBC = -1, +0xC0 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x38 f32, +0x3C f32, +0x40 u32, +0x44 u32, +0x48 u32, +0x4C u32, +0x5C f32, +0xA0 u32, +0xB4 u32, +0xB8 u8, +0xC4 f32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0xF0 f32, +0xF4 f32, +0xF8 f32, +0xFC f32, +0x100 f32, +0x104 f32, +0x108 f32, +0x10C f32, +0x110 f32, +0x114 f32, +0x118 f32, +0x11C f32, +0x120 f32, +0x124 f32, +0x128 f32, +0x12C f32, +0x130 u8, +0x131 u8, +0x132 u8, +0x134 f32, +0x138 u32, +0x13C u32, +0x140 u32.

### 0x402 CamArgFixedFollow

Parent 0xD1 CamArg > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x74; 39 placed in the space sectors, 105 in the game; e.g. `ss#_junkdepot_armor_cam`, `scene_aphelion_cam#`, `scene_vorselon_cam`; own methods: fix-up 0x0004AC2C, Init 0x0004AC20.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x64 | reference | Moby, set on 95 of 105: `ratchet`, `APHELION`, `aphelion_zoni#` | The Moby the fixed camera turns to follow (ratchet, the Aphelion, a Zoni, the armor pad) | E D |
| +0x68 | f32 | 540 x98, 2400 x2, 1080 x1, 720 x1, 1800 x1 ... (7 values) | 540 on nearly all, with +0x6C: turn speed limits | D |
| +0x6C | f32 | 540 x98, 2400 x2, 1080 x1, 720 x1, 1800 x1 ... (7 values) | | |
| +0x70 | f32 | 90 x93, 250 x2, 2400 x2, 500 x1, 275 x1 ... (11 values) | 90 on nearly all | D |

### 0x626 HazardSwampGasJet

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x20; 23 placed in the space sectors, 23 in the game; e.g. `HazardSwampGasJet#`; own methods: Init 0x0016781C, Update 0x001679E4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x14 | f32 | 4 x20, 2 x3 | 4 or 2; with +0x18 (2 or 4) and +0x1C (1): jet timing | D |
| +0x18 | f32 | 2 x13, 4 x10 | | |

Same on every instance: +0x1C = 1.

### 0x6E3 SpaceAsteroidField

Parent 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x14..+0x50; 4 placed in the space sectors, 4 in the game; e.g. `SpaceAsteroidField#`; own methods: fix-up 0x0030CF38, Init 0x0030D494, Update 0x0030D738.

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

Parent 0x714 SpaceAttackController > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 4 placed in the space sectors, 4 in the game; e.g. `SpaceAttackController_Agorian#`, `agorian_camera_satellite_controller#`, `agorian_camera_satellite_controller`.

Spawn method 0x0030E810: picks the wave, shuffles it and hands each ship to the carrier (0x00315E00).

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u8 | 0 x2, 1 x2 | Fighters are the advanced kind: 0 = class 0x756 (art 0xBA), 1 = class 0x82C (art 0xBB) | E |
| +0x5D | u8 | 0 x2, 1 x2 | Bombers are the advanced kind: 0 = class 0x755 (art 0xB8), 1 = class 0x839 (art 0xB9) | E |

### 0x75E SpaceAttackController (Nefarious)

Parent 0x714 SpaceAttackController > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 6 placed in the space sectors, 6 in the game; e.g. `nefarious_camera_satellite_controller#`, `vorselon_satellite_controller`, `ss#_mission_lost_attack_controller`.

Spawn method 0x0030E5B4, the same shape as the Agorian one with art classes 0xBE / 0xBF.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u8 | 0 x3, 1 x3 | Fighters are the advanced kind | E |
| +0x5D | u8 | 0 x3, 1 x3 | Bombers are the advanced kind | E |

### 0x81D SpaceAttackController (Valkyrie)

Parent 0x714 SpaceAttackController > 0xCF ControllerBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 3 placed in the space sectors, 3 in the game; e.g. `SpaceAttackController_Valkyrie#`, `valkyrie_boss_controller#`, `valkyrie_boss_controller`.

Spawn method 0x0030E450.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u8 | 1 x2, 0 x1 | Selects the ship kind, as on the other two | E |

## Moon gameplay objects

### 0x4D PlatformGrappleBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x470; not placed itself; values below are over its 27 placed descendants; own methods: fix-up 0x00375608, Init 0x00375634, Update 0x003754F4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x464 | 4 bytes | 0x00000000 x24, 0x01000000 x3 | | |

Same on every instance: +0x418 = 2, +0x428 = -1, +0x43C = 0x01000000, +0x450 = 3, +0x45C = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xD0 u32, +0xD4 u32, +0xD8 u32, +0xE0 f32, +0xE4 f32, +0xE8 f32, +0xEC f32, +0xF0 u32, +0xF4 u32, +0xF8 u32, +0x180 u8, +0x210 u8, +0x2A0 u8, +0x330 u8, +0x3C0 u8, +0x3D0 u8, +0x40C u32, +0x410 u32, +0x414 u32, +0x440 u32, +0x44C u8, +0x44D u8, +0x44E u8, +0x454 f32, +0x458 f32, +0x460 f32.

### 0x4F PlatformGrappleCatapult

Parent 0x4D PlatformGrappleBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x470..+0x4B0; 6 placed in the space sectors, 14 in the game; e.g. `wrench_catapult_arm#`, `moon#b_catapult#_arm`, `moon#c_catapult#_arm`; own methods: fix-up 0x00375A84, Init 0x0037610C, Update 0x00375948.

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

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xE0; not placed itself; values below are over its 59 placed descendants; own methods: Init 0x004B9074, Update 0x004B97D4.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x68 | u32 | 5 x58, 0 x1 | | |

Same on every instance: +0x5C = 4096, +0x60 = 16.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x64 f32, +0x6C f32, +0x70 f32, +0x74 u32, +0x78 u32, +0x8C u32, +0x90 u32, +0x94 u32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xC0 f32, +0xC4 f32, +0xC8 f32.

### 0xD8 PickupSpecialBase

Parent 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xE0..+0xF0; not placed itself; values below are over its 59 placed descendants; own methods: fix-up 0x004BF030, Init 0x004BEC34.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xE0 | reference | Controller, set on 37 of 59: `goldbolt#cam`, `CamArgFixed#`, `goldboltcam#` | Reveal camera | E D |
| +0xE4 | reference | Volume, set on 37 of 59: `goldbolt#vol`, `volume#`, `VOL_GOLDBOLT#` | Volume the camera is triggered from | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xE8 u8.

### 0x15C SimpleAnimated

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 166 placed in the space sectors, 366 in the game; e.g. `yy_moon_light#`, `a_ice_moon#`, `yy_ago_arena_spotlights#`; own methods: Init 0x003A563C, Update 0x003A5608.

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

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x120; 60 placed in the space sectors, 119 in the game; e.g. `moon#d_floatingplat_#m#`, `moon_floating_platform_#_#`; own methods: Init 0x00367624, Update 0x003675DC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x118 | 4 bytes | 0x00000100 x325, 0x00000000 x35, 0x00010000 x14, 0x00010100 x2 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xC0 u32, +0xC4 u32, +0xC8 u32, +0xD0 u32, +0xD4 u32, +0xD8 u32, +0xDC u32, +0xE0 f32, +0xE4 u32, +0xF4 u32, +0x116 u8.

### 0x210 PickupGoldBolt

Parent 0xD8 PickupSpecialBase > 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xF0..+0x110; 11 placed in the space sectors, 41 in the game; e.g. `moon#a_goldbolt`, `moon#x_reward`, `moon#b_goldbolt`; own methods: Init 0x004BD430.

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

Parent 0x220 VendorBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x2C0..+0x2D0; 5 placed in the space sectors, 12 in the game; e.g. `armorVendor#`, `ss#_armor_vendor`; own methods: fix-up 0x00384508, Init 0x0038455C.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x2B8 | reference | Controller, set on 12 of 12: `ss#_junkdepot_armor_cam`, `armor_pad_camera`, `CamArgFixed#` | Camera | E D |
| +0x2BC | reference | Moby, set on 12 of 12: `armor_pad#`, `ss#_armor_pad`, `ARMOR_PAD` | The armor pad (class 0x706) | E D |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x2C0 u32, +0x2C4 u8, +0x2C8 u32, +0x2CC u8.

### 0x245 ShotTargetBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xA0; not placed itself; values below are over its 74 placed descendants; own methods: fix-up 0x0037C788, Init 0x0037C7E0, Update 0x0037CA80.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | f32 | -1 x62, 30 x3, 20 x2, 15 x2, 25 x2 ... (8 values) | Range (-1 = default) | D |
| +0x74 | reference | Area, set on 1 of 74: `AREA_DISALLOW_SWINGSHOT_TARGET#` | Area where the target cannot be used | E D |
| +0x78 | reference | unset on all 74 | Reference slot: resolved by the class's fix-up | E |
| +0x7C | reference | Moby, set on 3 of 74: `wrench_platform_mag_twoaxis#` | Moby the target is attached to | E D |
| +0x88 | 4 bytes | 0x00000000 x67, 0x01000000 x7 | | |
| +0x94 | f32 | 8 x41, 0 x16, 6 x5, 11 x4, 7 x4 ... (8 values) | 8 by default | D |

Same on every instance: +0x80 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x84 f32, +0x8C u32, +0x90 u32.

### 0x246 ShotTargetSwing

Parent 0x245 ShotTargetBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xA0..+0xA0; 15 placed in the space sectors, 58 in the game; e.g. `moon#a_platform_#m#`, `moon#b_swingshot#`, `moon#e_swingshot_#m#`.

No instance sets anything in the class's own range.

### 0x291 BreakableCrateBase

Parent 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x250..+0x590; not placed itself; values below are over its 4932 placed descendants; own methods: Init 0x004B674C, Update 0x004B65BC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x580 | u32 | 0 x4880, 1 x52 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x250 f32, +0x254 f32, +0x258 f32, +0x25C f32, +0x260 u32, +0x264 u32, +0x268 u32, +0x2F0 u8, +0x380 u8, +0x410 u8, +0x4A0 u8, +0x530 u8, +0x540 u8, +0x550 u32, +0x554 f32, +0x558 f32, +0x55C f32, +0x560 u32, +0x570 f32, +0x574 f32, +0x578 f32, +0x57C f32, +0x584 f32.

### 0x292 BreakableBombCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x5C0; 59 placed in the space sectors, 312 in the game; e.g. `explosive_crate#`, `z_moon#a_mods_#`; own methods: Init 0x004B5100.

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x590 f32, +0x594 u16, +0x5A0 f32, +0x5A4 f32, +0x5A8 f32, +0x5AC f32, +0x5B0 f32, +0x5B4 u32.

### 0x2B6 PadBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0xC0; not placed itself; values below are over its 423 placed descendants; own methods: Init 0x00362FBC, Update 0x00362F90.

Same on every instance: +0xA4 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x60 u32, +0x64 u32, +0x68 u32, +0x6C u32, +0x70 u32, +0x80 u32, +0xA2 u8.

### 0x2C5 ShotTargetHook

Parent 0x245 ShotTargetBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xA0..+0xA0; 2 placed in the space sectors, 16 in the game; e.g. `moon#b_hookshot`, `moon#e_swingshot_#m#`.

No instance sets anything in the class's own range.

### 0x2D4 BreakableMetalCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 39 placed in the space sectors, 131 in the game; e.g. `indestructible_crate#`; own methods: Init 0x004B7420.

No instance sets anything in the class's own range.

### 0x2E7 BreakableAmmoCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 266 placed in the space sectors, 626 in the game; e.g. `ammo_crate#`, `z_moon#b_modCrates_#`; own methods: Init 0x004B4DDC.

No instance sets anything in the class's own range.

### 0x2EC machine burner

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 8 placed in the space sectors, 113 in the game; e.g. `dc_mf_machine_burner_flat#`; own methods: Init 0x001664D0, Update 0x0016655C.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | f32 | 2 x80, 100 x14, 4 x10, 1 x9 | Timing: +0x70 2, +0x74 3, +0x78 0 by default | D |
| +0x74 | f32 | 3 x59, 0 x45, 4 x9 | | |
| +0x78 | f32 | 0 x104, 2 x6, 1 x3 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x7C f32, +0x80 u8, +0x81 u8, +0x83 u8.

### 0x384 PlatformBoltCrank

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 1 placed in the space sectors, 6 in the game; e.g. `moon#a_bolt_platform`; own methods: fix-up 0x00367980, Init 0x003679C0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | reference | Moby, set on 6 of 6: `moon#a_bolt_crank`, `azimuth_bridge_winch`, `rot_bridge_crank` | The bolt crank that drives the platform | E D |

### 0x3A7 PromptTeleporter

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x140; 27 placed in the space sectors, 65 in the game; e.g. `moon#b_teleporter_#a`, `moon#b_teleporter_#b`, `moon#e_teleporter_pad#`; own methods: fix-up 0x003A4864, Init 0x003A5240.

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
| +0x138 | u32 | 257 x63, 256 x2 | | |

Same on every instance: +0x118 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x11C u32, +0x128 u32, +0x12C u32.

### 0x3B0 PadFling

Parent 0x2B6 PadBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xC0..+0xE8; 22 placed in the space sectors, 27 in the game; e.g. `moon#c_flingpad#`, `moon#d_flingpad#`, `flingPad#`; own methods: fix-up 0x003633E8, Init 0x00363390.

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

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x90; 33 placed in the space sectors, 36 in the game; e.g. `monster_closet_teleportation_pad#`, `moon#b_ap#_teleport_pad#`; own methods: fix-up 0x0037D984, Init 0x0037D9BC.

The monster closet teleport pads. No instance sets anything.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x88 | reference | unset on all 36 | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32.

### 0x3B2 VendorWeapon

Parent 0x220 VendorBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x2C0..+0x2C0; 5 placed in the space sectors, 31 in the game; e.g. `weapon_vendor#`; own methods: Init 0x003863E0.

No instance sets anything in the class's own range.

### 0x3ED PlatformMoving

Parent 0x201 PlatformBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x120..+0x1B0; 250 placed in the space sectors, 257 in the game; e.g. `moon#e_floatingplatform_#m#`, `moon#c_floatingplatform_#m#`, `moon#d_floatingplat_#m#`; own methods: fix-up 0x0036AF18, Init 0x0036B318, Update 0x0036AA9C.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x180 | reference | Volume, set on 2 of 257: `VOL_OIL_PLATFORM`, `VOL_MOVING_PLAT_END` | End volume (rare) | E D |
| +0x184 | reference | Volume, set on 2 of 257: `volume#`, `VOL_MOVING_PLAT_START` | Start volume (rare) | E D |
| +0x188 | reference | Path, set on 60 of 257: `curve#`, `moon#c_movingplatform_#m#_curve`, `moon#b_#m_moving_plat#_curve` | Path the platform moves along | E D |
| +0x198 | f32 | 2 x199, 5 x16, 6 x14, 8 x11, 4 x9 ... (8 values) | Speed-like value (2 by default) | D |
| +0x19C | f32 | 5 x199, 2 x42, 0 x10, 1 x4, 3 x2 | Wait-like value (5 by default) | D |
| +0x1A0 | 4 bytes | 0x01000100 x199, 0x00000100 x31, 0x00000000 x16, 0x00020100 x11 | | |
| +0x1A4 | 4 bytes | 0x00000000 x247, 0x01000000 x10 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x120 f32, +0x124 f32, +0x128 u16, +0x130 f32, +0x134 f32, +0x138 f32, +0x13C f32, +0x170 f32, +0x174 f32, +0x178 f32, +0x17C f32, +0x190 u32, +0x1A8 u32, +0x1AE u8, +0x1AF u8.

### 0x425 BoltCrankBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3D0; 1 placed in the space sectors, 6 in the game; e.g. `moon#a_bolt_crank`; own methods: Init 0x00353EE0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x37C | 4 bytes | 0xE4000000 x10, 0x00000000 x9, 0xD8000000 x7, 0xDC000000 x4, 0xAC000000 x3 ... (6 values) | | |
| +0x390 | f32 | 5 x15, 3 x13, 2 x6, 20 x1 | | |
| +0x398 | f32 | 5 x32, 3 x3 | | |
| +0x3C0 | u32 | 0 x33, 1 x2 | | |
| +0x3C4 | u32 | 0 x34, 1 x1 | | |
| +0x3CC | 4 bytes | 0x00140000 x9, 0x00840000 x4, 0x00000000 x3, 0x01700000 x3, 0x00D00000 x2 ... (17 values) | | |

Same on every instance: +0x360 = 2, +0x370 = -1, +0x3B0 = -1, +0x3B4 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 u32, +0x74 u32, +0x78 u32, +0x100 u8, +0x190 u8, +0x220 u8, +0x2B0 u8, +0x340 u8, +0x350 u8, +0x378 f32, +0x380 f32, +0x384 f32, +0x388 f32, +0x38C f32, +0x394 f32, +0x39C f32, +0x3A0 f32, +0x3A4 f32, +0x3A8 u32, +0x3AC f32, +0x3B8 u32, +0x3BC u32, +0x3C8 u8, +0x3C9 u8, +0x3CA u8, +0x3CB u8.

### 0x43B BreakableBoltCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 801 placed in the space sectors, 3164 in the game; e.g. `breakable_crate#`, `morph_crate#`, `z_moon#a_mods_#`; own methods: Init 0x004B4EB4.

No instance sets anything in the class's own range.

### 0x46B BreakableHealthCrate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 152 placed in the space sectors, 321 in the game; e.g. `health_crate#`; own methods: Init 0x004B6DF8.

No instance sets anything in the class's own range.

### 0x470 PlatformGrappleSpring

Parent 0x4D PlatformGrappleBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x470..+0x4A0; 13 placed in the space sectors, 13 in the game; e.g. `wrench_spring_pad#`, `moon#b_springpad`; own methods: Init 0x0037A34C, Update 0x00379F60.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x488 | f32 | 10 x3, 35 x3, 16 x2, 25 x1, 20 x1 ... (8 values) | Launch strength (10-35) | D |
| +0x48C | 4 bytes | 0x01000100 x8, 0x00000100 x4, 0x01010100 x1 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x478 u32, +0x47C f32, +0x480 f32, +0x484 f32, +0x490 f32.

### 0x548 Door

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x110; not placed itself; values below are over its 143 placed descendants; own methods: fix-up 0x0035AD14, Init 0x0035AE08, Update 0x0035B814.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | reference | unset on all 143 | Reference slot: resolved by the class's fix-up | E |
| +0xE8 | reference | Volume, set on 62 of 143: `door_open_hall_east#`, `build_elevator_#`, `GCD_PUZZ#_ENT_DOOR_OPEN_VOL` | Open volume | E D |
| +0xEC | reference | Volume, set on 71 of 143: `door_close_hall_east#`, `elevator_doors_close`, `ENEMY_WAVE_DOOR#_CLOSE_VOL` | Close volume | E D |
| +0xF0 | reference | Pod, set on 28 of 143: `gcc_subcon_puzzle#_p#_cham#_button`, `gcb_subcon_puzzle#_button_chamber#`, `gcd_subcon_puzzle#_chambutton_#` | Pod of the buttons that open it | E D |
| +0xF4 | reference | unset on all 143 | Reference slot: resolved by the class's fix-up | E |
| +0x100 | 4 bytes | 0x00000101 x91, 0x00000001 x42, 0x01000101 x6, 0x00000100 x3, 0x00000000 x1 | | |
| +0x108 | f32 | 1 x124, 7 x9, 8 x3, 0.5 x3, 0.3 x1 ... (8 values) | Speed-like value (1 by default) | D |

Same on every instance: +0xB0 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u16, +0x68 u16, +0x80 f32, +0x84 f32, +0x88 f32, +0x90 f32, +0x94 f32, +0x98 f32, +0x9C f32, +0xA0 f32, +0xA4 f32, +0xA8 f32, +0xAC f32, +0xC0 f32, +0xC4 u8, +0xC5 u8, +0xE0 f32, +0xE4 f32, +0xF8 u32, +0x104 u8, +0x105 u8.

### 0x54A DoorAnimated

Parent 0x548 Door > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x110..+0x110; 10 placed in the space sectors, 143 in the game; e.g. `moon#b_ap#_bunker_door#`, `moon#a_bunker#_door#`, `moon#e_ap#_bunker_door#`; own methods: Init 0x0035C130.

The bunker doors, opened by the moon scripts (door_open).

No instance sets anything in the class's own range.

### 0x569 Damageable

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x200; not placed itself; values below are over its 7908 placed descendants; own methods: fix-up 0x004B2B10, Init 0x004B24A4, Update 0x004B2660.

Base of everything breakable. GetInterface (0x004B2418) hands out its embedded components by class number: 0x4BD SEGMENT_Spawn at +0x5C, 0x137 ECON_Reward at +0x60, 0x51 Health at +0x84, 0x113 ColorFlash at +0x124.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 0 x4985, 3 x1648, 1 x1236, 5 x18, 4 x18 ... (6 values) | SEGMENT_Spawn rule, 0-7 (see *Segments and the spawn rule*): 0 always there, 1 first visit only, 3 stays gone once broken | E T |
| +0x60 | component |  | ECON_Reward (+0x60..+0x83): what breaking it pays. Filled by each class's Init (0x002983F8), not by the instance | E T |
| +0x1B4 | reference | unset on all 7908 | Reference slot: resolved by the class's fix-up | E |
| +0x1B8 | reference | unset on all 7908 | Reference slot: resolved by the class's fix-up | E |
| +0x1BC | 4 bytes | 0x01000000 x7872, 0x00000000 x36 | | |
| +0x1C0 | f32 | 0 x7815, 2 x93 | | |
| +0x1C4 | f32 | 3 x7907, 0 x1 | | |
| +0x1C8 | f32 | 3 x7907, 1 x1 | | |
| +0x1E0 | u32 | 0 x7612, 2 x272, 7 x19, 1 x4, 6 x1 | Index into a table at 0x00D374B4, read by Init next to the health; Init sets 6 in one case | E |
| +0x1E4 | f32 | 1 x7881, 10 x17, 3.5 x3, 2.5 x3, 5 x1 ... (8 values) | Health: given to the Health component by Init (0x0008D3C8). 1 on nearly everything | E |
| +0x1F0 | 4 bytes | 0x00000000 x6918, 0x00010000 x503, 0x00000001 x374, 0x01000000 x72, 0x00010001 x41 | | |
| +0x1F4 | 4 bytes | 0x00000000 x6063, 0x01000000 x1725, 0x00000100 x93, 0x00010000 x26, 0x01000001 x1 | | |

Same on every instance: +0xA0 = 1, +0xA4 = 1, +0xA8 = 1, +0x1B0 = 1, +0x1CC = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x8C u32, +0xAC u32, +0xB0 u32, +0xB4 u32, +0xB8 u32, +0xDC f32, +0xE0 f32, +0xE4 f32, +0x190 u32, +0x1D0 u32, +0x1D4 f32, +0x1D8 f32, +0x1DC u8, +0x1DD u8, +0x1E8 u32, +0x1EC f32.

### 0x56A Breakable

Parent 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x200..+0x250; 882 placed in the space sectors, 2874 in the game; e.g. `yy_moon_breakable_#_#`, `moon#y_breakables_#`, `Moon#a_destructible_pipes_and_pumps_group#_#`; own methods: Init 0x004AFC68, Update 0x004AFF18.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x208 | u32 | 0 x5694, 1 x2214 | 0 / 1: with 1 the break impulse below is x1.5 for some hits (0x004B0540) | E |
| +0x20C | u32 | 0 x7473, 1 x435 | | |
| +0x230 | f32 | 1 x5440, 3 x857, 10 x578, 4 x434, 5 x361 ... (14 values) | Break impulse: how hard the pieces fly (0x004B0540) | E |
| +0x234 | f32 | -1 x6229, 3 x1186, 5 x310, 2 x96, 0.5 x38 ... (8 values) | Seconds given to the piece effect; negative = 10 (0x004B092C) | E |
| +0x23C | 4 bytes | 0x00000100 x6020, 0x01000100 x1719, 0x01000101 x107, 0x01000000 x58, 0x00000000 x3 ... (6 values) | | |
| +0x240 | 4 bytes | 0x00000000 x7243, 0x00010000 x665 | | |
| +0x24C | u32 | 0 x7899, 2 x9 | | |

Same on every instance: +0x238 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x210 u32, +0x218 u32, +0x21C f32, +0x220 u32, +0x224 u32, +0x22C u32.

### 0x56E Destructible

Parent 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x250..+0x270; not placed itself; values below are over its 9 placed descendants; own methods: Init 0x004B36B4.

Same on every instance: +0x268 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x26C u8, +0x26D u8, +0x26E u8, +0x26F u8.

### 0x570 hydro plant

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3E0; 1 placed in the space sectors, 12 in the game; e.g. `hydro_plant#`; own methods: Init 0x00429F6C, Update 0x0042A480.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 0 x8, 3 x3, 1 x1 | | |
| +0x370 | f32 | 30 x8, 25 x2, 15 x1, 0 x1 | | |
| +0x3C8 | f32 | 8 x10, 10 x1, 11 x1 | | |
| +0x3D8 | u32 | 0 x8, 1 x4 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x60 u16, +0x62 u16, +0x64 f32, +0x68 f32, +0x6C u16, +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0x84 u32, +0x88 u32, +0x110 u8, +0x1A0 u8, +0x230 u8, +0x2C0 u8, +0x350 u8, +0x360 u8, +0x374 u32, +0x378 f32, +0x37E u16, +0x380 u32, +0x384 u32, +0x388 u32, +0x38C u32, +0x390 f32, +0x394 u32, +0x3A4 f32, +0x3C5 u8, +0x3C6 u8, +0x3CC u32, +0x3D0 u32, +0x3D4 f32.

### 0x577 hoverboot ramp / kick pad

Parent 0x2B6 PadBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xC0..+0x120; 37 placed in the space sectors, 396 in the game; e.g. `dm_hovboot_arrow_ramp#`, `moon#a_kickpad#`, `moon#b_hoverboot_ramp#_arrows`; own methods: fix-up 0x00363950, Init 0x00363BBC, Update 0x00363F80.

Also what a moon hill spawns around itself.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC8 | reference | Path, set on 52 of 396: `curve#`, `moon#a_kickpad#_curve`, `CURVE_KICKPAD_#` | Flight path | E D |
| +0xCC | reference | Volume, set on 143 of 396: `vol_ring_bn#_ramp#`, `volume#`, `HOVERBOOT_JUMP_VOL#` | Trigger volume | E D |
| +0xD0 | reference | Controller, set on 52 of 396: `CamArgFixed#`, `kickpad#cam`, `moon#a_kickpad#_cam` | Camera | E D |
| +0xD4 | reference | Controller, set on 1 of 396: `moon#a_kickpad#_fake_cam` | Second camera (one instance) | E D |
| +0xD8 | reference | Moby, set on 33 of 396: `wall_kick_pad#`, `ghost_pad_#`, `moon#a_kickpad#` | Next pad of a chain | E D |
| +0xDC | reference | unset on all 396 | Reference slot: resolved by the class's fix-up | E |
| +0xE0 | f32 | 35 x117, 40 x46, 20 x42, 25 x39, 18 x30 ... (32 values) | Launch tuning (+0xE0..+0xF4): 35, 2.5, 0.3, 1, -25, -25 by default | D |
| +0xE4 | f32 | 2.5 x143, 0.25 x65, 2 x57, 1.5 x41, 0.5 x32 ... (13 values) | | |
| +0xE8 | f32 | 0.3 x324, 0.6 x34, 0.8 x12, 1 x10, 0.45 x7 ... (8 values) | | |
| +0xEC | f32 | 1 x362, 0.25 x19, 0.5 x10, 2 x3, 0 x2 | | |
| +0xF0 | f32 | -25 x268, -20 x38, -15 x17, -22 x13, -18 x12 ... (21 values) | | |
| +0xF4 | f32 | -25 x294, -15 x20, -20 x20, -22 x13, -27 x8 ... (19 values) | | |
| +0x108 | u32 | 0 x380, 2 x10, 4 x6 | | |
| +0x10C | u32 | 0 x384, 2 x7, 3 x5 | | |
| +0x110 | 4 bytes | 0x00000000 x282, 0x01000000 x58, 0x00010000 x17, 0x00010100 x15, 0x00010001 x14 ... (10 values) | | |
| +0x114 | 4 bytes | 0x00000000 x322, 0x01000000 x61, 0x00010000 x13 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xC0 u32, +0xC4 u32, +0x100 f32, +0x104 f32, +0x118 u32.

### 0x591 GenericPrompt

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x160; 6 placed in the space sectors, 20 in the game; e.g. `ss#_starcrossed_fakeprompt#`, `ss#_mission_tow_fakeprompt`, `ss#_mission_transport_fakeprompt`; own methods: fix-up 0x00424028, Init 0x00424054.

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

own fields +0x4..+0x4A0; 21 placed in the space sectors, 45 in the game; e.g. `spring_pad#`, `moon#d_springpad#`; own methods: fix-up 0x00374988.

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

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3B0; 7 placed in the space sectors, 46 in the game; e.g. `battery_bot_socket#`, `moon#e_batterybotsocket#`; own methods: Init 0x0041BF3C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x60 f32, +0x64 f32, +0x68 f32, +0x6C f32, +0x70 u32, +0x74 u32, +0x78 u32, +0x100 u8, +0x190 u8, +0x220 u8, +0x2B0 u8, +0x340 u8, +0x350 u8, +0x360 u32, +0x364 u32, +0x368 u32, +0x3A0 u8.

### 0x61C destructible junk pile

Parent 0x56E Destructible > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x270..+0x570; 1 placed in the space sectors, 9 in the game; e.g. `tv_mf_destruct_junkpile_#_#`; own methods: Init 0x0041C488.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x270 f32, +0x274 f32, +0x278 f32, +0x27C f32, +0x280 u32, +0x284 u32, +0x288 u32, +0x310 u8, +0x3A0 u8, +0x430 u8, +0x4C0 u8, +0x550 u8, +0x560 u8.

### 0x623 DamageDistributer

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x68; 230 placed in the space sectors, 1300 in the game; e.g. `yy_moon_platform_antennae#`, `space_force_field_lasers#`, `yy_space_test_#_#`; own methods: Init 0x003A60A0, Update 0x003A6158.

All 4 One's name for this number; here the platform antennae and force field lasers.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x60 | 4 bytes | 0x01010000 x1296, 0x01010001 x4 | | |

Same on every instance: +0x5C = 64.

### 0x63D prized junk pickup

Parent 0xD8 PickupSpecialBase > 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xF0..+0xF0; 1 placed in the space sectors, 1 in the game; e.g. `moon#b_prized_junk`; own methods: Init 0x004BE96C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x65B crank-driven platform

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x1A0; 18 placed in the space sectors, 80 in the game; e.g. `moon#e_winchcrank#_plat#`, `moon_floating_platform_#_#`, `moon#c_floatingplatform_#m#`; own methods: fix-up 0x0039105C, Init 0x003910C4.

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

Parent 0x425 BoltCrankBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x3D0..+0x410; 6 placed in the space sectors, 29 in the game; e.g. `moon#c_winchcrank#`, `moon#b_winchcrank#`, `winch_crank#`; own methods: Init 0x00356C10, Update 0x00356DB4.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x408 = 101.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x400 f32.

### 0x6A4 camo crate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 30 placed in the space sectors, 337 in the game; e.g. `camo_crate#`; own methods: Init 0x004B5904.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x6D4 timed floating platform

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x390; 25 placed in the space sectors, 39 in the game; e.g. `moon#e_floatingplatform_#m#`; own methods: Init 0x00429BB8, Update 0x00429B64.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x68 | 4 bytes | 0x01010000 x25, 0x01000000 x12, 0x00000000 x2 | | |
| +0x6C | f32 | 15 x8, 7 x6, 12 x6, 3 x6, 9 x5 ... (7 values) | Per-instance time (3-15) | D |
| +0x380 | s32 | -1 x31, 0 x8 | | |

Same on every instance: +0x384 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x5C u32, +0x70 f32, +0x74 f32, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 u32, +0x94 u32, +0x98 u32, +0x120 u8, +0x1B0 u8, +0x240 u8, +0x2D0 u8, +0x360 u8, +0x370 u8.

### 0x6D5 soaker refill tank

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3D0; 4 placed in the space sectors, 17 in the game; e.g. `soaker_refill_tank#`, `moon#e_soaker_refill_tank`; own methods: Init 0x0043C6C8, Update 0x0043CAA8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x374 | u32 | 3 x10, 1 x5, 2 x2 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x70 f32, +0x74 f32, +0x78 f32, +0x7C f32, +0x80 u32, +0x84 u32, +0x88 u32, +0x110 u8, +0x1A0 u8, +0x230 u8, +0x2C0 u8, +0x350 u8, +0x360 u8, +0x370 u32, +0x3C4 u16.

### 0x6D7 vacuum pad / transport tube

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x110; 2 placed in the space sectors, 22 in the game; e.g. `moon#x_vacuum_pad`, `moon#x_vacuum_pad#`; own methods: fix-up 0x0035FC2C, Init 0x0035FB5C, Update 0x0035FC84.

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

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x3F0; 5 placed in the space sectors, 29 in the game; e.g. `moon#b_versabolt#`, `moon#b_batterybotsocket#`; own methods: Init 0x00437858, Update 0x0043797C.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x3B8 = 2, +0x3C8 = -1, +0x3DC = 0x01000000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x68 u16, +0x80 f32, +0x84 f32, +0x88 f32, +0x8C f32, +0x90 u32, +0x94 u32, +0x98 u32, +0x120 u8, +0x1B0 u8, +0x240 u8, +0x2D0 u8, +0x360 u8, +0x370 u8, +0x398 u32, +0x39C u32, +0x3A0 u32, +0x3A4 u32, +0x3D0 u32, +0x3E0 f32, +0x3E5 u8, +0x3E6 u8.

### 0x706 armor pad

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; 5 placed in the space sectors, 12 in the game; e.g. `armor_pad#`, `ss#_armor_pad`; own methods: Init 0x00384D2C.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x719 custom weapon part pickup

Parent 0xD8 PickupSpecialBase > 0xD7 PickupBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xF0..+0x110; 8 placed in the space sectors, 17 in the game; e.g. `moon#y_reward`, `moon#x_reward`, `moon#z_reward`; own methods: Init 0x004BC9D0.

The moons' "reward" on x / y / z moons.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xEC | u32 | 5 x1, 11 x1, 13 x1, 20 x1, 19 x1 ... (17 values) | Which part: different on each of the 17 instances | D |

Same on every instance: +0xF0 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xF4 u32, +0x100 u32, +0x104 u32, +0x108 u32.

### 0x810 large breakable crate

Parent 0x291 BreakableCrateBase > 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x590..+0x590; 9 placed in the space sectors, 41 in the game; e.g. `large_breakable_crate#`; own methods: Init 0x004B4E48.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x813 combat modifier / exploding barrel

Parent 0x56A Breakable > 0x569 Damageable > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x250..+0x550; 65 placed in the space sectors, 93 in the game; e.g. `z_combat_modifiers_#`, `z_moon#x_exploding_barrel_#`, `z_moon#c_mods_and_crates#`; own methods: Init 0x00421904, Update 0x004218B8.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x250 f32, +0x254 f32, +0x258 f32, +0x25C f32, +0x260 u32, +0x264 u32, +0x268 u32, +0x2F0 u8, +0x380 u8, +0x410 u8, +0x4A0 u8, +0x530 u8, +0x540 u8.

### 0x814 red mist tank

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x80; 1 placed in the space sectors, 5 in the game; e.g. `agorian_red_mist_tank#`; own methods: Init 0x0042E38C, Update 0x0042E608.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x6C | u32 | 1 x3, 0 x2 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x78 u32.

### 0x863 burner platform

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 25 placed in the space sectors, 25 in the game; e.g. `moon#d_floatingplatform_#m#`, `moon#d_burner#`, `yy_machine_burner_flat_quad#`; own methods: Init 0x00166B50, Update 0x00166BF0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x70 | f32 | 4 x18, 2 x7 | Timing: +0x70 4 or 2, +0x78 a per-instance offset (0-10) | D |
| +0x78 | f32 | 0 x6, 2 x3, 1 x3, 6 x2, 8 x2 ... (12 values) | | |

Same on every instance: +0x74 = 4.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x7C f32, +0x82 u16.

### 0x882 moon hill / crater

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 33 placed in the space sectors, 35 in the game; e.g. `habitable_moon_hill#`, `moon#a_hill#`, `moon#a_crater#`; own methods: Init 0x003117F8, Update 0x00311118.

A hill or crater on a moon surface. Init 0x003117F8 -> 0x0031111C spawns a ring of hoverboot ramps (class 0x577) around it and keeps their handles.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x5C | u32 | 3 x11, 2 x8, 5 x8, 0 x8 | Variant: 0-2 spawn 4 ramps, 3 and up spawn 8, with different radii and heights. Retail: 0, 2 (craters), 3, 5 (hills) | E D |
| +0x60 | run time |  | Handles of the spawned ramps (+0x60..+0x7F) and their count (+0x80) | E |

### 0x897 simple idle character

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x88; 4 placed in the space sectors, 55 in the game; e.g. `junk_trader_simpleidle#`, `junk_trader_simpleidler`; own methods: Init 0x003A57F8, Update 0x003A5940.

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

Parent 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x60..+0x70; not placed itself; values below are over its 23 placed descendants; own methods: Init 0x00169E68, Update 0x00169EE0.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x64 | 4 bytes | 0x00010000 x18, 0x00000000 x5 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x68 u32.

### 0xF6 HERO_HeroCharacter

Parent 0xE5 HERO_HeroBase > 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x70..+0x1AB0; not placed itself; values below are over its 18 placed descendants; own methods: Init 0x001E0B88, Update 0x001DF714.

Same on every instance: +0x17F0 = 1, +0x17F4 = 1, +0x17F8 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x11B4 u32, +0x120C u32, +0x1210 u16, +0x1212 u16, +0x1214 u16, +0x1216 u16, +0x1218 u32, +0x121C u32, +0x12AC u32, +0x1300 u32, +0x1304 u32, +0x1308 u32, +0x1508 u32, +0x1590 u8, +0x17E0 u8, +0x182C f32, +0x18F8 u32, +0x1904 u32, +0x1908 u32, +0x1910 u8, +0x1911 u8, +0x1912 u8, +0x1920 u32, +0x1980 f32, +0x1984 f32, +0x1988 f32, +0x198C f32, +0x19C0 u8, +0x19C1 u8, +0x19C2 u8, +0x1A10 f32, +0x1A14 f32, +0x1A18 f32, +0x1A1C f32, +0x1A20 f32, +0x1A24 f32, +0x1A28 f32, +0x1A2C f32, +0x1A30 f32, +0x1A34 f32, +0x1A38 f32, +0x1A40 f32, +0x1A44 f32, +0x1A48 f32, +0x1A4C u32, +0x1A80 f32, +0x1A84 f32, +0x1A88 f32, +0x1A8C f32, +0x1A90 f32, +0x1A98 f32, +0x1AA0 u32, +0x1AA4 u32, +0x1AA8 u8, +0x1AA9 u8, +0x1AAB u8.

### 0xF7 Ratchet

Parent 0xF6 HERO_HeroCharacter > 0xE5 HERO_HeroBase > 0x52E PlayerBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x1AB0..+0x2B50; 5 placed in the space sectors, 18 in the game; e.g. `ratchet`; own methods: fix-up 0x00936E04, Init 0x00181910, Update 0x00181280.

0x2B50 bytes, all run time: no instance sets anything but one byte at +0x65.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x1AE0 = 310, +0x1D90 = 12, +0x1D94 = 12, +0x1D98 = 6, +0x1D9C = 12.5664, +0x1DA0 = 12.5664, +0x1DA4 = 6.28319, +0x1DA8 = 0.5, +0x1DAC = 0.15, +0x1DB0 = -0.5, +0x1DB4 = 0.872665, +0x1EF4 = -25, +0x1EF8 = -50, +0x1EFC = 1, +0x223C = 15, +0x2240 = 0.35, +0x2244 = 0.7, +0x2248 = 0.7, +0x2510 = 242, +0x27EC = 10.

### 0x160 CharacterBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x840; not placed itself; values below are over its 2900 placed descendants; own methods: fix-up 0x0048FD30, Init 0x0048FFA8, Update 0x00491884.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x148 | 4 bytes | 0x01010100 x2878, 0x00000000 x14, 0x00000100 x8 | | |
| +0x808 | reference | Path, set on 602 of 2900: `agorian_warrior#_curve`, `agorian_battlebot#_curve`, `blob_small#_curve` | The character's path (<name>_curve) | D |
| +0x80C | reference | Path, set on 651 of 2900: `curve#`, `group#_intro_curve#`, `INTRO_SHOOTER#` | Intro path | Q D |
| +0x810 | reference | Path, set on 1132 of 2900: `agorian_warrior#_curve`, `agorian_battlebot#_curve`, `nefarious_medium_trooper#_curve` | Patrol path | Q D |
| +0x814 | reference | Area, set on 360 of 2900: `moon#e_ap#_homearea`, `moon#b_ap#_homearea`, `moon#a_ap#_homearea` | Home Area (moonXx_apNN_homearea) | D |
| +0x818 | reference | Path, set on 1 of 2900: `PATH_LAST_ZONI` | A further path (one Zoni) | E D |

Same on every instance: +0x170 = 1, +0x180 = 65535, +0x1A0 = 1, +0x1A4 = 1, +0x1A8 = 1, +0x820 = 1, +0x83C = 0x01000000.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x6C u32, +0x70 u16, +0x72 u16, +0x74 u32, +0x78 u32, +0x7C u32, +0x98 u32, +0x9C u32, +0xA0 u16, +0xA2 u16, +0xA4 u16, +0xA6 u16, +0xA8 u32, +0xAC u32, +0x10C u16, +0x134 u32, +0x138 u32, +0x13C u32, +0x140 u32, +0x150 f32, +0x154 f32, +0x158 f32, +0x15C f32, +0x18C u32, +0x1B0 u32, +0x234 u32, +0x250 u32, +0x2A0 f32, +0x2A4 f32, +0x2A8 f32, +0x2AC f32, +0x2B0 u32, +0x2B4 u32, +0x2B8 u32, +0x340 u8, +0x3D0 u8, +0x460 u8, +0x4F0 u8, +0x580 u8, +0x590 u8, +0x5A0 u32, +0x5A4 u32, +0x5A8 u32, +0x5AC u32, +0x5B0 u32, +0x5B4 u32, +0x5B8 u32, +0x5BC u32, +0x5C0 u32, +0x5C4 u32, +0x5C8 u32, +0x5CC u32, +0x5D0 u32, +0x5D4 u32, +0x5D8 u32, +0x5DC u32, +0x5E0 u32, +0x5E4 u32, +0x5E8 u32, +0x5EC u32, +0x5F0 u32, +0x5F4 u32, +0x5FC u32, +0x600 u32, +0x618 u32, +0x620 u32, +0x630 f32, +0x634 f32, +0x63C u32, +0x640 u32, +0x644 u32, +0x648 u32, +0x658 f32, +0x6A4 u32, +0x6A8 u8, +0x6B4 u32, +0x800 u32, +0x804 f32, +0x81C u32, +0x824 f32, +0x828 f32, +0x82C u32, +0x830 u8, +0x834 u32, +0x838 u32, +0x83D u8, +0x83E u8.

### 0x181 EnemyBase

Parent 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x840..+0x920; not placed itself; values below are over its 2672 placed descendants; own methods: fix-up 0x0008EF68, Init 0x00090B48, Update 0x00090914.

The two awareness blocks have QForce's ClueEnemy shape: (Volume, Area, distance, flag). A moon's enemy group is its moon_apNN set: _homearea, _inner_vol, _outer_vol and the _enemies Pod.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x840 | u32 | 0 x2356, 1 x261, 2 x55 | 1 = Init deactivates the enemy (0x005238B4): it waits for a script or a spawner. 2 on a few (role open) | E D |
| +0x844 | u32 | 0 x1589, 1 x1001, 3 x57, 2 x25 | SEGMENT_Spawn rule, the same component as Damageable +0x5C (GetInterface 0x0008EB38 returns it for class 0x4BD) | E T |
| +0x848 | u32 | 0 x1199, 1 x457, 2 x296, 9 x151, 6 x130 ... (16 values) | Variant index, read by each enemy class's Init (the Agorian warrior switches on 0-9) | E D |
| +0x864 | reference | Volume, set on 1647 of 2672: `hangar_int_inner_vol`, `bat_bot_home_vol#`, `VOL_TRIGGER_ENTRANCE_BATTLE` | Inner awareness volume: the hero inside it wakes the enemy | Q D |
| +0x868 | reference | Area, set on 231 of 2672: `cargo_near_area_inner_#`, `vorselon_inner`, `moon#e_ap#_awarenessarea` | Inner awareness Area (several volumes) | Q D |
| +0x86C | f32 | 30 x2082, 15 x298, 20 x110, 0 x39, 35 x36 ... (21 values) | Inner awareness distance (30 by default) | Q D |
| +0x870 | u32 | 0 x2652, 1 x20 | Inner awareness flag (0 / 1) | Q D |
| +0x874 | reference | Volume, set on 1641 of 2672: `hangar_int_outer_vol`, `bat_bot_home_vol#`, `volume#` | Outer awareness volume: the enemy gives up when the hero leaves it | Q D |
| +0x878 | reference | Area, set on 255 of 2672: `cargo_near_area_outter_#`, `vorselon_inner`, `moon#e_ap#_awarenessarea` | Outer awareness Area | Q D |
| +0x87C | f32 | 30 x2425, 20 x78, 0 x41, 35 x36, 0.5 x16 ... (20 values) | Outer awareness distance (30 by default) | Q D |
| +0x880 | u32 | 0 x2630, 1 x42 | Outer awareness flag (0 / 1) | Q D |
| +0x884 | reference | Area, set on 338 of 2672: `FIREPOINTS_BLOB_ALLEY_BLOBS#`, `firepoints_control_wave#_set#`, `firepoints_entrance_wave#_set#` | Firepoints Area | D |
| +0x888 | reference | Controller, set on 689 of 2672: `moon#x_battlefield_controller`, `moon#y_battlefield_controller`, `td_battlefieldcontroller_#` | EnemyBattlefieldController | D |
| +0x88C | reference | Controller, set on 502 of 2672: `EnemyFirepointController#`, `moon#x_group#_firepoint_controller`, `wasps#_firepoint_controller` | EnemyFirepointController | D |
| +0x890 | reference | Scent, set on 127 of 2672: `AMBIENT_A#_Targets`, `AREA_AMBIENT_DAM`, `AMBIENT_A#_Targets_wtower` | Scent of ambient targets | E D |
| +0x8A0 | reference | Area, set on 9 of 2672: `A#_BEAST_NOGO`, `batterybot_cant_go` | No-go Area | E D |
| +0x8A4 | reference | unset on all 2672 | Reference slot: resolved by the class's fix-up | E |
| +0x8A8 | reference | Pod, set on 1267 of 2672: `BLOB_SPAWNER_BATBOT#_#`, `AGORIAN_A#_PART_OF_POD`, `moon#x_group#_enemies` | The enemy's Pod (its group: moonXx_apNN_enemies) | E D |
| +0x8B4 | 4 bytes | 0x00000000 x2530, 0x01000000 x142 | | |
| +0x8B8 | component |  | Component of class 0x835 (+0x8B8..), returned by GetInterface; holds the two fields below | E |
| +0x8C4 | u8 | 0 x2498, 1 x174 | 1 on a tenth of the enemies (in the 0x835 component; role open) | D |
| +0x8C8 | f32 | -1 x2634, 2 x9, 35 x9, 50 x8, 5 x4 ... (8 values) | A distance of the 0x835 component: Init squares it when positive; -1 = none. 35-80 where set | E D |
| +0x918 | u32 | 0 x2635, 4 x21, 2 x6, 1 x5, 3 x5 | | |

Same on every instance: +0x8AC = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x84C u32, +0x8BC u32, +0x8C0 u32, +0x8D4 u32, +0x8DC u32, +0x8E4 u32.

### 0x182 EnemyBurrowerBeetle

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC90; 178 placed in the space sectors, 410 in the game; e.g. `dust_creeper#`; own methods: Init 0x00118F34, Update 0x00118464.

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

Parent 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x840..+0xD30; 5 placed in the space sectors, 10 in the game; e.g. `vorselon`; own methods: fix-up 0x002ADB78, Init 0x002AD8A8, Update 0x002AD1CC.

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x910 | component |  | Prompt component (+0x910..+0x9A7), see *Prompt component* | E |
| +0x968 | u32 text id | 442 `GEN_TRIPROMPT_GPLAY_TELEPORT` x217, 907 `GEN_PROMPT_SPACE_TLKTOJUNK` x6, 1768 `GEN_PROMPT_GPLAY_TLKTOMAC` x1 ... (7 values) | Talk prompt text (442 default; GEN_PROMPT_SPACE_TLKTOJUNK 907 on junk traders) | E T |
| +0x96C | reference | Volume, set on 2 of 228: `mackReturnConvoVol`, `ss#_vorselon_scene_pip_cam` | Prompt volume: the prompt shows while the hero is inside it. Only used when the range is <= 0 | E |
| +0x974 | f32 | 3 x224, 0 x4 | Prompt range: the prompt shows within this distance (x1.5 once shown); <= 0 = use the volume | E |
| +0x988 | u32 | 2 on all 228 | Prompt value +0x78: 2 on nearly every instance (role open) | D |
| +0x998 | f32 | -1 on all 228 | Prompt value +0x88: -1 on nearly every instance (role open) | D |
| +0x9A4 | run time u8 |  | Prompt 3D distance: tested in 3D instead of on the ground plane | E |
| +0x9A5 | run time u8 |  | Prompt enabled | E |
| +0x9A6 | run time u8 |  | Prompt showing | E |
| +0x9A7 | run time u8 |  | Prompt button state | E |
| +0xC88 | u32 | 0 x170, 2 x42, 3 x13, 1 x3 | 0-3 (role open) | D |
| +0xC8C | reference | Area, set on 18 of 228: `TRADER_STOP_VOLS#`, `TERACH_#_STOP_POINTS`, `AREA_PRISON_STOP#` | Area of stop points | E D |
| +0xCB4 | reference | Volume, set on 8 of 228: `VOL_POLLYX_TRAITOR_POS`, `terrachnoid_escape_vol` | Reference slot: resolved by the class's fix-up | E |
| +0xCB8 | reference | Volume, set on 10 of 228: `VOL_CTRL_TERACHNOID#_POS`, `terach_#_stop_point_#`, `terrachnoid_escape_vol` | Reference slot: resolved by the class's fix-up | E |
| +0xCBC | f32 | 10.5 x227, 10 x1 | | |
| +0xCC0 | 4 bytes | 0x00000000 x215, 0x01000000 x13 | | |
| +0xCC4 | u32 | 0 x218, 5 x5, 3 x5 | 0, 3 or 5 | D |
| +0xCCC | reference | Volume, set on 13 of 228: `vorselon_cam`, `ss#_mission_transport_npc_cam_vol`, `ss#_mission_junk_cam_vol` | Camera volume for the conversation / picture-in-picture | D |
| +0xCD0 | reference | Volume, set on 13 of 228: `junk_trader_lightlink`, `vorselon_lightlink`, `terachnoid_lightlink` | "Light link" volume | D |
| +0xCD8 | f32 | 0 x226, 1.175 x2 | | |
| +0xCDC | f32 | -2 x226, -3 x2 | | |
| +0xD04 | 4 bytes | 0x00010000 x186, 0x00010001 x31, 0x01010000 x7, 0x01010001 x4 | | |
| +0xD08 | reference | Controller, set on 66 of 228: `CTRL_TERA#_AMBIENT_DIALOGUE`, `DIALOGUE_FONGOID_PAST#`, `AmbientFongoidDialog#` | Ambient dialogue controller | E D |
| +0xD28 | u32 | 0 x222, 3 x3, 4 x1, 2 x1, 1 x1 | | |

Same on every instance: +0x9B0 = 322, +0xC04 = 0.5, +0xC08 = -0.5, +0xC0C = 0.872665, +0xC10 = 7, +0xC14 = 18, +0xC18 = 0.2, +0xC1C = 0.5, +0xC24 = -25, +0xC28 = -50, +0xC70 = 1, +0xD14 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x900 u32, +0x904 u32, +0x908 u32, +0x9B4 f32, +0x9B8 f32, +0xA80 u32, +0xA84 u32, +0xA88 f32, +0xAAC u32, +0xAC0 u32, +0xAC4 u32, +0xAC8 u32, +0xACC u32, +0xB00 u32, +0xB04 f32, +0xC50 u32, +0xC90 u32, +0xC94 u32, +0xCB0 u32, +0xCC8 u32, +0xCF8 u32, +0xCFC u32, +0xD00 u32, +0xD0C u32, +0xD10 u32, +0xD20 u32.

### 0x19A swamp flyer / wasp

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xCD0; 54 placed in the space sectors, 87 in the game; e.g. `swamp_flyer#`, `moon#e_wasp#`; own methods: Init 0x001289A0.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC84 | 4 bytes | 0x00020002 x77, 0x00010002 x10 | | |
| +0xC9C | u32 | 0 x84, 1 x3 | | |
| +0xCA0 | f32 | 2 x76, 5 x8, 6 x2, 1 x1 | | |

Same on every instance: +0x920 = 409, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50, +0xBE4 = 0.5, +0xBE8 = 0.1, +0xBEC = 6.28319, +0xC78 = 4, +0xC7C = 16, +0xC80 = 16, +0xC88 = 1, +0xC8C = 5, +0xC90 = 0.5, +0xC94 = 5, +0xC98 = 5.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 u32, +0x9F4 u32, +0x9F8 u32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBE0 f32, +0xCC0 u8, +0xCC1 u8, +0xCC2 u8, +0xCC3 u8.

### 0x4BF spinner bot

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC10; 117 placed in the space sectors, 117 in the game; e.g. `spinner_bot#`, `moon#e_spinnerbot#`, `moon#e_enemy_spinnerbot#`; own methods: Init 0x0011CEB4.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC04 | f32 | 2 x73, 0 x28, 1 x16 | | |

Same on every instance: +0x920 = 409, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50, +0xBE4 = 0.5, +0xBE8 = 0.1, +0xBEC = 6.28319, +0xBF8 = 4, +0xBFC = 4, +0xC00 = 8.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 u32, +0x9F4 u32, +0x9F8 u32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBE0 u32, +0xBF0 u32, +0xC08 u8.

### 0x4E1 Agorian warrior

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xD30; 147 placed in the space sectors, 306 in the game; e.g. `agorian_warrior#`, `agorian_bomber#`; own methods: fix-up 0x0010BEE8, Init 0x001102D4, Update 0x0010DDBC.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC88 | u32 | 0 x189, 3 x49, 1 x42, 2 x26 | | |
| +0xD10 | 4 bytes | 0x00000000 x304, 0x00010000 x2 | | |
| +0xD1C | u32 | 0 x299, 256 x7 | | |

Same on every instance: +0x930 = 322, +0xB84 = 0.5, +0xB88 = -0.5, +0xB8C = 0.872665, +0xB90 = 7, +0xB94 = 18, +0xB98 = 0.2, +0xB9C = 0.5, +0xBA4 = -25, +0xBA8 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x934 u32, +0x938 u32, +0xA00 u32, +0xA04 u32, +0xA08 u32, +0xA2C u32, +0xA80 u32, +0xA84 f32, +0xBD0 u32, +0xBD8 u32, +0xBDC f32, +0xBF4 u32, +0xBFC f32, +0xC00 f32, +0xC08 f32, +0xC0C f32, +0xC10 f32, +0xC14 f32, +0xC50 u32, +0xC5C u32, +0xC60 f32, +0xC64 f32, +0xC68 f32, +0xC6C f32, +0xC8C u32, +0xCD0 u32, +0xCD4 u32, +0xCD8 u32, +0xCDC u32, +0xCE0 u32, +0xCE4 u32, +0xCE8 u32, +0xCEC f32, +0xCF4 f32, +0xCF8 f32, +0xCFC f32, +0xD00 f32, +0xD08 f32, +0xD0C u32, +0xD14 u8, +0xD15 u8, +0xD18 u8, +0xD19 u8, +0xD1A u8, +0xD1B u8, +0xD20 f32, +0xD24 f32.

### 0x52E PlayerBase

Parent 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x5C..+0x60; not placed itself; values below are over its 23 placed descendants.

No instance sets anything in the class's own range.

### 0x54E battery bot

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC90; 3 placed in the space sectors, 34 in the game; e.g. `moon#e_batterybot#`; own methods: fix-up 0x00107034, Init 0x00107D6C, Update 0x001072B8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0x940 | reference | unset on all 34 | Reference slot: resolved by the class's fix-up | E |
| +0xC80 | reference | Area, set on 32 of 34: `paths_iris_bot`, `PATHS_BATTERY_BOTS`, `junktownBatteryBot_FleePaths` | Area of flee paths | E D |
| +0xC84 | reference | Volume, set on 8 of 34: `vol_burner_nolock` | A no-lock volume | E D |

Same on every instance: +0x960 = 322, +0xBB4 = 0.5, +0xBB8 = -0.5, +0xBBC = 0.872665, +0xBC0 = 7, +0xBC4 = 18, +0xBC8 = 0.2, +0xBCC = 0.5, +0xBD4 = -25, +0xBD8 = -50, +0xC60 = 2, +0xC70 = -1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x920 f32, +0x924 f32, +0x928 f32, +0x92C f32, +0x934 u32, +0x964 f32, +0x968 f32, +0xA30 f32, +0xA34 u32, +0xA38 f32, +0xA5C u32, +0xAB0 u32, +0xAB4 f32, +0xC00 u32, +0xC20 u16, +0xC22 u16, +0xC24 f32, +0xC28 f32, +0xC2C u16, +0xC30 u32, +0xC34 u32, +0xC38 u32, +0xC3C u32, +0xC40 u32, +0xC4C u32, +0xC50 u32, +0xC54 u32, +0xC58 u32, +0xC78 u8, +0xC79 u8, +0xC7A u8, +0xC7B u8, +0xC7C u8, +0xC7D u8, +0xC7E u8, +0xC7F u8, +0xC88 u8, +0xC89 u8, +0xC8A u8, +0xC8B u8.

### 0x590 Agorian battlebot

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xBF0; 129 placed in the space sectors, 186 in the game; e.g. `agorian_battlebot#`, `moon#e_battlebot#`, `moon#e_enemy_battlebot#`; own methods: Init 0x00109DE8, Update 0x00108BF4.

*The name is descriptive (from the instances): this class has no name in the tables.*

Same on every instance: +0x930 = 322, +0xB84 = 0.5, +0xB88 = -0.5, +0xB8C = 0.872665, +0xB90 = 7, +0xB94 = 18, +0xB98 = 0.2, +0xB9C = 0.5, +0xBA4 = -25, +0xBA8 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x920 u8, +0x921 u8, +0x922 u8, +0x923 u8, +0x924 u8, +0x925 u8, +0x928 u32, +0x934 u32, +0x938 u32, +0xA00 u32, +0xA04 u32, +0xA08 u32, +0xA2C u32, +0xA80 u32, +0xA84 f32, +0xBD0 u32, +0xBD8 u32, +0xBDC f32.

### 0x5B0 junk trader

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD30; 19 placed in the space sectors, 36 in the game; e.g. `junk_trader#`, `junk_trader`, `junk_trader_pip`; own methods: Init 0x002B7DE4.

*The name is descriptive (from the instances): this class has no name in the tables.*

No instance sets anything in the class's own range.

### 0x5D3 Zoni

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xCD0; 18 placed in the space sectors, 36 in the game; e.g. `moon#b_zoni`, `moon#a_reward`, `moon#c_zoni`; own methods: fix-up 0x00133F40, Init 0x00135140, Update 0x00133EA8.

A SpaceMoon finds its Zoni by this class.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC90 | reference | Area, set on 14 of 36: `moon#b_zoni_path`, `moon#c_zoni_chase_paths`, `moon#a_zoni_chase_paths` | Area of chase paths | E D |
| +0xC94 | reference | Volume, set on 8 of 36: `moon#b_zoni_homevol`, `zoni_chase#_inner_vol`, `zoni#_pre_wander_vol` | Volume: start of the chase / home | E D |
| +0xC98 | reference | Volume, set on 10 of 36: `moon#b_zoni_homevol`, `zoni_chase_end_inner_vol`, `zoni#_post_wander_vol` | Volume: end of the chase | E D |
| +0xCB8 | f32 | 0 x35, 1.25 x1 | | |
| +0xCBC | 4 bytes | 0x00000000 x35, 0x00010000 x1 | | |

Same on every instance: +0x920 = 409, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50, +0xBE4 = 0.5, +0xBE8 = 0.1, +0xBEC = 6.28319, +0xC30 = 2, +0xC40 = -1, +0xC50 = 1.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 f32, +0x928 f32, +0x9F0 f32, +0x9F4 f32, +0x9F8 f32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xB90 f32, +0xBC0 u32, +0xBE0 u32, +0xBF0 u16, +0xBF2 u16, +0xBF4 f32, +0xBF8 f32, +0xBFC u16, +0xC00 u32, +0xC04 u32, +0xC08 u32, +0xC0C u32, +0xC10 u32, +0xC20 u32, +0xC24 u32, +0xC28 u32, +0xC48 u8, +0xC49 u8, +0xC4A u8, +0xC4B u8, +0xC4C u8, +0xC4D u8, +0xC4E u8, +0xC4F u8, +0xC54 u32, +0xC60 u32, +0xC64 u32, +0xC80 f32, +0xC84 f32, +0xC88 f32, +0xC8C f32, +0xC9C u32, +0xCA4 f32, +0xCA8 f32, +0xCAC f32, +0xCB0 f32.

### 0x5E0 Fongoid

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD60; 2 placed in the space sectors, 127 in the game; e.g. `fongoid#`; own methods: fix-up 0x002B6D54, Init 0x002B6DE0, Update 0x002B6948.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xD40 | u32 | 0 x63, 1 x21, 2 x15, 3 x6, 8 x6 ... (13 values) | | |
| +0xD44 | reference | Pod, set on 9 of 127: `ENTRANCE_BAND`, `DIRT_BAND` | Reference slot: resolved by the class's fix-up | E |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xD48 u8, +0xD49 u8, +0xD4C f32, +0xD50 u8, +0xD51 u8.

### 0x66A blob

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC80; 60 placed in the space sectors, 249 in the game; e.g. `blob_small#`; own methods: fix-up 0x00116178, Init 0x00116590, Update 0x00117384.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xC30 | reference | Pod, set on 39 of 249: `BLOB_ALLEY_#`, `POD_FOREST_BLOBS`, `POD_WATERFALL_BLOBS` | Pod of the blob's group | E D |
| +0xC34 | 4 bytes | 0x00000000 x153, 0x01000000 x96 | | |
| +0xC6C | f32 | 0 x174, 2 x41, 1 x13, 2.5 x13, 1.5 x8 | | |

Same on every instance: +0x920 = 322, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 f32, +0x9F4 f32, +0x9F8 f32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBF0 u32, +0xBF4 u32, +0xBF8 u32, +0xC00 f32, +0xC04 f32, +0xC10 f32, +0xC14 f32, +0xC18 f32, +0xC1C f32, +0xC20 u32, +0xC38 u32, +0xC3C u32, +0xC54 u32, +0xC5C u32, +0xC64 u32, +0xC68 f32, +0xC78 u8, +0xC79 u8.

### 0x699 Nefarious swarmer

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xC10; 94 placed in the space sectors, 571 in the game; e.g. `nefarious_swarmer#`; own methods: Init 0x00127270.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xBFC | u32 | 2 x285, 1 x234, 0 x52 | | |
| +0xC00 | f32 | 0 x457, 2 x51, 5 x20, 1 x20, 0.5 x15 ... (8 values) | | |

Same on every instance: +0x920 = 322, +0xB74 = 0.5, +0xB78 = -0.5, +0xB7C = 0.872665, +0xB80 = 7, +0xB84 = 18, +0xB88 = 0.2, +0xB8C = 0.5, +0xB94 = -25, +0xB98 = -50.

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0x924 u32, +0x928 u32, +0x9F0 f32, +0x9F4 f32, +0x9F8 f32, +0xA1C u32, +0xA70 u32, +0xA74 f32, +0xBC0 u32, +0xBF0 u32, +0xBFA u16.

### 0x69A Nefarious trooper

Parent 0x181 EnemyBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0x920..+0xF00; 123 placed in the space sectors, 676 in the game; e.g. `nefarious_medium_trooper#`, `moon#x_nef_med_trooper#`; own methods: Init 0x00125514.

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

Parent 0x187 NPCBase > 0x160 CharacterBase > 0x47 MobyBaseUpdate > 0x6 IGG_GameObjectUpdate; own fields +0xD30..+0xD40; 2 placed in the space sectors, 55 in the game; e.g. `terachnoid`, `ss#_mission_starcrossed_terachnoid_dude`; own methods: fix-up 0x002B9994, Init 0x002B9FF8.

*The name is descriptive (from the instances): this class has no name in the tables.*

| Offset | Type | In the retail files | Meaning | Evidence |
| --- | --- | --- | --- | --- |
| +0xD24 | reference | Area, set on 7 of 55: `movepoints_control_terach#`, `movepoints_control_pollyx` | Reference slot: resolved by the class's fix-up | E |
| +0xD34 | 4 bytes | 0x00000000 x54, 0x00010000 x1 | | |

Zero in every retail file but used by the class's own code (run-time state, or a setting no level uses): +0xD30 u32.
