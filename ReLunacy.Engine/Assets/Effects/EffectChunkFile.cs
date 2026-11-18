// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Buffers.Binary;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>
/// The IGHW 0.2 "flat chunk" container that effect.dat (and fxconduit_packed.dat) are written in.
///
/// Layout, PROVEN on every effect.dat of five games (QForce 15 levels, All 4 One 10, Into the Nexus 8,
/// A Crack in Time 22, Tools of Destruction's metropolis) - Docs/reverse/NEW_ENGINE_EFFECTS_2026-09-25.md:
/// <code>
///   +0x00  "IGHW"   +0x04  u16 0, u16 2 (version 0.2)   +0x08  u32 chunk count   +0x0C 0
///   +0x10  chunk directory: count x { u32 id, u32 offset, u32 byte length, u32 0 }
///          id 0xFFFFFFFF = anonymous chunk (a key array, an FX array...), others are named sections
///   ...    the chunks, back to back, each 16-aligned
///   align16(end of last chunk):  u32 n, then n x u32 = the file offset of every POINTER word
/// </code>
/// The tail is a relocation table: the game adds the load address to each listed word. Every listed
/// word holds a file offset that lands inside a chunk (0 of 28 200 outside on acid_refinery_pvp, 0 on
/// the other 54 files), and on all 55 files every chunk but the root header is the target of exactly
/// ONE pointer, so the file is a tree - which is what lets a reader type an anonymous chunk by the
/// field that points at it.
/// </summary>
public sealed class EffectChunkFile
{
    public const uint AnonymousId = 0xFFFFFFFF;

    public readonly record struct Chunk(uint Id, int Offset, int Length)
    {
        public int End => Offset + Length;
    }

    public byte[] Data { get; }
    public IReadOnlyList<Chunk> Chunks { get; }

    /// <summary>File offsets of every relocated (pointer) word, sorted.</summary>
    public IReadOnlyList<int> Relocations { get; }

    /// <summary>File offset of the relocation table's count word.</summary>
    public int RelocationTableOffset { get; }

    private readonly int[] _chunkOffsets;
    private readonly HashSet<int> _relocationSet;

    private EffectChunkFile(byte[] data, Chunk[] chunks, int[] relocations, int relocationTableOffset)
    {
        Data = data;
        Chunks = chunks;
        Relocations = relocations;
        RelocationTableOffset = relocationTableOffset;
        _chunkOffsets = chunks.Select(c => c.Offset).ToArray();
        _relocationSet = [.. relocations];
    }

    public static bool LooksLikeChunkFile(ReadOnlySpan<byte> data) =>
        data.Length >= 0x10 &&
        data[0] == (byte)'I' && data[1] == (byte)'G' && data[2] == (byte)'H' && data[3] == (byte)'W' &&
        BinaryPrimitives.ReadUInt32BigEndian(data[4..]) == 2;

    /// <summary>Parses the directory and the relocation tail. Throws on anything retail never ships.</summary>
    public static EffectChunkFile Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!LooksLikeChunkFile(data))
            throw new InvalidDataException("Not an IGHW 0.2 chunk file (effect.dat/fxconduit_packed.dat).");

        int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)));
        if (count <= 0 || 0x10L + 0x10L * count > data.Length)
            throw new InvalidDataException($"IGHW 0.2 chunk directory of {count} entries does not fit the file.");

        var chunks = new Chunk[count];
        int previousEnd = 0x10 + 0x10 * count;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = data.AsSpan(0x10 + 0x10 * i, 0x10);
            uint id = BinaryPrimitives.ReadUInt32BigEndian(entry);
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]));
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]));
            if (offset < previousEnd || (long)offset + length > data.Length)
                throw new InvalidDataException($"IGHW 0.2 chunk {i} (0x{offset:X}+0x{length:X}) is out of order or out of the file.");
            chunks[i] = new Chunk(id, offset, length);
            previousEnd = offset + length;
        }

        int tableOffset = (previousEnd + 0xF) & ~0xF;
        int[] relocations = [];
        if (tableOffset + 4 <= data.Length)
        {
            int relocCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(tableOffset)));
            if (relocCount < 0 || tableOffset + 4L + 4L * relocCount > data.Length)
                throw new InvalidDataException($"IGHW 0.2 relocation table at 0x{tableOffset:X} claims {relocCount} words past the end of the file.");
            relocations = new int[relocCount];
            for (int i = 0; i < relocCount; i++)
                relocations[i] = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(tableOffset + 4 + 4 * i)));
            Array.Sort(relocations);
        }

        var file = new EffectChunkFile(data, chunks, relocations, tableOffset);
        foreach (int reloc in relocations)
        {
            if (file.ChunkIndexAt(reloc) < 0)
                throw new InvalidDataException($"Relocation 0x{reloc:X} is not inside any chunk.");
            uint target = file.U32(reloc);
            // One-past-the-end is legal: fxconduit's empty chance-pair list at the end of an array points
            // exactly at the array chunk's end (1 such pointer on some All 4 One / Into the Nexus levels).
            if (target != 0 && file.ChunkIndexAt((int)target) < 0 && !file.IsChunkEnd((int)target))
                throw new InvalidDataException($"Pointer at 0x{reloc:X} targets 0x{target:X}, outside every chunk.");
        }
        return file;
    }

    /// <summary>Index of the chunk that contains <paramref name="offset"/>, or -1.</summary>
    public int ChunkIndexAt(int offset)
    {
        int i = Array.BinarySearch(_chunkOffsets, offset);
        if (i < 0) i = ~i - 1;
        if (i < 0) return -1;
        Chunk c = Chunks[i];
        return offset < c.Offset + Math.Max(c.Length, 1) ? i : -1;
    }

    public bool IsPointer(int offset) => _relocationSet.Contains(offset);

    /// <summary>True when <paramref name="offset"/> is exactly the end of some chunk.</summary>
    public bool IsChunkEnd(int offset)
    {
        int i = Array.BinarySearch(_chunkOffsets, offset);
        if (i < 0) i = ~i - 1;
        return i >= 0 && Chunks[i].End == offset;
    }

    public uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(offset));
    public ushort U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(offset));
    public short S16(int offset) => BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(offset));
    public float F32(int offset) => BinaryPrimitives.ReadSingleBigEndian(Data.AsSpan(offset));
    public byte U8(int offset) => Data[offset];

    /// <summary>The target of the pointer word at <paramref name="offset"/>, or 0 when the word is null
    /// or not listed in the relocation table (a word the game never relocates is not a pointer).</summary>
    public int Pointer(int offset) => IsPointer(offset) ? (int)U32(offset) : 0;

    /// <summary>Every chunk with the given id, in file order.</summary>
    public IEnumerable<Chunk> ChunksWithId(uint id) => Chunks.Where(c => c.Id == id);

    /// <summary>
    /// How many pointers target each chunk. The effect file is a tree: every chunk except the 0x100
    /// header has exactly one. <see cref="EffectLibrary.Validation"/> reports any file that is not.
    /// </summary>
    public int[] InboundPointerCounts()
    {
        var counts = new int[Chunks.Count];
        foreach (int reloc in Relocations)
        {
            uint target = U32(reloc);
            if (target == 0) continue;
            int index = ChunkIndexAt((int)target);
            if (index >= 0) counts[index]++;
        }
        return counts;
    }
}
