using System.Numerics;

namespace ReLunacy.Engine.Assets.Lighting;

/// <summary>
/// Native old-engine level fog configuration loaded from main.dat section 0x7100.
///
/// The retail dispatcher at 0x4E2948 routes section 0x7100 to 0x4E2E00. That consumer sends
/// +0x40/+0x44/+0x48 to the fog-colour setter at 0x572758 and +0x4C/+0x50/+0x54 to 0x572774.
/// The latter validates near &lt; far and precomputes:
///
///   LinearScale = Amount / (FarDistance - NearDistance)
///   LinearBias  = -NearDistance * LinearScale
///
/// This closes the authored data and CPU setup. It does NOT by itself prove which ToD vertex-shader
/// value reaches the RSX FOGC interpolator, so the renderer must not silently substitute Euclidean
/// distance or view-Z until that producer is closed.
/// </summary>
public sealed class LevelFog
{
    public const uint SectionId = 0x7100;
    public const uint ConfirmedOldStride = 0xF0;

    public Vector3 Colour { get; init; }
    public float NearDistance { get; init; }
    public float FarDistance { get; init; }
    public float Amount { get; init; }
    public float LinearScale { get; init; }
    public float LinearBias { get; init; }

    /// <summary>Absolute source offset of the 0x7100 record inside main.dat.</summary>
    public uint SourceOffset { get; init; }

    /// <summary>
    /// The level's bloom, +0x70..+0x8C of the same record (PROVEN, Docs/reverse/TOD_RENDER_POST_2026-10-09.md):
    /// the 0x7100 consumer 0x004E2E00 copies +0x70 (on), +0x74 (mip weight), +0x78 (history), +0x80
    /// (extract scale), +0x84 (extract exponent), +0x88 (intensity), +0x8C (colour extract) to the render
    /// globals 0x10676580 +0x6B..+0x84, the frame's post settings (0x004C5C3C) hand them to the post
    /// function 0x006B8E2C, and its bloom 0x004F2E14 patches them into its shaders - the values the
    /// RPCS3 cache holds (Metropolis (0.6, 2.2, 0.6), Meridian City (0.85, 1.8, 0.8)). On in all 24 levels.
    /// Null when +0x70 is 0 or the record is shorter.
    /// </summary>
    public BloomParameters? Bloom { get; init; }

    /// <summary>Exact source record retained for cross-level reverse work and future lossless edit
    /// support. No unknown bytes are zeroed or repurposed.</summary>
    public byte[] RawRecord { get; init; } = [];

    public bool HasValidLinearRange =>
        float.IsFinite(NearDistance) && float.IsFinite(FarDistance) &&
        float.IsFinite(Amount) && FarDistance > NearDistance;

    /// <summary>
    /// Diagnostic evaluation of the proven CPU coefficients for a caller-supplied scalar. This is
    /// intentionally not named EvaluateWorldDistance: the exact ToD FOGC producer is still OPEN.
    /// </summary>
    public float EvaluateCoefficientPreview(float coordinate)
        => Math.Clamp(coordinate * LinearScale + LinearBias, 0f, MathF.Max(Amount, 0f));
}
