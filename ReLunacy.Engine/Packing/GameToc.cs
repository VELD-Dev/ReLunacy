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
