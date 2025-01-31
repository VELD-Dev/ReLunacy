using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering;

public class DrawableMesh
{
    public IMesh meshData;
    public uint id;
    public float[] vertexPositions;
    public uint[] indices;
    public uint[] boneWeight;
    public uint[] vertToBoneMap;

    public Material material;

    public int VertexCount => vertexPositions.Length;

    public int IndicesCount => indices.Length;

    public int FacesCount => indices.Length / 3;

    public DrawableMesh(ref IMesh data, uint id, Material mat)
    {
        meshData = data;
        this.id = id;
        material = mat;

        vertexPositions = data.vpos;
        indices = data.indices;
        boneWeight = data.boneWeight;
        vertToBoneMap = data.vertToBonemap;
    }
}
