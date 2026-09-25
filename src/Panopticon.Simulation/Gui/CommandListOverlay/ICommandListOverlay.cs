using Panopticon.Logic;

namespace Panopticon.Gui.CommandListOverlay;

public interface ICommandListOverlay
{
    public void Update(Player player);
    public void Render(DrawingContext2D drawingContext);
}

public interface ICommandListOverlaySource
{
    ICommandListOverlay Create(IGame game);
}
