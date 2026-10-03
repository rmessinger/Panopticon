#nullable enable

using System;
using System.Collections.Generic;
using Panopticon;
using Panopticon.FileFormats.Nif;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Evaluates NIF float key curves in the source frame (deep dive 4.2):
/// linear segments interpolate linearly, quadratic keys use cubic Hermite
/// over the segment-normalized parameter, and the curve holds the end
/// values outside the key range.
/// </summary>
internal static class NifKeyCurve
{
    /// <summary>
    /// Evaluates the group at <paramref name="time"/>. The group must come
    /// from a parsed key group, so its key list is non-empty and sorted.
    /// </summary>
    internal static float Evaluate(NifKeyGroup<NifKey<float>> group, float time)
    {
        var keys = group.Keys;
        var last = keys.Count - 1;
        Ensure.AtLeast(last, 0, "Key group has no keys.", nameof(group));

        if (time <= keys[0].Time)
        {
            return keys[0].Value;
        }

        if (time >= keys[last].Time)
        {
            return keys[last].Value;
        }

        var index = 0;
        while (index + 1 < last && keys[index + 1].Time <= time)
        {
            index++;
        }

        var first = keys[index];
        var second = keys[index + 1];
        var span = second.Time - first.Time;
        if (span <= 0f)
        {
            return second.Value;
        }

        var s = (time - first.Time) / span;
        if (group.Interpolation == NifKeyType.Linear)
        {
            return first.Value + (second.Value - first.Value) * s;
        }

        // Segment-normalized Hermite. The outgoing tangent is the first
        // key's backward field and the incoming tangent is the second key's
        // forward field, matching NifSkope's glcontroller.cpp.
        var s2 = s * s;
        var s3 = s2 * s;
        var h00 = 2f * s3 - 3f * s2 + 1f;
        var h10 = s3 - 2f * s2 + s;
        var h01 = -2f * s3 + 3f * s2;
        var h11 = s3 - s2;
        return h00 * first.Value
            + h10 * first.Backward.GetValueOrDefault()
            + h01 * second.Value
            + h11 * second.Forward.GetValueOrDefault();
    }
}
