using System.Buffers.Binary;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// Re-lays an IGHW 1.1 container (a new-engine zone blob, a region's collision.dat) with new payloads for some of
/// its sections (usually the old payload with records appended), the way the shipped builder lays them out:
///   - header 0x20: 'IGHW', 1.1, +0x08 section count, +0x0C header length (0x20 + 16 x count), +0x10 fileLength
///     (end of the last section), +0x14 fix-up count, +0x18 DEADDEADDEADDEAD;
///   - the section table {u32 id, u32 offset, u32 count, u32 length}: an array section has bit 0x10000000 in the
///     count and its stride as the length, any other its byte length;
///   - the sections in table order, each at the next 0x80 boundary (zero-size ones too), the gap before each filled
///     with "PAD0PAD1...PADFPAD0..." restarting at every gap;
///   - the fix-up table (u32 offsets of 32-bit pointers, sorted) at fileLength.
/// A pointer field moves with its section and its value with the section it points into. Rebuilding with nothing
/// appended gives the input's bytes (checked by dev/tools/tieappendprobe on every zone and collision file of the
/// ACiT levels).
/// </summary>
public static class IghwRelayout
{
    public const uint Magic = 0x49474857;

    public sealed class Section
    {
        public uint Id;
        public int Offset;
        public uint CountWord;
        public uint LengthWord;
        public bool IsArray => (CountWord & 0x10000000) != 0;
        public int Count => IsArray ? (int)(CountWord & 0x0FFFFFFF) : 1;
        public int Stride => IsArray ? (int)LengthWord : (int)LengthWord;
        public int Size => IsArray ? Count * (int)LengthWord : (int)LengthWord;
        public int End => Offset + Size;
    }

