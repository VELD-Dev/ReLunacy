# A Crack in Time: who uses each volume

NPUA80966 (01.20), 2026-11-14. Question: for every volume of a level, what uses it and how. Focus: the `ITEM_*`
volumes of insomniac_museum, the `AUD_*` volumes, and the controller classes behind them.

"Proven" = read in the EBOOT code or checked on every instance of the shipped data. "Inferred" = from data patterns
or partial code reading. Everything else is marked open.

Tools (gitignored, `dev/tools/volusers/`, run python without `-I`):

| Script | What |
| --- | --- |
| `extract.py <level>... [global]` | extracts a level's archives (and global_cached) into `arc/<level>/` (read-only on the game) |
| `users.py <level> [--files] [--json f] [-q]` | the "who uses this volume" index (routes below); `--files` adds the TUID / name search over every other file |
| `census.py [filter]` | game-wide: every reference word in every gp_prius, grouped by (owner kind, class, field), marked shown / HIDDEN in ReLunacy today |
| `gamewide.py <regex>` | game-wide gp_prius ownership of the volumes whose name matches |
| `text.py <id>...` | core_text_tags.us.pkg lookup (tag name + text) |
| `hashprobe.py <level>` | looks for crc32 / fnv1 / fnv1a / djb2 / sdbm hashes of volume names in all files (none found) |
| `out_<level>.txt`, `<level>.json` | the full per-volume index for insomniac_museum, axiom_city, krell_canyon, space_sector_1 |

## 1. Short answers

- **ITEM volumes (insomniac_museum) are owned by `infocontroller` (controller class 0x8A9)**, not by scripts. The
  link is the controller's settings field **+0x74 (Volume reference)**, inside an embedded Prompt component at
  +0x18. +0x70 is the prompt text (`INS_TXT_MUSEUM_ITEM_GENBTTN` = "(button) for Info"), **+0xB0 the popup title and
  +0xB4 the popup body** (text ids). Pressing the button opens an info screen with those two texts. 58 in the museum,
  4 plaques in agorian_arena, 1 in tombli. Proven (fix-up, prompt range test, update code; all 63 instances).
- **AUD volumes are owned by `SoundEnvironment` (controller class 0x202)**. Its settings field **+0x28 is a pointer
  to a list of Volume references** stored in the settings block's extra data, not in the 0x2501C struct. The
  environment's sound plays while the listener is inside any listed volume. 384 of the game's 391 `AUD*` volumes are
  listed this way. Proven (fix-up and update code). `SoundEmitter` (0x369) controllers then name the environments
  they depend on through their own list at +0x18.
- **Why ReLunacy shows nothing for them:** (1) the inspector's "Used by" scans only Clues, Mobys and *camera*
  controllers; every other controller (sound, info, FX, fog, time slow, firepoints...) is never searched;
  (2) `ReadSettings` keeps only the 0x2501C struct, so references in **lists in the settings extra data** are never
  decoded (SoundEnvironment, SoundEmitter, nefarious_dropship, PhysicsObject).
- **Nothing else links volumes.** No reference words in the unread gp_prius sections (0x25005, 0x25006, 0x2500C,
  0x25010, 0x25014, 0x25049, path half of 0x25088) nor in the Moby record +0x30..+0x4C; no volume TUID in any
  other file (level archive incl. cinematics.dat / zones.dat, global_cached); no volume name outside gp_prius,
  instances.lua and the scripts; no crc32 / fnv / djb2 / sdbm name hash anywhere; no reference words in
  gp_persist.dat, gameplay.dat, metadata.dat. Proven for the four levels (full scans).

## 2. The index: `users.py`

For every volume (file order = `Volume.Index`): name, layer, shape, position, scale, then every user:

| Route | Meaning | Where it lives |
| --- | --- | --- |
| a | its own Clue | Clue record (0x25064) +0x00 = reference to the volume |
| b | a reference word to it in any settings: struct field or an extra-data list; Moby / Clue / controller; private (0x25020) or class default (0x25030) | whole-file scan of every 4-aligned word that is not a fix-up location, attributed to the owning record / settings block / list |
| b2 | a reference to its Clue | same scan |
| c | member of an Area | 0x25080 +4 -> 0x25088 volume list |
| c2 | who references that Area | same scan, target = the Area |
| d | its Clue is in a Scent | 0x2508C +0 -> 0x25094 |
| d2 | who references that Scent | same scan |
| e | a reference in a section nobody reads | same scan |
| f | level script use | instances.lua `name = volume_handle:new(layer, index)` (also the Clue / Area / Scent handles that lead to it) + the level's `.lua` scripts (comments and string literals ignored), with the native it is passed to |
| g | other files | 8-byte TUID (big-endian, 4-aligned) or whole-token name in every other extracted file (`--files`) |

