using System.Numerics;
using Vortice.Vulkan;
using NeoVeldrid;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Draws a level's navigation mesh as a translucent surface with a solid wireframe over it.
///
/// Two pipelines rather than one, because the two halves want opposite raster state. The surface is
/// double-sided (a navmesh's winding is not meaningful - see NavmeshReader), alpha-blended and does
/// NOT write depth, so overlapping islands both show and nothing behind the mesh is occluded. The
/// wireframe is opaque and depth-biased towards the viewer so it sits on top of its own surface
/// instead of z-fighting it.
///
/// The wireframe is drawn as INSTANCED thin boxes, the same geometry the trigger volumes use, not
/// with VK_POLYGON_MODE_LINE. Three reasons: the device's fillModeNonSolid is NeoVeldrid's to enable
/// and not something this code can count on; line mode would redraw every shared edge twice, since
/// it walks triangles, whereas the navmesh stores its UNIQUE edge list and that is what we upload;
/// and a thin box has a settable world-space thickness, while gl_LineWidth above 1.0 needs yet
/// another optional feature. One draw call covers every edge in the level.
///
/// Both buffers are rebuilt only when the level changes (see SetNavmesh), not per frame.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private const string NavmeshSurfaceVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { vec4 uColor; vec4 uParams; };
layout(location = 0) in vec3 inPos;
void main() { gl_Position = uMvp * vec4(inPos, 1.0); }";

    private const string NavmeshSurfaceFragmentGlsl = @"#version 450
