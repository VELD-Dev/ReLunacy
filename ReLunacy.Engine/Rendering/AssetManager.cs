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
    // Keyed on (shader TUID, lightmap index), NOT on the TUID alone. Baked lighting is a
    // per-INSTANCE property — one shader is shared across many UFrags, each with its own entry in
    // the zone's 0x5400/0x5410 lists — while Bliss binds textures through the Material. Caching by
    // TUID alone would therefore give every instance whichever lightmap happened to be built
    // first. Materials with no baked lighting all collapse onto UFragMetadata.NoLightmap, so
    // nothing that existed before this distinction pays for it.
    private readonly Dictionary<(ulong ShaderId, ushort LightmapIndex), Material> _materialCache = [];
    // Every built variant of a given shader TUID. The live-tuning API (SetParallax,
    // SetDetailTiling) is addressed by TUID because that is what the ShaderBrowser lists, so it
    // has to reach all of a shader's lightmap variants rather than just one.
    private readonly Dictionary<ulong, List<Material>> _materialsByShader = [];
    // Parallel to _materialCache, keyed the same way — the built Material has no way to ask "was
    // I sourced from a vertex-alpha material," but SetLightingEnabled needs that to pick the right
    // effect when swapping back to unlit, so the original IMaterial is kept alongside the built one.
    private readonly Dictionary<ulong, IMaterial> _sourceMaterials = [];
    // Per-built-material data for the raw-Vulkan renderer: the game's own rendering mode (0-6) and
    // whether opacity comes from the per-vertex alpha. Bliss's 8 MaterialMap slots are all used, so
    // this rides a side table keyed by the built Material rather than a 9th slot that cannot exist.
    private readonly Dictionary<Material, (byte GameRenderMode, bool UsesVertexAlpha)> _vkMaterialInfo =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>The game's rendering mode (0 Opaque, 1 Overlay, 2 Additive, 3 Scunge, 4 Cutout,
    /// 5 Soft-Edge, 6 Blended) and vertex-alpha flag for a built material. See IMaterial.GameRenderMode
    /// and dev/chatgpt-eboot-1..3.txt for what each mode's real RSX state is.</summary>
    public bool TryGetVkMaterialInfo(Material bMat, out byte gameRenderMode, out bool usesVertexAlpha)
    {
        if (_vkMaterialInfo.TryGetValue(bMat, out var info))
        {
            (gameRenderMode, usesVertexAlpha) = info;
            return true;
        }
        gameRenderMode = 0;
        usesVertexAlpha = false;
        return false;
    }
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
    private Effect? _billboardModelEffect;
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
    // See GetDefaultLightColourTexture / GetDefaultLightDirTexture.
    private Texture2D? _defaultLightColourTexture;
    private Texture2D? _defaultLightDirTexture;

    // Veldrith's RasterizerStateDescription.DEFAULT assumes a clockwise front face; this game's
    // meshes wind the opposite way, so using DEFAULT as-is culled the near side of every triangle
    // and left the far side visible (backface culling looked "inside out" — confirmed by the user
    // after enabling it). Same CullMode.Back as DEFAULT, just the winding flipped.
    private static readonly RasterizerStateDescription BackfaceCullState = new(
        FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, depthClipEnabled: true,
        depthBias: 0, slopeScaledDepthBias: 0f, depthBiasClamp: 0f, scissorTestEnabled: false);

    // POLYGON OFFSET FOR NON-OPAQUE SURFACES, copied from the game rather than tuned. RenderDoc's
    // rasterizer state for an overlay draw (dev/RasterizerState_OverlayTranslucency.png) reads:
    //     Depth Bias        -86.9685      Slope-Scaled Bias  -0.33972      Clamp  0.0
    // which is why the game's decals and overlays never cut into the surface they sit on. Without
    // it a coplanar overlay passes the depth test only where float precision happens to land in
    // its favour, and gets sliced everywhere else.
    // DepthBias is an INT here (Veldrith follows D3D12's integer count of depth units, where Vulkan
    // takes a float), so -86.9685 becomes -87 - a difference of 0.03 units out of 2^24, around
    // 2e-9 of the depth range, i.e. nothing.
    // BOTH terms matter. The constant one handles surfaces facing the camera; the slope-scaled one
    // is what keeps a decal in front at grazing angles, and is exactly what a fixed vertex-space Z
    // nudge cannot reproduce - which is why no shader-side workaround was taken while this was
    // unavailable.
    // THE TWO TERMS ARE ONE NUMBER. 86.9685 / 0.33972 = 256.0 (0.33972 * 256 = 86.96832, and the
    // remainder is RenderDoc's display rounding). 256 is this engine's fixed-point unit everywhere
    // else - UFrag anchors, vertex positions - so both parameters almost certainly come from a
    // single authored offset in engine units, with the constant term being its depth-unit form.
    // Two consequences worth knowing before anyone re-derives this:
    //   - if the bias ever turns out to vary, it varies as ONE scalar, and these two constants
    //     should become (k, k * 256) rather than two independent knobs;
    //   - it is NOT stored per material. All 631 shader records on metropolis were scanned for
    //     either value, at every float offset, with a 1% tolerance: no matches anywhere. The old
    //     "decal offset" suspect at ShaderMetadataOld 0x48 is not it either - it is 1.0 or 0.0 on
    //     most materials, which is a multiplier's distribution, not an offset's.
    // So this is engine-global state (one polygon-offset setup for the whole non-opaque pass),
    // which is exactly how it is applied here.
    private const int OverlayDepthBias = -87;
    private const float OverlaySlopeScaledDepthBias = -0.33972f;

    private static readonly RasterizerStateDescription BiasedCullNoneState = new(
        FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, depthClipEnabled: true,
        depthBias: OverlayDepthBias, slopeScaledDepthBias: OverlaySlopeScaledDepthBias,
        depthBiasClamp: 0f, scissorTestEnabled: false);

    private static readonly RasterizerStateDescription BiasedBackfaceCullState = new(
        FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, depthClipEnabled: true,
        depthBias: OverlayDepthBias, slopeScaledDepthBias: OverlaySlopeScaledDepthBias,
        depthBiasClamp: 0f, scissorTestEnabled: false);

    /// <summary>Rasterizer state for a material, biased when it is not opaque. Gated on RenderMode
    /// rather than on the source RenderingMode byte so every blending path gets it - Overlay,
    /// Additive, Blended and Soft-Edge all end up here, which matches the note that the game draws
    /// every non-opaque surface in a separate, offset pass.</summary>
    private RasterizerStateDescription RasterizerStateFor(RenderMode renderMode)
    {
        bool biased = renderMode == RenderMode.Translucent;
        if (_backfaceCulling) return biased ? BiasedBackfaceCullState : BackfaceCullState;
        return biased ? BiasedCullNoneState : RasterizerStateDescription.CULL_NONE;
    }

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

        ZoneLightmaps = BuildZoneLighting(level.ZoneLightmaps, "lightmap");
        ZoneDirectionals = BuildZoneLighting(level.ZoneDirectionals, "directional");
        if (ZoneLightmaps.Count != 0)
            Console.WriteLine($"Zone lighting: {ZoneLightmaps.Count} light-colour and {ZoneDirectionals.Count} light-direction textures built.");

        BuildEnvironmentCubemap(level);
    }

    // The level's environment cubemap as a GPU samplerCube, for the lit shader's reflection term.
    // Always non-null once constructed: a level with no cubemap gets a 1x1 grey fallback so the lit
    // effect's declared set 10 is never bound to nothing (an unbound descriptor set is undefined
    // behaviour — the same class of fault BuildLitModelEffect documents). See CubemapReader for the
    // face format and LitModelShaderSource for how it's sampled.
    private Veldrith.Texture? _environmentCubemap;
    public Veldrith.TextureView? EnvironmentCubemapView { get; private set; }

    private void BuildEnvironmentCubemap(LevelData level)
    {
        var cubemap = level.Cubemaps.Count > 0 ? level.Cubemaps[0] : null;
        int size = cubemap?.FaceSize ?? 1;
        var factory = _gd.ResourceFactory;

        var tex = factory.CreateTexture(Veldrith.TextureDescription.Texture2D(
            (uint)size, (uint)size, 1, 6, Veldrith.PixelFormat.R8G8B8A8UNorm,
            Veldrith.TextureUsage.Sampled | Veldrith.TextureUsage.Cubemap));

        // Face order is the file's own +X,-X,+Y,-Y,+Z,-Z, which is exactly the cube array-layer
        // order Vulkan expects, so layer index == face index with no remap.
        for (uint f = 0; f < 6; f++)
        {
            byte[] rgba = (cubemap != null && f < cubemap.Faces.Count
                ? TextureUtils.DecodeToRgba8888(cubemap.Faces[(int)f], out _, out _)
                : null) ?? FallbackCubeFace(size);
            _gd.UpdateTexture(tex, rgba, 0, 0, 0, (uint)size, (uint)size, 1, 0, f);
        }

        _environmentCubemap = tex;
        EnvironmentCubemapView = factory.CreateTextureView(tex);
    }

    private static byte[] FallbackCubeFace(int size)
    {
        // Mid-grey, mid-alpha. Only ever sampled when a real cubemap is absent, in which case the
        // renderer's EnvironmentIntensity is 0 and this contributes nothing regardless.
        var data = new byte[size * size * 4];
        Array.Fill(data, (byte)128);
        return data;
    }

    /// <summary>Baked lighting is ON for TERRAIN. UFrags index a shared atlas via
    /// UFragMetadata.lightmapIndex (old-engine offset 0x4E) and sample it through
    /// UFragVertex.UVs2, which are half-float atlas coordinates — 1377 of metropolis's 1987
    /// UFrags are lit this way across 23 atlases.
    /// TIES are still excluded (EntityTie pins them to NoLightmap): they carry a real per-instance
    /// bake index, but VertexFormat0 has one UV pair and it TILES, so there is nothing correct to
    /// sample an atlas with. That is what produced the blotchy black patching, and it comes back
    /// the moment ties are re-enabled without their own UV set.
    /// Kept as a const rather than a setting so it disappears once nothing is provisional.</summary>
    public const bool EnableBakedLighting = true;

    /// <summary>Positionally indexed by an instance's lightmap index (see
    /// TieInstance.LightmapIndex), so a failed entry becomes null rather than being dropped —
    /// removing it would shift every later index onto the wrong texture.</summary>
    public IReadOnlyList<Texture2D?> ZoneLightmaps { get; } = [];
    public IReadOnlyList<Texture2D?> ZoneDirectionals { get; } = [];

    private List<Texture2D?> BuildZoneLighting(IReadOnlyList<ITexture> source, string label)
    {
        var built = new List<Texture2D?>(source.Count);
        foreach (var tex in source)
        {
            try
            {
                built.Add(GetOrBuildTexture(tex, mipmap: false));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to build zone {label} {built.Count}: {ex.Message}");
                built.Add(null);
            }
        }
        return built;
    }

    private Model BuildModel(IReadOnlyList<IMesh> meshes)
    {
        var bMeshes = new Bliss.CSharp.Geometry.Meshes.IMesh[meshes.Count];
        for (int i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            var material = GetOrBuildMaterial(mesh.Material);
            var vertices = ConvertGeometryToVertices(mesh.Geometry, mesh.Material.UsesVertexAlphaCandidate);
            var indices = mesh.Geometry.GetIndices();

            var bMesh = new Mesh<Vertex3D>(_gd, material, new BasicMeshData(vertices, indices));

            // New-renderer Stage 11+: register this mesh's raw geometry (interleaved pos+uv+normal +
            // indices) keyed by the Bliss mesh instance, so the raw-Vulkan renderer — which can't reach
            // Veldrith's mesh buffers — can upload it and resolve each scene instance's IMesh back to
            // its geometry. No effect on the normal render path.
            if (vertices.Length > 0 && indices.Length >= 3)
                Vulkan.VulkanSceneCapture.Register(bMesh, InterleaveForVk(vertices), indices);

            bMeshes[i] = bMesh;
        }
        return new Model(_gd, bMeshes, null, []);
    }

    /// <summary>Packs a Bliss Vertex3D[] into the raw-Vulkan renderer's interleaved layout: position
    /// xyz, texcoord uv, normal xyz (8 floats/vertex — see VulkanSceneCapture.FloatsPerVertex).</summary>
    internal static float[] InterleaveForVk(Bliss.CSharp.Graphics.VertexTypes.Vertex3D[] vertices)
    {
        var data = new float[vertices.Length * Vulkan.VulkanSceneCapture.FloatsPerVertex];
        for (int v = 0; v < vertices.Length; v++)
        {
            int o = v * Vulkan.VulkanSceneCapture.FloatsPerVertex;
            data[o + 0] = vertices[v].Position.X;
            data[o + 1] = vertices[v].Position.Y;
            data[o + 2] = vertices[v].Position.Z;
            data[o + 3] = vertices[v].TexCoords.X;
            data[o + 4] = vertices[v].TexCoords.Y;
            data[o + 5] = vertices[v].Normal.X;
            data[o + 6] = vertices[v].Normal.Y;
            data[o + 7] = vertices[v].Normal.Z;
            data[o + 8] = vertices[v].Tangent.X;
            data[o + 9] = vertices[v].Tangent.Y;
            data[o + 10] = vertices[v].Tangent.Z;
            data[o + 11] = vertices[v].Tangent.W;
            data[o + 12] = vertices[v].TexCoords2.X;
            data[o + 13] = vertices[v].TexCoords2.Y;
            data[o + 14] = vertices[v].Color.X;
            data[o + 15] = vertices[v].Color.Y;
            data[o + 16] = vertices[v].Color.Z;
            data[o + 17] = vertices[v].Color.W;
        }
        return data;
    }

    /// <summary>lightmapIndex: this instance's entry in the zone's baked-lighting lists (see
    /// IUFrag.LightmapIndex). Part of the cache key because baked lighting is per-instance while
    /// Bliss binds textures per-Material. Callers with no baked lighting omit it.</summary>
    public Material GetOrBuildMaterial(IMaterial material, ushort lightmapIndex = Loading.Objects.UFragMetadata.NoLightmap)
    {
        var cacheKey = (material.Id, lightmapIndex);
        if (_materialCache.TryGetValue(cacheKey, out var cached))
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
            RasterizerStateFor(renderMode),
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
        // This map's value slot is unrelated to the normal texture: it flags whether the expensive
        // map has a real alpha channel to read the detail mask from — see the detail-map section
        // below for why, and LitModelShaderSource's maps[1].value.
        var normal = material.NormalTexture != null ? GetOrBuildTexture(material.NormalTexture) : GetDefaultNormalTexture();
        bool detailMaskFromTexture = material.PropertiesTexture != null && HasAlphaChannel(material.PropertiesTexture.Format);
        bMat.AddMaterialMap(new MaterialMapKey(MaterialMapType.Normal), 1, new MaterialMap(normal, ResolveSampler(normal), value: detailMaskFromTexture ? 1f : 0f));

        // "fProperties" (a custom name, not one of Bliss's built-in MaterialMapType slots — none of
        // Metallic/Roughness/Emission etc. individually match what this actually is) is this game's
        // packed "expensive" intensity texture. Layout confirmed against the game's own captured
        // fragment shader (see fragment_shader_annotated.glsl): R=specular intensity,
        // G=parallax height, B=emissive/incandescence intensity, A=detail-map mask. Always added
        // (see GetDefaultNormalTexture for why), so materials with no PropertiesTexture get the
        // inert default from GetDefaultPropertiesTexture rather than leaving the lit effect's
        // texture layout unbound.
        // This map's own value slot is unrelated to the texture: it carries the DETAIL UV TILING
        // (maps[2].value in LitModelShaderSource), which had nowhere better to live once all 8
        // MaterialMap slots were spoken for. See the shader for why the game has no fragment
        // constant to read it from — the tiling is baked into a vertex interpolant there.
        // A tiling of literally 0 would collapse the detail map to one texel, so it can't be what
        // the field means — treat it as "not present" and fall back to 1 (base-map frequency).
        var properties = material.PropertiesTexture != null ? GetOrBuildTexture(material.PropertiesTexture) : GetDefaultPropertiesTexture();
        float detailTiling = material.DetailTiling != 0f ? material.DetailTiling : DefaultDetailTiling;
        bMat.AddMaterialMap(new MaterialMapKey("fProperties"), 2, new MaterialMap(properties, ResolveSampler(properties), value: detailTiling));

        // Two textureless MaterialMaps used purely as transport for a per-material float each:
        // Bliss uploads every registered map's Value into MaterialBuffer's maps[slot].value
        // (Renderable.UpdateMaterialBuffer), and unassigned slots stay zero, so this is the
        // cheapest way to get a tunable scalar to the shader without adding a whole uniform buffer
        // and its resource-set plumbing. They match no texture layout name, so
        // DecalAwareForwardRenderer's texture loop skips them.
        //
        // These reproduce the real game's parallax form exactly — height * scale + bias, per the
        // captured shader, where both are per-material fragment constants. Live-tunable per shader
        // from the ShaderBrowser (see SetParallax) so candidate float pairs spotted in the raw
        // metadata hex dump can be tried directly against the game's look; the whole point is that
        // a value read out of the dump can be typed in verbatim, so no hidden scaling factor is
        // applied on top of these anywhere.
        bMat.AddMaterialMap(new MaterialMapKey("fParallaxScale"), 3, new MaterialMap(value: material.ParallaxScale));
        bMat.AddMaterialMap(new MaterialMapKey("fParallaxBias"), 4, new MaterialMap(value: material.ParallaxBias));

        // Detail map: R,G = derivative perturbation added to the normal map's own derivatives,
        // B = additive albedo brightness, A = additive specular intensity, the whole fetch gated by
        // the expensive map's alpha. See IMaterial.DetailTexture.
        //
        // There are NO per-channel detail strengths: the floats previously read as
        // detailNormalStrength/detailSpecStrength/detailAlbedoStrength (ShaderMetadataOld
        // 0x28/0x2C/0x30) were misplaced onto what the EBOOT reverse proves is an RGB parameter triple
        // (dev/chatgpt-eboot-{4,5}.txt), so they've been removed. The map's value slot carries the
        // "this material actually uses its detail map" flag instead; the real per-channel scaling
        // constants the game applies are still unsourced.
        //
        // Requires BOTH a detail texture and the material's own useDetailMap flag (metadata 0x10,
        // see IMaterial.UsesDetailMap) — declaring the map and enabling it are separate things, and
        // a texture reference left in the slot by an unused authoring path shouldn't switch the
        // whole detail path on. No placeholder detail texture is invented; the already-existing
        // default model texture just stands in to keep the set bound.
        bool hasDetail = material.DetailTexture != null && material.UsesDetailMap;
        var detail = hasDetail ? GetOrBuildTexture(material.DetailTexture!) : GlobalResource.DefaultModelTexture;
        bMat.AddMaterialMap(new MaterialMapKey("fDetail"), 5, new MaterialMap(
            detail, ResolveSampler(detail), value: hasDetail ? 1f : 0f));

        // BAKED LIGHTING (the game's own, from zone sections 0x5400 / 0x5410) — per-INSTANCE, which
        // is why the material cache is keyed on the lightmap index. fLightColour's value slot
        // doubles as the "this material actually has a bake" flag the shader branches on; without
        // it the fallback textures below would read as a real full-strength white light.
        // Both are ALWAYS bound even when absent: a declared descriptor set left unbound is
        // undefined behaviour, and is exactly how the earlier GPUVM fault manifested.
        bool hasBakedLighting = EnableBakedLighting
            && lightmapIndex != Loading.Objects.Instances.TieInstance.NoLightmap
            && lightmapIndex < ZoneLightmaps.Count && ZoneLightmaps[lightmapIndex] != null
            && lightmapIndex < ZoneDirectionals.Count && ZoneDirectionals[lightmapIndex] != null;

        var lightColour = hasBakedLighting ? ZoneLightmaps[lightmapIndex]! : GetDefaultLightColourTexture();
        var lightDir = hasBakedLighting ? ZoneDirectionals[lightmapIndex]! : GetDefaultLightDirTexture();
        bMat.AddMaterialMap(new MaterialMapKey("fLightColour"), 6, new MaterialMap(lightColour, ResolveSampler(lightColour), value: hasBakedLighting ? 1f : 0f));
        bMat.AddMaterialMap(new MaterialMapKey("fLightDir"), 7, new MaterialMap(lightDir, ResolveSampler(lightDir)));

        // Value-only carrier for the raw-Vulkan renderer: 1 when opacity should come from the per-vertex
        // alpha rather than the albedo's own alpha (a transparent material whose albedo has no alpha
        // channel — see MaterialReader.UsesVertexAlphaCandidate). The albedo of such materials decodes
        // to a meaningless alpha, so the VK shader SELECTS vertex vs texture alpha on this flag rather
        // than multiplying them.
        // Data the raw-Vulkan renderer needs that does NOT fit in a MaterialMap: Bliss has exactly 8
        // map slots (0-7) and all are spoken for above, so adding more silently fails to register and
        // reads back as 0. Kept in a side table keyed by the built material instead.
        _vkMaterialInfo[bMat] = (material.GameRenderMode, material.UsesVertexAlphaCandidate);

        _materialCache[cacheKey] = bMat;
        _sourceMaterials[material.Id] = material;
        if (!_materialsByShader.TryGetValue(material.Id, out var variants))
            _materialsByShader[material.Id] = variants = [];
        variants.Add(bMat);
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

    /// <summary>Camera-facing sprite effect for foliage. Deliberately NOT reachable through
    /// SelectEffect: nothing about a material says "this is a billboard", it is a property of the
    /// GEOMETRY (foliage packs a shared anchor into the position and the corner offset into
    /// TexCoords2), so routing by material would silently billboard any mesh that happened to use
    /// a foliage shader. EntityFoliage asks for it explicitly.</summary>
    public Bliss.CSharp.Materials.Material GetOrBuildBillboardMaterial(IMaterial? material)
    {
        // Foliage is always double-sided and always blended: both of metropolis's foliage shaders
        // are RenderingMode.Blended, and a billboard has no meaningful facing to cull against.
        // Translucent also puts it in DecalAwareForwardRenderer's second pass, which is where the
        // game draws every non-opaque surface.
        var billboard = new Bliss.CSharp.Materials.Material(
            GetBillboardModelEffect(),
            BiasedCullNoneState,
            BlendStateDescription.SINGLE_ALPHA_BLEND,
            RenderMode.Translucent);

        var albedo = material?.AlbedoTexture != null ? GetOrBuildTexture(material.AlbedoTexture) : GlobalResource.DefaultModelTexture;
        billboard.AddMaterialMap(
            new MaterialMapKey(MaterialMapType.Albedo), 0,
            new MaterialMap(albedo, ResolveSampler(albedo), color: Color.White, value: material?.AlphaClipThreshold ?? 0f));

        return billboard;
    }

    private Effect GetBillboardModelEffect() => _billboardModelEffect ??= BuildBillboardModelEffect();

    // Identical layout to BuildVertexAlphaModelEffect - see the ordering note on the lit effect for
    // why buffers must take contiguous slots from 0 with textures immediately after.
    private Effect BuildBillboardModelEffect()
    {
        var effect = new Effect(_gd, BillboardModelShaderSource.Vertex, BillboardModelShaderSource.Fragment, new CrossCompileOptions(), []);
        effect.AddBufferLayout("MatrixBuffer", 0u, SimpleBufferType.Uniform, ShaderStages.Vertex);
        effect.AddBufferLayout("TransformBuffer", 1u, SimpleBufferType.Uniform, ShaderStages.Vertex);
        effect.AddBufferLayout("MaterialBuffer", 2u, SimpleBufferType.Uniform, ShaderStages.Fragment);
        effect.AddTextureLayout(MaterialMapType.Albedo.GetName(), 3u);
        return effect;
    }

    private Effect BuildVertexAlphaModelEffect()
    {
        var effect = new Effect(_gd, VertexAlphaModelShaderSource.Vertex, VertexAlphaModelShaderSource.Fragment, new CrossCompileOptions(), []);
        effect.AddBufferLayout("MatrixBuffer", 0u, SimpleBufferType.Uniform, ShaderStages.Vertex);
        effect.AddBufferLayout("TransformBuffer", 1u, SimpleBufferType.Uniform, ShaderStages.Vertex);
        effect.AddBufferLayout("MaterialBuffer", 2u, SimpleBufferType.Uniform, ShaderStages.Fragment);
        effect.AddTextureLayout(MaterialMapType.Albedo.GetName(), 3u);
        return effect;
    }

    // Same MatrixBuffer@0/TransformBuffer@1/MaterialBuffer@2 base as the other two effects, plus a
    // LightBuffer@3 (fragment-stage uniform: direction, ambient, color, camera position — see
    // LightData/EditorSettings.LightDirection etc.), then Albedo@4, Normal@5 and a Properties
    // texture@6 (specular/parallax-height/emissive intensities — see GetOrBuildMaterial).
    // DecalAwareForwardRenderer is what actually binds LightBuffer's resource set (conditionally,
    // only for effects that declare it) — see its DrawPreparedRenderable.
    //
    // ORDERING IS LOAD-BEARING, not stylistic: SimplePipeline builds the pipeline's ResourceLayout
    // array as [every buffer layout, in registration order] ++ [every texture layout, in
    // registration order], and the Vulkan set index is the POSITION in that array — while
    // Effect.GetBufferLayoutSlot/GetTextureLayoutSlot (what DecalAwareForwardRenderer binds
    // through) return the slot number declared here. So the two only agree when every buffer takes
    // a contiguous slot from 0 and every texture follows immediately after, which is exactly what
    // Bliss's own effects do (see GlobalResource: DefaultSkinnedModelEffect registers buffers
    // 0-3 then Albedo at 4, and its .frag declares set=3/set=4 to match).
    // This previously declared LightBuffer at 5, interleaved after the textures at 3/4. The
    // pipeline still laid it out at position 3, so set 3 was a uniform-buffer layout that the
    // shader read as texture2D and set 5 was a texture layout that the shader read as a uniform
    // buffer. The scalar load of that mangled descriptor is a GPUVM fault
    // (CLIENT_ID = SQC (data)) — a hard GPU hang on RADV as soon as lit geometry drew.
    // Any new binding added here must keep this invariant, and match LitModelShaderSource's
    // "set = N" declarations one-to-one.
    private Effect GetLitModelEffect() => _litModelEffect ??= BuildLitModelEffect();

    private Effect BuildLitModelEffect()
    {
        var effect = new Effect(_gd, LitModelShaderSource.Vertex, LitModelShaderSource.Fragment, new CrossCompileOptions(), []);
        effect.AddBufferLayout("MatrixBuffer", 0u, SimpleBufferType.Uniform, ShaderStages.Vertex);
        // Slot 1 is the per-object world transform. Unlike the unlit effects (a per-draw uniform), the
        // lit vertex shader reads it from a storage buffer indexed by gl_InstanceIndex — see
        // litmodelv.glsl's InstanceTransforms — so DecalAwareForwardRenderer can upload every visible
        // transform once and issue instanced draws. The layout is declared MANUALLY here (Bliss does
        // not reflect it from the shader), so the name/type/slot MUST match the shader exactly.
        effect.AddBufferLayout("InstanceTransforms", 1u, SimpleBufferType.StructuredReadOnly, ShaderStages.Vertex);
        effect.AddBufferLayout("MaterialBuffer", 2u, SimpleBufferType.Uniform, ShaderStages.Fragment);
        effect.AddBufferLayout("LightBuffer", 3u, SimpleBufferType.Uniform, ShaderStages.Fragment);
        effect.AddTextureLayout(MaterialMapType.Albedo.GetName(), 4u);
        effect.AddTextureLayout(MaterialMapType.Normal.GetName(), 5u);
        effect.AddTextureLayout("fProperties", 6u);
        effect.AddTextureLayout("fDetail", 7u);
        effect.AddTextureLayout("fLightColour", 8u);
        effect.AddTextureLayout("fLightDir", 9u);
        // The environment cubemap (samplerCube). Last texture slot, keeping the buffers-0..3 then
        // textures-4..N ordering the pipeline layout depends on. Bound scene-wide by
        // DecalAwareForwardRenderer (not a per-material MaterialMap), same as LightBuffer.
        effect.AddTextureLayout("fEnvCube", 10u);
        return effect;
    }

    private Texture2D GetDefaultNormalTexture() => _defaultNormalTexture ??=
        new Texture2D(_gd, new Image(1, 1, new Color(128, 128, 128, 128)));

    // Inert per-channel defaults matching the confirmed expensive-map layout (see
    // LitModelShaderSource): R=0 no specular, G=0 flat parallax height, B=0 no emissive,
    // A=0 no detail mask (so a material with no expensive map pulls in no detail either — A is
    // the detail-map mask, NOT roughness; that reading is retracted). Same "inert" fallback role
    // GetDefaultNormalTexture plays for Normal.
    private Texture2D GetDefaultPropertiesTexture() => _defaultPropertiesTexture ??=
        new Texture2D(_gd, new Image(1, 1, new Color(0, 0, 0, 0)));

    // Bound for materials with no baked lighting, purely so the declared descriptor sets are never
    // left unbound. Their CONTENTS are irrelevant: the shader gates the whole baked path on
    // maps[6].value, which is 0 for these materials. White / straight-up are chosen anyway so that
    // if the flag were ever wrongly set, the result is plainly wrong rather than subtly odd.
    private Texture2D GetDefaultLightColourTexture() => _defaultLightColourTexture ??=
        new Texture2D(_gd, new Image(1, 1, new Color(255, 255, 255, 255)));

    // (128,128,255) decodes through the shader's signed expansion to a tangent-space (0,0,1),
    // i.e. light coming straight along the surface normal.
    private Texture2D GetDefaultLightDirTexture() => _defaultLightDirTexture ??=
        new Texture2D(_gd, new Image(1, 1, new Color(128, 128, 255, 255)));

    // No GetDefaultDetailTexture counterpart on purpose — see GetOrBuildMaterial. A material
    // without a detail map gets zero strengths rather than a fabricated inert texture, so the
    // "nothing happens" guarantee doesn't depend on getting a placeholder's channel encoding right
    // (which would matter: 0 is NOT neutral for the signed-expanded R,G derivatives, it decodes to
    // a full -1, the same trap GetDefaultNormalTexture avoids by using 128).

    // Live opt-in toggle, same rationale/pattern as SetBackfaceCulling below — Material.Effect is
    // a plain public field, so swapping it on already-cached materials takes effect on the very
    // next draw with no rebuild needed. Every material's Vertex3D layout is identical regardless
    // of which of these three effects ends up drawing it, so this is safe across the swap.
    public void SetLightingEnabled(bool enabled)
    {
        if (_lightingEnabled == enabled) return;
        _lightingEnabled = enabled;

        foreach (var ((shaderId, _), bMat) in _materialCache)
            bMat.Effect = SelectEffect(_sourceMaterials[shaderId]);
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

        // Per material, not one shared state: toggling culling must not drop the depth bias off
        // the non-opaque ones, which would silently bring back overlay z-fighting whenever this
        // setting was touched. RasterizerStateFor keeps both axes in one place.
        foreach (var material in _materialCache.Values)
            material.RasterizerState = RasterizerStateFor(material.RenderMode);
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

    // The three per-channel detail strengths start INERT, not at 1. The game's equivalents are
    // fragment constants not located in ShaderMetadata yet, so there is no evidence for any value
    // — and unlike a multiplicative factor, an additive term has no well-defined "neutral". 1 is
    // actively unsafe here: every one of these contributions is added, and the detail mask driving
    // them is the expensive map's alpha, which BC1 decodes as 255 on every DXT1 expensive map (see
    // TextureUtils' Bc1Decoder). At strength 1 that means a full 1.0 lift added to linear albedo,
    // a full 1.0 added to specular intensity, and a +-1 perturbation added to derivatives already


    // Fallback only, for materials whose metadata has no identified tiling (new engine, or a
    // literal 0 in the field). The real value comes from IMaterial.DetailTiling / metadata 0x58.
    // 1 = detail sampled at the same frequency as the base map.
    public const float DefaultDetailTiling = 1f;

    /// <summary>Live per-material parallax scale/bias, reproducing the game's own
    /// height * scale + bias (see GetOrBuildMaterial's fParallaxScale/fParallaxBias maps, i.e.
    /// maps[3].value / maps[4].value in LitModelShaderSource). SetMapValue marks the Bliss material
    /// dirty; DecalAwareForwardRenderer propagates that to every renderable sharing the material
    /// (see its Draw for why that propagation can't rely on Bliss's own flag alone). materialId is
    /// the shader TUID, same key GetOrBuildMaterial caches under.</summary>
    public void SetParallax(ulong materialId, float scale, float bias)
    {
        if (!_materialsByShader.TryGetValue(materialId, out var variants)) return;
        foreach (var bMat in variants)
        {
            bMat.SetMapValue(new MaterialMapKey("fParallaxScale"), scale);
            bMat.SetMapValue(new MaterialMapKey("fParallaxBias"), bias);
        }
    }

    /// <summary>False when the material hasn't been built (nothing in the loaded region uses it)
    /// — callers should hide the control rather than show a dead default.</summary>
    public bool TryGetParallax(ulong materialId, out float scale, out float bias)
    {
        // Every variant of a shader is tuned together, so reading the first is representative.
        if (_materialsByShader.TryGetValue(materialId, out var variants) && variants.Count > 0)
        {
            var bMat = variants[0];
            scale = bMat.GetMapValue(new MaterialMapKey("fParallaxScale"));
            bias = bMat.GetMapValue(new MaterialMapKey("fParallaxBias"));
            return true;
        }
        scale = 0f;
        bias = 0f;
        return false;
    }

    /// <summary>Live per-material detail-map UV tiling (ShaderMetadataOld 0x58 — confirmed by the
    /// EBOOT reverse AND by in-game visual comparison). There are no per-channel detail STRENGTHS: the
    /// floats once read as those turned out to be an unrelated RGB parameter triple, so only tiling
    /// remains tunable here. Rides the fProperties map's value slot — see GetOrBuildMaterial.</summary>
    public void SetDetailTiling(ulong materialId, float tiling)
    {
        if (!_materialsByShader.TryGetValue(materialId, out var variants)) return;
        foreach (var bMat in variants)
            bMat.SetMapValue(new MaterialMapKey("fProperties"), tiling);
    }

    /// <summary>False when the material hasn't been built (see TryGetParallax) OR has no detail
    /// texture at all — in the latter case there is nothing to tune, so callers should hide the
    /// control rather than offer a slider against a placeholder binding.</summary>
    public bool TryGetDetailTiling(ulong materialId, out float tiling)
    {
        if (_materialsByShader.TryGetValue(materialId, out var variants) && variants.Count > 0
            && _sourceMaterials.TryGetValue(materialId, out var source) && source.DetailTexture != null)
        {
            tiling = variants[0].GetMapValue(new MaterialMapKey("fProperties"));
            return true;
        }
        tiling = DefaultDetailTiling;
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
    /// <summary>Whether this source format physically carries an alpha channel. Formats without
    /// one decode to a synthesised opaque 255, which must not be mistaken for authored data — see
    /// GetOrBuildMaterial's detail-mask handling. DXT3/DXT5 carry explicit alpha; DXT1's 1-bit
    /// punch-through is a per-block transparency flag, not a mask channel, so it counts as none.
    /// </summary>
    private static bool HasAlphaChannel(Assets.Interfaces.TextureFormat format) => format switch
    {
        Assets.Interfaces.TextureFormat.A8R8G8B8 => true,
        Assets.Interfaces.TextureFormat.DXT3 => true,
        Assets.Interfaces.TextureFormat.DXT5 => true,
        Assets.Interfaces.TextureFormat.A1R5G5B5 => true,
        Assets.Interfaces.TextureFormat.RGBA4 => true,
        Assets.Interfaces.TextureFormat.RGBA16F => true,
        _ => false,
    };

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

    /// <summary>mipmap: pass false for ATLASES. Mip generation averages neighbouring texels, which
    /// on an atlas blends across island boundaries — and the baked lightmap atlases have black
    /// gutters between their islands, so every minified pixel near an island edge pulls that black
    /// inward. That shows up as dark patches on lit terrain with no counterpart in the game.
    /// Normal textures keep mipmaps: they tile, so there are no islands to bleed between.</summary>
    public Texture2D GetOrBuildTexture(ITexture texture, bool mipmap = true)
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
        var tex = new Texture2D(_gd, image, mipmap);
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
    private static int _vertexAlphaDiagnosticsLogged;

    private static Vertex3D[] ConvertGeometryToVertices(IGeometry geometry, bool useVertexAlpha)
    {
        var positions = geometry.GetVertexPositions();
        var uvs = geometry.GetTextureCoordinates();
        var normals = geometry.GetNormals();
        var tangents = geometry.GetTangents();
        var vertexAlpha = useVertexAlpha ? geometry.GetVertexAlphaCandidates() : null;
        // TEMP diagnostic: vertex-alpha materials source their opacity from this array, so an all-zero
        // (or absent) one renders those surfaces fully transparent, i.e. invisible.
        if (useVertexAlpha && _vertexAlphaDiagnosticsLogged < 8)
        {
            _vertexAlphaDiagnosticsLogged++;
            if (vertexAlpha == null || vertexAlpha.Length == 0)
                Console.WriteLine("[VertexAlpha] material wants vertex alpha but geometry has NO candidates (falls back to opaque 1.0).");
            else
            {
                float mn = float.MaxValue, mx = float.MinValue, sum = 0f;
                foreach (float a in vertexAlpha) { mn = MathF.Min(mn, a); mx = MathF.Max(mx, a); sum += a; }
                Console.WriteLine($"[VertexAlpha] {vertexAlpha.Length} candidates: min={mn:0.###} max={mx:0.###} avg={sum / vertexAlpha.Length:0.###}");
            }
        }
        var lightmapUVs = geometry.GetLightmapUVs();

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

            // Second UV channel is the LIGHTMAP UV set where the geometry has one (ties do; see
            // IGeometry.GetLightmapUVs). Falling back to the base UV keeps the attribute
            // well-defined for everything else, and is harmless because no lightmap is bound for
            // those draws — mirrors EntityUFrag.ConvertUFragToVertices.
            var lmUV = lightmapUVs != null && lightmapUVs.Length >= i * 2 + 2
                ? new Vector2(lightmapUVs[i * 2], lightmapUVs[i * 2 + 1])
                : uv;

            float alpha = vertexAlpha != null && i < vertexAlpha.Length ? vertexAlpha[i] : 1f;
            vertices[i] = new Vertex3D(pos, uv, lmUV, n, tan, new Vector4(1f, 1f, 1f, alpha));
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
        _defaultLightColourTexture?.Dispose();
        _defaultLightColourTexture = null;
        _defaultLightDirTexture?.Dispose();
        _defaultLightDirTexture = null;

        _vertexAlphaModelEffect?.Dispose();
        _vertexAlphaModelEffect = null;
        _billboardModelEffect?.Dispose();
        _billboardModelEffect = null;
        _litModelEffect?.Dispose();
        _litModelEffect = null;

        EnvironmentCubemapView?.Dispose();
        EnvironmentCubemapView = null;
        _environmentCubemap?.Dispose();
        _environmentCubemap = null;

        Mobys.Clear();
        Ties.Clear();
        _materialCache.Clear();
        _materialsByShader.Clear();
        _sourceMaterials.Clear();
        _textureCache.Clear();
        _sourceTextures.Clear();
        _builtTextureIds.Clear();
        _perTextureFiltering.Clear();
    }
}
