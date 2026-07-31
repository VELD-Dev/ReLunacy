namespace ReLunacy.Engine.Rendering.Shaders;

// Identical to Bliss's bundled content/bliss/shaders/default_model.vert/.frag, except vColor is
// actually passed through and multiplied in — the bundled version declares vColor as a vertex
// input but never forwards it past the vertex stage (confirmed by reading its real source), so
// there's no way to make ordinary materials consume it without a second shader. Kept as our own
// Effect (see AssetManager.GetVertexAlphaModelEffect) instead of patching the vendored content
// files, which get overwritten by every NuGet restore and are shared by every other material.
internal static class VertexAlphaModelShaderSource
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

            mat4x4 transformation = uTransformation;
            vec4 v4Pos = vec4(vPosition, 1.0F);
            gl_Position = uProjection * uView * transformation * v4Pos;
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
                    // Same clip rule as LitModelShaderSource - read the material's own threshold
                    // instead of the 0.99 constant that used to be here, and compare with <= so a
                    // threshold of 0 (old engine, which clips at zero) still discards fully
                    // transparent texels. See MaterialReader.GetAlphaClip.
                    if (texelColor.a <= maps[0].value) {
                        discard;
                    }
                    break;
            }

            fFragColor = texelColor * maps[0].color * fColor;
        }
        """;
}
