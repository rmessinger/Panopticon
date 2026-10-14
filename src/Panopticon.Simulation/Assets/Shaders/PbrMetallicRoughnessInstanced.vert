#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"
#include "ForwardPass.h"

// Instanced metallic-roughness vertex path (B5-2): the fragment shader reads no per-item
// constants, so only the world matrix moves into the instance stream.
#define MESH_DECLARE_ITEM_CONSTANTS 0
#include "Mesh.h"
#include "Instancing.h"

MESH_VERTEX_INPUTS

layout(location = 0) out vec3 out_WorldPosition;
layout(location = 1) out vec3 out_WorldNormal;
layout(location = 2) out vec2 out_UV0;
layout(location = 3) out vec2 out_UV1;
layout(location = 4) out vec3 out_WorldTangent;
layout(location = 5) out vec3 out_WorldBinormal;
layout(location = 6) out vec2 out_CloudUV;

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

    out_UV0 = in_UV0;
    out_UV1 = in_UV1;

    out_WorldTangent = TransformNormal(in_Tangent, instance.World);
    out_WorldBinormal = TransformNormal(in_Binormal, instance.World);
}
