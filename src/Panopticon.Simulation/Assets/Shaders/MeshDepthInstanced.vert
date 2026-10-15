#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"

// Instanced depth/shadow vertex path (B5-2): position-only transform from the instance
// stream; no skinning (skinned meshes stay on the per-item depth path).
#define MESH_DECLARE_ITEM_CONSTANTS 0
#include "Mesh.h"
#include "Instancing.h"

MESH_VERTEX_INPUTS

void main()
{
    InstanceData instance = GetInstanceData();

    vec3 worldPosition;
    VSSkinnedInstancedPositionOnly(
        in_Position0,
        gl_Position,
        worldPosition,
        instance.World);

    DO_CLIPPING(worldPosition)
}
