using System.Numerics;
using NeoVeldrid;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>A space sector's play area to draw: a vertical cylinder (the game measures on X/Z only, see
/// Config.GameConfigs.BoundsRadius) and the rings where the ship's warning and turn-back begin.</summary>
public readonly record struct SectorBoundary(Vector3 Centre, float Radius, float HalfHeight, float WarningRadius, float TurnRadius);

/// <summary>
/// The sector boundary shell: an open cylinder, alpha-blended over the scene, depth-tested but not depth-written,
/// so the level stays readable through it. The fragment shader draws it as a faint film with a meridian and
/// latitude grid and a glow where the surface turns away from the camera (fresnel), fading out towards its top
/// and bottom. The warning and turn-back radii are dashed rings at the shell's mid height. Far beyond the far
/// plane the shell is kept at it rather than clipped, since a sector is often wider than the draw distance.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private const string BoundaryVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { vec4 uCentre; vec4 uParams; vec4 uColour; vec4 uCamera; };
layout(location = 0) in vec3 inPos;
layout(location = 0) out vec3 fWorld;
void main() {
    if (uParams.y > 1.5) {
        // Fly plane: a disc fanned from the centre, generated from the vertex index.
        int tri = gl_VertexIndex / 3, k = gl_VertexIndex % 3;
        float a = float(tri + (k == 2 ? 1 : 0)) * 6.2831853 / uParams.w;
        vec2 xz = k == 0 ? vec2(0.0) : vec2(cos(a), sin(a)) * uCentre.w;
        vec3 w = vec3(uCentre.x + xz.x, uCentre.y, uCentre.z + xz.y);
        fWorld = w;
        vec4 p = uMvp * vec4(w, 1.0);
        p.z = max(p.z, 0.0);
        gl_Position = p;
        return;
    }
    float radius = uParams.y > 0.5 ? uParams.z : uCentre.w;
    vec3 w = uCentre.xyz + vec3(inPos.x * radius, inPos.y * uParams.x, inPos.z * radius);
    fWorld = w;
    vec4 p = uMvp * vec4(w, 1.0);
    // Reversed depth: past the far plane z goes below 0; held at the far plane instead of clipped.
    p.z = max(p.z, 0.0);
    gl_Position = p;
}";

    // uParams: x half height, y mode (0 shell, 1 ring, 2 fly plane), z ring radius, w meridian count.
    private const string BoundaryFragmentGlsl = @"#version 450
