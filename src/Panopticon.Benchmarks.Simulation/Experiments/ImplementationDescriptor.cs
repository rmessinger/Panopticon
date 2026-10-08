using System;
using System.Text.RegularExpressions;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// One registered seam implementation: its stable identity (seam key plus implementation id),
/// a display name, a version string, and the factory that produces the runtime selection
/// value for the seam. Settings construction goes through this descriptor, never through
/// direct construction at call sites.
/// </summary>
public sealed partial class ImplementationDescriptor
{
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]*$")]
    private static partial Regex IdentifierPattern();

    public ImplementationDescriptor(
        string seam,
        string id,
        string displayName,
        string version,
        Func<object> createSettings)
    {
        ValidateIdentifier(seam, "seam");
        ValidateIdentifier(id, "id");
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(createSettings);

        Seam = seam;
        Id = id;
        DisplayName = displayName;
        Version = version;
        _createSettings = createSettings;
    }

    private readonly Func<object> _createSettings;

    public string Seam { get; }

    public string Id { get; }

    public string DisplayName { get; }

    public string Version { get; }

    /// <summary>Produces a fresh settings object for one run cell; factories must be pure.</summary>
    public object CreateSettings()
    {
        return _createSettings();
    }

    private static void ValidateIdentifier(string value, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException(
                $"The implementation {kind} '{value}' is invalid; identifiers are lowercase letters, digits, '-', and '.'.",
                nameof(value));
        }
    }
}
