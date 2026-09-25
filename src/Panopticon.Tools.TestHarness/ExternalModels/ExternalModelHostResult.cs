#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.TestHarness.ExternalModels;

/// <summary>
/// Summary of one external-model host execution: process exit code plus the diagnostics
/// accumulated and whether the report was published.
/// </summary>
internal sealed record ExternalModelHostResult(
    int ExitCode,
    IReadOnlyList<ExternalModelDiagnostic> Diagnostics,
    bool ReportWritten)
{
    public const int ExitOk = 0;

    public const int ExitRequestFileUnreadable = 3;

    public const int ExitRequestInvalid = 4;

    public const int ExitOperationFailed = 5;
}
