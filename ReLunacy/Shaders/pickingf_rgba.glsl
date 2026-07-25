#version 450 core

layout(location = 0) out vec4 color;

layout(set = 0, binding = 0) uniform sampler2D albedo;

layout(set = 1, binding = 1) uniform Parameters
{
    vec4 objectId; // objectId.x = encoded ID
} params;

void main()
{
    uint id = uint(params.objectId.x + 0.5);
    color = vec4(
        float( id        & 0xFFu) / 255.0,
        float((id >> 8)  & 0xFFu) / 255.0,
        float((id >> 16) & 0xFFu) / 255.0,
        float((id >> 24) & 0xFFu) / 255.0
    );
}
