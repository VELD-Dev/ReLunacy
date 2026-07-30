using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry.Models;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Transformations;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityTie : Entity
{
    public readonly ITie BaseTie;

    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public Model? Model { get; private set; }

    public EntityTie(IPlacedInstance<ITie> tieInstance, AssetManager assetManager)
    {
        BaseTie = tieInstance.Asset;

        // Ties are placed via a raw affine matrix read straight from the file. Decompose it
        // directly into translation/rotation/scale instead of going through IPlacedInstance's
        // Euler-angle properties (Position/Rotation/Scale) — those are a lossy decompose-then-
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
        // Deliberately NOT tieInstance.LightmapIndex yet. Tie instances really do carry a bake
        // index (1728 distinct on metropolis), but ties have no identified lightmap UV set —
        // VertexFormat0 holds exactly one UV pair and it TILES, so sampling an atlas with it
        // repeats the bake many times per mesh. Terrain has UFragVertex.UVs2 and works; ties wait
        // until their UV source turns up.
        //
        // And it will not turn up inside VertexFormat0, which is why searching that record kept
        // failing. The captured RPCS3 DrawParametersBuffer covers every draw in the frame, and the
        // 85250 draws with a 20-byte vertex record (VertexFormat0's exact size) match it for
        // attr0/attr1/attr2 at +0/+8/+12 - position+boneIndex, UVs, packed normal - and then carry
        // EXTRA attributes that live in their own streams. The candidate is sharply located: 33103
        // draws in the frame bind an attribute of stride 4 holding 2 HALF FLOATS - a dedicated,
        // tightly packed UV set outside the 20-byte record entirely - and it is at attribute
        // LOCATION 4 in every single one of them (30197 of those are 20-byte records). That is the
        // shape a lightmap UV channel has when it is bolted onto an existing vertex format without
        // changing it, and a location that consistent is a convention, not a coincidence.
        //
        // It also rules out reusing the terrain result directly. RPCS3 does manual vertex fetch, so
        // one compiled vertex program serves any layout - meaning if ties shared terrain's program,
        // terrain's answer (attr2 -> tc0.zw) would apply verbatim. It cannot: for 78752 of the 85250
        // 20-byte draws, attr2 is a CMP 11:11:10 at +12, i.e. VertexFormat0's packed NORMAL, not a UV
        // pair. Sampling an atlas with that would be meaningless, so ties reach the bake through a
        // different program and a different texcoord.
        // Consistent with this on the file side: TieMetadataOld's field at 0x18 (named
        // verticesBufferSize) is really the vertex buffer's END offset, not a size - the 0x18-0x14
        // span is divisible by 20 for 193/193 ties, so the vertex count is span/20. The gap between
        // one tie's end and the next tie's start is divisible by 4 for 192/192, and equals exactly
        // vertexCount*4 for 41 of them. Suggestive of a second stride-4 array packed between the
        // vertex buffers, NOT yet proven - the ratio is inconsistent for the rest, so do not build
        // on it until a tie drawcall's own vertex+fragment program confirms which attribute feeds
        // the bake sampler. Ties use a different shader program than the captured terrain one, so
        // the terrain result (attr2 -> tc0.zw) does not transfer.
        LightmapIndex = Loading.Objects.Instances.TieInstance.NoLightmap;
    }

    private readonly AssetManager _assetManager;

    /// <summary>This placement's baked lighting entry, or 0xFFFF for none — see
    /// IPlacedInstance.LightmapIndex.</summary>
    public ushort LightmapIndex { get; }

    public override void Draw(IRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderTies) return;

        var sphere = WorldBoundingSphere;
        var sphereCenter = new Vector3(sphere.X, sphere.Y, sphere.Z);
        if (EntityManager.Singleton.FrustumCullingEnabled && !camera.GetFrustum().ContainsSphere(sphereCenter, sphere.W)) return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        if (Model is null) return;

        if (IsDirty)
        {
            cachedRenderables.Clear();
            // Baked lighting is per-PLACEMENT while Model (and its meshes' materials) is shared by
            // every instance of this tie asset, so a lightmapped instance needs its own Material.
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

        foreach (var renderable in cachedRenderables)
            renderer.DrawRenderable(renderable);

        EntitiesRenderedThisFrame++;
    }
}
