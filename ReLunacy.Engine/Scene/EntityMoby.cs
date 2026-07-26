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

public class EntityMoby : Entity
{
    public readonly IMoby BaseMoby;
    public override string Name { get; protected set; }
    public Model[]? Models { get; private set; }

    public override Vector4 BoundingSphere { get; set; }

    /// <summary>In-game display distance for this instance (units), &lt; 0 = unlimited. Read straight from the level's own gameplay data — see MobyInstanceOld/New.</summary>
    public float DisplayDistance { get; }

    public EntityMoby(IPlacedInstance<IMoby> mobyInstance, AssetManager assetManager)
    {
        BaseMoby = mobyInstance.Asset;
        // Moby instance rotation is read straight from the file as ZYX Euler angles in radians
        // (see MobyInstanceOld/New — no unit conversion happens at the read site). The original
        // Lunacy app (Transform.SetRotation) builds this as Qz * Qy * Qx, i.e. rotate around X
        // first, then Y, then Z — NOT the same composition as CreateFromYawPitchRoll(Y,X,Z),
        // which was verified (numerically, against an unambiguous row-vector reference matrix)
        // to produce a different rotation whenever X and Z are both non-zero.
        var rotationQuat = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, mobyInstance.Rotation.Z)
                          * Quaternion.CreateFromAxisAngle(Vector3.UnitY, mobyInstance.Rotation.Y)
                          * Quaternion.CreateFromAxisAngle(Vector3.UnitX, mobyInstance.Rotation.X);

        Transform = new Transform
        {
            Translation = mobyInstance.Position,
            Rotation = rotationQuat,
            Scale = new Vector3(mobyInstance.Scale)
        };

        var (center, radius) = BaseMoby.GetBoundingSphere();
        BoundingSphere = new Vector4(center, radius);

        DisplayDistance = mobyInstance.DisplayDistance;

        Name = !string.IsNullOrEmpty(mobyInstance.Name) ? mobyInstance.Name.Split('/')[^1] : $"Moby_{BaseMoby.Id:X}_{mobyInstance.Group}";

        assetManager.Mobys.TryGetValue(BaseMoby.Id, out var models);
        Models = models;
    }

    public override void Draw(IRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderMobys) return;

        var sphere = WorldBoundingSphere;
        var sphereCenter = new Vector3(sphere.X, sphere.Y, sphere.Z);
        if (EntityManager.Singleton.FrustumCullingEnabled && !camera.GetFrustum().ContainsSphere(sphereCenter, sphere.W)) return;

        // camera.Position is stored negated relative to world/entity positions (same convention
        // used throughout the editor — see PropertyInspectorFrame's distance-to-entity readout).
        if (EntityManager.Singleton.MobyDistanceCullingEnabled && DisplayDistance >= 0 && Vector3.Distance(sphereCenter, -camera.Position) > DisplayDistance) return;

        if (Models is null) return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        if (IsDirty)
        {
            cachedRenderables.Clear();
            foreach (var model in Models)
                foreach (var mesh in model.Meshes)
                    cachedRenderables.Add(new Renderable(mesh, Transform));
            IsDirty = false;
        }

        foreach (var renderable in cachedRenderables)
            renderer.DrawRenderable(renderable);

        EntitiesRenderedThisFrame++;
    }
}
