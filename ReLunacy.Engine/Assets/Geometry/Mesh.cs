using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Geometry;

public sealed class Mesh : IMesh
{
    public IGeometry Geometry { get; init; }
    public IMaterial Material { get; init; }
    public string? Name { get; set; }
    public string? VertexFormatName { get; init; }
    public Func<int, string?>? VertexDumper { get; init; }

    public Mesh(IGeometry geometry, IMaterial material, string? name = null, string? vertexFormatName = null, Func<int, string?>? vertexDumper = null)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Material = material ?? throw new ArgumentNullException(nameof(material));
        Name = name;
        VertexFormatName = vertexFormatName;
        VertexDumper = vertexDumper;
    }
}
