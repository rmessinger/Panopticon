using System.Collections.Generic;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Geospatial.Preview;

/// <summary>
/// Outcome of one payload-to-package conversion. On failure <see cref="Model"/>,
/// <see cref="PackageRoot"/>, and <see cref="Limitations"/> are null or empty and
/// <see cref="Diagnostics"/> holds the failure texts.
/// </summary>
public sealed record GeneratedMapPackageResult(
    bool Success,
    IReadOnlyList<string> Diagnostics,
    MapInterchange? Model,
    string? PackageRoot,
    int ChunkCount,
    IReadOnlyList<string> Limitations);
