using System.Numerics;
using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// The level's paths (Scene.EntityPath): every segment of every path in one vertex buffer and one draw,
/// each segment a quad the vertex shader widens to a fixed width in pixels, so the lines read the same at
/// any distance. The same draw carries square markers at the points of the selected path. Depth-tested
/// (but not depth-written) against the level, nudged a little towards the camera so a path lying on the
/// ground is not lost in it.
///
/// A vertex is <see cref="PathVertexFloats"/> floats: segment start (3), segment end (3), corner (3: which
/// end 0/1, which side -1/+1, 1 for a point or key marker / 2 for a handle's), colour (4); six per segment or marker. Rebuilt by the
/// view only when a path, the selection or the colours change (<see cref="SetPathLines"/>), and uploaded
/// per frame slot so the buffer the GPU is reading is never the one being written.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    public const int PathVertexFloats = 13;

    /// <summary>Line and point-marker widths in pixels.</summary>
    public float PathLineWidth { get; set; } = 3f;
    public float PathPointSize { get; set; } = 7f;

    private const string PathLineVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { mat4 uUnused; vec4 uParams; }; // viewport px (xy), line width, point size
layout(location = 0) in vec3 inA;
layout(location = 1) in vec3 inB;
layout(location = 2) in vec3 inCorner;
layout(location = 3) in vec4 inColor;
layout(location = 0) out vec4 fColor;
void main() {
    fColor = inColor;
    vec4 a = uMvp * vec4(inA, 1.0);
    vec4 b = uMvp * vec4(inB, 1.0);
    const float nearW = 1e-3;
    if (inCorner.z > 0.5) {
        if (a.w < nearW) { gl_Position = vec4(0.0, 0.0, -2.0, 1.0); return; }
        // 1: a key or point marker; 2: a handle's, smaller.
        float size = inCorner.z > 1.5 ? uParams.w * 0.7 : uParams.w;
        vec2 offset = vec2(inCorner.x * 2.0 - 1.0, inCorner.y) * size / uParams.xy;
        gl_Position = vec4(a.xy + offset * a.w, a.z * 1.003, a.w);
        return;
    }
    // Clip the segment to the near plane, so a segment passing beside the camera keeps its on-screen part.
    if (a.w < nearW && b.w < nearW) { gl_Position = vec4(0.0, 0.0, -2.0, 1.0); return; }
    if (a.w < nearW) a = mix(a, b, (nearW - a.w) / (b.w - a.w));
    if (b.w < nearW) b = mix(b, a, (nearW - b.w) / (a.w - b.w));
    vec2 sa = a.xy / a.w * uParams.xy, sb = b.xy / b.w * uParams.xy;
    vec2 along = sb - sa;
    vec2 dir = dot(along, along) > 1e-8 ? normalize(along) : vec2(1.0, 0.0);
    vec2 normal = vec2(-dir.y, dir.x);
    vec4 p = inCorner.x < 0.5 ? a : b;
    p.xy += normal * inCorner.y * uParams.z / uParams.xy * p.w;
    // Reversed depth: a slightly larger z is slightly nearer - 0.3 % of the distance, so a path on the
    // ground stays above it at any range.
    p.z *= 1.003;
    gl_Position = p;
}";
    private const string PathLineFragmentGlsl = @"#version 450