Each user is printed with kind, name, class, field (struct offset, or `+0x28[i]` for element i of the list a
pointer field points to) and `[not shown]` when ReLunacy's inspector does not show that link today.

Script notes: instances.lua repeats names (ss1: five `moon_ap01_vol`). Lua keeps the **last** assignment, so a
script name reaches only the last instance of that name; `users.py` does the same. Example: axiom_city's two
`VOL_SCENE_TERA2_ENEMY_NPC1_POS1` (layers gameplay_2_spaceport and gameplay_scenes): only one is reachable.

## 3. Results on four levels

Counts are volumes (a volume with several users counts once per route).

| Level | Volumes | a Clue | b ref | c Area | c2 Area user | d Scent | d2 Scent user | e | f script |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| insomniac_museum | 566 | 56 | 260 | 267 | 82 | 3 | 1 | 0 | 171 |
| axiom_city | 899 | 54 | 161 | 411 | 223 | 4 | 0 | 0 | 267 |
| krell_canyon | 1,273 | 313 | 181 | 663 | 204 | 247 | 81 | 0 | 533 |
| space_sector_1 | 524 | 12 | 122 | 92 | 83 | 2 | 0 | 0 | 101 |

Route b2 (references to a volume's Clue) found nothing once Scent lists were attributed: nothing in ACiT
references a Clue except Scents. Route g found nothing real (one coincidental token in krell_canyon's text table).

Before / after:

| Level | Shown today (any link ReLunacy displays) | Orphans today | Gained from hidden gp_prius links | Any gp_prius user | Script only | Any user | Orphans left |
| --- | --- | --- | --- | --- | --- | --- | --- |
| insomniac_museum | 346 | 220 | 80 | 426 | 91 | 517 | **49** |
| axiom_city | 574 | 325 | 31 | 605 | 162 | 767 | **132** |
| krell_canyon | 1,079 | 194 | 11 | 1,090 | 114 | 1,203 | **70** |
| space_sector_1 | 225 | 299 | 0 | 225 | 44 | 269 | **255** |

"Shown today" counts the Clue, struct references of Clues / Mobys / camera controllers, and Area / Scent membership
names. It does not count who uses an Area (route c2), which ReLunacy never shows either.

Game-wide (gp_prius only, all 22 levels, `gamewide.py .`): 13,798 volumes, 8,765 with a gp_prius user, 5,033
without (many of those are script-driven).

### Orphan families (no user by any route)

**insomniac_museum (49):** 21 `moon3e_apNN_vol` (gameplay_moon3a), checkpoint / respawn volumes of the moon
(`moon3a_checkpointN_vol`, `_respawn_vol`, `moon3a_checkpoint_landing_pad`), racetrack `shortcut_N_start/end_vol`
and `spiral_N_start/end_vol` (8), kitchen volumes (4), `ITEM_xenoid_vol` (see 4), `booper_id_vol`, `clifflookat`,
`warp_gc_c_puzzle`, `warp_missile_temp`, `volume1` (art_background, sphere 800).

**axiom_city (132):** unnamed `volumeN` (62 over the gameplay layers), cinematic positions `VOL_SCENE_*_POS*`,
`VOL_TD_SCENE*_HERO_POS*` (namesakes shadowed in Lua, see 2), `VOL_TRIPAD_TD*` (6), `VOL_HALFPIPE_WARP*`,
boss-battle helpers `VOL_BB_PLAT*`, boss cylinders `Boss_1*` (layer ambient), `HeatMapVolume` (map layer).

**krell_canyon (70):** dialogue triggers of layer gameplay_krell_dialogue (`AZI_TRIG_GOTO_N_N` 9, `AZI_TRIG_PATH_*`,
`DIAL_TRIG_*`), Azimuth stop / warp points, `BAR_HEIGHT_*`, `A1_*_LOOPS`, playpath `MapBorder`, sound-layer
`bridge_vol`.

**space_sector_1 (255):** `level_gridN` 80 (user-tested: no effect), `moon_apNN_vol` 22 per moon x 5 moons = 110
(1x1 boxes; moon1x / 1y ones 0.5), moon1x `groupN_inner_vol` / `groupN_outer_vol` 16 + 16 spheres (25 / 35),
`ss1_location_*` spheres (200; quantos 800), Vorselon chase / exit volumes, `level_boundary(_planets)`.

The `moon_apNN_vol` family exists on every moon of every sector (22 each) and in the museum's moon. Placement:
boxes around the moon at the six axis points ~35 units from the centre plus 16 more on rings (museum moon3e
centre about (-2180, 700, -1000): ap01 (-2215.7, 735.4, -1000.1), ap11 (-2130.1, 700.0, -1000.1)...). No data,
script, name string or hash user found; only `moon3e_ap00_vol` is used, by the museum's main.lua (`on_enter`).
Their purpose is **open**.

**AUD and ITEM orphans:** none in these four levels except `ITEM_xenoid_vol`. Game-wide, 7 `AUD*` volumes have no
gp_prius user: agorian_arena `AUD_VOL_LIVEMIXER`, gimlick_valley `AUD_V_Outdoors_Happy` / `_Past`, valkyrie_fleet
`AUD_V_Towers1/2`, zolar_forest `AUD_V_Temple_Open_Hall1` / `AUD_V_Temple_Large_Room` (layer audio_reverb; scripts
of those levels not checked).

## 4. ITEM volumes of insomniac_museum: infocontroller 0x8A9

The museum has 52 volumes named `ITEM*` / `item*` (46 in layer gameplay, 5 gameplay_krell, 1 gameplay_moon3a):
49 are referenced by an infocontroller, 2 only by scripts, 1 by nothing. **58 infocontroller controllers** (layers gameplay, gameplay_krell, gameplay_moon3a) carry the texts:

| Controller | +0x74 volume | +0xB0 title | +0xB4 body |
| --- | --- | --- | --- |
| t042_swimming | ITEM_042_vol | INS_TXT_MUSEUM_ITEM_042T "Lombaxes Don't Swim" | INS_TXT_MUSEUM_ITEM_042 |
| t081_missile | ITEM_081_vol | ..._081T "Rocket Ride to Oblivion" | ..._081 |
| t151_dicecrate | ITEM_dicecrate_vol | ..._151T "Dice Crate" | ..._151 |
| ... (58 rows, `python3 users.py insomniac_museum`) | | | |

All 58 have +0x70 = 1934 `INS_TXT_MUSEUM_ITEM_GENBTTN` (" for Info"), +0x7C range 3.0 (2.0 on two, 10.0
on t148_hbs), +0x90 = 2, +0xA0 = -1.0. Four have no volume (+0x74 = 0: t001_welcome, t148_hbs, t040_tiny,
t040_tiny1) and work by range alone. Five reference non-ITEM volumes (sec1_arrive, razorclaws_id_vol,
shotgunchuk_id_vol, phoenix_id_vol, item_082_vol).

How it works (proven, EBOOT):

- Vtable 0xC3D6F0: slot 0 fix-up 0x34CCE4 calls the Prompt component's fix-up 0x4DF274 on `this+0x18`, which
  fixes the handle at component +0x5C (= +0x74). Slot 5 Init 0x34CD10: `Ctrl.Init`, then `Prompt.Init(this+0x18)`.
- Prompt "hero in range" test 0x4DEDF0: in range = (range +0x64 > 0 and distance(hero, owner) < range, x1.5 while
  showing; 2D or 3D by byte +0x94) **OR** (volume +0x5C valid and hero inside it, 0x61A124). So the volume and the
  range both count. (ACrackInTime.fields' Prompt volume sentence "Used when the range is 0" is wrong.)
- Slot 7 Update 0x34CC3C: updates the prompt; when the prompt reports a press (component +0x70 = this+0x88), it
  clears it and calls 0x4A8064(title = +0xB0, body = +0xB4), which stores both ids in the global info-screen
  request 0xD374AC, then opens screen 0x10 (0x4A74CC) (screen meaning inferred).

Script-driven ITEM volumes: `ITEM_005_vol` and `ITEM_movie_vol` (both cylinders at (15.9, 1.8, 133.3)) are handled
by museum_galleries.lua `on_enter(...) popup_message(...)`. 18 ITEM volumes are also used by scripts for item
cameras (`ItemCam(camNN, ITEM_0NN_vol)`, `is_inside`, `on_exit`), in addition to their infocontroller.

**Shipped data error:** `t136_moonenemy` (at (12.5, 0, 55.6), the xenoid exhibit) references `ITEM_032_vol`
(at (12.5, 0, 73.4), the same volume as t032_timecleaner) instead of `ITEM_xenoid_vol` (at (12.5, 0, 55.6)). In
game its prompt still shows through its 3.0 range. `ITEM_xenoid_vol` is therefore an orphan.

Elsewhere: agorian_arena `InfoController1..4` (Hall of Glory plaques, `ABP_TXT_GPLAY_PLAQUE0N` " Learn
about ...", title `..._PLAQUE0NA`, body `ABP_DESC_GPLAY_PLAQUE0N`), tombli `ASTEROID_PLAQUE`.

## 5. AUD volumes: the sound classes

Audio-related classes (names, natives): controllers **SoundEnvironment 0x202** (348 game-wide) and **SoundEmitter
0x369** (707); Mobys / Clues with audio-ish names that do not own volumes: hsb_ambient 0x775,
cluehbsambientmovement 0x773, dialogue_fongoid_past 0x79E (controller, all-default settings). Natives:
`play_explicit_sound_spec`, `stop_sound`, `is_sound_done`, `sound_group_fade_internal`, `play_music` /
`request_music_track` / `stop_music`, `play_explicit_dialog_spec`, `random_dialogue_*`, `on_audio_end`, `reveal` /
`hide`. No script in the four levels names an AUD instance. Sound ids are the `SoundSpecs` enum of the global
script `/scripts/soundspecs.lua` (e.g. 10934 `AMB_MUSEUM_Bathroom`, 10936 `AMB_MUSEUM_Wind_Balconies`).

The volume link is a **reference list** (no name convention, no containment, no hash, no external sound table):

```
insomniac_museum AUD_ENV_Chatter (0x202) settings block:
  struct @0x30: +0x14 = 0x2AB9 (AMB_MUSEUM_Office) ... +0x28 = 0xB4 (block-relative pointer, block fix-up)
  extra  @0xB0: count 17, then 17 Volume references AUD_V_Chatter1 .. AUD_V_Chatter17
AUD_ENV_Bathroom: +0x28 -> [AUD_V_Bathroom]; +0x2C -> [{AUD_ENV_Chatter, mode 1, 0.1, 0.2}]
AUD_E_Picture_Music (0x369): +0x14 = 10893 AMB_MUSEUM_Eight_Bit_Muswic; +0x18 -> [{1, AUD_ENV_Music_Trigger_DKO}]
AUD_ENV_Music_Trigger_DKO: no volumes; its own controller box (scale 8.8 x 4.3 x 11.4) is the zone
```

**List format (proven, fix-up methods 0x4EC9D0 / 0x4EC384 / dropship 0x45F79C):** the struct field holds a
block-relative pointer (listed in the settings block's own fix-up table, relocated by the block base at load);
the u32 **count is the word just before the pointed-to address**; elements follow. The class's fix-up loops
`count` elements and runs the reference fix-up (0x6134F4) on the reference word(s) of each element.

### SoundEnvironment 0x202 (proven unless marked)

- Fix-up 0x4EC9D0: `+0x28` list of 4-byte Volume references; `+0x2C` list of 16-byte entries whose word 0 is a
  reference (another SoundEnvironment, 172 of 175).
- Init 0x4ED1C4: builds its own box +0x50..+0x7C from the controller placement (0x62C798); +0x24 = -1 (sound
  handle); if +0x38 != 0 builds a reverb zone (0x4ECF1C) from its volumes (shape-aware: 0x629584 / 0x626EB4) and
  +0x44 / +0x48 / +0x4C (handle kept at +0x40); then sets +0xC = 100000.
- Listener-inside test 0x4ECAA4: inside any +0x28 volume (0x920D60); with an empty list, inside its own box
  (inferred from the branch and from the volume-less instances, which all have a non-unit scale).
- Update 0x4ECC14: when the listener enters and +0x14 != 0, plays sound +0x14 (0x95ECB0, handle -> +0x24) and
  fades it from 0 to 1 over +0x18 seconds (0x684B90); on leaving, stops it with a +0x1C seconds fade (0x685514).
  While inside, for each +0x2C entry whose target is playing: mode 1 -> 0x684AB0(target sound, 2, value +8, time
  +0xC), mode 2 -> parameter 3, mode 0 -> nothing; on leaving, the parameter goes back to 1.0 over +0xC
  (0x4EC7F4). Game data: mode 0 on 109 entries (inert), mode 1 on 66 (value 0.0 .. 0.42, time 0.1 .. 2), mode 2 never.
  Reading mode 1 as "duck the other environment's volume" is inferred (parameter 2 not traced).

### SoundEmitter 0x369 (proven unless marked)

- Fix-up 0x4EC384: `+0x18` list of 8-byte entries {u32 condition, reference}; the reference (word 1) is fixed.
  514 entries name a SoundEnvironment, 12 nothing; condition 1 on 519, 0 on 7.
- Init 0x4EC314: +0x1C = 0, +0x20 / +0x24 = -1. Update 0x4EC408: for each entry, asks the environment whether the
  listener is inside (0x4EC734); condition 1 = play while inside, 0 = while outside (inferred from the two
  branches), fading in / out with **the environment's** +0x18 / +0x1C times. The sound (+0x14, SoundSpecs id) plays
  at the controller's own position.
- The existing ACrackInTime.fields rows are wrong: `[0x4, ref]` is the reference inside a list entry, and
  `[0x18, u32] Volume` is the list pointer (its "value" 100 is the block offset 0x64; editing it would corrupt the
  block).

## 6. Hidden links game-wide (`census.py`)

Every reference that ReLunacy does not show today, all 22 levels (count = reference words):

| Count | Owner | Field | Targets | Levels |
| --- | --- | --- | --- | --- |
| 1,907 | Area | path list (0x25080 +0, 0x25088) | Path | 19 |
| 645 + 645 | Controller CarSpawner 0x35E | +0x14 / +0x18 | Path / Pod | 18 |
| 514 | Controller SoundEmitter 0x369 | +0x18 list | Controller (SoundEnvironment) | 20 |
| 398 | Controller SoundEnvironment 0x202 | +0x28 list | **Volume** | 16 |
| 311 | Controller EnemyFirepointController 0x173 | +0x14 | Area | 13 |
| 225 | Moby nefarious_dropship 0x6E0 | +0x1218 list (0x28-byte action entries) | Path 118, **Volume 50**, Pod 57 | 5 |
| 174 | Controller SoundEnvironment 0x202 | +0x2C list | Controller | 16 |
| 104 | Controller FxController 0x3D0 | +0x54 | **Volume** | 3 |
| 54 | Controller infocontroller 0x8A9 | +0x74 | **Volume** | 1 (the museum; arena / tombli ones have none) |
| 28 | Controller VerticalFogController 0x472 | +0x14 | **Volume** | 9 |
| 13 | Controller timeslowcontroller 0x7F8 | +0x18 | **Volume** | 6 |
| 5 | Moby PhysicsObject 0x3E7 | +0x5C list (0x48-byte joint entries: +0 Moby, +4 Volume) | **Volume** (+ Moby) | 1 |
| 5 / 4 / 4 / 2 / 2 | Controller zapper 0x713 +0x14, CameraSwayController 0x2D0 +0x34, SpaceAsteroidField 0x6E3 +0x30, EnemyGroupController 0x371 +0x9C, WeatherController 0x3EF +0x60 | | Pod / Area / Pod / Pod / Area | |

Plus everything that references an **Area** (enemy +0x814 home area, +0x884 firepoints, ClueGrindRail +0x18,
ClueHudMap +0x34.. +0x19C, the controllers above): ReLunacy lists a volume's Areas by name but never who uses
that Area. (Moby targets, kind 0, are excluded from the scan to avoid false positives on small integers.)

## 7. Proposed ACrackInTime.fields entries

Paste-ready (existing syntax). Confidence in the comment above each row: P = proven in code, D = inferred from
data, I = inferred from code shape. A list field is declared `u32, runtime` here because the file has no list
kind (see 8): its value is a block offset and must never be edited as a number.

```
class 0x8A9 infocontroller : 0xCF
{
    # P: Prompt.Init / fix-up / update on this+0x18
    [0x18, component(0x98)] Prompt = "The prompt shown near the exhibit.";
    # P (prompt +0x58)
    [0x70, text] Prompt text = "Text of the prompt.";
    # P: fix-up 0x4DF274, range test 0x4DEDF0
    [0x74, Volume] Prompt volume = "The prompt also shows while the hero is inside.";
    # P
    [0x7C, float] Prompt range = "The prompt shows within this distance.";
    # D: 2 on all 63
    [0x90, u32] Prompt value A = "";
    # D: -1.0 on all 63
    [0xA0, float] Prompt value B = "";
    # P: byte +0x94 of the prompt
    [0xAC, u8, runtime] Prompt 3D distance = "";
    [0xAD, u8, runtime] Prompt enabled = "";
    [0xAE, u8, runtime] Prompt showing = "";
    [0xAF, u8, runtime] Prompt button state = "";
    # P: passed to the info screen on a press
    [0xB0, text] Title = "Title of the info screen.";
    [0xB4, text] Body = "Text of the info screen.";
}

class 0x202 SoundEnvironment : 0xCF
{
    # P (0 on 230 of 348: no sound, reverb / ducking only)
    [0x14, u32] Sound = "Sound played while the listener is inside (SoundSpecs id).";
    # P
    [0x18, float] Fade in = "Seconds.";
    [0x1C, float] Fade out = "Seconds.";
    # I: -1 in all files, tested before playing
    [0x20, s32, runtime] Trigger state = "";
    # P
    [0x24, s32, runtime] Sound handle = "";
    # P: fix-up 0x4EC9D0; list of Volume references, count in the word before
    [0x28, u32, runtime] Volumes = "The volumes of the environment. None: its own box.";
    # P: list of 16-byte entries {Controller, mode, value, time}
    [0x2C, u32, runtime] Linked environments = "Environments changed while inside this one.";
    # P
    [0x30, u32, runtime] Listener inside = "";
    [0x34, u32, runtime] Linked changed = "";
    # P (Init)
    [0x38, bool32] Reverb = "Builds a reverb zone from the volumes.";
    [0x40, s32, runtime] Reverb zone = "";
    # I: passed with the zone to 0x6814C0; 10 on 277
    [0x44, u32] Reverb preset = "";
    # D: 1.0 on all
    [0x48, float] Reverb value A = "";
    [0x4C, float] Reverb value B = "";
    # P: built by Init from the placement
    [0x50, component(0x30), runtime] Box = "";
}

class 0x369 SoundEmitter : 0xCF
{
    # P
    [0x14, u32] Sound = "Sound played at the controller (SoundSpecs id).";
    # P: fix-up 0x4EC384; list of 8-byte entries {condition, SoundEnvironment}
    [0x18, u32, runtime] Environments = "Plays according to these environments.";
    # P (Init)
    [0x1C, u32, runtime] Playing = "";
    [0x20, s32, runtime] State = "";
    [0x24, s32, runtime] Sound handle = "";
}

class 0x472 VerticalFogController : 0xCF
{
    # P: Init 0x349D14 reads the volume's box
    [0x14, Volume] Fog volume = "The volume the fog fills.";
    # P/D: three bytes scaled by 1/255 (00FFE100 on 11 of 30)
    [0x18, u8] Fog red = "";
    [0x19, u8] Fog green = "";
    [0x1A, u8] Fog blue = "";
    # P written by Init from the volume; names I
    [0x1C, float, runtime] Fog density = "";
    [0x20, float, runtime] Fog height = "";
}

class 0x7F8 timeslowcontroller : 0xCF
{
    # P: Update passes it to 0x48B35C (the add_timeslow_volume path)
    [0x18, Volume] Time slow volume = "Time slows inside.";
    # P
    [0x1C, u32, runtime] Time slow handle = "";
    # I: read by Init (1 on 8 of 13)
    [0x20, u16] Start active = "";
    [0x22, u16] Time slow flags = "";
    # P: passed with the volume (1.0 / 0.25 / 0.1)
    [0x24, float] Time scale = "";
    # P: screen tint distances; 2048 = off (Update skips >= 2000)
    [0x28, float] Tint near = "";
    [0x2C, float] Tint far = "";
    # D: same on all 13 (0.01, 1.15, 1.6, 0.6)
    [0x30, float] Tint value A = "";
    [0x34, float] Tint value B = "";
    [0x38, float] Tint value C = "";
    [0x3C, float] Tint value D = "";
}

class 0x173 EnemyFirepointController : 0xCF
{
    # P: the only authored field (fix-up 0x97350); Init copies the Area's members into the list
    [0x14, Area] Firepoints = "Area of the firepoints it hands out.";
    [0x618, u8, runtime] Firepoint count = "";
}

class 0x6E0 nefarious_dropship : 0x181
{
    # P: fix-up 0x45F79C; list of 0x28-byte actions (type 0 fly path +0xC, 2 drop: volume +8 / Pod +0x14, 5 leave path +0xC)
    [0x1218, u32, runtime] Actions = "Its flight and drop steps.";
    [0x121C, u32, runtime] Action count = "";
    [0x1220, u32, runtime] Action list = "";
}
```

Corrections to existing rows: SoundEmitter `[0x4, ref]` and `[0x18, u32] Volume` (see 5); the shared Prompt
component sentence "Used when the range is 0" on VendorBase 0x220 / 0x910 / 0x70 / PromptTeleporter 0x3A7
should read "also shows while the hero is inside" (range OR volume, 0x4DEDF0).

Classes looked at with nothing authored to name: EnemyBattlefieldController 0x171 (77 instances, every settings
word 0 game-wide) and dialogue_fongoid_past 0x79E (42, all 0). Not done: hangar_chunk 0x7A3 (118; an effect-like
block copied from +0x14 by 0x8FFEBC), cam_gcb_subcon_puzzle_cham_button 0x789 (a CamArg), electric_arc_master
0x73B, PhysicsObject's joint list (its fix-up is not in the class's vtable slot 0; likely a component).

Totals: infocontroller 15 rows, SoundEnvironment 18, SoundEmitter 5, VerticalFogController 6, timeslowcontroller
12, EnemyFirepointController 2, nefarious_dropship 3.

## 8. What the editor should show (implementation list)

1. **Every controller's references, not only cameras'.** `GameplayGroupReader.ReadControllers` already decodes
   `References` for all controllers; `PropertyInspectorFrame.RenderUsedBy` only walks `region.Cameras`. Keep the
   non-camera controllers somewhere in the region (they are not entities today) and include them in "Used by".
   This alone covers infocontroller +0x74, FxController +0x54, VerticalFogController +0x14, timeslowcontroller
   +0x18 (and the Area / Pod / Path users).
2. **References in settings lists.** In `ReadSettings`, for an inner IGHW block: read the block's fix-up table
   (after the `PADn` words: u32 count + block-relative offsets). Each fix-up located inside the 0x2501C struct is
   a pointer field; its value is a block offset into an extra-data section (the 0xFFFFFFFF entries); the element
   count is the u32 just before it; elements = (section end - pointer) / count bytes. Decode every element word
   as a reference. Report it as `+0x28[i]` (owner field, element). Known lists: SoundEnvironment +0x28 (Volume,
   4 B), +0x2C (16 B: ref, mode, value, time), SoundEmitter +0x18 (8 B: condition, ref), nefarious_dropship +0x1218
   (0x28 B), PhysicsObject +0x5C (0x48 B). Writing such a list (add / remove a volume) means growing the block
   and its fix-ups; editing an element in place is safe.
3. **Who uses an Area / a Scent.** When a volume is in an Area, show the Area's users (enemy +0x814 / +0x884,
   EnemyFirepointController +0x14, ClueGrindRail +0x18, ClueHudMap...). Same for Scents (Moby +0x890).
4. **Area path lists** (0x25080 +0, u16 count at +8; 1,907 references): still unread.
5. **Script users.** Parse instances.lua (`name = kind_handle:new(layer, index)`, last assignment wins) and grep the
   level's `.lua` scripts for the names; show file:line and the native (`on_enter`, `is_inside`, `warp`,
   `activate` ...). This is the biggest route for what remains (f: 101 .. 533 volumes per level).
6. Optional: draw SoundEnvironments with no volume list as their own box (controller position / rotation / scale).

## 9. Not determined

- Purpose of the `moon_apNN_vol` family (22 per moon, everywhere) and of the other orphans listed in 3.
- SoundEnvironment +0x44 / +0x48 / +0x4C exact meaning (reverb preset id assumed), +0x20; the sound parameter
  indices 2 / 3 used by linked-environment modes 1 / 2 (volume duck assumed for 1).
- infocontroller / Prompt value A (+0x90 = 2) and value B (+0xA0 = -1.0).
- The info screen 0x10 and the reader of 0xD374AC (not traced).
- timeslowcontroller +0x22 and the four tint constants.
- PhysicsObject joint list fix-up location and its element fields beyond +0 / +4.
- The 7 game-wide AUD orphans (scripts of those levels not searched).
