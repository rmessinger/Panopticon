using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class ValidationGroundEditorModelTests
{
    // Extent large enough that the default layout (cluster at 600,600) sits inside it.
    private static readonly Vector3 Min = new(0f, 0f, 0f);
    private static readonly Vector3 Max = new(2000f, 2000f, 0f);

    private static ValidationGroundEditorModel NewModelWithSingleFaction()
    {
        var model = new ValidationGroundEditorModel();
        var scenario = ValidationGroundScenarioStore.Standard();
        scenario.FactionIndices = [2];
        // Editor-mechanics tests pin row mode so marker positions are the simple row math,
        // independent of the standard scenario's layout mode.
        scenario.Layout.Mode = LayoutMode.Rows;
        model.LoadScenario(scenario);
        model.AttachMap(MapMetadataService.CreateForTesting(Min, Max));
        return model;
    }

    [Fact]
    public void Regenerate_BuildsPlanFromScenario()
    {
        var model = NewModelWithSingleFaction();

        Assert.NotNull(model.Plan);
        Assert.NotEmpty(model.Plan!.UnitPlacements);
    }

    [Fact]
    public void HitTest_AtUnitMarker_ReturnsUnit()
    {
        var model = NewModelWithSingleFaction();
        // Single-faction unit 0 is at world (240, 560) with the default layout; with extent 0..2000
        // into a 400px preview that projects to (48, 288).
        Assert.Equal(new Vector3(240f, 560f, 0f), model.Plan!.UnitPlacements[0].Position);

        var hit = model.HitTest(canvasX: 48f, canvasY: 288f, offsetX: 0, offsetY: 0, previewSize: 400f);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.IsTarget == false);
        Assert.Equal(0, hit.Value.RosterIndex);
    }

    [Fact]
    public void HitTest_OffAllMarkers_ReturnsNull()
    {
        var model = NewModelWithSingleFaction();

        var hit = model.HitTest(canvasX: 1f, canvasY: 1f, offsetX: 0, offsetY: 0, previewSize: 400f);

        Assert.Null(hit);
    }

    [Fact]
    public void MovePlacement_WritesOverrideAndRegenerates()
    {
        var model = NewModelWithSingleFaction();

        model.MovePlacement(factionIndex: 2, rosterIndex: 0, isTarget: false, worldX: 500f, worldY: 600f);

        Assert.Equal(new Vector3(500f, 600f, 0f), model.Plan!.UnitPlacements[0].Position);
        // The standard scenario carries the terrorist's close-quarters override; a single-faction
        // (USA) model cannot produce another, so the moved override is the only faction-2 one.
        Assert.Contains(model.Scenario.PlacementOverrides,
            o => o.FactionIndex == 2 && o.RosterIndex == 0 && !o.IsTarget);
    }

    [Fact]
    public void MovePlacement_TwiceForSamePlacement_UpdatesExistingOverride()
    {
        var model = NewModelWithSingleFaction();

        model.MovePlacement(2, 0, false, 100f, 100f);
        model.MovePlacement(2, 0, false, 200f, 200f);

        Assert.Single(model.Scenario.PlacementOverrides, o => o.FactionIndex == 2 && o.RosterIndex == 0);
        Assert.Equal(new Vector3(200f, 200f, 0f), model.Plan!.UnitPlacements[0].Position);
    }

    [Fact]
    public void Unproject_IsInverseOfProject()
    {
        var world = new Vector3(500f, 500f, 0f);
        var pMin = new Vector3(0f, 0f, 0f);
        var pMax = new Vector3(1000f, 1000f, 0f);
        var screen = ValidationGroundMapRenderer.Project(pMin, pMax, world, 0, 0, 200f);

        var back = ValidationGroundEditorModel.Unproject(pMin, pMax, screen.X, screen.Y, 0, 0, 200f);

        Assert.Equal(new Vector2(500f, 500f), back);
    }

    [Fact]
    public void SaveLoad_RoundTripsEdits()
    {
        var model = NewModelWithSingleFaction();
        model.MovePlacement(2, 0, false, 123f, 456f);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vg_editor_{System.Guid.NewGuid():N}.json");

        try
        {
            model.SaveScenario(path);
            var reloaded = new ValidationGroundEditorModel();
            reloaded.LoadScenarioFile(path);

            Assert.Equal(new Vector3(123f, 456f, 0f), reloaded.Plan!.UnitPlacements[0].Position);
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }
}
