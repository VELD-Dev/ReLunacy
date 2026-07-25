using System.Numerics;

namespace ReLunacy.Engine.Loading.Interfaces;

public interface IMobyInstance
{
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public float Scale { get; set; }
    public ushort MobyIndex { get; set; }
}
