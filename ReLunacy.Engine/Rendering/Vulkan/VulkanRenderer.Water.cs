using System.Numerics;
using NeoVeldrid;
using ReLunacy.Engine.Assets.Interfaces;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>A water body to draw: zone 0x6641 (Assets.Water.WaterSurface) as the view hands it over. <paramref name="PickId"/>
/// is the entity id the pick pass writes for it (Scene.EntityWater), NoHit for none.</summary>
public readonly record struct WaterDraw(Vector3 Corner, Vector2 Size, Vector3 Colour, float Absorption, int Tessellation,
    uint PickId = uint.MaxValue, ulong ReflectionCube = 0, ulong ClipMask = 0);

/// <summary>A VerticalFogController to draw: its volume's matrix (the unit box, +-1 on each axis, as the game
/// tests it) and its colour (settings +0x18..+0x1A, 0..1).</summary>
public readonly record struct VerticalFogDraw(Matrix4x4 Box, Vector3 Colour);

/// <summary>
/// Water bodies and vertical fog, drawn after the opaque scene, depth-tested and not depth-written. Both read the
/// scene's depth (a copy taken with the scene colour copy, see CopySceneDepth), so they know how much of a view ray
/// lies under the water or in the fog.
///
/// Water (zone 0x6641): the editor uses an indexed grid with ambient waves and optional impulses.
/// ACIT's SPU generates its own mesh batches each frame. When available, the ACIT fragment programs
/// handle absorption and reflections. Absorption of 100 or more selects the opaque program.
///
/// Vertical fog (A Crack in Time VerticalFogController 0x472): the game's Init (0x00349D14) keeps the four corners
/// of its volume's +Y face, the fog density 0.0413 / |Y axis| (+0x1C) and the colour bytes as floats (+0x70);
/// each frame it draws that face as one quad (0x003499B4, through 0x005BB5A8: VP 0xDB05F0, FP 0xD904C0 = entry
/// 0x2C5 of the 0xDE2340 table, scene depth on unit 1, alpha blended, depth tested). Per pixel its alpha is
/// density x the scene point's height below the face x the view-depth gap between face and scene, saturated.
/// The quad here does the same. Its unit 0 texture (vfx_system texture 13, sampled at world XZ) only tints the
/// colour and is left out.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    private const string WaterVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC {
    vec4 uCorner; vec4 uSize; vec4 uColour; vec4 uCamera;
    vec4 uMotion; vec4 uClip; vec4 uRipples[2];
};
layout(location = 0) out vec3 fWorld;
layout(location = 1) out vec3 fNormal;
layout(location = 2) flat out mat4 fInverse;
// Three preview waves (direction XZ, wavelength, speed). The ACIT SPU uses spectral bands.
const vec4 W[3] = vec4[3](vec4(0.8, 0.6, 7.0, 1.6), vec4(-0.45, 0.89, 3.9, 1.1), vec4(0.2, -0.98, 2.3, 0.8));
void main() {
    int n = int(uCorner.w);
    vec2 f = vec2(gl_VertexIndex % (n + 1), gl_VertexIndex / (n + 1)) / float(n);
    vec3 w = vec3(uCorner.x + f.x * uSize.x, uCorner.y, uCorner.z + f.y * uSize.y);
    // Fade the waves out at the rim so the surface meets its banks.
    float rim = clamp(min(min(f.x, 1.0 - f.x) * uSize.x, min(f.y, 1.0 - f.y) * uSize.y) * 0.5, 0.0, 1.0);
    float amplitude = uSize.w * rim;
    vec2 slope = vec2(0.0);
    for (int i = 0; i < 3; i++) {
        float kw = 6.2831853 / W[i].z;
        // ACIT 0x675518: gravity and capillary dispersion in radians per second.
        float omega = uMotion.x > 0.5 ? sqrt(kw * (9.80665 + 0.0000728811865 * kw * kw)) : W[i].w * kw;
        float phase = dot(W[i].xy, w.xz) * kw + uSize.z * omega;
        float a = amplitude / float(i + 1);
        w.y += a * sin(phase);
        slope += W[i].xy * (a * kw * cos(phase));
    }
    for (int i = 0; i < int(uMotion.y); i++) {
        vec4 ripple = uRipples[i]; // centre XZ, elapsed seconds, height
        vec2 offset = w.xz - ripple.xy;
        float radius = length(offset);
        const float k = 7.0;
        const float omega = sqrt(k * (9.80665 + 0.0000728811865 * k * k));
        const float groupSpeed = (9.80665 + 3.0 * 0.0000728811865 * k * k) / (2.0 * omega);
        float travel = radius - ripple.z * groupSpeed;
        float envelope = ripple.w * rim * exp(-1.5 * travel * travel - 0.85 * ripple.z);
        float phase = k * radius - omega * ripple.z;
        w.y += envelope * cos(phase);
        slope += offset / max(radius, 1e-4) * envelope * (-k * sin(phase) - 3.0 * travel * cos(phase));
    }
    fWorld = w;
    fNormal = normalize(vec3(-slope.x, 1.0, -slope.y));
    fInverse = inverse(uMvp);
    gl_Position = uMvp * vec4(w, 1.0);
}";

    private const string WaterClipGlsl = @"
layout(set = 1, binding = 2) uniform sampler2DArray uWaterClips;
float waterClipValue(vec2 uv, vec3 clip) {
    if (clip.z < 0.5) return 1.0;
    vec2 pixel = clamp(uv * clip.xy, vec2(0.5), clip.xy - 0.5);
    return texture(uWaterClips, vec3(pixel / vec2(textureSize(uWaterClips, 0).xy), clip.z)).r;
}
bool waterClipped(vec2 uv, vec3 clip) { return waterClipValue(uv, clip) < 0.5; }
";

    private const string WaterPickVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform UBO { mat4 uMvp; mat4 uView; mat4 uProj; mat4 uPickMvp; };
