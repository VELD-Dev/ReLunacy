using System.Numerics;
using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// The new engine's bloom, as the QForce post function runs it (EBOOT <c>0x006BD8C0</c>,
/// Docs/reverse/NEW_ENGINE_POSTPROCESS_2026-10-07.md): the world shaders write a glow mask to alpha;
/// a quarter-size extract scales the colour by (k x a)^e; three levels (1/4, 1/8, 1/16) are blurred by a
/// 3-tap horizontal + vertical kernel, combined (s', 1/3, 2/3 - s'), mixed with last frame's result (h),
/// blurred twice more by a 5-tap Gaussian and added to the scene, x I. k, e, s, h, I come from region.dat
/// 0x34000 (<see cref="Assets.Lighting.BloomParameters"/>). Drawn when the level carries them and the
/// game's shaders are on; the glow mask then lives in the colour target's alpha until the composite
/// writes it back to 1.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private const VkFormat BloomFormat = VkFormat.R8G8B8A8Unorm;
    private VkRenderPass _rpBloom, _rpBloomComposite, _rpResolveKeep;
    private VkDescriptorSetLayout _bloomSetLayout;
    private VkPipelineLayout _bloomLayout;
    private VkDescriptorPool _bloomPool;
    private VkSampler _bloomSampler;
    private VkShaderModule _bloomVs;
    private VkPipeline _bloomExtract, _bloomBlurH, _bloomBlurV, _bloomCombine, _bloomGaussH, _bloomGaussV, _bloomComposite;

    // Levels L0..L2 (1/4, 1/8, 1/16), their blur temporaries, and the result F (two, for the history).
    private readonly BloomTarget[] _bloomLevels = new BloomTarget[3];
    private readonly BloomTarget[] _bloomTemps = new BloomTarget[3];
    private readonly BloomTarget[] _bloomResult = new BloomTarget[2];
    private readonly VkFramebuffer[] _fbBloomComposite = new VkFramebuffer[Frames];
    private bool _bloomTargetsReady;
    private bool _bloomHistoryValid;
    private int _bloomParity;

    private struct BloomTarget
    {
        public VkImage Image;
        public VkDeviceMemory Memory;
        public VkImageView View;
        public VkFramebuffer Framebuffer;
        public uint Width, Height;
    }

    /// <summary>True when this frame draws the bloom (set by Frame, read by the shaders through
    /// LightData.PostParams.x and by RecordCommands).</summary>
    private bool _bloomActive;

    // Debugging: RELUNACY_BLOOM=0 leaves it out.
    private static readonly bool BloomAllowed = Environment.GetEnvironmentVariable("RELUNACY_BLOOM") != "0";

    private bool BloomWanted =>
        BloomAllowed && UseRetailEmbeddedShaders && _bloomTargetsReady && _oldLightingEnvironment?.Bloom is { Intensity: > 0f, ExtractScale: > 0f };

    /// <summary>The bloom is on and its glow mask is the old engine's destination alpha, which only the Sky
    /// writes: the Dome its colour's alpha, the Color shells and Clouds attenuate it, the Bloom shells add
    /// to it (VulkanRenderer.Sky.cs). The opaque world leaves 0 there.</summary>
    private bool DestinationAlphaGlow => _bloomActive && _oldLightingEnvironment?.Bloom is { DestinationAlpha: true };

    private const string BloomVertexGlsl = @"#version 450
void main() {
    vec2 q = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    gl_Position = vec4(q * 2.0 - 1.0, 0.0, 1.0);
}";

    private const string BloomHead = @"#version 450
layout(set = 0, binding = 0) uniform sampler2D s0;
layout(set = 0, binding = 1) uniform sampler2D s1;
layout(set = 0, binding = 2) uniform sampler2D s2;
layout(set = 0, binding = 3) uniform sampler2D s3;
layout(push_constant) uniform P { vec4 a; vec4 b; } p;
layout(location = 0) out vec4 o;
";

    // Entry 0xA4: a 16-texel box from four bilinear taps, scaled by (k * glow)^e (alpha included).
    // a.xy = 1 / source size; b.xy = (k, e), b.z = 0: ToD's alpha-only extract (post table 0x100C3660
    // entry 95, picked by main.dat 0x7100 +0x8C = 0) outputs (k * glow)^e alone, a white glow.
    private const string BloomExtractGlsl = BloomHead + @"
