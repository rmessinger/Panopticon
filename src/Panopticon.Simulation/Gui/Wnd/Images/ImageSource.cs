using Panopticon.Content;
using Panopticon.Mathematics;
using Veldrid;

namespace Panopticon.Gui.Wnd.Images;

internal abstract class ImageSource
{
    public abstract Size NaturalSize { get; }

    public abstract Texture GetTexture(Size size);
}
