using ReLunacy.Engine.Rendering.Alister;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.EntityManagement.Entities;

public class UFragObject : Entity
{
    public override EntityType EntityType { get; init; } = EntityType.UFrag;

    public UFrag Asset { get; init; }

    public UFragObject(UFrag ufrag, ulong zoneId, int ufragIndex)
    {
        Asset = ufrag;
        name = $"UFrag_{zoneId}_{ufragIndex}";
        boundingSphere = ufrag.metadata.boundingSphere / 0x100 * YardToMeter;
        if (ufrag.isOld)
        {
            Transform = new Transform(ufrag.metadata.position / 0x100 * YardToMeter, Vec3.Zero, Vec3.One / 0x100 * YardToMeter);
            boundingSphere.W = 2.5f;
        }
        else
        {
            Transform = new Transform(ufrag.metadata.position * YardToMeter, Vec3.Zero, Vec3.One / 0x100 * YardToMeter);
        }

        Model = new Model(ufrag, AssetManager.Singleton.Materials[ufrag.metadata.shaderIndex]);
    }
}
