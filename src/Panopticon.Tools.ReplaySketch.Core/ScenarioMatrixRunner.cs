using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Data;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch;

public static class ScenarioMatrixRunner
{
    public const string ManifestFileName = "runset.json";
    public const string CellsFolderName = "cells";

    public static MatrixReport Run(ScenarioMatrix matrix, GameInstallation installation, string tempDirectory)
    {
        var cells = matrix.Expand();
        var results = new List<CellResult>(cells.Count);

        foreach (var cell in cells)
        {
            var report = HarnessRunner.RunAndAssert(cell.ConcreteScenario, installation, tempDirectory);
            results.Add(new CellResult(cell.Bindings, report));
        }

        return new MatrixReport(results);
    }

    /// <summary>
    /// WI-A5-16: runs every cell into its own keyed run bundle under <paramref name="runSetRoot"/>
    /// and writes the aggregate manifest. A cell that throws does not abort the set: it is recorded
    /// as failed with its error, and the remaining cells still run.
    /// </summary>
    public static MatrixRunSetReport RunToBundles(
        ScenarioMatrix matrix,
        GameInstallation installation,
        string tempDirectory,
        string runSetRoot)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentException.ThrowIfNullOrEmpty(runSetRoot);

        var report = RunCells(matrix, runSetRoot, (cell, cellDirectory) =>
            HarnessRunner.RunAndAssert(cell.ConcreteScenario, installation, tempDirectory, bundleDirectory: cellDirectory));
        WriteRunSetManifest(runSetRoot, report);

        return report;
    }

    internal static MatrixRunSetReport RunCells(
        ScenarioMatrix matrix,
        string runSetRoot,
        Func<MatrixCell, string, AssertionReport> runCell)
    {
        var cells = matrix.Expand();
        EnsureDistinctKeys(cells);
        var results = new List<MatrixRunSetCell>(cells.Count);

        foreach (var cell in cells)
        {
            var key = BuildCellKey(cell.Bindings);
            var relativePath = $"{CellsFolderName}/{key}";
            var cellDirectory = Path.Combine(runSetRoot, CellsFolderName, key);
            Directory.CreateDirectory(cellDirectory);

            try
            {
                var assertions = runCell(cell, cellDirectory);
                results.Add(new MatrixRunSetCell(
                    key, cell.Bindings, cell.SeamSelections, assertions.AllPassed ? "passed" : "failed", null,
                    assertions.AllPassed, relativePath));
            }
            catch (Exception ex)
            {
                results.Add(new MatrixRunSetCell(key, cell.Bindings, cell.SeamSelections, "failed", ex.Message, false, relativePath));
            }
        }

        return new MatrixRunSetReport(
            matrix.Axes.Select(axis => axis.Name).ToArray(),
            results,
            matrix.Bindings.Select(DescribeBinding).ToArray());
    }

    internal static void WriteRunSetManifest(string runSetRoot, MatrixRunSetReport report)
    {
        Directory.CreateDirectory(runSetRoot);
        File.WriteAllText(Path.Combine(runSetRoot, ManifestFileName), report.ToJson());
    }

    /// <summary>
    /// Stable cell key: axis=value pairs ordinal-sorted and joined with ';', sanitized for
    /// file names. Collisions after sanitizing are rejected so two distinct cells can never
    /// share a bundle directory.
    /// </summary>
    internal static string BuildCellKey(IReadOnlyDictionary<string, string> bindings)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var pairs = bindings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{Sanitize(pair.Key, invalid)}={Sanitize(pair.Value, invalid)}");
        return string.Join(";", pairs);
    }

    private static void EnsureDistinctKeys(IReadOnlyList<MatrixCell> cells)
    {
        var seen = new Dictionary<string, string>();
        foreach (var cell in cells)
        {
            var raw = string.Join(";", cell.Bindings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
            var key = BuildCellKey(cell.Bindings);
            if (seen.TryGetValue(key, out var first))
            {
                throw new InvalidOperationException(
                    $"Two matrix cells sanitize to the same key '{key}': [{first}] and [{raw}]. Rename one axis value.");
            }

            seen[key] = raw;
        }
    }

    private static string Sanitize(string value, char[] invalid)
    {
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static string DescribeBinding(MatrixBinding binding)
    {
        var target = binding.Target.ToString().ToLowerInvariant();
        return binding.Key is null ? $"{target}:{binding.Axis}" : $"{target}:{binding.Axis}:{binding.Key}";
    }
}
