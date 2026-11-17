using System.Buffers.Binary;

namespace ReLunacy.Engine.Packing;

/// <summary>One reference to an instance: the instance holding it (a Moby, Clue or controller for a settings word or a
/// Clue's volume, a Pod, Area or Scent for a list entry) and where in it.</summary>
public sealed record GpReference(GpInstance Target, GpInstance Holder, string Where);

public sealed partial class GpPriusDocument
{
    /// <summary>The kinds <see cref="Delete"/> takes out. Pods, Areas and Scents are named groups (their lists go with them,
    /// and the groups behind them in their layer are renumbered); Squadrons are not edited.</summary>
    public static bool Deletable(GpKind kind) => kind is GpKind.Moby or GpKind.Path or GpKind.Volume or GpKind.Clue or GpKind.Controller
        or GpKind.Pod or GpKind.Area or GpKind.Scent;

    private static bool IsGroup(GpKind kind) => kind is GpKind.Pod or GpKind.Area or GpKind.Scent;

    private enum SlotKind { Link, Member, Field, ListItem, Unresolved }

    /// <summary>A place that names an instance: a Clue's volume word, a Pod / Area / Scent list entry, a settings word,
    /// or (All 4 One / Into the Nexus) a pool entry of a settings block that no struct word uses (a list).</summary>
    private readonly record struct Slot(SlotKind Kind, GpInstance Holder, GpInstance Target, string Where, GpSection Section, int At);

    // ---- what references what ---------------------------------------------------------------------------------

    /// <summary>Every reference to one of <paramref name="targets"/>, one per place that names it. A settings block shared
    /// by several instances is listed under each.</summary>
    public List<GpReference> ReferencesTo(IReadOnlyCollection<GpInstance> targets)
    {
        var found = new List<GpReference>();
        foreach (var slot in Slots(shared: true))
            if (targets.Contains(slot.Target)) found.Add(new GpReference(slot.Target, slot.Holder, slot.Where));
        return found;
    }

    /// <summary>
    /// Every resolvable reference of the file. <paramref name="shared"/> keeps a settings block each instance uses
    /// (a read-only scan); false lists a block once, under the first instance using it (an edit writes it once).
    /// </summary>
    private List<Slot> Slots(bool shared)
    {
        var slots = new List<Slot>();
        if (RecordsOf(GpKind.Clue) is { } clues)
            for (int i = 0; i < Count(GpKind.Clue); i++)
                if (ResolveReference(clues, i * 0x10) is { } volume)
                    slots.Add(new Slot(SlotKind.Link, new(GpKind.Clue, i), volume, "volume", clues, i * 0x10));
        foreach (var (group, member) in new[] { (GpKind.Pod, GpKind.Moby), (GpKind.Scent, GpKind.Clue), (GpKind.Area, GpKind.Path), (GpKind.Area, GpKind.Volume) })
        {
            if (RecordsOf(group) is not { } records || ListOf(group, member) is not { } field) continue;
            for (int g = 0; g < Count(group); g++)
            {
                int record = g * 0x10;
                int n = field.CountSize == 4 ? (int)Word(records, record + field.Count) : Half(records, record + field.Count);
                if (PointerAt(records, record + field.Pointer) is not { } p) continue;
                for (int k = 0; k < n; k++)
                    if (ResolveReference(p.Target, p.TargetAt + 4 * k) is { } target)
                        slots.Add(new Slot(SlotKind.Member, new(group, g), target, "member", p.Target, p.TargetAt + 4 * k));
            }
        }
        var seen = new HashSet<(GpSection, int)>();
        foreach (var kind in new[] { GpKind.Moby, GpKind.Clue, GpKind.Controller })
            for (int i = 0; i < Count(kind); i++)
            {
                var holder = new GpInstance(kind, i);
                var (block, size) = SettingsOf(holder);
                if (block == null || size < 8 || block.TargetAt + size > block.Target.Data.Length) continue;
                if (!shared && !seen.Add((block.Target, block.TargetAt))) continue;
                SettingsSlots(holder, block, size, slots);
            }
        return slots;
    }

    private void SettingsSlots(GpInstance holder, GpPointer block, int size, List<Slot> slots)
    {
        var data = block.Target.Data.AsSpan(block.TargetAt, size);
        var sections = InnerSections(data);
        if (Pooled && sections.Count > 0)
        {
            PooledSettingsSlots(holder, block, data, sections, slots);
            return;
        }
        foreach (var (at, field, item) in SettingsWordPlaces(data, sections))
            if (DecodeInline(Word(data, at)) is { } target)
                slots.Add(new Slot(item >= 0 ? SlotKind.ListItem : SlotKind.Field, holder, target,
                    field >= 0 ? (item >= 0 ? $"+0x{field:X}[{item}]" : $"+0x{field:X}") : "extra data", block.Target, block.TargetAt + at));
    }

