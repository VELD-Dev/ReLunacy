#version 440 core

layout(location = 0) out vec4 color;

layout(location = 0) in vec2 UVs;

layout(set = 0, binding = 0) uniform sampler2D albedo;
layout(set = 1, binding = 1) uniform Parameters
{
	bool useTexture;
	bool isSelected;
} params;

void main()
{
	if(params.isSelected)
	{
		color = vec4(1, 1, 1, 1);
	}
	else
	{
		color = vec4(0.7f, 0.7f, 0.7f, 1);
	}
}