using System.Numerics;
using Bliss.CSharp;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Geometry.Meshes;
using Bliss.CSharp.Geometry.Meshes.Data;
using Bliss.CSharp.Geometry.Models;
using Bliss.CSharp.Graphics.VertexTypes;
using Bliss.CSharp.Images;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Textures;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Readers;
using Veldrith;
using IMesh = ReLunacy.Engine.Assets.Interfaces.IMesh;
using RenderMode = Bliss.CSharp.Graphics.Rendering.RenderMode;

namespace ReLunacy.Engine.Rendering;

// Builds Bliss GPU resources (Mesh/Model/Material/Texture2D) from the engine-owned asset model
// (Assets.Mobys.Moby / Assets.Ties.Tie / their meshes' IMaterial/ITexture), caching by TUID so
// shared materials/textures aren't rebuilt per mesh.
public sealed class AssetManager : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly Dictionary<ulong, Texture2D> _textureCache = [];
    private readonly Dictionary<ulong, ITexture> _sourceTextures = [];
    private readonly Dictionary<ulong, Material> _materialCache = [];
    private readonly float _decalOffset;

    public IReadOnlyDictionary<ulong, Texture2D> BuiltTextures => _textureCache;
    public IReadOnlyDictionary<ulong, ITexture> SourceTextures => _sourceTextures;

    public Dictionary<ulong, Model[]> Mobys { get; } = []; // one Model per bangle
    public Dictionary<ulong, Model> Ties { get; } = [];

    // decalOffset: see EditorSettings.DecalOffset — how far IMaterial.IsDecal geometry gets pushed
    // outward along its (computed) normal at mesh-build time, to avoid Z-fighting the opaque
    // surface it's decaling. Passed in rather than read from Program.Settings directly since this
    // project deliberately has no dependency on the app layer.
    public AssetManager(LevelData level, GraphicsDevice gd, float decalOffset = 0f)
    {
        _gd = gd;
        _decalOffset = decalOffset;

        foreach (var (id, moby) in level.Mobys)
        {
            var models = new Model[moby.Bangles.Count];
            for (int i = 0; i < moby.Bangles.Count; i++)
            {
                models[i] = BuildModel(moby.Bangles[i].Meshes);
            }
            Mobys[id] = models;
        }

        foreach (var (id, tie) in level.Ties)
        {
            Ties[id] = BuildModel(tie.Meshes);
        }

        // Build every texture the level loaded, not just the ones the loops above already pulled
        // in via a used material — some textures.dat/highmips.dat entries aren't wired to any
        // shader used by this level's geometry (cut/unused content), but are still worth being
        // able to see/export. A single bad one (unexpected dimensions/corrupt data) shouldn't take
        // the rest of the level down with it.
        foreach (var texture in level.AllTextures.Values)
        {
            try
            {
                GetOrBuildTexture(texture);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to build texture {texture.Id:X}: {ex.Message}");
            }
        }
    }

    private Model BuildModel(IReadOnlyList<IMesh> meshes)
    {
        var bMeshes = new Bliss.CSharp.Geometry.Meshes.IMesh[meshes.Count];
        for (int i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            var material = GetOrBuildMaterial(mesh.Material);
            // DecalOffsetCandidate (file offset 0x48) is an unconfirmed per-material hypothesis —
            // see IMaterial.DecalOffsetCandidate — used here as a multiplier on the global
            // EditorSettings.DecalOffset slider rather than the raw offset directly, so the slider
            // stays a meaningful "scale everything up/down" knob regardless of whether the file
            // value turns out to already be in the right units on its own (in which case DecalOffset
            // should just be left at 1) or needs further scaling.
            float decalOffset = mesh.Material.IsDecal ? _decalOffset * mesh.Material.DecalOffsetCandidate : 0f;
            var vertices = ConvertGeometryToVertices(mesh.Geometry, decalOffset);
            bMeshes[i] = new Mesh<Vertex3D>(_gd, material, new BasicMeshData(vertices, mesh.Geometry.GetIndices()));
        }
        return new Model(_gd, bMeshes, null, []);
    }

    public Material GetOrBuildMaterial(IMaterial material)
    {
        if (_materialCache.TryGetValue(material.Id, out var cached))
            return cached;

        var renderMode = material.RenderMode switch
        {
            Assets.Interfaces.RenderMode.AlphaClip => RenderMode.Cutout,
            Assets.Interfaces.RenderMode.AlphaBlend => RenderMode.Translucent,
            _ => RenderMode.Solid,
        };

        var bMat = new Material(
            GlobalResource.DefaultModelEffect,
            RasterizerStateDescription.CULL_NONE,
            renderMode == RenderMode.Translucent ? BlendStateDescription.SINGLE_ALPHA_BLEND : null,
            renderMode);

        var albedo = material.AlbedoTexture != null ? GetOrBuildTexture(material.AlbedoTexture) : GlobalResource.DefaultModelTexture;
        bMat.AddMaterialMap(new MaterialMapKey(MaterialMapType.Albedo), 0, new MaterialMap(albedo, color: Color.White));

        if (material.NormalTexture != null)
            bMat.AddMaterialMap(new MaterialMapKey(MaterialMapType.Normal), 1, new MaterialMap(GetOrBuildTexture(material.NormalTexture)));

        if (material.PropertiesTexture != null)
            bMat.AddMaterialMap(new MaterialMapKey(MaterialMapType.Emission), 2, new MaterialMap(GetOrBuildTexture(material.PropertiesTexture)));

        _materialCache[material.Id] = bMat;
        return bMat;
    }

    public Texture2D GetOrBuildTexture(ITexture texture)
    {
        if (_textureCache.TryGetValue(texture.Id, out var cached))
            return cached;

        _sourceTextures[texture.Id] = texture;

        // Some texture slots genuinely have no highmip data for a given level (Texture.ReadTexture
        // returns early, leaving data empty, when the highmips pointer's length is 0) — a real,
        // already-handled case in the loader, not a corrupt read. TextureUtils.DecodeToRgba8888
        // returns null for that case (and for unrecognized formats) instead of crashing.
        byte[]? rgba = TextureUtils.DecodeToRgba8888(texture, out int width, out int height);
        if (rgba == null)
        {
            _textureCache[texture.Id] = GlobalResource.DefaultModelTexture;
            return GlobalResource.DefaultModelTexture;
        }

        var image = new Image(width, height, rgba);
        var tex = new Texture2D(_gd, image, true);
        _textureCache[texture.Id] = tex;
        return tex;
    }

    // Geometry only carries positions/uvs/normals — tangents are derived here per-triangle
    // (standard UV-gradient method) since Moby/Tie meshes have no baked tangent data.
    // decalOffset (0 = no-op): pushes the final vertex position outward along its normal — see
    // AssetManager's constructor doc and EditorSettings.DecalOffset for why.
    private static Vertex3D[] ConvertGeometryToVertices(IGeometry geometry, float decalOffset = 0f)
    {
        var positions = geometry.GetVertexPositions();
        var uvs = geometry.GetTextureCoordinates();
        var normals = geometry.GetNormals();
        var indices = geometry.GetIndices();

        int vertexCount = positions.Length / 3;
        var pos = new Vector3[vertexCount];
        var uv = new Vector2[vertexCount];
        var norm = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            pos[i] = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            uv[i] = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
            norm[i] = normals != null && normals.Length >= i * 3 + 3
                ? new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2])
                : Vector3.Zero;
        }

        var tangentAccum = new Vector3[vertexCount];
        var bitangentAccum = new Vector3[vertexCount];

        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            uint i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
            Vector3 edge1 = pos[i1] - pos[i0];
            Vector3 edge2 = pos[i2] - pos[i0];
            Vector2 duv1 = uv[i1] - uv[i0];
            Vector2 duv2 = uv[i2] - uv[i0];

            float det = duv1.X * duv2.Y - duv2.X * duv1.Y;
            if (MathF.Abs(det) < 1e-8f) continue;

            float r = 1.0f / det;
            Vector3 tangent = (edge1 * duv2.Y - edge2 * duv1.Y) * r;
            Vector3 bitangent = (edge2 * duv1.X - edge1 * duv2.X) * r;

            tangentAccum[i0] += tangent; tangentAccum[i1] += tangent; tangentAccum[i2] += tangent;
            bitangentAccum[i0] += bitangent; bitangentAccum[i1] += bitangent; bitangentAccum[i2] += bitangent;
        }

        var vertices = new Vertex3D[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 n = norm[i] != Vector3.Zero ? Vector3.Normalize(norm[i]) : Vector3.UnitY;

            Vector3 tan = tangentAccum[i] - n * Vector3.Dot(n, tangentAccum[i]);
            if (tan.LengthSquared() < 1e-12f)
                tan = MathF.Abs(n.Y) < 0.99f ? Vector3.Cross(Vector3.UnitY, n) : Vector3.Cross(Vector3.UnitX, n);
            tan = Vector3.Normalize(tan);

            float handedness = Vector3.Dot(Vector3.Cross(n, tan), bitangentAccum[i]) < 0f ? -1f : 1f;

            Vector3 offsetPos = decalOffset != 0f ? pos[i] + n * decalOffset : pos[i];
            vertices[i] = new Vertex3D(offsetPos, uv[i], uv[i], n, new Vector4(tan, handedness), Vector4.One);
        }

        return vertices;
    }

    public void Dispose()
    {
        foreach (var models in Mobys.Values)
            foreach (var model in models)
                model.Dispose();

        foreach (var model in Ties.Values)
            model.Dispose();

        foreach (var texture in _textureCache.Values)
            if (texture != GlobalResource.DefaultModelTexture)
                texture.Dispose();

        Mobys.Clear();
        Ties.Clear();
        _materialCache.Clear();
        _textureCache.Clear();
        _sourceTextures.Clear();
    }
}
