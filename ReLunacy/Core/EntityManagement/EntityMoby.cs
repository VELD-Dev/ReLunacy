using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Transformations;
using LibLunacy.Objects;
using LibLunacy.Objects.Instances;
using LibLunacy.Shaders;
using ReLunacy.Utility;
using System.Numerics;
using Veldrid;

using Shader = LibLunacy.Shaders.Shader;

namespace ReLunacy.Core.EntityManagement;

public class EntityMoby : Entity
{
    public readonly Moby BaseMoby;
    public override Transform Transform { get; protected set; }
    public override string Name { get; protected set; }
    public Model[] Models { get; private set; }

    public override Vector4 BoundingSphere => BaseMoby.BoundingSphere;

    public EntityMoby(MobyInstance mobyInstance, AssetManager assetManager) : base()
    {
        BaseMoby = mobyInstance.Moby;
        var rotationQuat = Quaternion.CreateFromYawPitchRoll(
            mobyInstance.instanceData.Rotation.X,
            mobyInstance.instanceData.Rotation.Y,
            mobyInstance.instanceData.Rotation.Z
        );
        Transform = new Transform()
        {
            Translation = mobyInstance.instanceData.Position,
            Rotation = rotationQuat,
            Scale = new(mobyInstance.instanceData.Scale)
        };

        Name = mobyInstance.name != string.Empty ? mobyInstance.name.Split('/')[^1] : $"Moby_{BaseMoby.TUID:X}_{(mobyInstance.metadata is not null ? mobyInstance.metadata?.group : ID)}";

        if (!assetManager.Mobys.ContainsKey(mobyInstance.TUID))
            return;
        Models = assetManager.Mobys[mobyInstance.TUID];
    }

    public override void Draw(OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if(!allowRender || !EntityManager.Singleton.renderMobys)
            return;

        if (Program.Settings.FrustrumCulling && !camera.GetFrustum().ContainsSphere(BoundingSphere.GetXYZ(), BoundingSphere.W))
            return;

        if (Models is null)
            return;

        foreach (Model model in Models)
        {
            model.Draw(commandList, Transform, outputDescription);
        }

        EntitiesRenderedThisFrame++;
    }
}
