using System.Numerics;
using Vortice.Vulkan;
using NeoVeldrid;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Draws a level's static collision as an overlay, the way the navmesh is drawn: its own pipeline in
/// the load pass after the scene, uploaded once per level and toggled live (see CollisionGeometry for
/// how the buffers are laid out and why they are instanced).
///
/// Unlike the navmesh surface this one WRITES depth. Collision is a closed, mostly opaque mass of
/// overlapping shells; without depth writes, whatever was drawn last would cover the faces in front of
/// it. It still blends over the level, so lowering the opacity shows the art through the collision
/// without showing the collision through itself. It is biased towards the viewer, since collision
/// usually sits exactly on the art it was built from and would otherwise z-fight it.
///
/// Faces are flat-shaded from their screen-space derivatives and outlined from their barycentrics
/// (taken from gl_VertexIndex % 3; the pool is unshared), so no edge geometry is needed: a level's
/// collision has millions of edges, which the navmesh's instanced thin boxes could not afford.
/// Hiding a category (world, TIE, primitives) or a region collapses its vertices in the vertex shader
/// rather than rebuilding the batches.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    // uFlags.x: visible categories (CollisionCategories). uFlags.y: colour mode (CollisionColorMode).
    // uFlags.z: visible regions (bit N = the Nth region given to CollisionGeometry.Build).
    // uParams.x: opacity, uParams.y: 1 = outlines.
    private const string CollisionVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { vec4 uParams; uvec4 uFlags; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in uint inAttr;
layout(location = 2) in vec4 inRow0;
layout(location = 3) in vec4 inRow1;
layout(location = 4) in vec4 inRow2;
layout(location = 5) in vec4 inRow3;
layout(location = 6) in uint inInstance;
layout(location = 7) in uint inRegions;
layout(location = 0) out vec3 vWorld;
layout(location = 1) out vec3 vBary;
layout(location = 2) flat out vec3 vColor;

// Mirror CollisionGeometry.IdColor / FlagColor / the Type colours.
vec3 idColour(uint id) {
    float h = fract(0.08 + float(id) * 0.618034);
    vec3 rgb = clamp(vec3(abs(h * 6.0 - 3.0) - 1.0, 2.0 - abs(h * 6.0 - 2.0), 2.0 - abs(h * 6.0 - 4.0)), 0.0, 1.0);
    return 0.95 * mix(vec3(1.0), rgb, 0.7);
}
vec3 flagColour(uint f) { return f == 0u ? vec3(0.3) : idColour(f); }
vec3 typeColour(uint kind, bool tie) {
    if (kind == 0u) return tie ? vec3(1.0, 0.62, 0.18) : vec3(0.30, 0.78, 0.42);
    if (kind == 1u) return vec3(0.25, 0.55, 1.0);
    if (kind == 2u) return vec3(0.85, 0.35, 0.95);
    if (kind == 3u) return vec3(1.0, 0.32, 0.36);
    return vec3(0.95, 0.92, 0.30);
}

void main()
{
    uint kind = (inAttr >> 13) & 7u;
    bool tie = (inInstance & 1u) != 0u;
    uint category = kind != 0u ? 4u : (tie ? 2u : 1u);
    if ((uFlags.x & category) == 0u || (uFlags.z & inRegions) == 0u) { gl_Position = vec4(0.0, 0.0, 0.0, 1.0); return; }

    mat4 model = mat4(inRow0, inRow1, inRow2, inRow3);
    vec4 world = model * vec4(inPos, 1.0);
    vWorld = world.xyz;
    int corner = gl_VertexIndex % 3;
    vBary = vec3(corner == 0 ? 1.0 : 0.0, corner == 1 ? 1.0 : 0.0, corner == 2 ? 1.0 : 0.0);

    uint mode = uFlags.y;
    if (mode == 1u)      vColor = kind == 0u ? idColour(inAttr & 0xFFu) : vec3(0.5);
    else if (mode == 2u) vColor = kind == 0u ? flagColour((inAttr >> 8) & 0x1Fu) : vec3(0.5);
    else if (mode == 3u) vColor = idColour(inInstance >> 16);
    else                 vColor = typeColour(kind, tie);
    gl_Position = uMvp * world;
}";

    private const string CollisionFragmentGlsl = @"#version 450
layout(push_constant) uniform PC { vec4 uParams; uvec4 uFlags; };
layout(location = 0) in vec3 vWorld;
layout(location = 1) in vec3 vBary;
layout(location = 2) flat in vec3 vColor;
layout(location = 0) out vec4 o;
void main()
{
    // Flat face normal from the position's derivatives. The files' winding is not consistent, so the
    // shade uses |N.L| and both sides of a face look the same.
    // A face seen edge-on - which is what the camera passing through one does - has parallel
    // derivatives, so their cross product is zero and normalize() would return NaN. That NaN went into
    // the scene colour and from there into bloom's history, where it never left: a black square that
    // grew over the viewport until the level was reloaded. Fall back to an arbitrary normal instead.
    vec3 c = cross(dFdx(vWorld), dFdy(vWorld));
    float len2 = dot(c, c);
    vec3 n = len2 > 1e-30 ? c * inversesqrt(len2) : vec3(0.0, 1.0, 0.0);
    float shade = 0.45 + 0.55 * abs(dot(n, normalize(vec3(0.35, 1.0, 0.55))));
    vec3 rgb = vColor * shade;
    // Outlines: darken within about a pixel and a half of an edge. fwidth is taken unconditionally,
    // since derivatives are undefined inside non-uniform control flow. It is zero on an edge-on face,
    // and smoothstep with equal edges is undefined, hence the floor.
    vec3 width = max(fwidth(vBary), vec3(1e-6));
    vec3 inside = smoothstep(width * 0.5, width * 1.5, vBary);
    if (uParams.y > 0.5) rgb *= mix(0.25, 1.0, min(min(inside.x, inside.y), inside.z));
    // Premultiplied, with the real alpha in A for the blend (see the navmesh surface's note).
    o = vec4(rgb * uParams.x, uParams.x);
}";

    private VkShaderModule _colVs, _colFs;
    private VkPipelineLayout _colLayout;
    private VkPipeline _colPipeline;

    private VkBuffer _colVertexBuffer, _colInstanceBuffer;
    private VkDeviceMemory _colVbMemory, _colInstanceMemory;
    private CollisionBatch[] _colBatches = [];

    public bool CollisionVisible { get; set; }

    /// <summary>0..1; at 1 the collision hides the art behind it.</summary>
    public float CollisionOpacity { get; set; } = 0.85f;

    public bool CollisionOutlines { get; set; } = true;

    public CollisionColorMode CollisionColorMode { get; set; } = CollisionColorMode.Type;

    public CollisionCategories CollisionCategories { get; set; } = CollisionCategories.All;

    /// <summary>Bit N shows the Nth region's collision (see CollisionGeometry.Build).</summary>
    public uint CollisionRegionMask { get; set; } = uint.MaxValue;

    /// <summary>True once collision has been uploaded, so the UI can grey out its toggle on a level
    /// that has none.</summary>
    public bool HasCollision => _colBatches.Length > 0;

    private void CreateCollisionPipeline()
    {
        _colVs = Module(CollisionVertexGlsl, ShaderStages.Vertex);
        _colFs = Module(CollisionFragmentGlsl, ShaderStages.Fragment);

        VkDescriptorSetLayout setLayout = _descLayout;
        var push = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, offset = 0, size = 32 };
        var layoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &setLayout, pushConstantRangeCount = 1, pPushConstantRanges = &push };
        VkPipelineLayout layout;
        Check(_api.vkCreatePipelineLayout(&layoutInfo, &layout), "vkCreatePipelineLayout(collision)");
        _colLayout = layout;

        var entry = "main"u8;
        fixed (byte* name = entry)
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _colVs, pName = name };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _colFs, pName = name };

            VkVertexInputBindingDescription* binds = stackalloc VkVertexInputBindingDescription[2];
            binds[0] = new VkVertexInputBindingDescription { binding = 0, stride = (uint)sizeof(CollisionVertex), inputRate = VkVertexInputRate.Vertex };
            binds[1] = new VkVertexInputBindingDescription { binding = 1, stride = (uint)sizeof(CollisionInstanceData), inputRate = VkVertexInputRate.Instance };
            VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[8];
            attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
            attrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32Uint, offset = 12 };
            for (uint r = 0; r < 4; r++)
                attrs[r + 2] = new VkVertexInputAttributeDescription { location = r + 2, binding = 1, format = VkFormat.R32G32B32A32Sfloat, offset = r * 16 };
            attrs[6] = new VkVertexInputAttributeDescription { location = 6, binding = 1, format = VkFormat.R32Uint, offset = 64 };
            attrs[7] = new VkVertexInputAttributeDescription { location = 7, binding = 1, format = VkFormat.R32Uint, offset = 68 };
            var vin = new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = 2,
                pVertexBindingDescriptions = binds,
                vertexAttributeDescriptionCount = 8,
                pVertexAttributeDescriptions = attrs,
            };

            var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
            var vp = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
            // Reversed depth (GreaterOrEqual): a negative bias pulls the collision towards the viewer,
            // off the art it usually coincides with.
            var raster = new VkPipelineRasterizationStateCreateInfo
            {
                polygonMode = VkPolygonMode.Fill,
                cullMode = VkCullModeFlags.None,
                frontFace = VkFrontFace.CounterClockwise,
                lineWidth = 1f,
                depthBiasEnable = true,
                depthBiasConstantFactor = 0f,
                depthBiasSlopeFactor = -1.5f,
                depthBiasClamp = 0f,
            };
            var ms = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.GreaterOrEqual };
            // The colour write mask drops A, so the opaque target's alpha (the bloom glow mask) is kept.
            var blendAttach = new VkPipelineColorBlendAttachmentState
            {
                blendEnable = true,
                srcColorBlendFactor = VkBlendFactor.One,
                dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                colorBlendOp = VkBlendOp.Add,
                srcAlphaBlendFactor = VkBlendFactor.Zero,
                dstAlphaBlendFactor = VkBlendFactor.One,
                alphaBlendOp = VkBlendOp.Add,
                colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B,
            };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            VkDynamicState* dyn = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            var dynState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dyn };
            var info = new VkGraphicsPipelineCreateInfo
            {
                stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia,
                pViewportState = &vp, pRasterizationState = &raster, pMultisampleState = &ms,
                pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState,
                layout = _colLayout, renderPass = _rpOpaque, subpass = 0,
            };
            VkPipeline p;
            Check(_api.vkCreateGraphicsPipelines(default, 1, &info, &p), "vkCreateGraphicsPipelines(collision)");
            _colPipeline = p;
        }
    }

    /// <summary>
    /// Uploads the level's collision once (see CollisionGeometry.Build). Passing empty spans frees what
    /// was uploaded, which is what a level without collision wants.
    /// </summary>
    public void SetCollision(ReadOnlySpan<CollisionVertex> vertices, ReadOnlySpan<CollisionInstanceData> instances, ReadOnlySpan<CollisionBatch> batches)
    {
        WaitForPendingFrames();
        DestroyCollisionBuffers();
        _colBatches = [];
        if (vertices.IsEmpty || instances.IsEmpty || batches.IsEmpty) return;

        ulong vSize = (ulong)vertices.Length * (ulong)sizeof(CollisionVertex);
        (_colVertexBuffer, _colVbMemory) = CreateBuffer(vSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* vp;
        Check(_api.vkMapMemory(_colVbMemory, 0, vSize, 0, &vp), "vkMapMemory(collisionVb)");
        vertices.CopyTo(new Span<CollisionVertex>(vp, vertices.Length));
        _api.vkUnmapMemory(_colVbMemory);

        ulong iSize = (ulong)instances.Length * (ulong)sizeof(CollisionInstanceData);
        (_colInstanceBuffer, _colInstanceMemory) = CreateBuffer(iSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* ip;
        Check(_api.vkMapMemory(_colInstanceMemory, 0, iSize, 0, &ip), "vkMapMemory(collisionInstances)");
        instances.CopyTo(new Span<CollisionInstanceData>(ip, instances.Length));
        _api.vkUnmapMemory(_colInstanceMemory);

        _colBatches = batches.ToArray();
    }

    private void DrawCollision()
    {
        if (!CollisionVisible || _colBatches.Length == 0 || CollisionCategories == CollisionCategories.None || CollisionRegionMask == 0) return;

        VkDescriptorSet s0 = _descSet;
        byte* pc = stackalloc byte[32];
        *(Vector4*)pc = new Vector4(Math.Clamp(CollisionOpacity, 0f, 1f), CollisionOutlines ? 1f : 0f, 0f, 0f);
        ((uint*)(pc + 16))[0] = (uint)CollisionCategories;
        ((uint*)(pc + 16))[1] = (uint)CollisionColorMode;
        ((uint*)(pc + 16))[2] = CollisionRegionMask;
        ((uint*)(pc + 16))[3] = 0;

        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _colPipeline);
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _colLayout, 0, 1, &s0, 0, null);
        _api.vkCmdPushConstants(_cmd, _colLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 32, pc);
        VkBuffer* buffers = stackalloc VkBuffer[2] { _colVertexBuffer, _colInstanceBuffer };
        ulong* offsets = stackalloc ulong[2] { 0, 0 };
        _api.vkCmdBindVertexBuffers(_cmd, 0, 2, buffers, offsets);
        // One instanced draw per unique shape. Every shape's range starts on a multiple of three, so
        // gl_VertexIndex % 3 is the corner within its triangle.
        foreach (var batch in _colBatches)
            _api.vkCmdDraw(_cmd, batch.VertexCount, batch.InstanceCount, batch.FirstVertex, batch.FirstInstance);
    }

    private void DestroyCollisionBuffers()
    {
        if (_colVertexBuffer.Handle != 0) { _api.vkDestroyBuffer(_colVertexBuffer); _colVertexBuffer = default; }
        if (_colVbMemory.Handle != 0) { _api.vkFreeMemory(_colVbMemory); _colVbMemory = default; }
        if (_colInstanceBuffer.Handle != 0) { _api.vkDestroyBuffer(_colInstanceBuffer); _colInstanceBuffer = default; }
        if (_colInstanceMemory.Handle != 0) { _api.vkFreeMemory(_colInstanceMemory); _colInstanceMemory = default; }
    }

    private void DestroyCollisionPipeline()
    {
        DestroyCollisionBuffers();
        if (_colPipeline.Handle != 0) _api.vkDestroyPipeline(_colPipeline);
        if (_colLayout.Handle != 0) _api.vkDestroyPipelineLayout(_colLayout);
        if (_colVs.Handle != 0) _api.vkDestroyShaderModule(_colVs);
        if (_colFs.Handle != 0) _api.vkDestroyShaderModule(_colFs);
    }
}
