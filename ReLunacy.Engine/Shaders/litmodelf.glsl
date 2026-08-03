#version 450

#define MAX_MAPS_COUNT 8

struct MaterialMap {
    vec4 color;
    float value;
};

layout(std140, set = 2, binding = 0) uniform MaterialBuffer {
    int renderMode;
    MaterialMap maps[MAX_MAPS_COUNT];
};

// Set numbering is dictated by Bliss's pipeline layout construction, NOT free choice:
// every uniform buffer must come first in a contiguous run from 0, then every texture.
// See AssetManager.BuildLitModelEffect for the full explanation and the GPU fault that
// interleaving them caused.
layout(std140, set = 3, binding = 0) uniform LightBuffer {
    vec3 uLightDirection;
    float uAmbient;
    vec3 uLightColor;
    float uSpecularPower;
    vec3 uCameraPosition;
    float uReflectionDebug;
    vec3 uEnvironmentColour;
    float uEnvironmentIntensity;
    vec2 uLightmapUVScale;
    vec2 uLightmapUVOffset;
    float uBakedLightScale;
    float uBakedBumpFade;
    float uBakedDebugView;
    float uReflectionBase;
    vec2 uLightmapUVPivot;
    float uLightmapUVRotation;
    float _reserved2;
    // The level's analytic lighting environment (section 0x8b00): two directional lights and an
    // ambient, the game's OWN sun/ambient rather than the editor's fabricated one. Used for surfaces
    // with no decoded bake (which on metropolis is everything). uEnvHasLighting gates the fallback.
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

// The game's own baked lighting, from zone sections 0x5400 / 0x5410. Per-INSTANCE: the
// material cache is keyed on the lightmap index so each placement gets its own pair.
layout (set = 8, binding = 0) uniform texture2D fLightColour;
layout (set = 8, binding = 1) uniform sampler fLightColourSampler;

layout (set = 9, binding = 0) uniform texture2D fLightDir;
layout (set = 9, binding = 1) uniform sampler fLightDirSampler;

// The level's environment cubemap (old-engine section 0x5920), faces uploaded in the file's
// +X,-X,+Y,-Y,+Z,-Z order. The game samples this map EXCLUSIVELY for reflections, so it is
// read only in the ENVIRONMENT FILL section, along the world-space reflection vector.
// Always bound (AssetManager binds a 1x1 grey fallback when the level has no cubemap) so this
// declared set is never left empty - same invariant as the baked-lighting textures above.
// Slot 10 keeps the buffers-then-textures ordering intact; see AssetManager.BuildLitModelEffect.
layout (set = 10, binding = 0) uniform textureCube fEnvCube;
layout (set = 10, binding = 1) uniform sampler fEnvCubeSampler;

layout (location = 0) in vec2 fTexCoords;
layout (location = 1) in vec4 fColor;
layout (location = 2) in vec3 fWorldNormal;
layout (location = 3) in vec3 fWorldTangent;
layout (location = 4) in float fTangentHandedness;
layout (location = 5) in vec3 fWorldPos;
layout (location = 6) in vec2 fTexCoords2;

layout (location = 0) out vec4 fFragColor;

void main() {
    vec3 n = normalize(fWorldNormal);
    vec3 t = normalize(fWorldTangent - n * dot(fWorldTangent, n));
    vec3 b = cross(n, t) * fTangentHandedness;
    mat3 tbn = mat3(t, b, n);

    vec3 viewDir = normalize(uCameraPosition - fWorldPos);

    // Single-tap parallax offset from the expensive map's G channel, confirmed as the
    // parallax HEIGHTMAP by the game's own captured fragment shader (see
    // fragment_shader_annotated.glsl). This reproduces that shader's exact form:
    //
    //     height = heightRaw * scale + bias      (both per-material constants)
    //     uv     = uv + viewDirTS.xy * height    (ADDED, not subtracted)
    //
    // Two consequences worth not re-deriving later. First, there is no fixed sign
    // convention to discover: a negative scale flips the offset direction, so which way
    // relief appears to move is DATA, not a bug to fix in this math. Second, height is
    // scale/bias remapped rather than scaled by a bare multiplier, so a bias of 0 is what
    // gives "0 height -> no offset" - it is not inherent to the encoding.
    // maps[3].value / maps[4].value are those two constants, live-tunable per material
    // from the ShaderBrowser (see AssetManager.SetParallax) precisely so candidate float
    // pairs found in the raw shader-metadata hex dump can be tried verbatim - nothing
    // scales them further here, which is the point.
    // Offset-LIMITED parallax (no division by viewDirTS.z), which the game shader also
    // does NOT do: the classic divide amplifies the UV shift toward infinity at grazing
    // view angles, which with a single tap shreds the albedo/normal sampling into blocky
    // swimming artifacts (confirmed live: read as "pixelated artifacts over the albedo").
    vec3 viewDirTS = transpose(tbn) * viewDir;
    float heightRaw = texture(sampler2D(fProperties, fPropertiesSampler), fTexCoords).g;
    float height = heightRaw * maps[3].value + maps[4].value;
    vec2 texCoords = fTexCoords + viewDirTS.xy * height;

    vec4 texelColor = texture(sampler2D(fAlbedo, fAlbedoSampler), texCoords);

    switch (renderMode) {
        case 0:
            texelColor.a = 1.0F;
            break;
        case 1:
            // maps[0].value carries the material's alphaClip threshold from
            // AssetManager.GetOrBuildMaterial. On the OLD engine that is 0 and the game
            // clips at zero - see MaterialReader.GetAlphaClip for why the metadata field
            // that used to feed this is not a threshold at all.
            // The comparison is <=, not <, and that matters: at a threshold of 0 a strict
            // < can never be true, so nothing would ever be discarded and cutout surfaces
            // would render as opaque quads.
            if (texelColor.a <= maps[0].value) {
                discard;
            }
            break;
    }

    // This game's normal maps store partial derivatives, not a standard tangent-space
    // (nx,ny,nz) encoding - B is always constant/unused, R unused. Reconstruction is
    // simply normalize(vec3(dx, dy, 1)) in tangent space, taken straight from the game's
    // own captured fragment shader, which does no sign flip at all: it uses the sampled
    // texel's .xyz directly as (dx, dy, 1) and only ever ADDS the detail map's derivative
    // to .xy (see fragment_shader_annotated.glsl, "NORMAL MAP" section).
    // This previously negated both derivatives - normalize(vec3(-dx, -dy, 1)) - reasoning
    // from the classic height-gradient convention where the map stores dh/du and the
    // normal needs -dh/du. That double-negates here, because the stored value is already
    // the negated ratio, and it inverted the perceived relief on every normal-mapped
    // surface. It only became visible once lighting actually worked; before the descriptor
    // set-numbering fix (see AssetManager.BuildLitModelEffect) the light direction and
    // camera position were garbage, so nothing about the shading was trustworthy.
    // Channel assignment is dx=Alpha, dy=Green - confirmed against Negotiator/TextureEditor
    // (a separate, working reverse-engineering tool for this exact game's formats -
    // TextureHelper.BitmapFromDDS's DXT5 normal-map path reads p.A for dx and p.G for dy),
    // not G=dx/A=dy as originally guessed here. The captured shader can't corroborate the
    // channel order: RSX texture remap is folded into the Vulkan image-view swizzle and
    // never appears in the decompiled body. See TextureUtils.ReconstructNormalMap for the
    // export-side equivalent, which needs the same convention.
    vec4 normalSample = texture(sampler2D(fNormal, fNormalSampler), texCoords);
    float dx = normalSample.a * 2.0F - 1.0F;
    float dy = normalSample.g * 2.0F - 1.0F;

    // The expensive map's channels are pure INTENSITIES, never color/tint sources (the
    // only color a surface has is its albedo - emissive glows in the albedo's own color,
    // specular flashes in the LIGHT's color). Layout confirmed against the game's own
    // captured shader: R = specular intensity, G = parallax height (sampled above, at the
    // un-offset UV), B = emissive/incandescence intensity, A = DETAIL MAP MASK.
    // A is NOT roughness - that reading is retracted. In the captured shader A does
    // exactly one thing, gate the detail-map fetch, and never reaches a specular exponent;
    // Insomniac's own slide lists "detail map mask" as a named material input. Specular
    // power is per-MATERIAL there (the cubemap LOD constant), never per-texel.
    // Every earlier variant that promoted a channel into a tint (constant-white specTint,
    // albedo-tinted env fill, spec-channel-tinted fill) produced a scene-wide artifact in
    // live testing (white filter / pitch-black metals) - keep this a pure intensity model.
    vec4 propsSample = texture(sampler2D(fProperties, fPropertiesSampler), texCoords);
    float specIntensity = propsSample.r;
    float emissiveIntensity = propsSample.b;
    // The detail mask is the expensive map's alpha - but only when that texture actually
    // HAS an alpha channel. DXT1, R5G6B5, R8, BC4 and BC5 don't; block decoders synthesise
    // an opaque 255 there, which is not authored data. maps[1].value flags which case this
    // material is in (see AssetManager.GetOrBuildMaterial): 1 = real alpha, sample it;
    // 0 = none, so fall back to a constant fully-on mask. Fully-on rather than fully-off
    // because that is what the original hardware produced too - RSX also returns 1.0
    // sampling alpha from a DXT1 texture - so an unauthored mask simply doesn't attenuate.
    float detailMask = mix(1.0F, propsSample.a, maps[1].value);

    // DETAIL MAP. Layout, read straight off the captured shader's consumers:
    //   R,G -> a partial-derivative perturbation ADDED to the normal map's derivatives
    //   B   -> additive albedo brightness (a SCALAR lift, never a hue)
    //   A   -> additive specular intensity
    // B feeding albedo is the surprising one, so here is the register trace that proves it
    // (fragment_shader.glsl; h1 is the masked detail texel):
    //   L367  h6.xy = (h1.zwzz * fc[5].zwzz).xy  ->  h6.x = detail.B * fc[5].z
    //                                                h6.y = detail.A * fc[5].w
    //   L374  h3.x  = (h6.yyyy + h3).x           ->  specular += detail.A * fc[5].w
    //   L383  fma(h0.xxxx, h1, h6.xxxx)          ->  albedo = albedoScale * base + h6.x
    // h6.xxxx broadcasts one scalar across RGB, hence "lift, not hue".
    // WHY IT BLOWS OUT HERE AND NOT IN THE GAME: the game attenuates this by three factors
    // we cannot source - detailAlbedoStrength (fc[5].z), detailMaskStrength (fc[3].y) and
    // a per-vertex detail fade (tc6.z) - so our detailWeight is systematically the largest
    // it can possibly be. On top of that the mask itself reads 1.0 on every DXT1 expensive
    // map, whose alpha decodes as 255. At strength 1 that is a full +1.0 on linear albedo,
    // i.e. white. The operation is right; the magnitude has no evidence behind it, which is
    // exactly why the strengths default to 0 and are sliders.
    // The whole fetch is scaled by the expensive map's alpha, so a material with no detail
    // mask pulls in nothing. Plain ADDITION of the derivatives is the entire reason this
    // encoding is used - Insomniac's Prelighting deck calls it out explicitly; two
    // derivative maps compose without any reorientation or blend, which is exactly what
    // makes a cheap high-frequency detail layer viable.
    // maps[5].value is the normal strength and maps[5].color.r the specular strength -
    // both live-tunable per material from the ShaderBrowser (AssetManager.SetDetailStrengths).
    // The specular one rides a colour channel because slots 6 and 7 now carry the baked
    // lighting textures; see AssetManager's SLOT BUDGET note.
    // The detail map's ALBEDO contribution (its B channel) is not applied at all - see the
    // channel notes above for the trace proving it exists and why it stays off.
    // TILING. Detail maps are authored small and meant to tile at a higher frequency than
    // the base map. In the game that tiling is NOT a fragment constant: the detail UV
    // arrives pre-tiled in a vertex interpolant, traced from
    // `uvDetail = parallaxOffset * fc[2].z + tc6.xy` - tc6.xy is already the tiled detail
    // UV, built by a vertex program that wasn't captured. So the multiplier lives upstream,
    // either in that vertex shader or in a ShaderMetadata field not identified yet.
    // maps[2].value stands in for it, live-tunable per material from the ShaderBrowser,
    // defaulting to 1 (same frequency as the base map).
    // Useful when hunting it: the BASE map DOES get a fragment-constant tiling in the game,
    // fc[1].xy, applied as uvMain = uvParallax * fc[1].xy. This shader doesn't reproduce
    // that. All 8 MaterialMap slots AND their value fields are now spoken for, so adding it
    // needs a real per-material uniform buffer rather than another map slot.
    // Also not reproduced: the game gives the detail UV its own scaled share of the
    // parallax shift (the fc[2].z above). That constant isn't identified, so detail samples
    // at the UN-offset base UV - exactly what fc[2].z = 0 would give.
    // Order matters: R,G are SIGNED derivatives stored biased into an unsigned texture, so
    // they must be decoded to their signed range BEFORE the mask is applied. Masking the
    // raw texel first would turn a fully-masked-out pixel (detail = 0) into a decoded
    // derivative of -1, i.e. a full-strength perturbation exactly where the material asked
    // for none. The game gets this right for free: RSX's fixed-function signed expansion
    // happens at fetch, before its detailWeight multiply.
    vec2 detailCoords = fTexCoords * maps[2].value;
    vec4 detailSample = texture(sampler2D(fDetail, fDetailSampler), detailCoords);
    vec2 detailDerivative = vec2(detailSample.r * 2.0F - 1.0F, detailSample.g * 2.0F - 1.0F) * (detailMask * maps[5].value);
    float detailSpecAdd = detailSample.a * detailMask * maps[5].color.r;

    // Derivative composition by addition - see above. Only then normalize.
    vec2 derivativeSum = vec2(dx, dy) + detailDerivative;
    vec3 tangentNormal = normalize(vec3(derivativeSum, 1.0F));
    vec3 worldNormal = normalize(tbn * tangentNormal);

    specIntensity = specIntensity + detailSpecAdd;

    // All lighting happens in LINEAR space - the game's own pipeline lit in linear and the
    // source albedo textures are sRGB-authored, so lighting the raw gamma values (what this
    // shader originally did) double-darkens every midtone and crushes shadowed areas to
    // black. Approximate 2.2 decode here, matching encode at the end. The detail map's
    // albedo lift is added AFTER linearization, matching the captured shader, where the
    // detail texel and the base texel have both already been through the same fixed
    // function conversion before they meet.
    vec3 albedo = pow(texelColor.rgb * maps[0].color.rgb * fColor.rgb, vec3(2.2F));

    // Insomniac's own factoring (Prelighting / GDC09 decks), evaluated forward for a
    // single directional sun with l_att = 1:
    //   P_diffuse  = l_col * l_att * (n . l)
    //   P_specular = (l_dir . Refl(v, n))^p * l_col * l_att   (PHONG reflection vector,
    //                per the decks - not a Blinn half-vector)
    //   C = mp * P: final = albedo * diffuseLight + specIntensity * specLight (+ emissive)
    vec3 lightDir = normalize(uLightDirection);
    vec3 reflDir = reflect(-viewDir, worldNormal);

    float nDotL = max(dot(worldNormal, lightDir), 0.0F);
    vec3 diffuseAccum = uLightColor * nDotL;

    // Specular power is PER-MATERIAL, not per-texel. The captured shader takes it from a
    // material constant (the environment cubemap's LOD), and Insomniac's own deck says
    // "per material specular power" in as many words. A previous per-pixel version scaled
    // this exponent by the expensive map's alpha as a roughness - that channel is the
    // detail-map mask, so the whole idea is retracted. Removed with it: an
    // energy-conservation term, (specPower + 2) / (uSpecularPower + 2), which existed only
    // to suppress the artifact the alpha-as-roughness reading caused (DXT1 expensive maps
    // decode alpha as 255 = "fully rough" = a power-1 lobe at full strength, reading as a
    // view-independent white film). With the cause gone the correction would only dim
    // specular for no reason.
    float specPower = max(uSpecularPower, 1.0F);
    float specAccum = pow(max(dot(lightDir, reflDir), 0.0F), specPower);

    // The decks' baked-lighting/lightmap input has no equivalent here (no light data in
    // the level files at all). A FLAT ambient stand-in made every face turned away from
    // the sun an identical dead value (confirmed live: crates/props in shadowed
    // orientations read "unshaded" next to sunlit neighbors), so this is a two-tone
    // hemisphere instead: full ambient from above fading to half toward straight down -
    // the cheapest stand-in that keeps shadowed geometry readable and directional.
    vec3 ambientLight = uAmbient * mix(vec3(0.5F), vec3(1.0F), worldNormal.y * 0.5F + 0.5F);

    // Composite in the captured shader's own factoring:
    //   colour = albedo * (light * diffuseTerm + emissive) + specular
    // Emissive belongs INSIDE the light term, so it is multiplied by albedo - which is why
    // a surface glows in its own albedo colour and expensive.B stays a pure intensity.
    // Here the baked directional lightmap the game multiplies in is replaced by the
    // dynamic sun plus the hemisphere ambient below; the grouping is otherwise identical.
    // ---- BAKED LIGHTING (the game's own) --------------------------------------------
    // maps[6].value is 1 only when this material actually has a bake; the textures are
    // always bound (an unbound declared set is undefined behaviour) but hold inert
    // fallbacks otherwise, so this flag is what keeps them from reading as a real light.
    // Sampled at fTexCoords2, the LIGHTMAP UV SET - this is settled, not inferred. The
    // captured vertex program builds tc0 = (attr1.xy, attr2.xy) and the fragment program
    // reads the bakes at tc0.zw - BOTH of them, tex4 (0x5400) and tex14 (0x5410) - so the
    // lightmap UV is vertex attribute 2.
    // The attribute layout is no longer inferred from the shader body either: the captured
    // DrawParametersBuffer (set 0, binding 2) spells it out, and 2498 draws in that frame
    // carry this exact shape - stride 24, one non-volatile stream, swap_bytes set:
    //   attr0 +0  SINT16 x4     position (.w feeding the per-vertex albedo scale)
    //   attr1 +8  SFLOAT16 x2   UVs
    //   attr2 +12 SFLOAT16 x2   UVs2        <- the lightmap UV, and it is a HALF FLOAT
    //   attr3 +16 CMP 11:11:10  normal
    //   attr4 +20 CMP 11:11:10  tangent
    // That is UFragVertex field for field, so the captured draw is TERRAIN. It also settles
    // the normal/tangent order independently: the vertex program's tangent-space view
    // vector comes out as (dot(v, attr4), dot(v, bitangent), dot(v, attr3)), i.e. the
    // (T, B, N) rows, putting the normal at +16 and the tangent at +20 - which is what this
    // loader already assumed, now confirmed rather than guessed. The bitangent is
    // cross(attr3, attr4) with its handedness taken from sign(position.w).
    // An earlier version sampled at the base UV, reasoning that a unique bake per instance
    // implies the asset's own UV works. That was wrong twice over: a unique bake still
    // needs a unique UNWRAP, and the extracted tex4 is plainly an atlas of unwrapped
    // islands. Base UVs tile a detail texture across a surface, so the lightmap repeated
    // many times per mesh - the blotchy black patching.
    float hasBaked = maps[6].value;
    // Live UV transform - a research control, identity by default. See LightData.LightmapUVScale.
    // Rotation first, about uLightmapUVPivot, THEN scale and offset - so the pivot stays a
    // point in the source atlas rather than drifting whenever the scale changes.
    // Rotating about the atlas centre would be useless here: UVs2 are atlas coordinates, so
    // that sweeps an island across unrelated islands instead of spinning it in place. Aim
    // the pivot at the island being inspected (the UFrag Inspector can snap it there).
    vec2 uvCentred = fTexCoords2 - uLightmapUVPivot;
    float uvSin = sin(radians(uLightmapUVRotation));
    float uvCos = cos(radians(uLightmapUVRotation));
    vec2 uvRotated = vec2(uvCentred.x * uvCos - uvCentred.y * uvSin,
                          uvCentred.x * uvSin + uvCentred.y * uvCos) + uLightmapUVPivot;
    vec2 bakedUV = uvRotated * uLightmapUVScale + uLightmapUVOffset;
    vec4 bakedColour = texture(sampler2D(fLightColour, fLightColourSampler), bakedUV);
    vec4 bakedDirSample = texture(sampler2D(fLightDir, fLightDirSampler), bakedUV);

    // Tangent-space light direction. CHANNEL ORDER IS NOT (r,g,b)->(x,y,z): the
    // normal-ward component lives in GREEN. Measured across 400 DXT1 directional maps in
    // metropolis - G never drops below ~126 (0% of texels under 128) while R and B are both
    // centred exactly on 128 with symmetric spread (34% / 30% below). That is a signed
    // lateral pair plus one always-positive axis, and DXT1 gives green 6 bits against 5 for
    // red and blue, so the dominant component belongs there for precision.
    // The dump can't show this directly: RSX applies a per-texture channel REMAP that
    // RPCS3 folds into the Vulkan image-view swizzle, so the decompiled body just reads
    // .xyz off an already-swizzled texel (see [INFERRED] (b) in the annotated capture).
    // Reading B as z and expanding it made ~30% of texels come out with a NEGATIVE z, which
    // clamps nDotL to zero - the black artifacts.
    // G is NOT signed-expanded, only R and B are: G's floor of ~126 maps to ~0 under
    // expansion, which would reintroduce division-by-almost-zero right back. Left raw it
    // stays comfortably positive, so the renormalisation below is always well conditioned.
    // NO signed expansion - the components are stored RAW. Measured over 4800 texels from
    // 300 directional maps: taking (r,g,b) as-is gives a mean vector length of 0.955 with
    // a standard deviation of 0.041, and half the texels land within 5% of exactly 1.0
    // (the shortfall is DXT1 quantisation). Every signed-expansion variant tried -
    // including (r,g,b)*2-1 and the r/b-signed-with-g-raw form previously used here -
    // produces lengths of 0.28 to 0.64 and NOTHING near unity. A unit-length field is what
    // a direction is; nothing else in this format has a reason to be normalised.
    // Channel ORDER is the remaining unknown: permuting components can't change a length,
    // so this test cannot distinguish them. G is taken as the normal-ward axis because it
    // is systematically the largest (mean 158 vs 127 for r and b), which is what you expect
    // when light usually arrives from above the surface. Getting x/y backwards would shear
    // the lighting along a tangent axis, not produce artifacts.
    vec3 bakedLightDirTS = vec3(bakedDirSample.r, bakedDirSample.b, bakedDirSample.g);
    float bakedLen = length(bakedLightDirTS);
    bakedLightDirTS = bakedLen > 0.0F ? bakedLightDirTS / bakedLen : vec3(0.0F, 0.0F, 1.0F);

    // N.L against the TANGENT-space shading normal, matching the captured shader, which
    // never leaves tangent space for this term.
    // BUMP FADE stand-in. The game scales the normal's derivatives by vViewTS.w - a
    // clamped 0..1 per-vertex distance factor - BEFORE normalising, so normals flatten
    // toward (0,0,1) with distance. We have no source for that value, and applying the
    // derivatives at full strength is the worst case for this term: the light directions
    // are all-positive, normalising to roughly (0.52, 0.52, 0.65), so
    //     dot ~= 0.52*dx + 0.52*dy + 0.65
    // goes NEGATIVE whenever dx + dy < -1.25, and the clamp below then drives the pixel to
    // exactly zero no matter how bright the lightmap texel is. That is what punches pitch
    // black holes through otherwise correct baked colour.
    // Softening the derivatives here restores the property that actually matters: with a
    // flat normal the / lightDirTS.z renormalisation reproduces the baked value exactly, so
    // the normal map MODULATES the bake instead of being able to cancel it. 1.0 is the
    // unfaded game behaviour, 0.0 is the pure bake.
    // This only affects the BAKED term - the dynamic sun path keeps the full-strength
    // normal, since it has no baked intensity to preserve.
    vec3 bakedNormalTS = normalize(vec3(derivativeSum * uBakedBumpFade, 1.0F));
    float bakedNdotL = clamp(dot(bakedLightDirTS, bakedNormalTS), 0.0F, 1.0F);
    // Directional-lightmap renormalisation: dividing by the light's own .z makes a FLAT
    // normal reproduce the baked intensity exactly, so the normal map only modulates
    // around it instead of darkening everything. This division is the signature of the
    // technique, and the reason the bake can be authored independently of the normal map.
    float bakedDiffuse = bakedLightDirTS.z > 0.0F ? bakedNdotL / bakedLightDirTS.z : bakedNdotL;
    // NOT gamma-decoded, unlike the albedo. This is a light INTENSITY map, not an
    // sRGB-authored colour: measured across metropolis its median texel is 41/255 (~0.16),
    // and a 2.2 decode drops that to 0.016 - the whole scene reads as black. Only the
    // albedo gets the sRGB round trip; light values enter the linear math as stored.
    // EXPOSURE. The bake is genuinely dark as stored: measured on metropolis's terrain
    // atlases, non-black texels average 32/255 (~0.13), so albedo * light lands near black
    // even though the atlas itself is correct - verified by unswizzling one and confirming
    // 11 of 13 sampled UFrag islands land on lit data with their own atlas.
    // The game has a multiplier here that we cannot source: diffuseTerm = lightScale * nDotL,
    // where lightScale is tc1.z, i.e. the per-draw vertex constant vc[2].z. Packing baked
    // HDR lighting into 8 bits and scaling back up on read is the normal reason for such a
    // constant to exist. This stands in for it.
    // Raise until lit surfaces match the game; true-black texels stay black either way,
    // since this scales rather than lifts. If shadows then look too absolute rather than
    // too dark, that residue is the missing environment-cubemap fill, not this value.
    vec3 bakedDiffuseLight = bakedColour.rgb * bakedDiffuse * uBakedLightScale;

    // ---- COMPOSITE ------------------------------------------------------------------
    // Baked lighting replaces the dynamic sun entirely where present - they are two
    // answers to the same question, and summing them would double-light the scene.
    // Emissive stays INSIDE the light term either way, so it multiplies by albedo.
    // Surfaces WITHOUT a decoded bake are lit by the level's ANALYTIC LIGHTING ENVIRONMENT
    // (section 0x8b00): an ambient plus two directional lights - the game's OWN sun/ambient,
    // uploaded into the fragment constant bank at runtime and confirmed against a capture (see
    // LightingEnvironmentReader). This corrects a long-standing wrong assumption here that "the game
    // has no dynamic light / no analytic light data in the level files" - it does, this is it. The
    // fabricated editor sun is only the fallback now (uEnvHasLighting = 0 for levels without one).
    // N.L per light; directions are stored pointing toward the light.
    // Still NOT the whole story: the game also modulates undecoded surfaces by a per-vertex baked
    // term (tc1.x = fract(abs(in_pos.w) * vc[1].zw).x * vc[11].z, from UFragVertex.unk /
    // VertexFormat0.boneIndex - the WWS debrief confirms baked lighting is a MIX of lightmaps and
    // per-vertex data), which needs a vertex-constants capture to decode. But this is the game's
    // real light rig rather than an invented one.
    vec3 envDiffuse = uEnvAmbient
        + uEnvLight0Colour * max(dot(worldNormal, uEnvDirection0), 0.0F)
        + uEnvLight1Colour * max(dot(worldNormal, uEnvDirection1), 0.0F);
    vec3 undecodedFill = mix(vec3(uAmbient), envDiffuse, uEnvHasLighting);
    vec3 lighting = mix(undecodedFill, bakedDiffuseLight, hasBaked) + emissiveIntensity;

    // The game modulates specular by the baked light buffer's ALPHA (monochrome specular
    // light, per the deck's "don't allocate a separate specular buffer" optimisation).
    // Note 84% of this level's lightmaps are DXT1 and therefore have no authored alpha -
    // those decode to 1.0, leaving specular unattenuated, which is the correct outcome for
    // a channel that was never authored.
    float bakedSpecLight = mix(1.0F, bakedColour.a, hasBaked);
    // ENVIRONMENT FILL - a flat approximation of the game's cubemap reflection, which is
    // additive and NOT gated by the lightmap. That is what stops baked shadows reaching
    // pure black in the game: our shadowed areas fall to exactly albedo * 0 because we have
    // no environment term at all. Metropolis's cubemap is a near-uniform grey, so a single
    // averaged colour reproduces most of its contribution.
    // TINTED BY ALBEDO, which is the part that matters. The captured shader computes
    //     specularTint = fma(albedo - specTintPivot, specTintContrast, specTintOffset)
    // and then specular = envColour * specularTint * specularLight * specularAmount, so the
    // reflection carries the surface's own hue. An UNTINTED version of this term was what
    // desaturated the whole scene: adding a uniform grey after the albedo multiply pushes
    // every colour toward grey by construction, which read as "everything looks white /
    // not coloured" while the game stays warm and saturated.
    // The three tint constants are unidentified, so this uses albedo directly - the
    // contrast=1, pivot=0, offset=0 collapse of that expression. Recovering the real
    // constants (fc[6].xyz, fc[7]) needs the fragment-constants buffer from a capture.
    vec3 specularTint = albedo;
    // The ACTUAL cubemap, sampled along the world reflection vector, in place of the single
    // averaged uEnvironmentColour that used to stand in for it. The RGB channels are a mantissa;
    // alpha is the HDR exponent. This matches TexturesExplorer's cubemap preview path, so the
    // reflection term sees the same signal that the debug/export view shows.
    vec4 envTexel = texture(samplerCube(fEnvCube, fEnvCubeSampler), reflDir);
    float envExposure = exp2((envTexel.a * 255.0F - 128.0F) / 16.0F);
    vec3 envColour = envTexel.rgb * envExposure;
    // Reflectivity: the material's own specular map PLUS a Schlick-Fresnel base, so the environment
    // reflects on flat surfaces too - this game reads as reflective almost everywhere, not only on
    // specular-mapped texels. uReflectionBase is the head-on reflectance F0: 0 collapses to the old
    // "specular map only" behaviour, up to 1 is a near-mirror everywhere; grazing angles always rise
    // toward full reflection regardless. The cube is sampled at mip 0 (it has no mip chain), so the
    // reflection is perfectly sharp - roughness 0 - which is what the game shows.
    float NdotV = clamp(dot(worldNormal, viewDir), 0.0F, 1.0F);
    float fresnel = uReflectionBase + (1.0F - uReflectionBase) * pow(1.0F - NdotV, 5.0F);
    float reflectivity = clamp(specIntensity + fresnel, 0.0F, 1.0F);
    vec3 envFill = envColour * specularTint * uEnvironmentIntensity * reflectivity * bakedSpecLight;
    // The dynamic sun's Phong highlight is gated OFF where a bake exists, same rule as the
    // diffuse term: the game's specular on baked surfaces IS the cubemap reflection (which
    // envFill stands in for), not a directional-light lobe. Leaving both on double-counted
    // specular on every baked surface (read as "speculars look exaggerated").
    // No Phong lobe at all: it modelled a directional light this game does not have. The
    // only specular the game applies is the cubemap reflection, which envFill approximates.
    vec3 specPart = envFill;

    vec3 finalColor = albedo * lighting + specPart;

    // Debug: the raw bake with no albedo or shading. Baked surfaces show their scaled
    // lightmap texel, everything else drops to flat mid-grey - so a black surface is
    // immediately either "the bake is black here" or "shading is killing it".
    if (uBakedDebugView > 0.5F) {
        finalColor = mix(vec3(0.25F), bakedColour.rgb * uBakedLightScale, hasBaked);
    }

    // Debug: show the cubemap reflection directly on every surface, ungated by specIntensity /
    // uEnvironmentIntensity (which make it near-invisible in normal shading). This is the sample
    // the reflection term actually uses - envColour = envTexel.rgb * exp2(alpha) along the world
    // reflection vector - so it doubles as an axis-orientation check: a mirror ball would show the
    // environment reflected the way the game does. Gamma-encoded below like everything else.
    if (uReflectionDebug > 0.5F) {
        finalColor = envColour;
    }

    finalColor = pow(finalColor, vec3(1.0F / 2.2F));
    fFragColor = vec4(finalColor, texelColor.a * maps[0].color.a * fColor.a);
}
