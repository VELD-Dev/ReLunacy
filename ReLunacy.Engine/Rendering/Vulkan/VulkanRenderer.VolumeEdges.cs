using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// The volumes' wireframe edges (and every other edge-box overlay handed to <see cref="Frame"/>) as one instanced
/// draw: each edge is a 96-byte instance (world matrix, colour, pick id) in a host-mapped buffer per frame slot, so
/// thousands of edges cost one draw and one copy instead of a push-constant update and a draw each. Edges whose
/// sphere is outside the view frustum are dropped while copying. The picking pass draws the same buffer.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private const string VolumeEdgeVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec4 inM0;
layout(location = 2) in vec4 inM1;
layout(location = 3) in vec4 inM2;
layout(location = 4) in vec4 inM3;
layout(location = 5) in vec4 inColor;
layout(location = 0) out vec3 fColor;
void main() { fColor = inColor.rgb; gl_Position = uMvp * (mat4(inM0, inM1, inM2, inM3) * vec4(inPos, 1.0)); }";

    private const string VolumeEdgeFragmentGlsl = @"#version 450
layout(location = 0) in vec3 fColor;
layout(location = 0) out vec4 o;
void main() { o = vec4(fColor, 1.0); }";

    private const string PickVolumeEdgeVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; mat4 uPick; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec4 inM0;
layout(location = 2) in vec4 inM1;
layout(location = 3) in vec4 inM2;
layout(location = 4) in vec4 inM3;
layout(location = 6) in uint inId;
layout(location = 0) flat out uint fId;
void main() { fId = inId; gl_Position = uPick * (mat4(inM0, inM1, inM2, inM3) * vec4(inPos, 1.0)); }";

    private const string PickVolumeEdgeFragmentGlsl = @"#version 450
