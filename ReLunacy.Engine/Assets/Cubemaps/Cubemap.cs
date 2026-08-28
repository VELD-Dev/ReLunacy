using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Cubemaps;

/// <summary>The level's environment cubemap (old-engine section 0x5920). Six square faces the game
/// samples for reflections/ambient; on metropolis it is a near-grey HDR probe whose brightness
/// lives in the alpha channel (a shared exponent, colour ~= rgb * exp2(a*scale+bias)), which is why
/// a plain RGB preview reads as almost white and the alpha channel is where the scene is visible.
///
/// The faces are decoded (Morton-unswizzled, mip0 only) by Loading.Readers.CubemapReader; see that
/// reader for the on-disk layout. Face order is the GL/RSX convention, matching
/// <see cref="FaceNames"/>.</summary>
public sealed class Cubemap : IAsset
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded => true;

    /// <summary>Edge length of one face in texels (32 on metropolis).</summary>
    public int FaceSize { get; }

    /// <summary>The six faces, mip0, in the order given by <see cref="FaceNames"/> - each an
    /// A8R8G8B8 <see cref="ITexture"/> so the existing decode/preview path handles them unchanged.</summary>
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