layout(push_constant) uniform PC { vec4 uCentre; vec4 uParams; vec4 uColour; vec4 uCamera; };
layout(location = 0) in vec3 fWorld;
layout(location = 0) out vec4 o;
float gridLine(float coord, float width) {
    float d = abs(fract(coord - 0.5) - 0.5) / max(width, 1e-6);
    return 1.0 - clamp(d / 1.2, 0.0, 1.0);
}
void main() {
    vec3 d = fWorld - uCentre.xyz;
    if (uParams.y > 1.5) {
        // Fly plane: a faint film, a grid every 100 units and a bright ring at the edge.
        vec2 g = fWorld.xz / 100.0;
        vec2 fw = max(fwidth(g), vec2(1e-6));
        float lineX = gridLine(g.x, fw.x), lineZ = gridLine(g.y, fw.y);
        float density = clamp(1.0 - (max(fw.x, fw.y) - 0.15) / 0.25, 0.0, 1.0);
        float grid = max(lineX, lineZ) * density;
        float r = length(d.xz);
        float ring = 1.0 - smoothstep(0.0, 2.5 * fwidth(r), abs(r - uCentre.w));
        float a = (0.05 + 0.25 * grid + 0.6 * ring) * uColour.a;
        o = vec4(uColour.rgb, a);
        return;
    }
    float turns = atan(d.z, d.x) / 6.2831853;
    // atan jumps at -x; its twin jumps at +x, so one of the two always has a usable derivative.
    float turns2 = atan(-d.z, -d.x) / 6.2831853;
    float meridians = uParams.w;
    float lonWidth = min(fwidth(turns), fwidth(turns2)) * meridians;
    if (uParams.y > 0.5) {
        float band = 1.0 - smoothstep(0.0, 1.5 * fwidth(d.y), abs(d.y));
        float dash = step(0.5, fract(turns * meridians * 4.0));
        float a = band * dash * uColour.a;
        if (a <= 0.002) discard;
        o = vec4(uColour.rgb, a);
        return;
    }
    vec3 n = normalize(vec3(d.x, 0.0, d.z));
    vec3 v = normalize(uCamera.xyz - fWorld);
    float fresnel = pow(1.0 - abs(dot(n, v)), 3.0);
    float lon = gridLine(turns * meridians, lonWidth);
    // Latitudes as far apart as the meridians, so the cells are square.
    float spacing = 6.2831853 * uCentre.w / meridians;
    float h = d.y / spacing;
    float lat = gridLine(h, fwidth(h));
    // Lines closer than a few pixels fade instead of turning into a solid sheet.
    float density = clamp(1.0 - (max(lonWidth, fwidth(h)) - 0.15) / 0.25, 0.0, 1.0);
    float grid = max(lon, lat) * density;
    float edge = 1.0 - smoothstep(0.55, 1.0, abs(d.y) / uParams.x);
    float a = (0.03 + 0.2 * grid + 0.3 * fresnel) * edge * uColour.a;
    o = vec4(uColour.rgb, a);
}";

    /// <summary>The boundary to draw this frame, or null. Set by the view before <see cref="Frame"/>.</summary>
    public SectorBoundary? Boundary { get; set; }

    /// <summary>The Aphelion's flight plane to draw this frame: its world Y and the radius to clip it to, or null.</summary>
    public (float Height, float Radius)? FlyPlane { get; set; }

    public Vector3 FlyPlaneColour { get; set; } = new(0.4f, 1f, 0.6f);

    public Vector3 BoundaryColour { get; set; } = new(0.45f, 0.8f, 1f);

    private const int BoundarySegments = 128, BoundaryRows = 16;
    private const float BoundaryMeridians = 48f;

    private VkShaderModule _boundaryVs, _boundaryFs;
    private VkPipelineLayout _boundaryLayout;
    private VkPipeline _boundaryPipeline;
    private VkBuffer _boundaryVertexBuffer, _boundaryIndexBuffer;
    private VkDeviceMemory _boundaryVbMemory, _boundaryIbMemory;
    private uint _boundaryIndexCount;

    /// <summary>A unit open cylinder along Y: radius 1, y from -1 to 1, rows so the per-vertex depth clamp stays
    /// close to the truth.</summary>
    private void CreateBoundaryGeometry()
    {
        var vertices = new List<Vector3>();
        var indices = new List<uint>();
        for (int r = 0; r <= BoundaryRows; r++)
        {
            float y = -1f + 2f * r / BoundaryRows;
            for (int s = 0; s <= BoundarySegments; s++)
            {
                float a = s * MathF.Tau / BoundarySegments;
                vertices.Add(new Vector3(MathF.Cos(a), y, MathF.Sin(a)));
            }
        }
        const uint stride = BoundarySegments + 1;
        for (uint r = 0; r < BoundaryRows; r++)
            for (uint s = 0; s < BoundarySegments; s++)
            {
                uint a0 = r * stride + s, a1 = a0 + 1, b0 = a0 + stride, b1 = b0 + 1;
                indices.AddRange([a0, b0, b1, a0, b1, a1]);
            }
        _boundaryIndexCount = (uint)indices.Count;

        ulong vSize = (ulong)(vertices.Count * 12), iSize = (ulong)(indices.Count * 4);
        (_boundaryVertexBuffer, _boundaryVbMemory) = CreateBuffer(vSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        (_boundaryIndexBuffer, _boundaryIbMemory) = CreateBuffer(iSize, VkBufferUsageFlags.IndexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* vp; Check(_api.vkMapMemory(_boundaryVbMemory, 0, vSize, 0, &vp), "vkMapMemory(boundaryVb)");
        var vertexArray = vertices.ToArray();
        fixed (Vector3* s = vertexArray) Buffer.MemoryCopy(s, vp, vSize, vSize);
        _api.vkUnmapMemory(_boundaryVbMemory);
        void* ip; Check(_api.vkMapMemory(_boundaryIbMemory, 0, iSize, 0, &ip), "vkMapMemory(boundaryIb)");
        var indexArray = indices.ToArray();
        fixed (uint* s = indexArray) Buffer.MemoryCopy(s, ip, iSize, iSize);
        _api.vkUnmapMemory(_boundaryIbMemory);
    }

    private void CreateBoundaryPipeline(VkPipelineCache pipelineCache, byte* entry, VkPipelineViewportStateCreateInfo* viewportState,
        VkPipelineRasterizationStateCreateInfo* rasterCullNone, VkPipelineMultisampleStateCreateInfo* multisample,
        VkPipelineDynamicStateCreateInfo* dynState)
    {
        CreateBoundaryGeometry();
        _boundaryVs = Module(BoundaryVertexGlsl, ShaderStages.Vertex);
        _boundaryFs = Module(BoundaryFragmentGlsl, ShaderStages.Fragment);

        VkDescriptorSetLayout setLayout = _descLayout;
        var push = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, offset = 0, size = 64 };
        var layoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &setLayout, pushConstantRangeCount = 1, pPushConstantRanges = &push };
        VkPipelineLayout layout; Check(_api.vkCreatePipelineLayout(&layoutInfo, &layout), "vkCreatePipelineLayout(boundary)"); _boundaryLayout = layout;

        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _boundaryVs, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _boundaryFs, pName = entry };
        var vbind = new VkVertexInputBindingDescription { binding = 0, stride = 3 * sizeof(float), inputRate = VkVertexInputRate.Vertex };
        var vattr = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        var vin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbind, vertexAttributeDescriptionCount = 1, pVertexAttributeDescriptions = &vattr };
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
        var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = viewportState, pRasterizationState = rasterCullNone, pMultisampleState = multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = dynState, layout = _boundaryLayout, renderPass = _rpOpaque, subpass = 0 };
        VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(boundary)"); _boundaryPipeline = p;
    }

    private void DrawBoundary()
    {
        if (Boundary is not { } b || b.Radius <= 0f || b.HalfHeight <= 0f) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _boundaryPipeline);
        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _boundaryLayout, 0, 1, &s0, 0, null);
        VkBuffer vb = _boundaryVertexBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _boundaryIndexBuffer, 0, VkIndexType.Uint32);

        Vector4* pc = stackalloc Vector4[4];
        pc[0] = new Vector4(b.Centre, b.Radius);
        pc[3] = new Vector4(_cameraPosition, 0f);
        if (b.WarningRadius > 0f) DrawBoundaryPass(pc, b.HalfHeight, 1f, b.WarningRadius, new Vector4(1f, 0.8f, 0.35f, 0.45f));
        if (b.TurnRadius > 0f) DrawBoundaryPass(pc, b.HalfHeight, 1f, b.TurnRadius, new Vector4(1f, 0.5f, 0.3f, 0.5f));
        DrawBoundaryPass(pc, b.HalfHeight, 0f, 0f, new Vector4(BoundaryColour, 1f));
    }

    private void DrawFlyPlane()
    {
        if (FlyPlane is not { } f || f.Radius <= 0f) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _boundaryPipeline);
        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _boundaryLayout, 0, 1, &s0, 0, null);
        Vector4* pc = stackalloc Vector4[4];
        pc[0] = new Vector4(0f, f.Height, 0f, f.Radius);
        pc[1] = new Vector4(0f, 2f, 0f, BoundarySegments);
        pc[2] = new Vector4(FlyPlaneColour, 1f);
        pc[3] = new Vector4(_cameraPosition, 0f);
        _api.vkCmdPushConstants(_cmd, _boundaryLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 64, pc);
        _api.vkCmdDraw(_cmd, BoundarySegments * 3, 1, 0, 0);
    }

    private void DrawBoundaryPass(Vector4* pc, float halfHeight, float mode, float ring, Vector4 colour)
    {
        pc[1] = new Vector4(halfHeight, mode, ring, BoundaryMeridians);
        pc[2] = colour;
        _api.vkCmdPushConstants(_cmd, _boundaryLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 64, pc);
        _api.vkCmdDrawIndexed(_cmd, _boundaryIndexCount, 1, 0, 0, 0);
    }

    private void DestroyBoundary()
    {
        _api.vkDestroyPipeline(_boundaryPipeline);
        _api.vkDestroyPipelineLayout(_boundaryLayout);
        _api.vkDestroyShaderModule(_boundaryVs);
        _api.vkDestroyShaderModule(_boundaryFs);
        _api.vkDestroyBuffer(_boundaryVertexBuffer); _api.vkFreeMemory(_boundaryVbMemory);
        _api.vkDestroyBuffer(_boundaryIndexBuffer); _api.vkFreeMemory(_boundaryIbMemory);
    }
}
