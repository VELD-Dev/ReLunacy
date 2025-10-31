namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Represents a single mesh with material assignment
/// </summary>
public interface IMesh
{
    /// <summary>
    /// Geometry data for this mesh
    /// </summary>
    IGeometry Geometry { get; }

    /// <summary>
    /// Material applied to this mesh
    /// </summary>
    IMaterial Material { get; }

    /// <summary>
    /// Mesh name or identifier
    /// </summary>
    string? Name { get; }
}
