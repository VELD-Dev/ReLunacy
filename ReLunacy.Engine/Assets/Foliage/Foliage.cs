using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Assets.Foliage;

/// <summary>One sprite card of a foliage asset: a quad that faces the camera at runtime.
///
/// <paramref name="Anchor"/> is the card's position in the asset's local space and
/// <paramref name="CornerOffsets"/> are 2D offsets in the card's own plane - the game builds the
/// final vertex as <c>transform(anchor) + offset * scale</c>, so the offsets are what gives the
/// card its size and the anchor is what places it. <paramref name="Uvs"/> address one QUADRANT of
/// the foliage atlas (components are always multiples of 0.5), already V-corrected.
///
/// <paramref name="Packed"/> is the two undecoded bytes that drive the game's per-sprite rotation
/// through an indexed vertex-constant lookup - carried through so a future billboard shader can
/// use them, meaningless until those constants are recovered. See
/// Loading.Vertices.FoliageSpriteAnchor.</summary>
public readonly record struct FoliageSpriteCard(
    Vector3 Anchor,
    Vector2[] CornerOffsets,
    Vector2[] Uvs,
    (byte, byte) Packed,
    int Lod);

/// <summary>One placement of a foliage asset, straight from the file's affine matrix.</summary>
public readonly record struct FoliagePlacement(Matrix4x4 Transform, Vector4 BoundingSphere);

/// <summary>A foliage asset: a set of camera-facing sprite cards (plus branch geometry that is not
/// decoded yet), instanced across the level. See Loading.Objects.FoliageMetadata.</summary>
public sealed class Foliage : IAsset
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded => true;

    /// <summary>The parsed 0xA200 record, for inspectors and for the fields this class doesn't
    /// surface (branch LODs, sprite LOD distances, the unidentified flags).</summary>
    public FoliageMetadata Metadata { get; }

    /// <summary>Every sprite card across every LOD. Filter on <see cref="FoliageSpriteCard.Lod"/>
    /// to draw one level; drawing all of them at once overlaps the LOD chain on top of itself.</summary>
    public IReadOnlyList<FoliageSpriteCard> Sprites { get; }

    public IReadOnlyList<FoliagePlacement> Placements { get; private set; }

    /// <summary>The material this foliage draws with, resolved from <see cref="Metadata"/>'s
    /// TextureIndex through the old-engine 0x5200 table (see
    /// Loading.Objects.FoliageMetadata.TextureIndex and MaterialReader.GetFoliageMaterial — the
    /// game indexes that table directly, it is not a shader lookup). Null when the index is the
    /// 0xFFFFFFFF sentinel, out of range, or the level was read without a MaterialReader (e.g. new
    /// engine); the renderer then falls back to the default billboard texture. The resolved atlas
    /// itself is reachable as <c>Material.AlbedoTexture</c>.</summary>
    public IMaterial? Material { get; }

    public Foliage(ulong id, FoliageMetadata metadata, IReadOnlyList<FoliageSpriteCard> sprites,
        IReadOnlyList<FoliagePlacement> placements, IMaterial? material = null, string? name = null)
    {
        Id = id;
        Metadata = metadata;
        Sprites = sprites;
        Placements = placements;
        Material = material;
        Name = name ?? $"Foliage_{metadata.FoliageId}";
    }

    internal void SetPlacements(IReadOnlyList<FoliagePlacement> placements) => Placements = placements;

    /// <summary>Cards belonging to one sprite LOD, highest detail first (LOD 0 is the largest set).</summary>
    public IEnumerable<FoliageSpriteCard> SpritesForLod(int lod) => Sprites.Where(s => s.Lod == lod);
}
