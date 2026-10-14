// WI-B4-05: bounded async path requests beside the synchronous API.
// Pins that async results match the synchronous twin exactly (same waypoints, same
// endIsPassable), that completions apply in submission order, that cancelled and dead-requester
// requests never apply, that identical submissions reproduce byte-identical completions, and
// that the frame bound accounts deadline misses. Async submissions
// never increment the synchronous PathRequestCount.
//
// Run with: dotnet test src --filter "FullyQualifiedName~AsyncPathRequestTests"

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

// Serialized with the other Navigation classes: these tests assert deltas of the
// process-global Navigation.PathRequestCount, which parallel test classes would race.
[Collection("Navigation")]
public class AsyncPathRequestTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public void AsyncPathMatchesSynchronousTwinExactly()
    {
        var (navigation, heightMap, start, end) = CreateNavigation();
        var impassable = FindImpassablePosition(navigation, heightMap, start);

        var cases = new[]
        {
            (start, end),
            (end, start),
            (start, impassable),
        };
        var requester = NewUnit(start);
        var handles = new List<PathRequestHandle>();

        var before = NavigationClass.PathRequestCount;

        foreach (var (from, to) in cases)
        {
            handles.Add(navigation.SubmitPathRequest(from, to, requester));
        }

        // Async submissions never touch the synchronous counter.
        Assert.Equal(before, NavigationClass.PathRequestCount);

        // Synchronous twins computed after submission: the fixture graph never changes, so
        // the snapshot the workers searched equals the live graph.
        var twins = new List<IList<Vector3>>();
        var twinEndFlags = new List<bool>();
        foreach (var (from, to) in cases)
        {
            twins.Add(navigation.CalculatePath(from, to, out var twinEndIsPassable));
            twinEndFlags.Add(twinEndIsPassable);
        }

        Assert.Equal(before + cases.Length, NavigationClass.PathRequestCount);

        WaitForCompletion(handles);

        navigation.DrainPathCompletions();

        for (var i = 0; i < cases.Length; i++)
        {
            Assert.True(handles[i].TryGetResult(out var waypoints, out var endIsPassable),
                $"case {i} should have a result");
            Assert.Equal(twins[i].Count, waypoints.Count);

            for (var j = 0; j < twins[i].Count; j++)
            {
                Assert.Equal(twins[i][j], waypoints[j]);
            }

            // The nudged-impassable case reports false exactly like its synchronous twin.
            Assert.Equal(twinEndFlags[i], endIsPassable);
        }
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CompletionsApplyInSubmissionOrder()
    {
        var (navigation, heightMap, start, end) = CreateNavigation();
        var mid = heightMap.GetPosition(heightMap.Width / 2, heightMap.Height / 2);

        var appliedOrder = new List<int>();
        var handles = new List<PathRequestHandle>();
        var endpoints = new[] { end, mid, start, end };

        for (var i = 0; i < endpoints.Length; i++)
        {
            var index = i;
            handles.Add(navigation.SubmitPathRequest(start, endpoints[i], onCompleted: _ => appliedOrder.Add(index)));
        }

        WaitForCompletion(handles);

        navigation.DrainPathCompletions();

        Assert.Equal(new[] { 0, 1, 2, 3 }, appliedOrder);

        foreach (var handle in handles)
        {
            Assert.True(handle.TryGetResult(out _, out _));
        }
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CancelledAndDeadRequestsNeverApply()
    {
        var (navigation, _, start, end) = CreateNavigation();

        // Dead requester: the drain discards the request even if the worker finished it first.
        var deadRequester = NewUnit(start);
        deadRequester.SetObjectStatus(ObjectStatus.Destroyed, true);
        var deadHandle = navigation.SubmitPathRequest(start, end, deadRequester, onCompleted: _ => Assert.Fail("dead requester applied"));

        // Manual cancel racing the worker: whichever wins, the drain resolves to cancelled.
        var cancelledHandle = navigation.SubmitPathRequest(end, start, onCompleted: _ => Assert.Fail("cancelled request applied"));
        cancelledHandle.Cancel();

        WaitForTerminalState(deadHandle, cancelledHandle);

        navigation.DrainPathCompletions();

        Assert.True(deadHandle.IsCancelled);
        Assert.True(cancelledHandle.IsCancelled);
        Assert.False(deadHandle.TryGetResult(out _, out _));
        Assert.False(cancelledHandle.TryGetResult(out _, out _));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SameSeedBatchReproducesCompletionsExactly()
    {
        // The queue-level seeded-equality bar (card AC3): two identical submissions produce
        // byte-identical completions. Game-level twin runs land with the first engine consumer
        // (M4); no game code calls SubmitPathRequest yet.
        var (navigation, heightMap, start, end) = CreateNavigation();
        var mid = heightMap.GetPosition(heightMap.Width / 2, heightMap.Height / 2);
        var cases = new[] { (start, end), (end, start), (start, mid), (start, start) };

        List<List<Vector3>> RunBatch()
        {
            var handles = new List<PathRequestHandle>();

            foreach (var (from, to) in cases)
            {
                handles.Add(navigation.SubmitPathRequest(from, to));
            }

            WaitForCompletion(handles);
            navigation.DrainPathCompletions();

            var results = new List<List<Vector3>>();

            foreach (var handle in handles)
            {
                Assert.True(handle.TryGetResult(out var waypoints, out _));
                results.Add(waypoints.ToList());
            }

            return results;
        }

        var first = RunBatch();
        var second = RunBatch();

        Assert.Equal(first.Count, second.Count);

        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], second[i]);
        }
    }

    [Fact, Trait("Category", "Roadmap")]
    public void FrameBoundAccountsDeadlineMisses()
    {
        // Bound zero: any completion that waited at least one frame while queued counts as a
        // miss. Enough requests are submitted that the first drain lands while most are still
        // unresolved.
        var (zeroBound, _, start, end) = CreateNavigation();
        zeroBound.AsyncPathFrameBound = 0;

        var handles = new List<PathRequestHandle>();

        for (var i = 0; i < 100; i++)
        {
            handles.Add(zeroBound.SubmitPathRequest(start, end));
        }

        zeroBound.DrainPathCompletions();
        WaitForCompletion(handles);
        zeroBound.DrainPathCompletions();

        Assert.True(zeroBound.AsyncPathDeadlineMisses >= 1);

        // Default bound: the same immediate-drain pattern resolves well within 30 frames, so
        // no misses are recorded.
        var (defaultBound, _, dStart, dEnd) = CreateNavigation();
        var defaultHandles = new List<PathRequestHandle>();

        for (var i = 0; i < 100; i++)
        {
            defaultHandles.Add(defaultBound.SubmitPathRequest(dStart, dEnd));
        }

        defaultBound.DrainPathCompletions();
        WaitForCompletion(defaultHandles);
        defaultBound.DrainPathCompletions();

        Assert.Equal(0, defaultBound.AsyncPathDeadlineMisses);
    }

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

            Thread.Sleep(10);
        }

        throw new TimeoutException("Async path requests did not finish within 30 seconds.");
    }

    private static void WaitForTerminalState(params PathRequestHandle[] handles)
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

            Thread.Sleep(10);
        }

        throw new TimeoutException("Async path requests did not reach a terminal state within 30 seconds.");
    }

    private static Vector3 FindImpassablePosition(NavigationClass navigation, HeightMap heightMap, Vector3 fallback)
    {
        for (var y = 1; y < heightMap.Height - 1; y++)
        {
            for (var x = 1; x < heightMap.Width - 1; x++)
            {
                var position = heightMap.GetPosition(x, y);

                if (!navigation.IsPassable(position))
                {
                    return position;
                }
            }
        }

        return fallback;
    }

    private (NavigationClass Navigation, HeightMap HeightMap, Vector3 Start, Vector3 End) CreateNavigation()
    {
        using var fileSystem = new DiskFileSystem(
            Path.Combine(System.Environment.CurrentDirectory, "Data", "Map", "Assets"));
        var mapFile = MapFile.FromFileSystemEntry(fileSystem.GetFile("BlendTileData_CliffTextures.map"));
        Assert.NotNull(mapFile.BlendTileData);
        Assert.NotNull(mapFile.HeightMapData);
        var heightMap = new HeightMap(mapFile.HeightMapData);
        var navigation = new NavigationClass(mapFile.BlendTileData, heightMap);

        var start = heightMap.GetPosition(1, 1);
        var end = heightMap.GetPosition(heightMap.Width - 2, heightMap.Height - 2);
        Assert.True(navigation.IsPassable(start));
        Assert.True(navigation.IsPassable(end));
        return (navigation, heightMap, start, end);
    }

    private GameObject NewUnit(Vector3 position, ObjectKinds kind = ObjectKinds.Infantry)
    {
        var definition = new ObjectDefinition($"AsyncPathRequest_{kind}_{_nextObjectId}");
        definition.KindOf.Set(kind, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = new GameObject(definition, Generals.GameEngine, NewPlayer())
        {
            Id = new ObjectId(_nextObjectId++),
        };
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private Panopticon.Logic.Player NewPlayer() => new(1, null, default, Generals);

    private uint _nextObjectId = 1;
}
