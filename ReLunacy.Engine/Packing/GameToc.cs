using System.Buffers.Binary;
using System.Text;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// The game's own file index - a <c>.toc</c> - parsed into a form that can be written back out.
///
/// The format is documented in docs/reverse/tod_toc_format.md. In short: the last word points at an
/// index of (record offset, name offset) pairs, one per entry, and that index is the only place a
/// directory's name is held. Records and name strings are INTERLEAVED in document order, so there is
/// no single record table to walk - everything is reached through the index.
///
/// The model here is deliberately structural rather than a byte copy, because the point of it is to
/// prove the layout is understood: the region before the index is decomposed into spans at every
/// offset the index references, and writing re-derives every offset from the spans' order and
/// lengths. A file that survives that unchanged is one whose layout was read correctly. A byte copy
/// would prove nothing and could not be edited.
/// </summary>
public sealed class GameToc
{
    /// <summary>
    /// A stretch of the pre-index region. Span boundaries are exactly the offsets the index
    /// references, so every reference lands on a span's first byte and nothing needs to point into
    /// the middle of one.
    /// </summary>
    public sealed class Span
    {
        public required byte[] Bytes { get; init; }

        /// <summary>Where this span used to start. Only kept for diagnostics.</summary>
        public required int OriginalOffset { get; init; }

        /// <summary>Set when this span is a record whose first word held its entry's name offset,
        /// which therefore has to be rewritten if anything moves. Null when the word was zero.</summary>
        public int? NameSpanForWord0 { get; set; }
    }

    /// <summary>One index entry, as span indices rather than offsets.</summary>
    public readonly record struct Entry(int RecordSpan, int NameSpan);

    public List<Span> Spans { get; } = [];

    /// <summary>The index, in its original order - which is not the layout order and not sorted.</summary>
    public List<Entry> Index { get; } = [];

