# Tools of Destruction `.toc` files

The game's own file index: a path-to-size directory of everything the engine can open,
shipped inside the PSARC archives. Needed because adding or renaming a file in an archive
means adding or renaming it here too — replacing a file's contents does not.

Byte layout below is re-derived from ToD's shipped TOCs (NPUA80965) and validated against
the archives those TOCs describe. Fields whose meaning is still unknown are marked as such
and are listed as observations, not guesses.

## Where they live

Inside the archives, never loose on disk in a retail install. There are 64 distinct TOCs
in 125 copies.

- `global_cached.psarc` carries **61**: its own, plus all three (`level_cached`,
  `level_uncached`, `level_textures`) for each of the 20 levels.
- Each level archive **also** carries its own copy of its three.
- The remaining three sit alone in their own archives: `global_uncached.toc` in
  `global_uncached.psarc`, and `game.toc` and `debug.toc` in the two small archives
  `game.psarc` (67KB) and `debug.psarc` (780 bytes) beside them.

Those two copies of a level TOC are byte-identical in all 61 cases checked, so anything
that edits a level's TOC has to write both — the copy in `global_cached.psarc` and the
copy in the level's own archive.

Which TOC to edit follows from what is being added, and for scripts the answer is narrow:
**only `global_cached.toc` names a script**, all 525 of them, and both halves of every
script (`/levels/x/scripts/y.lua` and `/built/levels/x/scripts/y.lc`) live in
`global_cached.psarc` beside that index. No level TOC names a single `.lua` or `.lc`. So
adding a script touches one archive and one index inside it.

A TOC's own path inside the archive mirrors the archive's: `global_cached.psarc` holds
`/packed/game/global_cached.toc`, and `levels/cobalia/level_cached.psarc` holds
`/packed/levels/cobalia/level_cached.toc`.

## Layout

All integers are big-endian. There is no header: the file is read from the back.

```
  +-------------------------------------------------+ 0x00
  |  records and name strings, interleaved          |
  |  in document order (see below)                  |
  +-------------------------------------------------+ indexOffset
  |  index: count x 8 bytes                         |
  |    (record offset, name offset)                 |
  +-------------------------------------------------+ length - 4
  |  indexOffset                              u32   |
  +-------------------------------------------------+ length
```

`count = (length - 4 - indexOffset) / 8`.

The index is the spine. It enumerates every record in the file and is the **only** place a
directory's name is held, so a reader needs nothing but the index — it never has to work
out where the record region ends or how records are strided.

### Index entry

| Off | Type | Meaning |
|---|---|---|
| 00 | u32 | offset of this entry's record |
| 04 | u32 | offset of this entry's name |

Both offsets are absolute from the start of the file, and both are always 4-aligned
(12,383/12,383 entries).

The index is **not** sorted by path, but it is ordered: entries are in ascending
**record-offset** order in all 64 shipped ToD TOCs, which is to say the index follows the
layout. 61 of the 64 are also in ascending name-offset order — the three that are not are
the big interleaved ones, where names do not run monotonically.

That invariant is what makes appending an entry safe to attempt: a record placed after
every existing one, with its index entry last, preserves the order without moving anything.

### Record

What kind of entry it is follows from the record's **extent** — the distance to the next
record offset in the index — not from any field in it.

| Extent | Kind | Layout |
|---|---|---|
| 16 bytes | file | `u32 word0, u32 size`, then two words observed zero |
| 4 bytes | directory | a single word, zero |

`size` is the file's real **uncompressed** size: the same number the archive's own entry
records for it. Only read it on a 16-byte record — on a 4-byte one the following word
belongs to the next record, which is why 18 directory entries appear to carry a non-zero
size if the extent is ignored.

