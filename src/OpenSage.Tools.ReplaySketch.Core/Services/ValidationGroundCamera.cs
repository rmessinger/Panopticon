using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Frames the validation-ground formation in the tactical camera so a run opens on the whole
/// roster: looks at the bounding-box centre of every placement, then grows the camera height
/// until all of them project inside the viewport. Cluster placement spreads factions hundreds
/// of world units apart, so the game's default zoom shows at most one cluster (or none, since
/// the centroid of all clusters often falls on empty ground).
/// </summary>
public static class ValidationGroundCamera
{
    // Fraction of the half-viewport the formation must fit within, leaving room for the HUD and
    // window borders.
    private const float ScreenMargin = 0.8f;
    private const int MaxGrowthSteps = 8;
    private const int BisectionSteps = 10;

    // Empirical floor: the engine's terrain pass draws nothing (clear color shows through) for
    // large world regions when the tactical camera sits roughly 900-1500 world units above ground,
    // while ≤600 and ≥1500 render correctly in every observed run. The framing search therefore
    // never probes below this floor; the formation spans hundreds of units, so the fitted height
    // is well above it anyway. Revisit when the terrain mid-height void is root-caused (Track G
    // visual-regression territory).
    public const float MinimumSafeCameraHeight = 1600f;

    public static void FrameFormation(IScene3D scene, IReadOnlyList<Vector2> placements)
    {
        if (placements.Count == 0)
        {
            return;
        }

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var p in placements)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        var centre = (min + max) / 2f;

        var view = scene.TacticalView;
        view.LookAt(new Vector3(centre.X, centre.Y, 0f));
        view.InitHeightForMap();
        view.SetAngleAndPitchToDefault();

        // Growth phase: start no lower than the safe floor (see MinimumSafeCameraHeight) and
        // multiply until everything fits. Then bisect for a tight frame. Each probe is one matrix
        // multiply per placement.
        var viewHeight = MathF.Max(view.MaxHeightAboveGround, MinimumSafeCameraHeight);
        view.SnapHeightAboveGround(viewHeight);

        var tooClose = 0f;
        var fits = 0f;
        for (var i = 0; i < MaxGrowthSteps; i++)
        {
            if (AllPlacementsVisible(scene, placements))
            {
                fits = viewHeight;
                break;
            }
            tooClose = viewHeight;
            viewHeight *= 1.6f;
            view.SnapHeightAboveGround(viewHeight);
        }

        if (fits == 0f)
        {
            // Map larger than any growth probe: keep the furthest attempted height.
            return;
        }

        for (var i = 0; i < BisectionSteps && tooClose > 0f; i++)
        {
            var mid = (tooClose + fits) / 2f;
            view.SnapHeightAboveGround(mid);
            if (AllPlacementsVisible(scene, placements))
            {
                fits = mid;
            }
            else
            {
                tooClose = mid;
            }
        }

        view.SnapHeightAboveGround(fits);
    }

    private static bool AllPlacementsVisible(IScene3D scene, IReadOnlyList<Vector2> placements)
    {
        var viewProjection = scene.Camera.ViewProjection;
        foreach (var p in placements)
        {
            var v = Vector4.Transform(new Vector4(p.X, p.Y, 0f, 1f), viewProjection);
            if (v.W <= 0f
                || MathF.Abs(v.X / v.W) > ScreenMargin
                || MathF.Abs(v.Y / v.W) > ScreenMargin)
            {
                return false;
            }
        }
        return true;
    }
}
