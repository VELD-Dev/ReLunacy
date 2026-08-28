namespace ReLunacy.Engine.Rendering.Shaders;

// First real lighting pass for the live renderer (everything else in this engine is unlit - see
// GlobalResource.DefaultModelEffect / VertexAlphaModelShaderSource). The shading MODEL follows
// Insomniac's own "Prelighting" / "Pre-lighting in Resistance 2" (Mark Lee) decks - the same
// tech family Tools of Destruction shipped on: diffuse = albedo * sum(l_col * l_att * (n.l)),
// specular = gloss * sum((l_dir . Refl(v,n))^p * l_col * l_att) with a PHONG reflection vector
// (the decks' Refl(v,n) form), combined as C = mp * P, all evaluated in linear space with sRGB
// decode/encode at the edges. What is deliberately NOT ported from those decks is the screen-space
// deferred ARCHITECTURE (depth/normal pre-pass, light-accumulation buffers, stencil light
// volumes, sun-shadow min-blend buffers): that machinery only pays for itself with many dynamic
// lights and shadow casters, and at the time this was written the editor had exactly one configurable
// sun and no parsed light data - a forward evaluation of the identical equations produces the
// identical shading. (Analytic light data has SINCE been found, in main.dat section 0x8b00; see the
// note further down. The forward-vs-deferred reasoning is unaffected - it is still one rig, not many
// dynamic lights.)
//
// The MATERIAL half of this shader is a direct port of the game's own fragment shader, dumped from
// RPCS3 via RenderDoc and traced in fragment_shader_annotated.glsl. Ported 1:1 from it: parallax
// as height * scale + bias with the offset ADDED and no divide by the view vector's z; the
// expensive map as R=specular, G=height, B=emissive/incandescence, A=DETAIL MASK; normal maps as
// partial derivatives used verbatim as (dx, dy, 1) with no sign flip; the detail map's R,G
// composed into those derivatives by plain ADDITION (the property that motivates the encoding),
// its A added to specular, the whole fetch gated by the expensive alpha (its B channel feeds
// albedo in the game but is deliberately not applied here - see the detail-map notes); per-MATERIAL specular power; and emissive folded inside the light term so it multiplies
// by albedo. Those channels are pure INTENSITIES, never tints - the only colour a surface has is
// its albedo.
//
// BAKED LIGHTING is ported but DISABLED by default - see AssetManager.EnableBakedLighting.
// The game's own lives in zone sections 0x5400 (light colour) and 0x5410 (tangent-space light
// DIRECTION, despite InsomniaToolset naming it "ShadowMap"), indexed PER-INSTANCE. That those
// sections are the shader's tex4/tex14 is now confirmed rather than assumed: the extracted tex14
// has the same distinctive channel statistics as 0x5410 (green always high, red and blue centred
// on 128) and tex4 matches 0x5400's. tex4 is visibly an ATLAS of unwrapped lightmap islands.
// The lightmap UV is the SECOND UV SET, settled by the captured vertex program: it builds
// tc0 = (attr1.xy, attr2.xy) and the bakes are read at tc0.zw. Its five attributes match
// UFragVertex exactly - position, UVs, UVs2, normal, tangent - so that captured draw is TERRAIN
// and UVs2 is the lightmap UV. UFrag.ReadVertices now carries UVs2 through to TexCoords2.
// WHY IT IS STILL OFF: metropolis is the only extracted level, and there UFrags carry no lightmap
// index at all (every candidate field reads 0xFFFF across all 1987, full-record scan), so its
// terrain uses the per-vertex path instead (Insomniac's WWS debrief confirms baked lighting is a
// MIX of lightmaps and per-vertex data, so this is by design, not missing data).
// Its TIES do carry indices - 1728 distinct - but VertexFormat0 has no
// second UV pair, so there is nothing correct to sample them at. Enabling it therefore lights
// nothing on terrain and tiles the atlas on ties. It needs either a level whose UFrags are
// lightmapped, or the tie lightmap-UV source, whichever turns up first.
// Where a bake exists it REPLACES the dynamic sun rather than adding to it - they are two answers
// to the same question. maps[6].value is the flag that selects between them.
//
// The HDR environment cubemap (old-engine section 0x5920) is now decoded and sampled for
// reflections - see the ENVIRONMENT FILL section in the fragment source and CubemapReader.
// Still absent: the per-vertex distance/bump fade, the dedicated detail UV set, the specular-tint
// constants, fog, and the glow-mask alpha output.
//
// SUPERSEDED, and worth reading as a lesson in how to write these notes: this paragraph used to state
// flatly that there is NO analytic light data anywhere in the level files, on the grounds that neither
// InsomniaToolset nor Ymir had found any. That was a "we have not found it YET" - the strongest claim
// the evidence supported was "nobody has located it", which is not the same as "it does not exist".
// It DOES exist: main.dat section 0x8b00 carries a per-level ambient colour plus two directional
// lights, now decoded (LightingEnvironmentReader) and fed to the shader as uEnvAmbient /
// uEnvDirection0/1. So the fallback used where no bake exists is the game's OWN rig, not a fabricated
// editor sun - which also makes it a live question whether that ambient should reach baked surfaces
// too, rather than being replaced by the bake outright. See dev/ShaderExtraction.md.
//
// NOTE: GltfExporter.ApplyExpensiveChannels still splits the expensive texture under the older
// R=spec/G=metallic/B=emissive reading. That is now demonstrably wrong output - G is the parallax
// height, not metallic - and needs correcting separately.
// Always forwards vColor.a like
// VertexAlphaModelShaderSource does - see AssetManager.SelectEffect, which uses this one effect
// for every material regardless of UsesVertexAlphaCandidate once lighting is enabled, since
// vColor.a is already 1.0 for non-vertex-alpha materials (ConvertGeometryToVertices) so folding
// both into one shader is a safe simplification rather than needing 4 effect variants.
//
// Shader source lives in Shaders/litmodelv.glsl / litmodelf.glsl (see ShaderAsset).
internal static class LitModelShaderSource
{
    public static string Vertex => ShaderAsset.Load("litmodelv.glsl");
    public static string Fragment => ShaderAsset.Load("litmodelf.glsl");
}
