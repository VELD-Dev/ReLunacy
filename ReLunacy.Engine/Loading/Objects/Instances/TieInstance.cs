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
    [FileOffset(0x54), Reference(0x2C)] public byte[] Unk;

    /// <summary>This instance's baked lighting index into zone section 0x5400 (light colour) and
    /// 0x5410 (tangent-space light direction); 0xFFFF = none. Lives in the low 16 bits of the u32
    /// at 0x58 (Unk[4..8]), i.e. bytes 0x5A/0x5B big-endian.</summary>
    public readonly ushort LightmapIndex =>
        Unk != null && Unk.Length >= 8 ? (ushort)((Unk[6] << 8) | Unk[7]) : NoLightmap;

    public const ushort NoLightmap = 0xFFFF;
    public readonly bool HasLightmap => LightmapIndex != NoLightmap;

    public static TieInstance Read(StreamHelper sh) => FileUtils.ReadStructure<TieInstance>(sh);

    /// <summary>Old-engine instances lack the trailing Reference-indirected Unk field new-engine
    /// ones have, so this reads the known-good fields directly instead of going through the
    /// shared reflection path.</summary>
    public static TieInstance ReadOld(StreamHelper sh)
    {
        long recordBase = sh.BaseStream.Position;

        var transform = new Matrix4x4(
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
        var boundingSphere = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
        uint tieIndex = sh.ReadUInt32();

        // Trailing 0x2C bytes hold the baked-lighting index at record offset 0x5A (Unk[6..8]),
        // read as raw data rather than through the reflection/pointer path.
        byte[] unk = sh.ReadFromOffset(0x2C, (uint)(recordBase + 0x54));

        sh.Seek(recordBase + Size);

        return new TieInstance { transform = transform, boundingSphere = boundingSphere, tieIndex = tieIndex, Unk = unk };
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
