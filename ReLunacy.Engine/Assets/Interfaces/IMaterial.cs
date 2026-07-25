namespace ReLunacy.Engine.Assets.Interfaces;

public enum RenderMode
{
    Opaque = 0,
    AlphaClip = 1,
    AlphaBlend = 2,
    Additive = 3,
}

public interface IMaterial : IAsset
{
    ITexture? AlbedoTexture { get; }
    ITexture? NormalTexture { get; }
    ITexture? PropertiesTexture { get; }
    RenderMode RenderMode { get; }
    float AlphaClipThreshold { get; }

    // See Material.UsesVertexAlphaCandidate — true when this material's render mode blends and
    // its albedo has no format-level alpha channel, the one condition we've confirmed a per-vertex
    // alpha candidate (VertexFormat0.boneIndex) actually correlates with real fade behavior.
    bool UsesVertexAlphaCandidate { get; }
}
