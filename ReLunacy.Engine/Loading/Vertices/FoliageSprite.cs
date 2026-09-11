using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

/// <summary>One corner of a foliage sprite card: four big-endian half floats (offsetX, offsetY,
/// u, v), 8 bytes, one per vertex. RSX attribute location 0 of the foliage vertex program.
///
/// The offset is added to the transformed anchor, scaled by a vertex constant, so
/// <see cref="OffsetX"/>/<see cref="OffsetY"/> are in the card's own 2D plane and become world
/// units only after that constant is applied.
///
/// The UVs address one quadrant of the atlas (components are multiples of 0.5). V is negated
/// relative to this renderer's convention - see FoliageReader.ReadCorners.</summary>
public readonly record struct FoliageSpriteCorner
{
    /// <summary>Bytes per corner - the RSX attribute's stride.</summary>
    public const uint Size = 0x08;

    /// <summary>Corners per sprite card. Also the frequency divisor on the anchor attribute.</summary>
    public const int CornersPerSprite = 4;

    public readonly float OffsetX;
    public readonly float OffsetY;
    public readonly float U;
    public readonly float V;

    public FoliageSpriteCorner(float offsetX, float offsetY, float u, float v)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        U = u;
        V = v;
    }

    public static FoliageSpriteCorner Read(StreamHelper sh) =>
        new((float)sh.ReadHalf(), (float)sh.ReadHalf(), (float)sh.ReadHalf(), (float)sh.ReadHalf());
}

/// <summary>The per-quad record behind a foliage card: three big-endian half floats of anchor
/// position plus two trailing bytes, 8 bytes total, one per sprite rather than per vertex. RSX
/// attribute locations 1 and 2 of the same 8-byte record (loc1 = position, loc2 read at +6/+7).
///
/// The anchor is transformed by an object-to-world matrix before the corner offset is added, so
/// it is the sprite's position in the foliage asset's local space.
///
/// The two trailing bytes select a per-sprite 2x2 rotation applied to the corner offsets via an
/// indexed vertex-constant lookup. Packing is not decoded - the constants it indexes live in the
/// game's vertex constant block, not the level files. Stored raw.</summary>
public readonly record struct FoliageSpriteAnchor
{
    /// <summary>Bytes per sprite - the RSX attribute's stride, with a frequency divisor of 4.</summary>
    public const uint Size = 0x08;

    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    /// <summary>Raw bytes at +6 and +7 - packed (rotation index, flags), not yet decoded.</summary>
    public readonly byte Packed0;
    public readonly byte Packed1;

    public FoliageSpriteAnchor(float x, float y, float z, byte packed0, byte packed1)
    {
        X = x;
        Y = y;
        Z = z;
        Packed0 = packed0;
        Packed1 = packed1;
    }

    public static FoliageSpriteAnchor Read(StreamHelper sh)
    {
        float x = (float)sh.ReadHalf();
        float y = (float)sh.ReadHalf();
        float z = (float)sh.ReadHalf();
        byte p0 = sh.ReadByte();
        byte p1 = sh.ReadByte();
        return new FoliageSpriteAnchor(x, y, z, p0, p1);
    }
}
