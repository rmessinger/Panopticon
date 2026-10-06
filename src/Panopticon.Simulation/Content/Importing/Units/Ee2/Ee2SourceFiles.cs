#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.IO;

namespace Panopticon.Content.Importing.Units.Ee2;

/// <summary>
/// The bounded EE2 inputs: database archives (later layers override earlier ones when composed),
/// the graphics archive used for model and animation keys, and the exact DDF and CSV paths read.
/// </summary>
public sealed record Ee2SourceFiles(FileSystem Database, FileSystem Graphics, IReadOnlyList<string> DdfPaths, string UnitTablePath)
{
    public const string DefaultUnitTablePath = "db/TechTree/upgrade_unittypes.csv";
}
