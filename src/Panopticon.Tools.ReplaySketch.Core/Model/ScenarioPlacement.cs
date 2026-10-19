namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// One explicit initial placement (WI-A5-03): a definition spawned for a scenario player at a
/// declarative position before any player action runs. The position reuses the command
/// <see cref="PositionSpec"/> vocabulary so placements stay map-relative and replayable.
/// </summary>
public sealed class ScenarioPlacement
{
    /// <summary>Object definition name to spawn, e.g. <c>"AmericaTankPaladin"</c>.</summary>
    public string DefinitionName { get; set; } = string.Empty;

    /// <summary>Index into <see cref="ReplayScenario.Players"/> (0-based slot, not engine player id).</summary>
    public int PlayerIndex { get; set; }

    public required PositionSpec Position { get; set; }

    /// <summary>Spawn count for this placement entry; default 1.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Recorded health at capture time (WI-A5-11); null for placements authored by hand. The
    /// projection is lossy: only the recorded scalar is carried, no module state.
    /// </summary>
    public float? Health { get; set; }

    /// <summary>Recorded maximum health at capture time (WI-A5-11); null when authored by hand.</summary>
    public float? MaxHealth { get; set; }
}
