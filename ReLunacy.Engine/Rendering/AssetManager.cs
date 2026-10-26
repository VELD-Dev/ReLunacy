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
// engine-owned asset model, caching by TUID so shared materials/textures aren't rebuilt per mesh.
public sealed class AssetManager : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ReLunacy.Engine.Assets.Lighting.LightingEnvironment? _oldLightingEnvironment;
    /// <summary>Whether the level's Mobys follow the old engine's shading contract (see VkInstanceLight).</summary>
    private readonly bool _oldEngineMobys;

    /// <summary>The whole level's geometry/materials/textures, captured once and replayed every frame.
    /// Lifetime matches the level, not any particular panel's open/closed state.</summary>
    public VulkanRenderer? SceneRenderer { get; private set; }

    private readonly Dictionary<ulong, GpuTexture> _textureCache = [];
    private readonly Dictionary<ulong, ITexture> _sourceTextures = [];
    // Keyed on (shader TUID, lightmap index): baked lighting is per-instance, so one shader can need
    // multiple built materials. Materials with no baked lighting collapse onto NoLightmap.
    private readonly Dictionary<(ulong ShaderId, ushort LightmapIndex), RenderMaterial> _materialCache = [];
    // Two-layer TIE primitives: (first, lightmap, second material, blend mask) -> their composite.
    private readonly Dictionary<(ulong, ushort, ulong, ulong), RenderMaterial> _twoLayerMaterialCache = [];
    // The second layer's fragment groups 15 / 17 (material +0x50, +0x70) per composite material.
    private readonly Dictionary<RenderMaterial, (Vector4 Tint, Vector4 Parallax)> _vkSecondLayer = new(ReferenceEqualityComparer.Instance);

    /// <summary>The second layer's group 15 / 17 constants of a two-layer composite, or null.</summary>
    public (Vector4 Tint, Vector4 Parallax)? GetVkSecondLayer(RenderMaterial bMat) =>
        _vkSecondLayer.TryGetValue(bMat, out var v) ? v : null;
    // Every built variant of a given shader TUID. The live-tuning API (SetParallax,
    // SetDetailTiling) is addressed by TUID because that is what the ShaderBrowser lists, so it
    // has to reach all of a shader's lightmap variants rather than just one.
    private readonly Dictionary<ulong, List<RenderMaterial>> _materialsByShader = [];
    // Source material metadata by shader TUID, for the live-tuning API to read back.
    private readonly Dictionary<ulong, IMaterial> _sourceMaterials = [];
    // Per-built-material data for the raw-Vulkan renderer: game rendering mode, whether vertex
    // alpha was decoded, whether the albedo's own alpha is real.
    private readonly Dictionary<RenderMaterial, (byte GameRenderMode, bool UsesVertexAlpha, bool AlbedoHasAlphaChannel)> _vkMaterialInfo =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RenderMaterial, Vector4> _vkReflectionTint = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RenderMaterial, Vector4> _vkReflectionParameters = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RenderMaterial, int> _vkEnvironmentCube = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RenderMaterial, Vector2> _vkMaterialStrengths = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RenderMaterial, Vector4> _vkMaterialPermutation = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RenderMaterial, Vector4> _vkMobyTint = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RenderMaterial, EmbeddedProgram> _vkEmbeddedProgram = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RenderMaterial, Loading.Shaders.NewEngineShaderProgramBundle> _vkProgramBundle = new(ReferenceEqualityComparer.Instance);
    private readonly bool _retailProgramsMeasured;
    // The EBOOT's standard (class 0x00) programs, when a decrypted EBOOT is found (QForce only, like
    // the embedded ones).
    private readonly Loading.Shaders.NewEngineStandardPrograms? _standardPrograms;
    private readonly Dictionary<RenderMaterial, (bool IsNewEngine, byte ShaderClass, byte ShaderFlags)> _vkNewEngineShaderInfo =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Native reflection tint/mix vector for a built material (old +0x40, new +0x50).</summary>
    public Vector4 GetVkReflectionTint(RenderMaterial bMat) =>
        _vkReflectionTint.TryGetValue(bMat, out var tint) ? tint : Vector4.Zero;

    /// <summary>New-engine 0x5000 +0x60. Zero for old-engine materials, which have no such vector.</summary>
    public Vector4 GetVkReflectionParameters(RenderMaterial bMat) =>
        _vkReflectionParameters.TryGetValue(bMat, out var p) ? p : Vector4.Zero;

    /// <summary>New-engine 0x5000 +0x40, the environment cube this material reflects (0..3).</summary>
    public int GetVkEnvironmentCube(RenderMaterial bMat) =>
        _vkEnvironmentCube.TryGetValue(bMat, out var cube) ? cube : 0;

    /// <summary>New-engine 0x5000 (+0x30 normal-map strength, +0x34 reflection strength) for the
    /// classes whose renderers upload them (0x00 standard, 0x13 embedded); (1, 1) otherwise.</summary>
    public Vector2 GetVkMaterialStrengths(RenderMaterial bMat) =>
        _vkMaterialStrengths.TryGetValue(bMat, out var s) ? s : Vector2.One;

    /// <summary>New-engine retail permutation state: (runtime detail tiling, detail gain +0x3C, detail
    /// present, normal-map permutation). Zero for old-engine materials. See NewEnginePermutation.</summary>
    public Vector4 GetVkMaterialPermutation(RenderMaterial bMat) =>
        _vkMaterialPermutation.TryGetValue(bMat, out var p) ? p : Vector4.Zero;

    /// <summary>
    /// New-engine Moby tint (rgb, 1 = applies). PROVEN in the QForce EBOOT: the Moby list builder
    /// (0x00794A00) sends render modes 0/4/5 to pass 0x2B, whose c[20] is (1, 1, 1, 1) (0x00790318),
    /// and mode 1 to pass 0x3D / modes 2, 6 to the sorted pass, where c[20].rgb = material +0x24 x the
    /// instance colour (0x00791004, 0x007921C4) and VPs 166-181 route c[20] to TEX1, the albedo factor.
    /// The instance colour is runtime state and taken as 1. Zero when the tint does not apply.
    /// </summary>
    public Vector4 GetVkMobyTint(RenderMaterial bMat) =>
        _vkMobyTint.TryGetValue(bMat, out var t) ? t : Vector4.Zero;

    /// <summary>Class 0x13: the embedded program family the lit shader emulates (NewEngineEmbeddedPrograms).</summary>
    public EmbeddedProgram GetVkEmbeddedProgram(RenderMaterial bMat) =>
        _vkEmbeddedProgram.TryGetValue(bMat, out var p) ? p : EmbeddedProgram.Generic;

    /// <summary>Class 0x13: the material's own program bundle, which the renderer translates (RsxFragmentTranslator).</summary>
    public Loading.Shaders.NewEngineShaderProgramBundle? GetVkProgramBundle(RenderMaterial bMat) =>
        _vkProgramBundle.TryGetValue(bMat, out var b) ? b : null;

    /// <summary>New-engine renderer routing state from ShaderMetadataNew +0x22/+0x23.</summary>
    public (bool IsNewEngine, byte ShaderClass, byte ShaderFlags) GetVkNewEngineShaderInfo(RenderMaterial bMat) =>
        _vkNewEngineShaderInfo.TryGetValue(bMat, out var info) ? info : (false, (byte)0, (byte)0);

    // Tools of Destruction materials whose draws cull back faces: the TIE shell (0x00594C34) sends
    // CULL_FACE_ENABLE = !(+0x13 & 0x02) at 0x0059561C / 0x005972AC, and the other shells read the same
    // byte the same way (0x00501E08...). Only materials read from the level: billboards, collision and
    // other editor materials stay double-sided.
    private readonly HashSet<RenderMaterial> _vkOldEngineCulled = new(ReferenceEqualityComparer.Instance);

    public bool IsVkOldEngineCulled(RenderMaterial bMat) => _vkOldEngineCulled.Contains(bMat);

    // Foliage billboard materials: the set is what IsBillboardMaterial answers from, the cache
    // keeps one Material per source shader instead of one per placement.
    private readonly HashSet<RenderMaterial> _billboardMaterials = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ulong, RenderMaterial> _billboardMaterialCache = new();

    /// <summary>The game's rendering mode (0 Opaque, 1 Overlay, 2 Additive, 3 Scunge, 4 Cutout,
    /// 5 Soft-Edge, 6 Blended) and vertex-alpha flag for a built material.</summary>
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
    // Scene-wide default filtering plus per-texture overrides, keyed by texture TUID. _builtTextureIds
    // is the GpuTexture -> TUID reverse of _textureCache, used to re-resolve an already-built
    // MaterialMap's sampler live.
    private TextureFiltering _defaultTextureFiltering = TextureFiltering.Point;
    private readonly Dictionary<ulong, TextureFiltering> _perTextureFiltering = [];
    private readonly Dictionary<GpuTexture, ulong> _builtTextureIds = [];
    // Flat "no perturbation" fallback for the normal map slot (every material gets one bound, even
    // with no NormalTexture). G=128, A=128 decodes to a flat tangent-space normal (0,0,1).
    private GpuTexture? _defaultAlbedoTexture;
    private GpuTexture? _defaultNormalTexture;
    // See GetDefaultPropertiesTexture.
    private GpuTexture? _defaultPropertiesTexture;
    // See GetDefaultLightColourTexture / GetDefaultLightDirTexture.
    private GpuTexture? _defaultLightColourTexture;
    private GpuTexture? _defaultLightDirTexture;

    public IReadOnlyDictionary<ulong, GpuTexture> BuiltTextures => _textureCache;
    public IReadOnlyDictionary<ulong, ITexture> SourceTextures => _sourceTextures;

    public Dictionary<ulong, RenderModel[]> Mobys { get; } = []; // one Model per bangle
    public Dictionary<ulong, RenderModel> Ties { get; } = [];
    private readonly IReadOnlyDictionary<ulong, Assets.Mobys.Moby> _sourceMobys;

    /// <summary>Resolves a loaded Moby asset by TUID without requiring it to have a placement.</summary>
    public bool TryGetMobyAsset(ulong id, out Assets.Mobys.Moby? moby) =>
        _sourceMobys.TryGetValue(id, out moby);

    // Decoded textures waiting to be uploaded, filled by PrepareTextures. Entries are removed as they
    // are consumed so decoded pixels are freed as the build walks past them. A present-but-null entry
    // means the decode was attempted and failed.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, TextureLevels?> _prepared = new();

    /// <param name="preparedTextures">Textures already decoded by <see cref="PrepareTextures"/>. Null
    /// decodes them here instead, which blocks whatever thread this runs on.</param>
    public AssetManager(LevelData level, GraphicsDevice gd, IDictionary<ulong, TextureLevels?>? preparedTextures = null)
    {
        _gd = gd;
        _sourceMobys = level.Mobys;
        _oldLightingEnvironment = level.LightingEnvironment;
        _oldEngineMobys = level.IsOldEngine;
        // The retail-program routes and patch groups were measured on QForce (RsxPrograms); the other
        // new-engine games keep the generic path until theirs are (seaside's water drew flat white).
        // Same engine on A Crack in Time, All 4 One, QForce and Into the Nexus: the game's programs are
        // drawn on all of them (the seaside water that drew flat white was the unit-10/11 stub, now the
        // scene copy).
        _retailProgramsMeasured = !level.IsOldEngine;
        // The EBOOT's standard programs: this level's own game's when a decrypted EBOOT lies in its install
        // (QForce, All 4 One, Into the Nexus here), else another new-engine game's - the same engine, the
        // same table layout (A Crack in Time has none decrypted).
        _standardPrograms = !level.IsOldEngine
            ? Loading.Shaders.NewEngineStandardPrograms.Find(level.GameDefinition?.Id)
            : null;
        // Tools of Destruction's TIE programs, from its decrypted EBOOT (TodTiePrograms).
        Vulkan.VulkanRenderer.TodPrograms = level.IsOldEngine && level.GameDefinition?.Id is null or Games.GameId.ToolsOfDestruction
            ? Loading.Shaders.TodTiePrograms.Find()
            : null;
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

        // Keep every source texture discoverable by the editor, including cut/unused content,
        // without allocating a Vulkan image for all of it up front. On large levels the old loop
        // below eagerly created every GPU texture before the deferred upload queue started draining;
        // that could consume most of the device-local heap and leave NeoVeldrid unable to allocate
        // its staging memory in VkDeviceMemoryManager. Textures referenced by real materials were
        // already built above through GetOrBuildMaterial; unused textures stay source-only until an
        // editor path explicitly asks GetOrBuildTexture for one.
        foreach (var texture in level.AllTextures.Values)
            _sourceTextures.TryAdd(texture.Id, texture);

        long tTextures = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;
        ZoneLightmaps = BuildZoneLighting(level.ZoneLightmaps, "lightmap");
        ZoneDirectionals = BuildZoneLighting(level.ZoneDirectionals, "directional");
        if (ZoneLightmaps.Count != 0)
            Console.WriteLine($"Zone lighting: {ZoneLightmaps.Count} light-colour and {ZoneDirectionals.Count} light-direction textures built.");

        long tZone = sw.ElapsedMilliseconds - t0;
        BuildEnvironmentCubemap(level);
        if (!_prepared.IsEmpty)
            Console.WriteLine($"Warning: {_prepared.Count} decoded textures were prepared but never built - they are holding memory for nothing.");
        // GPU upload happens separately (see GetOrBuildTexture/UploadOnePendingTexture); these timings
        // are CPU-only mesh/material building. TotalQueuedUploads is drained by the caller afterwards.
        Console.WriteLine($"Assets built in {sw.ElapsedMilliseconds}ms (mobys {tMobys}, ties {tTies}, textures {tTextures}, zone lighting {tZone}). {TotalQueuedUploads} textures queued for GPU upload.");
    }

    /// <summary>Decodes every texture the level needs, in parallel, before any are uploaded. Safe to run
    /// concurrently: pixel data is already in memory, and the block decoders hold only readonly state.</summary>
    public static Dictionary<ulong, TextureLevels?> PrepareTextures(LevelData level)
    {
        // Generated mips remain disabled for atlases because a generic box filter bleeds their black
        // gutters into neighbouring islands. TextureUploadPreparer overrides this only when the source
        // physically carries the game's own authored mip chain, which is safe to preserve verbatim.
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
            Need(material.AuxiliaryTextureA);
            Need(material.AuxiliaryTextureB);
        }

        // Do not predecode every entry in AllTextures. Material/lightmap references below are the
        // actual render working set; decoding unused entries here retained their full RGBA mip chains
        // in RAM until AssetManager construction and previously led directly to eager GPU allocation.
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
                prepared[texture.Id] = TextureUploadPreparer.Prepare(
                    texture,
                    generateMipsWhenUnauthored: !withoutMips.Contains(texture.Id));
            }
            catch (Exception ex)
            {
                // Recorded as a failure rather than rethrown; GetOrBuildTexture substitutes the default.
                prepared[texture.Id] = null;
                Console.WriteLine($"Warning: Failed to decode texture {texture.Id:X}: {ex.Message}");
            }
        });
        return new Dictionary<ulong, TextureLevels?>(prepared);
    }

    // The level's environment cubemap as a GPU samplerCube, for the lit shader's reflection term.
    // Always non-null once constructed: a level with no cubemap gets a 1x1 grey fallback so the
    // descriptor set is never left unbound.
    private NeoVeldrid.Texture? _environmentCubemap;
    public NeoVeldrid.TextureView? EnvironmentCubemapView { get; private set; }

    /// <summary>
    /// New engine: which resource of <see cref="EnvironmentCubemapView"/> a cubemaps.dat TUID is - resource r
    /// holds cubes 4r..4r+3. Resource 0 is the region's default; zones pick the others per placement
    /// (EntityTie / NewUFrag .NewEngineCubeTuid). Empty on the old engine.
    /// </summary>
    public IReadOnlyDictionary<ulong, int> EnvironmentCubeResources { get; private set; } = new Dictionary<ulong, int>();

    /// <summary>
    /// New engine: every cube resource the region declares, four cubes each, uploaded as
    /// R16G16B16A16_Float with the retail channels intact (rgb tint, a intensity); resource r cube i is
    /// cube 4r+i of the texture (Vulkan layers 6(4r+i)..+5).
    ///
    /// Why four and why float. The retail programs gate every reflection by the cube's OWN fourth
    /// channel - `cube.rgb * max(cube.a + (+0x68), 0)` - so that channel has to reach the shader as
    /// itself, not folded into rgb behind an exponent. And each material's 0x5000 +0x40 is 0..3 on
    /// 24,126 of 24,179 new-engine materials while every cube resource holds exactly four cubes
    /// (1,024 cubes, 256 resources, 4 games): the material picks its cube. PROVEN (QForce EBOOT): the
    /// UFrag renderer binds unit 7 to resource[record +0x59] + material +0x40 * 0x20 (0x0074F980), the
    /// TIE sort does the same with +0x5D (0x006DE364), the Moby pass passes material +0x40 to the bind
    /// helper 0x00B4C80C (0x0078FC68) - Docs/reverse/NEW_ENGINE_REFLECTION_CUBES_2026-10-07.md.
    ///
    /// Why every resource. A level declares up to 21 of them (galacton_ship) and each zone names, per
    /// TIE placement and per UFrag, the one it reflects - PROVEN, see CubemapReader. Only the default
    /// used to be uploaded, so every placement that asked for another one reflected the wrong sky.
    ///
    /// Sizes. One image needs one face size, and retail mixes them in a level (phoenix_ph2: a 128 cube
    /// and a 16 one; meridian_city: 64 and 16), so smaller cubes are bilinearly upscaled to the largest -
    /// the shader samples them linearly anyway, so this only moves the face-edge filtering.
    ///
    /// Layer bookkeeping, checked against NeoVeldrid's IL: for a Cubemap texture NeoVeldrid counts
    /// CUBES in ArrayLayers and makes 6 * ArrayLayers Vulkan layers, while UpdateTexture's arrayLayer
    /// is the raw Vulkan layer - so ArrayLayers is the cube count and cube c, face f is layer 6c + f.
    /// (Both builders used to pass 6 per cube, which allocated six times the layers they wrote.)
    /// </summary>
    private bool TryBuildNewEngineHdrCubes(LevelData level, NeoVeldrid.ResourceFactory factory)
    {
        var resources = new List<(ulong Tuid, List<Assets.Cubemaps.Cubemap> Cubes)>();
        foreach (var cube in level.Cubemaps)
        {
            if (cube.HalfFloatFaces is not { Count: 6 })
                continue;
            int at = resources.FindIndex(r => r.Tuid == cube.ResourceTuid);
            if (at < 0)
                resources.Add((cube.ResourceTuid, [cube]));
            else
                resources[at].Cubes.Add(cube);
        }
        if (resources.Count == 0)
            return false;

        int size = resources.SelectMany(r => r.Cubes).Max(c => c.FaceSize);
        var cubes = new List<Assets.Cubemaps.Cubemap>(4 * resources.Count);
        var map = new Dictionary<ulong, int>();
        foreach (var (tuid, list) in resources)
        {
            list.Sort((a, b) => a.IndexInResource.CompareTo(b.IndexInResource));
            map.TryAdd(tuid, cubes.Count / 4);
            for (int i = 0; i < 4; i++)
                cubes.Add(list[Math.Min(i, list.Count - 1)]);
        }

        var tex = factory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(
            (uint)size, (uint)size, 1, (uint)cubes.Count, NeoVeldrid.PixelFormat.R16_G16_B16_A16_Float,
            NeoVeldrid.TextureUsage.Sampled | NeoVeldrid.TextureUsage.Cubemap));
        for (int c = 0; c < cubes.Count; c++)
            for (int f = 0; f < 6; f++)
                _gd.UpdateTexture(tex, HalfFace(cubes[c].HalfFloatFaces![f], cubes[c].FaceSize, size),
                    0, 0, 0, (uint)size, (uint)size, 1, 0, (uint)(6 * c + f));

        Console.WriteLine(
            $"[Environment] new-engine HDR cubes: {resources.Count} resource(s), {cubes.Count} x {size}x{size} " +
            "R16G16B16A16_Float, retail channels (rgb tint, a intensity), Morton-deswizzled.");
        _environmentCubemap = tex;
        EnvironmentCubemapView = factory.CreateTextureView(tex);
        EnvironmentCubeResources = map;
        return true;
    }

    /// <summary>Mip 0 of one half-float face at <paramref name="size"/>: as is, or bilinearly upscaled.</summary>
    private static byte[] HalfFace(byte[] face, int from, int size)
    {
        if (from == size)
            return face.AsSpan(0, size * size * 8).ToArray();
        var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(face.AsSpan(0, from * from * 8));
        var bytes = new byte[size * size * 8];
        var dst = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(bytes.AsSpan());
        float scale = from / (float)size;
        for (int y = 0; y < size; y++)
        {
            float sy = Math.Clamp((y + 0.5f) * scale - 0.5f, 0f, from - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, from - 1);
            float ty = sy - y0;
            for (int x = 0; x < size; x++)
            {
                float sx = Math.Clamp((x + 0.5f) * scale - 0.5f, 0f, from - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, from - 1);
                float tx = sx - x0;
                for (int ch = 0; ch < 4; ch++)
                {
                    float a = (float)src[(y0 * from + x0) * 4 + ch], b = (float)src[(y0 * from + x1) * 4 + ch];
                    float c = (float)src[(y1 * from + x0) * 4 + ch], d = (float)src[(y1 * from + x1) * 4 + ch];
                    dst[(y * size + x) * 4 + ch] = (Half)((a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty);
                }
            }
        }
        return bytes;
    }

    private void BuildEnvironmentCubemap(LevelData level)
    {
        if (TryBuildNewEngineHdrCubes(level, _gd.ResourceFactory))
            return;
        var cubemap = level.Cubemaps.Count > 0 ? level.Cubemaps[0] : null;
        int size = cubemap?.FaceSize ?? 1;
        int mipCount = cubemap is { Faces.Count: > 0 }
            ? cubemap.Faces.Min(face => Math.Max(1, face.MipmapCount))
            : 1;
        var factory = _gd.ResourceFactory;

        // The RSX samples the cube through its descriptor's ADDRESS gamma bits, and every retail ToD cube
        // (all 24 levels' 0x5920 records) sets all four - alpha included, so the exponent the TIE programs
        // read as EX2((a x 255 - 80) / 35) is the sRGB-decoded alpha. Uploading the raw bytes made the
        // reflections 4x too bright on average (Metropolis: mean 2.4 instead of 0.6). The decode happens
        // before filtering, as on the hardware, into 16-bit channels so the small exponents keep their steps.
        byte gammaArgb = cubemap is { Faces.Count: > 0 } && cubemap.Faces[0] is Assets.Textures.Texture { OldDescriptor: { } cubeDescriptor }
            ? Loading.Textures.RsxTextureFormatConversion.Decode(cubeDescriptor).GammaMaskArgb
            : (byte)0;
        bool decoded = gammaArgb != 0;

        var tex = factory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(
            (uint)size, (uint)size, (uint)mipCount, 1,
            decoded ? NeoVeldrid.PixelFormat.R16_G16_B16_A16_UNorm : NeoVeldrid.PixelFormat.R8_G8_B8_A8_UNorm,
            NeoVeldrid.TextureUsage.Sampled | NeoVeldrid.TextureUsage.Cubemap));

        for (uint f = 0; f < 6; f++)
        {
            uint w = (uint)size;
            uint h = (uint)size;
            for (uint mip = 0; mip < (uint)mipCount; mip++)
            {
                byte[] rgba;
                if (cubemap != null && f < cubemap.Faces.Count)
                {
                    var face = cubemap.Faces[(int)f];
                    if (face.Format != ReLunacy.Engine.Assets.Interfaces.TextureFormat.A8R8G8B8)
                        throw new InvalidDataException($"0x5920 cubemap face {f} has unsupported format {face.Format}.");
                    byte[] argb = face.GetMipmapData((int)mip);
                    rgba = TextureUtils.ARGB8888ToRGBA8888(argb, (int)w, (int)h);
                }
                else
                {
                    rgba = FallbackCubeFace((int)w);
                }

                _gd.UpdateTexture(tex, decoded ? GammaDecodedFace(rgba, gammaArgb) : rgba, 0, 0, 0, w, h, 1, mip, f);
                w = Math.Max(1u, w / 2);
                h = Math.Max(1u, h / 2);
            }
        }

        Console.WriteLine($"[Texture Update 1.2] Environment cubemap GPU upload: {size}x{size}, {mipCount} authored mip(s), 6 faces{(decoded ? $", gamma 0x{gammaArgb:X} (ARGB) decoded" : "")}.");
        _environmentCubemap = tex;
        EnvironmentCubemapView = factory.CreateTextureView(tex);
    }

    /// <summary>RGBA8 -> little-endian RGBA16 UNORM, the channels of the ARGB gamma mask (bit 0 A, 1 R,
    /// 2 G, 3 B) taken through the sRGB curve RPCS3 applies to them.</summary>
    private static byte[] GammaDecodedFace(byte[] rgba, byte gammaArgb)
    {
        Span<ushort> raw = stackalloc ushort[256];
        Span<ushort> linear = stackalloc ushort[256];
        for (int v = 0; v < 256; v++)
        {
            float c = v / 255f;
            float l = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
            raw[v] = (ushort)(v * 257);
            linear[v] = (ushort)MathF.Round(l * 65535f);
        }
        // RGBA order of the upload against the mask's A, R, G, B bits.
        ReadOnlySpan<int> maskBit = [1, 2, 3, 0];
        var result = new byte[rgba.Length * 2];
        for (int i = 0; i < rgba.Length; i++)
        {
            ushort value = ((gammaArgb >> maskBit[i & 3]) & 1) != 0 ? linear[rgba[i]] : raw[rgba[i]];
            result[2 * i] = (byte)value;
            result[2 * i + 1] = (byte)(value >> 8);
        }
        return result;
    }

    private static byte[] FallbackCubeFace(int size)
    {
        // Mid-grey, mid-alpha. Only sampled when a real cubemap is absent, in which case
        // EnvironmentIntensity is 0 and this contributes nothing regardless.
        var data = new byte[size * size * 4];
        Array.Fill(data, (byte)128);
        return data;
    }

    /// <summary>Baked lighting is enabled for terrain (UFrags), which index a shared atlas via
    /// UFragMetadata.lightmapIndex and sample it through UFragVertex.UVs2. Ties are excluded: they
    /// carry a per-instance bake index, but VertexFormat0's single UV pair tiles, so there is no
    /// correct way to sample an atlas with it.</summary>
    public const bool EnableBakedLighting = true;

    /// <summary>Positionally indexed by an instance's lightmap index. A failed entry becomes null
    /// rather than being dropped, so later indices don't shift onto the wrong texture.</summary>
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

    /// <param name="variant">Shader/material variant set to bind. 0 is the asset's own material; a
    /// higher index takes <see cref="IMesh.MaterialVariants"/>[variant] where the mesh has one, and
    /// falls back to the default material where it does not. Geometry is identical across variants,
    /// so only the material lookup changes.</param>
    private RenderModel BuildModel(IReadOnlyList<IMesh> meshes, uint variant = 0) =>
        BuildModel(meshes, i => variant > 0 && variant < (uint)meshes[i].MaterialVariants.Count
            ? meshes[i].MaterialVariants[(int)variant]
            : meshes[i].Material);

    /// <summary>
    /// Render models for a Moby drawn with materials chosen per mesh - a variant that exists only as a
    /// pending edit. Not cached: the choice changes with every edit, and geometry is rebuilt from the
    /// asset exactly as <see cref="GetMobyVariantModels"/> does.
    /// </summary>
    public RenderModel[] BuildMobyModels(Assets.Interfaces.IMoby moby, Func<int, int, IMaterial> materialFor)
    {
        ArgumentNullException.ThrowIfNull(moby);
        ArgumentNullException.ThrowIfNull(materialFor);
        var models = new RenderModel[moby.Bangles.Count];
        for (int b = 0; b < moby.Bangles.Count; b++)
        {
            int bangle = b;
            models[b] = BuildModel(moby.Bangles[b].Meshes, mesh => materialFor(bangle, mesh));
        }
        return models;
    }

    private RenderModel BuildModel(IReadOnlyList<IMesh> meshes, Func<int, IMaterial> materialAt)
    {
        var bMeshes = new RenderMesh[meshes.Count];
        for (int i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            IMaterial source = materialAt(i);
            // A two-layer primitive draws a composite of both materials, with the blend weight in alpha.
            var twoLayer = mesh as Assets.Geometry.Mesh is { SecondMaterial: not null, BlendMask: not null } layered ? layered : null;
            var material = twoLayer != null
                ? GetOrBuildMaterial(source, Loading.Objects.UFragMetadata.NoLightmap, twoLayer.SecondMaterial, twoLayer.BlendMask)
                : GetOrBuildMaterial(source);
            var vertices = ConvertGeometryToVertices(mesh.Geometry, source.UsesVertexAlpha || twoLayer != null);
            var indices = mesh.Geometry.GetIndices();

            var oldTieShaderUse = mesh is Assets.Geometry.Mesh sourceMesh ? sourceMesh.OldTieShaderUse : null;
            var bMesh = new RenderMesh(vertices, indices, material, oldTieShaderUse)
            {
                NewEngineTiePrimitive = mesh is Assets.Geometry.Mesh sourcePrimitive ? sourcePrimitive.NewEngineTiePrimitive : null,
                NewEngineFadeDistances = mesh is Assets.Geometry.Mesh fadeSource
                    ? fadeSource.NewEngineFadeDistances ?? fadeSource.NewEngineTiePrimitive?.FadeDistances
                    : null,
                UvAnimation = mesh is Assets.Geometry.Mesh uvSource ? uvSource.UvAnimation : null,
            };

            // Register this mesh's raw geometry keyed by the mesh instance, so the renderer can
            // upload it once and resolve every scene instance back to it.
            if (vertices.Length > 0 && indices.Length >= 3)
                Vulkan.VulkanSceneCapture.Register(bMesh, Vulkan.VulkanSceneCapture.Interleave(vertices), indices);

            bMeshes[i] = bMesh;
        }
        return new RenderModel(bMeshes);
    }

    /// <summary>lightmapIndex: this instance's entry in the zone's baked-lighting lists (see
    /// IUFrag.LightmapIndex). Part of the cache key because baked lighting is per-instance while
    /// Bliss binds textures per-Material. Callers with no baked lighting omit it.</summary>
    public RenderMaterial GetOrBuildMaterial(IMaterial material, ushort lightmapIndex = Loading.Objects.UFragMetadata.NoLightmap) =>
        GetOrBuildMaterial(material, lightmapIndex, null, null);

    /// <summary>With <paramref name="secondLayer"/> and <paramref name="blendMask"/>: the composite a
    /// two-layer TIE primitive draws with - the first material as usual, plus the second one's albedo /
    /// normal / properties / detail and the mask (fAlbedo2, fNormal2, fProperties2, fDetail2, fBlendMask:
    /// RSX units 8..12) and its +0x50 / +0x70 (<see cref="GetVkSecondLayer"/>).</summary>
    public RenderMaterial GetOrBuildMaterial(IMaterial material, ushort lightmapIndex, IMaterial? secondLayer, ITexture? blendMask)
    {
        bool twoLayer = secondLayer != null && blendMask != null;
        var cacheKey = (material.Id, lightmapIndex);
        var twoLayerKey = (material.Id, lightmapIndex, secondLayer?.Id ?? 0UL, blendMask?.Id ?? 0UL);
        if (!twoLayer && _materialCache.TryGetValue(cacheKey, out var cached))
            return cached;
        if (twoLayer && _twoLayerMaterialCache.TryGetValue(twoLayerKey, out var cachedTwoLayer))
            return cachedTwoLayer;

        var bMat = new RenderMaterial();

        // value = the game's own per-material alphaClip threshold. Only meaningful for Cutout
        // materials; harmless elsewhere.
        var albedo = material.AlbedoTexture != null ? GetOrBuildTexture(material.AlbedoTexture) : GetDefaultAlbedoTexture();
        bMat.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap(albedo, ResolveSampler(albedo), material.AlphaClipThreshold));

        // Always added, even with no NormalTexture, so the lit shader's texture layout is always
        // bound. This map's value slot flags whether the properties map has a real alpha channel
        // to read the detail mask from.
        var normal = material.NormalTexture != null ? GetOrBuildTexture(material.NormalTexture) : GetDefaultNormalTexture();
        bool detailMaskFromTexture = material.PropertiesTexture != null && HasAlphaChannel(material.PropertiesTexture.Format);
        bMat.AddMaterialMap(MaterialMapType.Normal, new MaterialMap(normal, ResolveSampler(normal), detailMaskFromTexture ? 1f : 0f));

        // "fProperties" is the game's packed "expensive" intensity texture: R=specular intensity,
        // G=parallax height, B=emissive intensity, A=detail-map mask. Always added so materials with
        // no PropertiesTexture still get the inert default rather than an unbound texture layout.
        // This map's value slot carries the detail UV tiling instead (unrelated to the texture
        // itself). A tiling of 0 is treated as "not present" and falls back to 1.
        var properties = material.PropertiesTexture != null ? GetOrBuildTexture(material.PropertiesTexture) : GetDefaultPropertiesTexture();
        float detailTiling = material.DetailTiling != 0f ? material.DetailTiling : DefaultDetailTiling;
        bMat.AddMaterialMap("fProperties", new MaterialMap(properties, ResolveSampler(properties), detailTiling));

        // Two textureless MaterialMaps used purely as transport for a per-material float each -
        // the cheapest way to get a tunable scalar to the shader. Reproduce the game's parallax
        // form: height * scale + bias. Live-tunable per shader from the ShaderBrowser.
        bMat.AddMaterialMap("fParallaxScale", new MaterialMap(value: material.ParallaxScale));
        bMat.AddMaterialMap("fParallaxBias", new MaterialMap(value: material.ParallaxBias));

        // Detail map: R,G = derivative perturbation added to the normal map's own derivatives,
        // B = additive albedo brightness, A = additive specular intensity, gated by the properties
        // map's alpha. See IMaterial.DetailTexture. No per-channel detail strengths; the map's value
        // slot just flags whether the material uses its detail map at all.
        //
        // Requires both a detail texture and the material's useDetailMap flag - a texture reference
        // left in the slot by an unused authoring path shouldn't switch the detail path on.
        bool hasDetail = material.DetailTexture != null && material.UsesDetailMap;
        var detail = hasDetail ? GetOrBuildTexture(material.DetailTexture!) : GetDefaultAlbedoTexture();
        bMat.AddMaterialMap("fDetail", new MaterialMap(detail, ResolveSampler(detail), hasDetail ? 1f : 0f));

        // New-engine slots 4/5 are native authored bindings, not metadata noise. Retail embedded
        // programs that use them sample RSX units 8/9. Keep them resident and attached to the
        // material even though their combine equations are program-family specific and therefore
        // are not injected into the generic Vulkan shader yet.
        if (material.AuxiliaryTextureA != null)
        {
            var auxA = GetOrBuildTexture(material.AuxiliaryTextureA);
            bMat.AddMaterialMap("fAuxiliaryA", new MaterialMap(auxA, ResolveSampler(auxA)));
        }
        if (material.AuxiliaryTextureB != null)
        {
            var auxB = GetOrBuildTexture(material.AuxiliaryTextureB);
            bMat.AddMaterialMap("fAuxiliaryB", new MaterialMap(auxB, ResolveSampler(auxB)));
        }

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

        _vkMaterialInfo[bMat] = (material.GameRenderMode, material.UsesVertexAlpha, material.AlbedoHasAlphaChannel);
        if (material is Assets.Materials.Material nativeMaterial)
        {
            _vkReflectionTint[bMat] = nativeMaterial.ReflectionTintSource;
            // Old engine: uMat4 carries +0x30 instead, the ToD TIE programs' group 1 (G6).
            _vkReflectionParameters[bMat] = nativeMaterial.IsNewEngineMaterial
                ? nativeMaterial.ReflectionParametersSource
                : nativeMaterial.OldShaderVector30;
            _vkEnvironmentCube[bMat] = nativeMaterial.EnvironmentCubeIndex;
            // PROVEN (QForce EBOOT): the TIE, UFrag and Moby renderers upload +0x30/+0x34 as vertex
            // constant c[21].xy (0x007444BC, 0x00745E50, 0x00751390, 0x0078EF94); the vertex programs
            // fade them with distance into TEX3.w / TEX2.w, and the fragment programs multiply the
            // normal map's xy by TEX3.w and the reflection mask by TEX2.w (949d655b3022, f6cdb4ccaa84,
            // 0d299ef3cf54). The fur class (0x0C) stores other values there.
            byte shaderClass = nativeMaterial.NewEngineShaderClass;
            _vkMaterialStrengths[bMat] = nativeMaterial.IsNewEngineMaterial && (shaderClass == 0x00 || shaderClass == 0x13)
                ? new Vector2(nativeMaterial.NewEngineParameter30, nativeMaterial.NewEngineParameter34)
                : Vector2.One;
            _vkNewEngineShaderInfo[bMat] = (
                nativeMaterial.IsNewEngineMaterial,
                nativeMaterial.NewEngineShaderClass,
                nativeMaterial.NewEngineShaderFlags);
            if (!nativeMaterial.IsNewEngineMaterial && (nativeMaterial.OldRenderFlags & 0x02) == 0)
                _vkOldEngineCulled.Add(bMat);
            // Old engine: .x = +0x58, the detail tiling the ToD TIE vertex programs apply (c[21].x); the
            // generic shader reads uMat6 on the new engine only.
            _vkMaterialPermutation[bMat] = nativeMaterial.IsNewEngineMaterial
                ? NewEnginePermutation(nativeMaterial, hasDetail)
                : new Vector4(nativeMaterial.DetailTiling, 0f, 0f, 0f);
            _vkMobyTint[bMat] = nativeMaterial.IsNewEngineMaterial && nativeMaterial.GameRenderMode is 1 or 2 or 6
                ? new Vector4(nativeMaterial.MaterialTintSource, 1f)
                : Vector4.Zero;
            if (nativeMaterial.IsNewEngineMaterial && shaderClass == 0x13)
            {
                _vkEmbeddedProgram[bMat] = NewEngineEmbeddedPrograms.Classify(nativeMaterial.NewEngineProgramKeys);
                if (_retailProgramsMeasured && nativeMaterial.NewEngineProgramBundle is { Variants.Count: > 0 } bundle)
                    _vkProgramBundle[bMat] = bundle;
            }
        }

        if (twoLayer)
        {
            AddSecondLayer(bMat, secondLayer!, blendMask!);
            _twoLayerMaterialCache[twoLayerKey] = bMat;
        }
        else
        {
            _materialCache[cacheKey] = bMat;
        }
        _sourceMaterials[material.Id] = material;
        if (!_materialsByShader.TryGetValue(material.Id, out var variants))
            _materialsByShader[material.Id] = variants = [];
        variants.Add(bMat);
        return bMat;
    }

    private void AddSecondLayer(RenderMaterial bMat, IMaterial second, ITexture blendMask)
    {
        Resources.GpuTexture Map(ITexture? texture, Resources.GpuTexture fallback) => texture != null ? GetOrBuildTexture(texture) : fallback;
        var albedo2 = Map(second.AlbedoTexture, GetDefaultAlbedoTexture());
        var normal2 = Map(second.NormalTexture, GetDefaultNormalTexture());
        // The first layer's inert default (0, 0, 0, 0): white would light the second layer up (b = emissive).
        var props2 = Map(second.PropertiesTexture, GetDefaultPropertiesTexture());
        var detail2 = Map(second.DetailTexture, GetDefaultAlbedoTexture());
        var mask = GetOrBuildTexture(blendMask);
        bMat.AddMaterialMap("fAlbedo2", new MaterialMap(albedo2, ResolveSampler(albedo2)));
        bMat.AddMaterialMap("fNormal2", new MaterialMap(normal2, ResolveSampler(normal2)));
        bMat.AddMaterialMap("fProperties2", new MaterialMap(props2, ResolveSampler(props2)));
        bMat.AddMaterialMap("fDetail2", new MaterialMap(detail2, ResolveSampler(detail2)));
        bMat.AddMaterialMap("fBlendMask", new MaterialMap(mask, ResolveSampler(mask)));
        _vkSecondLayer[bMat] = second is Assets.Materials.Material native
            ? (native.ReflectionTintSource, new Vector4(native.ParallaxScale, native.ParallaxBias, native.DetailTiling, 0f))
            : (Vector4.Zero, Vector4.Zero);
    }

    public void UpdateRegisteredMesh(RenderMesh mesh)
    {
        VulkanSceneCapture.Update(mesh, VulkanSceneCapture.Interleave(mesh.Vertices));
        InvalidateSceneRenderer();
    }

    /// <summary>The material for a foliage sprite card. Deliberately NOT reachable from
    /// GetOrBuildMaterial: nothing about a material says "this is a billboard", it is a property of the
    /// GEOMETRY (foliage packs a shared anchor into the position and the corner offset into
    /// TexCoords2), so routing by shader would silently billboard any mesh that happened to use a
    /// foliage shader. EntityFoliage asks for it explicitly.</summary>
    public RenderMaterial GetOrBuildBillboardMaterial(IMaterial? material)
    {
        // Cached per source shader, since many placements share a handful of shaders.
        if (_billboardMaterialCache.TryGetValue(material?.Id ?? ulong.MaxValue, out var cached)) return cached;

        // Foliage is always double-sided and blended; the renderer reads both facts from the source
        // material's own GameRenderMode below.
        var billboard = new RenderMaterial();

        var albedo = material?.AlbedoTexture != null ? GetOrBuildTexture(material.AlbedoTexture) : GetDefaultAlbedoTexture();
        billboard.AddMaterialMap(
            MaterialMapType.Albedo,
            new MaterialMap(albedo, ResolveSampler(albedo), material?.AlphaClipThreshold ?? 0f));

        _vkMaterialInfo[billboard] = (material?.GameRenderMode ?? 0, material?.UsesVertexAlpha ?? false, material?.AlbedoHasAlphaChannel ?? false);
        _billboardMaterials.Add(billboard);
        _billboardMaterialCache[material?.Id ?? ulong.MaxValue] = billboard;
        return billboard;
    }

    /// <summary>True if this material was built for foliage sprite cards, which are billboarded in
    /// the vertex shader and need the billboard vertex shader rather than the lit one.</summary>
    public bool IsBillboardMaterial(RenderMaterial bMat) => _billboardMaterials.Contains(bMat);

    /// <summary>Plain white, the stand-in for any albedo-like slot with no texture of its own.</summary>
    private GpuTexture GetDefaultAlbedoTexture() => _defaultAlbedoTexture ??= GpuTexture.Solid(_gd, 255, 255, 255, 255);

    private GpuTexture GetDefaultNormalTexture() => _defaultNormalTexture ??= GpuTexture.Solid(_gd, 128, 128, 128, 128);

    // Inert per-channel defaults for the properties map layout: R=0 no specular, G=0 flat parallax
    // height, B=0 no emissive, A=0 no detail mask.
    private GpuTexture GetDefaultPropertiesTexture() => _defaultPropertiesTexture ??= GpuTexture.Solid(_gd, 0, 0, 0, 0);

    // Bound for materials with no baked lighting so the descriptor set is never left unbound; the
    // shader gates the whole baked path on the value flag, so contents are otherwise irrelevant.
    private GpuTexture GetDefaultLightColourTexture() => _defaultLightColourTexture ??= GpuTexture.Solid(_gd, 255, 255, 255, 255);

    // CONTROL1 remaps the sampled tuple to (R,B,G), and the RSX then expands each component with
    // UNSIGNED_REMAP_BIASED (BX2). Therefore straight-up (0,0,1) must be stored as R=128, B=128,
    // G=255 in the underlying RGBA texture.
    private GpuTexture GetDefaultLightDirTexture() => _defaultLightDirTexture ??= GpuTexture.Solid(_gd, 128, 255, 128, 255);

    // Live scene-wide filtering toggle: MaterialMap.Sampler is a plain public field, so mutating the
    // cached maps needs no texture or material rebuild.
    public void SetTextureFiltering(TextureFiltering filtering)
    {
        if (_defaultTextureFiltering == filtering) return;
        _defaultTextureFiltering = filtering;
        RefreshMaterialSamplers();
    }

    /// <summary>Per-texture override, by texture TUID. Takes effect immediately, wins over the
    /// scene-wide default.</summary>
    public void SetTextureFiltering(ulong textureId, TextureFiltering filtering)
    {
        _perTextureFiltering[textureId] = filtering;
        RefreshMaterialSamplers();
    }

    // Fallback for materials with no identified detail tiling. The real value comes from
    // IMaterial.DetailTiling. 1 = detail sampled at the same frequency as the base map.
    public const float DefaultDetailTiling = 1f;

    /// <summary>
    /// What the retail new-engine programs need beyond the texture set, read in the QForce EBOOT
    /// (Docs/reverse/NEW_ENGINE_SHADER_SECTIONS_EBOOT_2026-09-27.md):
    ///   x  detail UV tiling. Every renderer uploads VP c[22] = (+0x78, +0x3C, 0, 0) (TIE 0x00743BA4,
    ///      UFrag 0x00751484, Moby 0x0078F058) and the programs scale the UV by c[22].x; the loader
    ///      rescales +0x78 by 512 / (detail width) for standard materials (0x00726E50).
    ///   y  detail gain +0x3C: weight = saturate(+0x3C * (1 - distance fade)) * props.a.
    ///   z  1 when the material draws its detail map.
    ///   w  the material's +0x20 feature bits (as the loader keeps them). Bit 0x08 picks the RNM baked
    ///      programs (lightmap scale c[4].x) over the flat ones (c[4].y, no RNM weights); 0x04 is the only
    ///      permutation family whose programs sample the reflection cube, 0x10 the detail map.
    /// </summary>
    private static Vector4 NewEnginePermutation(Assets.Materials.Material material, bool hasDetail)
    {
        float tiling = 0f;
        if (hasDetail)
        {
            tiling = material.DetailTiling != 0f ? material.DetailTiling : DefaultDetailTiling;
            uint width = material.DetailTexture?.Width ?? 0;
            if (material.NewEngineShaderClass == 0x00 && width != 0)
                tiling *= 512f / width;
        }
        return new Vector4(
            tiling,
            material.NewEngineParameter3C,
            hasDetail ? 1f : 0f,
            material.NewEngineFeatureFlags);
    }

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

    /// <summary>Live per-material detail-map UV tiling. Rides the fProperties map's value slot -
    /// see GetOrBuildMaterial.</summary>
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

    /// <summary>Whether this source format physically carries an alpha channel. Formats without one
    /// decode to a synthesised opaque 255. DXT1's 1-bit punch-through is a transparency flag, not a
    /// mask channel, so it counts as none.</summary>
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

    // The single TextureFiltering -> GPU sampler mapping. Game textures always tile, so every mode
    // maps to a Wrap-addressing sampler. Created once each and reused.
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
        // Samples the whole mip chain down to 1x1; clamping the maximum would pin minified textures.
        minimumLod: 0, maximumLod: uint.MaxValue, lodBias: 0, borderColor: SamplerBorderColor.TransparentBlack));

    /// <summary>mipmap: pass false for ATLASES. Mip generation averages neighbouring texels, which
    /// on an atlas blends across island boundaries - and the baked lightmap atlases have black
    /// gutters between their islands, so every minified pixel near an island edge pulls that black
    /// inward. That shows up as dark patches on lit terrain with no counterpart in the game.
    /// Native authored chains are the exception: TextureUploadPreparer preserves them even when
    /// this argument is false, because those levels already contain the game's own atlas gutters.</summary>
    public GpuTexture GetOrBuildTexture(ITexture texture, bool mipmap = true)
    {
        if (_textureCache.TryGetValue(texture.Id, out var cached))
            return cached;

        _sourceTextures[texture.Id] = texture;

        // Normally already decoded by PrepareTextures; decoded here only for a texture that pre-pass
        // could not see. Removed rather than read, so the decoded pixels are freed once uploaded.
        if (!_prepared.TryRemove(texture.Id, out var levels))
        {
            // Some texture slots genuinely have no highmip data for a given level (Texture.ReadTexture
            // returns early, leaving data empty, when the highmips pointer's length is 0) - a real,
            // already-handled case in the loader, not a corrupt read. TextureUploadPreparer returns
            // null for that case and otherwise chooses authored-vs-generated mip handling centrally.
            levels = TextureUploadPreparer.Prepare(texture, generateMipsWhenUnauthored: mipmap);
        }

        if (levels == null)
        {
            var fallback = GetDefaultAlbedoTexture();
            _textureCache[texture.Id] = fallback;
            return fallback;
        }

        // Allocated now but not uploaded yet; pixel data is queued for UploadOnePendingTexture so a
        // caller loading a whole level can spread the uploads across many frames.
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

    /// <summary>Uploads exactly one queued texture's full mip chain, so a caller can spread the cost
    /// across frames instead of blocking on it all at once. A no-op if nothing is queued.</summary>
    public void UploadOnePendingTexture()
    {
        if (!_pendingUploads.TryDequeue(out var item)) return;
        item.tex.UploadAll(_gd, item.levels);
    }

    // When useVertexAlpha is set, decoded vertex alpha is written into each vertex's color alpha
    // instead of the default fully-opaque white.
    private static Vertex3D[] ConvertGeometryToVertices(IGeometry geometry, bool useVertexAlpha)
    {
        var positions = geometry.GetVertexPositions();
        var uvs = geometry.GetTextureCoordinates();
        var normals = geometry.GetNormals();
        var tangents = geometry.GetTangents();
        var vertexAlpha = useVertexAlpha ? geometry.GetVertexAlpha() : null;
        var lightmapUVs = geometry.GetLightmapUVs();
        var jointIndices = geometry.GetJointIndices();
        var jointWeights = geometry.GetJointWeights();
        var bakedLightScales = geometry.GetVertexBakedLightScales();

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

            // Second UV channel is the lightmap UV set where the geometry has one (ties do). Falls
            // back to the base UV otherwise, harmless since no lightmap is bound for those draws.
            var lmUV = lightmapUVs != null && lightmapUVs.Length >= i * 2 + 2
                ? new Vector2(lightmapUVs[i * 2], lightmapUVs[i * 2 + 1])
                : uv;

            float alpha = vertexAlpha != null && i < vertexAlpha.Length ? vertexAlpha[i] : 1f;

            // -1 (unused slot) is stored as 0 with a 0 weight - the weight already makes it
            // contribute nothing, and clamping keeps it a valid SSBO index in the shader.
            Vector4 joints = Vector4.Zero, weights = Vector4.Zero;
            if (jointIndices != null && jointWeights != null && jointIndices.Length >= i * 4 + 4)
            {
                joints = new Vector4(
                    MathF.Max(0, jointIndices[i * 4 + 0]), MathF.Max(0, jointIndices[i * 4 + 1]),
                    MathF.Max(0, jointIndices[i * 4 + 2]), MathF.Max(0, jointIndices[i * 4 + 3]));
                weights = new Vector4(
                    jointWeights[i * 4 + 0], jointWeights[i * 4 + 1], jointWeights[i * 4 + 2], jointWeights[i * 4 + 3]);
            }

            // Retail old-engine TIE fragment programs multiply the whole lit result by the VP's TEX1.x
            // per-vertex multiplier: out = (light * TEX1.x) * albedo. The lit shader computes
            // albedo = texture * fColor.rgb and then albedo * lighting, so carrying the multiplier in
            // the vertex colour RGB is the same product.
            float lightScale = bakedLightScales != null && i < bakedLightScales.Length ? bakedLightScales[i] : 1f;
            vertices[i] = new Vertex3D(pos, uv, lmUV, n, tan, new Vector4(lightScale, lightScale, lightScale, alpha), joints, weights);
        }

        return vertices;
    }

    // Reused per-frame scratch: the selected entity's world matrices, pushed into the renderer's
    // transform SSBO.
    private readonly List<Matrix4x4> _vkTransformScratch = [];
    private bool _sceneRendererInvalidationPending;

    /// <summary>True after scene content changed and until LunaWindow reaches its post-Draw safe
    /// point. The current renderer must stay alive for the rest of that frame because ImGui may
    /// already have recorded a draw using its display texture.</summary>
    public bool SceneRendererInvalidationPending => _sceneRendererInvalidationPending;

    /// <summary>Pushes an edited entity's world matrices into the captured scene's transform SSBO
    /// without rebuilding or re-recording anything. No-op if the scene has not been captured yet.</summary>
    public void UpdateEntityTransforms(Entity moved)
    {
        if (SceneRenderer == null) return;
        UpdateCapturedEntityTransform(moved);
    }

    private void UpdateCapturedEntityTransform(Entity moved)
    {
        if (SceneRenderer == null) return;
        _vkTransformScratch.Clear();
        foreach (var (_, _, world, _) in moved.GetRenderablesForVk())
            _vkTransformScratch.Add(world);
        SceneRenderer.UpdateEntityTransforms(moved, _vkTransformScratch, moved.WorldBoundingSphere);
    }

    /// <summary>Assembles the whole level's scene from the geometry registry (VulkanSceneCapture) and
    /// EntityManager's live per-instance world transforms. Only geometries actually referenced by an
    /// instance are included, remapped to a compact index. Returns null until instances exist.</summary>
    private (List<float[]> verts, List<uint[]> idx, List<VkMaterialDesc> materials,
        List<(int geo, int mat, Matrix4x4 world, Vector4 sphere, object owner, float displayDistance, uint pickId)> instances,
        Texture? envCube, int maxSkeletonBones, int maxConcurrentAnimated)? BuildVkScene()
    {
        if (VulkanSceneCapture.VertexData.Count == 0)
            return null;

        var verts = new List<float[]>();
        var idx = new List<uint[]>();
        var materials = new List<VkMaterialDesc>();
        var instances = new List<(int, int, Matrix4x4, Vector4, object, float, uint)>();
        var geoRemap = new Dictionary<int, int>();
        var matRemap = new Dictionary<RenderMaterial, int>(ReferenceEqualityComparer.Instance);
        int maxSkeletonBones = 0;
        // One bone-palette region per moby that could possibly animate, so playing every skinned moby
        // in the level at once can never run out (see VulkanRenderer.TrySetAnimatedInstance) - sized
        // to what the level actually contains rather than a guessed constant.
        var skinnedOwners = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var entity in EntityManager.Singleton.AllRenderableEntities())
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

                if (entity is EntityMoby em && em.BaseMoby.Skeleton is { } skel)
                {
                    if (skel.Bones.Count > maxSkeletonBones) maxSkeletonBones = skel.Bones.Count;
                    skinnedOwners.Add(entity);
                }
            }
        }

        if (instances.Count == 0)
            return null;
        var envCube = EnvironmentCubemapView?.Target;
        return (verts, idx, materials, instances, envCube, maxSkeletonBones, skinnedOwners.Count);
    }

    /// <summary>Builds <see cref="SceneRenderer"/> if it does not exist yet; a no-op once it does, or
    /// while textures are still uploading (<see cref="HasPendingUploads"/>). Returns true once
    /// SceneRenderer is ready to use.</summary>
    public bool TryCaptureScene(GraphicsDevice gd, uint viewWidth, uint viewHeight)
    {
        if (SceneRenderer != null) return true;
        if (HasPendingUploads) return false;

        try
        {
            // Timed, because every Duplicate and every Save throws the whole retained scene away
            // (InvalidateSceneRenderer) and this rebuilds it from scratch on the next frame. That is
            // the prime suspect for "duplicating and saving are slow", and it logged nothing.
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var scene = BuildVkScene();
            if (scene is not { } s) return false;
            double built = timer.Elapsed.TotalSeconds;
            SceneRenderer = new VulkanRenderer(gd, s.verts, s.idx, s.materials, s.instances, true, _oldLightingEnvironment, s.envCube, viewWidth, viewHeight, _oldEngineMobys, EnvironmentCubeResources, _standardPrograms, s.maxSkeletonBones, s.maxConcurrentAnimated);
            Console.WriteLine(
                $"[Scene][Timing] retained scene captured in {timer.Elapsed.TotalSeconds:0.00}s " +
                $"(gather {built:0.00}s, GPU renderer {timer.Elapsed.TotalSeconds - built:0.00}s): " +
                $"{s.instances.Count:N0} instance(s), {s.verts.Count:N0} geometr(ies), {s.materials.Count:N0} material(s).");
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[VkRenderer] init failed: {e.Message}");
            SceneRenderer = null;
            return false;
        }
    }

    /// <summary>Requests a scene recapture. Destruction is deliberately deferred until after the
    /// current frame's ImGui draw has been submitted and the GPU is idle: View3D may already have
    /// emitted an ImDrawCmd referencing SceneRenderer.ColorTexture earlier in this same UI frame.</summary>
    // One RenderModel[] per (moby, variant) beyond variant 0, built on demand: 837 of 1409 retail
    // QForce models have a single variant and cost nothing, and the ones that do have several are
    // only built when an editor actually asks to see one.
    private readonly Dictionary<(ulong Id, uint Variant), RenderModel[]> _mobyVariantModels = [];

    /// <summary>
    /// The render models for one shader/material variant of a Moby, or null when the asset has no
    /// such variant. Variant 0 returns the shared models built at load.
    /// </summary>
    public RenderModel[]? GetMobyVariantModels(Assets.Interfaces.IMoby moby, uint variant)
    {
        ArgumentNullException.ThrowIfNull(moby);
        if (variant == 0)
            return Mobys.TryGetValue(moby.Id, out RenderModel[]? shared) ? shared : null;
        if (variant >= moby.ShaderVariantCount)
            return null;

        var key = (moby.Id, variant);
        if (_mobyVariantModels.TryGetValue(key, out RenderModel[]? cached))
            return cached;

        var models = new RenderModel[moby.Bangles.Count];
        for (int i = 0; i < moby.Bangles.Count; i++)
            models[i] = BuildModel(moby.Bangles[i].Meshes, variant);
        _mobyVariantModels[key] = models;
        return models;
    }

    /// <summary>
    /// Drops editor-only cached GPU resources for a staged material whose pixels were re-imported
    /// before Save. Resources are deliberately not disposed here: the current retained scene may
    /// still reference them until its deferred invalidation runs at the post-Draw safe point.
    /// They remain owned by the graphics device for the rest of this level session.
    /// </summary>
    public void InvalidateEditorMaterialPreview(ulong shaderId, IReadOnlyCollection<ulong> textureIds)
    {
        foreach ((ulong ShaderId, ushort LightmapIndex) key in _materialCache.Keys
                     .Where(key => key.ShaderId == shaderId)
                     .ToArray())
            _materialCache.Remove(key);

        _materialsByShader.Remove(shaderId);
        _sourceMaterials.Remove(shaderId);

        foreach (ulong textureId in textureIds.Distinct())
        {
            if (_textureCache.Remove(textureId, out GpuTexture? texture))
                _builtTextureIds.Remove(texture);
            _sourceTextures.Remove(textureId);
        }

        InvalidateSceneRenderer();
    }

    public void InvalidateSceneRenderer()
    {
        // If no retained scene exists, BuildVkScene will consume the latest registry naturally;
        // queuing an invalidation would only destroy that freshly captured scene at AfterUpdate.
        if (SceneRenderer != null) _sceneRendererInvalidationPending = true;
    }

    /// <summary>Executes a requested invalidation at LunaWindow's post-Draw safe point.</summary>
    public void FlushSceneRendererInvalidation()
    {
        if (!_sceneRendererInvalidationPending) return;
        _sceneRendererInvalidationPending = false;
        SceneRenderer?.Dispose();
        SceneRenderer = null;
    }

    /// <summary>Tears down the captured scene. Must be called before EntityManager.Singleton.Dispose()
    /// and before Dispose(), since the scene references live entity data and the textures/materials
    /// Dispose() frees.</summary>
    public void DisposeSceneRenderer()
    {
        _sceneRendererInvalidationPending = false;
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
        _sceneRendererInvalidationPending = false;
        SceneRenderer?.Dispose();
        SceneRenderer = null;

        // Only textures need releasing. The shared default stands in for every texture that failed
        // to decode, so it appears in the cache multiple times and must not be disposed through it.
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
        EnvironmentCubeResources = new Dictionary<ulong, int>();
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
