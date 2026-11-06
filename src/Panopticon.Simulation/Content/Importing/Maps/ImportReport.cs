#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Import result, not package content. <c>KernelAndSampleDistanceRatios</c> preserves the report's kernel and sample-distance ratio strings as read.</summary>
public sealed record ImportReport(
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> KernelAndSampleDistanceRatios,
    int OutsideFootprintCellCount,
    int NodataCellCount,
    IReadOnlyList<string> ControlPointResults);
