namespace ReLunacy.Engine.Assets.Interfaces;

public enum RenderMode
{
    Opaque = 0,
    AlphaClip = 1,
    AlphaBlend = 2
}

public interface IMaterial : IAsset
{
    ITexture? AlbedoTexture { get; }
    ITexture? NormalTexture { get; }
    ITexture? PropertiesTexture { get; }
    RenderMode RenderMode { get; }
    float AlphaClipThreshold { get; }
}
