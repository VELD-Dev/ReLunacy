using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward.Renderables;
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
using Veldrid;

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
        var rotationQuat = Quaternion.CreateFromYawPitchRoll(
            tieInstance.Rotation.X,
            tieInstance.Rotation.Y,
            tieInstance.Rotation.Z
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

    public override void Draw(ForwardRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderTies)
            return;

        if (Program.Settings.FrustrumCulling && !camera.GetFrustum().ContainsSphere(BoundingSphere.GetXYZ(), BoundingSphere.W))
            return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        if(IsDirty)
        {
            cachedRenderables.Clear();
            foreach (var mesh in Model.Meshes)
            {
                cachedRenderables.Add(new Renderable(mesh, Transform));
            }
            IsDirty = false;
        }

        EntitiesRenderedThisFrame++;
    }
}
