using System;
using System.Collections.Generic;

namespace Panopticon.Geospatial.Pipeline.GenerationPlanning;

/// <summary>
/// Canonical metadata paths without changing manifest spellings.
/// </summary>
internal static class ImportGenerationPlanPaths
{
    /// <summary>
    /// Drops empty and dot segments; source loading already rejects parent traversal.
    /// </summary>
    public static string CanonicalizeProvenancePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var segments = new List<string>();

        foreach (var segment in relativePath.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            segments.Add(segment);
        }

        return string.Join("/", segments);
    }
}
