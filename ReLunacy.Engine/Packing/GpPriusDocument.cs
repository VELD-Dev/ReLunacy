using System.Buffers.Binary;
using System.Text;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// A new-engine region's gp_prius.dat (A Crack in Time, All 4 One, Into the Nexus) held as editable parts, so instances
/// can be added and moved and the file re-laid out around them; see docs/reverse/newengine_gp_prius_writer.md and
/// docs/reverse/acit_gp_prius_coverage.md.
///
/// The file is an IGHW 1.1 container (big-endian):
///   - header 0x20: 'IGHW', 0x00010001, +0x08 section count, +0x0C header end (0x20 + 16 x count), +0x10 data end,
///     +0x14 fix-up count, +0x18 0xDEADDEAD fill;
///   - the section table {id, offset, count word, length}: an array section has 0x10000000 in its count word and its
///     element size as the length, any other section its byte length;
///   - the sections, each at the next 0x80 boundary, the gaps filled with the linker's "PAD0".."PADF" words;
///   - the fix-up table (sorted u32 offsets of every 32-bit pointer in the data), from the data end to the file end.
///
/// Every pointer of the file is in the fix-up table and every fix-up is a known pointer field (proven on all 135
/// regions of the three games), so the document keeps each pointer symbolically - (section, offset) of the field,
/// (section, offset) of its target - and the section bytes otherwise as they are. An edit splices bytes into or out
/// of a section (<see cref="Splice"/>), which moves every pointer field and target behind the splice point; the file
/// is then laid out again by the shipped rule. Unedited, <see cref="Serialize"/> gives back the input byte for byte
/// (the gaps keep their original bytes while their length is unchanged). Data after the last section that a
/// previous in-place save appended (PodWriter's grown lists) is kept as a tail of its own.
///
/// What the parts mean (records, layers, references) is in GpPriusDocument.Model.cs; the operations are in
/// GpPriusDocument.Edits.cs.
/// </summary>
public sealed partial class GpPriusDocument
{
    public const uint Magic = 0x49474857, Version11 = 0x00010001;
    private const int SectionAlignment = 0x80, TailAlignment = 0x10;

    /// <summary>One section of the container: its bytes and its table entry.</summary>
    public sealed class GpSection
    {
        public uint Id { get; internal set; }
        /// <summary>The table's count word as the file had it (flags kept); arrays keep their count in the low 24 bits.</summary>
        public uint CountWord { get; internal set; }
        /// <summary>Element size for an array section, byte length for any other.</summary>
        public uint LengthWord { get; internal set; }
        public bool IsArray => (CountWord & 0x10000000) != 0;
        public int Count => IsArray ? (int)(CountWord & 0xFFFFFF) : 1;
        public int Element => IsArray ? (int)LengthWord : Data.Length;
        public byte[] Data { get; internal set; } = [];
        /// <summary>Position in the section table; -1 for the tail (data after the last section, no table entry).</summary>
        internal int TableIndex;
        internal int OriginalOffset;
        /// <summary>The bytes that preceded this section in the input, reused while the gap keeps its length.</summary>
        internal byte[] OriginalGap = [];
        public bool IsTail => TableIndex < 0;
        public override string ToString() => IsTail ? "tail" : $"0x{Id:X5}";
    }

    /// <summary>A pointer field: the 32-bit word at <see cref="At"/> of <see cref="Field"/> holds the file offset of
    /// <see cref="TargetAt"/> in <see cref="Target"/>. <see cref="Type"/> is the fix-up entry's top two bits.</summary>
    public sealed class GpPointer
    {
        public GpSection Field = null!;
        public int At;
        public GpSection Target = null!;
        public int TargetAt;
        public uint Type;
    }

    private readonly byte[] _header;
    private readonly List<GpSection> _table = [];
    /// <summary>Sections in file order (the tail last).</summary>
    private readonly List<GpSection> _layout = [];
    private readonly List<GpPointer> _pointers = [];
    private Dictionary<(GpSection, int), GpPointer>? _pointerIndex;

    public IReadOnlyList<GpSection> Sections => _table;
    public IReadOnlyList<GpPointer> Pointers => _pointers;
    public int OriginalLength { get; }

    /// <summary>Whether anything was edited since the file was read.</summary>
    public bool Edited { get; private set; }

