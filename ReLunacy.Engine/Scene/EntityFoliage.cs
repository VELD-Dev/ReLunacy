using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

/// <summary>One placement of a foliage asset: a batch of camera-facing sprite cards. Geometry is built
/// once; billboarding is done in the vertex shader by adding each card's corner offset after the view
/// transform, so no per-frame rebuild is needed.
///
/// Only the highest-detail sprite LOD is currently built; distance-based LOD switching is not yet wired up.</summary>
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

        // BoundingSphere is LOCAL per Entity's convention; the placement record's sphere is world-space,
        // so the whole placement transform is undone here rather than just its translation.
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

        // material carries the atlas resolved from the asset's texture index; null falls back to the
        // default white texture in GetOrBuildBillboardMaterial.
        _material = assetManager.GetOrBuildBillboardMaterial(material);
        _mesh = BuildMesh(cards, _material);
    }

    /// <summary>Builds two triangles per card (winding 0-1-2, 0-2-3), each vertex storing the card's
    /// anchor as its position.</summary>
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

        // Registers geometry with the capture registry so the Vulkan scene walk can find it.
        Rendering.Vulkan.VulkanSceneCapture.Register(mesh, Rendering.Vulkan.VulkanSceneCapture.Interleave(vertices), indices);

        return mesh;
    }

    /// <summary>Fallback local bounds computed from the cards themselves, used when the instance
    /// record's radius is zero.</summary>
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
