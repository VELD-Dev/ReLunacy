using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Transformations;
using LibLunacy.Objects;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Veldrid;

namespace ReLunacy.Core.EntityManagement;

public class EntityUFrag : Entity
{
    public readonly UFrag UFrag;
    public override Transform Transform { get; protected set; }
    public override Vector4 BoundingSphere => UFrag.metadata.boundingSphere;
    public override string Name { get; protected set; }
    
    public Mesh UFragMesh { get; protected set; }

    public EntityUFrag(GraphicsDevice gd, UFrag ufrag, AssetManager assetManager) : base()
    {
        UFrag = ufrag;
        Name = $"UFrag_{ID}";

        UFragMesh = new(gd, assetManager.Materials[ufrag.metadata.shaderIndex], ufrag.vertices.ToVert3D(), ufrag.indices);
        Transform = new Transform()
        {
            Translation = UFrag.metadata.position / 0x100,
            Rotation = Quaternion.Identity,
            Scale = new Vector3(1f / 0x100)
        };
    }

    public override void Draw(OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderUFrags)
            return;

        if (Program.Settings.FrustrumCulling && !camera.GetFrustum().ContainsSphere(BoundingSphere.GetXYZ(), BoundingSphere.W))
            return;

        UFragMesh.Draw(commandList, Transform, outputDescription);
    }

    public override void Dispose()
    {
        UFragMesh.Dispose();
        GC.SuppressFinalize(this);
    }
}
