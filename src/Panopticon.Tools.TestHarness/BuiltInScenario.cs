namespace Panopticon.Tools.TestHarness;

public enum BuiltInScenario
{
    AlpineAssaultUSAvGLA,
    AlpineAssaultUSAvGLAFullBuild,
    UnitValidationGround,
    MassBattleDeployment,
    MassBattleCapacityProbe,
    MassBattle,
    MassBattleBatch,
    EngagementCapture,
    ScenarioCapture,
    VisualRegression,
    PhysicsStack,
    PbrMaterialProof,
    GltfRuntimeProof,
    Aoe3GrannyProof,
    Ee2NifProof,
    Ee2PropertyProof,
    Ee2ScaleProof,
    Ee2MorphProof,
    Ee2ParticleProof,
    Ee2LegacyKfProof,
    RenderingScaleProbe,
    TerrainZoomProof,
    TerrainTextureBake,
    RoutingChoke,
    PathfindingLoad,
    NavigationDeadlock,
}


/// <summary>WI-M5-07 batch variant: which rosters the batch's sides field.</summary>
public enum MassBattleBatchVariant
{
    /// <summary>The profile as authored (generals-led versus ee2-led).</summary>
    Battle,

    /// <summary>Side A's roster on both sides: the map-bias control.</summary>
    Mirror,

    /// <summary>Rosters exchanged between zones, separating roster strength from zone advantage.</summary>
    Swap,
}
