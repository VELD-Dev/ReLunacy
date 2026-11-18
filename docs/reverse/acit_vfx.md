# A Crack in Time: visual effects (effect.dat, fxconduit, VFX textures)

Game: NPUA80966, 22 levels. Reference layout: Into the Nexus (NPUA80908, same layout as QForce / All 4 One).
Prior work: Fianshoo's ReLunacy Resurgence (`dev/ReLunacy_Ressurgence`, `Docs/reverse/NEW_ENGINE_EFFECTS_2026-09-25.md`,
`NEW_ENGINE_FX_SPU_2026-09-26.md`, `NEW_ENGINE_FX_CONTROLLER_2026-09-25.md`) decodes the QForce layout and reads ACiT's
keys and textures only. This note fills the ACiT gaps.

Labels: **proven** = read in the ACiT EBOOT or holds on every record of all 22 levels; **strong** = consistent
on all data plus a matching QForce proof; **inferred** = from value patterns; **open**.

Tools (gitignored, `dev/tools/vfx`): `pull.py` (corpus from the install, read-only), `effect.py`, `conduit.py`,
`fxc.py`, `resolve_fxc.py`, `fxshow.py`, `fieldstats.py`, `textures.py`, `blscan.py`, `ighw02.py`.

## 1. Files

| File | Where | What |
| --- | --- | --- |
| `effect.dat` | each `level_cached.psarc` | IGHW 0.2 + relocation tail: every effect of the level (package, quads, lights, emitters, curves) |
| `fxconduit_packed.dat` | same | who plays which effect: (class, EventType, Tags key) -> effect id; also sound, dialogue, rumble, decal |
| `vfx_system_header.dat` | same | IGHW 1.1, section `0x5200`: 85-150 x 32-byte RSX texture descriptors |
| `vfx_system_texel.dat` | same | IGHW 1.1, `0x5300` texel blob, `0x5A00` {u32 hash, u32 padded size} per texture |
| `/scripts/tags.lua` | `packed/game/global_cached` | `Tags` enum: names every effect key (`AXIOM_FOUNTAIN_PILLAR` = 10187) |
| `/scripts/eventtypes.lua` | same | `EventTypes` enum (673; `ACTIVE` = 0) |
| `/scripts/visualspecs.lua` | same | explicit visual specs: one entry (`WeaponChange_Icon` = 10000) |

No global effect file: each level carries every effect it can play. Effect ids are game-wide (id space 1,718, 100-524
mapped per level).

## 2. effect.dat (proven, same container as QForce)

```
0x100  { u32 4, ptr list0, ptr list1, ptr list2, ptr list13, ptr 0x500, ptr 0x800 }
0x300  list { u32 piece type, u32 n, ptr -> 0x700 ptr[n] }      types 0 Pkg, 1 Quad, 2 Lite, 13 Emitter
0x400  one record per piece
0x600  u32 n + n x FxDefFX (placed pieces of a package), pointed at by package +0x00
0x500  { u32 1718, u32 1, 1718 x u32 package index or -1 }        effect id -> package
anon   u32 n + n keys, pointed at by a key header
```

Every chunk except 0x100 is the target of exactly one pointer (tree). Record sizes:

| Record | ACiT | QForce/A4O/ITN |
| --- | ---: | ---: |
| FxDefPkg | 336 | 424 |
| FxDefQuad | 372 | 384 |
| FxDefLite | 376 | 384 |
| FxDefEmitter | 896 | 932 |
| FxDefFX | 212 | 216 |

### 2.1 Keys (proven: ACiT binders `0x69D430`, `0x69CE78`, `0x69CA7C`)

Key header `{ptr keys, f32/u32 word, u8 count...}`; keys array has u32 n at -4; the binder caps n at 32.
Same key kinds as QForce: scalar 16 B `{f32 min, f32 max, f32 time, u8 mode}`, spawn life/speed 20 B
`{min, max, extra, time, mode}`, colour 40 B `{RGBA min, RGBA max, time, mode}`, rotation mode at +0x0D.
Mode > 6 -> 0; min == max -> mode 0; mode != 0 and min > max -> swapped. Size tracks x0.5, rotations x pi/180.
The binder sizes the particle pool with `life * (1 + extra) + 0.1` (so `extra` = relative life variance, inferred).

### 2.2 FxDefEmitter (896)

Keys and textures sit where QForce has them; the controls block is shifted (-0xC from +0x1C4, -0x10 from ~+0x218),
and QForce's render-flag block +0x358..+0x36B does not exist.

