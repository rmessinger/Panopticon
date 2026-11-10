#nullable enable

using System.Text.Json;
using Panopticon.Content.Importing.Units;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Helper for parsing Authored properties, including per-field origins, from JSON elements in unit packages.</summary>
public static class UnitPackageAuthoredHelper
{
    public static Authored<Quantity> ParseQuantity(JsonElement element, ContentSource defaultSource)
    {
        return ParseQuantity(element, defaultSource, out _);
    }

    public static Authored<Quantity> ParseQuantity(
        JsonElement element,
        ContentSource defaultSource,
        out string? error)
    {
        error = null;

        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return Authored<Quantity>.Omitted;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"Expected JSON object for quantity, got {element.ValueKind}.";
            return Authored<Quantity>.Omitted;
        }

        if (!element.TryGetProperty("unit", out _) &&
            element.TryGetProperty("value", out var vProp) &&
            vProp.ValueKind == JsonValueKind.Object)
        {
            if (UnitPackageQuantityDto.TryParse(vProp, out var qInner, out var innerReason))
            {
                return WithOrigin(element, defaultSource, Authored.Of(qInner.Value));
            }

            error = innerReason;
            return Authored<Quantity>.Omitted;
        }

        if (UnitPackageQuantityDto.TryParse(element, out var q, out var reason))
        {
            return Authored.Of(q.Value);
        }

        error = reason;
        return Authored<Quantity>.Omitted;
    }

    public static Authored<string> ParseString(JsonElement element, ContentSource defaultSource)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return Authored.Of(element.GetString() ?? string.Empty);
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var vProp) && vProp.ValueKind == JsonValueKind.String)
        {
            return WithOrigin(element, defaultSource, Authored.Of(vProp.GetString() ?? string.Empty));
        }

        return Authored<string>.Omitted;
    }

    public static Authored<int> ParseInt(JsonElement element, ContentSource defaultSource)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var iVal))
        {
            return Authored.Of(iVal);
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var vProp) && vProp.TryGetInt32(out var innerVal))
        {
            return WithOrigin(element, defaultSource, Authored.Of(innerVal));
        }

        return Authored<int>.Omitted;
    }

    public static Authored<double> ParseDouble(JsonElement element, ContentSource defaultSource)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var dVal) && double.IsFinite(dVal))
        {
            return Authored.Of(dVal);
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var vProp) && vProp.TryGetDouble(out var innerVal) && double.IsFinite(innerVal))
        {
            return WithOrigin(element, defaultSource, Authored.Of(innerVal));
        }

        return Authored<double>.Omitted;
    }

    public static Authored<bool> ParseBool(JsonElement element, ContentSource defaultSource)
    {
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return Authored.Of(element.GetBoolean());
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var vProp) && vProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return WithOrigin(element, defaultSource, Authored.Of(vProp.GetBoolean()));
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
                return WithOrigin(element, defaultSource, Authored.Of(innerId.Value));
            }
        }

        if (UnitPackageListOverrideDto.TryParseContentId(element, defaultSource, defaultKind, out var id) && id is not null)
        {
            return Authored.Of(id.Value);
        }

        return Authored<ContentId>.Omitted;
    }

    private static Authored<T> WithOrigin<T>(JsonElement element, ContentSource defaultSource, Authored<T> authored)
        where T : notnull
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("origin", out var originProp))
        {
            return authored;
        }

        if (!UnitPackageListOverrideDto.TryParseContentId(originProp, defaultSource, ContentKind.Unit, out var origin) || origin is null)
        {
            return authored;
        }

        return authored.WithOrigin(origin.Value);
    }
}