layout(location = 0) in vec4 fColor;
layout(location = 0) out vec4 o;
void main() { o = fColor; }";

    private VkShaderModule _pathVs, _pathFs;
    private VkPipeline _pathPipeline;
    private readonly VkBuffer[] _pathBuffers = new VkBuffer[Frames];
    private readonly VkDeviceMemory[] _pathMemory = new VkDeviceMemory[Frames];
    private readonly ulong[] _pathCapacity = new ulong[Frames];
    private readonly int[] _pathUploaded = [-1, -1];
    private float[] _pathVertices = [];
    private int _pathVertexCount, _pathVersion;

    /// <summary>The paths' vertices (<see cref="PathVertexFloats"/> floats each); kept, not copied, until replaced.</summary>
    public void SetPathLines(float[] vertices, int vertexCount)
    {
        _pathVertices = vertices;
        _pathVertexCount = vertexCount;
        _pathVersion++;
    }

    private void CreatePathLinePipeline(VkPipelineCache pipelineCache, byte* entry, VkPipelineViewportStateCreateInfo* viewportState,
        VkPipelineRasterizationStateCreateInfo* rasterCullNone, VkPipelineMultisampleStateCreateInfo* multisample,
        VkPipelineDynamicStateCreateInfo* dynState)
    {
        _pathVs = Module(PathLineVertexGlsl, ShaderStages.Vertex);
        _pathFs = Module(PathLineFragmentGlsl, ShaderStages.Fragment);
        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _pathVs, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _pathFs, pName = entry };
        var vbind = new VkVertexInputBindingDescription { binding = 0, stride = PathVertexFloats * sizeof(float), inputRate = VkVertexInputRate.Vertex };
        VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[4];
        attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        attrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 12 };
        attrs[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 24 };
        attrs[3] = new VkVertexInputAttributeDescription { location = 3, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 36 };
        var vin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbind, vertexAttributeDescriptionCount = 4, pVertexAttributeDescriptions = attrs };
        var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.GreaterOrEqual };
        // RGB only: the opaque pass's alpha is the bloom's glow mask.
        var blendAttach = new VkPipelineColorBlendAttachmentState
        {
            blendEnable = true,
            srcColorBlendFactor = VkBlendFactor.SrcAlpha, dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha, colorBlendOp = VkBlendOp.Add,
            srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add,
            colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B,
        };
        var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
        var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = viewportState, pRasterizationState = rasterCullNone, pMultisampleState = multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = dynState, layout = _volumeLayout, renderPass = _rpOpaque, subpass = 0 };
        VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(path)"); _pathPipeline = p;
    }

    private void DrawPathLines()
    {
        if (_pathVertexCount == 0) return;
        int slot = _slot;
        // This slot's previous frame is finished by the time it is recorded again, so its buffer is free.
        if (_pathUploaded[slot] != _pathVersion)
        {
            ulong size = (ulong)_pathVertexCount * PathVertexFloats * sizeof(float);
            if (size > _pathCapacity[slot])
            {
                if (_pathBuffers[slot].Handle != 0) { _api.vkDestroyBuffer(_pathBuffers[slot]); _api.vkFreeMemory(_pathMemory[slot]); }
                ulong capacity = Math.Max(size + size / 2, 64 * 1024);
                (_pathBuffers[slot], _pathMemory[slot]) = CreateBuffer(capacity, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
                _pathCapacity[slot] = capacity;
            }
            void* mapped; Check(_api.vkMapMemory(_pathMemory[slot], 0, size, 0, &mapped), "vkMapMemory(path)");
            fixed (float* s = _pathVertices) Buffer.MemoryCopy(s, mapped, size, size);
            _api.vkUnmapMemory(_pathMemory[slot]);
            _pathUploaded[slot] = _pathVersion;
        }

        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _pathPipeline);
        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _volumeLayout, 0, 1, &s0, 0, null);
        VkBuffer vb = _pathBuffers[slot]; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        byte* pc = stackalloc byte[80];
        *(Matrix4x4*)pc = Matrix4x4.Identity;
        *(Vector4*)(pc + 64) = new Vector4(_width, _height, PathLineWidth, PathPointSize);
        _api.vkCmdPushConstants(_cmd, _volumeLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
        _api.vkCmdDraw(_cmd, (uint)_pathVertexCount, 1, 0, 0);
    }

    private void DestroyPathLines()
    {
        _api.vkDestroyPipeline(_pathPipeline);
        _api.vkDestroyShaderModule(_pathVs);
        _api.vkDestroyShaderModule(_pathFs);
        for (int i = 0; i < Frames; i++)
            if (_pathBuffers[i].Handle != 0) { _api.vkDestroyBuffer(_pathBuffers[i]); _api.vkFreeMemory(_pathMemory[i]); }
    }
}
