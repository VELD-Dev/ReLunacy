#version 440 core

out vec4 color;

in vec2 UVs;
flat in uint instID;

uniform float picking;
uniform float maxInst;
uniform float type;

void main()
{
	//picking is the drawable index
	//iID is the instance id, maxInst is the total number of instances
	//type is 0.05 if it's a moby, 0.1 if it's a tie, tfrags unimplemented
	color = vec4(picking, float(instID) / maxInst, float(instID) / maxInst, 1);
}