# New-engine gp_prius.dat: structural writer (GpPriusDocument)

> Status (2026-11-16): engine API done (`ReLunacy.Engine/Packing/GpPriusDocument*.cs`), headless probe
> `dev/tools/gpwriteprobe` passes on every region of A Crack in Time, All 4 One and Into the Nexus. Not yet wired
> into File > Save. Nothing has been tested in game.

Builds on `acit_gp_prius_coverage.md` (ACiT byte coverage, fix-up audit, append recipe). This file adds what was
needed to cover A4O and ITN, and documents the C# writer.

## 1. Format facts learned (all 135 regions: ACiT 22, A4O 81, ITN 32)

- **The ACiT re-layout rule holds unchanged on A4O and ITN.** Sections sit at the next 0x80 boundary in file order,
  with PADn gaps, and the fix-up table follows the data. `append_moby.py --roundtrip` is byte-identical on all 135
  regions. A4O / ITN use a different section order (0x250A0..A8 first, 0x25022 between the controller and path
  tables), which the rule does not care about.
- **A4O / ITN layer record (0x25008) is 0x90 bytes**: name[0x40], then 10 (ptr, count) slots.
  - Slots 0..7 are ACiT's (Moby, path, volume, Clue, controller, Area, Pod, Scent).
  - Slot 8 is Squadrons (0x250A0 / 0x250A4).
  - Slot 9 (+0x88 / +0x8C) is not a pointer. It is never a fix-up and is nonzero on 276 of 1,559 layers.
    Copied as is.
  - 0x2500C is 0x24 bytes: 9 metadata pointers in the same order.
- **Every A4O / ITN fix-up is a known pointer field.** That is ACiT's set plus:
  - Squadron +0 (-> 0x250A8),
  - every Pod / Scent / Area list entry and every Clue +0 (pointers into the reference pool 0x25022).

  "Pointer fields == fix-ups" holds on all 135 regions.
- **Pool 0x25022 (A4O / ITN)** holds one 0x10 entry (TUID u64, kind u32, 0) per referencing word. No entry is shared
  and none is unused: 170,378 A4O + 21,230 ITN entries, each used exactly once. The pool is not in first-use order.
  So a new reference appends its own entry, and a removed one deletes its own.
- **The layer invariants hold on all three games.** Per kind, records and metadata are parallel and grouped by
  ascending layer. Layer pointers and counts match the runs. An empty kind is (0, 0) with no fix-up.
- **TUIDs.**
  - A4O: no duplicate in any kind.
  - ITN: 14 Moby, 9 Pod and 7 volume TUIDs repeat.
  - ACiT: 573 volume repeats (Clue-generated volumes on TUID 0 / 1).
- **Settings blocks (0x25020)** sit back to back, with no gap, 4-byte aligned, in no required order.
- **Path points (0x25058)** sit back to back in path-record order. Path +4 is always 0x07041776. Each point's w is the
  cumulative distance through the points, and record +8 is the last w. This holds for closed paths too (12,919
  open + 2,076 closed checked).
- **Lists.** Pod and Scent lists sit back to back in record order. ACiT Area lists are each contiguous but not in
  record order (A4O / ITN: in order). An empty list always has a null pointer.
- **gameplay.dat 0x25000 +0x10..+0x3C are maxima over the level's regions, on all 40 levels.** They are budgets, not
  counts. For single-region ACiT the two are the same thing.
  - A4O / ITN add more fields: +0x40 Squadrons, +0x44 Squadron entries, +0x4C Clues, +0x50 Scents (also maxima).
  - +0x48 is Squadron-related, but no formula fits.
  - The region count and pointer follow these fields.
- **metadata.dat** holds layer names only (0x25098, 0x25100, 0x25110, zero words at 0x25104). It has no instance
  counts, so no operation here touches it. Only a new layer would.

## 2. Model (`GpPriusDocument.cs`)

`GpPriusDocument.Parse(byte[])` keeps:

