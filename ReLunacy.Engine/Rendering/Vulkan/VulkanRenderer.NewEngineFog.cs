using System.Numerics;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// New-engine per-draw fog. The game measures each Tie, UFrag and Moby from the camera (Tie runtime
/// +0x40, UFrag record +0x30 = its bounding-sphere centre, Moby position; QForce EBOOT 0x0073EA78,
/// 0x0074D9B0, 0x0078C4F0 -> 0x00718030), divides by the view's far plane, reads region.dat 0x34004 at
/// that point (512 ARGB entries, two lerped) and patches pow(entry, 2.2) into fragment group 0, which
/// the fog shaders mix in as mix(colour, K.rgb, K.w). The shaders do the same per draw
/// (VulkanRenderer.LitShader.cs newEngineFog) from the centre and table set here.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    /// <summary>Marks every Tie, UFrag and Moby draw of a level with a fog table with its centre.</summary>
    private void ApplyNewEngineFogCentres(VkInstanceLight[] lights)
    {
        if (_oldLightingEnvironment?.FogTable is null) return;
        for (int i = 0; i < lights.Length && i < _instKind.Length; i++)
        {
            var kind = (SceneEntityKind)_instKind[i];
            if (kind is SceneEntityKind.Tie or SceneEntityKind.UFrag or SceneEntityKind.Moby or SceneEntityKind.Foliage)
                lights[i].FogCentre = new Vector4(_instCenter[i], 1f);
        }
    }

    // 0x007181B8's constants (pool 0x00718188): 1 / lambda for lambda = 650, 570, 475 nm, the Mie K(lambda)
    // and the two scattering factors; their ratio is 1 / (2 pi).
    private static readonly Vector3 AtmosphereInverseWavelength = new(1538461.5f, 1754386f, 2105263.2f);
    private static readonly Vector3 AtmosphereMieK = new(0.685f, 0.679f, 0.670f);
    private const float AtmosphereRayleighFactor = 5.635251e-16f, AtmosphereMieFactor = 3.540733e-15f;

    /// <summary>Per frame: the far plane, the tables and the atmosphere pass's constants, or off.</summary>
    private void WriteNewEngineFog(ref LightData light)
    {
        light.NewEngineFog = Vector4.Zero;
        light.NewEngineAtmosphereColour = light.NewEngineAtmosphereExtinction = Vector4.Zero;
        light.NewEngineAtmosphereDirection = light.NewEngineAtmospherePhase = Vector4.Zero;
        if (_oldLightingEnvironment is not { } environment) return;
        float far = (environment.CameraClip ?? DefaultGameCameraClip).Y;
        float invFar = far > 0f ? 1f / far : 0f;
        if (environment.FogTable is { Length: 512 } table)
        {
            light.NewEngineFog = new Vector4(invFar, 1f, 0f, 0f);
            for (int k = 0; k < 512; k++)
                light.NewEngineFogTable[k] = table[k];
        }
        bool inscatter = environment.InscatterTable is { Length: 512 };
        if (inscatter)
            for (int k = 0; k < 512; k++)
                light.NewEngineInscatterTable[k] = environment.InscatterTable![k];

        float g = 0f;
        Vector3 colour = Vector3.Zero, extinction = Vector3.Zero, direction = Vector3.Zero;
        if (environment.Atmosphere is { } atmosphere)
        {
            g = atmosphere.G;
            float turbidity = MathF.Max(atmosphere.Turbidity, 1e-5f);
            Vector3 inverseSquare = AtmosphereInverseWavelength * AtmosphereInverseWavelength;
            // vec1 = (1 - g²) x colour x intensity x 50 x (Rayleigh x 1/lambda² x T) / (Mie x K x 1/lambda² x T)
            colour = (1f - g * g) * atmosphere.Colour * atmosphere.Intensity * 50f *
                     (AtmosphereRayleighFactor / AtmosphereMieFactor) / AtmosphereMieK;
            // vec2 = Mie x K x 1/lambda² x T / ln 2
            extinction = AtmosphereMieK * inverseSquare * turbidity * AtmosphereMieFactor / MathF.Log(2f);
            direction = atmosphere.Direction;
            if (direction.X + direction.Y + direction.Z == 0f && environment.Lights.Count > 0)
                direction = -environment.Lights[0].Direction;
            if (direction.LengthSquared() > 0f) direction = Vector3.Normalize(direction);
        }
        bool scatter = colour.X > 0f || colour.Y > 0f || colour.Z > 0f;
        light.NewEngineAtmosphereColour = new Vector4(colour, scatter ? 1f : 0f);
        light.NewEngineAtmosphereExtinction = new Vector4(extinction, inscatter ? 1f : 0f);
        light.NewEngineAtmosphereDirection = new Vector4(direction, 0f);
        light.NewEngineAtmospherePhase = new Vector4(1f - g * g, g + 1.001f, 2f * g, invFar);
    }
}
