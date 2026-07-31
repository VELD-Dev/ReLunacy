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

    /// <summary>This UFrag's entry in the zone's baked light colour (0x5400) and light direction
    /// (0x5410) lists — a shared ATLAS, not a private bake: 1377 of metropolis's 1987 UFrags are
    /// lightmapped across just 23 atlases, each UFrag occupying its own island via UFragVertex.UVs2.
    /// 0xFFFF = none (610 UFrags). Read from old-engine offset 0x4E; see the constructor.
    /// Supersedes an earlier reading at 0x52, which was 0xFFFF for every UFrag in the level and so
    /// made terrain look unlit — it was taken from ReLunacy-Ymir on trust and never held up here.
    /// </summary>
    public ushort lightmapIndex;

    public const ushort NoLightmap = 0xFFFF;
    public readonly bool HasLightmap => lightmapIndex != NoLightmap;
    // New engine only
    public Vector3 newEnginePos;
    public byte[] Unk3b;
    public byte[] Unk4;

    public UFragMetadata(StreamHelper sh, bool oldEngine, int index = 0)
    {
        // Every literal offset below (0x00, 0x40, 0x60, ...) is relative to THIS record's own
        // start, not the file's. Unlike FileUtils.ReadStructure<T> (which captures this
        // automatically via [FileOffset]), this constructor is hand-written and reads through
        // StreamHelper's ReadXxx(offset)/Seek(offset) overloads, which all seek absolutely from
        // the start of the stream — so every literal offset here must be based off where this
        // record actually begins, or every UFrag past the first in a zone reads from the wrong
        // place in the file entirely (previously missing, causing garbage/absent geometry).
        uint recordBase = (uint)sh.Offset;

        if (oldEngine)
        {
            Unk1 = sh.ReadFromOffset(0x40, recordBase + 0x00);
            // Old-engine indexOffset is a vertex count, not a byte offset.
            indexOffset = sh.ReadUInt32(recordBase + 0x40) * sizeof(ushort);
            Unk3 = sh.ReadFromOffset(0x0E, recordBase + 0x52);
            // 0x4E, not 0x52. Terrain shares ATLASES rather than taking one bake each, so this
            // index has low cardinality — which is why earlier scans looking for a dense per-UFrag
            // index missed it entirely.
            // Verified on metropolis: 23 distinct values across 1987 UFrags (610 are 0xFFFF), and
            // every one of the 23 resolves to a 256x256 or 128x128 A8R8G8B8 entry in BOTH 0x5400
            // and 0x5410, in three contiguous runs. A field that wasn't this index would land on
            // one of the 85 large entries about 5% of the time; this lands 23/23.
            // Those atlases being A8R8G8B8 also matters: unlike the DXT1 per-tie bakes they carry a
            // real alpha channel, so the "alpha = monochrome specular light" reading is genuinely
            // populated for terrain.
            lightmapIndex = sh.ReadUInt16(recordBase + 0x4E);
            // Placement anchor, fixed-point x256 — ZoneReader divides. (0x6C, the float that would
            // follow it, is NaN on every UFrag in metropolis, so this is a Vector3 field and not a
            // sphere; reading a radius there is what forced the old 2.5f fallback.)
            sh.Seek(recordBase + 0x60);
            position = new Vector3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            // REAL bounding sphere: centre at 0x30 and radius at 0x3C, both plain world-space
            // floats needing no x256 decode. Verified against each UFrag's own decoded vertices on
            // metropolis (all 1987): the radius at 0x3C matches the sphere those vertices actually
            // describe to a median relative error of 0.0001, with 99.7% inside 10%, it is never
            // negative, and it spans 0.303..89.194 — so the 2.5f constant this replaces was wrong
            // for essentially every UFrag and made frustum culling drop large terrain chunks early.
            // The centre agrees with the anchor above to a median of 0.0025 world units (the two
            // describe the same point; 0x30 just carries full float precision instead of 1/256).
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
            // New engine keeps its lightmap/directional indices in zone section 0x6400 (one 0x10
            // entry per UFrag, lightmapindex at 0x06 and directionalindex at 0x08 — see Ymir), not
            // in this record. Not parsed yet, so no baked lighting is claimed for these.
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
