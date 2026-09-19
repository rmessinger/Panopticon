#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"
#include "ForwardPass.h"
#include "Mesh.h"

// Engine-owned core metallic-roughness material scaffold. WI-C3-06 implements the base-color
// path with explicit sRGB handling; metallic-roughness lighting and the remaining texture
// roles arrive in WI-C3-07. The render target stays non-sRGB until G6, so the final linear
// color is encoded to sRGB locally.

#define PBR_FLAG_HAS_BASE_COLOR_TEXTURE     0x01
#define PBR_FLAG_HAS_METALLIC_ROUGHNESS     0x02
#define PBR_FLAG_HAS_NORMAL_TEXTURE         0x04
#define PBR_FLAG_HAS_OCCLUSION_TEXTURE      0x08
#define PBR_FLAG_HAS_EMISSIVE_TEXTURE       0x10
#define PBR_FLAG_DOUBLE_SIDED               0x20

#define PBR_ALPHA_MODE_OPAQUE 0
#define PBR_ALPHA_MODE_MASK   1
#define PBR_ALPHA_MODE_BLEND  2

layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 0) uniform MaterialConstants
{
    vec4 BaseColorFactor;
    vec4 EmissiveFactor;
    float MetallicFactor;
    float RoughnessFactor;
    float NormalScale;
    float OcclusionStrength;
    float AlphaCutoff;
    int AlphaMode;
    int Flags;
    int BaseColorUvSet;
    int MetallicRoughnessUvSet;
    int NormalUvSet;
    int OcclusionUvSet;
    int EmissiveUvSet;
} _MaterialConstants;

layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 1) uniform texture2D BaseColorTexture;
layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 2) uniform texture2D MetallicRoughnessTexture;
layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 3) uniform texture2D NormalTexture;
layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 4) uniform texture2D OcclusionTexture;
layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 5) uniform texture2D EmissiveTexture;

layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 6) uniform sampler BaseColorSampler;
layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 7) uniform sampler MetallicRoughnessSampler;
layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 8) uniform sampler NormalSampler;
layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 9) uniform sampler OcclusionSampler;
layout(set = MATERIAL_CONSTANTS_RESOURCE_SET, binding = 10) uniform sampler EmissiveSampler;

layout(location = 0) in vec3 in_WorldPosition;
layout(location = 1) in vec3 in_WorldNormal;
layout(location = 2) in vec2 in_UV0;
layout(location = 3) in vec2 in_UV1;
layout(location = 4) in vec3 in_WorldTangent;
layout(location = 5) in vec3 in_WorldBinormal;

layout(location = 0) out vec4 out_Color;

vec2 SelectUV(int uvSet)
{
    vec2 uv = uvSet == 1 ? in_UV1 : in_UV0;
    uv.y = 1 - uv.y;
    return uv;
}

vec3 SrgbToLinear(vec3 srgb)
{
    vec3 cutoff = vec3(lessThanEqual(srgb, vec3(0.04045)));
    vec3 low = srgb / 12.92;
    vec3 high = pow((srgb + vec3(0.055)) / 1.055, vec3(2.4));
    return mix(high, low, cutoff);
}

vec3 LinearToSrgb(vec3 linear)
{
    vec3 cutoff = vec3(lessThanEqual(linear, vec3(0.0031308)));
    vec3 low = linear * 12.92;
    vec3 high = vec3(1.055) * pow(linear, vec3(1.0 / 2.4)) - vec3(0.055);
    return mix(high, low, cutoff);
}

void main()
{
    vec4 baseColor = _MaterialConstants.BaseColorFactor;

    if ((_MaterialConstants.Flags & PBR_FLAG_HAS_BASE_COLOR_TEXTURE) != 0)
    {
        vec4 texel = texture(
            sampler2D(BaseColorTexture, BaseColorSampler),
            SelectUV(_MaterialConstants.BaseColorUvSet));
        baseColor *= vec4(SrgbToLinear(texel.rgb), texel.a);
    }

    if (_MaterialConstants.AlphaMode == PBR_ALPHA_MODE_MASK
        && baseColor.a < _MaterialConstants.AlphaCutoff)
    {
        discard;
    }

    vec3 linearColor = baseColor.rgb;

    out_Color = vec4(LinearToSrgb(linearColor), baseColor.a);
}