void main() {
    vec2 t = gl_FragCoord.xy * 4.0;
    vec4 c = (texture(s0, t * p.a.xy) + texture(s0, (t - vec2(0.0, 2.0)) * p.a.xy)
            + texture(s0, (t - vec2(2.0, 0.0)) * p.a.xy) + texture(s0, (t - vec2(2.0)) * p.a.xy)) * 0.25;
    float m = p.b.x * c.a;
    float g = m > 0.0 ? exp2(log2(m) * p.b.y) : 0.0;
    o = p.b.z > 0.5 ? c * g : vec4(g);
}";

    // Entries 0xA5 / 0xA6: three bilinear taps (+0.196, -1.175, +2 texels; 1.24414, 0.295654, 0.0516968),
    // a.xy = 1 / source size, a.zw = source / destination size (2 on the downsampling passes).
    private const string BloomBlurGlsl = BloomHead + @"
void main() {
    vec2 t = gl_FragCoord.xy * p.a.zw;
    vec2 d = p.b.xy;
    o = texture(s0, (t + d * 0.196037) * p.a.xy) * 1.24414
      + texture(s0, (t - d * 1.17493) * p.a.xy) * 0.295654
      + texture(s0, (t + d * 2.0) * p.a.xy) * 0.0516968;
}";

    // Entry 0xA7: L0 s' + L1 / 3 + L2 (2/3 - s'), mixed with last frame's result by h, minus 1/255.
    // a.xy = 1 / size; b = (s', 1/3, 2/3 - s', h).
    private const string BloomCombineGlsl = BloomHead + @"
void main() {
    vec2 uv = gl_FragCoord.xy * p.a.xy;
    vec4 c = texture(s0, uv) * p.b.x + texture(s1, uv) * p.b.y + texture(s2, uv) * p.b.z;
    c = mix(c, texture(s3, uv), p.b.w);
    o = c - 1.0 / 255.0;
}";

    // Entries 0x7A / 0x7B: the 5-tap Gaussian (0.408925, 0.243839, 0.051699). a.xy = 1 / size, b.xy = axis.
    private const string BloomGaussGlsl = BloomHead + @"
void main() {
    vec2 uv = gl_FragCoord.xy * p.a.xy;
    vec2 d = p.b.xy * p.a.xy;
    o = texture(s0, uv) * 0.408925 + (texture(s0, uv - d) + texture(s0, uv + d)) * 0.243839
      + (texture(s0, uv - 2.0 * d) + texture(s0, uv + 2.0 * d)) * 0.051699;
}";

    // Entry 0x13 with SRC_ALPHA / ONE: scene += I x upsampled result. Alpha goes back to 1 (the viewport
    // composites with it). a.xy = 1 / scene size, b.x = I.
    private const string BloomCompositeGlsl = BloomHead + @"
