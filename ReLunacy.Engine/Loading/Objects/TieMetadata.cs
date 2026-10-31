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

    // FILE-PROVEN on the retail ToD corpus: +00 points at the first 0x3300 Primitive,
    // +04 points at the parallel first 0x3F00 PrimitiveBounds record, and +0C is the
    // shared record count. Both arrays use the same ordinal and partition their backing
    // sections in lockstep. See TOD_TIE_3400_3F00_BOUNDS_AUTHORING_2026-09-13.md.
    [FileOffset(0x00), Reference(nameof(PrimitiveCount))] public TieMesh[] meshes;
    [FileOffset(0x04), Reference(nameof(PrimitiveCount))] public TiePrimitiveBounds[] primitiveBounds;
    [FileOffset(0x08)] public uint Reserved08;
    [FileOffset(0x0C)] public uint primitiveCount;

    // Compatibility alias for older editor code. Because 0x3400 is big-endian and retail
    // primitive counts are small, the low byte of +0C is physically stored at +0F.
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

    // CORPUS + decoded-geometry proof. +30 is the whole-prototype sphere. +40/+50 are the
    // aggregate AABB half-extents/centre reconstructed from the per-primitive 0x3F00 boxes.
    // The W/padding words at +4C/+5C are serialized as zero on all 1,062 audited definitions.
    [FileOffset(0x30)] public Vector4 boundingSphere;
    [FileOffset(0x40)] public Vector3 boundsHalfExtents;
    [FileOffset(0x4C)] public float BoundsReserved4C;
    [FileOffset(0x50)] public Vector3 boundsCenter;
    [FileOffset(0x5C)] public float BoundsReserved5C;

    [FileOffset(0x64)] public uint nameOffset;

    public readonly uint PrimitiveCount => primitiveCount;
    public readonly uint MeshesCount => primitiveCount;

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

/// <summary>
/// New-engine Tie definition record (section 0x3400 of the tie's own IGHW container in ties.dat).
///
/// The record has TWO SIZES: 0x80 on A Crack in Time, 0x90 on QForce and Into the Nexus. Every
/// field below 0x80 is at the same offset in both, and the extra 0x10 bytes are zero on 2 360 of
/// the 2 378 wide records. Field map measured on 4 828 retail tie resources over the three games
/// (Docs/reverse/NEW_ENGINE_SHADER_TIE_COLLISION_2026-09-19.md); the layout is the old engine's
/// with a TUID bolted on, which is why <see cref="TieMetadataOld"/> reads almost the same offsets.
/// </summary>
[FileStructure(0x80)]
public record struct TieMetadataNew : ILunaObject, ILunaSerializable
{
    public const uint PointerID = 0x1D300;
    public const uint ID = 0x3400;

    /// <summary>A Crack in Time record size.</summary>
    public const uint Size = 0x80;

    /// <summary>QForce / Into the Nexus record size. The extra words live at +0x80..+0x8F.</summary>
    public const uint WideSize = 0x90;

    /// <summary>+0x00. Points at the first 0x3300 primitive: 4828/4828 resources.</summary>
    [FileOffset(0x00), Reference(nameof(MeshesCount))] public TieMesh[] meshes;

    /// <summary>+0x04. Points at the parallel first 0x3F00 bounds record: 4828/4828.</summary>
    [FileOffset(0x04)] public uint primitiveBoundsPointer;

    /// <summary>+0x08. Zero on 4828/4828.</summary>
    [FileOffset(0x08)] public uint Reserved08;

    /// <summary>+0x0C/+0x0D. Zero on 4828/4828 resources.</summary>
    [FileOffset(0x0C)] public ushort Reserved0C;

    /// <summary>+0x0E. Flag byte: 0 on 4718 resources, 4 on 81, 2 on 28, 6 on 1. OPEN.
    /// The old engine has no flag here, which is why <see cref="TieMetadataOld"/> can read the whole
    /// word at +0x0C as a count and this record cannot.</summary>
    [FileOffset(0x0E)] public byte primitiveFlags;

    /// <summary>+0x0F. Primitive count; equals the 0x3300 element count on 4828/4828, and the
    /// largest count in the retail corpus is 64, so one byte is the real width.</summary>
    [FileOffset(0x0F)] public byte meshesCount;

