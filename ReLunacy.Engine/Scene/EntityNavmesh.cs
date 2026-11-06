using System.Numerics;
using ReLunacy.Engine.Assets.Navigation;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// One chunk of a zone's navigation mesh, as a selectable entity.
///
/// Per CHUNK and not per navmesh, because the chunk is the unit the data itself is built around: it
/// is what carries a bounding sphere, what the edges' neighbour fields point at, and what a
/// pathfinder would hand off between. A zone has one navmesh but tens to hundreds of chunks, so this
/// is also the granularity at which selecting something is useful.
///
/// It has no renderables of its own. The navmesh is drawn by VulkanRenderer.Navmesh.cs as one merged
/// buffer per level, and selecting a chunk tells that pass which index range to re-draw highlighted
/// (see <see cref="FirstIndex"/>). Returning renderables here instead would mean uploading the same
/// triangles a second time per chunk.
/// </summary>
public sealed class EntityNavmesh : Entity
{
    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    /// <summary>The chunk this entity stands for. Held so the merged navmesh buffer's per-chunk
    /// ranges can be matched to entities by identity: a zone listed by several regions produces one
    /// EntityZone per region, hence several entities over the SAME chunk, and they must all end up
    /// with the one range rather than the geometry being uploaded once per duplicate.</summary>
    public NavmeshChunk Chunk { get; }

    /// <summary>The zone this chunk belongs to, for the hierarchy's grouping and the inspector.</summary>
    public string ZoneName { get; }

    /// <summary>Its index in the zone's chunk table, which is what the edges' neighbour fields name.</summary>
    public int ChunkIndex { get; }

    public int VertexCount { get; }
    public int TriangleCount { get; }
    public int EdgeCount { get; }

    /// <summary>How many of this chunk's edges lead into another chunk rather than ending at a
    /// boundary - i.e. how connected this island is.</summary>
    public int LinkedEdgeCount { get; }

    /// <summary>Where this chunk's triangles start in the level's merged navmesh index buffer, and
    /// how many indices are its own. Set by the View3D when it uploads the merged buffer, since only
    /// it knows the order the chunks were merged in.</summary>
    public int FirstIndex { get; set; } = -1;
    public int IndexCount { get; set; }

    public EntityNavmesh(NavmeshChunk chunk, string zoneName)
    {
        Chunk = chunk;
        ZoneName = zoneName;
        ChunkIndex = chunk.Index;
        VertexCount = chunk.Vertices.Length;
        TriangleCount = chunk.TriangleIndices.Length / 3;
        EdgeCount = chunk.Edges.Length;
        LinkedEdgeCount = chunk.EdgeNeighbours.Count(n => n.HasValue);
        Name = $"{zoneName}/Chunk_{chunk.Index}";

        // The game's own sphere, not a refitted one. Entity.BoundingSphere is LOCAL space and the
        // navmesh is already world-space, so the transform stays at identity and the centre goes in
        // as it is - matching how EntityVolume keeps its sphere centred on its own position.
        BoundingSphere = chunk.BoundingSphere;
    }
}
