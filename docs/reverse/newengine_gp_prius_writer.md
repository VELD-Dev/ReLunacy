# New-engine gp_prius.dat: structural writer (GpPriusDocument)

> Progress (2026-11-15): research done (sections 1-2 below); C# document + ops being written in
> ReLunacy.Engine/Packing/GpPrius*.cs; probe in dev/tools/gpwriteprobe.

## 1. Format facts learned for this writer (all 135 regions of ACiT 22, A4O 81, ITN 32)

- The ACiT re-layout rule holds unchanged on A4O and ITN: sections at the next 0x80 boundary in
  file order, PADn gaps, fix-up table after the data. `append_moby.py --roundtrip` is byte-identical on
  all 135 regions.
- A4O / ITN layer record (0x25008) is 0x90 bytes: name[0x40], **10** (ptr, count) slots. Slots 0..7 are
  ACiT's (Moby, path, volume, Clue, controller, Area, Pod, Scent), slot 8 is Squadrons (0x250A0 / 0x250A4),
  slot 9 (+0x88, +0x8C) is not a pointer (never a fix-up; nonzero on 276 layers) and is copied.
  0x2500C is 0x24 bytes (9 metadata pointers, same order).
- All fix-ups of A4O / ITN are known pointer fields: ACiT's set, plus Squadron +0 (-> 0x250A8), and every
  Pod / Scent / Area list entry and every Clue +0 (pointers into the reference pool 0x25022).
- Pool 0x25022 (A4O / ITN): one 0x10 entry (TUID, kind, 0) per referencing word; never shared, no unused
  entries (170,378 A4O + 21,230 ITN entries, all used exactly once). Order is not the user order.
- Per kind, records and metadata are grouped by ascending layer and the layer table pointers match, on all
  three games (same invariants as ACiT).
- TUIDs: A4O has no duplicate TUID in any kind. ITN repeats 14 Moby, 9 Pod, 7 volume TUIDs. ACiT: 573
  volume TUID repeats (the Clue-generated volumes on TUID 0/1).
- Settings blocks in 0x25020 sit back to back with no gap (4-byte aligned), in no required order.
- Path points (0x25058) are back to back in path record order, no gap. Path +4 is always 0x07041776.
- Pod and Scent lists are back to back in record order. ACiT Area lists are contiguous each but not in
  record order (A4O / ITN: in order). An empty list always has a null pointer (no fix-up).
- **gameplay.dat 0x25000 +0x10..+0x3C are maxima over the level's regions** (budgets), on all 40 levels;
  A4O / ITN add +0x40 Squadrons, +0x44 Squadron entries, +0x48 (Squadron-related, formula not found),
  +0x4C Clues, +0x50 Scents (maxima as well).
- metadata.dat holds the layer names only (0x25098 count, 0x25100 names, 0x25110 offsets, 0x25104 zero
  words): no per-layer instance counts.
