using System.Diagnostics;
using System.Numerics;
using NeoVeldrid;
using Vortice.Vulkan;
using ReLunacy.Engine.Assets.Sky;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Sky pass for <see cref="VulkanRenderer"/> (both engines share the same sky model).
///
/// The sky is drawn inside the same opaque render pass as the level, before any scene geometry and
/// without depth writes, so the level always covers it.
///
/// Pass 14 Dome, Color passes 15/18 and Clouds pass 17 are visible here in retail order. Bloom 16/19
/// remains disabled until its destination-alpha/post chain is closed. Sky texture descriptors retain
/// their authored old-engine RSX sampler state and are translated per texture by the raw Vulkan path;
/// any not-yet-portable RSX mode is logged explicitly as compatibility rather than silently replaced.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private const uint SkyVertexStride = VkSkyScene.FloatsPerVertex * sizeof(float);
    private const uint SkyDomeVertexStride = VkSkyDome.FloatsPerVertex * sizeof(float);
    private const float SkyTwoPi = 6.28318977355957f;

    private readonly Stopwatch _skyClock = Stopwatch.StartNew();
    private VkSkyScene? _skyScene;

    private VkBuffer _skyVertexBuffer;
    private VkDeviceMemory _skyVbMemory;
    private VkBuffer _skyIndexBuffer;
    private VkDeviceMemory _skyIbMemory;

    private VkBuffer _skyDomeVertexBuffer;
    private VkDeviceMemory _skyDomeVbMemory;
    private VkBuffer _skyDomeIndexBuffer;
    private VkDeviceMemory _skyDomeIbMemory;

    private VkDescriptorSetLayout _skyTextureLayout;
    private VkDescriptorPool _skyDescriptorPool;
    private VkDescriptorSet[] _skyTextureSets = [];
    private VkImageView[] _skyTextureViews = [];
    private VkSampler[] _skySamplers = [];

    private VkShaderModule _skyVs;
    private VkShaderModule _skyFs;
    private VkPipelineLayout _skyLayout;
    private VkPipeline _pipelineSky;

    private VkShaderModule _skyDomeVs;
    private VkShaderModule _skyDomeFs;
    private VkPipelineLayout _skyDomeLayout;
    private VkPipeline _pipelineSkyDome;

    private bool _skyLoggedFirstDraw;

    private const string SkyVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; };
layout(push_constant) uniform SkyPC { vec4 uSky0; };
layout(location = 0) in vec4 inSkyPos;
layout(location = 1) in vec4 inSkyColor;
layout(location = 2) in vec2 inSkyUv;
layout(location = 0) out vec2 fUv;
layout(location = 1) out vec4 fColor;
void main() {
    // Retail shell vertices are HALF4 and the RSX VP performs DP4s on all four authored
    // components, so the authored position is a genuine homogeneous point. Carry W through the
    // rotation chain (rotations keep the last row identity) and into projection instead of
    // replacing it with 1. The 0x580260 matrix chain centres the shell on the camera, so only the
    // rotational part of view remains on XYZ here.
    vec4 p = inSkyPos;
    float sp = sin(uSky0.x), cp = cos(uSky0.x);
    p.xyz = vec3(cp * p.x + sp * p.z, p.y, -sp * p.x + cp * p.z);
    float sz = sin(uSky0.y), cz = cos(uSky0.y);
    p.xyz = vec3(cz * p.x - sz * p.y, sz * p.x + cz * p.y, p.z);
    float sy = sin(uSky0.z), cy = cos(uSky0.z);
    p.xyz = vec3(cy * p.x + sy * p.z, p.y, -sy * p.x + cy * p.z);
    vec4 viewPos = vec4(mat3(uView) * p.xyz, p.w);
    gl_Position = uProj * viewPos;
    // Pin depth to the far plane after projection, matching the recovered VP (DP4 with c35 into both
    // HPOS.z and HPOS.w) and the pass-17 Clouds stage. Only z is touched, so the mapping is unchanged;
    // depth test and write are off for this pass. Retail shell radii are 1.0..3.6 (2026-09-16 corpus
    // census), so this is contract consistency, not a clipping fix. Authored W is 1.0 corpus-wide.
    gl_Position.z = gl_Position.w;
    fUv = inSkyUv;
    fColor = inSkyColor;
}";

    private const string SkyFragmentGlsl = @"#version 450
