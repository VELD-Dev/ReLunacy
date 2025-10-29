using System.Buffers;
using System.Buffers.Binary;
using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Meshes;
using LibLunacy.Numerics;

namespace LibLunacy.Objects;

[FileStructure(0x80)]
public record struct TieMetadataOld : ILunaObject, ILunaSerializable
{
    public const uint PointerID = 0x1D300;
    public const uint ID = 0x3400;
    public const uint Size = 0x80;

    [FileOffset(0x00)] public uint meshesOffset;
    [FileOffset(0x04)] [Reference(0x0B)] public byte[] Unk1;
    [FileOffset(0x0F)] public byte meshesCount;
    [FileOffset(0x10)] public uint Unk2;
    [FileOffset(0x14)] public uint verticesBufferStart;
    [FileOffset(0x18)] public uint verticesBufferSize;
    [FileOffset(0x1C)] public uint Unk3;
    [FileOffset(0x20)] public Vec3 scale;
    [FileOffset(0x2C)] [Reference(0x38)] public byte[] Unk4;
    [FileOffset(0x64)] public uint nameOffset;
    [FileOffset(0x68)] [Reference(0x18)] public byte[] Unk5;

    private ulong _tuidOverride;
    public ulong TUID { readonly get => _tuidOverride; init => _tuidOverride = value; }

    public TieMesh[] meshes;

    public static TieMetadataOld Read(StreamHelper sh, uint index)
    {
        var metadata = FileUtils.ReadStructure<TieMetadataOld>(sh);
        metadata._tuidOverride = index;
        metadata.meshes = new TieMesh[metadata.meshesCount];
        return metadata;
    }

    /// <summary>
    /// Reads the Ties meshes metadata. Note: You must read their vertices individually and manually later.
    /// </summary>
    /// <param Name="sh">Stream of the tie.dat file</param>
    /// <param Name="isOld">Whether it's on the old or the new engine.</param>
    public readonly void ReadMeshes(StreamHelper sh, bool isOld)
    {
        var offset = TUID + meshesOffset;
        sh.Seek((long)offset, SeekOrigin.Begin);
        for(uint i = 0; i < meshesCount; i++)
        {
            meshes[i] = new(sh, isOld);
            sh.BaseStream.Position += TieMesh.Size;
        }
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], meshesOffset);        offset += sizeof(uint);
        Unk1.CopyTo(span[offset..]);                                                offset += Unk1.Length;
        MemoryMarshal.Write(span[offset..], ref meshesCount);                       offset += sizeof(byte);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk2);                offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], verticesBufferStart); offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], verticesBufferSize);  offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk3);                offset += sizeof(uint);
        scale.ToBytes(span[offset..]);                                              offset += Vec3.Size;
        Unk4.CopyTo(span[offset..]);                                                offset += Unk4.Length;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], nameOffset);          offset += sizeof(uint);
        Unk5.CopyTo(span[offset..]);                                                offset += Unk5.Length;

        if(rented.Length != Size)
        {
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning a {nameof(TieMetadataOld)} into an array of bytes: Sizes does not match (0x{rented.Length:X}/0x{Size:X})");
        }
        return rented;
    }
}

[FileStructure(0x80)]
public record struct TieMetadataNew : ILunaObject, ILunaSerializable
{
    public const uint PointerID = 0x1D300;
    public const uint ID = 0x3400;
    public const uint Size = 0x80;

    [FileOffset(0x00)] public uint meshesOffset;
    [FileOffset(0x04)] [Reference(0x0B)] public byte[] Unk1;
    [FileOffset(0x0F)] public byte meshesCount;
    [FileOffset(0x10)] public uint Unk2;
    [FileOffset(0x14)] public uint verticesBufferStart;
    [FileOffset(0x18)] public uint verticesBufferSize;
    [FileOffset(0x1C)] public uint Unk3;
    [FileOffset(0x20)] public Vec3 scale;
    [FileOffset(0x2C)] [Reference(0x38)] public byte[] Unk4;
    [FileOffset(0x64)] public uint nameOffset;
    public ulong TUID { get => _tuid; init => _tuid = value; }
    [FileOffset(0x68)] private ulong _tuid;
    [FileOffset(0x70)] [Reference(0x10)] public byte[] Unk5;

    public TieMesh[] meshes;

    public static TieMetadataNew Read(StreamHelper sh)
    {
        var metadata = FileUtils.ReadStructure<TieMetadataNew>(sh);
        metadata.meshes = new TieMesh[metadata.meshesCount];
        return metadata;
    }

    /// <summary>
    /// Reads the Ties meshes metadata. Note: You must read their vertices individually and manually later.
    /// </summary>
    /// <param Name="sh">Stream of the tie.dat file</param>
    /// <param Name="isOld">Whether it's on the old or the new engine.</param>
    public readonly void ReadMeshes(StreamHelper sh, bool isOld)
    {
        var offset = TUID + meshesOffset;
        sh.Seek((long)offset, SeekOrigin.Begin);
        for(uint i = 0; i < meshesCount; i++)
        {
            meshes[i] = new(sh, isOld);
            sh.BaseStream.Position += TieMesh.Size;
        }
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], meshesOffset);        offset += sizeof(uint);
        Unk1.CopyTo(span[offset..]);                                                offset += Unk1.Length;
        MemoryMarshal.Write(span[offset..], ref meshesCount);                       offset += sizeof(byte);  // i know it's 1, it's just for consistency
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk2);                offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], verticesBufferStart); offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], verticesBufferSize);  offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk3);                offset += sizeof(uint);
        scale.ToBytes(span[offset..]);                                              offset += Vec3.Size;
        Unk4.CopyTo(span[offset..]);                                                offset += Unk4.Length;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], nameOffset);          offset += sizeof(uint); 
        if(!isOld)
        {
            BinaryPrimitives.WriteUInt64BigEndian(span[offset..], TUID);            offset += sizeof(ulong);
        }
        Unk5.CopyTo(span[offset..]);                                                offset += Unk5.Length;

        if(rented.Length != Size)
        {
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning a {nameof(TieMetadataNew)} into an array of bytes: Sizes does not match (0x{rented.Length:X}/0x{Size:X})");
        }
        return rented;
    }
}
