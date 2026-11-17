# New-engine Tie append (engine API)

Status 2026-11-16: done. Planet levels get a copied 0x8200 entry, untested in game. TieAppender, IghwRelayout and CollisionBvh are written. CollisionWriter refits the BVH,
and ZoneWriter grows cluster spheres. The probe passes on 16 levels of three games. Nothing has been tested in game.

Collision bug report 2026-11-17 ("a new Tie's collision is broken: some vertices and indices right, the mesh wrong"):
- `AppendCollision` is correct. The probe (`dev/tools/tieappendprobe`, `STRESS=1` adds 60 classes under translation, rotation,
  uniform scale 0.4 / 3, mirror and non-uniform scale) compares every new instance's world triangles with the source's moved
  by srcInverse * newMatrix: 79172 triangles on space_sector_1, worst error 0.001 (limit 1/512); krell_canyon 12135, 0.0002.
  Every leaf holds its instances, every instance is in one leaf, pre-existing instances read identically.
- Cause: a save that only adds Ties was not "structural", so the editor did not reload. Its `LevelCollision` kept the
  shape offsets of the file before the append (the shape blob moved by 0x80 x instances added), and the next save's
  `CollisionWriter.Patch` wrote material bytes and triangle flag bits (the top 5 bits of each word, indices untouched)
  at those offsets, i.e. into other shapes: 46428 faces in 824 shapes changed on space_sector_1, 114312 on axiom_city.
  The editor also still held the Ties as new, so the next save appended them again.
- Fix: `GameplayWriter.Build` marks a save that appended Ties structural (the level is read again);
  `CollisionWriter.Patch` refuses to write when the file's header +0x04 / +0x08 differ from the `LevelCollision`'s
  (`HeaderInstanceCount`, `HeaderShapeBase`); a moved primitive-set instance keeps its uniform scale in w (+0x6C).
- Not verified: in game; a save that appended Ties and has problems shows a Reload button instead of reloading, and saving
  again before reloading would append the same Ties again (`NewInstance.Saved` is never set).

Spec: `acit_zones_coverage.md` section 6 (zones) and `acit_collision_format.md` section 7 (collision).

## API (`ReLunacy.Engine/Packing`)

```csharp
public sealed record TieAppendRequest(ulong ZoneTuid, ulong ClassTuid, Matrix4x4 Transform, string Name,
    string Region = "default", bool Collision = true);
public static TieAppendResult TieAppender.Append(Func<string, byte[]?> read, IReadOnlyList<TieAppendRequest> requests);
// result: Files (archive path -> new bytes, changed files only), Changes, Problems,
//         Added (AppendedTie: Index, PlacementTuid, SourceOffset in zones.dat, Case "a"/"b", CopiedFrom, CollisionInstances),
//         Refused (request, reason)
```

- **`read`**: `FileManager.ReadOriginal` fits. The files it reads are `zones.dat`, `assetlookup.dat`,
  `lighting.dat`, `assetstats.dat`, `<region>/collision.dat` and `level_cached.toc`.
- **Several requests**: they are applied in order. A refused request leaves the files as they were before it.
- **Order with the other writers**: run the in-place writers (ZoneWriter, CollisionWriter) first and call Append on
  their output. Those writers address records by their offsets at load time, and an append moves later zone blobs.
  It also moves the shape blob that comes after the collision instances.
- **Region**: the caller passes the region whose `collision.dat` gets the Tie's collision, which is the zone's region.
  On levels with several regions, the class's mask in 0x1D301 must cover every region of the zone in 0x1DA01.
  Otherwise the request is refused.
- **Saving**: `Files` go through `PsarcPatcher`, with `Backups.Create` first, the same way as `GameplayWriter.Save`.
  That save path does not touch `level_cached.toc`; Append rewrites it itself.

## What Append does

### Source placement

Each new placement is a copy of an existing placement of the same class. Bytes +0x58..+0x7F are kept because they
are not fully decoded. The source is picked in this order:

