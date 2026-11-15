# A Crack in Time: audio (NPUA80966)

How a sound id in the level data (SoundEmitter / SoundEnvironment `+0x14`, `play_explicit_sound_spec`) becomes a named
sample, and where the samples are. Proven on all 22 levels unless marked. Tooling: `dev/tools/audio/` (gitignored):
`resolve.py <level> [id...]` resolves every SoundEmitter / SoundEnvironment of a level, `bank.py`, `sblk.py`,
`part.py` (ranged PSARC read), `census.py` (every level). `dev/tools/soundprobe` runs ReLunacy's own
`SoundReader` headless (`soundprobe <packed/levels> [level] [wav folder]`: list, timings, decode every sound).

ReLunacy: `Loading/Readers/SoundReader.cs` (tables only, in the background on first use, a few ms per level),
`Loading/IO/PsarcRanges.cs` (ranged reads), `Assets/Audio/` (model, PS-ADPCM, `ScreamBank`, `ScreamSound`: the grain
interpreter and mixer of section 6); field type `sound` in the .fields file. `dev/tools/audio/grains.py <level> <name|index>`
prints a sound's grains; `soundprobe <levels> <level> <wav folder> all` runs every bank sound.

## 1. Files

| File | Where | Size | What |
| --- | --- | --- | --- |
| `/scripts/soundspecs.lua` | `packed/game/global_cached.psarc` | 62 KB | `SoundSpecs` enum: name -> id, 1,287 ids (0..10, 10001..11276). Global. |
| `/scripts/soundgroups.lua` | same | 4 KB | `SoundGroups` enum (mixer groups). |
| `/built/game/sound_config.dat` | same | 9 KB | IGHW: mixer group table (MASTER, SFX, MUSIC, DIALOGUE, SFX_AMB_*...). Not needed for names. |
| `built/levels/<level>/fxconduit_packed.dat` | each `level_cached.psarc` (front_end: global_cached) | 155-352 KB | Effects conduit: **SoundSpecs id -> sound effect** table (section 2). |
| `built/levels/<level>/resident_sound.dat` | each `level_cached.psarc` (front_end: global_cached) | 9-34 MB | IGHW: sound names + Scream bank (PS-ADPCM samples in memory) + stream offsets. |
| `built/levels/<level>/streaming_sound.dat` | each `level_uncached.psarc` (front_end: global_cached) | 50-190 MB | Streamed sounds: `VAGp` (mono) or `XVAG` (multichannel) files back to back, 0x800-aligned. |
| `resident_dialogue.<lang>.dat`, `streaming_dialogue.<lang>.dat`, `dialogue.<lang>.pkg` | level archives; global ones in `sound/global/` of global_cached / global_uncached | 22 KB-143 MB | Dialogue: same IGHW name/stream layout, no Scream bank. Not read by ReLunacy yet. |

Every level has exactly one resident bank, one streaming file and one conduit; there is no game-wide sound bank in
use by the levels (front_end's bank is only the front end's). A sound not in a level's conduit cannot play there.

## 2. fxconduit_packed.dat (IGHW 0.2, big-endian, pointers are file offsets)

- Section `0x12000` (first entry of the section table): word 2 = **root** struct.
- root `+0x24` u32 effect count, `+0x28` pointer to `count` pointers to 0x30-byte **sound effect records**,
  `+0x2C` pointer to the **id table**.
- Id table: `u32 last`, `u32 base`, then `last + 1` s32 entries. Index = `id` for id < 10000, else `id - 10000 + base`
  (base = 11 in every level, last = 1288). Entry -1 = not in this level. Same lookup as the EBOOT's 0x6653C4, called
  by the play function 0x577FD8 (behind 0x95ECB0, the call SoundEmitter / SoundEnvironment use).
- Effect record: `+0x04` u32 flag (0/1, meaning not traced), `+0x18` u32 **sound name index** (into the level's
  resident_sound.dat name table), `+0x1C` u32 SoundGroups id (e.g. 10019 SFX_PLAYER_FOLEY, 10012 SFX_AMB_SPACE),
  `+0x20` f32 volume (inferred), `+0x24..+0x2C` more floats / flags.

## 3. resident_sound.dat (IGHW 1.1)

