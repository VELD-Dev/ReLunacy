using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Engine.Scene;
using NeoVeldrid;
using IMesh = ReLunacy.Engine.Assets.Interfaces.IMesh;

namespace ReLunacy.Engine.Rendering;

// Builds renderer-side resources (RenderMesh/RenderModel/RenderMaterial/GpuTexture) from the
// engine-owned asset model (Assets.Mobys.Moby / Assets.Ties.Tie / their meshes' IMaterial/ITexture),
// caching by TUID so shared materials/textures aren't rebuilt per mesh.
public sealed class AssetManager : IDisposable
{
    private readonly GraphicsDevice _gd;

    /// <summary>The whole level's geometry/materials/textures, captured once and replayed every frame
    /// by whichever 3D view needs it. Lives here, not on a DockedFrame, specifically so closing and
    /// reopening the 3D View panel does not force re-uploading the entire level to the GPU: this
    /// object's lifetime matches the LEVEL (constructed with the rest of AssetManager, disposed by
    /// <see cref="DisposeSceneRenderer"/> on level unload), not any particular panel's open/closed
    /// state. The panel still owns calling into it every frame (Frame/SubmitFrame/Pick/Resize) - only
    /// the expensive captured GPU state moved, not who drives it or when.</summary>
    public VulkanRenderer? SceneRenderer { get; private set; }

    private readonly Dictionary<ulong, GpuTexture> _textureCache = [];
    private readonly Dictionary<ulong, ITexture> _sourceTextures = [];
    // Keyed on (shader TUID, lightmap index), NOT on the TUID alone. Baked lighting is a
    // per-INSTANCE property - one shader is shared across many UFrags, each with its own entry in
    // the zone's 0x5400/0x5410 lists - while Bliss binds textures through the Material. Caching by
    // TUID alone would therefore give every instance whichever lightmap happened to be built
    // first. Materials with no baked lighting all collapse onto UFragMetadata.NoLightmap, so
    // nothing that existed before this distinction pays for it.
    private readonly Dictionary<(ulong ShaderId, ushort LightmapIndex), RenderMaterial> _materialCache = [];
    // Every built variant of a given shader TUID. The live-tuning API (SetParallax,
    // SetDetailTiling) is addressed by TUID because that is what the ShaderBrowser lists, so it
    // has to reach all of a shader's lightmap variants rather than just one.
    private readonly Dictionary<ulong, List<RenderMaterial>> _materialsByShader = [];
    // Parallel to _materialCache, keyed by shader TUID: the built material keeps none of the source
    // metadata, and the live-tuning API needs it back (see TryGetDetailTiling, which hides its control
    // for a shader that has no detail texture at all).
    private readonly Dictionary<ulong, IMaterial> _sourceMaterials = [];
    // Per-built-material data for the raw-Vulkan renderer: the game's own rendering mode (0-6),
    // whether vertex alpha was decoded for it, and whether its albedo's own alpha is real. A side
    // table rather than three more map slots, because none of them is a texture and the renderer
    // wants them as one lookup.
    private readonly Dictionary<RenderMaterial, (byte GameRenderMode, bool UsesVertexAlpha, bool AlbedoHasAlphaChannel)> _vkMaterialInfo =
        new(ReferenceEqualityComparer.Instance);

    // Foliage billboard materials: the set is what IsBillboardMaterial answers from, the cache is what
    // keeps one Material per source shader instead of one per placement.
    private readonly HashSet<RenderMaterial> _billboardMaterials = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ulong, RenderMaterial> _billboardMaterialCache = new();

    /// <summary>The game's rendering mode (0 Opaque, 1 Overlay, 2 Additive, 3 Scunge, 4 Cutout,
    /// 5 Soft-Edge, 6 Blended) and vertex-alpha flag for a built material. See IMaterial.GameRenderMode
    /// and dev/chatgpt-eboot-1..3.txt for what each mode's real RSX state is.</summary>
    public bool TryGetVkMaterialInfo(RenderMaterial bMat, out byte gameRenderMode, out bool usesVertexAlpha, out bool albedoHasAlphaChannel)
    {
        if (_vkMaterialInfo.TryGetValue(bMat, out var info))
        {
            (gameRenderMode, usesVertexAlpha, albedoHasAlphaChannel) = info;
            return true;
        }
        gameRenderMode = 0;
        usesVertexAlpha = false;
        albedoHasAlphaChannel = false;
        return false;
    }
    // Scene-wide default filtering plus per-texture overrides (keyed by texture TUID) - samplers
    // resolve through GetSamplerFor in exactly one place, so future filtering techniques are one
    // new TextureFiltering value + one switch arm. _builtTextureIds is the GpuTexture -> TUID
    // reverse of _textureCache, needed to re-resolve an already-built MaterialMap's sampler live
    // (the map only holds the GPU texture, not the id it was built from).
    private TextureFiltering _defaultTextureFiltering = TextureFiltering.Point;
    private readonly Dictionary<ulong, TextureFiltering> _perTextureFiltering = [];
    private readonly Dictionary<GpuTexture, ulong> _builtTextureIds = [];
    // Flat "no perturbation" fallback for the normal map slot, which every material gets an entry for
    // (see GetOrBuildMaterial) even when the source has no NormalTexture at all: the renderer samples
    // every slot unconditionally, same reason Albedo falls back to GetDefaultAlbedoTexture.
    // G=128, A=128 decodes to dx=dy=0 under LitModelShaderSource's derivative reconstruction,
    // i.e. a perfectly flat tangent-space normal (0,0,1) - R/B are unused by that reconstruction,
    // so their value doesn't matter.
    private GpuTexture? _defaultAlbedoTexture;
    private GpuTexture? _defaultNormalTexture;
    // See GetDefaultPropertiesTexture.
    private GpuTexture? _defaultPropertiesTexture;
    // See GetDefaultLightColourTexture / GetDefaultLightDirTexture.
    private GpuTexture? _defaultLightColourTexture;
    private GpuTexture? _defaultLightDirTexture;

