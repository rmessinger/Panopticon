namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Card WI-M3-01: the authored reference-roster conversion constants, mirrored in
/// <c>ee2-reference-validation.json</c> and <c>capture-ee2-reference-roster.json</c> with the
/// derivation comments; <c>Ee2ModelScaleTests</c> asserts the two stay identical. Derivation:
/// EE2 NIF models are authored in decimeters (epoch-15 infantry standing mesh 20.08 units,
/// late-era light-tank hull 100.94), one EE2 tile spans 50 model units (infantry SizeX 0.125
/// tile = 6.25 units against a 5.50 mesh width; tank SizeX 0.7 tile = 35 against 39.30;
/// artillery SizeX 1 tile = 50 against 37.57), and Generals scale is 3.6501 game units per
/// meter (G10 Crusader hull anchor). One tile is 5 m and one model unit is 0.1 m.
/// </summary>
public static class Ee2ReferenceScale
{
    /// <summary>Game units per EE2 tile: 50 model units x 0.1 m x 3.6501 units/m.</summary>
    public const double GameUnitsPerSourceDistance = 18.2505;

    /// <summary>Game units per EE2 model unit: 0.1 m x 3.6501 units/m.</summary>
    public const double GameUnitsPerSourceModelUnit = 0.36501;

    /// <summary>Model units per tile; the two constants must satisfy this invariant.</summary>
    public const double SourceModelUnitsPerSourceDistance = 50;
}
