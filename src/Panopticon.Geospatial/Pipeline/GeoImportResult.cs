using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Panopticon.Geospatial.Payload.Models;

namespace Panopticon.Geospatial.Pipeline;

/// <summary>
/// Result of one import run. A successful result carries the committed payload path, the
/// retained backup path when force replaced a destination, and the final report; a
/// failed result carries only diagnostics. Absolute paths appear only here.
/// </summary>
public sealed record GeoImportResult
{
    public GeoImportResult(
        bool success,
        IReadOnlyList<GeoDiagnostic>? diagnostics = null,
        string? payloadPath = null,
        string? retainedBackupPath = null,
        GeneratedMapPayloadReport? report = null)
    {
        List<GeoDiagnostic> copied;
        if (diagnostics is null)
        {
            copied = [];
        }
        else
        {
            copied = new List<GeoDiagnostic>(diagnostics.Count);

            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic is null)
                {
                    throw new ArgumentException("Diagnostics must not be null.", nameof(diagnostics));
                }

                copied.Add(diagnostic);
            }
        }

        if (success)
        {
            ArgumentException.ThrowIfNullOrEmpty(payloadPath);

            foreach (var diagnostic in copied)
            {
                if (diagnostic.Severity == GeoSeverity.Error)
                {
                    throw new ArgumentException(
                        "A successful import must not carry error diagnostics.",
                        nameof(diagnostics));
                }
            }
        }
        else if (payloadPath is not null)
        {
            throw new ArgumentException(
                "A failed import has no committed payload path; recovery paths belong in retainedBackupPath.",
                nameof(payloadPath));
        }

        ValidateOptionalPath(payloadPath, nameof(payloadPath));
        ValidateOptionalPath(retainedBackupPath, nameof(retainedBackupPath));

        Success = success;
        Diagnostics = new ReadOnlyCollection<GeoDiagnostic>(copied);
        PayloadPath = payloadPath;
        RetainedBackupPath = retainedBackupPath;
        Report = report;
    }

    public bool Success { get; }

    /// <summary>Immutable diagnostics in the order they were recorded.</summary>
    public IReadOnlyList<GeoDiagnostic> Diagnostics { get; }

    /// <summary>Absolute path of the committed payload directory; null unless success.</summary>
    public string? PayloadPath { get; }

    /// <summary>
    /// Absolute path of the retained sibling backup of a replaced destination, including
    /// preserved recovery paths after a failed restore.
    /// </summary>
    public string? RetainedBackupPath { get; }

    /// <summary>Final report of a successful import; null for recoverable failures.</summary>
    public GeneratedMapPayloadReport? Report { get; }

    private static void ValidateOptionalPath(string? path, string paramName)
    {
        if (path is not null && path.Length == 0)
        {
            throw new ArgumentException("The path must be non-empty when present.", paramName);
        }
    }
}
