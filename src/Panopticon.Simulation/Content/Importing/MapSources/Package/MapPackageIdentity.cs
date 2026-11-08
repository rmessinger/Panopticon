#nullable enable

using System;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Map identity and source payload version in the package manifest.</summary>
public sealed record MapPackageIdentity(string Name, string SourceKind, int? LegacyGlobalVersion)
{
    public static MapPackageIdentity FromModel(MapIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new MapPackageIdentity(identity.Name, identity.SourceKind.ToString(), identity.LegacyGlobalVersion);
    }

    public MapIdentity ToModel()
    {
        var kind = Enum.TryParse<MapSourceKind>(SourceKind, false, out var parsed) ? parsed : MapSourceKind.H3Package;
        return new MapIdentity(Name, kind, LegacyGlobalVersion);
    }
}
