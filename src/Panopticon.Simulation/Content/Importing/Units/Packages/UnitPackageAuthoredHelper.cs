#nullable enable

using System.Text.Json;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Helper for parsing Authored properties from JSON elements in unit packages.</summary>
public static class UnitPackageAuthoredHelper
{
    public static Authored<Quantity> ParseQuantity(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return Authored<Quantity>.Omitted;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("value", out var vProp) && vProp.ValueKind == JsonValueKind.Object)
            {
                if (UnitPackageQuantityDto.TryParse(vProp, out var qInner, out _))
                {
                    return Authored.Of(qInner.Value);
                }
            }

            if (UnitPackageQuantityDto.TryParse(element, out var q, out _))
            {
                return Authored.Of(q.Value);
            }
        }

        return Authored<Quantity>.Omitted;
    }

    public static Authored<string> ParseString(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Authored.Of(element.GetString() ?? string.Empty);
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var vProp) && vProp.ValueKind == JsonValueKind.String)
        {
            return Authored.Of(vProp.GetString() ?? string.Empty);
        }

        return Authored<string>.Omitted;
    }

    public static Authored<int> ParseInt(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var iVal))
        {
            return Authored.Of(iVal);
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var vProp) && vProp.TryGetInt32(out var innerVal))
        {
            return Authored.Of(innerVal);
        }

        return Authored<int>.Omitted;
    }

    public static Authored<double> ParseDouble(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var dVal) && double.IsFinite(dVal))
        {
            return Authored.Of(dVal);
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var vProp) && vProp.TryGetDouble(out var innerVal) && double.IsFinite(innerVal))
        {
            return Authored.Of(innerVal);
        }

        return Authored<double>.Omitted;
    }

    public static Authored<bool> ParseBool(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return Authored.Of(element.GetBoolean());
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var vProp) && vProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return Authored.Of(vProp.GetBoolean());
        }

        return Authored<bool>.Omitted;
    }

    public static Authored<ContentId> ParseContentId(JsonElement element, ContentSource defaultSource, ContentKind defaultKind)
    {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return Authored<ContentId>.Omitted;
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var vProp) && vProp.ValueKind is JsonValueKind.String or JsonValueKind.Object)
        {
            if (UnitPackageListOverrideDto.TryParseContentId(vProp, defaultSource, defaultKind, out var innerId) && innerId is not null)
            {
                return Authored.Of(innerId.Value);
            }
        }

        if (UnitPackageListOverrideDto.TryParseContentId(element, defaultSource, defaultKind, out var id) && id is not null)
        {
            return Authored.Of(id.Value);
        }

        return Authored<ContentId>.Omitted;
    }
}
