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

    [FileOffset(0x00)] public uint meshesPointer;
    [FileOffset(0x04)] public uint meshesCount;

    public MobyMesh[] meshes;

    public static MobyBangle Read(StreamHelper sh)
    {
        var bangle = FileUtils.ReadStructure<MobyBangle>(sh);
        bangle.meshes = new MobyMesh[bangle.meshesCount];
        return bangle;
    }

    public void ReadMeshes(StreamHelper sh)
    {
        for (int i = 0; i < meshesCount; i++)
        {
            meshes[i] = MobyMesh.Read(sh);
            sh.BaseStream.Position += MobyMesh.Size;
            sh.BaseStream.Position += MobyMesh.Size;
        }
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
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
    }
}
