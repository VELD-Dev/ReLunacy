using System.Numerics;
using NeoVeldrid;

namespace ReLunacy.Engine.Rendering;

/// <summary>The scene's lighting state, and the <see cref="LightData"/> the shaders consume.
///
/// This used to live on DecalAwareForwardRenderer, which meant the raw-Vulkan renderer could only get
/// at the level's lighting by going through a Bliss renderer it otherwise no longer uses. It is plain
/// state with one pure builder, so it belongs on its own: only <see cref="EnvironmentCubemap"/> touches
/// the graphics API at all, and that is a NeoVeldrid type, not a Bliss one.
///
/// Values default to a plain downward light; the view pushes the real EditorSettings values every
/// frame, same pattern as Camera.FarPlane / VolumeWireThickness.</summary>
public sealed class SceneLighting
{
    public Vector3 LightDirection = new(-0.4f, -0.8f, 0.3f);
    public Vector3 LightColor = Vector3.One;
    public float Ambient = 0.15f;
    public float SpecularPower = 32f;

    /// <summary>Averaged from the level's own cubemap at load; see LightData.EnvironmentColour.
    /// Intensity defaults to 0 so nothing changes until a level actually supplies one.</summary>
    public Vector3 EnvironmentColour = Vector3.One;
    public float EnvironmentIntensity;

    /// <summary>Debug: draw the raw cubemap reflection on everything (see LightData.ReflectionDebugView).</summary>
    public bool ReflectionDebugView;

    /// <summary>Fresnel F0 for the cubemap reflection (see LightData.ReflectionBase). 0 = specular-map-gated.</summary>
    public float ReflectionBase;

    /// <summary>The level's analytic lighting environment (section 0x8b00), pushed from
    /// LevelData.LightingEnvironment. Lights undecoded (non-baked) surfaces with the game's own
    /// sun/ambient. <see cref="HasLightingEnvironment"/> stays false for levels without one.</summary>
    public bool HasLightingEnvironment;
    public Vector3 EnvDirection0 = Vector3.UnitY;
    public Vector3 EnvDirection1 = Vector3.UnitY;
    public Vector3 EnvAmbient;
    public Vector3 EnvLight0Colour;
    public Vector3 EnvLight1Colour;

    /// <summary>The level's environment cubemap (AssetManager.EnvironmentCubemapView), sampled for
    /// reflections. Scene-wide; the view pushes it each frame like EnvironmentColour.</summary>
    public TextureView? EnvironmentCubemap;

    // Live lightmap research controls - see LightData for what each one stands in for.
    public Vector2 LightmapUVScale = Vector2.One;
    public Vector2 LightmapUVOffset = Vector2.Zero;
    public float BakedLightScale = 4f;
    public float BakedBumpFade = 1f;
    /// <summary>Fraction of the ambient fill kept under a baked surface (see LightData.BakedAmbient).
    /// Defaults low but non-zero: enough to keep parallax crevices off pure black without washing out
    /// the bake's own shadows.</summary>
    public float BakedAmbient = 0.15f;
    public bool BakedDebugView;
    public Vector2 LightmapUVPivot = new(0.5f, 0.5f);
    public float LightmapUVRotation;

    public LightData BuildLightData(Vector3 cameraPosition) => new()
    {
        Direction = LightDirection.LengthSquared() > 0f ? Vector3.Normalize(LightDirection) : Vector3.UnitY,
        Ambient = Ambient,
        Color = LightColor,
        SpecularPower = MathF.Max(SpecularPower, 1f),
        CameraPosition = cameraPosition,
        ReflectionDebugView = ReflectionDebugView ? 1f : 0f,
        ReflectionBase = ReflectionBase,
        EnvironmentColour = EnvironmentColour,
        EnvironmentIntensity = EnvironmentIntensity,
        LightmapUVScale = LightmapUVScale,
        LightmapUVOffset = LightmapUVOffset,
        BakedLightScale = BakedLightScale,
        BakedBumpFade = BakedBumpFade,
        BakedAmbient = BakedAmbient,
        BakedDebugView = BakedDebugView ? 1f : 0f,
        LightmapUVPivot = LightmapUVPivot,
        LightmapUVRotation = LightmapUVRotation,
        EnvHasLighting = HasLightingEnvironment ? 1f : 0f,
        EnvDirection0 = EnvDirection0,
        EnvDirection1 = EnvDirection1,
        EnvAmbient = EnvAmbient,
        EnvLight0Colour = EnvLight0Colour,
        EnvLight1Colour = EnvLight1Colour,
    };
}
