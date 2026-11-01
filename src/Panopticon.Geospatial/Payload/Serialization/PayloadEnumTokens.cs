using System;
using System.Collections.Generic;
using Panopticon.Geospatial.Manifest;

namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>
/// Strict kebab-case enum tokens: named strings only, integer enums are impossible by
/// construction. Reuses the manifest kebab spelling so shared enums keep one spelling.
/// </summary>
internal static class PayloadEnumTokens<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ValuesByToken;

    static PayloadEnumTokens()
    {
        ValuesByToken = new Dictionary<string, TEnum>(StringComparer.Ordinal);

        foreach (TEnum value in Enum.GetValues<TEnum>())
        {
            ValuesByToken.Add(ManifestTokens.Kebab(value), value);
        }
    }

    public static bool TryGetValue(string token, out TEnum value)
    {
        return ValuesByToken.TryGetValue(token, out value);
    }

    public static string ToToken(TEnum value)
    {
        return ManifestTokens.Kebab(value);
    }
}