1. Same zone, unbaked, with prelight.
2. Another zone, unbaked, with prelight. Only if the target zone has a lighting slice.
3. Same zone, unbaked, without prelight.
4. Same zone, baked. The copy shares the source's lightmap pair, which is untested and reported.
5. Any other placement. The copy is made unbaked with no prelight: +0x54 = 0xFFFFFFFF, +0x5A = 0xFFFF and
   +0x64/+0x66 = 0. This is reported.

Refused when the class has no placement in the level.

### Record

- Rows 0-3, columns 0-2: the new matrix. Column 3 is kept from the source.
- +0x40 sphere: the source's sphere carried by the move. The radius is scaled by the largest axis.
- +0x50: the class index.
- +0x5C: the mirrored byte, from the sign of the determinant.
- +0x5D: the cube index. In case b, the cube TUID is looked up in or appended to the zone's 0x72C1.

### Zone blob

- **0x7240**: the new record is appended.
- **0x72C0**: `{TUID, char*, 0}` is appended, with a new fix-up for the `char*`. The TUID is FNV-1a of
  zone/name/salt, unique in the level.
- **0x11300**: the name is appended.
- **0x7200**: case b only, the class TUID is appended.
- **Re-layout**: `IghwRelayout` lays the blob out again. Sections stay in table order on 0x80 boundaries. Gaps get
  `PAD0..PADF` filler, which restarts at each gap and uses hex digits, as retail does. Fix-ups are remapped and
  sorted.

### Clusters (0x72D0)

The new placement gets its own cluster `{sphere, N, N, 0, 1}` when both hold:

- it is outside the last cluster's sphere;
- the pool has a free slot.

Otherwise the last cluster's count goes up by 1 and its sphere grows to enclose the new one.

The pool is assetstats 0x9800 +0x30, which is the **per-region peak** of Tie clusters. The region sets come from the
0x1DA01 masks. This was checked on luminopolis, phonica_moon, meridian_city and krell_canyon. The game reserves
roundup16 of it. Append never goes past that: it falls back to growing the last cluster and reports it. When it adds
a cluster, it writes the new peak to +0x30.

### Lighting

A prelight block copied from another zone goes at the end of the target slice, 0x80-aligned. Its length runs up to
the next start in the source slice. The starts are every Tie +0x54, every Shrub +0x2C and every
0x5400/0x5410/0x5700/0x5200 +0x00, plus the slice end. That length is an upper bound and is never short. Later
slices move, and 0x1DB00 is updated.

### Files

- **zones.dat / lighting.dat**: later blobs shift by the 0x80-aligned growth. The zero filler is kept, and so is a
  file's trailing pad.
- **level_cached.toc**: entries are named `/built/levels/<level>/<path>`. An entry is matched by its path suffix and
  its old size.

### Collision

1. Pick the instances whose owner is `0x20000000 | class index in the TUID table` and whose matrix matches the
   source placement. Any other placement of the class is the fallback.
2. Copy them with the new matrix. On primitive sets, +0x6C = the scale of row 0.
3. Compute the box:
   - Same 3x3 as the source: shift it.
   - Mesh: the min/max of the transformed `CollisionMesh.Positions`.
   - Otherwise: transform the source box.
4. Sphere: the centre is carried by the move. The radius is at least the distance to every box corner.
5. Flags: bit 0x01 for mirrored and bit 0x04 for scaled.
6. Append the instances. Add one leaf per 255 instances under the deepest internal node that contains the leaf
   centre and has room, then grow that node and its ancestors.
7. Rebuild the payload with `CollisionBvh.BuildPayload`. This is byte-exact on retail.
8. If the tree block would pass 0x10000 (planet levels sit at 0xFFE0 to 0x10000), grow the leaf that ends the
   instance array instead. This is reported.
9. Set the header bounds to the union. Growth is reported.
10. Set assetstats +0x8C to the maximum of its old value and align80 of the new size.

## CollisionWriter refit (the known bug, fixed)

When instances move, `CollisionBvh.Refit` handles them in place:

- Each leaf that holds a moved instance gets the union of its instances' boxes again.
- Each ancestor gets the union of its children, working from the highest node index down.
- The header bounds become the exact union of all instances.

The topology does not change. Only growth of the header bounds is reported. The old engine is unchanged and still
gets its "grid not rewritten" warning.

