namespace ReLunacy.Engine.Rendering;

/// <summary>How a texture is sampled by the 3D view. Maps to a GPU sampler in
/// AssetManager.GetSamplerFor; game textures always wrap.</summary>
public enum TextureFiltering
{
    /// <summary>Nearest-neighbor. Crisp/blocky up close.</summary>
    Point,
    /// <summary>Bilinear interpolation between the 4 nearest texels.</summary>
    Bilinear,
}
