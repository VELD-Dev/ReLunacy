using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityUFrag : Entity
{
    /// <summary>UFragVertex's raw x/y/z are fixed-point shorts quantised x256 on both engines, so the
    /// mesh lives in that space and Transform.Scale divides it back out.</summary>
    private const float UFragQuantisation = 256f;

    public readonly IUFrag UFrag;
    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public RenderMesh UFragMesh { get; protected set; }

    public EntityUFrag(IUFrag ufrag, AssetManager assetManager)
    {
        UFrag = ufrag;
        Name = !string.IsNullOrEmpty(ufrag.Name) ? $"{ufrag.Name}_{ID}" : $"UFrag_{ID}";

        var vertices = ConvertUFragToVertices(ufrag);
        var indices = ufrag.GetIndices();

        // Passing the lightmap index is what makes UFrags sharing a shader but not a lightmap get
        // distinct Materials - see AssetManager.GetOrBuildMaterial. Until the 0x5400/0x5410
        // textures are actually built and bound this only splits the cache; it is the seam the
        // baked lighting hangs off, and getting it wrong later would silently give every UFrag one
        // shared lightmap.
        var material = assetManager.GetOrBuildMaterial(ufrag.Material, ufrag.LightmapIndex);
        UFragMesh = new RenderMesh(vertices, indices, material);

        // New-renderer geometry registry (Stage 11+): UFrags build their mesh here rather than via
        // AssetManager.BuildModel, so register it too or the raw-Vulkan renderer never sees UFrag
        // geometry. Keyed by this mesh instance; interleaved pos+uv+normal. See VulkanSceneCapture.
        if (vertices.Length > 0 && indices.Length >= 3)
            Rendering.Vulkan.VulkanSceneCapture.Register(UFragMesh, Rendering.Vulkan.VulkanSceneCapture.Interleave(vertices), indices);

        // UFragVertex's raw per-vertex x/y/z are fixed-point shorts quantized x256 on BOTH
        // engines (master's UFragVertex/OldUFragVertex structs are laid out identically - no
        // engine-specific scale) - using Scale = Vector3.One for new engine was rendering every
        // chunk's mesh 256x too large relative to its own bounding radius.
        // GetAnchor() is the chunk's real placement anchor (world-space, already descaled
        // per-engine by ZoneReader.ConvertUFrag) - local (0,0,0) of the mesh maps there. This is
        // NOT the same as GetBoundingCenter(): that's the true bounding-sphere center, a separate,
        // non-grid-aligned value only used for culling below (see ZoneReader.ConvertUFrag for how
        // the two were previously conflated, causing per-chunk placement gaps). Formula is
        // adapted from the last confirmed-working implementation (master's Entity.cs, CZone.UFrag
        // constructor) - rotation was investigated there and found to always be identity for
        // these chunks. That reference also multiplied by a yard-to-meter constant, dropped here:
        // this session already found (and the user confirmed) that Ties/Mobys need no such
        // conversion, and terrain has to share the same world-unit space as the props sitting on it.
        var anchor = ufrag.GetAnchor();
        Transform = new Transform { Translation = anchor, Rotation = Quaternion.Identity, Scale = Vector3.One / UFragQuantisation };
        // BoundingSphere is LOCAL space per Entity's convention: the space the mesh's own vertices are
        // in, which for a UFrag is the x256 fixed-point space Transform.Scale undoes. The file gives
        // both the centre and the radius in WORLD units, so both are multiplied back INTO that space
        // here, and WorldBoundingSphere's own scaling takes them straight back out again. Storing the
        // world values raw made the culling sphere 256x too small around a chunk that is metres across.
        // The true bounding-sphere centre does not generally coincide with the placement anchor, which
        // is why this is an offset at all.
        BoundingSphere = new Vector4(
            (ufrag.GetBoundingCenter() - anchor) * UFragQuantisation,
            ufrag.GetBoundingRadius() * UFragQuantisation);
    }

    private static Vertex3D[] ConvertUFragToVertices(IUFrag ufrag)
    {
        var positions = ufrag.GetVertexPositions();
        var uvs = ufrag.GetTextureCoordinates();
        var normals = ufrag.GetNormals();
        var tangents = ufrag.GetTangents();
        var lightmapUVs = ufrag.GetLightmapUVs();

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
            // ZoneReader now supplies real decoded normals/tangents for UFrags (same packed
            // 11:11:10 words as VertexFormat0/1 - see UFrag.ReadVertices), so these fallbacks are
            // genuine edge-case guards, not the every-vertex default they used to be. The tangent
            // fallback must stay a real (if arbitrary) unit vector, not Vector4.Zero:
            // LitModelShaderSource's TBN construction normalizes the tangent, and normalizing a
            // zero vector is NaN, which poisons the whole lighting calculation.
            var tangent = tangents != null && tangents.Length >= i * 4 + 4
                ? new Vector4(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2], tangents[i * 4 + 3])
                : new Vector4(1f, 0f, 0f, 1f);

            // TexCoords2 is the LIGHTMAP UV set (UFragVertex.UVs2), not a copy of the base UV -
            // the game samples its baked light colour/direction maps there. Falls back to the base
            // UV when this UFrag has none, which keeps the attribute well-defined for every vertex
            // rather than leaving it uninitialised; nothing samples it in that case anyway, since
            // no lightmap is bound for an unlit UFrag.
            var lightmapUV = lightmapUVs != null && lightmapUVs.Length >= uvIdx + 2
                ? new Vector2(lightmapUVs[uvIdx], lightmapUVs[uvIdx + 1])
                : uv;

            vertices[i] = new Vertex3D(position, uv, lightmapUV, normal, tangent, Vector4.One);
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
        // Nothing to release: the mesh is plain data, and its geometry is owned by the capture
        // registry, which the level unload clears wholesale.
        GC.SuppressFinalize(this);
    }
}
