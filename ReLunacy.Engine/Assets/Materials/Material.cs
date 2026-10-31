using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Materials;

public sealed class Material : IMaterial
{
    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => true;

    // Texture bindings are native authoring state, not immutable material identity. Old-engine
    // ShaderMetadataOld stores these four references independently at +00/+04/+08/+0C, so the
    // editor may safely retarget them while preserving the material/shader index itself.
    public ITexture? AlbedoTexture { get; set; }
    public ITexture? NormalTexture { get; set; }
    public ITexture? PropertiesTexture { get; set; }
    public ITexture? DetailTexture { get; set; }
    public ITexture? AuxiliaryTextureA { get; set; }
    public ITexture? AuxiliaryTextureB { get; set; }

    /// <summary>True when this material came from ShaderMetadataNew rather than the old-engine layout.</summary>
    public bool IsNewEngineMaterial { get; set; }
    /// <summary>ShaderMetadataNew +0x22. Drives the retail class-specific renderer dispatch.</summary>
    public byte NewEngineShaderClass { get; set; }
    /// <summary>ShaderMetadataNew +0x23. Bit 0x02 is retail no-cull/double-sided; bit 0x20 marks
    /// the non-standard class path. Other bits remain preserved losslessly.</summary>
    public byte NewEngineShaderFlags { get; set; }
    public float NewEngineParameter30 { get; set; }
    public float NewEngineParameter34 { get; set; }
    public uint NewEnginePackedState38 { get; set; }
    public float NewEngineParameter3C { get; set; }
    /// <summary>ShaderMetadataNew +0x20 as the QForce loader keeps it (0x006ACD28 drops the parallax
    /// bit 0x01 when the normal-map bit 0x08 is clear). Bits 0x04/0x08/0x10 pick the retail vertex
    /// shader: 44 + ((flags &gt;&gt; 2) &amp; 7) for baked Ties, 98 + k for UFrags (0x007473F4, 0x00753EB0).</summary>
    public byte NewEngineFeatureFlags { get; set; }
    /// <summary>ShaderMetadataNew +0x44. Retail class-0x13 uses this as framebuffer/render-target
    /// sampling state for dynamically supplied unit-10/unit-11 inputs.</summary>
    public uint NewEngineFramebufferSamplingMask { get; set; }
    /// <summary>Class 0x13: the family keys of the material's embedded fragment shaders
    /// (NewEngineShaderVariant.SkeletonKey). Empty for every other class.</summary>
    public IReadOnlyList<string> NewEngineShaderKeys { get; set; } = [];
    /// <summary>Class 0x13: the embedded shader bundle itself, for the renderer's microcode translation.</summary>
    public Loading.Shaders.NewEngineShaderBundle? NewEngineShaders { get; set; }

    public RenderMode RenderMode { get; init; }
    /// <summary>The game's own rendering-mode byte (0-6) - see IMaterial.GameRenderMode.</summary>
    public byte GameRenderMode { get; init; }

    /// <summary>
    /// Raw old-engine ShaderMetadataOld+0x10 feature/use byte. Keep it lossless: the retail loader
    /// intersects it with the authored Tie primitive +0x2C byte to build runtime TieFrag+0x2C.
    /// This byte by itself is NOT asserted to be callback52 shellFeatureKey.
    /// </summary>
    public byte OldFeatureFlags { get; init; }

    /// <summary>
    /// Raw old-engine ShaderMetadataOld+0x13 render/use byte. The retail loader copies this complete
    /// byte to runtime TieFrag+0x2E. Preserve all bits rather than naming only the bits already decoded.
    /// </summary>
    public byte OldRenderFlags { get; init; }

