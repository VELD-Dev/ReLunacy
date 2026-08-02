using System.Numerics;
using System.Runtime.InteropServices;

namespace ReLunacy.Engine.Rendering;

/// <summary>GPU-side layout for LightModelShaderSource's LightBuffer uniform — must match its
/// std140 layout exactly. Vector3 (12 bytes) followed by a scalar float packs into a 16-byte slot
/// under std140's own alignment rules (a vec3's base alignment is 16 bytes, and a directly
/// following 4-byte scalar fills the leftover space), which is also exactly how this sequential
/// C# struct lays out — Direction+Ambient, Color+padding and CameraPosition+padding each occupy
/// one 16-byte block, 48 bytes total. CameraPosition isn't really "light" data, but it's needed
/// for specular's view-direction term and this is already the one scene-wide per-frame buffer
/// every lit material binds — see DecalAwareForwardRenderer.Draw. SpecularPower is scene-wide
/// (not per-material) because this game's texture format has no per-pixel specular-power channel
/// to sample — see LitModelShaderSource's header comment.</summary>
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
    /// not a game value — the game needs no such transform because UFragVertex.UVs2 are already
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
    private readonly float _padding2;

    /// <summary>Pivot the lightmap UV rotation turns about, in UV space. Adjustable rather than
    /// fixed at the atlas centre on purpose: these are ATLAS coordinates, so rotating about (0.5,
    /// 0.5) would sweep an island off across unrelated islands and gutters instead of spinning it
    /// in place. Set it to the island's own centre (the UFrag Inspector has a button) to test
    /// whether a bake is stored rotated.</summary>
    public Vector2 LightmapUVPivot;
    /// <summary>Lightmap UV rotation in DEGREES, about LightmapUVPivot. Applied before scale and
    /// offset. Research control — the game has no such rotation.</summary>
    public float LightmapUVRotation;
    private readonly float _padding3;
}
