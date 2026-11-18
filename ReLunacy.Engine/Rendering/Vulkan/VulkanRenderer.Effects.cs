// Ported from ReLunacy Resurgence by Fianshoo (GPL-3.0). "Docs/reverse/..." paths cite that repository.
using NeoVeldrid;
using NeoVeldrid.SPIRV;
using ReLunacy.Engine.Assets.Effects;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Effect sprites in the level view: camera-facing textured quads built on the CPU each frame
/// (<see cref="EffectSpriteBatch"/>) and drawn in the resolve pass, after the translucent composite,
/// DEPTH-TESTED against the level but not depth-writing - so a smoke column behind a wall is hidden by
/// it, as in the game. One pipeline per <see cref="EffectSpriteBlend"/>: alpha and additive (the two a QForce
/// FxTexture's blend byte selects, Docs/reverse/NEW_ENGINE_EFFECTS_2026-09-25.md), and Tools of Destruction's
/// subtractive, opaque, multiply and screen (Docs/reverse/TOD_EFFECTS_2026-10-09.md). Drawn after the bloom, with
/// the editor's overlays, as Resurgence does.
///
/// Textures are the level's VFX table, set once per renderer (<see cref="SetEffectTextures"/>); slot -1 is
/// a radial glow owned here. Everything is created lazily, so a renderer that never plays an effect pays
/// nothing.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private const string EffectVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; mat4 uPick; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inUv;
layout(location = 2) in vec4 inColor;
layout(location = 0) out vec2 fUv;
layout(location = 1) out vec4 fColor;
layout(location = 2) out vec3 fWorldPos;
void main() { fUv = inUv; fColor = inColor; fWorldPos = inPos; gl_Position = uMvp * vec4(inPos, 1.0); }";

    // The game's particle programs (the effect table 0x00F357D0 of the QForce EBOOT, entries 0x26..0x4E,
    // e.g. 0x2A / 0x26 - 1,549 / 1,422 pipelines in the RPCS3 cache, with the particle VP 214) take
    // c = sprite x COL0, then c += c L3(TEX6.w) (1 - c) on unit 3 = the 0x34003 table, c = mix(c, TEX0.rgb,
    // TEX0.a), c += TEX5.rgb: the atmosphere pass's three steps with per-vertex inputs. The editor feeds
    // them the atmosphere at the particle (VulkanRenderer.NewEngineFog.cs). Additive sprites only fade by
    // the fog amount (their fog colour taken black: INFERRED). The soft-particle depth fade (unit 15) and
    // the distortion programs (0x41...) are not drawn. The sprite colour is display-encoded: decoded around.
    private const string EffectFragmentGlsl = @"#version 450