    // The non-opaque polygon offset the game applies (depth bias -87, slope-scaled -0.33972, read
    // out of a RenderDoc capture) used to be built into a RasterizerStateDescription here. It now
    // lives with the renderer that applies it, as VulkanRenderer.NonOpaqueDepthBias, along with the
    // full derivation of where those two numbers come from.

    public IReadOnlyDictionary<ulong, GpuTexture> BuiltTextures => _textureCache;
    public IReadOnlyDictionary<ulong, ITexture> SourceTextures => _sourceTextures;

    public Dictionary<ulong, RenderModel[]> Mobys { get; } = []; // one Model per bangle
    public Dictionary<ulong, RenderModel> Ties { get; } = [];

    // Decoded textures waiting to be uploaded, filled by PrepareTextures before anything is built.
    // Entries are REMOVED as they are consumed, so the decoded pixels are freed as the build walks
    // past them rather than being held for the whole load. A present-but-null entry means the decode
    // was attempted and failed, which is different from never having been prepared.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, TextureLevels?> _prepared = new();

    /// <param name="preparedTextures">Textures already decoded by <see cref="PrepareTextures"/>, ideally
    /// on the loading task so this constructor never pays for them. Null decodes them here instead,
    /// which is correct but blocks whatever thread this runs on.</param>
    public AssetManager(LevelData level, GraphicsDevice gd, IDictionary<ulong, TextureLevels?>? preparedTextures = null)
    {
        _gd = gd;
        var sw = System.Diagnostics.Stopwatch.StartNew(); long t0 = 0;

        foreach (var (id, levels) in preparedTextures ?? PrepareTextures(level)) _prepared[id] = levels;

        foreach (var (id, moby) in level.Mobys)
        {
            var models = new RenderModel[moby.Bangles.Count];
            for (int i = 0; i < moby.Bangles.Count; i++)
            {
                models[i] = BuildModel(moby.Bangles[i].Meshes);
            }
            Mobys[id] = models;
        }

        long tMobys = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;
        foreach (var (id, tie) in level.Ties)
        {
            Ties[id] = BuildModel(tie.Meshes);
        }
        long tTies = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;

        // Build every texture the level loaded, not just the ones the loops above already pulled
        // in via a used material - some textures.dat/highmips.dat entries aren't wired to any
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

        long tTextures = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;
        ZoneLightmaps = BuildZoneLighting(level.ZoneLightmaps, "lightmap");
        ZoneDirectionals = BuildZoneLighting(level.ZoneDirectionals, "directional");
        if (ZoneLightmaps.Count != 0)
            Console.WriteLine($"Zone lighting: {ZoneLightmaps.Count} light-colour and {ZoneDirectionals.Count} light-direction textures built.");

        long tZone = sw.ElapsedMilliseconds - t0;
        BuildEnvironmentCubemap(level);
        if (!_prepared.IsEmpty)
            Console.WriteLine($"Warning: {_prepared.Count} decoded textures were prepared but never built - they are holding memory for nothing.");
        // Timings kept because load time is a feature here and these are what showed where it went.
        // GPU upload no longer happens in here at all (see GetOrBuildTexture/UploadOnePendingTexture) -
        // these numbers are now CPU-only mesh/material building, which is why they're small even on a
        // level with thousands of materials; TotalQueuedUploads is what the caller drains afterwards.
        Console.WriteLine($"Assets built in {sw.ElapsedMilliseconds}ms (mobys {tMobys}, ties {tTies}, textures {tTextures}, zone lighting {tZone}). {TotalQueuedUploads} textures queued for GPU upload.");
    }

