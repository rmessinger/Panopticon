#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"

layout(set = 2, binding = 0) uniform texture2D Texture;
layout(set = 2, binding = 1) uniform sampler Sampler;

layout(location = 0) in vec2 in_UV;
layout(location = 1) in vec4 in_Color;

layout(location = 0) out vec4 out_Color;

void main()
{
    vec4 texColor = texture(sampler2D(Texture, Sampler), in_UV);
    out_Color = texColor * in_Color;
}
