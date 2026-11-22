# Tools of Destruction and Quest for Booty: instance classes and their settings

Old-engine (PS3, ToD NPUA80965, QfB NPEA00088) placed classes: Mobys (gameplay.dat 0x7340), Controllers (0x77C0) and Clues (0x7780).
Nothing here is tested in game. Resources: `ReLunacy.Engine/Resources/Classes/{ToolsOfDestruction,QuestForBooty}.{fields,classes}`
(the inspector's format, same as ACrackInTime.fields). Tooling: `dev/tools/toddebug/` (tod.py reader, mine.py statistics, classify.py, gen.py, doc.py).

## Method

1. **Class names (PROVEN).** Each game's lookup-enum debug file holds kVTGenEnums, a u32 per class number giving the file offset of the class name
   (ToD `built/game/game.dat`, QfB `built/game/debug.dat`, section 0x25600 row 1, string pool 0x25680). Parsed by `classnames.py`: 1588 ToD names, identical
   to the 1588 of the Resurgence header; 1410 QfB names. The EBOOT carries no class-name table (only 43 `_CLASS` strings used by Lua natives).
2. **Settings blob.** A Moby/Controller record points to a mini-IGHW (v0.2) whose section 0x25200 payload is the class data: u32 class number at +0, fields after.
   A Clue points to a raw payload (record +4, size +8, class +0xC; fields from +0xC). Payload size is constant per class (0 of 254 ToD Moby classes vary).
   Records with a null pointer (453 ToD Mobys, 30 Controllers) take the canonical library defaults (0x25300/0x25340/0x25380/0x253C0) and are not mined.
3. **Instance names.** ToD: `dev/luacorpus/tod/lua/built_levels_<level>_instances.lua` (name, family, index, guid for every instance of all 20 levels; matches
   debug.dat 1:1). QfB: each level's `built/levels/<level>/debug.dat` (rows of 0x18: guid, absolute file offset of the name; sections 0x73C0 Mobys, 0x7720 Paths,
   0x7760 Volumes, 0x77A0 Clues, 0x77D0 Controllers, 0x7920 Pods, 0x7960 Areas).
4. **Statistics over all instances of a class** (`mine.py`), per 4-byte word: constant / varies, float-like, small int, sentinel 0xFFFFFFFF, per-byte structure, plus difference from the class's canonical default blob.
5. **References.** For each word and each family, test that every non-sentinel value is a valid index in its own level (Path, Volume, Area, Pod, Clue, Scent, Controller: raw u32 index, 0xFFFFFFFF = none;
   Moby: packed `((n+1)<<16)|n`, proven structurally, see TOD_Global_Instance_Reference_Map in Resurgence). Then score:
   geometry (median distance from the instance to the referenced target against a random target of the same level: ratio <= 0.35 over >= 3 samples, e.g. an elevator's
   volume sitting exactly at its Moby), instance-to-target name word overlap, spread (>= 3 distinct values, max >= 8). Pod/Area geometry is the centroid of members.
6. **Corroboration.** (a) Resurgence's `ToolsOfDestruction.h` handle fields (EBOOT resolver dataflow + corpus): compared field by field. (b) Resurgence's `class_ref_candidates_table0graph.csv`:
   native resolver call sites reading a handle of family F at object offset N (class mostly unattributed): reported as `native site`.

Evidence labels. **STRONG**: valid + geometry or structure or names over >= 3 distinct values. **MEDIUM**: valid + weaker geometry/names, or one packed Moby handle. **GUESS**: physical type from values only.
**CONST**: identical in every instance (not emitted to .fields). No field is PROVEN in the sense of a class-attributed EBOOT read: that needs the class method map (vtable registry), which for ToD was not found (the ACiT registration-block trick does not transfer; open).

## Tools of Destruction: coverage

| | Mobys | Controllers | Clues |
|---|---:|---:|---:|
| placed classes | 254 | 31 | 24 |
| instances with class data | 17798 | 4760 | 415 |
| classes named | 254 | 31 | 24 |
| classes with >= 1 typed varying field | 169 | 23 | 19 |

Reference fields found: **474** (STRONG 374, MEDIUM 100). Native resolver site at the same (family, offset): 132.
Other varying scalar fields typed: MEDIUM 289 (floats/bools), GUESS 459 (u16/u32/s32).

Cross-check with Resurgence's 259 recovered handle fields: 235 agree, 1 disagree (ClueLedgeGrab+0x14 Resurgence Area / here Volume), the rest sit in classes with too few instances or none in the retail levels.
Not found by this method: single-instance or constant reference fields (a constant cannot show validity spread).

### Reference fields by class

