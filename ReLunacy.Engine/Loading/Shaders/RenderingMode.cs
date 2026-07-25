namespace ReLunacy.Engine.Loading.Shaders;

// Full byte->mode mapping, as reported by the user after independent investigation (all 9 values,
// superseding every earlier guess in this file's history — notably that 0x01 was "Decal": it's
// actually a general-purpose Overlay blend mode, decals are just one of the things it's used for,
// which is exactly why some 0x01-shaded meshes never needed the Z-fight vertex offset this project
// tried and later retracted entirely (the game doesn't decal-offset vertices at all — see
// ShaderMetadata's now-unknown 0x48 field) — they were never decals to begin with, just unrelated
// overlay-blended surfaces. 0x05/0x06 are NOT "with/without backface culling" as previously
// guessed either; they're distinct named modes (Soft-Edge vs. Blended). 0x06/0x07 were originally
// reported as one combined "Blended Baked Only" mode — corrected to two distinct modes, Blended
// (0x06) and Baked Only (0x07), which pushed the original 0x07 Lit Only up to 0x08.
//
// Only Opaque/Cutout have a well-understood render treatment right now. The exact blend/lighting
// behavior for Scunge, Soft-Edge, Blended, Baked Only, and Lit Only isn't independently confirmed
// against this engine's rendering — see MaterialReader.ToRenderMode for current (conservative)
// mapping choices and where that's still a guess.
public enum RenderingMode : byte
{
    Opaque = 0x00,
    Overlay = 0x01,
    Additive = 0x02,
    Scunge = 0x03,
    Cutout = 0x04,
    SoftEdge = 0x05,
    Blended = 0x06,
    BakedOnly = 0x07,
    LitOnly = 0x08,
}
