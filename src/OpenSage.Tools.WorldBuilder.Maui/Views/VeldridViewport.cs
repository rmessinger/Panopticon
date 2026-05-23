using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Maui.Views;

public class VeldridViewport : View
{
    public new event Action<uint, uint>? SizeAllocated;

    public GameViewportRenderer? Renderer { get; set; }

    internal void NotifySize(uint width, uint height)
    {
        SizeAllocated?.Invoke(width, height);
    }
}
