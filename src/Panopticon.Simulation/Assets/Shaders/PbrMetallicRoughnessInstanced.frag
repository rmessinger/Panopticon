#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"
#include "ForwardPass.h"

// Instanced variant of the stock fragment stage (B5-2): byte-for-byte the same shading;
// only the Mesh.h include drops the per-item constant blocks (this stage reads none of
// them, and the instance stream is vertex-stage-only).
#define MESH_DECLARE_ITEM_CONSTANTS 0
#include "Mesh.h"

// Engine-owned core metallic-roughness lighting. Uses the existing global lighting rig
// (directional color + ambient per light) with a Cook-Torrance GGX/Smith/Schlick BRDF.
// Base-color and emissive samples decode from sRGB; metallic-roughness, normal, and
// occlusion stay linear. The render target stays non-sRGB until G6, so the final linear
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

const float PI = 3.14159265359;

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

float DistributionGGX(float nDotH, float alpha)
{
    float alpha2 = alpha * alpha;
    float d = nDotH * nDotH * (alpha2 - 1.0) + 1.0;
    return alpha2 / (PI * d * d);
}

float GeometrySmith(float nDotV, float nDotL, float alpha)
{
    float k = alpha / 2.0;
    float gV = nDotV / (nDotV * (1.0 - k) + k);
    float gL = nDotL / (nDotL * (1.0 - k) + k);
    return gV * gL;
}

vec3 FresnelSchlick(float cosTheta, vec3 f0)
{
    return f0 + (vec3(1.0) - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

void EvaluateLight(
    Light light,
    vec3 worldPosition,
    vec3 normal,
    vec3 viewDirection,
    vec3 albedo,
    float metallic,
    float roughness,
    vec3 f0,
    out vec3 direct,
    out vec3 ambient)
{
    vec3 lightDirection = -light.Direction;
    float nDotL = saturate(dot(normal, lightDirection));

    ambient = light.Ambient;

    if (nDotL <= 0.0)
    {
        direct = vec3(0.0);
        return;
    }

    vec3 halfVector = normalize(lightDirection + viewDirection);
    float nDotV = saturate(dot(normal, viewDirection));
    float nDotH = saturate(dot(normal, halfVector));
    float vDotH = saturate(dot(viewDirection, halfVector));

    float alpha = roughness * roughness;

    float d = DistributionGGX(nDotH, alpha);
    float g = GeometrySmith(nDotV, nDotL, alpha);
    vec3 f = FresnelSchlick(vDotH, f0);

    vec3 specular = (d * g * f) / max(4.0 * nDotV * nDotL, 0.001);
    vec3 diffuse = (vec3(1.0) - f) * (vec3(1.0) - metallic) * albedo;

    direct = (diffuse / PI + specular) * light.Color * nDotL;
}

void main()
{
    int flags = _MaterialConstants.Flags;

    vec4 baseColor = _MaterialConstants.BaseColorFactor;
    if ((flags & PBR_FLAG_HAS_BASE_COLOR_TEXTURE) != 0)
    {
        vec4 texel = texture(
            sampler2D(BaseColorTexture, BaseColorSampler),
            SelectUV(_MaterialConstants.BaseColorUvSet));
        // Under the linear preset the upload is sRGB, so sampling already decoded the
        // texel; the local transfer only applies to the Legacy gamma pipeline.
        baseColor *= _GlobalLightingConstantsPS.LinearColourSpace
            ? texel
            : vec4(SrgbToLinear(texel.rgb), texel.a);
    }

    if (_MaterialConstants.AlphaMode == PBR_ALPHA_MODE_MASK
        && baseColor.a < _MaterialConstants.AlphaCutoff)
    {
        discard;
    }

    float metallic = _MaterialConstants.MetallicFactor;
    float roughness = _MaterialConstants.RoughnessFactor;
    if ((flags & PBR_FLAG_HAS_METALLIC_ROUGHNESS) != 0)
    {
        // Roughness in G, metallic in B; both linear.
        vec3 texel = texture(
            sampler2D(MetallicRoughnessTexture, MetallicRoughnessSampler),
            SelectUV(_MaterialConstants.MetallicRoughnessUvSet)).rgb;
        roughness *= texel.g;
        metallic *= texel.b;
    }

    vec3 normal = normalize(in_WorldNormal);
    if ((flags & PBR_FLAG_HAS_NORMAL_TEXTURE) != 0)
    {
        vec3 texel = texture(
            sampler2D(NormalTexture, NormalSampler),
            SelectUV(_MaterialConstants.NormalUvSet)).rgb;
        vec3 tangentSpaceNormal = texel * 2.0 - vec3(1.0);
        tangentSpaceNormal = vec3(tangentSpaceNormal.xy * _MaterialConstants.NormalScale, tangentSpaceNormal.z);
        tangentSpaceNormal = normalize(tangentSpaceNormal);

        mat3 tangentToWorld = mat3(
            in_WorldTangent,
            in_WorldBinormal,
            normal);
        normal = normalize(tangentToWorld * tangentSpaceNormal);
    }

    if ((flags & PBR_FLAG_DOUBLE_SIDED) != 0 && !gl_FrontFacing)
    {
        normal = -normal;
    }

    float ambientOcclusion = 1.0;
    if ((flags & PBR_FLAG_HAS_OCCLUSION_TEXTURE) != 0)
    {
        float texel = texture(
            sampler2D(OcclusionTexture, OcclusionSampler),
            SelectUV(_MaterialConstants.OcclusionUvSet)).r;
        ambientOcclusion = mix(1.0, texel, _MaterialConstants.OcclusionStrength);
    }

    vec3 albedo = baseColor.rgb;
    vec3 f0 = mix(vec3(0.04), albedo, metallic);
    vec3 viewDirection = normalize(CalculateViewVector(in_WorldPosition));

    vec3 directLighting = vec3(0.0);
    vec3 ambientLighting = vec3(0.0);
    for (int i = 0; i < NUM_LIGHTS; i++)
    {
        vec3 direct;
        vec3 ambient;
        EvaluateLight(
            _GlobalLightingConstantsPS.Object.Lights[i],
            in_WorldPosition,
            normal,
            viewDirection,
            albedo,
            metallic,
            roughness,
            f0,
            direct,
            ambient);
        directLighting += direct;
        ambientLighting += ambient;
    }

    // Occlusion modulates the summed per-light ambient contribution.
    vec3 color = directLighting + ambientLighting * albedo * ambientOcclusion;

    vec3 emissive = _MaterialConstants.EmissiveFactor.rgb;
    if ((flags & PBR_FLAG_HAS_EMISSIVE_TEXTURE) != 0)
    {
        vec3 texel = texture(
            sampler2D(EmissiveTexture, EmissiveSampler),
            SelectUV(_MaterialConstants.EmissiveUvSet)).rgb;
        emissive *= _GlobalLightingConstantsPS.LinearColourSpace ? texel : SrgbToLinear(texel);
    }
    color += emissive;

    // The linear preset defers the sRGB transfer to the output pass.
    out_Color = _GlobalLightingConstantsPS.LinearColourSpace
        ? vec4(color, baseColor.a)
        : vec4(LinearToSrgb(color), baseColor.a);
}
