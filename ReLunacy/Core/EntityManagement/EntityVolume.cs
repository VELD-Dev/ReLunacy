using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Transformations;
using LibLunacy.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Veldrid;

namespace ReLunacy.Core.EntityManagement;

public class EntityVolume : Entity
{
    public readonly Volume BaseVolume;

    public override Transform Transform { get; protected set; }

    public override Vector4 BoundingSphere => Vector4.Zero;
    public BoundingBox boundingBox;

    public override string Name { get => throw new NotImplementedException(); protected set => throw new NotImplementedException(); }

    public EntityVolume(Volume volume, GraphicsDevice gd) : base()
    {
        BaseVolume = volume;

        Transform = new()
        {
            Translation = volume.position,
            Rotation = volume.rotation,
            Scale = Vector3.One
        };
        boundingBox = new(-(volume.scale / 2f), (volume.scale / 2f));
    }

    public override void Draw(OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderVolumes)
            return;

        if (Program.Settings.FrustrumCulling && !camera.GetFrustum().ContainsOrientedBox(boundingBox, Transform.Translation, Transform.Rotation))
            return;

        immediateRenderer.DrawCubeWires(commandList, outputDescription, Transform, BaseVolume.scale, (selected ? Color.White : Color.DarkYellow));
        EntitiesRenderedThisFrame++;
        //Cube.Draw(commandList, Transform, outputDescription);
    }
}
