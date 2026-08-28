namespace ReLunacy.Engine.Rendering.Shaders;

// Identical to Bliss's bundled content/bliss/shaders/default_model.vert/.frag, except vColor is
// actually passed through and multiplied in - the bundled version declares vColor as a vertex
// input but never forwards it past the vertex stage (confirmed by reading its real source), so
// there's no way to make ordinary materials consume it without a second shader. Kept as our own
// Effect (see AssetManager.GetVertexAlphaModelEffect) instead of patching the vendored content
// files, which get overwritten by every NuGet restore and are shared by every other material.
//
// Shader source lives in Shaders/vertexalphav.glsl / vertexalphaf.glsl (see ShaderAsset).
internal static class VertexAlphaModelShaderSource
{
    public static string Vertex => ShaderAsset.Load("vertexalphav.glsl");
    public static string Fragment => ShaderAsset.Load("vertexalphaf.glsl");
}