    public static List<Section> Sections(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 0x20 || BinaryPrimitives.ReadUInt32BigEndian(blob) != Magic)
            throw new InvalidDataException("not an IGHW container");
        int n = BinaryPrimitives.ReadInt32BigEndian(blob[8..]);
        var list = new List<Section>(n);
        for (int i = 0; i < n; i++)
        {
            int o = 0x20 + 16 * i;
            list.Add(new Section
            {
                Id = BinaryPrimitives.ReadUInt32BigEndian(blob[o..]),
                Offset = BinaryPrimitives.ReadInt32BigEndian(blob[(o + 4)..]),
                CountWord = BinaryPrimitives.ReadUInt32BigEndian(blob[(o + 8)..]),
                LengthWord = BinaryPrimitives.ReadUInt32BigEndian(blob[(o + 12)..]),
            });
        }
        return list;
    }

    public static Section? Find(List<Section> sections, uint id) => sections.Find(s => s.Id == id);

    /// <summary>A new pointer: the field at <c>FieldOffset</c> of section <c>FieldSection</c>'s new payload points at
    /// <c>TargetOffset</c> of section <c>TargetSection</c>'s new payload; it gets a fix-up entry.</summary>
    public readonly record struct NewPointer(uint FieldSection, int FieldOffset, uint TargetSection, int TargetOffset);

    /// <summary>The container with <paramref name="payloads"/> in place of those sections' payloads, every offset
    /// re-derived, and <paramref name="pointers"/> written and listed as fix-ups. An existing pointer into a section
    /// keeps its offset inside it, so a new payload that pointers reach must start with the old one, unless
    /// <paramref name="offsetMaps"/> gives the section's offset map (old offset inside the section -> new offset inside it, null
    /// for a removed byte, defined at the old size too): a pointer field in removed bytes is dropped, and a pointer into
    /// them is an error. That is how records are taken out.</summary>
    public static byte[] Relayout(byte[] blob, IReadOnlyDictionary<uint, byte[]> payloads, IReadOnlyList<NewPointer>? pointers = null,
        IReadOnlyDictionary<uint, Func<int, int?>>? offsetMaps = null)
    {
        var secs = Sections(blob);
        int headerLength = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x0C));
        int fileLength = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x10));
        int fixCount = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x14));
        if (headerLength != 0x20 + 16 * secs.Count) throw new InvalidDataException("IGHW header length is not 0x20 + 16 x sections");
        int last = headerLength;
        foreach (var s in secs)
        {
            if (s.Offset < last || s.End > fileLength) throw new InvalidDataException($"section 0x{s.Id:X} is out of table order");
            if (s.IsArray && s.LengthWord == 0) throw new InvalidDataException($"section 0x{s.Id:X} has stride 0");
            last = s.End;
        }
        foreach (var (id, bytes) in payloads)
        {
            var s = Find(secs, id) ?? throw new InvalidDataException($"no section 0x{id:X} to write");
            if (s.IsArray && bytes.Length % s.LengthWord != 0) throw new InvalidDataException($"section 0x{id:X}: payload is not whole records");
        }

        var fix = new List<int>(fixCount);
        for (int i = 0; i < fixCount; i++)
        {
            uint f = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(fileLength + 4 * i));
            if ((f & 0xC0000000) != 0) throw new InvalidDataException("64-bit fix-ups are not handled");
            fix.Add((int)f);
        }

        // New layout.
        var newOffset = new Dictionary<uint, int>();
        var newSize = new Dictionary<uint, int>();
        int pos = headerLength;
        foreach (var s in secs)
        {
            pos = Align(pos, 0x80);
            int size = payloads.TryGetValue(s.Id, out var payload) ? payload.Length : s.Size;
            newOffset[s.Id] = pos;
            newSize[s.Id] = size;
            pos += size;
        }
        int newFileLength = pos;
        int? TryRemap(int offset)
        {
            foreach (var s in secs)
                if (offset >= s.Offset && offset < s.End)
                    return offsetMaps != null && offsetMaps.TryGetValue(s.Id, out var map)
                        ? map(offset - s.Offset) is { } inside ? newOffset[s.Id] + inside : null
                        : newOffset[s.Id] + (offset - s.Offset);
            foreach (var s in secs)
                if (offset == s.End)
                    return offsetMaps != null && offsetMaps.TryGetValue(s.Id, out var map)
                        ? newOffset[s.Id] + (map(s.Size) ?? newSize[s.Id])
                        : newOffset[s.Id] + Math.Min(s.Size, newSize[s.Id]);
            throw new InvalidDataException($"pointer 0x{offset:X} is in no section");
        }
        var keptFix = fix.Where(f => TryRemap(f) != null).ToList();
        int newFixCount = keptFix.Count + (pointers?.Count ?? 0);
        int trailing = blob.Length - (fileLength + 4 * fixCount);
        if (trailing < 0) throw new InvalidDataException("IGHW fix-up table runs past the end");

        var output = new byte[newFileLength + 4 * newFixCount + trailing];
        blob.AsSpan(0, headerLength).CopyTo(output);
        int end = headerLength;
        foreach (var s in secs)
        {
            int at = newOffset[s.Id];
            Pad(output.AsSpan(end, at - end));
            if (payloads.TryGetValue(s.Id, out var payload)) payload.CopyTo(output.AsSpan(at));
            else blob.AsSpan(s.Offset, s.Size).CopyTo(output.AsSpan(at));
            end = at + newSize[s.Id];
        }
        for (int i = 0; i < secs.Count; i++)
        {
            var s = secs[i];
            int o = 0x20 + 16 * i;
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(o + 4), newOffset[s.Id]);
            if (s.IsArray)
                BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(o + 8), (s.CountWord & 0xF0000000) | (uint)(newSize[s.Id] / (int)s.LengthWord));
            else
                BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(o + 12), newSize[s.Id]);
        }

        var newFix = new List<int>(newFixCount);
        foreach (int f in keptFix)
        {
            int field = TryRemap(f)!.Value;
            int target = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(f));
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(field), TryRemap(target) ?? throw new InvalidDataException($"pointer 0x{f:X} points into removed bytes"));
            newFix.Add(field);
        }
        if (pointers != null)
            foreach (var p in pointers)
            {
                int field = newOffset[p.FieldSection] + p.FieldOffset;
                BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(field), newOffset[p.TargetSection] + p.TargetOffset);
                newFix.Add(field);
            }
        newFix.Sort();
        for (int i = 0; i < newFix.Count; i++)
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(newFileLength + 4 * i), newFix[i]);
        blob.AsSpan(blob.Length - trailing, trailing).CopyTo(output.AsSpan(newFileLength + 4 * newFixCount));
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(0x10), newFileLength);
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(0x14), newFix.Count);
        return output;
    }

    /// <summary>The container without the sections <paramref name="ids"/>, which must be empty and pointed at by
    /// nothing (a new zone's unused lightmap tables); the rest laid out again as <see cref="Relayout"/> does.</summary>
    public static byte[] WithoutSections(byte[] blob, IReadOnlySet<uint> ids)
    {
        var secs = Sections(blob);
        if (secs.Any(s => ids.Contains(s.Id) && s.Size != 0)) throw new InvalidDataException("only empty sections can be removed");
        int fileLength = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x10));
        int fixCount = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x14));
        var kept = secs.Where(s => !ids.Contains(s.Id)).ToList();
        int header = 0x20 + 16 * kept.Count;
        var offsets = new Dictionary<uint, int>();
        int pos = header;
        foreach (var s in kept) { offsets[s.Id] = Align(pos, 0x80); pos = offsets[s.Id] + s.Size; }
        int trailing = blob.Length - (fileLength + 4 * fixCount);
        var output = new byte[pos + 4 * fixCount + trailing];
        blob.AsSpan(0, 0x20).CopyTo(output);
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(8), kept.Count);
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(0x0C), header);
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(0x10), pos);
        int end = header;
        for (int i = 0; i < kept.Count; i++)
        {
            var s = kept[i];
            int e = 0x20 + 16 * i;
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(e), s.Id);
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(e + 4), offsets[s.Id]);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(e + 8), s.CountWord);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(e + 12), s.LengthWord);
            Pad(output.AsSpan(end, offsets[s.Id] - end));
            blob.AsSpan(s.Offset, s.Size).CopyTo(output.AsSpan(offsets[s.Id]));
            end = offsets[s.Id] + s.Size;
        }
        int Remap(int offset)
        {
            foreach (var s in kept)
                if (offset >= s.Offset && offset < s.End) return offsets[s.Id] + (offset - s.Offset);
            foreach (var s in kept)
                if (offset == s.End) return offsets[s.Id] + s.Size;
            throw new InvalidDataException($"pointer 0x{offset:X} is in no kept section");
        }
        var fix = new List<int>();
        for (int i = 0; i < fixCount; i++)
        {
            int f = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(fileLength + 4 * i));
            int field = Remap(f);
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(field), Remap(BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(f))));
            fix.Add(field);
        }
        fix.Sort();
        for (int i = 0; i < fix.Count; i++) BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(pos + 4 * i), fix[i]);
        blob.AsSpan(blob.Length - trailing, trailing).CopyTo(output.AsSpan(pos + 4 * fixCount));
        return output;
    }

    /// <summary>The container with empty sections added: each {Id, CountWord, LengthWord} goes before section <c>Before</c> (an
    /// array section gets count 0 under its flag). Fill them with <see cref="Relayout"/> afterwards.</summary>
    public static byte[] WithSections(byte[] blob, IReadOnlyList<(uint Before, uint Id, uint CountWord, uint LengthWord)> added)
    {
        var secs = Sections(blob);
        int fileLength = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x10));
        int fixCount = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x14));
        var table = new List<(uint Id, int OldOffset, int Size, uint CountWord, uint LengthWord)>();
        foreach (var sec in secs)
        {
            foreach (var a in added.Where(a => a.Before == sec.Id))
            {
                if (Find(secs, a.Id) != null) throw new InvalidDataException($"section 0x{a.Id:X} already exists");
                table.Add((a.Id, -1, 0, (a.CountWord & 0x10000000) != 0 ? 0x10000000u : a.CountWord, (a.CountWord & 0x10000000) != 0 ? a.LengthWord : 0));
            }
            table.Add((sec.Id, sec.Offset, sec.Size, sec.CountWord, sec.LengthWord));
        }
        if (table.Count != secs.Count + added.Count) throw new InvalidDataException("a section to insert before is missing");
        int header = 0x20 + 16 * table.Count;
        var offsets = new int[table.Count];
        int pos = header;
        for (int i = 0; i < table.Count; i++) { offsets[i] = Align(pos, 0x80); pos = offsets[i] + table[i].Size; }
        int trailing = blob.Length - (fileLength + 4 * fixCount);
        var output = new byte[pos + 4 * fixCount + trailing];
        blob.AsSpan(0, 0x20).CopyTo(output);
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(8), table.Count);
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(0x0C), header);
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(0x10), pos);
        int end = header;
        for (int i = 0; i < table.Count; i++)
        {
            var t = table[i];
            int e = 0x20 + 16 * i;
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(e), t.Id);
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(e + 4), offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(e + 8), t.CountWord);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(e + 12), t.LengthWord);
            Pad(output.AsSpan(end, offsets[i] - end));
            if (t.OldOffset >= 0) blob.AsSpan(t.OldOffset, t.Size).CopyTo(output.AsSpan(offsets[i]));
            end = offsets[i] + t.Size;
        }
        int Remap(int offset)
        {
            for (int i = 0; i < table.Count; i++)
                if (table[i].OldOffset >= 0 && offset >= table[i].OldOffset && offset < table[i].OldOffset + table[i].Size) return offsets[i] + (offset - table[i].OldOffset);
            for (int i = 0; i < table.Count; i++)
                if (table[i].OldOffset >= 0 && offset == table[i].OldOffset + table[i].Size) return offsets[i] + table[i].Size;
            throw new InvalidDataException($"pointer 0x{offset:X} is in no section");
        }
        var fix = new List<int>();
        for (int i = 0; i < fixCount; i++)
        {
            int f = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(fileLength + 4 * i));
            int field = Remap(f);
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(field), Remap(BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(f))));
            fix.Add(field);
        }
        fix.Sort();
        for (int i = 0; i < fix.Count; i++) BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(pos + 4 * i), fix[i]);
        blob.AsSpan(blob.Length - trailing, trailing).CopyTo(output.AsSpan(pos + 4 * fixCount));
        return output;
    }

    /// <summary>The builder's gap filler: "PAD" and a hex digit, restarting at 0 in every gap.</summary>
    public static void Pad(Span<byte> gap)
    {
        for (int i = 0; i < gap.Length; i++)
        {
            int k = i & 3;
            gap[i] = k < 3 ? (byte)"PAD"[k] : (byte)"0123456789ABCDEF"[(i >> 2) & 0xF];
        }
    }

    public static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
