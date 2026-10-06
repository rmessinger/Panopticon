#nullable enable

using System;
using Panopticon.Data.Ini;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>Maps engine enum names (the <c>IniEnum</c> spelling, or the member name) without silently defaulting.</summary>
internal static class EngineEnumNames
{
    public static bool TryParse<T>(string name, out T value)
        where T : struct, Enum
    {
        if (IniParser.GetEnumMap<T>().TryGetValue(name.ToUpperInvariant(), out var mapped))
        {
            value = (T)mapped;
            return true;
        }

        return Enum.TryParse(name, ignoreCase: true, out value) && Enum.IsDefined(value);
    }
}