    /// <summary>The words of a settings block that may hold an inline reference: (offset in the block, struct field it
    /// belongs to or -1, item of the list it is in or -1). A flat block is all struct; an inner IGHW's struct (from +4),
    /// the elements of the lists its pointer fields name, and any other word of its extra data.</summary>
    private static List<(int At, int Field, int Item)> SettingsWordPlaces(ReadOnlySpan<byte> block, List<(uint Id, int Offset, int Length)> sections)
    {
        var words = new List<(int, int, int)>();
        if (sections.Count == 0)
        {
            for (int w = 4; w + 4 <= block.Length; w += 4) words.Add((w, w, -1));
            return words;
        }
        var pointers = InnerFixups(block);
        var covered = new HashSet<int>();
        foreach (var (id, offset, length) in sections)
        {
            if (id != 0x2501C || offset + length > block.Length) continue;
            foreach (int location in pointers.Where(l => l >= offset && l + 4 <= offset + length).Order())
            {
                int list = (int)Word(block, location);
                foreach (var (_, eo, el) in sections)
                {
                    if (list < eo + 4 || list > eo + el || eo + el > block.Length) continue;
                    int count = (int)Word(block, list - 4);
                    if (count <= 0 || (eo + el - list) < count * 4) break;
                    int step = (eo + el - list) / count & ~3;
                    covered.Add(list - 4);
                    for (int item = 0; item < count; item++)
                        for (int w = 0; w < step; w += 4)
                        {
                            words.Add((list + item * step + w, location - offset, item));
                            covered.Add(list + item * step + w);
                        }
                    break;
                }
            }
        }
        foreach (var (id, offset, length) in sections)
        {
            if (id == ReferencePool || offset + length > block.Length) continue;
            for (int w = offset + (id == 0x2501C ? 4 : 0); w + 4 <= offset + length; w += 4)
                if (!pointers.Contains(w) && (w & 3) == 0 && !covered.Contains(w))
                    words.Add((w, id == 0x2501C ? w - offset : -1, -1));
        }
        return words;
    }

    /// <summary>All 4 One / Into the Nexus: a settings block's struct words point at entries of the block's own pool.
    /// An entry no struct word uses belongs to a list, which is not read here: it is reported so a deletion refuses.</summary>
    private void PooledSettingsSlots(GpInstance holder, GpPointer block, ReadOnlySpan<byte> data, List<(uint Id, int Offset, int Length)> sections, List<Slot> slots)
    {
        var pool = sections.FirstOrDefault(s => s.Id == ReferencePool);
        var structure = sections.FirstOrDefault(s => s.Id == 0x2501C);
        if (pool.Length == 0 || structure.Length == 0 || pool.Offset + pool.Length > data.Length || structure.Offset + structure.Length > data.Length) return;
        var used = new HashSet<int>();
        for (int w = structure.Offset + 4; w + 4 <= structure.Offset + structure.Length; w += 4)
        {
            int value = (int)Word(data, w);
            if (value < pool.Offset || value + 0x0C > pool.Offset + pool.Length || (value - pool.Offset) % 0x10 != 0) continue;
            used.Add(value);
            if (PoolTarget(data, value) is { } target)
                slots.Add(new Slot(SlotKind.Field, holder, target, $"+0x{w - structure.Offset:X}", block.Target, block.TargetAt + w));
        }
        for (int e = pool.Offset; e + 0x10 <= pool.Offset + pool.Length; e += 0x10)
            if (!used.Contains(e) && PoolTarget(data, e) is { } target)
                slots.Add(new Slot(SlotKind.Unresolved, holder, target, "list", block.Target, block.TargetAt + e));
    }

    private GpInstance? PoolTarget(ReadOnlySpan<byte> data, int entry)
    {
        ulong tuid = BinaryPrimitives.ReadUInt64BigEndian(data[entry..]);
        uint kind = Word(data, entry + 8);
        if (kind > (uint)GpKind.Pod || MetadataOf((GpKind)kind) is not { } meta) return null;
        for (int i = 0; i < meta.Count; i++)
            if (BinaryPrimitives.ReadUInt64BigEndian(meta.Data.AsSpan(i * 0x10)) == tuid) return new GpInstance((GpKind)kind, i);
        return null;
    }

