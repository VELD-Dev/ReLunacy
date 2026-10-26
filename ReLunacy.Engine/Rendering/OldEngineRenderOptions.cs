using ReLunacy.Engine.Assets.Lighting;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Live proof-oriented controls for the Tools of Destruction old-engine renderer.
///
/// Proven source-driven lighting is not optional presentation state: native environment lighting,
/// per-Moby spatial lighting and material emissive are consumed automatically when their source data
/// exists. The legacy booleans below are kept only so older editor/settings code can load without a
/// compatibility break; the fidelity renderer deliberately does not consume them.
///
/// The fog is retail and on by default (closed 09-10-2026, Docs/reverse/TOD_RENDER_POST_2026-10-09.md §6).
/// The TIE cube reflections are retail too since c[16] was closed: on by default.
/// </summary>
public static class OldEngineRenderOptions
{
    // Legacy compatibility fields. Do not use these to gate old-engine fidelity.
    public static bool BakedLightingEnabled;
    /// <summary>
    /// Per-Moby sampling of the 0x8C00/0x8D00 volumes. Default OFF: a runtime probe of the retail volume
    /// selector (0x005510AC, the entry every spatial evaluator calls) saw ~4 queries/frame in a live level,
    /// all from a lattice scan, a once-per-frame hero query and FX particles - never one per Moby - and the
    /// only evaluator that takes a normal (0x00551390) has no live caller. Retail Mobys therefore appear to use
    /// the global 0x8B00 rig. See Tools/Reverse/evidence/TOD_Spatial_Lighting_Queries_2026-09-18.md. The A/B
    /// toggle stays in the Render menu.
    /// </summary>
    public static bool MobySpatialLightingEnabled;
    public static bool EnvironmentLightingEnabled = true;
    public static bool MaterialEmissiveEnabled = true;

    /// <summary>
    /// The TIE programs' cube reflection (keys with feature bit 0x04): env x 2^((a x 255 - 80) / 24.26) x
    /// mix(material +0x40, albedo, +0x4C) x props.r x distance fade. On by default; Render > Reflections
    /// only hides it.
    /// </summary>
    public static bool ReflectionsEnabled = true;

    public static bool NativeTextureSamplersEnabled = true;

    /// <summary>
    /// The level's fog, as the game draws it: main.dat 0x7100 colour / near / far / amount, set by
    /// 0x00572758 / 0x00572774 into the render globals 0x10676580 (+0x30 colour, +0x20 scale = amount /
    /// (far - near), +0x24 bias = -near x scale, +0x2C amount), uploaded every frame as VP c[5] by
    /// 0x004F59F4, and applied by the TIE and Moby programs as saturate(min(clip w x c5.x + c5.y, c5.w)).
    /// On by default; the Render > Fog toggle only hides it.
    /// </summary>
    public static bool FogEnabled = true;

    /// <summary>Current level's proven 0x7100 record. Kept separate from the research coordinate.</summary>
    public static LevelFog? LevelFog;
}

