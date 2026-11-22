// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Numerics;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>Where a spawn generator puts a new particle, in the emitter's frame (Z = the emitter's axis).</summary>
public enum EffectSpawnPosition
{
    /// <summary>d x R x U, d uniform on the sphere, U uniform in [0, 1) - denser towards the centre.</summary>
    SphereVolume,
    /// <summary>d x R.</summary>
    SphereSurface,
    /// <summary>c x R x U, c in the cone: tilt uniform in [min, max] degrees from +Z, azimuth uniform.</summary>
    ConeVolume,
    /// <summary>c x R.</summary>
    ConeSurface,
    /// <summary>r x R x U, r a unit direction of the XY plane (uniform azimuth).</summary>
    Disc,
    /// <summary>r x R.</summary>
    Ring,
    /// <summary>((2U - 1), (2U - 1), 0) x R: a square of the XY plane.</summary>
    Square,
    /// <summary>((2U - 1), (2U - 1), (2U - 1)) x R: a cube.</summary>
    Box,
    /// <summary>+Z x R x U: a segment along the axis.</summary>
    AxisSegment,
    /// <summary>d x R plus (camera - emitter) x U: spread along the line to the camera. The editor has no camera
    /// in effect space, so the preview draws the sphere part only.</summary>
    CameraLine,
    /// <summary>At the emitter.</summary>
    Point,
}

/// <summary>Which way a new particle moves (unit direction; the spawn speed scales it).</summary>
public enum EffectSpawnVelocity
{
    /// <summary>Along the position's own direction (d, c, r, or the normalized square/box point).</summary>
    Outward,
    Inward,
    /// <summary>Outward or inward, 50/50 per particle.</summary>
    EitherWay,
    /// <summary>A new uniform direction, independent of the position.</summary>
    Random,
    /// <summary>A uniform direction perpendicular to the sphere direction.</summary>
    Perpendicular,
    /// <summary>The cone direction's horizontal part, normalized (flat, away from the axis).</summary>
    FlatOutward,
    /// <summary>Along the ring's tangent, either sense.</summary>
    Tangent,
    /// <summary>Along the ring's tangent, counter-clockwise about +Z.</summary>
    TangentCounterClockwise,
    /// <summary>+Z.</summary>
    Axis,
    /// <summary>+Z or -Z, 50/50.</summary>
    AxisEitherWay,
    /// <summary>+Z tilted towards the ring direction by an angle uniform in [min, max].</summary>
    TiltOutward,
    /// <summary>+Z tilted towards the axis side (away from the ring direction) by [min, max].</summary>
    TiltInward,
    /// <summary>+Z tilted towards the ring direction by an angle uniform in [-min, max] (negative = inward).</summary>
    TiltSigned,
    /// <summary>The cone of <see cref="EffectSpawnPosition.ConeVolume"/>, drawn independently of the position.</summary>
    Cone,
    /// <summary>A uniform point of the unit ball (length below 1).</summary>
    Ball,
}

/// <summary>One spawn generator: the retail FXVis overlay a spawn style byte selects.</summary>
public readonly record struct EffectSpawnStyleInfo(int Style, EffectSpawnPosition Position, EffectSpawnVelocity Velocity, string Evidence)
{
    public string Label => $"{Position} / {Velocity}";
}