| Class | Kind | n | Offset | Target | Label | Targets seen |
|---|---|---:|---|---|---|---|
| ClueLedgeGrab (0x36D) | clue | 40 | +0x10 | Path | STRONG | LedgeGrab6, LedgeGrab4 |
| ClueLedgeGrab (0x36D) | clue | 40 | +0x14 | Volume | MEDIUM | AUD_V_ZEVERB_STATION_HALLS7, C_Checkpoint_Return |
| ClueGrindRail (0x36B) | clue | 31 | +0x10 | Path | STRONG | curve1, curve9 |
| ClueQuicksand (0x370) | clue | 28 | +0x20 | Moby | STRONG | G03_DumpDoor, G01_DumpDoor |
| ClueHudMap (0x535) | clue | 19 | +0x10 | Area | MEDIUM | A7_Firepoints_1, Z_Target_Turret2 |
| ClueHudMap (0x535) | clue | 19 | +0x20 | Area | MEDIUM | layer1, Secret1 |
| ClueHudMap (0x535) | clue | 19 | +0x34 | Area | MEDIUM | A13_Firepoints_1, A13_Firepoints_1 |
| ClueHudMap (0x535) | clue | 19 | +0x38 | Area | MEDIUM | Secret2, Secret2 |
| ClueHudMap (0x535) | clue | 19 | +0x4C | Area | MEDIUM | A13_Firepoints_1, A13_Firepoints_1 |
| ClueHudMap (0x535) | clue | 19 | +0x5C | Area | MEDIUM N | A13_Firepoints_1, A13_Firepoints_1 |
| ClueHudMap (0x535) | clue | 19 | +0x60 | Area | MEDIUM | A13_Firepoints_1, A13_Firepoints_1 |
| ClueHudMap (0x535) | clue | 19 | +0x64 | Area | MEDIUM | A13_Firepoints_1, A13_Firepoints_1 |
| ClueHudMap (0x535) | clue | 19 | +0x74 | Area | MEDIUM | A13_Firepoints_1, A13_Firepoints_1 |
| ClueHudMap (0x535) | clue | 19 | +0x78 | Area | MEDIUM | A13_Firepoints_1, A13_Firepoints_1 |
| ClueHudMap (0x535) | clue | 19 | +0x7C | Area | MEDIUM | A13_Firepoints_1, A13_Firepoints_1 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x10 | Volume | STRONG | AUD_V_ZEVERB_STATION, B04_Inner_Area_SwamerPod1 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x14 | Volume | STRONG | Cannon_TriggerB, volume54 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x1C | Moby | STRONG | A01_Shuttle4, G03_swarmer4 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x20 | Moby | STRONG | A01_Shuttle3, G02_Swa4 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x24 | Moby | STRONG | A29_Swa5, entity362 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x28 | Moby | STRONG | A28_AntSwrmer_Ast6_2, G05_Door1 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x2C | Moby | STRONG | A26_AntSwrmer_Ast5_4, G05_Door2 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x30 | Moby | STRONG | A18_AntSwrmer_Ast4_1, entity350 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x34 | Moby | STRONG | A8_AntSwrmer_Ast2_1, G05_rotater_pipe |
| ClueGalacticMap (0x43B) | clue | 19 | +0x38 | Moby | STRONG | A11_AntSwrmer_Ast2_1, G03_rotater_pipe |
| ClueGalacticMap (0x43B) | clue | 19 | +0x3C | Moby | STRONG | A27_AntSwrmer_Ast6_2, G02_door1 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x40 | Moby | STRONG N | A29_Swa3, G01_door1 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x44 | Moby | STRONG | GelPump1, G05_swarmer5 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x48 | Moby | STRONG | A11_AntSwrmer_Ast2_2, entity307 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x4C | Moby | STRONG | A6_AntSwrm_Ast1_2, entity381 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x50 | Moby | STRONG | A17_AntSwrmer_Ast4_2, entity259 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x54 | Moby | STRONG | A17_AntSwrmer_Ast4_3, G03_Gello |
| ClueGalacticMap (0x43B) | clue | 19 | +0x58 | Moby | STRONG | A18_AntSwrmer_Ast4_3, entity385 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x5C | Moby | STRONG | A26_AntSwrmer_Ast5_1, entity224 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x60 | Moby | STRONG | A29_Swa5, entity362 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x68 | Moby | STRONG | A2_AntSwrm_Ast1_2, G05_Gello |
| ClueGalacticMap (0x43B) | clue | 19 | +0x6C | Moby | STRONG | A01_Shuttle2, G05_swarmer3 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x70 | Moby | STRONG | A29_Swa4, G02_Swa2 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x74 | Moby | STRONG | A28_AntSwrmer_Ast6_3, entity234 |
| ClueGalacticMap (0x43B) | clue | 19 | +0x78 | Moby | STRONG | gmap_metropolis, gmap_metropolis |
| ClueGalacticMap (0x43B) | clue | 19 | +0x7C | Moby | STRONG | gmap_cobalia, gmap_cobalia |
| ClueGalacticMap (0x43B) | clue | 19 | +0x80 | Moby | STRONG N | gmap_stratus, gmap_stratus |
| ClueGalacticMap (0x43B) | clue | 19 | +0x84 | Moby | STRONG | gmap_fastoon, gmap_fastoon |
| ClueGalacticMap (0x43B) | clue | 19 | +0x88 | Moby | STRONG N | gmap_sci, gmap_sci |
| ClueGalacticMap (0x43B) | clue | 19 | +0x8C | Moby | STRONG | gmap_iff, gmap_iff |
| ClueGalacticMap (0x43B) | clue | 19 | +0x90 | Moby | STRONG | gmap_apogee, gmap_apogee |
| ClueGalacticMap (0x43B) | clue | 19 | +0x94 | Moby | STRONG | gmap_pirate, gmap_pirate |
| ClueGalacticMap (0x43B) | clue | 19 | +0x98 | Moby | STRONG | gmap_scii, gmap_scii |
| ClueGalacticMap (0x43B) | clue | 19 | +0x9C | Moby | STRONG | gmap_rykan, gmap_rykan |
| ClueGalacticMap (0x43B) | clue | 19 | +0xA0 | Moby | STRONG | gmap_sargasso, gmap_sargasso |
| ClueGalacticMap (0x43B) | clue | 19 | +0xA4 | Moby | STRONG | gmap_iris, gmap_iris |
| ClueGalacticMap (0x43B) | clue | 19 | +0xA8 | Moby | STRONG | gmap_zordoom, gmap_zordoom |
| ClueGalacticMap (0x43B) | clue | 19 | +0xAC | Moby | STRONG | gmap_sciii, gmap_sciii |
| ClueGalacticMap (0x43B) | clue | 19 | +0xB0 | Moby | STRONG | gmap_kerchu, gmap_kerchu |
| ClueGalacticMap (0x43B) | clue | 19 | +0xB4 | Moby | STRONG | gmap_slags, gmap_slags |
| ClueGalacticMap (0x43B) | clue | 19 | +0xB8 | Moby | STRONG | gmap_cragmite, gmap_cragmite |
| ClueGalacticMap (0x43B) | clue | 19 | +0xBC | Moby | STRONG | gmap_meridian, gmap_meridian |
| ClueGalacticMap (0x43B) | clue | 19 | +0xC0 | Moby | STRONG | gmap_fastoon, gmap_fastoon |
| ClueWind (0x36C) | clue | 18 | +0x18 | Path | STRONG | path_WindPlatform_2_Part_1, path_WindPlatform_2_Part_2 |
| ClueWind (0x36C) | clue | 18 | +0x1C | Moby | STRONG | moby_Saw_3, moby_Saw_1 |
| ClueZipLine (0x36A) | clue | 15 | +0x10 | Path | STRONG | T02_Zipline01, seg1_zipline_path |
| ClueCuttingLaser (0x36F) | clue | 8 | +0x10 | Volume | STRONG | C_GeoLaser_Warning_Volume, a3_geolaser_warning |
| ClueCuttingLaser (0x36F) | clue | 8 | +0x14 | Volume | STRONG | C_GeoLaser_Engage_Volume, a3_geolaser_engage |
| ClueCuttingLaser (0x36F) | clue | 8 | +0x18 | Moby | STRONG | A01_Smuggler, xbreakable_crate75 |
| ClueCuttingLaser (0x36F) | clue | 8 | +0x1C | Path | STRONG | C_GeoLaser_Path, a3_geolaser_curve |
| ClueCuttingLaser (0x36F) | clue | 8 | +0x20 | Moby | STRONG | igRefNodeMoby65, a3_geolaser_wall |
| ClueSpaceTeleporter (0x3AB) | clue | 7 | +0x10 | Moby | STRONG | A5_ast5_cannon_1, A1_ast1_cannon_1 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x10 | Moby | MEDIUM | gmp_1_in_POS |
| ClueLevelTransition (0x61F) | clue | 1 | +0x14 | Moby | MEDIUM | lt_background_01_in_POS |
| ClueLevelTransition (0x61F) | clue | 1 | +0x18 | Moby | MEDIUM | gmp_1 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x1C | Moby | MEDIUM | gmp_1 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x20 | Moby | MEDIUM | gmp_2 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x24 | Moby | MEDIUM | gmp_3 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x28 | Moby | MEDIUM | gmp_4 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x2C | Moby | MEDIUM | gmp_5 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x30 | Moby | MEDIUM | gmp_6 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x34 | Moby | MEDIUM | gmp_7 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x38 | Moby | MEDIUM | gmp_8 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x3C | Moby | MEDIUM | gmp_9 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x40 | Moby | MEDIUM N | gmp_10 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x44 | Moby | MEDIUM | gmp_11 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x4C | Moby | MEDIUM | gmp_13 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x50 | Moby | MEDIUM | gmp_14 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x54 | Moby | MEDIUM | gmp_15 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x58 | Moby | MEDIUM | gmp_16 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x5C | Moby | MEDIUM | gmp_17 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x60 | Moby | MEDIUM | gmp_3 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x64 | Moby | MEDIUM | lt_background_01 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x68 | Moby | MEDIUM | lt_background_01 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x6C | Moby | MEDIUM | lt_background_02 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x70 | Moby | MEDIUM | lt_background_03 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x74 | Moby | MEDIUM | lt_background_04 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x78 | Moby | MEDIUM | lt_background_05 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x7C | Moby | MEDIUM | lt_background_06 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x80 | Moby | MEDIUM N | lt_background_07 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x84 | Moby | MEDIUM | lt_background_08 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x88 | Moby | MEDIUM N | lt_background_09 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x8C | Moby | MEDIUM | lt_background_10 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x90 | Moby | MEDIUM | lt_background_11 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x94 | Moby | MEDIUM | lt_background_12 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x98 | Moby | MEDIUM | lt_background_13 |
| ClueLevelTransition (0x61F) | clue | 1 | +0x9C | Moby | MEDIUM | lt_background_14 |
| ClueLevelTransition (0x61F) | clue | 1 | +0xA0 | Moby | MEDIUM | lt_background_15 |
| ClueLevelTransition (0x61F) | clue | 1 | +0xA4 | Moby | MEDIUM | lt_background_16 |
| ClueLevelTransition (0x61F) | clue | 1 | +0xA8 | Moby | MEDIUM | lt_background_17 |
| ClueLevelTransition (0x61F) | clue | 1 | +0xAC | Moby | MEDIUM | lt_background_03 |
| FxController (0x180) | controller | 2074 | +0x54 | Volume | STRONG | FX_steam_gelArea2, FX_steam_gelArea3 |
| FxController (0x180) | controller | 2074 | +0x6C | Volume | STRONG N | ferris_damage_vol3, ferris_damage_vol1 |
| CamArgFixed (0x4C) | controller | 600 | +0x50 | Volume | STRONG N | A_Ship_Part_Forward_1, tals_ship_intro_camera2_forward |
| CarSpawner (0x233) | controller | 546 | +0x14 | Path | STRONG | A09path01, A12path02 |
| CarSpawner (0x233) | controller | 546 | +0x18 | Moby | MEDIUM | igRefNodeMoby31 |
| CarSpawner (0x233) | controller | 546 | +0x160 | Pod | MEDIUM | pterodactyl_layer, pterodactyl_layer |
| SoundEnvironment (0xBD) | controller | 544 | +0x24 | Volume | MEDIUM | volume9, volume9 |
| SoundEnvironment (0xBD) | controller | 544 | +0x38 | Controller | MEDIUM | controller59, seadust1 |
| CamArgIndexedPath (0x190) | controller | 112 | +0x50 | Volume | STRONG N | Checkpoint02_Pos, Checkpoint02_Pos |
| CamArgIndexedPath (0x190) | controller | 112 | +0x5C | Path | STRONG N | A6_Index_Path, A4_Cam_Cam |
| CamArgIndexedPath (0x190) | controller | 112 | +0x60 | Path | STRONG N | A6_Lookat_Path, A4_Cam_Lookat |
| CamArgIndexedPath (0x190) | controller | 112 | +0x64 | Path | STRONG N | A6_Camera_Path, A4_Cam_Cam |
| CamArgFollowPath (0x4B) | controller | 39 | +0x50 | Volume | STRONG N | G05_Camera_End_Facing, seg2_endzone_camera2_exitfacing |
| CamArgFollowPath (0x4B) | controller | 39 | +0x5C | Path | STRONG N | A01_Shuttle3_CamPath, A01_Shuttle4_CamPath |
| CamArgFollowPath (0x4B) | controller | 39 | +0x60 | Path | STRONG N | B12_Cam2_Lookat, C01_Lookat_Camera1 |
| CamArgFollowPath (0x4B) | controller | 39 | +0x64 | Volume | STRONG N | d1_zorthan_cine_camlook_2, d1_to_video_screen_look_at |
| CamArgFollowPath (0x4B) | controller | 39 | +0x68 | Moby | STRONG | A01_Shuttle3, A01_Shuttle4 |
| CamArgFollowPath (0x4B) | controller | 39 | +0x88 | Moby | STRONG N | entity173, entity170 |
| CamArgFixedFollow (0x336) | controller | 31 | +0x60 | Moby | STRONG | Aphelion, Aphelion |
| EnemyFirepointController (0x517) | controller | 18 | +0x14 | Area | STRONG | a1_firespots_norwes_1, a1_firespots_eas_1 |
| StalactiteSpawner (0x563) | controller | 12 | +0x14 | Area | STRONG | c3_area_background_stalactites_fall_2, c5_area_background_stalactites_fall_3 |
| EnemyGroupControllerMelee (0x32F) | controller | 10 | +0x9C | Pod | STRONG | EG_courtyardmelee, EG_buildingmelee |
| VerticalFogController (0x55C) | controller | 8 | +0x14 | Volume | STRONG | VOL_Cinematic_Fog, volume1 |
| MobySpawner (0xEC) | controller | 4 | +0x14 | Moby | STRONG | moby_Template_PlatformSpawner_2, moby_Template_PlatformSpawner_1 |
| EndSequenceController (0x610) | controller | 1 | +0x6C | Moby | MEDIUM | C_arena_tachyon |
| EndSequenceController (0x610) | controller | 1 | +0x70 | Moby | MEDIUM | C_dimx_tachyon |
| EndSequenceController (0x610) | controller | 1 | +0x78 | Moby | MEDIUM | A04_seg04_apogee_meteorite_x_044 |
| PathFollower (0xF7) | moby | 764 | +0xB0 | Path | STRONG N | meteorite_pathB02, meteorite_pathB04 |
| EnemyBurrowerBeetle (0x275) | moby | 311 | +0x4D0 | Volume | STRONG | EnemySeg_Minor_Innervol_1, EnemySeg_Minor_Innervol_1 |
| EnemyBurrowerBeetle (0x275) | moby | 311 | +0x4E0 | Volume | STRONG | EnemySeg_Minor_Outervol_1, EnemySeg_Minor_Outervol_1 |
| EnemyBurrowerBeetle (0x275) | moby | 311 | +0x4F8 | Path | STRONG N | EnemySegBeetle_Patrol_4, EnemySeg_Minor_Patrol_1 |
| EnemySecurityBot (0x2E8) | moby | 257 | +0x4D0 | Volume | STRONG | a4_swarmer_innervol, a1_swarmers_innervol |
| EnemySecurityBot (0x2E8) | moby | 257 | +0x4D4 | Area | STRONG | B05_Inner_Area, B21_Inner_Area |
| EnemySecurityBot (0x2E8) | moby | 257 | +0x4E0 | Volume | STRONG | a4_swarmer_outervol, a1_swarmers_outervol |
| EnemySecurityBot (0x2E8) | moby | 257 | +0x4E4 | Area | STRONG | B05_Outer_Area, B21_24_Outer_Area |
| EnemySecurityBot (0x2E8) | moby | 257 | +0x4F0 | Path | STRONG N | B05_PrePat_Swa2, B05_PrePat_Swa6 |
| EnemySecurityBot (0x2E8) | moby | 257 | +0x4F4 | Path | STRONG N | c10_swarmer1_patrol, c9a_trooper_intro_left |
| EnemySecurityBot (0x2E8) | moby | 257 | +0x4F8 | Path | STRONG N | a4_swarmer1_patrol, a1_swarmer1_patrol |
| EnemySecurityBot (0x2E8) | moby | 257 | +0x530 | Path | STRONG | a1_swarmer_pen, a1_swarmer_pen |
| EnemySecurityBot (0x2E8) | moby | 257 | +0x560 | Pod | STRONG | c15_swarmers_pod, c10_swarmers_pod |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x4D0 | Volume | STRONG | a1_battlefield_awareness, a1_battlefield_awareness |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x4D4 | Area | STRONG | B07_Inner_Area, B07_Inner_Area |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x4E0 | Volume | STRONG | a1_battlefield_awareness, a1_battlefield_awareness |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x4E4 | Area | STRONG | B07_Outer_Area, B02_03_Outer_Area |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x4F4 | Path | STRONG N | a1_norbaseleft_intropath_4, a1_norbaseleft_intropath_4 |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x4F8 | Path | STRONG N | seg1_bat1_group8_shootertrooper1_patrol, seg1_bat2_group5_jettrooper1_intro |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x504 | Area | STRONG N | a1_firespots_noreas_1, a1_firespots_norwes_1 |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x538 | Moby | STRONG | xbreakable_crate104, xbreakable_crate36 |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x53C | Moby | STRONG | xbreakable_crate61, xbreakable_crate61 |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x554 | Volume | STRONG | c7a_trigger_troopers, c7a_trigger_troopers |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x560 | Pod | STRONG | c14_troopers_pod, c15_troopers_pod |
| EnemyTachyonTrooperShooter (0x2E2) | moby | 256 | +0x604 | Volume | STRONG N | seg2_enemies_setup3_shootertrooper2_jump_vol, seg2_enemies_setup5_shootertrooper1_jump_vol |
| EnemySkinnyPirateMelee (0x1AD) | moby | 244 | +0x4D0 | Volume | STRONG | C_Innervol_1, C_Innervol_2 |
| EnemySkinnyPirateMelee (0x1AD) | moby | 244 | +0x4E0 | Volume | STRONG | C_Outervol_1, C_Outervol_2 |
| EnemySkinnyPirateMelee (0x1AD) | moby | 244 | +0x4F4 | Path | STRONG N | C_Intro_2, C_Intro_1 |
| EnemySkinnyPirateMelee (0x1AD) | moby | 244 | +0x4F8 | Path | STRONG N | C_Patrol_1, C_Patrol_2 |
| EnemySkinnyPirateMelee (0x1AD) | moby | 244 | +0x530 | Path | STRONG | BoundaryEdge_LargePirateGroup1, BoundaryEdge_LargePirateGroup1 |
| EnemySkinnyPirateMelee (0x1AD) | moby | 244 | +0x554 | Volume | MEDIUM | Campfire3_DamageVol1, Campfire3_DamageVol1 |
| EnemySkinnyPirateMelee (0x1AD) | moby | 244 | +0x560 | Pod | STRONG | Camp2_Pirates, Camp2_Pirates |
| EnemySkinnyPirateMelee (0x1AD) | moby | 244 | +0x6D4 | Volume | STRONG N | A_JumpVol1, A_JumpVol2 |
| EnemySkinnyPirateMelee (0x1AD) | moby | 244 | +0x974 | Moby | STRONG | A_Minor_Turret1, PirateTurret1 |
| MortarShot (0x22F) | moby | 232 | +0x80 | Volume | STRONG | mortar14_target, mortar21_target |
| MortarShot (0x22F) | moby | 232 | +0x84 | Moby | MEDIUM | Ratchet1 |
| FastPathFollower (0x203) | moby | 228 | +0x38 | Path | STRONG | K01_Path_Car4, K01_Path_Car8 |
| EnemyAntaraSwarmer (0x28F) | moby | 178 | +0x4D0 | Volume | STRONG | A28_InnerVol_AntSwrmer_Ast6, A27_InnerVol_AntSwrmer_Ast6 |
| EnemyAntaraSwarmer (0x28F) | moby | 178 | +0x4D4 | Area | STRONG | A29_Inner_Outer_SwaPod, A29_Inner_Outer_SwaPod |
| EnemyAntaraSwarmer (0x28F) | moby | 178 | +0x4E0 | Volume | STRONG | A28_OuterVol_AntSwrmer_Ast6, A27_OuterVol_AntSwrmer_Ast6 |
| EnemyAntaraSwarmer (0x28F) | moby | 178 | +0x4E4 | Area | STRONG | A29_Inner_Outer_SwaPod, A29_Inner_Outer_SwaPod |
| EnemyAntaraSwarmer (0x28F) | moby | 178 | +0x4F4 | Path | STRONG N | A5_IntroPath_AntSwrm_Ast1_1, A5_IntroPath_AntSwrm_Ast1_1 |
| EnemyAntaraSwarmer (0x28F) | moby | 178 | +0x4F8 | Path | STRONG N | A29_Patrol_Swa4, A29_Patrol_Swa3 |
| EnemyAntaraSwarmer (0x28F) | moby | 178 | +0x530 | Path | STRONG | A29_Swa_Bounds, A29_Swa_Bounds |
| EnemyAntaraSwarmer (0x28F) | moby | 178 | +0x560 | Pod | STRONG | B07_SwarmerPod1, B06_SwaPod1 |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x4D0 | Volume | STRONG | seg1_bat1_troopergroup1_inner, seg1_bat2_battlefield_alwaysaware_vol |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x4D4 | Area | STRONG | B12_13_Inner_AND_Outer_Area, B05_Inner_Area_Troopers |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x4E0 | Volume | STRONG | seg1_bat1_troopergroup1_outer, seg1_bat1_mixedgroup7a_outer |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x4E4 | Area | STRONG | B12_13_Inner_AND_Outer_Area, B05_Outer_Area |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x4F4 | Path | STRONG N | c9a_trooper_intro_left, c9a_trooper_intro_left |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x4F8 | Path | STRONG N | seg1_bat1_group1_meleetrooper2_patrol, seg1_bat1_group7a_meleetrooper1_patrol |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x530 | Path | STRONG | B21_Bounds, B05_Bounds |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x538 | Moby | MEDIUM | A03_seg47_apogee_meteorite_x_038, A03_seg47_apogee_meteorite_x_038 |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x53C | Moby | STRONG | A03_seg30_apogee_meteorite_x_038, A03_seg10_apogee_meteorite_x_027 |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x554 | Volume | STRONG | c7_trigger_trooper, c7_trigger_trooper |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x560 | Pod | STRONG | c11_secondtroopers_pod, c4_troopers_pod |
| EnemyTachyonTrooperMelee (0x2BD) | moby | 151 | +0x604 | Volume | STRONG N | seg2_enemies_setup4_shieldtrooper2_jump_vol, seg2_enemies_setup4_shieldtrooper1_jump_vol |
| GrindTrain (0x4B2) | moby | 146 | +0xB0 | Path | STRONG N | P02_Path_Train2, P02_Path_Train2 |
| GrindTrain (0x4B2) | moby | 146 | +0xF4 | Moby | STRONG | P02_Train2_8, P04_Train1_6 |
| DoorSlide (0x34B) | moby | 141 | +0x34 | Volume | STRONG | c3_npcdoor_trigger, A_PirateDoor2_OpenTrig |
| DoorSlide (0x34B) | moby | 141 | +0x60 | Volume | STRONG N | A_Lair_DoorPos_1, A_Lair_DoorPos_2 |
| EnemyCragmiteSwarmer (0x4FD) | moby | 115 | +0x4D0 | Volume | STRONG | B_inner_4, B_inner_1 |
| EnemyCragmiteSwarmer (0x4FD) | moby | 115 | +0x4E0 | Volume | STRONG | B_outer_4, B_outer_1 |
| EnemyCragmiteSwarmer (0x4FD) | moby | 115 | +0x4F4 | Path | STRONG N | B_intro_2, B_intro_2 |
| EnemyCragmiteSwarmer (0x4FD) | moby | 115 | +0x4F8 | Path | STRONG N | B_patrol_13, B_patrol_1 |
| EnemyCragmiteSwarmer (0x4FD) | moby | 115 | +0x530 | Path | STRONG | B_edge_3, B_edge_2 |
| EnemyCragmiteSwarmer (0x4FD) | moby | 115 | +0x560 | Pod | STRONG | seg1_bat1_cragpack2_pod, seg1_bat1_cragpack1_pod |
| EnemyCragmiteSwarmer (0x4FD) | moby | 115 | +0x850 | Area | STRONG | minor_seg_chew_area_1, moiz_temp_chew_spots_1 |
| EnemyCragmiteSwarmer (0x4FD) | moby | 115 | +0x854 | Volume | STRONG | B_chew_7, B_chew_1 |
| EnemySkinnyPirateShooter (0x1D3) | moby | 113 | +0x4D0 | Volume | STRONG | C_Innervol_4, C_Innervol_3 |
| EnemySkinnyPirateShooter (0x1D3) | moby | 113 | +0x4E0 | Volume | STRONG | C_Outervol_4, C_Outervol_3 |
| EnemySkinnyPirateShooter (0x1D3) | moby | 113 | +0x4F4 | Path | STRONG N | A_IntroPath_SecretAreaShooter2, A_IntroPath_SecretAreaShooter1 |
| EnemySkinnyPirateShooter (0x1D3) | moby | 113 | +0x4F8 | Path | STRONG N | C_Patrol_4, C_Patrol_6 |
| EnemySkinnyPirateShooter (0x1D3) | moby | 113 | +0x504 | Area | STRONG N | C_Firepoints_3, C_Firepoints_1 |
| EnemySkinnyPirateShooter (0x1D3) | moby | 113 | +0x554 | Volume | STRONG | A5_TriggerVol, A5_TriggerVol |
| EnemySkinnyPirateShooter (0x1D3) | moby | 113 | +0x560 | Pod | STRONG | A8_EnemyWave1, B_CampfirePirates |
| EnemySkinnyPirateShooter (0x1D3) | moby | 113 | +0x6D4 | Volume | STRONG N | volume22, A_PirateDoor3_DanceVolumeLeft |
| TriggerCommon (0x467) | moby | 112 | +0x3C | Moby | STRONG | B05_Section1b, B27_Door2 |
| EnemyConsumerBot (0x465) | moby | 102 | +0x4F8 | Path | STRONG N | Z07_Path, Z12_Path |
| EnemyConsumerBot (0x465) | moby | 102 | +0x7F8 | Area | STRONG N | Z07_Area, Z12_Area |
| HazardMovingLaser (0x3B1) | moby | 93 | +0x264 | Path | STRONG N | B5_path4, B5_path1 |
| EnemyTachyonGunship (0x334) | moby | 69 | +0x4D0 | Volume | MEDIUM | a1_battlefield_awareness, a1_battlefield_awareness |
| EnemyTachyonGunship (0x334) | moby | 69 | +0x4E0 | Volume | MEDIUM | a1_battlefield_awareness, a1_battlefield_awareness |
| EnemyTachyonGunship (0x334) | moby | 69 | +0x4F4 | Path | STRONG N | B17_Intro_Gunship1, B17_Intro_Gunship2 |
| EnemyTachyonGunship (0x334) | moby | 69 | +0x4F8 | Path | STRONG N | A5_patrol_gunships_1, A5_patrol_gunships_2 |
| EnemyTachyonGunship (0x334) | moby | 69 | +0x504 | Area | STRONG N | a1_firespots_gunships_norwes_2, a1_firespots_gunships_noreas_1 |
| EnemyTachyonGunship (0x334) | moby | 69 | +0x538 | Moby | STRONG | xbreakable_crate19, xbreakable_crate19 |
| EnemyCragmiteYoung (0x504) | moby | 69 | +0x4D0 | Volume | STRONG | c1_inner_vol, c1_inner_vol |
| EnemyCragmiteYoung (0x504) | moby | 69 | +0x4E0 | Volume | STRONG | c1_outer_vol, c1_outer_vol |
| EnemyCragmiteYoung (0x504) | moby | 69 | +0x4F4 | Path | STRONG N | c1b_particlepath_1, c1b_particlepath_2 |
| EnemyCragmiteYoung (0x504) | moby | 69 | +0x4F8 | Path | STRONG N | seg1_bat1_group5_crag_patrol, seg1_bat1_group7a_crag1_patrol |
| EnemyCragmiteYoung (0x504) | moby | 69 | +0x504 | Area | STRONG N | c2_firespot_1, c1b_firespot_1 |
| EnemyCragmiteYoung (0x504) | moby | 69 | +0x538 | Moby | STRONG | A04_seg11_apogee_meteorite_x_038, A04_seg11_apogee_meteorite_x_038 |
| EnemyCragmiteYoung (0x504) | moby | 69 | +0x53C | Moby | MEDIUM | A04_seg08_apogee_meteorite_x_022, A04_seg08_apogee_meteorite_x_022 |
| EnemyApogeeSpinner (0x365) | moby | 59 | +0x4D0 | Volume | STRONG | C6_Bot2_InnerVol, C4_Inner_Outer_Bots |
| EnemyApogeeSpinner (0x365) | moby | 59 | +0x4D4 | Area | STRONG | C10_Inner_Bot3, C10_Inner_Outer_Bot6 |
| EnemyApogeeSpinner (0x365) | moby | 59 | +0x4E0 | Volume | STRONG | C4_Inner_Outer_Bots, C4_Inner_Outer_Bots |
| EnemyApogeeSpinner (0x365) | moby | 59 | +0x4E4 | Area | STRONG | C10_UpperArea_Bot_Outer, C6_Bot2_Outer_Area |
| EnemyApogeeSpinner (0x365) | moby | 59 | +0x4F4 | Path | STRONG N | C6_Intro_BotPod1, C6_Intro_BotPod2 |
| EnemyApogeeSpinner (0x365) | moby | 59 | +0x4F8 | Path | STRONG N | C10_Patrol_3_5, C10_Patrol_6_1 |
| DoorAnim (0x34F) | moby | 56 | +0x34 | Volume | STRONG | G01_door1_open, B_IrisRoom_ClankDoorOpenTrigger |
| TachyonDropship (0x333) | moby | 54 | +0xD0 | Path | STRONG N | a1_dship_intro_norwes_1, a1_dship_intro_nor_1 |
| TachyonDropship (0x333) | moby | 54 | +0xDC | Volume | MEDIUM N | a1_norwes_dropspot_1, a1_nor_dropspot_1 |
| TachyonDropship (0x333) | moby | 54 | +0xE0 | Path | STRONG N | a1_dship_outro_norwes_1, a1_dship_outro_nor_1 |
| TachyonDropship (0x333) | moby | 54 | +0x208 | Pod | STRONG | a2a_pod_shooters_drop_norwes, a2c_pod_shooters_drop_nor |
| TachyonDropship (0x333) | moby | 54 | +0x20C | Area | STRONG N | B13_Dropship_Dropoff, B05_Drop_Area |
| TachyonDropship (0x333) | moby | 54 | +0x210 | Volume | STRONG | a1_landingspot_norwes_1_1, a1_landingspot_nor_1_1 |
| TachyonDropship (0x333) | moby | 54 | +0x214 | Volume | STRONG | a1_landingspot_norwes_1_2, a1_landingspot_nor_1_2 |
| TachyonDropship (0x333) | moby | 54 | +0x218 | Volume | STRONG | a1_landingspot_norwes_1_3, a1_landingspot_nor_1_3 |
| TachyonDropship (0x333) | moby | 54 | +0x21C | Volume | STRONG | seg1_bat2_east_dropzone_drop4, seg1_bat2_south_dropzone_drop4 |
| TachyonDropship (0x333) | moby | 54 | +0x220 | Volume | STRONG | seg1_bat2_east_dropzone_drop5, seg1_bat2_south_dropzone_drop5 |
| EnemyCaveBat (0x1E2) | moby | 52 | +0x4D0 | Volume | STRONG | InnerVolume_BG7, InnerVolume_BG4 |
| EnemyCaveBat (0x1E2) | moby | 52 | +0x4E0 | Volume | STRONG | OuterVolume_BG7, OuterVolume_BG3 |
| EnemyCaveBat (0x1E2) | moby | 52 | +0x4F4 | Path | STRONG N | curve5, curve4 |
| EnemyCaveBat (0x1E2) | moby | 52 | +0x4F8 | Path | STRONG N | PatrolPath_BG7_B4, PatrolPath_BG4_B3 |
| EnemyAntaraCentipede (0x363) | moby | 49 | +0x4D0 | Volume | STRONG | A21_InnerVol_Centi_Ast5, A8_InnerVol_Ast2 |
| EnemyAntaraCentipede (0x363) | moby | 49 | +0x4D4 | Area | STRONG | A23_Inner_Outer_Centis, A19_Inner_and_Outer_Areas |
| EnemyAntaraCentipede (0x363) | moby | 49 | +0x4E0 | Volume | STRONG | A21_OuterrVol_Centi_Ast5, A8_OuterVol_Ast2 |
| EnemyAntaraCentipede (0x363) | moby | 49 | +0x4E4 | Area | STRONG | A23_Inner_Outer_Centis, A23_Inner_Outer_Centis |
| EnemyAntaraCentipede (0x363) | moby | 49 | +0x4F4 | Path | STRONG N | A21_Intro_Cent1, A8_IntroPath1 |
| EnemyAntaraCentipede (0x363) | moby | 49 | +0x4F8 | Path | STRONG N | A13_Patrol_Centi2, A22_Patrol_Centi1 |
| EnemyAntaraCentipede (0x363) | moby | 49 | +0x504 | Area | STRONG N | A21_Firepoints, A8_Firepoints_1 |
| ShotTargetSwing (0x174) | moby | 48 | +0x48 | Moby | STRONG | dockmagnet3_6, dockmagnet3_5 |
| EnemySargassoWasp (0x27E) | moby | 47 | +0x4D0 | Volume | STRONG | CubeEnemySegMixed2Inner, CubeEnemySegMixed2Inner |
| EnemySargassoWasp (0x27E) | moby | 47 | +0x4E0 | Volume | STRONG | CubeEnemySegMixed2Outer, CubeEnemySegMixed2Outer |
| EnemySargassoWasp (0x27E) | moby | 47 | +0x4F4 | Path | STRONG N | PathEnemySegArena1Intro1, PathEnemySegArena1Intro2 |
| EnemySargassoWasp (0x27E) | moby | 47 | +0x4F8 | Path | STRONG N | Begin_Island_Patrol_Wasp_5, S_Island_Patrol_Wasp_2 |
| EnemySargassoWasp (0x27E) | moby | 47 | +0x504 | Area | STRONG N | AreaEnemySegMixed2FirePoints1, AreaEnemySegMixed2FirePoints2 |
| EnemyScavengerMelee (0x438) | moby | 46 | +0x4D0 | Volume | STRONG | B_Innervol_3, B_Innervol_4 |
| EnemyScavengerMelee (0x438) | moby | 46 | +0x4E0 | Volume | STRONG | B_Outervol_3, B_Outervol_4 |
| EnemyScavengerMelee (0x438) | moby | 46 | +0x4F4 | Path | STRONG N | B_intro_1, B_intro_2 |
| EnemyScavengerMelee (0x438) | moby | 46 | +0x4F8 | Path | STRONG N | B_patrol_7, B_patrol_3 |
| EnemyScavengerMelee (0x438) | moby | 46 | +0x560 | Pod | STRONG | A_Yard_Spawnergroup_1, A_Yard_Spawnergroup_3 |
| ShotTargetHang (0x394) | moby | 43 | +0x48 | Moby | STRONG | A4_sat_swingshot_1, P04_swingshotcar1_1 |
| WaterMine (0x329) | moby | 40 | +0x8C | Path | STRONG N | C5_Mine1_Path, C5_Mine3_Path |
| RotaterCranked (0x32B) | moby | 37 | +0x74 | Moby | STRONG | C4_Boltcrank, C4_Boltcrank |
| PadTri (0x313) | moby | 34 | +0x60 | Moby | STRONG | a2g_east_tripad_2, a2b_west_tripad_2 |
| PadTri (0x313) | moby | 34 | +0x64 | Moby | STRONG | a2g_east_tripad_3, a2b_west_tripad_3 |
| EnemyApogeePopupTurret (0x364) | moby | 32 | +0x4D0 | Volume | STRONG | C6_Turret1_InnerVol, C6_Inner_Turret2and3 |
| EnemyApogeePopupTurret (0x364) | moby | 32 | +0x4E0 | Volume | STRONG | C6_Turret1_OuterVol, C6_Outer_Turret2and3 |
| AllyZoni (0x495) | moby | 32 | +0x5F0 | Pod | STRONG | ZoniPod2, ZoniPod3 |
| PromptTeleporter (0x40D) | moby | 30 | +0x88 | Volume | STRONG N | C_Teleport_TalsRoom_Target, seg2_teleporter_start_area_vol |
| PromptTeleporter (0x40D) | moby | 30 | +0x8C | Area | MEDIUM | Z_Target_Turret5, machine_sparks |
| PickupGoldBolt (0x5C1) | moby | 29 | +0x9C | Moby | STRONG | igRefNodeMoby96, entity155 |
| PickupGoldBolt (0x5C1) | moby | 29 | +0xA0 | Volume | STRONG N | Goldbolt_Pos, T02_Goldbolt_Facing |
| HazardSteamVent (0x522) | moby | 26 | +0x30 | Pod | STRONG | Steam_Vent_Pod_4, Steam_Vent_Pod_2 |
| ZoniChallengeRing (0x510) | moby | 26 | +0x24 | Moby | STRONG | Glowing_Ring_10, Glowing_Ring_8 |
| EnemyHunchbackPirate (0xEA) | moby | 25 | +0x4D0 | Volume | STRONG | A7_InnerVol, A6_InnerVol_Hunchback |
| EnemyHunchbackPirate (0xEA) | moby | 25 | +0x4E0 | Volume | STRONG | A7_OuterVol, A6_OuterVol_Hunchback |
| EnemyHunchbackPirate (0xEA) | moby | 25 | +0x4F4 | Path | STRONG N | A8_IntroPath_Hunchback, B3_IntroPath_Hunchback |
| EnemyHunchbackPirate (0xEA) | moby | 25 | +0x4F8 | Path | STRONG N | A6_PatrolPath_Hunchback, A6_PatrolPath_Hunchback |
| EnemyHunchbackPirate (0xEA) | moby | 25 | +0x530 | Path | STRONG | Hunchback1_BoundPath, Boat2BoundArea |
| EnemyTachyonWalker (0x309) | moby | 22 | +0x4D0 | Volume | MEDIUM | courtyard_awareness, a1_battlefield_awareness |
| EnemyTachyonWalker (0x309) | moby | 22 | +0x4E0 | Volume | STRONG | courtyard_awareness, a1_battlefield_awareness |
| EnemyTachyonWalker (0x309) | moby | 22 | +0x4F4 | Path | STRONG N | a3c_courtyard_walker_intro, a1_norbaseleft_walkerpatrol_left_1 |
| EnemyTachyonWalker (0x309) | moby | 22 | +0x504 | Area | STRONG N | a3_firespots_courtyard_1, a3_firespots_bridge_1 |
| EnemyTachyonWalker (0x309) | moby | 22 | +0x538 | Moby | STRONG | xbreakable_crate108, xbreakable_crate92 |
| EnemyTachyonWalker (0x309) | moby | 22 | +0x80C | Volume | STRONG | a10_arcade_crash_source, a6_crashdown_source |
| EnemyTachyonWalker (0x309) | moby | 22 | +0x810 | Volume | STRONG | a10_arcade_crash_target, a6_crashdown_target |
| TachyonPlatform (0x5B5) | moby | 20 | +0x20 | Moby | MEDIUM | C_dimx_tachyon, C_dimx_tachyon |
| EnemySpaceShipBattleShip (0x509) | moby | 20 | +0x658 | Volume | STRONG N | large_ship_1_dest, capitalship_1_dest_volume |
| EnemyGelChomper (0x2AE) | moby | 19 | +0x4F8 | Path | STRONG N | G02_patrol_path_chomp2, G02_patrol_path_chomp1 |
| EnemyScavengerBomber (0x439) | moby | 19 | +0x4D0 | Volume | STRONG | B_Innervol_2, B_Innervol_2 |
| EnemyScavengerBomber (0x439) | moby | 19 | +0x4E0 | Volume | STRONG | B_Outervol_2, B_Outervol_2 |
| EnemyScavengerBomber (0x439) | moby | 19 | +0x4F8 | Path | STRONG N | B_patrol_5, B_patrol_4 |
| EnemyScavengerBomber (0x439) | moby | 19 | +0x504 | Area | STRONG N | B_Firepoints_2, B_Firepoints_1 |
| EnemyScavengerBomber (0x439) | moby | 19 | +0x53C | Moby | STRONG | fastoon_cave_kelp161, fastoon_cave_kelp82 |
| EnemyScavengerBomber (0x439) | moby | 19 | +0x560 | Pod | STRONG | A_Yard_Spawnergroup_2, A_Lair_Spawnergroup_1 |
| EnemyKerchuuFlameThrower (0x2DD) | moby | 19 | +0x4D0 | Volume | STRONG | InnerVolume_MinorSeg1_KerchuByBarge, A_KerchuFightingPirate_InnerVol |
| EnemyKerchuuFlameThrower (0x2DD) | moby | 19 | +0x4E0 | Volume | STRONG | OuterVolume_Seg1_BargeStartArea, A_KerchuFightingPirate_OuterVol |
| EnemyKerchuuFlameThrower (0x2DD) | moby | 19 | +0x4F8 | Path | STRONG N | PathEnemy2SegFlamerSolo1Patrol, PathEnemy2SegFlamers1Patrol1 |
| EnemyKerchuuFlameThrower (0x2DD) | moby | 19 | +0x504 | Area | STRONG N | FP_KerchuFlamer_MinorByBarge, FP_KerchuFightingPirate_G1 |
| PadRoboWing (0x2B0) | moby | 19 | +0x4C | Moby | STRONG | zoni_Challenge_1, zoni_Challenge_1 |
| PadFling (0x14C) | moby | 18 | +0x3C | Volume | STRONG | A17_Flingpad_Target, A1_flingpad_asteroid1_target |
| PadFling (0x14C) | moby | 18 | +0x40 | Path | STRONG N | A5_Index_Path, A1_Index_Path |
| EnemyCragmiteMature (0x515) | moby | 18 | +0x4D0 | Volume | STRONG | c5_inner_vol, seg1_bat3_battlefield_alwaysaware_vol |
| EnemyCragmiteMature (0x515) | moby | 18 | +0x4E0 | Volume | STRONG | seg1_bat2_mixedgroup2_outer, seg1_bat1_craggroup3_outer |
| EnemyCragmiteMature (0x515) | moby | 18 | +0x4F4 | Path | STRONG N | c5_particle_path_1, seg1_bat3_righttrock_intro2 |
| EnemyCragmiteMature (0x515) | moby | 18 | +0x504 | Area | STRONG N | c5_firespot_2, seg1_bat2_group2_bigcrag1_firepoints |
| EnemyCragmiteMature (0x515) | moby | 18 | +0x538 | Moby | STRONG | A04_seg11_apogee_meteorite_x_038, A04_seg11_apogee_meteorite_x_038 |
| EnemyCragmiteMature (0x515) | moby | 18 | +0x53C | Moby | MEDIUM | A04_seg08_apogee_meteorite_x_022, A04_seg08_apogee_meteorite_x_022 |
| EnemyStatue (0x3BD) | moby | 17 | +0x4D0 | Volume | STRONG | a1_trapstatue_inner, c13_innervol_trapstatues |
| EnemyStatue (0x3BD) | moby | 17 | +0x4E0 | Volume | STRONG | a1_trapstatue_outer, c13_outervol_trapstatues |
| PlatformMoving (0x338) | moby | 16 | +0x80 | Volume | STRONG | GondolaTaxiStartTrigger, MovingPlatform01_GotoStartTrigger |
| PlatformMoving (0x338) | moby | 16 | +0x84 | Volume | STRONG N | GondolaTaxiEndTrigger, MovingPlatform01_GotoEndTrigger |
| PlatformMoving (0x338) | moby | 16 | +0x88 | Path | STRONG N | B2_Path4, B4_path1 |
| EnemyLeviathan (0x307) | moby | 16 | +0x4D0 | Volume | STRONG | A10_InnerVol_Levia_Aster2, A15_InnerVol_Levia_Aster3 |
| EnemyLeviathan (0x307) | moby | 16 | +0x4E0 | Volume | STRONG | A14_OuterVol_Levia_Aster3, A10_OuterVol_Levia_Aster2 |
| EnemyLeviathan (0x307) | moby | 16 | +0x4F8 | Path | STRONG N | A14_ParoltPath_Levia_Aster3, A10_Patrolpath_Levia_Aster2_2 |
| EnemyLeviathan (0x307) | moby | 16 | +0xDF0 | Path | STRONG | A14_Firepath_Levia_Aster3_Path2, A10_Firepath_Levia_Aster2_2 |
| EnemyLeviathan (0x307) | moby | 16 | +0xDF4 | Path | STRONG | A14_Firepath_Levia_Aster3_Path1, A10_Firepath_Levia_Aster2_1 |
| EnemyLeviathan (0x307) | moby | 16 | +0xE00 | Path | STRONG N | B12_Exit_Path_Lev1, K02_Exit_Path_Lev1 |
| EnemyLeviathan (0x307) | moby | 16 | +0xE34 | Volume | STRONG N | A10_IntroVolume, A15_Intro_Vol |
| EnemyLeviathan (0x307) | moby | 16 | +0xE38 | Volume | STRONG N | generatedVolumeForClue_wind_direction, B12_Lev1_SplashLayer |
| PromptSpaceShip (0x3E9) | moby | 16 | +0x370 | Path | STRONG N | Aphelion_Path_Intro, Aphelion_Path_In |
| PromptSpaceShip (0x3E9) | moby | 16 | +0x374 | Moby | STRONG | igRefNodeMoby86, entity3 |
| PromptSpaceShip (0x3E9) | moby | 16 | +0x378 | Path | STRONG N | Aphelion_Path_Exit, Aphelion_Path_Out |
| PromptSpaceShip (0x3E9) | moby | 16 | +0x37C | Moby | STRONG | igRefNodeMoby66, entity151 |
| PromptSpaceShip (0x3E9) | moby | 16 | +0x380 | Path | STRONG N | Aphelion_TransitionPath, aphelion_flyOutPath |
| PromptSpaceShip (0x3E9) | moby | 16 | +0x384 | Moby | STRONG | entity209, CrateEnemy2SegOther24 |
| PlatformAttached (0x316) | moby | 16 | +0x70 | Moby | STRONG | P03_ferris_innerwheel, P03_ferris_innerwheel |
| EnemySargassoPredator (0x2D4) | moby | 16 | +0x4D0 | Volume | STRONG | CubeEnemySegPredatorsInner, E_Island_Innervol_Predator4 |
| EnemySargassoPredator (0x2D4) | moby | 16 | +0x4E0 | Volume | STRONG | CubeEnemySegPredatorsOuter, E_Island_Outervol_Predator4 |
| EnemySargassoPredator (0x2D4) | moby | 16 | +0x904 | Volume | STRONG N | CubePredator1JumpPoint, Big_Island_JumpPoint_Predator13 |
| SimpleElevator (0x2D7) | moby | 15 | +0x74 | Volume | STRONG N | B6_Elevator_BottomTarget, B20_Elevator_Up_Pos |
| SimpleElevator (0x2D7) | moby | 15 | +0x78 | Volume | STRONG | a3_tall_elevator_trigger_vol_bottom, c_backarea_elevator_trigger_vol |
| SimpleElevator (0x2D7) | moby | 15 | +0x8C | Volume | STRONG N | B6_Elevator_TopTarget, B20_Elevator_Down_Pos |
| EnemyKerchuuRoller (0x29C) | moby | 15 | +0x4D0 | Volume | STRONG | CubeEnemy2SegRollers1Inner, CubeEnemy2SegRollers2Inner |
| EnemyKerchuuRoller (0x29C) | moby | 15 | +0x4E0 | Volume | STRONG | CubeEnemy2SegRollers1Outer, CubeEnemy2SegRollers2Outer |
| EnemyKerchuuRoller (0x29C) | moby | 15 | +0x4F4 | Path | STRONG N | PathEnemy2SegRollers1Intro1, PathEnemy2SegRollers2Intro1 |
| EnemyKerchuuRoller (0x29C) | moby | 15 | +0x4F8 | Path | STRONG N | PathEnemySegRollers2Patrol2, PathEnemySegRollers2Patrol3 |
| PickupBlueprint (0x5FE) | moby | 13 | +0x9C | Moby | STRONG | igRefNodeMoby90, xbreakable_crate6 |
| PickupBlueprint (0x5FE) | moby | 13 | +0xA0 | Volume | STRONG N | RYNO_Blueprint_Facing, blueprint_pos |
| PickupBlueprint (0x5FE) | moby | 13 | +0xAC | Moby | STRONG | RYNO_Blueprint_Crate, blueprint_crate |
| EnemyTachyonElite (0x34D) | moby | 13 | +0x4D0 | Volume | STRONG | a1_battlefield_awareness_center, a1_battlefield_awareness |
| EnemyTachyonElite (0x34D) | moby | 13 | +0x4E0 | Volume | STRONG | a1_battlefield_awareness, a1_battlefield_awareness |
| EnemyTachyonElite (0x34D) | moby | 13 | +0x4F4 | Path | STRONG N | a2_westbase_intropath_1, a2a_intro_path_mturret1 |
| PadMagCycleLauncher (0x33B) | moby | 13 | +0x58 | Moby | STRONG | entity88, entity49 |
| PadMagCycleLauncher (0x33B) | moby | 13 | +0x64 | Path | STRONG N | C1_Path_MagCyclePuzzle1_Intro, curve2 |
| PadMagCycleLauncher (0x33B) | moby | 13 | +0x68 | Path | STRONG N | b3_maglauncher_path, b5_launch_to_robowings |
| ApogeeBaseTurret (0x5BD) | moby | 12 | +0x30 | Area | STRONG | Z_Target_Turret4, Z_Target_Turret8 |
| SpawnerGeneric (0x59B) | moby | 12 | +0x1D0 | Area | STRONG | c4_spawner_landing_spots_4, c2_spawner_landing_spots_2 |
| PromptDecryptor (0x471) | moby | 12 | +0x4D88 | Volume | STRONG | seg2_decryptor_game, a_decryptor_game_vol |
| AttachedSimple (0x3F1) | moby | 12 | +0x20 | Moby | STRONG | P04_swingshotride2, P04_swingshotride1 |
| HazardElectricity_KerchuCity (0x584) | moby | 12 | +0x30 | Pod | STRONG | G07_Electric_Hazard2, G05_Electric_Hazard3 |
| PlatformSynchronized (0x65B) | moby | 11 | +0x88 | Path | STRONG N | B2_Path3, B2_Path2 |
| PlatformSynchronized (0x65B) | moby | 11 | +0xAC | Pod | STRONG | B2_Plat_Pod1, B1_Plat_Pod1 |
| PickupBitfield (0x46E) | moby | 10 | +0x9C | Moby | STRONG | G05_rotater_path, fastoon_cave_kelp67 |
| PickupBitfield (0x46E) | moby | 10 | +0xA0 | Volume | STRONG N | Groove_Pos, A_Ship_Part_Vol_1 |
| PickupBitfield (0x46E) | moby | 10 | +0xB4 | Area | MEDIUM | B10_Cat1_Firepoints, A_Rim_Firepoints_1 |
| PirateDropShip (0x2A3) | moby | 10 | +0xD0 | Path | STRONG N | B4_Dropship_IntroPath, Dropship1_IntroPath1 |
| PirateDropShip (0x2A3) | moby | 10 | +0xDC | Volume | STRONG N | B4_Dropship_DropVolume, Dropship1_DropVolume |
| PirateDropShip (0x2A3) | moby | 10 | +0xE0 | Path | STRONG N | B4_Dropship_ExitPath, Dropship1_ExitPath |
| PirateDropShip (0x2A3) | moby | 10 | +0x2A0 | Pod | STRONG | B4_BargePirates, PirateDropship1_Passengers |
| EnemyShockBot (0x2FF) | moby | 9 | +0x4D0 | Volume | STRONG | c5_innervol_shockbot, c15_innervol_bots |
| EnemyShockBot (0x2FF) | moby | 9 | +0x4E0 | Volume | STRONG | c5_outervol_shockbot, c15_outervol_bots |
| EnemyShockBot (0x2FF) | moby | 9 | +0x4F4 | Path | STRONG N | c5_shockbot2_intro, c10_shockbot1_intro |
| EnemyShockBot (0x2FF) | moby | 9 | +0x4F8 | Path | STRONG N | c5_shockbot2_patrol, c2_shockbot_patrol |
| PromptTalkingSkull (0x437) | moby | 9 | +0x204 | Clue | MEDIUM | clue1, clue1 |
| PromptTalkingSkull (0x437) | moby | 9 | +0x208 | Clue | STRONG | clue4, clue4 |
| PromptTalkingSkull (0x437) | moby | 9 | +0x21C | Moby | STRONG | entity190, entity211 |
| PromptTalkingSkull (0x437) | moby | 9 | +0x220 | Pod | STRONG | A_PirateDoor2_Pirates, MOBYS |
| PromptTalkingSkull (0x437) | moby | 9 | +0x224 | Volume | STRONG N | A_PirateDoor2_DanceVolumeLeft, A_PirateDoor3_DanceVolumeLeft |
| PromptTalkingSkull (0x437) | moby | 9 | +0x228 | Volume | STRONG N | A_PirateDoor2_DanceVolumeRight, A_PirateDoor3_DanceVolumeRight |
| PromptTalkingSkull (0x437) | moby | 9 | +0x22C | Volume | STRONG | A_PirateDoor2_DanceVolumeCenter, A_PirateDoor3_DanceVolumeMiddle |
| PromptTalkingSkull (0x437) | moby | 9 | +0x240 | Volume | STRONG N | A_PirateDoor2_DiscoBallVolume, A_PirateDoor3_DiscoBallVolume |
| NPCSkull (0x66A) | moby | 9 | +0x414 | Volume | STRONG N | A_PirateDoor2_CheckVol, A_PirateDoor1_CheckVol |
| NPCSkull (0x66A) | moby | 9 | +0x63C | Moby | STRONG | TalkingSkull2, TalkingSkull1 |
| WeaponHookupGelTank (0x349) | moby | 8 | +0x4C | Pod | STRONG | G00_GelTanks, G00_GelTanks |
| HazardFissure (0x3E0) | moby | 8 | +0x3C | Path | STRONG | B11_Fissure_Path1, B21_Fissure_Path1 |
| VendorArmor (0x396) | moby | 8 | +0xA8 | Volume | STRONG | armor_pad_exit, ArmorDudeExitVol |
| VendorArmor (0x396) | moby | 8 | +0x1B4 | Moby | STRONG | G05_swarmer6, xbreakable_crate117 |
| VendorArmor (0x396) | moby | 8 | +0x1B8 | Moby | STRONG | V3_AmorVendor_Pad, armor_pad |
| ArenaSpawner (0x318) | moby | 8 | +0xEC | Moby | MEDIUM | d1_fake_detonator_13, d1_fake_detonator_13 |
| ArenaSpawner (0x318) | moby | 8 | +0xF0 | Moby | MEDIUM | arena_inner_ammopad1, arena_inner_ammopad1 |
| ArenaSpawner (0x318) | moby | 8 | +0xF4 | Moby | MEDIUM | d1_fake_detonator_4, d1_fake_detonator_4 |
| ArenaSpawner (0x318) | moby | 8 | +0xF8 | Moby | MEDIUM | arena_outer_ammopad12, arena_outer_ammopad12 |
| PickupWeapon (0x3DF) | moby | 7 | +0x9C | Moby | STRONG | igRefNodeMoby77, G03_swarmer6 |
| PickupWeapon (0x3DF) | moby | 7 | +0xA0 | Volume | STRONG N | RYNO_warp, Ryno_Hero_Pos |
| SpawnerFloor (0x355) | moby | 7 | +0x48 | Moby | STRONG | entity159, entity168 |
| ClankRotatingDoor (0x51F) | moby | 7 | +0x74 | Volume | STRONG N | B_bridge_1_direction, B_bridge_2_direction |
| ClankRotatingDoor (0x51F) | moby | 7 | +0x78 | Area | STRONG | Rotatebridge_1_positions, Rotatebridge_2_positions |
| ClankRotatingDoor (0x51F) | moby | 7 | +0x7C | Volume | STRONG | B_bridge_1_trigger, B_bridge_2_trigger |
| ClankRotatingDoor (0x51F) | moby | 7 | +0xC4 | Pod | STRONG | Rotatebridge_1_nodes, Rotatebridge_2_nodes |
| EnemyShinglePirate (0x2E5) | moby | 7 | +0x4D0 | Volume | STRONG | A_Shingle1_InnerVol, B_InnerVol_ShinglePirate1 |
| EnemyShinglePirate (0x2E5) | moby | 7 | +0x4E0 | Volume | STRONG | A_Shingle1_OuterVol, B_InnerVol_ShinglePirate2 |
| EnemyShinglePirate (0x2E5) | moby | 7 | +0x55C | Area | STRONG N | A_ShinglePirate1_MinorSeg_AssholeArea, A_ShinglePirate_AssholeArea |
| EnemyShinglePirate (0x2E5) | moby | 7 | +0x5C0 | Volume | STRONG | volume2, volume5 |
| JellyFish (0x480) | moby | 7 | +0xD4 | Path | STRONG N | B18_JellyPath_2, B18_JellyPath_3 |
| SpotlightTachyon (0x48A) | moby | 7 | +0x20 | Moby | STRONG | seg1_enemies_setup4c_turret3, seg1_enemies_setup3b_turret1 |
| SpotlightTachyon (0x48A) | moby | 7 | +0xB4 | Path | STRONG | seg1_gravramp_spotlight3_path, seg1_spotlight2_path |
| ZordoomSearchlight (0x5CB) | moby | 7 | +0xA4 | Path | STRONG | seg1_searchlight05_path, seg1_searchlight07_path |
| GelPumpPlatform (0x3AF) | moby | 6 | +0x74 | Area | MEDIUM | K03_Cat1_Firepoints, Z21_Area |
| StalactitePlatform (0x6C7) | moby | 6 | +0xB0 | Volume | STRONG N | c5_falling_stalag_platform_2_drop_loc, c5_falling_stalag_platform_1_drop_loc |
| SpawnerAnim (0x459) | moby | 6 | +0x144 | Volume | STRONG | A_Yard_SpawnerHomesPoint_4a, A_Lair_SpawnerHomePoint_2d |
| SpawnerAnim (0x459) | moby | 6 | +0x154 | Volume | STRONG | A_Yard_SpawnerHomesPoint_4d, A_Lair_SpawnerHomePoint_2c |
| SpawnerAnim (0x459) | moby | 6 | +0x164 | Volume | STRONG | A_Yard_SpawnerHomesPoint_4b, A_Lair_SpawnerHomePoint_2b |
| SpawnerAnim (0x459) | moby | 6 | +0x174 | Volume | STRONG | A_Yard_SpawnerHomesPoint_4c, A_Lair_SpawnerHomePoint_2a |
| SpawnerAnim (0x459) | moby | 6 | +0x1C4 | Path | STRONG | A_Yard_SpawnPoint_4a_IntroPath, A_Lair_SpawnPoint_1a_IntroPath |
| SpawnerAnim (0x459) | moby | 6 | +0x204 | Path | STRONG | A_Yard_SpawnPoint_4b_IntroPath, A_Lair_SpawnPoint_1b_IntroPath1 |
| SpawnerAnim (0x459) | moby | 6 | +0x240 | Area | STRONG | A_Yard_Spawnpoints_4, A_Lair_Spawnpoints_1 |
| SpawnerAnim (0x459) | moby | 6 | +0x254 | Path | STRONG | A_Yard_SpawnPoint_4a_IntroPath, A_Lair_SpawnPoint_2a_IntroPath |
| SpawnerAnim (0x459) | moby | 6 | +0x264 | Area | STRONG | A_Yard_Firepoints_7, A_Lair_Spawnpoints_2 |
| SpawnerAnim (0x459) | moby | 6 | +0x294 | Path | STRONG N | A_Yard_SpawnPoint_4b_IntroPath, A_Lair_SpawnPoint_2b_IntroPath |
| SpawnerAnim (0x459) | moby | 6 | +0x2A4 | Area | STRONG | A_Yard_Firepoints_7, A_Lair_Spawnpoints_2 |
| SpawnerAnim (0x459) | moby | 6 | +0x2D0 | Area | STRONG | A_Yard_Spawnpoints_4, A_Lair_Spawnpoints_2 |
| SpawnerAnim (0x459) | moby | 6 | +0x2D4 | Moby | STRONG | fastoon_cave_kelp125, fastoon_cave_kelp35 |
| SpawnerAnim (0x459) | moby | 6 | +0x528 | Pod | STRONG | A_Yard_Spawnergroup_4, A_Lair_Spawnergroup_1 |
| PirateBarge (0x16D) | moby | 6 | +0xD0 | Path | STRONG N | Dropship2_IntroPath, BargePath_PlayerBarge_Intro |
| PirateBarge (0x16D) | moby | 6 | +0xDC | Volume | STRONG N | Dropship2_DropVolume, pirate_barge_stop1 |
| PirateBarge (0x16D) | moby | 6 | +0xE0 | Path | STRONG N | Dropship2_ExitPath, BargePath_PlayerBarge_Exit |
| PirateBarge (0x16D) | moby | 6 | +0x2C4 | Volume | STRONG N | DropShip2_FinalVolume, pirate_barge_stop2 |
| ButtonMagCycle (0x352) | moby | 6 | +0x60 | Moby | MEDIUM | Button_MagCyclePuzzle1_2 |
| ButtonMagCycle (0x352) | moby | 6 | +0x64 | Moby | MEDIUM | Button_MagCyclePuzzle1_3 |
| ApogeeEscapePod (0x655) | moby | 5 | +0xAC | Controller | MEDIUM | controller29, controller65 |
| EnemyTachyonTurret (0x28E) | moby | 5 | +0x4D0 | Volume | STRONG | seg1_enemies_setup4c_turret3_inner_vol, seg1_enemies_setup4c_turret2_inner_vol |
| EnemyTachyonTurret (0x28E) | moby | 5 | +0x4E0 | Volume | STRONG | seg1_enemies_setup4c_turret3_outer_vol, seg1_enemies_setup4c_turret2_outer_vol |
| EnemyTachyonTurret (0x28E) | moby | 5 | +0x5D0 | Moby | MEDIUM | swingshottarget3, swingshottarget3 |
| ClankSimple (0x565) | moby | 4 | +0x20 | Area | STRONG | Panelbridge_1_positions, Rockpile_2_positions |
| ClankSimple (0x565) | moby | 4 | +0x24 | Volume | STRONG | B_panel_trigger_1, B_rock_trigger_2 |
| PromptClank (0x46B) | moby | 4 | +0x88 | Volume | STRONG N | B_clank_start_pos, A_Ratchet_Pos |
| DoorSlideAnim (0x486) | moby | 4 | +0x60 | Volume | STRONG N | B_OpenPoint_2, B_OpenPoint_4 |
| PadFlip (0x1FE) | moby | 4 | +0x3C | Volume | STRONG | kvolcorner3, kvolcorner4 |
| ClankElevator (0x4A1) | moby | 3 | +0x74 | Volume | STRONG N | B_Elevator_2_Pos_Lower, B_Elevator_1_Pos_Lower |
| ClankElevator (0x4A1) | moby | 3 | +0x8C | Volume | STRONG N | B_Elevator_2_Pos_Upper, B_Elevator_1_Pos_Upper |
| ClankElevator (0x4A1) | moby | 3 | +0xD0 | Volume | STRONG N | B_Elevator_2_PlayerUnderVolume, B_Elevator_1_PlayerUnderVolume |
| SimpleChargeUp (0x4E0) | moby | 3 | +0x24 | Volume | STRONG | C_Door1_Trigger, C_Door3_Trigger |
| SimpleChargeUp (0x4E0) | moby | 3 | +0x60 | Pod | STRONG | C_NodeGroup_1, C_NodeGroup_3 |
| PirateCannonDoor (0x4B4) | moby | 3 | +0xA0 | Moby | STRONG | A_TrapDoor6, A_TrapDoor5 |
| ElevatorFollowHero (0x5D7) | moby | 3 | +0x74 | Volume | STRONG N | A2_Elevator1_TopVol1, KerchuuElevatorTop |
| ElevatorFollowHero (0x5D7) | moby | 3 | +0x78 | Volume | STRONG | A_SecretArea_Elevator2_Deactivate, CubeEnemy2SegPirates2Inner |
| ElevatorFollowHero (0x5D7) | moby | 3 | +0x8C | Volume | STRONG N | A2_Elevator1_BottomVol1, KerchuuElevatorBottom |
| DoorAnimCranked (0x5A3) | moby | 3 | +0x50 | Moby | STRONG | G06_Bolt_Crank, G05_Bolt_Crank |
| SlagsBattleship (0x547) | moby | 3 | +0xB0 | Path | STRONG N | curve34, curve35 |
| InflatopodFanDoor (0x21D) | moby | 3 | +0x294 | Moby | STRONG | moby_Fan_1, moby_Fan_2 |
| BridgeCrankedAnim (0x4AE) | moby | 2 | +0x34 | Moby | STRONG | B_Boltcrank, A_Boltcrank_Bridge1 |
| InflatopodSeeSawCrane (0x235) | moby | 2 | +0x284 | Moby | STRONG | Platform_Crane2, Platform_Crane1 |
| ZoniChallengeZoni (0x514) | moby | 2 | +0x54 | Moby | STRONG | Glowing_Ring_1, C6_ring0 |
| InflatopodBoltCrankFan (0x2B3) | moby | 2 | +0x6C | Moby | STRONG | seg1_crates_courtyard_stack_4_crate3, seg1_crates_domeentrance_stack_1_crate2 |
| InflatopodBoltCrankFan (0x2B3) | moby | 2 | +0x70 | Moby | STRONG | moby_BoltCrankWalk_2, moby_BoltCrankWalk_1 |
| InflatopodBoltCrankFan (0x2B3) | moby | 2 | +0x74 | Moby | STRONG | seg3_boltcrank2, seg3_boltcrank1 |
| PickupChargeBoots (0x3E8) | moby | 1 | +0x9C | Moby | MEDIUM | a_tachyon_collectible1 |
| ElevatorCranked (0x5C2) | moby | 1 | +0x80 | Moby | MEDIUM N | Elevator1Boltcrank |
| KerchuuBoss (0x73) | moby | 1 | +0x3D8 | Moby | MEDIUM | kbridge1 |
| KerchuuBoss (0x73) | moby | 1 | +0x3DC | Moby | MEDIUM | kbridge2 |
| KerchuuBoss (0x73) | moby | 1 | +0x3E0 | Moby | MEDIUM | kbridge3 |
| KerchuuBoss (0x73) | moby | 1 | +0x3E4 | Moby | MEDIUM | kbridge4 |
| KerchuuBoss (0x73) | moby | 1 | +0x3E8 | Moby | MEDIUM | kbridgec1 |
| KerchuuBoss (0x73) | moby | 1 | +0x3EC | Moby | MEDIUM | kbridgec2 |
| KerchuuBoss (0x73) | moby | 1 | +0x3F0 | Moby | MEDIUM | kbridgec3 |
| KerchuuBoss (0x73) | moby | 1 | +0x3F4 | Moby | MEDIUM | kbridgec4 |
| KerchuuBoss (0x73) | moby | 1 | +0x3F8 | Moby | MEDIUM | kpadflip1 |
| KerchuuBoss (0x73) | moby | 1 | +0x3FC | Moby | MEDIUM | kpadflip2 |
| KerchuuBoss (0x73) | moby | 1 | +0x400 | Moby | MEDIUM | kpadflip3 |
| KerchuuBoss (0x73) | moby | 1 | +0x404 | Moby | MEDIUM | kpadflip4 |
| KerchuuBoss (0x73) | moby | 1 | +0x424 | Moby | MEDIUM | kbreakbridge2 |
| KerchuuBoss (0x73) | moby | 1 | +0x428 | Moby | MEDIUM | kbreakbridge3 |
| InflatopodBlusteryDoor (0x1C3) | moby | 1 | +0x2AC | Moby | MEDIUM | igRefNodeMoby10 |
| BossCaptainSlag (0x524) | moby | 1 | +0x6B8 | Moby | MEDIUM | rustypete |
| BridgeExplode (0x3D2) | moby | 1 | +0x2C | Moby | MEDIUM | A3_bridge_broken |
| BridgeExplode (0x3D2) | moby | 1 | +0x30 | Moby | MEDIUM | A3_bridge_pieces |

