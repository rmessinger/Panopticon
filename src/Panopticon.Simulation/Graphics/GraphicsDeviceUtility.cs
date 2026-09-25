using System;
using Veldrid;
using Veldrid.StartupUtilities;

namespace Panopticon.Graphics;

internal static class GraphicsDeviceUtility
{
    /// <summary>
    /// Backends that Veldrid can create an offscreen <see cref="GraphicsDevice"/> for,
    /// without an accompanying window/surface (OpenGL and OpenGL ES require a window-bound context).
    /// </summary>
    private static readonly GraphicsBackend[] HeadlessCapableBackends =
    {
        GraphicsBackend.Vulkan,
        GraphicsBackend.Direct3D11,
        GraphicsBackend.Metal,
    };

    public static GraphicsDevice CreateGraphicsDevice(GraphicsBackend? preferredBackend, GameWindow window)
    {
#if DEBUG
        const bool debug = true;
#else
        const bool debug = false;
#endif

        var options = new GraphicsDeviceOptions
        {
            Debug = debug,
            SyncToVerticalBlank = true,
            ResourceBindingModel = ResourceBindingModel.Improved,
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true
        };

        var backend = preferredBackend ?? VeldridStartup.GetPlatformDefaultBackend();

        if (window != null)
        {
            return window.CreateGraphicsDevice(options, backend);
        }

        // Without a window there's no surface to create an OpenGL/OpenGLES context against.
        // If the caller didn't explicitly request one of those, fall back to the first
        // supported backend that Veldrid can create headlessly (e.g. Vulkan on Linux).
        if ((backend == GraphicsBackend.OpenGL || backend == GraphicsBackend.OpenGLES) && preferredBackend == null)
        {
            foreach (var candidate in HeadlessCapableBackends)
            {
                if (GraphicsDevice.IsBackendSupported(candidate))
                {
                    backend = candidate;
                    break;
                }
            }
        }

        return backend switch
        {
            GraphicsBackend.Direct3D11 => GraphicsDevice.CreateD3D11(options),
            GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(options),
            GraphicsBackend.Metal => GraphicsDevice.CreateMetal(options),
            _ => throw new InvalidOperationException(
                $"Cannot create a graphics device for {backend} backend without a window. " +
                "OpenGL and OpenGL ES require a window-bound context; pass a window or select a different backend."),
        };
    }

    /// <summary>
    /// Cheap capability check (no device creation) for whether <see cref="CreateGraphicsDevice"/>
    /// can succeed without a window on this host; gates device-backed tests.
    /// </summary>
    public static bool CanCreateHeadlessDevice()
    {
        var backend = VeldridStartup.GetPlatformDefaultBackend();

        if (backend != GraphicsBackend.OpenGL && backend != GraphicsBackend.OpenGLES)
        {
            return true;
        }

        foreach (var candidate in HeadlessCapableBackends)
        {
            if (GraphicsDevice.IsBackendSupported(candidate))
            {
                return true;
            }
        }

        return false;
    }
}
