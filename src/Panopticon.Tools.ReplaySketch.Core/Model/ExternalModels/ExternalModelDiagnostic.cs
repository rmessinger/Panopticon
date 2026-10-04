#nullable enable

using System;
using System.Text.Json.Serialization;
using Panopticon;

namespace Panopticon.Tools.ReplaySketch.Model;

public enum ExternalDiagnosticSeverity
{
    Info,

    Warning,

    Error,
}

public enum ExternalDiagnosticStage
{
    Selection,

    Resolve,

    Parse,

    Convert,

    Validate,

    Materialize,

    Bind,

    Render,
}

public enum ExternalDiagnosticSourceRole
{
    Model,

    Animation,
}

/// <summary>
/// One structured diagnostic. Immutable; duplicate identity is Code + Source + Context +
/// Message, collapsed with an occurrence count by the mapper. Exception detail stays local
/// and is never serialized.
/// </summary>
public sealed class ExternalModelDiagnostic
{
    public string Code { get; }

    public ExternalDiagnosticSeverity Severity { get; }

    public ExternalDiagnosticStage Stage { get; }

    public ExternalDiagnosticSourceRole Source { get; }

    public string? Context { get; }

    public string Message { get; }

    public string? FixHint { get; }

    public int OccurrenceCount { get; }

    [JsonIgnore]
    public Exception? ExceptionDetail { get; }

    public ExternalModelDiagnostic(
        string code,
        ExternalDiagnosticSeverity severity,
        ExternalDiagnosticStage stage,
        ExternalDiagnosticSourceRole source,
        string? context,
        string message,
        string? fixHint = null,
        int occurrenceCount = 1,
        Exception? exceptionDetail = null)
    {
        Ensure.NotNullOrWhiteSpace(code, "Diagnostic code must not be empty.");

        Ensure.NotNullOrWhiteSpace(message, "Diagnostic message must not be empty.");

        if (occurrenceCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrenceCount), "Occurrence count must be at least 1.");
        }

        Code = code;
        Severity = severity;
        Stage = stage;
        Source = source;
        Context = context;
        Message = message;
        FixHint = fixHint;
        OccurrenceCount = occurrenceCount;
        ExceptionDetail = exceptionDetail;
    }

    public override bool Equals(object? obj)
    {
        return obj is ExternalModelDiagnostic other &&
            Code == other.Code &&
            Source == other.Source &&
            Context == other.Context &&
            Message == other.Message;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Code, Source, Context, Message);
    }
}