| Off | Field | QForce | Label |
| --- | --- | --- | --- |
| +0x000 | spawn rate (16 B keys) | same | proven |
| +0x018 / +0x028 / +0x034 | spawn life (20) / speed (20) / radius (16) | same | proven |
| +0x040 / +0x04C / +0x058 / +0x078 | emitter-time colour / alpha / alpha test / scale | same | proven offsets, names strong |
| +0x084 / +0x090 / +0x09C / +0x0BC | particle-life colour / alpha / alpha test / size (x0.5) | same | proven |
| +0x0C8 | particle spin (deg/s) | same | proven |
| +0x0D4 | 8 x FxTexture (28 B): 4 handles `(0x8000 \| index) << 16`, `0xFFFF0000` empty | same | proven (34,464 handles, 0 out of range) |
| +0x1B8 | texture count (u8, 1 or 2) | +0x1B4 | proven (data) |
| +0x1C0 / +0x1C2 / +0x1C4 / +0x1C6 | four shader selectors (odd bytes zeroed at load); +0x1C6 is the one set | +0x1CC..+0x1D2 | proven (`0x69E320`) |
| +0x1CC | distortion keys; bound only when flag +0x1DC (from the shader tables) is set | +0x1D8 | proven |
| +0x1E0 | FxEmtBeamControl: max length, min length, tail follow rate | +0x1EC | strong |
| +0x1EC | FxEmtStreakControl: +0x1ED points (rounded up to 8, max 128), +0x1F0 >= 0, +0x1FC clamped 0..90 deg -> rad, +0x204 clamped 0..64 | +0x1F8 | proven (class 3 path of `0x69E320`) |
| +0x248 | class-6 block, bound by `0x69B8B0` | +0x2E4? | proven call, meaning open |
| +0x24C | unit vector, (0,0,1) almost always | +0x25C | inferred |
| +0x280 | 180/180 bytes | +0x290 | inferred |
| +0x304 | emitter class, written at load (0 = disabled) | +0x314 | proven |
| +0x314 | emission duration (0.1 s bursts; 0 = continuous); > epsilon marks a burst for the pool size | +0x324 | proven (`0x69D430`) |
| +0x318 | spawn style byte (generator index, 0..52 used) | +0x328 | strong |
| +0x319 / +0x31A | cone min / max (deg) | +0x329/+0x32A | strong |
| +0x31C | inherit instance velocity | +0x32C | inferred |
| +0x328 | motion mode (axis; 9 = world -Y gravity, 8 = world +Y) | +0x338 | strong |
| +0x32C..+0x344 | acceleration, thrust, turbulence, drag, min speed, max speed (1024), wind | +0x33C..+0x354 | strong |
| +0x348..+0x37F | FxEmtSystemInfo runtime scratch (56 B) | +0x36C | proven (`0x69E320` passes +0x348) |

