using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>One primitive check record captured from a Core/CLI result; DTO fields only.</summary>
public sealed record SedanProofJournalCheck(string Category, string Name, string Status, double? Expected, double? Actual, double? Tolerance, string? Units, string? Message);

/// <summary>One primitive diagnostic record; never an exception object.</summary>
public sealed record SedanProofJournalDiagnostic(string Severity, string Code, string Stage, string? Message);

/// <summary>Journal of one proof stage: the API/argv actually invoked, its outcome, all checks, all diagnostics.</summary>
public sealed class SedanProofValidationJournal
{
    /// <summary>Local wall-clock timestamp in ISO 8601 round-trip format ("O", invariant culture).</summary>
    public string TimestampLocal { get; init; } = string.Empty;

    /// <summary>Proof scope label the stage belongs to.</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>Proof stage: CoreRun, CliImport, CliReport, CliPreview, or NativeGame.</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>API entry point invoked, or the exact argv joined.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Whether the stage completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Payload directory the stage operated on, when applicable.</summary>
    public string? PayloadPath { get; init; }

    /// <summary>Every primitive check captured from the stage result.</summary>
    public IReadOnlyList<SedanProofJournalCheck> Checks { get; init; } = Array.Empty<SedanProofJournalCheck>();

    /// <summary>Every primitive diagnostic captured from the stage.</summary>
    public IReadOnlyList<SedanProofJournalDiagnostic> Diagnostics { get; init; } = Array.Empty<SedanProofJournalDiagnostic>();

    /// <summary>Free-form note, when present.</summary>
    public string? Note { get; init; }
}

/// <summary>Writes journals as JSON under a directory OUTSIDE any owned import stage; unique files, never overwrites.</summary>
public sealed class SedanProofJournalWriter
{
    private readonly string _directory;

    /// <summary>Targets <paramref name="directory"/>, created on demand at write time.</summary>
    public SedanProofJournalWriter(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        _directory = directory;
    }

    /// <summary>
    /// Serializes the journal as indented JSON with default (PascalCase) options plus a
    /// trailing newline into a unique timestamped file, and returns the written path.
    /// An existing candidate path is refused, never overwritten or deleted.
    /// </summary>
    public string Write(SedanProofValidationJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        Directory.CreateDirectory(_directory);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(_directory, $"sedan-proof-journal-{journal.Stage}-{stamp}-{Guid.NewGuid().ToString("N")[..8]}.json");

        if (File.Exists(path))
        {
            throw new InvalidOperationException($"Refusing to overwrite an existing journal file: {path}");
        }

        var json = JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json + Environment.NewLine);

        return path;
    }
}
