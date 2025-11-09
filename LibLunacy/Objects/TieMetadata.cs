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

    [FileOffset(0x00), Reference(nameof(MeshesCount))] public TieMesh[] meshes;
    [FileOffset(0x0F)] public byte meshesCount;
    [FileOffset(0x10)] public uint Unk2;
    [FileOffset(0x14)] public uint verticesBufferStart;
    [FileOffset(0x18)] public uint verticesBufferSize;
    [FileOffset(0x1C)] public uint Unk3;
    [FileOffset(0x20)] public Vec3 scale;
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

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        throw new NotImplementedException();
        /*
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
        */
    }
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
    [FileOffset(0x20)] public Vec3 scale;
    [FileOffset(0x64)] public uint nameOffset;

    public readonly uint MeshesCount => meshesCount;
    public ulong TUID { get => _tuid; init => _tuid = value; }
    [FileOffset(0x68)] private ulong _tuid;

    public static TieMetadataNew Read(StreamHelper sh)
    {
        var metadata = FileUtils.ReadStructure<TieMetadataNew>(sh);
        return metadata;
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        throw new NotImplementedException();
        /*
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
        */
    }
}
