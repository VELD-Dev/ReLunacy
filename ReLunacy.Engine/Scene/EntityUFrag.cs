using System.Numerics;
using Bliss.CSharp;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry.Meshes;
using Bliss.CSharp.Geometry.Meshes.Data;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Graphics.VertexTypes;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Transformations;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityUFrag : Entity
{
    public readonly IUFrag UFrag;
    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public Mesh<Vertex3D> UFragMesh { get; protected set; }

    public EntityUFrag(GraphicsDevice gd, IUFrag ufrag, AssetManager assetManager)
    {
        UFrag = ufrag;
        Name = !string.IsNullOrEmpty(ufrag.Name) ? $"{ufrag.Name}_{ID}" : $"UFrag_{ID}";

        var vertices = ConvertUFragToVertices(ufrag);
        var indices = ufrag.GetIndices();

        // Passing the lightmap index is what makes UFrags sharing a shader but not a lightmap get
        // distinct Materials — see AssetManager.GetOrBuildMaterial. Until the 0x5400/0x5410
        // textures are actually built and bound this only splits the cache; it is the seam the
        // baked lighting hangs off, and getting it wrong later would silently give every UFrag one
        // shared lightmap.
        var material = assetManager.GetOrBuildMaterial(ufrag.Material, ufrag.LightmapIndex);
        UFragMesh = new Mesh<Vertex3D>(gd, material, new BasicMeshData(vertices, indices));

        // UFragVertex's raw per-vertex x/y/z are fixed-point shorts quantized ×256 on BOTH
        // engines (master's UFragVertex/OldUFragVertex structs are laid out identically — no
        // engine-specific scale) — using Scale = Vector3.One for new engine was rendering every
        // chunk's mesh 256x too large relative to its own bounding radius.
        // GetAnchor() is the chunk's real placement anchor (world-space, already descaled
        // per-engine by ZoneReader.ConvertUFrag) — local (0,0,0) of the mesh maps there. This is
        // NOT the same as GetBoundingCenter(): that's the true bounding-sphere center, a separate,
        // non-grid-aligned value only used for culling below (see ZoneReader.ConvertUFrag for how
        // the two were previously conflated, causing per-chunk placement gaps). Formula is
        // adapted from the last confirmed-working implementation (master's Entity.cs, CZone.UFrag
        // constructor) — rotation was investigated there and found to always be identity for
        // these chunks. That reference also multiplied by a yard-to-meter constant, dropped here:
        // this session already found (and the user confirmed) that Ties/Mobys need no such
        // conversion, and terrain has to share the same world-unit space as the props sitting on it.
        var anchor = ufrag.GetAnchor();
        Transform = new Transform { Translation = anchor, Rotation = Quaternion.Identity, Scale = Vector3.One / 256f };
        // BoundingSphere is LOCAL space per Entity's convention (offset from Transform.Translation)
        // — the true bounding-sphere center doesn't generally coincide with the placement anchor.
        BoundingSphere = new Vector4(ufrag.GetBoundingCenter() - anchor, ufrag.GetBoundingRadius());
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
            // 11:11:10 words as VertexFormat0/1 — see UFrag.ReadVertices), so these fallbacks are
            // genuine edge-case guards, not the every-vertex default they used to be. The tangent
            // fallback must stay a real (if arbitrary) unit vector, not Vector4.Zero:
            // LitModelShaderSource's TBN construction normalizes the tangent, and normalizing a
            // zero vector is NaN, which poisons the whole lighting calculation.
            var tangent = tangents != null && tangents.Length >= i * 4 + 4
                ? new Vector4(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2], tangents[i * 4 + 3])
                : new Vector4(1f, 0f, 0f, 1f);

            // TexCoords2 is the LIGHTMAP UV set (UFragVertex.UVs2), not a copy of the base UV —
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

    public override void Draw(IRenderer renderer, OutputDescription outputDescription, CommandList commandList, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender || !EntityManager.Singleton.renderUFrags) return;

        // Culling was dropped earlier this session ("not numerous enough to matter") back when
        // BoundingSphere was wrongly zeroed/coincident with the placement translation for new
        // engine — now that GetBoundingCenter() is a real, independent bounding sphere again
        // (see ZoneReader.ConvertUFrag / the constructor above), reinstate it, same pattern as
        // EntityMoby/EntityTie.
        var sphere = WorldBoundingSphere;
        var sphereCenter = new Vector3(sphere.X, sphere.Y, sphere.Z);
        if (EntityManager.Singleton.FrustumCullingEnabled && !camera.GetFrustum().ContainsSphere(sphereCenter, sphere.W)) return;

        if (EntityManager.Singleton.renderBoundingSpheres)
            DrawBoundingSphere(outputDescription, commandList, immediateRenderer);

        if (IsDirty)
        {
            cachedRenderables.Clear();
            cachedRenderables.Add(new Renderable(UFragMesh, Transform));
            IsDirty = false;
        }

        foreach (var renderable in cachedRenderables)
            renderer.DrawRenderable(renderable);

        EntitiesRenderedThisFrame++;
    }

    public override void Dispose()
    {
        UFragMesh.Dispose();
        GC.SuppressFinalize(this);
    }
}
