using LibLunacy.Objects.Instances;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.EntityManagement.Entities;

public class TieObject : Entity
{
    public override EntityType EntityType { get; init; } = EntityType.Tie;

    public TieInstance Instance { get; init; }
    public Tie Asset { get; init; }

    public TieObject(TieInstance tieInstance)
    {
        Asset = Window.Singleton.AssetLoader.Ties[tieInstance.tieIndex];
        Instance = tieInstance;
        ID = InstancesCount;
        InstancesCount++;
        Transform = new Transform(tieInstance.transform);
        name = Asset.Name == string.Empty ? $"Tie_{ID}" : $"{Asset.Name}_{ID}";
        boundingSphere = tieInstance.boundingSphere * YardToMeter;
    }
}
