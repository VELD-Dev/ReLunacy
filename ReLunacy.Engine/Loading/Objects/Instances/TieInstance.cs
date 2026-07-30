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

    /// <summary>This instance's baked lighting: entry X in BOTH zone section 0x5400 (light colour)
    /// and 0x5410 (tangent-space light direction). 0xFFFF = none.
    /// Lives in the low 16 bits of the u32 at 0x58 (Unk[4..8]) — i.e. bytes 0x5A/0x5B big-endian.
    /// VERIFIED against metropolis/main.dat: of 4848 tie instances, 1728 carry an index, every one
    /// of them DISTINCT, covering 0..1742 of that level's 1751 lightmap entries with no reuse. The
    /// high 16 bits are 0 in every instance, which is why the old engine takes the low half.
    /// That one-unique-texture-per-instance property is also why ties need no second UV set: each
    /// lightmapped instance has its own baked texture in the tie's own UV space.</summary>
    public readonly ushort LightmapIndex =>
        Unk != null && Unk.Length >= 8 ? (ushort)((Unk[6] << 8) | Unk[7]) : NoLightmap;

    public const ushort NoLightmap = 0xFFFF;
    public readonly bool HasLightmap => LightmapIndex != NoLightmap;

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

        // Read the trailing 0x2C bytes as RAW DATA rather than skipping them. The reflection path
        // can't be used here (it would treat these as a pointer and seek to garbage — see the
        // summary above), but they are not empty: the baked-lighting index lives at record offset
        // 0x5A, i.e. Unk[6..8]. This previously returned `Unk = []`, which silently made
        // LightmapIndex report "no lightmap" for every old-engine tie and left the entire baked
        // lighting path inert.
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
