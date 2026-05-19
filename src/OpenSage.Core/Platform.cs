using System;
using System.Runtime.InteropServices;
using Veldrid.Sdl2;

namespace OpenSage;

public static class Platform
{
    public static void Start()
    {
        // On macOS, SDL2 requires SDL_SetMainReady() before SDL_Init() whenever
        // the caller is not launched via SDL's own entry point (e.g. test hosts,
        // or apps that perform their own Cocoa/AppKit set-up). Without this call
        // SDL2 attempts to configure the main menu from a non-main thread, which
        // Cocoa rejects with NSInternalInconsistencyException.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetMainReadyIfAvailable();
        }

        Sdl2Native.SDL_Init(SDLInitFlags.Video);
    }

    private static void SetMainReadyIfAvailable()
    {
        // Try to load the SDL2 dylib by its unversioned name; this works whether
        // the library lives next to the assembly or in a system-wide location.
        foreach (var name in new[] { "libsdl2", "libSDL2", "libsdl2.dylib", "libSDL2.dylib" })
        {
            if (NativeLibrary.TryLoad(name, out var handle) &&
                NativeLibrary.TryGetExport(handle, "SDL_SetMainReady", out var fn))
            {
                Marshal.GetDelegateForFunctionPointer<Action>(fn)();
                return;
            }
        }
    }

    public static void Stop()
    {
        // ?
    }
}