    /// <summary>Decodes every texture the level is about to need, in parallel, before a single one is
    /// uploaded.
    ///
    /// This is the whole point of the exercise. Decoding and mip generation were ~6.7s of the ~10.5s
    /// the asset build spent frozen on metropolis, and both are plain arithmetic over byte arrays.
    /// What is left on this thread afterwards is the GPU upload, which cannot move.
    ///
    /// Safe to run concurrently because an ITexture's pixels are already in memory by this point: the
    /// file reads all happened on the loading task, and Texture.GetPixelData just hands back the array
    /// its loader closed over. The block decoders are shared statics but hold only readonly
    /// configuration, so they have no state to race on.</summary>
    public static Dictionary<ulong, TextureLevels?> PrepareTextures(LevelData level)
    {
        // Atlases are built without mips: averaging neighbouring texels across an island boundary pulls
        // the black gutters inward. Collected first so the decode below knows which is which.
        var withoutMips = new HashSet<ulong>();
        foreach (var texture in level.ZoneLightmaps) if (texture != null) withoutMips.Add(texture.Id);
        foreach (var texture in level.ZoneDirectionals) if (texture != null) withoutMips.Add(texture.Id);

        var needed = new Dictionary<ulong, ITexture>();
        void Need(ITexture? texture) { if (texture != null) needed.TryAdd(texture.Id, texture); }
        void NeedMaterial(IMaterial? material)
        {
            if (material == null) return;
            Need(material.AlbedoTexture);
            Need(material.NormalTexture);
            Need(material.PropertiesTexture);
            Need(material.DetailTexture);
        }

        foreach (var texture in level.AllTextures.Values) Need(texture);
        foreach (var texture in level.ZoneLightmaps) Need(texture);
        foreach (var texture in level.ZoneDirectionals) Need(texture);
        foreach (var (_, moby) in level.Mobys)
            foreach (var bangle in moby.Bangles)
                foreach (var mesh in bangle.Meshes) NeedMaterial(mesh.Material);
        foreach (var (_, tie) in level.Ties)
            foreach (var mesh in tie.Meshes) NeedMaterial(mesh.Material);

        var prepared = new System.Collections.Concurrent.ConcurrentDictionary<ulong, TextureLevels?>();
        Parallel.ForEach(needed.Values, texture =>
        {
            try
            {
                byte[]? rgba = TextureUtils.DecodeToRgba8888(texture, out int width, out int height);
                prepared[texture.Id] = rgba == null
                    ? null
                    : TextureLevels.Prepare((uint)width, (uint)height, rgba, !withoutMips.Contains(texture.Id));
            }
            catch (Exception ex)
            {
                // Recorded as a failure rather than rethrown: one unreadable texture must not take the
                // level down, and GetOrBuildTexture substitutes the default for a null entry exactly
                // as it does for a texture that decodes to nothing.
                prepared[texture.Id] = null;
                Console.WriteLine($"Warning: Failed to decode texture {texture.Id:X}: {ex.Message}");
            }
        });
        return new Dictionary<ulong, TextureLevels?>(prepared);
    }

    // The level's environment cubemap as a GPU samplerCube, for the lit shader's reflection term.
    // Always non-null once constructed: a level with no cubemap gets a 1x1 grey fallback so the lit
    // effect's declared set 10 is never bound to nothing (an unbound descriptor set is undefined
    // behaviour - the same class of fault BuildLitModelEffect documents). See CubemapReader for the
    // face format and LitModelShaderSource for how it's sampled.
    private NeoVeldrid.Texture? _environmentCubemap;
    public NeoVeldrid.TextureView? EnvironmentCubemapView { get; private set; }

    private void BuildEnvironmentCubemap(LevelData level)
    {
        var cubemap = level.Cubemaps.Count > 0 ? level.Cubemaps[0] : null;
        int size = cubemap?.FaceSize ?? 1;
        var factory = _gd.ResourceFactory;

        var tex = factory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(
            (uint)size, (uint)size, 1, 6, NeoVeldrid.PixelFormat.R8_G8_B8_A8_UNorm,
            NeoVeldrid.TextureUsage.Sampled | NeoVeldrid.TextureUsage.Cubemap));

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
    /// UFragVertex.UVs2, which are half-float atlas coordinates - 1377 of metropolis's 1987
    /// UFrags are lit this way across 23 atlases.
    /// TIES are still excluded (EntityTie pins them to NoLightmap): they carry a real per-instance
    /// bake index, but VertexFormat0 has one UV pair and it TILES, so there is nothing correct to
    /// sample an atlas with. That is what produced the blotchy black patching, and it comes back
    /// the moment ties are re-enabled without their own UV set.
    /// Kept as a const rather than a setting so it disappears once nothing is provisional.</summary>
    public const bool EnableBakedLighting = true;

    /// <summary>Positionally indexed by an instance's lightmap index (see
    /// TieInstance.LightmapIndex), so a failed entry becomes null rather than being dropped -
    /// removing it would shift every later index onto the wrong texture.</summary>
    public IReadOnlyList<GpuTexture?> ZoneLightmaps { get; } = [];
    public IReadOnlyList<GpuTexture?> ZoneDirectionals { get; } = [];

