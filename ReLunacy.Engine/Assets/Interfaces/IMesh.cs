namespace ReLunacy.Engine.Assets.Interfaces;

public interface IMesh
{
    IGeometry Geometry { get; }
    IMaterial Material { get; }
    string? Name { get; }

    // For the Asset Viewer's raw-vertex inspector — null where a mesh's source format doesn't
    // (yet) support this (e.g. UFrags). VertexFormatName identifies which raw vertex struct this
    // mesh was read with (VertexFormat0/1, old vs. new engine); DumpVertex returns a formatted
    // dump of one vertex's raw+decoded fields by index, or null if out of range.
    string? VertexFormatName { get; }
    Func<int, string?>? VertexDumper { get; }
}
