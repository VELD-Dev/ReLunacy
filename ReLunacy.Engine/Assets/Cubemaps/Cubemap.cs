using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Cubemaps;

/// <summary>The level's environment cubemap (old-engine section 0x5920): six square faces the game
/// samples for reflections/ambient. Brightness is HDR-encoded in the alpha channel as a shared
/// exponent, so a plain RGB preview reads as near-white.</summary>
public sealed class Cubemap : IAsset
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded => true;

    /// <summary>Edge length of one face in texels (32 on metropolis).</summary>
    public int FaceSize { get; }

    /// <summary>The six faces, mip0, in the order given by <see cref="FaceNames"/>, each an A8R8G8B8 <see cref="ITexture"/>.</summary>
    public IReadOnlyList<ITexture> Faces { get; }

    /// <summary>GL/RSX cube face order, parallel to <see cref="Faces"/>.</summary>
    public static readonly string[] FaceNames = ["+X", "-X", "+Y", "-Y", "+Z", "-Z"];

    public Cubemap(ulong id, int faceSize, IReadOnlyList<ITexture> faces, string? name = null)
    {
        Id = id;
        FaceSize = faceSize;
        Faces = faces;
        Name = name ?? $"Cubemap_{id:X}";
    }
}
