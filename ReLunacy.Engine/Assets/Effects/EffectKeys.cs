// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using System.Numerics;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>
/// One key of an effect curve. Every key in effect.dat has the same shape: a MIN value, a MAX value,
/// a normalised TIME and a random-MODE byte - PROVEN from the EBOOT's load-time key preparation
/// (QForce 0x007D8800 / 0x007D89A0 / 0x007DC7E8 / 0x007DB228):
/// <list type="bullet">
///   <item>the mode byte is clamped to 0..6 (<c>mode &gt;= 7 -&gt; 0</c>);</item>
///   <item>when MIN == MAX the mode is cleared, and when mode != 0 and MIN &gt; MAX they are swapped,
///   so a non-zero mode means "pick between MIN and MAX";</item>
///   <item>scale keys are multiplied by 0.5 (a diameter becomes a half-size), rotation keys by
///   pi/180 (degrees to radians), alpha keys are quantised to 1/255.</item>
/// </list>
/// What each non-zero mode does (per particle, per frame...) is INFERRED: glow quads that visibly flicker
/// carry mode 4, spawn-time spreads carry 1. The editor treats 4 and 5 as "re-rolled every frame" and
/// the rest as "rolled once per particle/instance".
/// </summary>
public readonly record struct EffectKey(float Min, float Max, float Extra, float Time, byte Mode)
{
    public bool IsRandom => Mode != 0 && Min != Max;
    public bool RerollsEveryFrame => Mode is 4 or 5;
    public float Sample(float random01) => IsRandom ? Min + (Max - Min) * random01 : Min;
}

/// <summary>40-byte colour key: RGBA MIN at +0x00, RGBA MAX at +0x10, time at +0x20, mode byte at +0x24.
/// Structure PROVEN (EBOOT 0x007D89A0 clamps each channel, reads the mode at +0x24); the RGBA order is
/// STRONG (glows read (0, 0.5, 1) = blue on effects whose light twin is blue).</summary>
public readonly record struct EffectColorKey(Vector4 Min, Vector4 Max, float Time, byte Mode)
{
    public bool IsRandom => Mode != 0 && Min != Max;
    public bool RerollsEveryFrame => Mode is 4 or 5;
    public Vector4 Sample(float random01) => IsRandom ? Vector4.Lerp(Min, Max, random01) : Min;
}

/// <summary>What a track is keyed against.</summary>
public enum EffectTrackDomain
{
    /// <summary>Normalised age of one particle (0 at spawn, 1 at death).</summary>
    ParticleLife,
    /// <summary>Normalised age of the piece that owns the track (emitter, quad, light, package).</summary>
    PieceLife,
}

/// <summary>
/// A keyed curve: the <c>Fx*Keys</c> structs of the EBOOT's class list (FxColorKeys, FxAlphaKeys,
/// FxScaleKeys...). In the file each is a small header inside the owning record -
/// <c>{ key* ptr; u32 word1; u8 count; ... }</c> - whose pointer targets an anonymous chunk that
/// starts with its own u32 key count. The runtime takes <c>min(chunk count, 32)</c> as the count and
/// stores it in the header's count byte (PROVEN, 0x007D8800); <see cref="Keys"/> is capped the same way.
/// </summary>
public sealed class EffectScalarTrack
{
    public required string Name { get; init; }
    /// <summary>Offset of the track header inside its owner record.</summary>
    public required int FieldOffset { get; init; }
    public required EffectKey[] Keys { get; init; }
    /// <summary>Second word of the header. A float on quad/light tracks (0, 0.25, 1, 2.85...: loop period in
    /// seconds, INFERRED - the runtime forces a single key when both it and the owner's life are 0);
    /// small integers on the emitter's alpha track. Kept raw.</summary>
    public uint Word1 { get; init; }
    public EffectTrackDomain Domain { get; init; }
    /// <summary>File offset of the key array, 0 when the pointer is null.</summary>
    public int KeysOffset { get; init; }
    public int KeySize { get; init; }

    public float Word1AsFloat => BitConverter.UInt32BitsToSingle(Word1);
    public bool IsConstant => Keys.Length <= 1 && (Keys.Length == 0 || !Keys[0].IsRandom);

    /// <summary>Linear interpolation between keys by time, each key sampled with <paramref name="random01"/>.</summary>
    public float Evaluate(float t, float random01, float fallback = 0f)
    {
        if (Keys.Length == 0) return fallback;
        if (Keys.Length == 1 || t <= Keys[0].Time) return Keys[0].Sample(random01);
        for (int i = 1; i < Keys.Length; i++)
        {
            if (t <= Keys[i].Time)
            {
                EffectKey a = Keys[i - 1], b = Keys[i];
                float span = b.Time - a.Time;
                float f = span > 1e-6f ? (t - a.Time) / span : 1f;
                return a.Sample(random01) + (b.Sample(random01) - a.Sample(random01)) * f;
            }
        }
        return Keys[^1].Sample(random01);
    }

    public (float Min, float Max) Range()
    {
        if (Keys.Length == 0) return (0, 0);
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (EffectKey k in Keys)
        {
            lo = MathF.Min(lo, MathF.Min(k.Min, k.IsRandom ? k.Max : k.Min));
            hi = MathF.Max(hi, MathF.Max(k.Min, k.IsRandom ? k.Max : k.Min));
        }
        return (lo, hi);
    }
}

/// <summary>Colour counterpart of <see cref="EffectScalarTrack"/> (FxColorKeys: 40-byte keys).</summary>
public sealed class EffectColorTrack
{
    public required string Name { get; init; }
    public required int FieldOffset { get; init; }
    public required EffectColorKey[] Keys { get; init; }
    public uint Word1 { get; init; }
    public EffectTrackDomain Domain { get; init; }
    public int KeysOffset { get; init; }

    /// <summary>A copy whose keys' alphas are multiplied by <paramref name="scale"/>.</summary>
    public EffectColorTrack ScaledAlpha(float scale) => new()
    {
        Name = Name,
        FieldOffset = FieldOffset,
        Keys = [.. Keys.Select(k => k with { Min = k.Min with { W = k.Min.W * scale }, Max = k.Max with { W = k.Max.W * scale } })],
        Word1 = Word1,
        Domain = Domain,
        KeysOffset = KeysOffset,
    };

    public Vector4 Evaluate(float t, float random01) => Evaluate(t, random01, Vector4.One);

    public Vector4 Evaluate(float t, float random01, Vector4 fallback)
    {
        if (Keys.Length == 0) return fallback;
        if (Keys.Length == 1 || t <= Keys[0].Time) return Keys[0].Sample(random01);
        for (int i = 1; i < Keys.Length; i++)
        {
            if (t <= Keys[i].Time)
            {
                EffectColorKey a = Keys[i - 1], b = Keys[i];
                float span = b.Time - a.Time;
                float f = span > 1e-6f ? (t - a.Time) / span : 1f;
                return Vector4.Lerp(a.Sample(random01), b.Sample(random01), f);
            }
        }
        return Keys[^1].Sample(random01);
    }
}
