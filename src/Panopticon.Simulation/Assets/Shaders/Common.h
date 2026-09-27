#ifndef COMMON_H

#define COMMON_H

#define GLOBAL_CONSTANTS_RESOURCE_SET 0
#define PASS_CONSTANTS_RESOURCE_SET 1
#define MATERIAL_CONSTANTS_RESOURCE_SET 2
#define RENDER_ITEM_CONSTANTS_RESOURCE_SET 3

struct GlobalConstantsType
{
    vec3 CameraPosition;
    float TimeInSeconds;

    mat4 ViewProjection;
    vec4 ClippingPlane1;
    vec4 ClippingPlane2;
    bool HasClippingPlane1;
    bool HasClippingPlane2;

    vec2 ViewportSize;
};

layout(set = GLOBAL_CONSTANTS_RESOURCE_SET, binding = 0) uniform GlobalConstants
{
    GlobalConstantsType _GlobalConstants;
};

bool FailsAlphaTest(float alpha)
{
    // 0x60 / 0xFF = 0.37647
    return alpha < 0.37647f;
}

vec3 TransformNormal(vec3 v, mat4 m)
{
    vec3 transformed = (m * vec4(v, 0)).xyz;
    float lengthSquared = dot(transformed, transformed);
    // Uniform bone scale changes normal length, not direction; renormalize so lighting
    // stays stable. The unit-length fast path stays bit-identical to the unnormalized
    // result, and zero-length inputs pass through instead of producing NaN.
    return lengthSquared > 0.0 && lengthSquared != 1.0
        ? transformed * inversesqrt(lengthSquared)
        : transformed;
}

float saturate(float v)
{
    return clamp(v, 0.0, 1.0);
}

vec3 saturate(vec3 v)
{
    return clamp(v, vec3(0, 0, 0), vec3(1, 1, 1));
}

float CalculateClippingPlane(vec3 position, bool hasClippingPlane, vec4 plane)
{
    if (hasClippingPlane)
    {
        return dot(vec4(position, 1), plane);
    }
    return 1;
}

#define DO_CLIPPING(position) \
    gl_ClipDistance[0] = CalculateClippingPlane(position, _GlobalConstants.HasClippingPlane1, _GlobalConstants.ClippingPlane1); \
    gl_ClipDistance[1] = CalculateClippingPlane(position, _GlobalConstants.HasClippingPlane2, _GlobalConstants.ClippingPlane2);

#endif