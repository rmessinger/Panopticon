using System;

namespace Panopticon.Geospatial.Features;

/// <summary>
/// Object candidate parameters: the required object-definition name and the resolved
/// orientation in degrees. 'none' maps to 0, 'fixed' angles are normalized into
/// [0, 360); 'along-feature' is undefined on a point and never reaches this record.
/// </summary>
public sealed record GeneratedObjectPlacement
{
    public GeneratedObjectPlacement(string objectDefinition, double orientationDegrees)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectDefinition);
        if (!double.IsFinite(orientationDegrees) || orientationDegrees < 0d || orientationDegrees >= 360d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(orientationDegrees),
                orientationDegrees,
                "Orientation degrees must be finite and within [0, 360).");
        }

        ObjectDefinition = objectDefinition;
        OrientationDegrees = orientationDegrees;
    }

    /// <summary>Object-definition name the rule requires.</summary>
    public string ObjectDefinition { get; }

    /// <summary>Resolved facing angle in degrees, normalized into [0, 360).</summary>
    public double OrientationDegrees { get; }
}