    private List<GpuTexture?> BuildZoneLighting(IReadOnlyList<ITexture> source, string label)
    {
        var built = new List<GpuTexture?>(source.Count);
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

    private RenderModel BuildModel(IReadOnlyList<IMesh> meshes)
    {
        var bMeshes = new RenderMesh[meshes.Count];
        for (int i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            var material = GetOrBuildMaterial(mesh.Material);
            var vertices = ConvertGeometryToVertices(mesh.Geometry, mesh.Material.UsesVertexAlphaCandidate);
            var indices = mesh.Geometry.GetIndices();

            var bMesh = new RenderMesh(vertices, indices, material);

            // Register this mesh's raw geometry (interleaved, plus indices) keyed by the mesh instance,
            // so the renderer can upload it once and resolve every scene instance back to it.
            if (vertices.Length > 0 && indices.Length >= 3)
                Vulkan.VulkanSceneCapture.Register(bMesh, Vulkan.VulkanSceneCapture.Interleave(vertices), indices);

            bMeshes[i] = bMesh;
        }
        return new RenderModel(bMeshes);
    }

    /// <summary>lightmapIndex: this instance's entry in the zone's baked-lighting lists (see
    /// IUFrag.LightmapIndex). Part of the cache key because baked lighting is per-instance while
    /// Bliss binds textures per-Material. Callers with no baked lighting omit it.</summary>
    public RenderMaterial GetOrBuildMaterial(IMaterial material, ushort lightmapIndex = Loading.Objects.UFragMetadata.NoLightmap)
    {
        var cacheKey = (material.Id, lightmapIndex);
        if (_materialCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var bMat = new RenderMaterial();

        // value = the game's own per-material alphaClip threshold (maps[0].value in
        // LitModelShaderSource's Cutout branch) - the same field GltfExporter already trusts for
        // glTF's alphaCutoff. Only meaningful for Cutout materials; harmless elsewhere.
        var albedo = material.AlbedoTexture != null ? GetOrBuildTexture(material.AlbedoTexture) : GetDefaultAlbedoTexture();
        bMat.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap(albedo, ResolveSampler(albedo), material.AlphaClipThreshold));

        // Always added (not conditional on NormalTexture existing) so LitModelShaderSource's
        // texture layout always has something bound to it once lighting is enabled - see
        // GetDefaultNormalTexture. Harmless for the unlit effects, which don't declare a Normal
        // texture layout at all, so this entry is just never looked up by anything.
        // This map's value slot is unrelated to the normal texture: it flags whether the expensive
        // map has a real alpha channel to read the detail mask from - see the detail-map section
        // below for why, and LitModelShaderSource's maps[1].value.
        var normal = material.NormalTexture != null ? GetOrBuildTexture(material.NormalTexture) : GetDefaultNormalTexture();
        bool detailMaskFromTexture = material.PropertiesTexture != null && HasAlphaChannel(material.PropertiesTexture.Format);
        bMat.AddMaterialMap(MaterialMapType.Normal, new MaterialMap(normal, ResolveSampler(normal), detailMaskFromTexture ? 1f : 0f));

        // "fProperties" (a custom name, not one of Bliss's built-in MaterialMapType slots - none of
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
        // constant to read it from - the tiling is baked into a vertex interpolant there.
        // A tiling of literally 0 would collapse the detail map to one texel, so it can't be what
        // the field means - treat it as "not present" and fall back to 1 (base-map frequency).
        var properties = material.PropertiesTexture != null ? GetOrBuildTexture(material.PropertiesTexture) : GetDefaultPropertiesTexture();
        float detailTiling = material.DetailTiling != 0f ? material.DetailTiling : DefaultDetailTiling;
        bMat.AddMaterialMap("fProperties", new MaterialMap(properties, ResolveSampler(properties), detailTiling));

        // Two textureless MaterialMaps used purely as transport for a per-material float each:
        // Bliss uploads every registered map's Value into MaterialBuffer's maps[slot].value
        // (Renderable.UpdateMaterialBuffer), and unassigned slots stay zero, so this is the
        // cheapest way to get a tunable scalar to the shader without adding a whole uniform buffer
        // and its resource-set plumbing. They match no texture layout name, so
        // DecalAwareForwardRenderer's texture loop skips them.
        //
        // These reproduce the real game's parallax form exactly - height * scale + bias, per the
        // captured shader, where both are per-material fragment constants. Live-tunable per shader
        // from the ShaderBrowser (see SetParallax) so candidate float pairs spotted in the raw
        // metadata hex dump can be tried directly against the game's look; the whole point is that
        // a value read out of the dump can be typed in verbatim, so no hidden scaling factor is
        // applied on top of these anywhere.
        bMat.AddMaterialMap("fParallaxScale", new MaterialMap(value: material.ParallaxScale));
        bMat.AddMaterialMap("fParallaxBias", new MaterialMap(value: material.ParallaxBias));

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
        // see IMaterial.UsesDetailMap) - declaring the map and enabling it are separate things, and
        // a texture reference left in the slot by an unused authoring path shouldn't switch the
        // whole detail path on. No placeholder detail texture is invented; the already-existing
        // default model texture just stands in to keep the set bound.
        bool hasDetail = material.DetailTexture != null && material.UsesDetailMap;
        var detail = hasDetail ? GetOrBuildTexture(material.DetailTexture!) : GetDefaultAlbedoTexture();
        bMat.AddMaterialMap("fDetail", new MaterialMap(detail, ResolveSampler(detail), hasDetail ? 1f : 0f));

        // BAKED LIGHTING (the game's own, from zone sections 0x5400 / 0x5410) - per-INSTANCE, which
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
        bMat.AddMaterialMap("fLightColour", new MaterialMap(lightColour, ResolveSampler(lightColour), hasBakedLighting ? 1f : 0f));
        bMat.AddMaterialMap("fLightDir", new MaterialMap(lightDir, ResolveSampler(lightDir)));

        // The game's own render mode, plus: 1 when this material has decoded vertex alpha to
        // contribute (any non-Opaque mode, see MaterialReader.UsesVertexAlphaCandidate), and whether
        // the albedo's own alpha channel is real enough to fold in alongside it (AlbedoHasAlphaChannel)
        // rather than being garbage sampled from a format with no alpha channel at all. Neither is a
        // texture, so neither belongs in a MaterialMap.
        _vkMaterialInfo[bMat] = (material.GameRenderMode, material.UsesVertexAlphaCandidate, material.AlbedoHasAlphaChannel);

        _materialCache[cacheKey] = bMat;
        _sourceMaterials[material.Id] = material;
        if (!_materialsByShader.TryGetValue(material.Id, out var variants))
            _materialsByShader[material.Id] = variants = [];
        variants.Add(bMat);
        return bMat;
    }

    /// <summary>The material for a foliage sprite card. Deliberately NOT reachable from
    /// GetOrBuildMaterial: nothing about a material says "this is a billboard", it is a property of the
    /// GEOMETRY (foliage packs a shared anchor into the position and the corner offset into
    /// TexCoords2), so routing by shader would silently billboard any mesh that happened to use a
    /// foliage shader. EntityFoliage asks for it explicitly.</summary>
    public RenderMaterial GetOrBuildBillboardMaterial(IMaterial? material)
    {
        // Cached per source shader. Every foliage PLACEMENT asks for its material, and a level has
        // hundreds of them (757 on metropolis) all sharing a handful of shaders - building a distinct
        // Material each time also gave the raw-Vulkan renderer one descriptor set per placement.
        if (_billboardMaterialCache.TryGetValue(material?.Id ?? ulong.MaxValue, out var cached)) return cached;

        // Foliage is always double-sided and always blended: both of metropolis's foliage shaders are
        // RenderingMode.Blended, and a billboard has no meaningful facing to cull against. The renderer
        // gets both facts from the source material's own GameRenderMode below.
        var billboard = new RenderMaterial();

        var albedo = material?.AlbedoTexture != null ? GetOrBuildTexture(material.AlbedoTexture) : GetDefaultAlbedoTexture();
        billboard.AddMaterialMap(
            MaterialMapType.Albedo,
            new MaterialMap(albedo, ResolveSampler(albedo), material?.AlphaClipThreshold ?? 0f));

        _vkMaterialInfo[billboard] = (material?.GameRenderMode ?? 0, material?.UsesVertexAlphaCandidate ?? false, material?.AlbedoHasAlphaChannel ?? false);
        _billboardMaterials.Add(billboard);
        _billboardMaterialCache[material?.Id ?? ulong.MaxValue] = billboard;
        return billboard;
    }

    /// <summary>True if this material was built for foliage sprite cards. The raw-Vulkan renderer needs
    /// to know because those cards are billboarded in the VERTEX SHADER from data packed into the
    /// geometry, so they need the billboard vertex shader rather than the lit one.</summary>
    public bool IsBillboardMaterial(RenderMaterial bMat) => _billboardMaterials.Contains(bMat);

    /// <summary>Plain white, the stand-in for any albedo-like slot with no texture of its own. Every
    /// slot is sampled unconditionally, so "no texture" still has to be something.</summary>
    private GpuTexture GetDefaultAlbedoTexture() => _defaultAlbedoTexture ??= GpuTexture.Solid(_gd, 255, 255, 255, 255);

    private GpuTexture GetDefaultNormalTexture() => _defaultNormalTexture ??= GpuTexture.Solid(_gd, 128, 128, 128, 128);

    // Inert per-channel defaults matching the confirmed expensive-map layout (see
    // LitModelShaderSource): R=0 no specular, G=0 flat parallax height, B=0 no emissive,
    // A=0 no detail mask (so a material with no expensive map pulls in no detail either - A is
    // the detail-map mask, NOT roughness; that reading is retracted). Same "inert" fallback role
    // GetDefaultNormalTexture plays for Normal.
    private GpuTexture GetDefaultPropertiesTexture() => _defaultPropertiesTexture ??= GpuTexture.Solid(_gd, 0, 0, 0, 0);

    // Bound for materials with no baked lighting, purely so the declared descriptor sets are never
    // left unbound. Their CONTENTS are irrelevant: the shader gates the whole baked path on
    // maps[6].value, which is 0 for these materials. White / straight-up are chosen anyway so that
    // if the flag were ever wrongly set, the result is plainly wrong rather than subtly odd.
    private GpuTexture GetDefaultLightColourTexture() => _defaultLightColourTexture ??= GpuTexture.Solid(_gd, 255, 255, 255, 255);

    // (128,128,255) decodes through the shader's signed expansion to a tangent-space (0,0,1),
    // i.e. light coming straight along the surface normal.
    private GpuTexture GetDefaultLightDirTexture() => _defaultLightDirTexture ??= GpuTexture.Solid(_gd, 128, 128, 255, 255);

    // No GetDefaultDetailTexture counterpart on purpose - see GetOrBuildMaterial. A material
    // without a detail map gets zero strengths rather than a fabricated inert texture, so the
    // "nothing happens" guarantee doesn't depend on getting a placeholder's channel encoding right
    // (which would matter: 0 is NOT neutral for the signed-expanded R,G derivatives, it decodes to
    // a full -1, the same trap GetDefaultNormalTexture avoids by using 128).

    // Lighting on/off and backface culling used to live here as live Effect / RasterizerState swaps
    // on the cached materials. Both are renderer state now: lighting is a uniform the shader branches
    // on every frame (VulkanRenderer.Frame's lit argument), and cull mode is baked into the renderer's
    // pipelines. Neither has anything left to do with a built material.

    // Live scene-wide filtering toggle: MaterialMap.Sampler is a plain public field, so mutating the
    // cached maps needs no texture or material rebuild.
    public void SetTextureFiltering(TextureFiltering filtering)
    {
        if (_defaultTextureFiltering == filtering) return;
        _defaultTextureFiltering = filtering;
        RefreshMaterialSamplers();
    }

    /// <summary>Per-texture override (by texture TUID) - the hook for future per-texture
    /// filtering techniques. Takes effect immediately, wins over the scene-wide default.</summary>
    public void SetTextureFiltering(ulong textureId, TextureFiltering filtering)
    {
        _perTextureFiltering[textureId] = filtering;
        RefreshMaterialSamplers();
    }

    // The three per-channel detail strengths start INERT, not at 1. The game's equivalents are
    // fragment constants not located in ShaderMetadata yet, so there is no evidence for any value
    // - and unlike a multiplicative factor, an additive term has no well-defined "neutral". 1 is
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
    /// - callers should hide the control rather than show a dead default.</summary>
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

    /// <summary>Live per-material detail-map UV tiling (ShaderMetadataOld 0x58 - confirmed by the
    /// EBOOT reverse AND by in-game visual comparison). There are no per-channel detail STRENGTHS: the
    /// floats once read as those turned out to be an unrelated RGB parameter triple, so only tiling
    /// remains tunable here. Rides the fProperties map's value slot - see GetOrBuildMaterial.</summary>
    public void SetDetailTiling(ulong materialId, float tiling)
    {
        if (!_materialsByShader.TryGetValue(materialId, out var variants)) return;
        foreach (var bMat in variants)
            bMat.SetMapValue(new MaterialMapKey("fProperties"), tiling);
    }

    /// <summary>False when the material hasn't been built (see TryGetParallax) OR has no detail
    /// texture at all - in the latter case there is nothing to tune, so callers should hide the
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
    // _builtTextureIds and just take the scene default - a per-texture override for a 1x1
    // constant would be meaningless anyway.
    /// <summary>Whether this source format physically carries an alpha channel. Formats without
    /// one decode to a synthesised opaque 255, which must not be mistaken for authored data - see
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

    private Sampler ResolveSampler(GpuTexture? texture) =>
        GetSamplerFor(texture != null && _builtTextureIds.TryGetValue(texture, out var id) && _perTextureFiltering.TryGetValue(id, out var overridden)
            ? overridden
            : _defaultTextureFiltering);

    // The single TextureFiltering -> GPU sampler mapping. Game textures always tile, so every
    // mode maps to a Wrap-addressing sampler; new filtering techniques are one new enum value
    // plus one arm here. Created once each and reused: a Sampler is immutable state, and a level
    // asks for one per material map.
    private Sampler? _pointWrapSampler;
    private Sampler? _linearWrapSampler;

    private Sampler GetSamplerFor(TextureFiltering filtering) => filtering switch
    {
        TextureFiltering.Bilinear => _linearWrapSampler ??= CreateWrapSampler(SamplerFilter.MinLinear_MagLinear_MipLinear),
        _ => _pointWrapSampler ??= CreateWrapSampler(SamplerFilter.MinPoint_MagPoint_MipPoint),
    };

    private Sampler CreateWrapSampler(SamplerFilter filter) => _gd.ResourceFactory.CreateSampler(new SamplerDescription(
        SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap,
        filter, comparisonKind: null, maximumAnisotropy: 0,
        // The whole chain. GpuTexture builds one down to 1x1, and clamping the maximum here would
        // silently pin minified textures to whichever level the clamp landed on.
        minimumLod: 0, maximumLod: uint.MaxValue, lodBias: 0, borderColor: SamplerBorderColor.TransparentBlack));

    /// <summary>mipmap: pass false for ATLASES. Mip generation averages neighbouring texels, which
    /// on an atlas blends across island boundaries - and the baked lightmap atlases have black
    /// gutters between their islands, so every minified pixel near an island edge pulls that black
    /// inward. That shows up as dark patches on lit terrain with no counterpart in the game.
    /// Normal textures keep mipmaps: they tile, so there are no islands to bleed between.</summary>
    public GpuTexture GetOrBuildTexture(ITexture texture, bool mipmap = true)
    {
        if (_textureCache.TryGetValue(texture.Id, out var cached))
            return cached;

        _sourceTextures[texture.Id] = texture;

        // Normally already decoded by PrepareTextures; decoded here only for a texture that pre-pass
        // could not see (UFrag and foliage materials are reached during entity loading, after it ran).
        // Removed rather than read, so the decoded pixels are freed once uploaded.
        if (!_prepared.TryRemove(texture.Id, out var levels))
        {
            // Some texture slots genuinely have no highmip data for a given level (Texture.ReadTexture
            // returns early, leaving data empty, when the highmips pointer's length is 0) - a real,
            // already-handled case in the loader, not a corrupt read. TextureUtils.DecodeToRgba8888
            // returns null for that case (and for unrecognized formats) instead of crashing.
            byte[]? rgba = TextureUtils.DecodeToRgba8888(texture, out int width, out int height);
            levels = rgba == null ? null : TextureLevels.Prepare((uint)width, (uint)height, rgba, mipmap);
        }

        if (levels == null)
        {
            var fallback = GetDefaultAlbedoTexture();
            _textureCache[texture.Id] = fallback;
            return fallback;
        }

        // Allocated now (cheap: no queue submission, just image+memory) but not uploaded yet - the
        // pixel data is queued for UploadOnePendingTexture instead, so a caller loading a whole level
        // can spread potentially thousands of GraphicsDevice.UpdateTexture calls across many frames
        // instead of blocking through all of them in this one constructor call. Safe to hand out
        // immediately: nothing samples it until the scene is actually rendered, well after the queue
        // this feeds has had a chance to drain (see View3D's gate on AssetManager.HasPendingUploads).
        var tex = new GpuTexture(_gd, levels.Width, levels.Height, (uint)levels.Levels.Length);
        _pendingUploads.Enqueue((tex, levels));
        TotalQueuedUploads++;
        _textureCache[texture.Id] = tex;
        _builtTextureIds[tex] = texture.Id;
        return tex;
    }

    // Deferred texture uploads - see GetOrBuildTexture's remarks and UploadOnePendingTexture.
    private readonly Queue<(GpuTexture tex, TextureLevels levels)> _pendingUploads = new();

    /// <summary>Total textures ever queued for upload this AssetManager's lifetime, for a progress bar
    /// ("done" = this minus <see cref="PendingUploadCount"/>). Never decreases.</summary>
    public int TotalQueuedUploads { get; private set; }
    public int PendingUploadCount => _pendingUploads.Count;
    public bool HasPendingUploads => _pendingUploads.Count > 0;

    /// <summary>Uploads exactly one queued texture's full mip chain - the same
    /// GraphicsDevice.UpdateTexture calls GpuTexture always made, just moved out of the constructor so
    /// a caller can call this repeatedly across frames (time-boxed, not all at once) instead of eating
    /// the whole level's texture upload cost in a single blocking call. A no-op if nothing is queued.</summary>
    public void UploadOnePendingTexture()
    {
        if (!_pendingUploads.TryDequeue(out var item)) return;
        item.tex.UploadAll(_gd, item.levels);
    }

    // Normals and tangents are decoded straight from the source vertex data (VertexFormat0/1's
    // packed 11:11:10 words - see PackedNormal/GeometryMath) rather than derived here; GeometryData
    // only falls back to UV-gradient derivation for formats that don't carry real data at all.
    // useVertexAlpha: see Material.UsesVertexAlphaCandidate - when set, GetVertexAlphaCandidates()
    // is written into each vertex's color alpha instead of the default fully-opaque white, and
    // GetOrBuildMaterial picks a shader that actually reads it.
    private static Vertex3D[] ConvertGeometryToVertices(IGeometry geometry, bool useVertexAlpha)
    {
        var positions = geometry.GetVertexPositions();
        var uvs = geometry.GetTextureCoordinates();
        var normals = geometry.GetNormals();
        var tangents = geometry.GetTangents();
        var vertexAlpha = useVertexAlpha ? geometry.GetVertexAlphaCandidates() : null;
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
            // those draws - mirrors EntityUFrag.ConvertUFragToVertices.
            var lmUV = lightmapUVs != null && lightmapUVs.Length >= i * 2 + 2
                ? new Vector2(lightmapUVs[i * 2], lightmapUVs[i * 2 + 1])
                : uv;

            float alpha = vertexAlpha != null && i < vertexAlpha.Length ? vertexAlpha[i] : 1f;
            vertices[i] = new Vertex3D(pos, uv, lmUV, n, tan, new Vector4(1f, 1f, 1f, alpha));
        }

        return vertices;
    }

