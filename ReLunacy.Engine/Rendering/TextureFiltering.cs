namespace ReLunacy.Engine.Rendering;

/// <summary>
/// How a texture is sampled by the 3D view. Deliberately its own enum (not Bliss's SamplerType,
/// which also encodes clamp-vs-wrap addressing) so call sites express intent only - game textures
/// always wrap, and the mapping to an actual GPU sampler lives in exactly one place
/// (AssetManager.GetSamplerFor). Designed to grow: future per-texture techniques (anisotropic,
/// trilinear once mip chains are uploaded, ...) are new values here plus one switch arm there,
/// and AssetManager.SetTextureFiltering(textureId, filtering) already scopes a choice to a single
/// texture.
/// </summary>
public enum TextureFiltering
{
    /// <summary>Nearest-neighbor. Crisp/blocky up close - the renderer's historical look.</summary>
    Point,
    /// <summary>Bilinear interpolation between the 4 nearest texels.</summary>
    Bilinear,
}