    private GpPriusDocument(byte[] header, int originalLength)
    {
        _header = header;
        OriginalLength = originalLength;
    }

    /// <summary>Reads a gp_prius.dat. Throws <see cref="InvalidDataException"/> when the file is not laid out as the
    /// document expects (a pointer outside every section, a fix-up type it does not know, ...): such a file is not
    /// edited structurally.</summary>
    public static GpPriusDocument Parse(byte[] file)
    {
        var span = file.AsSpan();
        if (file.Length < 0x20 || Word(span, 0) != Magic) throw new InvalidDataException("not an IGHW file");
        if (Word(span, 4) != Version11) throw new InvalidDataException($"IGHW version 0x{Word(span, 4):X8}, not 1.1");
        int count = (int)Word(span, 8), headerEnd = (int)Word(span, 0x0C), dataEnd = (int)Word(span, 0x10), fixups = (int)Word(span, 0x14);
        if (headerEnd != 0x20 + 16 * count) throw new InvalidDataException("header end is not after the section table");
        if (dataEnd + 4L * fixups != file.Length) throw new InvalidDataException("the file does not end with its fix-up table");

        var doc = new GpPriusDocument(file[..0x20], file.Length);
        for (int i = 0; i < count; i++)
        {
            int at = 0x20 + 16 * i;
            var s = new GpSection
            {
                Id = Word(span, at), CountWord = Word(span, at + 8), LengthWord = Word(span, at + 12),
                TableIndex = i, OriginalOffset = (int)Word(span, at + 4),
            };
            long size = s.IsArray ? (long)(s.CountWord & 0xFFFFFF) * s.LengthWord : s.LengthWord;
            if (s.OriginalOffset < headerEnd || s.OriginalOffset + size > dataEnd)
                throw new InvalidDataException($"section 0x{s.Id:X} lies outside the data");
            s.Data = file.AsSpan(s.OriginalOffset, (int)size).ToArray();
            doc._table.Add(s);
        }
        var ordered = doc._table.OrderBy(s => s.OriginalOffset).ThenBy(s => s.TableIndex).ToList();
        int previous = headerEnd;
        foreach (var s in ordered)
        {
            if (s.OriginalOffset < previous) throw new InvalidDataException($"section 0x{s.Id:X} overlaps the one before it");
            s.OriginalGap = file.AsSpan(previous, s.OriginalOffset - previous).ToArray();
            previous = s.OriginalOffset + s.Data.Length;
            doc._layout.Add(s);
        }
        if (previous < dataEnd)
        {
            // Data a previous save put after the last section (PodWriter appends grown Pod lists there).
            int start = Align(previous, TailAlignment);
            if (start > dataEnd || file.AsSpan(previous, start - previous).ContainsAnyExcept((byte)0)) start = previous;
            var tail = new GpSection
            {
                Id = 0, TableIndex = -1, OriginalOffset = start,
                OriginalGap = file.AsSpan(previous, start - previous).ToArray(),
                Data = file.AsSpan(start, dataEnd - start).ToArray(),
            };
            doc._layout.Add(tail);
        }

        // Every fix-up becomes a symbolic pointer.
        for (int i = 0; i < fixups; i++)
        {
            uint entry = Word(span, dataEnd + 4 * i);
            uint type = entry & 0xC0000000, location = entry & 0x3FFFFFFF;
            if (type != 0) throw new InvalidDataException($"fix-up type 0x{type >> 30:X} is not written by this document");
            var field = doc.SectionAt((int)location, 4, false) ?? throw new InvalidDataException($"fix-up 0x{location:X} is in no section");
            uint value = Word(span, (int)location);
            var target = doc.SectionAt((int)value, 0, true) ?? throw new InvalidDataException($"pointer at 0x{location:X} -> 0x{value:X} lands in no section");
            doc._pointers.Add(new GpPointer
            {
                Field = field, At = (int)location - field.OriginalOffset,
                Target = target, TargetAt = (int)value - target.OriginalOffset, Type = type,
            });
        }
        return doc;
    }

    /// <summary>The section holding [offset, offset + size) of the input; with <paramref name="allowEnd"/>, a target
    /// exactly at a section's end counts as in it when no section starts there.</summary>
    private GpSection? SectionAt(int offset, int size, bool allowEnd)
    {
        foreach (var s in _layout)
            if (offset >= s.OriginalOffset && offset + Math.Max(size, 1) <= s.OriginalOffset + s.Data.Length) return s;
        if (allowEnd)
            foreach (var s in _layout)
                if (offset == s.OriginalOffset + s.Data.Length) return s;
        return null;
    }