    /// <summary>+0x10 / +0x14. Zero on 4828/4828 - the new engine gives each tie its own 0x3000
    /// vertex section, so there is no offset into a shared arena the way the old engine has.</summary>
    [FileOffset(0x10)] public uint Reserved10;

    /// <inheritdoc cref="Reserved10"/>
    [FileOffset(0x14)] public uint verticesBufferStart;

    /// <summary>
    /// +0x18. Byte length of the POSITION/UV vertex block at 20 bytes per vertex: equals
    /// max(primitive.verticesIndex + verticesCount) * 0x14 on 4735/4828 resources. Section 0x3000
    /// is longer than this, and the surplus is exactly the baked lightmap UV array that
    /// <see cref="Tie.LightmapUVs"/> reads.
    /// </summary>
    [FileOffset(0x18)] public uint verticesBufferSize;

    /// <summary>+0x1C. A Crack in Time stores a distance here (4096.0f on 2340 of 2450, else
    /// 10000.0f/5000.0f); QForce and Into the Nexus leave it zero.</summary>
    [FileOffset(0x1C)] public uint Unk3;

    /// <summary>+0x20. Quantisation scale for the packed vertex positions; +0x2C is 1.0f on
    /// 4828/4828, so the four words form a Vector4 whose W is a constant.</summary>
    [FileOffset(0x20)] public Vector3 scale;

    /// <summary>+0x2C. 1.0f on 4828/4828.</summary>
    [FileOffset(0x2C)] public float scaleW;

    /// <summary>+0x30. Whole-prototype bounding sphere, same slot as the old engine's.</summary>
    [FileOffset(0x30)] public Vector4 boundingSphere;

    /// <summary>+0x40 / +0x50. Aggregate AABB, with a zero word after each, exactly like
    /// <see cref="TieMetadataOld"/>.</summary>
    [FileOffset(0x40)] public Vector3 boundsHalfExtents;

    /// <inheritdoc cref="boundsHalfExtents"/>
    [FileOffset(0x4C)] public float BoundsReserved4C;

    /// <inheritdoc cref="boundsHalfExtents"/>
    [FileOffset(0x50)] public Vector3 boundsCenter;

    /// <inheritdoc cref="boundsHalfExtents"/>
    [FileOffset(0x5C)] public float BoundsReserved5C;

    /// <summary>+0x60. Zero on 4828/4828.</summary>
    [FileOffset(0x60)] public uint Reserved60;

    /// <summary>+0x64. Points inside section 0x3410, this tie's name string: 4828/4828.</summary>
    [FileOffset(0x64)] public uint nameOffset;

    /// <summary>
    /// +0x74. Pointer to the tie's optional auxiliary block, or 0xFFFFFFFF. It resolves to section
    /// 0x3900 on all 2378 QForce / Into the Nexus resources (the only ones that carry 0x3900), and
    /// on A Crack in Time it is the sentinel except for the 55 Ties that own an embedded texture,
    /// where it points into their 0x5800 texel blob.
    /// </summary>
    [FileOffset(0x74)] public uint auxiliaryPointer;

    /// <summary>+0x78. Packed bytes; semantics OPEN.</summary>
    [FileOffset(0x78)] public uint Unk78;

    /// <summary>+0x7C. Zero on A Crack in Time; a {u16, u16} pair of small counts on the others.</summary>
    [FileOffset(0x7C)] public uint Unk7C;

    public readonly uint MeshesCount => meshesCount;
    public readonly uint PrimitiveCount => meshesCount;

    /// <summary>+0x68. The tie's own TUID, and it equals the assetlookup 0x1D300 pointer TUID on
    /// 4828/4828 resources. It used to read back as 0 because <see cref="FileUtils"/> skipped
    /// private fields - see the binding-flags note there.</summary>
    public ulong TUID { readonly get => _tuid; init => _tuid = value; }
    [FileOffset(0x68)] private ulong _tuid;

    public static TieMetadataNew Read(StreamHelper sh) => FileUtils.ReadStructure<TieMetadataNew>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
