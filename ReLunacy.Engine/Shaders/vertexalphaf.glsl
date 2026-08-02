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
