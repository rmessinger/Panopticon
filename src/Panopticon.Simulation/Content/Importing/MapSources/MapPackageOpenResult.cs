#nullable enable

using Panopticon.Data.Map;

namespace Panopticon.Content.Importing.MapSources;

/// <summary>
/// Result of opening an H3 package into the editor model with companion content and conversion context.
/// </summary>
public sealed record MapPackageOpenResult(
    MapFile MapFile,
    string MapIniContent,
    string MapStrContent,
    PackageConversionContext Context);
