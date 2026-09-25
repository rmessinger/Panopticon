using Panopticon.Logic;

namespace Panopticon.Gui.ControlBar;

public interface IControlBar
{
    void AddToScene(Scene2D scene2D);
    void Update(Player player);
}

public interface IControlBarSource
{
    IControlBar Create(string side, IGame game);
}
