namespace ReLunacy.Engine.Loading.Shaders;

// Confirmed against real level data via the Shader Browser (see MaterialReader's unrecognized-
// value logging): 0x05 and 0x06 are both alpha blend, differing only in backface culling — 0x06
// culls, 0x05 doesn't. That rules out the earlier guess that this byte was three independent
// bitflags (0x05/0x06 differ in two bit positions, not one), so treat every value here as a
// distinct named mode, not a combination — don't try to derive unknowns from bit math.
//
// 0x01 confirmed (metropolis, shader 0x2E): used on decal-like mesh parts that sit flush against
// (and Z-fight with) the surface underneath them — the albedo's own alpha is real and matches the
// in-game look, so this is a genuine alpha-blend variant, not an opaque/cutout one. Mapped to the
// same RenderMode.AlphaBlend as AlphaBlend/AlphaBlendNoCull below, which — via
// DecalAwareForwardRenderer — already renders every AlphaBlend material with depth test on but
// depth WRITE off. That's exactly the standard fix for this symptom (a decal writing its own
// depth over near-coincident geometry it's blending onto causes per-pixel depth-test flicker,
// which looks like hard clipping instead of a soft fade), so no separate handling should be
// needed here — if it turns out incomplete, the next thing to try is an actual depth bias/polygon
// offset instead of a blanket "no depth write", not a re-guess at what this byte value means.
//
// 0x02 has been observed in real files but its meaning isn't confirmed yet; 0x03 hasn't been
// observed at all. Leave them unnamed (MaterialReader.ToRenderMode logs the raw byte for anything
// not listed here) rather than guessing.
public enum RenderingMode : byte
{
    Opaque = 0x00,
    Decal = 0x01,
    AlphaClip = 0x04,
    AlphaBlendNoCull = 0x05,
    AlphaBlend = 0x06
}
