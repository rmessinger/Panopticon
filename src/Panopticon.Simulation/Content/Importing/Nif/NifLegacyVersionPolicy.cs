#nullable enable

using System.Globalization;
using Panopticon.FileFormats.Nif;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Version-dependent rules for NIF animation import. Legacy 10.0.1.0
/// clips pad text-key marker values with trailing spaces ("end ") and
/// use the cooked proof flag patterns 0x05 and 0x0C where 10.1 clips
/// use 0x2C. Controlled-block target names are exact identities in
/// both vintages and are never normalized here.
/// </summary>
internal static class NifLegacyVersionPolicy
{
    internal const ushort ModernFlags = 0x2C;
    internal const ushort LegacyProofFlagsIdle = 0x0C;
    internal const ushort LegacyProofFlagsWalk = 0x05;

    /// <summary>
    /// Trims padded marker values before comparing "start" and "end".
    /// Legacy text keys carry trailing spaces; the comparison result is
    /// identical for already-trimmed 10.1 values.
    /// </summary>
    public static string TrimTextKeyValue(string value)
    {
        return value.Trim();
    }

    public static bool IsAcceptedControllerFlags(ushort flags, uint version)
    {
        if (version == NifHeader.LegacyVersion)
        {
            return flags is LegacyProofFlagsIdle or LegacyProofFlagsWalk;
        }

        return flags == ModernFlags;
    }

    public static string DescribeAcceptedFlags(uint version)
    {
        return version == NifHeader.LegacyVersion
            ? "0x0C and 0x05 (the cooked 10.0.1.0 proof patterns)"
            : "0x2C";
    }

    public static string FormatFlags(ushort flags)
    {
        return "0x" + flags.ToString("X4", CultureInfo.InvariantCulture);
    }
}
