using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Transformations;
using LibLunacy.Experimental.Core.Interfaces;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Bliss.CSharp.Effects;
using Veldrid;
using Vortice.Mathematics;

namespace ReLunacy.Core.EntityManagement;

public class EntityTie : Entity
{
    public readonly ITie BaseTie;

    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public Model Model { get; private set; }

    public EntityTie(IPlacedInstance<ITie> tieInstance, AssetManager assetManager): base()
    {
        BaseTie = tieInstance.Asset;
        const float Deg2Rad = MathF.PI / 180f;
        var rotationQuat = Quaternion.CreateFromYawPitchRoll(
            tieInstance.Rotation.X * Deg2Rad,
            tieInstance.Rotation.Y * Deg2Rad,
            tieInstance.Rotation.Z * Deg2Rad
        );
        Transform = new Transform()
        {
            Translation = tieInstance.Position,
            Rotation = rotationQuat,
            Scale = new(tieInstance.Scale)
        };

        var (center, radius) = BaseTie.GetBoundingSphere();
        BoundingSphere = new(center + tieInstance.Position, radius);

        Name = !string.IsNullOrEmpty(BaseTie.Name) ? $"{BaseTie.Name.Split('/')[^1]}_{ID}" : $"Tie_{BaseTie.Id:X}_{ID}";

        if (!assetManager.Ties.ContainsKey(BaseTie.Id))
            return;
        Model = assetManager.Ties[BaseTie.Id];
    }

    public override void Draw(BasicForwardRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        //immediateRenderer.DrawSphere(commandList, outputDescription, Transform, 1f, 4, 4, Bliss.CSharp.Colors.Color.Cyan);
        if (!allowRender || !EntityManager.Singleton.renderTies)
            return;

        if (Program.Settings.FrustrumCulling && !camera.GetFrustum().ContainsSphere(BoundingSphere.GetXYZ(), BoundingSphere.W))
            return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        if (selected)
            DrawSelectionHighlight(outputDescription, commandList, immediateRenderer);

        if(IsDirty)
        {
            cachedRenderables.Clear();
            foreach (var mesh in Model.Meshes)
            {
                cachedRenderables.Add(new Renderable(mesh, Transform));
            }
            IsDirty = false;
        }

        foreach(var renderable in cachedRenderables)
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
        if (!allowRender || !EntityManager.Singleton.renderTies)
            return;

        if (Program.Settings.FrustrumCulling && !camera.GetFrustum().ContainsSphere(BoundingSphere.GetXYZ(), BoundingSphere.W))
            return;

        if(IsDirty)
        {
            cachedRenderables.Clear();
            foreach (var mesh in Model.Meshes)
            {
                cachedRenderables.Add(new Renderable(mesh, Transform));
            }
            IsDirty = false;
        }

        var pickingMaterial = new Material(pickingEffect);
        pickingMaterial.Parameters = [objectId, 0f, 0f, 0f];

        foreach (var mesh in Model.Meshes)
        {
            renderer.DrawRenderable(new Renderable(mesh, Transform, pickingMaterial));
        }
    }
}
