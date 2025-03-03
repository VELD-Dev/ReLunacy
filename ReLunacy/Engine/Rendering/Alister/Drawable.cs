using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering.Alister;

public class Drawable : IDisposable
{
    private readonly int vao;
    private readonly int vbo;
    private readonly int ebo;
    public readonly int vertexCount;
    private readonly Material material;
    private readonly Material wireframeMaterial;

    public Drawable(IMesh sourceMesh, Material material, Material wireframeMaterial) : this(sourceMesh.vpos, sourceMesh.indices, material, wireframeMaterial)
    {
      
    }

    public Drawable(float[] vertices, uint[] indices, Material material, Material wireframeMaterial)
    {
        this.material = material;
        this.wireframeMaterial = wireframeMaterial;

        vertexCount = indices.Length;

        vao = GL.GenVertexArray();
        vbo = GL.GenBuffer();
        ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        GL.BindVertexArray(0);
    }

    public void Draw(Transform transform, bool drawWireframe = false)
    {
        material.Use();
        material.SetMatrix4x4("model", transform.Matrix);

        GL.BindVertexArray(vao);
        GL.DrawElements(PrimitiveType.Triangles, vertexCount, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);
        
        if(drawWireframe)
        {
            wireframeMaterial.Use();
            wireframeMaterial.SetMatrix4x4("model", transform.Matrix);

            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
            GL.BindVertexArray(vao);
            GL.DrawElements(PrimitiveType.Triangles, vertexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        }
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(vao);
        GL.DeleteBuffer(vbo);
        GL.DeleteBuffer(ebo);

        GC.SuppressFinalize(this);
    }
}
