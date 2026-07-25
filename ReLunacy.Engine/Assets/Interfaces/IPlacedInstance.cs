using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IPlacedInstance<out TAsset> where TAsset : IAsset
{
    TAsset Asset { get; }
    Vector3 Position { get; }
    /// <summary>ZYX Euler angles. Radians for Mobys (raw from file); unused for Ties, which carry an exact placement matrix instead — see <see cref="GetTransformMatrix"/>.</summary>
    Vector3 Rotation { get; }
    float Scale { get; }
    public ulong ID { get; set; }
    /// <summary>Often found in debug.dat.</summary>
    public string Name { get; set; }
    /// <summary><c>0</c> on old engine.</summary>
    public ushort Group { get; init; }
    Matrix4x4 GetTransformMatrix();
}
