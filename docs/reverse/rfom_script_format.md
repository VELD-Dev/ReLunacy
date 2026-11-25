# Resistance: Fall of Man level scripts (`ps3script.dat`)

Fall of Man levels are scripted without Lua. Each level's `ps3script.dat` holds **scripts**: named
chains of **script elements**, which are ordinary GameClass objects (`ScriptElem_*`, class ids
0xFB-0x5xx) stored as settings blocks, like the gameplay classes. The game steps every script once
a frame, element by element, until an element yields.

Sources: the retail files (NPEA00430 `game.psarc`, 41 levels: 56 setups, 2,351 scripts, 23,165
elements) and `rfom_game.elf` (Ghidra project `/Resistance Fall of Man/rfom_game.elf`). Every
structural claim below was checked against all 41 levels by `dev/tools/rfomscript/elements.py`,
with zero exceptions. Anything not proven is marked **inferred** or **unknown**.

## Files

Per level, in `game.psarc` under `/packed/levels/<level>/`:

| File | Content |
|---|---|
| `ps3script.dat` | the scripts (this document) |
| `ps3script.<lang>.pkg` | 10 languages (`e u f i g s d p j k`); IGHW with section `0x2600`, a text table, see [Localized table](#localized-table-ps3scriptlangpkg) |

The game asks for `%s/%d_level_script.pkg` (0x514F4 path, strings at seg_0001/seg_0005), the
build-time name; the retail archive stores the files under the names above.

## Container

IGHW **version 0.2** (`49 47 48 57 00 00 00 02`), as `ps3gameplay.dat`:

```
+0x00  "IGHW"
+0x04  u16 0, u16 2          version 0.2
+0x08  u32 sectionCount
+0x0C  u32 0
+0x10  sectionCount x { u32 id, u32 offset, u32 size, u32 0 }
```

Every integer is big-endian. All pointers are **absolute file offsets**. The only named section
is **`0x25100`**, the root (8 bytes). The other sections have id `0xFFFFFFFF`: anonymous data
blocks, reached only through pointers. Blocks are padded to 16 bytes with `PAD0`, `PAD1`, …
words.

After the last block (aligned to 16, padding skipped) comes the **pointer fix-up table**:
`u32 count`, then `count` u32 file offsets, one per pointer word in the file. A writer that moves
anything must keep this table in step. Every element `next`/`else` pointer is listed in it.

## Root (`0x25100`)

```
+0x0  u32 setups      -> setup[count], 0x60 each
+0x4  u32 count       1 in story levels, up to 7 in multiplayer levels
```

The level loader at **0x514F4** maps gameplay `0x25000` and script `0x25100` together and passes
the root to **0x472A4C**. **0x472A7C** selects the setup whose name equals the current gameplay
group's name. If none matches, it uses the first setup.

Script setups are a subset of the gameplay groups. Story levels have one, `default`. Multiplayer
levels have one per game mode (`largedm`, `largenode`, … `tinydm`), and none for the editor-only
groups (`occlusion`, `*.config.<user>`).

## Setup (0x60)

| Off | Type | Meaning |
|---|---|---|
| +0x00 | ptr | scripts, `script[count]` (0x20 each) |
| +0x04 | ptr | element pointer list, `ptr[elementCount]` |
| +0x08 | ptr | element sizes, `u16[elementCount]`; each equals `GameClass::SizeOf` of its class |
| +0x0C | ptr | sub-setup names, `ptr[subSetupCount]` → C strings |
| +0x10 | u32 | script count |
| +0x14 | u32 | element count |
| +0x18 | u32 | sub-setup count |
| +0x1C | u32 | 0 in every file; **unknown** |
| +0x20 | char[0x40] | setup name, matches a gameplay group name |

**Sub-setups** are a second, script-only selector. Most levels have only `None`. level20 has
`None`, `Attract Camera 1..3`. `ScriptElem_SetCurrentSetup` (0x47BD94) stores
`min(value, subSetupCount)` in the current sub-setup (global 0x1034B6E4).
`ScriptElem_IsSetupActive` tests it.

## Script (0x20)

| Off | Type | Meaning |
|---|---|---|
| +0x00 | ptr | name (C string) |
| +0x04 | ptr | **runtime**: current element; 0 on disk |
| +0x08 | u32 | first element, as an index into the setup's element list; always a `ScriptElem_Start` |
| +0x0C | f32 | **runtime**: seconds spent on the current element; 0 on disk |
| +0x10 | u32 | **runtime**: finished; 0 on disk |
| +0x14..+0x1C | | 0 in every file |

Script names are the designers' labels (`1A_VTOLs_FlyAway`, `3C_Reveal_EndShamblesSniper`,
`0load+`). Nothing makes a name special to the engine. A script starts at its `Start` element and
runs from level load.

## Element