    // ---- delete -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Deletes instances (Mobys, paths, volumes, Clues, controllers) and everything that named them, so the level loads
    /// without a dangling reference:
    ///   - a volume's Clue goes with it (a Clue's +0 is its volume);
    ///   - Pod, Area and Scent list entries naming a deleted instance are removed (a list that empties loses its pointer);
    ///   - a settings word (Moby, Clue, controller) naming one is set to 0, the "no reference" every unset field holds;
    ///   - a list element in a settings block's extra data (a SoundEnvironment's volumes, a SoundEmitter's environments)
    ///     naming one is cut out of the list (the block is laid out again, its count and size follow);
    ///   - in A Crack in Time's form, every other reference to an instance behind a deleted one in its layer is rewritten
    ///     (a handle is the place in the layer); on All 4 One / Into the Nexus references are TUIDs and do not move;
    ///   - the records, metadata and what they own (a private settings block, a Moby's matrices, a path's points, a pool
    ///     entry) are cut out, and the layer counts and pointers follow.
    /// All or nothing: the document is not touched when anything is refused. The instances behind a deleted one are
    /// renumbered (<see cref="GpEditResult.Shifts"/>): instances.lua and the level scripts name them by handle and are not
    /// rewritten. Names stay in 0x11300 and gameplay.dat's budgets stay as they are (they are maxima).
    /// </summary>
    public GpEditResult Delete(IReadOnlyCollection<GpInstance> requested)
    {
        if (requested.Count == 0) return GpEditResult.Refuse("nothing to delete");
        var dead = new HashSet<GpInstance>();
        foreach (var i in requested)
        {
            if (!Deletable(i.Kind)) return GpEditResult.Refuse($"a {i.Kind} cannot be deleted");
            if (i.Index < 0 || i.Index >= Count(i.Kind) || RecordsOf(i.Kind) == null) return GpEditResult.Refuse($"no such {i.Kind}");
            dead.Add(i);
        }
        // A Clue goes with its volume.
        if (RecordsOf(GpKind.Clue) is { } clues)
            for (int c = 0; c < Count(GpKind.Clue); c++)
                if (ResolveReference(clues, c * 0x10) is { Kind: GpKind.Volume } v && dead.Contains(v)) dead.Add(new GpInstance(GpKind.Clue, c));
        if (Pooled)
            foreach (var i in dead)
                if (Enumerable.Range(0, Count(i.Kind)).Count(j => Tuid(new GpInstance(i.Kind, j)) == Tuid(i)) > 1)
                    return GpEditResult.Refuse($"{i.Kind} {Name(i)} shares its TUID with another {i.Kind}; a reference (by TUID) could name either");
        foreach (var slot in Slots(shared: false))
            if (slot.Kind == SlotKind.Unresolved && dead.Contains(slot.Target))
                return GpEditResult.Refuse($"{slot.Target.Kind} {Name(slot.Target)} is named in a list of {slot.Holder.Kind} {Name(slot.Holder)}'s settings, which this game's form is not read for");

        var result = new GpEditResult();
        var names = dead.ToDictionary(i => i, i => $"{i.Kind} {Name(i)}");

        // 0. A deleted group's lists go first, while every reference still resolves: its members leave it one by one.
        foreach (var group in dead.Where(i => IsGroup(i.Kind)))
            foreach (var member in new[] { GpKind.Moby, GpKind.Path, GpKind.Volume, GpKind.Clue })
                foreach (var held in Members(group, member).Where(m => m != null).Distinct().ToList())
                    while (Members(group, member).Contains(held))
                    {
                        var removed = RemoveMember(group, held!.Value);
                        if (!removed.Applied) throw new InvalidOperationException(string.Join("; ", removed.Problems));
                    }

        // The handles that move: in each layer's run of a kind, the later instances move up by one per deleted one before.
        var deadWords = new HashSet<uint>();
        var map = new Dictionary<uint, uint>();
        uint Encode(GpKind kind, int layer, int index) => (uint)((((int)kind << 10) | layer) << 16) | 0xF000u | (uint)index;
        foreach (var kind in dead.Select(i => i.Kind).Distinct())
            for (int layer = 0; layer < LayerCount; layer++)
            {
                var (first, count) = LayerRun(kind, layer);
                int removed = 0;
                for (int j = 0; j < count; j++)
                {
                    var instance = new GpInstance(kind, first + j);
                    if (dead.Contains(instance)) { removed++; deadWords.Add(Encode(kind, layer, j)); continue; }
                    if (removed == 0) continue;
                    map[Encode(kind, layer, j)] = Encode(kind, layer, j - removed);
                    result.Shifts.Add(new GpIndexShift(kind, Name(instance), Tuid(instance), (ushort)layer, j, (ushort)layer, j - removed));
                }
            }

        // 1. List entries (Pod, Area, Scent) naming a deleted instance.
        foreach (var slot in Slots(shared: false).Where(s => s.Kind == SlotKind.Member && dead.Contains(s.Target)).ToList())
        {
            while (Members(slot.Holder, slot.Target.Kind).Contains(slot.Target))
            {
                var removed = RemoveMember(slot.Holder, slot.Target);
                if (!removed.Applied) throw new InvalidOperationException(string.Join("; ", removed.Problems));
            }
            result.Changes.Add($"{slot.Holder.Kind} {Name(slot.Holder)}: {names[slot.Target]} removed");
        }

        // 2. List elements in settings blocks (inline form).
        if (!Pooled)
            foreach (var kind in new[] { GpKind.Moby, GpKind.Clue, GpKind.Controller })
                for (int i = 0; i < Count(kind); i++)
                    if (!dead.Contains(new GpInstance(kind, i))) PruneLists(new GpInstance(kind, i), deadWords, result);

        // 3. Settings words and Clue links: a deleted one is cleared, a moved one rewritten.
        foreach (var slot in Slots(shared: false))
        {
            // A Clue's link to a deleted volume goes with the Clue; list elements are cut out above.
            if (slot.Kind is SlotKind.Member or SlotKind.Unresolved or SlotKind.Link or SlotKind.ListItem || !dead.Contains(slot.Target)) continue;
            PutWord(slot.Section, slot.At, 0);
            result.ReferencesCleared++;
            result.Changes.Add($"{slot.Holder.Kind} {Name(slot.Holder)} {slot.Where}: {names[slot.Target]} cleared");
        }
        if (!Pooled)
            foreach (var slot in Slots(shared: false))
            {
                if (dead.Contains(slot.Target) || slot.Kind == SlotKind.Unresolved) continue;
                if (map.TryGetValue(Word(slot.Section, slot.At), out uint now))
                {
                    PutWord(slot.Section, slot.At, now);
                    result.ReferencesRewritten++;
                }
            }

        // 4. The records, last to first so every index in hand stays valid.
        foreach (var i in dead.OrderBy(i => i.Kind).ThenByDescending(i => i.Index))
        {
            RemoveRecord(i);
            result.Changes.Insert(0, $"{names[i]} deleted");
        }
        if (map.Count > 0) result.Changes.Add($"{result.Shifts.Count} later instance(s) renumbered in their layer");
        result.Applied = true;
        return result;
    }

