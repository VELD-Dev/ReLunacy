using System.Numerics;

namespace ReLunacy.Engine.Loading.Interfaces;

public interface IMobyInstance
{
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public float Scale { get; set; }
    public ushort MobyIndex { get; set; }
    /// <summary>Distance (in-game units) beyond which the game itself stops rendering this instance. Raw file value; &lt;= 0 means unlimited.</summary>
    public float DisplayDistance { get; set; }
    /// <summary>Distance (in-game units) beyond which the game stops updating this instance's logic. Raw file value; &lt;= 0 means unlimited.</summary>
    public float UpdateDistance { get; set; }
}
