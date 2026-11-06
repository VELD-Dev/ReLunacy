using System.Numerics;

namespace ReLunacy.Engine.Assets.Navigation;

/// <summary>
/// One island of a zone's navigation mesh: a planar subdivision of walkable floor, in WORLD space
/// (navmesh vertices are not relative to anything, unlike UFrag geometry).
/// </summary>
public sealed class NavmeshChunk
{
    /// <summary>This chunk's index in the zone's chunk table, which is what the edges' neighbour
    /// fields name.</summary>
    public required int Index { get; init; }

    /// <summary>Centre and radius as the game stores them, not as recomputed here.</summary>
    public required Vector4 BoundingSphere { get; init; }

    public required Vector3[] Vertices { get; init; }

    /// <summary>Three vertex indices per face. Every face of every retail chunk is a triangle.</summary>
    public required int[] TriangleIndices { get; init; }

    /// <summary>The unique edges, as stored - NOT the three edges of each triangle, so a shared edge
    /// appears once. This is what the wireframe draws.</summary>
    public required (ushort A, ushort B)[] Edges { get; init; }

    /// <summary>For each edge, the chunk on its far side, or null at a boundary. Not drawn; this is
    /// the chunk-to-chunk connectivity a pathfinder would walk.</summary>
    public required int?[] EdgeNeighbours { get; init; }
}

/// <summary>
/// A zone's navigation mesh.
///
/// DECODED 2026-11-06 from zone sections 0x19000 (chunk table) and 0x19200 (chunk data), which occur
/// together in 25 zones over all 55 retail new-engine levels and in NO zone holding art. See
/// docs/reverse/newengine_special_zones.md for the layout and the evidence; in short, the chunk
/// table's 0x40-byte records give a vertex/edge/face count, a 128-byte-aligned offset into the data
/// blob and a bounding sphere, and at that offset sit the chunk's vertices, then its edges, then its
/// faces as triples of edge indices.
///
/// Held per zone rather than per level because that is how the game stores it, and because a zone is
/// the unit the editor shows and hides.
/// </summary>
public sealed class Navmesh
{
    public required IReadOnlyList<NavmeshChunk> Chunks { get; init; }

    public int VertexCount => Chunks.Sum(c => c.Vertices.Length);
    public int TriangleCount => Chunks.Sum(c => c.TriangleIndices.Length) / 3;
    public int EdgeCount => Chunks.Sum(c => c.Edges.Length);
}
