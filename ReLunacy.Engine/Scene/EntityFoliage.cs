using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;

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
    public ulong? ZoneTuid { get; }
    public override bool IsReadOnly => ZoneTuid.HasValue;

    private readonly RenderMesh? _mesh;

    /// <summary>Which sprite LOD this entity draws. 0 is the densest set.</summary>
    public const int BuiltLod = 0;

    public EntityFoliage(Assets.Foliage.Foliage foliage, in Assets.Foliage.FoliagePlacement placement,
        IMaterial? material, AssetManager assetManager)
    {
        BaseFoliage = foliage;
        ZoneTuid = placement.ZoneTuid;

        Transform = Transform.FromExactMatrix(placement.Transform);
        var scale = Transform.Scale;

        // BoundingSphere is LOCAL per Entity's convention; the placement record's sphere is world-space,
        // so the whole placement transform is undone here rather than just its translation.
        var centre = new Vector3(placement.BoundingSphere.X, placement.BoundingSphere.Y, placement.BoundingSphere.Z);
        float radius = placement.BoundingSphere.W;
        float maxScale = MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
        BoundingSphere = radius > 0f && maxScale > 0f && Matrix4x4.Invert(placement.Transform, out var toLocal)
            ? new Vector4(Vector3.Transform(centre, toLocal), radius / maxScale)
            // ComputeLocalBounds already works in card-anchor space, so it needs no conversion.
            : ComputeLocalBounds(foliage);

        Name = string.IsNullOrEmpty(placement.Name) ? $"{foliage.Name}_{ID}" : placement.Name;
        _mesh = assetManager.GetOrBuildFoliageMesh(foliage, material);
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
