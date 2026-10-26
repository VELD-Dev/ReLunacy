using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

public struct UFragMetadata : ILunaSerializable
{
    public const uint ID = 0x6200;
    public const uint Size = 0x80;

    public byte[] Unk1;
    public Vector3 position;
    public Vector4 boundingSphere;
    public uint indexOffset;
    public uint vertexOffset;
    public ushort indexCount;
    public ushort vertexCount;
    public byte[] Unk2;
    public ushort shaderIndex;
    public byte[] Unk3;

    /// <summary>This UFrag's entry in the zone's baked light colour (0x5400) and light direction
    /// (0x5410) lists - a shared ATLAS, not a private bake: 1377 of metropolis's 1987 UFrags are
    /// lightmapped across just 23 atlases, each UFrag occupying its own island via UFragVertex.UVs2.
    /// 0xFFFF = none (610 UFrags in the old-engine metropolis sample). Read from record +0x4E
    /// on both engines; new-engine values remain zone-local until ZoneReader maps them into the
    /// combined runtime lighting pool. Supersedes an earlier old-engine reading at 0x52, which was
    /// 0xFFFF for every UFrag in the level and so
    /// made terrain look unlit - it was taken from ReLunacy-Ymir on trust and never held up here.
    /// </summary>
    public ushort lightmapIndex;

    public const ushort NoLightmap = 0xFFFF;
    public readonly bool HasLightmap => lightmapIndex != NoLightmap;

