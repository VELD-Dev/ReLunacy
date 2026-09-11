using System.Buffers;
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

    /// <summary>Index into the zone's baked light colour (0x5400) and light direction (0x5410)
    /// atlas lists; each UFrag occupies its own island via UFragVertex.UVs2. 0xFFFF = none. Read
    /// from old-engine offset 0x4E.</summary>
    public ushort lightmapIndex;

    public const ushort NoLightmap = 0xFFFF;
    public readonly bool HasLightmap => lightmapIndex != NoLightmap;
    // New engine only
    public Vector3 newEnginePos;
    public byte[] Unk3b;
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
            // New engine keeps lightmap/directional indices in zone section 0x6400, not this
            // record. Not parsed yet.
            lightmapIndex = NoLightmap;
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

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        if (isOld)
        {
            Unk1.CopyTo(span[offset..]); offset += Unk1.Length;
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], indexOffset / sizeof(ushort)); offset += sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], vertexOffset); offset += sizeof(uint);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indexCount);  offset += sizeof(ushort);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], vertexCount); offset += sizeof(ushort);
            Unk2.CopyTo(span[offset..]); offset += Unk2.Length;
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], shaderIndex); offset += sizeof(ushort);
            Unk3.CopyTo(span[offset..]); offset += Unk3.Length;
            position.ToBytes(span[offset..]); offset += 0x0C;
            Unk4.CopyTo(span[offset..]); offset += Unk4.Length;
        }
        else
        {
            Unk1.CopyTo(span[offset..]); offset += Unk1.Length;
            position.ToBytes(span[offset..]); offset += 0x0C;
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], indexOffset); offset += sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], vertexOffset); offset += sizeof(uint);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indexCount); offset += sizeof(ushort);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], vertexCount); offset += sizeof(ushort);
            Unk2.CopyTo(span[offset..]); offset += Unk2.Length;
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], shaderIndex); offset += sizeof(ushort);
            Unk3.CopyTo(span[offset..]); offset += Unk3.Length;
            newEnginePos.ToBytes(span[offset..]); offset += 0x0C;
            Unk3b.CopyTo(span[offset..]); offset += Unk3b.Length;
        }

        if (rented.Length != Size)
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning an {nameof(UFragMetadata)} into bytes: Size does not match (0x{rented.Length:X}/0x{Size:X})");
        return rented;
    }
}
