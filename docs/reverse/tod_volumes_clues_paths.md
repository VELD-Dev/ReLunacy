# Old engine (ToD, QfB): volumes, Clues, paths, areas, pods, scents

2026-11-17. Data: all 20 ToD levels (NPUA80965, `packed/levels/*/level_cached.psarc` gameplay.dat) and the 5 QfB levels
(NPEA00088; 4 carry gameplay families, `level_transitions` only Mobys). Probes (gitignored, `dev/tools/todvol/`, plain
python, no numpy): `lib.py` (loaders), `p1.py`/`p2.py` (volumes), `hv.py`/`idx.py`/`cov.py` (class data references,
coverage), `ppcdis.py <hexaddr> <ninsn>` (capstone on the ToD EBOOT.elf). Confidence: PROVEN = holds on every record of
every level checked, STRONG = holds on data plus code or name evidence, GUESS = pattern only.

All integers big-endian. Section ids are those of gameplay.dat (IGHW 1.1). The pointers inside records are absolute file
offsets (after the IGHW fix-up), so a record's pointee is found by plain subtraction from its section offset.

## 1. Names: instances.lua is the Rosetta for every level (PROVEN)

`debug.dat` exists only for metropolis, but **every level's names are in `global_cached.psarc`:
`/built/levels/<level>/instances.lua`** (and a compiled `.lc`; the 524 scripts are there too). Lines read
`name = <family>_handle:new( N ); -- <guid>` with families moby, volume, path, clue, controller, area, pod, scent.

- On metropolis, for all 8 families (1378 Mobys, 902 volumes, 505 paths, 77 Clues, 465 controllers, 70 areas, 123 pods,
  1 scent) the name AND the GUID of entry N equal debug.dat's record N. GUID = the two u64 of the 0x18 debug record
  (`tuid1`, `tuid2`) read as `uuid.UUID(bytes_le=pack('>QQ'))`.
- On all 20 levels the count per family equals the gameplay.dat section count and the N are contiguous 0..count-1.
  So `names[i] <-> instance[i]` in every level. Our `DebugReader` only reads debug.dat, so only metropolis is named today.
- debug.dat name sections are the gameplay id + 0x20: 0x73C0 Mobys, 0x7720 paths (gameplay 0x7700), 0x7760 volumes
  (0x7740), 0x77A0 Clues (0x7780), 0x77D0 controllers (0x77C0), 0x7920 pods (0x7900), 0x7960 areas (0x7940), 0x79A0 scents
  (0x7980). Each is 0x18 records `{u64 tuid1, u64 tuid2, u32 namePtr, u16 zone, u16 pad(=0)}`. `zone` is a per-instance
  editor group number (volumes: zones 9, 5, 10, 11 hold most); its meaning is open. Other debug sections: 0x72C0 Tie names
  (4848), 0x74C0 shrubs (757), 0x8C01 lighting volumes of main.dat (19: `v_rail*`, `jump*`, `lightingVolume2`).

## 2. Volumes (section 0x7740, stride 0x90) (PROVEN on 6,807 + 1,392 QfB records)

| Off | Size | Meaning |
| --- | --- | --- |
| 0x00 | 0x40 | row-major 4x4 matrix, row vectors: rows 0..2 = scaled axes, row 3 (+0x30) = translation, last column (0,0,0,1) |
| 0x40 | 0x30 | cached inverse of the 3x3 linear part, 3 rows of 4 floats (4th float 0). Matches `inv(M3x3)` on 6,806/6,807 (the one miss is a zero-scale volume) |
| 0x70 | 0x10 | Euler XYZ cache (4th float always 0) |
| 0x80 | 1 | primitive selector: 0 box, 1 cylinder, 2 sphere, 3 capsule |
| 0x81 | 0x0F | zero on every record |

- Rows are orthogonal on all non-degenerate records (no shear): scale = row length, rotation = row / length.
- Selector histogram (ToD, 20 levels): box 6450, cylinder 320, sphere 34, capsule 3. QfB: box 1357, cylinder 35. The unit
  shape is scaled by the matrix, as in the new engine.
- Code (STRONG): the containment routine at `0x00676468` (point r3, volume r4) subtracts the translation at +0x30, rotates by
  the +0x40 inverse, then switches on `lbz r10, 0x80(r4)`: 0 each lane `|v| <= c`; 1 `sqrt(a^2+b^2) <= c` on two lanes
  (the third bounded separately); 2 `sqrt(a^2+b^2+c^2) <= c`; 3 clamped axial lane, radial `<= c` (capsule). `c` is one
  constant from the TOC (`-0x52f4(r2)`), not read; whether it is 0.5 or 1.0 is open (keep our current wire convention).
  Axis of cylinder/capsule = local Y (Resurgence; consistent with the lane order, not independently re-proven here).
