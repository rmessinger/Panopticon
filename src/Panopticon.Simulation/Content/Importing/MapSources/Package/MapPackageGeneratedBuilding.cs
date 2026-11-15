#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

public sealed record MapPackageGeneratedBuilding(
    string SourceId,
    string FeatureId,
    SortedDictionary<string, string> Attributes,
    double HeightMeters,
    string HeightSource,
    double HeightGameUnits,
    string RoofRule,
    string? RoofFallbackReason,
    int MaterialVariant,
    string SeatMethod,
    double SupportMinZ,
    double SupportMaxZ,
    double SinkGameUnits,
    double BaseZ,
    double EaveZ,
    IReadOnlyList<MapPackageBuildingPart> Parts)
{
    public static MapPackageGeneratedBuilding FromModel(GeneratedBuilding building)
    {
        var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in building.Attributes)
        {
            attributes.Add(pair.Key, pair.Value);
        }

        return new MapPackageGeneratedBuilding(
            building.SourceId,
            building.FeatureId,
            attributes,
            building.HeightMeters,
            building.HeightSource,
            building.HeightGameUnits,
            building.RoofRule,
            building.RoofFallbackReason,
            building.MaterialVariant,
            building.SeatMethod,
            building.SupportMinZ,
            building.SupportMaxZ,
            building.SinkGameUnits,
            building.BaseZ,
            building.EaveZ,
            building.Parts.Select(MapPackageBuildingPart.FromModel).ToArray());
    }

    public GeneratedBuilding ToModel()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in Attributes)
        {
            attributes.Add(pair.Key, pair.Value);
        }

        return new GeneratedBuilding(
            SourceId,
            FeatureId,
            attributes,
            HeightMeters,
            HeightSource,
            HeightGameUnits,
            RoofRule,
            RoofFallbackReason,
            MaterialVariant,
            SeatMethod,
            SupportMinZ,
            SupportMaxZ,
            SinkGameUnits,
            BaseZ,
            EaveZ,
            Parts.Select(part => part.ToModel()).ToArray());
    }
}