/// <summary>
/// The 61 spawn generators of the new engine (QForce EBOOT; All 4 One and Into the Nexus use the same byte
/// values). PROVEN chain: the PPU packer 0x00B59D50 hands 0x007E3D60 a pointer to FxEmtSpawnStyleControl
/// (+0x328) and it copies the style byte to the SPU record byte 4 (and the cone bytes to 5 and 6); FXVis
/// unpacks byte 4 to its emitter state +96 (0x35B0) and uses it to DMA one of 61 SPU overlays (FX globals
/// +0x1640, filled by PPU 0x0085C294 from the table at 0x00C7C23C..0x00C7C4AC) which it calls through
/// service 0x3F80 for every batch of 8 new particles. Each generator's behaviour was measured by running the
/// retail overlay in <c>Tools/Reverse/effects/spuemu.py</c> (<c>fxvis_harness.py</c>, 480 draws per style) and
/// read in its code for the ambiguous cases; <see cref="EffectSpawnStyleInfo.Evidence"/> says which. The byte
/// is an index, not a set of flags.
///
/// Randomness (PROVEN, FXVis): directions come from a 128-vector table built on the PPU as a Fibonacci sphere
/// (0x007F3F00: z = i/64 - 0.99609375, r = sqrt(1 - z²), angle = i x (3 - √5)π), shuffled, and turned by a random
/// rotation at every job start (0x176D8); U comes from eight 16-bit LCGs x' = x·0xB97D + 1 (0x6BA8). The preview
/// draws the same distributions with its own generator.
/// </summary>
public static class EffectSpawnStyles
{
    private const string Measured = "PROVEN (retail overlay run in spuemu)";
    private const string Read = "PROVEN (overlay code read + run)";
    private const string Approx = "INFERRED (measured; unused by the three games' levels)";

    private static readonly EffectSpawnStyleInfo[] Table = BuildTable();

    private static EffectSpawnStyleInfo[] BuildTable()
    {
        var t = new EffectSpawnStyleInfo[0x3D];
        void Row(int s, EffectSpawnPosition p, EffectSpawnVelocity v, string ev) => t[s] = new(s, p, v, ev);
        EffectSpawnVelocity[] five = [EffectSpawnVelocity.Outward, EffectSpawnVelocity.Inward, EffectSpawnVelocity.EitherWay,
            EffectSpawnVelocity.Random, EffectSpawnVelocity.Perpendicular];
        for (int k = 0; k < 5; k++)
        {
            Row(0x00 + k, EffectSpawnPosition.SphereVolume, five[k], k is 0 or 1 ? Measured : Approx);
            Row(0x05 + k, EffectSpawnPosition.SphereSurface, five[k], k is 0 or 1 ? Measured : Approx);
            var coneVel = k == 4 ? EffectSpawnVelocity.FlatOutward : five[k];
            Row(0x0A + k, EffectSpawnPosition.ConeVolume, coneVel, k is 0 or 1 ? Read : Approx);
            Row(0x0F + k, EffectSpawnPosition.ConeSurface, coneVel, k is 0 or 1 ? Measured : Approx);
        }
        Row(0x14, EffectSpawnPosition.Disc, EffectSpawnVelocity.Outward, Measured);
        Row(0x15, EffectSpawnPosition.Disc, EffectSpawnVelocity.Inward, Approx);
        Row(0x16, EffectSpawnPosition.Disc, EffectSpawnVelocity.EitherWay, Approx);
        Row(0x17, EffectSpawnPosition.Disc, EffectSpawnVelocity.Random, Approx);
        Row(0x18, EffectSpawnPosition.Disc, EffectSpawnVelocity.Axis, Measured);
        Row(0x19, EffectSpawnPosition.Disc, EffectSpawnVelocity.TiltOutward, Read);
        Row(0x1A, EffectSpawnPosition.Disc, EffectSpawnVelocity.TiltInward, Approx);
        Row(0x1B, EffectSpawnPosition.Disc, EffectSpawnVelocity.Tangent, Approx);
        Row(0x1C, EffectSpawnPosition.Disc, EffectSpawnVelocity.TangentCounterClockwise, Approx);
        Row(0x1D, EffectSpawnPosition.Ring, EffectSpawnVelocity.Outward, Measured);
        Row(0x1E, EffectSpawnPosition.Ring, EffectSpawnVelocity.Inward, Measured);
        Row(0x1F, EffectSpawnPosition.Ring, EffectSpawnVelocity.EitherWay, Approx);
        Row(0x20, EffectSpawnPosition.Ring, EffectSpawnVelocity.Random, Measured);
        Row(0x21, EffectSpawnPosition.Ring, EffectSpawnVelocity.Axis, Measured);
        Row(0x22, EffectSpawnPosition.Ring, EffectSpawnVelocity.TiltOutward, Measured);
        Row(0x23, EffectSpawnPosition.Ring, EffectSpawnVelocity.TiltInward, Measured);
        Row(0x24, EffectSpawnPosition.Ring, EffectSpawnVelocity.TiltSigned, Read);
        Row(0x25, EffectSpawnPosition.Ring, EffectSpawnVelocity.Tangent, Measured);
        Row(0x26, EffectSpawnPosition.Ring, EffectSpawnVelocity.TangentCounterClockwise, Approx);
        Row(0x27, EffectSpawnPosition.Square, EffectSpawnVelocity.Random, Read);
        Row(0x28, EffectSpawnPosition.Square, EffectSpawnVelocity.Axis, Read);
        Row(0x29, EffectSpawnPosition.Box, EffectSpawnVelocity.Outward, Approx);
        Row(0x2A, EffectSpawnPosition.Box, EffectSpawnVelocity.Inward, Approx);
        Row(0x2B, EffectSpawnPosition.Box, EffectSpawnVelocity.EitherWay, Read);
        Row(0x2C, EffectSpawnPosition.Box, EffectSpawnVelocity.Random, Approx);
        Row(0x2D, EffectSpawnPosition.AxisSegment, EffectSpawnVelocity.Random, Measured);
        Row(0x2E, EffectSpawnPosition.AxisSegment, EffectSpawnVelocity.AxisEitherWay, Measured);
        Row(0x2F, EffectSpawnPosition.Disc, EffectSpawnVelocity.Cone, Measured);
        Row(0x30, EffectSpawnPosition.SphereSurface, EffectSpawnVelocity.Axis, Approx);
        Row(0x31, EffectSpawnPosition.CameraLine, EffectSpawnVelocity.Outward, Read);
        Row(0x32, EffectSpawnPosition.CameraLine, EffectSpawnVelocity.Inward, Approx);
        Row(0x33, EffectSpawnPosition.CameraLine, EffectSpawnVelocity.Random, Read);
        Row(0x34, EffectSpawnPosition.CameraLine, EffectSpawnVelocity.Outward, Approx);
        for (int s = 0x35; s <= 0x3C; s++)
            Row(s, EffectSpawnPosition.Point, s is 0x37 or 0x3B ? EffectSpawnVelocity.Ball : EffectSpawnVelocity.Random, Approx);
        return t;
    }

