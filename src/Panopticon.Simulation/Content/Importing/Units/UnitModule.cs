#nullable enable

using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// One behavior, body, or draw module on a unit, identified by tag. <paramref name="Family"/> is the
/// native module family name (for example <c>ActiveBody</c>) and drives capability validation.
/// Exactly one of <paramref name="Ability"/> or <paramref name="Legacy"/> may bind behavior; a
/// module with neither is a typed family materialized from the unit's own fields.
/// </summary>
/// <param name="ExclusiveGroup">
/// Modules sharing a non-null group replace each other even when not inherited; Generals uses
/// this for AI update modules.
/// </param>
public sealed record UnitModule(
    string Tag,
    string Family,
    ModuleKinds Kinds,
    ModuleInheritanceMode InheritanceMode = ModuleInheritanceMode.Default,
    ContentId? Ability = null,
    LegacyModuleBinding? Legacy = null,
    string? ExclusiveGroup = null,
    bool Inherited = false);
