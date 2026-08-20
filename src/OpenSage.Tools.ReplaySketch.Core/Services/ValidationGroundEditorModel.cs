using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;
using static OpenSage.Tools.ReplaySketch.Profiles.ValidationGroundMapRenderer;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Engine-free state and logic for the validation-ground map editor tab. Holds the scenario being
/// edited (layout knobs + placement overrides), the loaded map, and the derived spawn plan, and
/// exposes the operations the MAUI view binds to: regenerate from knobs, hit-test a canvas point
/// against the placement markers, move a placement (which writes a <see cref="PlacementOverride"/>
/// and rebuilds the plan), and save/load. Raises <see cref="PropertyChanged"/> so the view redraws.
/// All behaviour lives here so it is covered by headless tests; the MAUI view stays a thin shell.
/// </summary>
public sealed class ValidationGroundEditorModel : INotifyPropertyChanged
{
    public ValidationGroundScenario Scenario { get; private set; } = new();
    public MapMetadataService? Map { get; private set; }
    public SpawnPlan? Plan { get; private set; }
    public EditorTool Tool { get; set; } = EditorTool.Move;

    public (int FactionIndex, int RosterIndex, bool IsTarget)? Selected { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Rebuilds the spawn plan from the current scenario (knobs + overrides).</summary>
    public void Regenerate()
    {
        // The attached map (when present) feeds terrain-aware cluster placement; without one the
        // builder falls back to layout-only placement.
        Plan = ValidationGroundBuilder.Build(Scenario, Map);
        PropertyChanged?.Invoke(this, PlanChangedArgs);
    }

    /// <summary>Replaces the scenario and rebuilds. Used by Load and the default-document setup.</summary>
    public void LoadScenario(ValidationGroundScenario scenario)
    {
        Scenario = scenario;
        Regenerate();
    }

    /// <summary>Loads a scenario from a JSON file and rebuilds.</summary>
    public void LoadScenarioFile(string path) => LoadScenario(ValidationGroundScenarioStore.Load(path));

    /// <summary>Loads a scenario from a JSON string and rebuilds (used by the MAUI file picker).</summary>
    public void LoadScenarioJson(string json) => LoadScenario(ValidationGroundScenarioStore.FromJson(json));

    /// <summary>Saves the scenario (layout + overrides) to JSON.</summary>
    public void SaveScenario(string path) => ValidationGroundScenarioStore.Save(Scenario, path);

    /// <summary>Serializes the scenario to a JSON string (used by the MAUI file picker).</summary>
    public string GetScenarioJson() => ValidationGroundScenarioStore.ToJson(Scenario);

    /// <summary>
    /// Attaches a loaded map and regenerates the plan: cluster placement is terrain-aware, so the
    /// plan built before a map existed (uniform terrain term) is stale the moment terrain becomes
    /// available. The MAUI/Avalonia side resolves the install and builds the service.
    /// </summary>
    public void AttachMap(MapMetadataService map)
    {
        Map = map;
        Regenerate();
        PropertyChanged?.Invoke(this, MapChangedArgs);
    }

    /// <summary>
    /// Hit-tests a canvas-space point (pre-view-transform, in the offset/previewSize space the
    /// renderer draws into) against the placement markers, returning the nearest within
    /// <paramref name="radius"/> pixels. Units are preferred over targets when tied, since they are
    /// the larger, on-top marker.
    /// </summary>
    public PlacementMarker? HitTest(float canvasX, float canvasY, float offsetX, float offsetY, float previewSize, float radius = 12f)
    {
        if (Map == null || Plan == null)
        {
            return null;
        }

        var markers = ComputeMarkers(Map, Plan, offsetX, offsetY, previewSize);
        var best = (PlacementMarker?)null;
        var bestDist = radius * radius;
        foreach (var m in markers)
        {
            var dx = m.ScreenX - canvasX;
            var dy = m.ScreenY - canvasY;
            var dist = dx * dx + dy * dy;
            // Strictly closer wins; on a tie, a unit wins over a target (drawn on top, easier to grab).
            if (dist < bestDist || (Math.Abs(dist - bestDist) < float.Epsilon && best.HasValue && !m.IsTarget))
            {
                bestDist = dist;
                best = m;
            }
        }
        return best;
    }

    /// <summary>
    /// Moves a placement to a planar world position by writing (or replacing) its
    /// <see cref="PlacementOverride"/>, then regenerating. This is what a drag calls each move.
    /// </summary>
    public void MovePlacement(int factionIndex, int rosterIndex, bool isTarget, float worldX, float worldY)
    {
        var existing = Scenario.PlacementOverrides.FirstOrDefault(o =>
            o.FactionIndex == factionIndex && o.RosterIndex == rosterIndex && o.IsTarget == isTarget);
        if (existing != null)
        {
            existing.X = worldX;
            existing.Y = worldY;
        }
        else
        {
            Scenario.PlacementOverrides.Add(new PlacementOverride
            {
                FactionIndex = factionIndex,
                RosterIndex = rosterIndex,
                IsTarget = isTarget,
                X = worldX,
                Y = worldY,
            });
        }
        Regenerate();
    }

    /// <summary>
    /// Inverse of <see cref="ValidationGroundMapRenderer.Project"/>: converts a canvas-space point
    /// back to a planar world position. Used to turn a drag's canvas coordinate into the world
    /// coordinate stored in an override.
    /// </summary>
    public static Vector2 Unproject(Vector3 extentMin, Vector3 extentMax, float canvasX, float canvasY, float offsetX, float offsetY, float previewSize)
    {
        var nx = (canvasX - offsetX) / previewSize;
        var ny = 1f - (canvasY - offsetY) / previewSize;
        return new Vector2(
            extentMin.X + nx * (extentMax.X - extentMin.X),
            extentMin.Y + ny * (extentMax.Y - extentMin.Y));
    }

    private static readonly PropertyChangedEventArgs PlanChangedArgs = new(nameof(Plan));
    private static readonly PropertyChangedEventArgs MapChangedArgs = new(nameof(Map));
}

public enum EditorTool
{
    Pan,
    Move,
}
