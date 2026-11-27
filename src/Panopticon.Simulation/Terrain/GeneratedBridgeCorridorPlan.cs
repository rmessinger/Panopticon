#nullable enable

using System.Collections.Generic;

namespace Panopticon.Terrain;

/// <summary>
/// Planned lattice heights for one generated-bridge corridor, computed from original terrain.
/// </summary>
internal sealed record GeneratedBridgeCorridorPlan
{
    public required BridgeRoute Route { get; init; }

    public required double DatumZ { get; init; }

    public required double LateralMin { get; init; }

    public required double LateralMax { get; init; }

    public required double PoseLateralMin { get; init; }

    public required double PoseLateralMax { get; init; }

    public required double ToeStartS { get; init; }

    public required double ToeEndS { get; init; }

    public required GeneratedBridgeApproachProfile ApproachStart { get; init; }

    public required GeneratedBridgeApproachProfile ApproachEnd { get; init; }

    public required IReadOnlyList<GeneratedBridgeCorridorTarget> Targets { get; init; }

    public BridgeCorridor ToCorridor(double blendDistance) => new(Route, PoseLateralMin, PoseLateralMax, ToeStartS, ToeEndS, blendDistance);
}
