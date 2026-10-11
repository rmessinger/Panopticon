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
    /// Surface variant for part-wise bounds: casts from the source toward the model and pins the
    /// contact to the entry face of the largest crossed sub-object box, so thin protrusions like a
    /// forward gun barrel no longer push the effect out to the merged-box plane. Falls back to the
    /// merged-box exit when no part is crossed.
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

        var merged = partBounds[0];
        for (var i = 1; i < partBounds.Count; i++)
        {
            merged = AxisAlignedBoundingBox.CreateMerged(merged, partBounds[i]);
        }

        if (!Matrix4x4.Invert(localToWorld, out var worldToLocal))
        {
            return false;
        }

        var center = merged.GetCenter();
        var source = Vector3.Transform(sourceWorld, worldToLocal);
        var direction = center - source;
        if (direction == Vector3.Zero)
        {
            contact = Vector3.Transform(center, localToWorld);
            return float.IsFinite(contact.X) && float.IsFinite(contact.Y) && float.IsFinite(contact.Z);
        }

        var bestVolume = -1.0;
        var bestEnter = 0f;
        var found = false;
        foreach (var part in partBounds)
        {
            if (!TryRayEnterPartBox(source, direction, part, out var enter))
            {
                continue;
            }

            var size = part.Max - part.Min;
            var volume = (double)size.X * size.Y * size.Z;
            if (!found || volume > bestVolume)
            {
                bestVolume = volume;
                bestEnter = enter;
                found = true;
            }
        }

        var local = found ? source + direction * bestEnter : center;
        contact = Vector3.Transform(local, localToWorld);
        return float.IsFinite(contact.X) && float.IsFinite(contact.Y) && float.IsFinite(contact.Z);
    }

    /// <summary>
    /// Ray-box entry parameter at t &gt;= 0 from outside the box, or 0 when the origin is inside it.
    /// </summary>
    private static bool TryRayEnterPartBox(Vector3 origin, Vector3 direction, in AxisAlignedBoundingBox box, out float enter)
    {
        enter = 0f;
        var near = float.NegativeInfinity;
        var far = float.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(direction[axis]) < 1e-6f)
            {
                if (origin[axis] < box.Min[axis] || origin[axis] > box.Max[axis])
                {
                    return false;
                }

                continue;
            }

            var nearAxis = (box.Min[axis] - origin[axis]) / direction[axis];
            var farAxis = (box.Max[axis] - origin[axis]) / direction[axis];
            if (nearAxis > farAxis)
            {
                (nearAxis, farAxis) = (farAxis, nearAxis);
            }

            near = MathF.Max(near, nearAxis);
            far = MathF.Min(far, farAxis);
        }

        if (near > far || far < 0f)
        {
            return false;
        }

        enter = MathF.Max(near, 0f);
        return true;
    }
}