    /// <summary>Cuts the elements naming a deleted instance out of the lists of a settings block's extra data. The block is
    /// laid out again as an IGHW 0.2 (<see cref="Ighw02"/>); its size words in every record that uses it follow.</summary>
    private void PruneLists(GpInstance holder, HashSet<uint> deadWords, GpEditResult result)
    {
        var (block, size) = SettingsOf(holder);
        if (block == null || size < 0x20 || block.TargetAt + size > block.Target.Data.Length) return;
        var data = block.Target.Data.AsSpan(block.TargetAt, size);
        var sections = InnerSections(data);
        if (sections.Count == 0 || sections.Any(s => s.Id == ReferencePool)) return;
        bool any = false;
        foreach (var w in SettingsWordPlaces(data, sections)) any |= w.Item >= 0 && deadWords.Contains(Word(data, w.At));
        if (!any) return;

        var g = Ighw02.Parse(data.ToArray());
        var structure = g.Chunks.Find(c => c.Id == 0x2501C);
        if (structure == null) return;
        int removedItems = 0;
        var lists = g.Pointers.Where(p => p.Field == structure && p.Target != structure && p.TargetAt >= 4)
            .Select(p => (p.Target, p.TargetAt)).Distinct().ToList();
        foreach (var (chunk, list) in lists)
        {
            int count = (int)Word(chunk.Data, list - 4);
            if (count <= 0 || chunk.Data.Length - list < count * 4) continue;
            int step = (chunk.Data.Length - list) / count & ~3;
            for (int item = count - 1; item >= 0; item--)
            {
                bool named = false;
                for (int w = 0; w < step; w += 4) named |= deadWords.Contains(Word(chunk.Data, list + item * step + w));
                if (!named) continue;
                g.Splice(chunk, list + item * step, step, []);
                count--;
                BinaryPrimitives.WriteUInt32BigEndian(chunk.Data.AsSpan(list - 4), (uint)count);
                removedItems++;
            }
        }
        if (removedItems == 0) return;
        byte[] now = g.Serialize();
        var (section, at) = (block.Target, block.TargetAt);
        Splice(section, at, size, now);
        // Every record using the block holds its size.
        foreach (var kind in new[] { GpKind.Moby, GpKind.Clue, GpKind.Controller })
            for (int i = 0; i < Count(kind); i++)
            {
                var (b, s) = SettingsOf(new GpInstance(kind, i));
                if (b == null || b.Target != section || b.TargetAt != at || s != size) continue;
                var records = RecordsOf(kind)!;
                int field = RecordAt(new GpInstance(kind, i)) + kind switch { GpKind.Moby => 0x10, GpKind.Clue => 0x08, _ => 0x04 };
                PutWord(records, field, (uint)now.Length);
            }
        result.Changes.Add($"{holder.Kind} {Name(holder)}: {removedItems} list element(s) removed ({size} -> {now.Length} bytes of settings)");
        result.ReferencesCleared += removedItems;
    }

