using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

public sealed class ValidationError
{
    public string PlayerName { get; }
    public string ActionLabel { get; }
    public string Message { get; }

    public ValidationError(string playerName, string actionLabel, string message)
    {
        PlayerName = playerName;
        ActionLabel = actionLabel;
        Message = message;
    }

    public override string ToString() => $"[{PlayerName} / {ActionLabel}] {Message}";
}

public sealed class ValidationResult
{
    public IReadOnlyList<ValidationError> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    public ValidationResult(List<ValidationError> errors)
    {
        Errors = errors;
    }
}

public static class TerrainValidator
{
    /// <summary>
    /// Validates every action's resolved world position against the map extents.
    /// Uses a seeded <see cref="Random"/> for reproducible results during interactive validation.
    /// </summary>
    public static ValidationResult Validate(ReplayScenario scenario, MapMetadataService map)
    {
        var errors = new List<ValidationError>();
        var rng = new Random(0); // deterministic seed for validation

        for (var ownerIdx = 0; ownerIdx < scenario.Players.Count; ownerIdx++)
        {
            var player = scenario.Players[ownerIdx];

            // Determine the "enemy" slot: first slot that is not this one.
            var enemyIdx = ownerIdx == 0 ? 1 : 0;

            var ctx = BuildContext(scenario, map, ownerIdx, enemyIdx);

            foreach (var action in player.Actions)
            {
                if (action.Position == null)
                    continue; // no map position required for this action type

                Vector3 worldPos;
                try
                {
                    worldPos = action.Position.Resolve(ctx, rng);
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError(player.Name, action.Label,
                        $"Position resolution failed: {ex.Message}"));
                    continue;
                }

                if (!IsWithinExtents(worldPos, map.ExtentMin, map.ExtentMax))
                {
                    errors.Add(new ValidationError(player.Name, action.Label,
                        $"Resolved position ({worldPos.X:F0}, {worldPos.Y:F0}) is outside map extents " +
                        $"[({map.ExtentMin.X:F0},{map.ExtentMin.Y:F0}) – ({map.ExtentMax.X:F0},{map.ExtentMax.Y:F0})]."));
                }
            }
        }

        return new ValidationResult(errors);
    }

    private static PositionContext BuildContext(
        ReplayScenario scenario,
        MapMetadataService map,
        int ownerSlotIdx,
        int enemySlotIdx)
    {
        var center = map.MapCenter;

        return new PositionContext
        {
            GetLandmark = landmark => landmark switch
            {
                LandmarkType.OwnBase => map.PlayerStart(ownerSlotIdx),
                LandmarkType.EnemyBase => map.PlayerStart(enemySlotIdx),
                LandmarkType.MapCenter => center,
                _ => center, // MapNormalized — caller should not use this path
            },
            BaseRadiusWorldUnits = scenario.BaseRadiusWorldUnits,
            SampleHeight = map.SampleHeight,
            ExtentMin = map.ExtentMin,
            ExtentMax = map.ExtentMax,
        };
    }

    private static bool IsWithinExtents(Vector3 pos, Vector3 min, Vector3 max) =>
        pos.X >= min.X && pos.X <= max.X &&
        pos.Y >= min.Y && pos.Y <= max.Y;

    /// <summary>
    /// Builds a <see cref="PositionContext"/> for the given owner/enemy slot pair.
    /// Exposed for use by <see cref="ReplayExporter"/>.
    /// </summary>
    internal static PositionContext BuildContextPublic(
        ReplayScenario scenario, MapMetadataService map, int ownerIdx, int enemyIdx)
        => BuildContext(scenario, map, ownerIdx, enemyIdx);
}
