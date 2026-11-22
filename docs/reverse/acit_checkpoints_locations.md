# A Crack in Time: checkpoints, location names, controller census

Research 2026-11-14. Tools: `dev/tools/checkpoints/` (gitignored): `exscripts.py` (every level's .lua / .csv into
`arc/<level>/`), `census.py [Ctrl|Moby|Clue]` (placed classes, counts, documented fields), `findtext.py` (text ids
inside settings blocks), `cpvols.py` (checkpoint / respawn volumes per level and their script use), `dscan.py <disp>`
(D-form loads / stores with a displacement). Ghidra (MCP) was running: the natives are already named there
(`script_checkpoint`, `script_show_location` ...).

Tooling trap: the shell `grep` in this environment is a ugrep wrapper with `--ignore-files`, so it silently skips
gitignored folders such as `dev/tools/*/arc`. Use `/usr/bin/grep` there.

## 1. Checkpoints

**No class runs checkpoints. A checkpoint is one Lua native call** (proven, code + scripts):

    checkpoint(hero, segment, respawn_volume, complete_segment, load_point [, quiet])

291 uncommented calls in the level scripts (every level except front_end). The trigger is whatever the script
waits for: `on_enter_volume(ratchet, <x>_checkpoint_vol, ...)` (most moons), a cinematic end (`play_goal_cam`), a
moon or mission completion (`moon_set_complete`, `battle_end`), landing / take-off (`CheckpointMe`), a level start
(`moons_init.lua`). No controller, Clue or Moby class references a checkpoint as such; of the 35 controller classes
placed in the game none is a checkpoint (census below).

So a checkpoint in the data is just two volumes plus script:

- **trigger volume** (`moonXx_checkpointN_vol`, usually a 9-11 unit box): only ever passed to `on_enter_volume`
  in the level script. Some are not used at all (see below).
- **respawn volume** (`..._respawn_vol`, 1x1x1 box): the third argument of `checkpoint()`. Its transform (position
  and orientation) is where the hero reappears.

Space sector moons keep them in `scripts/gameplay/tables.lua` (`table_moonXx.checkpoint_vol` = the respawn volume
used on landing) and per-moon `table_checkpoints = { { vol = ..., respawn_vol = ... } }` in `moonXx.lua`.

### What the native does (`script_checkpoint` 0x221EFC, binding 0x292ECC)

Arguments read by the binding: 1 hero handle, 2 segment (8-byte userdata), 3 volume handle (its first word = the
ACiT inline reference word of the volume), 4-6 integers (bools; the 6th is optional, nil = 0).

Per-level save block: `L = save + level * 0x6C8 + 0x600` (`save` = `*(TOC-0x7FD4 via 0xD2D470)`):

| Offset in L | Written by | Meaning |
| --- | --- | --- |
| segment slot (+0x2C each, 12 slots) | arg 4 = true | segment state = 2 (completed), +0x24 counter += 1 |
| +0x6A8 (save +0xCA8) | always | respawn volume reference word (death respawn) |
| +0x6AC (save +0xCAC) | arg 5 = true, when it changes | load point volume (where a loaded save starts) |
| +0x6B0 | always | hero kind (from the hero object, 0x16A53C) |

Then `0x4D1104(segment)` records the current segment and `0x9334E8(0,0)` posts message 4 to the save system
(auto-save). When arg 5 sets a new load point, arg 6 is false and more than 600 frames have run, the HUD banner
shows text 451 `GEN_TXT_GPLAY_CONTINUEPOINT` "Save Point" (the same banner as location names, see 2).
Nothing happens if the hero passed is not the current hero, or the hero object's vtable +0xAC check is true
(probably "is dead / respawning").

Respawn (0x1E0938, hero spawn): if the load mode (`0x29A000()`) is < 2 (new load) the checkpoint is reset to the load
point (`+0xCA8 = +0xCAC`); otherwise it uses `+0xCA8`. The word is decoded as a volume reference (low 12 bits =
index into the volume table, stride 0x90, high 16 bits checked against +0x7E of the entry, 0xF000 bits must be
zero) and the hero gets the volume's matrix. `get_checkpoint_volume` / `has_valid_checkpoint` read +0xCA8.
0x49F884 (load-menu path) clears +0xCA8 / +0xCAC.

