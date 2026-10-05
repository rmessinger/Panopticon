namespace Panopticon.Content.Importing.Units;

public enum DependencyKind
{
    Parent,
    Weapon,
    Armor,
    Locomotor,
    Projectile,
    Ability,
    Upgrade,
    Production,
    Prerequisite,
    Visual,
    /// <summary>Sound and FX references; optional, so a missing target only warns.</summary>
    Presentation,
}