Each element is a settings block: the object image of its class, as for gameplay classes.
`+0` holds the class id on disk and the vtable once bound. Elements are stored back to back in
list order (the sizes table gives each one's length). Every script's chain ends at a
`ScriptElem_End`.

Base fields (`ScriptBaseElem`, 0xE9, 0x3C bytes):

| Off | Type | Meaning |
|---|---|---|
| +0x00 | u32 | class id |
| +0x04 | ptr | **next**: the next element (command), or the target when true (condition); 0 = none |
| +0x08 | ptr | **else**: the target when false (condition only; 0 in every command) |
| +0x0C | ptr | runtime: the element that jumped here, linking a chain of conditions still being watched |
| +0x10 | ptr | runtime: the branch last taken |
| +0x14 | ptr | runtime: display name (set by slot 3) |
| +0x18 | ptr | runtime: registered field list (see slot 3) |
| +0x30 | u32 | runtime: class id (the return of `GameClass::BindSettings`), kept for `GameClass::IsA` after +0 becomes the vtable |
| +0x34 | u32 | runtime: registered field count |
| +0x38 | u32 | debug override: bit 0 forces the true branch, bit 1 the false branch (0x472E00); 0 in every file |

+0x0C..+0x37 are 0 on disk in every element.

`ScriptConditionElem` (0xFA, 0x40 bytes) adds:

| Off | Type | Meaning |
|---|---|---|
| +0x3C | u32 | **watch mode**: what happens after the branch has been taken; counts across all levels in brackets |

- 0 (2,379): evaluated once.
- 1 (503): while the script is on the **false** branch, it keeps re-evaluating, and jumps to the true target when the condition turns true.
- 2 (13): while on the **true** branch, it jumps to the false target when the condition turns false.
- 3 (39): both.

The watch is 0x472EC4. It walks the `+0x0C` chain from the script's current element on every
frame, before the script steps. The jump needs a target: mode 1 needs a true target (+0x04),
mode 2 a false target (+0x08).

Class-specific fields start at +0x3C (commands) or +0x40 (conditions). See the
[table](#element-classes).

### Vtable slots

| Slot | Role | Base behaviour |
|---|---|---|
| 0, 1 | Destruct, Delete | |
| 2 | **kind** | `ScriptCommandElem` returns 2, `ScriptConditionElem` 1, `ScriptElem_End` 3 (end of script) |
| 3 | **register** | sets the display name at +0x14 and appends `{tag, field address}` pairs for its authored fields (helpers 0x473840-0x4738C0) |
| 4 | **execute** | returns the element to continue with: `this` to stay (yield), `+0x04`/`+0x08` to move on, 0 to stop |
| 5 | nugget evaluate | Truth-Nugget classes only, see below |

Register tags (the setters write `{tag, pointer}` into an 8-byte entry):

| Tag | Helper | Field |
|---|---|---|
| 1 | 0x473890, `kind` = 0 | target: Moby |
| 4 | 0x473890, `kind` = 1 | target: Pod |
| 3 | 0x4738C0, `kind` = 0 | area: volume |
| 2 | 0x4738C0, `kind` = 1 | area: area (volume list) |
| 6 | 0x473840 / 0x473850 / 0x473860 | number (f32 or u32; the three helpers are identical) |
| 7 | 0x473880 | text |
| 8 | 0x473870 | variable pointer |

The consumer of this list was not found by searching the decomp. Its 0x20000-byte buffer is
allocated at 0x472DB0. `ScriptElem_Wait` also registers its run-time timer (+0x40), so the list is
not limited to authored fields.

## Execution

**Setup start, 0x472C30.** For every script it clears `finished` and sets
`current = elements[first]`. For every element it binds the settings (`GameClass::BindSettings`)
and calls slot 3.

**Step, 0x48F8E4(pass).** For every script:

1. Pass 1 handles scripts whose current element is a `ScriptElem_RunAtLoad` (0x127); pass 0
   handles the rest. Callers: 0x1F8F0 runs pass 1, 0x4172C runs pass 0.
2. Run the watch (0x472EC4).
3. If the script is not finished, loop. `kind < 1` → stop. `kind == 3` → mark finished, stop.
   Otherwise `next = execute`. A result of 0 stops the loop; `this` yields until the next frame.
   Any other element becomes current, its `+0x0C` is pointed back at the previous element, and
   the time on the element is reset. At most **0x40D** steps a frame. A loop back to an element
   already on the back-link chain ends the step.
4. If the loop stopped on a command (kind 2), the script is finished.
5. Add the frame time to the script's +0x0C.

So a command that finishes at once costs nothing: a chain of instant commands runs in one frame.
Waiting elements (`Wait`, `WaitOnScriptEnd`, `TN_If` in wait mode, …) return `this`.

### References

| Shape | Fields | Resolves to |
|---|---|---|
| target `{u32 kind, s32 index}` | e.g. `Reveal` +0x3C | kind 0: Moby; kind 1: Pod (global table 0x1034897C, 8-byte `{count, list}`). Index < 0 = unset |
| area `{u32 kind, s32 index}` | e.g. `InArea` +0x48 | kind 0: volume (0x90 stride, the gameplay volume table, 0xBD7AC); kind 1: area (0x60 stride, 0x10311858: +0x04 `u16` volume indices, +0x4C count; 0x458F48). Index < 0 = unset |
| variable | `ptr` → f32 | see below |
| script | `ptr` → a script record's name string | `WaitOnScriptEnd` +0x40 compares **pointers**, not text (0x48A180) |

Indices are into the tables of the gameplay group with the same name as the setup, which
`FallOfManGameplayReader` reads.

`WaitOnScriptEnd`: 7 of 318 uses point at a copy of a name string that no script record owns.
Nothing matches, so those elements never wait. A writer that rebuilds strings must keep a
referenced name at the address the script record uses.

### Variables

Variables are f32 slots in an anonymous block (level20: 8 slots, 0x20 bytes), reached only
through element pointers, which are all in the fix-up table. They have no names and no table:
the slot address is the variable's identity. `MakeTrue` writes 1.0, `MakeFalse` 0.0. `SetValue`,
`IncrementVar`, `DecrementVar` and `ComputeValue` change them. `VarIsTrue`, `VarIsValue`,
`WaitVariable`, `EndOnVar` and Truth-Nugget comparisons read them.

### Truth Nuggets (structured conditions)

A second, structured way to branch, used alongside the condition elements:

```
TN_If  [nugget]*  TN_Then ... [TN_Else ...] TN_EndIf
TN_While [nugget]* TN_Do ... TN_EndWhile
```

- On its first execute, `TN_If` (0x4896D4) searches forward along `next` for its `TN_Then`
  (0x221), `TN_Else` (0x222) and `TN_EndIf` (0x223), and stores them at +0x50/+0x54/+0x58. It
  writes itself into each one's +0x3C, and the EndIf into the Else's +0x40.
- Nuggets are the elements between `TN_If` and `TN_Then` that are `TN_BaseNugget` (0x224)
  subclasses. 0x4895D8 folds them in order into an accumulator that starts **true**: each one's
  slot 5 receives the accumulator and returns the new one. The op is the nugget's +0x3C
  (0x489A40): 0 AND, 1 AND NOT, 2 OR, 3 OR NOT, 4 XOR, 5 XNOR.
- If the result is true, execution continues after `TN_Then`. If false, it continues after
  `TN_Else`, or after `TN_EndIf` when there is no Else. When the Then branch reaches the Else, the
  Else jumps to the EndIf.
- `TN_If` +0x3C = 1 waits (returns `this`) until the result is true.
- `TN_CompareVals` (0x225): A = +0x40, B = +0x50. A value of exactly **1337.0** means "read the
  variable at +0x44 / +0x54 instead". +0x48 is the test: 0 `A > B + d`, 1 `A < B + d`,
  2 `|A − B| < d`; d = +0x4C.
- `TN_MobyPropQuery` (0x226) switches on +0x40 over Moby property readers (0x49EB04). The case
  meanings are **unknown**.

### Text

- `DisplayMessage` +0x3C is a 32-bit message id (e.g. `0x08C16909`), looked up in a global table
  (0x5179A0 on 0x101A5AE0). It is not a `.pkg` id.
- `SquadMessage` +0x40 points at a C string in the file.
- `SetObjText` registers +0x3C as text (tag 7). No shipped level uses it.

## Localized table (`ps3script.<lang>.pkg`)

IGHW 0.2, one section `0x2600`:

```
+0x00 u32 count
+0x04 u32 ?          (points at a 4-byte block)
+0x08 u32 entries -> { u32 id, u32 string } [count]
+0x0C u32 strings
```

Strings are UTF-8 and include private-use glyphs (e.g. `E0 AB 9B`, inferred to be button icons).
Contents are short names (`bullets`, `Leaper Pod`, `Howler`, `sabots`). **None of the 328 ids
(English, all levels) occurs as a word in any `ps3script.dat`**, and only 2 occur in
`ps3gameplay.dat`. Their consumer is **unknown**.

## Layout seen in story levels

One setup gives 10 sections. level20, in file order:

| Offset | Size | Block |
|---|---|---|
| 0xB0 | 0x60 | setup |
| 0x110 | 0x1620 | scripts (177) |
| 0x1730 | 0x1287 | strings (script and sub-setup names) |
| 0x29C0 | 0x14C4 | element pointer list (1,329) |
| 0x3E90 | 0x17FB0 | elements |
| 0x1BE40 | 0x20 | variables |
| 0x1BE60 | 0xA62 | element sizes |
| 0x1C8D0 | 0x10 | sub-setup name pointers |
| 0x1C8E0 | 0x60 | a second 0x60 record: the setup's bytes from +0x10 on, different first 16 bytes; not referenced by the loader (**unknown**) |
| 0x1C940 | 0x8 | root `0x25100` |

then the fix-up table (0x9E8 pointers). Block order is not fixed (level101 puts the root at
0x10E0), so readers go through the pointers.

## Element classes

Generated by `dev/tools/rfomscript/elements.py`: the 220 classes the shipped levels use, by use
count.

- **display name** is the string slot 3 passes to 0x47391C.
- **fields** joins the slot-3 registrations (labelled with the tag names above) with the words
  that are non-zero in some instance (`value`) and the pointer words in the fix-up table (`ptr`,
  with what they point at).
- A `target`/`area` field covers 8 bytes (`kind`, `index`). For `WaitOnScriptEnd` +0x40 read
  "script" (see References).
- Unregistered `value` words have no name yet. Their meaning comes from each class's slot 4 in
  `rfom_game.elf` (`ScriptElem_<name>::vf04`).

| id | class | display name | kind | size | uses | fields |
|---|---|---|---|---|---|---|
| 0xFB | Start | Start | command | 0x3C | 2351 | - |
| 0xFC | End | End | command | 0x3C | 2351 | - |
| 0x11E | Wait | Wait | command | 0x44 | 1735 | +0x3C number, +0x40 number |
| 0x12A | Reveal | Reveal | command | 0x44 | 1366 | +0x3C target |
| 0x10A | InArea | In Area | condition | 0x60 | 1051 | +0x40 target, +0x48 area, +0x50 value, +0x54 number, +0x58 number |
| 0x129 | Vanish | Vanish | command | 0x44 | 729 | +0x3C target |
| 0x226 | TN_MobyPropQuery | Truth-Nugget Moby Property Query | command | 0x5C | 565 | +0x3C value, +0x40 value, +0x44 value, +0x48 value, +0x4C value, +0x50 value, +0x54 value, +0x58 value |
| 0x1CA | CheckpointIfNotComplete | Checkpoint If Not Complete | condition | 0x44 | 486 | +0x40 number |
| 0x1F6 | SetValue | Set Value | command | 0x44 | 469 | +0x3C variable (ptr variable), +0x40 number |
| 0x223 | TN_EndIf | Truth-Nugget End-If | command | 0x40 | 450 | +0x3C number |
| 0x173 | SquadCreate | Squad Create | command | 0x40 | 447 | +0x3C value |
| 0x220 | TN_If | Truth-Nugget If | command | 0x5C | 447 | +0x3C value, +0x40 value |
| 0x221 | TN_Then | Truth-Nugget Then | command | 0x40 | 447 | +0x3C number |
| 0x17B | SquadBFDefendArea | Squad Defend Area | command | 0x5C | 442 | +0x3C value, +0x40 value, +0x44 value, +0x48 value, +0x4C number, +0x50 number, +0x54 value, +0x58 number |
| 0x1D6 | PlayDialogue | Play Dialogue | command | 0x6C | 406 | +0x3C target, +0x44 value, +0x48 value, +0x4C value, +0x50 value, +0x58 value, +0x60 value, +0x64 value, +0x68 value |
| 0x104 | IsDead | Is Dead | condition | 0x50 | 381 | +0x40 target, +0x48 value, +0x4C number |
| 0x179 | SquadSetRespawnData | Squad Set Respawn Data | command | 0x4C | 339 | +0x3C number, +0x40 value, +0x44 value, +0x48 value |
| 0x13C | GoTo | GoTo | command | 0x50 | 335 | +0x3C target, +0x44 value, +0x48 value, +0x4C value |
| 0x230 | WaitOnScriptEnd | Wait For Script To Finish | command | 0x48 | 318 | +0x40 ptr script/string |
| 0x125 | Activate | Activate | command | 0x48 | 311 | +0x3C target, +0x44 value |
| 0x151 | HoldPosition | Hold Position | command | 0x54 | 297 | +0x3C target, +0x44 area, +0x4C value, +0x50 value |
| 0x160 | SetDamagePerc | Set Damage Percentage | command | 0x4C | 297 | +0x3C target, +0x44 number, +0x48 number |
| 0x174 | SquadMessage | Squad Message | command | 0x44 | 296 | +0x3C value, +0x40 text (ptr script/string) |
| 0x1D5 | AddMusicCueToTrack | Add Music Cue To Track | command | 0x54 | 285 | +0x3C value, +0x40 value, +0x44 value, +0x48 value, +0x4C value, +0x50 value |
| 0x1E7 | SimpleMotion | Simple Motion | command | 0x48 | 284 | +0x3C value, +0x40 value, +0x44 number |
| 0x177 | SquadBFSetData | Squad Battlefield Set Data | command | 0x54 | 280 | +0x3C value, +0x40 number, +0x44 number, +0x48 number, +0x4C value, +0x50 value |
| 0x22F | EndOnVar | Terminate Script If False | command | 0x40 | 277 | +0x3C ptr variable |
| 0x167 | PlayerWeaponAdd | Player Weapon Add | command | 0x54 | 267 | +0x3C target, +0x44 value, +0x48 value, +0x4C value, +0x50 value |
| 0x161 | SetHomeArea | Set Home Area | command | 0x48 | 244 | +0x3C target, +0x44 value |
| 0x10B | IfPlayerIsInside | If Player Is Inside | condition | 0x48 | 229 | +0x40 area |
| 0x1F2 | MakeTrue | Make True | command | 0x40 | 202 | +0x3C variable (ptr variable) |
| 0x154 | InnateBehavior | Innate Behavior | command | 0x44 | 190 | +0x3C target |
| 0x217 | SwarmerFollowPath | Swarmer Follow Path | command | 0x60 | 186 | +0x3C target, +0x44 number, +0x48 value, +0x4C value, +0x50 value, +0x54 value, +0x58 value, +0x5C value |
| 0x119 | VarIsTrue | Is Var True | condition | 0x44 | 184 | +0x40 variable (ptr variable) |
| 0x175 | SquadFollow | Squad Follow | command | 0x60 | 176 | +0x3C value, +0x40 value, +0x44 value, +0x48 number, +0x4C value, +0x50 number, +0x54 number, +0x58 number, +0x5C number |
| 0x11A | VarIsValue | Var Is Value | condition | 0x48 | 165 | +0x40 variable (ptr variable), +0x44 number |
| 0x144 | SetTargetPreference | Set Target Preference | command | 0x50 | 165 | +0x3C target, +0x44 number, +0x48 value, +0x4C number |
| 0x155 | SetAwareness | Set Awareness | command | 0x50 | 113 | +0x3C target, +0x44 value, +0x48 area |
| 0x1CB | CheckpointLoaded | Checkpoint Loaded | command | 0x40 | 108 | +0x3C number |
| 0x157 | MeleeAttack | Melee Attack | command | 0x4C | 101 | +0x3C target, +0x44 target |
| 0x163 | SetPlayerPosition | Set Player Position | command | 0x54 | 99 | +0x3C target, +0x44 area, +0x4C area |
| 0x1CC | CheckpointLoadedOrFurther | Checkpoint Loaded Or Further | command | 0x40 | 95 | +0x3C number |
| 0x126 | Deactivate | Deactivate | command | 0x48 | 90 | +0x3C target, +0x44 value |
| 0x152 | JumpDown | Jump Down | command | 0x50 | 89 | +0x3C target, +0x44 area, +0x4C value |
| 0x150 | Patrol | Patrol | command | 0x50 | 88 | +0x3C target, +0x44 number, +0x48 value, +0x4C number |
| 0x16A | PlayerSetAmmoMin | Player Set Ammo Min | command | 0x50 | 88 | +0x3C target, +0x44 value, +0x48 value, +0x4C number |
| 0x18D | AircraftFollowPath | Aircraft Follow Path | command | 0x44 | 86 | +0x3C value, +0x40 value |
| 0x1E5 | DamageMoby | Damage Moby | command | 0x5C | 85 | +0x3C target, +0x44 number, +0x48 number, +0x4C number, +0x50 area, +0x58 number |
| 0x191 | AircraftSetSpeedRange | Aircraft Set Speed Range | command | 0x48 | 77 | +0x3C value, +0x40 value, +0x44 value |
| 0x117 | IsActivated | Is Activated | condition | 0x48 | 71 | +0x40 target |
| 0x225 | TN_CompareVals | Truth-Nugget Compare Values | command | 0x58 | 63 | +0x3C value, +0x40 value, +0x44 ptr variable, +0x48 value, +0x4C value, +0x50 value, +0x54 ptr variable |
| 0x178 | SquadSetRespawnNumber | Squad Set Respawn Number | command | 0x44 | 62 | +0x3C number, +0x40 number |
| 0x102 | DifficultyLevel | Difficulty level | condition | 0x48 | 59 | +0x40 value, +0x44 value |
| 0x1C8 | CheckpointAdvance | Checkpoint Advance | command | 0x40 | 57 | +0x3C number |
| 0x4B5 | SquadSetDronePlanningData | Squad Set Drone Planning Data | command | 0x4C | 57 | +0x3C value, +0x40 number, +0x44 number, +0x48 number |
| 0x190 | AircraftSetSpeedMode | Aircraft Set Speed Mode | command | 0x44 | 56 | +0x3C value, +0x40 value |
| 0x12F | OccludeMobys | Occlude Mobys | command | 0x54 | 55 | +0x3C target, +0x44 area, +0x4C area |
| 0x1EB | FxControllerSetActive | FxController Set Active | command | 0x44 | 51 | +0x3C number, +0x40 value |
| 0x111 | IsAttacked | Is Attacked | condition | 0x54 | 48 | +0x40 target, +0x48 number, +0x4C target |
| 0x124 | SetPathfindingSearchDistance | Set Shadows | command | 0x48 | 47 | +0x3C target, +0x44 number |
| 0x197 | AircraftWaitTilPoint | Aircraft Wait Til Point | command | 0x44 | 47 | +0x3C value, +0x40 value |
| 0x15C | UseTurret | Use Turret | command | 0x4C | 43 | +0x3C number, +0x40 number, +0x44 value, +0x48 value |
| 0x1EE | PlaySound | Play Sound | command | 0x50 | 43 | +0x3C target, +0x44 area, +0x4C value |
| 0x109 | IsDamaged | Is Damaged | condition | 0x50 | 42 | +0x40 target, +0x48 value, +0x4C number |
| 0x228 | TN_While | Truth-Nugget While | command | 0x58 | 42 | +0x3C value, +0x40 value |
| 0x229 | TN_Do | Truth Nugget Do | command | 0x40 | 42 | - |
| 0x22A | TN_EndWhile | Truth Nugget End-While | command | 0x44 | 42 | - |
| 0x215 | TriggerEffectEvent | Trigger Effect Event | command | 0x4C | 40 | +0x3C value, +0x40 value, +0x44 value, +0x48 value |
| 0x4F7 | SetJunkDispenserAttributes | Set Junk Dispenser Attributes | command | 0x4C | 40 | +0x3C number, +0x40 value, +0x44 number, +0x48 number |
| 0x4E5 | BeatGameAtLeastOnce | BeatGameAtLeastOnce | condition | 0x40 | 37 | - |
| 0x13D | FollowPath | Follow Path | command | 0x54 | 35 | +0x3C target, +0x44 number, +0x48 number, +0x4C value |
| 0x16E | SetMobyPosition | Set Moby Position | command | 0x48 | 35 | +0x3C number, +0x40 value, +0x44 number |
| 0x1F4 | IncrementVar | Increment Var | command | 0x40 | 35 | +0x3C variable (ptr variable) |
| 0x4A3 | SquadClearCommands | Squad Clear Commands | command | 0x40 | 34 | +0x3C value |
| 0x123 | SetShadows | Set Shadows | command | 0x44 | 33 | +0x3C value, +0x40 number |
| 0x158 | SetupIntroAnim | Intro Anim Setup | command | 0x6C | 33 | +0x3C target, +0x44 value, +0x48 number, +0x4C number, +0x50 value, +0x54 value, +0x58 value, +0x5C value, +0x60 value, +0x68 number |
| 0x159 | TriggerIntroAnim | Intro Anim Trigger | command | 0x48 | 32 | +0x3C target, +0x44 number |
| 0x1BF | MenialSetWorkArea | Menial Set Work Area | command | 0x44 | 32 | +0x3C number, +0x40 number |
| 0x1F9 | Branch_Level | Branch - Level | command | 0x40 | 32 | +0x3C number |
| 0x4E9 | StopSound |  | command | 0x48 | 32 | +0x40 value, +0x44 value |
| 0x182 | SquadMerge | Squad Merge | command | 0x44 | 31 | +0x3C number, +0x40 number |
| 0x4F5 | GiveIntel | Give Intel | command | 0x44 | 30 | +0x3C number |
| 0x100 | Kill | Kill | command | 0x44 | 29 | +0x3C target |
| 0x19D | VTOLTransform | VTOL Transform | command | 0x44 | 29 | +0x3C value, +0x40 value |
| 0x22B | SE_GoTo | Simple Elevator Go To | command | 0x44 | 28 | +0x3C value, +0x40 value |
| 0x1EF | SetSoundCrowdLayer | Set Sound Crowd Layer | command | 0x44 | 26 | +0x3C value, +0x40 number |
| 0x15B | UseCover | Use Cover | command | 0x4C | 25 | +0x3C number, +0x40 number, +0x44 number, +0x48 value |
| 0x1EA | EffectInstant2 | Effect Instant 2 | command | 0x50 | 25 | +0x3C value, +0x40 value, +0x44 number, +0x48 number, +0x4C number |
| 0x116 | IsSetupActive | Is Setup Selected | condition | 0x44 | 24 | +0x40 number |
| 0x4E8 | SuspendDialogue |  | command | 0x44 | 24 | +0x3C value, +0x40 value |
| 0x17D | SquadBFChargeArea | Squad Charge Area | command | 0x48 | 23 | +0x3C value, +0x40 value, +0x44 number |
| 0x134 | SetKillZone | Set Kill Zone | command | 0x4C | 22 | +0x3C number, +0x40 number, +0x44 number, +0x48 number |
| 0x141 | FireFromArea | Fire From Area | command | 0x4C | 22 | +0x3C target, +0x44 area |
| 0x1F3 | MakeFalse | Make False | command | 0x40 | 22 | +0x3C variable (ptr variable) |
| 0x171 | SquadRunMinimalUpdate | Squad Run Minimal Updates | command | 0x44 | 21 | +0x3C number, +0x40 value |
| 0x1F5 | DecrementVar | Decrement Var | command | 0x40 | 21 | +0x3C variable (ptr variable) |
| 0x4CC | HelpdeskSetArea | Helpdesk Specify Area | command | 0x44 | 21 | +0x3C value, +0x40 value |
| 0x107 | HasJustDead | Has Just Died | condition | 0x5C | 20 | +0x40 target, +0x48 value, +0x4C number |
| 0x1E3 | SetAwarenessRangeMultiplier | Set Awareness Range Multiplier | command | 0x48 | 20 | +0x3C target, +0x44 value |
| 0x1E6 | DamageMobyInArea | Damage Moby In Area | command | 0x64 | 20 | +0x3C target, +0x44 area, +0x4C number, +0x50 number, +0x54 number, +0x58 area, +0x60 number |
| 0x1FA | SetAttractModeCamera | Set Attract Mode Camera | command | 0x40 | 20 | +0x3C number |
| 0x133 | PodFillByArea | Pod - Fill By Area | command | 0x4C | 19 | +0x3C number, +0x40 number, +0x44 area |
| 0x49D | Coop | Coop | condition | 0x40 | 18 | - |
| 0x110 | IsEngaged | Is Engaged | condition | 0x4C | 17 | +0x40 target |
| 0x211 | MechSetOptimalDist | Mech Set Optimal Dist | command | 0x4C | 17 | +0x3C target, +0x44 value, +0x48 value |
| 0x231 | GoToClue | Go to Clue | command | 0x48 | 17 | +0x3C value, +0x40 value, +0x44 value |
| 0x51D | FlagNavPolyEdges | Flag Nav Poly Edges | command | 0x44 | 17 | +0x3C number, +0x40 value |
| 0x147 | ThrowGrenades | Throw Grenades | command | 0x4C | 16 | +0x3C target, +0x44 target |
| 0x14B | FireAtArea | Fire At Area | command | 0x50 | 16 | +0x3C target, +0x44 area, +0x4C value |
| 0x4B4 | SetGlobalDronePlanningData | Set Global Drone Planning Data | command | 0x50 | 16 | +0x3C number, +0x40 number, +0x44 number, +0x48 number, +0x4C number |
| 0x164 | GatherPlayers | Set Player Position | command | 0x5C | 15 | +0x3C target, +0x44 area, +0x4C area, +0x54 area |
| 0x4AC | DisplayMessage | Display Message | command | 0x44 | 15 | +0x3C value |
| 0x4D0 | IsAware | Is Aware | condition | 0x4C | 15 | +0x40 value, +0x44 target |
| 0x130 | BlockNavmesh | Block Navmesh | command | 0x40 | 14 | +0x3C number |
| 0x183 | SquadSplit | Squad Split | command | 0x50 | 14 | +0x3C number, +0x40 number, +0x44 number, +0x48 value, +0x4C value |
| 0x106 | IsIntroSceneInactive | Is Intro Inactive | condition | 0x40 | 12 | - |
| 0x176 | PodPissed | Pod Pissed | command | 0x48 | 12 | +0x3C value, +0x40 value, +0x44 number |
| 0x18E | AircraftFollowPath2 | Aircraft Follow Path 2 | command | 0x4C | 12 | +0x3C value, +0x40 number, +0x44 number, +0x48 value |
| 0x192 | AircraftSetSpeedRange2 | Aircraft Set Speed Range 2 | command | 0x48 | 12 | +0x3C value, +0x40 number, +0x44 number |
| 0x1B3 | TankIsMobyInSeat | Tank Is Moby In Seat | condition | 0x4C | 12 | +0x40 value, +0x44 value, +0x48 value |
| 0x1BC | LightOn | Light On | command | 0x44 | 12 | +0x3C value, +0x40 number |
| 0x222 | TN_Else | Truth-Nugget Else | command | 0x44 | 12 | +0x3C number |
| 0x1B0 | BuggyEnter2 | Buggy Enter 2 | command | 0x4C | 11 | +0x3C value, +0x40 value, +0x44 value, +0x48 value |
| 0x1C9 | CheckpointIfComplete | Checkpoint If Complete | condition | 0x44 | 11 | +0x40 number |
| 0x12C | DeleteInArea | Delete In Area | command | 0x48 | 10 | +0x3C area, +0x44 value |
| 0x138 | SetDrone | Set Drone | command | 0x48 | 10 | +0x3C target, +0x44 value |
| 0x142 | AddOcclusionLine | Add Occlusion Line | command | 0x48 | 10 | +0x3C number, +0x40 number, +0x44 value |
| 0x143 | RemoveOcclusionLine | Remove Occlusion Line | command | 0x40 | 10 | +0x3C value |
| 0x1BA | MechEnter2 | Mech Enter 2 | command | 0x4C | 10 | +0x3C value, +0x40 value, +0x44 value, +0x48 value |
| 0x22C | SE_SetReponseMode | Simple Elevator Set Player Responsive | command | 0x44 | 10 | +0x3C value, +0x40 value |
| 0x233 | IfIsNotFollowingPath | If Is Not Following Path | condition | 0x4C | 10 | +0x40 target, +0x48 number |
| 0x4B1 | NexusSetHeight | Nexus Reactor Door Set Height | command | 0x44 | 10 | +0x3C value, +0x40 number |
| 0x12B | DeleteGroup | Delete Group | command | 0x44 | 9 | +0x3C target |
| 0x1B2 | TankLockSeat | Tank Lock Seat | command | 0x48 | 9 | +0x3C value, +0x40 value, +0x44 value |
| 0x1B7 | MechLockSeat | Mech Lock Seat | command | 0x48 | 9 | +0x3C value, +0x40 value, +0x44 value |
| 0x16B | SetPlayerFPNIS | Set Player FPNIS | command | 0x50 | 8 | +0x3C target, +0x48 value |
| 0x10D | IsInView | Is In View | condition | 0x4C | 7 | +0x40 area, +0x48 number |
| 0x114 | IsPlaythrough | Is Game In Playthrough Mode | condition | 0x40 | 7 | - |
| 0x198 | AircraftHoverAtPosition | Aircraft Hover At Position | command | 0x48 | 7 | +0x3C value, +0x40 value |
| 0x512 | PackBeastBounds | Pack Beast Bounds | command | 0x58 | 7 | +0x3C value, +0x40 area, +0x48 area, +0x50 area |
| 0xFE | BeginLoop | BeginLoop | command | 0x44 | 6 | +0x3C number, +0x40 number |
| 0xFF | EndLoop | End Loop | command | 0x3C | 6 | - |
| 0x14A | SetFlashlight | Set Flashlight | command | 0x48 | 6 | +0x3C target, +0x44 number |
| 0x15A | IsIntroAnimDone | Is Intro Anim Done | condition | 0x48 | 6 | +0x40 target |
| 0x17C | SquadBFFightLargeEnemy | Squad Fight Large Enemy | command | 0x50 | 6 | +0x3C value, +0x40 value, +0x44 value, +0x48 value, +0x4C value |
| 0x1BB | MechExit | Mech Exit | command | 0x44 | 6 | +0x3C value, +0x40 value |
| 0x1CE | CheckpointIfSaveFlagSet | Checkpoint If Save Flag Set | condition | 0x48 | 6 | +0x40 number, +0x44 value |
| 0x1D8 | NIS_RegisterActor | Non-Interactive Scene - Register Actor | command | 0x4C | 6 | +0x3C number, +0x40 number, +0x44 number, +0x48 value |
| 0x20F | VehicleDumpPlayer | Vehicle Dump Player | command | 0x44 | 6 | +0x3C value, +0x40 number |
| 0x219 | SwarmerEatCorpse | Swarmer EatCropse Spawn | command | 0x4C | 6 | +0x3C value, +0x40 value, +0x44 value, +0x48 value |
| 0x4F6 | SetCameraShakerAttributes | Set Camera Shaker Attributes | command | 0x54 | 6 | +0x3C number, +0x40 value, +0x44 number, +0x48 number, +0x4C number, +0x50 number |
| 0x146 | MoveSystemComponents | Move System Components | command | 0x4C | 5 | +0x3C target, +0x44 value |
| 0x194 | AircraftFacePos | Aircraft Face Pos | command | 0x44 | 5 | +0x3C value, +0x40 value |
| 0x1B5 | TankEnter2 | Tank Enter 2 | command | 0x4C | 5 | +0x3C value, +0x40 value, +0x44 value, +0x48 value |
| 0x1C3 | ScreenFade | Screen Fade | command | 0x4C | 5 | +0x40 number, +0x44 number, +0x48 number |
| 0x1C7 | EnterSegment | Enter Segment | command | 0x4C | 5 | +0x3C area, +0x44 number, +0x48 number |
| 0x1D7 | PlayMovie | Play Movie | command | 0x54 | 5 | +0x3C value, +0x40 number, +0x44 value, +0x48 value, +0x4C value, +0x50 value |
| 0x210 | MechSetWeaponUse | Mech Set Weapon Use | command | 0x4C | 5 | +0x3C target, +0x44 value, +0x48 value |
| 0x4D6 | Event_BurrowerDriveBy | Event - Burrower Drive By | command | 0x4C | 5 | +0x3C number, +0x40 number, +0x44 area |
| 0x139 | SetCanDrone | Set Drone | command | 0x48 | 4 | +0x3C target, +0x44 value |
| 0x1AD | BuggyLockSeat | Buggy Lock Seat | command | 0x48 | 4 | +0x3C value, +0x40 value, +0x44 value |
| 0x1B6 | TankExit | Tank Exit | command | 0x44 | 4 | +0x3C value, +0x40 value |
| 0x1B8 | MechIsMobyInSeat | Mech Is Moby In Seat | condition | 0x4C | 4 | +0x40 value, +0x44 value |
| 0x1D9 | NIS_Start | Non-Interactive Scene - Start | command | 0x44 | 4 | +0x3C number, +0x40 value |
| 0x1E4 | DamageSphere | Damage Sphere | command | 0x4C | 4 | +0x3C area, +0x44 number, +0x48 number |
| 0x214 | TempAssignElevatorVars | Test Change PVar | command | 0x54 | 4 | +0x3C value, +0x40 value, +0x44 value, +0x48 value, +0x4C value, +0x50 value |
| 0x21C | IsRandomChance | Is Random Chance | condition | 0x44 | 4 | +0x40 number |
| 0x227 | TN_MobyPropValueCompare |  | command | 0x60 | 4 | +0x3C value, +0x40 value, +0x44 value, +0x48 value, +0x4C value, +0x50 value, +0x54 value, +0x5C ptr variable |
| 0x4D5 | Event_PsychicVision | Event - Psychic Vision | command | 0x44 | 4 | +0x3C area |
| 0x140 | OpenFire | Open Fire | command | 0x4C | 3 | +0x3C target, +0x44 target |
| 0x16C | EndPlayerFPNIS | End Player FPNIS | command | 0x44 | 3 | +0x3C target |
| 0x193 | AircraftSetFacing | Aircraft Set facing | command | 0x44 | 3 | +0x3C value |
| 0x1A4 | CtDropCargo | CT Drop Cargo | command | 0x4C | 3 | +0x3C value, +0x40 value, +0x44 value, +0x48 number |
| 0x1BD | MenialMoveObject | Menial Move Object | command | 0x50 | 3 | +0x3C target, +0x44 value, +0x48 number, +0x4C value |
| 0x1BE | MenialUseConsole | Menial Use Console | command | 0x4C | 3 | +0x3C target, +0x44 value, +0x48 value |
| 0x1C1 | MenialThrowObject | Menial Throw Object | command | 0x58 | 3 | +0x3C target, +0x44 value, +0x48 target, +0x50 number |
| 0x1C4 | ScreenBookend | Screen Bookend | command | 0x4C | 3 | +0x3C number, +0x40 number, +0x44 number, +0x48 text (ptr variable) |
| 0x1C5 | ScreenShake | Screen Shake | command | 0x4C | 3 | +0x3C number, +0x40 number, +0x44 area |
| 0x1CD | CheckpointSetSaveFlag | Checkpoint Set Save Flag | command | 0x44 | 3 | +0x3C number, +0x40 value |
| 0x21A | FastHybridSetupIntro | Fast Hybrid Setup Intro | command | 0x48 | 3 | +0x3C value, +0x44 value |
| 0x21B | FastHybridTriggerIntro | Fast Hybrid Trigger Intro | command | 0x40 | 3 | +0x3C value |
| 0x22E | SE_SetMoveParameters | Simple Elevator Set Move Parameters | command | 0x4C | 3 | +0x3C value, +0x40 value, +0x44 value |
| 0x234 | PodFillByDead | Pod - Fill With Dead | command | 0x4C | 3 | +0x3C number, +0x40 number, +0x48 value |
| 0x4DE | ForceSingleScreen | Force Single Screen | command | 0x40 | 3 | +0x3C value |
| 0x514 | GravySetAimDirection | Gravy - Set Aim Direction | command | 0x4C | 3 | +0x3C target, +0x44 value, +0x48 number |
| 0x105 | IsNISActive | Is N.I.S. Active | condition | 0x44 | 2 | +0x40 number |
| 0x118 | IsDeactivated | Is Deactivated | condition | 0x48 | 2 | +0x40 target |
| 0x13F | HoldFire | Hold Fire | command | 0x44 | 2 | +0x3C target |
| 0x15D | ReleaseTurret | Release Turret | command | 0x44 | 2 | +0x3C number, +0x40 number |
| 0x18F | AircraftResumePath | Aircraft Resume Path | command | 0x40 | 2 | +0x3C value |
| 0x1A2 | VTOLDropTroopsParam | VTOL Drop Troops (Param) | command | 0x58 | 2 | +0x3C value, +0x40 value, +0x44 value, +0x48 number, +0x4C number, +0x50 number, +0x54 number |
| 0x1A7 | VehicleFollowPath | Vehicle Follow Path | command | 0x4C | 2 | +0x3C value, +0x40 number, +0x44 number, +0x48 value |
| 0x1AE | BuggyIsMobyInSeat | Buggy Is Moby In Seat | condition | 0x4C | 2 | +0x40 value, +0x44 value, +0x48 value |
| 0x1B9 | MechEnter | Mech Enter | command | 0x48 | 2 | +0x3C value, +0x40 value |
| 0x4B8 | HidePlayerWeapon | Hide Player Weapons | command | 0x3C | 2 | - |
| 0x4B9 | ShowPlayerWeapon | Show Player Weapons | command | 0x3C | 2 | - |
| 0x50A | SetFacingTarget | Set Facing Target | command | 0x4C | 2 | +0x3C target, +0x44 number, +0x48 value |
| 0x10C | IfPlayerIsInVehicle | If Player Is In Vehicle | condition | 0x40 | 1 | - |
| 0x113 | IsAttractModeIsActive | Is Attract Mode Is Active | condition | 0x40 | 1 | - |
| 0x127 | RunAtLoad | RunAtLoad | command | 0x3C | 1 | - |
| 0x135 | SetTargetability | Set Targetability | command | 0x44 | 1 | +0x3C number, +0x40 number |
| 0x14C | SetVulnerability | Set Vulnerability | command | 0x48 | 1 | +0x3C target |
| 0x165 | SetPlayerHealthRegen | Set Player Health Regen | command | 0x40 | 1 | - |
| 0x172 | SquadGiveSignal | Squad Give Signal | command | 0x44 | 1 | +0x3C number, +0x40 value |
| 0x180 | SquadAdd | Squad Assign To Squad | command | 0x4C | 1 | +0x3C number, +0x40 target, +0x48 number |
| 0x199 | AircraftGunTargetPos | Aircraft Gun Target Pos | command | 0x44 | 1 | +0x40 value |
| 0x19B | AircraftGunTargetOff | Aircraft Gun Target Off | command | 0x40 | 1 | - |
| 0x1A1 | VTOLDropTroops | VTOL Drop Troops | command | 0x48 | 1 | +0x3C value, +0x40 value, +0x44 value |
| 0x1A3 | VTOLTakeOffOrLand | VTOL Take Off Or Land | command | 0x4C | 1 | +0x3C value, +0x40 value |
| 0x1A5 | BurrowerDropPod | Burrower Drop Pod | command | 0x4C | 1 | +0x3C value, +0x40 value, +0x44 value |
| 0x1A9 | VehicleSetSpeedRange | Vehicle Set Speed Range | command | 0x48 | 1 | +0x3C value, +0x40 number, +0x44 number |
| 0x1AB | VehicleStopAtPoint | Vehicle Stop At Point | command | 0x44 | 1 | +0x3C value, +0x40 value |
| 0x1C6 | SetTint | SetTint | command | 0x54 | 1 | +0x3C target, +0x44 number, +0x48 number, +0x4C number, +0x50 number |
| 0x1D4 | SESetSpec | Set Sound Environment Spec | command | 0x4C | 1 | +0x3C value, +0x40 value, +0x44 value, +0x48 value |
| 0x4A4 | VTOLDropTank | VTOL Tank Dropoff | command | 0x40 | 1 | +0x3C value |
| 0x4A7 | VTOLPickupTank | VTOL Tank Pickup | command | 0x44 | 1 | +0x3C value, +0x40 value |
| 0x4C0 | TehWin | Teh Win - 4TW! | command | 0x3C | 1 | - |
| 0x4DC | FinishGlassPanes | Finish Glass Panes | command | 0x44 | 1 | +0x3C area |
| 0x4DF | DisplayIntel | Display Intel | command | 0x40 | 1 | +0x3C number |
| 0x4F4 | SpiderStabMoby | Spider Stabs A Specific Moby | command | 0x44 | 1 | +0x3C value, +0x40 value |

Defined but unused in the shipped levels (130): ScriptCommandElem, ScriptConditionElem, SuspendAfterThisFrame, MembersInPod, ACTLevel, HasPlayerKilledMoreThanX, InArea_DOA, IsTargeted, IsStratActive, IsDebug, CommandsAreDone, SquadFinished, IfPlayerOwnsMoby, WaitVariable, WaitFrames, AdjustLights, SetHudStyle, RunAtLoad_VanishOutsideOfArea, SetColl, AttachToWorld, CreateNavEffectors, MergePods, SetTargetUpdate, SetFootLocking, ResetBot, SetOverrideSpeed, Stand, PlayActionAnim, SetWeapon, SetWeapon2, FleeAndFire, InitStance, Parachute, SetupAmbush, Sentry, SetMortarTurretMode, SetCoverUse, SetGrenadeUse, SetPlayerPostEffectsAndFog, PlayerWeaponRemoveAll, PlayerSetHealthMin, SetBotPodPosition, SetCurrentSetup, KnockPlayer, SquadSetVTOLRespawnData, SquadTakeFormation, SquadRush, SquadRemove, SquadSteal, SquadDistribute, SquadClearEnemies, SquadClearArea, SquadDoorway, SquadSetCasualtyRates, TGS_SwarmerRun, TGS_HumanStandAndFire, TGS_HybridSimpleRun, AircraftFaceMoby, AircraftSetTime, AircraftGunTargetMoby, AircraftCrash, VTOLTriggerDoors, VTOLBomb, VTOLRocket, VehicleReleaseScriptControl, VehicleResumePath, VehicleWaitTilPoint, VehicleStopAtPosition, BuggyEnter, BuggyExit, TankEnter, MenialIsConnectionOccupied, CritterPanicRadius, CheckpointBeginScript, CheckPointRestart, CheckpointReached, IfCheckpointReached, IfCheckpointNotReached, IS_Start, IS_QueueSpeech, IS_QueueMovement, IS_QueueGesture, IS_End, VO, VOMulti, VOByState, VOSetChatter, SpawnRagdoll, EffectInstant, PlayFxp, SpawnChunk, SetObjPos, SetObjText, ComputeValue, WindSetClue, E3_EndDemo, DEMO_Idle, DEMO_Stand, DEMO_Advance, DEMO_Strafe, DEMO_Cover, DEMO_Movement, DEMO_Normal, DEMO_LowHP, DEMO_MayaCutscene, DEMO_SonyEnemyFlanking, DEBUG_MoveAndFace, DEBUG_TakeCover, DEBUG_Strafing, DEBUG_HeroMoment, Execute_HeroMoment, ResetPhysics, EruptSwarmerEgg, TestMove1, VehicleGrabPlayer, BehemothSetWeaponUse, BehemothSetOptimalDist, SwarmerSetParams, SwarmerVentSpawn, SetRandomValue, MarkLabel, GotoLabel, TN_BaseNugget, SE_RelinkVolumes, IsAtClue, GoreLevel, ButtonLock, SetPlayerFallDamageHeights, SetCreditsMusic, BlockoutDrone.