Consequence for ReLunacy: the respawn point is **a volume's transform**, saved by reference word. Moving a
`..._respawn_vol` moves the respawn; deleting / reindexing volumes of a region invalidates saved games that hold a
reference to it (stale index -> the decode fails and 0 is returned).

### The orphan volumes from the question

- `junktrader_checkpoint_vol` (cylinder r 7.87, all five sectors): no script, no settings block references it. The
  junk trader checkpoint is set by script on landing (`table_junktrader.checkpoint_vol =
  junktrader_checkpoint_respawn_vol` in each sector's tables.lua, used by `CheckpointMe` / `play_goal_cam`).
  Unused leftover.
- Landing pad beam 0x72C +0x84 (`junktrader_checkpoint_respawn_vol`, 5 of 34 beams): only the class's fix-up
  touches it (0x2F3028); no other read found (SpaceMoon_Init does not, no native maps it). Inferred: unused
  authoring link, the scripts do the work.
- insomniac_museum `moon3a_checkpoint2/3_vol`, `_respawn_vol`, `moon3a_checkpoint_landing_pad`,
  `moon4b_checkpoint1_vol`: the museum reuses moon3a's layer; its scripts never name them (its only checkpoint
  call is `procgen_chall1.lua: checkpoint(get_hero(), SEGMENT_PROCGEN, proc_return_vol, ...)`). Unused copies.
- Other unused checkpoint-named volumes (cpvols.py, script names only): galacton_ship `vs2_azi_checkpoint_vol`;
  great_clock_d `RESPAWN_GCD_SUBCON_PUZZLES_START`; ss2 `moon2a_checkpoint_landing_pad`, `moon3c_checkpoint5_vol`;
  ss3 `moon2e_landingpad_checkpoint1`, `moon3b_checkpoint1_vol1`, `minimoon_checkpoint_vol`, `minimoon_respawn_vol`;
  ss4 `moon3a_checkpoint2_respawn_vol`; tombli `VOL_RESPAWN10`; zolar_forest `VOL_TEMPLE_SAVE`
  (ss4 `moon4d_checkpoint4_firepointN` are firepoint volumes owned by EnemyFirepointController, not checkpoints).

Per level (cpvols.py): calls = `checkpoint(` calls; cpvols = volumes whose name has checkpoint/respawn/save.

    agorian_arena 3/0  axiom_city 7/1  galacton_ship 15/33  gimlick_valley 4/4  great_clock_a 9/18
    great_clock_b 24/16  great_clock_c 20/17  great_clock_d 10/18  great_clock_e 2/6  insomniac_museum 1/6
    krell_canyon 18/10  molonoth 2/0  nefarious_station 2/7  space_sector_1 28/15  space_sector_2 32/31
    space_sector_3 39/25  space_sector_4 31/44  space_sector_5 20/14  tombli 5/15  valkyrie_fleet 1/31
    zolar_forest 6/1

Many levels (axiom_city, galacton_ship, nefarious_station...) use a `*_SAVE_TABLE` of `{segment, vol, ...}`
entries walked by one helper, with SAVE.* flags deciding which entry applies on load.

## 2. Location names

