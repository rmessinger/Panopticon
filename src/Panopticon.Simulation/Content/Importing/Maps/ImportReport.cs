#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Import result, not package content. <c>KernelAndSampleDistanceRatios</c> preserves the report's kernel and sample-distance ratio strings as read. Collections are copied at construction.</summary>
public sealed record ImportReport
{
    public ImportReport(
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<string> kernelAndSampleDistanceRatios,
        int outsideFootprintCellCount,
        int nodataCellCount,
        IReadOnlyList<string> controlPointResults)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(kernelAndSampleDistanceRatios);
        ArgumentNullException.ThrowIfNull(controlPointResults);
        Diagnostics = diagnostics.ToArray();
        KernelAndSampleDistanceRatios = kernelAndSampleDistanceRatios.ToArray();
        OutsideFootprintCellCount = outsideFootprintCellCount;
        NodataCellCount = nodataCellCount;
        ControlPointResults = controlPointResults.ToArray();
    }

    public IReadOnlyList<string> Diagnostics { get; }

    public IReadOnlyList<string> KernelAndSampleDistanceRatios { get; }

    public int OutsideFootprintCellCount { get; }

    public int NodataCellCount { get; }

    public IReadOnlyList<string> ControlPointResults { get; }
}
