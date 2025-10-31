using System.Numerics;

namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Represents a 3D model composed of multiple meshes
/// </summary>
public interface IModel : IAsset
{
    /// <summary>
    /// All meshes in this model
    /// </summary>
    IReadOnlyList<IMesh> Meshes { get; }

    /// <summary>
    /// Model scale factor
    /// </summary>
    float Scale { get; }

    /// <summary>
    /// Gets the overall bounding sphere
    /// </summary>
    (Vector3 center, float radius) GetBoundingSphere();
}
