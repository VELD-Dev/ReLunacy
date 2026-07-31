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
    /// <summary>Despite the name, this is the vertex block's END offset, not its size — both
    /// relative to section 0x9000's start, same basis as <see cref="verticesBufferStart"/>.
    /// Measured on metropolis: 0x18-0x14 is exactly vertexCount*20 for 186 of 193 ties (and is
    /// divisible by 20 for all 193), while 0x18 alone equals vertexCount*20 for 0 of 193. So the
    /// tie's vertex count is (0x18-0x14)/20, and 0x18 is where its vertex data stops.
    /// Tie.cs reads this as a length and pulls that many bytes from verticesBufferStart, which
    /// over-reads by verticesBufferStart bytes. Harmless — meshes index in by vertex, so nothing
    /// downstream sees the surplus — but it is not what the field means.
    /// This offset also matters for baked lighting: for 61 of the 193 ties the lightmap UV array
    /// begins exactly here, which is what Tie.TryReadLightmapUVs reads. See
    /// Loading.Vertices.TieLightmapUV for the proof, and for the searched-and-not-found result on
    /// the other 132.</summary>
    [FileOffset(0x18)] public uint verticesBufferSize;
    /// <summary>Not an offset — a FLOAT (observed 4992.0, 504.0, 752.0 on metropolis; read as a
    /// uint these are 0x459C0000 / 0x43FC0000 / 0x443C0000). Insomniac's own post-mortem for this
    /// game (dev/Ratchet_and_Clank_WWS_Debrief_Feb_08.pdf, "Shader Usage Controls") lists per-use
    /// knobs that are exactly this shape: transition distance for discrete LOD, for fade-out LOD,
    /// for shader LOD, and the detail-map fade distance. A world-space distance in the hundreds to
    /// low thousands fits any of them. CANDIDATE ONLY — nothing has tied this value to observed LOD
    /// behaviour yet, and there are four knobs it could be.</summary>
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