- each section's bytes and table entry,
- the original gap bytes before each section,
- the 0x20 header,
- every fix-up as a symbolic pointer: (field section, offset) -> (target section, offset).

A pointer's target may sit exactly at a section's end.

Data that an in-place save appended after the last section is kept as a **tail** pseudo-section (16-aligned, no
table entry). PodWriter's grown Pod lists go there. The user-edited space_sector_1 archive round-trips with it.

`Serialize()` lays the sections out again:

- each section at the next 0x80 boundary;
- a gap keeps its original bytes while its length is unchanged, and gets PADn fill otherwise;
- every pointer is rewritten;
- the fix-ups are sorted, and a duplicate throws.

`Parse` throws `InvalidDataException` on anything it does not understand: a version other than 1.1, a pointer
outside every section, or a fix-up type other than 0. Such a file is not edited structurally.

The single primitive is `Splice(section, at, oldLength, bytes, owner)`:

- Pointer fields inside the replaced bytes are dropped.
- Fields and targets at or after the end of the replaced bytes move by the size change.
- A target at exactly `at` stays when bytes are replaced and moves when bytes are only inserted. The exception is the
  `owner` pointer.
- A target strictly inside the cut is refused with an exception.

Every operation is built from this primitive plus `SetPointer` / `ClearPointer`.

`Validate()` re-checks the invariants:

- the parallel tables,
- ascending layer grouping,
- layer pointers and counts,
- names,
- Moby record +0x02 == metadata layer,
- the 0x25005 / 0x25010 layer counts,
- pointer ranges.

The probe runs it after every operation.

## 3. Operations (`GpPriusDocument.Edits.cs`)

All operations return `GpEditResult` (`Applied`, `Changes`, `Problems`, `Created`, `CreatedClue`, `Shifts`,
`ReferencesRewritten`). An operation checks everything before it writes anything. A refusal leaves the document
unchanged; the probe checks this on every region.

New instances go at the **end of their layer's run**. The file must stay grouped by layer, and the end is the only
place where no existing handle changes. The metadata gets a fresh 64-bit TUID that is unique in the region and not
0 or 1 (`NewTuid`). The name is appended to 0x11300. The layer's count goes up. If the layer had none of that kind,
it also gets its two pointers (layer +0x40+8k, 0x2500C +4k).

| Operation | Writes | Refuses when |
| --- | --- | --- |
| `AppendMoby(GpMobySpec, regionDat, rng)` | Copies the template record. Sets +0x00 class index, +0x02 layer, +0x04 / +0x08 distances, +0x14 position, +0x20 Euler, +0x2C scale. Settings: a private block is copied to the end of 0x25020, a shared 0x25030 block is shared, or none. The template's 0x25049 matrices are copied to the end of 0x25049 | Class TUID not in region.dat 0x1C600; class index out of range; no Moby of the class to copy +0x30..+0x4C from (unless a template is given; another class's template is noted); ClassDefault with no shared block of the class |
| `AppendVolume(GpVolumeSpec, GpClueSpec?, rng)` | 0x40 record: row-major matrix, last word shape << 24. Optional Clue in the same layer: +0 volume reference (ACiT inline word; A4O / ITN a new pool entry and pointer), +4 / +8 settings, +0xC class | No volume / Clue tables; no Clue of the class to copy settings from ("needs a template"); template of another class; pooled form without 0x25022. Copied settings that name instances are reported in `Problems` |
| `AppendPath(GpPathSpec, rng)` | Points are inserted before the next path's run (keeps record order), with w = cumulative distance. Record +4 copied, +8 length, +0xC closed, +0xD kind, +0xE count | No path tables; 0 or more than 65,535 points |
| `AppendController(GpControllerSpec, rng)` | Copy of the template record at a new position / Euler / scale. Settings copied or shared like a Moby's (the class is the settings' first word) | No template; ClassDefault without a shared block. Copied references are reported |
| `SetPathPoints(path, points)` | Replaces the path's run, moves every later run, rewrites +8 and +0xE | 0 or more than 65,535 points |
| `AddMember(group, member)` / `RemoveMember` | Pod (any non-Squadron kind), Area (paths +0 / count +8, volumes +4 / count +0xA), Scent (Clues). Each list is rewritten in place. A list that empties loses its pointer. A new list goes at the end of its section. A4O / ITN add or delete the pool entry | Wrong member kind; already in / not in the list; handle does not fit (layer > 0x3FF, index > 0xFFF); pooled member whose TUID repeats in its kind (a TUID reference could name either) |
| `ChangeLayer(instance, layer)` | Moves the instance to the end of the new layer (see below) | Squadrons; same layer; 4,096 already in the target layer |

