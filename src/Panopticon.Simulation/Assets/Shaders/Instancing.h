#ifndef INSTANCING_H

#define INSTANCING_H

// Shared instance-stream contract for GPU-instanced mesh draws (B5-2). One structured
// buffer holds every instance payload of the frame; a batch draws a contiguous range of
// it. Included only by the *Instanced shader variants, which opt out of the per-item
// constant blocks in Mesh.h (see MESH_DECLARE_ITEM_CONSTANTS there).

struct InstanceData
{
    mat4 World;

    // xy texture offset, z baked-scroll scale, w unused (mirrors RenderItemConstantsVS).
    vec4 UVOffset;

    // rgb house colour, w opacity.
    vec4 HouseColorOpacity;

    // rgb tint, w unused.
    vec4 Tint;
};

struct InstancedItemConstantsType
{
    uint InstanceStart;
    float _Padding0;
    float _Padding1;
    float _Padding2;
};

layout(set = RENDER_ITEM_CONSTANTS_RESOURCE_SET, binding = 0) uniform MeshConstants
{
    MeshConstantsType _MeshConstants;
};

layout(set = RENDER_ITEM_CONSTANTS_RESOURCE_SET, binding = 1) uniform InstancedItemConstants
{
    InstancedItemConstantsType _InstancedItemConstants;
};

layout(set = RENDER_ITEM_CONSTANTS_RESOURCE_SET, binding = 2) readonly buffer InstanceDataBuffer
{
    InstanceData _InstanceData[];
};

InstanceData GetInstanceData()
{
    return _InstanceData[_InstancedItemConstants.InstanceStart + gl_InstanceIndex];
}

#endif
