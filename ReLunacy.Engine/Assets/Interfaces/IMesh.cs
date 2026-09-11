namespace ReLunacy.Engine.Assets.Interfaces;

public interface IMesh
{
    IGeometry Geometry { get; }
    IMaterial Material { get; }
    string? Name { get; }

    // For the Asset Viewer's raw-vertex inspector; null where unsupported (e.g. UFrags).
    // VertexFormatName identifies the raw vertex struct this mesh was read with; VertexDumper
    // returns a formatted dump of one vertex's raw+decoded fields by index, or null if out of range.
    string? VertexFormatName { get; }
    Func<int, string?>? VertexDumper { get; }
}