## Quest for Booty: coverage

| | Mobys | Controllers | Clues |
|---|---:|---:|---:|
| placed classes | 101 | 25 | 11 |
| instances with class data | 3174 | 949 | 140 |
| classes named | 101 | 25 | 11 |
| classes with >= 1 typed varying field | 76 | 20 | 7 |

Reference fields found: **123** (STRONG 99, MEDIUM 24). Native resolver site at the same (family, offset): 14 (ToD EBOOT sites; QfB EBOOT not scanned).
Other varying scalar fields typed: MEDIUM 130 (floats/bools), GUESS 239 (u16/u32/s32).

### Reference fields by class

| Class | Kind | n | Offset | Target | Label | Targets seen |
|---|---|---:|---|---|---|---|
| ClueGrindRail (0x36B) | clue | 37 | +0x10 | Path | STRONG | path_new_grindrail_path12, path_new_grindrail_path9 |
| ClueLedgeGrab (0x36D) | clue | 21 | +0x10 | Path | STRONG | village_ladder_rung_path, curve1 |
| ClueLedgeGrab (0x36D) | clue | 21 | +0x14 | Area | STRONG | dam_ladder_paths, training_cave_ladder_rungs |
| ClueWind (0x36C) | clue | 16 | +0x1C | Moby | STRONG | slag_east_ship_lantern_hanging_pirate2, slag_east_ship_lantern_hanging_pirate3 |
| ClueHudMap (0x535) | clue | 7 | +0x20 | Area | MEDIUM | clue_hud_map_secret1_area, secret_area_area |
| ClueZipLine (0x36A) | clue | 4 | +0x10 | Path | STRONG | training_zipline_path, boss_ship1_zipline |
| FxController (0x180) | controller | 506 | +0x54 | Volume | STRONG | FX_rainddrop_2, FX_rainddrop_9 |
| CamArgFixed (0x4C) | controller | 128 | +0x50 | Volume | STRONG N | wmill3_done_follow_forward, wmill2_done_follow_forward |
| CamArgIndexedPath (0x190) | controller | 39 | +0x5C | Path | STRONG N | newgrindrailcam1_indexpath, grindrail_springpadcam_indexpath |
| CamArgIndexedPath (0x190) | controller | 39 | +0x60 | Path | STRONG N | newgrindrailcam1_lookpath, grindrail_springpadcam_lookpath |
| CamArgIndexedPath (0x190) | controller | 39 | +0x64 | Path | STRONG N | newgrindrailcam1_camerapath, grindrail_springpadcam_campath |
| CarSpawner (0x233) | controller | 35 | +0x14 | Path | STRONG | A04_path02, A01_path03 |
| CarSpawner (0x233) | controller | 35 | +0x160 | Pod | MEDIUM | pteradactyls, pteradactyls |
| CamArgFollowPath (0x4B) | controller | 33 | +0x5C | Path | STRONG N | obsidian_eye_reveal_cam1_path, lighthouse_to_eye_campath |
| CamArgFollowPath (0x4B) | controller | 33 | +0x60 | Path | STRONG N | obsidian_eye_reveal_cam1_lookat_path, lighthouse_to_eye_lookat_path |
| CamArgFollowPath (0x4B) | controller | 33 | +0x64 | Volume | STRONG N | lighthouse_bolt_reveal_lookat_vol, exit_tal_vol |
| CamArgFollowPath (0x4B) | controller | 33 | +0x68 | Moby | STRONG | piratebargee, taxi_to_vc |
| EnemyFirepointController (0x517) | controller | 14 | +0x14 | Area | STRONG | battlefield_all_firepoints, beach_firepoints_pod |
| LightningFlashController (0x73B) | controller | 8 | +0x20 | Path | STRONG | undead_west_ship_cabin_melee2_patrol, undead_east_ship_ladder_path3 |
| VerticalFogController (0x55C) | controller | 8 | +0x14 | Volume | STRONG | vc2_vertical_fogging_volume, volume4 |
| CamArgFixedFollow (0x336) | controller | 7 | +0x60 | Moby | STRONG | tal, battle_darkwater_ship |
| MobySpawner (0xEC) | controller | 6 | +0x14 | Moby | STRONG | template_dead_skinny_pirate_melee_4, template_dead_skinny_pirate_melee_3 |
| UndeadEffectController (0x74E) | controller | 5 | +0x18 | Area | STRONG | undead_fx_cloud, undead_fx_area |
| EnemyGroupControllerMelee (0x32F) | controller | 5 | +0x9C | Pod | STRONG | roofjumppirates_group1, roofjumppirates_group3 |
| CameraSwayController (0x73E) | controller | 3 | +0x34 | Volume | STRONG | pirate_east_ship_hunchback1_inner, boss_slag_westship_firepoint4_vol |
| WispBulletController (0x7A3) | controller | 1 | +0x18 | Moby | MEDIUM | captain_darkwater_slaghead |
| LighthouseController (0x71A) | controller | 1 | +0x14 | Moby | MEDIUM | beacon_dome |
| LighthouseController (0x71A) | controller | 1 | +0x18 | Moby | MEDIUM | beacon_btm_bolt |
| LighthouseController (0x71A) | controller | 1 | +0x1C | Moby | MEDIUM | ni_lighthouse_keyboltcranklift_1 |
| LighthouseController (0x71A) | controller | 1 | +0x28 | Moby | MEDIUM | ni_lighthouse_key_2 |
| LighthouseController (0x71A) | controller | 1 | +0x2C | Moby | MEDIUM | ni_lighthouse_light_1 |
| LighthouseController (0x71A) | controller | 1 | +0x34 | Moby | MEDIUM | lighthouse_top_bolt |
| LighthouseController (0x71A) | controller | 1 | +0x38 | Moby | MEDIUM | tempboltcrankplatform01 |
| LighthouseController (0x71A) | controller | 1 | +0x3C | Moby | MEDIUM | ni_lighthouse_platform_top_1 |
| LighthouseController (0x71A) | controller | 1 | +0x44 | Moby | MEDIUM | ni_lighthouse_antanna_4 |
| LighthouseController (0x71A) | controller | 1 | +0x48 | Moby | MEDIUM | ni_lighthouse_antanna_3 |
| LighthouseController (0x71A) | controller | 1 | +0x4C | Moby | MEDIUM | ni_lighthouse_antanna_2 |
| LighthouseController (0x71A) | controller | 1 | +0x50 | Moby | MEDIUM | ni_lighthouse_antanna_1 |
| EnemySkinnyPirateMelee (0x1AD) | moby | 191 | +0x5A0 | Volume | STRONG | undead_west_ship_cabin_melee_inner, undead_barge3_pirates_inner |
| EnemySkinnyPirateMelee (0x1AD) | moby | 191 | +0x5B0 | Volume | STRONG | undead_west_ship_cabin_melee_outer1, undead_barge3_sniper_outer |
| EnemySkinnyPirateMelee (0x1AD) | moby | 191 | +0x5C4 | Path | STRONG | ship_to_beach_intro_center_west, water_to_beach_intro_west |
| EnemySkinnyPirateMelee (0x1AD) | moby | 191 | +0x5C8 | Path | STRONG | beach_west_patrol_path, beach_west_patrol_path |
| EnemySkinnyPirateMelee (0x1AD) | moby | 191 | +0x778 | Volume | STRONG | dropship1_pirate_jumppoint3, roofjumppirates_jumpvol3 |
| EnemySkinnyPirateMelee (0x1AD) | moby | 191 | +0xA44 | Moby | STRONG | ship2_turret2, ship2_turret1 |
| EnemyCaveBat (0x1E2) | moby | 149 | +0x5A0 | Volume | STRONG | ti_innervol_cavebat_group1, ti_innervol_cavebat_group2 |
| EnemyCaveBat (0x1E2) | moby | 149 | +0x5B0 | Volume | STRONG | ti_outervoll_cavebat_group1, ti_outervoll_cavebat_group2 |
| EnemyCaveBat (0x1E2) | moby | 149 | +0x5C4 | Path | STRONG | bat_intro_path_bat4, bat_intro_path_group2_bat5 |
| EnemyCaveBat (0x1E2) | moby | 149 | +0x5C8 | Path | STRONG | patrol_path_batgroup1_bat4, patrol_path_batgroup2_bat5 |
| EnemyCaveBat (0x1E2) | moby | 149 | +0x61C | Volume | STRONG | vc2_boltplatform2_cavebat_inner_vol1, vc2_boltplatform2_cavebat_inner_vol1 |
| EnemyCaveBat (0x1E2) | moby | 149 | +0x9C0 | Volume | STRONG | ti_outervoll_cavebat_group1, ti_outervoll_cavebat_group2 |
| EnemyCaveBat (0x1E2) | moby | 149 | +0x9C4 | Path | STRONG | bat_outro_path_bat4, bat_outro_path_group2_bat5 |
| EnemyBurrowerBeetle (0x275) | moby | 53 | +0x5A0 | Volume | STRONG | training_beetles_inner_vol, village_beetles_inner_vol |
| EnemyBurrowerBeetle (0x275) | moby | 53 | +0x5B0 | Volume | STRONG | training_beetles_outer_vol, village_beetles_outer_vol |
| EnemyBurrowerBeetle (0x275) | moby | 53 | +0x5C8 | Path | STRONG | training_beetle1_patrol, training_beetle3_patrol |
| EnemySkinnyPirateShooter (0x1D3) | moby | 50 | +0x5A0 | Volume | STRONG | battlefield_inner, slag_east_ship_cargo_pirates_inner |
| EnemySkinnyPirateShooter (0x1D3) | moby | 50 | +0x5B0 | Volume | STRONG | slag_east_ship_cargo_pirates_outer, slag_east_ship_cargo_pirates_outer |
| EnemySkinnyPirateShooter (0x1D3) | moby | 50 | +0x5C4 | Path | STRONG | ship_to_beach_intro_center, ship_to_beach_intro_center_east |
| EnemySkinnyPirateShooter (0x1D3) | moby | 50 | +0x5C8 | Path | STRONG | beach_center_top_patrol_path, beach_east_patrol_path |
| EnemySkinnyPirateShooter (0x1D3) | moby | 50 | +0x5D4 | Area | STRONG | slag_east_ship_cargo_pirate_shooter2_firepoints, slag_east_ship_cargo_pirate_shooter1_firepoints |
| EnemySkinnyPirateShooter (0x1D3) | moby | 50 | +0x618 | Moby | STRONG | underwater_dead_pirate5, underwater_dead_pirate4 |
| PlatformRaising (0x796) | moby | 26 | +0xC4 | Volume | STRONG | door_open_vol_platform6, door_open_vol_platform5 |
| FastPathFollower (0x203) | moby | 24 | +0x38 | Path | STRONG | pirate_swingshot_dinghy3_path, pirate_swingshot_dinghy2_path |
| GlowSlug (0x79E) | moby | 24 | +0x55C | Area | STRONG N | IP_FIRESLUG_7, IP_FIRESLUG_4 |
| GlowSlug (0x79E) | moby | 24 | +0x560 | Area | STRONG | IP_AREA_VALID_FIRESLUG_6_7, IP_AREA_VALID_FIRESLUG_4_5 |
| PathFollower (0xF7) | moby | 23 | +0xD0 | Path | STRONG N | pirate_background_barge2_path, pirate_background_barge3_path |
| PathFollower (0xF7) | moby | 23 | +0xD4 | Pod | STRONG | darkwaters_ship_pod, background_barge4_pirate_pod |
| PathFollower (0xF7) | moby | 23 | +0xD8 | Area | STRONG | move_area_barge8, movearea_turretfodderbarge |
| PathFollower (0xF7) | moby | 23 | +0x108 | Volume | STRONG | pete_barge_end_vol, pete_barge_end_vol |
| NPCVillager (0x717) | moby | 19 | +0x5C8 | Path | STRONG | N10_npc_path, N01_npc_path |
| DoorSlide (0x34B) | moby | 18 | +0x38 | Moby | MEDIUM | boltcrank1 |
| DoorSlide (0x34B) | moby | 18 | +0x60 | Volume | STRONG N | beach_door_to_village_vol, beach_door_to_beacon_vol |
| HazardMovingSaw (0x746) | moby | 18 | +0x20 | Path | STRONG | wmill3_saw11_path, wmill3_saw13_path |
| EnemyHotheadPirate (0x6F9) | moby | 16 | +0x5A0 | Volume | STRONG | inner_volume_battlefield, inner_volume_battlefield |
| EnemyHotheadPirate (0x6F9) | moby | 16 | +0x5B0 | Volume | STRONG | outer_volume_battlefield, outer_volume_battlefield |
| EnemyHotheadPirate (0x6F9) | moby | 16 | +0x5C4 | Path | STRONG | water_to_beach_intro_center, water_to_beach_intro_center_east |
| EnemyHotheadPirate (0x6F9) | moby | 16 | +0x5C8 | Path | STRONG | vc2_boltplatform1_hothead_pirate2_patrol_path, vc2_exit_hothead2_patrol |
| EnemyHotheadPirate (0x6F9) | moby | 16 | +0x5D4 | Area | STRONG | battlefield_all_firepoints, battlefield_all_firepoints |
| EnemyHotheadPirate (0x6F9) | moby | 16 | +0x600 | Path | STRONG | battle_pirate_pen, battle_pirate_pen |
| EnemyHotheadPirate (0x6F9) | moby | 16 | +0x618 | Moby | STRONG | underwater_dead_pirate4, underwater_dead_pirate4 |
| EnemyHotheadPirate (0x6F9) | moby | 16 | +0x61C | Volume | STRONG | vc2_boltplatform1_hothead_inner_vol, vc2_exit_shingle_inner_vol |
| EnemyCaveViper (0x723) | moby | 16 | +0x5A0 | Volume | STRONG | vc2_boltplatform1_viper2_inner_vol, vc2_entrance_viper1_innervol |
| EnemyCaveViper (0x723) | moby | 16 | +0x5B0 | Volume | STRONG | vc2_boltplatform1_viper2_outer_vol, vc2_entrance_viper1_outervol |
| EnemyCaveViper (0x723) | moby | 16 | +0x5D4 | Area | STRONG | vc2_boltplatform1_viper2_firepoint_layer, vc2_entrance_viper1_firepoint_layer |
| EnemyCaveViper (0x723) | moby | 16 | +0x618 | Moby | STRONG | platforms_to_drink_puzzle_1, electro_hazard4 |
| EnemyHotheadPirateSwarmer (0x711) | moby | 11 | +0x5C8 | Path | STRONG | vc2_boltplatform1_swarmer_patrol10, vc2_ledgegrab_swarmer_patrol4 |
| PlatformGrappleCatapult (0x78F) | moby | 10 | +0x390 | Volume | MEDIUM | volume1, village_wmile4_catapult_vol |
| PlatformGrappleCatapult (0x78F) | moby | 10 | +0x394 | Path | STRONG | curve1, village_wmile4_catapult_path |
| PlatformBoltCrank (0x7AA) | moby | 9 | +0x20 | Moby | STRONG | boltcrank1, vc_boltcrank2 |
| PlatformGrappleExtending (0x705) | moby | 7 | +0x390 | Path | STRONG | willage_wrench_plat_bridge1_path, training_wrench_plat_bridge_path |
| PickupWeaponMod (0x74C) | moby | 7 | +0x9C | Moby | STRONG | training_steamjet2, training_steamjet1 |
| PickupWeaponMod (0x74C) | moby | 7 | +0xA0 | Volume | STRONG N | powerup_tornado_cam_vol, powerup_combuster_cam_vol |
| DoorAnim (0x34F) | moby | 7 | +0x44 | Moby | MEDIUM | talking_skull_beach_door |
| HazardFissure (0x3E0) | moby | 7 | +0x20 | Path | STRONG | wmill2_steamjet4_path, wmill2_steamjet0_path |
| PlatformGrappleFolding (0x70B) | moby | 7 | +0x390 | Volume | STRONG | wmill3_foldingplat3_remap_vol, wmill3_foldingplat2_remap_vol |
| PlatformGrappleFlipping (0x70C) | moby | 5 | +0x390 | Volume | STRONG | wmill4_wrench_rotate_plat4_remap_vol, wmill4_wrench_rotate_plat5_remap_vol |
| Windmill (0x70E) | moby | 5 | +0x2C | Moby | STRONG | windmill3_crank, windmill4_crank |
| BoltCrankTeleporter (0x794) | moby | 5 | +0x2C | Moby | STRONG | windmill1_crank, windmill4_crank |
| BoltCrankTeleporter (0x794) | moby | 5 | +0x30 | Volume | STRONG | wmill1_teleport_dest_vol, wmill4_teleport_dest_vol |
| PlatformGrappleSliding (0x708) | moby | 5 | +0x390 | Path | STRONG | curve1, vc2_sliding_wrench_platform2_path |
| PirateTaxi (0x72A) | moby | 4 | +0x138 | Path | STRONG | taxi_path, smugglers_barge_path |
| PirateTaxi (0x72A) | moby | 4 | +0x13C | Volume | STRONG N | taxi_stop_vol, smugglers_barge_vol |
| PiratePathFollower (0x77B) | moby | 4 | +0xD0 | Path | STRONG N | pirate_traversal_barge1_path, pirate_traversal_barge3_path |
| SimpleElevator (0x2D7) | moby | 4 | +0x94 | Volume | STRONG | vc2_exit_elevator_vol2, vc2_exit_elevator_stop_vol |
| SimpleElevator (0x2D7) | moby | 4 | +0x98 | Volume | STRONG | vc2_exit_elevator_trigger_vol, vc2_exit_elevator_start_vol |
| PuzzleOrganPipe (0x751) | moby | 4 | +0x30 | Moby | STRONG | organ_pedal_4, organ_pedal_3 |
| PuzzleDrinkLiquid (0x753) | moby | 4 | +0x2C | Moby | STRONG | drink_red_button, drink_purple_button |
| EnemyPirateSkullWalker (0x757) | moby | 3 | +0x8AC | Volume | STRONG | b2_w4_walker1_dropzone_end_vol, b2_w1_walker1_dropzone_start_vol |
| EnemyPirateSkullWalker (0x757) | moby | 3 | +0x8B0 | Volume | STRONG | b2_w4_walker1_dropzone_start_vol, b2_w1_walker1_dropzone_end_vol |
| TurretPirate (0x649) | moby | 3 | +0x518 | Moby | MEDIUM | underwater_dead_pirate |
| TurretPirate (0x649) | moby | 3 | +0x51C | Moby | MEDIUM | battle_turret_cover |
| TurretPirate (0x649) | moby | 3 | +0x524 | Moby | MEDIUM | battle_darkwater_ship |
| NPCSmuggler (0x27C) | moby | 3 | +0xA84 | Moby | STRONG | smuggler_parrot, new_hunter_bird |
| EnemyHunchbackPirate (0xEA) | moby | 3 | +0x5A0 | Volume | STRONG | pirate_east_ship_hunchback1_inner, undead_east_ship_cabin_shooter_inner |
| EnemyHunchbackPirate (0xEA) | moby | 3 | +0x5B0 | Volume | STRONG | pirate_east_ship_hunchback1_outer, undead_east_ship_cabin_shooter_outer |
| ElevatorCranked (0x5C2) | moby | 3 | +0xA0 | Moby | STRONG | slag_east_ship_boltcrank, boltcrank_boltcrank_elevator |
| ElevatorCranked (0x5C2) | moby | 3 | +0xA4 | Volume | STRONG | slag_east_ship_boltcrank_elevator_bottom_vol, vc_volume_boltcrankelevator_top |
| ElevatorCranked (0x5C2) | moby | 3 | +0xA8 | Volume | STRONG | slag_east_ship_boltcrank_elevator_top_vol, vc_volume_caveelevator_bottom |
| NPCSkull (0x66A) | moby | 3 | +0x9B4 | Volume | STRONG | trig_beach_door_activate, light_puzzle_skull_volume |
| BridgeCrankedAnim (0x4AE) | moby | 3 | +0x34 | Moby | STRONG | tempboltcrank_5, vc_boltcrank2 |
| RotaterCranked (0x32B) | moby | 2 | +0x74 | Moby | STRONG | vc2_boltplatform2_boltcrank, vc2_boltplatform1_boltcrank |
| DarkwaterBattleship (0x7A6) | moby | 1 | +0x17C | Moby | MEDIUM | captain_darkwater_slaghead |
| DirectorSlagWater (0x7BE) | moby | 1 | +0x40 | Moby | MEDIUM N | battle_darkwater_ship |
| BossCaptainSlagwater (0x772) | moby | 1 | +0x720 | Moby | MEDIUM | rustypete |

## Open

- Class-attributed EBOOT proof (which class's method reads which offset): needs the ToD class registry/vtable map.
- Lists of references (pointer to a list) and Clue/Controller reference fields in classes with one or two instances.
- Meaning of non-reference fields: only physical types here; names come from Lua/EBOOT per class later.
- QfB has no Lua instance-name corpus here; names come from its debug.dat. Class numbers are shared with ToD for older classes but each game's own table is used.
- Inspector wiring: the old-engine reader does not yet expose class-data bytes (`MobyInstanceOld` keeps only the pointer), so the new .fields files are read once the old-engine instances carry Settings and a Class (needs `PlacedInstance`/region code that is not part of this change).
