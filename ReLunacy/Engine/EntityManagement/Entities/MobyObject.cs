using LibLunacy.Objects.Instances;
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
        Asset = Window.Singleton.AssetLoader.Mobys[mobyInstance.TUID];
        ID = InstancesCount;
        InstancesCount++;
        Transform = new Transform(
            mobyInstance.instanceData.Position * YardToMeter,
            mobyInstance.instanceData.Rotation,
            Vec3.One * mobyInstance.instanceData.Scale * YardToMeter
        );
        name = mobyInstance.name;
        boundingSphere = new(mobyInstance.Moby.BoundingSphere.XYZ * YardToMeter + Transform.Position, mobyInstance.Moby.BoundingSphere.W * mobyInstance.instanceData.Scale);
    }
}
