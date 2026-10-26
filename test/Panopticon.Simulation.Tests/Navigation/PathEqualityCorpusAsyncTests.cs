// WI-N1-03: async results equal synchronous results across the whole path-equality corpus.
// The worker's persistent graph must reproduce the live graph's output for every corpus
// case, including impassable endpoints and cross-region unreachable pairs, with all
// requests served from one shared passability snapshot.
//
// Run with: dotnet test src --filter "FullyQualifiedName~PathEqualityCorpusAsyncTests"

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Panopticon.Navigation;
using Xunit;

namespace Panopticon.Tests.Navigation;

// Serialized with the other Navigation classes: these tests assert deltas of the
// process-global Navigation.PathRequestCount, which parallel test classes would race.
[Collection("Navigation")]
public class PathEqualityCorpusAsyncTests
{
    [Fact, Trait("Category", "Roadmap")]
    public void AsyncResultsEqualSynchronousResultsOnTheCorpus()
    {
        var fixture = PathEqualityCorpus.Load(CorpusFixturePath);
        var navigation = AsyncPathRequestTests.BuildCorpusNavigation();

        // All cases share one submission-tick snapshot: one version, one worker-graph apply.
        var handles = new List<PathRequestHandle>();

        foreach (var testCase in fixture.Cases)
        {
            handles.Add(navigation.SubmitPathRequest(
                AsyncPathRequestTests.NodeWorld(testCase.StartX, testCase.StartY),
                AsyncPathRequestTests.NodeWorld(testCase.EndX, testCase.EndY)));
        }

        var twins = new List<IList<Vector3>>();
        var twinEndFlags = new List<bool>();

        foreach (var testCase in fixture.Cases)
        {
            twins.Add(navigation.CalculatePath(
                AsyncPathRequestTests.NodeWorld(testCase.StartX, testCase.StartY),
                AsyncPathRequestTests.NodeWorld(testCase.EndX, testCase.EndY),
                out var endIsPassable));
            twinEndFlags.Add(endIsPassable);
        }

        WaitForCompletion(handles);
        navigation.DrainPathCompletions();

        var routed = 0;
        var failed = 0;

        for (var i = 0; i < fixture.Cases.Count; i++)
        {
            var testCase = fixture.Cases[i];
            Assert.True(handles[i].TryGetResult(out var waypoints, out var endIsPassable), testCase.Name);

            Assert.Equal(twinEndFlags[i], endIsPassable);
            Assert.Equal(twins[i].Count, waypoints.Count);

            for (var j = 0; j < twins[i].Count; j++)
            {
                Assert.Equal(twins[i][j], waypoints[j]);
            }

            if (waypoints.Count > 0)
            {
                routed++;
            }
            else
            {
                failed++;
            }
        }

        Assert.True(routed > 0, "corpus must contain routed cases");
        Assert.True(failed > 0, "corpus must contain failed cases");
    }

    private static string CorpusFixturePath =>
        Path.Combine(Environment.CurrentDirectory, "Navigation", "PathEqualityCorpus.json");

    private static void WaitForCompletion(IReadOnlyList<PathRequestHandle> handles)
    {
        var deadline = Stopwatch.GetTimestamp() + TimeSpan.FromSeconds(30).Ticks;

        while (Stopwatch.GetTimestamp() < deadline)
        {
            var allDone = true;

            foreach (var handle in handles)
            {
                if (!handle.IsCompleted && !handle.IsCancelled)
                {
                    allDone = false;

                    break;
                }
            }

            if (allDone)
            {
                return;
            }

            System.Threading.Thread.Sleep(10);
        }

        throw new TimeoutException("Async path requests did not finish within 30 seconds.");
    }
}