| Section | Content |
| --- | --- |
| `0x21200` | Name table: 64-byte NUL-padded names, one per sound (230..765 per level). The only good names: `amb_bathroom_dko`, `ratchet_dirt_steps_vag`... (SoundSpecs names differ: `AMB_MUSEUM_Bathroom`). |
| `0x21300` | `u32 count`, 12 bytes, then `count` u32, one per name: high 16 bits 0 = resident Scream sound, 1 = stream; low 16 bits = index. |
| `0x21000` | 0x90-byte wrapper (`+0x04` = resident count), then a **Scream bank** (`.bnk`, version 3): `+0x00` 3, `+0x04` 2 parts, `+0x08` SBlk offset 0x20, `+0x0C` SBlk size, `+0x10` sample data offset, `+0x14` data size (offsets from the bnk start). |
| `0x21100` | `+0x80` three pointers A, B, C: A..B is the u32 offset of each stream in streaming_sound.dat. |

SBlk (`klBS`, v3): `+0x16` u16 sounds, `+0x18` u16 grains, `+0x1C` sound table, `+0x20` grain table, `+0x34` grain
data, `+0x38` 16-char legacy names (incomplete, ignored). The grains are a small program per sound; section 6 has it.

Samples: PS-ADPCM (16-byte frames, end flag 1 or 7) or 16-bit PCM (tone flag 0x80); section 6. Streams: `VAGp` (u32 size at 0x0C, rate at 0x10, data at 0x40)
or `XVAG` (header size at 0x04; `fmat` chunk: channels, codec 6 = PS-ADPCM, samples, rate at +0x1C; 0x10 interleave).

## 4. Chain, verified

`SoundSpecs id -> conduit id table -> effect record +0x18 -> name[i] in 0x21200 -> 0x21300[i] -> Scream sound / stream`.
Every SoundEmitter (706) and SoundEnvironment (118) with a non-zero id in all 22 levels resolves, none missing
(`census.py`; 230 environments and 1 emitter hold 0 = no sound). Examples:

| Level | Instance | Id | SoundSpecs | Sound | Bank |
| --- | --- | --- | --- | --- | --- |
| insomniac_museum | AUD_E_Picture_Music | 10893 | AMB_MUSEUM_Eight_Bit_Muswic | music_dwight_room01_dko_stream | stream 0 (VAGp 8 kHz) |
| insomniac_museum | AUD_ENV_Bathroom | 10934 | AMB_MUSEUM_Bathroom | amb_bathroom_dko | resident 0 |
| axiom_city | aud_techBeep19 | 10169 | amb_axiom_techBeeps | amb_axiom_techbeeps_jcm | resident 223 |
| krell_canyon | aud_waterfallTall3 | 10068 | amb_krell_waterFallTall | amb_krell_waterfalltall_jcm | resident 248 |
| space_sector_1 | aud_moon1a_osc_emitter | 10057 | amb_space_oscillator01 | amb_space_oscillator01b_44s_jcm_stream | stream 82 |
| zolar_forest | AUD_E_River_7 | 10020 | Gimlick_Stream | amb_gimlick_stream_loop_dko | resident 95 |

Effects not reached from a SoundSpecs id (e.g. 982 effects, 204 ids in the museum) are played by other means
(animation / conduit triggers), not traced. Moby settings were not surveyed for sound ids.

## 5. All 4 One / Into the Nexus

Same `soundspecs.lua` and `fxconduit_packed.dat`, but no per-level resident bank: banks live per sound asset under
`/built/sound/bank/<xx>/<id>/resident_sound[.lang].dat` + `streaming_sound[.lang].dat` in `packed/game/global_sound_*.psarc`
and global_cached. The conduit-to-bank link there is not traced; ReLunacy reads A Crack in Time only.

## 6. Scream sounds (grains), as the EBOOT runs them

Scream ("SCREAM Version", `common/grains.cpp`) runs on the PPU. Proven from the EBOOT (addresses below) and checked on
all 22 banks (every sound runs, none stuck after a stop); the stereo pan law and the bend direction of `+8`/`+9` are
inferred, ReLunacy's `Assets/Audio/ScreamSound.cs` follows this section.

**Sound** (12 bytes, sound table): `+0` volume 0..127, `+1` volume group, `+2` s16 pan (degrees), `+4` grain count,
`+5` instance limit (inferred), `+6` u16 flags (8 = limited), `+8` offset of its first grain in the grain table.