**Layer change = move to the end of the new layer.** Any other spot would also renumber instances of the new layer.
With the move to the end, only the old layer's later instances move up by one. Every changed handle is returned in
`Shifts`: the moved instance plus the ones renumbered. The caller warns that instances.lua / scripts use
`handle:new(layer, index)`.

In ACiT form, every reference word that names a shifted handle is rewritten before the move. These words are:

- Clue +0;
- every Pod / Area / Scent list entry;
- the words of every settings block, each block once:
  - a flat block from +4;
  - an inner IGHW's struct from +4 and its extra-data sections, excluding the block's own pointer words.

A4O / ITN references go through TUIDs and are untouched. Moby record +0x02 and metadata +0xC are updated, and an
emptied old layer loses its pointers.

**gameplay.dat** is handled by `GameplayBudgets.Update(gameplayDat, editedDocs)`. It recomputes each edited region's
needs (`Needs`) and only raises budgets, never lowers them, so an unedited level writes identical bytes:

- +0x14 size, +0x18..+0x3C as in the coverage doc;
- A4O / ITN also +0x40, +0x44, +0x4C, +0x50;
- +0x48 is never touched.

This replaces `PodWriter.PatchGameplay`, which refuses levels with more than one region.

## 4. Integration notes (for File > Save)

1. Run `GameplayWriter.Patch` (in place, by TUID) on the original first. Then `Parse` its output, apply the
   structural edits, and `Serialize`. Patching after the structural edits would match new instances' TUIDs to
   nothing.
2. Write `{region}/gp_prius.dat` for each edited region and `gameplay.dat` from `GameplayBudgets.Update`, all
   through PsarcPatcher with `Backups.Create` first. The document itself writes no file.
3. Reload the region after saving. Instance indices change, and the editor's entities hold `GameplayIndex` / `Index`.
4. Surface `Problems` and `Shifts` in the save dialog: copied references, Lua handles.

## 5. Headless verification (`dev/tools/gpwriteprobe`)

The probe reads the shipped archives (ACiT space_sector_1 from its shipped `.bak`, and the user-edited archive for a
round-trip only). It writes nothing. Each operation runs on a fresh copy, and then:

- the result must re-parse to itself and validate;
- it is read back with **ReLunacy's own `RegionReader.ReadOneRegionNew`**, with the gp_prius swapped in memory;
- the new or changed instance must read back with the right name, transform, layer and class / shape / points /
  members;
- a snapshot of every instance and relation, naming instances by name + placement and never by index, must be
  unchanged apart from lines that name the edited instance.

The relations in the snapshot are Moby / Clue / controller settings references, Clue -> volume, Area paths / volumes,
Scent volumes and Pod members. Run time: 3.5 minutes for all 40 levels.

