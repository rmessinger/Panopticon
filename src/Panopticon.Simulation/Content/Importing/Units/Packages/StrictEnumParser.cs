#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Parses enums by exact member name, rejecting numeric strings and numeric JSON tokens.
/// </summary>
public static class StrictEnumParser
{
    public static bool TryParse<T>(
        JsonElement element,
        [NotNullWhen(true)] out T? result,
        [NotNullWhen(false)] out string? reason)
        where T : struct, Enum
    {
        result = null;

        if (element.ValueKind == JsonValueKind.Number)
        {
            reason = $"Numeric JSON value '{element.GetRawText()}' is not allowed for enum {typeof(T).Name}; exact member name is required.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            reason = $"Expected string for enum {typeof(T).Name}, got {element.ValueKind}.";
            return false;
        }

        var text = element.GetString();
        return TryParse(text, out result, out reason);
    }

    public static bool TryParse<T>(
        string? text,
        [NotNullWhen(true)] out T? result,
        [NotNullWhen(false)] out string? reason)
        where T : struct, Enum
    {
        result = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            reason = $"Enum value for {typeof(T).Name} is empty.";
            return false;
        }

        if (long.TryParse(text, out _) || double.TryParse(text, out _))
        {
            reason = $"Numeric enum string '{text}' is not allowed for enum {typeof(T).Name}; exact member name is required.";
            return false;
        }

        var isFlags = typeof(T).GetCustomAttribute<FlagsAttribute>() is not null;

        if (isFlags && (text.Contains(',') || text.Contains(' ')))
        {
            var parts = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ulong combined = 0;

            foreach (var part in parts)
            {
                if (long.TryParse(part, out _))
                {
                    reason = $"Numeric flag member '{part}' is not allowed for flags enum {typeof(T).Name}.";
                    return false;
                }

                if (!Enum.TryParse<T>(part, ignoreCase: false, out var parsedPart))
                {
                    reason = $"Unknown flag member '{part}' for flags enum {typeof(T).Name}.";
                    return false;
                }

                combined |= Convert.ToUInt64(parsedPart);
            }

            result = (T)Enum.ToObject(typeof(T), combined);
            reason = null;
            return true;
        }

        if (!Enum.TryParse<T>(text, ignoreCase: false, out var parsed))
        {
            reason = $"Value '{text}' is not a valid member of enum {typeof(T).Name}.";
            return false;
        }

        result = parsed;
        reason = null;
        return true;
    }
}
