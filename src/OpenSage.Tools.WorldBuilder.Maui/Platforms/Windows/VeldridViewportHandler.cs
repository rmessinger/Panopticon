#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using OpenSage.Tools.WorldBuilder.Core;
using OpenSage.Tools.WorldBuilder.Maui.Views;
using Veldrid;

namespace OpenSage.Tools.WorldBuilder.Maui.Platforms.Windows;

public class VeldridViewportHandler : ViewHandler<VeldridViewport, SwapChainPanel>
{
    public static IPropertyMapper<VeldridViewport, VeldridViewportHandler> Mapper =
        new PropertyMapper<VeldridViewport, VeldridViewportHandler>(ViewMapper);

    public VeldridViewportHandler()
        : base(Mapper)
    {
    }

    protected override SwapChainPanel CreatePlatformView()
    {
        SwapChainPanel panel = new();
        panel.SizeChanged += OnSizeChanged;
        return panel;
    }

    protected override void ConnectHandler(SwapChainPanel platformView)
    {
        base.ConnectHandler(platformView);
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }

    protected override void DisconnectHandler(SwapChainPanel platformView)
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
        platformView.SizeChanged -= OnSizeChanged;
        base.DisconnectHandler(platformView);
    }

    private void OnRendering(object? sender, object e)
    {
        VirtualView?.Renderer?.RenderFrame();
    }

    private void OnSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        float dpi = (float) (VirtualView?.Window?.DisplayDensity ?? 1.0f);
        uint width = (uint) (e.NewSize.Width * dpi);
        uint height = (uint) (e.NewSize.Height * dpi);

        if (width == 0 || height == 0 || PlatformView == null)
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
            SwapchainSource source = SwapchainSource.CreateUwp(PlatformView, dpi * 96f);
            renderer.Initialize(source, width, height);
        }
        else
        {
            renderer.Resize(width, height);
        }
    }
}
#endif