layout(push_constant) uniform PC { vec4 uColor; vec4 uParams; };
layout(location = 0) out vec4 o;
// Premultiplied: rgb is the colour already scaled by alpha. A has to carry the REAL alpha even so,
// because the blend's dstColorBlendFactor is ONE_MINUS_SRC_ALPHA and reads it from here. Writing 1.0
// instead - which this did - makes that factor zero, discards the destination, and leaves the
// surface opaque and merely darkening as alpha falls. The colour write mask independently drops A,
// so the opaque target's alpha (the bloom glow mask) is untouched either way: what a fragment
// outputs for blending and what the mask lets it store are separate things.
void main() { o = vec4(uColor.rgb * uColor.a, uColor.a); }";

    // Per-instance edge transform, four vec4 rows of a mat4 in locations 1..4.
    private const string NavmeshEdgeVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { vec4 uColor; vec4 uParams; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec4 inRow0;
layout(location = 2) in vec4 inRow1;
layout(location = 3) in vec4 inRow2;
layout(location = 4) in vec4 inRow3;
void main()
{
    mat4 model = mat4(inRow0, inRow1, inRow2, inRow3);
    gl_Position = uMvp * (model * vec4(inPos, 1.0));
}";

    private const string NavmeshEdgeFragmentGlsl = @"#version 450
layout(push_constant) uniform PC { vec4 uColor; vec4 uParams; };
layout(location = 0) out vec4 o;
void main() { o = vec4(uColor.rgb, 1.0); }";

    private VkShaderModule _navSurfaceVs, _navSurfaceFs, _navEdgeVs, _navEdgeFs;
    private VkPipelineLayout _navLayout;
    private VkPipeline _navSurfacePipeline, _navEdgePipeline;

    private VkBuffer _navVertexBuffer, _navIndexBuffer, _navEdgeInstanceBuffer;
    private VkDeviceMemory _navVbMemory, _navIbMemory, _navEdgeMemory;
    private uint _navIndexCount, _navEdgeInstanceCount;

    /// <summary>Surface colour and alpha, straight from Editor Settings &gt; Navmesh Color.</summary>
    public Vector4 NavmeshColor { get; set; } = new(0.2f, 0.45f, 1f, 0.35f);

    /// <summary>World-space thickness of a wireframe edge.</summary>
    public float NavmeshWireThickness { get; set; } = 0.06f;

    public bool NavmeshVisible { get; set; }

    /// <summary>True once geometry has been uploaded, so the UI can say "no navmesh in this level"
    /// rather than leaving the toggle looking broken.</summary>
    public bool HasNavmesh => _navIndexCount > 0;

    private void CreateNavmeshPipelines()
    {
        _navSurfaceVs = Module(NavmeshSurfaceVertexGlsl, ShaderStages.Vertex);
        _navSurfaceFs = Module(NavmeshSurfaceFragmentGlsl, ShaderStages.Fragment);
        _navEdgeVs = Module(NavmeshEdgeVertexGlsl, ShaderStages.Vertex);
        _navEdgeFs = Module(NavmeshEdgeFragmentGlsl, ShaderStages.Fragment);

        VkDescriptorSetLayout setLayout = _descLayout;
        var push = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, offset = 0, size = 32 };
        var layoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &setLayout, pushConstantRangeCount = 1, pPushConstantRanges = &push };
        VkPipelineLayout layout;
        Check(_api.vkCreatePipelineLayout(&layoutInfo, &layout), "vkCreatePipelineLayout(navmesh)");
        _navLayout = layout;

        _navSurfacePipeline = NavmeshPipeline(surface: true);
        _navEdgePipeline = NavmeshPipeline(surface: false);
    }

    private VkPipeline NavmeshPipeline(bool surface)
    {
        var entry = "main"u8;
        fixed (byte* name = entry)
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = surface ? _navSurfaceVs : _navEdgeVs, pName = name };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = surface ? _navSurfaceFs : _navEdgeFs, pName = name };

            VkVertexInputBindingDescription* binds = stackalloc VkVertexInputBindingDescription[2];
            binds[0] = new VkVertexInputBindingDescription { binding = 0, stride = 3 * sizeof(float), inputRate = VkVertexInputRate.Vertex };
            binds[1] = new VkVertexInputBindingDescription { binding = 1, stride = 64, inputRate = VkVertexInputRate.Instance };
            VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[5];
            attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
            for (uint r = 0; r < 4; r++)
                attrs[r + 1] = new VkVertexInputAttributeDescription { location = r + 1, binding = 1, format = VkFormat.R32G32B32A32Sfloat, offset = r * 16 };
            var vin = new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = surface ? 1u : 2u,
                pVertexBindingDescriptions = binds,
                vertexAttributeDescriptionCount = surface ? 1u : 5u,
                pVertexAttributeDescriptions = attrs,
            };

            var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
            var vp = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
            // Depth is reversed here (GreaterOrEqual), so a NEGATIVE bias pulls a fragment towards
            // the viewer - the same sign the non-opaque lit pipelines use.
            var raster = new VkPipelineRasterizationStateCreateInfo
            {
                polygonMode = VkPolygonMode.Fill,
                cullMode = VkCullModeFlags.None,
                frontFace = VkFrontFace.CounterClockwise,
                lineWidth = 1f,
                depthBiasEnable = !surface,
                depthBiasConstantFactor = 0f,
                depthBiasSlopeFactor = surface ? 0f : -2f,
                depthBiasClamp = 0f,
            };
            var ms = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
            // Neither half writes depth: the overlay must not hide the level behind it, and the
            // surface's own islands overlap.
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.GreaterOrEqual };
            var mask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B;
            // The surface's alpha is premultiplied in the shader, so SRC is One, not SrcAlpha.
            var blendAttach = surface
                ? new VkPipelineColorBlendAttachmentState
                {
                    blendEnable = true,
                    srcColorBlendFactor = VkBlendFactor.One,
                    dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                    colorBlendOp = VkBlendOp.Add,
                    srcAlphaBlendFactor = VkBlendFactor.Zero,
                    dstAlphaBlendFactor = VkBlendFactor.One,
                    alphaBlendOp = VkBlendOp.Add,
                    colorWriteMask = mask,
                }
                : new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            VkDynamicState* dyn = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            var dynState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dyn };
            var info = new VkGraphicsPipelineCreateInfo
            {
                stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia,
                pViewportState = &vp, pRasterizationState = &raster, pMultisampleState = &ms,
                pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState,
                layout = _navLayout, renderPass = _rpOpaque, subpass = 0,
            };
            VkPipeline p;
            Check(_api.vkCreateGraphicsPipelines(default, 1, &info, &p),
                  surface ? "vkCreateGraphicsPipelines(navmeshSurface)" : "vkCreateGraphicsPipelines(navmeshEdge)");
            return p;
        }
    }

    /// <summary>
    /// Uploads the level's whole navmesh once: one vertex and index buffer for the surface, and one
    /// instance buffer of edge transforms for the wireframe. Positions are already world-space, so
    /// there is no per-chunk model matrix and the lot can be merged into a single draw each.
    ///
    /// Passing an empty span frees what was uploaded, which is what a level with no navmesh wants.
    /// </summary>
    public void SetNavmesh(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<uint> indices, ReadOnlySpan<Matrix4x4> edges)
    {
        WaitForPendingFrames();
        DestroyNavmeshBuffers();
        _navIndexCount = 0;
        _navEdgeInstanceCount = 0;
        if (vertices.IsEmpty || indices.IsEmpty) return;

        ulong vSize = (ulong)vertices.Length * 12;
        (_navVertexBuffer, _navVbMemory) = CreateBuffer(vSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* vp;
        Check(_api.vkMapMemory(_navVbMemory, 0, vSize, 0, &vp), "vkMapMemory(navVb)");
        for (int i = 0; i < vertices.Length; i++) ((Vector3*)vp)[i] = vertices[i];
        _api.vkUnmapMemory(_navVbMemory);

        ulong iSize = (ulong)indices.Length * sizeof(uint);
        (_navIndexBuffer, _navIbMemory) = CreateBuffer(iSize, VkBufferUsageFlags.IndexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* ip;
        Check(_api.vkMapMemory(_navIbMemory, 0, iSize, 0, &ip), "vkMapMemory(navIb)");
        for (int i = 0; i < indices.Length; i++) ((uint*)ip)[i] = indices[i];
        _api.vkUnmapMemory(_navIbMemory);
        _navIndexCount = (uint)indices.Length;

        if (edges.IsEmpty) return;
        ulong eSize = (ulong)edges.Length * 64;
        (_navEdgeInstanceBuffer, _navEdgeMemory) = CreateBuffer(eSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* ep;
        Check(_api.vkMapMemory(_navEdgeMemory, 0, eSize, 0, &ep), "vkMapMemory(navEdges)");
        for (int i = 0; i < edges.Length; i++) ((Matrix4x4*)ep)[i] = edges[i];
        _api.vkUnmapMemory(_navEdgeMemory);
        _navEdgeInstanceCount = (uint)edges.Length;
    }

    private void DrawNavmesh()
    {
        if (!NavmeshVisible || _navIndexCount == 0) return;

        VkDescriptorSet s0 = _descSet;
        byte* pc = stackalloc byte[32];
        ulong zero = 0;

        *(Vector4*)pc = NavmeshColor;
        *(Vector4*)(pc + 16) = Vector4.Zero;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _navSurfacePipeline);
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _navLayout, 0, 1, &s0, 0, null);
        _api.vkCmdPushConstants(_cmd, _navLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 32, pc);
        VkBuffer vb = _navVertexBuffer;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &zero);
        _api.vkCmdBindIndexBuffer(_cmd, _navIndexBuffer, 0, VkIndexType.Uint32);
        _api.vkCmdDrawIndexed(_cmd, _navIndexCount, 1, 0, 0, 0);

        if (_navEdgeInstanceCount == 0) return;
        // Solid black, whatever the surface colour is: the wireframe is there to read the topology.
        *(Vector4*)pc = new Vector4(0f, 0f, 0f, 1f);
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _navEdgePipeline);
        _api.vkCmdPushConstants(_cmd, _navLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 32, pc);
        // Binding 0 is the shared thin-box edge geometry the trigger volumes also draw, so the
        // wireframe honours the same thickness write (WriteEdgeVertices).
        VkBuffer evb = _edgeVertexBuffer;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &evb, &zero);
        VkBuffer inst = _navEdgeInstanceBuffer;
        _api.vkCmdBindVertexBuffers(_cmd, 1, 1, &inst, &zero);
        _api.vkCmdBindIndexBuffer(_cmd, _edgeIndexBuffer, 0, VkIndexType.Uint32);
        _api.vkCmdDrawIndexed(_cmd, _edgeIndexCount, _navEdgeInstanceCount, 0, 0, 0);
    }

    private void DestroyNavmeshBuffers()
    {
        if (_navVertexBuffer.Handle != 0) { _api.vkDestroyBuffer(_navVertexBuffer); _navVertexBuffer = default; }
        if (_navVbMemory.Handle != 0) { _api.vkFreeMemory(_navVbMemory); _navVbMemory = default; }
        if (_navIndexBuffer.Handle != 0) { _api.vkDestroyBuffer(_navIndexBuffer); _navIndexBuffer = default; }
        if (_navIbMemory.Handle != 0) { _api.vkFreeMemory(_navIbMemory); _navIbMemory = default; }
        if (_navEdgeInstanceBuffer.Handle != 0) { _api.vkDestroyBuffer(_navEdgeInstanceBuffer); _navEdgeInstanceBuffer = default; }
        if (_navEdgeMemory.Handle != 0) { _api.vkFreeMemory(_navEdgeMemory); _navEdgeMemory = default; }
    }

    private void DestroyNavmeshPipelines()
    {
        DestroyNavmeshBuffers();
        if (_navSurfacePipeline.Handle != 0) _api.vkDestroyPipeline(_navSurfacePipeline);
        if (_navEdgePipeline.Handle != 0) _api.vkDestroyPipeline(_navEdgePipeline);
        if (_navLayout.Handle != 0) _api.vkDestroyPipelineLayout(_navLayout);
        if (_navSurfaceVs.Handle != 0) _api.vkDestroyShaderModule(_navSurfaceVs);
        if (_navSurfaceFs.Handle != 0) _api.vkDestroyShaderModule(_navSurfaceFs);
        if (_navEdgeVs.Handle != 0) _api.vkDestroyShaderModule(_navEdgeVs);
        if (_navEdgeFs.Handle != 0) _api.vkDestroyShaderModule(_navEdgeFs);
    }
}
