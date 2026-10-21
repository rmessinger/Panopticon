using System;
using System.Numerics;

namespace Panopticon.Logic;

internal static class GroundFootprint
{
    internal static bool BlocksAdvance(Vector2 start, Vector2 end, Vector2 obstacle, float clearance)
    {
        var step = end - start;
        var lengthSquared = step.LengthSquared();
        if (lengthSquared < 0.000001f)
        {
            return false;
        }

        var offset = obstacle - start;
        var along = Vector2.Dot(offset, step);
        // Existing overlap may clear, but must not deepen. Never push units apart.
        if (along <= 0)
        {
            return false;
        }

        var closest = start + step * Math.Clamp(along / lengthSquared, 0, 1);
        return Vector2.DistanceSquared(closest, obstacle) < clearance * clearance;
    }
}
