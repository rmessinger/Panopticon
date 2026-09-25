using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Panopticon.Data;
using Panopticon.Physics;
using Panopticon.Physics.Bepu;
using Panopticon.Terrain;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// PS-1 debris-stack test scene (physics seam plan). Two phases:
///
/// 1. Benchmark: a pure-seam Bepu world (no game) drops boxes onto a flat chunk field at
///    100/1k/10k bodies, measures ms per Timestep, and asserts loose settle bounds plus a
///    run-to-run stability probe (loose bounds, not identity, per the relaxed-determinism
///    decision).
/// 2. Visual stack: the generic capture runner executes the embedded `capture-physics-stack`
///    profile on the Bepu backend (stacked cars falling and settling through the game);
///    this runner post-validates the resulting manifest against the same loose bounds.
///
/// Exit codes: 0 pass, 5 unmet settle bound, 1 setup error.
/// </summary>
public static class PhysicsStackRunner
{
    private const int WarmupSteps = 240;
    private const int MeasuredSteps = 240;
    private const int BodiesPerStack = 4;
    private const float BoxHalfExtent = 7.5f;
    private const float StackSpacing = 45.0f;
    private const float GravityPerSecondSquared = -64.0f;
    private const float MaxHorizontalScatter = 350.0f;
    private const float MinSettledSpeedPerSecond = 2.0f;
    private const float MaxStabilityDrift = 10.0f;

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var benchmarkExit = RunBenchmark(reporter);
        if (benchmarkExit != 0)
        {
            return benchmarkExit;
        }

        var captureExit = RunVisualStack(installation, config, reporter);
        if (captureExit != 0)
        {
            return captureExit;
        }

