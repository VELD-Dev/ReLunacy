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
// The LIGHTMAP UV set. Proven by the captured vertex program: it writes
// tc0 = (attr1.xy, attr2.xy), and the fragment program samples the baked light colour and
// direction at tc0.zw - i.e. vertex attribute 2, a second UV pair, NOT the base UV.
layout (location = 6) out vec2 fTexCoords2;

void main() {
    fTexCoords = vTexCoords;
    fTexCoords2 = vTexCoords2;
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
