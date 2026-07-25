#version 450 core

layout(location = 0) out uint outId;

layout(set = 0, binding = 0) uniform sampler2D albedo;

layout(set = 1, binding = 1) uniform Parameters
{
    vec4 objectId; // objectId.x = encoded ID
} params;

void main()
{
    uint id = uint(params.objectId.x + 0.5);
    outId = id;
}
