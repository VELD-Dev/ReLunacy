namespace ReLunacy.Engine.Rendering.Shaders;

// First real lighting pass for the live renderer (everything else in this engine is unlit — see
// GlobalResource.DefaultModelEffect / VertexAlphaModelShaderSource). The shading MODEL follows
// Insomniac's own "Prelighting" / "Pre-lighting in Resistance 2" (Mark Lee) decks — the same
// tech family Tools of Destruction shipped on: diffuse = albedo * sum(l_col * l_att * (n.l)),
// specular = gloss * sum((l_dir . Refl(v,n))^p * l_col * l_att) with a PHONG reflection vector
// (the decks' Refl(v,n) form), combined as C = mp * P, all evaluated in linear space with sRGB
// decode/encode at the edges. What is deliberately NOT ported from those decks is the screen-space
// deferred ARCHITECTURE (depth/normal pre-pass, light-accumulation buffers, stencil light
// volumes, sun-shadow min-blend buffers): that machinery only pays for itself with many dynamic
// lights and shadow casters, and this editor has exactly one configurable sun and no parsed light
// data (none exists in the level files) — a forward evaluation of the identical equations produces
// the identical shading. Material inputs come from the game's own packed "properties"/"expensive"
// texture (see AssetManager.GetOrBuildMaterial's "fProperties" map) — pure intensity channels,
// never tints. Current best-known layout per live experimentation: R=specular intensity,
// G=parallax height, B=emissive intensity, A=roughness. Roughness scales the scene-wide
// EditorSettings.LightSpecularPower per pixel — the decks' own per-pixel specular power idea (R2
// kept it in the normal buffer's alpha), just sourced from this map's alpha instead. NOTE:
// GltfExporter.ApplyExpensiveChannels still splits this texture under the older
// R=spec/G=metallic/B=emissive reading and needs revisiting once this layout is confirmed.
// Always forwards vColor.a like
// VertexAlphaModelShaderSource does — see AssetManager.SelectEffect, which uses this one effect
// for every material regardless of UsesVertexAlphaCandidate once lighting is enabled, since
// vColor.a is already 1.0 for non-vertex-alpha materials (ConvertGeometryToVertices) so folding
// both into one shader is a safe simplification rather than needing 4 effect variants.
internal static class LitModelShaderSource
{
    public const string Vertex = """
        #version 450

        layout(std140, set = 0, binding = 0) uniform MatrixBuffer {
            mat4x4 uProjection;
            mat4x4 uView;
        };

        layout(std140, set = 1, binding = 0) uniform TransformBuffer {
            mat4x4 uTransformation;
        };

        layout (location = 0) in vec3 vPosition;
        layout (location = 1) in vec2 vTexCoords;
        layout (location = 2) in vec2 vTexCoords2;
        layout (location = 3) in vec3 vNormal;
        layout (location = 4) in vec4 vTangent;
        layout (location = 5) in vec4 vColor;

        layout (location = 0) out vec2 fTexCoords;
        layout (location = 1) out vec4 fColor;
        layout (location = 2) out vec3 fWorldNormal;
        layout (location = 3) out vec3 fWorldTangent;
        layout (location = 4) out float fTangentHandedness;
        layout (location = 5) out vec3 fWorldPos;

        void main() {
            fTexCoords = vTexCoords;
            fColor = vColor;

            // A proper inverse-transpose normal matrix, not just the upper 3x3 of the world
            // transform - the naive matrix only happens to give the right answer for the special
            // case of a pure +-1-magnitude axis flip with no real scaling; this engine's actual
            // per-asset/per-instance Scale is an arbitrary float (moby.Scale, instance placement
            // scale, etc.), and for any OTHER scale magnitude - including negative ones used to
            // bake in a mirrored placement, which this game does often instead of an actual
            // rotation - the naive transform distorts the normal instead of just mirroring it,
            // which is what was reading as "shading looks inverted" on those instances.
            mat3 modelMatrix3 = mat3(uTransformation);
            mat3 normalMatrix = transpose(inverse(modelMatrix3));
            fWorldNormal = normalize(normalMatrix * vNormal);
            fWorldTangent = normalize(normalMatrix * vTangent.xyz);

            // Separately from the normal matrix above: a mirrored (negative-determinant) instance
            // transform also flips the surface's effective winding, so the TANGENT-SPACE
            // reconstruction in the fragment shader needs its bitangent handedness flipped to
            // match, or per-pixel normal-map detail comes out inverted even once the plain
            // per-vertex normal above is correct.
            fTangentHandedness = vTangent.w * sign(determinant(modelMatrix3));

            mat4x4 transformation = uTransformation;
            vec4 v4Pos = vec4(vPosition, 1.0F);
            vec4 worldPos = transformation * v4Pos;
            fWorldPos = worldPos.xyz;
            gl_Position = uProjection * uView * worldPos;
        }
        """;

