namespace ReLunacy.Engine.Rendering.Shaders;

// Camera-facing sprite cards for foliage. Same buffer/texture layout as
// VertexAlphaModelShaderSource, so it's a drop-in Effect swap (see AssetManager.BuildBillboardModelEffect).
//
// vPosition carries the sprite's anchor; vTexCoords2 carries the 2D corner offset, added after the
// view transform so the card faces the camera. All four vertices of a card share vPosition and
// differ only in vTexCoords2. Instance scale is recovered from the model matrix.
//
// Known differences from the game: card size ignores a per-draw scale constant not present in the
// level files, and cards stay axis-aligned/untilted rather than using the game's per-sprite tilt.
//
// Shader source lives in Shaders/billboardv.glsl / billboardf.glsl (see ShaderAsset). Must stay
// ASCII only, comments included, or the runtime shader compile fails.
internal static class BillboardModelShaderSource
{
    public static string Vertex => ShaderAsset.Load("billboardv.glsl");
    public static string Fragment => ShaderAsset.Load("billboardf.glsl");
}
