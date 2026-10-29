using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// Rewrites a PSARC with some files' contents replaced, copying every other file's data through
/// untouched.
///
/// This exists rather than using LibreFios's builder because the job is narrow and the builder's
/// shape fights it: that one decompresses every file into memory and compresses it again, which
/// means a rebuilt archive's bytes depend on our compressor agreeing with whatever produced the
/// original. Here an untouched file's compressed blocks are copied verbatim, so they are identical
/// by construction instead of by inspection - no decompression, no recompression, and nothing for
/// the console's decoder to accept that it did not already accept when the game shipped.
///
/// The two files that DO change are written as stored blocks. The format says a block-size entry of
/// zero means a full uncompressed block, and the shipped archive uses that 1117 times out of 6462,
/// so it is a path the engine demonstrably takes. That removes the only remaining question - whether
/// the console accepts deflate streams we produced - at a cost of a few kilobytes in a 222MB file.
///
/// Layout, all big-endian, as confirmed against six archives from two games:
///   0x00  magic 'PSAR', version, 4-byte compression tag
///   0x0C  FAT size, which is ALSO the offset where file data begins - it spans the header, the
///         entry array and the block-size table
///   0x18  block size, archive flags
///   0x20  <see cref="EntrySize"/> bytes per entry: 16-byte path hash, 4-byte first block index,
///         5-byte uncompressed size, 5-byte offset
///   then  the block-size table, one entry per block, width following the block size
/// Entry 0 is the archive's own path manifest; manifest line i names entry i+1, which is why
/// replacing a file must never renumber the entries.
/// </summary>
public static class PsarcPatcher
{
    private const int HeaderSize = 0x20;
    private const int EntrySize = 30;
    private const uint CaseInsensitivePaths = 1;

    private sealed record Entry(byte[] Hash, int FirstBlock, long Size, long Offset);

    /// <summary>
    /// Writes <paramref name="outputPath"/> as <paramref name="archivePath"/> with the named paths'
    /// contents replaced. Paths are the archive's own, e.g. <c>/scripts/tags.lua</c>.
    /// </summary>
    /// <returns>How many entries were replaced.</returns>
    public static int Patch(string archivePath, string outputPath,
                            IReadOnlyDictionary<string, byte[]> replacements)
    {
        using var source = File.OpenRead(archivePath);

        var header = new byte[HeaderSize];
        source.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32BigEndian(header) != 0x50534152u)
            throw new InvalidDataException($"{Path.GetFileName(archivePath)} is not a PSARC");