    /// <summary>
    /// New engine: the record's baked-light vertex constant c[4] = (+0x68, +0x6A, +0x68, +0x68 / 3) as
    /// scaled u16s. PROVEN in the QForce EBOOT, UFrag renderer 0x0074DC98 (see
    /// <see cref="NewEngineBakedLighting"/>). Retail: non-zero on every lit record, zero on unlit ones,
    /// one value per lightmap atlas. Null on the old engine, whose record has no such field.
    /// </summary>
    public readonly Vector4? NewEngineBakedLight =>
        Unk3 is { Length: >= 0x1A }
            ? NewEngineBakedLighting.VertexConstant(
                BinaryPrimitives.ReadUInt16BigEndian(Unk3.AsSpan(0x16, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(Unk3.AsSpan(0x18, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(Unk3.AsSpan(0x16, 2)))
            : null;

    // New engine only
    public Vector3 newEnginePos;
    public byte[] Unk3b;

    /// <summary>
    /// New engine: record +0x54, the mask the zone loader ANDs with the material's +0x20 feature bits
    /// (+0x55 receives the material's render mode) - QForce EBOOT 0x006B7380. 0x1D on all 3,566 QForce
    /// records, so no retail UFrag loses a feature. Null on the old engine.
    /// </summary>
    public readonly byte? NewEngineFeatureMask => Unk3 is { Length: >= 0x14 } ? Unk3[0x02] : null;

    /// <summary>
    /// New engine: record +0x60/+0x62/+0x64 (s16), the normal-map, reflection and detail fade distances
    /// the UFrag renderer turns into c[19] = K / d (0x0072360C, fed record + 0x60 at 0x007511AC).
    /// (30, 90, 8) on 3,343 of the 3,566 QForce records. Null when the record is too short.
    /// </summary>
    public readonly Vector3? NewEngineFadeDistances =>
        Unk3 is { Length: >= 0x14 }
            ? new Vector3(
                BinaryPrimitives.ReadInt16BigEndian(Unk3.AsSpan(0x0E, 2)),
                BinaryPrimitives.ReadInt16BigEndian(Unk3.AsSpan(0x10, 2)),
                BinaryPrimitives.ReadInt16BigEndian(Unk3.AsSpan(0x12, 2)))
            : null;
    public byte[] Unk4;

    public UFragMetadata(StreamHelper sh, bool oldEngine, int index = 0)
    {
        // Literal offsets below are relative to this record's own start, not the file's -
        // StreamHelper's ReadXxx(offset)/Seek(offset) overloads seek absolutely from the stream
        // start, so every offset here must be added to recordBase.
        uint recordBase = (uint)sh.Offset;

        if (oldEngine)
        {
            Unk1 = sh.ReadFromOffset(0x40, recordBase + 0x00);
            // Old-engine indexOffset is a vertex count, not a byte offset.
            indexOffset = sh.ReadUInt32(recordBase + 0x40) * sizeof(ushort);
            Unk3 = sh.ReadFromOffset(0x0E, recordBase + 0x52);
            lightmapIndex = sh.ReadUInt16(recordBase + 0x4E);
            // Placement anchor, fixed-point x256 - ZoneReader divides.
            sh.Seek(recordBase + 0x60);
            position = new Vector3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            // Bounding sphere: centre at 0x30, radius at 0x3C, plain world-space floats.
            sh.Seek(recordBase + 0x30);
            boundingSphere = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            Unk4 = sh.ReadFromOffset(0x14, recordBase + 0x6C);
            newEnginePos = position;
            Unk3b = [];
        }
        else
        {
            Unk1 = sh.ReadFromOffset(0x30, recordBase + 0x00);
            sh.Seek(recordBase + 0x30);
            position = new Vector3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            sh.Seek(recordBase + 0x30);
            boundingSphere = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            indexOffset = sh.ReadUInt32(recordBase + 0x40);
            Unk3 = sh.ReadFromOffset(0x1E, recordBase + 0x52);
            // New engine stores the UFrag's zone-local baked-lighting pair index here too.
            // 0xFFFF means no bake. The previously assumed zone section 0x6400/+0x06 mapping was
            // falsified against retail data: +0x06 produces values far outside the zone's lighting
            // descriptor count, while +0x4E has the same low-cardinality atlas-index shape as the
            // old engine and matches the presence of valid UV2/lightmap coordinates per UFrag.
            //
            // IMPORTANT: this remains the serialized, ZONE-LOCAL index. ZoneReader remaps it to the
            // combined runtime lighting pool when constructing NewUFrag; never write that runtime
            // index back into this metadata record.
            lightmapIndex = sh.ReadUInt16(recordBase + 0x4E);
            sh.Seek(recordBase + 0x70);
            newEnginePos = new Vector3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            Unk3b = sh.ReadFromOffset(0x04, recordBase + 0x7C);
            Unk4 = [];
        }

        vertexOffset = sh.ReadUInt32(recordBase + 0x44);
        indexCount = sh.ReadUInt16(recordBase + 0x48);
        vertexCount = sh.ReadUInt16(recordBase + 0x4A);
        Unk2 = sh.ReadFromOffset(0x04, recordBase + 0x4C);
        shaderIndex = sh.ReadUInt16(recordBase + 0x50);
    }

    /// <summary>
    /// Deterministic serializer. Unknown/open fields are preserved from their exact raw ranges; known
    /// fields are written back at their native absolute offsets. Do not use ArrayPool here: Rent only
    /// guarantees a capacity >= requested and therefore cannot be the returned serialized record.
    /// </summary>
    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        byte[] bytes = new byte[checked((int)Size)];
        Span<byte> span = bytes;

        if (isOld)
        {
            RequireLength(Unk1, 0x40, nameof(Unk1));
            RequireLength(Unk2, 0x04, nameof(Unk2));
            RequireLength(Unk3, 0x0E, nameof(Unk3));
            RequireLength(Unk4, 0x14, nameof(Unk4));

            Unk1.CopyTo(span[0x00..0x40]);
            BinaryPrimitives.WriteUInt32BigEndian(span[0x40..], checked(indexOffset / sizeof(ushort)));
            BinaryPrimitives.WriteUInt32BigEndian(span[0x44..], vertexOffset);
            BinaryPrimitives.WriteUInt16BigEndian(span[0x48..], indexCount);
            BinaryPrimitives.WriteUInt16BigEndian(span[0x4A..], vertexCount);
            Unk2.CopyTo(span[0x4C..0x50]);
            // lightmapIndex lives inside the formerly opaque +0x4C..+0x4F range and is now a known
            // authored field, so emit the typed value rather than silently preserving a stale raw copy.
            BinaryPrimitives.WriteUInt16BigEndian(span[0x4E..], lightmapIndex);
            BinaryPrimitives.WriteUInt16BigEndian(span[0x50..], shaderIndex);
            Unk3.CopyTo(span[0x52..0x60]);
            position.ToBytes(span[0x60..0x6C]);
            Unk4.CopyTo(span[0x6C..0x80]);
        }
        else
        {
            RequireLength(Unk1, 0x30, nameof(Unk1));
            RequireLength(Unk2, 0x04, nameof(Unk2));
            RequireLength(Unk3, 0x1E, nameof(Unk3));
            RequireLength(Unk3b, 0x04, nameof(Unk3b));

            Unk1.CopyTo(span[0x00..0x30]);
            position.ToBytes(span[0x30..0x3C]);
            BinaryPrimitives.WriteUInt32BigEndian(span[0x40..], indexOffset);
            BinaryPrimitives.WriteUInt32BigEndian(span[0x44..], vertexOffset);
            BinaryPrimitives.WriteUInt16BigEndian(span[0x48..], indexCount);
            BinaryPrimitives.WriteUInt16BigEndian(span[0x4A..], vertexCount);
            Unk2.CopyTo(span[0x4C..0x50]);
            // +0x4E is a known authored field on both engines. Unk2 still preserves +0x4C/+0x4D,
            // then the typed value overwrites only its two-byte slot.
            BinaryPrimitives.WriteUInt16BigEndian(span[0x4E..], lightmapIndex);
            BinaryPrimitives.WriteUInt16BigEndian(span[0x50..], shaderIndex);
            Unk3.CopyTo(span[0x52..0x70]);
            newEnginePos.ToBytes(span[0x70..0x7C]);
            Unk3b.CopyTo(span[0x7C..0x80]);
        }

        return bytes;
    }

    private static void RequireLength(byte[]? value, int expected, string name)
    {
        if (value == null || value.Length != expected)
            throw new InvalidOperationException($"{nameof(UFragMetadata)}.{name} must contain exactly 0x{expected:X} raw bytes before serialization.");
    }
}
