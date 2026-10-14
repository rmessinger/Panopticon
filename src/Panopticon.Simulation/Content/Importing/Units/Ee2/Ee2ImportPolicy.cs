#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units.Ee2;

/// <summary>
/// Caller decisions for an EE2 import, all explicit and recorded: which epoch row supplies stats,
/// which regional model variant to bind, which unsupported gameplay mechanics the caller accepts
/// leaving out, and how EE2 rps counter classes map onto Generals damage types (WI-M3-02).
/// Anything unsupported and not excluded here blocks publication.
/// </summary>
/// <param name="DamageType">
/// Engine damage type for EE2 attacks when <paramref name="RpsDamageTypes"/> is empty and
/// <c>rps</c> is an excluded mechanic. The default, <c>UNRESISTABLE</c>, applies authored EE2
/// damage one-to-one, so Generals armor never silently rescales it.
/// </param>
/// <param name="RpsDamageTypes">
/// Authored rps class to Generals damage type mapping (WI-M3-02). While non-empty, every
/// published unit's rps class must have an entry (unmapped classes fail load), weapons carry
/// the mapped type, and each class materializes an armor set from the stock
/// <c>db/Simulation/dbcombat.csv</c> defender row.
/// </param>
/// <param name="CorpseSeconds">
/// How long a dead unit plays its death state before removal. EE2 sinks corpses through a child
/// visual the engine does not import, so the caller declares the duration.
/// </param>
public sealed record Ee2ImportPolicy(
    int Epoch,
    string Region,
    IReadOnlyCollection<string> ExcludedMechanics,
    string DamageType = "UNRESISTABLE",
    double CorpseSeconds = 3,
    IReadOnlyDictionary<string, string>? RpsDamageTypes = null)
{
    public IReadOnlyDictionary<string, string> MappedDamageTypes { get; } =
        (RpsDamageTypes ?? new Dictionary<string, string>()).ToDictionary(pair => NormalizeRps(pair.Key), pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    public bool MapsRps => MappedDamageTypes.Count > 0;

    public bool Excludes(string mechanic) => ExcludedMechanics.Contains(mechanic, StringComparer.OrdinalIgnoreCase);

    internal static string NormalizeRps(string rps) => rps.Replace("_", string.Empty);
}
