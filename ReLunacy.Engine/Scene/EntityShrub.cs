using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// One shrub placement (zone section 0x7540): grass, ferns, groundcover. Drawn and listed, but read-only - no save path
/// writes shrubs, so the editor does not move, delete or rename them (see <see cref="Entity.IsReadOnly"/>).
/// </summary>
public class EntityShrub : Entity
{
    public readonly IShrub BaseShrub;

    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public RenderModel? Model { get; }

    /// <summary>The class's far draw distance; -1 where it has none. Culled past it like a Moby's display distance.</summary>
    public float DisplayDistance { get; }

    public override bool IsReadOnly => true;

    /// <summary>The placement's baked light (record +0x30); null when the placement carries none.</summary>
    public Vector3? BakedLight { get; }

    /// <summary>The placement's sway phase, 0-1 (record +0x1C over 32).</summary>
    public float WindPhase { get; }

    public EntityShrub(IPlacedInstance<IShrub> shrubInstance, AssetManager assetManager)
    {
        BaseShrub = shrubInstance.Asset;

        Matrix4x4.Decompose(shrubInstance.GetTransformMatrix(), out var scale, out var rotation, out var translation);
        Transform = new Transform { Translation = translation, Rotation = rotation, Scale = scale };

        var (center, radius) = BaseShrub.GetBoundingSphere();
        BoundingSphere = new Vector4(center, radius);

        Name = !string.IsNullOrEmpty(shrubInstance.Name) ? shrubInstance.Name.Split('/')[^1] : $"Shrub_{BaseShrub.Id:X}_{ID}";

        float far = BaseShrub.DrawDistances.Far;
        DisplayDistance = float.IsFinite(far) && far > 0f ? far : -1f;

        if (shrubInstance is Assets.Geometry.PlacedInstance<IShrub> placed)
        {
            BakedLight = placed.ShrubLight;
            WindPhase = placed.ShrubPhase / 32f;
        }

        Model = assetManager.GetOrBuildShrub(BaseShrub);
    }

    protected override void EnsureRenderables()
    {
        if (!IsDirty || Model is null) return;
        cachedRenderables.Clear();
        foreach (var mesh in Model.Meshes)
            cachedRenderables.Add(new Renderable(mesh, Transform));
        IsDirty = false;
    }
}
