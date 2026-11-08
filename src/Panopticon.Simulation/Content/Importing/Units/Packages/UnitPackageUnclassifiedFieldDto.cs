#nullable enable

using System.Collections.Generic;
using System.Text.Json;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>DTO and parser for valued unclassified fields in unit packages.</summary>
public static class UnitPackageUnclassifiedFieldDto
{
    public static UnclassifiedField Parse(JsonElement element, ContentSource defaultSource)
    {
        var name = element.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty;
        var gameplay = element.TryGetProperty("affectsGameplay", out var gProp) && gProp.GetBoolean();
        var excluded = element.TryGetProperty("excludedByPolicy", out var eProp) && eProp.GetBoolean();
        var rawValue = element.TryGetProperty("rawValue", out var vProp) && vProp.ValueKind == JsonValueKind.String ? vProp.GetString() : null;

        List<string>? rawBlock = null;

        if (element.TryGetProperty("rawBlock", out var bProp) && bProp.ValueKind == JsonValueKind.Array)
        {
            rawBlock = new List<string>();
            foreach (var item in bProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } line)
                {
                    rawBlock.Add(line);
                }
            }
        }

        Provenance? provenance = null;

        if (element.TryGetProperty("provenance", out var pProp) && pProp.ValueKind == JsonValueKind.Object)
        {
            var srcId = pProp.TryGetProperty("sourceId", out var sidProp) ? sidProp.GetString() : defaultSource.Id;
            var fPath = pProp.TryGetProperty("filePath", out var fpProp) ? fpProp.GetString() : string.Empty;
            var line = pProp.TryGetProperty("line", out var lProp) && lProp.TryGetInt32(out var ln) ? ln : 0;
            var layer = pProp.TryGetProperty("archiveLayer", out var alProp) ? alProp.GetString() : null;
            provenance = new Provenance(srcId ?? defaultSource.Id, fPath ?? string.Empty, line, layer);
        }

        return new UnclassifiedField(
            name,
            gameplay,
            provenance,
            excluded,
            rawValue,
            rawBlock);
    }
}