- Cylinders are legitimately elliptical: only 33% have X = Z scale (212 of 320 have three different scales), so the
  radial test is per-lane scaled, not circular. Spheres are uniform in 91% (non-uniform ones are ellipsoids).
- Scale range: boxes 0.2 .. 2900 (floor/bounds volumes), spheres 0.5 .. 32.
- There is NO owner/type field in the record: a volume is just geometry. What uses it comes from the references below.

Coverage (volume users by route; routes a-d are in gameplay.dat, scripts are not counted; `cov.py`):

| Level | Volumes | With a gp user | Clue | Controller | Moby class data | Area |
| --- | --- | --- | --- | --- | --- | --- |
| apogee space station | 507 | 244 | 40 | 2 | 74 | 128 |
| cobalia | 489 | 328 | 38 | 2 | 40 | 252 |
| cragmite ruins | 517 | 388 | 17 | 5 | 55 | 317 |
| fastoon | 341 | 209 | 8 | 2 | 29 | 173 |
| fastoon_return | 441 | 327 | 13 | 2 | 80 | 296 |
| imperial fight fest | 378 | 265 | 13 | 19 | 88 | 145 |
| iris | 334 | 184 | 22 | 7 | 67 | 94 |
| kerchu city | 345 | 224 | 25 | 1 | 83 | 153 |
| level_transitions | 1 | 1 | 1 | 0 | 0 | 0 |
| meridian city | 388 | 205 | 23 | 3 | 72 | 129 |
| metropolis | 902 | 359 | 77 | 3 | 159 | 123 |
| pirate base | 359 | 207 | 22 | 4 | 91 | 97 |
| rykan v | 456 | 271 | 22 | 3 | 40 | 234 |
| sargasso | 377 | 268 | 6 | 2 | 87 | 177 |
| slags_fleet | 214 | 108 | 23 | 1 | 39 | 49 |
| space combat i / ii / iii | 22 / 25 / 20 | 2 each | 1 | 1 | 0 | 1 |
| stratus city | 317 | 145 | 21 | 5 | 68 | 53 |
| zordoom prison | 374 | 204 | 41 | 3 | 100 | 61 |

The rest (roughly half) are script volumes (`is_in_volume(Ratchet1, F_Camera1_Trigger1)`, checkpoint volumes): the script
global is the instances.lua name. Only Moby-class and controller fields with a declared `Handle` in
Resurgence's `ToolsOfDestruction.h` were followed, so Moby/controller counts are lower bounds.

## 3. Clues (section 0x7780, stride 0x10) (PROVEN on 415 ToD + 140 QfB records)

A Clue is a volume with a behaviour class (same idea as the new engine, simpler record):

