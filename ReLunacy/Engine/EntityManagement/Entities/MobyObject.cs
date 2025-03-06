using LibLunacy.Objects.Instances;
using ReLunacy.Engine.Rendering.Alister;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.EntityManagement.Entities;

public class MobyObject : Entity
{
    public override EntityType EntityType { get; init; } = EntityType.Moby;

    public MobyInstance Instance { get; private set; }
    public Moby Asset { get; init; }

    public MobyObject(MobyInstance mobyInstance)
    {
        Instance = mobyInstance;
        Asset = mobyInstance.Moby;
        Transform = new Transform(
            mobyInstance.instanceData.Position * YardToMeter,
            mobyInstance.instanceData.Rotation,
            Vec3.One * mobyInstance.instanceData.Scale * YardToMeter
        );
        name = mobyInstance.name;
        boundingSphere = new(mobyInstance.Moby.BoundingSphere.XYZ * YardToMeter + Transform.Position, mobyInstance.Moby.BoundingSphere.W * mobyInstance.instanceData.Scale);

        var models = new List<List<Model>>();
        foreach(var bangle in Asset.Bangles)
        {
            var list = new List<Model>();
            foreach (var mesh in bangle.meshes)
            {
                list.Add(new Model(new Drawable(mesh, AssetManager.Singleton.Materials[mesh.shaderIndex], MaterialManager.SelectedVolumeMat), mesh.boneWeight, mesh.vertToBonemap));
            }
            models.Add(list);
        }

        Model = new Model(models);
    }
}
