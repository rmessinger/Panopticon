#nullable enable

using System;
using Panopticon.Terrain.Cdlod;

namespace Panopticon.Terrain;

public sealed class TerrainPatchStreamer : TerrainPatchStreamer<TerrainPatch>
{
    public TerrainPatchStreamer(
        Func<CdlodSelectedNode, int, TerrainPatch> createPatch,
        Action<TerrainPatch> releasePatch)
        : base(createPatch, releasePatch)
    {
    }
}
