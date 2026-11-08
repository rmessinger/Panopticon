#nullable enable

using System;
using Panopticon.Content.Importing.Units;

namespace Panopticon.Tools.H5Migrate;

/// <summary>
/// Provenance differences the `validate` subcommand accepts. The rules are enumerated and any
/// other difference fails:
/// 1. FilePath differs only by directory separator style ('\' against '/').
/// 2. ArchiveLayer is absent on one side and set on the other.
/// SourceId and Line always compare strictly. The convert-time gate never uses this allowlist.
/// </summary>
internal static class RoundTripProvenanceAllowlist
{
    public static bool Permits(Provenance expected, Provenance actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (!string.Equals(expected.SourceId, actual.SourceId, StringComparison.Ordinal) || expected.Line != actual.Line)
        {
            return false;
        }

        var pathsMatch = string.Equals(
            expected.FilePath.Replace('\\', '/'),
            actual.FilePath.Replace('\\', '/'),
            StringComparison.Ordinal);

        var layerMatches = string.Equals(expected.ArchiveLayer, actual.ArchiveLayer, StringComparison.Ordinal)
            || expected.ArchiveLayer is null
            || actual.ArchiveLayer is null;

        return pathsMatch && layerMatches;
    }
}
