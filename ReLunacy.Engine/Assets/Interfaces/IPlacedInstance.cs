using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IPlacedInstance<out TAsset> where TAsset : IAsset
{
    TAsset Asset { get; }
    Vector3 Position { get; }
    /// <summary>ZYX Euler angles. Radians for Mobys (raw from file); unused for Ties, which carry an exact placement matrix instead - see <see cref="GetTransformMatrix"/>.</summary>
    Vector3 Rotation { get; }
    float Scale { get; }
    public ulong ID { get; set; }
    /// <summary>Often found in debug.dat.</summary>
    public string Name { get; set; }
    /// <summary><c>0</c> on old engine.</summary>
    public ushort Group { get; init; }
    /// <summary>Distance (in-game units) beyond which the game itself stops rendering this instance. &lt; 0 means unlimited. Only Mobys carry this from the file; other instance types default to unlimited.</summary>
    public float DisplayDistance { get; init; }

    /// <summary>Distance (in-game units) beyond which the game stops updating this instance's logic
    /// (separate from <see cref="DisplayDistance"/>, which only gates rendering). &lt; 0 means
    /// unlimited. Only Mobys carry this from the file; other instance types default to unlimited.</summary>
    public float UpdateDistance { get; init; }

    /// <summary>This instance's entry in the level's baked lighting lists, or 0xFFFF for none.
    /// Per-instance, not per-asset. Only ties populate it today.</summary>
    public ushort LightmapIndex { get; init; }

    Matrix4x4 GetTransformMatrix();
}
