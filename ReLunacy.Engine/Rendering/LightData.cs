using System.Numerics;
using System.Runtime.InteropServices;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// GPU-side std140 layout consumed by VulkanRenderer. Every Vector3 + scalar pair deliberately
/// occupies one 16-byte slot. Keep this structure byte-for-byte aligned with the GLSL LightBuffer.
///
/// The four old-engine option scalars that sit beside the 0x8B00 vectors are retained in otherwise
/// spare std140 words. The current renderer may ignore some of them, but keeping them explicit makes
/// their ownership unambiguous and avoids silently reusing those bytes for unrelated state.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LightData
{
    public Vector3 Direction;
    public float Ambient;
    public Vector3 Color;
    public float SpecularPower;
    public Vector3 CameraPosition;
    public float ReflectionDebugView;
    public Vector3 EnvironmentColour;
    public float EnvironmentIntensity;

    public Vector2 LightmapUVScale;
    public Vector2 LightmapUVOffset;

    public float BakedLightScale;
    public float BakedBumpFade;
    public float BakedDebugView;
    public float ReflectionBase;

    public Vector2 LightmapUVPivot;
    public float LightmapUVRotation;
    /// <summary>
    /// Presentation floor under the unresolved baked fallback, kept at VELD's new-renderer value so
    /// perturbed baked normals are not crushed to absolute black. It is NOT an EBOOT-confirmed ToD
    /// constant. This is deliberately NOT zeroed in old-engine mode: an earlier comment here claimed
    /// "the proof-bounded old-engine path keeps this at zero", which SceneLighting has never done.
    /// </summary>
    public float BakedAmbient;

    // Global old-engine analytic lighting environment (main.dat 0x8B00).
    public Vector3 EnvDirection0;
    public float EnvHasLighting;
    public Vector3 EnvDirection1;
    public float OldEngineMode;
    public Vector3 EnvAmbient;
    public float MobySpatialLightingEnabled;
    public Vector3 EnvLight0Colour;
    public float BakedLightingEnabled;
    public Vector3 EnvLight1Colour;
    public float MaterialEmissiveEnabled;

    // Fog. Colour and scale/bias/amount come from the proven 0x7100 record / 0x572774 CPU
    // precomputation. The coordinate is now closed as well: the retail Tie and Moby VPs compute
    // min(HPOS.w * c5.x + c5.y, c5.w), i.e. clip-space w, saturated in the FP. FogEnabled still gates it.
    public Vector3 FogColour;
    public float FogScale;
    public float FogBias;
    public float FogAmount;
    public float FogEnabled;
    public float FogDebugView;

    // New engine: the camera LOD scale the retail fade constants use (QForce EBOOT 0x0075A090),
    // s = (960 / 2 / tan(0.6283185)) / (horizontal focal length in pixels) - 1 at QForce's own 960-wide
    // 72-degree view. Written by VulkanRenderer.Frame from the projection and the target width.
    public float NewEngineLodScale;
    // New engine: reflection occlusion floor b and full-strength luminance a (region.dat 0x8B00
    // +0x90/+0x94, LightingEnvironment.ReflectionOcclusion); a = 0 disables it. Written by Frame.
    public float NewEngineReflectionFloor;
    public float NewEngineReflectionFullLuminance;
    // Seconds since the renderer was built: the time constant of the new-engine embedded shaders that
    // animate (NewEngineEmbeddedShaders). Written by Frame.
    public float SceneTime;

    // x = 1 while the new-engine bloom runs (VulkanRenderer.Bloom.cs): the game's shaders then write
    // their glow mask to alpha and every other draw writes 0. Written by Frame.
    public Vector4 PostParams;

    // New engine: the per-draw fog (LightingEnvironment.FogTable): x = 1 / far plane, y = 1 when on;
    // and the 512 ARGB words of region.dat 0x34004 (GLSL uvec4[128]). Written by Frame.
    public Vector4 NewEngineFog;
    public NewEngineFogTable NewEngineFogTable;

    // New engine: the atmosphere pass (VulkanRenderer.NewEngineFog.cs), the constants 0x007181B8 builds:
    // in-scatter colour (w = 1: scattering on), extinction / ln 2 (w = 1: the 0x34003 table is on), the
    // scattering direction in world space, the phase (1 - g², g + 1.001, 2g, 1 / far), and region.dat 0x34003.
    public Vector4 NewEngineAtmosphereColour;
    public Vector4 NewEngineAtmosphereExtinction;
    public Vector4 NewEngineAtmosphereDirection;
    public Vector4 NewEngineAtmospherePhase;
    public NewEngineFogTable NewEngineInscatterTable;
}

/// <summary>The 512 words of the new-engine fog table, inline in <see cref="LightData"/>.</summary>
[System.Runtime.CompilerServices.InlineArray(512)]
public struct NewEngineFogTable
{
    private uint _word;
}
