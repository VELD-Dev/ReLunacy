#version 440 core

// shader inputs
in vec2 UVs;

// shader outputs
layout (location = 0) out vec4 frag;

// screen image
uniform sampler2D screen;

void main()
{
	frag = vec4(texture(screen, UVs).rgb, 1.0f);
}