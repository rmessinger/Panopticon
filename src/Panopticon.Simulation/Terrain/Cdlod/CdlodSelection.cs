using System.Collections.Generic;

namespace Panopticon.Terrain.Cdlod;

public readonly record struct CdlodSelectedNode(CdlodNode Node, float MorphFactor, CdlodEdges CoarserEdges);

public sealed class CdlodSelection
{
    internal CdlodSelection(IReadOnlyList<CdlodSelectedNode> nodes)
    {
        Nodes = nodes;
    }

    public IReadOnlyList<CdlodSelectedNode> Nodes { get; }
}