**Shader -> class** (ACiT table at `0x00DB49B0`, copied to BSS at run time): shader 0,1,2,7,10,13 -> 1;
3 -> 2; 4 -> 3; 5 -> 4; 6 -> 5; 8,11 -> 6; 9 -> 7; **12 -> 0**. QForce has 12 -> 8 (Swipe): ACiT has 7 classes
(Cam, Beam, Streak, Smoke, Flame, Distort, Trail by QForce's names, strong) and uses value **8** for "selectors
disagree" (QForce 9). Shaders used in ACiT: 0 (8,586), 3 (3,169), 5, 4, 9, 7 (1,508, distortion), 8, 10, 6, 2.

### 2.3 FxDefQuad (372), FxDefLite (376), FxDefPkg (336)

| Record | Off | Field | Label |
| --- | --- | --- | --- |
| Quad | +0x00 / +0x0C / +0x18 / +0x38 / +0x44 | colour / alpha / alpha test / size (x0.5) / scalar x1.0 | proven (`0x69CE78`) |
| Quad | +0x50 | 8 x FxTexture, count u8 +0x134 | proven (data) |
| Quad | +0x13C..+0x13F | shader bytes (+0x13D/+0x13F zeroed) | proven |
| Quad | +0x144 | lifetime (0 = permanent; forces one key) | proven |
| Quad | +0x152 | facing mode (0..2), copied to +0x13E | proven |
| Lite | +0x00 | colour keys; each key's alpha is multiplied by +0x14C (intensity) | proven (`0x69CA7C`) |
| Lite | +0x0C..+0x48 | six scalar tracks; +0x24 / +0x30 = radii (inferred, e.g. 5 / 10) | proven offsets |
| Lite | +0x148 | lifetime | proven |
| Pkg | +0x00 | ptr FxDefFX[] | proven |
| Pkg | +0xF4 / +0x100 / +0x10C / +0x12C / +0x138 | tint / scalar / scalar / scale / spin keys | proven (pointers) |
| Pkg | +0xD8..+0xE4 | distance floats (100/50/75/1000, 15/25) - cull/LOD | inferred |

### 2.4 FxDefFX (212): placed piece

QForce minus the always-zero word at +0x20.

| Off | Field | QForce | Label |
| --- | --- | --- | --- |
| +0x00 | `(index << 16) \| (type << 8)` | same | proven |
| +0x04 | start delay (s) | same | inferred |
| +0x20 | attached (1) | +0x24 | strong |
| +0x22 | orientation mode (8 = local +Z to world up) | +0x26 | strong |
| +0x23 / +0x24, s16 +0x26 / +0x28 | tilt min/max, azimuth min/max (deg) | +0x27..+0x2C | strong |
| +0x2A | offset frame | +0x2E | strong |
| (+0x34, +0x3C, +0x2C) | offset (x, y, z); spreads (+0x38, +0x40, +0x30) | (+0x38, +0x40, +0x30) | strong |
| +0x50 / +0x64 | motion-like block (mode 9, 1024) | +0x54 / +0x68 | inferred |
| +0x74 | optional spin keys (only pointer) | +0x78 | proven |
| +0xC4 / +0xC8 | restart cycle max / min (s); max >= min on all 516 non-zero | +0xC0 / +0xC4 | inferred |

## 3. Textures (proven)

`vfx_system_header 0x5200` descriptor = NV4097 register image: +0x00 offset in `0x5300`, +0x05 mips, +0x06 format
(0x86 DXT1, 0x88 DXT5 only), +0x10 remap (0xA9E4, 0x56E6, 0xAAE4...), +0x18 width << 16 | height (64..512).
A handle's index is the descriptor index. Same as QForce: the material texture decoder (remap included) reads them.
FxTexture byte +0x16 (0..3): QForce reads it as blend; in ACiT it does not split by format (DXT1: 0 x1,947 /
1 x5,736; DXT5: 0 x5,199 / 1 x8,099) - meaning **open**.

## 4. fxconduit_packed.dat (proven layout, ACiT-specific)

```
0x12000 root (32 B): +0x00 ptr u64 model TUIDs, +0x04 n, +0x08..+0x1C ptr descriptor x6
         (visual, sound, dialogue, rumble, post (empty), decal)
descriptor: +0x00 ptr u32[nrec] (0x12400)   +0x04 ptr records (0x12200, 44 B)   +0x08 nrec
            +0x0C ptr spec ptr[] (u32 count at -4)   +0x10 explicit-spec id table (as for sounds)
            +0x14 hash table { u32 buckets, u32 keys, bucket[] = record or -1 }   +0x18 open
record (44): +0x00 class (-1 any)  +0x04 EventType  +0x08 -1 (mostly)  +0x0C Tags key
             +0x10 ptr condition list { u8, u16 -1, u8 Tags } (e.g. Ratchet FOOTFELL + HERO_JUMP)
             +0x18 open  +0x20 ptr pairs { u16 spec, u8 weight (100 on 10,667 of 10,775), u8 0 }  +0x24 -1.0
visual spec (40): +0x00 0/1/2, +0x04 0/1, +0x08 0..29 (attach joint? inferred), +0x0C scale-like 1.0,
                  +0x14 -1.0, +0x18 -1, +0x1C / +0x20 effect id + 9999 (equal on 9,611 of 9,795)
```

Differences from QForce that make Resurgence's `EffectConduit.Read` fail on ACiT: the descriptor has no hash array
and no spec count (specs are a counted pointer array at +0x0C, like ToD's +0x08), the visual spec is 40 bytes with
the id at +0x1C/+0x20 (ToD: 36 bytes, +0x1C/+0x20), and `ACTIVE` is EventType 0 (QForce 3, ToD 1).

## 5. Who plays an effect

**FxController (0x3D0), proven on all 22 levels:** class data +0x14 = Tags key (all 3,235 are `Tags` values;
10 in zolar_forest hold 0 = none). Chain: key -> visual record {class 0x3D0, EventType 0 ACTIVE, +0x0C == key} ->
pairs -> spec +0x1C - 9999 -> `0x500` -> package. **3,225 / 3,225 non-zero keys resolve** (`resolve_fxc.py`).
QForce offsets minus 4: +0x0C range (256), +0x18 alternate key (7 non-zero), +0x1C start active (1,358 = 1),
+0x20 tracked, +0x24/+0x28 retrigger min/max, +0x2C retrigger chance.

Verified examples (`fxshow.py`):

| Level | Instance | Key | Effect | Content |
| --- | --- | --- | --- | --- |
| axiom_city | FX_fountain_main | AXIOM_FOUNTAIN_PILLAR | 1081 | 3 emitters: cone 0..5 deg up, speed 15, gravity (mode 9, 10 u/s2), life 2.5 s, blue; one shader-7 distortion layer |
| krell_canyon | debrisfirebig9 | FIRE_LARGE | 594 | smoke (orange -> grey, rising mode 8, drag 1, size 4 -> 10), embers, flames, attached light (5/10, offset z 3) |
| krell_canyon | Mortar_Wall_68 | MORTAR_INSTANT | 584 | four 0.2-0.3 s flash quads + a 0.1 s burst emitter |

The physically coherent fountain and fire are the check on the shifted spawn-style and motion offsets.

**Mobys and everything else:** conduit records keyed by Moby class + EventType (axiom_city visual: Ratchet 28,
Aphelion 23, Nefarious 20...; events EXPLODED, EXISTS, FLYING, TRAILING, FIRED, HIT...). Fired by game code, Lua
(`trigger_effect_event(EventTypes.X, obj)` 49, `trigger_tracked_effect_event` 99, `stop_tracked_effect_event` 77,
`register_fx_slave` 143 in level scripts) and animation clip events (animsets `0xFD00`, not traced). Attachment
joint: probably visual spec +0x08 (open). No effect id sits in Moby class data.

## 6. Engine side

| What | Address | Label |
| --- | --- | --- |
| Piece binder dispatch (types 1..15: 1 quad, 2 lite, 13 emitter) | `0x69E680` (renamed `FxDef_BindNextPiece`) | proven |
| Emitter binder | `0x69E320` (`FxDefEmitter_Bind`) | proven |
| Emitter keys binder | `0x69D430` (`FxEmtKeys_Bind`), via `0x69DE38` (+ spin) | proven |
| Quad / lite binders | `0x69CE78` / `0x69CA7C` | proven |
| Shader -> class table | `0x00DB49B0` | proven |
| SPU simulation module FXVis | image at `0x00B67900`, registered by `0x6AEB18` (name `0xAC3EA0`) | located, not disassembled |
| Other SPU jobs | `igParticle`, `igMobyParticle` (name table `0xC20CA8`) | located |

The ACiT EBOOT has no `Fx*` class-name table (QForce has one), so field names come from QForce.

## 7. Reuse of Resurgence

| Part | ACiT status |
| --- | --- |
| IGHW 0.2 + relocation reader, effect tree, 0x500 table | works as is |
| `EffectLayout.CrackInTime` | keys, textures, shader byte, distortion already right; add the offsets in 2.2-2.4 |
| Key model, curve evaluation | same |
| VFX textures (`VfxTextureSet`) | same format |
| `EffectSimulator`, `EffectSpawnStyles` (61 SPU generators), sprite batch, Vulkan effects pass | reusable; spawn indices assumed the same (open until ACiT FXVis is run in `spuemu.py`) |
| `EffectConduit` | needs an ACiT branch (section 4) |
| Old-engine (ToD) path | not relevant |

## 8. Implementation plan

1. Port Resurgence's new-engine effect stack (`EffectChunkFile`, `EffectLibrary`, `EffectModel`, `EffectKeys`,
   `VfxTextureSet`, `EffectSimulator`, `EffectSpawnStyles`, `EffectSpriteBatch`, the Vulkan pass), read-only.
2. Fill `EffectLayout.CrackInTime`: duration 0x314, spawn style 0x318, motion 0x328, beam 0x1E0, streak 0x1EC,
   direction 0x24C, spread 0x280, quad lifetime 0x144, the FxDefFX placement offsets (2.4), and the ACiT shader -> class
   table (12 -> 0, disabled = 8).
3. Add the ACiT conduit branch: descriptor +0x04/+0x08/+0x0C, 40-byte spec, id at +0x1C, ACTIVE = 0.
4. FxController in `ACrackInTime.fields`: play the effect from +0x14 (Tags key) through the conduit; show the Tags
   name; retrigger from +0x24/+0x28.
5. Later: run ACiT FXVis (`0xB67900`) in Resurgence's `spuemu.py` to confirm the spawn generators; trace Moby
   attachment (spec +0x08) and animation clip events; blend byte +0x16.
