using System.Numerics;
using NeoVeldrid;
using NeoVeldrid.SPIRV;
using Vortice.Vulkan;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>The from-scratch raw-Vulkan scene renderer (Docs/NewRenderer.md).
///
/// One command buffer, re-recorded per frame with only the frustum- and distance-visible draws, then
/// one submit. Draws are per-instance indexed draws (no hardware instancing), matching the original
/// engine.
///
/// Frame structure, three render passes over a shared depth-stencil buffer:
///   1. OPAQUE      Opaque + Cutout, then Soft-Edge's alpha-tested depth-only prepass, then Additive,
///                  then foliage billboards, then the editor's volume wireframes. Colour + depth.
///   2. ACCUMULATE  Overlay/Scunge/Blended and Soft-Edge's colour pass, blended into an RGBA16F accum
///                  and an R16F reveal target (weighted-blended OIT), depth-tested but not
///                  depth-writing, so translucency needs no sorting.
///   3. RESOLVE     A fullscreen triangle composites accum/reveal over the opaque colour, then the
///                  selection outline draws on top.
///
/// Every non-opaque draw carries the game's polygon offset (see NonOpaqueDepthBias). This renderer
/// compiles its own SPIR-V rather than using Bliss's lit shader.</summary>
public sealed unsafe class VulkanRenderer : IDisposable
{
    private const VkFormat ColorFormat = VkFormat.R8G8B8A8Unorm;
    // Depth carries stencil for the selection outline's stencil mask-and-inflate technique.
    private const VkFormat DepthFormat = VkFormat.D32SfloatS8Uint;
    private const VkFormat AccumFormat = VkFormat.R16G16B16A16Sfloat;
    private const VkFormat RevealFormat = VkFormat.R16Sfloat;
    private const uint VertexStride = VulkanSceneCapture.FloatsPerVertex * sizeof(float); // 104
    private const int TexPerMaterial = 5;

    // Polygon offset for the non-opaque pass, matching the real game (same values in AssetManager,
    // which applies them on the Bliss path).
    private const float NonOpaqueDepthBias = -87f;
    private const float NonOpaqueSlopeScaledDepthBias = -0.33972f;

    // uBoneBase[gl_InstanceIndex] is the skinned instance's bone-palette region offset (see
    // TrySetAnimatedInstance); 0 for every non-animating instance, which is the shared identity
    // region, so skinning is a no-op unless an animation is actually playing.
    private const string SkinningGlsl = @"
layout(set = 0, binding = 4) readonly buffer BonePalette { mat4 uBones[]; };
layout(set = 0, binding = 5) readonly buffer BoneBase { int uBoneBase[]; };
layout(location = 6) in vec4 inJoints;
layout(location = 7) in vec4 inWeights;
vec3 skinnedPosition, skinnedNormal, skinnedTangent;
void computeSkin(vec3 pos, vec3 nrm, vec3 tan) {
    vec3 p = vec3(0.0), n = vec3(0.0), t = vec3(0.0);
    float totalWeight = 0.0;
    int base = uBoneBase[gl_InstanceIndex];
    for (int slot = 0; slot < 4; slot++) {
        float w = inWeights[slot];
        if (w <= 0.0) continue;
        mat4 skin = uBones[base + int(inJoints[slot])];
        p += (skin * vec4(pos, 1.0)).xyz * w;
        n += (mat3(skin) * nrm) * w;
        t += (mat3(skin) * tan) * w;
        totalWeight += w;
    }
    if (totalWeight <= 1e-6) { skinnedPosition = pos; skinnedNormal = nrm; skinnedTangent = tan; return; }
    skinnedPosition = p / totalWeight;
    n /= totalWeight; t /= totalWeight;
    skinnedNormal = dot(n, n) > 1e-12 ? normalize(n) : nrm;
    skinnedTangent = dot(t, t) > 1e-12 ? normalize(t) : tan;
}";

    private const string VertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(set = 0, binding = 1) readonly buffer Transforms { mat4 uT[]; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inUV;
layout(location = 2) in vec3 inNormal;
layout(location = 3) in vec4 inTangent;
layout(location = 4) in vec2 inUV2;
layout(location = 5) in vec4 inColor;
layout(location = 0) out vec2 fUV;
layout(location = 1) out vec3 fWorldNormal;
layout(location = 2) out vec3 fWorldTangent;
layout(location = 3) out float fHandedness;
layout(location = 4) out vec3 fWorldPos;
layout(location = 5) out vec2 fUV2;
layout(location = 6) out vec4 fColor;
" + SkinningGlsl + @"
void main() {
    computeSkin(inPos, inNormal, inTangent.xyz);
    mat4 m = uT[gl_InstanceIndex];
    mat3 m3 = mat3(m);
    mat3 nrm = transpose(inverse(m3));
    fWorldNormal = normalize(nrm * skinnedNormal);
    fWorldTangent = normalize(nrm * skinnedTangent);
    fHandedness = inTangent.w * sign(determinant(m3));
    vec4 world = m * vec4(skinnedPosition, 1.0);
    fWorldPos = world.xyz;
    fUV = inUV;
    fUV2 = inUV2;
    fColor = inColor;
    gl_Position = uMvp * world;
}";

    // Shared lit shading, everything except the final output. The opaque and accumulate fragment
    // shaders each append their own main() and output declarations to this.
    private const string LitFragCommon = @"#version 450
layout(set = 0, binding = 2, std140) uniform LightBuffer {
    vec3 uLightDirection; float uAmbient;
    vec3 uLightColor; float uSpecularPower;
    vec3 uCameraPosition; float uReflectionDebug;
    vec3 uEnvironmentColour; float uEnvironmentIntensity;
    vec2 uLightmapUVScale; vec2 uLightmapUVOffset;
    float uBakedLightScale; float uBakedBumpFade; float uBakedDebugView; float uReflectionBase;
    vec2 uLightmapUVPivot; float uLightmapUVRotation; float uBakedAmbient;
    vec3 uEnvDirection0; float uEnvHasLighting;
    vec3 uEnvDirection1; float _p4;
    vec3 uEnvAmbient; float _p5;
    vec3 uEnvLight0Colour; float _p6;
    vec3 uEnvLight1Colour; float _p7;
};
layout(set = 0, binding = 3) uniform samplerCube uEnvCube;
layout(set = 1, binding = 0) uniform sampler2D uAlbedo;
layout(set = 1, binding = 1) uniform sampler2D uNormal;
layout(set = 1, binding = 2) uniform sampler2D uProps;
layout(set = 1, binding = 3) uniform sampler2D uLightColour;
layout(set = 1, binding = 4) uniform sampler2D uLightDir;
layout(push_constant) uniform PC { vec4 uMat0; vec4 uMat1; }; // uMat0=(hasBaked,pScale,pBias,alphaThr); uMat1=(renderMode,vtxAlpha,lit,albedoHasAlpha)
layout(location = 0) in vec2 fUV;
layout(location = 1) in vec3 fWorldNormal;
layout(location = 2) in vec3 fWorldTangent;
layout(location = 3) in float fHandedness;
layout(location = 4) in vec3 fWorldPos;
layout(location = 5) in vec2 fUV2;
layout(location = 6) in vec4 fColor;
void shade(out vec3 litColor, out float litAlpha) {
    vec3 n = normalize(fWorldNormal);
    vec3 t = normalize(fWorldTangent - n * dot(fWorldTangent, n));
    vec3 b = cross(n, t) * fHandedness;
    mat3 tbn = mat3(t, b, n);
    vec3 viewDir = normalize(uCameraPosition - fWorldPos);
    vec3 viewDirTS = transpose(tbn) * viewDir;
    float height = texture(uProps, fUV).g * uMat0.y + uMat0.z;
    vec2 uv = fUV + viewDirTS.xy * height;
    vec4 albedoTex = texture(uAlbedo, uv);
    // Alpha test, GEQUAL against the mode's hardcoded reference (uMat0.w; 0 = no test). Sourced like
    // litAlpha below.
    float testAlpha = uMat1.y > 0.5 ? (uMat1.w > 0.5 ? albedoTex.a * fColor.a : fColor.a) : albedoTex.a;
    if (uMat0.w > 0.0 && testAlpha < uMat0.w) discard;
    vec4 nrmSample = texture(uNormal, uv);
    vec2 derivativeSum = vec2(nrmSample.a * 2.0 - 1.0, nrmSample.g * 2.0 - 1.0);
    vec3 worldNormal = normalize(tbn * normalize(vec3(derivativeSum, 1.0)));
    vec4 props = texture(uProps, uv);
    float specIntensity = props.r;
    float emissive = props.b;
    vec3 albedo = pow(albedoTex.rgb, vec3(2.2)); // Color.rgb is always white in this engine (no vertex tint)
    vec3 envDiffuse = uEnvAmbient
        + uEnvLight0Colour * max(dot(worldNormal, uEnvDirection0), 0.0)
        + uEnvLight1Colour * max(dot(worldNormal, uEnvDirection1), 0.0);
    vec3 undecodedFill = mix(vec3(uAmbient), envDiffuse, uEnvHasLighting);
    vec2 uvCentred = fUV2 - uLightmapUVPivot;
    float sr = sin(radians(uLightmapUVRotation));
    float cr = cos(radians(uLightmapUVRotation));
    vec2 uvRot = vec2(uvCentred.x * cr - uvCentred.y * sr, uvCentred.x * sr + uvCentred.y * cr) + uLightmapUVPivot;
    vec2 bakedUV = uvRot * uLightmapUVScale + uLightmapUVOffset;
    vec4 bakedColour = texture(uLightColour, bakedUV);
    vec4 bakedDirSample = texture(uLightDir, bakedUV);
    vec3 bakedLightDirTS = vec3(bakedDirSample.r, bakedDirSample.b, bakedDirSample.g);
    float bl = length(bakedLightDirTS);
    bakedLightDirTS = bl > 0.0 ? bakedLightDirTS / bl : vec3(0.0, 0.0, 1.0);
    vec3 bakedNormalTS = normalize(vec3(derivativeSum * uBakedBumpFade, 1.0));
    float bakedNdotL = clamp(dot(bakedLightDirTS, bakedNormalTS), 0.0, 1.0);
    float bakedDiffuse = bakedLightDirTS.z > 0.0 ? bakedNdotL / bakedLightDirTS.z : bakedNdotL;
    vec3 bakedDiffuseLight = bakedColour.rgb * bakedDiffuse * uBakedLightScale;
    float hasBaked = uMat0.x;
    // A bake replaces the ambient fill rather than adding to it. Its N.L clamps to zero, so
    // uBakedAmbient keeps a fraction of the fill as a floor to stop it bottoming out to black.
    vec3 bakedFloor = undecodedFill * uBakedAmbient;
    vec3 lighting = mix(undecodedFill, max(bakedDiffuseLight, bakedFloor), hasBaked) + emissive;
    float bakedSpecLight = mix(1.0, bakedColour.a, hasBaked);
    vec3 reflDir = reflect(-viewDir, worldNormal);
    vec4 envTexel = texture(uEnvCube, reflDir);
    float envExposure = exp2((envTexel.a * 255.0 - 128.0) / 16.0);
    vec3 envColour = envTexel.rgb * envExposure;
    float NdotV = clamp(dot(worldNormal, viewDir), 0.0, 1.0);
    float fresnel = uReflectionBase + (1.0 - uReflectionBase) * pow(1.0 - NdotV, 5.0);
    float reflectivity = clamp(specIntensity + fresnel, 0.0, 1.0);
    vec3 envFill = envColour * albedo * uEnvironmentIntensity * reflectivity * bakedSpecLight;
    // UNLIT (uMat1.z == 0): the albedo straight through, still parallax-offset and alpha-tested.
    litColor = uMat1.z > 0.5
        ? pow(albedo * lighting + envFill, vec3(1.0 / 2.2))
        : albedoTex.rgb;
    // Opacity multiplies vertex alpha and the albedo's own alpha (when real, uMat1.w) for any
    // material with vertex alpha to contribute (uMat1.y). Opaque materials read straight from albedo.
    litAlpha = uMat1.y > 0.5 ? (uMat1.w > 0.5 ? albedoTex.a * fColor.a : fColor.a) : albedoTex.a;
}";
    private const string FragmentOpaqueGlsl = LitFragCommon + @"
layout(location = 0) out vec4 o;
void main() { vec3 c; float a; shade(c, a); o = vec4(c, 1.0); }";
    // Weighted-blended OIT accumulation. accum sums premultiplied colour * weight; reveal multiplies
    // down by (1 - alpha). Weight favours nearer, more-opaque fragments.
    private const string FragmentAccumGlsl = LitFragCommon + @"
layout(location = 0) out vec4 accum;
layout(location = 1) out float reveal;
void main() {
    vec3 c; float a; shade(c, a);
    float w = clamp(pow(min(1.0, a * 10.0) + 0.01, 3.0) * 1e8 * pow(1.0 - gl_FragCoord.z * 0.9, 3.0), 1e-2, 3e3);
    accum = vec4(c * a, a) * w;
    reveal = a;
}";
    // Additive (mode 2): the pipeline blends SrcAlpha/One, so the shader just outputs the lit colour
    // and its alpha. No OIT needed since addition is order-independent.
    private const string FragmentAdditiveGlsl = LitFragCommon + @"
layout(location = 0) out vec4 o;
void main() { vec3 c; float a; shade(c, a); o = vec4(c, a); }";
    private const string ResolveVertexGlsl = @"#version 450
void main() {
    vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}";
    // Composite: averageColor = accum.rgb / accum.a; blended over the opaque colour as
    // averageColor*(1-reveal) + dst*reveal (see the resolve pipeline's blend factors).
    private const string ResolveFragmentGlsl = @"#version 450
layout(set = 0, binding = 0) uniform sampler2D uAccum;
layout(set = 0, binding = 1) uniform sampler2D uReveal;
layout(location = 0) out vec4 o;
void main() {
    ivec2 c = ivec2(gl_FragCoord.xy);
    vec4 accum = texelFetch(uAccum, c, 0);
    float reveal = texelFetch(uReveal, c, 0).r;
    vec3 avg = accum.rgb / max(accum.a, 1e-5);
    o = vec4(avg, reveal);
}";

    // Volumes: a unit-cube wireframe drawn per volume, depth-tested against the opaque scene, coloured
    // by a per-volume push constant (box matrix + colour). Provided fresh each frame by View3D.
    // Debug lines: world-space segments with a per-vertex colour, drawn last with depth test off so
    // they always read on top (skeleton bones, vertex markers, bounding spheres, etc).
    private const string DebugLineVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; mat4 uPick; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec4 inColor;
layout(location = 0) out vec4 fColor;
void main() { fColor = inColor; gl_Position = uMvp * vec4(inPos, 1.0); }";

    private const string DebugLineFragmentGlsl = @"#version 450
layout(location = 0) in vec4 fColor;
layout(location = 0) out vec4 outColor;
void main() { outColor = fColor; }";

    // GPU colour-ID picking. uPick is the view-projection matrix post-multiplied by a clip-space
    // window around the cursor, so the render target is only a few pixels square and the frustum
    // planes reject everything that can't be under the cursor.
    private const string PickVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; mat4 uPick; };
layout(set = 0, binding = 1) readonly buffer Transforms { mat4 uT[]; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inUV;
layout(location = 2) in vec3 inNormal;
layout(location = 3) in vec4 inTangent;
layout(location = 4) in vec2 inUV2;
layout(location = 5) in vec4 inColor;
" + SkinningGlsl + @"
void main() {
    computeSkin(inPos, inNormal, inTangent.xyz);
    gl_Position = uPick * (uT[gl_InstanceIndex] * vec4(skinnedPosition, 1.0));
}";

    // Volume edges use their own per-draw world matrix rather than the transform SSBO, like the
    // visible wireframe pass.
    private const string PickVolumeVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; mat4 uPick; };
layout(push_constant) uniform Push { mat4 uWorld; uvec4 uId; };
layout(location = 0) in vec3 inPos;
void main() { gl_Position = uPick * (uWorld * vec4(inPos, 1.0)); }";

    // Foliage billboards need the same view-space corner offset as BillboardVertexGlsl, or every
    // corner projects to the shared anchor point and the card never appears in the pick target.
    // uPickProj is the projection half of the windowed pick matrix (see Pick()), since the offset
    // must be added in view space before the windowing is applied.
    private const string PickBillboardVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; mat4 uPick; mat4 uPickProj; };
layout(set = 0, binding = 1) readonly buffer Transforms { mat4 uT[]; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inUV;
layout(location = 2) in vec3 inNormal;
layout(location = 3) in vec4 inTangent;
layout(location = 4) in vec2 inUV2;
layout(location = 5) in vec4 inColor;
void main() {
    mat4 m = uT[gl_InstanceIndex];
    vec4 anchorView = uView * (m * vec4(inPos, 1.0));
    vec2 instanceScale = vec2(length(m[0].xyz), length(m[1].xyz));
    anchorView.xy += inUV2 * instanceScale;
    gl_Position = uPickProj * anchorView;
}";

    // The id is written as four bytes of an RGBA8 target (little-endian on readback), keeping the
    // whole path to plain colour attachments with no integer-format support needed.
    private const string PickFragmentGlsl = @"#version 450
layout(push_constant) uniform Push { mat4 uWorld; uvec4 uId; };
layout(location = 0) out vec4 outColor;
void main() {
    uint id = uId.x;
    outColor = vec4(
        float(id & 0xFFu) / 255.0,
        float((id >> 8) & 0xFFu) / 255.0,
        float((id >> 16) & 0xFFu) / 255.0,
        float((id >> 24) & 0xFFu) / 255.0);
}";

    // Foliage. Every vertex stores the sprite card's anchor as its position and its 2D corner offset
    // in the lightmap UV slot; the card faces the camera by adding that offset after the view
    // transform, so nothing is billboarded on the CPU. Instance scale is recovered from the model
    // matrix's X/Y basis lengths. Mirrors billboardv.glsl, but reads the world matrix from the
    // transform SSBO.
    private const string BillboardVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; mat4 uView; mat4 uProj; };
