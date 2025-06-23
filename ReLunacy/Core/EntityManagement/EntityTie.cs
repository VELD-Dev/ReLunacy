using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Transformations;
using LibLunacy.Objects;
using LibLunacy.Objects.Instances;
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
    public readonly Tie BaseTie;

    public override Transform Transform { get => throw new NotImplementedException(); protected set => throw new NotImplementedException(); }
    public override Vector4 BoundingSphere { get; protected set; }
    public override string Name { get; protected set; }

    public Model Model { get; private set; }

    public EntityTie(TieInstance tieInstance, AssetManager assetManager, LunaLoader loader): base()
    {
        BaseTie = loader.Ties[tieInstance.tieIndex];
        Transform = new()
        {
            Translation = tieInstance.transform.ExtractTranslation(),
            Rotation = tieInstance.transform.ExtractRotation(),
            Scale = tieInstance.transform.ExtractScale()
        };

        BoundingSphere = tieInstance.boundingSphere;

        Name = $"{BaseTie.Name}_{ID}";

        Model = assetManager.Ties[tieInstance.tieIndex];
    }

    public override void Draw(OutputDescription outputDescription, CommandList commandList, Cam3D camera)
    {
        if (!camera.GetFrustum().ContainsSphere(BoundingSphere.GetXYZ(), BoundingSphere.W))
            return;

        Model.Draw(commandList, Transform, outputDescription);
    }
}
