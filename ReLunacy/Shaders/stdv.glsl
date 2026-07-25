#version 450 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 4) in mat4 aModel;

layout(location = 0) out vec2 UVs;
layout(location = 1) flat out uint instID;

layout(set = 0, binding = 0) uniform UBO { mat4 worldToClip; } ubo;

void main()
{
	UVs = aTexCoord;
	instID = uint(gl_InstanceIndex);
	gl_Position = vec4(aPosition, 1.0) * aModel * ubo.worldToClip;
}