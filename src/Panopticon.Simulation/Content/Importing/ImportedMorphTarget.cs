#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Panopticon.Content.Importing;

/// <summary>
/// One morph target: optional per-vertex deltas for position, normal, and tangent. Absent
/// arrays contribute nothing; present arrays must match the owning mesh's vertex count.
/// </summary>
public sealed class ImportedMorphTarget
{
    public IReadOnlyList<Vector3>? PositionDeltas { get; }

    public IReadOnlyList<Vector3>? NormalDeltas { get; }

    public IReadOnlyList<Vector3>? TangentDeltas { get; }

    public ImportedMorphTarget(
        IReadOnlyList<Vector3>? positionDeltas,
        IReadOnlyList<Vector3>? normalDeltas = null,
        IReadOnlyList<Vector3>? tangentDeltas = null)
    {
        if (positionDeltas is null && normalDeltas is null && tangentDeltas is null)
        {
            throw new ArgumentException("A morph target must carry at least one delta array.", nameof(positionDeltas));
        }

        var expectedCount = (positionDeltas ?? normalDeltas ?? tangentDeltas)!.Count;
        ValidateDeltas(positionDeltas, expectedCount, nameof(positionDeltas));
        ValidateDeltas(normalDeltas, expectedCount, nameof(normalDeltas));
        ValidateDeltas(tangentDeltas, expectedCount, nameof(tangentDeltas));

        PositionDeltas = positionDeltas?.ToArray();
        NormalDeltas = normalDeltas?.ToArray();
        TangentDeltas = tangentDeltas?.ToArray();
    }

    private static void ValidateDeltas(IReadOnlyList<Vector3>? deltas, int expectedCount, string paramName)
    {
        if (deltas is null)
        {
            return;
        }

        if (deltas.Count != expectedCount)
        {
            throw new ArgumentException(
                $"Delta arrays must share one length; {paramName} has {deltas.Count} entries, expected {expectedCount}.",
                paramName);
        }

        for (var i = 0; i < deltas.Count; i++)
        {
            if (!float.IsFinite(deltas[i].X) || !float.IsFinite(deltas[i].Y) || !float.IsFinite(deltas[i].Z))
            {
                throw new ArgumentException($"Delta {i} in {paramName} must be finite.", paramName);
            }
        }
    }
}
