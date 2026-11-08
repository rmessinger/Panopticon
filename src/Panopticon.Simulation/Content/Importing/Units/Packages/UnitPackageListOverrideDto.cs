#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>DTO and parser for ListOverride collections in unit packages.</summary>
public static class UnitPackageListOverrideDto
{
    public static ListOverride<string> ParseStringList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                {
                    list.Add(s);
                }
            }

            return ListOverride<string>.Replace(list);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var mode = ListOverrideMode.Inherit;

            if (element.TryGetProperty("mode", out var mProp) && StrictEnumParser.TryParse<ListOverrideMode>(mProp, out var parsedMode, out _))
            {
                mode = parsedMode.Value;
            }

            var items = new List<string>();

            if (element.TryGetProperty("items", out var iProp) && iProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in iProp.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                    {
                        items.Add(s);
                    }
                }
            }

            var removals = new List<string>();

            if (element.TryGetProperty("removals", out var rProp) && rProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rProp.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                    {
                        removals.Add(s);
                    }
                }
            }

            return mode switch
            {
                ListOverrideMode.Replace => ListOverride<string>.Replace(items),
                ListOverrideMode.Edit => ListOverride<string>.Edit(items, removals),
                _ => ListOverride<string>.Inherit,
            };
        }

        return ListOverride<string>.Inherit;
    }

    public static ListOverride<ContentId> ParseContentIdList(JsonElement element, ContentSource defaultSource, ContentKind defaultKind)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<ContentId>();
            foreach (var item in element.EnumerateArray())
            {
                if (TryParseContentId(item, defaultSource, defaultKind, out var id) && id is not null)
                {
                    list.Add(id.Value);
                }
            }

            return ListOverride<ContentId>.Replace(list);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var mode = ListOverrideMode.Inherit;

            if (element.TryGetProperty("mode", out var mProp) && StrictEnumParser.TryParse<ListOverrideMode>(mProp, out var parsedMode, out _))
            {
                mode = parsedMode.Value;
            }

            var items = new List<ContentId>();

            if (element.TryGetProperty("items", out var iProp) && iProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in iProp.EnumerateArray())
                {
                    if (TryParseContentId(item, defaultSource, defaultKind, out var id) && id is not null)
                    {
                        items.Add(id.Value);
                    }
                }
            }

            var removals = new List<ContentId>();

            if (element.TryGetProperty("removals", out var rProp) && rProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rProp.EnumerateArray())
                {
                    if (TryParseContentId(item, defaultSource, defaultKind, out var id) && id is not null)
                    {
                        removals.Add(id.Value);
                    }
                }
            }

            return mode switch
            {
                ListOverrideMode.Replace => ListOverride<ContentId>.Replace(items),
                ListOverrideMode.Edit => ListOverride<ContentId>.Edit(items, removals),
                _ => ListOverride<ContentId>.Inherit,
            };
        }

        return ListOverride<ContentId>.Inherit;
    }

    public static bool TryParseContentId(
        JsonElement element,
        ContentSource defaultSource,
        ContentKind defaultKind,
        out ContentId? result)
    {
        result = null;

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Split(':');

            if (parts.Length == 1)
            {
                result = new ContentId(defaultSource, defaultKind, parts[0]);
                return true;
            }

            if (parts.Length == 2)
            {
                if (StrictEnumParser.TryParse<ContentKind>(parts[0], out var kind, out _))
                {
                    result = new ContentId(defaultSource, kind.Value, parts[1]);
                    return true;
                }

                result = new ContentId(new ContentSource(parts[0], defaultSource.CaseSensitiveNames), defaultKind, parts[1]);
                return true;
            }

            if (parts.Length == 3)
            {
                var src = string.Equals(parts[0], defaultSource.Id, StringComparison.OrdinalIgnoreCase)
                    ? defaultSource
                    : new ContentSource(parts[0], defaultSource.CaseSensitiveNames);

                if (StrictEnumParser.TryParse<ContentKind>(parts[1], out var kind, out _))
                {
                    result = new ContentId(src, kind.Value, parts[2]);
                    return true;
                }
            }

            return false;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var name = element.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var kind = defaultKind;

            if (element.TryGetProperty("kind", out var kProp) && StrictEnumParser.TryParse<ContentKind>(kProp, out var parsedKind, out _))
            {
                kind = parsedKind.Value;
            }

            var source = defaultSource;

            if (element.TryGetProperty("source", out var sProp) && sProp.ValueKind == JsonValueKind.String && sProp.GetString() is { Length: > 0 } srcName)
            {
                if (!string.Equals(srcName, defaultSource.Id, StringComparison.OrdinalIgnoreCase))
                {
                    source = new ContentSource(srcName, defaultSource.CaseSensitiveNames);
                }
            }

            result = new ContentId(source, kind, name);
            return true;
        }

        return false;
    }
}
