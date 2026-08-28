using System.Numerics;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Interfaces;

namespace ReLunacy.Engine.Loading.Objects.Instances;

/// <summary>One placement of a foliage asset. Old-engine section 0x9340, 224 bytes per record
/// (metropolis: 757 instances across 2 assets).
///
/// The 224-byte size is exactly InsomniaToolset's FoliageInstance - Matrix44 (0x40) + float[33]
/// (0x84) + a foliage pointer + uint[2] + uint[4] - and the content agrees: the first 64 bytes
/// decode as an affine matrix (instance 0 translates to 340.49, 30.45, -250.64) and the field at
/// 0xC4 holds an absolute main.dat address that lands on a record of section 0xA200 for all 757.
/// The toolset's own ID for this (0x9700) does not appear in this game's files; 0x9340 is the
/// old-engine equivalent, matched by size and by that pointer resolving.</summary>
public record struct FoliageInstance : ILunaSerializable
{
    public const uint ID = 0x9340;
    public const uint Size = 0xE0;

    /// <summary>Placement matrix, read raw. Decompose it directly rather than going through Euler
    /// properties, for the same reason EntityTie does.</summary>
    public Matrix4x4 Transform;

    /// <summary>Absolute offset of this placement's <see cref="FoliageMetadata"/> record inside
    /// main.dat - not an index. Resolve it against section 0xA200's own offset to get an ordinal.</summary>
    public uint FoliageOffset;

    /// <summary>0xFFFFFFFF on all 757 metropolis instances. Not identified.</summary>
    public uint Unk1;

    /// <summary>Bounding sphere in world space: 0xB0..0xB8 is the centre and 0xBC the radius. The
    /// centre tracks the matrix translation closely and the radius is a small positive float, both
    /// consistent with the same layout UFragMetadata uses at its own 0x30/0x3C. NOT verified
    /// against the actual sprite extents the way the UFrag one was - treat the radius as a
    /// candidate and prefer computing bounds from geometry if a cull ever looks wrong.</summary>
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
