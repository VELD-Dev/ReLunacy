#version 450

layout(location = 0) out vec4 color;

layout(location = 0) in vec2 UVs;

layout(set = 0, binding = 0) uniform sampler2D albedo;
layout(set = 1, binding = 1) uniform Parameters
{
	bool useTexture;
	float alphaClip;
} params;

//uniform bool useTexture;
//uniform float alphaClip;
//uniform mat4 dissolvePattern;

void main()
{
	if(params.useTexture)
	{
		color = texture(albedo, UVs);
		if(color.a > params.alphaClip)
		{
			vec2 pixel = vec2(gl_FragCoord.x, gl_FragCoord.y);
			ivec2 patternPos = ivec2(int(mod(pixel.x, 4.0)), int(mod(pixel.y, 4.0)));
			//if(dissolvePattern[patternPos.x][patternPos.y] > color.a) discard;
		}
		if(color.a < params.alphaClip) discard;
		if(color.a == 0) discard;
	}
	else
	{
		color = vec4(1.0, 0.0, 1.0, 1.0);
	}
}