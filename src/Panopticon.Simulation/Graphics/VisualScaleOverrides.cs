using System;
using System.Collections.Generic;

namespace Panopticon.Graphics;

/// <summary>
/// Presentation-only visual multipliers for aircraft whose authored model scale diverges
/// from the ground-vehicle anchor. Derivations come from the WI-G10-01 reference manifest
/// (test/Panopticon.Simulation.Tests/Data/Graphics/Proportions/reference-scale-manifest.json):
/// the Crusader hull anchors 28.9443 hull units to 7.93 m, and each multiplier is the real
/// reference length divided by the model's rendered length at that anchor. Gameplay
/// transforms, selection, collision and weapon bones are unaffected; see
/// docs/roadmap/implementation/track-g-g10-realistic-proportions.md.
/// </summary>
internal static class VisualScaleOverrides
{
    private const float HullUnits = 28.9443f;
    private const float HullMeters = 7.93f;

    private static readonly IReadOnlyDictionary<string, float> Overrides =
        new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            // MiG-29 length 17.32 m vs 33.0134 fuselage units (units -> meters via the hull anchor).
            ["ChinaJetMiG"] = 17.32f * HullUnits / (33.0134f * HullMeters),
            // RAH-66 Comanche length 14.28 m vs 44.0651 fuselage units (rotor disk excluded).
            ["AmericaVehicleComanche"] = 14.28f * HullUnits / (44.0651f * HullMeters),
        };

    public static float GetScale(string? objectDefinitionName)
    {
        return objectDefinitionName != null
            && Overrides.TryGetValue(objectDefinitionName, out var scale)
            ? scale
            : 1f;
    }
}
