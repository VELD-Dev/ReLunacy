using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

/// <summary>One placement of a foliage asset: a batch of camera-facing sprite cards.
///
/// The geometry is built ONCE, not per frame. Every vertex stores the card's ANCHOR as its
/// position and its own 2D corner offset in TexCoords2; BillboardModelShaderSource does the
/// facing by adding that offset after the view transform. So the vertex buffer is static and the
/// cards still turn with the camera - no per-frame rebuild, no CPU billboarding.
///
/// Only ONE sprite LOD is built (the highest-detail one). The LOD chain is a distance-switching
/// mechanism and drawing every level at once stacks 117 cards where the game draws 58; wiring the
/// switch needs the LOD distances in Loading.Objects.FoliageSpriteLodRange, which are read but not
/// yet acted on.</summary>
public class EntityFoliage : Entity
{
    public readonly Assets.Foliage.Foliage BaseFoliage;

    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    private readonly RenderMesh? _mesh;
    private readonly RenderMaterial? _material;

    /// <summary>Which sprite LOD this entity draws. 0 is the densest set.</summary>
    public const int BuiltLod = 0;

    public EntityFoliage(Assets.Foliage.Foliage foliage, in Assets.Foliage.FoliagePlacement placement,
        IMaterial? material, AssetManager assetManager)
    {
        BaseFoliage = foliage;

        Matrix4x4.Decompose(placement.Transform, out var scale, out var rotation, out var translation);
        Transform = new Transform { Translation = translation, Rotation = rotation, Scale = scale };

        // BoundingSphere is LOCAL per Entity's convention, i.e. in the space the card anchors are in.
        // The placement record's sphere is WORLD-space, so the whole placement transform is undone
        // rather than just its translation: subtracting the translation alone left the sphere rotated
        // and scaled wrongly about the placement, which culled foliage that was still in frame.
        var centre = new Vector3(placement.BoundingSphere.X, placement.BoundingSphere.Y, placement.BoundingSphere.Z);
        float radius = placement.BoundingSphere.W;
        float maxScale = MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
        BoundingSphere = radius > 0f && maxScale > 0f && Matrix4x4.Invert(placement.Transform, out var toLocal)
            ? new Vector4(Vector3.Transform(centre, toLocal), radius / maxScale)
            // ComputeLocalBounds already works in card-anchor space, so it needs no conversion.
            : ComputeLocalBounds(foliage);

        Name = $"{foliage.Name}_{ID}";

        var cards = foliage.SpritesForLod(BuiltLod).ToList();
        if (cards.Count == 0) return;

        // material carries the atlas resolved from FoliageMetadata.TextureIndex — a DIRECT index
        // into the 0x5200 texture table, proven by the game's own A200 loader (see that field). It
        // is null only when the asset's index is the 0xFFFFFFFF sentinel or the level is new-engine;
        // GetOrBuildBillboardMaterial then falls back to the default white texture, which still
        // shows the billboarding and card geometry while making an unresolved case obvious on screen.
        _material = assetManager.GetOrBuildBillboardMaterial(material);
        _mesh = BuildMesh(cards, _material);
    }

    /// <summary>Two triangles per card, sharing the anchor as every corner's position. The corner
    /// order in the file is already a consistent winding around the quad (0,1,2,3), so the two
    /// triangles are 0-1-2 and 0-2-3.</summary>
    private static RenderMesh BuildMesh(List<Assets.Foliage.FoliageSpriteCard> cards, RenderMaterial material)
    {
        var vertices = new Vertex3D[cards.Count * 4];
        var indices = new uint[cards.Count * 6];

        for (int c = 0; c < cards.Count; c++)
        {
            var card = cards[c];
            for (int k = 0; k < 4; k++)
            {
                vertices[c * 4 + k] = new Vertex3D(
                    card.Anchor,
                    card.Uvs[k],
                    card.CornerOffsets[k],
                    Vector3.UnitY,
                    new Vector4(1f, 0f, 0f, 1f),
                    Vector4.One);
            }

            int v = c * 4;
            int i = c * 6;
            indices[i + 0] = (uint)(v + 0);
            indices[i + 1] = (uint)(v + 1);
            indices[i + 2] = (uint)(v + 2);
            indices[i + 3] = (uint)(v + 0);
            indices[i + 4] = (uint)(v + 2);
            indices[i + 5] = (uint)(v + 3);
        }

        var mesh = new RenderMesh(vertices, indices, material);

        // Foliage builds its mesh here rather than through AssetManager.BuildModel, so it has to
        // register its own geometry with the capture registry - otherwise the scene walk finds no
        // geometry for it and foliage silently never renders (the same gap EntityUFrag had).
        Rendering.Vulkan.VulkanSceneCapture.Register(mesh, Rendering.Vulkan.VulkanSceneCapture.Interleave(vertices), indices);

        return mesh;
    }

    /// <summary>Fallback bounds from the cards themselves, used when the instance record's radius
    /// is zero. Card offsets are added in view space so they can point any direction in world
    /// space - the anchor spread is padded by the largest corner offset rather than assuming the
    /// cards lie in some plane.</summary>
    private static Vector4 ComputeLocalBounds(Assets.Foliage.Foliage foliage)
    {
        if (foliage.Sprites.Count == 0) return Vector4.Zero;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        float pad = 0f;
        foreach (var card in foliage.Sprites)
        {
            min = Vector3.Min(min, card.Anchor);
            max = Vector3.Max(max, card.Anchor);
            foreach (var o in card.CornerOffsets) pad = MathF.Max(pad, o.Length());
        }

        var centre = (min + max) * 0.5f;
        return new Vector4(centre, (max - centre).Length() + pad);
    }

    protected override void EnsureRenderables()
    {
        if (!IsDirty || _mesh == null) return;
        cachedRenderables.Clear();
        cachedRenderables.Add(new Renderable(_mesh, Transform));
        IsDirty = false;
    }

}
