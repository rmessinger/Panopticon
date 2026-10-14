using System.Collections.Generic;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Card WI-M3-02: the authored rps class to Generals damage type mapping, mirrored in
/// <c>ee2-reference-validation.json</c> and <c>capture-ee2-reference-roster.json</c>; the model
/// scale tests assert the three stay identical. The types are distinct engine-known damage types
/// that no native Generals armor authors (audited against the install's Armor.ini
/// <c>Armor =</c> lines), so EE2-authored damage applies one-to-one against native armor and
/// native damage applies one-to-one against imported armor, while EE2-versus-EE2 resolves
/// through the stock dbcombat matrix. The names carry no semantic claim; distinctness and
/// native silence are the requirements.
/// </summary>
public static class Ee2ReferenceRps
{
    public static readonly IReadOnlyDictionary<string, string> DamageTypes = new Dictionary<string, string>
    {
        ["Heavy_Infantry"] = "PIERCE",
        ["Light_Mounted"] = "SLASH",
        ["Heavy_Mounted"] = "SIEGE",
        ["Heavy_Artillery"] = "BIG_ROCK",
        ["Air_Superiority"] = "HERO_RANGED",
        ["Bomber"] = "URUK",
        ["Helicopter"] = "HERO",
    };
}
