#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"
#include "ForwardPass.h"

// Instanced fixed-function vertex path (B5-2): per-instance world, UV offset, house
// colour, opacity and tint come from the instance stream; the eligible items an
// instanced batch covers are exactly the ones whose per-item constants are at defaults.
#define MESH_DECLARE_ITEM_CONSTANTS 0
#include "Mesh.h"
#include "Instancing.h"

MESH_VERTEX_INPUTS

layout(location = 0) out vec3 out_WorldPosition;
layout(location = 1) out vec3 out_WorldNormal;
layout(location = 2) out vec2 out_UV0;
layout(location = 3) out vec2 out_UV1;
layout(location = 4) out vec2 out_CloudUV;
layout(location = 5) out float out_ViewSpaceDepth;
layout(location = 6) out float out_TextureScrollScale;
layout(location = 7) flat out vec3 out_InstanceHouseColor;
layout(location = 8) flat out float out_InstanceOpacity;
layout(location = 9) flat out vec3 out_InstanceTint;

void main()
{
    InstanceData instance = GetInstanceData();

    VSSkinnedInstanced(
        in_Position0,
        in_Normal0,
        gl_Position,
        out_WorldPosition,
        out_WorldNormal,
        out_CloudUV,
        instance.World);

    DO_CLIPPING(out_WorldPosition)

    out_UV0 = in_UV0 + instance.UVOffset.xy;
    out_UV1 = in_UV1;
    out_TextureScrollScale = instance.UVOffset.z;

    out_ViewSpaceDepth = gl_Position.z;

    out_InstanceHouseColor = instance.HouseColorOpacity.rgb;
    out_InstanceOpacity = instance.HouseColorOpacity.a;
    out_InstanceTint = instance.Tint.rgb;
}
