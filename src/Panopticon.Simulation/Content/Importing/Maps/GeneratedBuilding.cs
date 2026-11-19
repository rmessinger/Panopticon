#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One generated building: source provenance, height and seating metadata, and its renderable parts. Attributes and parts are copied at construction.</summary>
public sealed record GeneratedBuilding
{
    public GeneratedBuilding(
        string sourceId,
        string featureId,
        IReadOnlyDictionary<string, string> attributes,
        double heightMeters,
        string heightSource,
        double heightGameUnits,
        string roofRule,
        string? roofFallbackReason,
        int materialVariant,
        string seatMethod,
        double supportMinZ,
        double supportMaxZ,
        double sinkGameUnits,
        double baseZ,
        double eaveZ,
        IReadOnlyList<BuildingPart> parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(parts);
        SourceId = sourceId;
        FeatureId = featureId;
        Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
        HeightMeters = heightMeters;
        HeightSource = heightSource;
        HeightGameUnits = heightGameUnits;
        RoofRule = roofRule;
        RoofFallbackReason = roofFallbackReason;
        MaterialVariant = materialVariant;
        SeatMethod = seatMethod;
        SupportMinZ = supportMinZ;
        SupportMaxZ = supportMaxZ;
        SinkGameUnits = sinkGameUnits;
        BaseZ = baseZ;
        EaveZ = eaveZ;
        Parts = parts.ToArray();
    }

    public string SourceId { get; }

    public string FeatureId { get; }

    /// <summary>Defensive copy with ordinal key comparison; the source dictionary is not retained.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>Source height above ground in meters.</summary>
    public double HeightMeters { get; }

    /// <summary>How HeightMeters was obtained: SourceAttribute, UsageMedian, or GlobalMedian.</summary>
    public string HeightSource { get; }

    /// <summary>HeightMeters converted to game units.</summary>
    public double HeightGameUnits { get; }

    /// <summary>Roof treatment applied to the extruded footprint: Flat or Gabled.</summary>
    public string RoofRule { get; }

    /// <summary>Why the requested roof rule fell back to another rule; null when no fallback happened.</summary>
    public string? RoofFallbackReason { get; }

    public int MaterialVariant { get; }

    /// <summary>How the part geometry was seated on the terrain.</summary>
    public string SeatMethod { get; }

    public double SupportMinZ { get; }

    public double SupportMaxZ { get; }

    public double SinkGameUnits { get; }

    public double BaseZ { get; }

    public double EaveZ { get; }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<BuildingPart> Parts { get; }
}
