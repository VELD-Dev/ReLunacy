using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Meshes;
using System.Buffers;
using System.Buffers.Binary;

namespace LibLunacy.Objects;

[FileStructure(0x08)]
public record struct MobyBangle : ILunaSerializable
{
    public const uint Size = 0x08;

    [FileOffset(0x00), Reference(nameof(MeshesCount))] public MobyMesh[] meshes;
    [FileOffset(0x04)] public uint meshesCount;

    public readonly uint MeshesCount => meshesCount;

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        throw new NotImplementedException();

        /*
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], meshesPointer);   offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], meshesCount);     offset += sizeof(uint);

        if (rented.Length != Size)
        {
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning a {nameof(MobyBangle)} into an array of bytes: Sizes does not match (0x{rented.Length:X}/0x{Size:X})");
        }
        return rented;
        */
    }
}
