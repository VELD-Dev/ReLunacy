namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>The eight named slots every built material fills. Only Albedo and Normal keep their
/// conventional meaning here; the rest of the game's inputs are addressed by name instead (see
/// AssetManager.GetOrBuildMaterial for the full list).</summary>
public enum MaterialMapType
{
    Albedo,
    Metallic,
    Normal,
    Roughness,
    Occlusion,
    Emission,
    Opacity,
    Height,
}

/// <summary>Names a slot on a <see cref="RenderMaterial"/>. Implicitly convertible from both a
/// <see cref="MaterialMapType"/> and a plain string, so the game's own inputs ("fLightColour",
/// "fParallaxScale", ...) sit in the same dictionary as the conventional ones.</summary>
public readonly record struct MaterialMapKey(string Name)
{
    public MaterialMapKey(MaterialMapType type) : this(type.ToString()) { }

    public static implicit operator MaterialMapKey(MaterialMapType type) => new(type);
    public static implicit operator MaterialMapKey(string name) => new(name);

    public override string ToString() => Name;
}

/// <summary>One slot's contents: a texture, the sampler to read it with, and a scalar.
///
/// The scalar is not decoration. Several of the game's per-material constants (alpha-clip threshold,
/// parallax scale/bias, detail tiling, "this material has a real bake") are carried in it, which is why
/// a map with no texture at all is still worth storing.</summary>
public sealed class MaterialMap(GpuTexture? texture = null, NeoVeldrid.Sampler? sampler = null, float value = 0f)
{
    public GpuTexture? Texture = texture;
    public NeoVeldrid.Sampler? Sampler = sampler;
    public float Value = value;
}