layout(push_constant) uniform PC { vec4 uCorner; vec4 uSize; vec4 uClip; uvec4 uId; };
layout(location = 0) out vec2 fUv;
void main() {
    const vec2 corners[6] = vec2[6](vec2(0,0), vec2(1,0), vec2(1,1), vec2(0,0), vec2(1,1), vec2(0,1));
    fUv = corners[gl_VertexIndex];
    vec2 xz = fUv * uSize.xy;
    gl_Position = uPickMvp * vec4(uCorner.xyz + vec3(xz.x, 0.0, xz.y), 1.0);
}";

    private const string WaterPickFragmentGlsl = @"#version 450
" + WaterClipGlsl + @"
layout(push_constant) uniform PC { vec4 uCorner; vec4 uSize; vec4 uClip; uvec4 uId; };
layout(location = 0) in vec2 fUv;
layout(location = 0) out vec4 o;
void main() {
    if (waterClipped(fUv, uClip.xyz)) discard;
    o = vec4((uvec4(uId.x) >> uvec4(0, 8, 16, 24)) & 255u) / 255.0;
}";

    private const string WaterFragmentGlsl = @"#version 450
" + WaterClipGlsl + @"
layout(set = 1, binding = 0) uniform sampler2D uSceneDepth;
layout(set = 1, binding = 1) uniform sampler2D uSceneColour;
layout(push_constant) uniform PC { vec4 uCorner; vec4 uSize; vec4 uColour; vec4 uCamera; vec4 uMotion; vec4 uClip; };
layout(location = 0) in vec3 fWorld;
layout(location = 1) in vec3 fNormal;
layout(location = 2) flat in mat4 fInverse;
layout(location = 0) out vec4 o;
vec3 sceneWorld(ivec2 p, vec2 size) {
    float z = texelFetch(uSceneDepth, p, 0).r;
    vec2 ndc = vec2(gl_FragCoord.x / size.x * 2.0 - 1.0, 1.0 - gl_FragCoord.y / size.y * 2.0);
    vec4 w = fInverse * vec4(ndc, z, 1.0);
    return w.xyz / w.w;
}
void main() {
    if (waterClipped((fWorld.xz - uCorner.xz) / uSize.xy, uClip.xyz)) discard;
    vec2 size = vec2(textureSize(uSceneDepth, 0));
    ivec2 p = ivec2(gl_FragCoord.xy);
    vec3 view = fWorld - uCamera.xyz;
    float surface = length(view);
    vec3 v = view / max(surface, 1e-5);
    vec3 n = normalize(fNormal);
    if (dot(n, v) > 0.0) n = -n;
    float cosine = clamp(-dot(n, v), 0.0, 1.0);
    float fresnel = 0.02 + 0.98 * pow(1.0 - cosine, 5.0);
    // Sky tint: what a calm surface mirrors, brighter towards the horizon.
    vec3 sky = mix(vec3(0.30, 0.38, 0.45), vec3(0.55, 0.65, 0.75), pow(1.0 - cosine, 2.0));
    vec3 deep = max(uColour.rgb, vec3(0.0));
    vec3 under;
    // Cleared reverse depth (nothing behind, the sky writes none) counts as bottomless.
    if (uCamera.w > 0.5 || texelFetch(uSceneDepth, p, 0).r <= 0.0) {
        under = deep;
    } else {
        float depth = max(dot(sceneWorld(p, size) - uCamera.xyz, v) - surface, 0.0);
        float keep = exp(-uColour.w * depth);
        under = mix(deep, texelFetch(uSceneColour, p, 0).rgb, keep);
    }
    o = vec4(mix(under, sky, fresnel), 1.0);
}";

    private static string RetailWaterSource(Loading.Shaders.NewEngineShaderVariant variant, bool clipped)
    {
        var shader = RsxFragmentTranslator.Translate(variant.Microcode, variant.SlotGroups(),
            variant.Exports32, out string? failure);
        if (shader == null)
            throw new InvalidDataException(failure);
        if (shader.TextureUnits.Any(unit => unit > 5 && !(clipped && unit == 13)) ||
            shader.Groups.Any(group => group is < 1 or > 10) ||
            shader.Inputs.Any(input => input is not ("WPOS" or "TEX0" or "TEX1" or "TEX2") && !(clipped && input == "TEX5")))
            throw new InvalidDataException("Unsupported fragment program bindings.");
        return "#version 450\n" + (clipped ? "#define WATER_RSX_CLIP\n" : "") +
            RetailWaterLibraryGlsl + shader.Body + RetailWaterMainGlsl;
    }

    private const string RetailWaterLibraryGlsl = WaterClipGlsl + @"
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(set = 0, binding = 7) uniform samplerCube uWaterCube;
layout(set = 1, binding = 0) uniform sampler2D uSceneDepth;
layout(set = 1, binding = 1) uniform sampler2D uSceneColour;
layout(push_constant) uniform PC { vec4 uCorner; vec4 uSize; vec4 uColour; vec4 uCamera; vec4 uMotion; vec4 uClip; };
layout(location = 0) in vec3 fWorld;
layout(location = 1) in vec3 fNormal;
layout(location = 2) flat in mat4 fInverse;
layout(location = 0) out vec4 o;
vec4 WPOS, TEX0, TEX1, TEX2, TEX5;
vec4 G1, G2, G3, G4, G5, G6, G7, G8, G9, G10;
vec4 rsxNormalize(vec4 v) { return vec4(normalize(v.xyz), 0.0); }
vec4 rsxProject(vec4 c) {
    vec2 uv = c.w > 1e-6 ? c.xy / c.w : gl_FragCoord.xy / vec2(textureSize(uSceneDepth, 0));
    return vec4(uv, c.w > 1e-6 ? 1.0 : 0.0, 1.0);
}
// The vertex grid supplies the wave slope in TEX1.xy. Neutral detail samples keep that slope.
vec4 rsxTex0(vec4 c) { return vec4(0.0); }
vec4 rsxTex1(vec4 c) { return vec4(0.0); }
vec4 rsxTex2(vec4 c) {
    // The game samples packed reciprocal eye depth. Reconstruct it from the editor's depth copy.
    vec2 size = vec2(textureSize(uSceneDepth, 0));
    vec2 uv = clamp(c.xy, 0.5 / size, 1.0 - 0.5 / size);
    float z = texture(uSceneDepth, uv).r;
    vec4 worldH = fInverse * vec4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, z, 1.0);
    float reciprocalDepth = max(worldH.w, 1e-6);
    return vec4(reciprocalDepth, 0.0, 0.0, 0.0);
}
vec4 rsxTex3(vec4 c) { return vec4(texture(uSceneColour, c.xy).rgb, 1.0); }
vec4 rsxTex4(vec4 c) {
    vec3 n = normalize(fNormal);
    vec3 r = reflect(-normalize(TEX0.xyz), n);
    float horizon = pow(1.0 - abs(r.y), 2.0);
    vec3 environment = mix(vec3(0.30, 0.38, 0.45), vec3(0.55, 0.65, 0.75), horizon);
    if (uCamera.w > 0.5) {
        vec4 sampleColour = texture(uWaterCube, r);
        environment = sampleColour.rgb * max(sampleColour.a, 0.0);
    }
    if (c.z == 0.0 || any(lessThan(c.xy, vec2(0.0))) || any(greaterThan(c.xy, vec2(1.0))))
        return vec4(environment, 1.0);
    // The game's 0x2A3 pass mixes a quarter-resolution copy with 20% HDR cubemap.
    // Sample the existing scene copy over the corresponding footprint.
    vec2 q = 1.0 / vec2(textureSize(uSceneColour, 0));
    vec3 screen = (texture(uSceneColour, c.xy + q).rgb + texture(uSceneColour, c.xy - q).rgb +
                   texture(uSceneColour, c.xy + vec2(q.x, -q.y)).rgb +
                   texture(uSceneColour, c.xy + vec2(-q.x, q.y)).rgb) * 0.25;
    return vec4(mix(screen, environment, 0.2), 1.0);
}
// ACIT 0x6701A8: unpolarised Fresnel, including total internal reflection, stored as R16 UNORM.
float waterFresnelTexel(float index) {
    float signedCosine = index * (2.0 / 255.0) - 1.0;
    float eta = signedCosine < 0.0 ? 1.0 / 1.332986 : 1.332986;
    float facing = abs(signedCosine);
    float g2 = eta * eta + facing * facing - 1.0;
    if (g2 < 0.0) return 1.0;
    float g = sqrt(g2);
    float s = (facing - g) / (facing + g);
    float p = (eta * eta * facing - g) / (eta * eta * facing + g);
    return floor(0.5 * (s * s + p * p) * 65535.0) / 65535.0;
}
vec4 rsxTex5(vec4 c) {
    float x = clamp(c.x * 256.0 - 0.5, 0.0, 255.0);
    return vec4(mix(waterFresnelTexel(floor(x)), waterFresnelTexel(min(floor(x) + 1.0, 255.0)), fract(x)));
}
vec4 rsxTex13(vec4 c) { return vec4(waterClipValue(c.xy, uClip.xyz)); }
vec4 screenColumn(vec4 c) { return vec4((c.x + c.w) * 0.5, (c.w - c.y) * 0.5, 0.0, c.w); }
";

    private const string RetailWaterMainGlsl = @"
