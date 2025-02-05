using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.EntityManagement.Entities;

public class VolumeObject : Entity
{
    public override EntityType EntityType { get; init; } = EntityType.Volume;
    public Volume Instance { get; init; }

    public VolumeObject(Volume volumeInstance)
    {
        Instance = volumeInstance;
        ID = InstancesCount;
        InstancesCount++;
        Transform = new Transform(
            volumeInstance.position * YardToMeter,
            volumeInstance.rotation,
            volumeInstance.scale * YardToMeter
        );
        name = volumeInstance.name;
        boundingSphere = new(volumeInstance.position * YardToMeter, volumeInstance.scale.Length * YardToMeter);
    }
}