## ZoneWriter (bonus)

A Tie moved out of its cluster sphere would be culled with its cluster. `GrowClusters` walks the IGHW blobs of
zones.dat and grows the sphere of every cluster that holds a moved record so that it holds the record's sphere again.

## Probe: `dev/tools/tieappendprobe`

Usage: `dotnet run -- <levels dir> [--full] level...`. It reads the archives read-only. For ss1 it uses the shipped
`.bak`.

Checks per level:

- Every zone blob and every region collision.dat, re-laid with nothing appended, is byte-identical. Rebuilding
  collision from its parsed tree is also byte-identical.
- `Append(read, [])` changes no file.
- Three requests are made in one call: (a) beside a placement, (a') a different class rotated, scaled 1.5 and moved
  about 70 units, and (b) a class from another zone.
- The target zone is read back with `Objects.Zone` and `TieInstance.Read`. Every old record, name and TUID is
  unchanged. Each new one has the right class, matrix, name, `SourceOffset` and mirrored byte.
- TUIDs are unique, the clusters partition the placements and hold the new ones, and the fix-ups are sorted and land
  in sections.
- Every other zone is byte-identical at its new offset, and the lighting slices keep their bytes.
- Assetstats: +0x30 equals the per-region peak and the pool is not outgrown. The toc records the new sizes and
  round-trips.
- Collision:
  - Old instances and the shape blob are unchanged.
  - BVH validity, ported from `colresearch/verify.py` and written independently: every instance is in exactly one
    leaf, every box holds its children, the header holds every instance, and the block is at most 64 KB.
  - The vertices of each new mesh instance lie inside its box, and a point query finds the instance.
- CollisionWriter: moving a placement's instances 212 units still leaves a valid tree, and a query finds them. With
  no edit, the output is byte-identical.
- ZoneWriter cluster growth: a sphere moved 500 units ends up held by its cluster.
- `--full`: the patched archive is written to `./work` and loaded with `LevelReader`. The new Ties are in the zone
  with their class asset, matrix and `SourceOffset`, and `CollisionReader` reads the file.

Results (2026-11-16):

| Game | Levels | Checks |
| --- | --- | --- |
| ACiT | space_sector_1..5, krell_canyon, axiom_city, zolar_forest, tombli, front_end (no Ties: round trip only) | 472, ALL PASS |
| A4O | luminopolis, phonica_moon, terawatt | 190, ALL PASS |
| ITN | meridian_city, vendra_homeworld, swamp_wasteland | 175, ALL PASS |

`--full` passed on space_sector_1 and krell_canyon. The zone, collision and assetlookup layouts are the same in A4O
and ITN.

## Limits and open risks

- **Not tested in game.** The main risks are listed below.
- **Shared baked lightmap pair.** Case 4 above has no retail precedent.
- **Prelight copy length.** It is an upper bound, so it can copy extra bytes. For example, the ss1 asteroid field
  copied 64,460 bytes.
- **Planet levels (collision.dat 0x8200 / 0x8300): entry copied, untested.** 0x8200 is little-endian except the
  TUID.
  - Header: +0x04 end of table D; {offset, count} of tables A, B, C and D at +0x08..+0x24; +0x2A u16 object total;
    +0x2C u16 count of table E.
  - Record (16 bytes): {u16 global object index, u16 per-class constant, u32 hash-like, u64 BE TUID}.
  - Table B holds the region's Tie placements in per-zone runs. A, D and E are other objects; C repeats a few B
    entries.
  - 0x8300 is a grid (not decoded) whose BE +0x20 is the total + 1.
  - Append copies a same-class B record with index = the old total and the new TUID, inserts it after its zone's
    run, and raises B's count, the later offsets, the end, the total and 0x8300's total + 1. Existing indices are
    untouched.
  - Proven on copies: ACiT all 22 levels (1151 checks; --full on krell, axiom, tombli, zolar, ss1); A4O luminopolis,
    phonica_moon, terawatt; ITN meridian_city, vendra_homeworld, swamp_wasteland (all --full). Every other byte is
    identical.
- **Full tree (64 KB).** Growing the last leaf makes its box large. Queries stay correct but get slower.
- **Grown collision header bounds.** These change the game's runtime grid, whose buffers are fixed and not decoded.
- **Grown cluster.** When the pool is full, the last cluster's sphere grows, so the zone culls less.
- **Not handled:**
  - a zone with no Tie tables (refused);
  - a zone with no lighting slice (the copy gets no prelight);
  - a new 0x1DB00 entry (except for a zone `CreateZone` makes; see `newengine_new_layers_zones.md`);
  - classes not in the level (refused: they would need ties.dat, 0x1D300 and region lists);
  - foliage, Shrubs and UFrags.
- **Building the probe.** It needs the engine to build. While other edits were in progress it was built against HEAD
  with these files copied in, in a temporary tree under `work/` that was then deleted.

## Deleting a Tie (2026-11-17)

`TieAppender.Delete(read, IReadOnlyList<TieDeleteRequest>)` is the mirror of Append, run before it in the save chain and on the
output of ZoneWriter / CollisionWriter. A request names the zone, the record's `SourceOffset`, the class (checked), the file matrix, the
matrix the collision carries when the editor moved the Tie (fallback), and the regions that load the zone. A refused request changes nothing.

- **Zone blob**: the rows leave 0x7240 and 0x72C0 (with the name pointer's fix-up); the name leaves 0x11300 when no other
  pointer reaches it; cluster ranges close up (+0x10 / +0x14 first, +0x1C count) and an emptied cluster goes, with assetstats
  0x9800 +0x30 following when it was the per-region peak. `IghwRelayout.Relayout` takes `offsetMaps` for this. Checked on
  data: no other zone section is indexed by placement and no fix-up points into 0x7240 / 0x72C0 / 0x72D0 (ss1, krell).
- **Kept**: lighting.dat (the prelight block and lightmap pair stay unused: blocks are shared, pairs indexed by number), the
  collision shape blob (shapes can be shared), assetstats +0x8C (not lowered).
- **Collision**: the Tie instances of the class's owner with the placement's matrix go; leaves are renumbered
  (`CollisionBvh.RemoveInstances`), an empty leaf or node goes, touched boxes are recomputed, header bounds are the exact union.
  Placements sharing a matrix take chunks out in proportion; if that does not divide, the request is refused.
- **Planet regions**: every placement is in the 0x8200 table B (krell: 9312 of 9312) and the numbering is also used by the undecoded 0x8300
  grid, so the records are kept (a stale TUID, as retail has on phonica_moon a_landing). Reported as a problem; untested in game.
- **Probe** `dev/tools/tiedeleteprobe` (3 Ties, with and without collision, in one call): ss1 26 checks and krell 23, no failure:
  Tie count -3, remaining records identical, other zones byte-identical, collision instances -43 (ss1) / -2 (krell) with the rest
  identical in order, BVH valid, bounds exact. Append then Delete: zones.dat, lighting.dat identical; collision differs in 3 (ss1) / 6
  (krell) tree bytes (box float rounding); assetstats differs in the collision-size and peak bytes Append raised.

## Renaming a Tie (2026-11-17)

`TieAppender.Rename(read, requests)` (TieRenameRequest: zone TUID, class TUID, the row's matrix as the file holds it, old name, new
name). The row of the zone blob is found by class, matrix and name (indexes and offsets move when Ties are deleted or added, so it
runs LAST in the save chain, after the in-place writers, the deletions and the appends). Its string in 0x11300 is replaced in place
by `IghwRelayout.Relayout` with an offset map for the pool (every later string moves; a pointer into the middle of the string is an
error); when another fix-up points at the string or its tail (retail has none: 0 shared strings in every level of the three games), the row gets a new string at the end
of the pool and its field is repointed. A name with a '/' keeps its prefix. The zone blob is spliced into zones.dat, assetlookup 0x1DA00
and level_cached.toc follow (as `Delete`); a level reload follows since the Ties' offsets moved. Only the name changes: collision,
lighting and assetstats go by TUID, class and row. Probe: `dev/tools/renameprobe` (a renamed retail Tie, a new Tie renamed, `shared`).
