#if MACCATALYST
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using OpenSage.Tools.WorldBuilder.Core;
using OpenSage.Tools.WorldBuilder.Maui.Views;
using UIKit;
using Veldrid;

namespace OpenSage.Tools.WorldBuilder.Maui.Platforms.MacCatalyst;

public class VeldridViewportHandler : ViewHandler<VeldridViewport, UIView>
{
    public static IPropertyMapper<VeldridViewport, VeldridViewportHandler> Mapper =
        new PropertyMapper<VeldridViewport, VeldridViewportHandler>(ViewMapper);

    private CADisplayLink? _displayLink;

    public VeldridViewportHandler()
        : base(Mapper)
    {
    }

    protected override UIView CreatePlatformView()
    {
        return new UIView
        {
            Opaque = true,
            ContentScaleFactor = UIScreen.MainScreen.Scale,
        };
    }

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);

        _displayLink = CADisplayLink.Create(OnDisplayLinkTick);
        _displayLink.AddToRunLoop(NSRunLoop.Current, NSRunLoopMode.Default);
    }

    protected override void DisconnectHandler(UIView platformView)
    {
        _displayLink?.Invalidate();
        _displayLink?.Dispose();
        _displayLink = null;

        base.DisconnectHandler(platformView);
    }

    private void OnDisplayLinkTick()
    {
        UpdateViewportSize();
        VirtualView?.Renderer?.RenderFrame();
    }

    private void UpdateViewportSize()
    {
        if (PlatformView == null)
        {
            return;
        }

        CGRect bounds = PlatformView.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        double scale = PlatformView.ContentScaleFactor;
        uint width = (uint)(bounds.Width * scale);
        uint height = (uint)(bounds.Height * scale);
        if (width == 0 || height == 0)
        {
            return;
        }

        VirtualView?.NotifySize(width, height);

        GameViewportRenderer? renderer = VirtualView?.Renderer;
        if (renderer == null)
        {
            return;
        }

        if (!renderer.IsInitialized)
        {
            SwapchainSource source = SwapchainSource.CreateUIView(PlatformView.Handle);
            renderer.Initialize(source, width, height);
        }
        else if (renderer.Width != width || renderer.Height != height)
        {
            renderer.Resize(width, height);
        }
    }
}
#endif
