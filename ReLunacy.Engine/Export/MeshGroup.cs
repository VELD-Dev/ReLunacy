using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Export;

/// <summary>A named group of meshes that should stay a distinct submesh/node on export - a Moby's
/// bangle, or (for assets with no such grouping, e.g. Ties) the whole model as a single group.</summary>
public readonly record struct MeshGroup(string Name, IReadOnlyList<IMesh> Meshes);
