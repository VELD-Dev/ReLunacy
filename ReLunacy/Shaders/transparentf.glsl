#version 440 core

layout(location = 0) out vec4 accum;
layout(location = 1) out float reveal;

layout(location = 0) in vec2 UVs;

layout(set = 0, binding = 0) uniform sampler2D albedo;
layout(set = 1, binding = 1) uniform Parameters
{
	bool useTexture;
} params;

void main()
{
	vec4 color;
	if(params.useTexture)
	{
		color = texture(albedo, UVs);
	}
	else
	{
		color = vec4(1.0, 0.0, 1.0, 1.0);
	}

	reveal = color.a;
	float weight = clamp(pow(min(1.0, color.a * 10.0) + 0.01, 3.0) * 1e8 * pow(1.0 - gl_FragCoord.z * 0.9, 3.0), 1e-2, 3e3);
	accum = vec4(color.rgb * color.a, color.a);
}