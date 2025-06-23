#version 440 core

out vec4 color;

in vec2 UVs;

uniform sampler2D albedo;
uniform bool useTexture;
uniform bool isSelected;

void main()
{
	if(isSelected)
	{
		color = vec4(1, 1, 1, 1);
	}
	else
	{
		color = vec4(0.7f, 0.7f, 0.7f, 1);
	}
}