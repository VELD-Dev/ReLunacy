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
// ASCII ONLY below this point - non-ASCII characters anywhere in these strings, including in
// comments, make the runtime shader compile fail with a misleading syntax error.
internal static class BillboardModelShaderSource
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

        void main() {
            fTexCoords = vTexCoords;
            fColor = vColor;

            // Anchor into view space, then offset along the view axes so the quad always faces
            // the camera. vTexCoords2 is the card-local corner offset.
            vec4 anchorView = uView * uTransformation * vec4(vPosition, 1.0F);

            // The offset is added AFTER the model matrix, so it would otherwise miss the
            // instance's scale entirely and every card would render at asset-local size. Recover
            // that scale from the model matrix's own basis vectors - column 0 and column 1 are the
            // X and Y axes, and their lengths are the scale on each. Foliage placements are
            // uniformly scaled in practice (measured over all 757 on metropolis: X, Y and Z basis
            // lengths agree to 0.0000 relative), so taking them per-axis costs nothing and stays
            // correct if a level ever scales non-uniformly.
            vec2 instanceScale = vec2(length(uTransformation[0].xyz), length(uTransformation[1].xyz));
            anchorView.xy += vTexCoords2 * instanceScale;

            gl_Position = uProjection * anchorView;
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

        layout (location = 0) in vec2 fTexCoords;
        layout (location = 1) in vec4 fColor;

        layout (location = 0) out vec4 fFragColor;

        void main() {
            vec4 texelColor = texture(sampler2D(fAlbedo, fAlbedoSampler), fTexCoords);

            switch (renderMode) {
                case 0:
                    texelColor.a = 1.0F;
                    break;
                case 1:
                    // Same clip rule as the other effects: the material's own threshold, compared
                    // with <= so a threshold of 0 (old engine, which clips at zero) still discards
                    // fully transparent texels. See MaterialReader.GetAlphaClip.
                    if (texelColor.a <= maps[0].value) {
                        discard;
                    }
                    break;
            }

            fFragColor = texelColor * maps[0].color * fColor;
        }
        """;
}
