#nullable enable

using System;
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
}
