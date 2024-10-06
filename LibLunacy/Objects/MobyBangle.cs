using LibLunacy.Interfaces;
using LibLunacy.Meshes;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects;

public record struct MobyBangle : ILunaSerializable
{
    public const uint Size = 0x08;

    public uint meshesPointer;
    public uint meshesCount;

    public MobyMesh[] meshes;

    public MobyBangle(LunaStream stream)
    {
        meshesPointer = stream.ReadUInt32(0x00);
        meshesCount =   stream.ReadUInt32(0x04);
        
        meshes = new MobyMesh[meshesCount];
    }

    public void ReadMeshes(LunaStream stream)
    {
        for (int i = 0; i < meshesCount; i++)
        {
            meshes[i] = new MobyMesh(stream);
            stream.JumpRead((int)MobyMesh.Size);
            stream.JumpRead((int)MobyMesh.Size);
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
