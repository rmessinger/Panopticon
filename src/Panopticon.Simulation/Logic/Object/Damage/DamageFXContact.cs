#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Object;

/// <summary>Where an armor set's damage effects play.</summary>
internal enum DamageFXPlacement
{
    /// <summary>The victim's origin; native Generals behavior.</summary>
    VictimOrigin,

    /// <summary>The victim's rendered bounds, on the side facing the damage source (card WI-X1-07).</summary>
    AttackerFacingBounds,
}

/// <summary>
/// Presentation-only impact point from transformed rendered bounds. This approximates a surface
/// hit with the bounds box; it is not mesh collision.
/// </summary>
internal static class DamageFXContact
{
    /// <summary>
    /// Casts from the bounds center toward the source in bounds space and returns the world point
    /// where the ray leaves the box. A source inside the box, or directly at its center, yields the
    /// world bounds center. Returns false when the transform cannot be inverted or the result is
    /// not finite.
    /// </summary>
    public static bool TryGetAttackerFacingPoint(in AxisAlignedBoundingBox localBounds, in Matrix4x4 localToWorld, in Vector3 sourceWorld, out Vector3 contact)
    {
        contact = default;
        if (!Matrix4x4.Invert(localToWorld, out var worldToLocal))
        {
            return false;
        }

        var center = localBounds.GetCenter();
        var halfExtents = (localBounds.Max - localBounds.Min) / 2;
        var direction = Vector3.Transform(sourceWorld, worldToLocal) - center;

        var exit = 1f;
        var outside = false;
        for (var axis = 0; axis < 3; axis++)
        {
            var distance = MathF.Abs(direction[axis]);
            if (distance > halfExtents[axis])
            {
                outside = true;
                exit = MathF.Min(exit, halfExtents[axis] / distance);
            }
        }

        var local = outside ? center + direction * exit : center;
        contact = Vector3.Transform(local, localToWorld);
        return float.IsFinite(contact.X) && float.IsFinite(contact.Y) && float.IsFinite(contact.Z);
    }

    /// <summary>
    /// Six-side surface variant for part-wise bounds: picks the body box face whose axis points
    /// at the source and intersects the source-to-center ray with it, clamping to the face
    /// rectangle. The body is the largest part box, so thin protrusions like a forward gun barrel
    /// no longer push the effect out to the merged-box plane. The part list itself is cached by
    /// the draw module, so a hit costs only this arithmetic.
    /// </summary>
    public static bool TryGetAttackerFacingSurfacePoint(
        IReadOnlyList<AxisAlignedBoundingBox> partBounds,
        in Matrix4x4 localToWorld,
        in Vector3 sourceWorld,
        out Vector3 contact)
    {
        contact = default;
        if (partBounds.Count == 0)
        {
            return false;
        }

        if (!Matrix4x4.Invert(localToWorld, out var worldToLocal))
        {
            return false;
        }

        var body = partBounds[0];
        var merged = partBounds[0];
        var bestVolume = PartVolume(partBounds[0]);
        for (var i = 1; i < partBounds.Count; i++)
        {
            merged = AxisAlignedBoundingBox.CreateMerged(merged, partBounds[i]);
            var volume = PartVolume(partBounds[i]);
            if (volume > bestVolume)
            {
                bestVolume = volume;
                body = partBounds[i];
            }
        }

        var center = merged.GetCenter();
        var source = Vector3.Transform(sourceWorld, worldToLocal);
        var direction = center - source;
        if (direction == Vector3.Zero)
        {
            contact = Vector3.Transform(center, localToWorld);
            return float.IsFinite(contact.X) && float.IsFinite(contact.Y) && float.IsFinite(contact.Z);
        }

        var axis = 0;
        if (MathF.Abs(direction.Y) > MathF.Abs(direction[axis]))
        {
            axis = 1;
        }

        if (MathF.Abs(direction.Z) > MathF.Abs(direction[axis]))
        {
            axis = 2;
        }

        // The face whose normal points toward the source; interior sources land on themselves.
        var face = source[axis] >= center[axis] ? body.Max[axis] : body.Min[axis];
        var local = source;
        var distance = (face - source[axis]) / direction[axis];
        if (distance > 0f)
        {
            var hit = source + direction * distance;
            local = new Vector3(
                axis == 0 ? face : Math.Clamp(hit.X, body.Min.X, body.Max.X),
                axis == 1 ? face : Math.Clamp(hit.Y, body.Min.Y, body.Max.Y),
                axis == 2 ? face : Math.Clamp(hit.Z, body.Min.Z, body.Max.Z));
        }

        contact = Vector3.Transform(local, localToWorld);
        return float.IsFinite(contact.X) && float.IsFinite(contact.Y) && float.IsFinite(contact.Z);
    }

    private static double PartVolume(in AxisAlignedBoundingBox box)
    {
        var size = box.Max - box.Min;
        return (double)size.X * size.Y * size.Z;
    }
}
