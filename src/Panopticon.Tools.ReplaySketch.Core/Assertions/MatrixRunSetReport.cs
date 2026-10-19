#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Tools.ReplaySketch.Assertions;

/// <summary>
/// WI-A5-16: one persisted matrix cell. The key is the sanitized binding identity, the
/// relative path points at the cell's run bundle under the run-set root, and the status
/// separates assertion verdicts from execution failures.
/// </summary>
public sealed class MatrixRunSetCell
{
    public string Key { get; }

    public IReadOnlyDictionary<string, string> Bindings { get; }

    public IReadOnlyDictionary<string, string> SeamSelections { get; }

    /// <summary>Passed or failed; failed covers both assertion failures and execution errors.</summary>
    public string Status { get; }

    /// <summary>Execution error message; null when the cell ran to completion.</summary>
    public string? Error { get; }

    /// <summary>Assertion verdict; false when the cell failed before assertions could run.</summary>
    public bool AllPassed { get; }

    /// <summary>Bundle path relative to the run-set root, e.g. cells/seed=11.</summary>
    public string RelativePath { get; }

    public MatrixRunSetCell(
        string key,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlyDictionary<string, string> seamSelections,
        string status,
        string? error,
        bool allPassed,
        string relativePath)
    {
        Key = key;
        Bindings = bindings;
        SeamSelections = seamSelections;
        Status = status;
        Error = error;
        AllPassed = allPassed;
        RelativePath = relativePath;
    }
}

/// <summary>
/// WI-A5-16: aggregate over a persisted matrix run set. Serializes to the run-set manifest
/// with relative cell paths only, so a run-set directory relocates as a unit.
/// </summary>
public sealed class MatrixRunSetReport
{
    public const int CurrentSchemaVersion = 1;

    public IReadOnlyList<string> Axes { get; }

    public IReadOnlyList<MatrixRunSetCell> Cells { get; }

    public IReadOnlyList<string> BindingDeclarations { get; }

    public int FailedCells => Cells.Count(cell => cell.Status != "passed");

    public bool AllPassed => FailedCells == 0;

    public MatrixRunSetReport(
        IReadOnlyList<string> axes,
        IReadOnlyList<MatrixRunSetCell> cells,
        IReadOnlyList<string> bindingDeclarations)
    {
        Axes = axes;
        Cells = cells;
        BindingDeclarations = bindingDeclarations;
    }

    public string ToJson()
    {
        var payload = new
        {
            schemaVersion = CurrentSchemaVersion,
            axes = Axes,
            bindings = BindingDeclarations,
            totalCells = Cells.Count,
            failedCells = FailedCells,
            cells = Cells.Select(cell => new
            {
                key = cell.Key,
                bindings = cell.Bindings,
                seamSelections = cell.SeamSelections.Count == 0 ? null : cell.SeamSelections,
                status = cell.Status,
                error = cell.Error,
                allPassed = cell.AllPassed,
                relativePath = cell.RelativePath,
            }),
        };

        return JsonSerializer.Serialize(payload);
    }
}