**Grain** (8 bytes): `+0` u32 opcode: type in the top byte, then either three argument bytes (b1 b2 b3) or a 24-bit
offset into the grain data; `+4` s32 delay in ticks before it runs. **240 ticks a second** (0x983768: output samples /
(rate / 240)).

**Runner** (0x979510; tick 0x977B10): a sound starts with its first grain due after that grain's delay. Each tick
counts down; at zero the next grain runs, then the one after waits its own delay plus what the grain returned (a
negative return ends the grains). Dispatch: table of 52 descriptors at 0xDE3BFC (types 12..19 invalid). A sound ends
when its grains, voices and child sounds have ended. Stopping a sound (0x9828B8, 0x9779B8): its children stop, the
grains after an **On Stop Marker** run at once (no delays), voices are keyed off into their release.

| Type | Name (EBOOT) | Handler | Arguments / data | What it does |
| --- | --- | --- | --- | --- |
| 0, 2, 3, 20 | NULL, XREF ID, XREF Num, Null | 0x97DBD8.. | | nothing (20 pads Random Play slots: an empty pick is silence) |
| 1, 9 | Start tone grain | 0x97D820 | tone, 0x18 bytes (below) | starts a voice |
| 4 | LFO | 0x97D640 | 0x10 bytes (below) | starts / stops one of 4 LFOs |
| 5 | Start Child Sound | 0x97A5A0 | `+0` s32 vol, `+4` s32 pan, `+8` flags, `+C` s32 sound (<0: by name at `+10`) | plays another sound alongside |
| 6 | Stop Child Sound | 0x97A158 | `+C` sound | stops the children playing it |
| 7 | Plugin | 0x97B270 | | game plugin message; ignored |
| 8 | Branch To Child Sound | 0x97AE00 | `+9` keep voices, `+C` sound | this sound becomes that one (its volume and pan) |
| 10, 11 | Ducker, Reverb | 0x97A068, 0x97A030 | | mixer effects; ignored |
| 21 | Loop Start | 0x97D608 | | marks the loop |
| 22 | Loop End | 0x97D4F0 | | back to the Loop Start before it; a second loop in one tick waits 1 tick |
| 23 | Loop Continue | 0x97D3D8 | | leaves the loop (to after its Loop End) |
| 24 | Stop Sound | 0x97D3A0 | | ends the grains |
| 25 | Random Play | 0x97D208 | b1 options, b2 grains each, b3 last pick (written back) | runs one of the next b1 runs of b2 grains, skips the rest; never the last pick twice |
| 26 | Random Delay | 0x97CC58 | 24-bit max | waits rand % (max + 1) ticks |
| 27 | Random Pitchbend | 0x97C948 | b1 percent | bend = rand(-1..1) * b1 / 100 |
| 28, 29 | Set / Add Pitchbend | 0x97C858, 0x97C758 | b1 s8 | bend (+)= b1 / 127 (b1 / 128 when negative), clamped -1..1 |
| 30 | Set Register | 0x97C678 | b1 register, b2 value | |
| 31 | Set Register Random | 0x97C568 | b1 register, b2 low, b3 high | register = low..high |
| 32, 33 | Increment / Decrement Register | 0x97C450, 0x97C338 | b1 register | clamped -128..127 |
| 34 | Test Register | 0x97C1F0 | b1 register, b2 op, b3 value | the next grain runs only if register <, ==, >, != value (op 0..3) |
| 35 | Marker | 0x97C1B8 | b1 id | target of Goto |
| 36 | Goto Marker | 0x97C028 | b1 id | jumps there (max 8 gotos a tick, then waits a tick) |
| 37 | Goto Random Marker | 0x97BE58 | b1 low, b2 high | a random marker id |
| 38 | Wait For Voices | 0x97B860 | | waits (1 tick at a time) until this sound's voices and children have ended |
| 39 | Play Cycle | 0x97B760 | as Random Play, b3 = next | the runs in turn |
| 40 | Add Register | 0x97B610 | b1 amount, b2 register | |
| 41 | Key Off Voices | 0x97B578 | | voices go to release |
| 42 | Silence Voices | 0x97B4E0 | | voices cut |
| 43 | On Stop Marker | 0x97B488 | | ends the grains; the ones after run when the sound is stopped |
| 44 | Copy Register | 0x97B310 | b1 from, b2 to | |
| 45, 49, 50 | Stream, Stream MultiTrack Volumes (Ex) | 0x97ACD8.. | | streamed tracks (no resident sound uses them) |
| 46 | Goto Random Marker Ex | 0x97B8F8 | data `+0` low, `+1` high, history | as 37 without repeats (unused in ACiT) |
| 47 | Delay | 0x97CA48 | data `+0` mode, `+1` register, `+2` u16 | mode 2: wait rand % u16; 6: rand % register; 4: register |
| 48 | Random Play Ex | 0x97CCD8 | data `+0` options, `+1` grains each, `+2` mode, `+3` depth, `+4` index, `+5..+20` history, `+21` state | mode 1: no pick among the last `depth` (depth = options - 1: a shuffle) |
| 51 | Wait For Sync Event | 0x97A370 | | music sync; no wait without music |

