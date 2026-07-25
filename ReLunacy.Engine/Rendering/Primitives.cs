using System.Numerics;
using Bliss.CSharp.Geometry.Meshes;
using Bliss.CSharp.Geometry.Meshes.Data;
using Bliss.CSharp.Graphics.VertexTypes;
using Bliss.CSharp.Materials;
using Veldrith;

namespace ReLunacy.Engine.Rendering;

public static class Primitives
{
    public static Mesh<Vertex3D> CreateCube(GraphicsDevice graphicsDevice, Material material, float size = 1.0f)
        => CreateBox(graphicsDevice, material, new Vector3(size));

    /// <summary>Same as <see cref="CreateCube"/> but with independent X/Y/Z extents — every face's
    /// normal/u/v is a single-axis unit vector, so scaling by a non-uniform half-extents vector
    /// component-wise still lands each corner at the right axis-aligned offset.</summary>
    public static Mesh<Vertex3D> CreateBox(GraphicsDevice graphicsDevice, Material material, Vector3 size)
    {
        Vector3 h = size * 0.5f;

        (Vector3 normal, Vector3 u, Vector3 v)[] faces =
        [
            (new(0, 0, 1), new(1, 0, 0), new(0, 1, 0)),
            (new(0, 0, -1), new(-1, 0, 0), new(0, 1, 0)),
            (new(0, 1, 0), new(1, 0, 0), new(0, 0, -1)),
            (new(0, -1, 0), new(1, 0, 0), new(0, 0, 1)),
            (new(1, 0, 0), new(0, 0, -1), new(0, 1, 0)),
            (new(-1, 0, 0), new(0, 0, 1), new(0, 1, 0)),
        ];

        Vertex3D[] vertices = new Vertex3D[faces.Length * 4];
        uint[] indices = new uint[faces.Length * 6];

        for (int f = 0; f < faces.Length; f++)
        {
            var (normal, u, v) = faces[f];
            Vector3 center = normal * h;
            Vector4 tangent = new(u, 1.0f);

            vertices[f * 4 + 0] = new Vertex3D(center - u * h - v * h, new(0, 1), new(0, 1), normal, tangent, Vector4.One);
            vertices[f * 4 + 1] = new Vertex3D(center + u * h - v * h, new(1, 1), new(1, 1), normal, tangent, Vector4.One);
            vertices[f * 4 + 2] = new Vertex3D(center + u * h + v * h, new(1, 0), new(1, 0), normal, tangent, Vector4.One);
            vertices[f * 4 + 3] = new Vertex3D(center - u * h + v * h, new(0, 0), new(0, 0), normal, tangent, Vector4.One);

            uint baseIndex = (uint)(f * 4);
            int i = f * 6;
            indices[i + 0] = baseIndex + 0;
            indices[i + 1] = baseIndex + 1;
            indices[i + 2] = baseIndex + 2;
            indices[i + 3] = baseIndex + 0;
            indices[i + 4] = baseIndex + 2;
            indices[i + 5] = baseIndex + 3;
        }

        return new Mesh<Vertex3D>(graphicsDevice, material, new BasicMeshData(vertices, indices));
    }

    /// <summary>Builds a single box edge as actual triangle geometry instead of a GPU line
    /// primitive — a pair of thin quads crossed in a "+" through the edge's centerline (one quad
    /// thin along each of the two axes perpendicular to the edge), so it always presents real
    /// screen-space area to a picking pass regardless of view angle, unlike a single flat quad
    /// which can go edge-on and disappear. Same cross-section technique Replanetizer uses for
    /// volume picking, but built as ONE shared unit-length edge (running -0.5 to +0.5 along local
    /// X, constant thickness) meant to be GPU-instanced per real edge (12 per box) with a
    /// per-instance Transform supplying that edge's actual length via non-uniform Scale.X — scale
    /// is applied in local space before rotation (see Transform.GetMatrix()'s Scale*Rotation*
    /// Translation order), so Scale.X always stretches along this mesh's own local length axis
    /// regardless of how the instance is subsequently rotated to align with a real box edge. This
    /// is what makes thickness constant across boxes of any size, since the thickness axes
    /// (Y/Z) are never touched by that per-instance scale — see EntityVolume, which replaced its
    /// old per-volume custom box mesh with instances of this one shared mesh.</summary>
    public static Mesh<Vertex3D> CreateWireEdge(GraphicsDevice graphicsDevice, Material material, float thickness)
    {
        float t = MathF.Max(thickness, 0.001f) * 0.5f;

        var vertices = new List<Vertex3D>(8);
        var indices = new List<uint>(12);

        void AddQuad(Vector3 axisThin, Vector3 normal)
        {
            Vector3 lengthExt = Vector3.UnitX * 0.5f;
            Vector3 thinExt = axisThin * t;
            Vector4 tangent = new(Vector3.UnitX, 1.0f);

            uint baseIndex = (uint)vertices.Count;
            vertices.Add(new Vertex3D(-lengthExt - thinExt, new(0, 1), new(0, 1), normal, tangent, Vector4.One));
            vertices.Add(new Vertex3D(lengthExt - thinExt, new(1, 1), new(1, 1), normal, tangent, Vector4.One));
            vertices.Add(new Vertex3D(lengthExt + thinExt, new(1, 0), new(1, 0), normal, tangent, Vector4.One));
            vertices.Add(new Vertex3D(-lengthExt + thinExt, new(0, 0), new(0, 0), normal, tangent, Vector4.One));

            // The two AddQuad calls below don't share a consistent (length, thin, normal)
            // handedness — for one of them, UnitX x axisThin points opposite the declared
            // `normal`. Flip the two triangles' winding in that case so the front face (by the
            // standard CCW-from-outside convention) always actually faces `normal`, instead of
            // silently depending on RasterizerState being CULL_NONE to hide the mismatch.
            if (Vector3.Dot(Vector3.Cross(Vector3.UnitX, axisThin), normal) < 0f)
            {
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 2); indices.Add(baseIndex + 1);
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 3); indices.Add(baseIndex + 2);
            }
            else
            {
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
                indices.Add(baseIndex + 0); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
            }
        }

        AddQuad(Vector3.UnitY, Vector3.UnitZ);
        AddQuad(Vector3.UnitZ, Vector3.UnitY);

        return new Mesh<Vertex3D>(graphicsDevice, material, new BasicMeshData([.. vertices], [.. indices]));
    }
}
