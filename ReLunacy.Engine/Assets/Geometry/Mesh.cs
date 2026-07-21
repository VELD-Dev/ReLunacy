using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Geometry;

public sealed class Mesh : IMesh
{
    public IGeometry Geometry { get; init; }
    public IMaterial Material { get; init; }
    public string? Name { get; set; }

    public Mesh(IGeometry geometry, IMaterial material, string? name = null)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Material = material ?? throw new ArgumentNullException(nameof(material));
        Name = name;
    }
}
