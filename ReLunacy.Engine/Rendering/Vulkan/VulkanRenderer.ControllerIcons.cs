using System.Numerics;
using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// The controllers that have an icon (Rendering.ControllerIcons), each drawn as a unit quad facing the camera
/// with its icon from one array texture: premultiplied alpha, blended, the clear background discarded so it
/// writes no depth, tinted when selected. Pickable like the volumes, the whole quad through the edge pick
/// pipeline (positions only).
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    /// <summary>The icons to draw this frame: world matrix of the unit quad (its XY plane faces the camera), icon
    /// layer, tint (w = how much of it), pick id.</summary>
    public IReadOnlyList<(Matrix4x4 World, int Icon, Vector4 Tint, uint PickId)> ControllerIconMarkers { get; set; } = [];

    /// <summary>The largest icon size uploaded; bigger files are box-filtered down to it.</summary>
    private const int ControllerIconMaxSize = 256;

    private const string ControllerIconVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { mat4 uModel; vec4 uTint; vec4 uLayer; };
layout(location = 0) in vec3 inPos;
layout(location = 0) out vec2 fUV;
void main() {
    fUV = vec2(inPos.x + 0.5, 0.5 - inPos.y);
    gl_Position = uMvp * (uModel * vec4(inPos, 1.0));
}";
    private const string ControllerIconFragmentGlsl = @"#version 450
