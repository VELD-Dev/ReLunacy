using System.Numerics;
using ReLunacy.Engine.Assets.Navigation;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Flattens a level's per-zone navmeshes into the three buffers the renderer draws: one vertex and
/// index buffer for the translucent surface, and one array of edge transforms for the instanced
/// wireframe. Built once per level, not per frame.
/// </summary>
public static class NavmeshGeometry
{
    /// <summary>
    /// Merges every chunk of every navmesh. Navmesh positions are world-space already, so chunks can
    /// share one buffer with no per-chunk matrix and the whole level draws in one call per half.
    /// </summary>
    /// <param name="ranges">Each chunk's [first, count) span in the returned index buffer, so a
    /// selected chunk can be re-drawn on its own without a second upload. Keyed by chunk identity,
    /// not position, because the same chunk can be reached through several regions.</param>
    public static (Vector3[] Vertices, uint[] Indices, Matrix4x4[] Edges) Build(
        IEnumerable<Navmesh> navmeshes, Dictionary<NavmeshChunk, (int First, int Count)>? ranges = null)
    {
        var vertices = new List<Vector3>();
        var indices = new List<uint>();
        var edges = new List<Matrix4x4>();

        foreach (Navmesh navmesh in navmeshes)
        {
            foreach (NavmeshChunk chunk in navmesh.Chunks)
            {
                int baseIndex = vertices.Count;
                int firstIndex = indices.Count;
                vertices.AddRange(chunk.Vertices);
                foreach (int index in chunk.TriangleIndices)
                    indices.Add((uint)(baseIndex + index));
                ranges?.TryAdd(chunk, (firstIndex, indices.Count - firstIndex));

                foreach ((ushort a, ushort b) in chunk.Edges)
                {
                    if (a >= chunk.Vertices.Length || b >= chunk.Vertices.Length) continue;
                    if (EdgeTransform(chunk.Vertices[a], chunk.Vertices[b]) is { } m)
                        edges.Add(m);
                }
            }
        }

        return ([.. vertices], [.. indices], [.. edges]);
    }

    /// <summary>
    /// Places the renderer's shared thin-box edge mesh on the segment a..b. That mesh runs from -0.5
    /// to +0.5 along its local X with its thickness in Y and Z, so this scales X to the segment's
    /// length, turns local +X onto the segment's direction and centres it - the same convention
    /// EntityVolume.ComposeEdgeTransform uses for a trigger volume's 12 edges.
    ///
    /// Null for a zero-length edge, which has no direction to align to. Retail data has none, but a
    /// degenerate edge would otherwise produce a NaN rotation and take the whole draw with it.
    /// </summary>
    private static Matrix4x4? EdgeTransform(Vector3 a, Vector3 b)
    {
        Vector3 delta = b - a;
        float length = delta.Length();
        if (length < 1e-6f) return null;

        Vector3 dir = delta / length;

        // Rotation taking local +X onto dir, via the halfway vector rather than an axis and an angle.
        // The arrangement matters and was measured: an earlier version special-cased "antiparallel"
        // at dot < -0.9999 and snapped those edges to an exact 180-degree turn about Y, which
        // discards the small perpendicular component such an edge still has. On QForce's
        // acid_refinery_pre that misplaced one end of 8 of its 788 edges by up to 1.3% of their
        // length - about a quarter of a unit on a 160-unit edge, i.e. visibly detached from its own
        // triangle. An axis-angle built from acos has the same trouble, because the cross product it
        // needs to normalise vanishes just where the angle is least certain.
        //
        // The halfway form has no special case except true antiparallel, and needs no normalising:
        // +X and h are both unit vectors, so (cross, dot) is already a unit quaternion.
        Vector3 sum = Vector3.UnitX + dir;
        Quaternion rotation;
        if (sum.LengthSquared() < 1e-12f)
            // Exactly antiparallel: any perpendicular axis does, and the edge mesh is radially
            // symmetric so which one is immaterial.
            rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        else
        {
            Vector3 h = Vector3.Normalize(sum);
            rotation = new Quaternion(Vector3.Cross(Vector3.UnitX, h), Vector3.Dot(Vector3.UnitX, h));
        }

        return Matrix4x4.CreateScale(length, 1f, 1f)
             * Matrix4x4.CreateFromQuaternion(rotation)
             * Matrix4x4.CreateTranslation((a + b) * 0.5f);
    }
}
