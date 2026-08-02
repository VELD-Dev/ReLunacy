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
