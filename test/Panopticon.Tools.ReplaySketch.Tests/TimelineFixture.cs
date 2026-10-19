using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon;
using Panopticon.Recording;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-04 shared bundle fixture: writes a real state artifact (snapshot at cadence, deltas
/// between), a manifest with money + domain metric samples, and a fixed event stream.
/// </summary>
internal static class TimelineFixture
{
    public static string WriteBundle(string bundleDir, uint lastFrame, uint snapshotInterval)
    {
        Directory.CreateDirectory(bundleDir);
        var statePath = Path.Combine(bundleDir, RunBundle.StateFileName);

        using (var writer = new WorldStateArtifactWriter(statePath, SageGame.CncGenerals))
        {
            WorldStateSummary? previous = null;
            for (var frame = 0u; frame <= lastFrame; frame++)
            {
                var current = StateAt(frame);
                if (frame % snapshotInterval == 0)
                {
                    writer.WriteSnapshot(current);
                }
                else
                {
                    writer.WriteDelta(WorldStateDelta.Diff(frame, previous!, current));
                }

                previous = current;
            }
        }

        var manifest = new RunOutput
        {
            Seed = 99,
            ScenarioId = "timeline-fixture",
            SnapshotInterval = snapshotInterval,
            SnapshotArtifactPath = statePath,
            MetricSamples = [.. Enumerable.Range(0, (int)lastFrame + 1).Select(f => SampleAt((uint)f))],
        };
        manifest.SaveToFile(Path.Combine(bundleDir, RunBundle.ManifestFileName));

        RunBundle.WriteEvents(
            Path.Combine(bundleDir, RunBundle.EventsFileName),
            [
                new RunEventRecord(0, "actionDispatched", "Construct", "USA", 0, "AmericaCommandCenter", null),
                new RunEventRecord(3, "objectCreated", null, "USA", 0, "AmericaVehicleDozer", 10),
                new RunEventRecord(7, "actionCompleted", "Construct", "USA", 0, null, null),
                new RunEventRecord(11, "objectDestroyed", null, "GLA", 1, "GLAWorker", 44),
            ]);
        return bundleDir;
    }

    public static WorldStateSummary StateAt(uint frame)
    {
        var objects = new List<WorldObjectState>
        {
            new() { ObjectId = 1, DefinitionName = "Thing1", OwnerIndex = 0, Position = new System.Numerics.Vector3(frame, 1, 0), Health = 100 - frame, MaxHealth = 100 },
            new() { ObjectId = 2, DefinitionName = frame % 5 == 0 ? "Renamed" : "Thing2", OwnerIndex = 1, Position = new System.Numerics.Vector3(frame * 2, 2, 0), Health = 90 - frame, MaxHealth = 100 },
            new() { ObjectId = 3, DefinitionName = "Thing3", OwnerIndex = 1, Position = new System.Numerics.Vector3(frame * 3, 3, 0), Health = 80 - frame, MaxHealth = 100 },
        };
        if (frame >= 7 && frame < 15)
        {
            objects.Add(new WorldObjectState { ObjectId = 10, DefinitionName = "Visitor", OwnerIndex = 0, Position = new System.Numerics.Vector3(4, 4, 0), Health = 50, MaxHealth = 50 });
        }

        if (frame >= 30)
        {
            objects.Add(new WorldObjectState { ObjectId = 4, DefinitionName = "LateJoiner", OwnerIndex = 1, Position = new System.Numerics.Vector3(5, 5, 0), Health = 60, MaxHealth = 60 });
        }

        var summary = new WorldStateSummary { Frame = frame };
        summary.Objects.AddRange(objects.OrderBy(o => o.ObjectId));
        summary.PlayerMoney.AddRange([1000 - frame, 2000 + frame]);
        return summary;
    }

    private static RunMetricSample SampleAt(uint frame) => new()
    {
        Frame = frame,
        Players = [new RunPlayerMetrics { Player = "USA", Money = 1000 - frame }, new RunPlayerMetrics { Player = "GLA", Money = 2000 + frame }],
        DomainMetrics = frame % 2 == 0
            ? [new RunDomainMetricValue { Series = "unitCount", Key = "infantry", Value = 4 }]
            : [],
    };
}
