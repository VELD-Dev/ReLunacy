using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityUFrag : Entity
{
    /// <summary>UFragVertex's raw x/y/z are fixed-point shorts quantised x256; Transform.Scale
    /// divides it back out.</summary>
    private const float UFragQuantisation = 256f;

    public readonly IUFrag UFrag;
    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public RenderMesh UFragMesh { get; protected set; }

    public EntityUFrag(IUFrag ufrag, AssetManager assetManager)
    {
        UFrag = ufrag;
        Name = !string.IsNullOrEmpty(ufrag.Name) ? $"{ufrag.Name}_{ID}" : $"UFrag_{ID}";

        var vertices = ConvertUFragToVertices(ufrag, ufrag.Material.UsesVertexAlpha);
        var indices = ufrag.GetIndices();

        // Lightmap index splits the material cache so UFrags sharing a shader but not a lightmap
        // get distinct Materials.
        var material = assetManager.GetOrBuildMaterial(ufrag.Material, ufrag.LightmapIndex);
        UFragMesh = new RenderMesh(vertices, indices, material);

        if (vertices.Length > 0 && indices.Length >= 3)
            Rendering.Vulkan.VulkanSceneCapture.Register(UFragMesh, Rendering.Vulkan.VulkanSceneCapture.Interleave(vertices), indices);

        // GetAnchor() is the chunk's world-space placement anchor - local (0,0,0) of the mesh maps
        // here. Not the same as GetBoundingCenter(), the true (non-grid-aligned) bounding-sphere
        // center used for culling below.
        var anchor = ufrag.GetAnchor();
        Transform = new Transform { Translation = anchor, Rotation = Quaternion.Identity, Scale = Vector3.One / UFragQuantisation };
        // BoundingSphere is local space (Entity's convention) - the x256 fixed-point space
        // Transform.Scale undoes. The file gives center/radius in world units, so both are
        // multiplied back in here.
        BoundingSphere = new Vector4(
            (ufrag.GetBoundingCenter() - anchor) * UFragQuantisation,
            ufrag.GetBoundingRadius() * UFragQuantisation);
    }

    private static Vertex3D[] ConvertUFragToVertices(IUFrag ufrag, bool useVertexAlpha)
    {
        var positions = ufrag.GetVertexPositions();
        var uvs = ufrag.GetTextureCoordinates();
        var normals = ufrag.GetNormals();
        var tangents = ufrag.GetTangents();
        var lightmapUVs = ufrag.GetLightmapUVs();
        var vertexAlpha = useVertexAlpha ? ufrag.GetVertexAlpha() : null;

        int vertexCount = positions.Length / 3;
        var vertices = new Vertex3D[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            int posIdx = i * 3;
            int uvIdx = i * 2;

            var position = new Vector3(positions[posIdx], positions[posIdx + 1], positions[posIdx + 2]);
            var uv = new Vector2(uvs[uvIdx], uvs[uvIdx + 1]);
            var normal = normals != null && normals.Length >= posIdx + 3
                ? new Vector3(normals[posIdx], normals[posIdx + 1], normals[posIdx + 2])
                : Vector3.UnitY;
            // Tangent fallback must be a real unit vector, not zero - normalizing a zero vector
            // produces NaN in the lighting shader.
            var tangent = tangents != null && tangents.Length >= i * 4 + 4
                ? new Vector4(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2], tangents[i * 4 + 3])
                : new Vector4(1f, 0f, 0f, 1f);

            // Lightmap UV set (UFragVertex.UVs2); falls back to the base UV when this UFrag has none.
            var lightmapUV = lightmapUVs != null && lightmapUVs.Length >= uvIdx + 2
                ? new Vector2(lightmapUVs[uvIdx], lightmapUVs[uvIdx + 1])
                : uv;

            float alpha = vertexAlpha != null && i < vertexAlpha.Length ? vertexAlpha[i] : 1f;
            vertices[i] = new Vertex3D(position, uv, lightmapUV, normal, tangent, new Vector4(1f, 1f, 1f, alpha));
        }

        return vertices;
    }

    protected override void EnsureRenderables()
    {
        if (!IsDirty || UFragMesh is null) return;
        cachedRenderables.Clear();
        cachedRenderables.Add(new Renderable(UFragMesh, Transform));
        IsDirty = false;
    }

    public override void Dispose()
    {
        // Nothing to release: geometry is owned by the capture registry, cleared on level unload.
        GC.SuppressFinalize(this);
    }
}