| Check | ACiT | A4O | ITN |
| --- | --- | --- | --- |
| Unedited round-trip byte-identical | 23/23 (incl. user-edited ss1) | 81/81 | 32/32 |
| Validate on shipped files | 23/23 | 81/81 | 32/32 |
| Refusals leave the file unchanged | 22/22 | 81/81 | 32/32 |
| Append Moby | 22/22 | 78/78 | 30/30 |
| Append volume + Clue | 21/21 | 81/81 | 30/30 |
| Append path | 21/21 | 78/78 | 30/30 |
| Append controller | 21/21 | 78/78 | 32/32 |
| Path points (+2 on one path, -1 on another) | 21/21 | 77/77 | 30/30 |
| Pod membership (add + remove) | 21/21 | 78/78 | 30/30 |
| Area membership (add volume + remove) | 21/21 | 77/77 | 30/30 |
| Scent membership (add Clue + remove) | 19/19 | 77/77 | 30/30 |
| Moby to a layer without Mobys (old layer may empty) | 21/21 | 78/78 | 30/30 |
| Volume appended to a layer without volumes | 21/21 | 75/75 | 30/30 |
| Moby / volume / Clue layer change | 63/63 | 234/234 | 90/90 |

Regions without a kind's tables count as n/a, not passed. The layer-change ops reported 2,073 (ACiT), 15,113 (A4O)
and 4,760 (ITN) shifted handles. In ACiT, 3,384 reference words were rewritten.

Mutation check: with the reference rewrite disabled, 11 of the 15 great_clock layer-change runs fail. So the snapshot
does detect a wrong rewrite.

## 6. Open risks

- **Not tested in game.** The coverage doc's runtime risks still apply: Moby pool capacity, and copied settings
  carrying the template's ids and references.
- **Settings words are recognised heuristically.** A settings word that happens to decode as a moved handle
  (0xF000 pattern, valid kind / layer / index) would be rewritten too. Inner pointer words are excluded, but a float or
  int that matches is not.
- **instances.lua / .lc, .trimmed and level scripts are not rewritten.** The caller gets `Shifts`.
- **A4O / ITN TUID references.** A moved instance keeps its TUID, so its references stay valid. ITN's repeated TUIDs
  were already ambiguous in shipped files. Adding such an instance to a list is refused.
- **Not supported:** Squadron edits, deleting Pods / Areas / Scents / Ties,
  creating a missing section. gameplay.dat +0x48 is not updated. New layers: see `newengine_new_layers_zones.md`.
- **RegionReader drops Mobys whose class asset is not loaded.** The probe compares those by name only.

## 7. Deleting instances (`GpPriusDocument.Delete`, `ReferencesTo`; `EntityRegion.Delete`)

