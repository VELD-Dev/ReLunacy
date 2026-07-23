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

    public RenderMode RenderMode { get; init; }
    public float AlphaClipThreshold { get; init; }
    public bool IsDecal { get; init; }
    public float DecalOffsetCandidate { get; init; }

    public Material(ulong id)
    {
        Id = id;
        RenderMode = RenderMode.Opaque;
        AlphaClipThreshold = 0.5f;
    }

    public static Material Create(ulong id, ITexture? albedo = null, ITexture? normal = null, ITexture? properties = null, RenderMode renderMode = RenderMode.Opaque, float alphaClipThreshold = 0.01f, bool isDecal = false, float decalOffsetCandidate = 0f)
    {
        return new Material(id)
        {
            AlbedoTexture = albedo,
            NormalTexture = normal,
            PropertiesTexture = properties,
            RenderMode = renderMode,
            AlphaClipThreshold = alphaClipThreshold,
            IsDecal = isDecal,
            DecalOffsetCandidate = decalOffsetCandidate
        };
    }
}
