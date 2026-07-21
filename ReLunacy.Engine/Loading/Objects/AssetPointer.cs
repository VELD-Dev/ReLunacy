using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

public record struct AssetPointer : ILunaObject, ILunaSerializable
{
    public const uint Size = 0x10;

    public ulong TUID { get; init; }
    public uint offset;
    public uint length;

    public AssetPointer(StreamHelper sh)
    {
        // sh.ReadUInt64(0x00) looks like "read at absolute offset 0" but isn't: the literal 0
        // implicitly converts to StreamHelper.Endianness (whose first member is 0), so that call
        // actually resolved to ReadUInt64(Endianness.Little) — wrong byte order (this format is
        // big-endian throughout) and no seek at all. And offset/length's literal offsets (0x08,
        // 0x0C) are absolute from the stream's start, not relative to this record, so every
        // AssetPointer past the first in an array read from the same fixed two bytes regardless of
        // which record it actually was. Read sequentially instead, relying on the caller having
        // already sought to this record's start (same as every other AssetPointer array read here).
        TUID = sh.ReadUInt64();
        offset = sh.ReadUInt32();
        length = sh.ReadUInt32();
    }

    /// <summary>Reads `count` consecutive AssetPointer records starting at the stream's current position — the array-reading counterpart to the single-record constructor above, since AssetPointer's hand-written constructor isn't compatible with FileUtils.ReadStructureArray's [FileStructure]/[FileOffset] reflection.</summary>
    public static AssetPointer[] ReadArray(StreamHelper sh, uint count)
    {
        var items = new AssetPointer[count];
        for (uint i = 0; i < count; i++)
            items[i] = new AssetPointer(sh);
        return items;
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
