namespace ReLunacy.Engine.Rendering.Shaders;

// Forward-lit shader: the only lit pass in the engine, everything else is unlit.
//
// Lighting model: Insomniac's Prelighting shading (diffuse = albedo * sum(light_col * atten *
// n.l), specular = gloss * sum((l_dir . Refl(v,n))^p * light_col * atten), Phong reflection,
// linear space with sRGB at the edges). Forward, not deferred - one light rig, no need for a
// G-buffer.
//
// Material channels are a direct port of the game's own fragment shader: parallax = height *
// scale + bias, no divide by view-z; expensive map is R=specular, G=height, B=emissive, A=detail
// mask; normal maps are raw partial derivatives (dx, dy, 1); detail map's RG adds into those
// derivatives, its A adds to specular, gated by the expensive map's alpha; specular power is
// per-material; emissive is folded into the light term so it multiplies by albedo. All of these
// are intensities, never tints - only albedo carries colour.
//
// Baked lighting is ported but off by default (AssetManager.EnableBakedLighting). It lives in
// zone sections 0x5400 (light colour) / 0x5410 (light direction, tangent-space), indexed
// per-instance, sampled at the lightmap UV (the vertex's second UV set). Where a bake exists it
// replaces the dynamic sun (maps[6].value selects between them).
//
// The HDR environment cubemap (old-engine section 0x5920) is decoded and sampled for reflections.
// Still missing: per-vertex distance/bump fade, dedicated detail UV set, specular-tint constants,
// fog, glow-mask alpha output.
//
// Analytic level lighting (main.dat section 0x8b00: ambient + two directional lights) feeds
// uEnvAmbient/uEnvDirection0/1 as the fallback rig where no bake exists.
//
// Always forwards vColor.a (used for every material once lighting is enabled, not just
// vertex-alpha ones - see AssetManager.SelectEffect).
//
// Shader source: Shaders/litmodelv.glsl / litmodelf.glsl (see ShaderAsset).
internal static class LitModelShaderSource
{
    public static string Vertex => ShaderAsset.Load("litmodelv.glsl");
    public static string Fragment => ShaderAsset.Load("litmodelf.glsl");
}
