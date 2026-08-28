#version 450

// Lit fragment shader, ported from the game's own captured fragment program (RPCS3 + RenderDoc,
// traced in fragment_shader_annotated.glsl). Shading model follows Insomniac's "Prelighting" decks,
// evaluated forward instead of deferred: this editor has one light rig and no dynamic lights.
//
// TEXTURE CHANNEL MAP (all confirmed from the capture, all pure intensities, never tints):
//   Properties ("expensive")  R = specular   G = parallax height   B = emissive   A = detail mask
//   Detail                    R,G = normal derivative delta        B = albedo lift (unused)  A = spec add
//   Normal                    stores PARTIAL DERIVATIVES, not a normal: dx = A, dy = G,
//                             reconstructed as normalize(vec3(dx, dy, 1)) with no sign flip.
//                             Channel order confirmed against Negotiator/TextureEditor's DXT5 path.
//
// LIGHTING SOURCES, in priority order:
//   1. Baked lightmaps (zone sections 0x5400 colour / 0x5410 tangent-space direction), per INSTANCE,
//      sampled at the SECOND UV set. Where a bake exists it REPLACES the analytic lights.
//   2. The level's analytic lighting environment (main.dat 0x8b00): ambient + two directional lights.
//   3. Flat editor ambient, only when the level supplies neither.
// The cubemap reflection (0x5920) is additive on top and is the ONLY specular the game applies.
//
// All lighting is in LINEAR space: albedo is sRGB-decoded on read and the result re-encoded at the
// end. Light/bake values are NOT decoded, they are intensities, not authored colour.

#define MAX_MAPS_COUNT 8

struct MaterialMap {
    vec4 color;
    float value;
};

// Set numbering is dictated by Bliss's pipeline layout: every uniform buffer first in a contiguous
// run from 0, then every texture. Interleaving them faults the GPU. See AssetManager.BuildLitModelEffect.
layout(std140, set = 2, binding = 0) uniform MaterialBuffer {
    int renderMode;
    MaterialMap maps[MAX_MAPS_COUNT];
};

layout(std140, set = 3, binding = 0) uniform LightBuffer {
    vec3 uLightDirection;
    float uAmbient;
    vec3 uLightColor;
    float uSpecularPower;
    vec3 uCameraPosition;
    float uReflectionDebug;
    vec3 uEnvironmentColour;
    float uEnvironmentIntensity;
    // Lightmap UV transform + bake tuning: research controls, identity/neutral by default.
    vec2 uLightmapUVScale;
    vec2 uLightmapUVOffset;
    float uBakedLightScale;
    float uBakedBumpFade;
    float uBakedDebugView;
    float uReflectionBase;
    vec2 uLightmapUVPivot;
    float uLightmapUVRotation;
    float _reserved2;
    // The level's analytic lighting environment (0x8b00). uEnvHasLighting gates it.
    vec3 uEnvDirection0;
    float uEnvHasLighting;
    vec3 uEnvDirection1;
    float _padding4;
    vec3 uEnvAmbient;
    float _padding5;
    vec3 uEnvLight0Colour;
    float _padding6;
    vec3 uEnvLight1Colour;
    float _padding7;
};

layout (set = 4, binding = 0) uniform texture2D fAlbedo;
layout (set = 4, binding = 1) uniform sampler fAlbedoSampler;

layout (set = 5, binding = 0) uniform texture2D fNormal;
layout (set = 5, binding = 1) uniform sampler fNormalSampler;

layout (set = 6, binding = 0) uniform texture2D fProperties;
layout (set = 6, binding = 1) uniform sampler fPropertiesSampler;

layout (set = 7, binding = 0) uniform texture2D fDetail;
layout (set = 7, binding = 1) uniform sampler fDetailSampler;

// Baked lighting, per instance. Always bound (inert fallbacks when absent) because an unbound
// declared set is undefined behaviour; maps[6].value is what says a real bake exists.
layout (set = 8, binding = 0) uniform texture2D fLightColour;
layout (set = 8, binding = 1) uniform sampler fLightColourSampler;

layout (set = 9, binding = 0) uniform texture2D fLightDir;
layout (set = 9, binding = 1) uniform sampler fLightDirSampler;

// Environment cubemap, faces in the file's own +X,-X,+Y,-Y,+Z,-Z order. Sampled ONLY for reflections.
layout (set = 10, binding = 0) uniform textureCube fEnvCube;
layout (set = 10, binding = 1) uniform sampler fEnvCubeSampler;

layout (location = 0) in vec2 fTexCoords;
layout (location = 1) in vec4 fColor;
layout (location = 2) in vec3 fWorldNormal;
layout (location = 3) in vec3 fWorldTangent;
layout (location = 4) in float fTangentHandedness;
layout (location = 5) in vec3 fWorldPos;
// The LIGHTMAP UV set (vertex attribute 2), settled by the captured vertex program.
layout (location = 6) in vec2 fTexCoords2;

layout (location = 0) out vec4 fFragColor;

