#nullable enable

using System.Text.Json;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>DTO and parser for unit module definitions in unit packages.</summary>
public static class UnitPackageUnitModuleDto
{
    public static (UnitModule? Module, bool HasLegacyBinding, string? Error) TryParse(
        JsonElement element,
        ContentSource defaultSource)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, false, $"Expected JSON object for module, got {element.ValueKind}.");
        }

        var tag = element.TryGetProperty("tag", out var tProp) ? tProp.GetString() : null;
        var family = element.TryGetProperty("family", out var fProp) ? fProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(tag))
        {
            return (null, false, "Module is missing required 'tag'.");
        }

        if (string.IsNullOrWhiteSpace(family))
        {
            return (null, false, "Module is missing required 'family'.");
        }

        var kinds = ModuleKinds.None;

        if (element.TryGetProperty("kinds", out var kProp))
        {
            if (!StrictEnumParser.TryParse<ModuleKinds>(kProp, out var parsedKinds, out var kindError))
            {
                return (null, false, $"Module 'kinds' invalid: {kindError}");
            }

            kinds = parsedKinds.Value;
        }

        var inheritanceMode = ModuleInheritanceMode.Default;

        if (element.TryGetProperty("inheritanceMode", out var imProp))
        {
            if (!StrictEnumParser.TryParse<ModuleInheritanceMode>(imProp, out var parsedMode, out var modeError))
            {
                return (null, false, $"Module 'inheritanceMode' invalid: {modeError}");
            }

            inheritanceMode = parsedMode.Value;
        }

        ContentId? ability = null;

        if (element.TryGetProperty("ability", out var aProp) && aProp.ValueKind != JsonValueKind.Null)
        {
            if (aProp.ValueKind == JsonValueKind.String && aProp.GetString() is { Length: > 0 } abilityName)
            {
                ability = new ContentId(defaultSource, ContentKind.Ability, abilityName);
            }
            else if (aProp.ValueKind == JsonValueKind.Object && aProp.TryGetProperty("name", out var anProp) && anProp.GetString() is { Length: > 0 } aName)
            {
                ability = new ContentId(defaultSource, ContentKind.Ability, aName);
            }
        }

        var hasLegacy = element.TryGetProperty("hasLegacyBinding", out var hlbProp) && hlbProp.GetBoolean();

        if (!hasLegacy && element.TryGetProperty("legacy", out var lProp) && lProp.ValueKind == JsonValueKind.True)
        {
            hasLegacy = true;
        }

        var exclusiveGroup = element.TryGetProperty("exclusiveGroup", out var egProp) && egProp.ValueKind == JsonValueKind.String
            ? egProp.GetString()
            : null;

        var inherited = element.TryGetProperty("inherited", out var iProp) && iProp.GetBoolean();

        var module = new UnitModule(
            tag,
            family,
            kinds,
            inheritanceMode,
            ability,
            Legacy: null,
            exclusiveGroup,
            inherited);

        return (module, hasLegacy, null);
    }
}