    // Reused per-frame scratch (moved from View3D's own instance-scoped field): the selected entity's
    // world matrices, pushed into the renderer's transform SSBO. Fine to share across whichever single
    // 3D view is driving the renderer, same as SceneRenderer itself.
    private readonly List<Matrix4x4> _vkTransformScratch = [];

    /// <summary>Pushes an edited entity's world matrices straight into the captured scene's transform
    /// SSBO, without rebuilding or re-recording anything - see VulkanRenderer.UpdateEntityTransforms.
    /// No-op if the scene has not been captured yet.</summary>
    public void UpdateEntityTransforms(Entity moved)
    {
        if (SceneRenderer == null) return;
        _vkTransformScratch.Clear();
        foreach (var (_, _, world, _) in moved.GetRenderablesForVk())
            _vkTransformScratch.Add(world);
        SceneRenderer.UpdateEntityTransforms(moved, _vkTransformScratch, moved.WorldBoundingSphere);
    }

    /// <summary>Assembles the whole level's scene from the geometry registry (VulkanSceneCapture) and
    /// EntityManager's live per-instance world transforms - moved here from View3D verbatim (see that
    /// file's history): every dependency below (VulkanSceneCapture, EntityManager.Singleton, this
    /// AssetManager itself) was already level-scoped, not panel-scoped, so there was nothing
    /// View3D-specific about it in the first place. Only geometries actually referenced by an instance
    /// are included, remapped to a compact index. Returns null until instances exist.</summary>
    private (List<float[]> verts, List<uint[]> idx, List<VkMaterialDesc> materials,
        List<(int geo, int mat, Matrix4x4 world, Vector4 sphere, object owner, float displayDistance, uint pickId)> instances,
        Texture? envCube)? BuildVkScene()
    {
        if (VulkanSceneCapture.VertexData.Count == 0)
            return null;

        var verts = new List<float[]>();
        var idx = new List<uint[]>();
        var materials = new List<VkMaterialDesc>();
        var instances = new List<(int, int, Matrix4x4, Vector4, object, float, uint)>();
        var geoRemap = new Dictionary<int, int>();
        var matRemap = new Dictionary<RenderMaterial, int>(ReferenceEqualityComparer.Instance);

        foreach (var entity in EntityManager.Singleton.AllEntities())
        {
            foreach (var (mesh, material, world, sphere) in entity.GetRenderablesForVk())
            {
                if (!VulkanSceneCapture.TryGet(mesh, out int gi))
                    continue;
                if (!geoRemap.TryGetValue(gi, out int geoSlot))
                {
                    geoSlot = verts.Count;
                    verts.Add(VulkanSceneCapture.VertexData[gi]);
                    idx.Add(VulkanSceneCapture.Indices[gi]);
                    geoRemap[gi] = geoSlot;
                }
                if (!matRemap.TryGetValue(material, out int matSlot))
                {
                    matSlot = materials.Count;
                    materials.Add(VkMaterialBuilder.Build(material, this));
                    matRemap[material] = matSlot;
                }
                float displayDistance = entity is EntityMoby moby ? moby.DisplayDistance : -1f;
                instances.Add((geoSlot, matSlot, world, sphere, entity, displayDistance, (uint)entity.ID));
            }
        }

        if (instances.Count == 0)
            return null;
        var envCube = EnvironmentCubemapView?.Target;
        return (verts, idx, materials, instances, envCube);
    }

