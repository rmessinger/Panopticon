#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Map identity and source payload version in the package manifest.</summary>
/// <param name="Name">Map name.</param>
/// <param name="SourceKind">Authored provenance: the manifest records the source the package content was authored from, while a reader returns MapSourceKind.H3Package for its own read.</param>
/// <param name="LegacyGlobalVersion">Optional legacy global version.</param>
public sealed record MapPackageIdentity(string Name, string SourceKind, int? LegacyGlobalVersion)
{
    private static readonly Dictionary<string, MapSourceKind> ValidSourceKinds = new(StringComparer.Ordinal)
    {
        [nameof(MapSourceKind.LegacyMap)] = MapSourceKind.LegacyMap,
        [nameof(MapSourceKind.H3Package)] = MapSourceKind.H3Package,
        [nameof(MapSourceKind.Generated)] = MapSourceKind.Generated,
    };

    public static MapPackageIdentity FromModel(MapIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new MapPackageIdentity(identity.Name, identity.SourceKind.ToString(), identity.LegacyGlobalVersion);
    }

    public MapIdentity ToModel()
    {
        if (!ValidSourceKinds.TryGetValue(SourceKind, out var kind))
        {
            throw new InvalidOperationException(
                $"SourceKind '{SourceKind}' is not a valid MapSourceKind name; the package manifest validator should have rejected it.");
        }

        return new MapIdentity(Name, kind, LegacyGlobalVersion);
    }
}
