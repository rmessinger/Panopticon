#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.IO;

namespace Panopticon.Content.Importing.Units.Ee2;

/// <summary>
/// The bounded EE2 inputs: database archives (later layers override earlier ones when composed),
/// the graphics archive used for model and animation keys, and the exact DDF and CSV paths read.
/// </summary>
public sealed record Ee2SourceFiles(FileSystem Database, FileSystem Graphics, IReadOnlyList<string> DdfPaths, string UnitTablePath)
{
    public const string DefaultUnitTablePath = "db/TechTree/upgrade_unittypes.csv";

    public const string UnitsDirectory = "db/Units/";

    /// <summary>Missile table referenced by attack <c>missileName</c> values; outside <c>db/Units/</c>.</summary>
    public const string MissileTablePath = "db/Visuals/ammo.ddf";

    /// <summary>
    /// Patch archives present beside the database but not composed into these inputs. Reported by
    /// the adapter so stock-layer results are never mistaken for as-patched values.
    /// </summary>
    public IReadOnlyList<string> UncomposedPatchArchives { get; init; } = [];

    /// <summary>
    /// Full-inventory inputs: every <c>db/Units/*.ddf</c> in the database archive, in a fixed
    /// order, plus the unit table. Bounded profiles keep the constructor and its explicit paths.
    /// </summary>
    public static Ee2SourceFiles FromDatabase(FileSystem database, FileSystem graphics, IReadOnlyList<string>? uncomposedPatchArchives = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        var ddfPaths = database.GetFilesInDirectory(UnitsDirectory, "*.ddf", SearchOption.AllDirectories)
            .Select(entry => entry.FilePath.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new Ee2SourceFiles(database, graphics, ddfPaths, DefaultUnitTablePath)
        {
            UncomposedPatchArchives = uncomposedPatchArchives ?? [],
        };
    }
}
