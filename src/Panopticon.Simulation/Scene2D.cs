using Panopticon.Gui;
using Panopticon.Gui.Apt;
using Panopticon.Gui.CommandListOverlay;
using Panopticon.Gui.ControlBar;
using Panopticon.Gui.Wnd;
using Panopticon.Logic;

namespace Panopticon;

public sealed class Scene2D
{
    public WndWindowManager WndWindowManager { get; }
    public AptWindowManager AptWindowManager { get; }
    public IControlBar ControlBar { get; set; }
    public ICommandListOverlay UnitOverlay { get; set; }

    public Scene2D(IGame game)
    {
        WndWindowManager = new WndWindowManager(game);
        AptWindowManager = new AptWindowManager(game);
    }

    internal void LocalLogicTick(in TimeInterval gameTime, Player localPlayer)
    {
        ControlBar?.Update(localPlayer);
        UnitOverlay?.Update(localPlayer);

        WndWindowManager.Update(gameTime);
        AptWindowManager.Update(gameTime);
    }

    internal void Render(DrawingContext2D drawingContext)
    {
        UnitOverlay?.Render(drawingContext);

        WndWindowManager.Render(drawingContext);
        AptWindowManager.Render(drawingContext);
    }
}
