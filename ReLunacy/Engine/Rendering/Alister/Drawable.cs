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
    public readonly int facesCount;
    public readonly Material material;
    public readonly Material wireframeMaterial;

    public float[] UVs { get; private set; }
    public float[] Vpos { get; private set; }
    public uint[] Indices { get; private set; }

    public Drawable(Material material, Material wireframeMaterial)
    {
        UVs = [
            0, 0,
            1, 0,
            0.5f, 1
        ];

        Vpos = [
            -0.5f, -0.5f, 0.0f,
             0.5f, -0.5f, 0.0f,
             0.0f,  0.5f, 0.0f,
        ];
        Indices = [
            0, 1, 2,
        ];
        this.material = material;
        this.wireframeMaterial = wireframeMaterial;
        vertexCount = Vpos.Length / 3;
        facesCount = Indices.Length / 3;

        float[] vertexData = new float[Vpos.Length + UVs.Length];
        int j = 0;
        for(int i = 0; i < Vpos.Length / 3; i++) {
            vertexData[j++] = Vpos[i * 3 + 0];
            vertexData[j++] = Vpos[i * 3 + 1];
            vertexData[j++] = Vpos[i * 3 + 2];
            vertexData[j++] = UVs[i * 2 + 0];
            vertexData[j++] = UVs[i * 2 + 1];
        }

        vao = GL.GenVertexArray();
        GLUtil.CheckGlError("GenVertexArray");
        vbo = GL.GenBuffer();
        GLUtil.CheckGlError("GenBuffer");
        ebo = GL.GenBuffer();
        GLUtil.CheckGlError("GenBuffer");
        GL.BindVertexArray(vao);
        GLUtil.CheckGlError("BindVertexArray");
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GLUtil.CheckGlError("BindBuffer");
        GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float), vertexData, BufferUsageHint.StaticDraw);
        GLUtil.CheckGlError("BufferData");
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GLUtil.CheckGlError("BindBuffer");
        GL.BufferData(BufferTarget.ElementArrayBuffer, Indices.Length * sizeof(uint), Indices, BufferUsageHint.StaticDraw);
        GLUtil.CheckGlError("BufferData");
        // Vertices attributes.
        // Position
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
        GLUtil.CheckGlError("VertexAttribPointer");
        GL.EnableVertexAttribArray(0);
        GLUtil.CheckGlError("EnableVertexAttribArray");
        // UVs
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
        GLUtil.CheckGlError("VertexAttribPointer");
        GL.EnableVertexAttribArray(1);
        GLUtil.CheckGlError("EnableVertexAttribArray");

        GL.BindVertexArray(0);
        GLUtil.CheckGlError("BindVertexArray");
    }

    public Drawable(IMesh sourceMesh, Material material, Material wireframeMaterial) : this(sourceMesh.vpos, sourceMesh.indices, sourceMesh.uvs, material, wireframeMaterial) { }

    public Drawable(float[] vertices, uint[] indices, float[] uvs, Material material, Material wireframeMaterial)
    {
        if(vertices.Length < 3)
        {
            throw new Exception("Drawable has empty vertices array. It requires at least 3 vertices !");
        }
        if(indices.Length < 3)
        {   
            throw new Exception("Drawable has empty indices array. It requires at least 3 indices !");
        }

        Vpos = vertices;
        Indices = indices;
        UVs = uvs;
        this.material = material;
        this.wireframeMaterial = wireframeMaterial;

        vertexCount = Vpos.Length / 3;
        facesCount = indices.Length / 3;

        vao = GL.GenVertexArray();
        vbo = GL.GenBuffer();
        ebo = GL.GenBuffer();

        float[] vertexData = new float[Vpos.Length + UVs.Length];
        int j = 0;
        for(int i = 0; i < Vpos.Length / 3; i++) {
            vertexData[j++] = Vpos[i * 3 + 0];
            vertexData[j++] = Vpos[i * 3 + 1];
            vertexData[j++] = Vpos[i * 3 + 2];
            vertexData[j++] = UVs[i * 2 + 0];
            vertexData[j++] = UVs[i * 2 + 1];
        }

        GL.BindVertexArray(vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float), vertexData, BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        // position
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        // uvs
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        GL.BindVertexArray(0);
    }

    public void Draw(Transform transform, bool drawWireframe = false)
    {
        material.Use();
        material.SetMatrix4x4("model", transform.Matrix);

        GL.BindVertexArray(vao);
        GLUtil.CheckGlError("Bind VAO");
        GL.DrawElements(PrimitiveType.Triangles, 1, DrawElementsType.UnsignedInt, 0);
        GLUtil.CheckGlError("DrawElements");
        GL.BindVertexArray(0);
        GLUtil.CheckGlError("Unbind VAO");
        
        if(drawWireframe)
        {
            wireframeMaterial.Use();
            wireframeMaterial.SetMatrix4x4("model", transform.Matrix);

            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
            GL.BindVertexArray(vao);
            GL.DrawElements(PrimitiveType.Triangles, 1, DrawElementsType.UnsignedInt, 0);
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
