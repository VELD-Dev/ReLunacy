using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Cubemaps;

/// <summary>The level's environment cubemap (old-engine section 0x5920). Six square faces the game
/// samples for reflections/ambient; on metropolis it is a near-grey HDR probe whose brightness
/// lives in the alpha channel (a shared exponent, colour ~= rgb * exp2(a*scale+bias)), which is why
/// a plain RGB preview reads as almost white and the alpha channel is where the scene is visible.
/// Every ToD cube descriptor sets ADDRESS gamma on all four channels: the RSX sRGB-decodes the exponent
/// too before the programs read it (the renderer's upload does the same).
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

    /// <summary>The six faces, mip0, in the order given by <see cref="FaceNames"/>, each an A8R8G8B8 <see cref="ITexture"/>.</summary>
    public IReadOnlyList<ITexture> Faces { get; }

    /// <summary>GL/RSX cube face order, parallel to <see cref="Faces"/>.</summary>
    public static readonly string[] FaceNames = ["+X", "-X", "+Y", "-Y", "+Z", "-Z"];

    /// <summary>
    /// New engine only: the six faces exactly as the RSX samples them - format 0x9A, four half floats
    /// per texel, deswizzled to row-major, the full mip chain with mip 0 first (every retail cube has
    /// one mip) - as little-endian R16G16B16A16 bytes ready for an R16G16B16A16_Float upload. Null on
    /// the old engine.
    ///
    /// <see cref="Faces"/> is an A8R8G8B8 re-encoding for the preview paths, and it cannot stand in
    /// for these: it folds the fourth half into rgb and stores an exponent in alpha. The retail
    /// programs need the fourth half ON ITS OWN - they compute `cube.rgb * max(cube.a + bias, 0)`,
    /// with the bias authored per material at 0x5000 +0x68
    /// (Docs/reverse/NEW_ENGINE_REFLECTION_FOG_TIE_UFRAG_2026-09-23.md).
    /// </summary>
    public IReadOnlyList<byte[]>? HalfFloatFaces { get; init; }

    /// <summary>
    /// New engine: the cubemaps.dat resource this cube belongs to (each holds four, picked by a material's
    /// 0x5000 +0x40) and its place in it. 0 on the old engine.
    /// </summary>
    public ulong ResourceTuid { get; init; }

    public int IndexInResource { get; init; }

    public Cubemap(ulong id, int faceSize, IReadOnlyList<ITexture> faces, string? name = null)
    {
        Id = id;
        FaceSize = faceSize;
        Faces = faces;
        Name = name ?? $"Cubemap_{id:X}";
    }
}
