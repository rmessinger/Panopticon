using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Recording;
using OpenSage.Simulation;
using Xunit;

namespace OpenSage.Tests.Simulation;

/// <summary>
/// Recording domain fake: a non-RTS ISimulationDomainModel capturing lifecycle calls and
/// emitting fixed entities/keys/metrics, used to pin the host contract (WI-A4-07) without
/// the RTS adapter.
/// </summary>
public sealed class RecordingDomainFake : ISimulationDomainModel
{
    public List<string> Calls { get; } = new();

    public string DomainId => "fake";

    public DomainFidelityPolicy Fidelity => DomainFidelityPolicy.Identity;

    public void Initialize(SimulationHost host) => Calls.Add($"initialize:{host != null}");

    public void RunStarting() => Calls.Add("run-starting");

    public void RunComplete(in SimulationHostRunResult result) => Calls.Add($"run-complete:{result.FramesCompleted}");

    public IReadOnlyList<DomainEntitySummary> CaptureEntities() => [];

    public IReadOnlyList<uint> CapturePlayerMoneyVector() => [];

    public string TopologyKey(in DomainEntitySummary entity) => "fake:0";

    public DomainTopologyDescription DescribeTopology() => new(["fake:0"], []);

    public DomainMetricsSnapshot SampleMetrics() => new();

    public uint ResolveInteractionAuthority(in DomainInteraction interaction) => interaction.InitiatorId;
}
