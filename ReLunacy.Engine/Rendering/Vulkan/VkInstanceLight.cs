using System.Numerics;
using System.Runtime.InteropServices;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Per-draw std430 payload matching the InstanceLight struct in VulkanRenderer.
///
/// The first five vec4s are the existing old-engine spatial-lighting packet. The final three vec4s
/// carry proof-bounded TIE source metadata. Byte-valued fields are stored as exact 0..255 floats so
/// the shader can inspect them without bit-casting or precision loss. No terminal retail route is
/// encoded here: EffectiveUseMask remains source/runtime evidence, not a callback52 shellFeatureKey.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VkInstanceLight
{
    public Vector4 Ambient;
    public Vector4 Light0Colour;
    public Vector4 Light1Colour;
    public Vector4 Direction0;
    public Vector4 Direction1;

    // x=SerializedUseMask, y=MaterialFeatureMask, z=EffectiveUseMask, w=SerializedRaw2F
    public Vector4 TieShaderUse0;
    // x=SerializedRenderMode, y=EffectiveRenderMode, z=SerializedRenderFlags, w=EffectiveRenderFlags
    public Vector4 TieShaderUse1;
    // x=IsOldEngine, y=SourceHadExplicitUse, z/w reserved for a separately proven terminal route.
    public Vector4 TieShaderUseMeta;
    // New engine: the gameplay vec4 the game patches into fragment group 19 for this draw (0x33003
    // selector, callback 0x00677240); zero = none, the programs then read (0, 0, 0, 1).
    public Vector4 Gameplay;
    // New engine, two-layer TIE draws: the second material's fragment groups 15 / 17 (+0x50, +0x70).
    public Vector4 SecondTint;
    public Vector4 SecondParallax;
    /// <summary>Animated UVs (section 0x5C00): (pivot u, pivot v, rotation per game frame, 1 = on).</summary>
    public Vector4 UvAnim;
    /// <summary>Animated UVs: (scroll u, scroll v per second, the game's frame rate, 0).</summary>
    public Vector4 UvScroll;
    /// <summary>New engine: the world point the draw's fog is measured from (its bounding-sphere centre),
    /// w = 1 when the draw is fogged (VulkanRenderer.NewEngineFog.cs).</summary>
    public Vector4 FogCentre;
    /// <summary>x = 1 on a two-layer draw (terrain or TIE whose second material a mask paints in): the generic
    /// shader blends it as the game's two-layer programs do (VulkanRenderer.LitShader.cs).</summary>
    public Vector4 Layers;

    public static VkInstanceLight From(OldEngineLightingSampler.Sample sample)
    {
        if (!sample.HasLighting)
            return default;

        return new VkInstanceLight
        {
            Ambient = new Vector4(sample.Ambient, 1f),
            Light0Colour = new Vector4(sample.Light0Colour, sample.VolumeIndex + 1),
            Light1Colour = new Vector4(sample.Light1Colour, sample.VoxelWeight),
            Direction0 = new Vector4(sample.Direction0, 0f),
            Direction1 = new Vector4(sample.Direction1, 0f),
        };
    }

    /// <summary>
    /// Moby instance payload. Direction0.w marks the draw as a Moby for the fragment stage even when
    /// no spatial-lighting sample exists, because the Moby shading contract differs from TIE/UFrag
    /// independently of the lighting data (see the retail Moby FP notes in VulkanRenderer.cs).
    /// </summary>
    public static VkInstanceLight ForMoby(OldEngineLightingSampler.Sample sample, bool oldEngine = true)
    {
        var light = From(sample);
        light.Direction0.W = oldEngine ? MobyClassFlag : NewEngineMobyClassFlag;
        return light;
    }

    /// <summary>Direction0.w value identifying an old-engine Moby draw. Other classes leave it at 0.</summary>
    public const float MobyClassFlag = 1f;

    /// <summary>
    /// Direction0.w value identifying a NEW-engine Moby draw. The two engines' Moby fragment programs
    /// differ on the environment cube: no retail Tools of Destruction program paired with a skinned
    /// Moby VP samples it, while every QForce one does - unit 4 by the world normal for ambient, unit
    /// 7 by the reflection vector (RPCS3 pipeline cache, fp_texture_dimensions 0x9615: units 4 and
    /// 7 are cubes; FP 855d8827ca1d/1a28fd1dc600). The fragment stage keys the reflection term on it.
    /// </summary>
    public const float NewEngineMobyClassFlag = 3f;

    /// <summary>Direction0.w value identifying an old-engine TIE draw that carries a retail baked light
    /// scale in Direction1.w.</summary>
    public const float TieBakedScaleFlag = 2f;

    public static VkInstanceLight ForOldTie(float bakedLightScale)
    {
        var light = default(VkInstanceLight);
        light.Direction0.W = TieBakedScaleFlag;
        light.Direction1.W = bakedLightScale;
        return light;
    }

    /// <summary>Direction0.w value identifying a new-engine TIE or UFrag draw that carries the game's
    /// per-draw baked-light vertex constant c[4]: Direction1.w = c[4].x (the RNM programs' TEX1.z),
    /// TieShaderUseMeta.z = c[4].y (the flat programs'). See Loading.Objects.NewEngineBakedLighting.</summary>
    public const float NewEngineBakedLightFlag = 4f;
    public const float NewEnginePrelightFlag = 5f;

    public static VkInstanceLight ForNewEngineBaked(Vector4 bakedLight)
    {
        var light = default(VkInstanceLight);
        light.Direction0.W = NewEngineBakedLightFlag;
        light.Direction1.W = bakedLight.X;
        light.TieShaderUseMeta.Z = bakedLight.Y;
        return light;
    }

    /// <summary>
    /// A new-engine TIE draw lit by its placement's prelight: Direction0.w = 5, Direction1.w = c[4].z
    /// (placement +0x68, the scale the unbaked programs apply), TieShaderUseMeta.w = the bits of the
    /// signed offset from gl_VertexIndex to this draw's first prelight word.
    /// </summary>
    public static VkInstanceLight ForNewEnginePrelight(Vector4 bakedLight, int wordDelta)
    {
        var light = default(VkInstanceLight);
        light.Direction0.W = NewEnginePrelightFlag;
        light.Direction1.W = bakedLight.Z;
        light.TieShaderUseMeta.W = BitConverter.Int32BitsToSingle(wordDelta);
        return light;
    }

    /// <summary>Direction0.w value of an old-engine (ToD) TIE draw lit by its placement's prelight.</summary>
    public const float OldPrelitFlag = 6f;

    /// <summary>
    /// A ToD unbaked TIE draw lit by its placement's prelight: Direction0.w = 6, Direction1.w = f (the
    /// placement's +0x60, c[4] = (2f, -f, f)), Direction0.z = the offset from gl_VertexIndex to this draw's
    /// first prelight word, as a float (exact below 2^24; Direction0.xyz only meets a zero light colour on
    /// TIE draws, and the old TIE shader-use wiring overwrites the other spare fields).
    /// </summary>
    public static VkInstanceLight ForOldPrelit(float f, int wordDelta)
    {
        var light = default(VkInstanceLight);
        light.Direction0.W = OldPrelitFlag;
        light.Direction0.Z = wordDelta;
        light.Direction1.W = f;
        return light;
    }

    /// <summary>
    /// New-engine distance fades: TieShaderUse1 = (1/d0, 1/d1, 1/d2, 1) from the primitive's (TIE) or
    /// record's (UFrag) fade distances. The lit shader never reads TieShaderUse1 otherwise.
    /// </summary>
    public void ApplyNewEngineFades(Vector3 distances)
    {
        if (distances.X <= 0f || distances.Y <= 0f || distances.Z <= 0f)
            return;
        TieShaderUse1 = new Vector4(1f / distances.X, 1f / distances.Y, 1f / distances.Z, 1f);
    }

    /// <summary>
    /// New-engine TIE / UFrag draws: TieShaderUse0.w = the primitive's (record's) feature mask + 1, which
    /// the lit shader ANDs with the material's +0x20 bits (0 = no mask known: Mobys, old engine).
    /// </summary>
    public void ApplyNewEngineFeatureMask(byte mask) => TieShaderUse0.W = mask + 1f;

    public void ApplyOldTieDrawState(in VkOldTieDrawState state)
    {
        if (!state.SourceHadExplicitUse)
            return;

        TieShaderUse0 = new Vector4(
            state.SerializedUseMask,
            state.MaterialFeatureMask,
            state.EffectiveUseMask,
            state.SerializedRaw2F);
        TieShaderUse1 = new Vector4(
            state.SerializedRenderMode,
            state.EffectiveRenderMode,
            state.SerializedRenderFlags,
            state.EffectiveRenderFlags);
        // .zw: the VP's c[16].xy distance fade (VkOldTieDrawState.DistanceFadeScale / Bias).
        TieShaderUseMeta = new Vector4(
            state.IsOldEngine ? 1f : 0f,
            state.SourceHadExplicitUse ? 1f : 0f,
            state.DistanceFadeScale,
            state.DistanceFadeBias);
    }
}
