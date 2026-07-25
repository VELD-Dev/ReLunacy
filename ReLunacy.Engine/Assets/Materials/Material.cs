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
    public float AlphaClipThreshold { get; init; }

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

    public static Material Create(ulong id, ITexture? albedo = null, ITexture? normal = null, ITexture? properties = null, ITexture? detail = null, RenderMode renderMode = RenderMode.Opaque, float alphaClipThreshold = 0.01f, bool usesVertexAlphaCandidate = false)
    {
        return new Material(id)
        {
            AlbedoTexture = albedo,
            NormalTexture = normal,
            PropertiesTexture = properties,
            DetailTexture = detail,
            RenderMode = renderMode,
            AlphaClipThreshold = alphaClipThreshold,
            UsesVertexAlphaCandidate = usesVertexAlphaCandidate
        };
    }
}
