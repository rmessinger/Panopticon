using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Mutable state for one mass-battle reachability probe: the representative unit, its ordered
/// target, and the progress flags polled each frame by <see cref="MassBattleDeploymentRunner"/>.
/// </summary>
internal sealed class ReachabilityProbe
{
    public required Surfaces Surfaces { get; init; }

    public required string RepresentativeDefinition { get; init; }

    public required float MaxSpeed { get; init; }

    public required GameObject Unit { get; init; }

    public required Vector3 Target { get; init; }

    public required int FrameBudget { get; init; }

    public required float Distance { get; init; }

    public uint StartFrame { get; set; }

    public uint FinishFrame { get; set; }

    public bool SawPath { get; set; }

    public bool Finished { get; set; }
}