Kinds: Moby, volume (its Clue goes with it: a Clue's +0 is its volume), path, controller / camera, Clue. Pods, Areas,
Scents, Squadrons and Ties are refused (Ties: zones.dat + collision + assetlookup, not attempted).
All or nothing; the document is unchanged on a refusal. Order inside `Delete`:

1. **Pod / Area / Scent list entries** naming a deleted instance are removed (`RemoveMember`: a list that empties loses
   its pointer; A4O / ITN also drop the pool entry).
2. **List elements in settings extra data** (SoundEnvironment +0x28 volumes, +0x2C 16-byte entries, SoundEmitter +0x18,
   dropship +0x1218 actions): an element goes whole when any of its words names a deleted instance. The inner block is an
   IGHW 0.2 that round-trips through `Ighw02` on all 29,882 inner blocks of ACiT; it is laid out again with the element
   cut, the list's count word (the word before the list) is decremented, and the size word of every record using the block
   (Moby +0x10, Clue +0x08, controller +0x04) follows. A list may end up empty (count 0, pointer kept).
3. **Settings words** (struct fields) naming a deleted instance are set to 0, the "no reference" every unset field holds.
4. **Handle rewrite** (ACiT, inline form): every reference word (Clue +0, list entries, settings words) naming an instance
   behind a deleted one in its layer is rewritten (index in layer minus the deleted ones before it). All 4 One / Into the
   Nexus references are TUIDs: nothing moves; a TUID that repeats within the deleted kind is refused.
5. **Records**: record, metadata, layer count / pointers (`AdjustLayerCount`), and what only the record owned: a private
   settings block (0x25020, 4-aligned length), a Moby's matrices (0x25049), a path's points, a Clue's pool entry. Shared
   targets stay. Names stay in 0x11300; gameplay.dat budgets are maxima and are left alone.

A4O / ITN: a pool entry in a settings block that no struct word uses belongs to a list, which is not read in that form, so
deleting an instance named there is refused (all 81 A4O controllers: SoundEnvironment lists).

**Editor.** `EntityRegion.Delete` moves entities to `Deleted`, removes them from Pods, and the live layout
(`GameplayReferences.SetDeletedSource`) drops them so `SyncHandles` clears the words naming them (and drops the
`ClueReference`) and renumbers the rest, which keeps the settings arrays `GameplayWriter.Patch` writes equal to what the
structural pass wrote. `GameplayStructure.Apply` tracks them like other existing instances and deletes last; `Patch`
counts the records of deleted instances still in the file (refused deletions) so TUID-in-order matching holds.
instances.lua / scripts name instances by handle and are not rewritten (a note is added to the Save dialog).

**Verification.** `dev/tools/delprobe` (document level, 6 scenarios + 20 mixed per region): validate, re-serialize, no
word naming a handle that does not resolve, the multiset of (holder, field, target by kind/TUID/name) equals the old one
minus the deleted, reader snapshot loses only what names the deleted, every reference encodes back to its word.
ACiT 22 levels: all scenarios pass (volume in a list 16/16); A4O 81 regions and ITN 32 pass, with controllers and list
users refused as above. Mutation check: with the handle rewrite disabled the probe fails. `dev/tools/delsaveprobe`:
editor delete of a referenced Moby, a Clue volume, a path and a controller on space_sector_1 and krell_canyon, undo leaves
nothing to save, Save to a copy, re-read: counts -1 each, surviving relations identical by TUID. Not tested in game.


## New Pods and Areas

`GpPriusDocument.AppendGroup(kind, layer, name)` appends an empty Pod or Area (record 0x10 zero bytes, null list pointers,
metadata with TUID, name and layer) at the end of the layer's run. Pods and Areas are numbered by table position (Clue
references name them by (layer, index in layer); volumes' and paths' Area membership and the editor by table index), so the
layer must be at or after the last layer holding the kind (`GroupLayerProblem`); an earlier one would renumber later groups.
Members are added with `AddMember` afterwards; gameplay.dat's Pod / Area budgets are raised by `GameplayBudgets`.
Retail moons: layer `gameplay_<id>` holds `SEGMENT_SS<sector>_<ID>` (crates, enemies, breakables, Zoni; moon1x's also the landing
beam) plus `<id>_enemies`, `<id>_antennae`... Pods and `<id>_apNN_homearea` / `_firepoints` Areas of volumes. A segment is live
only through level.csv's `Pod` row (name match, `Type` SEG_MOON, pools), which is outside the level folder.

## Renaming (2026-11-17)

**Where names live.** gp_prius: one NUL-terminated Latin-1 string per instance in 0x11300, pointed at only by its metadata entry +8
(Moby, path, volume, Clue, controller, Scent, Area, Pod). Data (ACiT 22 levels, A4O 81 regions, ITN 32): 263,000 names, none shared,
no dead bytes, none holds a '/', only letters, digits and '_', longest 64 (ACiT), 56, 48. Tie names are 0x72C0 rows
{TUID, char* -> 0x11300, 0} inside each zone blob of zones.dat (0x72C0 is row-parallel to the 0x7240 placements); every level with Ties of the three games, 0
shared strings, longest 57. The editor shows a Moby's or Tie's name after its last '/'.

**What binds by name.** Nothing in the files we write. References are (layer, index) words (ACiT) or TUID pool entries (A4O / ITN); collision,
lighting and lookups go by TUID, class and row. Two outside things follow names: level.csv's `Pod` row names a segment's Pod (the
save renames that cell through `SegmentRegistry.PendingRenames` / `ApplyRename`, after the new columns are added; a Pod not in the
row changes nothing), and instances.lua(.lc) names the instances for scripts as Lua variables bound to `handle:new(layer, index)`
(`SEGMENT_SS1_MOON1A = pod_handle:new(...)`; .trimmed lists the 43 of 1,201 Mobys the scripts use). instances.lua is generated from the
names the level was built with and is not rewritten: a renamed instance keeps working through its handle, the scripts keep the old
variable. The save lists a problem line naming the renamed instances instances.lua.trimmed uses. Not known: whether the game
looks any instance up by its gp_prius name at run time (no reader of 0x11300 text was found).