    /// <summary>The generator a style byte selects; values past 0x3C have no overlay (the game would load
    /// garbage) and fall back to style 0.</summary>
    public static EffectSpawnStyleInfo Describe(int style) => style >= 0 && style < Table.Length ? Table[style] : Table[0] with { Style = style, Evidence = "no overlay for this byte" };

    /// <summary>
    /// Tools of Destruction's 60 generators: its SPU job fxvis (EBOOT 0x1015A700, LS base 0x4000) calls, for every new
    /// particle, the function its style byte indexes in the table at LS 0x22170. Every one of them was run in spuemu
    /// (<c>Tools/Reverse/effects/tod_fxvis_harness.py compare</c>, 240 draws each, the direction table shuffled as the game
    /// does) next to QForce's overlay of the same index: 52 draw the same shape, PROVEN. The others differ and are drawn
    /// from ToD's own code: the cone styles 0x0E/0x13 send particles perpendicular to their position (QForce: flat and
    /// outward); 0x30-0x34 spread along the segment from a vector of the emitter state (its previous position, by how
    /// the spawn loop at 0x092F8 passes it - STRONG) to the emitter, then like 0x00 / 0x01 / random / random / the point -
    /// a still emitter (the editor's) collapses the segment, which is what is drawn; 0x37/0x3B keep a unit direction
    /// (QForce: a point of the ball). There is no generator past 0x3B.
    /// </summary>
    public static EffectSpawnStyleInfo Describe(int style, bool oldEngine)
    {
        if (!oldEngine) return Describe(style);
        return style >= 0 && style < OldTable.Length ? OldTable[style] : OldTable[0] with { Style = style, Evidence = "no generator for this byte (ToD has 60)" };
    }