void main() {
    TEX5 = vec4((fWorld.xz - uCorner.xz) / uSize.xy, 0.0, 0.0);
#ifndef WATER_RSX_CLIP
    if (waterClipped(TEX5.xy, uClip.xyz)) discard;
#endif
    vec3 n = normalize(fNormal);
    WPOS = gl_FragCoord;
    TEX0 = vec4(uCamera.xyz - fWorld, 0.0);
    TEX1 = vec4(-n.xz / max(n.y, 1e-4), 0.0, 1.0 / gl_FragCoord.w);
    TEX2 = vec4(0.0);
    G1 = uMotion.z > 0.5 ? vec4(0.0) : uColour;
    G2 = vec4(1.0, 0.0, 0.0, 0.0);
    G3 = screenColumn(uMvp[0]);
    G4 = screenColumn(uMvp[1]);
    G5 = screenColumn(uMvp[2]);
    G6 = vec4(0.0);
    G7 = vec4(0.0);
    G8 = vec4(1.0 / vec2(textureSize(uSceneDepth, 0)), 0.0, 0.0);
    G9 = vec4(0.0, 0.0, 0.0, 1.0);
    G10 = vec4(0.1, 0.2, 4.0, 0.5);
    vec3 rgb = rsxShader().rgb;
    rgb = mix(clamp(rgb, 0.0, 1000.0), vec3(0.0), isnan(rgb));
    o = vec4(rgb, 1.0);
}";

    private const string FogVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { vec4 uTop; vec4 uX; vec4 uY; vec4 uZ; vec4 uColour; vec4 uCamera; };