    public float AlphaClipThreshold { get; init; }
    public float ParallaxScale { get; init; }
    public float ParallaxBias { get; init; }
    public float DetailTiling { get; init; }
    /// <summary>New-engine 0x5000 +0x24 RGB material tint. Kept even before every retail
    /// consumer is reproduced so team/variant colour is no longer lost at the asset boundary.</summary>
    public System.Numerics.Vector3 MaterialTintSource { get; set; } = System.Numerics.Vector3.One;
    /// <summary>Native per-material reflection tint/mix vector. Old engine sources it from +0x40;
    /// new engine from +0x50, where the embedded unit-7 fragment path consumes it.</summary>
    public System.Numerics.Vector4 ReflectionTintSource { get; set; }
    /// <summary>
    /// New-engine 0x5000 +0x60, the second reflection vector, consumed around the unit-7 cubemap
    /// fetch. Component roles are PROVEN by set equality between this field's values and the
    /// constants the engine uploads into its own shaders
    /// (Tools/Reverse/newengine_shader_constant_binding.py, Docs/reverse/NEW_ENGINE_SHADER_CORPUS_2026-09-21.md):
    ///   .y  Fresnel exponent          - retail values 3, 5, 6.112, 6.5, 7.84, 13.536
    ///   .z  bias added to the cube's alpha before the max(.., 0) HDR gate - -1, -4, -0.576, -0.232, 0
    ///   .w  gain on the Fresnel term  - 4 on most materials, 0.512 on one
    ///   .x  present in the vector, not consumed by any shader read so far (2, 9.856, 37.568, 64)
    /// The old engine has no equivalent vector, so this stays zero there and the shader falls back
    /// to its existing presentation Fresnel when .y is zero.
    /// </summary>
    public System.Numerics.Vector4 ReflectionParametersSource { get; set; }
    /// <summary>
    /// New-engine 0x5000 +0x40: which of the level's four environment-cube descriptors this material
    /// reflects, 0..3. QForce EBOOT tracing closes the standard path: descriptor =
    /// cubeBase + (material[+0x40] * 0x20), then that descriptor is bound on RSX unit 7.
    /// Shader classes 0x0E/0x0F use this word differently, so out-of-range values still fall back to
    /// cube 0 in the generic editor path. Always 0 on the old engine, which has one cube.
    /// </summary>
    public int EnvironmentCubeIndex { get; set; }
    // Compatibility name retained for old-engine editor code.
    public System.Numerics.Vector4 OldReflectionTintSource
    {
        get => ReflectionTintSource;
        set => ReflectionTintSource = value;
    }
    public bool UsesDetailMap { get; init; }
    /// <summary>Old engine: 0x5000 +0x30..+0x3C, the Tie shaders' patch group 1 (Loading.Shaders.TodTieShaders).
    /// Zero on the new engine.</summary>
    public System.Numerics.Vector4 OldShaderVector30 { get; set; }

    // True when this material's render mode is anything but Opaque. AssetManager only writes
    // decoded vertex alpha into the vColor attribute for materials with this set; other materials
    // get a synthetic fully-opaque alpha instead.
    public bool UsesVertexAlpha { get; init; }

    // Whether the albedo texture's own format carries real alpha bits.
    public bool AlbedoHasAlphaChannel { get; init; }

    public Material(ulong id)
    {
        Id = id;
        RenderMode = RenderMode.Opaque;
        AlphaClipThreshold = 0.5f;
    }

    public static Material Create(ulong id, ITexture? albedo = null, ITexture? normal = null, ITexture? properties = null, ITexture? detail = null, RenderMode renderMode = RenderMode.Opaque, byte gameRenderMode = 0, float alphaClipThreshold = 0.01f, bool usesVertexAlpha = false, bool albedoHasAlphaChannel = false, float parallaxScale = 0f, float parallaxBias = 0f, float detailTiling = 0f, bool usesDetailMap = false, byte oldFeatureFlags = 0, byte oldRenderFlags = 0, ITexture? auxiliaryA = null, ITexture? auxiliaryB = null)
    {
        return new Material(id)
        {
            AlbedoTexture = albedo,
            NormalTexture = normal,
            PropertiesTexture = properties,
            DetailTexture = detail,
            AuxiliaryTextureA = auxiliaryA,
            AuxiliaryTextureB = auxiliaryB,
            RenderMode = renderMode,
            GameRenderMode = gameRenderMode,
            OldFeatureFlags = oldFeatureFlags,
            OldRenderFlags = oldRenderFlags,
            AlphaClipThreshold = alphaClipThreshold,
            UsesVertexAlpha = usesVertexAlpha,
            AlbedoHasAlphaChannel = albedoHasAlphaChannel,
            ParallaxScale = parallaxScale,
            ParallaxBias = parallaxBias,
            DetailTiling = detailTiling,
            UsesDetailMap = usesDetailMap
        };
    }
}