using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Assets.Foliage;

/// <summary>One sprite card of a foliage asset: a quad that faces the camera at runtime.
/// <paramref name="Anchor"/> is the card's position in the asset's local space;
/// <paramref name="CornerOffsets"/> are 2D offsets in the card's own plane giving it its size.
/// <paramref name="Uvs"/> address one quadrant of the foliage atlas. <paramref name="Packed"/> is
/// two undecoded bytes driving the game's per-sprite rotation; not yet used here.</summary>
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
    /// TextureIndex. Null when the index is the 0xFFFFFFFF sentinel, out of range, or unavailable;
    /// the renderer then falls back to the default billboard texture.</summary>
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
