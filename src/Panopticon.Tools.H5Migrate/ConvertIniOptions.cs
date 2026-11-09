#nullable enable

using System.Collections.Generic;
using CommandLine;

namespace Panopticon.Tools.H5Migrate;

/// <summary>Command-line options for the convert-ini subcommand (WI-H5-02).</summary>
[Verb("convert-ini", HelpText = "Converts a bounded legacy Generals INI roster into an H4 unit package.")]
public sealed class ConvertIniOptions
{
    [Option('o', "out", Required = true, HelpText = "Output package directory.")]
    public string OutputDirectory { get; set; } = null!;

    [Option('g', "game-path", Required = false, HelpText = "Path to the C&C Generals installation. Defaults to the CNC_GENERALS_PATH environment variable.")]
    public string? GamePath { get; set; }

    [Option('i', "install", Required = false, HelpText = "Alias for --game-path.")]
    public string? InstallPath { get; set; }

    [Option("p6b", Required = false, Default = false, HelpText = "Preset expanding to the six P6b USA archetype units.")]
    public bool P6b { get; set; }

    [Option('u', "units", Required = false, Separator = ',', HelpText = "Explicit unit object names to convert.")]
    public IEnumerable<string>? Units { get; set; }

    [Value(0, Required = false, HelpText = "Explicit unit object names to convert.")]
    public IEnumerable<string>? PositionalUnits { get; set; }
}
