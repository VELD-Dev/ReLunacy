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

        // Ties are placed via a raw affine matrix read straight from the file. Decompose it
        // directly into translation/rotation/scale instead of going through IPlacedInstance's
        // Euler-angle properties (Position/Rotation/Scale) - those are a lossy decompose-then-
        // recompose round trip through a custom quaternion->Euler conversion whose axis mapping
        // doesn't match System.Numerics' Quaternion.CreateFromYawPitchRoll, and they collapse
        // anisotropic scale into a single averaged float. Decomposing once here is exact.
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
        // Baked lighting for this placement, gated on the ASSET actually having a lightmap UV set.
        // The instance's own index is real either way (1728 distinct on metropolis), but binding a
        // bake to a tie whose second UV channel fell back to the base UV would tile the bake across
        // every mesh - visibly wrong, and wrong in a way that looks like a shading bug rather than a
        // missing offset. Ties without the UV array therefore render unlit, exactly as before.
        //
        // The channel itself is settled. The game's tie vertex program (dev/ties/, and see
        // Loading.Vertices.TieLightmapUV) routes RSX attribute location 4 - a stride-4 pair of half
        // floats in its own stream, outside the 20-byte VertexFormat0 record - straight into tc0.zw,
        // untransformed, which is where the tie fragment programs sample the baked light colour
        // (tex4) and light direction (tex14). Ties and UFrags reach the bake identically; the older
        // note here that ties must use "a different texcoord" was wrong. What differs per shader
        // variant is only tc0's packing (the unlit tie variants read tc0.z as a lone scalar).
        //
        // What is NOT settled is where that array lives for two thirds of ties - see
        // TieLightmapUV.TryReadLightmapUVs' caller for the one location that is known.
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
        // Baked lighting is per-PLACEMENT while the model (and its meshes' materials) is shared by
        // every instance of this tie asset, so a lightmapped instance needs its own material.
        // Renderable's material-override constructor gives us that without duplicating the
        // mesh: the vertex/index buffers stay shared, only the material differs. Instances with
        // no bake keep using the mesh's own material, so nothing extra is built for them.
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