**Also script-only.** The banner is the Lua native `show_location(text_id [, seconds])` (`script_show_location`
0x220030: looks the text up, calls the HUD's banner object, HUD +0x220D4 vtable +0x48). `clear_location()`
(0x21FFD4) hides it. 114 uncommented calls. No settings block of any Moby / Clue / controller in the 22 levels holds
a location text id (findtext.py: the only hit for ids 947-1034/1353/1354/1430 is SpacePlanet `ClueSpacePlanet_Insom`
+0xCC = 1034 "Insomniac Museum", the planet's name in space). The banner object is otherwise only reached by the
checkpoint "Save Point" message and the HUD's construction.

Text ids: `<LVL>_TXT_GPLAY_LOCATION<n>` (947-1034, plus GC1 1353, 1354, 1430); level titles on arrival use
`<LVL>_TXT_MENU_LVLMC`. Molonoth: 1012 "Vullard Junkyard", 1013 "The Hollow", **1014 "Volgram Pass"**.

Triggers are plain volumes, by script. Patterns:

- `on_enter(get_hero(), VOLGRAM_LOC, function() UpdateLocation(MOL_TXT_GPLAY_LOCATION3) end)` (molonoth
  `scripts/gameplay/gameplay_0_global.lua:888`; `UpdateLocation` (line 736) only calls `show_location` when the tag
  differs from the last one). Same in axiom_city (`BUS_DEPOT_LOC`, `VOL_SPACEPORT_LOC`, `VOL_THEATRE_LOC`...) and
  nefarious_station (`SAVE.NS_HeroLocationTag` remembers the last one).
- `LOCATION_TABLE = { { inner_vol = ..., outer_vol = ..., tag = ... } }` + `on_enter_volume(get_hero(), v.inner_vol,
  ...)` (agorian_arena, galacton_ship, valkyrie_fleet); the outer volume was for re-arming (commented out).
- One-shot `on_enter(get_hero(), LOC_TAG_TRIG_LOCATIONn, function() show_location(...) unset_event() end)`
  (great_clock_b..d, krell_canyon `main.lua:1041+`), or on cinematic end / level start.

Location names are therefore NOT at checkpoints in data; some scripts happen to call both in the same handler
(agorian_arena requests an auto-save on entering the arena). Unused location texts (no script names them):
ABP 4, KRL 3, TOM 6, VS1 9-11/13/14, VS2 1/2/4/6-8/10, VS3 1/2/4-6.

What ReLunacy could do: list `show_location` / `checkpoint` script uses on the volume (volusers route f already
finds the volume names in scripts) and show the text id's string next to it.

## 3. Controller census (all 22 levels)

35 controller classes, 6,682 placed controllers (every controller has a settings block). Columns: placed count,
class, name (.fields; A4O name only where it is the same class - A4O names above ~0x6A0 are unrelated), struct size
(registry), documented fields in ACrackInTime.fields for the class + parents, authored = 4-byte slots that are
non-zero in at least one retail instance, undoc = authored slots no documented field covers.

| Placed | Class | Name | Size | Doc fields | Authored | Undoc | Undocumented authored slots |
| ---: | --- | --- | ---: | ---: | ---: | ---: | --- |
| 3235 | 0x3D0 | FxController | 0x1B0 | 27 | 31 | 9 | 0x60-0x68, 0x7C-0x8C, 0x1A0 |
| 707 | 0x369 | SoundEmitter | 0x28 | 7 | 4 | 0 | |
| 645 | 0x35E | CarSpawner | 0x60 | 14 | 17 | 9 | 0x20-0x2C, 0x40, 0x44, 0x4C, 0x58, 0x5C |
| 464 | 0x360 | CamArgFixed | 0x6C | 10 | 16 | 7 | CamArg base 0x1C 0x20 0x28 0x38 0x3C 0x58, own 0x50 |
| 348 | 0x202 | SoundEnvironment | 0x80 | 17 | 13 | 0 | |
| 322 | 0x173 | EnemyFirepointController | 0x620 | 4 | 2 | 0 | (rest is run-time) |
| 238 | 0xD2 | CamArgFollowPath | 0xA8 | 22 | 30 | 9 | CamArg base + 0x94, 0xA0 |
| 118 | 0x7A3 | hangar_chunk | 0xC8 | 2 | 36 | **35** | almost everything |
| 105 | 0x402 | CamArgFixedFollow | 0x74 | 11 | 19 | 9 | CamArg base + 0x6C |
| 77 | 0x171 | EnemyBattlefieldController | 0xAC | 2 | 1 | 0 | (only Range is authored) |
| 65 | 0x260 | CamArgIndexedPath | 0x9C | 11 | 27 | 17 | CamArg base + 0x6C-0x98 |
| 63 | 0x8A9 | infocontroller | 0xB8 | 14 | 8 | 0 | |
| 63 | 0x789 | cam_gcb_subcon_puzzle_cham_button | 0x78 | 8 | 14 | 9 | CamArg base + 0x64-0x70 |
| 42 | 0x79E | dialogue_fongoid_past | 0xD8 | 2 | 1 | 0 | (only Range is authored) |
| 41 | 0x73B | electric_arc_master | 0x8BC | 2 | 19 | **18** | 0x14-0x98, 0x8B8 |
| 37 | 0x333 | CamArgOpeningView | 0x68 | 10 | 12 | 4 | CamArg base |
| 30 | 0x472 | VerticalFogController | 0x90 | 8 | 3 | 0 | |
| 23 | 0x626 | HazardSwampGasJet | 0x20 | 3 | 4 | 2 | 0x18, 0x1C |
| 13 | 0x7F8 | timeslowcontroller | 0x40 | 13 | 10 | 0 | |
| 6 | 0x75E | SpaceAttackController (Nefarious) | 0x60 | 13 | 20 | 9 | 0x14-0x34 (0x714 base) |
| 5 | 0x713 | zapper | 0x28 | 3 | 3 | 1 | 0x18 |
| 5 | 0x3EF | WeatherController | 0xA0 | 3 | 22 | **20** | almost everything |
| 4 | 0x75D | SpaceAttackController (Agorian) | 0x60 | 13 | 20 | 9 | 0x14-0x34 (0x714 base) |
| 4 | 0x6E3 | SpaceAsteroidField | 0x50 | 3 | 2 | 0 | |
| 4 | 0x2D0 | CameraSwayController | 0x48 | 3 | 6 | 4 | 0x14, 0x38-0x40 |
| 4 | 0x13A | AlarmController | 0x2C | 4 | 4 | 3 | 0x1C-0x24 |
| 3 | 0x81D | SpaceAttackController (Valkyrie) | 0x60 | 12 | 16 | 8 | 0x14-0x34 (0x714 base) |
| 2 | 0x72B | poison_fog | 0x50 | 2 | 8 | **7** | 0x14-0x30 |
| 2 | 0x371 | EnemyGroupController | 0xA0 | 3 | 3 | 1 | 0x98 |
| 2 | 0x2E2 | camarggravtube | 0x60 | 8 | 8 | 3 | CamArg base |
| 1 | 0x8B8 | controller_nefarious_disguise | 0x1DC | 2 | 1 | 0 | |
| 1 | 0x858 | crash_fire | 0x14 | 2 | 1 | 0 | |
| 1 | 0x3CC | swingcam | 0x60 | 8 | 8 | 3 | CamArg base |
| 1 | 0x22E | CamArgFixedZoom | 0x68 | 8 | 10 | 5 | CamArg base + 0x60, 0x64 |
| 1 | 0x211 | MoveGameObjectController | 0x80 | 2 | 1 | 0 | |

The CamArg base (0xD1) slots 0x18-0x28, 0x38, 0x3C, 0x58 are undocumented in every camera class (~980 placed).

### One-line roles of the undocumented / thin classes

- **0x7A3 hangar_chunk** (118; galacton_ship HANGAR/SWING/BOSS_CHUNK, great_clock_e CheapChunk, great_clock_a
  TIME_FREEZE_CHUNKS): debris spawner. Bursts of chunk Mobys (NoPhysicsChunk 0x56C family) of a given Moby class
  thrown from the controller's matrix at random intervals. A4O's equivalent is ChunkSpawnController (0x82B there).
  Decoded below.
- **0x73B electric_arc_master** (41; Electric_Arc_Master, explosion_master, mortar barrage "Master_Ctrlr"): random
  FX master. Scripts register FxControllers with `register_fx_slave(master, fx)`; the master triggers a random slave
  that lies in a distance band from the hero and in front of the camera, on up to four timed channels. Decoded below.
- **0x171 EnemyBattlefieldController** (77) and **0x79E dialogue_fongoid_past** (42), **0x8B8
  controller_nefarious_disguise**, **0x858 crash_fire**, **0x211 MoveGameObjectController**: nothing authored but
  Range; all other bytes are run-time (script-driven via natives). Nothing to document for the editor.
- **CamArg base (0xD1)** slots +0x18..+0x28 / +0x38..+0x48: the two CAM_CameraBlendParam blocks (0x20 bytes each:
  +0 type, +0x18 seconds proven by `script_activate_cam`, which fills one as {type 6 or 0, seconds}). The middle words
  (+4 0/1/2, +8 0/1, +0xC always 1, +0x10 0/1, +0x14 always 1) were not traced. Defaults written by 0x2A0B4:
  +0x18 = 0, +0x1C = 1, +0x20 = 1, +0x24 = 0, +0x28 = 1. +0x58 = camera type (run-time, set by each Init).
- **0x3EF WeatherController**, **0x72B poison_fog**, **0x2D0 CameraSwayController**, **0x13A AlarmController**,
  **0x626 HazardSwampGasJet**, **0x713 zapper**, **0x371 EnemyGroupController**: see the decoded sections.
- Partly documented: FxController (9 slots: 0x60-0x68, 0x7C-0x8C, 0x1A0), CarSpawner (9), CamArgIndexedPath (17,
  mostly CamArg base + 0x6C-0x98), SpaceAttackController base 0x714 +0x14..+0x34 (already listed open in the space
  sector research).

## 4. Decoded classes

### 0x7A3 hangar_chunk (debris spawner) - proven unless marked

Init 0x34FEE8: Range = 4096, active = (+0xC5 != 0). Update 0x3506A0: when the burst timer expires, picks a new
interval (+0x28 +- +0x2C, >= 0) and spawns `+0x30 + random(0..1) * +0x34` chunks (0x34FF04 each). If +0x14 > 0 it
counts down and then turns the controller off (0 = runs until deactivated). Slot 20 (0x3505E8) copies a whole
0xAC-byte parameter block over +0x14..+0xBF (the settings block is that parameter block) and arms attach mode when
+0xB8 != -1. Spawn 0x34FF04: position by +0x38 (0 = controller origin, 1 = random in a sphere of radius +0x40,
2 = box +-(+0x40, +0x44, +0x48) along the matrix axes); direction = matrix up (+0x20 row) tilted by a random
+-(+0x3C) degrees; speed +0x54 +- +0x58; lifetime +0x4C +- +0x50; the Moby class is +0x1C; a random model variant
bit; flags +0xB2/+0xB3/+0xB4 set chunk flags 0x10/0x20/0x40; +0xB0 marks NoPhysicsChunk ones (flag 4 at +0x108).
+0x64 / +0x68 (+- +0x6C) go to chunk +0x98/+0x9C, +0x70..+0x7C are lerped pairs to chunk +0xA0/+0xA4 (inferred:
scale start/end and fade). +0x5C (360 everywhere), +0x60 (60/180, a random draw whose result the decompiler loses),
+0x80..+0xAC (1.0 everywhere), +0x20, +0x24, +0xB1 not traced.

    class 0x7A3 ChunkSpawner : 0xCF
    {
        [0x14, float] Duration = "Seconds before it stops; 0 runs until deactivated.";      // proven
        [0x18, float] Duration random = "";                                                  // proven
        [0x1C, u32] Chunk class = "Moby class of the chunks.";                               // proven
        [0x28, float] Interval = "Seconds between bursts.";                                  // proven
        [0x2C, float] Interval random = "";                                                  // proven
        [0x30, u32] Chunks per burst = "";                                                   // proven
        [0x34, u32] Extra chunks random = "";                                                // proven
        [0x38, ChunkSpawnArea] Spawn area = "";                                              // proven
        [0x3C, float] Spread angle = "Degrees off the controller's up axis.";                // proven
        [0x40, float] Area size X = "Radius for a sphere.";                                  // proven
        [0x44, float] Area size Y = "";                                                      // proven
        [0x48, float] Area size Z = "";                                                      // proven
        [0x4C, float] Chunk lifetime = "Seconds.";                                           // proven
        [0x50, float] Chunk lifetime random = "";                                            // proven
        [0x54, float] Speed = "";                                                            // proven
        [0x58, float] Speed random = "";                                                     // proven
        [0x64, float] Chunk value A = "";                                                    // proven passed, role inferred
        [0x68, float] Chunk value B = "";
        [0x6C, float] Chunk value random = "";
        [0xB0, bool] No physics = "";                                                        // proven (flag), name inferred
        [0xB2, bool] Chunk flag 1 = "";
        [0xB3, bool] Chunk flag 2 = "";
        [0xB4, bool] Chunk flag 3 = "";
        [0xB8, Moby, runtime] Attached to = "";                                              // proven (resolved as a handle)
        [0xBC, bool] Follow attachment = "";                                                 // proven
        [0xC5, bool] Start active = "";                                                      // proven
    }

    enum ChunkSpawnArea : u32
    {
        Point = 0;
        Sphere = 1;
        Box = 2;
    }

### 0x73B electric_arc_master (random FX master) - proven

Init 0x34B9C0 clears the slave list (+0xB4, 0x200 words) and its count (+0x8B4), sets up four channels at +0x14,
+0x3C, +0x64, +0x8C (0x28 bytes each, 0x34B754) and starts active when +0x8B8 != 0. Update 0x34BD50 runs each
channel (0x34BA70): a channel is on when its max distance > 0. When its timer (random between min and max interval)
expires it tries the slaves from a random start and fires (vtable +0x38) the first one whose distance to the hero is
within [min, max] and that is in front of the camera (0x34B7D8); with "exclusive" set it skips slaves another
channel used last. Slaves come from `register_fx_slave(master, fx)` in the scripts (natives), not from data.

Channel layout (offset inside the channel): +0x00 min distance, +0x04 max distance, +0x08 min interval, +0x0C max
interval, +0x10 hold time (how long a slave stays "used"), +0x14 exclusive, +0x18/+0x1C run-time last slaves,
+0x20/+0x24 run-time timers.

    class 0x73B FxMaster : 0xCF
    {
        [0x14, float] Channel 1 min distance = "From the hero.";
        [0x18, float] Channel 1 max distance = "0 or less turns the channel off.";
        [0x1C, float] Channel 1 min interval = "Seconds.";
        [0x20, float] Channel 1 max interval = "Seconds.";
        [0x24, float] Channel 1 hold time = "Seconds before a used effect can fire again.";
        [0x28, bool32] Channel 1 exclusive = "Skips effects other channels just used.";
        [0x3C, float] Channel 2 min distance = "";
        [0x40, float] Channel 2 max distance = "";
        [0x44, float] Channel 2 min interval = "";
        [0x48, float] Channel 2 max interval = "";
        [0x4C, float] Channel 2 hold time = "";
        [0x50, bool32] Channel 2 exclusive = "";
        [0x64, float] Channel 3 min distance = "";
        [0x68, float] Channel 3 max distance = "";
        [0x6C, float] Channel 3 min interval = "";
        [0x70, float] Channel 3 max interval = "";
        [0x74, float] Channel 3 hold time = "";
        [0x78, bool32] Channel 3 exclusive = "";
        [0x8C, float] Channel 4 min distance = "";
        [0x90, float] Channel 4 max distance = "";
        [0x94, float] Channel 4 min interval = "";
        [0x98, float] Channel 4 max interval = "";
        [0x9C, float] Channel 4 hold time = "";
        [0xA0, bool32] Channel 4 exclusive = "";
        [0x8B4, u32, runtime] Effect count = "Effects registered by the level script.";
        [0x8B8, bool32] Start active = "";
    }

### 0x72B poison_fog - proven (colour / distances inferred from the values)

Init 0x344818: inactive, blend 0. Update 0x344858: while active it blends (over +0x14 seconds, out over +0x18) the
global fog from the level's own values (saved from fog globals +0xD0..+0xE4 when it starts) to its own: 0x5D208C
gets +0x28/+0x2C/+0x30, 0x5D209C gets +0x1C/+0x20/+0x24. Values: (0.6, 0, 0.1) = a purple-red colour,
(5 or 7, 50, 0.6 / 0.3) = near, far, opacity. Turned on by script (`activate`). agorian_arena and valkyrie_fleet.

    class 0x72B poison_fog : 0xCF
    {
        [0x14, float] Fade in time = "Seconds.";                    // proven
        [0x18, float] Fade out time = "Seconds.";                   // proven
        [0x1C, float] Fog near = "";                                // inferred name
        [0x20, float] Fog far = "";                                 // inferred name
        [0x24, float] Fog opacity = "";                             // inferred name
        [0x28, float] Fog red = "";                                 // inferred name
        [0x2C, float] Fog green = "";                               // inferred name
        [0x30, float] Fog blue = "";                                // inferred name
        [0x34, float, runtime] Blend = "";                          // proven
    }

### 0x3EF WeatherController - structure proven, most names inferred

Init 0x34A6C8 pushes its values into the weather system (globals at `*(0xD2F768-0x7FE0)`): vector +0x20..+0x2C
(0x65F2C0), vector +0x30..+0x3C (0x65F2A0), +0x40 (0x65EE78), +0x44 (0x65EEC4), +0x4C (0x65EF5C), +0x50 (0x65EFA8),
and +0x5C as a 16-bit id looked up in a table (0x6AF348; 27 rain, 85 tombli). Active = +0x70. Update 0x34AA44:
when on (0x34A478) the intensity fades to +0x48 over +0x54 seconds (0x65EF10); off fades to 0. The Area +0x60
(AREA_NO_RAIN / AREA_NOSNOW) switches it off while the hero is inside, after +0x58 seconds. With +0x71 set it also
creates a post-process node (+0x98, run-time): tint +0x80/+0x84/+0x88, saturation +0x8C, brightness +0x94 (only
with +0x72), blur +0x90 (the setters are the ones behind the natives pfx_set_node_tint / _saturation /
_brightness / _blur). gimlick_valley, tombli, valkyrie_fleet (2), zolar_forest.

    class 0x3EF WeatherController : 0xCF
    {
        [0x20, float] Wind X = "";                                  // inferred (5 on three levels)
        [0x24, float] Wind Y = "";
        [0x28, float] Wind Z = "";
        [0x30, float] Weather value 1 = "";                         // proven pushed; role unknown (0.5 everywhere)
        [0x34, float] Weather value 2 = "";
        [0x38, float] Weather value 3 = "";
        [0x3C, float] Weather value 4 = "";
        [0x40, float] Weather value 5 = "";                         // 2 .. 30
        [0x44, float] Weather value 6 = "";
        [0x48, float] Intensity = "";                               // proven
        [0x4C, float] Weather value 7 = "";
        [0x50, float] Weather value 8 = "";
        [0x54, float] Fade time = "Seconds to reach the intensity."; // proven
        [0x58, float] Area delay = "Seconds before the no-weather area takes effect."; // proven
        [0x5C, u32] Weather effect = "";                            // proven id, table unknown
        [0x60, Area] No-weather area = "Inside it the weather stops."; // proven
        [0x68, float, runtime] Timer = "";
        [0x6C, u32, runtime] State = "";
        [0x70, bool] Start active = "";                             // proven
        [0x71, bool] Screen effect = "Also tints the screen.";      // proven
        [0x72, bool] Screen colour = "Applies the tint, saturation and brightness."; // proven
        [0x80, float] Tint red = "";                                // proven
        [0x84, float] Tint green = "";
        [0x88, float] Tint blue = "";
        [0x8C, float] Saturation = "";                              // proven
        [0x90, float] Blur = "";                                    // proven
        [0x94, float] Brightness = "";                              // proven
    }

### 0x2D0 CameraSwayController - proven (parameter roles inferred)

Update 0x3445EC: while active and the hero is inside the Area +0x34, starts a camera sway once (0x4B6F0 gets
+0x3C, +0x38 converted from degrees at Init, +0x40) and stops it when the hero leaves. +0x14 is overwritten at Init.
galacton_ship (4).

    class 0x2D0 CameraSwayController : 0xCF
    {
        [0x34, Area] Sway area = "The camera sways while the hero is inside."; // proven
        [0x38, float] Sway angle = "Degrees.";                     // proven conversion, name inferred
        [0x3C, float] Sway speed = "";                             // inferred
        [0x40, float] Sway period = "";                            // inferred
    }

### 0x13A AlarmController - proven

On (slot 14): sets the screen alarm flash (0x1E2074 with +0x1C, +0x20, +0x24), plays its sound, switches on the
alarm lights (Mobys of class 0x46D) of the Pod +0x14. Off (slot 15) reverses it. Update: while on, turns itself off
once no hero is inside the Volume +0x18 (none = stays on). `alarmcontroller_noflash` has +0x1C = 10000.

    class 0x13A AlarmController : 0xCF
    {
        [0x14, Pod] Alarm lights = "Lights switched on with the alarm.";   // proven
        [0x18, Volume] Alarm volume = "Turns off when no hero is inside.";  // proven
        [0x1C, float] Flash period = "Seconds between flashes.";            // inferred from noflash = 10000
        [0x20, float] Flash strength = "";                                  // inferred
        [0x24, float] Flash length = "";                                    // inferred
        [0x28, bool, runtime] On = "";
    }

### 0x626 HazardSwampGasJet - proven

Update 0x1679E4: three states timed by the controller's state clock: 0 idle for +0x14 s, then 1 (warning, sound
0xB9) for +0x18 s, then 2 (gas burst, sound 0x197) for +0x1C s, then idle again. ss2 moons.

    class 0x626 HazardSwampGasJet : 0xCF
    {
        [0x14, float] Idle time = "Seconds between bursts.";
        [0x18, float] Warning time = "Seconds of warning before a burst.";
        [0x1C, float] Burst time = "Seconds the gas burns.";
    }

### 0x713 zapper - proven

Update 0x4232C0: while active, every +0x18 seconds zaps the next Moby of the Pod +0x14 (0x2B63B4 from the
controller's position); stops after the last one. Init: active = +0x24. zolar_forest (5).

    class 0x713 zapper : 0xCF
    {
        [0x14, Pod] Targets = "Mobys zapped one after the other.";
        [0x18, float] Interval = "Seconds between zaps.";
        [0x1C, float, runtime] Timer = "";
        [0x20, u32, runtime] Next target = "";
        [0x24, bool32] Start active = "";
    }

### 0x371 EnemyGroupController (partial)

Init 0x97B48: gives every Moby of the Pod +0x9C its handle (enemy +0x894 = this controller) and copies +0x98 into
two 0x40-byte group blocks (+0x18, +0x58). +0x98 role not traced. axiom_city (2).

### 0x35E CarSpawner (additions) - proven

Spawn 0x34EE14 takes a random Moby of the Pod +0x18 and clones it as a path follower (SimplePathFollower 0x337,
PathFollower 0x24C or class 0x647), on Path +0x14, speed +0x3C, with a random offset +-(+0x20, +0x24, +0x28)
from the path. +0x40 goes to the follower's second value (PathFollower +0x4E8 start position / SimplePathFollower
+0x134), +0x4C to SimplePathFollower +0x140, bytes +0x50/+0x51/+0x52 to follower flags (+0x51 also attaches effect
0x183). With +0x55 set the clone spins (follower +0x147 = 1, axis mode 3, speed random between +0x58 and +0x5C
degrees/s, random sign). **+0x53 is not "Extra update": it pre-fills the path** (0x34FA4C walks the path spawning
objects spaced speed x interval, then clears +0x53) - the current .fields name is wrong. +0x2C, +0x44 not read in
these functions.

    [0x20, float] Offset X = "Random sideways spread from the path.";       // proven
    [0x24, float] Offset Y = "";                                             // proven
    [0x28, float] Offset Z = "";                                             // proven
    [0x40, float] Follower start = "";                                       // proven passed, role inferred
    [0x4C, u32] Follower mode = "";                                          // proven passed, role unknown
    [0x50, bool] Follower flag 1 = "";
    [0x51, bool] Follower flag 2 = "";
    [0x52, bool] Follower flag 3 = "";
    [0x53, bool] Fill path at start = "Starts with the path already populated.";  // proven (replaces "Extra update")
    [0x55, bool] Spin = "Spawned objects spin.";                             // proven
    [0x58, float] Spin speed min = "Degrees per second.";                    // proven (unit inferred)
    [0x5C, float] Spin speed max = "";                                       // proven

## 5. Not determined

- Landing pad beam 0x72C +0x84: no reader found outside its fix-up; a reader through a pointer (not a linear
  displacement) cannot be excluded without Ghidra cross references on the beam object.
- What exactly sets `+0x6B0` in the checkpoint record (hero kind via 0x16A53C) and which save message 4 is.
- CamArg blend block middle words (+4, +8, +0x10 of each 0x20 block).
- WeatherController +0x30..+0x50 roles, +0x5C table; CameraSway parameter roles; Alarm flash parameters' exact
  meaning; hangar_chunk +0x5C, +0x60, +0x80..+0xAC, +0x20, +0x24; CarSpawner +0x2C, +0x44; FxController 9 slots;
  SpaceAttackController 0x714 +0x14..+0x34; EnemyGroupController +0x98.
- Whether any location banner or checkpoint is fired from native code other than the HUD and the natives (only the
  HUD construction and the two natives reach the banner object by a direct +0x220D4 displacement).
