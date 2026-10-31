using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Panopticon.Geospatial.Manifest;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Immutable ordered palette of material names (B4b deep dive section 5.2): entry 0 is
/// the default material, every index is a byte, and the palette holds at most
/// <see cref="MaxEntries"/> entries. Names come only from manifest rule text; imagery
/// colors are never quantized into palette entries.
/// </summary>
public sealed class MaterialPalette
{
    public const int MaxEntries = 255;

    private readonly string[] _names;

    private readonly Dictionary<string, byte> _indices;

    public MaterialPalette(IReadOnlyList<string>? names)
    {
        var count = names?.Count ?? 0;
        if (count == 0)
        {
            throw new ArgumentException("The palette needs at least the default material entry.", nameof(names));
        }

        if (count > MaxEntries)
        {
            throw new ArgumentException(
                $"The palette holds at most {MaxEntries} entries; found {count}.",
                nameof(names));
        }

        var copy = new string[count];
        _indices = new Dictionary<string, byte>(count, StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            var name = names![i];
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"The material name at index {i} is blank.", nameof(names));
            }

            var trimmed = name.Trim();
            if (_indices.ContainsKey(trimmed))
            {
                throw new ArgumentException($"The material name '{trimmed}' appears more than once.", nameof(names));
            }

            copy[i] = trimmed;
            _indices.Add(trimmed, (byte)i);
        }

        _names = copy;
        Names = new ReadOnlyCollection<string>(copy);
    }

    public IReadOnlyList<string> Names { get; }

    public int Count => _names.Length;

    public string DefaultMaterial => _names[0];

    /// <summary>Index of the default material; cells matched by no rule use it.</summary>
    public byte DefaultIndex => 0;

    /// <summary>Ordinal lookup of a material name; names are matched trimmed and case-sensitively.</summary>
    public bool TryGetIndex(string? material, out byte index)
    {
        index = 0;

        if (material is null)
        {
            return false;
        }

        return _indices.TryGetValue(material.Trim(), out index);
    }

    /// <summary>
    /// Derives the manifest palette: the default material first, then every
    /// <see cref="MaterialRule"/> material in list order, then every material-target
    /// <see cref="FeatureMappingRule"/> material in list order, each added once. Blank
    /// names and names duplicated beyond the byte-index capacity fail like the
    /// constructor.
    /// </summary>
    public static MaterialPalette FromRules(
        string defaultMaterial,
        IReadOnlyList<MaterialRule>? materialRules,
        IReadOnlyList<FeatureMappingRule>? featureMappings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultMaterial);

        var names = new List<string> { defaultMaterial.Trim() };
        var seen = new HashSet<string>(StringComparer.Ordinal) { defaultMaterial.Trim() };

        if (materialRules is not null)
        {
            for (var i = 0; i < materialRules.Count; i++)
            {
                AddDistinct(names, seen, materialRules[i].Material);
            }
        }

        if (featureMappings is not null)
        {
            for (var i = 0; i < featureMappings.Count; i++)
            {
                var rule = featureMappings[i];
                if (rule.Target == ManifestFeatureTarget.Material)
                {
                    AddDistinct(names, seen, rule.Material);
                }
            }
        }

        return new MaterialPalette(names);

        static void AddDistinct(List<string> names, HashSet<string> seen, string? material)
        {
            if (string.IsNullOrWhiteSpace(material))
            {
                // Material rules always carry a name; a material-target mapping rule must too.
                throw new ArgumentException("A material rule must carry a non-empty material name.", nameof(material));
            }

            var trimmed = material.Trim();
            if (seen.Add(trimmed))
            {
                names.Add(trimmed);
            }
        }
    }
}
