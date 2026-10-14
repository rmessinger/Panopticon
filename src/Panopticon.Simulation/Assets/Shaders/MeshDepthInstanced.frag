#version 450
#extension GL_GOOGLE_include_directive : enable

#include "Common.h"

// Instanced depth/shadow fragment stage (B5-2): depth-only, same as MeshDepth.frag. It
// deliberately declares no set-3 resources; the instance stream is vertex-stage-only, so
// the reflected item layout of the MeshDepthInstanced set comes from the vertex stage.
void main()
{
    // TODO: Alpha testing
}