| Off | Meaning |
| --- | --- |
| 0x00 | pointer to its volume record in 0x7740 (always `0x7740.offset + k*0x90`; 415/415 resolve, no volume is shared by two Clues) |
| 0x04 | pointer to its class data (raw, NOT an IGHW), in 0x25280 (explicit, 212) or 0x253C0 (shared, 203) |
| 0x08 | class data byte size (equals the class's `Class.Size`: 16..200) |
| 0x0C | class id (`VkTGenEnumsClass`) |

- Rosetta check (metropolis): Clue `X` always owns the volume named `generatedVolumeForClue_X`. A Clue's volume is created
  by the editor from the Clue, so those volumes are Clue-only. The class data's first 12 bytes are the common prefix; class
  fields start at +0x0C (flags) and +0x10 onward.
- Shared class data: many Clues point at one blob (metropolis: 60 ClueFallToDeath share 0x1F02E0).
- 24 classes in ToD (counts, levels): ClueFallToDeath 881 (111, 12), ClueLedgeGrab 877 (40, 8), ClueDeath 1266 (37, 7),
  ClueGrindRail 875 (31, 5), ClueQuicksand 880 (28), ClueGalacticMap 1083 (19, all), ClueHudMap 1333 (19), ClueWind 876
  (18), ClueZipLine 874 (15), ClueWater 878 (13), ClueGravity 873 (11), ClueLava 1517 (11), ClueSpecialDestroy 1282 (10),
  ClueSwingshotEquip 1258 (9), ClueCuttingLaser 879 (8), ClueSpaceTeleporter 939 (7), ClueDirectionalFallToDeath 1661 (6),
  ClueJumpSlot 1496 (5), ClueNoFlipJump 1682 (5), ClueRoboWingContainer 1249 (3), ClueLeaveMagCycle 1260 (3), ClueDeathPath
  1744 (3), ClueLeaveRoboWings 1261 (2), ClueLevelTransition 1567 (1). QfB adds 1986 and 1573 (ClueNoWeather), and its table
  may differ from ToD's: not checked against the QfB EBOOT.
- References from Clue class data (all in range on every instance; -1 = none for Path/Area/Volume): ClueGrindRail +0x10 Path
  (30/31, one none), ClueZipLine +0x10 Path (15/15), ClueLedgeGrab +0x10 Path / +0x14 Area (30 Path + 10 Area, rest none),
  ClueWind +0x18 Path, ClueCuttingLaser +0x10 / +0x14 Volume, +0x1C Path, +0x18 / +0x20 Moby, ClueDeathPath +0x10 Path,
  ClueRoboWingContainer +0x10 Path, ClueSpaceTeleporter +0x10 Moby, ClueQuicksand +0x20 Moby, GalacticMap / LevelTransition
  = tables of Moby handles. Handle encodings (Resurgence, consistent with every value seen): Path/Area/Volume/Pod/Scent
  raw int32 index, -1 none; Moby/Controller `((i+1)<<16)|i`, 0 none; Clue `0x10000|i`.
- Scent members (0x7990) are 32-bit values `0x10000|clue index` (STRONG: all 17 scents, indices in Clue range).

## 4. Controllers (0x77C0, stride 0x30) (PROVEN, 4,592 records over 20 levels, 30 with null data)

`+0x00` pointer to a mini-IGHW (`IGHW 2`, one section 0x25200; SoundEnvironment adds a 0xFFFFFFFF "extra" section of 20
bytes, 443 controllers carry one), `+0x04` position XYZ, `+0x10` rotation XYZ, `+0x1C` scale XYZ, `+0x28` mini-IGHW size,
`+0x2C` zero. The payload's first u32 is the class id and the payload length equals `Class.Size` (0x25200 count). Classes in
the data: CamArgFixed 600, FxController 2074, SoundEmitter 634, SoundEnvironment 544, CarSpawner 546, CamArgIndexedPath 112,
MeteoriteSpawner 42, CamArgFollowPath 39, CamArgFixedFollow 31, EnemyBattlefieldController 30, EnemyFirepointController 18,
... (31 classes). Volume/Path/Area/Pod references (all resolve, none out of range): SoundEnvironment +0x24 Volume (544/544),
VerticalFogController +0x14 Volume (8), CamArgFollowPath +0x5C/+0x60 Path, +0x64 Volume (5 set), CamArgIndexedPath +0x5C/
+0x60/+0x64 Path (110 of 112), CarSpawner +0x14 Path (527 of 546), EnemyFirepointController +0x14 Area (18),
StalactiteSpawner +0x14 Area (12), WeatherController +0x5C Area (2), AlarmController +0x18 Pod, EnemyGroupControllerMelee
+0x9C Pod, FxController +0x6C Volume (26 of 2074).

## 5. Areas, pods, scents (PROVEN)

- Area (0x7940, 0x10): `+0 ptr` to u16 path indices, `+4 ptr` to u16 volume indices (both inside 0x7950), `+8 u16 pathCount`,
  `+0xA u16 volumeCount`, `+0xC u32` always 0 (966/966). All indices in range.
- Pod (0x7900, 0x10): `+0 ptr` to u32 packed Moby handles in 0x7910, `+4 count`, `+8/+0xC` zero (967/967 in range).
- Scent (0x7980, 0x10): `+0 ptr` into 0x7990, `+4 count`, entries as above.

## 6. Paths (0x7700 records, 0x7710 points) (PROVEN on 3,196 ToD + 1,077 QfB paths)

Record (0x10): `+0 ptr` to the first point (inside 0x7710), `+4 u32 pointCount`, `+8 f32 totalLength`, `+0xC u32 flags`.
Point (0x10): `x, y, z, f32 cumulative arc length from point 0` (so +0xC of point 0 is 0).

- Points are laid out in path order with no gaps: every path's pointer equals the previous path's end and the last ends at
  the 0x7710 end, in all 24 files.
- Cumulative value = sum of Euclidean XYZ segment lengths, within 1e-3 relative, on every point of every path; the record's
  `totalLength` = last point's value. Monotone. No tangents, no timing: it is a polyline whose 4th float is distance, so any
  speed/time comes from the user (CarSpawner, CamArg, enemy class data), not the path.
- Counts: 7 one-point paths, 743 two-point, 1,880 with 8 or more. Open/closed: there is no closed bit; 7 paths happen to
  start at their end point (geometry only).
- Flags: 0 (3,029) or 0x100 (167). 0x100 is not "closed" (not correlated with start == end) and not tied to a user class
  (users of 0x100 paths: EnemyLeviathan 37, CamArgIndexedPath 6, ClueDeathPath 2, ...; 108 unused): meaning OPEN. QfB same
  (0 or 0x100).
- Referenced by: clue class data (grind/zip/ledge/wind/death/laser/robowing), CarSpawner, CamArgFollowPath/IndexedPath,
  Moby class data (patrol, intro, exit paths of enemies) and Areas. About 45% of paths have a user in gameplay.dat (metropolis
  280/505); the rest are used by scripts (`ally_followpath`, `check_path_trigger`).

## 7. QfB (PROVEN structurally)

Same section ids and strides: volumes 0x90, paths 0x10, Clues 0x10, controllers 0x30, areas/pods/scents 0x10. npc_island 412 /
279 / 70, prologue 367 / 179 / 20, treasure_island 299 / 365 / 11, viper_caverns 314 / 254 / 39 (volumes / paths / Clues). All
volume, path and Clue invariants of sections 2, 3, 6 hold. Not done: QfB class names (its own enum), its instances.lua.

## 8. Resurgence assessment (dev/ReLunacy_Ressurgence)

Right (all re-verified here): 0x90 volume layout with +0x40 inverse cache and +0x80 selector; Clue record; Area/Pod/Scent
lists; path point format and the arc-length rule (`OldPathSerializedProperties`); controller record and class id at
payload +0; handle encodings; `ToolsOfDestruction.h` Clue/Controller class definitions (every `Handle` in range on all data;
class sizes equal the payload sizes). Better than ours: `OldGameplayReader` (decodes all families, ranges validated,
relative-pointer fallback), `OldVolumeSerializedProperties.GetShape`, the class header. Not found by Resurgence: the
instances.lua route to names for every level (it mined debug.dat of 19 levels, which the user's install lacks). Resurgence
claim to treat with care: "19 retail debug.dat" (only metropolis present here); its 394 Clues vs 415 found here (it excluded
space combat/level_transitions or similar; not investigated).

## 9. State of our code and implementation plan

Today `RegionReader.ReadVolumesOld` read only the matrix (shape always Box, name only on metropolis); no old Clues, paths,
controllers, areas, pods or scents exist in the model for the old engine.

Done in this pass: `ReLunacy.Engine/Loading/Readers/RegionReader.cs` `ReadVolumesOld` now reads the +0x80 selector, so
cylinder / sphere / capsule volumes get their shape (`dotnet build ReLunacy.sln` passes). `Volume.Index` is left -1 on
purpose (new-engine code treats Index >= 0 as a table slot).

Plan (new old-engine files first, shared-model changes last):
1. `OldInstanceNames` (new, Loading/Readers): read `/built/levels/<level>/instances.lua` from `packed/game/global_cached.psarc`
   (PsarcRanges exists) when debug.dat is absent; feed `DebugReader.Get*Name`. Gives names to all ToD levels (and QfB's
   global_cached, untested). Needs only `DebugReader` and FileManager (no shared model).
2. `OldGameplayReader` (new): port Resurgence's reader (paths, controllers, Clues, areas, pods, scents) without its writer
   half. Clue -> volume by pointer; Clue class by the 24-class list.
3. Shared model (needs the other agents' files, so not done): `Volume.Clue`/`VolumeClue` population for the old engine (Clue,
   ClueClasses names from ToolsOfDestruction.h), `Volume.Areas`/`Scents`, `Volume.Index` for old volumes, paths as
   `GameplayPath` entities in `EntityRegion`, controllers as `GameplayController`, the "Used by" inspector scan over
   controller/Moby `Handle` fields. All fields exist already for the new engine; the old reader must fill them with the
   old section's index as `Index`.
4. Writers: Resurgence's `OldGameplayWriter` family handles relocation, arc-length rebuild and cache refresh; none ported.
   Any volume transform edit must rewrite +0x40 (inverse) and +0x70 (Euler) too.

## 10. Open

- Box/sphere/cylinder unit constant `c` (0.5 or 1.0) from the TOC at `-0x52f4(r2)`; cylinder axis independently.
- Path flag 0x100; volume `zone` u16 in debug/instances order; meaning of controller extra section 0xFFFFFFFF (443 records).
- QfB class table (ids 1986 etc.), QfB instances.lua names, FxController +0x6C only 26 of 2074 set.
- Which scripts use which volume: needs a token search of `levels/<level>/scripts/*.lua` against instances.lua names
  (not run; metropolis' cameras.lua visibly does `is_in_volume(Ratchet1, F_Camera1_Trigger1)`).