layout(set = 0, binding = 1) readonly buffer Transforms { mat4 uT[]; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inUV;
layout(location = 2) in vec3 inNormal;
layout(location = 3) in vec4 inTangent;
layout(location = 4) in vec2 inUV2;
layout(location = 5) in vec4 inColor;
layout(location = 0) out vec2 fUV;
layout(location = 1) out vec4 fColor;
void main() {
    mat4 m = uT[gl_InstanceIndex];
    fUV = inUV;
    fColor = inColor;
    vec4 anchorView = uView * (m * vec4(inPos, 1.0));
    vec2 instanceScale = vec2(length(m[0].xyz), length(m[1].xyz));
    anchorView.xy += inUV2 * instanceScale;
    gl_Position = uProj * anchorView;
}";

    private const string BillboardFragmentGlsl = @"#version 450
layout(set = 1, binding = 0) uniform sampler2D uAlbedo;
layout(push_constant) uniform Push { vec4 uMat0; vec4 uMat1; };
layout(location = 0) in vec2 fUV;
layout(location = 1) in vec4 fColor;
layout(location = 0) out vec4 outColor;
void main() {
    vec4 texel = texture(uAlbedo, fUV);
    // Same combine as LitFragCommon.shade's testAlpha - see that comment.
    float testAlpha = uMat1.y > 0.5 ? (uMat1.w > 0.5 ? texel.a * fColor.a : fColor.a) : texel.a;
    if (uMat0.w > 0.0 && testAlpha < uMat0.w) discard;
    outColor = vec4(texel.rgb * fColor.rgb, 1.0);
}";

    // Selection outline: a two-pass stencil "mask and inflate".
    // Pass 1 (uParams.x == 0) redraws the real geometry with colour writes off, stamping stencil 1 over
    // the selected object's visible footprint. Pass 2 (uParams.x > 0) redraws it inflated in clip space
    // with the stencil test set to NotEqual 1, so only the part sticking out past that footprint (the
    // rim) survives.
    private const string OutlineVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(set = 0, binding = 1) readonly buffer Transforms { mat4 uT[]; };
layout(push_constant) uniform Push { vec4 uColor; vec4 uParams; };
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inUV;
layout(location = 2) in vec3 inNormal;
layout(location = 3) in vec4 inTangent;
layout(location = 4) in vec2 inUV2;
layout(location = 5) in vec4 inColor;
" + SkinningGlsl + @"
void main() {
    computeSkin(inPos, inNormal, inTangent.xyz);
    mat4 m = uT[gl_InstanceIndex];
    vec4 clipPos = uMvp * (m * vec4(skinnedPosition, 1.0));
    if (uParams.x > 0.0) {
        vec4 clipNormal = uMvp * (m * vec4(skinnedNormal, 0.0));
        if (length(clipNormal.xy) > 0.0001)
            clipPos.xy += normalize(clipNormal.xy) * uParams.x * clipPos.w;
    } else {
        // The mask redraws vertices the opaque pass already wrote depth for, through a different
        // pipeline, so the LessEqual test is not guaranteed to win on bit-identical depth. Nudge
        // toward the camera - far smaller than any real occlusion gap, so occlusion still masks out.
        clipPos.z -= 0.0005 * clipPos.w;
    }
    gl_Position = clipPos;
}";

    private const string OutlineFragmentGlsl = @"#version 450
layout(push_constant) uniform Push { vec4 uColor; vec4 uParams; };
layout(location = 0) out vec4 outColor;
void main() { outColor = uColor; }";

    private const string VolumeVertexGlsl = @"#version 450
layout(set = 0, binding = 0) uniform Mvp { mat4 uMvp; };
layout(push_constant) uniform PC { mat4 uModel; vec4 uColor; };
layout(location = 0) in vec3 inPos;
void main() { gl_Position = uMvp * (uModel * vec4(inPos, 1.0)); }";
    private const string VolumeFragmentGlsl = @"#version 450
layout(push_constant) uniform PC { mat4 uModel; vec4 uColor; };
layout(location = 0) out vec4 o;
void main() { o = vec4(uColor.rgb, 1.0); }";

    private readonly VulkanContext _ctx;
    private readonly VkDeviceApi _api;

    private readonly int _instanceCount;
    private readonly uint[] _drawIndexCount;
    private readonly uint[] _drawFirstIndex;
    private readonly int[] _drawVertexOffset;
    private readonly int[] _drawMatSlot;
    private readonly Vector4[] _matPC0;
    private readonly float[] _matRenderMode;   // the game's mode 0-6 (pushed as uMat1.x)
    private readonly float[] _matVertexAlpha;
    private readonly float[] _matAlbedoHasAlpha; // pushed as uMat1.w
    private readonly float[] _matAlphaRef;     // per-mode alpha-test reference (GEQUAL), 0 = no test
    // Bucket boundaries in the mode-sorted instance list: [0,_overStart) opaque+cutout,
    // [_overStart,_addStart) over-blended (WBOIT), [_addStart,_softStart) additive,
    // [_softStart,_billStart) soft-edge (drawn twice: depth prepass + WBOIT),
    // [_billStart,_instanceCount) foliage billboards.
    private readonly int _overStart, _addStart, _softStart, _billStart;

    // Frustum culling: static per-instance world bounding spheres, plus per-frame scratch. Visible
    // lists hold indices into the sorted static arrays, ordered so material-run batching still holds;
    // culling is threaded over chunks that compact into disjoint regions of _cullScratch, then merged.
    private readonly Vector3[] _instCenter;
    private readonly float[] _instRadius;
    private readonly byte[] _instKind;
    private VkSampler _samplerPoint, _samplerLinear;
    private VkImageView[]? _matViews;
    private TextureFiltering _filtering = TextureFiltering.Bilinear;
    private byte _kindMask = (byte)SceneEntityKind.All;
    private bool _frustumCulling = true;
    /// <summary>Per-instance in-game display distance (units); negative = unlimited. The game's
    /// per-placement cull radius, read from the level's gameplay data.</summary>
    private readonly float[] _instDisplayDist;
    private Vector3 _cameraPosition;
    private bool _mobyDistanceCulling;
    private readonly Vector4[] _planes = new Vector4[6];
    private readonly int[] _cullScratch;
    private readonly int[] _chunkCounts;
    private readonly int[] _visibleOpaque;
    private readonly int[] _visibleTranslucent;
    private readonly int[] _visibleAdditive;
    private readonly int[] _visibleSoftEdge;
    private readonly int[] _visibleBillboard;
    private int _visOpaqueCount, _visTransCount, _visAddCount, _visSoftCount, _visBillCount;

    private uint _width, _height;
    private readonly Texture _envCube;
    private readonly bool _ownsEnvCube;

    /// <summary>Frames the scene pass keeps in flight. Two, so the GPU can execute the previous frame
    /// while this one is culled and recorded; the fence wait happens after recording and the submit
    /// after swapchain present, creating the overlap. Per-slot state (command buffer, uniform buffers,
    /// descriptor set, colour target) is what makes this safe, since ImGui samples the previous frame's
    /// colour target while the GPU writes this one's. Depth, accum and reveal stay shared since only
    /// one command buffer is ever submitted at a time.</summary>
    private const int Frames = 2;

    private VkCommandPool _pool;
    private readonly VkCommandBuffer[] _cmds = new VkCommandBuffer[Frames];
    private readonly VkFence[] _fences = new VkFence[Frames];
    private readonly bool[] _pending = new bool[Frames];
    /// <summary>Slot being recorded this frame. The other slot holds the frame on screen.</summary>
    private int _slot;
    /// <summary>Slot whose colour target is complete and safe for ImGui to sample.</summary>
    private int _displaySlot;
    private bool _everSubmitted;
    /// <summary>A frame has been recorded and is waiting for <see cref="SubmitFrame"/>.</summary>
    private bool _recorded;
    /// <summary>The command buffer currently being recorded. Every vkCmd* helper writes through this.</summary>
    private VkCommandBuffer _cmd;
    private VkBuffer _vertexBuffer; private VkDeviceMemory _vbMemory;
    private VkBuffer _indexBuffer; private VkDeviceMemory _ibMemory;
    private readonly VkBuffer[] _uniformBuffers = new VkBuffer[Frames];
    private readonly VkDeviceMemory[] _ubMemories = new VkDeviceMemory[Frames];
    private readonly void*[] _ubMappings = new void*[Frames];
    private readonly VkBuffer[] _lightBuffers = new VkBuffer[Frames];
    private readonly VkDeviceMemory[] _lightMemories = new VkDeviceMemory[Frames];
    private readonly void*[] _lightMappings = new void*[Frames];
    private VkBuffer _transformBuffer; private VkDeviceMemory _tbMemory; private void* _tbMapped;
    // GPU skeletal animation: a fixed-size bone palette with one shared "identity" region (index 0,
    // every non-animating instance points here) plus one reserved region per moby in the scene that
    // has a skeleton (_maxConcurrentAnimated - every skinned moby could in principle be playing at
    // once, so this can never run out), each maxSkeletonBones mat4s wide. Pre-sized once at scene
    // build so starting/stopping an animation only ever writes into an already-allocated region (see
    // TrySetAnimatedInstance) - never a buffer resize or descriptor rebind, which would need to
    // happen on the low-frequency Play/Stop path but per-frame pose updates must stay cheap.
    private readonly int _maxSkeletonBones;
    private readonly int _maxConcurrentAnimated;
    private VkBuffer _bonePaletteBuffer; private VkDeviceMemory _bpMemory; private void* _bpMapped;
    private VkBuffer _boneBaseBuffer; private VkDeviceMemory _bbMemory; private void* _bbMapped;
    private readonly object?[] _animRegionOwners;
    private VkDescriptorSetLayout _descLayout; private VkDescriptorSetLayout _matSetLayout; private VkDescriptorSetLayout _resolveSetLayout;
    private VkDescriptorPool _descPool; private readonly VkDescriptorSet[] _descSets = new VkDescriptorSet[Frames]; private VkDescriptorSet _resolveSet;
    /// <summary>Set 0 for the slot being recorded.</summary>
    private VkDescriptorSet _descSet;
    private VkSampler _sampler;
    private VkImageView _envCubeView;
    private VkImageView[] _texViews = [];
    private VkDescriptorSet[] _matSets = [];
    private VkRenderPass _rpOpaque, _rpAccum, _rpResolve;
    private VkShaderModule _vs, _fsOpaque, _fsAccum, _fsAdditive, _resolveVs, _resolveFs, _volumeVs, _volumeFs;
    private VkShaderModule _outlineVs, _outlineFs;
    private VkPipelineLayout _layout, _resolveLayout, _volumeLayout, _outlineLayout;
    private VkPipeline _pipelineOpaque, _pipelineAccum, _pipelineResolve, _volumePipeline;
    private VkPipeline _pipelineSoftEdgeDepth, _pipelineAdditive;
    private VkPipeline _pipelineOutlineMask, _pipelineOutlineRim, _pipelineBillboard;

    // GPU picking: a PickTargetSize-square colour+depth target, its own one-shot command buffer and
    // fence, and a host-visible buffer the result is copied into. Sized once, never resized: the pick
    // window is a fixed number of screen pixels regardless of viewport size.
    private const uint PickTargetSize = 8;
    /// <summary>Side, in screen pixels, of the square window around the cursor a pick can resolve to.
    /// Clicks near a thin silhouette can land a pixel or two off, so the nearest hit inside this window
    /// wins rather than demanding the exact pixel under the cursor.</summary>
    public const int PickWindowPixels = 5;
    /// <summary>Returned by <see cref="Pick"/> when nothing was under the cursor.</summary>
    public const uint NoHit = uint.MaxValue;
    // 5 mat4s - see CreateUniformBuffers for what each slot holds.
    private const ulong UboSize = 320;
    private VkRenderPass _rpPick;
    private VkPipeline _pipelinePick, _pipelinePickVolume, _pipelinePickBillboard;
    private VkPipelineLayout _pickLayout;
    private VkShaderModule _pickVs, _pickVolumeVs, _pickFs, _pickBillboardVs;
    private VkImage _pickImage; private VkDeviceMemory _pickMemory; private VkImageView _pickView;
    private VkImage _pickDepthImage; private VkDeviceMemory _pickDepthMemory; private VkImageView _pickDepthView;
    private VkFramebuffer _fbPick;
    private VkBuffer _pickReadback; private VkDeviceMemory _pickReadbackMemory; private void* _pickReadbackMapped;
    private VkCommandBuffer _pickCmd; private VkFence _pickFence;
    private readonly uint[] _instPickId;
    private readonly Vector4[] _pickPlanes = new Vector4[6];

    // Debug line list, refilled every frame. The buffer grows to the high-water mark and is never shrunk.
    private const int DebugLineFloats = 7; // pos xyz + rgba
    private VkPipeline _pipelineDebugLines;
    private VkShaderModule _debugLineVs, _debugLineFs;
    private VkBuffer _debugLineBuffer; private VkDeviceMemory _debugLineMemory; private void* _debugLineMapped;
    private int _debugLineCapacity;
    private int _debugVertexCount;

    /// <summary>Colour the scene is cleared to before anything is drawn.</summary>
    public Vector4 ClearColour = new(0.05f, 0.06f, 0.09f, 1f);
    private VkShaderModule _billboardVs, _billboardFs;

    // Which scene instances belong to which entity, so the selection outline and live transform edits
    // can address one entity's draws without rescanning the instance list every frame.
    private readonly Dictionary<object, int[]> _ownerInstances = new(ReferenceEqualityComparer.Instance);
    private object? _selected;
    /// <summary>Lit shading on/off. Unlit still parallax-offsets and alpha-tests, so silhouettes and
    /// cutouts are unchanged - only the lighting, baked or analytic, and the reflections drop out.</summary>
    private bool _lit = true;
    private Vector4 _outlineColor = new(1f, 0.6f, 0.1f, 1f);
    private float _outlineThickness = 0.004f;

    // Thin-box edge geometry (8 verts, 12 tri indices; thickness-dependent, host-mapped so it rebuilds
    // when the setting changes) + the per-frame volume-edge list (one (worldMatrix, colour) per edge,
    // 12 per volume).
    private VkBuffer _edgeVertexBuffer; private VkDeviceMemory _edgeVbMemory; private void* _edgeVbMapped;
    private VkBuffer _edgeIndexBuffer; private VkDeviceMemory _edgeIbMemory; private uint _edgeIndexCount;
    private float _edgeThickness = -1f;
    private IReadOnlyList<(Matrix4x4 world, Vector4 color, uint pickId)> _volumes = System.Array.Empty<(Matrix4x4, Vector4, uint)>();
    private int _volumeCount;

    // Size-dependent targets. Colour is per-slot (ImGui reads one while the GPU writes the other);
    // depth/accum/reveal are shared, see the Frames comment.
    private readonly Texture[] _colorTex = new Texture[Frames];
    private readonly VkImage[] _colorImage = new VkImage[Frames];
    private readonly VkImageView[] _colorView = new VkImageView[Frames];
    private VkImage _depthImage; private VkDeviceMemory _depthMemory; private VkImageView _depthView;
    private VkImage _accumImage; private VkDeviceMemory _accumMemory; private VkImageView _accumView;
    private VkImage _revealImage; private VkDeviceMemory _revealMemory; private VkImageView _revealView;
    private readonly VkFramebuffer[] _fbOpaque = new VkFramebuffer[Frames];
    private readonly VkFramebuffer[] _fbResolve = new VkFramebuffer[Frames];
    private VkFramebuffer _fbAccum;

    private long _submits; private bool _loggedInit;

    /// <summary>Draws that survived this frame's culling, across every bucket.</summary>
    public int VisibleDrawCount => _visOpaqueCount + _visTransCount + _visAddCount + _visSoftCount + _visBillCount;

    /// <summary>The scene image for ImGui to display: the last completed frame, not the one being
    /// recorded, so the viewport runs one frame behind the camera.</summary>
    public Texture ColorTexture => _colorTex[_displaySlot];

    public VulkanRenderer(GraphicsDevice graphicsDevice, List<float[]> geomVerts, List<uint[]> geomIndices, List<VkMaterialDesc> materials, List<(int geo, int mat, Matrix4x4 world, Vector4 sphere, object owner, float displayDistance, uint pickId)> instances, Texture? envCube, uint width, uint height, int maxSkeletonBones = 0, int maxConcurrentAnimated = 0)
    {
        _ctx = new VulkanContext(graphicsDevice);
        _api = _ctx.DeviceApi;
        _instanceCount = instances.Count;
        _maxSkeletonBones = Math.Max(maxSkeletonBones, 0);
        _maxConcurrentAnimated = Math.Max(maxConcurrentAnimated, 0);
        _animRegionOwners = new object?[_maxConcurrentAnimated];
        _width = Math.Max(width, 1u);
        _height = Math.Max(height, 1u);
        // A scene without an environment cubemap is legitimate (e.g. the asset preview before any
        // level is loaded). Falls back to a 1x1 mid-grey cube; reflections are gated on
        // EnvironmentIntensity, which is 0 without a level, so it just keeps the descriptor bound.
        _ownsEnvCube = envCube == null;
        _envCube = envCube ?? CreateFallbackCubemap(graphicsDevice);

        _matPC0 = new Vector4[materials.Count];
        _matRenderMode = new float[materials.Count];
        _matVertexAlpha = new float[materials.Count];
        _matAlbedoHasAlpha = new float[materials.Count];
        _matAlphaRef = new float[materials.Count];
        for (int i = 0; i < materials.Count; i++)
        {
            var mode = (GameRenderMode)(byte)materials[i].GameRenderMode;
            // Alpha-test references are hardcoded per mode by the engine, not per material. Cutout
            // clips at 128/255; the blended paths clip at 4/255 to skip fully-transparent texels.
            float alphaRef = mode switch
            {
                // Foliage source shaders classify as Blended (to route into the game's polygon-offset
                // pass), but the cards are alpha-cut leaves, so they clip like Cutout and write depth.
                _ when materials[i].IsBillboard > 0.5f => 128f / 255f,
                GameRenderMode.Cutout => 128f / 255f,
                GameRenderMode.SoftEdge => 4f / 255f,   // pass 2 (the depth prepass uses 128/255, below)
                GameRenderMode.Overlay or GameRenderMode.Additive => 4f / 255f,
                _ => 0f,
            };
            _matPC0[i] = new Vector4(materials[i].HasBaked, materials[i].ParallaxScale, materials[i].ParallaxBias, alphaRef);
            _matRenderMode[i] = materials[i].GameRenderMode;
            _matVertexAlpha[i] = materials[i].UsesVertexAlpha;
            _matAlbedoHasAlpha[i] = materials[i].AlbedoHasAlphaChannel;
            _matAlphaRef[i] = alphaRef;
        }

        int geoCount = geomVerts.Count;
        var baseVertex = new int[geoCount];
        var firstIndex = new uint[geoCount];
        var idxCount = new uint[geoCount];
        int vTotal = 0; uint iTotal = 0;
        for (int g = 0; g < geoCount; g++)
        {
            baseVertex[g] = vTotal;
            firstIndex[g] = iTotal;
            idxCount[g] = (uint)geomIndices[g].Length;
            vTotal += geomVerts[g].Length / VulkanSceneCapture.FloatsPerVertex;
            iTotal += (uint)geomIndices[g].Length;
        }
        var mergedVerts = new float[vTotal * VulkanSceneCapture.FloatsPerVertex];
        var mergedIdx = new uint[iTotal];
        int vo = 0; uint io = 0;
        for (int g = 0; g < geoCount; g++)
        {
            var p = geomVerts[g];
            Array.Copy(p, 0, mergedVerts, vo, p.Length); vo += p.Length;
            var ix = geomIndices[g];
            Array.Copy(ix, 0, mergedIdx, (int)io, ix.Length); io += (uint)ix.Length;
        }

        // Bucket each material by the game's render mode, then order instances bucket-major (and by
        // material within a bucket, so each material's descriptor set still binds once per run):
        //   0 OPAQUE     modes Opaque + Cutout (+ Soft-Edge's depth prepass, drawn from bucket 3)
        //   1 OVER       modes Overlay/Scunge/Blended + Soft-Edge colour pass -> WBOIT accumulate
        //   2 ADDITIVE   mode Additive (SrcAlpha/One) -> its own additive pass
        //   3 SOFTEDGE   mode Soft-Edge: drawn twice (depth prepass in the opaque pass, then WBOIT)
        //   4 BILLBOARD  foliage sprite cards -> the billboard vertex shader, alpha-tested, opaque pass
        var matBucket = new int[materials.Count];
        for (int i = 0; i < materials.Count; i++)
            matBucket[i] = materials[i].IsBillboard > 0.5f ? 4 : (GameRenderMode)(byte)materials[i].GameRenderMode switch
            {
                GameRenderMode.Opaque or GameRenderMode.Cutout => 0,
                GameRenderMode.Additive => 2,
                GameRenderMode.SoftEdge => 3,
                _ => 1, // Overlay, Scunge, Blended
            };
        var order = new int[_instanceCount];
        for (int i = 0; i < _instanceCount; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int ba = matBucket[instances[a].mat], bb = matBucket[instances[b].mat];
            return ba != bb ? ba - bb : instances[a].mat.CompareTo(instances[b].mat);
        });

        _drawIndexCount = new uint[_instanceCount];
        _drawFirstIndex = new uint[_instanceCount];
        _drawVertexOffset = new int[_instanceCount];
        _drawMatSlot = new int[_instanceCount];
        _instCenter = new Vector3[_instanceCount];
        _instRadius = new float[_instanceCount];
        _instKind = new byte[_instanceCount];
        _instDisplayDist = new float[_instanceCount];
        _instPickId = new uint[_instanceCount];
        var worlds = new Matrix4x4[_instanceCount];
        int overStart = _instanceCount, addStart = _instanceCount, softStart = _instanceCount, billStart = _instanceCount;
        var ownerRuns = new Dictionary<object, List<int>>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < _instanceCount; i++)
        {
            var (g, mat, world, sphere, owner, displayDistance, pickId) = instances[order[i]];
            if (owner is not null)
            {
                if (!ownerRuns.TryGetValue(owner, out var run)) ownerRuns[owner] = run = new List<int>(1);
                run.Add(i);
            }
            _drawIndexCount[i] = idxCount[g];
            _drawFirstIndex[i] = firstIndex[g];
            _drawVertexOffset[i] = baseVertex[g];
            _drawMatSlot[i] = mat;
            worlds[i] = world;
            // The game's own world bounding sphere (Entity.WorldBoundingSphere): xyz centre, w radius.
            _instCenter[i] = new Vector3(sphere.X, sphere.Y, sphere.Z);
            _instRadius[i] = sphere.W;
            // Resolved from the owner rather than passed in: the owner is already here, and widening the
            // instance tuple again would make every caller carry a field only the level view can fill.
            _instKind[i] = (byte)(owner switch
            {
                Scene.EntityMoby => SceneEntityKind.Moby,
                Scene.EntityTie => SceneEntityKind.Tie,
                Scene.EntityUFrag => SceneEntityKind.UFrag,
                Scene.EntityFoliage => SceneEntityKind.Foliage,
                _ => SceneEntityKind.Other,
            });
            _instDisplayDist[i] = displayDistance;
            _instPickId[i] = pickId;
            int bucket = matBucket[mat];
            if (bucket >= 1 && i < overStart) overStart = i;
            if (bucket >= 2 && i < addStart) addStart = i;
            if (bucket >= 3 && i < softStart) softStart = i;
            if (bucket >= 4 && i < billStart) billStart = i;
        }
        foreach (var (owner, run) in ownerRuns) _ownerInstances[owner] = [.. run];
        // Empty buckets collapse to the following bucket's start so every range stays well-ordered.
        _billStart = billStart;
        _softStart = Math.Min(softStart, _billStart);
        _addStart = Math.Min(addStart, _softStart);
        _overStart = Math.Min(overStart, _addStart);

        // Per-frame culling scratch (no per-frame allocation).
        _cullScratch = new int[_instanceCount];
        _visibleOpaque = new int[_instanceCount];
        _visibleTranslucent = new int[_instanceCount];
        _visibleAdditive = new int[_instanceCount];
        _visibleSoftEdge = new int[_instanceCount];
        _visibleBillboard = new int[_instanceCount];
        _chunkCounts = new int[Environment.ProcessorCount];

        var poolInfo = new VkCommandPoolCreateInfo { flags = VkCommandPoolCreateFlags.ResetCommandBuffer, queueFamilyIndex = _ctx.GraphicsQueueFamilyIndex };
        VkCommandPool pool; Check(_api.vkCreateCommandPool(&poolInfo, &pool), "vkCreateCommandPool"); _pool = pool;
        var cbAlloc = new VkCommandBufferAllocateInfo { commandPool = _pool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1 };
        var fenceInfo = new VkFenceCreateInfo();
        for (int f = 0; f < Frames; f++)
        {
            VkCommandBuffer cmd; Check(_api.vkAllocateCommandBuffers(&cbAlloc, &cmd), "vkAllocateCommandBuffers"); _cmds[f] = cmd;
            VkFence fence; Check(_api.vkCreateFence(&fenceInfo, &fence), "vkCreateFence"); _fences[f] = fence;
        }
        _cmd = _cmds[0];

        var vsw = System.Diagnostics.Stopwatch.StartNew(); long vt0 = 0;
        UploadGeometry(mergedVerts, mergedIdx);
        CreateVolumeGeometry();
        UploadTransforms(worlds);
        UploadBonePalette();
        UploadBoneBase();
        long tUpload = vsw.ElapsedMilliseconds - vt0; vt0 = vsw.ElapsedMilliseconds;
        CreateUniformBuffers();
        CreateSampler();
        long tSamplers = vsw.ElapsedMilliseconds - vt0; vt0 = vsw.ElapsedMilliseconds;
        // The one phase here that scales with the level's material count rather than a fixed
        // viewport/shader cost.
        CreateDescriptors(materials);
        long tDescriptors = vsw.ElapsedMilliseconds - vt0; vt0 = vsw.ElapsedMilliseconds;
        CreateRenderPasses();
        CreatePipelines();
        long tPipelines = vsw.ElapsedMilliseconds - vt0; vt0 = vsw.ElapsedMilliseconds;
        CreateTargets(graphicsDevice);
        CreatePickResources();
        long tTargets = vsw.ElapsedMilliseconds - vt0;
        long buildMs = vsw.ElapsedMilliseconds;
        // Command buffer is recorded per-frame in Frame() (only the visible, frustum-culled draws).

        // Per-mode material census, logged below.
        var modeCensus = new int[7];
        var modeVertexAlpha = new int[7];
        foreach (var m in materials)
        {
            int mi = Math.Clamp((int)m.GameRenderMode, 0, 6);
            modeCensus[mi]++;
            if (m.UsesVertexAlpha > 0.5f) modeVertexAlpha[mi]++;
        }
        string[] modeNames = ["Opaque", "Overlay", "Additive", "Scunge", "Cutout", "SoftEdge", "Blended"];
        var census = string.Join(", ", Enumerable.Range(0, 7)
            .Where(m => modeCensus[m] > 0)
            .Select(m => $"{modeNames[m]}={modeCensus[m]}" + (modeVertexAlpha[m] > 0 ? $"({modeVertexAlpha[m]} vtxA)" : "")));
        Console.WriteLine($"[VkRenderer] init OK - {geoCount} geometries, {materials.Count} materials, {_instanceCount} draws. Buckets: {_overStart} opaque/cutout, {_addStart - _overStart} over-blended, {_softStart - _addStart} additive, {_billStart - _softStart} soft-edge, {_instanceCount - _billStart} foliage. Materials by mode: {census}. Into a {_width}x{_height} texture. Built in {buildMs}ms (upload {tUpload}, samplers/UBOs {tSamplers}, descriptors {tDescriptors}, pipelines {tPipelines}, targets {tTargets}).");
    }

    private void UploadGeometry(float[] mergedVerts, uint[] mergedIdx)
    {
        ulong vSize = (ulong)(mergedVerts.Length * sizeof(float));
        (_vertexBuffer, _vbMemory) = CreateBuffer(vSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* vp; Check(_api.vkMapMemory(_vbMemory, 0, vSize, 0, &vp), "vkMapMemory(vb)");
        fixed (float* s = mergedVerts) Buffer.MemoryCopy(s, vp, vSize, vSize);
        _api.vkUnmapMemory(_vbMemory);

        ulong iSize = (ulong)(mergedIdx.Length * sizeof(uint));
        (_indexBuffer, _ibMemory) = CreateBuffer(iSize, VkBufferUsageFlags.IndexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* ip; Check(_api.vkMapMemory(_ibMemory, 0, iSize, 0, &ip), "vkMapMemory(ib)");
        fixed (uint* s = mergedIdx) Buffer.MemoryCopy(s, ip, iSize, iSize);
        _api.vkUnmapMemory(_ibMemory);
    }

    private void CreateVolumeGeometry()
    {
        // Two quads (thin in Y, thin in Z) -> a unit-length "+" cross section, 4 verts + 2 tris each.
        uint[] idx = { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        _edgeIndexCount = (uint)idx.Length;
        ulong iSize = (ulong)(idx.Length * sizeof(uint));
        (_edgeIndexBuffer, _edgeIbMemory) = CreateBuffer(iSize, VkBufferUsageFlags.IndexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* ip; Check(_api.vkMapMemory(_edgeIbMemory, 0, iSize, 0, &ip), "vkMapMemory(edgeIb)");
        fixed (uint* s = idx) Buffer.MemoryCopy(s, ip, iSize, iSize);
        _api.vkUnmapMemory(_edgeIbMemory);

        // Vertex buffer stays host-mapped so WriteEdgeVertices can rebuild it when the thickness changes.
        ulong vSize = 8 * 3 * sizeof(float);
        (_edgeVertexBuffer, _edgeVbMemory) = CreateBuffer(vSize, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* vp; Check(_api.vkMapMemory(_edgeVbMemory, 0, vSize, 0, &vp), "vkMapMemory(edgeVb)"); _edgeVbMapped = vp;
    }

    // Rebuilds the unit edge's 8 vertices for a given world-space thickness (t = thickness/2), matching
    // Length +/-0.5 along X, thin +/-t on Y (quad 1) and Z (quad 2).
    private void WriteEdgeVertices(float thickness)
    {
        // Only runs when the wire-thickness setting changes, so the drain is free.
        WaitForPendingFrames();
        float t = MathF.Max(thickness, 0.001f) * 0.5f;
        float* p = (float*)_edgeVbMapped;
        // quad 1 (thin in Y)
        p[0] = -0.5f; p[1] = -t; p[2] = 0f;   p[3] = 0.5f; p[4] = -t; p[5] = 0f;
        p[6] = 0.5f;  p[7] = t;  p[8] = 0f;    p[9] = -0.5f; p[10] = t; p[11] = 0f;
        // quad 2 (thin in Z)
        p[12] = -0.5f; p[13] = 0f; p[14] = -t; p[15] = 0.5f; p[16] = 0f; p[17] = -t;
        p[18] = 0.5f;  p[19] = 0f; p[20] = t;  p[21] = -0.5f; p[22] = 0f; p[23] = t;
        _edgeThickness = thickness;
    }

    private void UploadTransforms(Matrix4x4[] worlds)
    {
        ulong size = (ulong)(Math.Max(worlds.Length, 1) * 64);
        (_transformBuffer, _tbMemory) = CreateBuffer(size, VkBufferUsageFlags.StorageBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        // Stays mapped for the lifetime of the renderer: moving an entity in the editor rewrites its
        // matrices here in place, which is what lets a gizmo edit show up without re-recording anything.
        void* p; Check(_api.vkMapMemory(_tbMemory, 0, size, 0, &p), "vkMapMemory(tb)"); _tbMapped = p;
        var dst = (Matrix4x4*)p;
        for (int i = 0; i < worlds.Length; i++) dst[i] = worlds[i];
    }

    /// <summary>Region 0 (shared identity) plus _maxConcurrentAnimated reserved regions, each
    /// _maxSkeletonBones mat4s. All initialized to identity, so any instance pointing at a
    /// not-yet-assigned region still renders in bind pose rather than garbage.</summary>
    private void UploadBonePalette()
    {
        int bonesPerRegion = Math.Max(_maxSkeletonBones, 1);
        int totalMats = bonesPerRegion * (1 + _maxConcurrentAnimated);
        ulong size = (ulong)totalMats * 64;
        (_bonePaletteBuffer, _bpMemory) = CreateBuffer(size, VkBufferUsageFlags.StorageBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* p; Check(_api.vkMapMemory(_bpMemory, 0, size, 0, &p), "vkMapMemory(bp)"); _bpMapped = p;
        var dst = (Matrix4x4*)p;
        for (int i = 0; i < totalMats; i++) dst[i] = Matrix4x4.Identity;
    }

    /// <summary>One bone-palette region offset per draw instance, defaulting to 0 (the shared identity
    /// region) - see TrySetAnimatedInstance.</summary>
    private void UploadBoneBase()
    {
        ulong size = (ulong)(Math.Max(_instanceCount, 1) * sizeof(int));
        (_boneBaseBuffer, _bbMemory) = CreateBuffer(size, VkBufferUsageFlags.StorageBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* p; Check(_api.vkMapMemory(_bbMemory, 0, size, 0, &p), "vkMapMemory(bb)"); _bbMapped = p;
        var dst = (int*)p;
        for (int i = 0; i < _instanceCount; i++) dst[i] = 0;
    }

    private void CreateUniformBuffers()
    {
        // 5 * mat4: viewProj, view, proj, the pick window's view-projection, and the pick window's
        // projection-only (for billboards, see PickBillboardVertexGlsl). A shader may declare only a
        // prefix of the UBO's contents.
        // One set per in-flight frame, since these are written while the previous frame still reads
        // its own copy on the GPU.
        ulong lightSize = (ulong)sizeof(LightData);
        for (int f = 0; f < Frames; f++)
        {
            (_uniformBuffers[f], _ubMemories[f]) = CreateBuffer(UboSize, VkBufferUsageFlags.UniformBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            void* p; Check(_api.vkMapMemory(_ubMemories[f], 0, UboSize, 0, &p), "vkMapMemory(ub)"); _ubMappings[f] = p;
            var ident = (Matrix4x4*)p;
            ident[0] = ident[1] = ident[2] = ident[3] = ident[4] = Matrix4x4.Identity;

            (_lightBuffers[f], _lightMemories[f]) = CreateBuffer(lightSize, VkBufferUsageFlags.UniformBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            void* lp; Check(_api.vkMapMemory(_lightMemories[f], 0, lightSize, 0, &lp), "vkMapMemory(light)"); _lightMappings[f] = lp;
            *(LightData*)lp = default;
        }
    }

    // Both filtering modes, created up front. Game textures always tile, so only the filter differs;
    // switching modes rewrites the material descriptor sets rather than rebuilding anything.
    private void CreateSampler()
    {
        _samplerLinear = CreateWrapSampler(VkFilter.Linear, VkSamplerMipmapMode.Linear);
        _samplerPoint = CreateWrapSampler(VkFilter.Nearest, VkSamplerMipmapMode.Nearest);
        _sampler = _filtering == TextureFiltering.Point ? _samplerPoint : _samplerLinear;
    }

    private VkSampler CreateWrapSampler(VkFilter filter, VkSamplerMipmapMode mipmapMode)
    {
        var info = new VkSamplerCreateInfo
        {
            magFilter = filter, minFilter = filter, mipmapMode = mipmapMode,
            addressModeU = VkSamplerAddressMode.Repeat, addressModeV = VkSamplerAddressMode.Repeat, addressModeW = VkSamplerAddressMode.Repeat,
            minLod = 0f, maxLod = 16f, mipLodBias = 0f, maxAnisotropy = 1f,
        };
        VkSampler s; Check(_api.vkCreateSampler(&info, &s), "vkCreateSampler"); return s;
    }

    /// <summary>Switches how the scene's textures are sampled. Only rewrites the material descriptor
    /// sets, which is safe with no extra synchronisation because Frame() waits its own fence before
    /// returning, so no submitted work is ever still reading them.</summary>
    private void ApplyFiltering(TextureFiltering filtering)
    {
        if (filtering == _filtering) return;
        // Rewrites every material descriptor set, which an in-flight frame is bound to.
        WaitForPendingFrames();
        _filtering = filtering;
        _sampler = filtering == TextureFiltering.Point ? _samplerPoint : _samplerLinear;
        WriteMaterialSets();
    }

    /// <summary>Points every material's five samplers at <see cref="_sampler"/> and its stored views.</summary>
    private void WriteMaterialSets()
    {
        if (_matSets == null || _matViews == null) return;

        VkDescriptorImageInfo* imgs = stackalloc VkDescriptorImageInfo[TexPerMaterial];
        VkWriteDescriptorSet* w = stackalloc VkWriteDescriptorSet[TexPerMaterial];
        for (int i = 0; i < _matSets.Length; i++)
        {
            for (uint bnd = 0; bnd < TexPerMaterial; bnd++)
            {
                imgs[bnd] = new VkDescriptorImageInfo { sampler = _sampler, imageView = _matViews[i * TexPerMaterial + bnd], imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
                w[bnd] = new VkWriteDescriptorSet { dstSet = _matSets[i], dstBinding = bnd, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &imgs[bnd] };
            }
            _api.vkUpdateDescriptorSets(TexPerMaterial, w, 0, null);
        }
    }

    private void CreateDescriptors(List<VkMaterialDesc> materials)
    {
        VkDescriptorSetLayoutBinding* set0 = stackalloc VkDescriptorSetLayoutBinding[6];
        set0[0] = new VkDescriptorSetLayoutBinding { binding = 0, descriptorType = VkDescriptorType.UniformBuffer, descriptorCount = 1, stageFlags = VkShaderStageFlags.Vertex };
        set0[1] = new VkDescriptorSetLayoutBinding { binding = 1, descriptorType = VkDescriptorType.StorageBuffer, descriptorCount = 1, stageFlags = VkShaderStageFlags.Vertex };
        set0[2] = new VkDescriptorSetLayoutBinding { binding = 2, descriptorType = VkDescriptorType.UniformBuffer, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        set0[3] = new VkDescriptorSetLayoutBinding { binding = 3, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        set0[4] = new VkDescriptorSetLayoutBinding { binding = 4, descriptorType = VkDescriptorType.StorageBuffer, descriptorCount = 1, stageFlags = VkShaderStageFlags.Vertex };
        set0[5] = new VkDescriptorSetLayoutBinding { binding = 5, descriptorType = VkDescriptorType.StorageBuffer, descriptorCount = 1, stageFlags = VkShaderStageFlags.Vertex };
        var set0Info = new VkDescriptorSetLayoutCreateInfo { bindingCount = 6, pBindings = set0 };
        VkDescriptorSetLayout dl0; Check(_api.vkCreateDescriptorSetLayout(&set0Info, &dl0), "vkCreateDescriptorSetLayout(0)"); _descLayout = dl0;

        VkDescriptorSetLayoutBinding* set1 = stackalloc VkDescriptorSetLayoutBinding[TexPerMaterial];
        for (uint i = 0; i < TexPerMaterial; i++)
            set1[i] = new VkDescriptorSetLayoutBinding { binding = i, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        var set1Info = new VkDescriptorSetLayoutCreateInfo { bindingCount = TexPerMaterial, pBindings = set1 };
        VkDescriptorSetLayout dl1; Check(_api.vkCreateDescriptorSetLayout(&set1Info, &dl1), "vkCreateDescriptorSetLayout(1)"); _matSetLayout = dl1;

        VkDescriptorSetLayoutBinding* setR = stackalloc VkDescriptorSetLayoutBinding[2];
        setR[0] = new VkDescriptorSetLayoutBinding { binding = 0, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        setR[1] = new VkDescriptorSetLayoutBinding { binding = 1, descriptorType = VkDescriptorType.CombinedImageSampler, descriptorCount = 1, stageFlags = VkShaderStageFlags.Fragment };
        var setRInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 2, pBindings = setR };
        VkDescriptorSetLayout dlR; Check(_api.vkCreateDescriptorSetLayout(&setRInfo, &dlR), "vkCreateDescriptorSetLayout(resolve)"); _resolveSetLayout = dlR;

        int nMat = materials.Count;
        VkDescriptorPoolSize* sizes = stackalloc VkDescriptorPoolSize[3];
        // Set 0 exists once per in-flight frame, so its descriptors are counted Frames times.
        sizes[0] = new VkDescriptorPoolSize { type = VkDescriptorType.UniformBuffer, descriptorCount = 2 * Frames };
        sizes[1] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageBuffer, descriptorCount = 3 * Frames }; // Transforms + BonePalette + BoneBase
        sizes[2] = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = (uint)(TexPerMaterial * nMat + Frames + 2) }; // +cube per set0 +resolve(accum,reveal)
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = (uint)(Frames + nMat + 1), poolSizeCount = 3, pPoolSizes = sizes };
        VkDescriptorPool dp; Check(_api.vkCreateDescriptorPool(&poolInfo, &dp), "vkCreateDescriptorPool"); _descPool = dp;

        VkImage cubeImage = _ctx.BackendInfo.GetVkImage(_envCube);
        var cubeViewInfo = new VkImageViewCreateInfo { image = cubeImage, viewType = VkImageViewType.ImageCube, format = ColorFormat, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Color, baseMipLevel = 0, levelCount = Math.Max(1u, _envCube.MipLevels), baseArrayLayer = 0, layerCount = 6 } };
        VkImageView cubeView; Check(_api.vkCreateImageView(&cubeViewInfo, &cubeView), "vkCreateImageView(cube)"); _envCubeView = cubeView;

        // One set 0 per in-flight frame. Only the two uniform buffers differ between them; the
        // transform SSBO and the cubemap are shared (see UpdateEntityTransforms for why the SSBO can be).
        var ssboInfo = new VkDescriptorBufferInfo { buffer = _transformBuffer, offset = 0, range = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE };
        var boneInfo = new VkDescriptorBufferInfo { buffer = _bonePaletteBuffer, offset = 0, range = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE };
        var boneBaseInfo = new VkDescriptorBufferInfo { buffer = _boneBaseBuffer, offset = 0, range = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE };
        var cubeInfo = new VkDescriptorImageInfo { sampler = _sampler, imageView = _envCubeView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        // stackalloc lives until the method returns, not the iteration, so these sit outside the loop.
        VkWriteDescriptorSet* w0 = stackalloc VkWriteDescriptorSet[6];
        for (int f = 0; f < Frames; f++)
        {
            VkDescriptorSetLayout l0 = _descLayout;
            var alloc0 = new VkDescriptorSetAllocateInfo { descriptorPool = _descPool, descriptorSetCount = 1, pSetLayouts = &l0 };
            VkDescriptorSet ds0; Check(_api.vkAllocateDescriptorSets(&alloc0, &ds0), "vkAllocateDescriptorSets(0)"); _descSets[f] = ds0;
            var uboInfo = new VkDescriptorBufferInfo { buffer = _uniformBuffers[f], offset = 0, range = UboSize };
            var lightInfo = new VkDescriptorBufferInfo { buffer = _lightBuffers[f], offset = 0, range = (ulong)sizeof(LightData) };
            w0[0] = new VkWriteDescriptorSet { dstSet = ds0, dstBinding = 0, descriptorCount = 1, descriptorType = VkDescriptorType.UniformBuffer, pBufferInfo = &uboInfo };
            w0[1] = new VkWriteDescriptorSet { dstSet = ds0, dstBinding = 1, descriptorCount = 1, descriptorType = VkDescriptorType.StorageBuffer, pBufferInfo = &ssboInfo };
            w0[2] = new VkWriteDescriptorSet { dstSet = ds0, dstBinding = 2, descriptorCount = 1, descriptorType = VkDescriptorType.UniformBuffer, pBufferInfo = &lightInfo };
            w0[3] = new VkWriteDescriptorSet { dstSet = ds0, dstBinding = 3, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &cubeInfo };
            w0[4] = new VkWriteDescriptorSet { dstSet = ds0, dstBinding = 4, descriptorCount = 1, descriptorType = VkDescriptorType.StorageBuffer, pBufferInfo = &boneInfo };
            w0[5] = new VkWriteDescriptorSet { dstSet = ds0, dstBinding = 5, descriptorCount = 1, descriptorType = VkDescriptorType.StorageBuffer, pBufferInfo = &boneBaseInfo };
            _api.vkUpdateDescriptorSets(6, w0, 0, null);
        }
        _descSet = _descSets[0];

        VkDescriptorSetLayout lR = _resolveSetLayout;
        var allocR = new VkDescriptorSetAllocateInfo { descriptorPool = _descPool, descriptorSetCount = 1, pSetLayouts = &lR };
        VkDescriptorSet dsR; Check(_api.vkAllocateDescriptorSets(&allocR, &dsR), "vkAllocateDescriptorSets(resolve)"); _resolveSet = dsR;
        // Written in CreateTargets once accum/reveal exist (and re-written on resize).

        Texture? fallback = null;
        foreach (var m in materials) { fallback = m.Albedo ?? m.Normal ?? m.Props ?? m.LightColour ?? m.LightDir; if (fallback != null) break; }
        if (fallback == null) throw new InvalidOperationException("[VkRenderer] Scene has no material textures.");

        var viewOf = new Dictionary<Texture, VkImageView>(ReferenceEqualityComparer.Instance);
        VkImageView ViewFor(Texture? t)
        {
            t ??= fallback;
            if (viewOf.TryGetValue(t, out var existing)) return existing;
            VkImage image = _ctx.BackendInfo.GetVkImage(t);
            uint mips = Math.Max(1u, t.MipLevels);
            var vi = new VkImageViewCreateInfo { image = image, viewType = VkImageViewType.Image2D, format = ColorFormat, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Color, baseMipLevel = 0, levelCount = mips, baseArrayLayer = 0, layerCount = 1 } };
            VkImageView view; Check(_api.vkCreateImageView(&vi, &view), "vkCreateImageView(tex)");
            viewOf[t] = view;
            return view;
        }

        _matSets = new VkDescriptorSet[nMat];
        // Kept so the sets can be rewritten when the filtering setting changes without re-resolving
        // every texture back to its view.
        _matViews = new VkImageView[nMat * TexPerMaterial];
        // Hoisted out of the loop and reused by every iteration (stackalloc lives until the method
        // returns, so one inside the loop would blow the stack on a level with many materials).
        VkImageView* v = stackalloc VkImageView[TexPerMaterial];
        VkDescriptorImageInfo* imgs = stackalloc VkDescriptorImageInfo[TexPerMaterial];
        VkWriteDescriptorSet* w = stackalloc VkWriteDescriptorSet[TexPerMaterial];
        for (int i = 0; i < nMat; i++)
        {
            var m = materials[i];
            v[0] = ViewFor(m.Albedo); v[1] = ViewFor(m.Normal); v[2] = ViewFor(m.Props); v[3] = ViewFor(m.LightColour); v[4] = ViewFor(m.LightDir);
            for (int bnd = 0; bnd < TexPerMaterial; bnd++) _matViews[i * TexPerMaterial + bnd] = v[bnd];
            VkDescriptorSetLayout l1 = _matSetLayout;
            var alloc1 = new VkDescriptorSetAllocateInfo { descriptorPool = _descPool, descriptorSetCount = 1, pSetLayouts = &l1 };
            VkDescriptorSet ds; Check(_api.vkAllocateDescriptorSets(&alloc1, &ds), "vkAllocateDescriptorSets(mat)"); _matSets[i] = ds;
            for (uint bnd = 0; bnd < TexPerMaterial; bnd++)
            {
                imgs[bnd] = new VkDescriptorImageInfo { sampler = _sampler, imageView = v[bnd], imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
                w[bnd] = new VkWriteDescriptorSet { dstSet = ds, dstBinding = bnd, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &imgs[bnd] };
            }
            _api.vkUpdateDescriptorSets(TexPerMaterial, w, 0, null);
        }
        _texViews = [.. viewOf.Values];
    }

    private VkRenderPass MakeColorDepthPass(bool depthWrites, VkImageLayout colorFinal)
    {
        VkAttachmentDescription* a = stackalloc VkAttachmentDescription[2];
        a[0] = new VkAttachmentDescription { format = ColorFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = colorFinal };
        a[1] = new VkAttachmentDescription { format = DepthFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = depthWrites ? VkAttachmentStoreOp.Store : VkAttachmentStoreOp.DontCare, stencilLoadOp = VkAttachmentLoadOp.Clear, stencilStoreOp = VkAttachmentStoreOp.Store, initialLayout = VkImageLayout.Undefined, finalLayout = VkImageLayout.DepthStencilAttachmentOptimal };
        var colorRef = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        var depthRef = new VkAttachmentReference { attachment = 1, layout = VkImageLayout.DepthStencilAttachmentOptimal };
        var subpass = new VkSubpassDescription { pipelineBindPoint = VkPipelineBindPoint.Graphics, colorAttachmentCount = 1, pColorAttachments = &colorRef, pDepthStencilAttachment = &depthRef };
        VkSubpassDependency* deps = stackalloc VkSubpassDependency[2];
        deps[0] = new VkSubpassDependency { srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, dstSubpass = 0, srcStageMask = VkPipelineStageFlags.FragmentShader, srcAccessMask = VkAccessFlags.ShaderRead, dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.EarlyFragmentTests, dstAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentWrite };
        deps[1] = new VkSubpassDependency { srcSubpass = 0, dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.LateFragmentTests, srcAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.EarlyFragmentTests, dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.DepthStencilAttachmentRead };
        var info = new VkRenderPassCreateInfo { attachmentCount = 2, pAttachments = a, subpassCount = 1, pSubpasses = &subpass, dependencyCount = 2, pDependencies = deps };
        VkRenderPass rp; Check(_api.vkCreateRenderPass(&info, &rp), "vkCreateRenderPass"); return rp;
    }

    private void CreateRenderPasses()
    {
        // Opaque: clears + writes colour and depth; colour left as attachment (resolve writes it later).
        _rpOpaque = MakeColorDepthPass(depthWrites: true, colorFinal: VkImageLayout.ColorAttachmentOptimal);

        // Accumulate: two colour targets (accum, reveal) + read-only depth from the opaque pass.
        VkAttachmentDescription* a = stackalloc VkAttachmentDescription[3];
        a[0] = new VkAttachmentDescription { format = AccumFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = VkImageLayout.ShaderReadOnlyOptimal };
        a[1] = new VkAttachmentDescription { format = RevealFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = VkImageLayout.ShaderReadOnlyOptimal };
        a[2] = new VkAttachmentDescription { format = DepthFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Load, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.Load, stencilStoreOp = VkAttachmentStoreOp.Store, initialLayout = VkImageLayout.DepthStencilAttachmentOptimal, finalLayout = VkImageLayout.DepthStencilAttachmentOptimal };
        VkAttachmentReference* colorRefs = stackalloc VkAttachmentReference[2];
        colorRefs[0] = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        colorRefs[1] = new VkAttachmentReference { attachment = 1, layout = VkImageLayout.ColorAttachmentOptimal };
        var depthRefRO = new VkAttachmentReference { attachment = 2, layout = VkImageLayout.DepthStencilReadOnlyOptimal };
        var subA = new VkSubpassDescription { pipelineBindPoint = VkPipelineBindPoint.Graphics, colorAttachmentCount = 2, pColorAttachments = colorRefs, pDepthStencilAttachment = &depthRefRO };
        VkSubpassDependency* depsA = stackalloc VkSubpassDependency[2];
        depsA[0] = new VkSubpassDependency { srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, dstSubpass = 0, srcStageMask = VkPipelineStageFlags.LateFragmentTests, srcAccessMask = VkAccessFlags.DepthStencilAttachmentWrite, dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.EarlyFragmentTests, dstAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentRead };
        depsA[1] = new VkSubpassDependency { srcSubpass = 0, dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput, srcAccessMask = VkAccessFlags.ColorAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader, dstAccessMask = VkAccessFlags.ShaderRead };
        var infoA = new VkRenderPassCreateInfo { attachmentCount = 3, pAttachments = a, subpassCount = 1, pSubpasses = &subA, dependencyCount = 2, pDependencies = depsA };
        VkRenderPass rpA; Check(_api.vkCreateRenderPass(&infoA, &rpA), "vkCreateRenderPass(accum)"); _rpAccum = rpA;

        // Resolve: load the opaque colour, blend the resolved translucent over it, leave it ShaderRead.
        VkAttachmentDescription* rAtt = stackalloc VkAttachmentDescription[2];
        rAtt[0] = new VkAttachmentDescription { format = ColorFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Load, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.ColorAttachmentOptimal, finalLayout = VkImageLayout.ShaderReadOnlyOptimal };
        rAtt[1] = new VkAttachmentDescription { format = DepthFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Load, storeOp = VkAttachmentStoreOp.DontCare, stencilLoadOp = VkAttachmentLoadOp.Load, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.DepthStencilAttachmentOptimal, finalLayout = VkImageLayout.DepthStencilAttachmentOptimal };
        var cRef = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        var rDepthRef = new VkAttachmentReference { attachment = 1, layout = VkImageLayout.DepthStencilAttachmentOptimal };
        var subR = new VkSubpassDescription { pipelineBindPoint = VkPipelineBindPoint.Graphics, colorAttachmentCount = 1, pColorAttachments = &cRef, pDepthStencilAttachment = &rDepthRef };
        VkSubpassDependency* depsR = stackalloc VkSubpassDependency[2];
        depsR[0] = new VkSubpassDependency { srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, dstSubpass = 0, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.LateFragmentTests, srcAccessMask = VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.EarlyFragmentTests, dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentRead | VkAccessFlags.DepthStencilAttachmentWrite };
        depsR[1] = new VkSubpassDependency { srcSubpass = 0, dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput, srcAccessMask = VkAccessFlags.ColorAttachmentWrite, dstStageMask = VkPipelineStageFlags.FragmentShader, dstAccessMask = VkAccessFlags.ShaderRead };
        var infoR = new VkRenderPassCreateInfo { attachmentCount = 2, pAttachments = rAtt, subpassCount = 1, pSubpasses = &subR, dependencyCount = 2, pDependencies = depsR };
        VkRenderPass rpR; Check(_api.vkCreateRenderPass(&infoR, &rpR), "vkCreateRenderPass(resolve)"); _rpResolve = rpR;

        // Pick: a tiny colour+depth target, cleared to NoHit, left readable so it can be copied out.
        VkAttachmentDescription* pk = stackalloc VkAttachmentDescription[2];
        pk[0] = new VkAttachmentDescription { format = ColorFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = VkAttachmentStoreOp.Store, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = VkImageLayout.TransferSrcOptimal };
        pk[1] = new VkAttachmentDescription { format = DepthFormat, samples = VkSampleCountFlags.Count1, loadOp = VkAttachmentLoadOp.Clear, storeOp = VkAttachmentStoreOp.DontCare, stencilLoadOp = VkAttachmentLoadOp.DontCare, stencilStoreOp = VkAttachmentStoreOp.DontCare, initialLayout = VkImageLayout.Undefined, finalLayout = VkImageLayout.DepthStencilAttachmentOptimal };
        var pkColor = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        var pkDepth = new VkAttachmentReference { attachment = 1, layout = VkImageLayout.DepthStencilAttachmentOptimal };
        var subP = new VkSubpassDescription { pipelineBindPoint = VkPipelineBindPoint.Graphics, colorAttachmentCount = 1, pColorAttachments = &pkColor, pDepthStencilAttachment = &pkDepth };
        var depP = new VkSubpassDependency { srcSubpass = 0, dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL, srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput, srcAccessMask = VkAccessFlags.ColorAttachmentWrite, dstStageMask = VkPipelineStageFlags.Transfer, dstAccessMask = VkAccessFlags.TransferRead };
        var infoP = new VkRenderPassCreateInfo { attachmentCount = 2, pAttachments = pk, subpassCount = 1, pSubpasses = &subP, dependencyCount = 1, pDependencies = &depP };
        VkRenderPass rpP; Check(_api.vkCreateRenderPass(&infoP, &rpP), "vkCreateRenderPass(pick)"); _rpPick = rpP;
    }

    // Compiled SPIR-V, cached across renderer instances (the asset preview builds a whole renderer
    // each time the selection changes). Keyed by source + stage.
    private static readonly Dictionary<(string, ShaderStages), byte[]> SpirvCache = new();

    /// <summary>Every shader this renderer compiles, as (source, stage). The sources are compile-time
    /// constants, so the SPIR-V for all of them is known before any level exists.</summary>
    private static (string Source, ShaderStages Stage)[] AllShaders =>
    [
        (VertexGlsl, ShaderStages.Vertex),
        (FragmentOpaqueGlsl, ShaderStages.Fragment),
        (FragmentAccumGlsl, ShaderStages.Fragment),
        (FragmentAdditiveGlsl, ShaderStages.Fragment),
        (ResolveVertexGlsl, ShaderStages.Vertex),
        (ResolveFragmentGlsl, ShaderStages.Fragment),
        (DebugLineVertexGlsl, ShaderStages.Vertex),
        (DebugLineFragmentGlsl, ShaderStages.Fragment),
        (PickVertexGlsl, ShaderStages.Vertex),
        (PickVolumeVertexGlsl, ShaderStages.Vertex),
        (PickFragmentGlsl, ShaderStages.Fragment),
        (BillboardVertexGlsl, ShaderStages.Vertex),
        (BillboardFragmentGlsl, ShaderStages.Fragment),
        (OutlineVertexGlsl, ShaderStages.Vertex),
        (OutlineFragmentGlsl, ShaderStages.Fragment),
        (VolumeVertexGlsl, ShaderStages.Vertex),
        (VolumeFragmentGlsl, ShaderStages.Fragment),
    ];

    /// <summary>Compiles every shader into the shared SPIR-V cache. Touches nothing on the graphics
    /// device, so it can run on any thread at startup instead of blocking a level load. Safe to call
    /// more than once or race with a real load.</summary>
    public static void WarmUpShaderCache()
    {
        foreach (var (source, stage) in AllShaders)
        {
            lock (SpirvCache)
            {
                if (SpirvCache.ContainsKey((source, stage))) continue;
            }
            // Compiled OUTSIDE the lock: glslang is the slow part, and holding the lock across it would
            // serialise a concurrent renderer build behind the whole warm-up instead of just the miss.
            byte[] spirv = SpirvCompilation.CompileGlslToSpirv(source, "vk", stage, new GlslCompileOptions()).SpirvBytes;
            lock (SpirvCache) SpirvCache.TryAdd((source, stage), spirv);
        }
    }

    // Driver-side pipeline cache, shared across renderer instances so the driver doesn't recompile
    // every pipeline's SPIR-V on each rebuild. Tied to the device it was made on; never destroyed.
    private static VkPipelineCache _sharedPipelineCache;
    private static nint _sharedPipelineCacheDevice;

    private VkPipelineCache GetPipelineCache()
    {
        nint device = _ctx.BackendInfo.Device;
        if (_sharedPipelineCacheDevice == device && _sharedPipelineCache.Handle != 0) return _sharedPipelineCache;
        var info = new VkPipelineCacheCreateInfo();
        VkPipelineCache cache;
        if (_api.vkCreatePipelineCache(&info, &cache) != VkResult.Success) return default;
        _sharedPipelineCache = cache;
        _sharedPipelineCacheDevice = device;
        return cache;
    }

    private VkShaderModule Module(string glsl, ShaderStages stage)
    {
        byte[] spirv;
        lock (SpirvCache)
        {
            if (!SpirvCache.TryGetValue((glsl, stage), out spirv!))
            {
                spirv = SpirvCompilation.CompileGlslToSpirv(glsl, "vk", stage, new GlslCompileOptions()).SpirvBytes;
                SpirvCache[(glsl, stage)] = spirv;
            }
        }
        fixed (byte* p = spirv)
        {
            var info = new VkShaderModuleCreateInfo { codeSize = (nuint)spirv.Length, pCode = (uint*)p };
            VkShaderModule m; Check(_api.vkCreateShaderModule(&info, &m), "vkCreateShaderModule");
            return m;
        }
    }

    private void CreatePipelines()
    {
        _vs = Module(VertexGlsl, ShaderStages.Vertex);
        _fsOpaque = Module(FragmentOpaqueGlsl, ShaderStages.Fragment);
        _fsAccum = Module(FragmentAccumGlsl, ShaderStages.Fragment);
        _fsAdditive = Module(FragmentAdditiveGlsl, ShaderStages.Fragment);
        _resolveVs = Module(ResolveVertexGlsl, ShaderStages.Vertex);
        _resolveFs = Module(ResolveFragmentGlsl, ShaderStages.Fragment);

        // Lit pipeline layout: set0 (scene) + set1 (material) + 32-byte fragment push constant.
        VkDescriptorSetLayout* litSets = stackalloc VkDescriptorSetLayout[2] { _descLayout, _matSetLayout };
        var pushRange = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Fragment, offset = 0, size = 32 };
        var litLayoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 2, pSetLayouts = litSets, pushConstantRangeCount = 1, pPushConstantRanges = &pushRange };
        VkPipelineLayout litLayout; Check(_api.vkCreatePipelineLayout(&litLayoutInfo, &litLayout), "vkCreatePipelineLayout(lit)"); _layout = litLayout;

        VkDescriptorSetLayout rl = _resolveSetLayout;
        var resolveLayoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &rl };
        VkPipelineLayout resolveLayout; Check(_api.vkCreatePipelineLayout(&resolveLayoutInfo, &resolveLayout), "vkCreatePipelineLayout(resolve)"); _resolveLayout = resolveLayout;

        VkPipelineCache pipelineCache = GetPipelineCache();
        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        var mask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A;
        VkDynamicState* dyn = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        var dynState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dyn };
        var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
        var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };

        // --- Lit vertex input (opaque + accumulate).
        var vbinding = new VkVertexInputBindingDescription { binding = 0, stride = VertexStride, inputRate = VkVertexInputRate.Vertex };
        VkVertexInputAttributeDescription* attrs = stackalloc VkVertexInputAttributeDescription[8];
        attrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
        attrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32Sfloat, offset = 12 };
        attrs[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 20 };
        attrs[3] = new VkVertexInputAttributeDescription { location = 3, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 32 };
        attrs[4] = new VkVertexInputAttributeDescription { location = 4, binding = 0, format = VkFormat.R32G32Sfloat, offset = 48 };
        attrs[5] = new VkVertexInputAttributeDescription { location = 5, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 56 };
        // Joint indices, stored as whole-number floats (cast with int() in the shader) rather than a
        // uint format, so Interleave can write them alongside every other field with no format split.
        attrs[6] = new VkVertexInputAttributeDescription { location = 6, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 72 };
        attrs[7] = new VkVertexInputAttributeDescription { location = 7, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 88 };
        var litVertexInput = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbinding, vertexAttributeDescriptionCount = 8, pVertexAttributeDescriptions = attrs };
        var rasterCullNone = new VkPipelineRasterizationStateCreateInfo { polygonMode = VkPolygonMode.Fill, cullMode = VkCullModeFlags.None, frontFace = VkFrontFace.CounterClockwise, lineWidth = 1f };

        // Polygon offset for every non-opaque draw, matching the game's decal/overlay offset (see
        // AssetManager.OverlayDepthBias). Without it, Overlay-mode surfaces (decals, posters, grime)
        // z-fight with the wall they're painted on. Applied to the WBOIT accumulate, additive and
        // Soft-Edge depth-prepass pipelines; Opaque and Cutout draw unbiased.
        var rasterBiased = new VkPipelineRasterizationStateCreateInfo { polygonMode = VkPolygonMode.Fill, cullMode = VkCullModeFlags.None, frontFace = VkFrontFace.CounterClockwise, lineWidth = 1f, depthBiasEnable = true, depthBiasConstantFactor = NonOpaqueDepthBias, depthBiasSlopeFactor = NonOpaqueSlopeScaledDepthBias, depthBiasClamp = 0f };

        // Opaque: depth write, no blend, into _rpOpaque.
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _vs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _fsOpaque, pName = entry };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.LessOrEqual };
            var blendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _layout, renderPass = _rpOpaque, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(opaque)"); _pipelineOpaque = p;
        }

        // Accumulate: depth test only, two attachments - accum additive, reveal multiplicative - into _rpAccum.
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _vs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _fsAccum, pName = entry };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.LessOrEqual };
            VkPipelineColorBlendAttachmentState* atts = stackalloc VkPipelineColorBlendAttachmentState[2];
            atts[0] = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.One, dstColorBlendFactor = VkBlendFactor.One, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.One, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add, colorWriteMask = mask };
            atts[1] = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.Zero, dstColorBlendFactor = VkBlendFactor.OneMinusSrcColor, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.OneMinusSrcColor, alphaBlendOp = VkBlendOp.Add, colorWriteMask = VkColorComponentFlags.R };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 2, pAttachments = atts };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterBiased, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _layout, renderPass = _rpAccum, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(accum)"); _pipelineAccum = p;
        }

        // Resolve: fullscreen triangle, no vertex input, blend over the opaque colour, into _rpResolve.
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _resolveVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _resolveFs, pName = entry };
            var emptyVertexInput = new VkPipelineVertexInputStateCreateInfo();
            var rasterCullNoneR = new VkPipelineRasterizationStateCreateInfo { polygonMode = VkPolygonMode.Fill, cullMode = VkCullModeFlags.None, frontFace = VkFrontFace.CounterClockwise, lineWidth = 1f };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = false, depthWriteEnable = false, depthCompareOp = VkCompareOp.Always };
            // final = avg*(1-reveal) + dst*reveal; keep dst alpha (=1). src.a = reveal.
            var blendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha, dstColorBlendFactor = VkBlendFactor.SrcAlpha, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add, colorWriteMask = mask };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &emptyVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNoneR, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _resolveLayout, renderPass = _rpResolve, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(resolve)"); _pipelineResolve = p;
        }

        // Soft-Edge depth prepass (mode 5, pass 1): alpha-tested depth-only draw into the opaque pass,
        // colour writes off, depth write on, so soft-edge geometry occludes correctly before its
        // blended colour pass.
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _vs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _fsOpaque, pName = entry };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.LessOrEqual };
            var noColor = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = 0 };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &noColor };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterBiased, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _layout, renderPass = _rpOpaque, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(softEdgeDepth)"); _pipelineSoftEdgeDepth = p;
        }

        // Additive (mode 2): SrcAlpha/One - the surface ADDS light to what's behind it, so it must not
        // go through the over-blend WBOIT path. Additive blending is commutative, so it needs no sorting;
        // drawn depth-tested (no write) straight into the opaque colour after everything else.
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _vs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _fsAdditive, pName = entry };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.LessOrEqual };
            var add = new VkPipelineColorBlendAttachmentState
            {
                blendEnable = true,
                srcColorBlendFactor = VkBlendFactor.SrcAlpha, dstColorBlendFactor = VkBlendFactor.One, colorBlendOp = VkBlendOp.Add,
                srcAlphaBlendFactor = VkBlendFactor.Zero, dstAlphaBlendFactor = VkBlendFactor.One, alphaBlendOp = VkBlendOp.Add,
                colorWriteMask = mask,
            };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &add };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterBiased, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _layout, renderPass = _rpOpaque, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(additive)"); _pipelineAdditive = p;
        }

        // Volumes: line-list wireframe cube, depth-tested + writing (occluded by opaque scene), drawn in
        // the opaque pass. Own layout = set0 (for uMvp) + a Vertex|Fragment push constant (mat4 + colour).
        _volumeVs = Module(VolumeVertexGlsl, ShaderStages.Vertex);
        _volumeFs = Module(VolumeFragmentGlsl, ShaderStages.Fragment);
        VkDescriptorSetLayout vl = _descLayout;
        var volPush = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, offset = 0, size = 80 };
        var volLayoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &vl, pushConstantRangeCount = 1, pPushConstantRanges = &volPush };
        VkPipelineLayout volLayout; Check(_api.vkCreatePipelineLayout(&volLayoutInfo, &volLayout), "vkCreatePipelineLayout(volume)"); _volumeLayout = volLayout;
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _volumeVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _volumeFs, pName = entry };
            var vbind = new VkVertexInputBindingDescription { binding = 0, stride = 3 * sizeof(float), inputRate = VkVertexInputRate.Vertex };
            var vattr = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
            var vin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbind, vertexAttributeDescriptionCount = 1, pVertexAttributeDescriptions = &vattr };
            var ia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.LessOrEqual };
            var blendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &vin, pInputAssemblyState = &ia, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _volumeLayout, renderPass = _rpOpaque, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(volume)"); _volumePipeline = p;
        }

        // Foliage billboards: the lit vertex layout and the lit pipeline layout (so set0/set1 and the
        // push constant are shared), but the billboard vertex shader and a plain alpha-tested textured
        // fragment shader. Drawn in the opaque pass, depth-writing - see the alpha-reference note above.
        _billboardVs = Module(BillboardVertexGlsl, ShaderStages.Vertex);
        _billboardFs = Module(BillboardFragmentGlsl, ShaderStages.Fragment);
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _billboardVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _billboardFs, pName = entry };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.LessOrEqual };
            var blendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttach };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _layout, renderPass = _rpOpaque, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(billboard)"); _pipelineBillboard = p;
        }

        // Shared by the debug-line and selection-outline pipelines: set0 (camera UBO + transform SSBO)
        // plus a Vertex|Fragment push constant. Must be created before any pipeline that references it.
        VkDescriptorSetLayout ol = _descLayout;
        var outlinePush = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, offset = 0, size = 32 };
        var outlineLayoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &ol, pushConstantRangeCount = 1, pPushConstantRanges = &outlinePush };
        VkPipelineLayout outLayout; Check(_api.vkCreatePipelineLayout(&outlineLayoutInfo, &outLayout), "vkCreatePipelineLayout(outline)"); _outlineLayout = outLayout;

        // Debug lines: drawn in the resolve pass (so they sit on the finished image) with the depth
        // test off and straight alpha blending.
        _debugLineVs = Module(DebugLineVertexGlsl, ShaderStages.Vertex);
        _debugLineFs = Module(DebugLineFragmentGlsl, ShaderStages.Fragment);
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _debugLineVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _debugLineFs, pName = entry };
            var lbind = new VkVertexInputBindingDescription { binding = 0, stride = DebugLineFloats * sizeof(float), inputRate = VkVertexInputRate.Vertex };
            VkVertexInputAttributeDescription* lattrs = stackalloc VkVertexInputAttributeDescription[2];
            lattrs[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
            lattrs[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 12 };
            var lin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &lbind, vertexAttributeDescriptionCount = 2, pVertexAttributeDescriptions = lattrs };
            var lia = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.LineList };
            var ldepth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = false, depthWriteEnable = false, depthCompareOp = VkCompareOp.Always };
            var lblendAttach = new VkPipelineColorBlendAttachmentState { blendEnable = true, srcColorBlendFactor = VkBlendFactor.SrcAlpha, dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha, colorBlendOp = VkBlendOp.Add, srcAlphaBlendFactor = VkBlendFactor.One, dstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha, alphaBlendOp = VkBlendOp.Add, colorWriteMask = mask };
            var lblend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &lblendAttach };
            var linfo = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &lin, pInputAssemblyState = &lia, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &ldepth, pColorBlendState = &lblend, pDynamicState = &dynState, layout = _outlineLayout, renderPass = _rpResolve, subpass = 0 };
            VkPipeline lp; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &linfo, &lp), "vkCreateGraphicsPipelines(debugLines)"); _pipelineDebugLines = lp;
        }

        // Selection outline: two pipelines over the same shader pair, drawn in the RESOLVE pass so the
        // rim sits on top of the fully composited image (opaque + resolved translucent) rather than
        // underneath the glass. Own layout = set0 (camera UBO + transform SSBO) + a Vertex|Fragment
        // push constant (colour + inflate amount).
        _outlineVs = Module(OutlineVertexGlsl, ShaderStages.Vertex);
        _outlineFs = Module(OutlineFragmentGlsl, ShaderStages.Fragment);
        {
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _outlineVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _outlineFs, pName = entry };

            // Pass 1 (mask): depth-tested against the scene, no depth write, NO colour write, stamp 1.
            var stamp = new VkStencilOpState { failOp = VkStencilOp.Keep, passOp = VkStencilOp.Replace, depthFailOp = VkStencilOp.Keep, compareOp = VkCompareOp.Always, compareMask = 0xFF, writeMask = 0xFF, reference = 1 };
            var maskDepth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.LessOrEqual, stencilTestEnable = true, front = stamp, back = stamp };
            var noColor = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = 0 };
            var maskBlend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &noColor };
            var maskInfo = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &maskDepth, pColorBlendState = &maskBlend, pDynamicState = &dynState, layout = _outlineLayout, renderPass = _rpResolve, subpass = 0 };
            VkPipeline pm; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &maskInfo, &pm), "vkCreateGraphicsPipelines(outlineMask)"); _pipelineOutlineMask = pm;

            // Pass 2 (rim): same depth test, stencil NotEqual 1 so only what pass 1 did NOT cover draws.
            var rim = new VkStencilOpState { failOp = VkStencilOp.Keep, passOp = VkStencilOp.Keep, depthFailOp = VkStencilOp.Keep, compareOp = VkCompareOp.NotEqual, compareMask = 0xFF, writeMask = 0, reference = 1 };
            var rimDepth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = false, depthCompareOp = VkCompareOp.LessOrEqual, stencilTestEnable = true, front = rim, back = rim };
            var rimAttach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
            var rimBlend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &rimAttach };
            var rimInfo = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &rimDepth, pColorBlendState = &rimBlend, pDynamicState = &dynState, layout = _outlineLayout, renderPass = _rpResolve, subpass = 0 };
            VkPipeline pr; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &rimInfo, &pr), "vkCreateGraphicsPipelines(outlineRim)"); _pipelineOutlineRim = pr;
        }

        // Picking: scene geometry (transform SSBO, id per draw) and volume edges (world matrix per
        // draw) into the same tiny id target. One layout for both - a mat4 + a uvec4 push constant,
        // 80 bytes, well inside the 128-byte guaranteed minimum.
        _pickVs = Module(PickVertexGlsl, ShaderStages.Vertex);
        _pickVolumeVs = Module(PickVolumeVertexGlsl, ShaderStages.Vertex);
        _pickBillboardVs = Module(PickBillboardVertexGlsl, ShaderStages.Vertex);
        _pickFs = Module(PickFragmentGlsl, ShaderStages.Fragment);
        VkDescriptorSetLayout pl = _descLayout;
        var pickPush = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, offset = 0, size = 80 };
        var pickLayoutInfo = new VkPipelineLayoutCreateInfo { setLayoutCount = 1, pSetLayouts = &pl, pushConstantRangeCount = 1, pPushConstantRanges = &pickPush };
        VkPipelineLayout pkLayout; Check(_api.vkCreatePipelineLayout(&pickLayoutInfo, &pkLayout), "vkCreatePipelineLayout(pick)"); _pickLayout = pkLayout;
        {
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.LessOrEqual };
            var attach = new VkPipelineColorBlendAttachmentState { blendEnable = false, colorWriteMask = mask };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &attach };

            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _pickVs, pName = entry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _pickFs, pName = entry };
            var info = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = stages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _pickLayout, renderPass = _rpPick, subpass = 0 };
            VkPipeline p; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &info, &p), "vkCreateGraphicsPipelines(pick)"); _pipelinePick = p;

            VkPipelineShaderStageCreateInfo* vstages = stackalloc VkPipelineShaderStageCreateInfo[2];
            vstages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _pickVolumeVs, pName = entry };
            vstages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _pickFs, pName = entry };
            var vbind = new VkVertexInputBindingDescription { binding = 0, stride = 3 * sizeof(float), inputRate = VkVertexInputRate.Vertex };
            var vattr = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
            var vin = new VkPipelineVertexInputStateCreateInfo { vertexBindingDescriptionCount = 1, pVertexBindingDescriptions = &vbind, vertexAttributeDescriptionCount = 1, pVertexAttributeDescriptions = &vattr };
            var vinfo = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = vstages, pVertexInputState = &vin, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _pickLayout, renderPass = _rpPick, subpass = 0 };
            VkPipeline vp; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &vinfo, &vp), "vkCreateGraphicsPipelines(pickVolume)"); _pipelinePickVolume = vp;

            // Foliage billboards: same vertex layout and pipeline state as the generic pick pipeline
            // above, just PickBillboardVertexGlsl in place of PickVertexGlsl so the card's corner
            // offset gets applied before projecting - see that shader's comment.
            VkPipelineShaderStageCreateInfo* bstages = stackalloc VkPipelineShaderStageCreateInfo[2];
            bstages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = _pickBillboardVs, pName = entry };
            bstages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = _pickFs, pName = entry };
            var binfo = new VkGraphicsPipelineCreateInfo { stageCount = 2, pStages = bstages, pVertexInputState = &litVertexInput, pInputAssemblyState = &inputAssembly, pViewportState = &viewportState, pRasterizationState = &rasterCullNone, pMultisampleState = &multisample, pDepthStencilState = &depth, pColorBlendState = &blend, pDynamicState = &dynState, layout = _pickLayout, renderPass = _rpPick, subpass = 0 };
            VkPipeline bp; Check(_api.vkCreateGraphicsPipelines(pipelineCache, 1, &binfo, &bp), "vkCreateGraphicsPipelines(pickBillboard)"); _pipelinePickBillboard = bp;
        }
    }

    // The pick target never changes size (the window is a fixed pixel count), so it is built once.
    private void CreatePickResources()
    {
        (_pickImage, _pickMemory, _pickView) = CreateAttachmentImageSized(ColorFormat, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferSrc, VkImageAspectFlags.Color, PickTargetSize);
        (_pickDepthImage, _pickDepthMemory, _pickDepthView) = CreateAttachmentImageSized(DepthFormat, VkImageUsageFlags.DepthStencilAttachment, VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil, PickTargetSize);

        VkImageView* views = stackalloc VkImageView[2] { _pickView, _pickDepthView };
        var fb = new VkFramebufferCreateInfo { renderPass = _rpPick, attachmentCount = 2, pAttachments = views, width = PickTargetSize, height = PickTargetSize, layers = 1 };
        VkFramebuffer f; Check(_api.vkCreateFramebuffer(&fb, &f), "vkCreateFramebuffer(pick)"); _fbPick = f;

        ulong size = PickTargetSize * PickTargetSize * 4;
        (_pickReadback, _pickReadbackMemory) = CreateBuffer(size, VkBufferUsageFlags.TransferDst, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* p; Check(_api.vkMapMemory(_pickReadbackMemory, 0, size, 0, &p), "vkMapMemory(pickReadback)"); _pickReadbackMapped = p;

        var cbAlloc = new VkCommandBufferAllocateInfo { commandPool = _pool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1 };
        VkCommandBuffer cb; Check(_api.vkAllocateCommandBuffers(&cbAlloc, &cb), "vkAllocateCommandBuffers(pick)"); _pickCmd = cb;
        var fenceInfo = new VkFenceCreateInfo();
        VkFence fence; Check(_api.vkCreateFence(&fenceInfo, &fence), "vkCreateFence(pick)"); _pickFence = fence;
    }

    private static Texture CreateFallbackCubemap(GraphicsDevice gd)
    {
        var tex = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 6, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled | TextureUsage.Cubemap));
        var grey = new byte[] { 128, 128, 128, 128 };
        for (uint face = 0; face < 6; face++)
            gd.UpdateTexture(tex, grey, 0, 0, 0, 1, 1, 1, 0, face);
        return tex;
    }

    private (VkImage, VkDeviceMemory, VkImageView) CreateAttachmentImageSized(VkFormat format, VkImageUsageFlags usage, VkImageAspectFlags aspect, uint size)
    {
        var info = new VkImageCreateInfo { imageType = VkImageType.Image2D, format = format, extent = new VkExtent3D { width = size, height = size, depth = 1 }, mipLevels = 1, arrayLayers = 1, samples = VkSampleCountFlags.Count1, tiling = VkImageTiling.Optimal, usage = usage, sharingMode = VkSharingMode.Exclusive, initialLayout = VkImageLayout.Undefined };
        VkImage image; Check(_api.vkCreateImage(&info, &image), "vkCreateImage");
        VkMemoryRequirements reqs; _api.vkGetImageMemoryRequirements(image, &reqs);
        VkDeviceMemory memory = Allocate(reqs, VkMemoryPropertyFlags.DeviceLocal);
        Check(_api.vkBindImageMemory(image, memory, 0), "vkBindImageMemory");
        var viewInfo = new VkImageViewCreateInfo { image = image, viewType = VkImageViewType.Image2D, format = format, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = aspect, baseMipLevel = 0, levelCount = 1, baseArrayLayer = 0, layerCount = 1 } };
        VkImageView view; Check(_api.vkCreateImageView(&viewInfo, &view), "vkCreateImageView");
        return (image, memory, view);
    }

    private (VkImage, VkDeviceMemory, VkImageView) CreateAttachmentImage(VkFormat format, VkImageUsageFlags usage, VkImageAspectFlags aspect)
    {
        var info = new VkImageCreateInfo { imageType = VkImageType.Image2D, format = format, extent = new VkExtent3D { width = _width, height = _height, depth = 1 }, mipLevels = 1, arrayLayers = 1, samples = VkSampleCountFlags.Count1, tiling = VkImageTiling.Optimal, usage = usage, sharingMode = VkSharingMode.Exclusive, initialLayout = VkImageLayout.Undefined };
        VkImage image; Check(_api.vkCreateImage(&info, &image), "vkCreateImage");
        VkMemoryRequirements reqs; _api.vkGetImageMemoryRequirements(image, &reqs);
        VkDeviceMemory memory = Allocate(reqs, VkMemoryPropertyFlags.DeviceLocal);
        Check(_api.vkBindImageMemory(image, memory, 0), "vkBindImageMemory");
        var viewInfo = new VkImageViewCreateInfo { image = image, viewType = VkImageViewType.Image2D, format = format, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = aspect, baseMipLevel = 0, levelCount = 1, baseArrayLayer = 0, layerCount = 1 } };
        VkImageView view; Check(_api.vkCreateImageView(&viewInfo, &view), "vkCreateImageView");
        return (image, memory, view);
    }

    private void CreateTargets(GraphicsDevice gd)
    {
        for (int f = 0; f < Frames; f++)
        {
            _colorTex[f] = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(_width, _height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled | TextureUsage.RenderTarget));
            _colorImage[f] = _ctx.BackendInfo.GetVkImage(_colorTex[f]);
            var cvi = new VkImageViewCreateInfo { image = _colorImage[f], viewType = VkImageViewType.Image2D, format = ColorFormat, components = default, subresourceRange = new VkImageSubresourceRange { aspectMask = VkImageAspectFlags.Color, baseMipLevel = 0, levelCount = 1, baseArrayLayer = 0, layerCount = 1 } };
            VkImageView cv; Check(_api.vkCreateImageView(&cvi, &cv), "vkCreateImageView(color)"); _colorView[f] = cv;
        }

        (_depthImage, _depthMemory, _depthView) = CreateAttachmentImage(DepthFormat, VkImageUsageFlags.DepthStencilAttachment, VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil);
        (_accumImage, _accumMemory, _accumView) = CreateAttachmentImage(AccumFormat, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color);
        (_revealImage, _revealMemory, _revealView) = CreateAttachmentImage(RevealFormat, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color);

        // Opaque and resolve write colour, so they are per-slot. Accum only touches the shared
        // accum/reveal/depth attachments, so one is enough.
        VkImageView* oViews = stackalloc VkImageView[2];
        VkImageView* rViews = stackalloc VkImageView[2];
        for (int f = 0; f < Frames; f++)
        {
            oViews[0] = _colorView[f]; oViews[1] = _depthView;
            var fbO = new VkFramebufferCreateInfo { renderPass = _rpOpaque, attachmentCount = 2, pAttachments = oViews, width = _width, height = _height, layers = 1 };
            VkFramebuffer fo; Check(_api.vkCreateFramebuffer(&fbO, &fo), "vkCreateFramebuffer(opaque)"); _fbOpaque[f] = fo;

            rViews[0] = _colorView[f]; rViews[1] = _depthView;
            var fbR = new VkFramebufferCreateInfo { renderPass = _rpResolve, attachmentCount = 2, pAttachments = rViews, width = _width, height = _height, layers = 1 };
            VkFramebuffer fr; Check(_api.vkCreateFramebuffer(&fbR, &fr), "vkCreateFramebuffer(resolve)"); _fbResolve[f] = fr;
        }

        VkImageView* aViews = stackalloc VkImageView[3] { _accumView, _revealView, _depthView };
        var fbA = new VkFramebufferCreateInfo { renderPass = _rpAccum, attachmentCount = 3, pAttachments = aViews, width = _width, height = _height, layers = 1 };
        VkFramebuffer fa; Check(_api.vkCreateFramebuffer(&fbA, &fa), "vkCreateFramebuffer(accum)"); _fbAccum = fa;

        // (Re)point the resolve descriptor set at the current accum/reveal views.
        var accumInfo = new VkDescriptorImageInfo { sampler = _sampler, imageView = _accumView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var revealInfo = new VkDescriptorImageInfo { sampler = _sampler, imageView = _revealView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        VkWriteDescriptorSet* wr = stackalloc VkWriteDescriptorSet[2];
        wr[0] = new VkWriteDescriptorSet { dstSet = _resolveSet, dstBinding = 0, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &accumInfo };
        wr[1] = new VkWriteDescriptorSet { dstSet = _resolveSet, dstBinding = 1, descriptorCount = 1, descriptorType = VkDescriptorType.CombinedImageSampler, pImageInfo = &revealInfo };
        _api.vkUpdateDescriptorSets(2, wr, 0, null);
    }

    private void DestroyTargets()
    {
        for (int f = 0; f < Frames; f++)
        {
            _api.vkDestroyFramebuffer(_fbResolve[f]);
            _api.vkDestroyFramebuffer(_fbOpaque[f]);
            _api.vkDestroyImageView(_colorView[f]);
            _colorTex[f]?.Dispose();
            _colorTex[f] = null!;
        }
        _api.vkDestroyFramebuffer(_fbAccum);
        _api.vkDestroyImageView(_revealView); _api.vkDestroyImage(_revealImage); _api.vkFreeMemory(_revealMemory);
        _api.vkDestroyImageView(_accumView); _api.vkDestroyImage(_accumImage); _api.vkFreeMemory(_accumMemory);
        _api.vkDestroyImageView(_depthView); _api.vkDestroyImage(_depthImage); _api.vkFreeMemory(_depthMemory);
    }

    private void SetFullViewport()
    {
        // Negative-height viewport (Vulkan 1.1) flips Y to match Bliss's Cam3D projection. Set once;
        // it persists across the render passes in this command buffer.
        var vp = new VkViewport { x = 0, y = _height, width = _width, height = -(float)_height, minDepth = 0, maxDepth = 1 };
        var sc = new VkRect2D { offset = default, extent = new VkExtent2D { width = _width, height = _height } };
        _api.vkCmdSetViewport(_cmd, 0, 1, &vp);
        _api.vkCmdSetScissor(_cmd, 0, 1, &sc);
    }

    // Re-recorded every frame (implicit reset via the pool's ResetCommandBuffer flag) with only the
    // frustum-visible draws. Safe because Frame() waits the fence before the next record.
    // Per-frame draw/material-bind counts, surfaced as profiler counters.
    private int _statBinds, _statDraws;

    private void RecordCommands()
    {
        _statBinds = 0; _statDraws = 0;
        Check(_api.vkResetCommandBuffer(_cmd, VkCommandBufferResetFlags.None), "vkResetCommandBuffer");
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(_api.vkBeginCommandBuffer(_cmd, &begin), "vkBeginCommandBuffer");
        SetFullViewport();

        var area = new VkRect2D { offset = default, extent = new VkExtent2D { width = _width, height = _height } };

        // Pass 1: OPAQUE -> colour + depth.
        VkClearValue* clearsO = stackalloc VkClearValue[2];
        clearsO[0] = new VkClearValue { color = new VkClearColorValue(ClearColour.X, ClearColour.Y, ClearColour.Z, ClearColour.W) };
        clearsO[1] = new VkClearValue { depthStencil = new VkClearDepthStencilValue(1f, 0) };
        var rpO = new VkRenderPassBeginInfo { renderPass = _rpOpaque, framebuffer = _fbOpaque[_slot], renderArea = area, clearValueCount = 2, pClearValues = clearsO };
        _api.vkCmdBeginRenderPass(_cmd, &rpO, VkSubpassContents.Inline);
        BindLitState();
        // Everything that writes depth goes first - Opaque + Cutout, then Soft-Edge's alpha-tested
        // DEPTH PREPASS (colour writes off) so its geometry occludes correctly, then the foliage
        // billboards. Only then Additive, which is depth-tested but does NOT write, so it has to see
        // the finished depth buffer. Volume wireframes last. Over-blending is the WBOIT pass below.
        DrawVisible(_pipelineOpaque, _visibleOpaque, _visOpaqueCount);
        DrawVisible(_pipelineSoftEdgeDepth, _visibleSoftEdge, _visSoftCount, softEdgeDepthPrepass: true);
        DrawVisible(_pipelineBillboard, _visibleBillboard, _visBillCount);
        DrawVisible(_pipelineAdditive, _visibleAdditive, _visAddCount);
        DrawVolumes();
        _api.vkCmdEndRenderPass(_cmd);

        // Pass 2: ACCUMULATE translucent -> accum (=0) + reveal (=1), depth-tested (no write).
        VkClearValue* clearsA = stackalloc VkClearValue[2];
        clearsA[0] = new VkClearValue { color = new VkClearColorValue(0f, 0f, 0f, 0f) };
        clearsA[1] = new VkClearValue { color = new VkClearColorValue(1f, 0f, 0f, 0f) };
        var rpA = new VkRenderPassBeginInfo { renderPass = _rpAccum, framebuffer = _fbAccum, renderArea = area, clearValueCount = 2, pClearValues = clearsA };
        _api.vkCmdBeginRenderPass(_cmd, &rpA, VkSubpassContents.Inline);
        BindLitState();
        // Over-blended modes (Overlay/Scunge/Blended) plus Soft-Edge's colour pass - all order-
        // independent through WBOIT, which also gives Blended its back-to-front result for free.
        DrawVisible(_pipelineAccum, _visibleTranslucent, _visTransCount);
        DrawVisible(_pipelineAccum, _visibleSoftEdge, _visSoftCount);
        _api.vkCmdEndRenderPass(_cmd);

        // Pass 3: RESOLVE -> composite over the opaque colour (fullscreen triangle).
        var rpR = new VkRenderPassBeginInfo { renderPass = _rpResolve, framebuffer = _fbResolve[_slot], renderArea = area, clearValueCount = 0, pClearValues = null };
        _api.vkCmdBeginRenderPass(_cmd, &rpR, VkSubpassContents.Inline);
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _pipelineResolve);
        VkDescriptorSet rset = _resolveSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _resolveLayout, 0, 1, &rset, 0, null);
        _api.vkCmdDraw(_cmd, 3, 1, 0, 0);
        DrawSelectionOutline();
        DrawDebugLines();
        _api.vkCmdEndRenderPass(_cmd);

        Check(_api.vkEndCommandBuffer(_cmd), "vkEndCommandBuffer");
    }

    // Draws the given ordered visible-index list (indices into the sorted static arrays), binding each
    // material's set + push constant once per run. firstInstance stays the STATIC index so it still
    // addresses that instance's transform in the (whole, unculled) SSBO.
    /// <summary>Uploads this frame's debug segments. Each entry is one line; the buffer grows to the
    /// high-water mark. Call before <see cref="Frame"/>; passing nothing clears the overlay.</summary>
    public void SetDebugLines(IReadOnlyList<(Vector3 a, Vector3 b, Vector4 color)> lines)
    {
        // Rewrites a buffer the in-flight frame may be reading, and can destroy it outright to grow it.
        // Called only when the overlay changes, so draining first costs nothing.
        WaitForPendingFrames();
        _debugVertexCount = lines.Count * 2;
        if (_debugVertexCount == 0) return;

        if (_debugVertexCount > _debugLineCapacity)
        {
            if (_debugLineBuffer.Handle != 0)
            {
                _api.vkDeviceWaitIdle();
                _api.vkDestroyBuffer(_debugLineBuffer);
                _api.vkFreeMemory(_debugLineMemory);
            }
            _debugLineCapacity = Math.Max(_debugVertexCount, 1024);
            ulong size = (ulong)(_debugLineCapacity * DebugLineFloats * sizeof(float));
            (_debugLineBuffer, _debugLineMemory) = CreateBuffer(size, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            void* p; Check(_api.vkMapMemory(_debugLineMemory, 0, size, 0, &p), "vkMapMemory(debugLines)");
            _debugLineMapped = p;
        }

        var dst = (float*)_debugLineMapped;
        int o = 0;
        foreach (var (a, b, colour) in lines)
        {
            dst[o + 0] = a.X; dst[o + 1] = a.Y; dst[o + 2] = a.Z;
            dst[o + 3] = colour.X; dst[o + 4] = colour.Y; dst[o + 5] = colour.Z; dst[o + 6] = colour.W;
            dst[o + 7] = b.X; dst[o + 8] = b.Y; dst[o + 9] = b.Z;
            dst[o + 10] = colour.X; dst[o + 11] = colour.Y; dst[o + 12] = colour.Z; dst[o + 13] = colour.W;
            o += DebugLineFloats * 2;
        }
    }

    private void DrawDebugLines()
    {
        if (_debugVertexCount == 0) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _pipelineDebugLines);
        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _outlineLayout, 0, 1, &s0, 0, null);
        VkBuffer vb = _debugLineBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        _api.vkCmdDraw(_cmd, (uint)_debugVertexCount, 1, 0, 0);
    }

    // Stencil mask-and-inflate rim around the selected entity's own instances, drawn last in the
    // resolve pass using the scene's own vertex/index buffers and transform SSBO.
    private void DrawSelectionOutline()
    {
        if (_selected is null || !_ownerInstances.TryGetValue(_selected, out var owned) || owned.Length == 0) return;

        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _outlineLayout, 0, 1, &s0, 0, null);
        VkBuffer vb = _vertexBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _indexBuffer, 0, VkIndexType.Uint32);

        // Pass 1 stamps the whole selection's footprint before pass 2 reads it, so a multi-mesh entity
        // does not outline the seams between its own parts.
        Vector4* pc = stackalloc Vector4[2];
        pc[0] = _outlineColor;
        for (int pass = 0; pass < 2; pass++)
        {
            _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, pass == 0 ? _pipelineOutlineMask : _pipelineOutlineRim);
            pc[1] = new Vector4(pass == 0 ? 0f : _outlineThickness, 0f, 0f, 0f);
            _api.vkCmdPushConstants(_cmd, _outlineLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 32, pc);
            foreach (int i in owned)
                _api.vkCmdDrawIndexed(_cmd, _drawIndexCount[i], 1, _drawFirstIndex[i], _drawVertexOffset[i], (uint)i);
        }
    }

    // Binds everything the lit draws share: the scene descriptor set (camera UBO + transform SSBO +
    // light UBO + cubemap) and the merged scene vertex/index buffers. Called at the start of every pass
    // that issues lit draws, since DrawVolumes and the outline bind through pipeline layouts with
    // different push-constant ranges, which invalidates the lit descriptor set bindings under Vulkan's
    // layout-compatibility rules.
    private void BindLitState()
    {
        VkDescriptorSet set0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _layout, 0, 1, &set0, 0, null);
        VkBuffer vb = _vertexBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _indexBuffer, 0, VkIndexType.Uint32);
    }

    // Draws the per-frame volume list as depth-tested wireframe cubes (line list), one push constant
    // (box matrix + colour) each. Cheap (few volumes); provided fresh each frame so selection/edits show.
    private void DrawVolumes()
    {
        if (_volumeCount == 0) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, _volumePipeline);
        VkDescriptorSet s0 = _descSet;
        _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _volumeLayout, 0, 1, &s0, 0, null);
        VkBuffer evb = _edgeVertexBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_cmd, 0, 1, &evb, &offset);
        _api.vkCmdBindIndexBuffer(_cmd, _edgeIndexBuffer, 0, VkIndexType.Uint32);
        byte* pc = stackalloc byte[80];
        for (int i = 0; i < _volumeCount; i++)
        {
            var v = _volumes[i];
            *(Matrix4x4*)pc = v.world;
            *(Vector4*)(pc + 64) = v.color;
            _api.vkCmdPushConstants(_cmd, _volumeLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
            _api.vkCmdDrawIndexed(_cmd, _edgeIndexCount, 1, 0, 0, 0);
        }
    }

    private void DrawVisible(VkPipeline pipeline, int[] visible, int count, bool softEdgeDepthPrepass = false)
    {
        if (count == 0) return;
        _api.vkCmdBindPipeline(_cmd, VkPipelineBindPoint.Graphics, pipeline);
        int boundMat = -1;
        _statDraws += count;
        // Outside the loop: see CreateDescriptors - a stackalloc per material switch would grow this
        // frame by 32 bytes for every bind in the pass.
        Vector4* pc = stackalloc Vector4[2];
        for (int k = 0; k < count; k++)
        {
            int i = visible[k];
            if (_drawMatSlot[i] != boundMat)
            {
                boundMat = _drawMatSlot[i];
                _statBinds++;
                VkDescriptorSet ms = _matSets[boundMat];
                _api.vkCmdBindDescriptorSets(_cmd, VkPipelineBindPoint.Graphics, _layout, 1, 1, &ms, 0, null);
                var pc0 = _matPC0[boundMat];
                // Soft-Edge pass 1 clips at 128/255 (the material's stored ref is pass 2's 4/255).
                if (softEdgeDepthPrepass) pc0.W = 128f / 255f;
                pc[0] = pc0;
                pc[1] = new Vector4(_matRenderMode[boundMat], _matVertexAlpha[boundMat], _lit ? 1f : 0f, _matAlbedoHasAlpha[boundMat]);
                _api.vkCmdPushConstants(_cmd, _layout, VkShaderStageFlags.Fragment, 0, 32, pc);
            }
            _api.vkCmdDrawIndexed(_cmd, _drawIndexCount[i], 1, _drawFirstIndex[i], _drawVertexOffset[i], (uint)i);
        }
    }

    /// <summary>Records and submits one frame. <paramref name="selected"/> is the entity to outline
    /// (null for none) - it is matched by reference against the owners captured with the instances.</summary>
    public void Frame(Matrix4x4 view, Matrix4x4 projection, LightData light,
        IReadOnlyList<(Matrix4x4 world, Vector4 color, uint pickId)> volumes, float volumeThickness,
        object? selected = null, Vector4 outlineColor = default, float outlineThickness = 0.004f,
        Vector3 cameraPosition = default, bool mobyDistanceCulling = false, bool lit = true,
        bool frustumCulling = true, SceneEntityKind kinds = SceneEntityKind.All,
        TextureFiltering filtering = TextureFiltering.Bilinear)
    {
        ApplyFiltering(filtering);
        _cmd = _cmds[_slot];
        _descSet = _descSets[_slot];
        Matrix4x4 viewProj = view * projection;
        var ub = (Matrix4x4*)_ubMappings[_slot];
        ub[0] = viewProj; ub[1] = view; ub[2] = projection;
        _selected = selected;
        if (outlineColor != default) _outlineColor = outlineColor;
        _outlineThickness = outlineThickness;
        _cameraPosition = cameraPosition;
        _mobyDistanceCulling = mobyDistanceCulling;
        _frustumCulling = frustumCulling;
        _kindMask = (byte)kinds;
        _lit = lit;
        *(LightData*)_lightMappings[_slot] = light;
        _volumes = volumes;
        _volumeCount = volumes.Count;
        if (volumeThickness != _edgeThickness) WriteEdgeVertices(volumeThickness);

        // Frustum-cull (threaded) then re-record only the visible draws. Safe to reset/re-record the
        // command buffer here: the previous frame's fence wait (below) already drained the GPU.
        using (Diagnostics.FrameProfiler.Sample("Vk Cull"))
        {
            ExtractFrustumPlanes(viewProj);
            _visOpaqueCount = CullRange(0, _overStart, _visibleOpaque);
            _visTransCount = CullRange(_overStart, _addStart, _visibleTranslucent);
            _visAddCount = CullRange(_addStart, _softStart, _visibleAdditive);
            _visSoftCount = CullRange(_softStart, _billStart, _visibleSoftEdge);
            _visBillCount = CullRange(_billStart, _instanceCount, _visibleBillboard);
        }
        using (Diagnostics.FrameProfiler.Sample("Vk Record"))
            RecordCommands();
        _recorded = true;

        // The frame is now recorded but not submitted - SubmitFrame does that, after the swapchain
        // present, so the GPU works through the previous frame while the CPU does the event pump,
        // ImGui's frame, culling and recording. "Vk GPU Wait" is the leftover: how much longer the GPU
        // needed than the CPU took to get back here.
        int previous = _slot ^ 1;
        if (_pending[previous])
        {
            using (Diagnostics.FrameProfiler.Sample("Vk GPU Wait"))
            {
                VkFence fence = _fences[previous];
                _api.vkWaitForFences(1, &fence, true, ulong.MaxValue);
                _api.vkResetFences(1, &fence);
            }
            _pending[previous] = false;
            // Finished, so it is the one ImGui can safely sample this frame.
            _displaySlot = previous;
            _ctx.BackendInfo.OverrideImageLayout(_colorTex[previous], (uint)VkImageLayout.ShaderReadOnlyOptimal);
        }
        else if (!_everSubmitted)
        {
            // First frame: there is no previous result to show, and an unrendered target is undefined
            // memory. Submit and wait inline just this once so the viewport starts on a real image.
            // The slot must be captured BEFORE submitting: SubmitFrame advances _slot, so reading it
            // afterwards waits on a fence nothing was ever submitted with, which blocks forever.
            int submitted = _slot;
            SubmitFrame();
            VkFence fence = _fences[submitted];
            _api.vkWaitForFences(1, &fence, true, ulong.MaxValue);
            _api.vkResetFences(1, &fence);
            _pending[submitted] = false;
            _displaySlot = submitted;
            _ctx.BackendInfo.OverrideImageLayout(_colorTex[submitted], (uint)VkImageLayout.ShaderReadOnlyOptimal);
        }

        if (!_loggedInit) { _loggedInit = true; Console.WriteLine($"[VkRenderer] frame 1 visible - {_visOpaqueCount} opaque/cutout, {_visTransCount} over-blended, {_visAddCount} additive, {_visSoftCount} soft-edge, {_visBillCount} foliage. Recorded {_statDraws} draws with {_statBinds} material binds."); }
        Diagnostics.FrameProfiler.SetCounter("Vk material binds", _statBinds);
        Diagnostics.FrameProfiler.SetCounter("Vk frames in flight", Frames);
        Diagnostics.FrameProfiler.SetCounter("Vk visible draws", _visOpaqueCount + _visTransCount + _visAddCount + _visSoftCount + _visBillCount);
        Diagnostics.FrameProfiler.SetCounter("Vk total draws", _instanceCount);
    }

    /// <summary>Submits the frame <see cref="Frame"/> recorded, and moves to the other slot. Called
    /// after the swapchain present (which does a full device wait, see Window.Draw), so the GPU works
    /// through the scene while the CPU starts the next frame.</summary>
    public void SubmitFrame()
    {
        // Nothing recorded since the last submit: the view did not draw this frame (collapsed, closed,
        // or the renderer was rebuilt). Resubmitting a stale buffer would just burn GPU time.
        if (!_recorded || _pending[_slot]) return;
        _recorded = false;

        VkCommandBuffer cmd = _cmds[_slot];
        var submit = new VkSubmitInfo { commandBufferCount = 1, pCommandBuffers = &cmd };
        Check(_api.vkQueueSubmit(_ctx.GraphicsQueue, 1, &submit, _fences[_slot]), "vkQueueSubmit");
        _pending[_slot] = true;
        _everSubmitted = true;
        _submits++;
        _slot ^= 1;
    }

    /// <summary>Blocks until nothing this renderer submitted is still running. Anything that mutates
    /// state the GPU reads outside a recorded command buffer has to call this first.</summary>
    private void WaitForPendingFrames()
    {
        for (int f = 0; f < Frames; f++)
        {
            if (!_pending[f]) continue;
            VkFence fence = _fences[f];
            _api.vkWaitForFences(1, &fence, true, ulong.MaxValue);
            _api.vkResetFences(1, &fence);
            _pending[f] = false;
        }
    }

    /// <summary>GPU colour-ID pick at a viewport pixel. Returns the id of the entity under the cursor,
    /// or <see cref="NoHit"/>. The view-projection is post-multiplied by a clip-space window that
    /// expands the pick region to fill NDC, shrinking the target and the frustum to just the cursor
    /// area. Synchronous: submits its own command buffer and waits.</summary>
    public uint Pick(Matrix4x4 view, Matrix4x4 projection, int mouseX, int mouseY, float viewportWidth, float viewportHeight)
    {
        if (viewportWidth <= 0f || viewportHeight <= 0f) return NoHit;

        // Clip-space window: scale/offset so the PickWindowPixels-square region around the cursor fills
        // NDC. Y is inverted because screen y runs down while NDC y runs up (the same mapping the rest
        // of the editor's screen-space maths uses).
        float halfW = PickWindowPixels / viewportWidth;
        float halfH = PickWindowPixels / viewportHeight;
        float centreX = 2f * (mouseX + 0.5f) / viewportWidth - 1f;
        float centreY = 1f - 2f * (mouseY + 0.5f) / viewportHeight;
        var window = Matrix4x4.Identity;
        window.M11 = 1f / halfW;
        window.M22 = 1f / halfH;
        window.M41 = -centreX / halfW;
        window.M42 = -centreY / halfH;

        // The scene may still be running (frames are in flight), and this both writes into the slot's
        // uniform buffer and shares the transform SSBO with it. Drain first - a click can afford it.
        WaitForPendingFrames();

        Matrix4x4 pickViewProj = (view * projection) * window;
        // Billboards add their corner offset in VIEW space (see BillboardVertexGlsl), so the windowing
        // has to be folded into the projection alone rather than the combined view+projection above -
        // PickBillboardVertexGlsl applies uView itself, then this in place of the main pass's uProj.
        Matrix4x4 pickBillboardProj = projection * window;
        var ub = (Matrix4x4*)_ubMappings[_displaySlot];
        ub[3] = pickViewProj;
        ub[4] = pickBillboardProj;
        _descSet = _descSets[_displaySlot];
        ExtractPlanes(pickViewProj, _pickPlanes);

        Check(_api.vkResetCommandBuffer(_pickCmd, VkCommandBufferResetFlags.None), "vkResetCommandBuffer(pick)");
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(_api.vkBeginCommandBuffer(_pickCmd, &begin), "vkBeginCommandBuffer(pick)");

        var vp = new VkViewport { x = 0, y = PickTargetSize, width = PickTargetSize, height = -(float)PickTargetSize, minDepth = 0, maxDepth = 1 };
        var sc = new VkRect2D { offset = default, extent = new VkExtent2D { width = PickTargetSize, height = PickTargetSize } };
        _api.vkCmdSetViewport(_pickCmd, 0, 1, &vp);
        _api.vkCmdSetScissor(_pickCmd, 0, 1, &sc);

        VkClearValue* clears = stackalloc VkClearValue[2];
        clears[0] = new VkClearValue { color = new VkClearColorValue(1f, 1f, 1f, 1f) }; // decodes to NoHit
        clears[1] = new VkClearValue { depthStencil = new VkClearDepthStencilValue(1f, 0) };
        var area = new VkRect2D { offset = default, extent = new VkExtent2D { width = PickTargetSize, height = PickTargetSize } };
        var rp = new VkRenderPassBeginInfo { renderPass = _rpPick, framebuffer = _fbPick, renderArea = area, clearValueCount = 2, pClearValues = clears };
        _api.vkCmdBeginRenderPass(_pickCmd, &rp, VkSubpassContents.Inline);

        VkDescriptorSet set0 = _descSet;
        _api.vkCmdBindDescriptorSets(_pickCmd, VkPipelineBindPoint.Graphics, _pickLayout, 0, 1, &set0, 0, null);

        byte* pc = stackalloc byte[80];
        *(Matrix4x4*)pc = Matrix4x4.Identity;

        // Scene geometry. Every non-billboard bucket is walked, including the two the visible lists
        // overlap on (Soft-Edge) - drawing an instance twice is harmless here, both draws write the
        // same id. Billboards are excluded: PickVertexGlsl transforms inPos alone, and every corner of
        // a foliage card shares the same anchor position, so it would draw a zero-area triangle -
        // they get their own pipeline/loop below instead.
        _api.vkCmdBindPipeline(_pickCmd, VkPipelineBindPoint.Graphics, _pipelinePick);
        VkBuffer vb = _vertexBuffer; ulong offset = 0;
        _api.vkCmdBindVertexBuffers(_pickCmd, 0, 1, &vb, &offset);
        _api.vkCmdBindIndexBuffer(_pickCmd, _indexBuffer, 0, VkIndexType.Uint32);
        for (int i = 0; i < _billStart; i++)
        {
            if (_instPickId[i] == NoHit) continue;
            if (!InPickFrustum(i)) continue;
            *(uint*)(pc + 64) = _instPickId[i];
            _api.vkCmdPushConstants(_pickCmd, _pickLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
            _api.vkCmdDrawIndexed(_pickCmd, _drawIndexCount[i], 1, _drawFirstIndex[i], _drawVertexOffset[i], (uint)i);
        }

        // Foliage billboards: same shared vertex/index buffers, but PickBillboardVertexGlsl so the
        // card's corner offset gets applied in view space before projecting, same as the visible pass.
        if (_billStart < _instanceCount)
        {
            _api.vkCmdBindPipeline(_pickCmd, VkPipelineBindPoint.Graphics, _pipelinePickBillboard);
            for (int i = _billStart; i < _instanceCount; i++)
            {
                if (_instPickId[i] == NoHit) continue;
                if (!InPickFrustum(i)) continue;
                *(uint*)(pc + 64) = _instPickId[i];
                _api.vkCmdPushConstants(_pickCmd, _pickLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
                _api.vkCmdDrawIndexed(_pickCmd, _drawIndexCount[i], 1, _drawFirstIndex[i], _drawVertexOffset[i], (uint)i);
            }
        }

        // Volume edges, so trigger volumes stay selectable - same thin-box geometry the wireframe uses.
        if (_volumeCount > 0)
        {
            _api.vkCmdBindPipeline(_pickCmd, VkPipelineBindPoint.Graphics, _pipelinePickVolume);
            VkBuffer evb = _edgeVertexBuffer; ulong eoffset = 0;
            _api.vkCmdBindVertexBuffers(_pickCmd, 0, 1, &evb, &eoffset);
            _api.vkCmdBindIndexBuffer(_pickCmd, _edgeIndexBuffer, 0, VkIndexType.Uint32);
            for (int i = 0; i < _volumeCount; i++)
            {
                var v = _volumes[i];
                if (v.pickId == NoHit) continue;
                *(Matrix4x4*)pc = v.world;
                *(uint*)(pc + 64) = v.pickId;
                _api.vkCmdPushConstants(_pickCmd, _pickLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, 80, pc);
                _api.vkCmdDrawIndexed(_pickCmd, _edgeIndexCount, 1, 0, 0, 0);
            }
        }

        _api.vkCmdEndRenderPass(_pickCmd);

        var copy = new VkBufferImageCopy
        {
            bufferOffset = 0, bufferRowLength = 0, bufferImageHeight = 0,
            imageSubresource = new VkImageSubresourceLayers { aspectMask = VkImageAspectFlags.Color, mipLevel = 0, baseArrayLayer = 0, layerCount = 1 },
            imageOffset = default,
            imageExtent = new VkExtent3D { width = PickTargetSize, height = PickTargetSize, depth = 1 },
        };
        _api.vkCmdCopyImageToBuffer(_pickCmd, _pickImage, VkImageLayout.TransferSrcOptimal, _pickReadback, 1, &copy);
        Check(_api.vkEndCommandBuffer(_pickCmd), "vkEndCommandBuffer(pick)");

        VkCommandBuffer cmd = _pickCmd;
        var submit = new VkSubmitInfo { commandBufferCount = 1, pCommandBuffers = &cmd };
        Check(_api.vkQueueSubmit(_ctx.GraphicsQueue, 1, &submit, _pickFence), "vkQueueSubmit(pick)");
        VkFence fence = _pickFence;
        _api.vkWaitForFences(1, &fence, true, ulong.MaxValue);
        _api.vkResetFences(1, &fence);

        // Nearest hit to the centre of the window wins: a click a pixel or two off a thin silhouette
        // should still select the object rather than miss it.
        var px = (byte*)_pickReadbackMapped;
        uint best = NoHit;
        int bestDistSq = int.MaxValue;
        const int centre = (int)PickTargetSize / 2;
        for (int y = 0; y < PickTargetSize; y++)
        {
            for (int x = 0; x < PickTargetSize; x++)
            {
                byte* t = px + ((y * (int)PickTargetSize + x) * 4);
                uint id = (uint)(t[0] | (t[1] << 8) | (t[2] << 16) | (t[3] << 24));
                if (id == NoHit) continue;
                int dx = x - centre, dy = y - centre;
                int distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq) { bestDistSq = distSq; best = id; }
            }
        }
        return best;
    }

    private bool InPickFrustum(int i)
    {
        Vector3 c = _instCenter[i]; float r = _instRadius[i];
        for (int p = 0; p < 6; p++)
        {
            Vector4 pl = _pickPlanes[p];
            if (pl.X * c.X + pl.Y * c.Y + pl.Z * c.Z + pl.W < -r) return false;
        }
        return true;
    }

    /// <summary>Rewrites one entity's world matrices in the transform SSBO (and its culling sphere
    /// centre), so an editor move/rotate/scale shows immediately without rebuilding the scene. The
    /// matrices must arrive in the same order <c>Entity.GetRenderablesForVk</c> produced them, which is
    /// the order they were captured in. Returns false if the entity is not part of the captured scene.</summary>
    public bool UpdateEntityTransforms(object owner, IReadOnlyList<Matrix4x4> worlds, Vector4 worldBoundingSphere)
    {
        if (!_ownerInstances.TryGetValue(owner, out var owned) || owned.Length != worlds.Count) return false;

        // Written straight into the transform SSBO, which is shared across frames in flight, without a
        // fence wait (waiting every frame would defeat the overlap). The equality check below means
        // this only actually writes while the gizmo is being dragged; worst case is one frame reading a
        // half-updated matrix.
        var dst = (Matrix4x4*)_tbMapped;
        for (int k = 0; k < owned.Length; k++)
        {
            int i = owned[k];
            if (dst[i] != worlds[k]) dst[i] = worlds[k];
            _instCenter[i] = new Vector3(worldBoundingSphere.X, worldBoundingSphere.Y, worldBoundingSphere.Z);
            _instRadius[i] = worldBoundingSphere.W;
        }
        return true;
    }

    /// <summary>Assigns (or reuses) a bone-palette region for <paramref name="owner"/> and writes
    /// <paramref name="skinMatrices"/> into it, pointing every one of its draw instances at that region
    /// via the BoneBase SSBO. Safe to call every frame while an animation plays - it only overwrites
    /// the region's contents, never reallocates. Returns false if the owner has no instances in this
    /// scene, its skeleton is larger than the palette was sized for, or every region is already taken
    /// by other playing entities - shouldn't happen in practice, since _maxConcurrentAnimated is
    /// sized to the number of skinned mobys actually in the scene (see AssetManager.BuildVkScene).</summary>
    public bool TrySetAnimatedInstance(object owner, IReadOnlyList<Matrix4x4> skinMatrices)
    {
        if (!_ownerInstances.TryGetValue(owner, out var owned)) return false;
        if (skinMatrices.Count > Math.Max(_maxSkeletonBones, 1)) return false;

        int region = Array.IndexOf(_animRegionOwners, owner);
        if (region < 0)
        {
            region = Array.IndexOf(_animRegionOwners, null);
            if (region < 0) return false;
            _animRegionOwners[region] = owner;
        }

        int bonesPerRegion = Math.Max(_maxSkeletonBones, 1);
        int baseIndex = (1 + region) * bonesPerRegion;
        var bones = (Matrix4x4*)_bpMapped;
        for (int k = 0; k < skinMatrices.Count; k++) bones[baseIndex + k] = skinMatrices[k];

        var boneBase = (int*)_bbMapped;
        foreach (int i in owned) boneBase[i] = baseIndex;
        return true;
    }

    /// <summary>Frees the owner's bone-palette region (if any) and snaps its instances back to the
    /// shared identity region (bind pose). No-op if the owner was never assigned one.</summary>
    public void ClearAnimatedInstance(object owner)
    {
        int region = Array.IndexOf(_animRegionOwners, owner);
        if (region < 0) return;
        _animRegionOwners[region] = null;

        if (_ownerInstances.TryGetValue(owner, out var owned))
        {
            var boneBase = (int*)_bbMapped;
            foreach (int i in owned) boneBase[i] = 0;
        }
    }

    // Six frustum planes (left,right,bottom,top,near,far) in world space from the row-vector viewProj
    // (Gribb-Hartmann; D3D/Vulkan clip with z in [0,1]). Plane (a,b,c,d): a*x+b*y+c*z+d >= 0 is inside.
    private void ExtractFrustumPlanes(Matrix4x4 m) => ExtractPlanes(m, _planes);

    private static void ExtractPlanes(Matrix4x4 m, Vector4[] planes)
    {
        planes[0] = NormalizePlane(new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41));
        planes[1] = NormalizePlane(new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41));
        planes[2] = NormalizePlane(new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42));
        planes[3] = NormalizePlane(new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42));
        planes[4] = NormalizePlane(new Vector4(m.M13, m.M23, m.M33, m.M43));
        planes[5] = NormalizePlane(new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43));
    }

    private static Vector4 NormalizePlane(Vector4 p)
    {
        float len = new Vector3(p.X, p.Y, p.Z).Length();
        return len > 0f ? p / len : p;
    }

    // Culls [lo,hi) of the sorted instances into outBuf (ordered, indices preserved). Threaded for large
    // ranges: each chunk compacts into its own disjoint region of _cullScratch (no locks), then the
    // per-chunk runs are concatenated in order - so material-run batching in DrawVisible still holds.
    private int CullRange(int lo, int hi, int[] outBuf)
    {
        int n = hi - lo;
        if (n <= 0) return 0;
        if (n < 4096)
        {
            int c = 0;
            for (int i = lo; i < hi; i++) if (Visible(i)) outBuf[c++] = i;
            return c;
        }
        int threads = Math.Min(_chunkCounts.Length, Math.Max(2, n / 4096));
        int chunkLen = (n + threads - 1) / threads;
        System.Threading.Tasks.Parallel.For(0, threads, t =>
        {
            int s = lo + t * chunkLen; int e = Math.Min(s + chunkLen, hi);
            int w = s;
            for (int i = s; i < e; i++) if (Visible(i)) _cullScratch[w++] = i;
            _chunkCounts[t] = w - s;
        });
        int total = 0;
        for (int t = 0; t < threads; t++)
        {
            int s = lo + t * chunkLen;
            Array.Copy(_cullScratch, s, outBuf, total, _chunkCounts[t]);
            total += _chunkCounts[t];
        }
        return total;
    }

    private bool Visible(int i)
    {
        // Per-type visibility first: it is a single mask test, and a type switched off is switched off
        // whatever the camera is doing.
        if ((_instKind[i] & _kindMask) == 0) return false;

        Vector3 c = _instCenter[i]; float r = _instRadius[i];
        // The game's own per-moby display distance, checked before the frustum planes (a single
        // squared compare).
        if (_mobyDistanceCulling)
        {
            float d = _instDisplayDist[i];
            if (d >= 0f && Vector3.DistanceSquared(c, _cameraPosition) > d * d) return false;
        }
        if (!_frustumCulling) return true;
        for (int p = 0; p < 6; p++)
        {
            Vector4 pl = _planes[p];
            if (pl.X * c.X + pl.Y * c.Y + pl.Z * c.Z + pl.W < -r) return false;
        }
        return true;
    }

    public void Resize(GraphicsDevice gd, uint width, uint height)
    {
        width = Math.Max(width, 1u); height = Math.Max(height, 1u);
        if (width == _width && height == _height) return;
        WaitForPendingFrames();
        _api.vkDeviceWaitIdle();
        DestroyTargets();
        _width = width; _height = height;
        CreateTargets(gd);
        // Both colour targets are new and hold undefined memory, so the next Frame() has to go through
        // the first-frame path again rather than presenting one of them as a finished image.
        _slot = 0;
        _displaySlot = 0;
        _everSubmitted = false;
    }

    private (VkBuffer, VkDeviceMemory) CreateBuffer(ulong size, VkBufferUsageFlags usage, VkMemoryPropertyFlags props)
    {
        var info = new VkBufferCreateInfo { size = size, usage = usage, sharingMode = VkSharingMode.Exclusive };
        VkBuffer buffer; Check(_api.vkCreateBuffer(&info, &buffer), "vkCreateBuffer");
        VkMemoryRequirements reqs; _api.vkGetBufferMemoryRequirements(buffer, &reqs);
        VkDeviceMemory memory = Allocate(reqs, props);
        Check(_api.vkBindBufferMemory(buffer, memory, 0), "vkBindBufferMemory");
        return (buffer, memory);
    }

    private VkDeviceMemory Allocate(VkMemoryRequirements reqs, VkMemoryPropertyFlags required)
    {
        VkPhysicalDeviceMemoryProperties memProps;
        _ctx.InstanceApi.vkGetPhysicalDeviceMemoryProperties(_ctx.PhysicalDevice, &memProps);
        uint typeIndex = uint.MaxValue;
        for (uint i = 0; i < memProps.memoryTypeCount; i++)
            if ((reqs.memoryTypeBits & (1u << (int)i)) != 0 && (memProps.memoryTypes[(int)i].propertyFlags & required) == required) { typeIndex = i; break; }
        if (typeIndex == uint.MaxValue) throw new InvalidOperationException($"[VkRenderer] No memory type for {required}.");
        var allocInfo = new VkMemoryAllocateInfo { allocationSize = reqs.size, memoryTypeIndex = typeIndex };
        VkDeviceMemory memory; Check(_api.vkAllocateMemory(&allocInfo, &memory), "vkAllocateMemory");
        return memory;
    }

    private static void Check(VkResult result, string what)
    {
        if (result != VkResult.Success) throw new InvalidOperationException($"[VkRenderer] {what} -> {result}");
    }

    public void Dispose()
    {
        WaitForPendingFrames();
        _api.vkDeviceWaitIdle();
        DestroyTargets();
        _api.vkDestroyImageView(_envCubeView);
        foreach (var v in _texViews) _api.vkDestroyImageView(v);
        _api.vkDestroySampler(_samplerPoint);
        _api.vkDestroySampler(_samplerLinear);
        _api.vkDestroyPipeline(_pipelineOpaque);
        _api.vkDestroyPipeline(_pipelineAccum);
        _api.vkDestroyPipeline(_pipelineResolve);
        _api.vkDestroyFence(_pickFence);
        _api.vkDestroyFramebuffer(_fbPick);
        _api.vkDestroyImageView(_pickDepthView); _api.vkDestroyImage(_pickDepthImage); _api.vkFreeMemory(_pickDepthMemory);
        _api.vkDestroyImageView(_pickView); _api.vkDestroyImage(_pickImage); _api.vkFreeMemory(_pickMemory);
        _api.vkDestroyBuffer(_pickReadback); _api.vkFreeMemory(_pickReadbackMemory);
        _api.vkDestroyRenderPass(_rpPick);
        _api.vkDestroyPipelineLayout(_pickLayout);
        _api.vkDestroyPipeline(_pipelinePickVolume);
        _api.vkDestroyPipeline(_pipelinePickBillboard);
        _api.vkDestroyPipeline(_pipelinePick);
        _api.vkDestroyPipeline(_pipelineDebugLines);
        if (_debugLineBuffer.Handle != 0) { _api.vkDestroyBuffer(_debugLineBuffer); _api.vkFreeMemory(_debugLineMemory); }
        _api.vkDestroyPipeline(_pipelineBillboard);
        _api.vkDestroyPipeline(_pipelineOutlineRim);
        _api.vkDestroyPipeline(_pipelineOutlineMask);
        _api.vkDestroyPipeline(_volumePipeline);
        _api.vkDestroyPipeline(_pipelineSoftEdgeDepth);
        _api.vkDestroyPipeline(_pipelineAdditive);
        _api.vkDestroyPipelineLayout(_layout);
        _api.vkDestroyPipelineLayout(_resolveLayout);
        _api.vkDestroyPipelineLayout(_outlineLayout);
        _api.vkDestroyPipelineLayout(_volumeLayout);
        _api.vkDestroyShaderModule(_vs);
        _api.vkDestroyShaderModule(_fsOpaque);
        _api.vkDestroyShaderModule(_fsAccum);
        _api.vkDestroyShaderModule(_fsAdditive);
        _api.vkDestroyShaderModule(_resolveVs);
        _api.vkDestroyShaderModule(_resolveFs);
        _api.vkDestroyShaderModule(_volumeVs);
        _api.vkDestroyShaderModule(_volumeFs);
        _api.vkDestroyShaderModule(_debugLineVs);
        _api.vkDestroyShaderModule(_debugLineFs);
        _api.vkDestroyShaderModule(_pickVs);
        _api.vkDestroyShaderModule(_pickVolumeVs);
        _api.vkDestroyShaderModule(_pickBillboardVs);
        _api.vkDestroyShaderModule(_pickFs);
        _api.vkDestroyShaderModule(_billboardVs);
        _api.vkDestroyShaderModule(_billboardFs);
        _api.vkDestroyShaderModule(_outlineVs);
        _api.vkDestroyShaderModule(_outlineFs);
        _api.vkDestroyBuffer(_edgeVertexBuffer); _api.vkFreeMemory(_edgeVbMemory);
        _api.vkDestroyBuffer(_edgeIndexBuffer); _api.vkFreeMemory(_edgeIbMemory);
        _api.vkDestroyRenderPass(_rpOpaque);
        _api.vkDestroyRenderPass(_rpAccum);
        _api.vkDestroyRenderPass(_rpResolve);
        _api.vkDestroyDescriptorPool(_descPool);
        if (_ownsEnvCube) _envCube.Dispose();
        _api.vkDestroyDescriptorSetLayout(_matSetLayout);
        _api.vkDestroyDescriptorSetLayout(_descLayout);
        _api.vkDestroyDescriptorSetLayout(_resolveSetLayout);
        _api.vkDestroyBuffer(_transformBuffer); _api.vkFreeMemory(_tbMemory);
        _api.vkDestroyBuffer(_bonePaletteBuffer); _api.vkFreeMemory(_bpMemory);
        _api.vkDestroyBuffer(_boneBaseBuffer); _api.vkFreeMemory(_bbMemory);
        for (int f = 0; f < Frames; f++)
        {
            _api.vkDestroyBuffer(_lightBuffers[f]); _api.vkFreeMemory(_lightMemories[f]);
            _api.vkDestroyBuffer(_uniformBuffers[f]); _api.vkFreeMemory(_ubMemories[f]);
        }
        _api.vkDestroyBuffer(_indexBuffer); _api.vkFreeMemory(_ibMemory);
        _api.vkDestroyBuffer(_vertexBuffer); _api.vkFreeMemory(_vbMemory);
        for (int f = 0; f < Frames; f++) _api.vkDestroyFence(_fences[f]);
        _api.vkDestroyCommandPool(_pool);
    }
}
