using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering.Alister;

public class Model : IRenderable
{
    public uint id;
    public float[] Vertices { get; set; }
    public uint[] Indices { get; set; }
    public uint[] boneWeight;
    public uint[] vertToBoneMap;

    public Material material;

    private List<IMesh> meshes;
    private List<Drawable> drawableList;

    public int VertexCount => Vertices.Length;

    public int IndicesCount => Indices.Length;

    public int FacesCount => Indices.Length / 3;

    public Model(uint id, Material mat)
    {
        this.id = id;
        material = mat;
    }

    public void AddMesh(IMesh mesh)
    {
        Indices = [.. Indices, .. mesh.indices.Select(i => i + (uint)Vertices.Length)];
        Vertices = [.. Vertices, .. mesh.vpos];
        boneWeight = [.. boneWeight, .. mesh.boneWeight];
        vertToBoneMap = [.. vertToBoneMap, .. mesh.vertToBonemap];
        meshes.Add(mesh);
        drawableList.Add(new Drawable(mesh, material, MaterialManager.SelectedVolumeMat));
    }

    public void RemoveMesh(int index)
    {
        throw new NotImplementedException("Hahaha go fuck yourself I'm not crazy I'm not doing this shit lmao");
    }


}
