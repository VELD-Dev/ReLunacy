using System.Numerics;

namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Represents an instance of an asset placed in the world
/// </summary>
/// <typeparam name="TAsset">Type of asset being instanced</typeparam>
public interface IPlacedInstance<out TAsset> where TAsset : IAsset
{
    /// <summary>
    /// The asset being instanced
    /// </summary>
    TAsset Asset { get; }

    /// <summary>
    /// World position
    /// </summary>
    Vector3 Position { get; }

    /// <summary>
    /// World rotation (Euler angles in radians)
    /// </summary>
    Vector3 Rotation { get; }

    /// <summary>
    /// Scale multiplier
    /// </summary>
    float Scale { get; }

    /// <summary>
    /// Index or TUID of the instance.
    /// </summary>
    public ulong ID { get; set; }

    /// <summary>
    /// Name of the instance. Often found in debug.dat.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Group of the instance. <c>0</c> on old engine
    /// </summary>
    public ushort Group { get; init; }

    /// <summary>
    /// Gets the transformation matrix
    /// </summary>
    Matrix4x4 GetTransformMatrix();
}
