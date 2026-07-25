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

layout (location = 0) out vec4 fFragColor;

void main() {
    // The object ID is encoded in maps[0].color.r
    // renderMode is used to store the ID (cast to int, then back to uint)
    uint id = uint(renderMode);

    fFragColor = vec4(
        float( id        & 0xFFu) / 255.0,
        float((id >> 8)  & 0xFFu) / 255.0,
        float((id >> 16) & 0xFFu) / 255.0,
        float((id >> 24) & 0xFFu) / 255.0
    );
}
