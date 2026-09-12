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
        // Reads sequentially from the current stream position - caller must already be
        // seeked to this record's start.
        TUID = sh.ReadUInt64();
        offset = sh.ReadUInt32();
        length = sh.ReadUInt32();
    }

    /// <summary>Reads `count` consecutive AssetPointer records starting at the stream's current position.</summary>
    public static AssetPointer[] ReadArray(StreamHelper sh, uint count)
    {
        var items = new AssetPointer[count];
        for (uint i = 0; i < count; i++)
            items[i] = new AssetPointer(sh);
        return items;
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
