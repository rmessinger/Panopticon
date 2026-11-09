#nullable enable

using System.Collections.Generic;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources;

/// <summary>
/// Preserved metadata from an opened H3 package for faithful round trip during save.
/// </summary>
public sealed class PackageConversionContext
{
    public MapIdentity? Identity { get; init; }

    public Provenance? Provenance { get; init; }

    public MapQuantity? SampleSpacing { get; init; }

    public IReadOnlyDictionary<string, AssetReference>? ObjectTypesByStableId { get; init; }

    public IReadOnlyList<AssetReference>? ObjectTypesByIndex { get; init; }

    public IReadOnlyDictionary<string, AssetReference>? ObjectTypesByName { get; init; }
}
