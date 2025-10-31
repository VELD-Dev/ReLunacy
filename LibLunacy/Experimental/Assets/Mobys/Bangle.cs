using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Assets.Mobys;

/// <summary>
/// Represents a Bangle - a group of meshes that can be enabled/disabled at runtime on <see cref="IMoby"/><br/>
/// Used for character skins, NPC variations, etc.
/// </summary>
public sealed class Bangle : IBangle
{
    /// <summary>
    /// Meshes in this bangle
    /// </summary>
    public IReadOnlyList<IMesh> Meshes { get; init; }

    /// <summary>
    /// Bangle name or identifier <br/>I know they don't really have one but it can still be useful for debugging for example
    /// </summary>
    public string? Name { get; init; }

    public Bangle(IReadOnlyList<IMesh> meshes, string? name = null)
    {
        Meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        Name = name;
    }
}
