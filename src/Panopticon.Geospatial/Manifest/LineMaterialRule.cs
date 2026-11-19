using System.Collections.Generic;

namespace Panopticon.Geospatial.Manifest;

/// <summary>
/// One opt-in line-corridor material rule: line features of one source rasterize to a
/// painted corridor of material cells. Widths are source metres, resolved per feature in
/// a fixed order: explicit width attribute, lanes attribute times per-lane metres, class
/// widths by a class attribute value, then the rule default. The generator paints
/// corridors between classification samples and polygon rules.
/// </summary>
/// <param name="SourceId">Exact source id whose line features this rule paints.</param>
/// <param name="Material">Palette entry name painted into the corridor cells.</param>
/// <param name="DefaultWidthMeters">
/// Corridor width in source metres when no attribute path resolves; required to be
/// positive and finite when present.
/// </param>
/// <param name="WidthAttribute">
/// Attribute carrying an explicit width in source metres; present, finite, positive
/// values win over every other path.
/// </param>
/// <param name="LanesAttribute">Attribute carrying a lane count, for example "NB_VOIES".</param>
/// <param name="MetersPerLane">
/// Metres per lane multiplied with <paramref name="LanesAttribute"/>; required to be
/// positive and finite when present.
/// </param>
/// <param name="ClassAttribute">
/// Attribute whose value keys <paramref name="ClassWidthsMeters"/>, for example "NATURE".
/// </param>
/// <param name="ClassWidthsMeters">
/// Width in source metres per class attribute value, for example
/// {"Autoroute": 20, "Chemin": 4}; values must be positive and finite.
/// </param>
public sealed record LineMaterialRule(
    string SourceId,
    string Material,
    double? DefaultWidthMeters = null,
    string? WidthAttribute = null,
    string? LanesAttribute = null,
    double? MetersPerLane = null,
    string? ClassAttribute = null,
    IReadOnlyDictionary<string, double>? ClassWidthsMeters = null);
