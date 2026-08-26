#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"
#include "ForwardPass.h"

layout(location = 0) in vec3 in_Position;
layout(location = 1) in vec3 in_Direction;
layout(location = 2) in float in_Side;
layout(location = 3) in float in_V;
layout(location = 4) in vec4 in_Color;

layout(location = 0) out vec2 out_UV;
layout(location = 1) out vec4 out_Color;

void main()
{
    vec3 toEye = normalize(_GlobalConstants.CameraPosition - in_Position);
    vec3 side = cross(in_Direction, toEye);
    float sideLength = length(side);
    side = sideLength > 0.0001
        ? side / sideLength
        : normalize(cross(in_Direction, vec3(0.0, 0.0, 1.0)));

    vec3 worldPosition = in_Position + side * in_Side;

    DO_CLIPPING(worldPosition)

    gl_Position = _GlobalConstants.ViewProjection * vec4(worldPosition, 1);

    out_UV = vec2(in_Side * 0.5 + 0.5, in_V);
    out_Color = in_Color;
}
