using System.Numerics;
using NeoVeldrid;
using Vortice.Vulkan;
using ReLunacy.Engine.Assets.Sky;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Retail old-engine Sky pass 17. The geometry, projected attributes, texture slots, animation
/// recurrence and fragment algebra below are all recovered from the retail ToD renderer. Bloom is a
/// separate destination-alpha chain and deliberately does not live here.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private VkShaderModule _skyCloudVs;
    private VkShaderModule _skyCloudFs;
    private VkPipelineLayout _skyCloudLayout;
    private VkPipeline _pipelineSkyCloud, _pipelineSkyCloudGlow;
    private bool _skyLoggedFirstCloudDraw;

    private bool _skyRuntimeStarted;
    private double _skyLastClockSeconds;
    private float _skyRetailTime;
    private float _cloudPhase0;
    private float _cloudPhase1;
    private float _cloudDir0 = 1f;
    private float _cloudDir1 = 1f;
    private double _cloudState00;
    private double _cloudState01;
    private double _cloudState10;
    private double _cloudState11;

    private const string SkyCloudVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; };
layout(push_constant) uniform CloudPC {
    vec4 uState0;   // x=T, y=layer0 state0, z=layer0 state1, w=A.x
    vec4 uState1;   // x=layer1 state0, y=layer1 state1, z=A.y, w=B.z layer blend
    vec4 uLayer0A;  // period, angle, primary scroll, bias0
    vec4 uLayer0B;  // secondary scroll, bias1, B.x perturb scale, c42.x
    vec4 uLayer1A;  // period, angle, primary scroll, bias0
    vec4 uLayer1B;  // secondary scroll, bias1, B.y perturb scale, c42.y
    vec4 uColour;   // C
};
layout(location = 0) in vec4 inDomePos;
layout(location = 1) in vec4 inDomeColor;
layout(location = 2) in vec2 inDomeProjected;
layout(location = 0) out vec4 fTex1;
layout(location = 1) out vec4 fTex2;
layout(location = 2) out vec4 fTex3;
layout(location = 3) out float fVerticalFade;

float fmod1(float x) { return x - trunc(x); }

void buildLayer(float T, float state0, float state1, vec4 a, vec2 b,
                out vec4 c0, out vec4 c1, out vec4 c2) {
    const float twoDegrees = 0.03490658476948738;
    const float kY = 0.937;
    const float kX = 1.137;
    const float kW = 1.1319;

    float angle0 = a.y + twoDegrees;
    float q0 = (T + state0) * a.z;
    c0 = vec4(
        fmod1(q0 * sin(angle0)),
        fmod1(q0 * cos(angle0)),
        a.w,
        1.0);

    float angle1 = a.y - twoDegrees;
    float q1 = (T + state1) * a.z;
    c1 = vec4(
        fmod1(q1 * sin(angle1) * kX),
        fmod1(q1 * cos(angle1) * kY),
        a.w * kW,
        kW);

    float angle2 = a.y;
    float q2 = T * (-b.x);
    c2 = vec4(
        fmod1(q2 * sin(angle2)),
        fmod1(q2 * cos(angle2)),
        b.y,
        kW);
}

void main() {
    vec4 c36, c37, c38;
    vec4 c39, c40, c41;
    buildLayer(uState0.x, uState0.y, uState0.z, uLayer0A, uLayer0B.xy, c36, c37, c38);
    buildLayer(uState0.x, uState1.x, uState1.y, uLayer1A, uLayer1B.xy, c39, c40, c41);

    float x = inDomeProjected.x;
    float y = inDomeProjected.y;
    fTex1 = vec4(
        x * c36.z + c36.x,
        y * c36.z + c36.y,
        y * c37.z + c37.y,
        x * c37.z + c37.x);
    fTex2 = vec4(
        x * c39.z + c39.x,
        y * c39.z + c39.y,
        y * c40.z + c40.y,
        x * c40.z + c40.x);
    fTex3 = vec4(
        x * c38.z + c38.x,
        y * c38.z + c38.y,
        x * c41.z + c41.x,
        y * c41.z + c41.y);
    // PROVEN (retail Clouds VP): TEX4.x = saturate((pos.y - c42.x) * c42.y), same as the Dome.
    fVerticalFade = clamp((inDomePos.y - uLayer0B.w) * uLayer1B.w, 0.0, 1.0);

    // Pass 17 shares the Dome position stream. Its recovered VP uses c32/c33/c35 and writes
    // DP4(position,c35) to BOTH HPOS.z and HPOS.w, exactly like the other old-engine Sky stages.
    vec3 viewPos = mat3(uView) * inDomePos.xyz;
    vec4 clip = uProj * vec4(viewPos, inDomePos.w);
    // Far plane of the renderer's reversed depth (VulkanRenderer.ReverseDepth) is z = 0.
    gl_Position = vec4(clip.x, clip.y, 0.0, clip.w);
}";

    private const string SkyCloudFragmentGlsl = @"#version 450