void main() {
    o = vec4(texture(s0, gl_FragCoord.xy * p.a.xy).rgb * p.b.x, 1.0);
}";

    private void CreateBloomPipelines()
    {
        _bloomSampler = CreateClampSampler(VkFilter.Linear, VkSamplerMipmapMode.Nearest);

        VkAttachmentDescription att = new() { format = BloomFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.DontCare, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = VkImageLayout.ShaderReadOnlyOptimal };
        _rpBloom = MakeColorPass(att);
        VkAttachmentDescription sceneAtt = new() { format = ColorFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Load, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.ShaderReadOnlyOptimal, finalLayout = VkImageLayout.ColorAttachmentOptimal };
        _rpBloomComposite = MakeColorPass(sceneAtt);
        _rpResolveKeep = MakeResolveKeepPass();

        VkDescriptorSetLayoutBinding* b = stackalloc VkDescriptorSetLayoutBinding[4];
        for (uint i = 0; i < 4; i++)
            b[i] = new VkDescriptorSetLayoutBinding { binding = i, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        var setInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 4, pBindings = b };
        VkDescriptorSetLayout sl; Check(_api.vkCreateDescriptorSetLayout(&setInfo, &sl), "vkCreateDescriptorSetLayout(bloom)"); _bloomSetLayout = sl;
        var range = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Fragment, offset = 0, size = 32 };
        VkDescriptorSetLayout l = _bloomSetLayout;
        var layoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &l, pushConstantRangeCount = 1, pPushConstantRanges = &range };
        VkPipelineLayout pl; Check(_api.vkCreatePipelineLayout(&layoutInfo, &pl), "vkCreatePipelineLayout(bloom)"); _bloomLayout = pl;

        _bloomVs = Module(BloomVertexGlsl, ShaderStages.Vertex);
        _bloomExtract = BloomPipeline(BloomExtractGlsl, _rpBloom, additive: false);
        _bloomBlurH = BloomPipeline(BloomBlurGlsl, _rpBloom, additive: false);
        _bloomBlurV = _bloomBlurH;
        _bloomCombine = BloomPipeline(BloomCombineGlsl, _rpBloom, additive: false);
        _bloomGaussH = BloomPipeline(BloomGaussGlsl, _rpBloom, additive: false);
        _bloomGaussV = _bloomGaussH;
        _bloomComposite = BloomPipeline(BloomCompositeGlsl, _rpBloomComposite, additive: true);
    }

    private VkRenderPass MakeColorPass(VkAttachmentDescription attachment)
    {
        var colorRef = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        var subpass = new VkSubpassDescription { pipelineBindPoint = VkPipelineBindPoint.Graphics, colorAttachmentCount = 1, pColorAttachments = &colorRef };
        VkSubpassDependency* deps = stackalloc VkSubpassDependency[2];
        deps[0] = new VkSubpassDependency { srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, dstSubpass = 0, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.FragmentShader, srcAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.ShaderRead, dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput, dstAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.ColorAttachmentRead };
        deps[1] = new VkSubpassDependency { srcSubpass = 0, dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput, srcAccessMask = VkAccessFlags.ColorAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ColorAttachmentOutput, dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite };
        var info = new VkRenderPassCreateInfo { attachmentCount = 1, pAttachments = &attachment, subpassCount = 1, pSubpasses = &subpass, dependencyCount = 2, pDependencies = deps };
        VkRenderPass rp; Check(_api.vkCreateRenderPass(&info, &rp), "vkCreateRenderPass(bloom)"); return rp;
    }

    /// <summary>The resolve pass that keeps depth and stencil for the overlay pass after the bloom (the
    /// plain one discards them); compatible with <see cref="_rpResolve"/>'s framebuffers.</summary>
    private VkRenderPass MakeResolveKeepPass()
    {
        VkAttachmentDescription* a = stackalloc VkAttachmentDescription[2];
        a[0] = new VkAttachmentDescription { format = ColorFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Load, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.ColorAttachmentOptimal, finalLayout = VkImageLayout.ShaderReadOnlyOptimal };
        a[1] = new VkAttachmentDescription { format = DepthFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Load, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.Load, stencilStoreOp = VkAttachmentStoreOp.Store, initialLayout = VkImageLayout.DepthStencilAttachmentOptimal, finalLayout = VkImageLayout.DepthStencilAttachmentOptimal };
        var cRef = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        var dRef = new VkAttachmentReference { attachment = 1, layout = VkImageLayout.DepthStencilAttachmentOptimal };
        var sub = new VkSubpassDescription { pipelineBindPoint = VkPipelineBindPoint.Graphics, colorAttachmentCount = 1, pColorAttachments = &cRef, pDepthStencilAttachment = &dRef };
        VkSubpassDependency* deps = stackalloc VkSubpassDependency[2];
        deps[0] = new VkSubpassDependency { srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, dstSubpass = 0, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.LateFragmentTests, srcAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.EarlyFragmentTests, dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentRead | VkAccessFlags.DepthStencilAttachmentWrite };
        deps[1] = new VkSubpassDependency { srcSubpass = 0, dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.LateFragmentTests, srcAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.EarlyFragmentTests, dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.DepthStencilAttachmentRead };
        var info = new VkRenderPassCreateInfo { attachmentCount = 2, pAttachments = a, subpassCount = 1, pSubpasses = &sub, dependencyCount = 2, pDependencies = deps };
        VkRenderPass rp; Check(_api.vkCreateRenderPass(&info, &rp), "vkCreateRenderPass(resolve keep)"); return rp;
    }

    private VkPipeline BloomPipeline(string fragmentGlsl, VkRenderPass pass, bool additive)
    {
        VkShaderModule fs = Module(fragmentGlsl, ShaderStages.Fragment);
        try
        {
            var entry = "main"u8;
            fixed (byte* name = entry)
            {
                VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
                stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _bloomVs, pName = name };
                stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = fs, pName = name };
                var vin = new VkPipelineVertexInputStateCreateInfo();
                var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
                var vp = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
                var raster = new VkPipelineRasterizationStateCreateInfo { polygonMode = VkPolygonMode.Fill, cullMode = VkCullModeFlags.None, frontFace = VkFrontFace.CounterClockwise, lineWidth = 1f };
                var ms = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
                var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = false, depthWriteEnable = false, depthCompareOp = VkCompareOp.Always };
                var mask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A;
                var blendAttach = additive
                    ? new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.One, dstColorBlendFactor = VkBlendFactor.One, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.One, dstAlphaBlendFactor = VkBlendFactor.Zero, alphaBlendOp = VkBlendOp.Add, colorWriteMask = mask }
                    : new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
                var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
                VkDynamicState* dyn = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
                var dynState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dyn };
                var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = &vp, pRasterizationState = &raster, pMultisampleState = &ms, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _bloomLayout, renderPass = pass, subpass = 0 };
                VkPipeline p; Check(_api.vkCreateGraphicsPipelines(default, 1, &info, &p), "vkCreateGraphicsPipelines(bloom)");
                return p;
            }
        }
        finally
        {
            _api.vkDestroyShaderModule(fs);
        }
    }

    /// <summary>Size-dependent: the levels, temporaries, results and every descriptor set reading them.
    /// Called by CreateTargets (also on resize).</summary>
    private void CreateBloomTargets()
    {
        if (_bloomLayout.Handle == 0) return;
        uint w0 = Math.Max(_width / 4, 1u), h0 = Math.Max(_height / 4, 1u);
        for (int i = 0; i < 3; i++)
        {
            uint w = Math.Max(w0 >> i, 1u), h = Math.Max(h0 >> i, 1u);
            _bloomLevels[i] = MakeBloomTarget(w, h, _rpBloom);
            _bloomTemps[i] = MakeBloomTarget(w, h, _rpBloom);
        }
        for (int i = 0; i < 2; i++)
            _bloomResult[i] = MakeBloomTarget(w0, h0, _rpBloom);
        for (int f = 0; f < Frames; f++)
        {
            VkImageView v = _colorView[f];
            var fbInfo = new VkFramebufferCreateInfo { renderPass = _rpBloomComposite, attachmentCount = 1, pAttachments = &v, width = _width, height = _height, layers = 1 };
            VkFramebuffer fb; Check(_api.vkCreateFramebuffer(&fbInfo, &fb), "vkCreateFramebuffer(bloom composite)"); _fbBloomComposite[f] = fb;
        }

        // One set per (pass, frame slot or result parity), allocated fresh with the targets.
        var sizes = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = 4 * 32 };
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = 32, poolSizeCount = 1, pPoolSizes = &sizes };
        VkDescriptorPool pool; Check(_api.vkCreateDescriptorPool(&poolInfo, &pool), "vkCreateDescriptorPool(bloom)"); _bloomPool = pool;
        _bloomTargetsReady = true;
        _bloomHistoryValid = false;
    }

    private BloomTarget MakeBloomTarget(uint width, uint height, VkRenderPass pass)
    {
        var t = new BloomTarget { Width = width, Height = height };
        var info = new VkImageCreateInfo { imageType = VkImageType.Image2D, format = BloomFormat, extent = new VkExtent3D { width = width, height = height, depth = 1 }, mipLevels = 1, arrayLayers = 1, samples = VkSampleCountFlags.Count1, tiling = VkImageTiling.Optimal, usage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, sharingMode = VkSharingMode.Exclusive, initialLayout = VkImageLayout.Undefined };
        VkImage image; Check(_api.vkCreateImage(&info, &image), "vkCreateImage(bloom)"); t.Image = image;
        VkMemoryRequirements reqs; _api.vkGetImageMemoryRequirements(image, &reqs);
        t.Memory = Allocate(reqs, VkMemoryPropertyFlags.DeviceLocal);
        Check(_api.vkBindImageMemory(image, t.Memory, 0), "vkBindImageMemory(bloom)");
        var viewInfo = new VkImageViewCreateInfo { image = image, viewType = VkImageViewType.Image2D, format = BloomFormat, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Color, baseMipLevel = 0, levelCount = 1, baseArrayLayer = 0, layerCount = 1 } };
        VkImageView view; Check(_api.vkCreateImageView(&viewInfo, &view), "vkCreateImageView(bloom)"); t.View = view;
        VkImageView v = view;
        var fbInfo = new VkFramebufferCreateInfo { renderPass = pass, attachmentCount = 1, pAttachments = &v, width = width, height = height, layers = 1 };
        VkFramebuffer fb; Check(_api.vkCreateFramebuffer(&fbInfo, &fb), "vkCreateFramebuffer(bloom)"); t.Framebuffer = fb;
        return t;
    }

    private void DestroyBloomTargets()
    {
        if (!_bloomTargetsReady) return;
        foreach (BloomTarget[] group in new[] { _bloomLevels, _bloomTemps, _bloomResult })
            for (int i = 0; i < group.Length; i++)
            {
                _api.vkDestroyFramebuffer(group[i].Framebuffer);
                _api.vkDestroyImageView(group[i].View);
                _api.vkDestroyImage(group[i].Image);
                _api.vkFreeMemory(group[i].Memory);
                group[i] = default;
            }
        for (int f = 0; f < Frames; f++)
            _api.vkDestroyFramebuffer(_fbBloomComposite[f]);
        _api.vkDestroyDescriptorPool(_bloomPool);
        _bloomSets.Clear();
        _bloomTargetsReady = false;
    }

    private void DestroyBloomPipelines()
    {
        if (_bloomLayout.Handle == 0) return;
        foreach (VkPipeline p in new[] { _bloomExtract, _bloomBlurH, _bloomCombine, _bloomGaussH, _bloomComposite })
            _api.vkDestroyPipeline(p);
        _api.vkDestroyShaderModule(_bloomVs);
        _api.vkDestroyPipelineLayout(_bloomLayout);
        _api.vkDestroyDescriptorSetLayout(_bloomSetLayout);
        _api.vkDestroyRenderPass(_rpBloom);
        _api.vkDestroyRenderPass(_rpBloomComposite);
        _api.vkDestroyRenderPass(_rpResolveKeep);
        _api.vkDestroySampler(_bloomSampler);
    }

    // Descriptor sets keyed by the views they read (four bindings; unused ones repeat binding 0).
    private readonly Dictionary<(ulong, ulong, ulong, ulong), VkDescriptorSet> _bloomSets = [];

    private VkDescriptorSet BloomSet(VkImageView v0, VkImageView? v1 = null, VkImageView? v2 = null, VkImageView? v3 = null)
    {
        VkImageView a = v1 ?? v0, b = v2 ?? v0, c = v3 ?? v0;
        var key = (v0.Handle, a.Handle, b.Handle, c.Handle);
        if (_bloomSets.TryGetValue(key, out VkDescriptorSet existing)) return existing;
        VkDescriptorSetLayout l = _bloomSetLayout;
        var alloc = new VkDescriptorSetAllocateInfo { descriptorPool = _bloomPool, descriptorSetCount = 1, pSetLayouts = &l };
        VkDescriptorSet set; Check(_api.vkAllocateDescriptorSets(&alloc, &set), "vkAllocateDescriptorSets(bloom)");
        VkDescriptorImageInfo* infos = stackalloc VkDescriptorImageInfo[4];
        VkWriteDescriptorSet* w = stackalloc VkWriteDescriptorSet[4];
        VkImageView* views = stackalloc VkImageView[4] { v0, a, b, c };
        for (uint i = 0; i < 4; i++)
        {
            infos[i] = new VkDescriptorImageInfo { sampler = _bloomSampler, imageView = views[i], imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
            w[i] = new VkWriteDescriptorSet { dstSet = set, dstBinding = i, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &infos[i] };
        }
        _api.vkUpdateDescriptorSets(4, w, 0, null);
        _bloomSets[key] = set;
        return set;
    }

    private void BloomPass(VkPipeline pipeline, VkRenderPass pass, VkFramebuffer framebuffer, uint width, uint height,
        VkDescriptorSet set, Vector4 a, Vector4 b)
    {
        var area = new VkRect2D { offset = default, extent = new VkExtent2D { width = width, height = height } };
        var begin = new VkRenderPassBeginInfo { renderPass = pass, framebuffer = framebuffer, renderArea = area, clearValueCount = 0, pClearValues = null };
        _api.vkCmdBeginRenderPass(_cmd, &begin, VkSubpassContents.Inline);
        var viewport = new VkViewport { x = 0, y = 0, width = width, height = height, minDepth = 0, maxDepth = 1 };
        _api.vkCmdSetViewport(_cmd, 0, 1, &viewport);
        _api.vkCmdSetScissor(_cmd, 0, 1, &area);
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, pipeline);
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _bloomLayout, 0, 1, &set, 0, null);
        Vector4* pc = stackalloc Vector4[2] { a, b };
        _api.vkCmdPushConstants(_cmd, _bloomLayout, VkShaderStageFlags.Fragment, 0, 32, pc);
        _api.vkCmdDraw(_cmd, 3, 1, 0, 0);
        _api.vkCmdEndRenderPass(_cmd);
    }

    /// <summary>The bloom chain on the slot's colour target (ShaderReadOnly on entry, ColorAttachment on
    /// exit, the glow alpha replaced by 1).</summary>
    private void RecordBloom()
    {
        var p = _oldLightingEnvironment!.Bloom!.Value;
        static Vector4 Inv(uint w, uint h) => new(1f / w, 1f / h, 0f, 0f);
        int cur = _bloomParity, prev = _bloomParity ^ 1;

        // Extract + 4x downsample into L0.
        ref BloomTarget l0 = ref _bloomLevels[0];
        BloomPass(_bloomExtract, _rpBloom, l0.Framebuffer, l0.Width, l0.Height, BloomSet(_colorView[_slot]),
            Inv(_width, _height), new Vector4(p.ExtractScale, p.ExtractExponent, p.ColourExtract ? 1f : 0f, 0f));

        // Per level: horizontal from the level above (2x down for 1, 2), vertical back into the level.
        for (int i = 0; i < 3; i++)
        {
            ref BloomTarget src = ref _bloomLevels[Math.Max(i - 1, 0)];
            ref BloomTarget tmp = ref _bloomTemps[i];
            ref BloomTarget dst = ref _bloomLevels[i];
            BloomPass(_bloomBlurH, _rpBloom, tmp.Framebuffer, tmp.Width, tmp.Height, BloomSet(src.View),
                new Vector4(1f / src.Width, 1f / src.Height, (float)src.Width / tmp.Width, (float)src.Height / tmp.Height),
                new Vector4(1f, 0f, 0f, 0f));
            BloomPass(_bloomBlurV, _rpBloom, dst.Framebuffer, dst.Width, dst.Height, BloomSet(tmp.View),
                new Vector4(1f / tmp.Width, 1f / tmp.Height, 1f, 1f), new Vector4(0f, 1f, 0f, 0f));
        }

        // Combine the levels (and last frame's result) into F.
        float s = Math.Clamp(p.MipWeight, 0.01f, 0.65f);
        float h = _bloomHistoryValid ? p.HistoryBlend : 0f;
        ref BloomTarget f = ref _bloomResult[cur];
        BloomPass(_bloomCombine, _rpBloom, f.Framebuffer, f.Width, f.Height,
            BloomSet(_bloomLevels[0].View, _bloomLevels[1].View, _bloomLevels[2].View, _bloomHistoryValid ? _bloomResult[prev].View : null),
            Inv(f.Width, f.Height), new Vector4(s, 1f / 3f, 2f / 3f - s, h));

        // Two Gaussian rounds, through L0's surface.
        for (int round = 0; round < 2; round++)
        {
            BloomPass(_bloomGaussH, _rpBloom, l0.Framebuffer, l0.Width, l0.Height, BloomSet(f.View),
                Inv(f.Width, f.Height), new Vector4(1f, 0f, 0f, 0f));
            BloomPass(_bloomGaussV, _rpBloom, f.Framebuffer, f.Width, f.Height, BloomSet(l0.View),
                Inv(l0.Width, l0.Height), new Vector4(0f, 1f, 0f, 0f));
        }

        // scene.rgb += I x F, alpha = 1.
        BloomPass(_bloomComposite, _rpBloomComposite, _fbBloomComposite[_slot], _width, _height, BloomSet(f.View),
            Inv(_width, _height), new Vector4(p.Intensity, 0f, 0f, 0f));

        _bloomHistoryValid = true;
        _bloomParity ^= 1;
        SetFullViewport();
    }
}