    /// <summary>The name a span holds, up to its NUL. Empty when the span is not a string.</summary>
    public string NameOf(int span)
    {
        var bytes = Spans[span].Bytes;
        int end = Array.IndexOf(bytes, (byte)0);
        if (end < 0) end = bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, end);
    }

    /// <summary>
    /// An entry's size, or null when its record is too short to carry one.
    ///
    /// A 16-byte record's second word is the file's uncompressed size; a 4-byte record is a
    /// directory and has none. The kind follows from the record's extent, not from any field in it -
    /// reading the first word as a "has a name" flag misclassifies thousands of real files.
    /// </summary>
    public long? SizeOf(int entry)
    {
        var bytes = Spans[Index[entry].RecordSpan].Bytes;
        return bytes.Length >= 8 ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)) : null;
    }

    /// <summary>The entry naming this path, or -1. Paths are compared case-insensitively, as the
    /// archives' own hashing does.</summary>
    public int Find(string path)
    {
        for (int i = 0; i < Index.Count; i++)
            if (string.Equals(NameOf(Index[i].NameSpan), path, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// Changes an entry's recorded size. Only valid on a record long enough to hold one - a
    /// directory's 4 bytes have nowhere to put it, and writing there would land in the next record.
    /// </summary>
    public void SetSize(int entry, long size)
    {
        if (size < 0 || size > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(size));
        var bytes = Spans[Index[entry].RecordSpan].Bytes;
        if (bytes.Length < 8)
            throw new InvalidOperationException($"entry {entry} has no size field to set");
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)size);
    }

    public static GameToc Read(byte[] data)
    {
        if (data.Length < 8) throw new InvalidDataException("too short to be a .toc");

        int indexOffset = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(data.Length - 4));
        if (indexOffset < 0 || indexOffset > data.Length - 4)
            throw new InvalidDataException($"index offset {indexOffset} is outside the file");
        if ((data.Length - 4 - indexOffset) % 8 != 0)
            throw new InvalidDataException("the index is not a whole number of 8-byte entries");

        int count = (data.Length - 4 - indexOffset) / 8;
        var recordOffsets = new int[count];
        var nameOffsets = new int[count];
        for (int i = 0; i < count; i++)
        {
            recordOffsets[i] = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(indexOffset + i * 8));
            nameOffsets[i] = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(indexOffset + i * 8 + 4));
            foreach (int offset in (int[])[recordOffsets[i], nameOffsets[i]])
                if (offset < 0 || offset >= indexOffset)
                    throw new InvalidDataException($"entry {i} points at {offset}, outside the region");
        }

        // Every referenced offset becomes a span boundary, so no reference ever lands mid-span. The
        // same offset can be referenced as both a record and a name - debug.toc does exactly that -
        // which is why these are merged rather than kept as two sets.
        var boundaries = new SortedSet<int>(recordOffsets) { 0 };
        boundaries.UnionWith(nameOffsets);

        var toc = new GameToc();
        var spanAt = new Dictionary<int, int>();
        var starts = boundaries.ToArray();
        for (int i = 0; i < starts.Length; i++)
        {
            int start = starts[i];
            int end = i + 1 < starts.Length ? starts[i + 1] : indexOffset;
            spanAt[start] = toc.Spans.Count;
            toc.Spans.Add(new Span { Bytes = data[start..end], OriginalOffset = start });
        }

        for (int i = 0; i < count; i++)
        {
            int record = spanAt[recordOffsets[i]];
            int name = spanAt[nameOffsets[i]];
            toc.Index.Add(new Entry(record, name));

            // A record whose first word is its own name offset is holding a pointer that has to move
            // with the name. Where it is zero there is nothing to maintain.
            var bytes = toc.Spans[record].Bytes;
            if (bytes.Length >= 4 && BinaryPrimitives.ReadInt32BigEndian(bytes) == nameOffsets[i])
                toc.Spans[record].NameSpanForWord0 = name;
        }

        return toc;
    }

    /// <summary>
    /// Appends a file entry: a name, a 16-byte record carrying its uncompressed size, and an index
    /// entry for the two.
    ///
    /// Appending rather than inserting is deliberate. The index is in ascending record-offset order
    /// in all 64 shipped ToD TOCs, so putting the new record after every existing one and its index
    /// entry at the end is the only placement that keeps that invariant without moving anything
    /// else - no existing offset changes at all.
    ///
    /// What this CANNOT establish is whether the engine accepts an entry added this way. The
    /// ordering invariant is evidence, not proof: if the big TOCs' directory entries imply a
    /// traversal that expects a directory's files to sit together, a file appended at the end would
    /// satisfy every structural check here and still not be found. That needs the game to say so.
    /// </summary>
    /// <returns>The index of the entry that was added.</returns>
    public int AddFile(string path, long size)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("a path is required", nameof(path));
        if (size < 0 || size > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(size));

        // Names are NUL-terminated and 4-aligned.
        byte[] ascii = Encoding.ASCII.GetBytes(path);
        var name = new byte[(ascii.Length + 4) & ~3];
        ascii.CopyTo(name, 0);

        var record = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(4), (uint)size);

        Spans.Add(new Span { Bytes = name, OriginalOffset = -1 });
        int nameSpan = Spans.Count - 1;
        Spans.Add(new Span { Bytes = record, OriginalOffset = -1 });
        int recordSpan = Spans.Count - 1;

        // Match whatever this file does with a record's first word: the simple TOCs put the entry's
        // own name offset there, the big ones leave it zero. Nothing is known to read it, so the
        // conservative choice is to look like the neighbours rather than to pick.
        if (MostRecordsCarryTheirNameOffset()) Spans[recordSpan].NameSpanForWord0 = nameSpan;

        Index.Add(new Entry(recordSpan, nameSpan));
        return Index.Count - 1;
    }

    private bool MostRecordsCarryTheirNameOffset()
    {
        int carrying = 0, total = 0;
        foreach (var entry in Index)
        {
            if (Spans[entry.RecordSpan].Bytes.Length < 16) continue;
            total++;
            if (Spans[entry.RecordSpan].NameSpanForWord0 is not null) carrying++;
        }
        return total > 0 && carrying * 2 > total;
    }

    /// <summary>
    /// Writes the file back out, re-deriving every offset from the spans' order and lengths rather
    /// than reusing the ones it was read with. That is what makes an unchanged round trip meaningful.
    /// </summary>
    public byte[] Write()
    {
        var offsets = new int[Spans.Count];
        int at = 0;
        for (int i = 0; i < Spans.Count; i++)
        {
            offsets[i] = at;
            at += Spans[i].Bytes.Length;
        }
        int indexOffset = at;

        var output = new byte[indexOffset + Index.Count * 8 + 4];
        foreach (var (span, i) in Spans.Select((s, i) => (s, i)))
        {
            span.Bytes.CopyTo(output, offsets[i]);
            if (span.NameSpanForWord0 is { } name)
                BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(offsets[i]), offsets[name]);
        }

        for (int i = 0; i < Index.Count; i++)
        {
            var entry = Index[i];
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(indexOffset + i * 8), offsets[entry.RecordSpan]);
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(indexOffset + i * 8 + 4), offsets[entry.NameSpan]);
        }
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(output.Length - 4), indexOffset);
        return output;
    }
}
