#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// WI-A5-23: declarative population generator. Distances and centers are normalized map
/// coordinates (0..1 over the map extents), so expansion stays engine-free and deterministic.
/// Grid and formation placements never overlap (centered rows and columns at fixed spacing);
/// scattered placements have no overlap guarantee (documented policy).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GridPopulationGenerator), "grid")]
[JsonDerivedType(typeof(FormationPopulationGenerator), "formation")]
[JsonDerivedType(typeof(ScatteredPopulationGenerator), "scattered")]
public abstract record PopulationGenerator(
    int PlayerIndex,
    string DefinitionName,
    float? Health = null,
    float? MaxHealth = null);

/// <summary>Row-major centered grid; quantity is rows x columns.</summary>
public sealed record GridPopulationGenerator(
    int PlayerIndex,
    string DefinitionName,
    uint Rows,
    uint Columns,
    float OriginX,
    float OriginY,
    float SpacingX,
    float SpacingY,
    float? Health = null,
    float? MaxHealth = null) : PopulationGenerator(PlayerIndex, DefinitionName, Health, MaxHealth);

/// <summary>Line, column, or box formation at fixed spacing; quantity is authored.</summary>
public sealed record FormationPopulationGenerator(
    int PlayerIndex,
    string DefinitionName,
    FormationShape Shape,
    uint Quantity,
    float OriginX,
    float OriginY,
    float Spacing,
    float? Health = null,
    float? MaxHealth = null) : PopulationGenerator(PlayerIndex, DefinitionName, Health, MaxHealth);

public enum FormationShape
{
    Line,
    Column,
    Box,
}

/// <summary>Seeded uniform distribution inside a circle; positions come from the scenario seed.</summary>
public sealed record ScatteredPopulationGenerator(
    int PlayerIndex,
    string DefinitionName,
    uint Quantity,
    float CenterX,
    float CenterY,
    float Radius,
    float? Health = null,
    float? MaxHealth = null) : PopulationGenerator(PlayerIndex, DefinitionName, Health, MaxHealth);

/// <summary>
/// WI-A5-23: declarative command timeline. The template actions are repeated
/// <see cref="RepeatCount"/> times; repetition r starts at
/// <see cref="StartFrame"/> + r * <see cref="IntervalFrames"/> and keeps the template's internal
/// fixed-time offsets. Template timings must be <see cref="FixedTiming"/> (validated) so the
/// expansion stays deterministic; the first action of each repetition is re-timed to land exactly
/// at the repetition start under cumulative scheduling. Repetitions must not overlap the previous
/// one's actions (validated).
/// </summary>
public sealed record CommandTimelineGenerator(
    int PlayerIndex,
    List<ActionEntry> Template,
    uint RepeatCount,
    uint StartFrame,
    uint IntervalFrames);
