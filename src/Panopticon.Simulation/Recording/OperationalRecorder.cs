using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Logic.Orders;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// Operational run bundle writer (WI-A1-21): assembles &lt;bundle&gt;/run.json + operational.bin +
/// checkpoints/NNNNNNNNN.ckpt from one run. A frame-zero checkpoint is written at attach, further
/// checkpoints at cadence checkpoint boundaries in TickComplete, and one operational stream chunk
/// per sampled (operational-boundary) frame. The manifest is written at attach and rewritten on
/// Complete, so a crashed run keeps a readable bundle.
/// </summary>
internal sealed class OperationalRecorder : IDisposable
{
    private readonly SimulationHost _host;
    private readonly WorldStateSampler _sampler;
    private readonly RecordingCadence _cadence;
    private readonly string _bundleDirectory;
    private readonly string _runId;
    private readonly OperationalStreamWriter _stream;
    private readonly IDomainStateProjection _projection;
    private readonly IReconstructableDomain _reconstructable;
    private readonly ISimulationDomainModel _domain;
    private readonly SignatureTolerances _tolerances;
    private readonly bool _recordT1;
    private readonly uint _seed;
    private readonly string _scenarioId;
    private readonly List<RunCheckpointRecord> _checkpoints = new();
    private readonly List<OperationalDispatchedOrder> _dispatched = new();
    private DomainStateFrame _previousDomainState;

    internal OperationalRecorder(
        SimulationHost host,
        IGame game,
        WorldStateSampler sampler,
        uint seed,
        string scenarioId,
        RecordingOptions options,
        RecordingCadence cadence,
        SignatureTolerances tolerances)
    {
        if (options.Mode != RecordingMode.Operational)
        {
            throw new ArgumentException("The operational recorder requires operational mode.", nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.BundleDirectory);

        _host = host;
        _sampler = sampler;
        _cadence = cadence;
        _bundleDirectory = options.BundleDirectory;
        _recordT1 = options.RecordT1;
        _seed = seed;
        _scenarioId = scenarioId;
        _projection = host.DomainModel as IDomainStateProjection;
        _reconstructable = host.DomainModel as IReconstructableDomain;
        _domain = host.DomainModel;
        _tolerances = tolerances;
        _runId = Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(Path.Combine(_bundleDirectory, "checkpoints"));
        _stream = new OperationalStreamWriter(
            new FileStream(Path.Combine(_bundleDirectory, "operational.bin"), FileMode.Create, FileAccess.Write, FileShare.Read),
            _runId,
            _domain.DomainId,
            _projection?.StateSchema.Version ?? 0,
            game);

        _sampler.FrameSampled += OnFrameSampled;
        _host.OrdersInjected += OnOrdersInjected;
        _host.TickComplete += OnTickComplete;

        WriteCheckpoint();
        WriteManifest();
    }

    /// <summary>The bundle's run id; the manifest and every checkpoint share it.</summary>
    internal string RunId => _runId;

    private void OnOrdersInjected(uint injectionFrame, uint dispatchFrame, IReadOnlyList<Order> orders)
    {
        foreach (var order in orders)
        {
            _dispatched.Add(new OperationalDispatchedOrder(injectionFrame, dispatchFrame, order));
        }
    }

    private void OnFrameSampled(WorldStateSummary summary, WorldStateDelta delta)
    {
        var frame = summary.Frame;
        var records = new List<OperationalRecord>
        {
            new OperationalTick(_host.Frame, frame, _host.CaptureHostState().MapTime.Ticks),
        };

        if (_projection is not null)
        {
            var current = _projection.CaptureState(frame);
            if (_cadence.IsCheckpointBoundary(frame))
            {
                records.Add(new OperationalDomainSnapshot(frame, current));
            }

            var domainDelta = _previousDomainState is null
                ? new DomainStateDelta { Frame = frame }
                : DomainStateDelta.Diff(frame, _previousDomainState, current);
            _previousDomainState = current;
            records.Add(new OperationalDomainDelta(frame, domainDelta));
        }
        else
        {
            records.Add(new OperationalWorldDelta(frame, delta));
        }

        if (_reconstructable is not null)
        {
            var rollups = _reconstructable.CaptureRollups(frame);
            if (rollups.Count > 0)
            {
                records.Add(new OperationalRollups(frame, rollups));
            }

            var events = _reconstructable.DrainOperationalEvents();
            if (events.Count > 0)
            {
                records.Add(new OperationalEvents(frame, events));
            }
        }

        var metrics = _domain.SampleMetrics();
        if (metrics.Series.Count > 0)
        {
            records.Add(new OperationalMetrics(
                frame,
                metrics.Series.Select(s => new OperationalMetricSample(s.Series, s.Key, s.Value)).ToList()));
        }

        if (_dispatched.Count > 0)
        {
            records.Add(new OperationalInputs(_dispatched.ToList()));
            _dispatched.Clear();
        }

        if (_recordT1)
        {
            var tree = SignatureTreeBuilder.Build(frame, summary, _domain, _tolerances);
            var payload = JsonSerializer.SerializeToUtf8Bytes(RunOutput.ToDto(tree), RunOutput.JsonOptions);
            records.Add(new OperationalT1(frame, payload));
        }

        _stream.WriteChunk(frame, records);
    }

    private void OnTickComplete(object sender, SimulationHostTickEventArgs e)
    {
        if (_cadence.IsCheckpointBoundary(e.LogicFrame.Value))
        {
            WriteCheckpoint();
        }
    }

    private void WriteCheckpoint()
    {
        var (_, mapTime, _) = _host.CaptureHostState();
        var frame = _host.LogicFrame.Value;
        var relativePath = $"checkpoints/{frame:D9}.ckpt";
        var fullPath = Path.Combine(_bundleDirectory, relativePath);
        var sections = EngineCheckpoint.CaptureSections(_host);
        using (var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            EngineCheckpoint.Write(output, new EngineCheckpointHeader(frame, mapTime.Ticks, _runId), sections);
        }

        var bytes = File.ReadAllBytes(fullPath);
        _checkpoints.Add(new RunCheckpointRecord
        {
            Frame = frame,
            Path = relativePath,
            Bytes = bytes.Length,
            XxHash3 = EngineCheckpoint.ContentHash(bytes),
        });
    }

    /// <summary>
    /// Writes the attach-time manifest so a crashed run keeps a readable bundle; Complete
    /// rewrites it from the final run output.
    /// </summary>
    private void WriteManifest()
    {
        var manifest = new RunOutput
        {
            RunId = _runId,
            Seed = _seed,
            ScenarioId = _scenarioId,
            DomainId = _domain.DomainId,
            RecordingMode = RecordingMode.Operational,
            OperationalStreamPath = "operational.bin",
            Checkpoints = _checkpoints.ToList(),
        };
        manifest.SaveToFile(Path.Combine(_bundleDirectory, "run.json"));
    }

    /// <summary>
    /// Finishes the bundle: writes the stream footer, stamps the manifest fields onto the run
    /// output, and rewrites run.json with the completed manifest.
    /// </summary>
    internal void Complete(RunOutput output)
    {
        _stream.WriteFooter();
        output.RunId = _runId;
        output.RecordingMode = RecordingMode.Operational;
        output.OperationalStreamPath = "operational.bin";
        output.Checkpoints = _checkpoints.ToList();
        output.SaveToFile(Path.Combine(_bundleDirectory, "run.json"));
    }

    public void Dispose()
    {
        _sampler.FrameSampled -= OnFrameSampled;
        _host.OrdersInjected -= OnOrdersInjected;
        _host.TickComplete -= OnTickComplete;
        _stream.Dispose();
    }
}
