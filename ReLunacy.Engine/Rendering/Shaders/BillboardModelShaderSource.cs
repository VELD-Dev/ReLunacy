namespace ReLunacy.Engine.Rendering.Shaders;

// Camera-facing sprite cards for foliage. Same buffer/texture layout as
// VertexAlphaModelShaderSource and Bliss's default_model (MatrixBuffer@0 vertex,
// TransformBuffer@1 vertex, MaterialBuffer@2 fragment, Albedo@3), so it is a drop-in Effect swap
// with no pipeline differences - see AssetManager.BuildBillboardModelEffect.
//
// The billboard is built the way the game's own foliage vertex program builds it: transform the
// sprite's ANCHOR normally, then add the corner offset in a plane that faces the viewer. Doing the
// add after the view matrix is what makes the card face the camera, because view space already has
// the camera at the origin looking down -Z, so its X/Y axes are the screen axes by construction.
//
// vTexCoords2 carries the 2D corner offset (see EntityFoliage - it is the only free per-vertex
// vec2 in Vertex3D, and foliage has no lightmap UV to compete for it). vPosition carries the
// anchor, NOT the final corner position, which is why every four vertices of a card share the same
// vPosition and differ only in vTexCoords2.
//
// The instance's scale is recovered from the model matrix rather than being lost - see the vertex
// body. Skipping that made every card about 5.9x too large on metropolis, whose foliage placements
// scale by a median of 0.17.
//
// KNOWN DIFFERENCES FROM THE GAME, both from vertex constants that are not in the level files:
//   - the game multiplies the corner offset by vc[41].x as well, a per-draw scale we do not have,
//     so card size is right only up to that constant;
//   - the per-sprite rotation comes from an indexed lookup, vc[42 + a0] / vc[43 + a0], selected by
//     the two packed bytes on each sprite (see Loading.Vertices.FoliageSpriteAnchor). Those
//     constants also carry a Z component, so the game can tilt a card out of the screen plane.
//     Cards here stay axis-aligned to the screen and untilted.
//
// Shader source lives in Shaders/billboardv.glsl / billboardf.glsl (see ShaderAsset). ASCII ONLY in
// those files, comments included - a non-ASCII byte makes the runtime shader compile fail with a
// misleading "unexpected end of file" error.
internal static class BillboardModelShaderSource
{
    public static string Vertex => ShaderAsset.Load("billboardv.glsl");
    public static string Fragment => ShaderAsset.Load("billboardf.glsl");
}
