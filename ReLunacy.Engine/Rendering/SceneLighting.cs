using System.Numerics;
using NeoVeldrid;

namespace ReLunacy.Engine.Rendering;

/// <summary>The scene's lighting state, and the <see cref="LightData"/> consumed by VulkanRenderer.
/// Old-engine proof-bounded inputs stay separate from explicitly-labelled research previews.</summary>
public sealed class SceneLighting
{
    public Vector3 LightDirection = new(-0.4f, -0.8f, 0.3f);
    public Vector3 LightColor = Vector3.One;
    public float Ambient = 0.15f;
    public float SpecularPower = 32f;

    public Vector3 EnvironmentColour = Vector3.One;
    public float EnvironmentIntensity;
    public bool ReflectionDebugView;
    public float ReflectionBase;

    public bool HasLightingEnvironment;
    public bool OldEngineMode;
    public Vector3 EnvDirection0 = Vector3.UnitY;
    public Vector3 EnvDirection1 = Vector3.UnitY;
    public Vector3 EnvAmbient;
    public Vector3 EnvLight0Colour;
    public Vector3 EnvLight1Colour;

    // Non-old/editor controls retained for the generic renderer path. Old-engine fidelity does not
    // use these as feature switches: environment, per-Moby spatial light and emissive are automatic,
    // while baked Final stays withheld until callback52.arg2.low8 routing is proven.
    public bool BakedLightingEnabled = true;
    public bool MobySpatialLightingEnabled = true;

    // Per-level source state belongs to the SceneLighting instance. Keeping authored fog here avoids
    // leaking one level's 0x7100 record through OldEngineRenderOptions when switching engines/levels.
    public ReLunacy.Engine.Assets.Lighting.LevelFog? LevelFog;

    public TextureView? EnvironmentCubemap;

    public Vector2 LightmapUVScale = Vector2.One;
    public Vector2 LightmapUVOffset = Vector2.Zero;
    // Generic/editor tuning values. Old-engine Final does not use BakedLightScale as a substitute for
    // an unknown retail LM2 permutation. While that route is unresolved, the old-engine shader uses
    // the original ReLunacy directional-lightmap reconstruction strictly as a presentation fallback.
    public float BakedLightScale = 4f;
    public float BakedBumpFade = 1f;
    // Keep the same low floor as VELD's new-renderer. It prevents the unresolved presentation fallback
    // from crushing perturbed baked normals to absolute black; it is not asserted as a retail constant.
    public float BakedAmbient = 0.15f;
    public bool BakedDebugView;
    public Vector2 LightmapUVPivot = new(0.5f, 0.5f);
    public float LightmapUVRotation;

    public LightData BuildLightData(Vector3 cameraPosition)
    {
        bool old = OldEngineMode;
        bool reflections = !old || OldEngineRenderOptions.ReflectionsEnabled;

        var fog = old ? LevelFog : null;
        bool fogEnabled = old
            && OldEngineRenderOptions.FogEnabled
            && fog is { HasValidLinearRange: true }
            && fog.Amount > 0f;

        // Old engine: the retail TIE cube reflection at the programs' own gain (1). Closed 09-10-2026: the
        // cube's alpha is its exponent as the programs read it, and the distance fade c[16] that kept far
        // TIEs from reflecting is per TieFrag (Docs/reverse/TOD_RENDER_POST_2026-10-09.md §6). Before it,
        // reflections glowed at every distance and were held back to a 0.12 A/B gain.
        float environmentIntensity = old
            ? (reflections ? 1f : 0f)
            : EnvironmentIntensity;

        return new LightData
        {
            Direction = LightDirection.LengthSquared() > 0f ? Vector3.Normalize(LightDirection) : Vector3.UnitY,
            Ambient = Ambient,
            Color = LightColor,
            SpecularPower = MathF.Max(SpecularPower, 1f),
            CameraPosition = cameraPosition,
            ReflectionDebugView = ReflectionDebugView ? 1f : 0f,
            EnvironmentColour = EnvironmentColour,
            EnvironmentIntensity = environmentIntensity,

            LightmapUVScale = LightmapUVScale,
            LightmapUVOffset = LightmapUVOffset,
            BakedLightScale = BakedLightScale,
            BakedBumpFade = BakedBumpFade,
            BakedDebugView = BakedDebugView ? 1f : 0f,
            ReflectionBase = ReflectionBase,
            LightmapUVPivot = LightmapUVPivot,
            LightmapUVRotation = LightmapUVRotation,
            BakedAmbient = BakedAmbient,

            EnvDirection0 = EnvDirection0,
            // Fidelity path: 0x8B00 drives lighting whenever it exists. Turning a proven source off
            // produced the misleading near-black editor fallback the old Render menu exposed.
            EnvHasLighting = HasLightingEnvironment ? 1f : 0f,
            EnvDirection1 = EnvDirection1,
            OldEngineMode = old ? 1f : 0f,
            EnvAmbient = EnvAmbient,
            // The actual per-draw validity bit is VkInstanceLight.Ambient.w. Keep this std140 slot
            // deterministic for ABI compatibility; old-engine shaders do not treat it as a UI gate.
            MobySpatialLightingEnabled = old ? 1f : (MobySpatialLightingEnabled ? 1f : 0f),
            EnvLight0Colour = EnvLight0Colour,
            // Old-engine Final is intentionally unavailable until the callback52 shellFeatureKey is
            // proven. The shader may still use a clearly-labelled presentation fallback for LM1/LM2.
            BakedLightingEnabled = old ? 0f : (BakedLightingEnabled ? 1f : 0f),
            EnvLight1Colour = EnvLight1Colour,
            // Material emissive is source-driven fidelity, not a graphics option.
            MaterialEmissiveEnabled = 1f,

            // PROVEN: colour, LinearScale, LinearBias, Amount come from 0x7100 / 0x572774.
            // PROVEN since 2026-09-16: coordinate = clip-space w (retail TIE/Moby VPs). The shaders use it
            // only when this explicit gate is enabled.
            FogColour = fog?.Colour ?? Vector3.Zero,
            FogScale = fog?.LinearScale ?? 0f,
            FogBias = fog?.LinearBias ?? 0f,
            FogAmount = fogEnabled ? MathF.Max(fog!.Amount, 0f) : 0f,
            FogEnabled = fogEnabled ? 1f : 0f,
            FogDebugView = 0f,
        };
    }
}