layout(location = 0) flat in uint fId;
layout(location = 0) out vec4 outColor;
void main() {
    outColor = vec4(
        float(fId & 0xFFu) / 255.0,
        float((fId >> 8) & 0xFFu) / 255.0,
        float((fId >> 16) & 0xFFu) / 255.0,
        float((fId >> 24) & 0xFFu) / 255.0);
}";

    [StructLayout(LayoutKind.Sequential)]
    private struct EdgeInstance
    {
        public Matrix4x4 World;
        public Vector4 Color;
        public uint PickId, Pad0, Pad1, Pad2;
    }

    private VkShaderModule _volumeEdgeVs, _volumeEdgeFs, _pickVolumeEdgeVs, _pickVolumeEdgeFs;
    private VkPipeline _volumeEdgePipeline, _pickVolumeEdgePipeline;
    private readonly VkBuffer[] _edgeInstBuffer = new VkBuffer[Frames];
    private readonly VkDeviceMemory[] _edgeInstMemory = new VkDeviceMemory[Frames];
    private readonly void*[] _edgeInstMapped = new void*[Frames];
    private readonly int[] _edgeInstCapacity = new int[Frames];
    private readonly int[] _edgeInstCount = new int[Frames];

    /// <summary>Edges drawn / handed in last frame (what the frustum cull left).</summary>
    public (int Drawn, int Total) VolumeEdgeStats { get; private set; }

    private void CreateVolumeEdgePipelines(VkPipelineCache pipelineCache, byte* entry, VkPipelineViewportStateCreateInfo* viewportState,
        VkPipelineRasterizationStateCreateInfo* rasterCullNone, VkPipelineMultisampleStateCreateInfo* multisample,
        VkPipelineDynamicStateCreateInfo* dynState)
    {
        _volumeEdgeVs = Module(VolumeEdgeVertexGlsl, ShaderStages.Vertex);
        _volumeEdgeFs = Module(VolumeEdgeFragmentGlsl, ShaderStages.Fragment);
        _pickVolumeEdgeVs = Module(PickVolumeEdgeVertexGlsl, ShaderStages.Vertex);
        _pickVolumeEdgeFs = Module(PickVolumeEdgeFragmentGlsl, ShaderStages.Fragment);

        VkVertexInputBindingDescription* binds = stackalloc VkVertexInputBindingDescription[2];
        binds[0] = new VkVertexInputBindingDescription { binding = 0, stride = 3 * sizeof(float), inputRate = VkVertexInputRate.Vertex };
        binds[1] = new VkVertexInputBindingDescription { binding = 1, stride = (uint)sizeof(EdgeInstance), inputRate = VkVertexInputRate.Instance };
        VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[7];
        attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        for (uint c = 0; c < 4; c++)
            attrs[1 + c] = new VkVertexInputAttributeDescription { location = 1 + c, binding = 1, format = VkFormat.R32G32B32A32Sfloat, offset = c * 16 };
        attrs[5] = new VkVertexInputAttributeDescription { location = 5, binding = 1, format = VkFormat.R32G32B32A32Sfloat, offset = 64 };
        attrs[6] = new VkVertexInputAttributeDescription { location = 6, binding = 1, format = VkFormat.R32Uint, offset = 80 };
        var vin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 2, pVertexBindingDescriptions = binds, vertexAttributeDescriptionCount = 7, pVertexAttributeDescriptions = attrs };
        var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.GreaterOrEqual };
        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];

        // RGB only: the opaque pass's alpha is the bloom's glow mask (VulkanRenderer.Bloom.cs).
        var blendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B };
        var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _volumeEdgeVs, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _volumeEdgeFs, pName = entry };
        var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = viewportState, pRasterizationState = rasterCullNone, pMultisampleState = multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = dynState, layout = _volumeLayout, renderPass = _rpOpaque, subpass = 0 };
        VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(volumeEdge)"); _volumeEdgePipeline = p;

        var pickAttach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A };
        var pickBlend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &pickAttach };
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _pickVolumeEdgeVs, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _pickVolumeEdgeFs, pName = entry };
        var pickInfo = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = viewportState, pRasterizationState = rasterCullNone, pMultisampleState = multisample, pDepthStencilState = &depth, pColorBlendState = &pickBlend, pDynamicState = dynState, layout = _pickLayout, renderPass = _rpPick, subpass = 0 };
        VkPipeline pp; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &pickInfo, &pp), "vkCreateGraphicsPipelines(pickVolumeEdge)"); _pickVolumeEdgePipeline = pp;
    }

    /// <summary>Copies this slot's edges into its instance buffer, frustum-culled by each edge's bounding sphere.</summary>
    private void FillVolumeEdges(IReadOnlyList<(Matrix4x4 world, Vector4 color, uint pickId)> volumes)
    {
        int slot = _slot, total = volumes.Count;
        _edgeInstCount[slot] = 0;
        VolumeEdgeStats = (0, total);
        if (total == 0) return;
        if (total > _edgeInstCapacity[slot])
        {
            WaitForPendingFrames();
            if (_edgeInstBuffer[slot].Handle != 0) { _api.vkDestroyBuffer(_edgeInstBuffer[slot]); _api.vkFreeMemory(_edgeInstMemory[slot]); }
            int capacity = Math.Max(total + total / 4, 4096);
            ulong bytes = (ulong)capacity * (ulong)sizeof(EdgeInstance);
            (_edgeInstBuffer[slot], _edgeInstMemory[slot]) = CreateBuffer(bytes, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            void* mapped; Check(_api.vkMapMemory(_edgeInstMemory[slot], 0, bytes, 0, &mapped), "vkMapMemory(edgeInst)");
            _edgeInstMapped[slot] = mapped;
            _edgeInstCapacity[slot] = capacity;
        }
        var dst = (EdgeInstance*)_edgeInstMapped[slot];
        float pad = MathF.Max(_edgeThickness, 0.001f) + 0.01f;
        bool cull = _frustumCulling && Environment.GetEnvironmentVariable("NOEDGECULL") == null;
        int n = 0;
        if (volumes is List<(Matrix4x4 world, Vector4 color, uint pickId)> list)
        {
            foreach (ref readonly var v in CollectionsMarshal.AsSpan(list))
                if (!cull || EdgeVisible(v.world, pad)) { dst[n].World = v.world; dst[n].Color = v.color; dst[n].PickId = v.pickId; n++; }
        }
        else
        {
            for (int i = 0; i < total; i++)
            {
                var v = volumes[i];
                if (!cull || EdgeVisible(v.world, pad)) { dst[n].World = v.world; dst[n].Color = v.color; dst[n].PickId = v.pickId; n++; }
            }
        }
        _edgeInstCount[slot] = n;
        VolumeEdgeStats = (n, total);
    }

    /// <summary>Whether an edge's bounding sphere (its matrix's longest axis, half of it, plus the thickness) touches the frustum.</summary>
    private bool EdgeVisible(in Matrix4x4 m, float pad)
    {
        float radius = 0.5f * MathF.Sqrt(MathF.Max(MathF.Max(m.M11 * m.M11 + m.M12 * m.M12 + m.M13 * m.M13,
            m.M21 * m.M21 + m.M22 * m.M22 + m.M23 * m.M23), m.M31 * m.M31 + m.M32 * m.M32 + m.M33 * m.M33)) + pad;
        for (int p = 0; p < 6; p++)
        {
            Vector4 pl = _planes[p];
            if (pl.X * m.M41 + pl.Y * m.M42 + pl.Z * m.M43 + pl.W < -radius) return false;
        }
        return true;
    }

    private void DrawVolumeEdges()
    {
        int count = _edgeInstCount[_slot];
        if (count == 0) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _volumeEdgePipeline);
        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _volumeLayout, 0, 1, &s0, 0, null);
        VkBuffer* vbs = stackalloc VkBuffer[2] { _edgeVertexBuffer, _edgeInstBuffer[_slot] };
        ulong* offsets = stackalloc ulong[2] { 0, 0 };
        _api.vkCmdBindVertexBuffers(_cmd, 0, 2, vbs, offsets);
        _api.vkCmdBindIndexBuffer(_cmd, _edgeIndexBuffer, 0, VkIndexType.Uint32);
        _api.vkCmdDrawIndexed(_cmd, _edgeIndexCount, (uint)count, 0, 0, 0);
    }

    /// <summary>Picking draw of the edges the last <see cref="Frame"/> recorded.</summary>
    private void PickVolumeEdges()
    {
        int slot = _lastRecordedSlot, count = _edgeInstCount[slot];
        if (count == 0) return;
        _api.vkCmdBindPipeline(_pickCmd, VkPipelineBindPoint.Graphics, _pickVolumeEdgePipeline);
        VkBuffer* vbs = stackalloc VkBuffer[2] { _edgeVertexBuffer, _edgeInstBuffer[slot] };
        ulong* offsets = stackalloc ulong[2] { 0, 0 };
        _api.vkCmdBindVertexBuffers(_pickCmd, 0, 2, vbs, offsets);
        _api.vkCmdBindIndexBuffer(_pickCmd, _edgeIndexBuffer, 0, VkIndexType.Uint32);
        _api.vkCmdDrawIndexed(_pickCmd, _edgeIndexCount, (uint)count, 0, 0, 0);
    }

    private void DestroyVolumeEdges()
    {
        for (int s = 0; s < Frames; s++)
            if (_edgeInstBuffer[s].Handle != 0) { _api.vkDestroyBuffer(_edgeInstBuffer[s]); _api.vkFreeMemory(_edgeInstMemory[s]); _edgeInstBuffer[s] = default; }
        _api.vkDestroyPipeline(_volumeEdgePipeline);
        _api.vkDestroyPipeline(_pickVolumeEdgePipeline);
        _api.vkDestroyShaderModule(_volumeEdgeVs);
        _api.vkDestroyShaderModule(_volumeEdgeFs);
        _api.vkDestroyShaderModule(_pickVolumeEdgeVs);
        _api.vkDestroyShaderModule(_pickVolumeEdgeFs);
    }
}
