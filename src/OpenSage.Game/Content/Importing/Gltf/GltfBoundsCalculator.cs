#nullable enable

using System.Collections.Generic;
using System.Numerics;
using OpenSage.Mathematics;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Box and sphere bounds over already-converted model-space positions. The sphere is the
/// axis-aligned bounding sphere of the box (center = box center, radius = half diagonal).
/// </summary>
internal static class GltfBoundsCalculator
{
    public static AxisAlignedBoundingBox ComputeBox(IEnumerable<Vector3> positions)
    {
        return AxisAlignedBoundingBox.CreateFromPoints(positions);
    }

    public static BoundingSphere ComputeSphere(IEnumerable<Vector3> positions)
    {
        var box = ComputeBox(positions);
        var center = (box.Min + box.Max) / 2;

        return new BoundingSphere(center, Vector3.Distance(center, box.Max));
    }
}