    private static readonly EffectSpawnStyleInfo[] OldTable = BuildOldTable();

    private static EffectSpawnStyleInfo[] BuildOldTable()
    {
        const string same = "PROVEN (ToD fxvis generator run in spuemu: same shape as QForce's)";
        const string own = "PROVEN (ToD fxvis generator run in spuemu and read; differs from QForce)";
        const string moving = "PROVEN for a still emitter (ToD generator run); the segment to the previous position is STRONG";
        var t = new EffectSpawnStyleInfo[60];
        for (int s = 0; s < t.Length; s++) t[s] = Table[s] with { Evidence = same };
        void Row(int s, EffectSpawnPosition p, EffectSpawnVelocity v, string ev) => t[s] = new(s, p, v, ev);
        Row(0x0E, EffectSpawnPosition.ConeVolume, EffectSpawnVelocity.Perpendicular, own);
        Row(0x13, EffectSpawnPosition.ConeSurface, EffectSpawnVelocity.Perpendicular, own);
        Row(0x30, EffectSpawnPosition.SphereVolume, EffectSpawnVelocity.Outward, moving);
        Row(0x31, EffectSpawnPosition.SphereVolume, EffectSpawnVelocity.Inward, moving);
        Row(0x32, EffectSpawnPosition.SphereVolume, EffectSpawnVelocity.Random, moving);
        Row(0x33, EffectSpawnPosition.SphereVolume, EffectSpawnVelocity.Random, moving);
        Row(0x34, EffectSpawnPosition.Point, EffectSpawnVelocity.Random, moving);
        Row(0x37, EffectSpawnPosition.Point, EffectSpawnVelocity.Random, own);
        Row(0x3B, EffectSpawnPosition.Point, EffectSpawnVelocity.Random, own);
        return t;
    }