**`word0` is not a kind discriminator**, and an earlier draft of this document had it
wrong. It holds the entry's own name offset in 2,688 records and zero in 9,695, and both
groups contain real files with correct sizes — so the index, not the record, is the
authoritative source of a name. In the simple TOCs every record has `word0 == nameOffset`;
in `debug.toc`, `game.toc` and `global_cached.toc` most are zero. Reading `word0` as a
"has a name" flag classifies 3,057 real files as directories.

Extents are implied rather than stated, so a writer should preserve the ones it parsed.
`debug.toc`'s second record occupies only 8 bytes before the name region begins.

### Names

NUL-terminated ASCII, 4-aligned, full paths for files
(`/built/levels/cobalia/debug.dat`) and bare components for directories (`cobalia`).

### Interleaving

Records and name blocks alternate in document order. This is the detail that defeats any
model assuming two contiguous regions:

- In the 61 simple TOCs it happens to come out as one 16-byte record run, a 4-byte field,
  then all the names — so a flat table appears to work.
- `debug.toc`, `game.toc` and `global_cached.toc` interleave several groups: records, their
  names, more records, more names. Record offsets there are both below and above the lowest
  name offset.

The first word of the file equals record 0's name offset (64/64), which is why it looks
like a "names begin here" header in the simple TOCs. It is not one — it is just the first
record's first field.

### The 4-byte field after a simple TOC's record run — unknown

Where all records are strided 16, the last one is followed by 4 bytes before the names
begin. Values are small integers, 7..14, constant per file and rising with the length of
the level's directory name:

| `len(levelDir)` | `level_cached` | `level_textures` | `level_uncached` |
|---|---|---|---|
| 4 (`iris`) | 7 | 9 | 10 |
| 7 (`cobalia`) | 8 | 9 | 11 |
| 11 (`pirate base`) | 9 | 10 | 12 |
| 14 (`zordoom prison`) | 10 | 11 | 13 |
| 20 (`apogee space station`) | 11 | 13 | 14 |

It is **not** the longest path or longest component in words — for
`cobalia/level_cached` the field is 8 while those are 12 and 7. Meaning unknown; preserve
it rather than recompute it.

## Worked example: `debug.toc`

1,856 bytes, 42 entries, the smallest TOC that exercises both record kinds and the
interleaving.

```
0x0000  00 00 00 18  00 04 BC 75  00 00 00 00  00 00 00 00   record 0: file
0x0010  00 00 04 5C  00 00 00 06                             record 1: 8 bytes, unexplained
0x0018  "/built/game/debug.dat\0"                            names begin
0x0034  "apogee space station\0"
  ...   the 20 level directory names, 4-aligned
0x0158  "/built/levels/apogee space station/debug.dat\0"
  ...   the 20 level debug.dat paths
0x045C  00 00 00 00                                          record: directory
0x0460  00 00 00 00  00 0B AA B8  00 00 00 00  00 00 00 00   record: file
  ...   20 x (directory, file), 4 + 16 bytes each, to 0x05EC
0x05EC  index: 42 x (record offset, name offset)
0x073C  00 00 05 EC                                          indexOffset
```

Reading it through the index:

| # | record | extent | name | kind | word0 | size |
|---|---|---|---|---|---|---|
| 0 | 0x0000 | 16 | 0x0018 `/built/game/debug.dat` | file | 0x0018 | 310,389 |
| 1 | 0x0010 | 8 | 0x045C `""` | ? | 0x045C | 6 |
| 2 | 0x045C | 4 | 0x0034 `apogee space station` | directory | 0 | — |
| 3 | 0x0460 | 16 | 0x0158 `/built/levels/apogee space station/debug.dat` | file | 0 | 764,600 |
| 4 | 0x0470 | 4 | 0x004C `cobalia` | directory | 0 | — |
| 5 | 0x0474 | 16 | 0x0188 `/built/levels/cobalia/debug.dat` | file | 0 | 933,984 |

Entries 3 and 5 are the ones that disprove the `word0` rule: both name a file and carry a
correct size, with `word0` zero.

