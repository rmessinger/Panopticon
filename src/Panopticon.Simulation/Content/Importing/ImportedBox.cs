#nullable enable

using System;

using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Content.Importing;

/// <summary>
/// Bounds-only box, mirroring the runtime bounds-only box path.
/// </summary>
public sealed class ImportedBox : ImportedRenderObject
{
    public Vector3 Center { get; }

    public Vector3 Extent { get; }

    public override AxisAlignedBoundingBox BoundingBox { get; }

    public override BoundingSphere BoundingSphere { get; }

    public ImportedBox(in Vector3 center, in Vector3 extent)
    {
        if (extent.X < 0 || extent.Y < 0 || extent.Z < 0)
            throw new ArgumentException("Box extent must not be negative.", nameof(extent));

        Center = center;
        Extent = extent;
        BoundingBox = new AxisAlignedBoundingBox(center - extent, center + extent);
        BoundingSphere = new BoundingSphere(center, extent.Length());
    }
}
