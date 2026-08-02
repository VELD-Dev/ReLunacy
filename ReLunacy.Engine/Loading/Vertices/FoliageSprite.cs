using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

/// <summary>One CORNER of a foliage sprite card: four big-endian half floats, 8 bytes, one per
/// vertex. This is RSX attribute location 0 of the foliage vertex program.
///
/// CONFIRMED AGAINST A CAPTURE, not inferred. RPCS3's DrawParametersBuffer for metropolis
/// (dev/VS_buffer_set0-2.csv) contains 3,133 draws with this exact layout, all three attributes
/// agreeing on stride and frequency in every single one:
///     loc0  stride=8  SFLOAT16 x4  frequency=1   -> THIS, one per corner
///     loc1  stride=8  SFLOAT16 x3  frequency=4   -> FoliageSpriteAnchor, one per QUAD
///     loc2  stride=8  UBYTE_RAW x4 frequency=4   -> the same 8-byte record, read at +4
/// The frequency=4 divisor is what makes the layout make sense: loc1/loc2 advance once every four
/// corners, so a 468-corner card set has only 117 anchor records behind it.
///
/// The four halves are (offsetX, offsetY, u, v), and the game's own vertex program says so:
///     r4.xy = in_pos.xy;                                  // corner offset
///     r3.xy = in_pos.zw;  ...  dst_reg9 = r3;  tc2 = dst_reg9;
/// and the foliage fragment program samples with exactly that: texture(tex0, tc2.xy).
/// The offset is added to the transformed anchor, scaled by a vertex constant:
///     r2.xyz = fma(r4.xyz, vc[41].x, transform(in_weight.xyz))
/// so <see cref="OffsetX"/>/<see cref="OffsetY"/> are in the card's own 2D plane and become world
/// units only after that constant is applied.
///
/// THE UVs ADDRESS ONE QUADRANT OF THE ATLAS. Over all 468 corners of metropolis's foliage the UV
/// components take exactly five values and nothing else: 0.0 (300x), -0.5 (234x), 0.5 (234x),
/// -1.0 (166x), 1.0 (2x). Every one is a multiple of 0.5, so the texture is split into four
/// quadrants and each sprite picks one. V is NEGATIVE, i.e. the vertical axis is flipped relative
/// to this renderer's convention - do not "fix" that by clamping, negate it (see
/// FoliageReader.ReadCorners) or the card samples the wrong quadrant.</summary>
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

/// <summary>The per-QUAD record behind a foliage card: three big-endian half floats of anchor
/// position plus two trailing bytes, 8 bytes total, one per sprite rather than per vertex.
///
/// This is attributes location 1 and location 2 of the same 8-byte record — the capture shows loc2
/// starting exactly 4 bytes after loc1, both at stride 8 with frequency 4. loc1 is SFLOAT16 x3, so
/// it occupies bytes +0..+5; the vertex program then reads only <c>in_normal.zw</c> out of loc2's
/// four raw bytes, which is bytes +6 and +7. Nothing reads +4/+5 twice — the halves and the two
/// used bytes tile the record exactly.
///
/// The anchor is transformed by vertex constants 32..35 (an object-to-world matrix) before the
/// corner offset is added, so it is the sprite's position in the foliage asset's local space.
///
/// The two trailing bytes drive an ADDRESS REGISTER in the vertex program:
///     r3.zw = (in_normal.zw >= vc[467].x);            // a threshold test per byte
///     r4.zw = fma(-r3.zw, vc[467].x, in_normal.zw);   // subtract it back out - integer unpacking
///     a0.x  = int(r0.w * vc[467].y);
///     ... vc[42 + a0.x], vc[43 + a0.x]                // indexed constant lookup
/// which selects a per-sprite 2x2 rotation applied to the corner offsets, gated on the high byte's
/// threshold flag. So these two bytes are a packed (rotation index, flags) pair. The exact packing
/// is NOT decoded here, because the constants it indexes live in the game's vertex constant block
/// and are not in the level files - reproducing the rotation needs those, or needs the rotation to
/// be re-derived. Stored raw so nothing is silently invented.</summary>
public readonly record struct FoliageSpriteAnchor
{
    /// <summary>Bytes per sprite - the RSX attribute's stride, with a frequency divisor of 4.</summary>
    public const uint Size = 0x08;

    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    /// <summary>Raw bytes at +6 and +7 - see the type comment. Packed, not yet decoded.</summary>
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
