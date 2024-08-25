#version 440 core

out vec4 color;

in vec2 UVs;

uniform sampler2D albedo;
uniform bool useTexture;
uniform float alphaClip;
uniform mat4 dissolvePattern;

void main()
{
	if(useTexture)
	{
		color = texture(albedo, UVs);
		if(color.a > alphaClip)
		{
			vec2 pixel = vec2(gl_FragCoord.x, gl_FragCoord.y);
			ivec2 patternPos = ivec2(int(mod(pixel.x, 4.0)), int(mod(pixel.y, 4.0)));
			if(dissolvePattern[patternPos.x][patternPos.y] > color.a) discard;
		}
		if(color.a < alphaClip) discard;
		if(color.a == 0) discard;
	}
	else
	{
		color = vec4(1.0, 0.0, 1.0, 1.0);
	}
}