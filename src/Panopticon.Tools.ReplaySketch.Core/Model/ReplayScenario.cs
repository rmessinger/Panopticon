using System.Collections.Generic;

namespace Panopticon.Tools.ReplaySketch.Model;

public sealed class ReplayScenario
{
    /// <summary>Current declarative schema version (WI-A5-03); loaders reject any other.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Declarative schema version of this scenario file (WI-A5-03); defaults for code authors.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Map file path as stored in the replay metadata,
    /// e.g. <c>"maps/alpine assault/alpine assault.map"</c>.
    /// </summary>
    public string MapPath { get; set; } = string.Empty;

    /// <summary>
    /// World-unit radius used as "1 base width" for landmark-relative positions.
    /// Roughly the footprint radius of a Command Center (~120 world units).
    /// </summary>
    public float BaseRadiusWorldUnits { get; set; } = 120f;

    /// <summary>
    /// The A4 domain adapter the run uses (WI-A5-03): <c>"rts"</c> (default) or another
    /// registered domain id. Semantic validation of the id against the map is WI-A5-10.
    /// </summary>
    public string DomainId { get; set; } = "rts";

    /// <summary>
    /// Optional run seed (WI-A5-03). Overrides the replay-embedded seed for order-dispatch RNG
    /// and run-output identity when present; null keeps the existing replay-derived seed path
    /// unchanged.
    /// </summary>
    public uint? Seed { get; set; }

    /// <summary>
    /// Initial population: explicit placements resolved before any player action (WI-A5-03).
    /// Schema-complete; population consumers land with WI-A5-14/23.
    /// </summary>
    public List<ScenarioPlacement> Placements { get; set; } = new();

    /// <summary>WI-A5-33: authored topology for graph domains; null means the domain default.</summary>
    public ScenarioGraph? Graph { get; set; }

    /// <summary>
    /// Procedural population generators (WI-A5-23), expanded deterministically by
    /// <see cref="Services.ScenarioGeneratorService"/> before execution.
    /// </summary>
    public List<PopulationGenerator> PopulationGenerators { get; set; } = new();

    /// <summary>
    /// Procedural command timelines (WI-A5-23), expanded deterministically by
    /// <see cref="Services.ScenarioGeneratorService"/> before execution.
    /// </summary>
    public List<CommandTimelineGenerator> CommandGenerators { get; set; } = new();

    /// <summary>
    /// Free-form experiment parameters (WI-A5-03): domain and harness knobs as string
    /// key/value pairs, recorded with the run. The visual editor is WI-A5-32.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>
    /// End criteria (WI-A5-03). Frame caps are consumed by the harness; victory ends naturally
    /// when the engine decides an outcome. Steady-state detection is not yet expressible.
    /// </summary>
    public List<StopCondition> StopConditions { get; set; } = new();

    /// <summary>
    /// Domain metric series to record (WI-A5-03); empty records every series the domain emits.
    /// </summary>
    public List<string> RecordedMetricSeries { get; set; } = new();

    /// <summary>Recording options (WI-A5-03); null keeps the default full-mode T0 capture.</summary>
    public ScenarioRecordingOptions? Recording { get; set; }

    public List<PlayerSlotConfig> Players { get; set; } = new();

    public List<ScenarioAssertion> Assertions { get; set; } = new();
}