" + LightBufferGlsl + NewEngineAtmosphereGlsl + @"
layout(set = 1, binding = 0) uniform sampler2D uSprite;
layout(location = 0) in vec2 fUv;
layout(location = 1) in vec4 fColor;
layout(location = 2) in vec3 fWorldPos;
layout(location = 0) out vec4 outColor;
void main() {
    vec4 c = texture(uSprite, fUv) * fColor;
    // Old engine: the particle programs end with mix(c, fog, fog amount) (0x2EA..0x2EF), the fog the lit
    // shader uses (clip w x scale + bias, capped). Sprites that add light only fade by it.
    float fog = clamp(min(uFogScale / gl_FragCoord.w + uFogBias, uFogAmount), 0.0, 1.0) * uFogEnabled * uOldEngineMode;
#ifdef FX_ADDITIVE
    c.rgb *= 1.0 - fog;
#else
    c.rgb = mix(c.rgb, uFogColour, fog);
#endif
    if (newEngineAtmosphereOn()) {
        vec3 lin = pow(max(c.rgb, vec3(0.0)), vec3(2.2));
        float d = distance(fWorldPos, uCameraPosition);
#ifdef FX_ADDITIVE
        float t = clamp(d * uAtmPhase.w, 0.0, 1.0);
        if (uAtmExtinction.w > 0.5) { vec3 l = lin * newEngineAtmosphereSample(false, t).rgb; lin = lin - lin * l + l; }
        if (uNewEngineFog.y > 0.5) lin *= 1.0 - newEngineAtmosphereSample(true, t).a;
#else
        lin = newEngineAtmosphere(lin, d, fWorldPos - uCameraPosition);
#endif
        c.rgb = pow(max(lin, vec3(0.0)), vec3(1.0 / 2.2));
    }
    outColor = c;
}";

    private bool _fxReady;
    private VkDescriptorSetLayout _fxSetLayout;
    private VkPipelineLayout _fxLayout;
    private readonly VkPipeline[] _fxPipelines = new VkPipeline[6];
    private VkShaderModule _fxVs, _fxFs, _fxFsAdditive;
    private VkDescriptorPool _fxPool;
    private VkDescriptorSet[] _fxSets = [];
    private VkImageView[] _fxViews = [];
    private VkDescriptorSet _fxGlowSet;
    private VkImageView _fxGlowView;
    private Texture? _fxGlow;
    // One vertex buffer per frame slot: the frame the GPU may still be drawing reads its own, so filling this
    // slot's never waits on it (the writes of Frame's uniforms follow the same rule).
    private readonly VkBuffer[] _fxBuffer = new VkBuffer[Frames];
    private readonly VkDeviceMemory[] _fxMemory = new VkDeviceMemory[Frames];
    private readonly void*[] _fxMapped = new void*[Frames];
    private readonly int[] _fxCapacityFloats = new int[Frames];
    private readonly int[] _fxVertexCount = new int[Frames];
    private readonly List<EffectSpriteDraw>[] _fxDraws = [[], []];
    private object? _fxTextureOwner;

    /// <summary>The object whose textures are bound (the caller's VFX set) - lets a caller that outlives
    /// this renderer know whether it still has to send them.</summary>
    public object? EffectTextureOwner => _fxTextureOwner;

    /// <summary>Binds the level's VFX textures, slot = VFX index (null = glow). Replaces any earlier set.</summary>
    public void SetEffectTextures(GraphicsDevice graphicsDevice, IReadOnlyList<Texture?> textures, object owner)
    {
        EnsureEffectPipelines(graphicsDevice);
        WaitForPendingFrames();
        _api.vkDeviceWaitIdle();
        if (_fxPool.Handle != 0) { _api.vkDestroyDescriptorPool(_fxPool); _fxPool = default; }
        foreach (VkImageView view in _fxViews) if (view.Handle != 0) _api.vkDestroyImageView(view);
        if (_fxGlowView.Handle != 0) { _api.vkDestroyImageView(_fxGlowView); _fxGlowView = default; }

        int count = textures.Count;
        var size = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = (uint)(count + 1) };
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = (uint)(count + 1), poolSizeCount = 1, pPoolSizes = &size };
        VkDescriptorPool pool; Check(_api.vkCreateDescriptorPool(&poolInfo, &pool), "vkCreateDescriptorPool(fx)"); _fxPool = pool;

        _fxGlowView = ViewOfEffectTexture(_fxGlow!);
        _fxGlowSet = AllocateEffectSet(_fxGlowView);
        _fxViews = new VkImageView[count];
        _fxSets = new VkDescriptorSet[count];
        for (int i = 0; i < count; i++)
        {
            if (textures[i] is not { } texture) { _fxSets[i] = _fxGlowSet; continue; }
            _fxViews[i] = ViewOfEffectTexture(texture);
            _fxSets[i] = AllocateEffectSet(_fxViews[i]);
        }
        _fxTextureOwner = owner;
    }

    /// <summary>This frame's sprites (world space). An empty batch draws nothing.</summary>
    public void SetEffectSprites(EffectSpriteBatch batch)
    {
        int slot = _slot;
        _fxVertexCount[slot] = 0;
        _fxDraws[slot].Clear();
        if (!_fxReady || batch.VertexCount == 0) return;

        int floats = batch.VertexCount * EffectSpriteBatch.FloatsPerVertex;
        if (floats > _fxCapacityFloats[slot])
        {
            if (_fxBuffer[slot].Handle != 0)
            {
                WaitForPendingFrames();
                _api.vkDestroyBuffer(_fxBuffer[slot]);
                _api.vkFreeMemory(_fxMemory[slot]);
            }
            _fxCapacityFloats[slot] = Math.Max(floats, 64 * 1024);
            ulong bytes = (ulong)(_fxCapacityFloats[slot] * sizeof(float));
            (_fxBuffer[slot], _fxMemory[slot]) = CreateBuffer(bytes, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            void* p; Check(_api.vkMapMemory(_fxMemory[slot], 0, bytes, 0, &p), "vkMapMemory(fx)"); _fxMapped[slot] = p;
        }
        batch.Vertices.CopyTo(new Span<float>(_fxMapped[slot], floats));
        _fxVertexCount[slot] = batch.VertexCount;
        _fxDraws[slot].AddRange(batch.Draws);
    }

    private void DrawEffectSprites()
    {
        int slot = _slot;
        var draws = _fxDraws[slot];
        if (!_fxReady || _fxVertexCount[slot] == 0 || draws.Count == 0) return;
        VkDescriptorSet s0 = _descSet;
        VkBuffer vb = _fxBuffer[slot]; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        EffectSpriteBlend? bound = null;
        foreach (EffectSpriteDraw draw in draws)
        {
            if (bound != draw.Blend)
            {
                _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _fxPipelines[(int)draw.Blend]);
                _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _fxLayout, 0, 1, &s0, 0, null);
                bound = draw.Blend;
            }
            VkDescriptorSet set = (uint)draw.TextureIndex < (uint)_fxSets.Length ? _fxSets[draw.TextureIndex] : _fxGlowSet;
            if (set.Handle == 0) continue;
            _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _fxLayout, 1, 1, &set, 0, null);
            _api.vkCmdDraw(_cmd, (uint)draw.VertexCount, 1, (uint)draw.FirstVertex, 0);
        }
    }

    private VkImageView ViewOfEffectTexture(Texture texture)
    {
        VkImage image = _ctx.BackendInfo.GetVkImage(texture);
        uint mips = Math.Max(1u, texture.MipLevels);
        var vi = new VkImageViewCreateInfo { image = image, viewType = VkImageViewType.Image2D, format = ColorFormat, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Color, baseMipLevel = 0, levelCount = mips, baseArrayLayer = 0, layerCount = 1 } };
        VkImageView view; Check(_api.vkCreateImageView(&vi, &view), "vkCreateImageView(fx)");
        return view;
    }

    private VkDescriptorSet AllocateEffectSet(VkImageView view)
    {
        VkDescriptorSetLayout layout = _fxSetLayout;
        var alloc = new VkDescriptorSetAllocateInfo { descriptorPool = _fxPool, descriptorSetCount = 1, pSetLayouts = &layout };
        VkDescriptorSet set; Check(_api.vkAllocateDescriptorSets(&alloc, &set), "vkAllocateDescriptorSets(fx)");
        var image = new VkDescriptorImageInfo { sampler = _samplerLinear, imageView = view, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var write = new VkWriteDescriptorSet { dstSet = set, dstBinding = 0, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &image };
        _api.vkUpdateDescriptorSets(1, &write, 0, null);
        return set;
    }

    private void EnsureEffectPipelines(GraphicsDevice graphicsDevice)
    {
        if (_fxReady) return;

        var binding = new VkDescriptorSetLayoutBinding { binding = 0, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        var setInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 1, pBindings = &binding };
        VkDescriptorSetLayout setLayout; Check(_api.vkCreateDescriptorSetLayout(&setInfo, &setLayout), "vkCreateDescriptorSetLayout(fx)"); _fxSetLayout = setLayout;

        VkDescriptorSetLayout* sets = stackalloc VkDescriptorSetLayout[2] { _descLayout, _fxSetLayout };
        var layoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 2, pSetLayouts = sets };
        VkPipelineLayout layout; Check(_api.vkCreatePipelineLayout(&layoutInfo, &layout), "vkCreatePipelineLayout(fx)"); _fxLayout = layout;

        _fxVs = Module(EffectVertexGlsl, ShaderStages.Vertex);
        _fxFs = Module(EffectFragmentGlsl, ShaderStages.Fragment);
        _fxFsAdditive = Module(EffectFragmentGlsl.Replace("#version 450", "#version 450\n#define FX_ADDITIVE 1"), ShaderStages.Fragment);

        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        var mask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A;
        VkDynamicState* dyn = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        var dynState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dyn };
        var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
        var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
        var raster = new VkPipelineRasterizationStateCreateInfo { polygonMode = VkPolygonMode.Fill, cullMode = VkCullModeFlags.None, frontFace = VkFrontFace.CounterClockwise, lineWidth = 1f };
        var assembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.GreaterOrEqual };

        var vb = new VkVertexInputBindingDescription { binding = 0, stride = EffectSpriteBatch.FloatsPerVertex * sizeof(float), inputRate = VkVertexInputRate.Vertex };
        VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[3];
        attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        attrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32Sfloat, offset = 12 };
        attrs[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 20 };
        var input = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vb, vertexAttributeDescriptionCount = 3, pVertexAttributeDescriptions = attrs };

        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _fxVs, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _fxFs, pName = entry };

        // Destination alpha is left alone: the level image is displayed opaque. Blends that add light (additive,
        // subtractive, screen) take the additive program, whose fog only fades them.
        for (int k = 0; k < _fxPipelines.Length; k++)
        {
            var kind = (EffectSpriteBlend)k;
            bool additive = kind is EffectSpriteBlend.Additive or EffectSpriteBlend.Subtractive or EffectSpriteBlend.Screen;
            stages[1].module = additive ? _fxFsAdditive : _fxFs;
            (VkBlendFactor src, VkBlendFactor dst, VkBlendOp op) = kind switch
            {
                EffectSpriteBlend.Additive => (VkBlendFactor.SrcAlpha, VkBlendFactor.One, VkBlendOp.Add),
                EffectSpriteBlend.Subtractive => (VkBlendFactor.SrcAlpha, VkBlendFactor.One, VkBlendOp.ReverseSubtract),
                EffectSpriteBlend.Opaque => (VkBlendFactor.One, VkBlendFactor.Zero, VkBlendOp.Add),
                EffectSpriteBlend.Multiply => (VkBlendFactor.Zero, VkBlendFactor.SrcColor, VkBlendOp.Add),
                EffectSpriteBlend.Screen => (VkBlendFactor.One, VkBlendFactor.OneMinusSrcColor, VkBlendOp.Add),
                _ => (VkBlendFactor.SrcAlpha, VkBlendFactor.OneMinusSrcAlpha, VkBlendOp.Add),
            };
            var attach = new VkPipelineColorBlendAttachmentState
            {
                blendEnable = true,
                srcColorBlendFactor = src,
                dstColorBlendFactor = dst,
                colorBlendOp = op,
                srcAlphaBlendFactor = VkBlendFactor.Zero,
                dstAlphaBlendFactor = VkBlendFactor.One,
                alphaBlendOp = VkBlendOp.Add,
                colorWriteMask = mask,
            };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &attach };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &input, pInputAssemblyState = &assembly, pViewportState = &viewportState, pRasterizationState = &raster, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _fxLayout, renderPass = _rpResolve, subpass = 0 };
            VkPipeline pipeline; Check(_api.vkCreateGraphicsPipelines(GetPipelineCache(), 1, &info, &pipeline), $"vkCreateGraphicsPipelines(fx {kind})");
            _fxPipelines[k] = pipeline;
        }

        _fxGlow = graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        graphicsDevice.UpdateTexture(_fxGlow, BuildEffectGlow(64), 0, 0, 0, 64, 64, 1, 0, 0);
        _fxReady = true;
    }

    private static byte[] BuildEffectGlow(int size)
    {
        var data = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
            float a = MathF.Max(0f, 1f - MathF.Sqrt(dx * dx + dy * dy));
            int i = (y * size + x) * 4;
            data[i] = data[i + 1] = data[i + 2] = 255;
            data[i + 3] = (byte)(a * a * 255f);
        }
        return data;
    }

    private void DisposeEffects()
    {
        if (!_fxReady) return;
        for (int i = 0; i < Frames; i++)
            if (_fxBuffer[i].Handle != 0) { _api.vkDestroyBuffer(_fxBuffer[i]); _api.vkFreeMemory(_fxMemory[i]); _fxBuffer[i] = default; _fxCapacityFloats[i] = 0; }
        if (_fxPool.Handle != 0) _api.vkDestroyDescriptorPool(_fxPool);
        foreach (VkImageView view in _fxViews) if (view.Handle != 0) _api.vkDestroyImageView(view);
        if (_fxGlowView.Handle != 0) _api.vkDestroyImageView(_fxGlowView);
        foreach (VkPipeline pipeline in _fxPipelines) if (pipeline.Handle != 0) _api.vkDestroyPipeline(pipeline);
        Array.Clear(_fxPipelines);
        _api.vkDestroyPipelineLayout(_fxLayout);
        _api.vkDestroyDescriptorSetLayout(_fxSetLayout);
        _api.vkDestroyShaderModule(_fxVs);
        _api.vkDestroyShaderModule(_fxFs);
        _api.vkDestroyShaderModule(_fxFsAdditive);
        _fxGlow?.Dispose();
        _fxReady = false;
    }
}
