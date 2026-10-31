using NeoVeldrid;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// The fragment shader every lit draw in the editor runs, for BOTH engines, in one piece.
///
/// It used to be two. The generic shader lived in VulkanRenderer as LitFragCommon, and
/// VulkanRenderer.OldEngineShading rewrote nine passages of it with string Replace at type-init
/// time, then cached the compiled result under the ORIGINAL source's key so the renderer was
/// handed the patched shader without knowing. Reading what the editor actually draws meant
/// applying the nine replacements by hand, and every proof comment about old- or new-engine shading
/// was attached to a passage that did not exist in the file that declared it.
///
/// What that cost is recorded, honestly, in the file that is now gone: the cubemap exposure and
/// reflection replacements were written and GUARDED but never listed in the Replace chain, so from
/// the day they were added until 2026-09-21 they compiled into nothing - the renderer kept the
/// generic exposure decode and the generic reflection fill for both engines, while every note said
/// the retail ones were "applied". A guard that throws when the text it is about to replace has
/// drifted cannot catch a replacement nobody performs.
///
/// The fold was mechanical: the same nine strings in the same order, and the result is character
/// for character what the patch produced, so nothing about what the GPU runs changed here. What
/// changes is that the engine is now an ARGUMENT to one shader - uOldEngineMode and uMat2.w select
/// the branch - which is what the two sources were simulating all along.
///
/// Reading guide. Three things differ per engine and each says so at the branch:
///  * the baked term - old engine's unresolved LM1/LM2 presentation fallback vs the new engine's
///    decoded Radiosity Normal Mapping (Docs/reverse/NEW_ENGINE_TIE_SHADING_2026-09-19.md);
///  * the cubemap exposure and the reflection composition;
///  * whether Mobys reach the cube at all - ToD's never do, QForce's always do.
///
/// Evidence boundary, unchanged by the fold. ToD has no single directional-lightmap equation: the
/// retail Tie shell routes callback52.arg2.low8 (shellFeatureKey) through Full/Reduced shader
/// tables, the source use bytes are wired per draw but the terminal key is a separate unresolved
/// value, and runtime V4 proved use=0x0C can route with key=0x04. LM1/0x5400 and LM2/0x5410 are
/// proven INPUTS; their final combination is permutation-dependent. So the old-engine baked branch
/// is ReLunacy's original directional reconstruction, kept because it restores authored local
/// light/shadow variation, and labelled NON-RETAIL at the branch. Its input domain is not a
/// fallback: the native ADDRESS BIASED/BX2 expansion on 0x5410 is proven state and is applied from
/// the decoded descriptor regardless of which equation consumes the vector.
///
/// Native 0x8B00 environment lighting, per-Moby sampled spatial lighting and material emissive are
/// source-driven fidelity, not optional graphics features.
/// </summary>
public sealed unsafe partial class VulkanRenderer
{
    // Shared lit shading: everything except the final output. Each of the three fragment shaders
    // below appends its own main() and output declarations to this.
    // The LightBuffer (LightData) and the new-engine atmosphere, shared by the lit shaders and the sky.
    private const string LightBufferGlsl = @"layout(set = 0, binding = 2, std140) uniform LightBuffer {
    vec3 uLightDirection; float uAmbient;
    vec3 uLightColor; float uSpecularPower;
    vec3 uCameraPosition; float uReflectionDebug;
    vec3 uEnvironmentColour; float uEnvironmentIntensity;
    vec2 uLightmapUVScale; vec2 uLightmapUVOffset;
    float uBakedLightScale; float uBakedBumpFade; float uBakedDebugView; float uReflectionBase;
    vec2 uLightmapUVPivot; float uLightmapUVRotation; float uBakedAmbient;
    vec3 uEnvDirection0; float uEnvHasLighting;
    vec3 uEnvDirection1; float uOldEngineMode;
    vec3 uEnvAmbient; float _oldEngineReserved0;
    vec3 uEnvLight0Colour; float _oldEngineReserved1;
    vec3 uEnvLight1Colour; float _oldEngineReserved2;
    vec3 uFogColour; float uFogScale;
    float uFogBias; float uFogAmount; float uFogEnabled; float uFogDebugView;
    float uNewEngineLodScale; float uReflOcclusionFloor; float uReflOcclusionFullLuminance; float uSceneTime;
    vec4 uPostParams;
    vec4 uNewEngineFog; uvec4 uNewEngineFogTable[128];
    vec4 uAtmColour; vec4 uAtmExtinction; vec4 uAtmDirection; vec4 uAtmPhase; uvec4 uInscatterTable[128];
};
";

    // QForce's atmosphere pass (EBOOT 0x006FA0D8 mode 2, fragment shaders 0x87 + k of 0x00F10100, groups
    // 0..4 = the view block 0x007181B8 fills): at the pixel's distance d and t = saturate(d / far),
    //   c += c L3(t) (1 - c)                 0x34003, linear, when the table is on (k bit 0)
    //   c = mix(c, F.rgb, F.a)               F = 0x34004 decoded by the texture's gamma, when on (bit 2)
    //   c += S x (2g cos + g + 1.001)^-1.5 x (1 - exp2(-d B 1.44294))   when scattering is on (bit 1)
    // with cos between the view ray and the scattering direction. The game draws it over the opaque scene
    // and the sky before the blended passes; the editor applies it to the same pixels where they are shaded.
    // The pass's overlay (unit 4 at TEX2) is not bound by the pass itself: taken neutral here (OPEN).
    private const string NewEngineAtmosphereGlsl = @"
