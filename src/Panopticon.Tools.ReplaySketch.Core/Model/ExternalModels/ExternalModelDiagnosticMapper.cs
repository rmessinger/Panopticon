#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Explicit stage-and-exception-type mapping table for C1-C4 failures, plus deterministic
/// sorting and duplicate collapse. Code selection never inspects exception message text;
/// unexpected exceptions map to EXT999 and retain their exception as local detail.
/// </summary>
public static class ExternalModelDiagnosticMapper
{
    public const string InternalFailureCode = "EXT999";

    private static readonly IReadOnlyDictionary<ExternalDiagnosticStage, string> InvalidDataCodes =
        new Dictionary<ExternalDiagnosticStage, string>
        {
            [ExternalDiagnosticStage.Selection] = "EXT002",
            [ExternalDiagnosticStage.Resolve] = "EXT020",
            [ExternalDiagnosticStage.Parse] = "GLTF010",
            [ExternalDiagnosticStage.Convert] = "GLTF020",
            [ExternalDiagnosticStage.Validate] = "IMP010",
            [ExternalDiagnosticStage.Materialize] = "PBR010",
            [ExternalDiagnosticStage.Bind] = "ANIM010",
            [ExternalDiagnosticStage.Render] = "GPU010",
        };

    private static readonly IReadOnlyDictionary<ExternalDiagnosticStage, string> NotSupportedCodes =
        new Dictionary<ExternalDiagnosticStage, string>
        {
            [ExternalDiagnosticStage.Selection] = "EXT003",
            [ExternalDiagnosticStage.Resolve] = "EXT021",
            [ExternalDiagnosticStage.Parse] = "GLTF011",
            [ExternalDiagnosticStage.Convert] = "GLTF021",
            [ExternalDiagnosticStage.Validate] = "IMP011",
            [ExternalDiagnosticStage.Materialize] = "PBR011",
            [ExternalDiagnosticStage.Bind] = "ANIM011",
            [ExternalDiagnosticStage.Render] = "GPU011",
        };

    /// <summary>
    /// Maps one exception to a structured diagnostic using the stage and exception type.
    /// Invalid data and unsupported-subset failures stay Error severity; anything else
    /// becomes EXT999 InternalFailure with the exception retained for local logs.
    /// </summary>
    public static ExternalModelDiagnostic Map(
        ExternalDiagnosticStage stage,
        ExternalDiagnosticSourceRole source,
        string? context,
        Exception exception,
        string? fixHint = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is InvalidDataException)
        {
            return new ExternalModelDiagnostic(
                InvalidDataCodes[stage],
                ExternalDiagnosticSeverity.Error,
                stage,
                source,
                context,
                exception.Message,
                fixHint);
        }

        if (exception is NotSupportedException)
        {
            return new ExternalModelDiagnostic(
                NotSupportedCodes[stage],
                ExternalDiagnosticSeverity.Error,
                stage,
                source,
                context,
                exception.Message,
                fixHint);
        }

        return new ExternalModelDiagnostic(
            InternalFailureCode,
            ExternalDiagnosticSeverity.Error,
            stage,
            source,
            context,
            $"Internal failure: {exception.GetType().Name}: {exception.Message}",
            fixHint,
            exceptionDetail: exception);
    }

    /// <summary>
    /// Deterministic order: severity descending, then stage, then source, then context
    /// (null first), then code; remaining duplicates (same identity) collapse with a summed
    /// occurrence count.
    /// </summary>
    public static IReadOnlyList<ExternalModelDiagnostic> SortAndCollapse(
        IEnumerable<ExternalModelDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var collapsed = new Dictionary<ExternalModelDiagnostic, List<ExternalModelDiagnostic>>();

        foreach (var diagnostic in diagnostics)
        {
            if (!collapsed.TryGetValue(diagnostic, out var group))
            {
                group = [];
                collapsed[diagnostic] = group;
            }

            group.Add(diagnostic);
        }

        return collapsed.Values
            .Select(group => group.Count == 1
                ? group[0]
                : new ExternalModelDiagnostic(
                    group[0].Code,
                    group[0].Severity,
                    group[0].Stage,
                    group[0].Source,
                    group[0].Context,
                    group[0].Message,
                    group[0].FixHint,
                    group.Sum(d => d.OccurrenceCount),
                    group[0].ExceptionDetail))
            .OrderBy(d => d.Severity, SeverityComparer.Instance)
            .ThenBy(d => (int)d.Stage)
            .ThenBy(d => (int)d.Source)
            .ThenBy(d => d.Context, StringComparer.Ordinal)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class SeverityComparer : IComparer<ExternalDiagnosticSeverity>
    {
        public static readonly SeverityComparer Instance = new();

        public int Compare(ExternalDiagnosticSeverity x, ExternalDiagnosticSeverity y)
        {
            // Error first, then Warning, then Info.
            return Rank(y) - Rank(x);

            static int Rank(ExternalDiagnosticSeverity severity)
            {
                return severity switch
                {
                    ExternalDiagnosticSeverity.Error => 2,
                    ExternalDiagnosticSeverity.Warning => 1,
                    _ => 0,
                };
            }
        }
    }
}
