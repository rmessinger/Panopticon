#nullable enable

using System.Text.Json;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>DTO and parser for ordered module edits in unit packages.</summary>
public static class UnitPackageModuleEditDto
{
    public static (ModuleEdit? Edit, bool HasLegacyBinding, string? Error) TryParse(
        JsonElement element,
        ContentSource defaultSource)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, false, $"Expected JSON object for module edit, got {element.ValueKind}.");
        }

        var operation = ModuleEditOperation.Add;

        if (element.TryGetProperty("operation", out var opProp))
        {
            if (!StrictEnumParser.TryParse<ModuleEditOperation>(opProp, out var parsedOp, out var opError))
            {
                return (null, false, $"Module edit 'operation' invalid: {opError}");
            }

            operation = parsedOp.Value;
        }

        var tag = element.TryGetProperty("tag", out var tProp) ? tProp.GetString() : null;

        UnitModule? module = null;
        var hasLegacy = false;

        if (element.TryGetProperty("module", out var mProp) && mProp.ValueKind == JsonValueKind.Object)
        {
            var (parsedModule, moduleHasLegacy, moduleError) = UnitPackageUnitModuleDto.TryParse(mProp, defaultSource);

            if (moduleError is not null)
            {
                return (null, false, moduleError);
            }

            module = parsedModule;
            hasLegacy = moduleHasLegacy;
            tag ??= module?.Tag;
        }
        else if (operation != ModuleEditOperation.Remove)
        {
            // If the element itself describes the module directly (shorthand Add edit)
            var (parsedModule, moduleHasLegacy, moduleError) = UnitPackageUnitModuleDto.TryParse(element, defaultSource);

            if (moduleError is not null)
            {
                return (null, false, moduleError);
            }

            module = parsedModule;
            hasLegacy = moduleHasLegacy;
            tag ??= module?.Tag;
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            return (null, false, "Module edit is missing required 'tag'.");
        }

        var edit = new ModuleEdit(operation, tag, module);
        return (edit, hasLegacy, null);
    }
}