layout(set = 1, binding = 0) uniform sampler2D uSky;
layout(location = 0) in vec2 fUv;
layout(location = 1) in vec4 fColor;
layout(location = 0) out vec4 outColor;
void main() {
    // PROVEN (retail Color-shell FP): out = texture(tex0, TEX0) * TEX1, where TEX1 is the vertex
    // colour. RGB and alpha are both modulated; the texture fetch is skipped when TEX1 is zero.
    // Colour passes 15/18 blend RGB with retail SRC_ALPHA / ONE_MINUS_SRC_ALPHA
    // (emission sites identical in the NPUA80965 EBOOT). That
    // blend is only meaningful if the fragment output carries real coverage.
    //
    // Retail corpus census (19 levels, 2026-09-16): all 143 Color shell textures are DXT5 with
    // CONTROL1 0xAAE4 (identity) and no gamma, so logical alpha is the authored DXT5 alpha; authored
    // vertex alpha spans 0..255 on most levels. Forcing alpha to 1 turned every Color shell into an
    // opaque overwrite, so a level with 11 ordered shells only ever showed the last one per pixel.
    //
    vec4 texel = texture(uSky, fUv);
    outColor = vec4(texel.rgb * fColor.rgb, texel.a * fColor.a);
}";

    private const string SkyDomeVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; };
layout(push_constant) uniform DomePC { vec4 uC42; vec4 uC0; };
layout(location = 0) in vec4 inDomePos;
layout(location = 1) in vec4 inDomeColor;
layout(location = 2) in vec2 inDomeProjected;
layout(location = 0) out vec4 fColor;
layout(location = 1) out float fBlend;
void main() {
    // Same contract as the SkyShell and Clouds stages: carry the generated homogeneous W and pin
    // depth to the far plane so the Dome cannot be near/far clipped by the editor camera.
    vec4 viewPos = vec4(mat3(uView) * inDomePos.xyz, inDomePos.w);
    vec4 clip = uProj * viewPos;
    gl_Position = vec4(clip.x, clip.y, clip.w, clip.w);
    fColor = inDomeColor;
    // PROVEN (retail Dome VP, RPCS3 cache + EBOOT): TEX4.x = saturate((pos.y - c42.x) * c42.y).
    // The SkyDefs gradient only spans part of the Dome, so without the saturate the fragment mix
    // extrapolated past both endpoints - on Metropolis the factor ran from -0.64 at the zenith to 1.58
    // at the lower ring, pushing the top beyond the zenith colour and the bottom beyond C0.
    fBlend = clamp((inDomePos.y - uC42.x) * uC42.y, 0.0, 1.0);
}";

    private const string SkyDomeFragmentGlsl = @"#version 450
