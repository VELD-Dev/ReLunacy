namespace ReLunacy.Engine.Assets.Interfaces;

public interface IMesh
{
    IGeometry Geometry { get; }
    IMaterial Material { get; }
    string? Name { get; }

    /// <summary>One material per shader/material variant set of the owning asset, element 0 being
    /// <see cref="Material"/>; empty when the asset has no variants. See
    /// Docs/reverse/NEW_ENGINE_MOBY_CLASS_2026-09-19.md.</summary>
    IReadOnlyList<IMaterial> MaterialVariants => [];

    // For the Asset Viewer's raw-vertex inspector - null where a mesh's source format doesn't
    // (yet) support this (e.g. UFrags). VertexFormatName identifies which raw vertex struct this
    // mesh was read with (VertexFormat0/1, old vs. new engine); DumpVertex returns a formatted
    // dump of one vertex's raw+decoded fields by index, or null if out of range.
    string? VertexFormatName { get; }
    Func<int, string?>? VertexDumper { get; }
}
