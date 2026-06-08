using System.Collections.Generic;
using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;
using SkiaSharp;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// Golden-image tests for <see cref="MapPreviewRenderer"/>.
/// First run (or <c>UPDATE_GOLDENS=1</c>) auto-creates PNG baselines.
/// </summary>
public sealed class MapPreviewRendererTests
{
    // ── Shared test map — 800×800 world units, 2 players ─────────────────────

    private static readonly MapMetadataService s_map = MapMetadataService.CreateForTesting(
        extentMin: new Vector3(0f, 0f, 0f),
        extentMax: new Vector3(800f, 800f, 0f),
        playerStarts:
        [
            new Vector3(150f, 150f, 0f),  // slot 0 (bottom-left)
            new Vector3(650f, 650f, 0f),  // slot 1 (top-right)
        ]);

    private const float BaseRadius = 80f;
    private const int Width = 500;
    private const int Height = 500;

    // ── Helper ────────────────────────────────────────────────────────────────

    private static byte[] Render(
        IReadOnlyList<PlayerSlotConfig> players,
        string? selectedActionId = null,
        int selectedSlot = 0)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x2A, 0x2A, 0x2A));

        MapPreviewRenderer.Render(
            canvas, s_map, previewBitmap: null, players,
            baseRadiusWorldUnits: BaseRadius,
            selectedSlotIndex: selectedSlot,
            selectedActionId: selectedActionId,
            offsetX: 0, offsetY: 0, previewSize: Width);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static PlayerSlotConfig MakeSlot(params ActionEntry[] actions)
    {
        var slot = new PlayerSlotConfig();
        slot.Actions.AddRange(actions);
        return slot;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_EmptyMap_MatchesGolden()
    {
        var players = new List<PlayerSlotConfig>
        {
            MakeSlot(),
            MakeSlot(),
        };

        var png = Render(players);
        GoldenImageHelper.AssertMatchesGolden(png, "map-empty");
    }

    [Fact]
    public void Render_NormalizedPositionMarker_MatchesGolden()
    {
        var action = new ActionEntry(
            label: "Construct Barracks",
            type: ActionType.Construct,
            position: new NormalizedPosition(0.3f, 0.4f),
            timing: new FixedTiming(120));

        var players = new List<PlayerSlotConfig>
        {
            MakeSlot(action),
            MakeSlot(),
        };

        var png = Render(players);
        GoldenImageHelper.AssertMatchesGolden(png, "map-normalized-position");
    }

    [Fact]
    public void Render_FixedAngleLandmarkMarker_MatchesGolden()
    {
        var action = new ActionEntry(
            label: "Attack East",
            type: ActionType.Construct,
            position: new LandmarkRelativePosition(
                LandmarkType.OwnBase,
                DistanceInBaseWidths: 1.2f,
                Angle: new FixedAngle(45f)),
            timing: new FixedTiming(200));

        var players = new List<PlayerSlotConfig>
        {
            MakeSlot(action),
            MakeSlot(),
        };

        var png = Render(players);
        GoldenImageHelper.AssertMatchesGolden(png, "map-fixed-angle-landmark");
    }

    [Fact]
    public void Render_RandomAngleArcMarker_MatchesGolden()
    {
        var action = new ActionEntry(
            label: "Rush",
            type: ActionType.Construct,
            position: new LandmarkRelativePosition(
                LandmarkType.OwnBase,
                DistanceInBaseWidths: 1.5f,
                Angle: new RandomAngle(30f, 90f)),
            timing: new FixedTiming(300));

        var players = new List<PlayerSlotConfig>
        {
            MakeSlot(action),
            MakeSlot(),
        };

        var png = Render(players);
        GoldenImageHelper.AssertMatchesGolden(png, "map-random-angle-arc");
    }

    [Fact]
    public void Render_SelectedSlot_MatchesGolden()
    {
        var action = new ActionEntry(
            label: "Build Barracks",
            type: ActionType.Construct,
            position: new NormalizedPosition(0.5f, 0.5f),
            timing: new FixedTiming(100));

        var players = new List<PlayerSlotConfig>
        {
            MakeSlot(action),
            MakeSlot(),
        };

        // Render with slot 0 selected
        var png = Render(players, selectedSlot: 0);
        GoldenImageHelper.AssertMatchesGolden(png, "map-selected-slot");
    }

    [Fact]
    public void Render_SelectedAction_MatchesGolden()
    {
        var action = new ActionEntry(
            label: "Build Barracks",
            type: ActionType.Construct,
            position: new NormalizedPosition(0.4f, 0.35f),
            timing: new FixedTiming(100));

        var players = new List<PlayerSlotConfig>
        {
            MakeSlot(action),
            MakeSlot(),
        };

        var png = Render(players, selectedActionId: action.Id, selectedSlot: 0);
        GoldenImageHelper.AssertMatchesGolden(png, "map-selected-action");
    }

    [Fact]
    public void Render_MultiplePlayersAndActions_MatchesGolden()
    {
        var a1 = new ActionEntry("Barracks", ActionType.Construct,
            new NormalizedPosition(0.25f, 0.30f), new FixedTiming(100));
        var a2 = new ActionEntry("Rush NE", ActionType.Construct,
            new LandmarkRelativePosition(LandmarkType.OwnBase, 1.2f, new FixedAngle(60f)),
            new FixedTiming(200));

        var b1 = new ActionEntry("Barracks", ActionType.Construct,
            new NormalizedPosition(0.75f, 0.70f), new FixedTiming(100));

        var players = new List<PlayerSlotConfig>
        {
            MakeSlot(a1, a2),
            MakeSlot(b1),
        };

        var png = Render(players);
        GoldenImageHelper.AssertMatchesGolden(png, "map-multi-player-actions");
    }
}