vec4 newEngineTableEntry(uint v) {
    return vec4(float((v >> 16) & 0xFFu), float((v >> 8) & 0xFFu), float(v & 0xFFu), float(v >> 24)) / 255.0;
}
vec4 newEngineFogTexel(int i) { return pow(newEngineTableEntry(uNewEngineFogTable[i >> 2][i & 3]), vec4(2.2)); }
vec4 newEngineInscatterTexel(int i) { return newEngineTableEntry(uInscatterTable[i >> 2][i & 3]); }
// A 512 x 1 texture sampled linearly with clamp-to-edge, as the RSX units 2 / 3 are set (0x007186C0,
// 0x0071853C): texel centres at (i + 0.5) / 512, the gamma decode per texel before the filter.
vec4 newEngineAtmosphereSample(bool fog, float t) {
    float x = clamp(t, 0.0, 1.0) * 512.0 - 0.5;
    float fl = floor(x);
    int i0 = clamp(int(fl), 0, 511), i1 = clamp(int(fl) + 1, 0, 511);
    float f = x - fl;
    return fog ? mix(newEngineFogTexel(i0), newEngineFogTexel(i1), f)
               : mix(newEngineInscatterTexel(i0), newEngineInscatterTexel(i1), f);
}
bool newEngineAtmosphereOn() { return uNewEngineFog.y > 0.5 || uAtmExtinction.w > 0.5 || uAtmColour.w > 0.5; }
// c: the linear colour; d: distance from the camera; dir: the view ray (world space, any length).
vec3 newEngineAtmosphere(vec3 c, float d, vec3 dir) {
    float t = clamp(d * uAtmPhase.w, 0.0, 1.0);
    if (uAtmExtinction.w > 0.5) {
        vec3 l = c * newEngineAtmosphereSample(false, t).rgb;
        c = c - c * l + l;
    }
    if (uNewEngineFog.y > 0.5) {
        vec4 f = newEngineAtmosphereSample(true, t);
        c = c + f.a * (f.rgb - c);
    }
    if (uAtmColour.w > 0.5) {
        float cosine = dot(normalize(dir), uAtmDirection.xyz);
        float x = max(clamp(cosine * uAtmPhase.z + uAtmPhase.y, 0.0, 1.0), 1e-6);
        vec3 transmission = exp2(-d * uAtmExtinction.xyz * 1.44294);
        c = pow(x, -1.5) * uAtmColour.rgb * (1.0 - transmission) + c;
    }
    return c;
}
";

    // Shared lit shading: everything except the final output. Each of the three fragment shaders
    // below appends its own main() and output declarations to this.
    private const string LitFragCommon = @"#version 450
