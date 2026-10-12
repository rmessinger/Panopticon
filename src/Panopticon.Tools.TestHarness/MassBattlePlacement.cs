using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Panopticon.Data;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

// The Navigation class shares its name with its namespace; alias it to keep call sites readable.
using NavigationGrid = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Placement helpers shared by the mass-battle scenarios (WI-M0-03 deployment proof, WI-M1-01
/// capacity probe): ground positions, spawns, airfield placement with anchor validation, the
/// zone layout walk for ground blocks. Placement data comes from the
/// profile; the map itself is never edited.
/// </summary>
internal static class MassBattlePlacement
{
    /// <summary>Result of one side's ground spawn: the units plus the proof-report counters.</summary>
    public sealed record GroundSpawnResult(
        List<GameObject> Units,
        int Spawned,
        int Impassable,
        int ImpassableSlotsSkipped,
        float LongestWeaponRange,
        List<string> FailureReasons);

    public static Vector3 GroundPosition(Game game, float x, float y)
    {
        var height = game.Scene3D.Terrain.GetGroundHeight(new Vector2(x, y)) ?? 0f;
        return new Vector3(x, y, height);
    }

    public static float YawToward(Vector3 from, Vector3 to) =>
        MathF.Atan2(to.Y - from.Y, to.X - from.X);

    public static GameObject SpawnAt(Game game, ObjectDefinition definition, Player owner, float x, float y, float yawRadians)
    {
        var position = GroundPosition(game, x, y);
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yawRadians);
        gameObject.UpdateTransform(position, rotation);
        // Colliders only refresh on the next logic tick; refresh immediately so culling and any
        // pre-tick inspection see the object at its spawn point.
        gameObject.UpdateColliders();
        return gameObject;
    }

    public static ColorRgb ParseColor(string? hex, int playerIndex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            var trimmed = hex.TrimStart('#');
            if (uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)
                && trimmed.Length == 6)
            {
                return new ColorRgb(
                    (byte)((packed >> 16) & 0xFF),
                    (byte)((packed >> 8) & 0xFF),
                    (byte)(packed & 0xFF));
            }
        }

        return playerIndex == 0 ? new ColorRgb(0x40, 0x9F, 0xFF) : new ColorRgb(0xFF, 0x50, 0x40);
    }

    public static uint CurrentLogicFrame(Game game) =>
        unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));

    /// <summary>World-space bounding box over all zone rectangles and airfield anchors.</summary>
    public static (float X, float Y, float Width, float Height) PlacementBounds(MassBattleDeploymentProfile profile)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        foreach (var side in profile.Sides)
        {
            minX = MathF.Min(minX, side.Zone.X);
            minY = MathF.Min(minY, side.Zone.Y);
            maxX = MathF.Max(maxX, side.Zone.X + side.Zone.Width);
            maxY = MathF.Max(maxY, side.Zone.Y + side.Zone.Height);
            foreach (var anchor in side.AirfieldAnchors)
            {
                minX = MathF.Min(minX, anchor.X);
                minY = MathF.Min(minY, anchor.Y);
                maxX = MathF.Max(maxX, anchor.X);
                maxY = MathF.Max(maxY, anchor.Y);
            }
        }

        return (minX, minY, maxX - minX, maxY - minY);
    }

    public static float RectGap(MassBattleRect a, MassBattleRect b)
    {
        var dx = MathF.Max(0f, MathF.Max(b.X - (a.X + a.Width), a.X - (b.X + b.Width)));
        var dy = MathF.Max(0f, MathF.Max(b.Y - (a.Y + a.Height), a.Y - (b.Y + b.Height)));
        return dx == 0f && dy == 0f ? 0f : MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Spawns one side's ground roster inside its zone: blocks are laid out row-major at
    /// twice footprint spacing (floored at the profile's minimum pitch), skipping grid slots
    /// that fall on impassable cells, so every unit lands on passable ground with spacing
    /// preserved. <paramref name="expectedGround"/> is the spawn target (the M0 proof passes the
    /// full UnitsPerSide; a profile with aircraft passes the ground remainder).
    /// </summary>
    public static GroundSpawnResult SpawnGroundArmy(
        Game game,
        MassBattleDeploymentProfile profile,
        int sideIndex,
        NavigationGrid navigation,
        ITestHarnessReporter reporter,
        int expectedGround)
    {
        var side = profile.Sides[sideIndex];
        var zone = side.Zone;
        var owner = game.Scene3D.Players[2 + sideIndex];
        var yaw = MathF.PI * side.FacingDegrees / 180f;
        var reasons = new List<string>();
        var units = new List<GameObject>();
        var spawned = 0;
        var impassable = 0;
        var skippedSlots = 0;
        var longestWeaponRange = 0f;
        var cursorY = zone.Y;
        var column = 0;

        foreach (var block in profile.Capacity.Blocks)
        {
            var definition = game.AssetStore.ObjectDefinitions.GetByName(block.Definition);
            if (definition == null)
            {
                reporter.Error($"Unknown object definition '{block.Definition}' in capacity block.");
                reasons.Add($"unknown definition '{block.Definition}'");
                continue;
            }

            var pitch = MathF.Max(profile.Capacity.SpacingFactor * 2f * definition.Geometry.MajorRadius, profile.Capacity.MinPitch);
            var columns = Math.Max(1, (int)(zone.Width / pitch));

            // Start every block on a fresh row; the roster fits several times over, so the blank
            // strip is cheaper than mixing per-block pitches on one row.
            if (column != 0)
            {
                column = 0;
                cursorY += pitch;
            }

            var placed = 0;
            while (placed < block.Count)
            {
                if (cursorY + pitch > zone.Y + zone.Height)
                {
                    reasons.Add(
                        $"block '{block.Definition}' exhausted the zone: {placed.ToString(CultureInfo.InvariantCulture)} of {block.Count.ToString(CultureInfo.InvariantCulture)} placed before running out of passable ground");
                    break;
                }

                var x = zone.X + pitch / 2f + column * pitch;
                var y = cursorY + pitch / 2f;
                column++;
                if (column >= columns)
                {
                    column = 0;
                    cursorY += pitch;
                }

                if (!navigation.IsPassable(GroundPosition(game, x, y)))
                {
                    skippedSlots++;
                    continue;
                }

                var unit = SpawnAt(game, definition, owner, x, y, yaw);
                if (!navigation.IsPassable(unit.Translation))
                {
                    impassable++;
                }

                if (placed == 0)
                {
                    longestWeaponRange = MathF.Max(longestWeaponRange, unit.CurrentWeapon?.Template.AttackRange ?? 0f);
                }

                units.Add(unit);
                placed++;
                spawned++;
            }
        }

        if (spawned != expectedGround)
        {
            reasons.Add($"spawned {spawned} of {expectedGround} requested units");
        }

        if (impassable > 0)
        {
            reasons.Add($"{impassable} spawn points are on impassable grid cells");
        }

        return new GroundSpawnResult(units, spawned, impassable, skippedSlots, longestWeaponRange, reasons);
    }

    /// <summary>
    /// Places one side's airfields at their anchors. Behind = on the far side of the own zone
    /// from the opposing zone (between the zone and the map edge), never inside the deployment
    /// rectangle itself. Returns the placed airfield objects (the probe parks aircraft on them).
    /// </summary>
    public static (List<GameObject> Airfields, List<string> FailureReasons) PlaceAirfields(
        Game game,
        MassBattleDeploymentProfile profile,
        int sideIndex,
        NavigationGrid navigation,
        ITestHarnessReporter reporter)
    {
        var side = profile.Sides[sideIndex];
        var zone = side.Zone;
        var opposing = profile.Sides[1 - sideIndex].Zone;
        var owner = game.Scene3D.Players[2 + sideIndex];
        var yaw = MathF.PI * side.FacingDegrees / 180f;
        var reasons = new List<string>();
        var airfields = new List<GameObject>();

        var retreatX = zone.CenterX - opposing.CenterX;
        var retreatY = zone.CenterY - opposing.CenterY;
        foreach (var anchor in side.AirfieldAnchors)
        {
            var definition = game.AssetStore.ObjectDefinitions.GetByName(side.AirfieldDefinition);
            if (definition == null)
            {
                reporter.Error($"Unknown airfield definition '{side.AirfieldDefinition}'.");
                reasons.Add($"unknown airfield definition '{side.AirfieldDefinition}'");
                continue;
            }

            var position = GroundPosition(game, anchor.X, anchor.Y);
            if (position.Z <= 0f && game.Scene3D.Terrain.GetGroundHeight(new Vector2(anchor.X, anchor.Y)) == null)
            {
                reasons.Add($"airfield anchor {anchor.X.ToString("F0", CultureInfo.InvariantCulture)},{anchor.Y.ToString("F0", CultureInfo.InvariantCulture)} is outside the map");
                continue;
            }

            if (zone.Contains(anchor.X, anchor.Y))
            {
                reasons.Add($"airfield anchor {anchor.X.ToString("F0", CultureInfo.InvariantCulture)},{anchor.Y.ToString("F0", CultureInfo.InvariantCulture)} is inside the deployment zone");
                continue;
            }

            if ((anchor.X - zone.CenterX) * retreatX + (anchor.Y - zone.CenterY) * retreatY <= 0f)
            {
                reasons.Add($"airfield anchor {anchor.X.ToString("F0", CultureInfo.InvariantCulture)},{anchor.Y.ToString("F0", CultureInfo.InvariantCulture)} is not behind the zone");
                continue;
            }

            if (!navigation.IsPassable(position))
            {
                reasons.Add($"airfield anchor {anchor.X.ToString("F0", CultureInfo.InvariantCulture)},{anchor.Y.ToString("F0", CultureInfo.InvariantCulture)} is on impassable ground");
                continue;
            }

            airfields.Add(SpawnAt(game, definition, owner, anchor.X, anchor.Y, yaw));
        }

        if (airfields.Count != side.AirfieldAnchors.Count)
        {
            reasons.Add($"placed {airfields.Count} of {side.AirfieldAnchors.Count} airfields");
        }

        return (airfields, reasons);
    }

}
