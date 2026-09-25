using Panopticon.Content;

namespace Panopticon.Gui;

public interface IMainMenuSource
{
    void AddToScene(IGame game, Scene2D scene, bool useShellMap);
}
