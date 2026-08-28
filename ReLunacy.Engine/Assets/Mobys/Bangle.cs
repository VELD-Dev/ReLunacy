using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Mobys;

/// <summary>Group of meshes enabled/disabled at runtime on an <see cref="IMoby"/> - character skins, NPC variations, etc.</summary>
public sealed class Bangle : IBangle
{
    public IReadOnlyList<IMesh> Meshes { get; init; }
    public string? Name { get; init; }

    public Bangle(IReadOnlyList<IMesh> meshes, string? name = null)
    {
        Meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        Name = name;
    }
}
