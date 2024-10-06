using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using LibLunacy.Interfaces;
using LibLunacy.Meshes;

namespace LibLunacy.Objects;

public record struct Tie : ILunaObject, ILunaSerializable
{
    public const uint ID = 0x3400;
    public const uint Size = 0x80;

    public uint meshesOffset;
    public byte[] Unk1;
    public byte meshesCount;
    public uint Unk2;
    public uint verticesBufferStart;
    public uint verticesBufferSize;
    public uint Unk3;
    public Vector3 scale;
    public byte[] Unk4;
    public uint nameOffset;  // Not on old engine
    public byte[] Unk5;
    public ulong TUID { get; init; } // Old Engine didn't have TUIDs for ties back then

    public TieMesh[] meshes = Array.Empty<TieMesh>();

    public Tie(LunaStream stream, bool isOld, nuint sectionPointer, uint index)
    {
        meshesOffset =          stream.ReadUInt32(0x00);
        Unk1 =                  stream.Peek(0x04, 0x0B);
        meshesCount =           stream.Peek(0x0F, 1)[0];
        Unk2 =                  stream.ReadUInt32(0x10);
        verticesBufferStart =   stream.ReadUInt32(0x14);
        verticesBufferSize =    stream.ReadUInt32(0x18);
        Unk3 =                  stream.ReadUInt32(0x1C);
        scale =                 stream.ReadVector3(0x20);
        Unk4 =                  stream.Peek(0x2C, 0x38);
        nameOffset =            stream.ReadUInt32(0x64);
        if (isOld)
        {
            TUID =              sectionPointer + index * 0x80;
            Unk5 =              stream.Peek(0x68, 0x18);
        }
        else
        {
            TUID =              stream.ReadUInt64(0x68);
            Unk5 =              stream.Peek(0x70, 0x10);
        }
        meshes = new TieMesh[meshesCount];
    }

    /// <summary>
    /// Reads the Ties meshes metadata. Note: You must read their vertices individually and manually later.
    /// </summary>
    /// <param name="stream">Stream of the tie.dat file</param>
    /// <param name="isOld">Wether it's on the old or the new engine.</param>
    public void ReadMeshes(LunaStream stream, bool isOld)
    {
        var offset = TUID + meshesOffset;
        stream.Seek((long)offset, SeekOrigin.Begin);
        for(uint i = 0; i < meshesCount; i++)
        {
            meshes[i] = new(stream, isOld, i, TUID);
            stream.JumpRead((int)TieMesh.Size);
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
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], scale.X);             offset += sizeof(float);
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], scale.Y);             offset += sizeof(float);
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], scale.Z);             offset += sizeof(float);
        Unk4.CopyTo(span[offset..]);                                                offset += Unk4.Length;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], nameOffset);          offset += sizeof(uint); 
        if(!isOld)
        {
            BinaryPrimitives.WriteUInt64BigEndian(span[offset..], TUID);            offset += sizeof(ulong);
        }
        Unk5.CopyTo(span[offset..]);                                                offset += Unk5.Length;

        if(rented.Length != Size)
        {
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning a {nameof(Tie)} into an array of bytes: Sizes does not match (0x{rented.Length:X}/0x{Size:X})");
        }
        return rented;
    }
}
