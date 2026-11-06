#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Import result, not package content.</summary>
public sealed record ImportReport(
    IReadOnlyList<string> Diagnostics,
    int OutsideFootprintCellCount,
    int NodataCellCount,
    IReadOnlyList<string> ControlPointResults);
