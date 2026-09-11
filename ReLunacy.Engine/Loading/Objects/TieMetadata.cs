using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Meshes;

namespace ReLunacy.Engine.Loading.Objects;

[FileStructure(0x80)]
public record struct TieMetadataOld : ILunaObject, ILunaSerializable
{
    public const uint PointerID = 0x1D300;
    public const uint ID = 0x3400;
    public const uint Size = 0x80;

    [FileOffset(0x00), Reference(nameof(MeshesCount))] public TieMesh[] meshes;
    [FileOffset(0x0F)] public byte meshesCount;
    [FileOffset(0x10)] public uint Unk2;
    [FileOffset(0x14)] public uint verticesBufferStart;
    /// <summary>Despite the name, this is the vertex block's end offset, not its size - relative to
    /// section 0x9000's start, same basis as <see cref="verticesBufferStart"/>. Vertex count is
    /// (verticesBufferSize - verticesBufferStart) / 20. Also doubles as the start offset of the
    /// per-tie lightmap UV array, when present.</summary>
    [FileOffset(0x18)] public uint verticesBufferSize;
    /// <summary>Not an offset - a float. Likely an LOD/shader transition distance (unconfirmed).</summary>
    [FileOffset(0x1C)] public uint Unk3;
    [FileOffset(0x20)] public Vector3 scale;
    [FileOffset(0x64)] public uint nameOffset;

    public readonly uint MeshesCount => meshesCount;

    private ulong _tuidOverride;
    public ulong TUID { readonly get => _tuidOverride; init => _tuidOverride = value; }

    public static TieMetadataOld Read(StreamHelper sh, uint index)
    {
        var metadata = FileUtils.ReadStructure<TieMetadataOld>(sh);
        metadata._tuidOverride = index;
        return metadata;
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}

[FileStructure(0x80)]
public record struct TieMetadataNew : ILunaObject, ILunaSerializable
{
    public const uint PointerID = 0x1D300;
    public const uint ID = 0x3400;
    public const uint Size = 0x80;

    [FileOffset(0x00), Reference(nameof(MeshesCount))] public TieMesh[] meshes;
    [FileOffset(0x0F)] public byte meshesCount;
    [FileOffset(0x10)] public uint Unk2;
    [FileOffset(0x14)] public uint verticesBufferStart;
    [FileOffset(0x18)] public uint verticesBufferSize;
    [FileOffset(0x1C)] public uint Unk3;
    [FileOffset(0x20)] public Vector3 scale;
    [FileOffset(0x64)] public uint nameOffset;

    public readonly uint MeshesCount => meshesCount;
    public ulong TUID { get => _tuid; init => _tuid = value; }
    [FileOffset(0x68)] private ulong _tuid;

    public static TieMetadataNew Read(StreamHelper sh) => FileUtils.ReadStructure<TieMetadataNew>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
