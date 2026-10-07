using System;
using Panopticon.Graphics;
using Veldrid;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon;

public sealed class GamePanel : DisposableBase
{
    private readonly RenderTarget _renderTarget;

    public Framebuffer Framebuffer => _renderTarget.Framebuffer;

    public OutputDescription OutputDescription { get; } = RenderTarget.OutputDescription;

    public event EventHandler ClientSizeChanged;

    /// <summary>
    /// Panel rectangle in physical pixels (the window's drawable size, not its logical points).
    /// </summary>
    public Rectangle Frame { get; private set; }

    /// <summary>
    /// Panel size in physical pixels, at the origin.
    /// </summary>
    public Rectangle ClientBounds => new Rectangle(0, 0, Frame.Width, Frame.Height);

    internal GamePanel(GraphicsDevice graphicsDevice)
    {
        _renderTarget = AddDisposable(new RenderTarget(graphicsDevice));
    }

    public void EnsureFrame(in Rectangle frame)
    {
        if (frame == Frame)
        {
            return;
        }

        Frame = frame;

        if (_renderTarget.EnsureSize(frame.Size))
        {
            ClientSizeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
