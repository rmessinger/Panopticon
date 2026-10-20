#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"
#include "ForwardPass.h"

layout(set = 2, binding = 1) uniform texture2D ParticleTexture;
layout(set = 2, binding = 2) uniform sampler Sampler;

layout(location = 0) in vec2 in_TexCoords;
layout(location = 1) in vec3 in_Color;
layout(location = 2) in float in_Alpha;

layout(location = 0) out vec4 out_Color;

void main()
{
    vec4 texColor = texture(sampler2D(ParticleTexture, Sampler), in_TexCoords);

    // Authored fades can cross below zero; negative opacity subtracts color when blended.
    texColor = vec4(
        texColor.xyz * in_Color,
        clamp(texColor.w * in_Alpha, 0.0, 1.0));

    // TODO: Alpha test

    out_Color = texColor;
}
