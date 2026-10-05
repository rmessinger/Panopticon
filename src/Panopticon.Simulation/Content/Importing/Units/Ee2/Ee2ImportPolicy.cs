#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units.Ee2;

/// <summary>
/// Caller decisions for an EE2 import, all explicit and recorded: which epoch row supplies stats,
/// which regional model variant to bind, which unsupported gameplay mechanics the caller accepts
/// leaving out, and the engine damage type EE2 attacks deal. Anything unsupported and not excluded
/// here blocks publication.
/// </summary>
/// <param name="DamageType">
/// Engine damage type for EE2 attacks. The default, <c>UNRESISTABLE</c>, applies authored EE2
/// damage one-to-one, so Generals armor never silently rescales it; EE2's own class modifiers are
/// a separate, excluded mechanic.
/// </param>
public sealed record Ee2ImportPolicy(int Epoch, string Region, IReadOnlyCollection<string> ExcludedMechanics, string DamageType = "UNRESISTABLE")
{
    public bool Excludes(string mechanic) => ExcludedMechanics.Contains(mechanic, StringComparer.OrdinalIgnoreCase);
}