" + LightBufferGlsl + NewEngineAtmosphereGlsl + @"
// True in the opaque pass's shader: the game draws those pixels before its atmosphere pass, so they
// take the atmosphere (VulkanRenderer.NewEngineFog.cs) instead of the per-draw fog constant.
bool gBeforeAtmosphere = false;
// What the opaque pass writes to alpha: 1, or with the bloom on (uPostParams.x) the glow mask the
// game's shaders output and 0 for everything else (VulkanRenderer.Bloom.cs).
float gGlow = 1.0;
layout(set = 0, binding = 3) uniform samplerCube uEnvCube;
// New engine: the other three cubes of the level's environment resource (R16G16B16A16_Float, retail
// channels). The old engine binds its single cube to all four slots.
layout(set = 0, binding = 5) uniform samplerCube uEnvCube1;
layout(set = 0, binding = 6) uniform samplerCube uEnvCube2;
layout(set = 0, binding = 7) uniform samplerCube uEnvCube3;
// The opaque scene's colour (sky + opaque geometry), copied before the additive and blended draws:
// what the retail shaders that refract or composite read on RSX units 10 / 11.
layout(set = 0, binding = 10) uniform sampler2D uSceneColor;
struct InstanceLight {
    vec4 ambient;
    vec4 light0Colour;
    vec4 light1Colour;
    vec4 direction0;
    vec4 direction1;
    // PROVEN source metadata, not a resolved retail route.
    // use0 = serialized use, material feature mask, effective use, raw +0x2F.
    vec4 tieShaderUse0;
    // use1 = serialized/effective render mode and serialized/effective render flags.
    vec4 tieShaderUse1;
    // meta.xy = IsOldEngine, SourceHadExplicitUse. meta.zw stay zero until terminal routing is proven.
    vec4 tieShaderUseMeta;
    // New engine: fragment group 19 for this draw (VkInstanceLight.Gameplay); zero = none.
    vec4 gameplay;
    vec4 secondTint;
    vec4 secondParallax;
    vec4 uvAnim;
    vec4 uvScroll;
    // New engine: the draw's centre the fog is measured from (w = 1 when fogged).
    vec4 fogCentre;
    // x = 1: a two-layer draw (VkInstanceLight.Layers).
    vec4 layers;
};
layout(set = 0, binding = 4, std430) readonly buffer InstanceLights { InstanceLight values[]; } uInstanceLights;
layout(set = 1, binding = 0) uniform sampler2D uAlbedo;
layout(set = 1, binding = 1) uniform sampler2D uNormal;
layout(set = 1, binding = 2) uniform sampler2D uProps;
layout(set = 1, binding = 3) uniform sampler2D uLightColour;
layout(set = 1, binding = 4) uniform sampler2D uLightDir;
// Retail new-engine routing: material slot 3 is detail, slots 4/5 bind RSX units 8/9.
// Aux A/B remain shader-family specific; binding them does not invent one generic combine rule.
layout(set = 1, binding = 5) uniform sampler2D uDetail;
layout(set = 1, binding = 6) uniform sampler2D uAuxA;
layout(set = 1, binding = 7) uniform sampler2D uAuxB;
// Two-layer Ties: the second material and the blend mask (RSX units 8..12 of their shaders).
layout(set = 1, binding = 8) uniform sampler2D uAlbedo2;
layout(set = 1, binding = 9) uniform sampler2D uNormal2;
layout(set = 1, binding = 10) uniform sampler2D uProps2;
layout(set = 1, binding = 11) uniform sampler2D uDetail2;
layout(set = 1, binding = 12) uniform sampler2D uBlendMask;
layout(push_constant) uniform PC { vec4 uMat0; vec4 uMat1; vec4 uMat2; vec4 uMat3; vec4 uMat4; vec4 uMat5; vec4 uMat6; vec4 uMat7; }; // uMat0=(hasBaked,pScale,pBias,alphaThr); uMat1=(class-0x13 embedded shader, NewEngineEmbeddedShaders; vtxAlpha,lit,albedoHasAlpha); uMat2=(lightDirBx2,0,0,0) native RSX ADDRESS conversion state; uMat3=reflection tint (old +0x40 / new +0x50); uMat4=new-engine +0x60 reflection parameters; uMat5.x=new-engine +0x40 environment cube slot; uMat5.yz=new-engine +0x30 normal-map / +0x34 reflection strength; uMat5.w=new-engine normal-map permutation (+0x20 bit 0x08); uMat6=new-engine (detail tiling, detail gain +0x3C, detail present, material +0x20 feature bits); uMat7=new-engine Moby tint (rgb, applies)
layout(location = 0) in vec2 fUV;
layout(location = 1) in vec3 fWorldNormal;
layout(location = 2) in vec3 fWorldTangent;
layout(location = 3) in float fHandedness;
layout(location = 4) in vec3 fWorldPos;
layout(location = 5) in vec2 fUV2;
layout(location = 6) in vec4 fColor;
layout(location = 7) flat in uint fInstanceIndex;
layout(location = 8) in vec3 fPrelightChroma;
layout(location = 9) in vec4 fPrelightIntensity;
// New engine: the fog constant K of this draw, as the game patches it into fragment group 0 (QForce
// EBOOT 0x00718030): region.dat 0x34004 at t = distance(centre, camera) / far x 512, the two nearest
// ARGB entries lerped, then pow 2.2 on all four channels. (0) when off: mix(c, K.rgb, 0) = c.
vec4 newEngineFogEntry(int i) {
    uint v = uNewEngineFogTable[i >> 2][i & 3];
    return vec4(float((v >> 16) & 0xFFu), float((v >> 8) & 0xFFu), float(v & 0xFFu), float(v >> 24)) / 255.0;
}
vec4 newEngineFog() {
    vec4 centre = uInstanceLights.values[fInstanceIndex].fogCentre;
    if (uNewEngineFog.y < 0.5 || centre.w < 0.5) return vec4(0.0);
    float t = clamp(distance(centre.xyz, uCameraPosition) * uNewEngineFog.x, 0.0, 1.0) * 512.0;
    int i0 = min(int(t), 511);
    vec4 k = mix(newEngineFogEntry(i0), newEngineFogEntry(min(i0 + 1, 511)), t - floor(t));
    return pow(k, vec4(2.2));
}
// S, the retail shaders' tex5(WPOS).z (1 lit, 0 in the sun shadow of the world or of a moving
// Moby). The game's producer of that mask is not reconstructed, so every pixel is fully lit.
float sunShadowTerm() { return 1.0; }
void shade(out vec3 litColor, out float litAlpha) {
    vec3 n = normalize(fWorldNormal);
    vec3 t = normalize(fWorldTangent - n * dot(fWorldTangent, n));
    vec3 b = cross(n, t) * fHandedness;
    mat3 tbn = mat3(t, b, n);
    vec3 viewDir = normalize(uCameraPosition - fWorldPos);
    vec3 viewDirTS = transpose(tbn) * viewDir;
    InstanceLight instanceLight = uInstanceLights.values[fInstanceIndex];
    // New-engine distance fades (VkInstanceLight.ApplyNewEngineFades: tieShaderUse1 = 1/d, from the
    // Tie primitive's +0x30 or the UFrag record's +0x60). The retail VPs build c[19] = K / d with
    // K = (-2.5, -2.5, 1, 2.5) * the camera LOD scale (uNewEngineLodScale) and the view depth (clip w):
    //   normal-map strength  *= saturate(2.5 * (1 - depth / d0))
    //   reflection strength  *= saturate(2.5 * (1 - depth / d1))
    //   detail weight         = saturate(+0x3C * (1 - saturate(depth / d2)))
    // and past d0 the draw-list builder switches to the permutation without normal map and parallax,
    // which is also the flat-bake one (QForce EBOOT 0x0073E1DC).
    float viewDepth = (uNewEngineLodScale > 0.0 ? uNewEngineLodScale : 1.0) / gl_FragCoord.w;
    bool hasFades = uOldEngineMode < 0.5 && instanceLight.tieShaderUse1.w > 0.5;
    float normalFade = hasFades ? clamp(2.5 - 2.5 * viewDepth * instanceLight.tieShaderUse1.x, 0.0, 1.0) : 1.0;
    float reflectionFade = hasFades ? clamp(2.5 - 2.5 * viewDepth * instanceLight.tieShaderUse1.y, 0.0, 1.0) : 1.0;
    float detailFade = hasFades ? clamp(viewDepth * instanceLight.tieShaderUse1.z, 0.0, 1.0) : 0.0;
    bool farPermutation = hasFades && viewDepth * instanceLight.tieShaderUse1.x > 1.0;
    // The permutation bits the game draws with (QForce EBOOT 0x006AE85C / 0x006B7380): the material's
    // +0x20 (uMat6.w) AND the Tie primitive's +0x20 / UFrag record's +0x54 (tieShaderUse0.w - 1).
    // Only shaders of bit 0x04 sample the reflection cube, only 0x08 the normal map, only 0x10 the
    // detail map, and 0x01 is parallax. No mask (Mobys, old engine): nothing is gated.
    bool hasPermutationMask = uOldEngineMode < 0.5 && instanceLight.tieShaderUse0.w > 0.5;
    int permutationBits = hasPermutationMask
        ? int(uMat6.w + 0.5) & (int(instanceLight.tieShaderUse0.w + 0.5) - 1)
        : 0x1F;
    bool allowParallax = (permutationBits & 0x01) != 0;
    bool allowSpecular = (permutationBits & 0x04) != 0;
    bool allowNormalMap = (permutationBits & 0x08) != 0;
    bool allowDetail = (permutationBits & 0x10) != 0;
    float height = (farPermutation || !allowParallax) ? 0.0 : texture(uProps, fUV).g * uMat0.y + uMat0.z;
    vec2 uv = fUV + viewDirTS.xy * height;
    vec4 albedoTex = texture(uAlbedo, uv);
    // Two-layer UFrags and Ties, as the game's two-layer shaders do it (ToD FP aa00291718be, QForce's
    // standard two-layer family): w = vertex weight x mask(lightmap uv).x, then albedo, normal and
    // properties mixed by w, the second material at the same UV. ToD's weight is 127/128 on every
    // retail vertex (Docs/reverse/tod_catchup_2026-10-10/02_textures_manquantes.md); the new engine's
    // is the vertex alpha the loader filled with |pos.w| & 0x7F / 128.
    float layerWeight = 0.0;
    if (instanceLight.layers.x > 0.5) {
        float vertexWeight = uOldEngineMode > 0.5 ? 127.0 / 128.0 : fColor.a;
        layerWeight = clamp(vertexWeight * texture(uBlendMask, fUV2).x, 0.0, 1.0);
        albedoTex = mix(albedoTex, texture(uAlbedo2, uv), layerWeight);
    }
    // Alpha test, GEQUAL against the mode's hardcoded reference (uMat0.w; 0 = no test). Cutout uses
    // 128/255, the blended paths 4/255 - see dev/chatgpt-eboot-{2,3,5}.txt. Sourced like litAlpha below.
    float testAlpha = uMat1.y > 0.5 ? (uMat1.w > 0.5 ? albedoTex.a * fColor.a : fColor.a) : albedoTex.a;
    if (uMat0.w > 0.0 && testAlpha < uMat0.w) discard;
    vec4 nrmSample = texture(uNormal, uv);
    if (layerWeight > 0.0) nrmSample = mix(nrmSample, texture(uNormal2, uv), layerWeight);
    // TextureUploadPreparer already applies NV4097_SET_TEXTURE_CONTROL1. The normal derivative
    // pair is therefore the LOGICAL R/G pair for both DXT5 (0x6AE0) and G8B8 (0x6AF0).
    vec2 derivativeRaw = allowNormalMap ? nrmSample.rg * 2.0 - 1.0 : vec2(0.0);
    vec4 props = texture(uProps, uv);
    if (layerWeight > 0.0) props = mix(props, texture(uProps2, uv), layerWeight);
    // New-engine detail map, as the retail shaders apply it (FP c8872868fd5f / cbf6dd28f655 paired
    // with Tie VPs 49 / 50; Docs/reverse/NEW_ENGINE_SHADER_SECTIONS_EBOOT_2026-09-27.md):
    //   d = tex3(uv * c[22].x), sampled signed (every retail detail descriptor has ADDRESS
    //       unsigned_remap = BIASED), weighted by m = saturate(+0x3C * (1 - fade)) * props.a;
    //   normal-map permutations: n.xy -= d.xy * m * (1 - |n.xy|^2); albedo *= 1 + d.z * m;
    //   specular *= 1 + d.w * m.
    // The fade is detailFade above (0 when the draw carries no fade distances).
    vec4 detail = vec4(0.0);
    if (uOldEngineMode < 0.5 && uMat6.z > 0.5 && allowDetail)
        detail = (texture(uDetail, uv * uMat6.x) * 2.0 - 1.0) * (clamp(uMat6.y * (1.0 - detailFade), 0.0, 1.0) * props.a);
    bool normalPermutation = uMat5.w > 0.5 && !farPermutation && allowNormalMap;
    // PROVEN (QForce EBOOT + retail shaders): the new engine scales the normal map's xy by material
    // +0x30 (c[21].x, faded into TEX3.w) and keeps z = 1 - |xy|^2 from the unscaled sample.
    // Old-engine Tie: TEX2.w = saturate(clip w x c16.x + c16.y), the VP's distance fade (TieFrag +0x30 /
    // +0x34, tieShaderUseMeta.zw), scales the normal map's derivatives and the cube reflection.
    bool oldFadeKnown = uOldEngineMode > 0.5 && instanceLight.tieShaderUseMeta.x > 0.5
        && (instanceLight.tieShaderUseMeta.z != 0.0 || instanceLight.tieShaderUseMeta.w != 0.0);
    float oldDistanceFade = oldFadeKnown
        ? clamp((1.0 / gl_FragCoord.w) * instanceLight.tieShaderUseMeta.z + instanceLight.tieShaderUseMeta.w, 0.0, 1.0)
        : 1.0;
    vec2 derivativeSum = derivativeRaw * (uOldEngineMode > 0.5 ? oldDistanceFade : uMat5.y * normalFade);
    if (normalPermutation) derivativeSum -= detail.xy * (1.0 - dot(derivativeRaw, derivativeRaw));
    vec3 worldNormal = normalize(tbn * normalize(vec3(derivativeSum, 1.0)));
    float specIntensity = props.r * (1.0 + detail.w);
    float emissive = props.b;
    // PROVEN (retail Moby, Tie and prelit-Tie shaders): the sun term is multiplied by S, and all the
    // non-emissive light by 0.2 S + 0.8 (below, where lighting is summed). S is 1 on the old engine.
    float sunS = sunShadowTerm();
    // Normal game geometry carries white RGB here. Collision debug batches use the same attribute
    // for stable category/material/flag colours, so multiplying it is backwards-compatible while
    // keeping colour selection out of the proprietary material fields.
    // Retail old-engine 0x5200 descriptors measured on Kerchu, Apogee and Level Transitions
    // all have ADDRESS.gamma = 0. Do arithmetic on the raw UNORM sample; do not manufacture an
    // sRGB decode in the shader when the RSX descriptor did not request one.
    // Moby albedo tint: closed below (uMat7). The skinned main-pass VPs (198+) never read c[20], pass
    // 0x2B (modes 0/4/5) uploads c[20] = 1, and only the mode 1/2/6 Moby passes tint (QForce EBOOT).
    vec3 albedoSample = uMat2.y > 0.5 ? pow(max(albedoTex.rgb, vec3(0.0)), vec3(2.2)) : albedoTex.rgb;
    vec3 albedo = albedoSample * fColor.rgb * (1.0 + detail.z);
    // New-engine Moby tint (AssetManager.GetVkMobyTint): only the Moby passes of render modes 1, 2 and
    // 6 upload c[20] = material +0x24 * instance colour, which VPs 166-181 hand the FP as the albedo
    // factor; modes 0/4/5 draw with c[20] = 1.
    if (uMat7.w > 0.5 && step(2.5, instanceLight.direction0.w) * step(instanceLight.direction0.w, 3.5) > 0.5)
        albedo *= uMat7.rgb;
    // PROVEN (retail Tie VP 64fed2abefb9 paired with the analytic-light FP 5a90cc1a17e6): unbaked Ties
    // on the analytic route use the same hemispheric ambient as Mobys, Amb * (1 + 0.5 * n.y).
    vec3 globalEnvDiffuse = uEnvAmbient * (uOldEngineMode > 0.5 ? 1.0 + 0.5 * worldNormal.y : 1.0)
        + uEnvLight0Colour * max(dot(worldNormal, uEnvDirection0), 0.0) * sunS
        + uEnvLight1Colour * max(dot(worldNormal, uEnvDirection1), 0.0);
    // Direction0.w: 1 = old-engine Moby draw, 3 = new-engine Moby draw (VkInstanceLight.ForMoby),
    // 2 = old Tie with a retail baked scale. Both Moby kinds take the hemispheric ambient below; only
    // the old one is kept away from the environment cube.
    float isOldMoby = step(0.5, instanceLight.direction0.w) * step(instanceLight.direction0.w, 1.5);
    float isNewMoby = step(2.5, instanceLight.direction0.w) * step(instanceLight.direction0.w, 3.5);
    float isMoby = max(isOldMoby, isNewMoby);
    // PROVEN (retail Moby FPs paired with the skinned Moby VPs): the ambient term is hemispheric,
    // ambient * (1 + 0.5 * n.y), before the two directional terms and the props.b emissive add.
    vec3 instanceEnvDiffuse = instanceLight.ambient.xyz * (1.0 + 0.5 * worldNormal.y)
        + instanceLight.light0Colour.xyz * max(dot(worldNormal, instanceLight.direction0.xyz), 0.0) * sunS
        + instanceLight.light1Colour.xyz * max(dot(worldNormal, instanceLight.direction1.xyz), 0.0);
    float useInstanceLighting = instanceLight.ambient.w * uEnvHasLighting;
    vec3 envDiffuse = mix(globalEnvDiffuse, instanceEnvDiffuse, useInstanceLighting);
    vec3 undecodedFill = mix(vec3(uAmbient), envDiffuse, uEnvHasLighting);
    vec2 uvCentred = fUV2 - uLightmapUVPivot;
    float sr = sin(radians(uLightmapUVRotation));
    float cr = cos(radians(uLightmapUVRotation));
    vec2 uvRot = vec2(uvCentred.x * cr - uvCentred.y * sr, uvCentred.x * sr + uvCentred.y * cr) + uLightmapUVPivot;
    vec2 bakedUV = uvRot * uLightmapUVScale + uLightmapUVOffset;
    vec4 bakedColour = texture(uLightColour, bakedUV);
    vec4 bakedDirSample = texture(uLightDir, bakedUV);
    float hasBaked = uMat0.x;
    vec3 bakedDiffuseLight;
    if (uOldEngineMode > 0.5) {
        // Source-authored per-draw state is physically present here:
        // instanceLight.tieShaderUse0 / tieShaderUse1 / tieShaderUseMeta.
        // Do NOT alias tieShaderUse0.z (EffectiveUseMask) to callback52.arg2.low8: runtime V4 proved
        // use=0x0C can route with key=0x04.
        //
        // Until the real terminal key is resolved, preserve useful editor presentation by matching
        // VELD's original new-renderer directional-lightmap fallback. This branch is deliberately
        // NOT labelled retail fidelity.
        //
        // TextureUploadPreparer applies CONTROL1, so no legacy R/B/G swizzle is needed at this
        // stage. CONTROL1 is NOT the whole native path: ADDRESS.UNSIGNED_REMAP=BIASED is a separate
        // hardware stage that expands the logical sample from [0,1] to [-1,1], and the baked-sampler
        // reverse shows 0x5410 setting it on every audited descriptor (ADDRESS=0x00031303) while
        // 0x5400 does not (0x00030303). The expansion is deliberately not baked into the uploaded
        // RGBA8, so it has to happen here. Without it every LM2 vector stays in the +++ octant: a
        // neutral 0.5 texel, which should decode to a null direction, normalises to (.577,.577,.577)
        // and fakes a 45-degree light, so NdotL is positive almost everywhere and the authored
        // directional variation collapses into flat, over-bright baked lighting. uMat2.x is decoded
        // per material from the real descriptor by RsxTextureFormatConversion - it is never assumed.
        vec3 bakedLightDirTS = uMat2.x > 0.5 ? bakedDirSample.rgb * 2.0 - 1.0 : bakedDirSample.rgb;
        vec3 bakedNormalTS = normalize(vec3(derivativeSum * uBakedBumpFade, 1.0));
        // Old Tie draws carrying a retail per-instance scale (Direction0.w = 2) use the decoded retail
        // Full[0x28] arithmetic: LM2 NOT normalized, NdotL = saturate(dot(N, LM2)), divide by LM2.z when
        // positive, scale by c[4].z (TieInstance +0x60 candidate). Every other baked draw keeps the
        // VELD presentation fallback, whose fixed 4.0 scale made lightmapped Ties about 3x brighter
        // than their retail median scale (1.31) and produced the uneven contrast.
        bool retailTieScale = instanceLight.direction0.w > 1.5;
        vec3 fallbackDir = normalize(bakedLightDirTS + vec3(0.0, 0.0, 1e-6));
        vec3 lm2 = retailTieScale ? bakedLightDirTS : fallbackDir;
        float bakedNdotL = clamp(dot(lm2, bakedNormalTS), 0.0, 1.0);
        float bakedDiffuse = lm2.z > 0.0 ? bakedNdotL / lm2.z : bakedNdotL;
        // PROVEN (runtime census, Tools/Reverse/evidence/TOD_TIE_Route_Key_2026-09-18.md): the shell key is the
        // runtime TieFrag+0x2C use byte, so use bit 0x08 selects the directional LM2 shaders (Full[0x28]/[0x2C]/[0x2D])
        // and its absence the LM1-only ones (Full[0x20]/[0x24]): light = LM1 * c[4].z with NO N.L and no LM2.
        float retailDirectional = mod(floor(instanceLight.tieShaderUse0.z / 8.0), 2.0);
        if (retailTieScale && instanceLight.tieShaderUseMeta.y > 0.5 && retailDirectional < 0.5) bakedDiffuse = 1.0;
        float bakedScale = retailTieScale ? instanceLight.direction1.w : uBakedLightScale;
        bakedDiffuseLight = bakedColour.rgb * bakedDiffuse * bakedScale;
    } else if (uMat2.w > 0.5) {
        // New engine (QForce / All 4 One), decoded from the retail Tie fragment shaders in the
        // RPCS3 shader cache (e.g. FP 352970cec940 / 03039ca17211, paired with the Tie VPs that route
        // attribute 4 to TEX0.zw):
        //   0x5410 is NOT a direction. It holds three Radiosity Normal Mapping weights, one per
        //   Half-Life-2 basis vector: R <-> (-0.408, 0.707, 0.577), G <-> (-0.408, -0.707, 0.577),
        //   B <-> (0.816, 0, 0.577). indirect = LM.rgb * dot(saturate(N.b_i)^2, weights).
        //   LM.a is the baked sun visibility: sun = sunColour * LM.a * saturate(N.sunDir).
        //   The tangent normal is (x, y, 1 - x*x - y*y) normalised, x/y from the normal map.
        // The screen-space shadow S multiplies the sun and, through 0.2 S + 0.8, the whole light
        // (sunShadowTerm); the deferred light buffer (tex6) is empty in a static scene. The sun comes
        // from the level's lighting environment.
        vec3 lm = uMat2.z > 0.5 ? pow(max(bakedColour.rgb, vec3(0.0)), vec3(2.2)) : bakedColour.rgb;
        vec2 nxy = derivativeSum * uBakedBumpFade;
        vec3 rnmN = normalize(vec3(nxy, 1.0 - dot(derivativeRaw, derivativeRaw)));
        vec3 rnmW = vec3(
            clamp(dot(rnmN, vec3(-0.408203, 0.707031, 0.577148)), 0.0, 1.0),
            clamp(dot(rnmN, vec3(-0.408203, -0.707031, 0.577148)), 0.0, 1.0),
            clamp(dot(rnmN, vec3(0.816406, 0.0, 0.577148)), 0.0, 1.0));
        // PROVEN (QForce EBOOT 0x007473F4 / 0x00753EB0 + the VP listings): only the normal-map
        // permutations (+0x20 bit 0x08) are RNM; the others draw the flat bake, LM.rgb * c[4].y, and
        // do not sample the weights map at all (FP c8872868fd5f binds no unit 14).
        bool rnmRoute = normalPermutation;
        float rnm = rnmRoute ? dot(rnmW * rnmW, bakedDirSample.rgb) : 1.0;
        vec3 sun = uEnvLight0Colour * bakedColour.a * clamp(dot(worldNormal, uEnvDirection0), 0.0, 1.0) * sunS;
        if (uMat2.x > 0.5) {
            // A Crack in Time: a BIASED tangent-space direction and the sun baked into the colour (see
            // rsxTex14): light = LM x sat(N.D) / D.z, the Tools of Destruction form.
            vec3 d = bakedDirSample.rgb * 2.0 - 1.0;
            float nd = clamp(dot(rnmN, d), 0.0, 1.0);
            rnm = rnmRoute ? (d.z > 1.0e-3 ? nd / d.z : nd) : 1.0;
            sun = vec3(0.0);
        }
        // PROVEN (QForce EBOOT, Loading.Objects.NewEngineBakedLighting): the RNM vertex shaders write
        // TEX1.z = c[4].x, the per-draw constant the Tie renderer uploads from the placement's +0x64
        // (0x0073E9C0) and the UFrag renderer from the record's +0x68 (0x0074DC98), each u16 * 256/65535.
        // It reaches the shader as Direction1.w on draws flagged Direction0.w = 4. A draw without it
        // keeps the old labelled stand-in of 3, which is not a retail constant.
        bool retailBakedScale = instanceLight.direction0.w > 3.5 && instanceLight.direction0.w < 4.5;
        float lightmapScale = retailBakedScale ? (rnmRoute ? instanceLight.direction1.w : instanceLight.tieShaderUseMeta.z) : 3.0;
        bakedDiffuseLight = lm * rnm * lightmapScale + sun;
    } else {
        // Existing non-old-engine/editor directional reconstruction.
        vec3 bakedLightDirTS = bakedDirSample.rgb * 2.0 - 1.0;
        float bl = length(bakedLightDirTS);
        bakedLightDirTS = bl > 0.0 ? bakedLightDirTS / bl : vec3(0.0, 0.0, 1.0);
        vec3 bakedNormalTS = normalize(vec3(derivativeSum * uBakedBumpFade, 1.0));
        float bakedNdotL = clamp(dot(bakedLightDirTS, bakedNormalTS), 0.0, 1.0);
        float bakedDiffuse = bakedLightDirTS.z > 0.0 ? bakedNdotL / bakedLightDirTS.z : bakedNdotL;
        bakedDiffuseLight = bakedColour.rgb * bakedDiffuse * uBakedLightScale;
    }
    // A bake REPLACES the ambient fill rather than adding to it, because the bake already carries the
    // bounce. But its N.L clamps to zero, so a parallax-perturbed normal that tilts past the baked
    // light direction lands on exactly black. uBakedAmbient keeps a fraction of the fill underneath as
    // a floor - the bake's own shadows still read, they just stop bottoming out.
    vec3 bakedFloor = undecodedFill * uBakedAmbient;
    // Old Tie draws are marked by Direction0.w = 2. Lightmapped retail Tie shaders have no ambient
    // floor, so the editor floor is removed for them. Unbaked Ties deliberately KEEP the environment
    // fill: the retail unbaked shaders use a per-vertex UBYTE4 prelight whose constants are not
    // closed, and replacing it with a neutral 1.0 removed all shading and made Ties glow (2026-09-17).
    float oldTieDraw = uOldEngineMode > 0.5 ? step(1.5, instanceLight.direction0.w) : 0.0;
    // New engine, same rule: a draw carrying the game's own c[4] is drawn the retail way, and the
    // lightmapped QForce shaders (949d655b3022, d70d44879d79) add nothing under the bake either.
    float newRetailBakeDraw = uOldEngineMode > 0.5
        ? 0.0
        : step(3.5, instanceLight.direction0.w) * step(instanceLight.direction0.w, 4.5);
    vec3 selectedFloor = mix(bakedFloor, vec3(0.0), max(oldTieDraw, newRetailBakeDraw));
    // PROVEN, new engine: the retail emissive term is 4 * props.b, in BOTH shader families - the
    // Tie ones (Reduced 03039ca17211 ends '+ 4 * expensive.b') and the skinned-Moby ones
    // ('+ emissive(tex2.b) * 4'). The editor used to reach 4 only on a lightmapped draw, by adding
    // 3 * emissive inside the new-engine baked branch on top of the 1 this line adds - so the same
    // material property glowed four times brighter on a lightmapped Tie than on a Moby, in a game
    // whose shaders say 4 for both. The gain belongs here, once, where every draw passes.
    //
    // Old engine keeps 1: the recovered ToD Tie permutation Full[0x28] is LM1.rgb * factor +
    // properties.b, with no multiplier, and no ToD shader has been read that says otherwise.
    float emissiveGain = uOldEngineMode > 0.5 ? 1.0 : 4.0;
    // New-engine unbaked Tie with its placement's prelight (Direction0.w = 5), as the retail shaders
    // light it (FP 210bbe6e06dc flat, af2e853165e2 RNM): prelight + key light * N.L * the baked sun
    // visibility bit, with no environment fill. The normal-map permutations weight the three basis
    // intensities by the squared RNM weights of the tangent normal; the flat ones average them
    // (c[4].w = c[4].z / 3). The key light takes S like every other shader's sun.
    vec3 unbakedFill = undecodedFill;
    // ToD unbaked Tie with its placement's prelight (Direction0.w = 6, FPs 86591e25cdd7 flat /
    // 952806cc549c normal-mapped): chroma x max(N.TEX3, 0), N the tangent normal (z without a normal map),
    // + props.b (the emissive term below); the albedo already carries TEX0.z (the vertex light scale).
    if (uOldEngineMode > 0.5 && instanceLight.direction0.w > 5.5 && instanceLight.direction0.w < 6.5)
        unbakedFill = fPrelightChroma * max(dot(normalize(vec3(derivativeSum, 1.0)), fPrelightIntensity.xyz), 0.0);
    if (uOldEngineMode < 0.5 && instanceLight.direction0.w > 4.5 && instanceLight.direction0.w < 5.5) {
        vec3 intensities = fPrelightIntensity.xyz;
        float basisLight = (intensities.x + intensities.y + intensities.z) / 3.0;
        if (normalPermutation) {
            vec3 pn = normalize(vec3(derivativeSum, 1.0 - dot(derivativeRaw, derivativeRaw)));
            vec3 pw = vec3(
                clamp(dot(pn, vec3(-0.408203, 0.707031, 0.577148)), 0.0, 1.0),
                clamp(dot(pn, vec3(-0.408203, -0.707031, 0.577148)), 0.0, 1.0),
                clamp(dot(pn, vec3(0.816406, 0.0, 0.577148)), 0.0, 1.0));
            basisLight = dot(pw * pw, intensities);
        }
        unbakedFill = fPrelightChroma * basisLight
            + uEnvLight0Colour * fPrelightIntensity.w * max(dot(worldNormal, uEnvDirection0), 0.0) * sunS;
    }
    float shadowLight = uOldEngineMode > 0.5 ? 1.0 : 0.2 * sunS + 0.8;
    vec3 lighting = mix(unbakedFill, max(bakedDiffuseLight, selectedFloor), hasBaked) * shadowLight + emissive * emissiveGain;
    float bakedSpecLight = mix(1.0, bakedColour.a, hasBaked);
    vec3 reflDir = reflect(-viewDir, worldNormal);
    vec4 envTexel = texture(uEnvCube, reflDir);
    // PROVEN (retail Tie cubemap FPs): env.rgb *= EX2((env.a * 255 - 80) * 0.0285691) - the RSX EXP
    // opcode is base 2 (RPCS3's EX2), so 2^((a*255 - 80) / 35.0). Read as a natural exp (e^x, i.e.
    // /24.26) until 09-10-2026, which made the HDR cube up to 3x too bright (a = 204 on Metropolis:
    // 34x instead of 11.7x) and is why the reflections were once held back to a 0.12 gain. The alpha read
    // here is already sRGB-decoded: ToD's cube descriptors set ADDRESS gamma on all four channels, applied at
    // upload (AssetManager.BuildEnvironmentCubemap).
    float envExposure = uOldEngineMode > 0.5
        ? exp2((envTexel.a * 255.0 - 80.0) * 0.0285691)
        : exp2((envTexel.a * 255.0 - 128.0) / 16.0);
    vec3 envColour = envTexel.rgb * envExposure;
    float NdotV = clamp(dot(worldNormal, viewDir), 0.0, 1.0);
    float fresnel = uReflectionBase + (1.0 - uReflectionBase) * pow(1.0 - NdotV, 5.0);
    float reflectivity = clamp(specIntensity + fresnel, 0.0, 1.0);
    // OLD engine, PROVEN: no retail ToD fragment shader paired with a skinned Moby VP samples the
    // cubemap unit (7); Moby reflection comes from the props.b emissive term only. NEW engine, PROVEN
    // the other way (QForce RPCS3 cache, FP 1a28fd1dc600 paired with skinned VP c29b70f7428f, unit 7
    // a cube per the pipeline's texture dimensions): Mobys sample the reflection cube by the reflect
    // vector like Ties do, so only the old-engine Moby is kept away from envFill.
    // PROVEN (retail Tie cubemap FPs, keys with feature bit 0x04 only):
    //   reflection = env * mix(ShaderMetadataOld+0x40 rgb, albedo, +0x40 w) * props.r * saturate(fade) * c4.w
    // Eligibility is shell feature bit 0x04: every table entry that samples unit 7 has it and no
    // other does, and runtime V4 shows the bit surviving from the effective use mask into the terminal
    // key. The distance fade (c[16]) is not closed and is taken as 1; c4.w is 1.0 on every instance.
    float oldReflectionBit = mod(floor(instanceLight.tieShaderUse0.z / 4.0), 2.0);
    vec3 oldReflection = envColour * mix(uMat3.rgb, albedo, uMat3.w) * specIntensity * oldReflectionBit * oldDistanceFade;
    // NEW ENGINE reflection (Docs/reverse/NEW_ENGINE_REFLECTION_FOG_TIE_UFRAG_2026-09-23.md section 2),
    // read off the retail shaders with Tools/Reverse/rsx/rsxfp_dataflow.py and checked against the raw
    // listings by the 23-09 verification:
    //
    //   N      = TBN * normalize(nm.xy * s, 1 - dot(nm.xy, nm.xy))   z BEFORE the scale; s = VP c[17].w
    //   cube   = texture(cube[+0x40], reflect(-V, N))                 rgb = tint, a = intensity (RGBA16F)
    //   refl   = cube.rgb * max(cube.a + (+0x60.z), 0)  +  (rim + lightBuffer.a) * (+0x60.w)
    //   mask   = spec * distanceFade * saturate(dot(lit, lum * 1.65) + 0.01)
    //   colour = albedo * lit + refl * mix(+0x50.rgb, albedo, +0x50.w) * mask
    //
    // PROVEN in all three reference families (0d299ef3cf54 Tie, 949d655b3022 Tie+UFrag lightmapped,
    // f6cdb4ccaa84 skinned Moby): the gate on the cube's own fourth channel with bias +0x60.z, the
    // tint mix (ADD then MAD against the same +0x50 vector), the luminance mask on 'lit' INCLUDING the
    // 4 * emissive term (x1.65, +0.01: fixed constants in every family), reflection added before fog.
    //
    // Where the families differ - so one editor shader cannot be term-for-term for all of them:
    //  * spec is props.r (unit 2, .x) in the dominant form - 344 skinned-Moby and about 130 Tie/UFrag
    //    shaders - but AUXILIARY A's .x (unit 8) in f6cdb4ccaa84 (51 shaders), whose emissive is also
    //    Aux A.y * 4 and which never reads props.r/.b. The editor uses props for every material: the
    //    unit-8 routing is per shader and not closed for the materials whose shaders are not in the
    //    level files.
    //  * 0d299ef3cf54 multiplies the mask further by TEX1.w * albedo.a^2 * (1 + detail.a*TEX6.z*props.a);
    //    949d/f6cd do not. Not applied.
    //  * the Fresnel rim is pow(1 - N.V, +0x60.y) * VP c[0].x (value OPEN) in some families, absent in
    //    others; the light buffer does not exist here. Both left out - a rim is additive, never a factor
    //    on the cube, which is how this branch wrongly used it before.
    //  * distanceFade is VP state (c[19]/c[21], OPEN), taken as 1.
    // Which materials reflect: every measured new-engine material has +0x60 != 0 except two class-0x04
    // ones, so the test below excludes almost nothing (SUPPOSE, kept only for a zeroed vector). 15 of
    // 987 class-0x13 materials have a non-zero vector and no unit-7 fetch; they reflect here and not in
    // the game. A material with no props texture gets a black one, so it reflects nothing here; what
    // the engine binds to an empty unit 2 is OPEN.
    vec3 reflNormal = normalize(tbn * vec3(derivativeSum, 1.0 - dot(derivativeRaw, derivativeRaw)));
    vec3 newReflDir = reflect(-viewDir, reflNormal);
    int cubeSlot = int(uMat5.x + 0.5);
    vec4 newCube = cubeSlot == 1 ? texture(uEnvCube1, newReflDir)
                 : cubeSlot == 2 ? texture(uEnvCube2, newReflDir)
                 : cubeSlot == 3 ? texture(uEnvCube3, newReflDir)
                 : texture(uEnvCube, newReflDir);
    float newReflects = step(1e-6, dot(abs(uMat4), vec4(1.0)));
    vec3 newReflRadiance = newCube.rgb * max(newCube.a + uMat4.z, 0.0);
    // Reflection occlusion (QForce EBOOT fragment-constant group 1, 0x0079DE48): the shaders scale the
    // reflection by saturate(b + (1 - b) * lum(light) / a), light being the diffuse lighting plus
    // 4 * emissive. b, a come from region.dat 0x8B00 +0x90/+0x94 (uReflOcclusion*, STRONG); (0.01, 0.6) -
    // the value on every QForce level, i.e. the (0.35079, 1.18008, 0.11913, 0.01) the shaders carry -
    // when the level gives none.
    float occlusionFloor = uReflOcclusionFullLuminance > 0.0 ? uReflOcclusionFloor : 0.01;
    float occlusionFull = uReflOcclusionFullLuminance > 0.0 ? uReflOcclusionFullLuminance : 0.6;
    float newLitMask = clamp(occlusionFloor + (1.0 - occlusionFloor)
        * dot(lighting, vec3(0.2126, 0.7152, 0.0722)) / occlusionFull, 0.0, 1.0);
    // PROVEN: the reflection mask also takes material +0x34 (c[21].y, faded into TEX2.w).
    vec3 newReflection = newReflRadiance * mix(uMat3.rgb, albedo, uMat3.w) * specIntensity * uMat5.z * reflectionFade * (allowSpecular ? 1.0 : 0.0) * newLitMask * newReflects;
    // New-engine Mobys take the cube too: QForce's skinned-Moby FPs sample unit 7 by the reflect
    // vector (RPCS3 pipeline cache: unit 7 is a cube for FP 1a28fd1dc600), unlike ToD's, which never
    // do. isOldMoby is what keeps the old engine's Mobys out of it. uEnvironmentIntensity stays as the
    // user's gain (1.0 by default on the new engine), not as a retail term.
    vec3 envFill = uOldEngineMode > 0.5
        ? oldReflection * uEnvironmentIntensity * oldTieDraw
        : newReflection * uEnvironmentIntensity * (1.0 - isOldMoby);
    // UNLIT (uMat1.z == 0): the albedo straight through, still parallax-offset and alpha-tested so the
    // surface keeps its real silhouette and texel. Everything above still runs - the compiler drops it,
    // and branching around it would need the texture fetches hoisted out anyway (they feed the test).
    litColor = uMat1.z > 0.5
        ? (albedo * lighting + envFill)
        : albedoTex.rgb * fColor.rgb;
    // Class 0x13 with a decoded shader (uMat1.x, NewEngineEmbeddedShaders): its own equation replaces
    // the generic one. 1 = family be10f7bae86f, a veil whose albedo holds masks: scrolling, facing-weighted
    // alpha over a flat (props.z + 0.25) * 4, faded out over the first 30 units of depth.
    int embeddedShader = uOldEngineMode > 0.5 ? 0 : int(uMat1.x + 0.5);
    if (embeddedShader == 1) {
        float t = uSceneTime;
        vec4 veilA = texture(uAlbedo, fUV + vec2(0.015, 0.01) * t);
        vec4 veilB = texture(uAlbedo, fUV + vec2(0.01, 0.0) * t);
        float veilMask = veilA.z * mix(veilA.x, veilB.y, cos(t * 0.5) * 0.5 + 0.5);
        float facing = pow(clamp(dot(viewDir, reflNormal), 0.0, 1.0), 5.0);
        litColor = vec3((props.z + 0.25) * 4.0);
        litAlpha = clamp(facing * veilMask, 0.0, 1.0) * clamp((1.0 / gl_FragCoord.w - 15.0) / 15.0, 0.0, 1.0);
    }
    // PROVEN (retail Tie and Moby VPs): fog = min(HPOS.w * c5.x + c5.y, c5.w), saturated in the FP.
    // The coordinate is clip-space w (linear view depth), not radial eye distance. In Vulkan
    // gl_FragCoord.w is 1/clip.w, so the retail coordinate is recovered exactly per pixel.
    float fogCoordinate = 1.0 / gl_FragCoord.w;
    float fogFactor = clamp(min(fogCoordinate * uFogScale + uFogBias, uFogAmount), 0.0, 1.0) * uFogEnabled;
    litColor = uFogDebugView > 0.5 ? vec3(fogFactor) : mix(litColor, uFogColour, fogFactor);
    if (gBeforeAtmosphere) { if (newEngineAtmosphereOn()) litColor = newEngineAtmosphere(litColor, distance(fWorldPos, uCameraPosition), fWorldPos - uCameraPosition); }
    else if (uNewEngineFog.y > 0.5) { vec4 k = newEngineFog(); litColor = mix(litColor, k.rgb, k.w); }
    if (uMat2.y > 0.5 && uMat1.z > 0.5 && embeddedShader == 0) litColor = pow(max(litColor, vec3(0.0)), vec3(1.0 / 2.2));
    // Opacity combines vertex alpha and the albedo's own alpha for any material with decoded vertex
    // alpha to contribute (uMat1.y - any non-Opaque mode). MULTIPLY, not select: vertex alpha (a
    // decal fade, LOD dither, etc.) and the texture's own alpha are independent sources, not
    // mutually exclusive ones - a glass pane with edge falloff baked into vertex colour can still
    // have its own alpha-cut leaf pattern. The albedo's alpha only enters that product when it is
    // real (uMat1.w, AlbedoHasAlphaChannel) - a format with no alpha channel decodes to garbage
    // there, so a no-alpha albedo contributes nothing and vertex alpha alone stands in for it.
    // Materials with nothing to contribute (Opaque, uMat1.y == 0) read straight from the albedo.
    if (embeddedShader == 0)
        litAlpha = uMat1.y > 0.5 ? (uMat1.w > 0.5 ? albedoTex.a * fColor.a : fColor.a) : albedoTex.a;
}";

    private const string FragmentOpaqueGlsl = LitFragCommon + @"