        int fatSize = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0x0C));
        int entrySize = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0x10));
        int count = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0x14));
        int blockSize = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0x18));
        uint flags = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x1C));

        if (entrySize != EntrySize)
            throw new InvalidDataException($"unexpected entry size {entrySize}");

        var entries = ReadEntries(source, count, entrySize);
        int[] blocks = ReadBlockTable(source, fatSize, count, entrySize, blockSize);

        // Which entry each replacement belongs to, found by hashing its path the way the archive
        // does. The hash is checked rather than the manifest's ordering so a mismatch is an error
        // instead of a silently wrong file.
        var wanted = new Dictionary<int, byte[]>();
        foreach (var (path, contents) in replacements)
        {
            byte[] hash = HashPath(path, flags);
            int index = Array.FindIndex(entries, e => e.Hash.AsSpan().SequenceEqual(hash));
            if (index < 0) throw new InvalidDataException($"{path} is not in the archive");
            wanted[index] = contents;
        }

        // Pass one: lay out the new blocks and entries, so the table's size - and with it where data
        // begins - is known before anything is written.
        var newEntries = new List<Entry>(count);
        var newBlocks = new List<int>(blocks.Length);
        foreach (var (entry, index) in entries.Select((e, i) => (e, i)))
        {
            int firstBlock = newBlocks.Count;
            long size;
            if (wanted.TryGetValue(index, out byte[]? contents))
            {
                size = contents.Length;
                AppendStoredBlockSizes(newBlocks, contents.Length, blockSize);
            }
            else
            {
                size = entry.Size;
                foreach (int length in BlockLengths(entry, blocks, blockSize)) newBlocks.Add(length);
            }
            newEntries.Add(new Entry(entry.Hash, firstBlock, size, 0));
        }

        int blockWidth = BlockWidth(blockSize);
        long dataStart = HeaderSize + (long)count * entrySize + (long)newBlocks.Count * blockWidth;

        // Pass two: write the data, recording where each entry landed.
        using var output = File.Create(outputPath);
        output.Position = dataStart;
        for (int i = 0; i < newEntries.Count; i++)
        {
            long offset = output.Position;
            if (wanted.TryGetValue(i, out byte[]? contents)) output.Write(contents);
            else CopyBlocks(source, output, entries[i], blocks, blockSize);
            newEntries[i] = newEntries[i] with { Offset = offset };
        }

        // Pass three: the header and tables, now that every offset is known.
        output.Position = 0;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0x0C), checked((int)dataStart));
        output.Write(header);
        foreach (var entry in newEntries) output.Write(EncodeEntry(entry));
        var blockBytes = new byte[blockWidth];
        foreach (int length in newBlocks)
        {
            WriteBigEndian(blockBytes, length);
            output.Write(blockBytes);
        }

        if (output.Position != dataStart)
            throw new InvalidDataException(
                $"the tables ended at {output.Position} but data begins at {dataStart}");

        return wanted.Count;
    }

    private static Entry[] ReadEntries(Stream source, int count, int entrySize)
    {
        var entries = new Entry[count];
        var raw = new byte[entrySize];
        for (int i = 0; i < count; i++)
        {
            source.ReadExactly(raw);
            entries[i] = new Entry(
                raw[..16],
                BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(16)),
                ReadBig(raw.AsSpan(20, 5)),
                ReadBig(raw.AsSpan(25, 5)));
        }
        return entries;
    }

    private static int[] ReadBlockTable(Stream source, int fatSize, int count, int entrySize, int blockSize)
    {
        int width = BlockWidth(blockSize);
        int bytes = fatSize - HeaderSize - count * entrySize;
        if (bytes < 0) throw new InvalidDataException($"FAT size {fatSize} is too small for {count} entries");

        var raw = new byte[bytes];
        source.ReadExactly(raw);
        var table = new int[bytes / width];
        for (int i = 0; i < table.Length; i++)
            table[i] = (int)ReadBig(raw.AsSpan(i * width, width));
        return table;
    }

    /// <summary>
    /// The on-disk length of each of an entry's blocks. A table value of zero means a whole
    /// uncompressed block, so its length is the block size; anything else is the length as written,
    /// compressed or not.
    /// </summary>
    private static IEnumerable<int> BlockLengths(Entry entry, int[] blocks, int blockSize)
    {
        long produced = 0;
        for (int i = entry.FirstBlock; produced < entry.Size && i < blocks.Length; i++)
        {
            int length = blocks[i];
            yield return length;
            // A stored block contributes its whole block size; a compressed one contributes however
            // much it decompresses to, which is all that is left when it is the last.
            produced += length == 0 ? blockSize : Math.Min(blockSize, entry.Size - produced);
        }
    }

    /// <summary>
    /// Block-size entries for content written uncompressed: zero for each whole block, then the
    /// remainder's real length for a short final one - which a reader recognises as stored because
    /// it equals the bytes still owed.
    /// </summary>
    private static void AppendStoredBlockSizes(List<int> blocks, int length, int blockSize)
    {
        int remaining = length;
        while (remaining >= blockSize)
        {
            blocks.Add(0);
            remaining -= blockSize;
        }
        if (remaining > 0 || length == 0) blocks.Add(remaining);
    }

    private static void CopyBlocks(Stream source, Stream output, Entry entry, int[] blocks, int blockSize)
    {
        source.Position = entry.Offset;
        var buffer = new byte[blockSize];
        foreach (int length in BlockLengths(entry, blocks, blockSize))
        {
            int onDisk = length == 0 ? blockSize : length;
            source.ReadExactly(buffer, 0, onDisk);
            output.Write(buffer, 0, onDisk);
        }
    }

    /// <summary>
    /// The archive's own hash of a path: MD5 of its bytes, upper-cased first when the archive
    /// declares case-insensitive paths - which the shipped files do, and which is why hashing the
    /// path as written finds nothing.
    /// </summary>
    private static byte[] HashPath(string path, uint flags)
    {
        string normalised = path.Replace('\\', '/');
        if ((flags & CaseInsensitivePaths) != 0) normalised = normalised.ToUpperInvariant();
        return MD5.HashData(System.Text.Encoding.ASCII.GetBytes(normalised));
    }

    private static byte[] EncodeEntry(Entry entry)
    {
        var raw = new byte[EntrySize];
        entry.Hash.CopyTo(raw, 0);
        BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(16), entry.FirstBlock);
        WriteBig(raw.AsSpan(20, 5), entry.Size);
        WriteBig(raw.AsSpan(25, 5), entry.Offset);
        return raw;
    }

    private static int BlockWidth(int blockSize) =>
        blockSize <= 0x10000 ? 2 : blockSize <= 0x1000000 ? 3 : 4;

    private static long ReadBig(ReadOnlySpan<byte> bytes)
    {
        long value = 0;
        foreach (byte b in bytes) value = (value << 8) | b;
        return value;
    }

    private static void WriteBig(Span<byte> bytes, long value)
    {
        for (int i = bytes.Length - 1; i >= 0; i--)
        {
            bytes[i] = (byte)(value & 0xFF);
            value >>= 8;
        }
    }

    private static void WriteBigEndian(byte[] bytes, int value) => WriteBig(bytes, value);
}
