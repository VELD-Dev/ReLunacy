using System.Numerics;
using System.Runtime.InteropServices;

namespace ReLunacy.Engine.Rendering;

/// <summary>GPU-side layout for LightModelShaderSource's LightBuffer uniform - must match its
/// std140 layout exactly. Each Vector3+float pair packs into one 16-byte block.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct LightData
{
    public Vector3 Direction;
    public float Ambient;
    public Vector3 Color;
    public float SpecularPower;
    public Vector3 CameraPosition;
    /// <summary>&gt;0.5 renders the raw cubemap reflection on every surface, ungated by specular
    /// intensity.</summary>
    public float ReflectionDebugView;
    /// <summary>Flat stand-in for the environment cubemap, averaged from the level's own.</summary>
    public Vector3 EnvironmentColour;
    public float EnvironmentIntensity;

    /// <summary>Live lightmap UV transform: uv = fTexCoords2 * Scale + Offset. Research control, not
    /// a game value. Identity is Scale=(1,1), Offset=(0,0).</summary>
    public Vector2 LightmapUVScale;
    public Vector2 LightmapUVOffset;

    /// <summary>Stand-in for the game's per-draw lightScale vertex constant, which scales the baked
    /// light colour on read.</summary>
    public float BakedLightScale;
    /// <summary>Scales the normal-map derivatives feeding the baked N.L. 1 = full normal influence,
    /// 0 = flat (pure bake).</summary>
    public float BakedBumpFade;
    /// <summary>&gt;0.5 renders the raw baked light colour instead of shading.</summary>
    public float BakedDebugView;
    /// <summary>Head-on reflectance (Fresnel F0) for the cubemap reflection: 0 = reflect only where
    /// the material's specular map says to, rising to 1 = near-mirror everywhere. Grazing angles
    /// always reflect fully regardless.</summary>
    public float ReflectionBase;

    /// <summary>Pivot the lightmap UV rotation turns about, in UV space (atlas coordinates, so not
    /// fixed at 0.5,0.5).</summary>
    public Vector2 LightmapUVPivot;
    /// <summary>Lightmap UV rotation in degrees, about LightmapUVPivot. Applied before scale and
    /// offset. Research control - the game has no such rotation.</summary>
    public float LightmapUVRotation;
    /// <summary>How much of the undecoded-surface ambient fill is added under a baked surface, 0..1.
    /// Research control that puts a floor under the bake so a hard-clamped N.L doesn't go pure
    /// black where the normal tilts past the baked light direction.</summary>
    public float BakedAmbient;

    // The level's analytic lighting environment: two directional lights (Direction0/1 + Colour1/2)
    // plus an ambient (Colour0). EnvHasLighting is >0.5 only when the level supplied one.
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
