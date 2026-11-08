#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Tools.H5Migrate;

/// <summary>Round-trip validator result: per-section and per-record verdicts.</summary>
public sealed record RoundTripReport(
    string Mode,
    IReadOnlyList<RoundTripSectionVerdict> Sections,
    IReadOnlyList<RoundTripRecordVerdict> Records)
{
    public bool Passed => Sections.All(section => section.Passed);

    public static RoundTripReport Create(
        string mode,
        IEnumerable<string> canonicalSections,
        IEnumerable<RoundTripFinding> findings,
        IEnumerable<RoundTripRecordVerdict> records)
    {
        var findingList = findings.ToList();
        var sectionNames = canonicalSections.ToList();

        foreach (var finding in findingList)
        {
            if (!sectionNames.Contains(finding.Section))
            {
                sectionNames.Add(finding.Section);
            }
        }

        var sections = sectionNames
            .Select(name => new RoundTripSectionVerdict(name, findingList.Where(f => f.Section == name).ToList()))
            .ToList();

        return new RoundTripReport(mode, sections, records.ToList());
    }
}
