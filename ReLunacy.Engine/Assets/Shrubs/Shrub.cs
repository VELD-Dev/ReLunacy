using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Shrubs;

/// <inheritdoc cref="IShrub"/>
public sealed class Shrub : IShrub
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded { get; private set; }

    public IReadOnlyList<IMesh> Meshes { get; init; }
    public string? SourcePath { get; init; }
    public ulong MaterialTUID { get; init; }
    public (float Near, float Far) DrawDistances { get; init; }

    private readonly Lazy<(Vector3 center, float radius)>? _boundingSphere;

    public Shrub(ulong id, IReadOnlyList<IMesh> meshes, string? sourcePath = null,
                 (float Near, float Far) drawDistances = default,
                 Func<(Vector3, float)>? boundingSphereCalculator = null,
                 ulong materialTuid = 0)
    {
        Id = id;
        Meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        SourcePath = sourcePath;
        MaterialTUID = materialTuid;
        // The leaf of the asset path is what a user recognises; the full path is kept beside it.
        Name = sourcePath is null
            ? null
            : System.IO.Path.GetFileName(sourcePath).Replace(".entity.irb", "", StringComparison.OrdinalIgnoreCase);
        DrawDistances = drawDistances;
        IsLoaded = true;

        if (boundingSphereCalculator != null)
            _boundingSphere = new Lazy<(Vector3, float)>(boundingSphereCalculator);
    }

    public (Vector3 center, float radius) GetBoundingSphere()
        => _boundingSphere?.Value ?? (Vector3.Zero, 0f);
}
