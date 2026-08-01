using System;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Multiplies a game's tactical-camera <c>MaxHeightAboveGround</c> by a configured multiplier
/// on the first frame a tactical view is available, then unsubscribes. Shared by the replay and
/// validation-ground runners.
/// </summary>
internal static class CameraZoomInitializer
{
    public static void ApplyOnce(Game game, float multiplier)
    {
        if (multiplier == 1.0f)
        {
            return;
        }

        EventHandler<GameUpdatingEventArgs> handler = null!;
        handler = (_, _) =>
        {
            var view = game.Scene3D?.TacticalView;
            if (view == null)
            {
                return;
            }

            view.MaxHeightAboveGround *= multiplier;
            game.Updating -= handler;
        };
        game.Updating += handler;
    }
}