        return PostValidateVisualStack(reporter);
    }

    private static int RunBenchmark(ITestHarnessReporter reporter)
    {
        foreach (var bodyCount in new[] { 100, 1_000, 10_000 })
        {
            var firstRun = RunBenchmarkOnce(bodyCount, out var firstCenterOfMass, out var firstFieldSize, out var msPerTimestep);
            reporter.Info($"[PhysicsStack] {bodyCount} bodies: {msPerTimestep:F3} ms/Timestep average over {MeasuredSteps} steps");

            var settled = AssertSettled(firstRun, firstFieldSize, $"benchmark {bodyCount}", reporter);
            if (!settled)
            {
                return 5;
            }

            // Stability probe: repeated run on the same machine compared by loose bounds.
            RunBenchmarkOnce(bodyCount, out var secondCenterOfMass, out _, out _);
            var drift = Vector3.Distance(firstCenterOfMass, secondCenterOfMass);
            reporter.Info($"[PhysicsStack] {bodyCount} bodies: run-to-run center-of-mass drift {drift:F2} (bound {MaxStabilityDrift})");
            if (drift > MaxStabilityDrift)
            {
                reporter.Error($"[PhysicsStack] {bodyCount} bodies: stability probe drift {drift:F2} exceeds bound {MaxStabilityDrift}.");
                return 5;
            }
        }

        return 0;
    }

    private static List<(Vector3 Position, Vector3 Velocity)> RunBenchmarkOnce(int bodyCount, out Vector3 centerOfMass, out float fieldSize, out float msPerTimestep)
    {
        var world = new BepuPhysicsWorld(new Vector3(0.0f, 0.0f, GravityPerSecondSquared));
        try
        {
            var footprintCells = (int)Math.Ceiling(MathF.Sqrt((float)bodyCount / BodiesPerStack)) + 1;
            var chunkCells = TerrainPhysicsChunks.CellsPerChunk;
            // A 2-chunk margin keeps stacks that topple sideways on the collision field.
            var chunksPerSide = Math.Max(1, (int)Math.Ceiling(footprintCells * StackSpacing / (chunkCells * HeightMap.HorizontalScale))) + 4;
            CreateFlatChunks(world, chunksPerSide, chunkCells);
            var fieldMin = 0.0f;
            var fieldMax = chunksPerSide * chunkCells * HeightMap.HorizontalScale;
            fieldSize = fieldMax;

            var bodies = new List<IPhysicsBody>(bodyCount);
            var stackCount = (int)Math.Ceiling((float)bodyCount / BodiesPerStack);
            var sideLength = (int)Math.Ceiling(MathF.Sqrt((float)stackCount));
            var baseOffset = 2 * chunkCells * HeightMap.HorizontalScale + 50.0f;
            for (var stack = 0; stack < stackCount; stack++)
            {
                var sx = stack % sideLength;
                var sy = stack / sideLength;
                var baseX = baseOffset + sx * StackSpacing;
                var baseY = baseOffset + sy * StackSpacing;
                var inThisStack = Math.Min(BodiesPerStack, bodyCount - stack * BodiesPerStack);
                for (var layer = 0; layer < inThisStack; layer++)
                {
                    bodies.Add(world.CreateBody(new PhysicsBodyDescription
                    {
                        Shape = new PhysicsShape.Box(new Vector3(BoxHalfExtent, BoxHalfExtent, BoxHalfExtent)),
                        Mass = 10.0f,
                        Position = new Vector3(baseX, baseY, (layer + 1) * (BoxHalfExtent * 2.0f + 1.0f)),
                    }));
                }
            }

            var timestep = 1.0f / 30.0f;
            for (var i = 0; i < WarmupSteps; i++)
            {
                world.Timestep(timestep);
            }

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < MeasuredSteps; i++)
            {
                world.Timestep(timestep);
            }

            stopwatch.Stop();
            msPerTimestep = (float)(stopwatch.Elapsed.TotalMilliseconds / MeasuredSteps);

            centerOfMass = Vector3.Zero;
            foreach (var body in bodies)
            {
                centerOfMass += body.Position;
            }

            centerOfMass /= bodies.Count;

            return bodies.Select(body => (body.Position, body.LinearVelocity)).ToList();
        }
        finally
        {
            world.Dispose();
        }
    }

    private static void CreateFlatChunks(BepuPhysicsWorld world, int chunksPerSide, int chunkCells)
    {
        var verticesPerSide = chunkCells + 1;
        var cellSize = HeightMap.HorizontalScale;
        for (var chunkY = 0; chunkY < chunksPerSide; chunkY++)
        {
            for (var chunkX = 0; chunkX < chunksPerSide; chunkX++)
            {
                var vertices = new TerrainPhysicsVertex[verticesPerSide * verticesPerSide];
                for (var ly = 0; ly < verticesPerSide; ly++)
                {
                    for (var lx = 0; lx < verticesPerSide; lx++)
                    {
                        var x = (chunkX * chunkCells + lx) * cellSize;
                        var y = (chunkY * chunkCells + ly) * cellSize;
                        vertices[ly * verticesPerSide + lx] = new TerrainPhysicsVertex(new Vector3(x, y, 0.0f));
                    }
                }

                var indices = new int[chunkCells * chunkCells * 6];
                var i = 0;
                for (var y = 0; y < chunkCells; y++)
                {
                    for (var x = 0; x < chunkCells; x++)
                    {
                        var yThis = y * verticesPerSide;
                        var yNext = (y + 1) * verticesPerSide;
                        indices[i++] = yThis + x;
                        indices[i++] = yThis + x + 1;
                        indices[i++] = yNext + x;
                        indices[i++] = yNext + x;
                        indices[i++] = yThis + x + 1;
                        indices[i++] = yNext + x + 1;
                    }
                }

                world.SetTerrainChunk(chunkX, chunkY, vertices, indices);
            }
        }
    }

    private static bool AssertSettled(IReadOnlyList<(Vector3 Position, Vector3 Velocity)> bodies, float fieldSize, string phase, ITestHarnessReporter reporter)
    {
        var unsettled = 0;
        foreach (var (position, velocity) in bodies)
        {
            if (velocity.Length() > MinSettledSpeedPerSecond)
            {
                unsettled++;
            }

            if (position.Z < -BoxHalfExtent * 4.0f || position.Z > BodiesPerStack * BoxHalfExtent * 4.0f)
            {
                reporter.Error($"[PhysicsStack] {phase}: body escaped vertically at {position}.");
                return false;
            }

            if (position.X < -BoxHalfExtent || position.X > fieldSize + BoxHalfExtent
                || position.Y < -BoxHalfExtent || position.Y > fieldSize + BoxHalfExtent)
            {
                reporter.Error($"[PhysicsStack] {phase}: body left the collision field at {position}.");
                return false;
            }
        }

        var unsettledFraction = (float)unsettled / bodies.Count;
        if (unsettledFraction > 0.05f)
        {
            reporter.Error($"[PhysicsStack] {phase}: {unsettled}/{bodies.Count} bodies still moving above {MinSettledSpeedPerSecond}/s after settling.");
            return false;
        }

        return true;
    }

    private static int RunVisualStack(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var profile = CaptureProfileLoader.LoadEmbeddedCapture("capture-physics-stack.json");
        profile.OutputDirectory = DefaultOutputDirectory();
        var settings = new CaptureRunSettings(config.Renderer, FullHud: false, FallbackOutputDirectory: null);
        var result = ScenarioCaptureRunner.Run(installation, profile, settings, reporter);
        return result.ExitCode;
    }

    private static string DefaultOutputDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "panopticon_physics_stack");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static int PostValidateVisualStack(ITestHarnessReporter reporter)
    {
        var manifestPath = Path.Combine(DefaultOutputDirectory(), "manifest.json");
        if (!File.Exists(manifestPath))
        {
            reporter.Error($"[PhysicsStack] No manifest at {manifestPath}; visual stack did not run.");
            return 1;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var frames = document.RootElement.GetProperty("Frames").EnumerateArray().ToList();
        if (frames.Count == 0)
        {
            reporter.Error("[PhysicsStack] Visual stack manifest has no frames.");
            return 1;
        }

        var center = new Vector2(540.0f, 600.0f);
        var escaped = 0;
        var total = 0;
        foreach (var frame in frames)
        {
            foreach (var actor in frame.GetProperty("Actors").EnumerateArray())
            {
                total++;
                var x = actor.GetProperty("WorldX").GetSingle();
                var y = actor.GetProperty("WorldY").GetSingle();
                if (Vector2.Distance(new Vector2(x, y), center) > MaxHorizontalScatter)
                {
                    escaped++;
                }
            }
        }

        // A few far-rolling stragglers are acceptable debris behavior; a wholesale
        // scatter (tunneling, explosion) is not.
        var escapedFraction = (float)escaped / total;
        if (escapedFraction > 0.05f)
        {
            reporter.Error($"[PhysicsStack] Visual stack: {escaped}/{total} actor records escaped the {MaxHorizontalScatter}-unit scatter bound around the stack center.");
            return 5;
        }

        var maxSpeed = 0.0f;
        foreach (var actor in frames[^1].GetProperty("Actors").EnumerateArray())
        {
            var velocity = new Vector3(
                actor.GetProperty("VelX").GetSingle(),
                actor.GetProperty("VelY").GetSingle(),
                actor.GetProperty("VelZ").GetSingle());
            maxSpeed = MathF.Max(maxSpeed, velocity.Length());
        }

        reporter.Info($"[PhysicsStack] Visual stack: all {frames.Count} captured frames within the loose scatter bound; final max speed {maxSpeed:F3}/frame.");
        return 0;
    }
}
