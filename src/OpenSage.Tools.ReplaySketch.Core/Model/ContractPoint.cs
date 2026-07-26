namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// The six contract points every archetype scenario asserts. From
/// docs/roadmap/deep-dives/p6b-unit-contract-validation.md section 2.2.
/// </summary>
public enum ContractPoint
{
    /// <summary>Ordered/produced (or built) and present in GameLogic plus owner tracking.</summary>
    Produced,

    /// <summary>Mobile archetypes move; static archetypes (structures) do not.</summary>
    MoveOrStatic,

    /// <summary>The archetype-defining action: garrison, land/takeoff, construct, heal, charge, etc.</summary>
    ArchetypeAction,

    /// <summary>Combat units damage a target; non-combat units decline the attack explicitly.</summary>
    AttackOrDecline,

    /// <summary>Every weapon slot and every command-set SpecialPowerType has an observable effect.</summary>
    WeaponModesAndAbilities,

    /// <summary>Unit is removed from GameLogic, owner counts, and selection; containers cleared.</summary>
    DieWithCleanup,
}

/// <summary>
/// Outcome of evaluating a single <see cref="ContractPoint"/>. A <see cref="Gap"/> is an explicit
/// unimplemented-handler failure (message prefixed "P6b gap:"), never a silent pass.
/// </summary>
public enum ContractPointOutcome
{
    Passed,
    Failed,
    Gap,
}
