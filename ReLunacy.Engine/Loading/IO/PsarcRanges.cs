using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ReLunacy.Engine.Loading.IO;

/// <summary>
/// Reads part of a file stored in a PSARC (v1, zlib) without inflating the rest of it: a level's sound files are
/// tens to hundreds of megabytes, and the sound list needs a few kilobytes at each end, a sample a few more. Opens
/// the archive for the duration of one call, so it never keeps a save from replacing the file.
/// </summary>
public static class PsarcRanges
{
    private sealed record Entry(int FirstBlock, long Size, long Offset);

    private sealed record Toc(int BlockSize, int[] Blocks, Dictionary<string, Entry> Paths);

    // The table of contents of an archive, by path and last write time: parsed once.
    private static readonly Dictionary<string, (DateTime Stamp, Toc Toc)> Tocs = [];

    /// <summary>The size of <paramref name="path"/> (a full path or a file name) in the archive, or -1.</summary>
    public static long Size(string archivePath, string path)
    {
        using var file = File.OpenRead(archivePath);
        return Find(ReadToc(archivePath, file), path) is { } entry ? entry.Size : -1;
    }

    /// <summary><paramref name="length"/> bytes of <paramref name="path"/> from <paramref name="start"/>, cut short at
    /// its end; null when the archive does not have it.</summary>
    public static byte[]? Read(string archivePath, string path, long start, int length)
    {
        using var file = File.OpenRead(archivePath);
        var toc = ReadToc(archivePath, file);
        if (Find(toc, path) is not { } entry || start < 0 || start > entry.Size) return null;
        length = (int)Math.Min(length, entry.Size - start);
        if (length <= 0) return [];
        return ReadRange(file, toc, entry, start, length);
    }

    private static Entry? Find(Toc toc, string path)
    {
        if (toc.Paths.TryGetValue(path, out var exact)) return exact;
        foreach (var (name, entry) in toc.Paths)
            if (name.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)) return entry;
        return null;
    }

    private static byte[] ReadRange(FileStream file, Toc toc, Entry entry, long start, int length)
    {
        int bs = toc.BlockSize;
        long first = start / bs, last = (start + length - 1) / bs;
        long position = entry.Offset;
        for (long b = 0; b < first; b++) position += Stored(toc, entry.FirstBlock + (int)b);
        var output = new byte[length];
        var raw = new byte[bs];
        var block = new byte[bs];
        int written = 0;
        file.Position = position;
        for (long b = first; b <= last; b++)
        {
            int stored = Stored(toc, entry.FirstBlock + (int)b);
            long blockStart = b * bs;
            int plain = (int)Math.Min(bs, entry.Size - blockStart);
            file.ReadExactly(raw, 0, stored);
            ReadOnlySpan<byte> data;
            if (stored == plain || raw[0] != 0x78) data = raw.AsSpan(0, Math.Min(stored, plain));
            else
            {
                using var z = new ZLibStream(new MemoryStream(raw, 0, stored), CompressionMode.Decompress);
                z.ReadExactly(block, 0, plain);
                data = block.AsSpan(0, plain);
            }
            int from = b == first ? (int)(start - blockStart) : 0;
            int take = Math.Min(data.Length - from, length - written);
            data.Slice(from, take).CopyTo(output.AsSpan(written));
            written += take;
        }
        return output;
    }

    // A block-size entry of 0 is a full, stored block.
    private static int Stored(Toc toc, int index) => toc.Blocks[index] == 0 ? toc.BlockSize : toc.Blocks[index];

    private static Toc ReadToc(string archivePath, FileStream file)
    {
        string key = Path.GetFullPath(archivePath);
        DateTime stamp = File.GetLastWriteTimeUtc(key);
        lock (Tocs)
            if (Tocs.TryGetValue(key, out var cached) && cached.Stamp == stamp) return cached.Toc;

        Span<byte> header = stackalloc byte[0x20];
        file.Position = 0;
        file.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32BigEndian(header) != 0x50534152) throw new InvalidDataException("Not a PSARC file");
        int tocLength = BinaryPrimitives.ReadInt32BigEndian(header[0x0C..]);
        int entrySize = BinaryPrimitives.ReadInt32BigEndian(header[0x10..]);
        int count = BinaryPrimitives.ReadInt32BigEndian(header[0x14..]);
        int blockSize = BinaryPrimitives.ReadInt32BigEndian(header[0x18..]);
        bool caseless = (BinaryPrimitives.ReadUInt32BigEndian(header[0x1C..]) & 1) != 0;
        var table = new byte[tocLength - 0x20];
        file.ReadExactly(table);

        var entries = new (byte[] Hash, Entry Entry)[count];
        for (int i = 0; i < count; i++)
        {
            var e = table.AsSpan(i * entrySize, entrySize);
            entries[i] = (e[..16].ToArray(), new Entry(BinaryPrimitives.ReadInt32BigEndian(e[16..]), ReadUInt40(e[20..]), ReadUInt40(e[25..])));
        }
        int width = blockSize <= 0x10000 ? 2 : blockSize <= 0x1000000 ? 3 : 4;
        var sizes = table.AsSpan(count * entrySize);
        var blocks = new int[sizes.Length / width];
        for (int i = 0; i < blocks.Length; i++)
        {
            int v = 0;
            for (int k = 0; k < width; k++) v = (v << 8) | sizes[i * width + k];
            blocks[i] = v;
        }

        var toc = new Toc(blockSize, blocks, new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase));
        // Entry 0 is the manifest; the others are matched to its paths by the MD5 of the path.
        var manifest = ReadRange(file, toc, entries[0].Entry, 0, (int)entries[0].Entry.Size);
        var byHash = new Dictionary<string, Entry>();
        for (int i = 1; i < count; i++) byHash.TryAdd(Convert.ToHexString(entries[i].Hash), entries[i].Entry);
        foreach (string line in Encoding.ASCII.GetString(manifest).Split(['\n', '\0'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string path = line.Replace('\\', '/');
            if (byHash.TryGetValue(Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(caseless ? line.ToUpperInvariant() : line))), out var found)
                || byHash.TryGetValue(Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(line))), out found))
                toc.Paths[path] = found;
        }
        lock (Tocs) Tocs[key] = (stamp, toc);
        return toc;
    }

    private static long ReadUInt40(ReadOnlySpan<byte> b) =>
        ((long)b[0] << 32) | ((long)b[1] << 24) | ((long)b[2] << 16) | ((long)b[3] << 8) | b[4];
}