layout(location = 0) out vec3 fWorld;
layout(location = 1) flat out mat4 fInverse;
void main() {
    // The +Y face: top centre +- X +- Z, the corners Init keeps at +0x30..+0x60.
    const vec2 C[6] = vec2[6](vec2(1, 1), vec2(-1, 1), vec2(-1, -1), vec2(1, 1), vec2(-1, -1), vec2(1, -1));
    vec2 c = C[gl_VertexIndex];
    vec3 w = uTop.xyz + uZ.xyz * c.x + uX.xyz * c.y;
    fWorld = w;
    fInverse = inverse(uMvp);
    gl_Position = uMvp * vec4(w, 1.0);
}";

    private const string FogFragmentGlsl = @"#version 450
layout(set = 1, binding = 0) uniform sampler2D uSceneDepth;
layout(push_constant) uniform PC { vec4 uTop; vec4 uX; vec4 uY; vec4 uZ; vec4 uColour; vec4 uCamera; };
layout(location = 0) in vec3 fWorld;
layout(location = 1) flat in mat4 fInverse;
layout(location = 0) out vec4 o;
void main() {
    vec2 size = vec2(textureSize(uSceneDepth, 0));
    float z = texelFetch(uSceneDepth, ivec2(gl_FragCoord.xy), 0).r;
    vec2 ndc = vec2(gl_FragCoord.x / size.x * 2.0 - 1.0, 1.0 - gl_FragCoord.y / size.y * 2.0);
    vec4 sw = fInverse * vec4(ndc, z, 1.0);
    vec3 scene = sw.xyz / sw.w;
    // ACIT FP 0xD904C0: density x (face height - scene height) x (scene view depth - face view depth),
    // linear and not clipped to the box, so the fog runs on past the volume's sides and floor.
    float below = fWorld.y - scene.y;
    float span = 1.0 / sw.w - 1.0 / gl_FragCoord.w;
    float fog = clamp(uColour.w * below * span, 0.0, 1.0);
    if (fog <= 0.002) discard;
    o = vec4(uColour.rgb * fog, fog);
}";

    /// <summary>The water bodies to draw this frame. Set by the view before <see cref="Frame"/>.</summary>
    public IReadOnlyList<WaterDraw> Waters { get; set; } = [];

    /// <summary>The vertical fogs to draw this frame. Set by the view before <see cref="Frame"/>.</summary>
    public IReadOnlyList<VerticalFogDraw> VerticalFogs { get; set; } = [];

    /// <summary>Height of the editor's water waves; 0 holds the water flat.</summary>
    public float WaterWaveHeight { get; set; } = 0.12f;

    private const int WaterRippleCount = 2;
    private const float WaterRippleLifetime = 4f;
    private readonly record struct WaterRipple(Vector2 Position, float Start, float Height);
    private readonly Dictionary<uint, List<WaterRipple>> _waterRipples = [];
    private readonly Dictionary<int, (VkBuffer Buffer, VkDeviceMemory Memory)> _waterIndices = [];
    private readonly Dictionary<ulong, Vector3> _waterClipLayers = [];
    private Texture? _waterClipTexture;
    private VkImageView _waterClipView;

    private void CreateWaterMasks(GraphicsDevice gd, IReadOnlyList<ITexture> masks)
    {
        var decoded = masks.Select(mask => (mask.Id, Pixels: TextureUploadPreparer.Prepare(mask, generateMipsWhenUnauthored: false)))
            .Where(mask => mask.Pixels != null).ToArray();
        uint width = decoded.Length == 0 ? 1 : decoded.Max(mask => mask.Pixels!.Width);
        uint height = decoded.Length == 0 ? 1 : decoded.Max(mask => mask.Pixels!.Height);
        uint layers = (uint)decoded.Length + 1;
        _waterClipTexture = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, layers, PixelFormat.R8_UNorm, TextureUsage.Sampled));
        var pixels = new byte[checked((int)(width * height))];
        Array.Fill(pixels, byte.MaxValue);
        gd.UpdateTexture(_waterClipTexture, pixels, 0, 0, 0, width, height, 1, 0, 0);
        for (int i = 0; i < decoded.Length; i++)
        {
            var (id, data) = decoded[i];
            var mask = data!;
            byte[] rgba = mask.Levels[0];
            for (int y = 0; y < mask.Height; y++)
            for (int x = 0; x < mask.Width; x++)
                pixels[y * width + x] = rgba[(y * mask.Width + x) * 4];
            uint layer = (uint)i + 1;
            gd.UpdateTexture(_waterClipTexture, pixels, 0, 0, 0, width, height, 1, 0, layer);
            _waterClipLayers.Add(id, new Vector3(mask.Width, mask.Height, layer));
        }
        var view = new VkImageViewCreateInfo
        {
            image = _ctx.BackendInfo.GetVkImage(_waterClipTexture), viewType = VkImageViewType.Image2DArray,
            format = VkFormat.R8Unorm, subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VkImageAspectFlags.Color, levelCount = 1, layerCount = layers,
            },
        };
        VkImageView imageView; Check(_api.vkCreateImageView(&view, &imageView), "vkCreateImageView(water masks)");
        _waterClipView = imageView;
    }

    /// <summary>Adds a local impulse to a visible water record for previewing interactive waves.</summary>
    public void AddWaterRipple(uint pickId, Vector2 position)
    {
        if (pickId == NoHit || !float.IsFinite(position.X) || !float.IsFinite(position.Y))
            return;
        if (!_waterRipples.TryGetValue(pickId, out var ripples))
            _waterRipples.Add(pickId, ripples = []);
        if (ripples.Count == WaterRippleCount) ripples.RemoveAt(0);
        ripples.Add(new WaterRipple(position, WaterTime, 0.24f));
    }

    private float WaterTime => SceneTimeOverride ?? (float)_skyClock.Elapsed.TotalSeconds;

    /// <summary>The game's fog constant: density x half height (VerticalFogController Init, 0x00349FB8).
    /// Density is per metre below the face, per metre of view depth.</summary>
    private const float VerticalFogDensityScale = 0.0413f;

    private VkShaderModule _waterVs, _waterFs, _fogVs, _fogFs;
    private VkDescriptorSetLayout _waterSetLayout;
    private VkPipelineLayout _waterLayout;
    private VkPipeline _waterPipeline, _fogPipeline, _waterPickPipeline;
    private readonly VkPipeline[] _waterRsxPipelines = new VkPipeline[4];
    private VkSampler _waterDepthSampler;
    private VkDescriptorPool _waterPool;
    private readonly VkDescriptorSet[] _waterSets = new VkDescriptorSet[Frames];
    private VkImage _sceneDepthImage; private VkDeviceMemory _sceneDepthMemory; private VkImageView _sceneDepthView;
    private bool _sceneDepthFresh;

    private void CreateWaterPipelines(VkPipelineCache pipelineCache, byte* entry, VkPipelineViewportStateCreateInfo* viewportState,
        VkPipelineRasterizationStateCreateInfo* rasterCullNone, VkPipelineMultisampleStateCreateInfo* multisample,
        VkPipelineDynamicStateCreateInfo* dynState)
    {
        _waterDepthSampler = CreateClampSampler(VkFilter.Nearest, VkSamplerMipmapMode.Nearest);
        VkDescriptorSetLayoutBinding* bindings = stackalloc VkDescriptorSetLayoutBinding[3];
        bindings[0] = new VkDescriptorSetLayoutBinding { binding = 0, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        bindings[1] = new VkDescriptorSetLayoutBinding { binding = 1, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        bindings[2] = bindings[1] with { binding = 2 };
        var setInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 3, pBindings = bindings };
        VkDescriptorSetLayout sl; Check(_api.vkCreateDescriptorSetLayout(&setInfo, &sl), "vkCreateDescriptorSetLayout(water)"); _waterSetLayout = sl;
        var poolSize = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = 3 * Frames };
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = Frames, poolSizeCount = 1, pPoolSizes = &poolSize };
        VkDescriptorPool pool; Check(_api.vkCreateDescriptorPool(&poolInfo, &pool), "vkCreateDescriptorPool(water)"); _waterPool = pool;
        for (int f = 0; f < Frames; f++)
        {
            var alloc = new VkDescriptorSetAllocateInfo { descriptorPool = _waterPool, descriptorSetCount = 1, pSetLayouts = &sl };
            VkDescriptorSet set; Check(_api.vkAllocateDescriptorSets(&alloc, &set), "vkAllocateDescriptorSets(water)"); _waterSets[f] = set;
        }

        VkDescriptorSetLayout* sets = stackalloc VkDescriptorSetLayout[2] { _descLayout, _waterSetLayout };
        var push = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, offset = 0, size = 128 };
        var layoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 2, pSetLayouts = sets, pushConstantRangeCount = 1, pPushConstantRanges = &push };
        VkPipelineLayout layout; Check(_api.vkCreatePipelineLayout(&layoutInfo, &layout), "vkCreatePipelineLayout(water)"); _waterLayout = layout;

        _waterVs = Module(WaterVertexGlsl, ShaderStages.Vertex);
        _waterFs = Module(WaterFragmentGlsl, ShaderStages.Fragment);
        _fogVs = Module(FogVertexGlsl, ShaderStages.Vertex);
        _fogFs = Module(FogFragmentGlsl, ShaderStages.Fragment);

        var vin = new VkPipelineVertexInputStateCreateInfo();
        var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
        var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.GreaterOrEqual };
        // RGB only: the opaque pass's alpha is the bloom's glow mask.
        var opaqueAttach = new VkPipelineColorBlendAttachmentState
        {
            blendEnable = false,
            colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B,
        };
        var premultipliedAttach = new VkPipelineColorBlendAttachmentState
        {
            blendEnable = true,
            srcColorBlendFactor = VkBlendFactor.One, dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha, colorBlendOp = VkBlendOp.Add,
            srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add,
            colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B,
        };
        VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        for (int which = 0; which < 7; which++)
        {
            bool fog = which == 1;
            bool retail = which is >= 2 and <= 5;
            bool pick = which == 6;
            VkShaderModule specialisedVs = default, specialisedFs = default;
            try
            {
                if (retail)
                {
                    if (!UseRetailEmbeddedShaders) continue;
                    bool clipped = which >= 4;
                    var variant = _standardShaders?.GetWater(opaque: (which & 1) != 0, clipped);
                    if (variant == null) continue;
                    specialisedFs = RetailModule(RetailWaterSource(variant, clipped));
                }
                if (pick)
                {
                    specialisedVs = Module(WaterPickVertexGlsl, ShaderStages.Vertex);
                    specialisedFs = Module(WaterPickFragmentGlsl, ShaderStages.Fragment);
                }
                stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = pick ? specialisedVs : fog ? _fogVs : _waterVs, pName = entry };
                stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = pick || retail ? specialisedFs : fog ? _fogFs : _waterFs, pName = entry };
                var attach = fog ? premultipliedAttach : opaqueAttach;
                if (pick) attach.colorWriteMask |= VkColorComponentFlags.A;
                var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &attach };
                var passDepth = depth with { depthWriteEnable = pick };
                var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = viewportState, pRasterizationState = rasterCullNone, pMultisampleState = multisample, pDepthStencilState = &passDepth, pColorBlendState = &blend, pDynamicState = dynState, layout = _waterLayout, renderPass = pick ? _rpPick : _rpOpaque, subpass = 0 };
                VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), fog ? "vkCreateGraphicsPipelines(fog)" : "vkCreateGraphicsPipelines(water)");
                if (pick) _waterPickPipeline = p;
                else if (retail) _waterRsxPipelines[which - 2] = p;
                else if (fog) _fogPipeline = p;
                else _waterPipeline = p;
            }
            catch (Exception e) when (retail)
            {
                Console.WriteLine($"[RetailShaders] ACIT water: {e.Message}");
            }
            finally
            {
                if (specialisedVs.Handle != 0) _api.vkDestroyShaderModule(specialisedVs);
                if (specialisedFs.Handle != 0) _api.vkDestroyShaderModule(specialisedFs);
            }
        }
    }

    /// <summary>The scene-depth copy the water and the fog read, and their sets pointing at it and at each slot's
    /// scene colour. Remade with the other targets on a resize.</summary>
    private void CreateWaterTargets()
    {
        if (_waterPool.Handle == 0) return;
        var info = new VkImageCreateInfo { imageType = VkImageType.Image2D, format = DepthFormat, extent = new VkExtent3D { width = _width, height = _height, depth = 1 }, mipLevels = 1, arrayLayers = 1, samples = VkSampleCountFlags.Count1, tiling = VkImageTiling.Optimal, usage = VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst, sharingMode = VkSharingMode.Exclusive, initialLayout = VkImageLayout.Undefined };
        VkImage image; Check(_api.vkCreateImage(&info, &image), "vkCreateImage(sceneDepth)");
        VkMemoryRequirements reqs; _api.vkGetImageMemoryRequirements(image, &reqs);
        _sceneDepthMemory = Allocate(reqs, VkMemoryPropertyFlags.DeviceLocal);
        Check(_api.vkBindImageMemory(image, _sceneDepthMemory, 0), "vkBindImageMemory(sceneDepth)");
        _sceneDepthImage = image;
        // Depth aspect only: a sampled view of a depth/stencil image may name one aspect.
        var viewInfo = new VkImageViewCreateInfo { image = image, viewType = VkImageViewType.Image2D, format = DepthFormat, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Depth, baseMipLevel = 0, levelCount = 1, baseArrayLayer = 0, layerCount = 1 } };
        VkImageView view; Check(_api.vkCreateImageView(&viewInfo, &view), "vkCreateImageView(sceneDepth)"); _sceneDepthView = view;
        _sceneDepthFresh = false;

        VkDescriptorImageInfo* images = stackalloc VkDescriptorImageInfo[3];
        VkWriteDescriptorSet* writes = stackalloc VkWriteDescriptorSet[3];
        images[2] = new VkDescriptorImageInfo { sampler = _samplerBaked, imageView = _waterClipView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        for (int f = 0; f < Frames; f++)
        {
            images[0] = new VkDescriptorImageInfo { sampler = _waterDepthSampler, imageView = _sceneDepthView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
            images[1] = new VkDescriptorImageInfo { sampler = _samplerBaked, imageView = _sceneColorView[f], imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
            writes[0] = new VkWriteDescriptorSet { dstSet = _waterSets[f], dstBinding = 0, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &images[0] };
            writes[1] = new VkWriteDescriptorSet { dstSet = _waterSets[f], dstBinding = 1, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &images[1] };
            writes[2] = new VkWriteDescriptorSet { dstSet = _waterSets[f], dstBinding = 2, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &images[2] };
            _api.vkUpdateDescriptorSets(3, writes, 0, null);
        }
    }

    private void DestroyWaterTargets()
    {
        if (_sceneDepthImage.Handle == 0) return;
        _api.vkDestroyImageView(_sceneDepthView); _api.vkDestroyImage(_sceneDepthImage); _api.vkFreeMemory(_sceneDepthMemory);
        _sceneDepthImage = default;
    }

    private bool WaterWanted => (Waters.Count > 0 || VerticalFogs.Count > 0) && _sceneDepthImage.Handle != 0;

    private bool WaterInFrustum(in WaterDraw water)
    {
        if (!_frustumCulling) return true;
        var extent = new Vector3(water.Size.X * 0.5f, MathF.Abs(WaterWaveHeight) * 2f + 0.75f, water.Size.Y * 0.5f);
        Vector3 centre = water.Corner + new Vector3(extent.X, 0f, extent.Z);
        foreach (Vector4 plane in _planes)
        {
            var normal = new Vector3(plane.X, plane.Y, plane.Z);
            if (Vector3.Dot(normal, centre) + plane.W + Vector3.Dot(Vector3.Abs(normal), extent) < 0f)
                return false;
        }
        return true;
    }

    // The game clears the surface-colour constants for a camera within four metres below the nearest
    // water at its X/Z position (0x6771E8, 0x674988). Use the record height for the preview query.
    private bool CameraJustUnderWater()
    {
        float closest = float.PositiveInfinity;
        foreach (var w in Waters)
        {
            Vector3 fromCorner = _cameraPosition - w.Corner;
            if (fromCorner.X >= 0 && fromCorner.X <= w.Size.X && fromCorner.Z >= 0 && fromCorner.Z <= w.Size.Y &&
                MathF.Abs(fromCorner.Y) < MathF.Abs(closest))
                closest = fromCorner.Y;
        }
        return closest is > -4f and < 0f;
    }

    private VkBuffer WaterIndexBuffer(int cells)
    {
        if (_waterIndices.TryGetValue(cells, out var existing)) return existing.Buffer;
        var indices = new ushort[cells * cells * 6];
        int at = 0;
        for (int z = 0; z < cells; z++)
        for (int x = 0; x < cells; x++)
        {
            int top = z * (cells + 1) + x;
            int bottom = top + cells + 1;
            indices[at++] = (ushort)top;
            indices[at++] = (ushort)(top + 1);
            indices[at++] = (ushort)(bottom + 1);
            indices[at++] = (ushort)top;
            indices[at++] = (ushort)(bottom + 1);
            indices[at++] = (ushort)bottom;
        }
        var buffer = Upload<ushort>(indices, VkBufferUsageFlags.IndexBuffer);
        _waterIndices.Add(cells, buffer);
        return buffer.Item1;
    }

    /// <summary>Copies the scene's depth (the opaque pass, ended) into the copy the water and fog sample, and
    /// gives the attachment back for the passes that load it.</summary>
    private void CopySceneDepth()
    {
        if (!WaterWanted) return;
        var range = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Depth, baseMipLevel = 0, levelCount = 1, baseArrayLayer = 0, layerCount = 1 };
        var fullRange = range with { aspectMask = VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil };
        VkImageMemoryBarrier* pre = stackalloc VkImageMemoryBarrier[2];
        pre[0] = new VkImageMemoryBarrier { srcAccessMask = VkAccessFlags.DepthStencilAttachmentWrite, dstAccessMask = VkAccessFlags.TransferRead, oldLayout = VkImageLayout.DepthStencilAttachmentOptimal, newLayout = VkImageLayout.TransferSrcOptimal, srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED, dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED, image = _depthImage, subresourceRange = fullRange };
        pre[1] = new VkImageMemoryBarrier { srcAccessMask = VkAccessFlags.ShaderRead, dstAccessMask = VkAccessFlags.TransferWrite, oldLayout = _sceneDepthFresh ? VkImageLayout.ShaderReadOnlyOptimal : VkImageLayout.Undefined, newLayout = VkImageLayout.TransferDstOptimal, srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED, dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED, image = _sceneDepthImage, subresourceRange = fullRange };
        _api.vkCmdPipelineBarrier(_cmd, VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests | VkPipelineStageFlags.FragmentShader, VkPipelineStageFlags.Transfer, 0, 0, null, 0, null, 2, pre);
        var layers = new VkImageSubresourceLayers { aspectMask = VkImageAspectFlags.Depth, mipLevel = 0, baseArrayLayer = 0, layerCount = 1 };
        var region = new VkImageCopy { srcSubresource = layers, dstSubresource = layers, extent = new VkExtent3D { width = _width, height = _height, depth = 1 } };
        _api.vkCmdCopyImage(_cmd, _depthImage, VkImageLayout.TransferSrcOptimal, _sceneDepthImage, VkImageLayout.TransferDstOptimal, 1, &region);
        VkImageMemoryBarrier* post = stackalloc VkImageMemoryBarrier[2];
        post[0] = new VkImageMemoryBarrier { srcAccessMask = VkAccessFlags.TransferRead, dstAccessMask = VkAccessFlags.DepthStencilAttachmentRead | VkAccessFlags.DepthStencilAttachmentWrite, oldLayout = VkImageLayout.TransferSrcOptimal, newLayout = VkImageLayout.DepthStencilAttachmentOptimal, srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED, dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED, image = _depthImage, subresourceRange = fullRange };
        post[1] = new VkImageMemoryBarrier { srcAccessMask = VkAccessFlags.TransferWrite, dstAccessMask = VkAccessFlags.ShaderRead, oldLayout = VkImageLayout.TransferDstOptimal, newLayout = VkImageLayout.ShaderReadOnlyOptimal, srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED, dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED, image = _sceneDepthImage, subresourceRange = fullRange };
        _api.vkCmdPipelineBarrier(_cmd, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests | VkPipelineStageFlags.FragmentShader, 0, 0, null, 0, null, 2, post);
        _sceneDepthFresh = true;
    }

    private void DrawWater()
    {
        if (!WaterWanted || !_sceneDepthFresh) return;
        VkDescriptorSet* sets = stackalloc VkDescriptorSet[2] { _descSet, _waterSets[_slot] };
        Vector4* pc = stackalloc Vector4[8];
        float time = WaterTime;
        if (Waters.Count > 0)
        {
            VkPipeline bound = default;
            VkDescriptorSet boundSet = default;
            int boundCells = 0;
            bool cameraUnderwater = _standardShaders?.IsCrackInTime == true && CameraJustUnderWater();
            foreach (var w in Waters)
            {
                if (!WaterInFrustum(w)) continue;
                bool opaque = w.Absorption >= 100f;
                var clip = _waterClipLayers.GetValueOrDefault(w.ClipMask);
                int opacity = opaque ? 1 : 0;
                VkPipeline retail = _waterRsxPipelines[opacity + (clip.Z > 0 ? 2 : 0)];
                if (retail.Handle == 0) retail = _waterRsxPipelines[opacity];
                bool useRetail = UseRetailEmbeddedShaders && retail.Handle != 0;
                VkPipeline pipeline = useRetail ? retail : _waterPipeline;
                if (pipeline != bound)
                {
                    _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, pipeline);
                    bound = pipeline;
                }
                int resource = useRetail ? _cubeResourceMap?.GetValueOrDefault(w.ReflectionCube) ?? 0 : 0;
                sets[0] = _cubeDescSets[_slot * _cubeResourceCount + Math.Clamp(resource, 0, _cubeResourceCount - 1)];
                if (sets[0] != boundSet)
                {
                    _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _waterLayout, 0, 2, sets, 0, null);
                    boundSet = sets[0];
                }
                // Preview density. The retail SPU supplies its own per-batch index count at 0x674B90.
                int n = 1 << Math.Clamp(w.Tessellation - 10, 3, 7);
                if (boundCells != n)
                {
                    _api.vkCmdBindIndexBuffer(_cmd, WaterIndexBuffer(n), 0, VkIndexType.Uint16);
                    boundCells = n;
                }
                pc[0] = new Vector4(w.Corner, n);
                pc[1] = new Vector4(w.Size, time, WaterWaveHeight);
                pc[2] = new Vector4(w.Colour, w.Absorption);
                // The generic shader needs the opacity mode. Each RSX program already has that baked in.
                pc[3] = new Vector4(_cameraPosition, (useRetail ? !_ownsEnvCube : opaque) ? 1f : 0f);
                pc[5] = new Vector4(clip, 0f);
                for (int i = 0; i < WaterRippleCount; i++) pc[6 + i] = default;
                int count = 0;
                if (_waterRipples.TryGetValue(w.PickId, out var ripples))
                {
                    ripples.RemoveAll(r => time - r.Start is < 0f or >= WaterRippleLifetime);
                    foreach (var r in ripples)
                        pc[6 + count++] = new Vector4(r.Position, time - r.Start, r.Height);
                    if (count == 0) _waterRipples.Remove(w.PickId);
                }
                pc[4] = new Vector4(_standardShaders?.IsCrackInTime == true ? 1f : 0f, count, cameraUnderwater ? 1f : 0f, 0f);
                _api.vkCmdPushConstants(_cmd, _waterLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 128, pc);
                _api.vkCmdDrawIndexed(_cmd, (uint)(n * n * 6), 1, 0, 0, 0);
            }
        }
    }

    /// <summary>The vertical fogs, over the resolved translucents: retail queues them in bucket 0x46
    /// (VerticalFogController_Update 0x00349C04 -> 0x005BCC3C), after water (0x3D) and the world's blended
    /// buckets. Runs in the resolve pass, which is compatible with the pass the pipeline was made for.</summary>
    private void DrawVerticalFog()
    {
        if (VerticalFogs.Count == 0 || !WaterWanted || !_sceneDepthFresh) return;
        VkDescriptorSet* sets = stackalloc VkDescriptorSet[2] { _descSet, _waterSets[_slot] };
        Vector4* pc = stackalloc Vector4[8];
        {
            _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _fogPipeline);
            _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _waterLayout, 0, 2, sets, 0, null);
            foreach (var fog in VerticalFogs)
            {
                var m = fog.Box;
                var x = new Vector3(m.M11, m.M12, m.M13);
                var y = new Vector3(m.M21, m.M22, m.M23);
                var z = new Vector3(m.M31, m.M32, m.M33);
                var centre = m.Translation;
                if (y.LengthSquared() < 1e-8f) continue;
                // The game's vertex colour (the bytes) times its constant colour (the same bytes / 255), so the
                // colour is squared; then into the scene's linear light.
                var c2 = fog.Colour * fog.Colour;
                var colour = new Vector3(MathF.Pow(c2.X, 2.2f), MathF.Pow(c2.Y, 2.2f), MathF.Pow(c2.Z, 2.2f));
                pc[0] = new Vector4(centre + y, 0f);
                pc[1] = new Vector4(x, 0f);
                pc[2] = new Vector4(y, 0f);
                pc[3] = new Vector4(z, 0f);
                pc[4] = new Vector4(colour, VerticalFogDensityScale / y.Length());
                pc[5] = new Vector4(_cameraPosition, 0f);
                _api.vkCmdPushConstants(_cmd, _waterLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 96, pc);
                _api.vkCmdDraw(_cmd, 6, 1, 0, 0);
            }
        }
    }

    /// <summary>Picks the water at its authored height, clipped by its shoreline mask and scene depth.</summary>
    private void PickWaters()
    {
        if (Waters.Count == 0) return;
        _api.vkCmdBindPipeline(_pickCmd, VkPipelineBindPoint.Graphics, _waterPickPipeline);
        VkDescriptorSet* sets = stackalloc VkDescriptorSet[2] { _descSet, _waterSets[_displaySlot] };
        _api.vkCmdBindDescriptorSets(_pickCmd, VkPipelineBindPoint.Graphics, _waterLayout, 0, 2, sets, 0, null);
        Vector4* pc = stackalloc Vector4[4];
        foreach (var w in Waters)
        {
            if (w.PickId == NoHit) continue;
            pc[0] = new Vector4(w.Corner, 0f);
            pc[1] = new Vector4(w.Size, 0f, 0f);
            pc[2] = new Vector4(_waterClipLayers.GetValueOrDefault(w.ClipMask), 0f);
            pc[3] = default;
            *(uint*)(pc + 3) = w.PickId;
            _api.vkCmdPushConstants(_pickCmd, _waterLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 64, pc);
            _api.vkCmdDraw(_pickCmd, 6, 1, 0, 0);
        }
        _api.vkCmdBindDescriptorSets(_pickCmd, VkPipelineBindPoint.Graphics, _pickLayout, 0, 1, sets, 0, null);
    }

    private void DestroyWater()
    {
        DestroyWaterTargets();
        _api.vkDestroyPipeline(_waterPickPipeline);
        _api.vkDestroyPipeline(_waterPipeline);
        foreach (var pipeline in _waterRsxPipelines)
            if (pipeline.Handle != 0) _api.vkDestroyPipeline(pipeline);
        foreach (var (buffer, memory) in _waterIndices.Values)
        {
            _api.vkDestroyBuffer(buffer);
            _api.vkFreeMemory(memory);
        }
        _api.vkDestroyPipeline(_fogPipeline);
        _api.vkDestroyPipelineLayout(_waterLayout);
        _api.vkDestroyDescriptorPool(_waterPool);
        _api.vkDestroyDescriptorSetLayout(_waterSetLayout);
        _api.vkDestroyImageView(_waterClipView);
        _waterClipTexture?.Dispose();
        _api.vkDestroySampler(_waterDepthSampler);
        _api.vkDestroyShaderModule(_waterVs); _api.vkDestroyShaderModule(_waterFs);
        _api.vkDestroyShaderModule(_fogVs); _api.vkDestroyShaderModule(_fogFs);
    }
}
