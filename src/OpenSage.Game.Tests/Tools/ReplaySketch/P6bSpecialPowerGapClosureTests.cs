using System.Collections.Generic;
using System.Linq;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

// WI-P6b-07: Special-power gap closure.
// Runs all six P6b archetype contract scenarios together, collects every "P6b gap:" failure, and
// asserts each surfaced gap is classified and documented as an accepted out-of-scope gap with a
// follow-up. No unimplemented handler may silently pass: a gap not in P6bAcceptedGaps fails here.

public class P6bSpecialPowerGapClosureTests : P6bContractTestBase
{
    private record SurfacedGap(string Unit, string SpecialPower, string Detail);

    private static List<SurfacedGap> CollectAllGaps()
    {
        var gaps = new List<SurfacedGap>();
        foreach (var archetype in UnitArchetypeCatalog.Archetypes)
        {
            foreach (var faction in UnitArchetypeCatalog.Factions)
            {
                if (!UnitArchetypeCatalog.HasReferenceUnit(archetype, faction))
                {
                    continue;
                }

                var scenario = UnitArchetypeCatalog.BuildScenario(archetype, faction);
                var report = RunContract(scenario);

                foreach (var result in report.Gaps)
                {
                    // Extract the special-power token from the gap detail ("<unit> special power <X> ...").
                    var specialPower = ExtractSpecialPower(result.Detail ?? string.Empty);
                    gaps.Add(new SurfacedGap(scenario.UnitDefinitionName, specialPower, result.Detail ?? string.Empty));
                }
            }
        }
        return gaps;
    }

    private static string ExtractSpecialPower(string detail)
    {
        const string marker = "special power ";
        var idx = detail.IndexOf(marker, System.StringComparison.Ordinal);
        if (idx < 0)
        {
            return string.Empty;
        }
        var rest = detail[(idx + marker.Length)..];
        var end = rest.IndexOf(' ');
        return end < 0 ? rest : rest[..end];
    }

    [Fact]
    public void AllArchetypeContracts_RunTogether_ProduceTheDocumentedGapSet()
    {
        var gaps = CollectAllGaps();

        // Every surfaced gap must be a registered accepted out-of-scope gap with a follow-up.
        Assert.NotEmpty(gaps);
        foreach (var gap in gaps)
        {
            Assert.True(P6bAcceptedGaps.IsAccepted(gap.SpecialPower),
                $"Surfaced gap '{gap.SpecialPower}' ({gap.Unit}) is not a documented accepted gap: {gap.Detail}");
            var accepted = P6bAcceptedGaps.Get(gap.SpecialPower);
            Assert.NotNull(accepted);
            Assert.Equal(GapClassification.OutOfScopeFollowUp, accepted!.Classification);
            Assert.False(string.IsNullOrWhiteSpace(accepted.FollowUp));
        }

        // The full documented set must be surfaced (ParticleUplinkCannon, NuclearMissile,
        // ScudStorm) across the matrix. CleanupArea is implemented (P6c) and must not surface.
        var surfacedNames = gaps.Select(g => g.SpecialPower).Distinct().OrderBy(n => n).ToList();
        var documentedNames = P6bAcceptedGaps.All
            .Select(g => g.SpecialPowerName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
        Assert.Equal(documentedNames, surfacedNames);
    }

    [Fact]
    public void NonCombatArchetypes_HaveNoSilentlyPassedGaps()
    {
        // Archetypes whose reference units expose no command-set special powers must report zero
        // gaps (infantry, vehicle, aircraft, builder). This locks that no handler silently passes.
        var noGapArchetypes = new[]
        {
            UnitArchetype.Infantry,
            UnitArchetype.Vehicle,
            UnitArchetype.Aircraft,
            UnitArchetype.Builder,
        };

        foreach (var archetype in noGapArchetypes)
        {
            foreach (var faction in UnitArchetypeCatalog.Factions)
            {
                if (!UnitArchetypeCatalog.HasReferenceUnit(archetype, faction))
                {
                    continue;
                }

                var report = RunContract(UnitArchetypeCatalog.BuildScenario(archetype, faction));
                Assert.False(report.HasGaps,
                    $"{archetype}/{faction} should have no gaps but reported: "
                    + string.Join("; ", report.GapMessages));
            }
        }
    }

    [Fact]
    public void AcceptedGaps_AreDocumentedWithReasonAndFollowUp()
    {
        foreach (var gap in P6bAcceptedGaps.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(gap.SpecialPowerName));
            Assert.False(string.IsNullOrWhiteSpace(gap.Reason));
            Assert.False(string.IsNullOrWhiteSpace(gap.FollowUp));
            Assert.Equal(GapClassification.OutOfScopeFollowUp, gap.Classification);
        }
    }
}
