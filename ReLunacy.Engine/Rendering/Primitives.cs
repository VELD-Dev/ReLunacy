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
    {
        float h = size * 0.5f;

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
}