layout(push_constant) uniform DomePC { vec4 uC42; vec4 uC0; };
layout(location = 0) in vec4 fColor;
layout(location = 1) in float fBlend;
layout(location = 0) out vec4 outColor;
void main() {
    vec4 colour = mix(fColor, uC0, fBlend);
    outColor = vec4(colour.rgb, 1.0);
}";

    /// <summary>
    /// Installs the current authored Sky packet into this renderer. AssetManager calls this immediately
    /// after constructing a renderer, before its first frame, so no in-flight GPU work needs draining.
    /// </summary>
    public void SetSky(VkSkyScene? scene)
    {
        DestroySkyResources();
        _skyScene = scene;
        if (scene is not { HasGeometry: true })
            return;

        if (scene.HasShellGeometry)
        {
            ulong vSize = checked((ulong)scene.VertexData.Length * sizeof(float));
            (_skyVertexBuffer, _skyVbMemory) = CreateBuffer(
                vSize, VkBufferUsageFlags.VertexBuffer,
                VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            void* vp;
            Check(_api.vkMapMemory(_skyVbMemory, 0, vSize, 0, &vp), "vkMapMemory(skyVB)");
            fixed (float* src = scene.VertexData) Buffer.MemoryCopy(src, vp, vSize, vSize);
            _api.vkUnmapMemory(_skyVbMemory);

            ulong iSize = checked((ulong)scene.Indices.Length * sizeof(uint));
            (_skyIndexBuffer, _skyIbMemory) = CreateBuffer(
                iSize, VkBufferUsageFlags.IndexBuffer,
                VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            void* ip;
            Check(_api.vkMapMemory(_skyIbMemory, 0, iSize, 0, &ip), "vkMapMemory(skyIB)");
            fixed (uint* src = scene.Indices) Buffer.MemoryCopy(src, ip, iSize, iSize);
            _api.vkUnmapMemory(_skyIbMemory);
        }

        if (scene.Dome is { HasGeometry: true } dome)
        {
            ulong vSize = checked((ulong)dome.VertexData.Length * sizeof(float));
            (_skyDomeVertexBuffer, _skyDomeVbMemory) = CreateBuffer(
                vSize, VkBufferUsageFlags.VertexBuffer,
                VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            void* vp;
            Check(_api.vkMapMemory(_skyDomeVbMemory, 0, vSize, 0, &vp), "vkMapMemory(skyDomeVB)");
            fixed (float* src = dome.VertexData) Buffer.MemoryCopy(src, vp, vSize, vSize);
            _api.vkUnmapMemory(_skyDomeVbMemory);

            ulong iSize = checked((ulong)dome.Indices.Length * sizeof(uint));
            (_skyDomeIndexBuffer, _skyDomeIbMemory) = CreateBuffer(
                iSize, VkBufferUsageFlags.IndexBuffer,
                VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            void* ip;
            Check(_api.vkMapMemory(_skyDomeIbMemory, 0, iSize, 0, &ip), "vkMapMemory(skyDomeIB)");
            fixed (uint* src = dome.Indices) Buffer.MemoryCopy(src, ip, iSize, iSize);
            _api.vkUnmapMemory(_skyDomeIbMemory);
        }

        CreateSkyDescriptors(scene);
        CreateSkyPipelines(scene);
        Console.WriteLine(
            $"[VkRenderer][Sky] direct pass ready: Dome={(scene.HasDome ? "yes" : "no")}, " +
            $"Clouds={(scene.HasClouds ? "yes" : "no")}, shells={scene.Draws.Count(d => !d.Bloom)}, " +
            $"textures={scene.Textures.Length}. Bloom remains proof-gated.");
    }

    /// <summary>
    /// Swaps the sky packet of a renderer that is already drawing (the editor's per-sky visibility).
    /// The swap waits for the start of the next <see cref="Frame"/>: this is called from the UI pass,
    /// which runs AFTER Frame recorded the sky's pipelines and buffers into a command buffer and BEFORE
    /// SubmitFrame sends it - destroying them here made that submit fail with VK_ERROR_DEVICE_LOST
    /// (measured: ticking a second Seaside sky on). At the next Frame nothing recorded is pending, the
    /// frames in flight are drained, and the old packet can go.
    /// </summary>
    public void ReplaceSky(VkSkyScene? scene)
    {
        _replacementSky = scene;
        _hasReplacementSky = true;
    }

    private VkSkyScene? _replacementSky;
    private bool _hasReplacementSky;

    private void ApplyReplacementSky()
    {
        if (!_hasReplacementSky) return;
        _hasReplacementSky = false;
        WaitForPendingFrames();
        _api.vkDeviceWaitIdle();
        SetSky(_replacementSky);
        _replacementSky = null;
    }

    private void CreateSkyDescriptors(VkSkyScene scene)
    {
        if (scene.Textures.Length == 0)
            return;

        var binding = new VkDescriptorSetLayoutBinding
        {
            binding = 0,
            descriptorType = VkDescriptorType.CombinedImageSampler,
            descriptorCount = 1,
            stageFlags = VkShaderStageFlags.Fragment,
        };
        var layoutInfo = new VkDescriptorSetLayoutCreateInfo
        {
            bindingCount = 1,
            pBindings = &binding,
        };
        VkDescriptorSetLayout layout;
        Check(_api.vkCreateDescriptorSetLayout(&layoutInfo, &layout), "vkCreateDescriptorSetLayout(sky)");
        _skyTextureLayout = layout;

        var poolSize = new VkDescriptorPoolSize
        {
            type = VkDescriptorType.CombinedImageSampler,
            descriptorCount = (uint)scene.Textures.Length,
        };
        var poolInfo = new VkDescriptorPoolCreateInfo
        {
            maxSets = (uint)scene.Textures.Length,
            poolSizeCount = 1,
            pPoolSizes = &poolSize,
        };
        VkDescriptorPool pool;
        Check(_api.vkCreateDescriptorPool(&poolInfo, &pool), "vkCreateDescriptorPool(sky)");
        _skyDescriptorPool = pool;

        _skyTextureSets = new VkDescriptorSet[scene.Textures.Length];
        _skyTextureViews = new VkImageView[scene.Textures.Length];
        _skySamplers = new VkSampler[scene.Textures.Length];
        for (int i = 0; i < scene.Textures.Length; i++)
        {
            var texture = scene.Textures[i];
            VkImage image = _ctx.BackendInfo.GetVkImage(texture);
            var viewInfo = new VkImageViewCreateInfo
            {
                image = image,
                viewType = VkImageViewType.Image2D,
                format = ColorFormat,
                components = default,
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = VkImageAspectFlags.Color,
                    baseMipLevel = 0,
                    levelCount = Math.Max(1u, texture.MipLevels),
                    baseArrayLayer = 0,
                    layerCount = 1,
                },
            };
            VkImageView view;
            Check(_api.vkCreateImageView(&viewInfo, &view), "vkCreateImageView(sky)");
            _skyTextureViews[i] = view;

            VkSampler slotSampler;
            if (i < scene.TextureDescriptors.Length && scene.TextureDescriptors[i] is { } authoredSampler)
            {
                slotSampler = CreateSkySampler(authoredSampler, i).Sampler;
            }
            else
            {
                // Old-engine Sky sources are expected to carry a native descriptor. Keep an explicit
                // fallback for malformed/incomplete packets, but never label it as retail-equivalent.
                var compat = new VkSamplerCreateInfo
                {
                    magFilter = VkFilter.Linear,
                    minFilter = VkFilter.Linear,
                    mipmapMode = VkSamplerMipmapMode.Linear,
                    addressModeU = VkSamplerAddressMode.Repeat,
                    addressModeV = VkSamplerAddressMode.Repeat,
                    addressModeW = VkSamplerAddressMode.Repeat,
                    minLod = 0f,
                    maxLod = Math.Max(0f, texture.MipLevels - 1f),
                    mipLodBias = 0f,
                    maxAnisotropy = 1f,
                };
                Check(_api.vkCreateSampler(&compat, &slotSampler), $"vkCreateSampler(sky[{i}] compat)");
                LunaLog.LogWarning($"[Sky][Sampler][COMPAT] slot={i} has no authored old-engine descriptor; using Repeat+Linear fallback.");
            }
            _skySamplers[i] = slotSampler;

            VkDescriptorSetLayout setLayout = _skyTextureLayout;
            var allocInfo = new VkDescriptorSetAllocateInfo
            {
                descriptorPool = _skyDescriptorPool,
                descriptorSetCount = 1,
                pSetLayouts = &setLayout,
            };
            VkDescriptorSet set;
            Check(_api.vkAllocateDescriptorSets(&allocInfo, &set), "vkAllocateDescriptorSets(sky)");
            _skyTextureSets[i] = set;

            var imageInfo = new VkDescriptorImageInfo
            {
                sampler = slotSampler,
                imageView = view,
                imageLayout = VkImageLayout.ShaderReadOnlyOptimal,
            };
            var write = new VkWriteDescriptorSet
            {
                dstSet = set,
                dstBinding = 0,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.CombinedImageSampler,
                pImageInfo = &imageInfo,
            };
            _api.vkUpdateDescriptorSets(1, &write, 0, null);
        }
    }

    private void CreateSkyPipelines(VkSkyScene scene)
    {
        VkPipelineCache cache = GetPipelineCache();
        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        var mask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A;
        VkDynamicState* dyn = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        var dynState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dyn };
        var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
        var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var raster = new VkPipelineRasterizationStateCreateInfo
        {
            polygonMode = VkPolygonMode.Fill,
            cullMode = VkCullModeFlags.None,
            frontFace = VkFrontFace.CounterClockwise,
            lineWidth = 1f,
        };
        var noDepth = new VkPipelineDepthStencilStateCreateInfo
        {
            depthTestEnable = false,
            depthWriteEnable = false,
            depthCompareOp = VkCompareOp.Always,
        };

        if (scene.HasShellGeometry && _skyTextureLayout.Handle != 0)
        {
            _skyVs = Module(SkyVertexGlsl, ShaderStages.Vertex);
            _skyFs = Module(SkyFragmentGlsl, ShaderStages.Fragment);

            VkDescriptorSetLayout* sets = stackalloc VkDescriptorSetLayout[2] { _descLayout, _skyTextureLayout };
            var push = new VkPushConstantRange
            {
                stageFlags = VkShaderStageFlags.Vertex,
                offset = 0,
                size = 16,
            };
            var layoutInfo = new VkPipelineLayoutCreateInfo
            {
                setLayoutCount = 2,
                pSetLayouts = sets,
                pushConstantRangeCount = 1,
                pPushConstantRanges = &push,
            };
            VkPipelineLayout layout;
            Check(_api.vkCreatePipelineLayout(&layoutInfo, &layout), "vkCreatePipelineLayout(sky)");
            _skyLayout = layout;

            var binding = new VkVertexInputBindingDescription
            {
                binding = 0,
                stride = SkyVertexStride,
                inputRate = VkVertexInputRate.Vertex,
            };
            VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[3];
            attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 0 };
            attrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 16 };
            attrs[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32Sfloat, offset = 32 };
            var vertexInput = new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = 1,
                pVertexBindingDescriptions = &binding,
                vertexAttributeDescriptionCount = 3,
                pVertexAttributeDescriptions = attrs,
            };
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _skyVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _skyFs, pName = entry };
            var blendAttach = new VkPipelineColorBlendAttachmentState
            {
                blendEnable = true,
                srcColorBlendFactor = VkBlendFactor.SrcAlpha,
                dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                colorBlendOp = VkBlendOp.Add,
                // Retail SET_BLEND_FUNC words 0x00000302 / 0x03030303 use ZERO/ONE_MINUS_SRC_ALPHA
                // on the alpha channel, but on RSX that channel drives the destination-alpha Bloom
                // chain. Replaying it against the host colour target instead ERASES the editor's
                // compositing coverage: with the fragment committing src.a = 1 it evaluates to
                // dst.a = 0*1 + (1-1)*dst.a = 0, so every pixel the Sky covered became fully
                // transparent and the shells vanished behind the ImGui background. Accumulate
                // coverage like every other blended pipeline in this renderer; the authored retail
                // alpha state is preserved for the Bloom reverse in SkyRenderOptions.RetailClearAlpha.
                srcAlphaBlendFactor = VkBlendFactor.One,
                dstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                alphaBlendOp = VkBlendOp.Add,
                colorWriteMask = mask,
            };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            var info = new VkGraphicsPipelineCreateInfo
            {
                stageCount = 2,
                pStages = stages,
                pVertexInputState = &vertexInput,
                pInputAssemblyState = &inputAssembly,
                pViewportState = &viewportState,
                pRasterizationState = &raster,
                pMultisampleState = &multisample,
                pDepthStencilState = &noDepth,
                pColorBlendState = &blend,
                pDynamicState = &dynState,
                layout = _skyLayout,
                renderPass = _rpOpaque,
                subpass = 0,
            };
            VkPipeline pipeline;
            Check(_api.vkCreateGraphicsPipelines(cache, 1, &info, &pipeline), "vkCreateGraphicsPipelines(sky)");
            _pipelineSky = pipeline;
        }

        if (scene.Dome is { HasGeometry: true })
        {
            _skyDomeVs = Module(SkyDomeVertexGlsl, ShaderStages.Vertex);
            _skyDomeFs = Module(SkyDomeFragmentGlsl, ShaderStages.Fragment);

            VkDescriptorSetLayout set0 = _descLayout;
            var push = new VkPushConstantRange
            {
                stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment,
                offset = 0,
                size = 32,
            };
            var layoutInfo = new VkPipelineLayoutCreateInfo
            {
                setLayoutCount = 1,
                pSetLayouts = &set0,
                pushConstantRangeCount = 1,
                pPushConstantRanges = &push,
            };
            VkPipelineLayout layout;
            Check(_api.vkCreatePipelineLayout(&layoutInfo, &layout), "vkCreatePipelineLayout(skyDome)");
            _skyDomeLayout = layout;

            var binding = new VkVertexInputBindingDescription
            {
                binding = 0,
                stride = SkyDomeVertexStride,
                inputRate = VkVertexInputRate.Vertex,
            };
            VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[3];
            attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 0 };
            attrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 16 };
            attrs[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32Sfloat, offset = 32 };
            var vertexInput = new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = 1,
                pVertexBindingDescriptions = &binding,
                vertexAttributeDescriptionCount = 3,
                pVertexAttributeDescriptions = attrs,
            };
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _skyDomeVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _skyDomeFs, pName = entry };
            var blendAttach = new VkPipelineColorBlendAttachmentState
            {
                blendEnable = false,
                colorWriteMask = mask,
            };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            var info = new VkGraphicsPipelineCreateInfo
            {
                stageCount = 2,
                pStages = stages,
                pVertexInputState = &vertexInput,
                pInputAssemblyState = &inputAssembly,
                pViewportState = &viewportState,
                pRasterizationState = &raster,
                pMultisampleState = &multisample,
                pDepthStencilState = &noDepth,
                pColorBlendState = &blend,
                pDynamicState = &dynState,
                layout = _skyDomeLayout,
                renderPass = _rpOpaque,
                subpass = 0,
            };
            VkPipeline pipeline;
            Check(_api.vkCreateGraphicsPipelines(cache, 1, &info, &pipeline), "vkCreateGraphicsPipelines(skyDome)");
            _pipelineSkyDome = pipeline;
        }

        CreateSkyCloudPipeline(scene);
    }

    private void DrawSky()
    {
        var scene = _skyScene;
        if (!SkyRenderOptions.Enabled || scene is not { HasGeometry: true })
            return;

        VkDescriptorSet sceneSet = _descSet;
        float retailTime = UpdateSkyRuntime(scene);

        if (scene.Dome is { HasGeometry: true } dome &&
            _pipelineSkyDome.Handle != 0 &&
            _skyDomeVertexBuffer.Handle != 0 &&
            _skyDomeIndexBuffer.Handle != 0)
        {
            _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _pipelineSkyDome);
            _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _skyDomeLayout, 0, 1, &sceneSet, 0, null);
            VkBuffer vb = _skyDomeVertexBuffer;
            ulong offset = 0;
            _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
            _api.vkCmdBindIndexBuffer(_cmd, _skyDomeIndexBuffer, 0, VkIndexType.Uint32);
            Vector4* pc = stackalloc Vector4[2];
            pc[0] = dome.VertexConstant42;
            pc[1] = dome.FragmentConstant0;
            _api.vkCmdPushConstants(
                _cmd, _skyDomeLayout,
                VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment,
                0, 32, pc);
            _api.vkCmdDrawIndexed(_cmd, (uint)dome.Indices.Length, 1, 0, 0, 0);
        }

        bool canDrawShells = scene.HasShellGeometry &&
            _pipelineSky.Handle != 0 &&
            _skyVertexBuffer.Handle != 0 &&
            _skyIndexBuffer.Handle != 0;
        bool cloudsSubmitted = false;
        int submitted = 0;

        // Draws are sorted by retail pass. Insert pass 17 exactly between pre-shell pass 15/16 and
        // post-shell pass 18/19. Bloom records remain skipped, but they still preserve the ordering
        // boundary so Clouds are never accidentally moved after post-shell imagery.
        foreach (var draw in scene.Draws)
        {
            if (!cloudsSubmitted && draw.RetailPassId > SkyData.CloudsPassId)
            {
                DrawSkyCloudPass(scene, sceneSet);
                cloudsSubmitted = true;
            }

            if (!canDrawShells ||
                draw.Bloom ||
                (draw.RetailPassId != SkyData.ColorPreShellPassId && draw.RetailPassId != SkyData.ColorPostShellPassId) ||
                draw.TextureSlot < 0 || draw.TextureSlot >= _skyTextureSets.Length)
                continue;

            // Clouds bind a different pipeline/layout and the Dome vertex buffer. Rebind the authored
            // SkyShell state for every Color draw so pass 18 cannot inherit pass-17 state.
            BindSkyShellState(sceneSet);
            VkDescriptorSet textureSet = _skyTextureSets[draw.TextureSlot];
            _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _skyLayout, 1, 1, &textureSet, 0, null);
            float phase = (draw.InitialPhase + draw.SpinRate * retailTime) % SkyTwoPi;
            Vector4 pc = new(phase, draw.Rotation10, draw.Rotation14, 0f);
            _api.vkCmdPushConstants(_cmd, _skyLayout, VkShaderStageFlags.Vertex, 0, 16, &pc);
            _api.vkCmdDrawIndexed(_cmd, draw.IndexCount, 1, draw.FirstIndex, 0, 0);
            submitted++;
        }

        if (!cloudsSubmitted)
            DrawSkyCloudPass(scene, sceneSet);

        if (!_skyLoggedFirstDraw)
        {
            _skyLoggedFirstDraw = true;
            Console.WriteLine($"[VkRenderer][Sky] first frame submitted {submitted} Color SkyShell draw(s) in retail 15/17/18 order.");
        }
    }

    private void DestroySkyResources()
    {
        DestroySkyCloudResources();

        if (_pipelineSky.Handle != 0) _api.vkDestroyPipeline(_pipelineSky);
        if (_pipelineSkyDome.Handle != 0) _api.vkDestroyPipeline(_pipelineSkyDome);
        if (_skyLayout.Handle != 0) _api.vkDestroyPipelineLayout(_skyLayout);
        if (_skyDomeLayout.Handle != 0) _api.vkDestroyPipelineLayout(_skyDomeLayout);
        if (_skyVs.Handle != 0) _api.vkDestroyShaderModule(_skyVs);
        if (_skyFs.Handle != 0) _api.vkDestroyShaderModule(_skyFs);
        if (_skyDomeVs.Handle != 0) _api.vkDestroyShaderModule(_skyDomeVs);
        if (_skyDomeFs.Handle != 0) _api.vkDestroyShaderModule(_skyDomeFs);

        foreach (var view in _skyTextureViews)
            if (view.Handle != 0) _api.vkDestroyImageView(view);
        foreach (var sampler in _skySamplers)
            if (sampler.Handle != 0) _api.vkDestroySampler(sampler);
        if (_skyDescriptorPool.Handle != 0) _api.vkDestroyDescriptorPool(_skyDescriptorPool);
        if (_skyTextureLayout.Handle != 0) _api.vkDestroyDescriptorSetLayout(_skyTextureLayout);

        if (_skyIndexBuffer.Handle != 0) _api.vkDestroyBuffer(_skyIndexBuffer);
        if (_skyIbMemory.Handle != 0) _api.vkFreeMemory(_skyIbMemory);
        if (_skyVertexBuffer.Handle != 0) _api.vkDestroyBuffer(_skyVertexBuffer);
        if (_skyVbMemory.Handle != 0) _api.vkFreeMemory(_skyVbMemory);
        if (_skyDomeIndexBuffer.Handle != 0) _api.vkDestroyBuffer(_skyDomeIndexBuffer);
        if (_skyDomeIbMemory.Handle != 0) _api.vkFreeMemory(_skyDomeIbMemory);
        if (_skyDomeVertexBuffer.Handle != 0) _api.vkDestroyBuffer(_skyDomeVertexBuffer);
        if (_skyDomeVbMemory.Handle != 0) _api.vkFreeMemory(_skyDomeVbMemory);

        _pipelineSky = default;
        _pipelineSkyDome = default;
        _skyLayout = default;
        _skyDomeLayout = default;
        _skyVs = default;
        _skyFs = default;
        _skyDomeVs = default;
        _skyDomeFs = default;
        _skyTextureViews = [];
        _skyTextureSets = [];
        _skySamplers = [];
        _skyDescriptorPool = default;
        _skyTextureLayout = default;
        _skyIndexBuffer = default;
        _skyIbMemory = default;
        _skyVertexBuffer = default;
        _skyVbMemory = default;
        _skyDomeIndexBuffer = default;
        _skyDomeIbMemory = default;
        _skyDomeVertexBuffer = default;
        _skyDomeVbMemory = default;
        _skyScene = null;
        _skyLoggedFirstDraw = false;
    }
}
