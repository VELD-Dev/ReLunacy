using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityTie : Entity
{
    public readonly ITie BaseTie;

    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public RenderModel? Model { get; private set; }

    public EntityTie(IPlacedInstance<ITie> tieInstance, AssetManager assetManager)
    {
        BaseTie = tieInstance.Asset;

        // Ties are placed via a raw affine matrix; decomposed directly rather than through
        // IPlacedInstance's Euler-angle properties, which are lossy and collapse anisotropic scale
        // into a single averaged float.
        Matrix4x4.Decompose(tieInstance.GetTransformMatrix(), out var scale, out var rotation, out var translation);

        Transform = new Transform
        {
            Translation = translation,
            Rotation = rotation,
            Scale = scale
        };

        var (center, radius) = BaseTie.GetBoundingSphere();
        BoundingSphere = new Vector4(center, radius);

        Name = !string.IsNullOrEmpty(tieInstance.Name) ? tieInstance.Name.Split('/')[^1] : $"Tie_{BaseTie.Id:X}_{ID}";

        assetManager.Ties.TryGetValue(BaseTie.Id, out var model);
        Model = model;

        _assetManager = assetManager;
        // Baked lighting for this placement, gated on the asset actually having a lightmap UV set -
        // binding a bake to a tie whose UVs fell back to the base UV would tile the bake incorrectly,
        // so those ties render unlit instead.
        //
        // Where the lightmap-UV array lives is not confirmed for all ties; see TieLightmapUV.
        LightmapIndex = BaseTie.GetLightmapUVs() is not null
            ? tieInstance.LightmapIndex
            : Loading.Objects.Instances.TieInstance.NoLightmap;
    }

    private readonly AssetManager _assetManager;

    /// <summary>This placement's baked lighting entry, or 0xFFFF for none - see
    /// IPlacedInstance.LightmapIndex.</summary>
    public ushort LightmapIndex { get; }

    protected override void EnsureRenderables()
    {
        if (!IsDirty || Model is null) return;
        cachedRenderables.Clear();
        // Baked lighting is per-placement while the model is shared across instances, so lit
        // instances get their own material via Renderable's override constructor; unlit instances
        // share the mesh's own material.
        bool lit = LightmapIndex != Loading.Objects.Instances.TieInstance.NoLightmap;
        for (int i = 0; i < Model.Meshes.Length; i++)
        {
            var mesh = Model.Meshes[i];
            if (lit && i < BaseTie.Meshes.Count)
            {
                var perInstance = _assetManager.GetOrBuildMaterial(BaseTie.Meshes[i].Material, LightmapIndex);
                cachedRenderables.Add(new Renderable(mesh, Transform, perInstance));
            }
            else
            {
                cachedRenderables.Add(new Renderable(mesh, Transform));
            }
        }
        IsDirty = false;
    }

}
