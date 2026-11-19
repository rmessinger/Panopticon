#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>One vector feature in the package vector sidecar. Attributes serialize in ordinal key order.</summary>
public sealed record MapPackageVectorFeature(
    string SourceStableId,
    string SourceId,
    string Kind,
    int RuleIndex,
    IReadOnlyDictionary<string, string> Attributes,
    bool? Passable,
    bool? Buildable,
    string? Coverage,
    MapPackageVectorGeometry Geometry)
{
    public static MapPackageVectorFeature FromModel(VectorFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in feature.Attributes)
        {
            attributes[key] = value;
        }

        return new MapPackageVectorFeature(
            feature.SourceStableId,
            feature.SourceId,
            feature.Kind,
            feature.RuleIndex,
            attributes,
            feature.Passable,
            feature.Buildable,
            feature.Coverage,
            MapPackageVectorGeometry.FromModel(feature.Geometry));
    }

    public VectorFeature ToModel()
    {
        if (Geometry is null)
        {
            throw new FormatException("Vector feature is missing its geometry.");
        }

        return new VectorFeature(
            SourceStableId,
            SourceId,
            Kind,
            RuleIndex,
            Attributes,
            Passable,
            Buildable,
            Coverage,
            Geometry.ToModel());
    }
}
