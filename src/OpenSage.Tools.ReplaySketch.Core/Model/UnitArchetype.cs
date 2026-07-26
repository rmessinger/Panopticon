namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// The six unit archetypes whose full contract P6b validates. One per meaningful variation
/// in Generals / Zero Hour unit behaviour. See
/// docs/roadmap/deep-dives/p6b-unit-contract-validation.md section 2.1.
/// </summary>
public enum UnitArchetype
{
    Infantry,
    Vehicle,
    Aircraft,
    Builder,
    Support,
    Superweapon,
}
