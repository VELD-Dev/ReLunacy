using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Materials;

public sealed class Material : IMaterial
{
    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => true;

    public ITexture? AlbedoTexture { get; init; }
    public ITexture? NormalTexture { get; init; }
    public ITexture? PropertiesTexture { get; init; }
    public ITexture? DetailTexture { get; init; }

    public RenderMode RenderMode { get; init; }
    /// <summary>The game's own rendering-mode byte (0-6) — see IMaterial.GameRenderMode.</summary>
    public byte GameRenderMode { get; init; }
    public float AlphaClipThreshold { get; init; }
    public float ParallaxScale { get; init; }
    public float ParallaxBias { get; init; }
    public float DetailTiling { get; init; }
    public bool UsesDetailMap { get; init; }

    // True when this material's render mode blends (Overlay/SoftEdge/Blended — the game's
    // RenderingMode, not this simplified RenderMode) and its albedo has no format-level alpha
    // channel to source transparency from. The only place we've confirmed a per-vertex alpha
    // candidate actually varies meaningfully is on meshes matching this condition — see
    // VertexFormat0's boneIndex field and AssetManager, which only writes decoded vertex alpha
    // into the vColor attribute for materials with this flag set.
    public bool UsesVertexAlphaCandidate { get; init; }

    public Material(ulong id)
    {
        Id = id;
        RenderMode = RenderMode.Opaque;
        AlphaClipThreshold = 0.5f;
    }

    public static Material Create(ulong id, ITexture? albedo = null, ITexture? normal = null, ITexture? properties = null, ITexture? detail = null, RenderMode renderMode = RenderMode.Opaque, byte gameRenderMode = 0, float alphaClipThreshold = 0.01f, bool usesVertexAlphaCandidate = false, float parallaxScale = 0f, float parallaxBias = 0f, float detailTiling = 0f, bool usesDetailMap = false)
    {
        return new Material(id)
        {
            AlbedoTexture = albedo,
            NormalTexture = normal,
            PropertiesTexture = properties,
            DetailTexture = detail,
            RenderMode = renderMode,
            GameRenderMode = gameRenderMode,
            AlphaClipThreshold = alphaClipThreshold,
            UsesVertexAlphaCandidate = usesVertexAlphaCandidate,
            ParallaxScale = parallaxScale,
            ParallaxBias = parallaxBias,
            DetailTiling = detailTiling,
            UsesDetailMap = usesDetailMap
        };
    }
}