**Operation.** `GpPriusDocument.Rename(instance, name)`: `Splice(0x11300, at, oldLength, newBytes, owner)` replaces the string in place
(the pool changes length, every pointer behind it moves, the owner pointer stays); when another pointer targets the string or
its tail, the instance gets a new string at the end of the pool and the others keep theirs. `NameProblem`: 1 to 64 characters,
printable ASCII, no space, no '/'. gameplay.dat's +0x14 size follows through the usual budget pass (the serialized size).
Pods and Areas are renamed by their table index (appends go to the end of the table, so indexes hold); Clues keep their own names.

**Editor.** `Entity.Rename` / `LoadedName` / `IsRenamed` (the name the files hold, set by the first rename; a rename back leaves
nothing to save), `EntityPod`, `EntityRegion.AreaRenames`, `EntityRegion.SetName / SetPodName / SetAreaName`, validation in
`RenameProblem`, `PodRenameProblem` (unique among Pods), `AreaRenameProblem` (unique among Areas), duplicates among instances
allowed and flagged (`NameIsShared`). A new instance only changes its pending name (the append reads it). The reference table
(`GameplayReferences.SetNameSource`) shows the new names. The old engine refuses (names come from debug.dat / instances.lua, not written).
`GameplayStructure` step 7b writes the renames after the appends and before deletions. Layers and zones are not renamed (their names
are in metadata.dat, gameplay.dat, region.dat and the zone tables).
UI: F2, the hierarchy menu or a double-click on the name; the inspector's name field; the Pods frame; every rename one undo step.

**Verification.** `dev/tools/renameprobe <level_cached.psarc> <scratch>` (copies, Save to a file): rename a Moby, volume, path, controller,
Tie, Pod and Area of the level to longer and shorter names, make a new Moby, volume, path, controller, Tie, Pod and Area and rename
them, save, re-read with the normal readers: every name equals the expected one, relations by TUID unchanged, gp_prius validates,
re-serializes to itself and has no dead bytes, every Tie line unchanged except the renamed one, rename + undo leaves 0 changes. space_sector_1,
krell_canyon (ACiT) and clank_puzzles (ITN, pooled references): ALL OK. `renameprobe shared <psarc>` makes two Tie rows share one
string (retail has none) and renames one: it gets its own string, the other keeps its name, every other zone blob is identical.
`renameprobe scan <levels dir>` is the data scan above. Not tested in game.

## Deleting Pods, Areas and Scents (2026-11-17)

`GpPriusDocument.Delete` takes `GpKind.Pod/Area/Scent`. Members leave first (RemoveMember, so pooled entries go too), then the same
reference pass as instances: words naming the group are cleared, list elements cut, and on A Crack in Time the later groups of the
layer are renumbered in every inline word (Slots map). The record, metadata entry, the group's own name string in 0x11300 (unless shared)
and any leftover list are cut. `AppendGroup` also takes Scent. Editor side: `EntityRegion.DeleteGroups` flags the handle (tables keep
their loaded indices until the reload after the save); `GameplayStructure` skips new groups that were deleted (landed index = handle
minus dropped ones, `GroupAt`) and deletes the loaded ones in step 8. level.csv: `SegmentRegistry.ApplyRemoval` removes the Pod's
column and reverts level-wide pool sums. Verified (probe, ACiT space_sector_1/krell_canyon, ITN meridian_city): survivors keep name+members,
all references resolve to the same group, none dangling, Validate passes, undo leaves nothing to save, level.csv round-trips byte-identical.