    /// <summary>Cuts one instance's record, metadata and what only it owns out of the file.</summary>
    private void RemoveRecord(GpInstance i)
    {
        var info = Info(i.Kind);
        var records = RecordsOf(i.Kind)!;
        var meta = MetadataOf(i.Kind)!;
        ushort layer = Layer(i);
        int from = RecordAt(i);
        var (block, blockSize) = SettingsOf(i);
        (GpSection Section, int At, int Length)? payload = null;
        if (block != null && blockSize > 0 && block.Target is { Id: PrivateSettings, IsTail: false })
            payload = (block.Target, block.TargetAt, Math.Min((blockSize + 3) & ~3, block.Target.Data.Length - block.TargetAt));
        (GpSection Section, int At, int Length)? matrices = null, points = null, pool = null;
        if (i.Kind == GpKind.Moby && PointerAt(records, from + 0x40) is { } m && Half(records, from + 0x44) > 0)
            matrices = (m.Target, m.TargetAt, Math.Min(Half(records, from + 0x44) * 0x40, m.Target.Data.Length - m.TargetAt));
        if (i.Kind == GpKind.Path && PointerAt(records, from) is { } pp && Half(records, from + 0x0E) > 0)
            points = (pp.Target, pp.TargetAt, Math.Min(Half(records, from + 0x0E) * 0x10, pp.Target.Data.Length - pp.TargetAt));
        if (Pooled && i.Kind == GpKind.Clue && PointerAt(records, from) is { Target.Id: ReferencePool } vp)
            pool = (vp.Target, vp.TargetAt, 0x10);

        // A group's name is its own string in 0x11300 (a string others point at stays).
        (GpSection Section, int At, int Length)? name = null;
        if (IsGroup(i.Kind) && PointerAt(meta, i.Index * 0x10 + 8) is { } np && np.Target.Id == Names && !np.Target.IsTail)
        {
            int end = Array.IndexOf(np.Target.Data, (byte)0, np.TargetAt);
            if (end >= 0) name = (np.Target, np.TargetAt, end - np.TargetAt + 1);
        }

        // What is left of a group's lists (members that did not resolve).
        var lists = new List<(GpSection Section, int At, int Length)?>();
        if (IsGroup(i.Kind))
            foreach (var (pointer, count, size) in i.Kind == GpKind.Area ? new[] { (0x00, 0x08, 2), (0x04, 0x0A, 2) } : [(0x00, 0x04, 4)])
                if (PointerAt(records, from + pointer) is { } lp)
                {
                    int n = size == 4 ? (int)Word(records, from + count) : Half(records, from + count);
                    if (n > 0) lists.Add((lp.Target, lp.TargetAt, Math.Min(n * 4, lp.Target.Data.Length - lp.TargetAt)));
                }

        Splice(records, from, info.RecordSize, []);
        Splice(meta, i.Index * 0x10, 0x10, []);
        AdjustLayerCount(i.Kind, layer, -1, 0);

        // What the record owned goes unless something else still points into it (blocks and pool entries can be shared).
        foreach (var owned in new[] { payload, matrices, points, pool, name }.Concat(lists))
        {
            if (owned is not { } o || o.Length <= 0) continue;
            if (_pointers.Any(p => p.Target == o.Section && p.TargetAt >= o.At && p.TargetAt < o.At + o.Length)) continue;
            Splice(o.Section, o.At, o.Length, []);
        }
    }
}
