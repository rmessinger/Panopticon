#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"
#include "ForwardPass.h"
#include "TerrainLighting.h"

layout(set = 2, binding = 0) uniform texture2D Texture;
layout(set = 2, binding = 1) uniform sampler Sampler;
layout(set = 2, binding = 2) uniform TerrainDecalConstants
{
    int TerrainLit;
    float _Padding1;
    float _Padding2;
    float _Padding3;
} _TerrainDecalConstants;

layout(location = 0) in vec3 in_WorldPosition;
layout(location = 1) in vec3 in_WorldNormal;
layout(location = 2) in vec2 in_UV;
layout(location = 3) in vec2 in_CloudUV;
layout(location = 4) in float in_ViewSpaceDepth;

layout(location = 0) out vec4 out_Color;

void main()
{
    vec4 color = texture(sampler2D(Texture, Sampler), in_UV);
    vec3 tint = _GlobalLightingConstantsPS.Terrain.Lights[0].Ambient
        + _GlobalLightingConstantsPS.Terrain.Lights[0].Color * 0.5;
    if (_TerrainDecalConstants.TerrainLit != 0)
    {
        tint = GetTerrainDiffuseLighting(in_WorldPosition, in_WorldNormal,
            in_ViewSpaceDepth, ivec2(gl_FragCoord.xy)) * GetCloudColor(in_CloudUV);
    }
    out_Color = vec4(color.rgb * tint, color.a);
}