layout(set = 1, binding = 0) uniform sampler2D uTexture4;
layout(set = 2, binding = 0) uniform sampler2D uTexture5;
layout(set = 3, binding = 0) uniform sampler2D uTexture6;
layout(push_constant) uniform CloudPC {
    vec4 uState0;
    vec4 uState1;
    vec4 uLayer0A;
    vec4 uLayer0B;
    vec4 uLayer1A;
    vec4 uLayer1B;
    vec4 uColour;
};
layout(location = 0) in vec4 fTex1;
layout(location = 1) in vec4 fTex2;
layout(location = 2) in vec4 fTex3;
layout(location = 3) in float fVerticalFade;
layout(location = 0) out vec4 outColor;
void main() {
    vec2 perturb0 = texture(uTexture6, fTex3.xy).xy * uLayer0B.z;
    vec2 perturb1 = texture(uTexture6, fTex3.zw).xy * uLayer1B.z;

    vec4 a = texture(uTexture4, fTex1.xy + perturb0);
    vec4 b = texture(uTexture4, fTex1.wz + perturb0);
    vec4 layer0 = mix(a, b, uState0.w);

    vec4 c = texture(uTexture5, fTex2.xy + perturb1);
    vec4 d = texture(uTexture5, fTex2.wz + perturb1);
    vec4 layer1 = mix(c, d, uState1.z);

    vec4 colour = mix(layer0, layer1, uState1.w) * uColour;
    colour.a *= (1.0 - fVerticalFade);
    outColor = colour;
}";

    private void CreateSkyCloudPipeline(VkSkyScene scene)
    {
        ResetSkyRuntime();
        if (!scene.HasClouds || _skyTextureLayout.Handle == 0)
            return;

        _skyCloudVs = Module(SkyCloudVertexGlsl, ShaderStages.Vertex);
        _skyCloudFs = Module(SkyCloudFragmentGlsl, ShaderStages.Fragment);

        VkDescriptorSetLayout* sets = stackalloc VkDescriptorSetLayout[4]
        {
            _descLayout,
            _skyTextureLayout,
            _skyTextureLayout,
            _skyTextureLayout,
        };
        var push = new VkPushConstantRange
        {
            stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment,
            offset = 0,
            size = 7 * 16,
        };
        var layoutInfo = new VkPipelineLayoutCreateInfo
        {
            setLayoutCount = 4,
            pSetLayouts = sets,
            pushConstantRangeCount = 1,
            pPushConstantRanges = &push,
        };
        VkPipelineLayout layout;
        Check(_api.vkCreatePipelineLayout(&layoutInfo, &layout), "vkCreatePipelineLayout(skyClouds)");
        _skyCloudLayout = layout;

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
        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
        var raster = new VkPipelineRasterizationStateCreateInfo
        {
            polygonMode = VkPolygonMode.Fill,
            cullMode = VkCullModeFlags.None,
            frontFace = VkFrontFace.CounterClockwise,
            lineWidth = 1f,
        };
        var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
        // Pass 17 locally proves depth writes off. Its inherited depth-test state is not terminally
        // closed yet; all visible Sky stages are rendered before scene depth writes, so keep the same
        // no-depth editor boundary rather than inventing an inherited compare mode.
        var noDepth = new VkPipelineDepthStencilStateCreateInfo
        {
            depthTestEnable = false,
            depthWriteEnable = false,
            depthCompareOp = VkCompareOp.Always,
        };
        var mask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A;
        var blendAttach = new VkPipelineColorBlendAttachmentState
        {
            blendEnable = true,
            srcColorBlendFactor = VkBlendFactor.SrcAlpha,
            dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
            colorBlendOp = VkBlendOp.Add,
            // Retail SET_BLEND_FUNC words 0x00000302 / 0x03030303 drive the RSX destination-alpha
            // Bloom chain on the alpha channel. Replaying ZERO/ONE_MINUS_SRC_ALPHA against the host
            // colour target instead decays the editor's compositing coverage wherever Clouds draw.
            // Accumulate coverage like the SkyShell and every other blended pipeline here.
            srcAlphaBlendFactor = VkBlendFactor.One,
            dstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
            alphaBlendOp = VkBlendOp.Add,
            // RGB only: alpha is the bloom source (VulkanRenderer.Sky.cs).
            colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B,
        };
        var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
        VkDynamicState* dyn = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        var dynState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dyn };
        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _skyCloudVs, pName = entry };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _skyCloudFs, pName = entry };
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
            layout = _skyCloudLayout,
            renderPass = _rpOpaque,
            subpass = 0,
        };
        VkPipeline pipeline;
        Check(_api.vkCreateGraphicsPipelines(GetPipelineCache(), 1, &info, &pipeline), "vkCreateGraphicsPipelines(skyClouds)");
        _pipelineSkyCloud = pipeline;
        // With the old engine's bloom on: the retail alpha factors, written (A = Adst (1 - As)).
        blendAttach.srcAlphaBlendFactor = VkBlendFactor.Zero;
        blendAttach.colorWriteMask = mask;
        Check(_api.vkCreateGraphicsPipelines(GetPipelineCache(), 1, &info, &pipeline), "vkCreateGraphicsPipelines(skyClouds glow)");
        _pipelineSkyCloudGlow = pipeline;
    }

    private float UpdateSkyRuntime(VkSkyScene scene)
    {
        double now = _skyClock.Elapsed.TotalSeconds;
        if (!_skyRuntimeStarted)
        {
            _skyRuntimeStarted = true;
            _skyLastClockSeconds = now;
            return _skyRetailTime;
        }

        double rawDt = Math.Max(0.0, now - _skyLastClockSeconds);
        _skyLastClockSeconds = now;
        float clampedDt = (float)Math.Min(rawDt, 0.06666667014360428);
        float scaledDt = clampedDt * scene.TimeScale;
        if (scaledDt == 0f)
            return _skyRetailTime;

        _skyRetailTime += scaledDt;
        if (scene.Clouds is { } clouds)
        {
            AdvanceCloudPhase(
                ref _cloudPhase0, ref _cloudDir0,
                ref _cloudState00, ref _cloudState01,
                clouds.Layer0.Period, scaledDt);
            AdvanceCloudPhase(
                ref _cloudPhase1, ref _cloudDir1,
                ref _cloudState10, ref _cloudState11,
                clouds.Layer1.Period, scaledDt);
        }
        return _skyRetailTime;
    }

    private void AdvanceCloudPhase(
        ref float phase,
        ref float direction,
        ref double upperOffset,
        ref double lowerOffset,
        float period,
        float scaledDt)
    {
        phase += scaledDt * direction;
        double wrapped = (_skyRetailTime % 100f) + 200f;
        if (phase >= period)
        {
            upperOffset += wrapped;
            direction = -1f;
        }
        if (phase <= 0f)
        {
            lowerOffset += wrapped;
            direction = 1f;
        }
    }

    private void BindSkyShellState(VkDescriptorSet sceneSet, VkPipeline pipeline)
    {
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, pipeline);
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _skyLayout, 0, 1, &sceneSet, 0, null);
        VkBuffer shellVb = _skyVertexBuffer;
        ulong shellOffset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &shellVb, &shellOffset);
        _api.vkCmdBindIndexBuffer(_cmd, _skyIndexBuffer, 0, VkIndexType.Uint32);
    }

    private void DrawSkyCloudPass(VkSkyScene scene, VkDescriptorSet sceneSet)
    {
        if (scene.Dome is not { HasGeometry: true } dome ||
            scene.Clouds is not { HasTextures: true } clouds ||
            _pipelineSkyCloud.Handle == 0 ||
            _skyCloudLayout.Handle == 0 ||
            _skyDomeVertexBuffer.Handle == 0 ||
            _skyDomeIndexBuffer.Handle == 0)
            return;

        if ((uint)clouds.Texture4Slot >= (uint)_skyTextureSets.Length ||
            (uint)clouds.Texture5Slot >= (uint)_skyTextureSets.Length ||
            (uint)clouds.Texture6Slot >= (uint)_skyTextureSets.Length)
            return;

        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics,
            DestinationAlphaGlow && _pipelineSkyCloudGlow.Handle != 0 ? _pipelineSkyCloudGlow : _pipelineSkyCloud);
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _skyCloudLayout, 0, 1, &sceneSet, 0, null);
        VkDescriptorSet* textureSets = stackalloc VkDescriptorSet[3]
        {
            _skyTextureSets[clouds.Texture4Slot],
            _skyTextureSets[clouds.Texture5Slot],
            _skyTextureSets[clouds.Texture6Slot],
        };
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _skyCloudLayout, 1, 3, textureSets, 0, null);

        VkBuffer vb = _skyDomeVertexBuffer;
        ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _skyDomeIndexBuffer, 0, VkIndexType.Uint32);

        float a0 = clouds.Layer0.Period != 0f ? _cloudPhase0 / clouds.Layer0.Period : 0f;
        float a1 = clouds.Layer1.Period != 0f ? _cloudPhase1 / clouds.Layer1.Period : 0f;
        Vector4* pc = stackalloc Vector4[7];
        pc[0] = new Vector4(_skyRetailTime, (float)_cloudState00, (float)_cloudState01, a0);
        pc[1] = new Vector4((float)_cloudState10, (float)_cloudState11, a1, clouds.LayerBlend);
        pc[2] = new Vector4(clouds.Layer0.Period, clouds.Layer0.Angle, clouds.Layer0.PrimaryScroll, clouds.Layer0.Bias0);
        pc[3] = new Vector4(clouds.Layer0.SecondaryScroll, clouds.Layer0.Bias1, clouds.Texture4PerturbScale, clouds.VertexConstant42.X);
        pc[4] = new Vector4(clouds.Layer1.Period, clouds.Layer1.Angle, clouds.Layer1.PrimaryScroll, clouds.Layer1.Bias0);
        pc[5] = new Vector4(clouds.Layer1.SecondaryScroll, clouds.Layer1.Bias1, clouds.Texture5PerturbScale, clouds.VertexConstant42.Y);
        pc[6] = clouds.ColourMultiplier;
        _api.vkCmdPushConstants(
            _cmd,
            _skyCloudLayout,
            VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment,
            0,
            7 * 16,
            pc);
        _api.vkCmdDrawIndexed(_cmd, (uint)dome.Indices.Length, 1, 0, 0, 0);

        if (!_skyLoggedFirstCloudDraw)
        {
            _skyLoggedFirstCloudDraw = true;
            Console.WriteLine("[VkRenderer][Sky] retail pass 17 Clouds submitted from SkyDefs TEX4/TEX5/TEX6 sources.");
        }
    }

    private void DestroySkyCloudResources()
    {
        if (_pipelineSkyCloud.Handle != 0) _api.vkDestroyPipeline(_pipelineSkyCloud);
        if (_pipelineSkyCloudGlow.Handle != 0) _api.vkDestroyPipeline(_pipelineSkyCloudGlow);
        if (_skyCloudLayout.Handle != 0) _api.vkDestroyPipelineLayout(_skyCloudLayout);
        if (_skyCloudVs.Handle != 0) _api.vkDestroyShaderModule(_skyCloudVs);
        if (_skyCloudFs.Handle != 0) _api.vkDestroyShaderModule(_skyCloudFs);
        _pipelineSkyCloud = default;
        _pipelineSkyCloudGlow = default;
        _skyCloudLayout = default;
        _skyCloudVs = default;
        _skyCloudFs = default;
        _skyLoggedFirstCloudDraw = false;
        ResetSkyRuntime();
    }

    private void ResetSkyRuntime()
    {
        _skyRuntimeStarted = false;
        _skyLastClockSeconds = 0.0;
        _skyRetailTime = 0f;
        _cloudPhase0 = 0f;
        _cloudPhase1 = 0f;
        _cloudDir0 = 1f;
        _cloudDir1 = 1f;
        _cloudState00 = 0.0;
        _cloudState01 = 0.0;
        _cloudState10 = 0.0;
        _cloudState11 = 0.0;
    }
}