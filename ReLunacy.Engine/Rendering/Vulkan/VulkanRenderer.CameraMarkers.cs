using System.Numerics;
using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// The camera controllers (Scene.EntityCamera), each drawn with the editor's camera model at its placement:
/// opaque, with its vertex colours under a fixed light from above so its shape reads, tinted when selected.
/// The model looks down its local -Z. Pickable like the volumes, through its own position-only buffer.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    /// <summary>The camera model, set once by the application before any renderer is built; null draws no cameras.</summary>
    public static MarkerModel? CameraModel { get; set; }

    /// <summary>The cameras to draw this frame: world matrix, tint (w = how much of it), pick id.</summary>
    public IReadOnlyList<(Matrix4x4 World, Vector4 Tint, uint PickId)> CameraMarkers { get; set; } = [];

    private const string CameraMarkerVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { mat4 uModel; vec4 uTint; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inColor;
layout(location = 0) out vec3 fColor;
layout(location = 1) out vec3 fNormal;
void main() {
    fColor = inColor;
    fNormal = mat3(uModel) * inNormal;
    gl_Position = uMvp * (uModel * vec4(inPos, 1.0));
}";
    private const string CameraMarkerFragmentGlsl = @"#version 450
layout(push_constant) uniform PC { mat4 uModel; vec4 uTint; };
layout(location = 0) in vec3 fColor;
layout(location = 1) in vec3 fNormal;
layout(location = 0) out vec4 o;
void main() {
    float light = 0.45 + 0.55 * max(dot(normalize(fNormal), normalize(vec3(0.35, 1.0, 0.55))), 0.0);
    o = vec4(mix(fColor, uTint.rgb, uTint.w) * light, 1.0);
}";

    private VkShaderModule _cameraVs, _cameraFs;
    private VkPipeline _cameraPipeline;
    private VkBuffer _cameraVertexBuffer, _cameraPositionBuffer, _cameraIndexBuffer;
    private VkDeviceMemory _cameraVbMemory, _cameraPbMemory, _cameraIbMemory;
    private uint _cameraIndexCount;

    private void CreateCameraMarkerPipeline(VkPipelineCache pipelineCache, byte* entry, VkPipelineViewportStateCreateInfo* viewportState,
        VkPipelineRasterizationStateCreateInfo* rasterCullNone, VkPipelineMultisampleStateCreateInfo* multisample,
        VkPipelineDynamicStateCreateInfo* dynState)
    {
        var model = CameraModel;
        if (model == null || model.Indices.Length == 0) return;

        var interleaved = new float[model.Positions.Length * 9];
        for (int i = 0; i < model.Positions.Length; i++)
        {
            var (pos, n, c) = (model.Positions[i], model.Normals[i], model.Colours[i]);
            interleaved[i * 9 + 0] = pos.X; interleaved[i * 9 + 1] = pos.Y; interleaved[i * 9 + 2] = pos.Z;
            interleaved[i * 9 + 3] = n.X; interleaved[i * 9 + 4] = n.Y; interleaved[i * 9 + 5] = n.Z;
            interleaved[i * 9 + 6] = c.X; interleaved[i * 9 + 7] = c.Y; interleaved[i * 9 + 8] = c.Z;
        }
        (_cameraVertexBuffer, _cameraVbMemory) = Upload(interleaved.AsSpan(), VkBufferUsageFlags.VertexBuffer);
        (_cameraPositionBuffer, _cameraPbMemory) = Upload(model.Positions.AsSpan(), VkBufferUsageFlags.VertexBuffer);
        (_cameraIndexBuffer, _cameraIbMemory) = Upload(model.Indices.AsSpan(), VkBufferUsageFlags.IndexBuffer);
        _cameraIndexCount = (uint)model.Indices.Length;

        _cameraVs = Module(CameraMarkerVertexGlsl, ShaderStages.Vertex);
        _cameraFs = Module(CameraMarkerFragmentGlsl, ShaderStages.Fragment);
        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _cameraVs, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _cameraFs, pName = entry };
        var vbind = new VkVertexInputBindingDescription { binding = 0, stride = 9 * sizeof(float), inputRate = VkVertexInputRate.Vertex };
        VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[3];
        attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        attrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 12 };
        attrs[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 24 };
        var vin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbind, vertexAttributeDescriptionCount = 3, pVertexAttributeDescriptions = attrs };
        var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.GreaterOrEqual };
        // RGB only, like the volumes: the opaque pass's alpha is the bloom's glow mask.
        var blendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B };
        var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
        var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = viewportState, pRasterizationState = rasterCullNone, pMultisampleState = multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = dynState, layout = _volumeLayout, renderPass = _rpOpaque, subpass = 0 };
        VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(camera)"); _cameraPipeline = p;
    }

    private (VkBuffer, VkDeviceMemory) Upload<T>(ReadOnlySpan<T> data, VkBufferUsageFlags usage) where T : unmanaged
    {
        ulong size = (ulong)(data.Length * sizeof(T));
        var (buffer, memory) = CreateBuffer(size, usage, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* mapped; Check(_api.vkMapMemory(memory, 0, size, 0, &mapped), "vkMapMemory(camera)");
        fixed (T* s = data) Buffer.MemoryCopy(s, mapped, size, size);
        _api.vkUnmapMemory(memory);
        return (buffer, memory);
    }

    private void DrawCameraMarkers()
    {
        var cameras = CameraMarkers;
        if (cameras.Count == 0 || _cameraPipeline.Handle == 0) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _cameraPipeline);
        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _volumeLayout, 0, 1, &s0, 0, null);
        VkBuffer vb = _cameraVertexBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _cameraIndexBuffer, 0, VkIndexType.Uint32);
        byte* pc = stackalloc byte[80];
        foreach (var (world, tint, _) in cameras)
        {
            *(Matrix4x4*)pc = world;
            *(Vector4*)(pc + 64) = tint;
            _api.vkCmdPushConstants(_cmd, _volumeLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
            _api.vkCmdDrawIndexed(_cmd, _cameraIndexCount, 1, 0, 0, 0);
        }
    }

    /// <summary>The cameras into the pick target, with the volume-edge pick pipeline (positions only).</summary>
    private void PickCameraMarkers(byte* pc)
    {
        var cameras = CameraMarkers;
        if (cameras.Count == 0 || _cameraPipeline.Handle == 0) return;
        _api.vkCmdBindPipeline(_pickCmd, VkPipelineBindPoint.Graphics, _pipelinePickVolume);
        VkBuffer vb = _cameraPositionBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_pickCmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_pickCmd, _cameraIndexBuffer, 0, VkIndexType.Uint32);
        foreach (var (world, _, pickId) in cameras)
        {
            *(Matrix4x4*)pc = world;
            *(uint*)(pc + 64) = pickId;
            _api.vkCmdPushConstants(_pickCmd, _pickLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
            _api.vkCmdDrawIndexed(_pickCmd, _cameraIndexCount, 1, 0, 0, 0);
        }
    }

    private void DestroyCameraMarkers()
    {
        if (_cameraPipeline.Handle == 0) return;
        _api.vkDestroyPipeline(_cameraPipeline);
        _api.vkDestroyShaderModule(_cameraVs);
        _api.vkDestroyShaderModule(_cameraFs);
        _api.vkDestroyBuffer(_cameraVertexBuffer); _api.vkFreeMemory(_cameraVbMemory);
        _api.vkDestroyBuffer(_cameraPositionBuffer); _api.vkFreeMemory(_cameraPbMemory);
        _api.vkDestroyBuffer(_cameraIndexBuffer); _api.vkFreeMemory(_cameraIbMemory);
    }
}