    /// <summary>
    /// A new particle's position and unit direction in the emitter frame (+Z = axis). <paramref name="radius"/>
    /// is the spawn radius, <paramref name="lane"/> the particle's slot in its batch of 8 (styles 0x19/0x1A
    /// multiply the radius of slots 4-7 by one more U - the retail overlay reuses the scaled radius of slots
    /// 0-3 instead of its second scale argument; kept because that is what the game draws).
    /// </summary>
    public static void Draw(int style, Random rng, float radius, float coneMinDegrees, float coneMaxDegrees, int lane,
        out Vector3 position, out Vector3 direction, bool oldEngine = false)
    {
        EffectSpawnStyleInfo info = Describe(style, oldEngine);
        // ToD draws one particle per call: no batch lanes.
        if (oldEngine) lane = 0;
        float U() => rng.NextSingle();
        Vector3 d, r = Vector3.Zero;
        float lo = coneMinDegrees * MathF.PI / 180f, hi = coneMaxDegrees * MathF.PI / 180f;
        switch (info.Position)
        {
            case EffectSpawnPosition.SphereVolume:
                d = UnitSphere(rng); position = d * radius * U(); break;
            case EffectSpawnPosition.SphereSurface:
            case EffectSpawnPosition.CameraLine:
                d = UnitSphere(rng); position = d * radius; break;
            case EffectSpawnPosition.ConeVolume:
                d = ConeDirection(rng, lo, hi); position = d * radius * U(); break;
            case EffectSpawnPosition.ConeSurface:
                d = ConeDirection(rng, lo, hi); position = d * radius; break;
            case EffectSpawnPosition.Disc:
            {
                r = RingDirection(rng);
                float scale = radius * U();
                if (style is 0x19 or 0x1A && lane >= 4) scale *= U();
                d = r; position = r * scale; break;
            }
            case EffectSpawnPosition.Ring:
                r = RingDirection(rng); d = r; position = r * radius; break;
            case EffectSpawnPosition.Square:
                position = new Vector3(2f * U() - 1f, 2f * U() - 1f, 0f) * radius;
                d = Normalized(position); r = d; break;
            case EffectSpawnPosition.Box:
                position = new Vector3(2f * U() - 1f, 2f * U() - 1f, 2f * U() - 1f) * radius;
                d = Normalized(position); break;
            case EffectSpawnPosition.AxisSegment:
                d = Vector3.UnitZ; position = d * radius * U(); break;
            default:
                d = UnitSphere(rng); position = Vector3.Zero; break;
        }

        switch (info.Velocity)
        {
            case EffectSpawnVelocity.Outward: direction = d; break;
            case EffectSpawnVelocity.Inward: direction = -d; break;
            case EffectSpawnVelocity.EitherWay: direction = U() < 0.5f ? -d : d; break;
            case EffectSpawnVelocity.Random: direction = UnitSphere(rng); break;
            case EffectSpawnVelocity.Perpendicular:
                direction = Normalized(Vector3.Cross(d, UnitSphere(rng))); break;
            case EffectSpawnVelocity.FlatOutward:
                direction = Normalized(new Vector3(d.X, d.Y, 0f)); break;
            case EffectSpawnVelocity.Tangent:
            case EffectSpawnVelocity.TangentCounterClockwise:
            {
                Vector3 tangent = Normalized(Vector3.Cross(Vector3.UnitZ, d));
                direction = info.Velocity == EffectSpawnVelocity.Tangent && U() < 0.5f ? -tangent : tangent;
                break;
            }
            case EffectSpawnVelocity.Axis: direction = Vector3.UnitZ; break;
            case EffectSpawnVelocity.AxisEitherWay: direction = U() < 0.5f ? -Vector3.UnitZ : Vector3.UnitZ; break;
            case EffectSpawnVelocity.TiltOutward:
            case EffectSpawnVelocity.TiltInward:
            case EffectSpawnVelocity.TiltSigned:
            {
                float tilt = info.Velocity == EffectSpawnVelocity.TiltSigned
                    ? -lo + (lo + hi) * U()
                    : lo + (hi - lo) * U();
                if (info.Velocity == EffectSpawnVelocity.TiltInward) tilt = -tilt;
                direction = Vector3.UnitZ * MathF.Cos(tilt) + r * MathF.Sin(tilt);
                break;
            }
            case EffectSpawnVelocity.Cone: direction = ConeDirection(rng, lo, hi); break;
            case EffectSpawnVelocity.Ball:
            {
                Vector3 v;
                do v = new Vector3(2f * U() - 1f, 2f * U() - 1f, 2f * U() - 1f); while (v.LengthSquared() > 1f);
                direction = v; break;
            }
            default: direction = d; break;
        }
    }

    /// <summary>Uniform unit vector (the retail table is a rotated Fibonacci sphere: same distribution).</summary>
    public static Vector3 UnitSphere(Random rng)
    {
        float z = 2f * rng.NextSingle() - 1f;
        float phi = rng.NextSingle() * MathF.Tau;
        float s = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3(s * MathF.Cos(phi), s * MathF.Sin(phi), z);
    }

    /// <summary>+Z tilted by an angle uniform in [lo, hi] radians (uniform in angle, not in solid angle - the
    /// overlays draw tilt = min + (max - min) U), at a uniform azimuth.</summary>
    public static Vector3 ConeDirection(Random rng, float lo, float hi)
    {
        float tilt = lo + (hi - lo) * rng.NextSingle();
        float phi = rng.NextSingle() * MathF.Tau;
        float s = MathF.Sin(tilt);
        return new Vector3(s * MathF.Cos(phi), s * MathF.Sin(phi), MathF.Cos(tilt));
    }

    private static Vector3 RingDirection(Random rng)
    {
        float phi = rng.NextSingle() * MathF.Tau;
        return new Vector3(MathF.Cos(phi), MathF.Sin(phi), 0f);
    }

    private static Vector3 Normalized(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.UnitX;
}