    /// <summary>Builds <see cref="SceneRenderer"/> if it does not exist yet - a no-op once it does, which
    /// is the whole point: the caller (View3D) can call this every frame with no cost once the scene is
    /// captured, instead of needing to track "have I captured yet" itself. Also a no-op while textures
    /// are still uploading (<see cref="HasPendingUploads"/>), so the very first capture never samples a
    /// texture before its pixel data has actually reached the GPU. Returns true once SceneRenderer is
    /// ready to use (whether captured just now or already captured before).</summary>
    public bool TryCaptureScene(GraphicsDevice gd, uint viewWidth, uint viewHeight)
    {
        if (SceneRenderer != null) return true;
        if (HasPendingUploads) return false;

        try
        {
            var scene = BuildVkScene();
            if (scene is not { } s) return false;
            SceneRenderer = new VulkanRenderer(gd, s.verts, s.idx, s.materials, s.instances, s.envCube, viewWidth, viewHeight);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[VkRenderer] init failed: {e.Message}");
            SceneRenderer = null;
            return false;
        }
    }

    /// <summary>Disposes and drops <see cref="SceneRenderer"/> after it faults mid-frame (submit/resize
    /// failure) - the caller just tried to use it and hit an exception, so the safe recovery is to
    /// throw the whole thing away and let TryCaptureScene rebuild it next frame, same as before this
    /// was ever captured. Unlike the ad-hoc "set the field to null" this replaces, this actually
    /// disposes the GPU resources first instead of leaking them on the failure path.</summary>
    public void InvalidateSceneRenderer()
    {
        SceneRenderer?.Dispose();
        SceneRenderer = null;
    }

