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
    }

    public override void Draw(IRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderTies) return;

        var sphere = WorldBoundingSphere;
        var sphereCenter = new Vector3(sphere.X, sphere.Y, sphere.Z);
        if (!camera.GetFrustum().ContainsSphere(sphereCenter, sphere.W)) return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        if (Model is null) return;

        if (IsDirty)
        {
            cachedRenderables.Clear();
            foreach (var mesh in Model.Meshes)
                cachedRenderables.Add(new Renderable(mesh, Transform));
            IsDirty = false;
        }

        foreach (var renderable in cachedRenderables)
            renderer.DrawRenderable(renderable);

        EntitiesRenderedThisFrame++;
    }
}