layout(set = 1, binding = 0) uniform sampler2DArray uIcons;
layout(push_constant) uniform PC { mat4 uModel; vec4 uTint; vec4 uLayer; };
layout(location = 0) in vec2 fUV;
layout(location = 0) out vec4 o;
void main() {
    vec4 c = texture(uIcons, vec3(fUV, uLayer.x));
    if (c.a < 0.25) discard;
    o = vec4(mix(c.rgb, uTint.rgb * c.a, uTint.w), c.a);
}";

    private Texture? _iconTexture;
    private VkImageView _iconView;
    private VkDescriptorSetLayout _iconSetLayout;
    private VkDescriptorPool _iconPool;
    private VkDescriptorSet _iconSet;
    private VkPipelineLayout _iconLayout;
    private VkShaderModule _iconVs, _iconFs;
    private VkPipeline _iconPipeline;
    private VkBuffer _iconQuadBuffer, _iconQuadIndexBuffer;
    private VkDeviceMemory _iconQuadMemory, _iconQuadIndexMemory;

    /// <summary>The icons as one array texture, a layer each, with premultiplied mips; null when there are none.</summary>
    private static Texture? CreateControllerIconTexture(GraphicsDevice gd)
    {
        var images = ControllerIcons.Images;
        if (images == null || images.Length == 0 || images.All(i => i == null)) return null;

        // Every layer gets the same size: the largest icon's, capped, square.
        int size = 1;
        foreach (var image in images)
            if (image != null) size = Math.Max(size, Math.Max(image.Width, image.Height));
        size = Math.Min((int)BitOperations.RoundUpToPowerOf2((uint)size), ControllerIconMaxSize);
        uint mips = (uint)BitOperations.Log2((uint)size) + 1;
        // Every scene builds a renderer; the mips are filtered once.
        if (!ReferenceEquals(_iconLevelsSource, images))
        {
            var levels = new byte[images.Length][][];
            for (int layer = 0; layer < images.Length; layer++)
            {
                levels[layer] = new byte[mips][];
                float[] level = Premultiplied(images[layer], size);
                for (int mip = 0, s = size; mip < mips; mip++, s = Math.Max(1, s / 2))
                {
                    if (mip > 0) level = HalfSize(level, s * 2);
                    var bytes = levels[layer][mip] = new byte[level.Length];
                    for (int i = 0; i < level.Length; i++) bytes[i] = (byte)Math.Clamp(level[i] * 255f + 0.5f, 0f, 255f);
                }
            }
            (_iconLevels, _iconLevelsSource) = (levels, images);
        }
        var tex = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)size, (uint)size, mips, (uint)images.Length, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        for (int layer = 0; layer < images.Length; layer++)
            for (uint mip = 0, s = (uint)size; mip < mips; mip++, s = Math.Max(1u, s / 2))
                gd.UpdateTexture(tex, _iconLevels![layer][mip], 0, 0, 0, s, s, 1, mip, (uint)layer);
        return tex;
    }

    private static byte[][][]? _iconLevels;
    private static object? _iconLevelsSource;

    /// <summary>An icon at size x size, alpha-premultiplied, as floats; clear when missing. A square source a
    /// power of two larger is box-filtered down, anything else resampled nearest.</summary>
    private static float[] Premultiplied(Resources.Image? image, int size)
    {
        if (image == null) return new float[size * size * 4];
        int w = image.Width;
        bool halvable = image.Height == w && w > size && w % size == 0 && BitOperations.IsPow2(w / size);
        int sampled = halvable ? w : size;
        var o = new float[sampled * sampled * 4];
        for (int y = 0; y < sampled; y++)
            for (int x = 0; x < sampled; x++)
            {
                int sx = x * image.Width / sampled, sy = y * image.Height / sampled;
                int s = (sy * image.Width + sx) * 4, d = (y * sampled + x) * 4;
                float a = image.Data[s + 3] / 255f;
                o[d] = image.Data[s] / 255f * a; o[d + 1] = image.Data[s + 1] / 255f * a; o[d + 2] = image.Data[s + 2] / 255f * a; o[d + 3] = a;
            }
        for (int s = sampled; s > size; s /= 2) o = HalfSize(o, s);
        return o;
    }

    /// <summary>A 2x2 box filter: size x size down to half.</summary>
    private static float[] HalfSize(float[] src, int size)
    {
        int half = Math.Max(1, size / 2);
        var o = new float[half * half * 4];
        for (int y = 0; y < half; y++)
            for (int x = 0; x < half; x++)
                for (int c = 0; c < 4; c++)
                {
                    int x1 = Math.Min(x * 2 + 1, size - 1), y1 = Math.Min(y * 2 + 1, size - 1);
                    o[(y * half + x) * 4 + c] = 0.25f * (src[(y * 2 * size + x * 2) * 4 + c] + src[(y * 2 * size + x1) * 4 + c]
                                                       + src[(y1 * size + x * 2) * 4 + c] + src[(y1 * size + x1) * 4 + c]);
                }
        return o;
    }

    private void CreateControllerIconPipeline(VkPipelineCache pipelineCache, byte* entry, VkPipelineViewportStateCreateInfo* viewportState,
        VkPipelineRasterizationStateCreateInfo* rasterCullNone, VkPipelineMultisampleStateCreateInfo* multisample,
        VkPipelineDynamicStateCreateInfo* dynState)
    {
        if (_iconTexture == null) return;

        Vector3[] quad = [new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f)];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        (_iconQuadBuffer, _iconQuadMemory) = Upload(quad.AsSpan(), VkBufferUsageFlags.VertexBuffer);
        (_iconQuadIndexBuffer, _iconQuadIndexMemory) = Upload(indices.AsSpan(), VkBufferUsageFlags.IndexBuffer);

        VkImage image = _ctx.BackendInfo.GetVkImage(_iconTexture);
        var viewInfo = new VkImageViewCreateInfo { image = image, viewType = VkImageViewType.Image2DArray, format = ColorFormat, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Color, baseMipLevel = 0, levelCount = _iconTexture.MipLevels, baseArrayLayer = 0, layerCount = _iconTexture.ArrayLayers } };
        VkImageView view; Check(_api.vkCreateImageView(&viewInfo, &view), "vkCreateImageView(icons)"); _iconView = view;

        var binding = new VkDescriptorSetLayoutBinding { binding = 0, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        var setInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 1, pBindings = &binding };
        VkDescriptorSetLayout sl; Check(_api.vkCreateDescriptorSetLayout(&setInfo, &sl), "vkCreateDescriptorSetLayout(icons)"); _iconSetLayout = sl;
        var poolSize = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = 1 };
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = 1, poolSizeCount = 1, pPoolSizes = &poolSize };
        VkDescriptorPool pool; Check(_api.vkCreateDescriptorPool(&poolInfo, &pool), "vkCreateDescriptorPool(icons)"); _iconPool = pool;
        var alloc = new VkDescriptorSetAllocateInfo { descriptorPool = _iconPool, descriptorSetCount = 1, pSetLayouts = &sl };
        VkDescriptorSet set; Check(_api.vkAllocateDescriptorSets(&alloc, &set), "vkAllocateDescriptorSets(icons)"); _iconSet = set;
        // Linear and clamped whatever the texture-filtering choice: a point-sampled icon shimmers.
        var imageInfo = new VkDescriptorImageInfo { sampler = _samplerBaked, imageView = _iconView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var write = new VkWriteDescriptorSet { dstSet = _iconSet, dstBinding = 0, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &imageInfo };
        _api.vkUpdateDescriptorSets(1, &write, 0, null);

        VkDescriptorSetLayout* sets = stackalloc VkDescriptorSetLayout[2] { _descLayout, _iconSetLayout };
        var push = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, offset = 0, size = 96 };
        var layoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 2, pSetLayouts = sets, pushConstantRangeCount = 1, pPushConstantRanges = &push };
        VkPipelineLayout layout; Check(_api.vkCreatePipelineLayout(&layoutInfo, &layout), "vkCreatePipelineLayout(icons)"); _iconLayout = layout;

        _iconVs = Module(ControllerIconVertexGlsl, ShaderStages.Vertex);
        _iconFs = Module(ControllerIconFragmentGlsl, ShaderStages.Fragment);
        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _iconVs, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _iconFs, pName = entry };
        var vbind = new VkVertexInputBindingDescription { binding = 0, stride = 3 * sizeof(float), inputRate = VkVertexInputRate.Vertex };
        var vattr = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        var vin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbind, vertexAttributeDescriptionCount = 1, pVertexAttributeDescriptions = &vattr };
        var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.GreaterOrEqual };
        // Premultiplied over the scene, RGB only: the opaque pass's alpha is the bloom's glow mask.
        var blendAttach = new VkPipelineColorBlendAttachmentState
        {
            blendEnable = true,
            srcColorBlendFactor = VkBlendFactor.One, dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha, colorBlendOp = VkBlendOp.Add,
            srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add,
            colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B,
        };
        var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
        var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = viewportState, pRasterizationState = rasterCullNone, pMultisampleState = multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = dynState, layout = _iconLayout, renderPass = _rpOpaque, subpass = 0 };
        VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(icons)"); _iconPipeline = p;
    }

    private void DrawControllerIcons()
    {
        var icons = ControllerIconMarkers;
        if (icons.Count == 0 || _iconPipeline.Handle == 0) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _iconPipeline);
        VkDescriptorSet* sets = stackalloc VkDescriptorSet[2] { _descSet, _iconSet };
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _iconLayout, 0, 2, sets, 0, null);
        VkBuffer vb = _iconQuadBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _iconQuadIndexBuffer, 0, VkIndexType.Uint32);
        byte* pc = stackalloc byte[96];
        foreach (var (world, icon, tint, _) in icons)
        {
            *(Matrix4x4*)pc = world;
            *(Vector4*)(pc + 64) = tint;
            *(Vector4*)(pc + 80) = new Vector4(icon, 0f, 0f, 0f);
            _api.vkCmdPushConstants(_cmd, _iconLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 96, pc);
            _api.vkCmdDrawIndexed(_cmd, 6, 1, 0, 0, 0);
        }
    }

    /// <summary>The icons' quads into the pick target, with the volume-edge pick pipeline (positions only).</summary>
    private void PickControllerIcons(byte* pc)
    {
        var icons = ControllerIconMarkers;
        if (icons.Count == 0 || _iconPipeline.Handle == 0) return;
        _api.vkCmdBindPipeline(_pickCmd, VkPipelineBindPoint.Graphics, _pipelinePickVolume);
        VkBuffer vb = _iconQuadBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_pickCmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_pickCmd, _iconQuadIndexBuffer, 0, VkIndexType.Uint32);
        foreach (var (world, _, _, pickId) in icons)
        {
            *(Matrix4x4*)pc = world;
            *(uint*)(pc + 64) = pickId;
            _api.vkCmdPushConstants(_pickCmd, _pickLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
            _api.vkCmdDrawIndexed(_pickCmd, 6, 1, 0, 0, 0);
        }
    }

    private void DestroyControllerIcons()
    {
        if (_iconPipeline.Handle != 0)
        {
            _api.vkDestroyPipeline(_iconPipeline);
            _api.vkDestroyPipelineLayout(_iconLayout);
            _api.vkDestroyShaderModule(_iconVs);
            _api.vkDestroyShaderModule(_iconFs);
            _api.vkDestroyDescriptorPool(_iconPool);
            _api.vkDestroyDescriptorSetLayout(_iconSetLayout);
            _api.vkDestroyImageView(_iconView);
            _api.vkDestroyBuffer(_iconQuadBuffer); _api.vkFreeMemory(_iconQuadMemory);
            _api.vkDestroyBuffer(_iconQuadIndexBuffer); _api.vkFreeMemory(_iconQuadIndexMemory);
        }
        _iconTexture?.Dispose();
    }
}