    /// <summary>Tears down the captured scene - called explicitly by LunaWindow.TryWipeLevel, BEFORE
    /// EntityManager.Singleton.Dispose(): the scene references live entity meshes/geometry and the
    /// VulkanSceneCapture registry, both of which the level's own disposal invalidates. This is
    /// deliberately not part of Dispose() itself, which callers only reach afterwards (Dispose() then
    /// frees the textures/materials SceneRenderer's descriptor sets point at, which is only safe once
    /// SceneRenderer itself is already gone).</summary>
    public void DisposeSceneRenderer()
    {
        SceneRenderer?.Dispose();
        SceneRenderer = null;
        VulkanSceneCapture.Clear();
    }

    public void Dispose()
    {
        // Safety net: the real teardown order is DisposeSceneRenderer() then this (see that method's
        // remarks) - SceneRenderer's descriptor sets reference the textures freed below, so it must
        // already be gone before they go. Idempotent (DisposeSceneRenderer already nulls it) - only
        // does anything if some future caller reaches Dispose() without calling that first.
        SceneRenderer?.Dispose();
        SceneRenderer = null;

        // Models and meshes hold no GPU resources any more, so only the textures need releasing.
        // The shared default stands in for every texture that failed to decode, so it is in the cache
        // many times over and must not be disposed through it.
        foreach (var texture in _textureCache.Values)
            if (texture != _defaultAlbedoTexture)
                texture.Dispose();

        _defaultAlbedoTexture?.Dispose();
        _defaultAlbedoTexture = null;
        _defaultNormalTexture?.Dispose();
        _defaultNormalTexture = null;
        _defaultPropertiesTexture?.Dispose();
        _defaultPropertiesTexture = null;
        _defaultLightColourTexture?.Dispose();
        _defaultLightColourTexture = null;
        _defaultLightDirTexture?.Dispose();
        _defaultLightDirTexture = null;

        _pointWrapSampler?.Dispose();
        _pointWrapSampler = null;
        _linearWrapSampler?.Dispose();
        _linearWrapSampler = null;

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