    public const string Fragment = """
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

        layout (set = 3, binding = 0) uniform texture2D fAlbedo;
        layout (set = 3, binding = 1) uniform sampler fAlbedoSampler;

        layout (set = 4, binding = 0) uniform texture2D fNormal;
        layout (set = 4, binding = 1) uniform sampler fNormalSampler;

        layout(std140, set = 5, binding = 0) uniform LightBuffer {
            vec3 uLightDirection;
            float uAmbient;
            vec3 uLightColor;
            float uSpecularPower;
            vec3 uCameraPosition;
            float _reserved0;
        };

        layout (set = 6, binding = 0) uniform texture2D fProperties;
        layout (set = 6, binding = 1) uniform sampler fPropertiesSampler;

        layout (location = 0) in vec2 fTexCoords;
        layout (location = 1) in vec4 fColor;
        layout (location = 2) in vec3 fWorldNormal;
        layout (location = 3) in vec3 fWorldTangent;
        layout (location = 4) in float fTangentHandedness;
        layout (location = 5) in vec3 fWorldPos;

        layout (location = 0) out vec4 fFragColor;

        void main() {
            vec3 n = normalize(fWorldNormal);
            vec3 t = normalize(fWorldTangent - n * dot(fWorldTangent, n));
            vec3 b = cross(n, t) * fTangentHandedness;
            mat3 tbn = mat3(t, b, n);

            vec3 viewDir = normalize(uCameraPosition - fWorldPos);

            // Single-tap parallax offset from the expensive map's G channel, which live
            // experimentation identified as a parallax HEIGHTMAP (not metallic, as earlier
            // guessed - heightmaps are mid-range nearly everywhere, which is exactly why every
            // metallic interpretation produced broad scene-wide artifacts). Height 0 = flat/no
            // offset (so empty heightmaps parallax nothing at all), 1 = maximum displacement
            // toward the viewer - per the project owner, NOT the centered 0.5-neutral convention.
            // The z clamp limits swim/shimmer at grazing view angles; the scale is deliberately
            // subtle while this channel's meaning is still "likely", not confirmed. If surface
            // relief visibly moves the WRONG way when orbiting, negate the offset.
            // maps[2].value is the per-material parallax multiplier (default 1), live-tunable
            // from the ShaderBrowser - see AssetManager.SetParallaxMultiplier. Offset-LIMITED
            // parallax (no division by viewDirTS.z): the classic divide amplifies the UV shift
            // toward infinity at grazing view angles, which with a single tap shreds the
            // albedo/normal sampling into blocky swimming artifacts (confirmed live: read as
            // "pixelated artifacts over the albedo"). Dropping the divide caps the shift at
            // height * scale texels no matter the angle - the standard single-tap-friendly form.
            vec3 viewDirTS = transpose(tbn) * viewDir;
            float height = texture(sampler2D(fProperties, fPropertiesSampler), fTexCoords).g;
            const float PARALLAX_SCALE = 0.02F;
            float parallaxScale = PARALLAX_SCALE * maps[2].value;
            vec2 texCoords = fTexCoords - viewDirTS.xy * (height * parallaxScale);

            vec4 texelColor = texture(sampler2D(fAlbedo, fAlbedoSampler), texCoords);

            switch (renderMode) {
                case 0:
                    texelColor.a = 1.0F;
                    break;
                case 1:
                    // maps[0].value carries the material's own alphaClip threshold from the
                    // game's shader metadata (see AssetManager.GetOrBuildMaterial), replacing a
                    // hardcoded 0.99: that constant was invisible under point sampling (alpha is
                    // mostly pure 0/255) but under bilinear filtering every softened edge texel
                    // falls below 0.99 and gets discarded, eroding cutout foliage/decals into
                    // sparse pixel speckle (confirmed live).
                    if (texelColor.a < maps[0].value) {
                        discard;
                    }
                    break;
            }

            // This game's normal maps store partial derivatives (dx=-nx/nz, dy=-ny/nz), not a
            // standard tangent-space (nx,ny,nz) encoding - B is always constant/unused, R unused.
            // Reconstruction is nx=-dx, ny=-dy, nz=1, normalized, all in tangent space - the
            // formula itself is confirmed against Insomniac's own "Prelighting" (Mark Lee) slides.
            // Channel assignment is dx=Alpha, dy=Green - confirmed against Negotiator/TextureEditor
            // (a separate, working reverse-engineering tool for this exact game's formats -
            // TextureHelper.BitmapFromDDS's DXT5 normal-map path reads p.A for dx and p.G for dy),
            // not G=dx/A=dy as originally guessed here. See TextureUtils.ReconstructNormalMap for
            // the export-side equivalent.
            vec4 normalSample = texture(sampler2D(fNormal, fNormalSampler), texCoords);
            float dx = normalSample.a * 2.0F - 1.0F;
            float dy = normalSample.g * 2.0F - 1.0F;
            vec3 tangentNormal = normalize(vec3(-dx, -dy, 1.0F));
            vec3 worldNormal = normalize(tbn * tangentNormal);

            // The expensive map's channels are pure INTENSITIES, never color/tint sources (the
            // only color a surface has is its albedo - emissive glows in the albedo's own color,
            // specular flashes in the LIGHT's color). Current best-known layout, per the project
            // owner's live experimentation: R = specular intensity, G = parallax height (sampled
            // above, pre-offset), B = emissive intensity, A = roughness. Every earlier variant
            // that promoted a channel into a tint (constant-white specTint, albedo-tinted env
            // fill, spec-channel-tinted fill) produced a scene-wide artifact in live testing
            // (white filter / pitch-black metals) - keep this a pure intensity model.
            vec4 propsSample = texture(sampler2D(fProperties, fPropertiesSampler), texCoords);
            float specIntensity = propsSample.r;
            float emissiveIntensity = propsSample.b;
            float roughness = propsSample.a;

            // All lighting happens in LINEAR space - the game's own pipeline lit in linear and the
            // source albedo textures are sRGB-authored, so lighting the raw gamma values (what this
            // shader originally did) double-darkens every midtone and crushes shadowed areas to
            // black. Approximate 2.2 decode here, matching encode at the end.
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

            // Roughness narrows or broadens the highlight by scaling the Phong exponent:
            // roughness 0 = full uSpecularPower (tight, sharp), roughness 1 = exponent 1 (broad,
            // dull). The decks kept a per-pixel specular power the same way (R2 stored it in the
            // normal buffer's alpha); this format carries it in the expensive map's alpha instead.
            float specPower = max(uSpecularPower * (1.0F - roughness), 1.0F);
            // Normalized (energy-conserving) Phong, relative to the slider's own power so the
            // slider still means "brightness at the power you chose": a broader lobe spreads the
            // same light over more directions, so it must be proportionally DIMMER. Without this,
            // any pixel whose roughness lands high - including every DXT1-compressed expensive
            // map, whose missing alpha decodes as 255 = fully rough - got a power-1 lobe at FULL
            // strength, which covers the whole sun-facing hemisphere and reads as a
            // view-independent white film (confirmed live).
            float specNorm = (specPower + 2.0F) / (uSpecularPower + 2.0F);
            float specAccum = pow(max(dot(lightDir, reflDir), 0.0F), specPower) * specNorm;

            // The decks' baked-lighting/lightmap input has no equivalent here (no light data in
            // the level files at all). A FLAT ambient stand-in made every face turned away from
            // the sun an identical dead value (confirmed live: crates/props in shadowed
            // orientations read "unshaded" next to sunlit neighbors), so this is a two-tone
            // hemisphere instead: full ambient from above fading to half toward straight down -
            // the cheapest stand-in that keeps shadowed geometry readable and directional.
            vec3 ambientLight = uAmbient * mix(vec3(0.5F), vec3(1.0F), worldNormal.y * 0.5F + 0.5F);

            vec3 diffusePart = albedo * (ambientLight + diffuseAccum);
            vec3 specPart = uLightColor * specAccum * specIntensity;

            vec3 finalColor = diffusePart + specPart + albedo * emissiveIntensity;
            finalColor = pow(finalColor, vec3(1.0F / 2.2F));
            fFragColor = vec4(finalColor, texelColor.a * maps[0].color.a * fColor.a);
        }
        """;
}
