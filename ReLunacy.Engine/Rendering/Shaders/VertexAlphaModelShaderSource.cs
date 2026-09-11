namespace ReLunacy.Engine.Rendering.Shaders;

// Like Bliss's bundled default_model shader, but actually forwards and multiplies in vColor,
// which the bundled version never passes past the vertex stage. Kept as our own Effect (see
// AssetManager.GetVertexAlphaModelEffect) rather than patching the vendored files, which NuGet
// restores overwrite and which every other material shares.
//
// Shader source lives in Shaders/vertexalphav.glsl / vertexalphaf.glsl (see ShaderAsset).
internal static class VertexAlphaModelShaderSource
{
    public static string Vertex => ShaderAsset.Load("vertexalphav.glsl");
    public static string Fragment => ShaderAsset.Load("vertexalphaf.glsl");
}
