#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>DTO and parser for quantity {value, unit} pairs in unit packages.</summary>
public static class UnitPackageQuantityDto
{
    public static bool TryParse(
        JsonElement element,
        [NotNullWhen(true)] out Quantity? result,
        [NotNullWhen(false)] out string? reason)
    {
        result = null;

        if (element.ValueKind != JsonValueKind.Object)
        {
            reason = $"Expected JSON object for quantity, got {element.ValueKind}.";
            return false;
        }

        if (!element.TryGetProperty("value", out var valueProp) || !valueProp.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            reason = "Quantity is missing a finite numeric 'value'.";
            return false;
        }

        if (!element.TryGetProperty("unit", out var unitProp))
        {
            reason = "Quantity is missing required 'unit'.";
            return false;
        }

        if (!StrictEnumParser.TryParse<QuantityUnit>(unitProp, out var unit, out var enumError))
        {
            reason = $"Quantity unit invalid: {enumError}";
            return false;
        }

        result = new Quantity(value, unit.Value);
        reason = null;
        return true;
    }
}
