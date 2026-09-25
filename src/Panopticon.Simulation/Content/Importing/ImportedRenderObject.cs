#nullable enable

using Panopticon.Mathematics;

namespace Panopticon.Content.Importing;

/// <summary>
/// Base contract for imported renderable geometry. Bounds are CPU values; importers never
/// allocate GPU resources.
/// </summary>
public abstract class ImportedRenderObject
{
    public abstract AxisAlignedBoundingBox BoundingBox { get; }

    public abstract BoundingSphere BoundingSphere { get; }
}
