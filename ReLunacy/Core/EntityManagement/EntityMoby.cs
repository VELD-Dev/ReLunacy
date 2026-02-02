using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Transformations;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Objects;
using LibLunacy.Objects.Instances;
using LibLunacy.Shaders;
using ReLunacy.Utility;
using System.Numerics;
using Bliss.CSharp.Effects;
using Veldrid;

using Shader = LibLunacy.Shaders.Shader;
using Model = Bliss.CSharp.Geometry.Model;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;

namespace ReLunacy.Core.EntityManagement;

public class EntityMoby : Entity
{
    public readonly IMoby BaseMoby;
    public override string Name { get; protected set; }
    public Model[] Models { get; private set; }

    public override Vector4 BoundingSphere { get; set; }

    public EntityMoby(IPlacedInstance<IMoby> mobyInstance, AssetManager assetManager) : base()
    {
        BaseMoby = mobyInstance.Asset;
        const float Deg2Rad = MathF.PI / 180f;
        var rotationQuat = Quaternion.CreateFromYawPitchRoll(
            mobyInstance.Rotation.X * Deg2Rad,
            mobyInstance.Rotation.Y * Deg2Rad,
            mobyInstance.Rotation.Z * Deg2Rad
        );
        Transform = new Transform()
        {
            Translation = mobyInstance.Position,
            Rotation = rotationQuat,
            Scale = new(mobyInstance.Scale)
        };

        var (center, radius) = BaseMoby.GetBoundingSphere();
        BoundingSphere = new(center + mobyInstance.Position, radius);

        Name = !string.IsNullOrEmpty(mobyInstance.Name) ? mobyInstance.Name.Split('/')[^1] : $"Moby_{BaseMoby.Id:X}_{mobyInstance.Group}";

        if (!assetManager.Mobys.TryGetValue(BaseMoby.Id, out Model[]? value))
            return;
        Models = value;
    }

    public override void Draw(BasicForwardRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if(!allowRender || !EntityManager.Singleton.renderMobys)
            return;

        if (Program.Settings.FrustrumCulling && !camera.GetFrustum().ContainsSphere(BoundingSphere.GetXYZ(), BoundingSphere.W))
            return;

        if (Models is null)
            return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        if (IsDirty)
        {
            cachedRenderables.Clear();
            foreach (Model model in Models)
            {
                cachedRenderables.Clear();
                foreach (var mesh in model.Meshes)
                    cachedRenderables.Add(new Renderable(mesh, Transform));
            }
            IsDirty = false;
        }

        foreach (var renderable in cachedRenderables)
        {
            renderer.DrawRenderable(renderable);
        }

        EntitiesRenderedThisFrame++;
    }
    
    public override void DrawPicking(
        BasicForwardRenderer renderer,
        OutputDescription outputDescription,
        CommandList commandList,
        Cam3D camera,
        ImmediateRenderer immediateRenderer,
        Effect pickingEffect,
        uint objectId,
        List<MaterialOverrideState> restoreList)
    {
        if(!allowRender || !EntityManager.Singleton.renderMobys)
            return;

        if (Program.Settings.FrustrumCulling && !camera.GetFrustum().ContainsSphere(BoundingSphere.GetXYZ(), BoundingSphere.W))
            return;

        if (Models is null)
            return;

        if (IsDirty)
        {
            cachedRenderables.Clear();
            foreach (Model model in Models)
            {
                cachedRenderables.Clear();
                foreach (var mesh in model.Meshes)
                    cachedRenderables.Add(new Renderable(mesh, Transform));
            }
            IsDirty = false;
        }

        foreach (var renderable in cachedRenderables)
        {
            var mat = renderable.Mesh.Material;
            restoreList.Add(new MaterialOverrideState(mat, mat.Effect, mat.Parameters));
            mat.Effect = pickingEffect;
            mat.Parameters = [objectId, 0f, 0f, 0f];
            renderer.DrawRenderable(renderable);
        }
    }
}