Entry 1 is unexplained. Its name offset points at the four zero bytes at 0x045C — which are
also entry 2's record — so its name reads as the empty string, its extent is 8 rather than
4 or 16, and its `word1` is 6.

## How this was validated

Across all 64 ToD TOCs there are 12,383 index entries. By extent: **11,919 files**
(16 or 20 bytes, the latter being a simple TOC's last record plus its trailing field),
**382 directories** (4 bytes, every one of which names a bare component rather than a
path), and 82 that fit neither cleanly — see the open questions.

Of all entries, 4,780 name a path that exists in one of ToD's archives (4,778 distinct
paths across 64 archives). **All 4,780 sizes match the archive exactly; none differ.** The
rest name files no retail archive carries, such as `/built/game/debug.dat`.

That total is the figure to trust. An earlier pass that classified by `word0` checked only
1,723 of them and silently skipped 3,057 real files — the sizes it did check all matched,
which is exactly why a validation that quietly narrows its own scope is worth distrusting.

An independent confirmation: `game.toc` lists `/packed/game/global_cached.psarc` at
233,666,474 bytes, which is that file's exact size on disk. (An earlier draft of this
document attributed that entry to `global_cached.toc`, which does not record any archive's
size — `game.toc` is the one place the game writes archive lengths down.)

## Notes for writing one

`ReLunacy.Engine/Packing/GameToc.cs` implements reading, writing and appending, and
`psarcprobe --toc` is the harness described below.

1. **Re-emit before inserting.** Parse to a model, write it back out, require the bytes to
   be identical. Reproducing the interleaving means keeping records and name blocks in
   their original document order. Verified on 125 files: all re-emit byte for byte, and all
   survive having the whole layout shifted four bytes with their contents intact — the
   second check being the one that proves offsets are re-derived rather than reused.
2. **Append, do not insert.** Offsets are absolute, so a record placed mid-file shifts
   every later offset. Appending the record after all the others, with its index entry
   last, keeps the ascending-record-offset invariant and moves nothing.
3. **Structural checks are not acceptance.** An appended entry can satisfy every property
   above and still not be found by the engine — if the directory entries imply a traversal
   that expects a directory's files to sit together, appending puts a file outside its
   group. Only running the game settles that.
4. **Both copies** of a level TOC, as above.
5. **Sizes must be real.** They match the archive exactly everywhere, and the engine most
   likely allocates from them.
6. **Archive sizes live somewhere else.** `game.toc`, inside `game.psarc`, records the
   length of 62 archives — `global_cached`, `global_uncached`, and each level's three — so
   editing *any* archive makes a number in a *different* one stale. No TOC records the
   archive it lives in, and `game.toc` does not record `game.psarc`, so rewriting it needs
   only one pass and chases nothing. `GameArchiveSizes.Refresh` does this; it has to run
   after the edit, since it reads the lengths from disk.

   Whether the engine reads those numbers is still unknown, and one observation bears on
   it: in a working install, a level archive repacked uncompressed came out 137,711,709
   bytes larger than `game.toc` records, and nothing in the format noticed. That is
   evidence the field is not validated, not proof — it only proves the file can be wrong,
   not that the level still loads.

## Open questions

- What `word0` is for. It is the entry's own name offset in every record of the simple
  TOCs and zero in most records of the big ones, with no observed effect on anything else.
- The 4-byte field after a simple TOC's record run.
- `debug.toc` entry 1: empty name, 8-byte extent, `word1` of 6.
- The 82 entries that fit neither extent cleanly: 59 with a full path but a 4-byte gap to
  the next record, 18 bare names whose following word is non-zero, and a handful with
  larger gaps. Small enough to be mis-parses of an interleaving case rather than a third
  record kind, but unconfirmed.
- Whether the engine validates the archive sizes in `game.toc`. The cheapest experiment:
  load a level whose archive has been repacked to a different length without refreshing
  them.
