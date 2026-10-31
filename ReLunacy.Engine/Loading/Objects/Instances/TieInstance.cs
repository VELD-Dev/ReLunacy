using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects.Instances;

[FileStructure(0x80)]
public record struct TieInstance : ILunaSerializable
{
    /// <summary>New-engine tie instance struct array section.</summary>
    public const uint ID = 0x7240;
    public const uint OldID = 0x9240;
    /// <summary>New engine only: tie instance name pointer table, positionally matched to the instance array.</summary>
    public const uint NameTableID = 0x72C0;
    public const uint Size = 0x80;

    [FileOffset(0x00)] public Matrix4x4 transform;
    [FileOffset(0x40)] public Vector4 boundingSphere;
    /// <summary>OldEngine: index of the tie. NewEngine: index of Tie TUID in section 0x7200.</summary>
    [FileOffset(0x50)] public uint tieIndex;
    /// <summary>Inline trailing bytes at +0x54..+0x7F on both engine families. On new-engine
    /// TieInstanceV2 this starts with mainDataOffset and contains the signed lightMapId at +0x5A;
    /// it is not a pointer. Keep the opaque remainder byte-exact until more V2 fields are typed.</summary>
    [FileOffset(0x54)] public byte[] Unk;

    /// <summary>This instance's baked-lighting entry. The index is the signed/ushort field at
    /// record +0x5A (Unk[6..7]); 0xFFFF/-1 means none. New-engine TieInstanceV2 uses the same
    /// offset for its per-zone 0x5400/0x5410 pair, while old-engine levels index their global
    /// main.dat 0x5400/0x5410 pools.
    ///
    /// Old-engine corpus evidence: of 4848 metropolis tie instances, 1728 carry an index, every one
    /// of them DISTINCT, covering 0..1742 of that level's 1751 lightmap entries with no reuse. The
    /// high 16 bits are 0 in every instance, which is why the old engine takes the low half.
    ///
    /// Do not confuse texture ownership with UV ownership: the baked texture is per INSTANCE, while
    /// the recovered HALF2 lightmap-UV stream is per ASSET/MESH and is shared by every placement of
    /// that tie. Each lightmapped instance paints its own 0x5400/0x5410 texture into the same authored
    /// unwrap. Some retail Tie draws instead bind a polymorphic UNORM8x4 per-vertex prelight input;
    /// exact per-draw HALF2/UBYTE4 routing remains a separate reverse target.</summary>
    public readonly ushort LightmapIndex =>
        Unk != null && Unk.Length >= 8 ? (ushort)((Unk[6] << 8) | Unk[7]) : NoLightmap;

    public const ushort NoLightmap = 0xFFFF;

    /// <summary>
    /// New-engine record +0x5C (Unk[8]): 1 when the placement matrix is mirrored. PROVEN on QForce:
    /// 1 on 11 180 / 11 180 placements whose 3x3 has a negative determinant, 0 on the other 43 853.
    /// The Tie draw loop reads it as a byte and writes RSX FRONT_FACE = 0x901 - flag (CCW, or CW
    /// when mirrored) - QForce EBOOT 0x006E1570 / 0x006E206C - so culling keeps the outside visible.
    /// The renderer derives the same bit from the drawn matrix so editor transforms stay correct.
    /// </summary>
    public readonly bool NewEngineMirroredFlag => Unk != null && Unk.Length > 8 && Unk[8] != 0;

    /// <summary>
    /// Old-engine per-instance baked light scale candidate at +0x60 (BE float). +0x60 and +0x64 are
    /// equal on 61,986 / 61,986 retail Tie instances, matching the runtime pair the Tie render shell
    /// reads at callback52 arg1+0xF0/+0xF4 into VP constant c[4] = (2f, -f, f, ...), whose .z is the
    /// baked FPs' light scale (0x59912C). STRONG, not PROVEN: the instance-to-packet copy is not traced.
    /// Null when the record is too short.
    /// </summary>
    public readonly float? OldBakedLightScale =>
        Unk != null && Unk.Length >= 16
            ? System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(Unk.AsSpan(12, 4))
            : null;
    /// <summary>
    /// New engine: the placement's baked-light vertex constant c[4], from the u16 scales at +0x64 (RNM
    /// shaders), +0x66 (flat shaders) and +0x68 (c[4].z, and .w = z / 3). PROVEN in the QForce EBOOT:
    /// the loader copies the three u16s into the runtime placement (0x0073E02C) and the Tie renderer
    /// uploads them per draw (0x0073E9C0) - see <see cref="NewEngineBakedLighting"/>. Retail: +0x64 is
    /// non-zero on 53,448 / 53,448 lit placements and +0x64/+0x66 zero on 1,585 / 1,585 unlit ones.
    /// Null when the record is too short.
    /// </summary>
    public readonly Vector4? NewEngineBakedLight =>
        Unk != null && Unk.Length >= 0x16
            ? NewEngineBakedLighting.VertexConstant(
                BinaryPrimitives.ReadUInt16BigEndian(Unk.AsSpan(0x10, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(Unk.AsSpan(0x12, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(Unk.AsSpan(0x14, 2)))
            : null;

    public readonly bool HasLightmap => LightmapIndex != NoLightmap;

    /// <summary>
    /// New engine: record +0x54, the offset of this placement's prelight words inside its zone's
    /// lighting.dat slice (0xFFFFFFFF = none). The loader copies it to the runtime placement +0xD0
    /// (QForce EBOOT 0x0073DFAC) and the Tie renderer binds it as vertex attribute 4 (0x0073F5D8).
    /// </summary>
    public readonly uint NewEnginePrelightOffset =>
        Unk != null && Unk.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(Unk.AsSpan(0, 4)) : NoPrelight;

    public const uint NoPrelight = 0xFFFFFFFF;

    /// <summary>
    /// Reads the fixed 0x80-byte placement record without following +0x54 as a reference.
    /// New-engine TieInstanceV2 stores mainDataOffset inline there and lightMapId at +0x5A;
    /// old-engine placements also use this range as inline record data. Keeping one raw reader
    /// makes the on-disk contract agree with ToBytes() on both engines.
    /// </summary>
    public static TieInstance Read(StreamHelper sh) => ReadInline(sh);

    public static TieInstance ReadOld(StreamHelper sh) => ReadInline(sh);

    private static TieInstance ReadInline(StreamHelper sh)
    {
        long recordBase = sh.BaseStream.Position;

        var transform = new Matrix4x4(
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
        var boundingSphere = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
        uint tieIndex = sh.ReadUInt32();
        byte[] unk = sh.ReadFromOffset(0x2C, (uint)(recordBase + 0x54));

        sh.Seek(recordBase + Size);
        return new TieInstance
        {
            transform = transform,
            boundingSphere = boundingSphere,
            tieIndex = tieIndex,
            Unk = unk
        };
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        transform.ToBytes(span[offset..]); offset += 0x40;
        boundingSphere.ToBytes(span[offset..]); offset += 0x10;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], tieIndex); offset += sizeof(uint);
        Unk.CopyTo(span[offset..]); offset += Unk.Length;

        if (offset != Size)
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Some data have been lost while turning {nameof(TieInstance)} into an array of bytes. Sizes does not match ({offset:X}/{Size:X})");

        return rented;
    }
}
