using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Tools.TestHarness;
using Xunit;

namespace Panopticon.Tests.Tools.TestHarness;

public class NavigationDeadlockPlannerTests
{
    [Fact]
    public void BuildCases_ListsTheSixDeepDiveCasesInOrder()
    {
        var cases = NavigationDeadlockCasePlanner.BuildCases();

        var names = cases.Select(plan => plan.Name).ToArray();
        Assert.Equal(
        [
            NavigationDeadlockCasePlanner.HeadOnPair,
            NavigationDeadlockCasePlanner.CorridorOpposed20,
            NavigationDeadlockCasePlanner.CrowdGoal100,
            NavigationDeadlockCasePlanner.QueueBehindIdle,
            NavigationDeadlockCasePlanner.AsyncNoRoute,
            NavigationDeadlockCasePlanner.SquadOffsetRetrace,
        ], names);
        Assert.All(cases, plan => Assert.False(string.IsNullOrWhiteSpace(plan.Mechanism)));
    }

    [Fact]
    public void FilterCases_KeepsOnlyRequestedNames()
    {
        var cases = NavigationDeadlockCasePlanner.BuildCases();

        var filtered = NavigationDeadlockCasePlanner.FilterCases(
            cases, $"{NavigationDeadlockCasePlanner.HeadOnPair}, {NavigationDeadlockCasePlanner.CrowdGoal100}");

        Assert.Equal(
            new[] { NavigationDeadlockCasePlanner.HeadOnPair, NavigationDeadlockCasePlanner.CrowdGoal100 },
            filtered.Select(plan => plan.Name));
    }

    [Fact]
    public void FilterCases_EmptyFilterReturnsEveryCase()
    {
        var cases = NavigationDeadlockCasePlanner.BuildCases();

        Assert.Same(cases, NavigationDeadlockCasePlanner.FilterCases(cases, null));
        Assert.Same(cases, NavigationDeadlockCasePlanner.FilterCases(cases, "  "));
        Assert.Empty(NavigationDeadlockCasePlanner.FilterCases(cases, "no-such-case"));
    }

    [Fact]
    public void Report_RoundTripsThroughJson()
    {
        var records = new List<NavigationStallRecord>
        {
            new(7, "AmericaTankCrusader", "BlockedByUnit", 9, 120, new System.Numerics.Vector3(10, 20, 0), 340),
        };
        var cycles = new List<NavigationWaitForCycle> { new([7, 9]) };
        var result = new NavigationDeadlockCaseResult(
            NavigationDeadlockCasePlanner.HeadOnPair,
            "mechanism",
            UnitsSpawned: 2,
            SettledUnits: 0,
            FramesRun: 3600,
            TerminationReason: "BudgetEnded",
            FramesToSettle: null,
            PathSpanMs: 12.5,
            MaxTickPathSpanMs: 3.25,
            SyncRequests: 2,
            AsyncSubmitted: 0,
            AsyncApplied: 0,
            AsyncCancelled: 0,
            ImpassableRetracePoints: 0,
            ReroutedFollowers: 0,
            BlockedRepaths: 0,
            MechanismConfirmed: true,
            MechanismNote: "2 mutually blocked records, 1 wait-for cycle(s), max 120 frames without progress.",
            StallRecords: records,
            WaitForCyclesAtEnd: cycles);
        var report = new NavigationDeadlockReport(
            NavigationDeadlockReport.CurrentSchemaVersion,
            "maps/alpine assault/alpine assault.map",
            Seed: 1,
            FrameBudget: 3600,
            ValidateOffsetRoutes: false,
            BlockedResolution: false,
            DestinationSlots: false,
            Cases: [result]);

        var path = Path.Combine(Path.GetTempPath(), $"navigation-deadlock-{Guid.NewGuid():N}.json");
        var textPath = Path.ChangeExtension(path, ".txt");
        try
        {
            report.WriteJson(path);
            report.WriteText(textPath);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            var caseElement = root.GetProperty("cases")[0];
            Assert.Equal(NavigationDeadlockCasePlanner.HeadOnPair, caseElement.GetProperty("name").GetString());
            Assert.Equal(3600, caseElement.GetProperty("framesRun").GetInt32());
            Assert.Equal("BudgetEnded", caseElement.GetProperty("terminationReason").GetString());
            Assert.True(caseElement.GetProperty("mechanismConfirmed").GetBoolean());
            var stall = caseElement.GetProperty("stallRecords")[0];
            Assert.Equal(7u, stall.GetProperty("unitId").GetUInt32());
            Assert.Equal("BlockedByUnit", stall.GetProperty("reason").GetString());
            Assert.Equal(9u, stall.GetProperty("blockerId").GetUInt32());
            Assert.Equal(9u, caseElement.GetProperty("waitForCyclesAtEnd")[0].GetProperty("unitIds")[1].GetUInt32());
            Assert.Contains("head-on-pair", File.ReadAllText(textPath));
        }
        finally
        {
            File.Delete(path);
            File.Delete(textPath);
        }
    }
}