layout(location = 0) out vec4 o;
void main() { vec3 c; float a; gGlow = 1.0 - uPostParams.x; gBeforeAtmosphere = true; shade(c, a); o = vec4(c, gGlow); }";

    // Weighted-blended OIT accumulation. accum sums premultiplied colour * weight; reveal multiplies
    // down by (1 - alpha). Weight favours nearer, more-opaque fragments (McGuire's depth+alpha form).
    private const string FragmentAccumGlsl = LitFragCommon + @"
layout(location = 0) out vec4 accum;
layout(location = 1) out float reveal;
void main() {
    vec3 c; float a; shade(c, a);
    // Depth is reversed (VulkanRenderer.ReverseDepth): 1 - z is the usual near-0 depth McGuire's weight wants.
    float w = clamp(pow(min(1.0, a * 10.0) + 0.01, 3.0) * 1e8 * pow(1.0 - (1.0 - gl_FragCoord.z) * 0.9, 3.0), 1e-2, 3e3);
    accum = vec4(c * a, a) * w;
    reveal = a;
}";

    // Additive (mode 2): the pipeline blends SrcAlpha/One, so the shader just outputs the lit colour and
    // its alpha (which scales the contribution). No OIT needed - addition is order-independent already.
    private const string FragmentAdditiveGlsl = LitFragCommon + @"
layout(location = 0) out vec4 o;
void main() { vec3 c; float a; shade(c, a); o = vec4(c, a); }";
}
