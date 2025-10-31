using System.Numerics;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Experimental.Core.Primitives;

namespace LibLunacy.Experimental.Assets.Geometry;

/// <summary>
/// Concrete model implementation
/// </summary>
public sealed class Model : IModel
{
    private readonly List<IMesh> _meshes;

    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => _meshes.All(m => m.Geometry.IsLoaded && m.Material.IsLoaded);

    public IReadOnlyList<IMesh> Meshes => _meshes;
    public float Scale { get; init; }

    public Model(ulong id, float scale = 1.0f)
    {
        Id = id;
        Scale = scale;
        _meshes = [];
    }

    /// <summary>
    /// Adds a mesh to this model
    /// </summary>
    public void AddMesh(IMesh mesh)
    {
        _meshes.Add(mesh ?? throw new ArgumentNullException(nameof(mesh)));
    }

    /// <summary>
    /// Gets the combined bounding sphere of all meshes
    /// </summary>
    public (Vector3 center, float radius) GetBoundingSphere()
    {
        if (_meshes.Count == 0)
            return (Vector3.Zero, 0f);

        // Calculate the bounding sphere that encompasses all mesh bounding spheres
        var allCenters = _meshes.Select(m => m.Geometry.GetBoundingCenter()).ToArray();
        var center = Vector3.Zero;

        foreach (var c in allCenters)
            center += c;
        center /= allCenters.Length;

        float maxRadius = 0f;
        foreach (var mesh in _meshes)
        {
            var meshCenter = mesh.Geometry.GetBoundingCenter();
            var meshRadius = mesh.Geometry.GetBoundingRadius();
            float dist = Vector3.Distance(center, meshCenter) + meshRadius;
            if (dist > maxRadius)
                maxRadius = dist;
        }

        return (center, maxRadius * Scale);
    }

    /// <summary>
    /// Creates a model from a collection of meshes
    /// </summary>
    public static Model FromMeshes(ulong id, IEnumerable<IMesh> meshes, float scale = 1.0f, string? name = null)
    {
        var model = new Model(id, scale) { Name = name };
        foreach (var mesh in meshes)
        {
            model.AddMesh(mesh);
        }
        return model;
    }
}
