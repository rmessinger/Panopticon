#nullable enable

using System.Collections.Generic;
using CommandLine;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// `validate` options (WI-H5-03). Map mode takes `--input` (the legacy .map). Units mode takes a roster
/// (`--p6b` and/or unit names) with the install from `--game-path`, `--install`, or CNC_GENERALS_PATH.
/// </summary>
[Verb("validate", HelpText = "Compare a converted package with its source and report every mismatch.")]
public sealed class ValidateOptions
{
    [Option('i', "input", Required = false, HelpText = "Absolute legacy .map path (map mode).")]
    public string? InputPath { get; set; }

    [Option('p', "package", Required = true, HelpText = "Absolute converted package directory.")]
    public string PackageDirectory { get; set; } = string.Empty;

    [Option('g', "game-path", Required = false, HelpText = "Generals installation directory (units mode).")]
    public string? GamePath { get; set; }

    [Option("install", Required = false, HelpText = "Alias of --game-path.")]
    public string? InstallPath { get; set; }

    [Option("p6b", Required = false, HelpText = "Include the P6b archetype roster (units mode).")]
    public bool P6b { get; set; }

    [Option('u', "units", Separator = ',', Required = false, HelpText = "Unit object names (units mode).")]
    public IEnumerable<string>? Units { get; set; }

    [Option("json", Required = false, HelpText = "Write the report as JSON to standard output.")]
    public bool Json { get; set; }

    [Value(0, MetaName = "unit", Required = false, HelpText = "Unit object names (units mode).")]
    public IEnumerable<string>? PositionalUnits { get; set; }
}
