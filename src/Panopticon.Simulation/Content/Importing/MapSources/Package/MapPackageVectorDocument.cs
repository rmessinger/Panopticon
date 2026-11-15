#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Root document of the strict-JSON vector feature sidecar.</summary>
public sealed record MapPackageVectorDocument(int Version, IReadOnlyList<MapPackageVectorFeature> Features);
