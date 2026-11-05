using System.Collections.Generic;
using Panopticon.Data.Map;
using Panopticon.Geospatial;

namespace Panopticon.Geospatial.Preview;

/// <summary>
/// Outcome of one preview build: the resident map, its transform and report on success,
/// or typed diagnostics. On failure <see cref="MapFile"/>, <see cref="Transform"/>, and
/// <see cref="Report"/> are null.
/// </summary>
public sealed class GeneratedMapPreviewResult
{
    internal GeneratedMapPreviewResult(MapFile? mapFile, PreviewTransform? transform, GeneratedMapPreviewReport? report, IReadOnlyList<GeoDiagnostic> diagnostics)
    {
        MapFile = mapFile;
        Transform = transform;
        Report = report;
        Diagnostics = diagnostics;
    }

    public bool Success => MapFile is not null;

    public MapFile? MapFile { get; }

    public PreviewTransform? Transform { get; }

    public GeneratedMapPreviewReport? Report { get; }

    public IReadOnlyList<GeoDiagnostic> Diagnostics { get; }
}
