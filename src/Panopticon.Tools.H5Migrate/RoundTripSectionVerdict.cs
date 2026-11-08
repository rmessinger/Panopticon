#nullable enable

using System.Collections.Generic;

namespace Panopticon.Tools.H5Migrate;

/// <summary>Verdict for one comparison section; passed when it carries no findings.</summary>
public sealed record RoundTripSectionVerdict(string Section, IReadOnlyList<RoundTripFinding> Findings)
{
    public bool Passed => Findings.Count == 0;
}