void main() {
    vec3 n = normalize(fWorldNormal);
    vec3 t = normalize(fWorldTangent - n * dot(fWorldTangent, n));
    vec3 b = cross(n, t) * fTangentHandedness;
    mat3 tbn = mat3(t, b, n);

    vec3 viewDir = normalize(uCameraPosition - fWorldPos);

    // PARALLAX, ported exactly: height = raw * scale + bias, offset ADDED, and NOT divided by
    // viewDirTS.z. The classic divide blows the UV shift up at grazing angles and, with a single tap,
    // shreds the sampling into swimming artifacts. A negative scale simply flips the relief direction,
    // so which way it moves is DATA, not a bug. Height is sampled at the UN-offset UV.
    vec3 viewDirTS = transpose(tbn) * viewDir;
    float heightRaw = texture(sampler2D(fProperties, fPropertiesSampler), fTexCoords).g;
    float height = heightRaw * maps[3].value + maps[4].value;
    vec2 texCoords = fTexCoords + viewDirTS.xy * height;

    vec4 texelColor = texture(sampler2D(fAlbedo, fAlbedoSampler), texCoords);

    // maps[0].value is the alpha-clip threshold. The comparison is <=, not <: on the old engine the
    // threshold is 0, and a strict < would never discard anything.
    switch (renderMode) {
        case 0:
            texelColor.a = 1.0F;
            break;
        case 1:
            if (texelColor.a <= maps[0].value) {
                discard;
            }
            break;
    }

    // Normal map: partial derivatives (dx = A, dy = G), see the header.
    vec4 normalSample = texture(sampler2D(fNormal, fNormalSampler), texCoords);
    float dx = normalSample.a * 2.0F - 1.0F;
    float dy = normalSample.g * 2.0F - 1.0F;

    vec4 propsSample = texture(sampler2D(fProperties, fPropertiesSampler), texCoords);
    float specIntensity = propsSample.r;
    float emissiveIntensity = propsSample.b;
    // Detail mask is the properties alpha, but only where that texture HAS an alpha channel.
    // maps[1].value flags which case this is; block formats without alpha decode to an unauthored
    // 1.0, which is what the original hardware produced too, so it simply does not attenuate.
    float detailMask = mix(1.0F, propsSample.a, maps[1].value);

    // DETAIL MAP. maps[5].value is just "this material uses its detail map" (1/0). The per-channel
    // strengths this once multiplied by are gone: they read ShaderMetadataOld 0x28/0x2C/0x30, which
    // the EBOOT reverse proves is an unrelated RGB parameter triple (dev/chatgpt-eboot-4,5.txt).
    // The game does scale these by constants of its own, but none is located, so the derivatives are
    // added plainly, gated only by the mask. Plain ADDITION is the whole point of the encoding: two
    // derivative maps compose with no reorientation. Decode to signed BEFORE masking, or a fully
    // masked-out texel becomes a full-strength -1 perturbation.
    // maps[2].value is the detail UV tiling (ShaderMetadataOld 0x58). Detail samples at the un-offset
    // base UV: the game gives it its own scaled share of the parallax shift, which is not identified.
    vec2 detailCoords = fTexCoords * maps[2].value;
    vec4 detailSample = texture(sampler2D(fDetail, fDetailSampler), detailCoords);
    vec2 detailDerivative = vec2(detailSample.r * 2.0F - 1.0F, detailSample.g * 2.0F - 1.0F) * (detailMask * maps[5].value);
    float detailSpecAdd = detailSample.a * detailMask * maps[5].value;

    vec2 derivativeSum = vec2(dx, dy) + detailDerivative;
    vec3 tangentNormal = normalize(vec3(derivativeSum, 1.0F));
    vec3 worldNormal = normalize(tbn * tangentNormal);

    specIntensity = specIntensity + detailSpecAdd;

    vec3 albedo = pow(texelColor.rgb * maps[0].color.rgb * fColor.rgb, vec3(2.2F));

    vec3 reflDir = reflect(-viewDir, worldNormal);

    // BAKED LIGHTING. maps[6].value is 1 only where a real bake exists. The UV transform above it is
    // a research control (identity by default): rotation about a pivot, then scale and offset. The
    // pivot is adjustable because UVs2 are ATLAS coordinates, so rotating about the atlas centre
    // would sweep an island across unrelated ones instead of spinning it in place.
    float hasBaked = maps[6].value;
    vec2 uvCentred = fTexCoords2 - uLightmapUVPivot;
    float uvSin = sin(radians(uLightmapUVRotation));
    float uvCos = cos(radians(uLightmapUVRotation));
    vec2 uvRotated = vec2(uvCentred.x * uvCos - uvCentred.y * uvSin,
                          uvCentred.x * uvSin + uvCentred.y * uvCos) + uLightmapUVPivot;
    vec2 bakedUV = uvRotated * uLightmapUVScale + uLightmapUVOffset;
    vec4 bakedColour = texture(sampler2D(fLightColour, fLightColourSampler), bakedUV);
    vec4 bakedDirSample = texture(sampler2D(fLightDir, fLightDirSampler), bakedUV);

    // Tangent-space light direction. Channel order is (r, b, g), NOT (r, g, b): the normal-ward
    // component lives in GREEN. Measured over 400 directional maps, G never drops below ~126 while
    // R and B centre on 128 with symmetric spread, and DXT1 gives green the extra bit of precision.
    // Components are taken RAW with NO signed expansion: as stored, the mean vector length is 0.955,
    // whereas every signed-expansion variant lands at 0.28 to 0.64. A unit field is what a direction is.
    vec3 bakedLightDirTS = vec3(bakedDirSample.r, bakedDirSample.b, bakedDirSample.g);
    float bakedLen = length(bakedLightDirTS);
    bakedLightDirTS = bakedLen > 0.0F ? bakedLightDirTS / bakedLen : vec3(0.0F, 0.0F, 1.0F);

    // N.L stays in tangent space, matching the capture. uBakedBumpFade stands in for the game's
    // per-vertex distance bump fade (vViewTS.w), which flattens normals with distance. At full
    // strength the derivatives can drive the dot negative and punch black holes through correct bake.
    vec3 bakedNormalTS = normalize(vec3(derivativeSum * uBakedBumpFade, 1.0F));
    float bakedNdotL = clamp(dot(bakedLightDirTS, bakedNormalTS), 0.0F, 1.0F);
    // Dividing by the light's own .z is the signature of directional lightmaps: it makes a FLAT
    // normal reproduce the baked intensity exactly, so the normal map modulates the bake rather than
    // cancelling it. uBakedLightScale stands in for the game's per-draw lightScale (vc[2].z): the
    // bake is genuinely dark as stored (non-black texels average 32/255).
    float bakedDiffuse = bakedLightDirTS.z > 0.0F ? bakedNdotL / bakedLightDirTS.z : bakedNdotL;
    vec3 bakedDiffuseLight = bakedColour.rgb * bakedDiffuse * uBakedLightScale;

    // Surfaces with no bake use the level's analytic rig, or the flat editor ambient if it has none.
    // Not the whole story: the game also modulates these by a per-vertex baked term (tc1.x) that
    // needs a vertex-constants capture to decode.
    vec3 envDiffuse = uEnvAmbient
        + uEnvLight0Colour * max(dot(worldNormal, uEnvDirection0), 0.0F)
        + uEnvLight1Colour * max(dot(worldNormal, uEnvDirection1), 0.0F);
    vec3 undecodedFill = mix(vec3(uAmbient), envDiffuse, uEnvHasLighting);
    // Emissive sits INSIDE the light term so it multiplies by albedo: a surface glows in its own colour.
    vec3 lighting = mix(undecodedFill, bakedDiffuseLight, hasBaked) + emissiveIntensity;

    // ENVIRONMENT FILL, the only specular the game applies. Additive and NOT gated by the lightmap,
    // which is what stops baked shadows reaching pure black. TINTED BY ALBEDO: the capture computes
    // a specular tint from albedo, and an untinted grey fill desaturates the whole scene.
    // The cube's RGB is a mantissa and its alpha an HDR exponent. Sampled at mip 0 (no mip chain), so
    // the reflection is sharp, which is what the game shows. Reflectivity is the material's specular
    // map plus a Schlick-Fresnel base, so flat surfaces reflect too; uReflectionBase is F0.
    // The game modulates specular by the bake's ALPHA (monochrome specular light); DXT1 bakes have no
    // authored alpha and decode to 1.0, correctly leaving it unattenuated.
    float bakedSpecLight = mix(1.0F, bakedColour.a, hasBaked);
    vec3 specularTint = albedo;
    vec4 envTexel = texture(samplerCube(fEnvCube, fEnvCubeSampler), reflDir);
    float envExposure = exp2((envTexel.a * 255.0F - 128.0F) / 16.0F);
    vec3 envColour = envTexel.rgb * envExposure;
    float NdotV = clamp(dot(worldNormal, viewDir), 0.0F, 1.0F);
    float fresnel = uReflectionBase + (1.0F - uReflectionBase) * pow(1.0F - NdotV, 5.0F);
    float reflectivity = clamp(specIntensity + fresnel, 0.0F, 1.0F);
    vec3 envFill = envColour * specularTint * uEnvironmentIntensity * reflectivity * bakedSpecLight;
    // No Phong lobe: it modelled a directional light this game does not have. (uLightDirection,
    // uLightColor and uSpecularPower are therefore unused here, kept only for buffer layout.)
    vec3 specPart = envFill;

    vec3 finalColor = albedo * lighting + specPart;

    // Debug: the raw bake with no albedo or shading, so a black surface is immediately either
    // "the bake is black here" or "shading is killing it".
    if (uBakedDebugView > 0.5F) {
        finalColor = mix(vec3(0.25F), bakedColour.rgb * uBakedLightScale, hasBaked);
    }

    // Debug: the cubemap reflection on every surface, ungated. Doubles as an axis-orientation check.
    if (uReflectionDebug > 0.5F) {
        finalColor = envColour;
    }

    finalColor = pow(finalColor, vec3(1.0F / 2.2F));
    fFragColor = vec4(finalColor, texelColor.a * maps[0].color.a * fColor.a);
}
