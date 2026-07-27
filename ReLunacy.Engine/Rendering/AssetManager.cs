using System.Numerics;
using Bliss.CSharp;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Effects;
using Bliss.CSharp.Graphics;
using Bliss.CSharp.Geometry.Meshes;
using Bliss.CSharp.Geometry.Meshes.Data;
using Bliss.CSharp.Geometry.Models;
using Bliss.CSharp.Graphics.Pipelines.Buffers;
using Bliss.CSharp.Graphics.VertexTypes;
using Bliss.CSharp.Images;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Textures;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering.Shaders;
using Veldrith;
using Veldrith.SPIRV;
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
    // Parallel to _materialCache, keyed the same way — the built Material has no way to ask "was
    // I sourced from a vertex-alpha material," but SetLightingEnabled needs that to pick the right
    // effect when swapping back to unlit, so the original IMaterial is kept alongside the built one.
    private readonly Dictionary<ulong, IMaterial> _sourceMaterials = [];
    private bool _backfaceCulling;
    private bool _lightingEnabled;
    // Scene-wide default filtering plus per-texture overrides (keyed by texture TUID) — samplers
    // resolve through GetSamplerFor in exactly one place, so future filtering techniques are one
    // new TextureFiltering value + one switch arm. _builtTextureIds is the Texture2D -> TUID
    // reverse of _textureCache, needed to re-resolve an already-built MaterialMap's sampler live
    // (the map only holds the GPU texture, not the id it was built from).
    private TextureFiltering _defaultTextureFiltering = TextureFiltering.Point;
    private readonly Dictionary<ulong, TextureFiltering> _perTextureFiltering = [];
    private readonly Dictionary<Texture2D, ulong> _builtTextureIds = [];
    private Effect? _vertexAlphaModelEffect;
    private Effect? _litModelEffect;
    // Flat "no perturbation" fallback for LitModelShaderSource's normal map slot, which every
    // material now gets a MaterialMap entry for (see GetOrBuildMaterial) even when the source has
    // no NormalTexture at all — the lit effect's texture layout expects something bound there
    // regardless, same reason Albedo already falls back to GlobalResource.DefaultModelTexture.
    // G=128, A=128 decodes to dx=dy=0 under LitModelShaderSource's derivative reconstruction,
    // i.e. a perfectly flat tangent-space normal (0,0,1) — R/B are unused by that reconstruction,
    // so their value doesn't matter.
    private Texture2D? _defaultNormalTexture;
    // See GetDefaultPropertiesTexture.
    private Texture2D? _defaultPropertiesTexture;

    // Veldrith's RasterizerStateDescription.DEFAULT assumes a clockwise front face; this game's
    // meshes wind the opposite way, so using DEFAULT as-is culled the near side of every triangle
    // and left the far side visible (backface culling looked "inside out" — confirmed by the user
    // after enabling it). Same CullMode.Back as DEFAULT, just the winding flipped.
    private static readonly RasterizerStateDescription BackfaceCullState = new(
        FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false);

    public IReadOnlyDictionary<ulong, Texture2D> BuiltTextures => _textureCache;
    public IReadOnlyDictionary<ulong, ITexture> SourceTextures => _sourceTextures;

    public Dictionary<ulong, Model[]> Mobys { get; } = []; // one Model per bangle
    public Dictionary<ulong, Model> Ties { get; } = [];

    public AssetManager(LevelData level, GraphicsDevice gd)
    {
        _gd = gd;

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
            var vertices = ConvertGeometryToVertices(mesh.Geometry, mesh.Material.UsesVertexAlphaCandidate);
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
            // Bliss's own RenderMode has no Additive case — Translucent is the closest bucket
            // (same depth-test-no-write handling via DecalAwareForwardRenderer), the real
            // distinction is the blend state passed below.
            Assets.Interfaces.RenderMode.Additive => RenderMode.Translucent,
            _ => RenderMode.Solid,
        };

        // Standard "over" alpha blend for everything that blends, including vertex-alpha-fallback
        // materials (see Material.UsesVertexAlphaCandidate) — additive and Screen were both tried
        // here and reverted; the darkening/brightening those were chasing turned out to be this
        // renderer being unlit (no specular/lighting response the real game has), not a wrong blend
        // equation. Standard alpha blend is correct; the visual mismatch is a lighting gap to close
        // separately, later.
        BlendStateDescription? blendState = material.RenderMode switch
        {
            Assets.Interfaces.RenderMode.Additive => BlendStateDescription.SINGLE_ADDITIVE_BLEND,
            Assets.Interfaces.RenderMode.AlphaBlend => BlendStateDescription.SINGLE_ALPHA_BLEND,
            _ => null,
        };

        var bMat = new Material(
            SelectEffect(material),
            _backfaceCulling ? BackfaceCullState : RasterizerStateDescription.CULL_NONE,
            blendState,
            renderMode);

        // value = the game's own per-material alphaClip threshold (maps[0].value in
        // LitModelShaderSource's Cutout branch) — the same field GltfExporter already trusts for
        // glTF's alphaCutoff. Only meaningful for Cutout materials; harmless elsewhere.
        var albedo = material.AlbedoTexture != null ? GetOrBuildTexture(material.AlbedoTexture) : GlobalResource.DefaultModelTexture;
        bMat.AddMaterialMap(new MaterialMapKey(MaterialMapType.Albedo), 0, new MaterialMap(albedo, ResolveSampler(albedo), color: Color.White, value: material.AlphaClipThreshold));

        // Always added (not conditional on NormalTexture existing) so LitModelShaderSource's
        // texture layout always has something bound to it once lighting is enabled — see
        // GetDefaultNormalTexture. Harmless for the unlit effects, which don't declare a Normal
        // texture layout at all, so this entry is just never looked up by anything.
        var normal = material.NormalTexture != null ? GetOrBuildTexture(material.NormalTexture) : GetDefaultNormalTexture();
        bMat.AddMaterialMap(new MaterialMapKey(MaterialMapType.Normal), 1, new MaterialMap(normal, ResolveSampler(normal)));

        // "fProperties" (a custom name, not one of Bliss's built-in MaterialMapType slots — none of
        // Metallic/Roughness/Emission etc. individually match what this actually is) is this game's
        // packed "expensive" intensity texture. Current best-known layout per live experimentation
        // (see LitModelShaderSource's header): R=specular intensity, G=parallax height,
        // B=emissive intensity, A=roughness. Always added (see GetDefaultNormalTexture for why),
        // so materials with no PropertiesTexture get the inert default from
        // GetDefaultPropertiesTexture rather than leaving the lit effect's texture layout unbound.
        // value = the per-material parallax multiplier (maps[2].value in LitModelShaderSource),
        // default 1 — live-tunable per shader from the ShaderBrowser via SetParallaxMultiplier,
        // for testing hex-dump candidates against the real game's look.
        var properties = material.PropertiesTexture != null ? GetOrBuildTexture(material.PropertiesTexture) : GetDefaultPropertiesTexture();
        bMat.AddMaterialMap(new MaterialMapKey("fProperties"), 2, new MaterialMap(properties, ResolveSampler(properties), value: 1f));

        _materialCache[material.Id] = bMat;
        _sourceMaterials[material.Id] = material;
        return bMat;
    }

    private Effect SelectEffect(IMaterial material) => _lightingEnabled
        ? GetLitModelEffect()
        : (material.UsesVertexAlphaCandidate ? GetVertexAlphaModelEffect() : GlobalResource.DefaultModelEffect);

    // Same buffer/texture layout as GlobalResource.DefaultModelEffect (MatrixBuffer@0 vertex,
    // TransformBuffer@1 vertex, MaterialBuffer@2 fragment, Albedo texture@3) — a drop-in swap.
    // Bliss's bundled default_model shaders never pass vColor through the vertex stage at all
    // (confirmed by reading the actual GLSL), so consuming it needs a real second shader rather
    // than a material-level trick; kept as our own Effect instead of touching the vendored content
    // files so every other material (the overwhelming majority) is completely unaffected.
    private Effect GetVertexAlphaModelEffect() => _vertexAlphaModelEffect ??= BuildVertexAlphaModelEffect();

    private Effect BuildVertexAlphaModelEffect()
    {
        var effect = new Effect(_gd, VertexAlphaModelShaderSource.Vertex, VertexAlphaModelShaderSource.Fragment, new CrossCompileOptions(), []);
        effect.AddBufferLayout("MatrixBuffer", 0u, SimpleBufferType.Uniform, ShaderStages.Vertex);
        effect.AddBufferLayout("TransformBuffer", 1u, SimpleBufferType.Uniform, ShaderStages.Vertex);
        effect.AddBufferLayout("MaterialBuffer", 2u, SimpleBufferType.Uniform, ShaderStages.Fragment);
        effect.AddTextureLayout(MaterialMapType.Albedo.GetName(), 3u);
        return effect;
    }

    // Same MatrixBuffer@0/TransformBuffer@1/MaterialBuffer@2/Albedo@3 base as the other two
    // effects, plus a Normal texture@4, a new LightBuffer@5 (fragment-stage uniform: direction,
    // ambient, color, camera position — see LightData/EditorSettings.LightDirection etc.) and a
    // Properties texture@6 (specular/metallic/emissive-intensity — see GetOrBuildMaterial).
    // DecalAwareForwardRenderer is what actually binds LightBuffer's resource set (conditionally,
    // only for effects that declare it) — see its DrawPreparedRenderable.
    private Effect GetLitModelEffect() => _litModelEffect ??= BuildLitModelEffect();

    private Effect BuildLitModelEffect()
    {
        var effect = new Effect(_gd, LitModelShaderSource.Vertex, LitModelShaderSource.Fragment, new CrossCompileOptions(), []);
        effect.AddBufferLayout("MatrixBuffer", 0u, SimpleBufferType.Uniform, ShaderStages.Vertex);
        effect.AddBufferLayout("TransformBuffer", 1u, SimpleBufferType.Uniform, ShaderStages.Vertex);
        effect.AddBufferLayout("MaterialBuffer", 2u, SimpleBufferType.Uniform, ShaderStages.Fragment);
        effect.AddTextureLayout(MaterialMapType.Albedo.GetName(), 3u);
        effect.AddTextureLayout(MaterialMapType.Normal.GetName(), 4u);
        effect.AddBufferLayout("LightBuffer", 5u, SimpleBufferType.Uniform, ShaderStages.Fragment);
        effect.AddTextureLayout("fProperties", 6u);
        return effect;
    }

    private Texture2D GetDefaultNormalTexture() => _defaultNormalTexture ??=
        new Texture2D(_gd, new Image(1, 1, new Color(128, 128, 128, 128)));

    // Inert per-channel defaults matching the current best-known expensive-map layout (see
    // LitModelShaderSource): R=0 no specular, G=0 flat parallax height (0 = no UV offset in this
    // game's convention — height only ever displaces upward from 0 to 1), B=0 no emissive,
    // A=255 fully rough (moot while R=0). Same "inert" fallback role GetDefaultNormalTexture
    // plays for Normal.
    private Texture2D GetDefaultPropertiesTexture() => _defaultPropertiesTexture ??=
        new Texture2D(_gd, new Image(1, 1, new Color(0, 0, 0, 255)));

    // Live opt-in toggle, same rationale/pattern as SetBackfaceCulling below — Material.Effect is
    // a plain public field, so swapping it on already-cached materials takes effect on the very
    // next draw with no rebuild needed. Every material's Vertex3D layout is identical regardless
    // of which of these three effects ends up drawing it, so this is safe across the swap.
    public void SetLightingEnabled(bool enabled)
    {
        if (_lightingEnabled == enabled) return;
        _lightingEnabled = enabled;

        foreach (var (id, bMat) in _materialCache)
            bMat.Effect = SelectEffect(_sourceMaterials[id]);
    }

    // Live opt-in toggle, not a rebuild trigger: Bliss's Material.RasterizerState is a plain public
    // field read fresh by BasicForwardRenderer.Draw every draw call (verified via IL — it feeds
    // directly into that frame's SimplePipelineDescription), so mutating it on the already-cached
    // Material instances takes effect on the very next frame with no need to touch geometry or
    // rebuild anything. See EditorSettings.BackfaceCulling for why this defaults off.
    public void SetBackfaceCulling(bool enabled)
    {
        if (_backfaceCulling == enabled) return;
        _backfaceCulling = enabled;

        var state = enabled ? BackfaceCullState : RasterizerStateDescription.CULL_NONE;
        foreach (var material in _materialCache.Values)
            material.RasterizerState = state;
    }

    // Live scene-wide filtering toggle, same pattern as SetBackfaceCulling/SetLightingEnabled:
    // MaterialMap.Sampler is a plain public field read fresh every draw (DecalAwareForwardRenderer
    // falls back to PointWrap only when it's null), so mutating the cached maps takes effect next
    // frame with no texture/material rebuild.
    public void SetTextureFiltering(TextureFiltering filtering)
    {
        if (_defaultTextureFiltering == filtering) return;
        _defaultTextureFiltering = filtering;
        RefreshMaterialSamplers();
    }

    /// <summary>Per-texture override (by texture TUID) — the hook for future per-texture
    /// filtering techniques. Takes effect immediately, wins over the scene-wide default.</summary>
    public void SetTextureFiltering(ulong textureId, TextureFiltering filtering)
    {
        _perTextureFiltering[textureId] = filtering;
        RefreshMaterialSamplers();
    }

    /// <summary>Live per-material parallax-strength multiplier (rides the fProperties
    /// MaterialMap's value slot, i.e. maps[2].value in LitModelShaderSource — no extra GPU
    /// plumbing). SetMapValue marks the Bliss material dirty; DecalAwareForwardRenderer
    /// propagates that to every renderable sharing the material (see its Draw for why that
    /// propagation can't rely on Bliss's own flag alone). materialId is the shader TUID, same
    /// key GetOrBuildMaterial caches under.</summary>
    public void SetParallaxMultiplier(ulong materialId, float multiplier)
    {
        if (_materialCache.TryGetValue(materialId, out var bMat))
            bMat.SetMapValue(new MaterialMapKey("fProperties"), multiplier);
    }

    /// <summary>False when the material hasn't been built (nothing in the loaded region uses it)
    /// — callers should hide the control rather than show a dead default.</summary>
    public bool TryGetParallaxMultiplier(ulong materialId, out float multiplier)
    {
        if (_materialCache.TryGetValue(materialId, out var bMat))
        {
            multiplier = bMat.GetMapValue(new MaterialMapKey("fProperties"));
            return true;
        }
        multiplier = 1f;
        return false;
    }

    private void RefreshMaterialSamplers()
    {
        foreach (var bMat in _materialCache.Values)
        {
            foreach (var key in bMat.GetMaterialMapKeys())
            {
                var map = bMat.GetMaterialMap(key);
                if (map != null)
                    map.Sampler = ResolveSampler(map.Texture);
            }
        }
    }

    // Fallback/default textures (DefaultModelTexture, 1x1 normal/properties) aren't in
    // _builtTextureIds and just take the scene default — a per-texture override for a 1x1
    // constant would be meaningless anyway.
    private Sampler ResolveSampler(Texture2D? texture) =>
        GetSamplerFor(texture != null && _builtTextureIds.TryGetValue(texture, out var id) && _perTextureFiltering.TryGetValue(id, out var overridden)
            ? overridden
            : _defaultTextureFiltering);

    // The single TextureFiltering -> GPU sampler mapping. Game textures always tile, so every
    // mode maps to a Wrap-addressing sampler; new filtering techniques are one new enum value
    // plus one arm here.
    private Sampler GetSamplerFor(TextureFiltering filtering) => filtering switch
    {
        TextureFiltering.Bilinear => GraphicsHelper.GetSampler(_gd, SamplerType.LinearWrap),
        _ => GraphicsHelper.GetSampler(_gd, SamplerType.PointWrap),
    };

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
        _builtTextureIds[tex] = texture.Id;
        return tex;
    }

    // Normals and tangents are decoded straight from the source vertex data (VertexFormat0/1's
    // packed 11:11:10 words — see PackedNormal/GeometryMath) rather than derived here; GeometryData
    // only falls back to UV-gradient derivation for formats that don't carry real data at all.
    // useVertexAlpha: see Material.UsesVertexAlphaCandidate — when set, GetVertexAlphaCandidates()
    // is written into each vertex's color alpha instead of the default fully-opaque white, and
    // GetOrBuildMaterial picks a shader that actually reads it.
    private static Vertex3D[] ConvertGeometryToVertices(IGeometry geometry, bool useVertexAlpha)
    {
        var positions = geometry.GetVertexPositions();
        var uvs = geometry.GetTextureCoordinates();
        var normals = geometry.GetNormals();
        var tangents = geometry.GetTangents();
        var vertexAlpha = useVertexAlpha ? geometry.GetVertexAlphaCandidates() : null;

        int vertexCount = positions.Length / 3;
        var vertices = new Vertex3D[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            var pos = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            var uv = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
            var n = normals != null && normals.Length >= i * 3 + 3
                ? new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2])
                : Vector3.UnitY;
            var tan = tangents != null && tangents.Length >= i * 4 + 4
                ? new Vector4(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2], tangents[i * 4 + 3])
                : new Vector4(1f, 0f, 0f, 1f);

            float alpha = vertexAlpha != null && i < vertexAlpha.Length ? vertexAlpha[i] : 1f;
            vertices[i] = new Vertex3D(pos, uv, uv, n, tan, new Vector4(1f, 1f, 1f, alpha));
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

        _defaultNormalTexture?.Dispose();
        _defaultNormalTexture = null;
        _defaultPropertiesTexture?.Dispose();
        _defaultPropertiesTexture = null;

        _vertexAlphaModelEffect?.Dispose();
        _vertexAlphaModelEffect = null;
        _litModelEffect?.Dispose();
        _litModelEffect = null;

        Mobys.Clear();
        Ties.Clear();
        _materialCache.Clear();
        _sourceMaterials.Clear();
        _textureCache.Clear();
        _sourceTextures.Clear();
        _builtTextureIds.Clear();
        _perTextureFiltering.Clear();
    }
}
