using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering.Alister;

public class Model : IRenderable, IDisposable
{
    public float[] Vertices { get; set; }
    public uint[] Indices { get; set; }
    public uint[] boneWeight;
    public uint[] vertToBoneMap;
    public bool allowRender = true;

    public Material? Material => Drawable?.material;
    public IMesh? sourceMesh { get; private set; }
    public Drawable? Drawable { get; private set; }
    public List<Model> Children = [];
    public int VertexCount
    {
        get
        {
            var sum = Vertices.Length;
            foreach (var child in Children)
            {
                sum += child.VertexCount;
            }
            return sum;
        }
    }

    public int IndicesCount
    {
        get
        {
            var sum = Indices.Length;
            foreach (var child in Children)
            {
                sum += child.IndicesCount;
            }
            return sum;
        }
    }
    public int FacesCount => Indices.Length / 3;
    public int StaticVerticesCount { get; private set; } = 0;
    public int StaticIndicesCount { get; private set; } = 0;
    public int StaticFacesCount { get; private set; } = 0;

    public Model(IMesh mesh, Material mat)
    {
        SetMesh(mesh, mat);
    }

    public Model(Drawable drawable, uint[] boneWeights, uint[] vertBoneMap)
    {
        Drawable = drawable;
        Vertices = drawable.Vpos;
        Indices = drawable.Indices;
        boneWeight = boneWeights;
        vertToBoneMap = vertBoneMap;
        RefreshStaticCounts();
    }

    public Model(List<Drawable> drawableList)
    {
        foreach (var drawable in drawableList)
        {
            Children.Add(new Model(drawable, [], []));
        }
        Vertices = [];
        Indices = [];
        boneWeight = [];
        vertToBoneMap = [];
        RefreshStaticCounts();
    }

    public Model(List<Model> modelList)
    {
        Children = modelList;
        Vertices = [];
        Indices = [];
        boneWeight = [];
        vertToBoneMap = [];
        RefreshStaticCounts();
    }

    public Model(List<List<Model>> modelListList)
    {
        foreach(var modList in modelListList)
        {
            Children.Add(new Model(modList));
        }
        Vertices = [];
        Indices = [];
        boneWeight = [];
        vertToBoneMap = [];
        RefreshStaticCounts();
    }

    public void SetMesh(IMesh mesh, Material mat)
    {
        Indices = mesh.indices;
        Vertices = mesh.vpos;
        boneWeight = mesh.boneWeight;
        vertToBoneMap = mesh.vertToBonemap;
        sourceMesh = mesh;
        Drawable = new Drawable(mesh, mat, MaterialManager.SelectedVolumeMat);
        RefreshStaticCounts();
    }

    public void AddMesh(IMesh mesh, Material mat)
    {
        Children.Add(new Model(mesh, mat));
        RefreshStaticCounts();
    }

    public void RefreshStaticCounts()
    {
        StaticVerticesCount = VertexCount;
        StaticIndicesCount = IndicesCount;
        StaticFacesCount = FacesCount;
    }

    public void RemoveMesh(int index)
    {
        Children.RemoveAt(index);
        RefreshStaticCounts();
    }

    public void Draw(Transform transform, bool wireframe = false)
    {
        if (!allowRender) return;

        if(Drawable is not null)
        {
            Drawable.Draw(transform, wireframe);
        }
        else
        {
            foreach (var child in Children)
            {
                child.Draw(transform, wireframe);
            }
        }
    }

    public void Dispose()
    {
        Drawable?.Dispose();
        Material?.Dispose();
        foreach (var child in Children)
        {
            child.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
