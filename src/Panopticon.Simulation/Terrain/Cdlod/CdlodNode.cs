using System;
using Panopticon.Mathematics;

namespace Panopticon.Terrain.Cdlod;

public readonly record struct CdlodNode(int X, int Y, int Level)
{
    internal const int LeafIntervals = 16;

    public Rectangle ClippedExtent(int latticeWidth, int latticeHeight)
    {
        var span = (long)LeafIntervals << Level;

        var (originX, width) = ClipAxis(X, span, latticeWidth);
        var (originY, height) = ClipAxis(Y, span, latticeHeight);

        return new Rectangle(originX, originY, width, height);
    }

    private static (int Origin, int Length) ClipAxis(int index, long span, int latticeCount)
    {
        var last = (long)latticeCount - 1;
        var origin = Math.Min((long)index * span, last);
        var length = Math.Min(span + 1, latticeCount - origin);

        return ((int)origin, (int)length);
    }
}
