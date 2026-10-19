#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Simulation;

namespace Panopticon.Recording;

/// <summary>
/// T1 hierarchical signature recording (WI-A1-09): builds the tolerance-quantized signature
/// tree per sampled frame. Per-frame trees are built only while a T2 trigger is armed, keeping
/// the always-on cost proportional to the sample interval.
/// </summary>
public sealed class TieredSignatureRecorder : IDisposable
{
    private readonly WorldStateSampler _sampler;
    private readonly ISimulationDomainModel? _domain;
    private Func<uint, SignatureTreeFrame, IReadOnlyList<string>?>? _trigger;
    private TriggeredDetailCapture? _detailCapture;

    public SignatureTolerances Tolerances { get; }
    public float CellSize { get; }
    public uint SampleInterval { get; }
    public List<SignatureTreeFrame> SampledFrames { get; } = new();

    private SignatureTreeFrame? _lastBuilt;

    /// <summary>The most recently built per-frame tree (sampled or trigger-built).</summary>
    public SignatureTreeFrame? LastBuilt => _lastBuilt;

    /// <summary>WI-A5-07: whether bucket keys come from the domain topology contract.</summary>
    internal bool UsesDomainBuckets => _domain is not null;

    /// <summary>
    /// Bucket keys come from <paramref name="domain"/>'s topology contract when given,
    /// otherwise the legacy origin-anchored spatial-cell strategy is used.
    /// </summary>
    public TieredSignatureRecorder(
        WorldStateSampler sampler,
        SignatureTolerances? tolerances = null,
        uint sampleInterval = RunOutputRecorder.DefaultSampleInterval,
        float cellSize = SignatureTreeBuilder.DefaultCellSize,
        ISimulationDomainModel? domain = null)
    {
        _sampler = sampler;
        _domain = domain;
        Tolerances = tolerances ?? SignatureTolerances.Default;
        SampleInterval = sampleInterval == 0 ? RunOutputRecorder.DefaultSampleInterval : sampleInterval;
        CellSize = cellSize;
        _sampler.FrameSampled += OnFrameSampled;
    }

    /// <summary>
    /// Arms T2 triggered detail capture: from now on every frame's tree is built and offered to
    /// <paramref name="trigger"/>; a non-null diverged-subtree list trips the armed
    /// <see cref="TriggeredDetailCapture"/> and is recorded with the window.
    /// </summary>
    public void ArmTrigger(Func<uint, SignatureTreeFrame, IReadOnlyList<string>?> trigger, TriggeredDetailCapture detailCapture)
    {
        _trigger = trigger;
        _detailCapture = detailCapture;
    }

    private void OnFrameSampled(WorldStateSummary summary, WorldStateDelta delta)
    {
        var sampleDue = summary.Frame % SampleInterval == 0;

        if (sampleDue)
        {
            var tree = BuildTree(summary.Frame, summary);
            _lastBuilt = tree;
            SampledFrames.Add(tree);
        }

        if (_trigger is not null)
        {
            // Reuse the sampled tree when this frame was just sampled.
            var tree = sampleDue
                ? _lastBuilt!
                : BuildTree(summary.Frame, summary);
            _lastBuilt = tree;
            var diverged = _trigger(summary.Frame, tree);
            if (diverged is { Count: > 0 } && _detailCapture is not null)
            {
                _detailCapture.Trip(summary.Frame, tree, diverged);
            }
        }
    }

    private SignatureTreeFrame BuildTree(uint frame, WorldStateSummary summary)
    {
        return _domain is not null
            ? SignatureTreeBuilder.Build(frame, summary, _domain, Tolerances)
            : SignatureTreeBuilder.Build(frame, summary, Tolerances, CellSize);
    }

    public void Dispose() => _sampler.FrameSampled -= OnFrameSampled;
}
