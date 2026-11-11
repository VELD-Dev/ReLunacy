using System.Numerics;
using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Translucent faces for volumes (the death Clues by default, see EntityManager.VolumeFillMode): the
/// volume's unit shape - box, sphere, or cylinder along local Y (capsules too, whose caps depend on the
/// scale) - scaled by its matrix, alpha-blended over the scene, depth-tested but not depth-written, so the
/// wireframe drawn after it and everything behind it stay visible. Not pickable: the wireframe is.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private const string VolumeFillFragmentGlsl = @"#version 450
layout(push_constant) uniform PC { mat4 uModel; vec4 uColor; };
layout(location = 0) out vec4 o;
void main() { o = uColor; }";

    /// <summary>The volumes to fill this frame: world matrix of the unit shape, RGBA colour, shape
    /// (0 box, 1 cylinder, 2 sphere, 3 capsule). Set by the view before <see cref="Frame"/>.</summary>
    public IReadOnlyList<(Matrix4x4 World, Vector4 Colour, int Shape)> VolumeFills { get; set; } = [];

    private VkShaderModule _volumeFillFs;
    private VkPipeline _volumeFillPipeline;
    private VkBuffer _fillVertexBuffer, _fillIndexBuffer;
    private VkDeviceMemory _fillVbMemory, _fillIbMemory;
    // Index range per shape: box, cylinder, sphere (capsules use the cylinder).
    private readonly (uint First, uint Count)[] _fillShapes = new (uint, uint)[3];

    private void CreateVolumeFillGeometry()
    {
        var vertices = new List<Vector3>();
        var indices = new List<uint>();

        // Box: +-0.5.
        _fillShapes[0] = ((uint)indices.Count, 36);
        for (int i = 0; i < 8; i++)
            vertices.Add(new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f));
        uint[] box = [0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3];
        indices.AddRange(box);

        // Cylinder along Y: radius 0.5, height 1, capped.
        const int segments = 24;
        uint start = (uint)vertices.Count;
        int first = indices.Count;
        for (int s = 0; s < segments; s++)
        {
            float a = s * MathF.Tau / segments;
            vertices.Add(new Vector3(MathF.Cos(a) * 0.5f, -0.5f, MathF.Sin(a) * 0.5f));
            vertices.Add(new Vector3(MathF.Cos(a) * 0.5f, 0.5f, MathF.Sin(a) * 0.5f));
        }
        uint bottom = (uint)vertices.Count; vertices.Add(new Vector3(0f, -0.5f, 0f));
        uint top = (uint)vertices.Count; vertices.Add(new Vector3(0f, 0.5f, 0f));
        for (uint s = 0; s < segments; s++)
        {
            uint b0 = start + s * 2, t0 = b0 + 1, b1 = start + (s + 1) % segments * 2, t1 = b1 + 1;
            indices.AddRange([b0, b1, t1, b0, t1, t0, bottom, b1, b0, top, t0, t1]);
        }
        _fillShapes[1] = ((uint)first, (uint)(indices.Count - first));

        // Sphere: radius 0.5.
        const int rings = 12;
        start = (uint)vertices.Count;
        first = indices.Count;
        for (int r = 0; r <= rings; r++)
        {
            float phi = r * MathF.PI / rings;
            for (int s = 0; s < segments; s++)
            {
                float a = s * MathF.Tau / segments;
                vertices.Add(new Vector3(MathF.Sin(phi) * MathF.Cos(a), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(a)) * 0.5f);
            }
        }
        for (uint r = 0; r < rings; r++)
            for (uint s = 0; s < segments; s++)
            {
                uint a0 = start + r * segments + s, a1 = start + r * segments + (s + 1) % segments;
                uint b0 = a0 + segments, b1 = a1 + segments;
                indices.AddRange([a0, b0, b1, a0, b1, a1]);
            }
        _fillShapes[2] = ((uint)first, (uint)(indices.Count - first));

        ulong vSize = (ulong)(vertices.Count * 12), iSize = (ulong)(indices.Count * 4);
        (_fillVertexBuffer, _fillVbMemory) = CreateBuffer(vSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        (_fillIndexBuffer, _fillIbMemory) = CreateBuffer(iSize, VkBufferUsageFlags.IndexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* vp; Check(_api.vkMapMemory(_fillVbMemory, 0, vSize, 0, &vp), "vkMapMemory(fillVb)");
        var vertexArray = vertices.ToArray();
        fixed (Vector3* s = vertexArray) Buffer.MemoryCopy(s, vp, vSize, vSize);
        _api.vkUnmapMemory(_fillVbMemory);
        void* ip; Check(_api.vkMapMemory(_fillIbMemory, 0, iSize, 0, &ip), "vkMapMemory(fillIb)");
        var indexArray = indices.ToArray();
        fixed (uint* s = indexArray) Buffer.MemoryCopy(s, ip, iSize, iSize);
        _api.vkUnmapMemory(_fillIbMemory);
    }

    /// <summary>Built beside the wireframe pipeline, with its layout and vertex format.</summary>
    private void CreateVolumeFillPipeline(VkPipelineCache pipelineCache, byte* entry, VkPipelineViewportStateCreateInfo* viewportState,
        VkPipelineRasterizationStateCreateInfo* rasterCullNone, VkPipelineMultisampleStateCreateInfo* multisample,
        VkPipelineDynamicStateCreateInfo* dynState)
    {
        CreateVolumeFillGeometry();
        _volumeFillFs = Module(VolumeFillFragmentGlsl, ShaderStages.Fragment);
        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _volumeVs, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _volumeFillFs, pName = entry };
        var vbind = new VkVertexInputBindingDescription { binding = 0, stride = 3 * sizeof(float), inputRate = VkVertexInputRate.Vertex };
        var vattr = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        var vin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbind, vertexAttributeDescriptionCount = 1, pVertexAttributeDescriptions = &vattr };
        var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.GreaterOrEqual };
        // RGB only, like the wireframe: the opaque pass's alpha is the bloom's glow mask.
        var blendAttach = new VkPipelineColorBlendAttachmentState
        {
            blendEnable = true,
            srcColorBlendFactor = VkBlendFactor.SrcAlpha, dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha, colorBlendOp = VkBlendOp.Add,
            srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add,
            colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B,
        };
        var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
        var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = viewportState, pRasterizationState = rasterCullNone, pMultisampleState = multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = dynState, layout = _volumeLayout, renderPass = _rpOpaque, subpass = 0 };
        VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(volumeFill)"); _volumeFillPipeline = p;
    }

    private void DrawVolumeFills()
    {
        var fills = VolumeFills;
        if (fills.Count == 0) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _volumeFillPipeline);
        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _volumeLayout, 0, 1, &s0, 0, null);
        VkBuffer vb = _fillVertexBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _fillIndexBuffer, 0, VkIndexType.Uint32);
        byte* pc = stackalloc byte[80];
        foreach (var (world, colour, shape) in fills)
        {
            var (first, count) = _fillShapes[shape switch { 1 or 3 => 1, 2 => 2, _ => 0 }];
            *(Matrix4x4*)pc = world;
            *(Vector4*)(pc + 64) = colour;
            _api.vkCmdPushConstants(_cmd, _volumeLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
            _api.vkCmdDrawIndexed(_cmd, count, 1, first, 0, 0);
        }
    }

    private void DestroyVolumeFill()
    {
        _api.vkDestroyPipeline(_volumeFillPipeline);
        _api.vkDestroyShaderModule(_volumeFillFs);
        _api.vkDestroyBuffer(_fillVertexBuffer); _api.vkFreeMemory(_fillVbMemory);
        _api.vkDestroyBuffer(_fillIndexBuffer); _api.vkFreeMemory(_fillIbMemory);
    }
}
