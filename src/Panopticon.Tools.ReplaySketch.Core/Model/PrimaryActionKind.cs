namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// The primary action kind a unit performs in the validation ground. Most of the roster attacks;
/// builders, gatherers, and support units have their own kind. <see cref="Move"/> is the universal
/// fallback every unit additionally performs. See deep dive P9 §2.4.
/// </summary>
public enum PrimaryActionKind
{
    Move,
    Attack,
    Build,
    Gather,
    SpecialPower,
}
