#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

public enum ModuleSupport
{
    Unsupported,
    /// <summary>Materialized from interchange fields.</summary>
    Typed,
    /// <summary>Carried by a <see cref="LegacyModuleBinding"/>; accepted only from <see cref="UnitCapabilityRegistry.LegacySource"/>.</summary>
    Legacy,
}

/// <summary>
/// Closed set of module and ability families the runtime can materialize (decision D1). Anything
/// outside it is reported as unsupported rather than passed through.
/// </summary>
public sealed class UnitCapabilityRegistry
{
    public static readonly IReadOnlyCollection<string> DefaultTypedModuleFamilies =
    [
        "ActiveBody",
        "StructureBody",
        "ImmortalBody",
        "HighlanderBody",
        "UndeadBody",
        "AIUpdateInterface",
        "JetAIUpdate",
        "PhysicsBehavior",
        "SlowDeathBehavior",
    ];

    /// <summary>
    /// Ability families with a typed materializer. Generals abilities (garrison, heal, construct,
    /// special powers) arrive as legacy module bindings instead.
    /// </summary>
    public static readonly IReadOnlyCollection<string> DefaultAbilityFamilies =
    [
        "Attack",
    ];

    private readonly HashSet<string> _typedModules;
    private readonly HashSet<string> _legacyModules;
    private readonly HashSet<string> _abilities;

    public ContentSource LegacySource { get; }

    public UnitCapabilityRegistry(
        IEnumerable<string> typedModuleFamilies,
        IEnumerable<string> legacyModuleFamilies,
        IEnumerable<string> abilityFamilies,
        ContentSource? legacySource = null)
    {
        ArgumentNullException.ThrowIfNull(typedModuleFamilies);
        ArgumentNullException.ThrowIfNull(legacyModuleFamilies);
        ArgumentNullException.ThrowIfNull(abilityFamilies);

        _typedModules = typedModuleFamilies.ToHashSet(StringComparer.Ordinal);
        _legacyModules = legacyModuleFamilies.ToHashSet(StringComparer.Ordinal);
        _abilities = abilityFamilies.ToHashSet(StringComparer.Ordinal);
        LegacySource = legacySource ?? ContentSource.Generals;
    }

    public static UnitCapabilityRegistry CreateDefault(IEnumerable<string>? legacyModuleFamilies = null)
    {
        return new UnitCapabilityRegistry(DefaultTypedModuleFamilies, legacyModuleFamilies ?? [], DefaultAbilityFamilies);
    }

    public ModuleSupport GetModuleSupport(string family)
    {
        if (_typedModules.Contains(family))
        {
            return ModuleSupport.Typed;
        }

        return _legacyModules.Contains(family) ? ModuleSupport.Legacy : ModuleSupport.Unsupported;
    }

    public bool SupportsAbility(string family) => _abilities.Contains(family);
}