    public GpSection? Section(uint id) => _table.Find(s => s.Id == id);

    /// <summary>
    /// The file: the sections laid out again (each at the next 0x80 boundary, in file order; a gap keeps its input
    /// bytes when its length did not change, else it is PADn-filled), every pointer written with its target's new
    /// offset, and the sorted fix-up table.
    /// </summary>
    public byte[] Serialize()
    {
        int headerEnd = 0x20 + 16 * _table.Count;
        var offsets = new Dictionary<GpSection, int>();
        int at = headerEnd;
        var gaps = new List<(int Start, int Length, GpSection Section)>();
        foreach (var s in _layout)
        {
            int start = Align(at, s.IsTail ? TailAlignment : SectionAlignment);
            gaps.Add((at, start - at, s));
            offsets[s] = start;
            at = start + s.Data.Length;
        }
        int dataEnd = at;
        var fixups = new List<uint>(_pointers.Count);
        foreach (var p in _pointers) fixups.Add((uint)(offsets[p.Field] + p.At) | p.Type);
        fixups.Sort();
        for (int i = 1; i < fixups.Count; i++)
            if (fixups[i] == fixups[i - 1]) throw new InvalidOperationException($"two pointers at 0x{fixups[i]:X}");

        byte[] file = new byte[dataEnd + 4 * fixups.Count];
        _header.CopyTo(file, 0);
        Put(file, 0x0C, (uint)headerEnd);
        Put(file, 0x10, (uint)dataEnd);
        Put(file, 0x14, (uint)fixups.Count);
        foreach (var s in _table)
        {
            int e = 0x20 + 16 * s.TableIndex;
            Put(file, e, s.Id);
            Put(file, e + 4, (uint)offsets[s]);
            Put(file, e + 8, s.CountWord);
            Put(file, e + 12, s.IsArray ? s.LengthWord : (uint)s.Data.Length);
        }
        foreach (var (start, length, s) in gaps)
        {
            var gap = file.AsSpan(start, length);
            if (s.OriginalGap.Length == length) s.OriginalGap.CopyTo(gap);
            else if (!s.IsTail) Pad(gap);
            s.Data.CopyTo(file, offsets[s]);
        }
        foreach (var p in _pointers) Put(file, offsets[p.Field] + p.At, (uint)(offsets[p.Target] + p.TargetAt));
        for (int i = 0; i < fixups.Count; i++) Put(file, dataEnd + 4 * i, fixups[i]);
        return file;
    }

    /// <summary>The linker's alignment filler: "PAD0PAD1...PADF" repeating, the last word cut at the boundary.</summary>
    private static void Pad(Span<byte> gap)
    {
        for (int i = 0; i < gap.Length; i++)
        {
            int k = i & 3;
            gap[i] = k < 3 ? (byte)"PAD"[k] : (byte)"0123456789ABCDEF"[(i >> 2) & 0xF];
        }
    }

    // ---- pointers ---------------------------------------------------------------------------------------------

    /// <summary>The pointer whose field is the word at <paramref name="at"/> of <paramref name="section"/>, if any.</summary>
    public GpPointer? PointerAt(GpSection section, int at)
    {
        _pointerIndex ??= _pointers.ToDictionary(p => (p.Field, p.At));
        return _pointerIndex.GetValueOrDefault((section, at));
    }

    /// <summary>Makes the word at (<paramref name="field"/>, <paramref name="at"/>) a pointer to
    /// (<paramref name="target"/>, <paramref name="targetAt"/>), replacing a pointer already there.</summary>
    public GpPointer SetPointer(GpSection field, int at, GpSection target, int targetAt)
    {
        if (at < 0 || at + 4 > field.Data.Length) throw new ArgumentOutOfRangeException(nameof(at));
        if (targetAt < 0 || targetAt > target.Data.Length) throw new ArgumentOutOfRangeException(nameof(targetAt));
        if (PointerAt(field, at) is { } existing)
        {
            existing.Target = target;
            existing.TargetAt = targetAt;
            Edited = true;
            return existing;
        }
        var p = new GpPointer { Field = field, At = at, Target = target, TargetAt = targetAt };
        _pointers.Add(p);
        _pointerIndex = null;
        Edited = true;
        return p;
    }

