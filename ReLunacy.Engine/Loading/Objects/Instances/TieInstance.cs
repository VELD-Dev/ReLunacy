using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects.Instances;

[FileStructure(0x80)]
public record struct TieInstance : ILunaSerializable
{
    /// <summary>New-engine tie instance struct array section. 0x72C0 (formerly used here) is actually the tie name pointer table — see <see cref="NameTableID"/>.</summary>
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

    public static TieInstance Read(StreamHelper sh) => FileUtils.ReadStructure<TieInstance>(sh);

    /// <summary>
    /// Old engine tie instances don't have the trailing Reference-indirected Unk field new-engine
    /// ones do — reading it via the shared reflection path would interpret whatever old-engine
    /// bytes happen to sit at 0x54 as a pointer and seek there, which is garbage for this engine
    /// and throws. Read the known-good fields directly instead.
    /// </summary>
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

        sh.Seek(recordBase + Size);

        return new TieInstance { transform = transform, boundingSphere = boundingSphere, tieIndex = tieIndex, Unk = [] };
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
