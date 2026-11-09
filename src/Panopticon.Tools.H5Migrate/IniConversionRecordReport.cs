#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// Per-record diagnostic report for an error-carrying unit resolution (WI-H5-02).
/// Names each failing record and groups its error diagnostics.
/// </summary>
public sealed class IniConversionRecordReport
{
    public IReadOnlyList<RecordErrorReport> RecordErrors { get; }

    public int TotalErrorCount { get; }

    public IniConversionRecordReport(IReadOnlyList<RecordErrorReport> recordErrors, int totalErrorCount)
    {
        RecordErrors = recordErrors;
        TotalErrorCount = totalErrorCount;
    }

    public static IniConversionRecordReport Create(UnitImportResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var errors = resolution.Diagnostics.Where(d => d.IsError).ToList();
        var groups = errors.GroupBy(d => d.Subject?.ToString() ?? "(unassigned)");

        var recordErrors = groups.Select(group => new RecordErrorReport(
            group.Key,
            group.Select(d => new DiagnosticEntry(d.Code, d.Message)).ToList()
        )).ToList();

        return new IniConversionRecordReport(recordErrors, errors.Count);
    }

    public IEnumerable<string> FormatLines(string subcommand)
    {
        foreach (var record in RecordErrors)
        {
            foreach (var error in record.Errors)
            {
                yield return $"error {error.Code} {subcommand} record '{record.RecordIdentifier}': {H5MigrateCli.NormalizeWhitespace(error.Message)}";
            }
        }
    }
}

public sealed record RecordErrorReport(string RecordIdentifier, IReadOnlyList<DiagnosticEntry> Errors);

public sealed record DiagnosticEntry(string Code, string Message);