    /// <summary>Makes the word at (<paramref name="field"/>, <paramref name="at"/>) a plain zero word (a null pointer
    /// has no fix-up).</summary>
    public void ClearPointer(GpSection field, int at)
    {
        if (PointerAt(field, at) is { } p) { _pointers.Remove(p); _pointerIndex = null; }
        PutWord(field, at, 0);
        Edited = true;
    }

    /// <summary>
    /// Replaces <paramref name="oldLength"/> bytes at <paramref name="at"/> of <paramref name="section"/> with
    /// <paramref name="bytes"/>. Pointer fields inside the replaced bytes are dropped (the caller adds the ones the new
    /// bytes hold); fields and targets at or after the end of the replaced bytes move by the size change. A target at
    /// exactly <paramref name="at"/> stays when bytes are replaced (it names what now starts there) and moves when
    /// bytes are only inserted (it names what followed), except <paramref name="owner"/>'s, which stays. A target
    /// strictly inside the replaced bytes is a broken reference: the splice is refused with an exception.
    /// </summary>
    public void Splice(GpSection section, int at, int oldLength, ReadOnlySpan<byte> bytes, GpPointer? owner = null)
    {
        if (at < 0 || oldLength < 0 || at + oldLength > section.Data.Length) throw new ArgumentOutOfRangeException(nameof(at));
        int end = at + oldLength, delta = bytes.Length - oldLength;
        if (section.IsArray && (section.Data.Length + delta) % section.LengthWord != 0)
            throw new InvalidOperationException($"{section}: a splice of {delta} bytes breaks its {section.LengthWord}-byte elements");
        foreach (var p in _pointers)
            if (p.Target == section && p.TargetAt > at && p.TargetAt < end && !(p.Field == section && p.At >= at && p.At < end))
                throw new InvalidOperationException($"{section}: 0x{p.TargetAt:X} is pointed at from {p.Field} 0x{p.At:X} and would be cut");
        _pointers.RemoveAll(p => p.Field == section && p.At >= at && p.At < end && oldLength > 0);
        foreach (var p in _pointers)
        {
            if (p.Field == section && p.At >= end) p.At += delta;
            if (p.Target == section && p.TargetAt >= end && p != owner && !(oldLength > 0 && p.TargetAt == at)) p.TargetAt += delta;
        }
        var data = new byte[section.Data.Length + delta];
        section.Data.AsSpan(0, at).CopyTo(data);
        bytes.CopyTo(data.AsSpan(at));
        section.Data.AsSpan(end).CopyTo(data.AsSpan(at + bytes.Length));
        section.Data = data;
        if (section.IsArray)
            section.CountWord = (section.CountWord & 0xFF000000) | (uint)(data.Length / (int)section.LengthWord);
        _pointerIndex = null;
        Edited = true;
    }

    // ---- words ------------------------------------------------------------------------------------------------

    public static uint Word(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
    public static uint Word(GpSection s, int at) => BinaryPrimitives.ReadUInt32BigEndian(s.Data.AsSpan(at));
    public static ushort Half(GpSection s, int at) => BinaryPrimitives.ReadUInt16BigEndian(s.Data.AsSpan(at));
    public static float Float(GpSection s, int at) => BinaryPrimitives.ReadSingleBigEndian(s.Data.AsSpan(at));
    public void PutWord(GpSection s, int at, uint value) { BinaryPrimitives.WriteUInt32BigEndian(s.Data.AsSpan(at), value); Edited = true; }
    public void PutHalf(GpSection s, int at, ushort value) { BinaryPrimitives.WriteUInt16BigEndian(s.Data.AsSpan(at), value); Edited = true; }
    public void PutFloat(GpSection s, int at, float value) { BinaryPrimitives.WriteSingleBigEndian(s.Data.AsSpan(at), value); Edited = true; }
    private static void Put(byte[] file, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(at), value);
    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    /// <summary>The NUL-terminated string a pointer names (instance names in 0x11300).</summary>
    public static string String(GpPointer? p)
    {
        if (p == null) return string.Empty;
        var data = p.Target.Data.AsSpan(p.TargetAt);
        int end = data.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? data : data[..end]);
    }
}
