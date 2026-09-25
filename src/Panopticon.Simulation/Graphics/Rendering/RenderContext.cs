using Panopticon.Content;
using Veldrid;

namespace Panopticon.Graphics.Rendering;

public sealed class RenderContext
{
    public ContentManager ContentManager;
    public GraphicsDevice GraphicsDevice;

    public IScene3D Scene3D;
    public Scene2D Scene2D;
    public Framebuffer RenderTarget;

    public TimeInterval GameTime;
}
