#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"
#include "ForwardPass.h"

layout(location = 0) in vec3 in_Position;
layout(location = 1) in vec3 in_Direction;
layout(location = 2) in vec4 in_Color;
layout(location = 3) in float in_Length;
layout(location = 4) in float in_Width;

layout(location = 0) out vec4 out_Color;

// Vertex layout per tracer (repeated 4 times, expanded in the vertex shader):
// 0 - 1   0/1 = head, 2/3 = tail; odd = right side, even = left side.
// | / |
// 2 - 3

void main()
{
    uint corner = uint(mod(gl_VertexIndex, 4));

    vec3 toEye = normalize(_GlobalConstants.CameraPosition - in_Position);
    vec3 side = cross(in_Direction, toEye);
    float sideLength = length(side);
    side = sideLength > 0.0001
        ? side / sideLength
        : normalize(cross(in_Direction, vec3(0.0, 0.0, 1.0)));
    side *= in_Width * 0.5;

    float headTail = (corner < 2) ? 0.0 : -in_Length;
    float leftRight = ((corner == 1) || (corner == 3)) ? 1.0 : -1.0;

    vec3 worldPosition = in_Position + (in_Direction * headTail) + (side * leftRight);

    DO_CLIPPING(worldPosition)

    gl_Position = _GlobalConstants.ViewProjection * vec4(worldPosition, 1);

    float tailBrightness = (corner < 2) ? 1.0 : 0.35;

    out_Color = vec4(in_Color.rgb * in_Color.a * tailBrightness, in_Color.a);
}
