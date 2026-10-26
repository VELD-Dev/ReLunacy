using System.Numerics;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Interfaces;

namespace ReLunacy.Engine.Loading.Objects.Instances;

/// <summary>One placement of a foliage asset. Old-engine section 0x9340, 224 bytes per record.</summary>
public record struct FoliageInstance : ILunaSerializable
{
    public const uint ID = 0x9340;
    public const uint Size = 0xE0;

    /// <summary>Placement matrix, read raw rather than through Euler properties.</summary>
    public Matrix4x4 Transform;

    /// <summary>Absolute offset of this placement's <see cref="FoliageMetadata"/> record inside
    /// main.dat - not an index. Resolve it against section 0xA200's own offset to get an ordinal.</summary>
    public uint FoliageOffset;

    /// <summary>Not identified.</summary>
    public uint Unk1;

    /// <summary>Bounding sphere in world space: 0xB0..0xB8 is the centre, 0xBC the radius.
    /// Not verified against actual sprite extents - treat the radius as a candidate.</summary>
    public Vector4 BoundingSphere;

    public static FoliageInstance Read(StreamHelper sh, uint recordBase)
    {
        sh.Seek(recordBase);
        var m = new Matrix4x4(
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
            sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());

        sh.Seek(recordBase + 0xB0);
        var sphere = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());

        return new FoliageInstance
        {
            Transform = m,
            BoundingSphere = sphere,
            FoliageOffset = sh.ReadUInt32(recordBase + 0xC4),
            Unk1 = sh.ReadUInt32(recordBase + 0xC8),
        };
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
