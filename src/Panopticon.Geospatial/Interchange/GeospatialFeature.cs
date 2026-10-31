using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Panopticon.Geospatial.Interchange;

/// <summary>
/// One vector feature read from a source, in that source's CRS. The constructor copies the
/// attribute dictionary.
/// </summary>
public sealed record GeospatialFeature
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes = new Dictionary<string, string>();

    public GeospatialFeature(
        string sourceId,
        string featureId,
        GeoGeometry geometry,
        CoordinateSystemId coordinateSystem,
        IReadOnlyDictionary<string, string>? attributes = null,
        GeoAltitudeMode? altitudeMode = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentException.ThrowIfNullOrEmpty(featureId);
        ArgumentNullException.ThrowIfNull(geometry);

        SourceId = sourceId;
        FeatureId = featureId;
        Geometry = geometry;
        CoordinateSystem = coordinateSystem;
        AltitudeMode = altitudeMode;
        Attributes = CopyAttributes(attributes);
    }

    /// <summary>Stable identifier of the source that produced this feature.</summary>
    public string SourceId { get; }

    /// <summary>Stable feature identifier within the source.</summary>
    public string FeatureId { get; }

    public GeoGeometry Geometry { get; }

    public CoordinateSystemId CoordinateSystem { get; }

    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>Altitude interpretation policy, or null when the source did not declare one.</summary>
    public GeoAltitudeMode? AltitudeMode { get; }

    private static ReadOnlyDictionary<string, string> CopyAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in attributes ?? EmptyAttributes)
        {
            if (pair.Key is null || pair.Value is null)
            {
                throw new ArgumentException("Attribute keys and values must not be null.", nameof(attributes));
            }

            copy.Add(pair.Key, pair.Value);
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