Registers: 0..3 are the sound's own (signed bytes, handler `+0x12C`), negative numbers are 64 global ones (`~n`).
Random Play's and Random Play Ex's state lives in the bank, so the no-repeat rule holds across plays.

**Tone** (0x18 bytes, more with flags 0x100 / 0x4000): `+0` priority, `+1` s8 volume (0..127; -1..-4 register
0..3, -5 random, -6.. global register; 0x979A28), `+2` s8 center note, `+3` fine, `+4` s16 pan (degrees; -1..-4
register * 360 / 127, -5 random, -20007..-20000 speaker pans; 0x9797A0), `+8` bend range down, `+9` up (semitones),
`+A` ADSR1, `+C` ADSR2 (PS2 SPU2 envelope, 0x98C648), `+E` flags, `+10` sample offset, `+14` size.
Rate (0x9790F8): 48000 * 2^((60 - |note| + fine / 128) / 12), fine below 127 counting from the semitone under
(0xC4/0x00 = 48 kHz, 0xB6/0x42 = 22050). Flags: 0x80 16-bit big-endian PCM after a 16-byte header, else PS-ADPCM;
0x40 loops (ADPCM: trailing block `-1, loop start sample, ...`, else the frame flagged 6); 0x2000 loops from the start;
0x8 noise (unsupported); 0x100 effect data at `+18`; bits 1-2 pick 0 / 0.2 / 0.5 / 1 for the voice (a send level; not traced). The last 16 bytes of a
sample are never audio.

**Voice**: volume = tone / 127 * sound volume * LFO volume (0x984130); pan = tone + sound + LFO pan (0x983C70);
pitch = rate * 2^(c / 1536), c = bend * range (down or up) * 128 + LFO pitch, clamped to 5 octaves (0x985EF8). The
sound's bend applies to its voices already playing.

**Start Child Sound** flags: low nibble 0 = vol * this sound's, 1 = * the child's own, 2 = both, else the grain's alone;
bits 4-6 the same for pan (added). The child starts like any sound.

**LFO** (0x97D640, applied 0x97DFE8): `+0` slot 0..3, `+1` target (0 stop, 1 volume, 2 pan, 3 pitch, 4 bend, 5 rate of
LFO `+2`, 6 its depth), `+3` shape (1 sine from a cosine table started a quarter in, 2 square with duty `+4`, 3
triangle, 4 saw, 5 random step each half cycle), `+6` u16 depth (1024 = full), `+9` flags (1 invert, 2 random phase),
`+A` u16 phase (2048 a cycle), `+C` u32 phase step a tick (0x8000000 a cycle: 0x88888 = 1 Hz). Volume = 1 - (w + 1) / 2
* depth; pan = w * depth * 360; pitch = w * depth * 7680 (1/128 semitones); bend = w * depth.

Examples (insomniac_museum): `ratchet_dirt_steps_vag` = random volume, random bend, a tremolo LFO, one of 4 dirt
layers, then 10 ticks later one of 5 step layers; `amb_bathroom_dko` = a looping bed, a child sound looping drips
every 0..1000 ticks, and its own loop of 4 drips at random pans every 0..3000 ticks; `ratchet_moneybagged_dko_cro`
(agorian_arena) = 75 bolt clinks without repeats, 6..12 ticks apart, then a final tone.
