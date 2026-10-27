using System.Numerics;
using System.Security.Cryptography;
using NeoVeldrid;
using NeoVeldrid.SPIRV;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Class-0x13 materials drawn with their OWN fragment programs: the microcode the level ships
/// (0x33001), translated to GLSL by <see cref="RsxFragmentTranslator"/> and fed with what the retail
/// vertex programs and per-draw constants would give it. Docs/reverse/NEW_ENGINE_EMBEDDED_PROGRAMS_2026-10-07.md.
///
/// Which program of the bundle a draw uses is its ROUTE, measured against the RPCS3 cache's VP pairings:
/// 3-program bundles are [probe, baked, prelit] (Mobys and unbaked TIEs, VPs c29b70f7428f / 514d2fc8147c;
/// lightmapped TIEs and UFrags, dc2839ddb021; prelit TIEs, accc87a130bd), 4-program ones put another
/// pass first. The library below rebuilds those VPs' interpolants from the editor's own varyings, maps
/// the RSX texture units to the material's textures and the scene's buffers, and fills the patch groups
/// (fog, reflection occlusion, key light, material +0x50/+0x60/+0x70, clock, switches).
///
/// Standard (class 0x00) materials go the same way with the EBOOT's own programs
/// (<see cref="Loading.Shaders.NewEngineStandardPrograms"/>, when a decrypted EBOOT is found): the draw's
/// geometry picks the table block and the VP family, its permutation bits the program and the VP layout,
/// Docs/reverse/NEW_ENGINE_STANDARD_PROGRAMS_2026-10-07.md.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    /// <summary>Draw class-0x13 materials with their translated retail programs (false: the generic path).</summary>
    public static bool UseRetailEmbeddedPrograms { get; set; } =
        Environment.GetEnvironmentVariable("RELUNACY_RETAIL_PROGRAMS") != "0";

    private enum LitKind
    {
        Opaque, OpaqueCull, OpaqueCullCw,
        Accum, AccumCull, AccumCullCw,
        SoftEdgeDepth, SoftEdgeDepthCull, SoftEdgeDepthCullCw,
        Additive, AdditiveCull, AdditiveCullCw,
    }

    private const int RouteProbe = 0, RouteBaked = 1, RoutePrelit = 2;
    // The EBOOT's standard (class 0x00) programs, NewEngineStandardPrograms: interpolants of the
    // lightmapped TIE / UFrag VPs 44 + k / 98 + k, the prelit TIE VPs 52 + k, and the probe-lit
    // TIE / Moby VPs 60 + k / 166 + k (k = specular, normal map, detail bits).
    private const int RouteStdLightmapped = 3, RouteStdPrelit = 4, RouteStdProbe = 5;
    // Tools of Destruction's TIE programs (Loading.Shaders.TodTiePrograms): the baked family (lightmap
    // units 4 / 14) and the unbaked one lit by the placement's prelight. k = the key's use bits.
    private const int RouteTodBaked = 6, RouteTodPrelit = 7;

    /// <summary>Tools of Destruction's TIE programs for the level being drawn (set by AssetManager), or null.</summary>
    public static Loading.Shaders.TodTiePrograms? TodPrograms { get; set; }
    private int[] _instTodKey = [];
    private int _todDraws;
    private readonly Loading.Shaders.NewEngineStandardPrograms? _standardPrograms;
    // Per instance: the program set past its normal-map distance (the TIE builder drops 0x01 | 0x08
    // there, 0x0073E1DC), and 1 / that distance; -1 / 0 when the draw keeps one program.
    private int[] _instRsxFar = [];
    private float[] _instFarInvD0 = [];
    private float _lodScale = 1f;
    private Vector3 _viewForward;
    private int _rsxStandardDraws;

    private sealed class RsxSet
    {
        public required RsxSource Source;
        public VkShaderModule Opaque, Accum, Additive;
        public bool Broken;
        public readonly Dictionary<LitKind, VkPipeline> Pipelines = [];
    }

    /// <param name="ReadsScene">The program samples units 10 / 11 as the scene already drawn (refraction):
    /// an opaque draw of it waits for the scene copy.</param>
    private sealed record RsxSource(string? Opaque, string? Accum, string? Additive, string? Failure, bool ReadsScene = false);

    // (microcode with its group layout, route, VP permutation) -> GLSL, shared by every renderer of the process.
    private static readonly Dictionary<(string Key, int Route, int K), RsxSource> RsxSources = new();

    private readonly List<RsxSet> _rsxSets = [];
    private readonly Dictionary<ulong, LitKind> _litKindOf = [];
    private int[] _instRsx = [];
    // Opaque draws whose program refracts the scene (units 10 / 11): drawn after the scene copy, as the
    // game draws its water after the world it shows through it - drawn before it, seaside's ocean read
    // the previous frame's copy (black on the first) through its ripples.
    private bool[] _instLate = [];
    private int[] _visibleOpaqueLate = [];
    private int _visOpaqueLateCount;
    private int _rsxTranslated, _rsxFailed;

    /// <summary>Retail embedded programs that translated / fell back to the generic path in this scene.</summary>
    public (int Translated, int Failed) RetailProgramStats => (_rsxTranslated, _rsxFailed);

    /// <summary>Draw class-0x00 materials with the EBOOT's programs when they were found (debugging:
    /// RELUNACY_STANDARD_PROGRAMS=0 keeps them on the generic path alone).</summary>
    public static bool UseStandardPrograms { get; set; } =
        Environment.GetEnvironmentVariable("RELUNACY_STANDARD_PROGRAMS") != "0";

    /// <summary>Render > Team: 0 neutral, 1 red, 2 blue. Besides the material variant sets
    /// (EntityManager.ApplyTeamPreview) it gives the team colour to the draws whose program takes
    /// group 19 from the Moby's gameplay colour (selector source 0x0B).</summary>
    public static uint TeamPreview { get; set; }

    // Group 19 source 0x0B values seen in the RPCS3 cache: red team, blue team.
    private static readonly Vector4 TeamRed = new(1f, 0f, 0f, 1f), TeamBlue = new(0f, 0.67f, 0.95f, 1f);

    /// <summary>The group-19 vec4 of each draw the editor can know statically (VkInstanceLight.Gameplay).</summary>
    private void ApplyGameplayConstants(List<VkMaterialDesc> materials, VkInstanceLight[] instanceLights)
    {
        for (int i = 0; i < _instanceCount; i++)
            if (materials[_drawMatSlot[i]].IsTwoLayer)
            {
                instanceLights[i].SecondTint = materials[_drawMatSlot[i]].SecondTint;
                instanceLights[i].SecondParallax = materials[_drawMatSlot[i]].SecondParallax;
                instanceLights[i].Layers = new Vector4(1f, 0f, 0f, 0f);
            }
        if (TeamPreview is not (1 or 2))
            return;
        Vector4 team = TeamPreview == 1 ? TeamRed : TeamBlue;
        for (int i = 0; i < _instanceCount; i++)
            if (GroupNineteenSource(materials[_drawMatSlot[i]].ProgramBundle) == 0x0B)
                instanceLights[i].Gameplay = team;
    }

    /// <summary>The 0x33003 source feeding group 19, from the first record as the loader keeps it
    /// (0x00724F60: {u8 n, n x (u8 source, u8 group)}); -1 when none.</summary>
    private static int GroupNineteenSource(Loading.Shaders.NewEngineShaderProgramBundle? bundle)
    {
        if (bundle?.SelectorData is not { Length: >= 1 } data)
            return -1;
        int n = data[0];
        for (int k = 0; k < n && 2 + 2 * k < data.Length; k++)
            if (data[2 + 2 * k] == 0x13)
                return data[1 + 2 * k];
        return -1;
    }

    /// <summary>Draws (instances) drawn with an EBOOT standard program.</summary>
    public int StandardProgramDraws => _rsxStandardDraws;

    private void RegisterLitKinds()
    {
        void Add(VkPipeline p, LitKind k) { if (p.Handle != 0) _litKindOf[p.Handle] = k; }
        Add(_pipelineOpaque, LitKind.Opaque); Add(_pipelineOpaqueCull, LitKind.OpaqueCull); Add(_pipelineOpaqueCullCw, LitKind.OpaqueCullCw);
        Add(_pipelineAccum, LitKind.Accum); Add(_pipelineAccumCull, LitKind.AccumCull); Add(_pipelineAccumCullCw, LitKind.AccumCullCw);
        Add(_pipelineSoftEdgeDepth, LitKind.SoftEdgeDepth); Add(_pipelineSoftEdgeDepthCull, LitKind.SoftEdgeDepthCull); Add(_pipelineSoftEdgeDepthCullCw, LitKind.SoftEdgeDepthCullCw);
        Add(_pipelineAdditive, LitKind.Additive); Add(_pipelineAdditiveCull, LitKind.AdditiveCull); Add(_pipelineAdditiveCullCw, LitKind.AdditiveCullCw);
    }

    /// <summary>One lit pipeline of the given kind - the generic ones and the translated programs' share
    /// every state but the fragment module.</summary>
    private VkPipeline CreateLitPipeline(LitKind kind, VkShaderModule fragment)
    {
        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        var mask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A;
        VkDynamicState* dyn = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        var dynState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dyn };
        var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
        var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var vbinding = new VkVertexInputBindingDescription { binding = 0, stride = VertexStride, inputRate = VkVertexInputRate.Vertex };
        VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[6];
        attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        attrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32Sfloat, offset = 12 };
        attrs[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 20 };
        attrs[3] = new VkVertexInputAttributeDescription { location = 3, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 32 };
        attrs[4] = new VkVertexInputAttributeDescription { location = 4, binding = 0, format = VkFormat.R32G32Sfloat, offset = 48 };
        attrs[5] = new VkVertexInputAttributeDescription { location = 5, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 56 };
        var vertexInput = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbinding, vertexAttributeDescriptionCount = 6, pVertexAttributeDescriptions = attrs };

        bool opaque = kind is LitKind.Opaque or LitKind.OpaqueCull or LitKind.OpaqueCullCw;
        bool cull = kind is LitKind.OpaqueCull or LitKind.OpaqueCullCw or LitKind.AccumCull or LitKind.AccumCullCw
            or LitKind.SoftEdgeDepthCull or LitKind.SoftEdgeDepthCullCw or LitKind.AdditiveCull or LitKind.AdditiveCullCw;
        bool cw = kind is LitKind.OpaqueCullCw or LitKind.AccumCullCw or LitKind.SoftEdgeDepthCullCw or LitKind.AdditiveCullCw;
        var raster = new VkPipelineRasterizationStateCreateInfo
        {
            polygonMode = VkPolygonMode.Fill,
            cullMode = cull ? VkCullModeFlags.Back : VkCullModeFlags.None,
            frontFace = cw ? VkFrontFace.Clockwise : VkFrontFace.CounterClockwise,
            lineWidth = 1f,
            depthBiasEnable = !opaque,
            depthBiasConstantFactor = 0f,
            depthBiasSlopeFactor = opaque ? 0f : -NonOpaqueSlopeScaledDepthBias,
            depthBiasClamp = 0f,
        };

        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = opaque ? _vs : _vsNonOpaque, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = fragment, pName = entry };

        VkPipelineColorBlendAttachmentState* atts = stackalloc VkPipelineColorBlendAttachmentState[2];
        VkPipelineDepthStencilStateCreateInfo depth;
        VkPipelineColorBlendStateCreateInfo blend;
        VkRenderPass pass;
        switch (kind)
        {
            case LitKind.Opaque or LitKind.OpaqueCull or LitKind.OpaqueCullCw:
                depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.GreaterOrEqual };
                atts[0] = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
                blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = atts };
                pass = _rpOpaque;
                break;
            case LitKind.Accum or LitKind.AccumCull or LitKind.AccumCullCw:
                depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.GreaterOrEqual };
                atts[0] = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.One, dstColorBlendFactor = VkBlendFactor.One, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.One, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add, colorWriteMask = mask };
                atts[1] = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.Zero, dstColorBlendFactor = VkBlendFactor.OneMinusSrcColor, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.OneMinusSrcColor, alphaBlendOp = VkBlendOp.Add, colorWriteMask = VkColorComponentFlags.R };
                blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 2, pAttachments = atts };
                pass = _rpAccum;
                break;
            case LitKind.SoftEdgeDepth or LitKind.SoftEdgeDepthCull or LitKind.SoftEdgeDepthCullCw:
                depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.GreaterOrEqual };
                atts[0] = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = 0 };
                blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = atts };
                pass = _rpOpaque;
                break;
            default:
                depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.GreaterOrEqual };
                atts[0] = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.SrcAlpha, dstColorBlendFactor = VkBlendFactor.One, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add, colorWriteMask = mask };
                blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = atts };
                pass = _rpOpaque;
                break;
        }

        var info = new VkGraphicsPipelineCreateInfo
        {
            stageCount = 2, pStages = stages, pVertexInputState = &vertexInput, pInputAssemblyState = &inputAssembly,
            pViewportState = &viewportState, pRasterizationState = &raster, pMultisampleState = &multisample,
            pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _layout,
            renderPass = pass, subpass = 0,
        };
        VkPipeline p;
        Check(_api.vkCreateGraphicsPipelines(GetPipelineCache(), 1, &info, &p), $"vkCreateGraphicsPipelines({kind})");
        return p;
    }

    /// <summary>The bundle program a route draws with: 3-program bundles are [probe, baked, prelit],
    /// 4-program bundles put another pass first (RPCS3 cache VP pairings, 07-10-2026).</summary>
    private static int BundleVariant(int count, int route) =>
        Math.Clamp((count == 4 ? 1 : 0) + route, 0, Math.Max(0, count - 1));

    /// <summary>Per instance: the translated program set it draws with, or -1 for the generic path.</summary>
    private void BuildRetailPrograms(List<VkMaterialDesc> materials, VkInstanceLight[] instanceLights)
    {
        _instRsx = new int[_instanceCount];
        Array.Fill(_instRsx, -1);
        _instRsxFar = new int[_instanceCount];
        Array.Fill(_instRsxFar, -1);
        _instFarInvD0 = new float[_instanceCount];
        _rsxStandardDraws = 0;
        _instLate = [];
        if (!UseRetailEmbeddedPrograms)
            return;
        var setOf = new Dictionary<(string Key, int Route, int K), int>();
        // Debugging: RELUNACY_RETAIL_ONLY=<skeleton,...> translates only those programs.
        string? only = Environment.GetEnvironmentVariable("RELUNACY_RETAIL_ONLY") is { Length: > 0 } o ? o : null;
        int SetFor(Loading.Shaders.NewEngineShaderProgramVariant variant, int route, int k)
        {
            if (only != null && !only.Contains(variant.SkeletonKey))
                return -1;
            string key = ProgramKey(variant);
            if (!setOf.TryGetValue((key, route, k), out int set))
            {
                set = CompileRetailProgram(variant, key, route, k);
                setOf[(key, route, k)] = set;
                if (Environment.GetEnvironmentVariable("RELUNACY_RSX_LIST") == "1")
                    Console.Error.WriteLine($"[RetailPrograms] set {set}: {variant.SkeletonKey} route {route} k {k}");
            }
            return set;
        }
        _todDraws = 0;
        for (int i = 0; i < _instanceCount; i++)
        {
            int mat = _drawMatSlot[i];
            // ToD TIE: the shell's program, baked family when the draw has its lightmap, the prelit one
            // when it has its placement's prelight; the analytic route (neither) keeps the generic shader.
            if (TodPrograms != null && i < _instTodKey.Length && _instTodKey[i] >= 0)
            {
                bool todBaked = _matPC0[mat].X > 0.5f;
                bool todPrelit = !todBaked && instanceLights[i].Direction0.W is > 5.5f and < 6.5f;
                // Two-layer primitives take the two-layer table (lightmapped only: the prelit tables have
                // no unit 12); one that does not parse keeps the generic shader, which blends them too.
                bool todTwoLayer = materials[mat].IsTwoLayer;
                if ((todBaked || todPrelit) && !(todTwoLayer && !todBaked) &&
                    TodPrograms.Get(todBaked, _instTodKey[i], todTwoLayer) is { } todProgram)
                {
                    _instRsx[i] = SetFor(todProgram, todBaked ? RouteTodBaked : RouteTodPrelit, _instTodKey[i]);
                    if (_instRsx[i] >= 0) _todDraws++;
                }
                continue;
            }
            if (materials[mat].ProgramBundle is { Variants.Count: > 0 } bundle)
            {
                int route = InstanceRoute(i, mat, instanceLights[i]);
                if (route < 0)
                    continue;
                _instRsx[i] = SetFor(bundle.Variants[BundleVariant(bundle.Variants.Count, route)], route, 0);
                continue;
            }
            if (_standardPrograms == null || !UseStandardPrograms || !materials[mat].IsNewEngine || materials[mat].ShaderClass != 0 ||
                materials[mat].IsBillboard > 0.5f ||
                !StandardRoute(i, mat, instanceLights[i], out var block, out int stdRoute, out int bits))
                continue;
            bool translucent = (int)materials[mat].GameRenderMode is not (0 or 4 or 5);
            // Two-layer draws exist on the opaque pass only (the translucent passes pass 0xFFFF).
            bool twoLayer = materials[mat].IsTwoLayer && !translucent;
            if (_standardPrograms.Get(block, twoLayer, translucent, bits) is not { } near)
                continue;
            _instRsx[i] = SetFor(near, stdRoute, StandardVpPermutation(stdRoute, bits));
            if (_instRsx[i] < 0)
                continue;
            _rsxStandardDraws++;
            // Past d0 the TIE builder draws the permutation without normal map and parallax.
            VkInstanceLight light = instanceLights[i];
            int farBits = bits & ~0x09;
            if ((SceneEntityKind)_instKind[i] == SceneEntityKind.Tie && light.TieShaderUse1.W > 0.5f && farBits != bits &&
                _standardPrograms.Get(block, twoLayer, translucent, farBits) is { } far)
            {
                _instRsxFar[i] = SetFor(far, stdRoute, StandardVpPermutation(stdRoute, farBits));
                _instFarInvD0[i] = light.TieShaderUse1.X;
            }
        }

        if (_todDraws > 0)
            Console.WriteLine($"[RetailPrograms] Tools of Destruction TIE programs on {_todDraws} draw(s).");
        // The SPIR-V the scene needs, compiled in parallel (about 0.1 s per source with shaderc): the
        // opaque program for modes 0/4/5, the OIT one for 1/3/5/6, the additive one for mode 2.
        var wanted = new HashSet<string>();
        for (int i = 0; i < _instanceCount; i++)
        {
            foreach (int set in (ReadOnlySpan<int>)[_instRsx[i], _instRsxFar[i]])
            {
                if (set < 0) continue;
                RsxSource src = _rsxSets[set].Source;
                int mode = (int)materials[_drawMatSlot[i]].GameRenderMode;
                if (mode is 0 or 4 or 5) wanted.Add(src.Opaque!);
                if (mode is 1 or 3 or 5 or 6) wanted.Add(src.Accum!);
                if (mode == 2) wanted.Add(src.Additive!);
            }
        }
        var broken = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        Parallel.ForEach(wanted, glsl =>
        {
            try { RetailSpirv(glsl); }
            catch (Exception e) { broken[glsl] = e.Message; }
        });
        foreach (RsxSet set in _rsxSets)
            if (broken.ContainsKey(set.Source.Opaque!) || broken.ContainsKey(set.Source.Accum!) || broken.ContainsKey(set.Source.Additive!))
                set.Broken = true;
        _instLate = new bool[_instanceCount];
        for (int i = 0; i < _instanceCount; i++)
            _instLate[i] = (_instRsx[i] >= 0 && !_rsxSets[_instRsx[i]].Broken && _rsxSets[_instRsx[i]].Source.ReadsScene) ||
                           (_instRsxFar[i] >= 0 && !_rsxSets[_instRsxFar[i]].Broken && _rsxSets[_instRsxFar[i]].Source.ReadsScene);
        if (!broken.IsEmpty)
        {
            for (int i = 0; i < _instanceCount; i++)
            {
                if (_instRsxFar[i] >= 0 && (_rsxSets[_instRsxFar[i]].Broken || _instRsx[i] < 0 || _rsxSets[_instRsx[i]].Broken))
                    _instRsxFar[i] = -1;
                if (_instRsx[i] >= 0 && _rsxSets[_instRsx[i]].Broken)
                    _instRsx[i] = -1;
            }
            _rsxFailed += _rsxSets.Count(set => set.Broken);
            _rsxTranslated -= _rsxSets.Count(set => set.Broken);
            Console.WriteLine($"[RetailPrograms] {broken.Count} GLSL source(s) rejected: {broken.Values.First()}");
        }
    }

    // SPIR-V of the translated programs, kept on disk: a level's first capture compiles a few dozen of
    // them, every later one (and every later session) reads them back.
    private static readonly string RetailSpirvCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReLunacy", "RetailPrograms");

    private static byte[] RetailSpirv(string glsl)
    {
        lock (SpirvCache)
            if (SpirvCache.TryGetValue((glsl, ShaderStages.Fragment), out byte[]? cached))
                return cached;
        string name = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(glsl))) + ".spv";
        string path = Path.Combine(RetailSpirvCacheDirectory, name);
        byte[]? spirv = null;
        try
        {
            if (File.Exists(path))
                spirv = File.ReadAllBytes(path);
        }
        catch (IOException) { }
        if (spirv == null)
        {
            spirv = SpirvCompilation.CompileGlslToSpirv(glsl, "vk", ShaderStages.Fragment, new GlslCompileOptions()).SpirvBytes;
            try
            {
                Directory.CreateDirectory(RetailSpirvCacheDirectory);
                string temp = path + "." + Environment.CurrentManagedThreadId + ".tmp";
                File.WriteAllBytes(temp, spirv);
                File.Move(temp, path, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        lock (SpirvCache)
            SpirvCache[(glsl, ShaderStages.Fragment)] = spirv;
        return spirv;
    }

    private VkShaderModule RetailModule(string glsl)
    {
        byte[] spirv = RetailSpirv(glsl);
        fixed (byte* p = spirv)
        {
            var info = new VkShaderModuleCreateInfo { codeSize = (nuint)spirv.Length, pCode = (uint*)p };
            VkShaderModule m;
            Check(_api.vkCreateShaderModule(&info, &m), "vkCreateShaderModule(retail)");
            return m;
        }
    }

    private int InstanceRoute(int instance, int mat, VkInstanceLight light)
    {
        switch ((SceneEntityKind)_instKind[instance])
        {
            case SceneEntityKind.Moby:
                return RouteProbe;
            case SceneEntityKind.Tie:
                if (_matPC0[mat].X > 0.5f) return RouteBaked;
                return light.Direction0.W is > 4.5f and < 5.5f ? RoutePrelit : RouteProbe;
            case SceneEntityKind.UFrag:
                return _matPC0[mat].X > 0.5f ? RouteBaked : RouteProbe;
            default:
                return -1;
        }
    }

    /// <summary>The standard-program block and VP family of a class-0x00 draw, and its permutation bits
    /// (material +0x20 AND the primitive's / record's mask, 0x1D); false when no retail route applies.</summary>
    private bool StandardRoute(int instance, int mat, VkInstanceLight light,
        out Loading.Shaders.NewEngineStandardPrograms.Block block, out int route, out int bits)
    {
        Vector4 permutation = _matPermutation[mat];
        bits = (int)(permutation.W + 0.5f);
        if (light.TieShaderUse0.W > 0.5f)
            bits &= (int)(light.TieShaderUse0.W + 0.5f) - 1;
        // A detail bit with no detail map bound would read the fallback texture as a full-strength map.
        if (permutation.Z < 0.5f)
            bits &= ~0x10;
        bits &= 0x1D;
        bool baked = _matPC0[mat].X > 0.5f;
        switch ((SceneEntityKind)_instKind[instance])
        {
            case SceneEntityKind.Moby:
                (block, route) = (Loading.Shaders.NewEngineStandardPrograms.Block.Probe, RouteStdProbe);
                return true;
            case SceneEntityKind.Tie:
                if (baked)
                    (block, route) = (Loading.Shaders.NewEngineStandardPrograms.Block.Lightmapped, RouteStdLightmapped);
                else if (light.Direction0.W is > 4.5f and < 5.5f)
                    (block, route) = (Loading.Shaders.NewEngineStandardPrograms.Block.Prelit, RouteStdPrelit);
                else
                    (block, route) = (Loading.Shaders.NewEngineStandardPrograms.Block.Probe, RouteStdProbe);
                return true;
            case SceneEntityKind.UFrag when baked:
                (block, route) = (Loading.Shaders.NewEngineStandardPrograms.Block.Lightmapped, RouteStdLightmapped);
                return true;
            default:
                (block, route) = (default, -1);
                return false;
        }
    }

    /// <summary>The VP permutation k of a standard draw (VPs 44 / 52 / 60 / 98 / 166 + k): bit 0
    /// specular (0x04), bit 1 normal map (0x08), bit 2 detail (0x10).</summary>
    private static int StandardVpPermutation(int route, int bits) => (bits >> 2) & 7;

    /// <summary>The far (no normal map, no parallax) program of a TIE once its view depth passes d0,
    /// chosen per draw as the game's list builder does.</summary>
    private int RetailSetFor(int instance)
    {
        int far = _instRsxFar[instance];
        if (far < 0)
            return _instRsx[instance];
        float depth = Vector3.Dot(_instCenter[instance] - _cameraPosition, _viewForward);
        return depth * _lodScale * _instFarInvD0[instance] > 1f ? far : _instRsx[instance];
    }

    private static string ProgramKey(Loading.Shaders.NewEngineShaderProgramVariant variant)
    {
        // The microcode with its patched slots zeroed (they are zero in the files anyway) plus the group
        // layout: two materials with the same key run the same GLSL, their own values coming from uMat*.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(variant.Microcode);
        foreach (var (slot, group) in variant.SlotGroups().OrderBy(p => p.Key))
        {
            hash.AppendData(BitConverter.GetBytes(slot));
            hash.AppendData(BitConverter.GetBytes(group));
        }
        hash.AppendData(BitConverter.GetBytes(variant.Exports32));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private int CompileRetailProgram(Loading.Shaders.NewEngineShaderProgramVariant variant, string key, int route, int k)
    {
        RsxSource source;
        lock (RsxSources)
        {
            if (!RsxSources.TryGetValue((key, route, k), out source!))
            {
                var program = RsxFragmentTranslator.Translate(variant.Microcode, variant.SlotGroups(), variant.Exports32, out string? failure);
                source = program == null
                    ? new RsxSource(null, null, null, failure)
                    : new RsxSource(
                        RetailFragmentSource(FragmentOpaqueGlsl, program, route, k, blended: false),
                        RetailFragmentSource(FragmentAccumGlsl, program, route, k, blended: true),
                        RetailFragmentSource(FragmentAdditiveGlsl, program, route, k, blended: true),
                        null,
                        !(route >= RouteStdLightmapped && program.TextureUnits.Contains(12)) &&
                        (program.TextureUnits.Contains(10) || program.TextureUnits.Contains(11)));
                RsxSources[(key, route, k)] = source;
                if (failure != null)
                    Console.WriteLine($"[RetailPrograms] {variant.Tuid:X16} ({variant.SkeletonKey}) not translated: {failure}");
            }
        }
        if (source.Failure != null)
        {
            _rsxFailed++;
            return -1;
        }
        _rsxSets.Add(new RsxSet { Source = source });
        _rsxTranslated++;
        return _rsxSets.Count - 1;
    }

    /// <summary>The generic fragment program with its shade() swapped for the translated one.</summary>
    private static string RetailFragmentSource(string generic, RsxFragmentTranslator.Result program, int route, int k, bool blended)
    {
        string head = generic[..LitFragCommon.Length];
        string tail = generic[LitFragCommon.Length..].Replace("shade(c, a);", "shadeRetail(c, a);");
        // The EBOOT's opaque programs (no fog group) write the glow mask to alpha and leave fog out: the
        // editor takes coverage from the albedo and fogs their output itself (RSX_OPAQUE_PROGRAM).
        bool opaqueProgram = route >= RouteStdLightmapped && !program.Groups.Contains(0);
        // The EBOOT's two-layer programs read the blend mask on unit 12 and the second material on 8..11.
        bool twoLayer = route >= RouteStdLightmapped && program.TextureUnits.Contains(12);
        head = head.Replace("#version 450",
            $"#version 450\n#define RSX_ROUTE {route}\n#define RSX_K {k}\n#define RSX_BLENDED {(blended ? 1 : 0)}" +
            $"\n#define RSX_OPAQUE_PROGRAM {(opaqueProgram ? 1 : 0)}\n#define RSX_TWO_LAYER {(twoLayer ? 1 : 0)}" +
            $"\n#define RSX_DEBUG {Environment.GetEnvironmentVariable("RELUNACY_RSX_DEBUG") ?? "0"}");
        return head + RetailLibraryGlsl + program.Body + RetailShadeGlsl + tail;
    }

    /// <summary>The pipeline a translated draw uses in place of a generic one, built on first use.</summary>
    private VkPipeline RetailPipeline(int set, VkPipeline generic)
    {
        if (!_litKindOf.TryGetValue(generic.Handle, out LitKind kind))
            return generic;
        RsxSet s = _rsxSets[set];
        if (s.Broken)
            return generic;
        if (!s.Pipelines.TryGetValue(kind, out VkPipeline p))
        {
            try
            {
                VkShaderModule fs;
                switch (kind)
                {
                    case LitKind.Accum or LitKind.AccumCull or LitKind.AccumCullCw:
                        if (s.Accum.Handle == 0) s.Accum = RetailModule(s.Source.Accum!);
                        fs = s.Accum;
                        break;
                    case LitKind.Additive or LitKind.AdditiveCull or LitKind.AdditiveCullCw:
                        if (s.Additive.Handle == 0) s.Additive = RetailModule(s.Source.Additive!);
                        fs = s.Additive;
                        break;
                    default:
                        if (s.Opaque.Handle == 0) s.Opaque = RetailModule(s.Source.Opaque!);
                        fs = s.Opaque;
                        break;
                }
                p = CreateLitPipeline(kind, fs);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[RetailPrograms] pipeline {kind} failed: {e.Message}");
                s.Broken = true;
                return generic;
            }
            s.Pipelines[kind] = p;
        }
        return p;
    }

    private void DestroyRetailPrograms()
    {
        foreach (RsxSet s in _rsxSets)
        {
            foreach (VkPipeline p in s.Pipelines.Values) _api.vkDestroyPipeline(p);
            if (s.Opaque.Handle != 0) _api.vkDestroyShaderModule(s.Opaque);
            if (s.Accum.Handle != 0) _api.vkDestroyShaderModule(s.Accum);
            if (s.Additive.Handle != 0) _api.vkDestroyShaderModule(s.Additive);
        }
        _rsxSets.Clear();
    }

    // Appended after LitFragCommon: the RSX side of the translated programs.
    private const string RetailLibraryGlsl = @"
// ---------------- retail embedded programs (VulkanRenderer.RsxPrograms.cs) ----------------
vec4 WPOS, COL0, COL1, FOGC, TEX0, TEX1, TEX2, TEX3, TEX4, TEX5, TEX6, TEX7, TEX8, TEX9, SSFC, IN15;
vec4 G0, G1, G2, G3, G4, G5, G6, G7, G8, G9, G10, G11, G12, G13, G14, G15, G16, G17, G18, G19, G20, G21, G22;
int rsxCubeSlot;
vec4 rsxProject(vec4 c) { return vec4(c.xyz / c.w, 1.0); }
vec4 rsxDst(vec4 a, vec4 b) { return vec4(1.0, a.y * b.y, a.z, b.w); }
// NRM of a zero vector stays zero instead of turning the whole pixel into NaN.
vec4 rsxNormalize(vec4 v) { float l = dot(v.xyz, v.xyz); return vec4(l > 0.0 ? v.xyz * inversesqrt(l) : vec3(0.0), 0.0); }
vec4 rsxLit(vec4 a) {
    float d = max(a.x, 0.0);
    float s = a.x > 0.0 ? pow(max(a.y, 0.0), clamp(a.w, -128.0, 128.0)) : 0.0;
    return vec4(1.0, d, s, 1.0);
}
// The probe (unit 4 on the probe route): the retail program reads ambient rgb and the sun gate in a
// and adds the key light itself (groups 3/4), so this is the editor's ambient without its key light.
vec3 rsxAmbient(vec3 n) {
    InstanceLight il = uInstanceLights.values[fInstanceIndex];
    vec3 inst = il.ambient.xyz * (1.0 + 0.5 * n.y) + il.light1Colour.xyz * max(dot(n, il.direction1.xyz), 0.0);
    vec3 env = mix(uEnvAmbient + uEnvLight1Colour * max(dot(n, uEnvDirection1), 0.0), inst, il.ambient.w * uEnvHasLighting);
    return mix(vec3(uAmbient), env, uEnvHasLighting);
}
// The game's per-Moby probe (an 8x8 cube the SPU job igDiffuseCubeMaps fills from L2 SH accumulated by
// 0x007FEF20 over the lights gathered for the root Moby, Docs/reverse/NEW_ENGINE_MOBY_PROBE_2026-10-07.md).
// A static scene's lights are the region's own (region.dat 0x1DB03: the sun, which only sets the
// visibility in alpha, and the ambient, which adds its colour to c0), so the SH reduce to E = ambient and
// each texel is T2 (E + T1 Eup) with T1 = sat(-0.05 y), T2 = 1 - sat(-0.8 y); alpha = T2 x sun visibility
// (1 here: the program's own shadow term S gates the sun per pixel).
vec4 rsxProbe(vec3 n) {
    float t1 = clamp(-n.y * 0.05, 0.0, 1.0);
    float t2 = 1.0 - clamp(-n.y * 0.8, 0.0, 1.0);
    vec3 e = max(uEnvAmbient, vec3(0.0));
    return vec4(t2 * e * (1.0 + t1), t2);
}
vec4 rsxTex0(vec4 c) { vec4 t = texture(uAlbedo, c.xy); if (uMat2.y > 0.5) t.rgb = pow(max(t.rgb, vec3(0.0)), vec3(2.2)); return t; }
vec4 rsxTex1(vec4 c) { return texture(uNormal, c.xy) * 2.0 - 1.0; }
vec4 rsxTex2(vec4 c) { return texture(uProps, c.xy); }
vec4 rsxTex3(vec4 c) { return texture(uDetail, c.xy) * 2.0 - 1.0; }
vec4 rsxTex4(vec4 c) {
#if RSX_ROUTE == 6
    // ToD LM1 (0x5400).
    vec4 t = texture(uLightColour, c.xy);
    if (uMat2.z > 0.5) t.rgb = pow(max(t.rgb, vec3(0.0)), vec3(2.2));
    return t;
#elif RSX_ROUTE == 1 || RSX_ROUTE == 3
    vec4 t = texture(uLightColour, c.xy);
    if (uMat2.z > 0.5) t.rgb = pow(max(t.rgb, vec3(0.0)), vec3(2.2));
    // A direction-encoded bake (BIASED 0x5410, A Crack in Time): DXT1, the sun already in the colour -
    // no baked visibility for the program to add the sun with.
    if (uMat2.x > 0.5) t.a = 0.0;
    return t;
#else
    return uEnvHasLighting > 0.5 && uOldEngineMode < 0.5 ? rsxProbe(normalize(c.xyz)) : vec4(rsxAmbient(normalize(c.xyz)), 1.0);
#endif
}
vec4 rsxTex5(vec4 c) { return vec4(sunShadowTerm()); }
vec4 rsxTex6(vec4 c) { return vec4(0.0); }
vec4 rsxTex7(vec4 c) {
    return rsxCubeSlot == 1 ? texture(uEnvCube1, c.xyz)
         : rsxCubeSlot == 2 ? texture(uEnvCube2, c.xyz)
         : rsxCubeSlot == 3 ? texture(uEnvCube3, c.xyz)
         : texture(uEnvCube, c.xyz);
}
#if RSX_TWO_LAYER
vec4 rsxTex8(vec4 c) { vec4 t = texture(uAlbedo2, c.xy); if (uMat2.y > 0.5) t.rgb = pow(max(t.rgb, vec3(0.0)), vec3(2.2)); return t; }
vec4 rsxTex9(vec4 c) { return texture(uNormal2, c.xy) * 2.0 - 1.0; }
#else
vec4 rsxTex8(vec4 c) { return texture(uAuxA, c.xy); }
vec4 rsxTex9(vec4 c) { return texture(uAuxB, c.xy); }
#endif
// Units 10 / 11: the colour already drawn behind the surface, at the screen UV the program builds from
// WPOS x group 17 (+ its refraction offset), linearised like every other colour the program reads.
vec4 rsxSceneColor(vec4 c) {
    vec4 t = texture(uSceneColor, c.xy);
    if (uMat2.y > 0.5) t.rgb = pow(max(t.rgb, vec3(0.0)), vec3(2.2));
    return t;
}
#if RSX_TWO_LAYER
vec4 rsxTex10(vec4 c) { return texture(uProps2, c.xy); }
vec4 rsxTex11(vec4 c) { return texture(uDetail2, c.xy) * 2.0 - 1.0; }
#else
vec4 rsxTex10(vec4 c) { return rsxSceneColor(c); }
vec4 rsxTex11(vec4 c) { return rsxSceneColor(c); }
#endif
#if RSX_TWO_LAYER
vec4 rsxTex12(vec4 c) { return texture(uBlendMask, c.xy); }
#else
vec4 rsxTex12(vec4 c) { return vec4(0.0); }
#endif
vec4 rsxTex13(vec4 c) { return vec4(1.0); }
// 0x5410 is three RNM weights on All 4 One / QForce / Into the Nexus. A Crack in Time's (BIASED, CONTROL1
// 0xA8B4) is the dominant light direction in tangent space, the Tools of Destruction encoding (light =
// LM x sat(N.D) / D.z): turn it into the weights that give the same light on the flat normal (exactly 1)
// and lean the same way, w_i = 3 sat(b_i.D)^2 / sum_j sat(b_j.D)^2 (INFERRED: ACiT's own programs are in
// its encrypted EBOOT).
vec4 rsxTex14(vec4 c) {
    vec4 t = texture(uLightDir, c.xy);
#if RSX_ROUTE == 6
    // ToD LM2 (0x5410): the light direction, expanded by the sampler's BIASED address state.
    return uMat2.x > 0.5 ? vec4(t.rgb * 2.0 - 1.0, t.a) : t;
#endif
    if (uMat2.x < 0.5) return t;
    vec3 d = t.rgb * 2.0 - 1.0;
    if (dot(d, d) < 1.0e-6) return vec4(1.0, 1.0, 1.0, t.a);
    d = normalize(d);
    vec3 s = clamp(vec3(dot(d, vec3(-0.408203, 0.707031, 0.577148)),
                        dot(d, vec3(-0.408203, -0.707031, 0.577148)),
                        dot(d, vec3(0.816406, 0.0, 0.577148))), 0.0, 1.0);
    float sum = dot(s, s);
    return vec4(sum > 1.0e-4 ? 3.0 * s * s / sum : vec3(0.0), t.a);
}
vec4 rsxTex15(vec4 c) { return vec4(0.0); }

vec2 rsxLightmapUV() {
    vec2 centred = fUV2 - uLightmapUVPivot;
    float s = sin(radians(uLightmapUVRotation));
    float k = cos(radians(uLightmapUVRotation));
    vec2 rotated = vec2(centred.x * k - centred.y * s, centred.x * s + centred.y * k) + uLightmapUVPivot;
    return rotated * uLightmapUVScale + uLightmapUVOffset;
}

void rsxSetup() {
    InstanceLight il = uInstanceLights.values[fInstanceIndex];
    vec3 n = normalize(fWorldNormal);
    vec3 t = normalize(fWorldTangent - n * dot(fWorldTangent, n));
    vec3 b = cross(n, t) * fHandedness;
    vec3 v = uCameraPosition - fWorldPos;
    vec3 l = uEnvDirection0;
    float viewDepth = (uNewEngineLodScale > 0.0 ? uNewEngineLodScale : 1.0) / gl_FragCoord.w;
    bool hasFades = il.tieShaderUse1.w > 0.5;
    float normalFade = hasFades ? clamp(2.5 - 2.5 * viewDepth * il.tieShaderUse1.x, 0.0, 1.0) : 1.0;
    float reflFade = hasFades ? clamp(2.5 - 2.5 * viewDepth * il.tieShaderUse1.y, 0.0, 1.0) : 1.0;
    float detailWeight = clamp(uMat6.y * (1.0 - (hasFades ? clamp(viewDepth * il.tieShaderUse1.z, 0.0, 1.0) : 0.0)), 0.0, 1.0);
    vec2 detailUV = fUV * uMat6.x;
    bool moby = il.direction0.w > 2.5 && il.direction0.w < 3.5;
    WPOS = gl_FragCoord; COL0 = fColor; COL1 = vec4(0.0); FOGC = vec4(0.0);
    TEX7 = vec4(0.0); TEX8 = vec4(0.0); TEX9 = vec4(0.0); SSFC = vec4(0.0); IN15 = vec4(0.0);
#if RSX_ROUTE == 1
    // Lightmapped TIE / UFrag (VP dc2839ddb021): view and key light in tangent space, c[4].x in TEX4.w.
    bool retailScale = il.direction0.w > 3.5 && il.direction0.w < 4.5;
    TEX0 = vec4(fUV, rsxLightmapUV());
    TEX1 = vec4(fColor.r, fColor.a, detailUV);
    TEX2 = vec4(dot(v, t), dot(v, b), dot(v, n), reflFade * uMat5.z);
    TEX3 = vec4(n, normalFade * uMat5.y);
    TEX4 = vec4(t, retailScale ? il.direction1.w : 3.0);
    TEX5 = vec4(b, detailWeight);
    TEX6 = vec4(dot(t, l), dot(b, l), dot(n, l), 0.0);
#elif RSX_ROUTE == 2
    // Prelit TIE (VP accc87a130bd): chroma in TEX1, the three basis intensities x c[4].z in TEX6.
    float scale = max(il.direction1.w, 1.0e-6);
    TEX0 = vec4(fUV, fColor.r, fColor.a);
    TEX1 = vec4(fPrelightChroma / scale, fPrelightIntensity.w);
    TEX2 = vec4(dot(v, t), dot(v, b), dot(v, n), reflFade * uMat5.z);
    TEX3 = vec4(n, normalFade * uMat5.y);
    TEX4 = vec4(t, 0.0);
    TEX5 = vec4(b, 0.0);
    TEX6 = vec4(fPrelightIntensity.xyz * scale, 0.0);
    TEX7 = vec4(detailUV, detailWeight, 0.0);
    TEX8 = vec4(dot(t, l), dot(b, l), dot(n, l), 0.0);
#elif RSX_ROUTE == 3
    // EBOOT standard programs, lightmapped TIE / UFrag: VPs 44 + k / 98 + k, k = specular | normal map << 1
    // | detail << 2. TEX1.xy = the per-vertex light multiplier and blend weight (|position.w| / 32768,
    // low 7 bits / 128), TEX1.z = c[4].x on the RNM permutations, c[4].y on the flat ones.
    bool retailScale = il.direction0.w > 3.5 && il.direction0.w < 4.5;
    float c4x = retailScale ? il.direction1.w : 3.0;
    float c4y = retailScale ? il.tieShaderUseMeta.z : 3.0;
    vec2 w = vec2(fColor.r, fColor.a);
    vec3 vts = vec3(dot(v, t), dot(v, b), dot(v, n));
    vec3 lts = vec3(dot(t, l), dot(b, l), dot(n, l));
    TEX0 = vec4(fUV, rsxLightmapUV());
    TEX1 = vec4(0.0); TEX2 = vec4(0.0); TEX3 = vec4(0.0); TEX4 = vec4(0.0); TEX5 = vec4(0.0); TEX6 = vec4(0.0);
#if RSX_K == 0
    TEX1 = vec4(w, c4y, 0.0); TEX2 = vec4(n, 0.0);
#elif RSX_K == 1
    TEX1 = vec4(w, c4y, 0.0); TEX2 = vec4(v, reflFade * uMat5.z); TEX3 = vec4(n, 0.0);
#elif RSX_K == 2
    TEX1 = vec4(w, c4x, 0.0); TEX2 = vec4(vts, normalFade * uMat5.y); TEX3 = vec4(lts, 0.0);
#elif RSX_K == 3
    TEX1 = vec4(w, c4x, 0.0); TEX2 = vec4(vts, reflFade * uMat5.z); TEX3 = vec4(n, normalFade * uMat5.y);
    TEX4 = vec4(t, 0.0); TEX5 = vec4(b, 0.0);
#elif RSX_K == 4
    TEX1 = vec4(w, c4y, 0.0); TEX2 = vec4(detailUV, detailWeight, 0.0); TEX3 = vec4(n, 0.0);
#elif RSX_K == 5
    TEX1 = vec4(w, c4y, 0.0); TEX2 = vec4(v, reflFade * uMat5.z); TEX3 = vec4(n, detailWeight);
    TEX4 = vec4(detailUV, 0.0, 0.0);
#elif RSX_K == 6
    TEX1 = vec4(w, c4x, 0.0); TEX2 = vec4(vts, normalFade * uMat5.y); TEX3 = vec4(detailUV, detailWeight, 0.0);
    TEX4 = vec4(lts, 0.0);
#else
    TEX1 = vec4(w, detailUV); TEX2 = vec4(vts, reflFade * uMat5.z); TEX3 = vec4(n, normalFade * uMat5.y);
    TEX4 = vec4(t, c4x); TEX5 = vec4(b, detailWeight); TEX6 = vec4(lts, 0.0);
#endif
#elif RSX_ROUTE == 4
    // EBOOT standard programs, prelit TIE: VPs 52 + k. C = the unit-luminance chroma x c[1].rgb, I = the
    // three squared basis intensities, s = the baked sun visibility (bit 0); the flat permutations take
    // C x dot(I, c[4].w), the RNM ones C and I x c[4].z apart.
    float scale = max(il.direction1.w, 1.0e-6);
    vec3 chroma = fPrelightChroma / scale;
    vec3 basis = fPrelightIntensity.xyz * scale;
    float sun = fPrelightIntensity.w;
    vec3 flatLight = chroma * dot(fPrelightIntensity.xyz, vec3(scale / 3.0));
    vec3 vts = vec3(dot(v, t), dot(v, b), dot(v, n));
    vec3 lts = vec3(dot(t, l), dot(b, l), dot(n, l));
    TEX0 = vec4(fUV, fColor.r, fColor.a);
    TEX1 = vec4(0.0); TEX2 = vec4(0.0); TEX3 = vec4(0.0); TEX4 = vec4(0.0); TEX5 = vec4(0.0); TEX6 = vec4(0.0);
#if RSX_K == 0
    TEX1 = vec4(flatLight, 0.0); TEX2 = vec4(n, sun);
#elif RSX_K == 1
    TEX1 = vec4(flatLight, 0.0); TEX2 = vec4(v, reflFade * uMat5.z); TEX3 = vec4(n, sun);
#elif RSX_K == 2
    TEX1 = vec4(chroma, 0.0); TEX2 = vec4(vts, normalFade * uMat5.y); TEX3 = vec4(basis, 0.0); TEX4 = vec4(lts, sun);
#elif RSX_K == 3
    TEX1 = vec4(chroma, sun); TEX2 = vec4(vts, reflFade * uMat5.z); TEX3 = vec4(n, normalFade * uMat5.y);
    TEX4 = vec4(t, 0.0); TEX5 = vec4(b, 0.0); TEX6 = vec4(basis, 0.0);
#elif RSX_K == 4
    TEX1 = vec4(flatLight, 0.0); TEX2 = vec4(detailUV, detailWeight, 0.0); TEX3 = vec4(n, sun);
#elif RSX_K == 5
    TEX1 = vec4(flatLight, sun); TEX2 = vec4(v, reflFade * uMat5.z); TEX3 = vec4(n, detailWeight);
    TEX4 = vec4(detailUV, 0.0, 0.0);
#elif RSX_K == 6
    TEX1 = vec4(chroma, 0.0); TEX2 = vec4(vts, normalFade * uMat5.y); TEX3 = vec4(basis, detailWeight);
    TEX4 = vec4(detailUV, 0.0, 0.0); TEX5 = vec4(lts, sun);
#else
    TEX1 = vec4(chroma, sun); TEX2 = vec4(vts, reflFade * uMat5.z); TEX3 = vec4(n, normalFade * uMat5.y);
    TEX4 = vec4(t, 0.0); TEX5 = vec4(b, 0.0); TEX6 = vec4(basis, 0.0);
    TEX7 = vec4(detailUV, detailWeight, 0.0); TEX8 = vec4(lts, 0.0);
#endif
#elif RSX_ROUTE == 5
    // EBOOT standard programs, probe-lit draws: Mobys (VPs 166 + k: TEX0.w = c[21].z, TEX1 = c[20], the
    // view always) and TIEs with neither lightmap nor prelight (VPs 60 + k: TEX0.w = c[0].x = 0, TEX1 =
    // the vertex light multiplier xxx and weight). Normal-map permutations add T / B in TEX4 / TEX5 and
    // move the detail UV from TEX4 to TEX6.
    bool spec = moby || (RSX_K & 1) != 0;
    bool nrm = (RSX_K & 2) != 0;
    bool det = (RSX_K & 4) != 0;
    TEX0 = vec4(fUV, 0.0, moby ? uMat4.x : 0.0);
    TEX1 = moby ? (uMat7.w > 0.5 ? vec4(uMat7.rgb, 1.0) : vec4(1.0)) : vec4(fColor.rrr, fColor.a);
    TEX2 = spec ? vec4(v, reflFade * uMat5.z) : (nrm ? vec4(v, 0.0) : vec4(0.0));
    TEX3 = vec4(n, nrm ? normalFade * uMat5.y : 0.0);
    TEX4 = vec4(0.0); TEX5 = vec4(0.0); TEX6 = vec4(0.0);
    if (nrm) { TEX4 = vec4(t, 0.0); TEX5 = vec4(b, 0.0); if (det) TEX6 = vec4(detailUV, detailWeight, 0.0); }
    else if (det) TEX4 = vec4(detailUV, detailWeight, 0.0);
#elif RSX_ROUTE == 6 || RSX_ROUTE == 7
    // Tools of Destruction TIE VPs (Docs/reverse/TOD_RENDER_POST_2026-10-09.md section 8). TEX1 / TEX0.zw:
    // the vertex light multiplier |pos.w| / 16384 and alpha, c[4].z = f (placement +0x60), the fog
    // min(w c5.x + c5.y, c5.w); the view vector, c[16]'s fade (TieFrag +0x30) and the detail UV (x +0x58,
    // weight 1 - sat(w c16.z), c16.z = 0.125 on every TieFrag) take the next interpolants by the key's
    // bits - normal map 0x08, reflection 0x04, detail 0x10 - as the VPs of each key do.
    float clipW = 1.0 / gl_FragCoord.w;
    bool todFadeKnown = il.tieShaderUseMeta.x > 0.5 && (il.tieShaderUseMeta.z != 0.0 || il.tieShaderUseMeta.w != 0.0);
    float todFade = todFadeKnown ? clamp(clipW * il.tieShaderUseMeta.z + il.tieShaderUseMeta.w, 0.0, 1.0) : 1.0;
    vec4 todDetail = vec4(fUV * uMat6.x, 1.0 - clamp(clipW * 0.125, 0.0, 1.0), 0.0);
    float todF = il.direction1.w > 0.0 ? il.direction1.w : 1.0;
    float todFog = clamp(min(clipW * uFogScale + uFogBias, uFogAmount), 0.0, 1.0) * uFogEnabled;
    vec3 vts = vec3(dot(v, t), dot(v, b), dot(v, n));
    TEX1 = vec4(0.0); TEX2 = vec4(0.0); TEX3 = vec4(0.0); TEX4 = vec4(0.0); TEX5 = vec4(0.0); TEX6 = vec4(0.0);
#if RSX_ROUTE == 6
    // Baked (VPs 46762dbb0a8d, 0b8c48f58f66, b086f64553b0, 22fd535cf21b, ad142f156628, 55ce56df2332,
    // b8dd9e82d57c, 934bb4caf4ee).
    TEX0 = vec4(fUV, rsxLightmapUV());
    TEX1 = vec4(fColor.r, fColor.a, todF, todFog);
#if (RSX_K & 8) != 0
    TEX2 = vec4(vts, todFade);
#if (RSX_K & 4) != 0
    TEX3 = vec4(n, 1.0); TEX4 = vec4(t, 0.0); TEX5 = vec4(b, 0.0);
#if (RSX_K & 16) != 0
    TEX6 = todDetail;
#endif
#elif (RSX_K & 16) != 0
    TEX3 = todDetail;
#endif
#elif (RSX_K & 4) != 0
    TEX2 = vec4(v, todFade); TEX3 = vec4(n, 1.0);
#if (RSX_K & 16) != 0
    TEX4 = todDetail;
#endif
#elif (RSX_K & 16) != 0
    TEX2 = todDetail;
#endif
#else
    // Prelit (VPs 0e6bff23bfa4, a90074276a83, 55e841dad031, 602fff3914f7, b1f6aa0ebd68, 8d15ff407013,
    // 0265bdc811d6): chroma x c[1] in TEX1, the tangent-space light vector f (2 bx - 1, 2 by - 1, bz) -
    // its .z alone, folded into TEX1, without a normal map.
    vec3 todLight = fPrelightIntensity.xyz;
    TEX0 = vec4(fUV, fColor.r, fColor.a);
#if (RSX_K & 8) != 0
    TEX1 = vec4(fPrelightChroma, todFog);
    TEX2 = vec4(vts, todFade);
#if (RSX_K & 4) != 0
    TEX3 = vec4(n, 1.0); TEX4 = vec4(t, 0.0); TEX5 = vec4(b, 0.0); TEX6 = vec4(todLight, 0.0);
#if (RSX_K & 16) != 0
    TEX7 = todDetail;
#endif
#else
    TEX3 = vec4(todLight, 0.0);
#if (RSX_K & 16) != 0
    TEX4 = todDetail;
#endif
#endif
#else
    TEX1 = vec4(fPrelightChroma * todLight.z, todFog);
#if (RSX_K & 4) != 0
    TEX2 = vec4(v, todFade); TEX3 = vec4(n, 1.0);
#if (RSX_K & 16) != 0
    TEX4 = todDetail;
#endif
#elif (RSX_K & 16) != 0
    TEX2 = todDetail;
#endif
#endif
#endif
#else
    // Probe route: skinned Moby (VP c29b70f7428f: TEX1 = c[20], TEX0.w = c[21].z = +0x60.x) or unbaked
    // TIE (VP 514d2fc8147c: TEX1 = vertex colour x c[20], TEX0.w = VP c[0].x, value OPEN - 0 here).
    TEX0 = vec4(fUV, 0.0, moby ? uMat4.x : 0.0);
    TEX1 = moby ? (uMat7.w > 0.5 ? vec4(uMat7.rgb, 1.0) : vec4(1.0)) : vec4(fColor.rrr, fColor.a);
    TEX2 = vec4(v, reflFade * uMat5.z);
    TEX3 = vec4(n, normalFade * uMat5.y);
    TEX4 = vec4(t, 0.0);
    TEX5 = vec4(b, 0.0);
    TEX6 = vec4(detailUV, detailWeight, 0.0);
#endif

    // Patch groups (Docs/reverse/NEW_ENGINE_SHADER_SECTIONS_EBOOT_2026-09-27.md section 4; values checked
    // against the RPCS3 cache, 07-10-2026).
    float fog = clamp(min((1.0 / gl_FragCoord.w) * uFogScale + uFogBias, uFogAmount), 0.0, 1.0) * uFogEnabled;
    G0 = uNewEngineFog.y > 0.5 ? newEngineFog() : vec4(uFogColour, fog);
    float floorB = uReflOcclusionFullLuminance > 0.0 ? uReflOcclusionFloor : 0.01;
    float fullA = uReflOcclusionFullLuminance > 0.0 ? uReflOcclusionFullLuminance : 0.6;
    float k = (1.0 - floorB) / max(fullA, 0.01);
    G1 = vec4(0.2126 * k, 0.7152 * k, 0.0722 * k, floorB);
    G2 = vec4(1.0, 1000.0, 0.0, 0.0);
    G3 = vec4(uEnvLight0Colour, 0.0);
    G4 = vec4(uEnvDirection0, 0.0);
    G5 = uMat3;
    G6 = uMat4;
#if RSX_ROUTE >= 3
    // The EBOOT programs carry the detail tiling in .z and add the parallax offset x .z to the detail UV.
    G7 = vec4(uMat0.y, uMat0.z, uMat6.x, 0.0);
#else
    G7 = vec4(uMat0.y, uMat0.z, 1.0, uSceneTime);
#endif
    G8 = vec4(1.0, 0.0, 0.0, 0.0);
    G9 = vec4(0.5, 0.5, 0.5, 1.0);
    G10 = vec4(0.0); G11 = vec4(0.0); G12 = vec4(0.0); G13 = vec4(0.0); G14 = vec4(0.0);
    G15 = vec4(mod(uSceneTime, 400.0), 0.0, 0.0, 1.0);
    // Switches (+0x2C..+0x2F of the draw state, 0x006CBE2C): reflection cube, parallax, detail, blended pass.
    bool hasMask = il.tieShaderUse0.w > 0.5;
    int bits = int(uMat6.w + 0.5) & (hasMask ? int(il.tieShaderUse0.w + 0.5) - 1 : 0xFF);
    G16 = vec4((bits & 0x04) != 0 ? 1.0 : 0.0, (bits & 0x01) != 0 ? 1.0 : 0.0,
               ((bits & 0x10) != 0 && uMat6.z > 0.5) ? 1.0 : 0.0, float(RSX_BLENDED));
    // The game's own buffer is 960 x 704; here WPOS x .xy has to land on this view's scene copy.
    G17 = vec4(1.0 / vec2(textureSize(uSceneColor, 0)), 1.0 / 176.0, 1.0 - 1.0 / 176.0);
    G18 = vec4(-0.0648, -0.0003, 16.6667, -16.601);
    G19 = dot(il.gameplay, il.gameplay) > 0.0 ? il.gameplay : vec4(0.0, 0.0, 0.0, 1.0);
    G20 = vec4(0.0);
    G21 = vec4(0.06, 0.06, 0.5, 0.5);
#if RSX_TWO_LAYER
    // The second material's +0x50 and +0x70 (the chooser fills groups 15 / 17 for it, 0x00740354).
    G15 = il.secondTint;
    G17 = il.secondParallax;
#endif
    G22 = vec4(0.0);
    rsxCubeSlot = int(uMat5.x + 0.5);
}
";

    private const string RetailShadeGlsl = @"
void shadeRetail(out vec3 litColor, out float litAlpha) {
    if (uMat1.z < 0.5) {
        vec4 t = texture(uAlbedo, fUV);
        litColor = t.rgb * fColor.rgb;
        litAlpha = t.a;
        return;
    }
    rsxSetup();
    vec4 o = rsxProgram();
    litColor = o.rgb;
    litAlpha = clamp(o.a, 0.0, 1.0);
    // The opaque pass's alpha is the bloom source: the program's own output alpha - not on the old
    // engine, whose world programs draw with alpha blend ZERO / ZERO (Docs/reverse/TOD_RENDER_POST_2026-10-09.md).
#if RSX_ROUTE < 6
    if (uPostParams.x > 0.5) gGlow = litAlpha;
#endif
#if RSX_OPAQUE_PROGRAM
    // The EBOOT's opaque programs leave fog out and write the glow mask (the bloom source) to alpha:
    // fog with the group-0 colour and amount the blended ones use, and take coverage - the cut-out
    // test, the soft-edge pass - from the albedo, as the generic shader does (the game cuts in its
    // depth pre-pass: none of these programs discards). In the opaque pass the atmosphere does it below.
    if (!gBeforeAtmosphere) litColor = mix(litColor, G0.rgb, G0.w);
    vec4 albedoTex = texture(uAlbedo, fUV);
    litAlpha = uMat1.y > 0.5 ? (uMat1.w > 0.5 ? albedoTex.a * fColor.a : fColor.a) : albedoTex.a;
#endif
    if (uMat0.w > 0.0 && litAlpha < uMat0.w) discard;
    // The game's atmosphere pass runs over everything the opaque passes drew, in the programs' linear
    // output space (before the editor re-encodes).
    if (gBeforeAtmosphere && newEngineAtmosphereOn())
        litColor = newEngineAtmosphere(litColor, distance(fWorldPos, uCameraPosition), fWorldPos - uCameraPosition);
    if (uMat2.y > 0.5) litColor = pow(max(litColor, vec3(0.0)), vec3(1.0 / 2.2));
#if RSX_DEBUG == 1
    litColor = rsxTex4(vec4(TEX0.zw, 0.0, 0.0)).rgb;
#elif RSX_DEBUG == 2
    litColor = vec3(TEX4.w / 4.0, TEX3.w, TEX2.w);
#elif RSX_DEBUG == 3
    litColor = rsxTex0(TEX0).rgb;
#elif RSX_DEBUG == 4
    litColor = rsxTex14(vec4(TEX0.zw, 0.0, 0.0)).rgb;
#elif RSX_DEBUG == 5
    litColor = abs(o.rgb) * 16.0;
#elif RSX_DEBUG == 6
    litColor = G16.xyz;
#elif RSX_DEBUG == 8
    { vec4 n1 = rsxTex1(TEX0); InstanceLight il8 = uInstanceLights.values[fInstanceIndex];
      litColor = vec3(dot(n1.xy, n1.xy) * 0.5, (il8.tieShaderUse0.w - 1.0) / 32.0, uMat6.w / 32.0); }
#elif RSX_DEBUG == 9
    litColor = texture(uNormal, TEX0.xy).agb;
#elif RSX_DEBUG == 7
    litColor = vec3(any(isnan(o)) ? 1.0 : 0.0, any(isinf(o)) ? 1.0 : 0.0, 0.0);
#endif
}
";
}

public sealed unsafe partial class VulkanRenderer
{
    /// <summary>Translates one embedded program for one route and compiles its three fragment variants
    /// to SPIR-V, with no device: null when it would draw, else why not. Tests/RendererShaderSmoke.</summary>
    public static string? CheckRetailProgram(Loading.Shaders.NewEngineShaderProgramVariant variant, int route, out string? glsl, int k = 0)
    {
        glsl = null;
        var program = RsxFragmentTranslator.Translate(variant.Microcode, variant.SlotGroups(), variant.Exports32, out string? failure);
        if (program == null)
            return "translation: " + failure;
        foreach (var (generic, blended) in new[] { (FragmentOpaqueGlsl, false), (FragmentAccumGlsl, true), (FragmentAdditiveGlsl, true) })
        {
            glsl = RetailFragmentSource(generic, program, route, k, blended);
            try
            {
                SpirvCompilation.CompileGlslToSpirv(glsl, "vk", ShaderStages.Fragment, new GlslCompileOptions());
            }
            catch (Exception e)
            {
                return "GLSL: " + e.Message;
            }
        }
        return null;
    }
}
