using System.Numerics;
using System.Runtime.InteropServices;

namespace ReLunacy.Engine.Rendering;

/// <summary>GPU-side layout for LightModelShaderSource's LightBuffer uniform - must match its
/// std140 layout exactly. Vector3 (12 bytes) followed by a scalar float packs into a 16-byte slot
/// under std140's own alignment rules (a vec3's base alignment is 16 bytes, and a directly
/// following 4-byte scalar fills the leftover space), which is also exactly how this sequential
/// C# struct lays out - Direction+Ambient, Color+padding and CameraPosition+padding each occupy
/// one 16-byte block, 48 bytes total. CameraPosition isn't really "light" data, but it's needed
/// for specular's view-direction term and this is already the one scene-wide per-frame buffer
/// every lit material binds - see DecalAwareForwardRenderer.Draw. SpecularPower is scene-wide
/// (not per-material) because this game's texture format has no per-pixel specular-power channel
/// to sample - see LitModelShaderSource's header comment.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct LightData
{
    public Vector3 Direction;
    public float Ambient;
    public Vector3 Color;
    public float SpecularPower;
    public Vector3 CameraPosition;
    /// <summary>&gt;0.5 renders the raw cubemap reflection (envColour) on every surface, ungated by
    /// specIntensity/EnvironmentIntensity, so the near-invisible reflection can be seen and its axis
    /// orientation checked. Occupies uCameraPosition's std140 tail slot (was a reserved pad).</summary>
    public float ReflectionDebugView;
    /// <summary>Flat stand-in for the environment cubemap, averaged from the level's own (see
    /// TextureShaderLoader.EnvironmentAverage). The game's specular term is additive and
    /// independent of the lightmap, so it is what keeps baked shadows from reaching pure black;
    /// with metropolis's cubemap being a near-uniform grey, one colour captures nearly all of it.
    /// Same 16-byte std140 block rule as the vec3+float pairs above.</summary>
    public Vector3 EnvironmentColour;
    public float EnvironmentIntensity;

    /// <summary>Live lightmap UV transform: uv = fTexCoords2 * Scale + Offset. A RESEARCH CONTROL,
    /// not a game value - the game needs no such transform because UFragVertex.UVs2 are already
    /// atlas coordinates. It exists so a suspected missing scalar/offset can be searched for by eye
    /// against the real game. Identity is Scale=(1,1), Offset=(0,0).
    /// Same 16-byte std140 block rule as the vec3+float pairs above (vec2+vec2 = one block).</summary>
    public Vector2 LightmapUVScale;
    public Vector2 LightmapUVOffset;

    /// <summary>Stand-in for the game's per-draw lightScale vertex constant (vc[2].z), which scales
    /// the baked light colour on read and is unsourced.</summary>
    public float BakedLightScale;
    /// <summary>Scales the normal-map derivatives feeding the baked N.L, standing in for the game's
    /// vViewTS.w bump fade. 1 = full normal influence, 0 = flat (pure bake).</summary>
    public float BakedBumpFade;
    /// <summary>&gt;0.5 renders the raw baked light colour instead of shading, so "why is this
    /// black" splits into bake-is-black vs shading-kills-it at a glance.</summary>
    public float BakedDebugView;
    /// <summary>Head-on reflectance (Fresnel F0) for the cubemap reflection: 0 = reflect only where
    /// the material's specular map says to (the faithful default), rising to 1 = near-mirror on every
    /// surface. Grazing angles always reflect fully regardless. See LitModelShaderSource's ENVIRONMENT
    /// FILL. Occupies the std140 slot after uBakedDebugView (was a reserved pad).</summary>
    public float ReflectionBase;

    /// <summary>Pivot the lightmap UV rotation turns about, in UV space. Adjustable rather than
    /// fixed at the atlas centre on purpose: these are ATLAS coordinates, so rotating about (0.5,
    /// 0.5) would sweep an island off across unrelated islands and gutters instead of spinning it
    /// in place. Set it to the island's own centre (the UFrag Inspector has a button) to test
    /// whether a bake is stored rotated.</summary>
    public Vector2 LightmapUVPivot;
    /// <summary>Lightmap UV rotation in DEGREES, about LightmapUVPivot. Applied before scale and
    /// offset. Research control - the game has no such rotation.</summary>
    public float LightmapUVRotation;
    /// <summary>How much of the undecoded-surface fill (the level's analytic ambient, or the flat
    /// editor ambient) is added UNDER a baked surface, 0..1.
    ///
    /// A bake stores total incident irradiance plus a dominant direction, so reconstructing it with a
    /// hard clamped N.L is wrong at the bottom end: wherever the parallax-perturbed normal tilts past
    /// the baked light direction, N.L hits exactly 0 and the surface goes pure black, with nothing to
    /// catch it. The real engine has the cubemap reflection sitting under it, but that term is gated on
    /// specular intensity, so it vanishes on matte surfaces and the black comes back.
    ///
    /// Research control: the game has no such constant. It exists to put a floor under the bake.</summary>
    public float BakedAmbient;

    // The level's analytic lighting environment (main.dat section 0x8b00), the game's own sun/ambient
    // - see Assets.Lighting.LightingEnvironment and the shader's undecoded-surface lighting. Two
    // directional lights (Direction0/1 + Colour1/2) plus an ambient (Colour0). EnvHasLighting is >0.5
    // only when the level actually supplied one; otherwise the shader falls back to the flat editor
    // ambient. Each Vector3+float pair is one std140 16-byte block, same rule as the pairs above.
    public Vector3 EnvDirection0;
    public float EnvHasLighting;
    public Vector3 EnvDirection1;
    private readonly float _padding4;
    public Vector3 EnvAmbient;   // Colour0
    private readonly float _padding5;
    public Vector3 EnvLight0Colour;   // Colour1
    private readonly float _padding6;
    public Vector3 EnvLight1Colour;   // Colour2
    private readonly float _padding7;
}
