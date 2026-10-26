// WI-B4-05: bounded async path requests beside the synchronous API.
// Pins that async results match the synchronous twin exactly (same waypoints, same
// endIsPassable), that completions apply in submission order, that cancelled and dead-requester
// requests never apply, that identical submissions reproduce byte-identical completions, and
// that the frame bound accounts deadline misses. Async submissions
// never increment the synchronous PathRequestCount. Since WI-N1-03 also pins that requests
// compute against submission-time passability versions and that steady-state submits allocate
// no graph.
//
// Run with: dotnet test src --filter "FullyQualifiedName~AsyncPathRequestTests"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
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

    [Fact, Trait("Category", "Roadmap")]
    public void RequestsComputeAgainstSubmissionTimePassability()
    {
        var (navigation, heightMap, start, _) = CreateNavigation();
        var target = FindInteriorOpenTile(navigation, heightMap);
        var targetPosition = heightMap.GetPosition(target.X, target.Y);

        // Twin computed while the target tile is still passable.
        var twin = navigation.CalculatePath(start, targetPosition, out var twinEndIsPassable);
        Assert.True(twinEndIsPassable);
        Assert.NotEmpty(twin);

        var versionBefore = navigation.PassabilityVersion;
        var beforeChange = navigation.SubmitPathRequest(start, targetPosition);

        BlockTile(navigation, heightMap, target.X, target.Y);
        Assert.False(navigation.IsPassable(targetPosition));
        Assert.True(navigation.PassabilityVersion > versionBefore);

        var afterChange = navigation.SubmitPathRequest(start, targetPosition);

        WaitForTerminalState(beforeChange, afterChange);
        navigation.DrainPathCompletions();

        // The pre-change request still routes onto the now-blocked tile exactly like its twin.
        Assert.True(beforeChange.TryGetResult(out var beforeWaypoints, out var beforeEndIsPassable));
        Assert.True(beforeEndIsPassable);
        Assert.Equal(twin.Count, beforeWaypoints.Count);

        for (var i = 0; i < twin.Count; i++)
        {
            Assert.Equal(twin[i], beforeWaypoints[i]);
        }

        // The post-change request sees the block: nudged end, impassable flag, no arrival.
        Assert.True(afterChange.TryGetResult(out var afterWaypoints, out var afterEndIsPassable));
        Assert.False(afterEndIsPassable);
        Assert.NotEmpty(afterWaypoints);
        Assert.NotEqual((targetPosition.X, targetPosition.Y), (afterWaypoints[^1].X, afterWaypoints[^1].Y));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SteadyStateSubmitsAllocateNoGraph()
    {
        var navigation = BuildCorpusNavigation();
        var fixture = PathEqualityCorpus.Load(CorpusFixturePath);
        var first = fixture.Cases[0];
        var start = NodeWorld(first.StartX, first.StartY);
        var end = NodeWorld(first.EndX, first.EndY);

        // Warm the shared snapshot and the worker graph.
        var warmup = navigation.SubmitPathRequest(start, end);
        WaitForTerminalState(warmup);
        navigation.DrainPathCompletions();

        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var handles = new List<PathRequestHandle>();

        for (var i = 0; i < 50; i++)
        {
            handles.Add(navigation.SubmitPathRequest(start, end));
        }

        // The old implementation re-cloned the whole node graph after every drain.
        navigation.DrainPathCompletions();
        handles.Add(navigation.SubmitPathRequest(start, end));

        WaitForCompletion(handles);
        navigation.DrainPathCompletions();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        // A 656x460 graph clone allocates well over 20 MB; request bookkeeping is a few KB.
        // The 1 MB bound leaves room for list growth while failing on any graph allocation.
        Assert.True(allocated < 1_000_000, $"steady-state submits allocated {allocated} bytes");
    }

    [Fact, Trait("Category", "Roadmap")]
    public void PendingHandle_CompletionAssignsWaypoints()
    {
        var (navigation, heightMap, start, end) = CreateNavigation();
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var unit = NewUnit(start, game: game);
        Assert.NotNull(unit.AIUpdate);

        unit.AIUpdate.RequestPathAsync(end);
        DrainUntil(navigation, () => unit.AIUpdate.TargetPoints.Count > 0);

        // Same post-processing as AppendPathToTargetPoint: start point dropped, exact target
        // landed on (the end tile is passable).
        var points = unit.AIUpdate.TargetPoints;
        Assert.True(points.Count > 1);
        Assert.NotEqual(start, points[0]);
        Assert.Equal(end, points[^1]);
        Assert.Equal(0, navigation.AsyncPathOutstanding);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void PendingHandle_SecondRequestReplacesTheFirst()
    {
        var (navigation, heightMap, start, end) = CreateNavigation();
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var unit = NewUnit(start, game: game);
        var otherEnd = heightMap.GetPosition(1, heightMap.Height - 2);

        unit.AIUpdate.RequestPathAsync(end);
        unit.AIUpdate.RequestPathAsync(otherEnd);
        DrainUntil(navigation, () => unit.AIUpdate.TargetPoints.Count > 0);

        // The first request was cancelled and never applied; only the second path stands.
        var points = unit.AIUpdate.TargetPoints;
        Assert.True(points.Count > 1);
        Assert.Equal(otherEnd, points[^1]);
        Assert.NotEqual(end, points[^1]);
        Assert.Equal(0, navigation.AsyncPathOutstanding);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void PendingHandle_ResetCancelsWithoutApplying()
    {
        var (navigation, _, start, end) = CreateNavigation();
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var unit = NewUnit(start, game: game);

        unit.AIUpdate.RequestPathAsync(end);
        unit.AIUpdate.ResetMovementIntent();
        DrainUntil(navigation, () => navigation.AsyncPathOutstanding == 0);

        // The cancelled request never applies waypoints.
        Assert.Empty(unit.AIUpdate.TargetPoints);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void PendingHandle_DestroyedRequesterIsCancelledByDrain()
    {
        var (navigation, _, start, end) = CreateNavigation();
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var unit = NewUnit(start, game: game);

        unit.AIUpdate.RequestPathAsync(end);
        unit.SetObjectStatus(ObjectStatus.Destroyed, true);
        DrainUntil(navigation, () => navigation.AsyncPathOutstanding == 0);

        Assert.Empty(unit.AIUpdate.TargetPoints);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SetTargetPoint_AsyncOrderPathsSubmitsOrderEligibleCalls()
    {
        var (navigation, _, start, end) = CreateNavigation();
        navigation.Options.AsyncOrderPaths = true;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var unit = NewUnit(start, game: game);

        var before = NavigationClass.PathRequestCount;
        unit.AIUpdate.SetTargetPoint(end, asyncEligible: true);

        // WI-N4-02: the order path submits; no synchronous search runs and the unit holds.
        Assert.Equal(before, NavigationClass.PathRequestCount);
        Assert.Equal(1, navigation.AsyncPathSubmitted);
        Assert.Empty(unit.AIUpdate.TargetPoints);

        DrainUntil(navigation, () => unit.AIUpdate.TargetPoints.Count > 0);

        var points = unit.AIUpdate.TargetPoints;
        Assert.True(points.Count > 1);
        Assert.Equal(end, points[^1]);
        Assert.Equal(0, navigation.AsyncPathOutstanding);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SetTargetPoint_AsyncOrderPathsKeepsScriptCallsSynchronous()
    {
        var (navigation, _, start, end) = CreateNavigation();
        navigation.Options.AsyncOrderPaths = true;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var unit = NewUnit(start, game: game);

        var before = NavigationClass.PathRequestCount;
        unit.AIUpdate.SetTargetPoint(end);

        // Default eligibility is false (scripts, workflow callers): the path stays synchronous.
        Assert.Equal(before + 1, NavigationClass.PathRequestCount);
        Assert.Equal(0, navigation.AsyncPathSubmitted);
        Assert.True(unit.AIUpdate.TargetPoints.Count > 0);
        Assert.Equal(end, unit.AIUpdate.TargetPoints[^1]);
    }

    /// <summary>Drains in a loop until <paramref name="done"/> holds; the queue applies
    /// completions only inside <see cref="NavigationClass.DrainPathCompletions"/>.</summary>
    private static void DrainUntil(NavigationClass navigation, Func<bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!done())
        {
            navigation.DrainPathCompletions();
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"Async path request did not settle within 30 seconds (outstanding={navigation.AsyncPathOutstanding}).");
            }

            Thread.Sleep(10);
        }
    }

    private static (int X, int Y) FindInteriorOpenTile(NavigationClass navigation, HeightMap heightMap)
    {
        // The fixture map's interior is small and pocked with impassable cells, so a full
        // 3x3 open block does not exist; a passable center with passable cardinal neighbours
        // is enough for the block-mark and the deterministic nudge.
        (int, int)[] cardinals = [(0, -1), (0, 1), (-1, 0), (1, 0)];

        for (var y = 4; y <= heightMap.Height - 3; y++)
        {
            for (var x = 4; x <= heightMap.Width - 3; x++)
            {
                if (!navigation.IsPassable(heightMap.GetPosition(x, y)))
                {
                    continue;
                }

                var open = true;

                foreach (var (dx, dy) in cardinals)
                {
                    open &= navigation.IsPassable(heightMap.GetPosition(x + dx, y + dy));
                }

                if (open)
                {
                    return (x, y);
                }
            }
        }

        Assert.Fail("No interior tile with passable cardinal neighbours on the fixture map.");
        return default;
    }

    private static void BlockTile(NavigationClass navigation, HeightMap heightMap, int x, int y)
    {
        // Same collider geometry as NavigationRegionsTests: the marking loop's top-right
        // bound is exclusive, so the rect reaches one tile past the last intended center.
        var corner = heightMap.GetPosition(x - 1, y - 1);
        const float tile = 10f;
        var width = 2 * tile + 1.01f * tile;
        var collider = new BoxCollider(new RectangleF(corner.X, corner.Y, width, width), 1000f);
        navigation.UpdateAreaPassability(collider, false);
    }

    private static string CorpusFixturePath =>
        Path.Combine(System.Environment.CurrentDirectory, "Navigation", "PathEqualityCorpus.json");

    internal static NavigationClass BuildCorpusNavigation()
    {
        var fixture = PathEqualityCorpus.Load(CorpusFixturePath);
        var tileData = BlendTileData.Create((uint)fixture.Width, (uint)fixture.Height);
        var bits = Convert.FromBase64String(fixture.PassabilityBits);

        for (var y = 0; y < fixture.Height; y++)
        {
            for (var x = 0; x < fixture.Width; x++)
            {
                var index = y * fixture.Width + x;
                tileData.Impassability[x, y] = (bits[index / 8] & (1 << (index % 8))) != 0;
            }
        }

        var heightMap = new HeightMap(HeightMapData.Create(0, new ushort[fixture.Width, fixture.Height]));
        return new NavigationClass(tileData, heightMap);
    }

    internal static Vector3 NodeWorld(int x, int y) => new(x * 10f, y * 10f, 0f);

    private static void WaitForCompletion(IReadOnlyList<PathRequestHandle> handles)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
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
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
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

    private GameObject NewUnit(Vector3 position, ObjectKinds kind = ObjectKinds.Infantry, TestGame game = null)
    {
        var definition = new ObjectDefinition($"AsyncPathRequest_{kind}_{_nextObjectId}");
        definition.KindOf.Set(kind, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default, inherited: false));
        definition.Behaviors.Add("ModuleTag_AI",
            new ModuleDataContainer("ModuleTag_AI",
                new AIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = new GameObject(definition, game?.GameEngine ?? Generals.GameEngine, NewPlayer())
        {
            Id = new ObjectId(_nextObjectId++),
        };
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private Panopticon.Logic.Player NewPlayer() => new(1, null, default, Generals);

    private uint _nextObjectId = 1;
}
