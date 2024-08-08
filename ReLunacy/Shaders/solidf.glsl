#version 440 core

out vec4 color;

in vec2 UVs;

uniform sampler2D albedo;
uniform bool useTexture;
uniform float alphaClip;

void main()
{
	if(useTexture)
	{
		color = texture(albedo, UVs);
		if(color.a < alphaClip) discard;
	}
	else
	{
		color = vec4(1.0, 0.0, 1.0, 1.0);
	}
}