using System;
using System.Runtime.InteropServices;
using Veldrid.Sdl2;

namespace Panopticon.Utilities;

internal static class Sdl2Interop
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate SDL_Cursor SDL_CreateColorCursor_Delegate(SDL_Surface Sdl2Surface, int hot_x, int hot_y);

    private static readonly SDL_CreateColorCursor_Delegate CreateColorCursorImpl = Sdl2Native.LoadFunction<SDL_CreateColorCursor_Delegate>("SDL_CreateColorCursor");

    public static SDL_Cursor SDL_CreateColorCursor(SDL_Surface Sdl2Surface, int hotX, int hotY) => CreateColorCursorImpl(Sdl2Surface, hotX, hotY);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SDL_FreeCursor_Delegate(SDL_Cursor Sdl2Cursor);

    private static readonly SDL_FreeCursor_Delegate FreeCursorImpl = Sdl2Native.LoadFunction<SDL_FreeCursor_Delegate>("SDL_FreeCursor");

    public static void SDL_FreeCursor(SDL_Cursor Sdl2Cursor) => FreeCursorImpl(Sdl2Cursor);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SDL_SetCursor_Delegate(SDL_Cursor Sdl2Cursor);

    private static readonly SDL_SetCursor_Delegate SetCursorImpl = Sdl2Native.LoadFunction<SDL_SetCursor_Delegate>("SDL_SetCursor");

    public static void SDL_SetCursor(SDL_Cursor Sdl2Cursor) => SetCursorImpl(Sdl2Cursor);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate SDL_Surface SDL_CreateRGBSurfaceWithFormatFrom_Delegate(void* pixels, int width, int height, int depth, int pitch, SDL_PixelFormat format);

    private static readonly SDL_CreateRGBSurfaceWithFormatFrom_Delegate CreateRGBSurfaceWithFormatFromImpl = Sdl2Native.LoadFunction<SDL_CreateRGBSurfaceWithFormatFrom_Delegate>("SDL_CreateRGBSurfaceWithFormatFrom");

    public static unsafe SDL_Surface SDL_CreateRGBSurfaceWithFormatFrom(void* pixels, int width, int height, int depth, int pitch, SDL_PixelFormat format) => CreateRGBSurfaceWithFormatFromImpl(pixels, width, height, depth, pitch, format);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate SDL_Surface SDL_CreateRGBSurfaceWithFormat_Delegate(uint flags, int width, int height, int depth, SDL_PixelFormat format);

    private static readonly SDL_CreateRGBSurfaceWithFormat_Delegate CreateRGBSurfaceWithFormatImpl = Sdl2Native.LoadFunction<SDL_CreateRGBSurfaceWithFormat_Delegate>("SDL_CreateRGBSurfaceWithFormat");

    public static unsafe SDL_Surface SDL_CreateRGBSurfaceWithFormat(uint flags, int width, int height, int depth, SDL_PixelFormat format) => CreateRGBSurfaceWithFormatImpl(flags, width, height, depth, format);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SDL_FreeSurface_Delegate(SDL_Surface Sdl2Surface);

    private static readonly SDL_FreeSurface_Delegate FreeSurfaceImpl = Sdl2Native.LoadFunction<SDL_FreeSurface_Delegate>("SDL_FreeSurface");

    public static void SDL_FreeSurface(SDL_Surface Sdl2Surface) => FreeSurfaceImpl(Sdl2Surface);


    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SDL_GetWindowDisplayIndex_Delegate(SDL_Window Sdl2Window);

    private static readonly SDL_GetWindowDisplayIndex_Delegate GetWindowDisplayIndexImpl = Sdl2Native.LoadFunction<SDL_GetWindowDisplayIndex_Delegate>("SDL_GetWindowDisplayIndex");

    public static int SDL_GetWindowDisplayIndex(SDL_Window Sdl2Window) => GetWindowDisplayIndexImpl(Sdl2Window);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int SDL_GetDisplayDPI_Delegate(int displayIndex, float* ddpi, float* hdpi, float* vdpi);

    private static readonly SDL_GetDisplayDPI_Delegate GetDisplayDPIImpl = Sdl2Native.LoadFunction<SDL_GetDisplayDPI_Delegate>("SDL_GetDisplayDPI");

    public static unsafe int SDL_GetDisplayDPI(int displayIndex, float* ddpi, float* hdpi, float* vdpi) => GetDisplayDPIImpl(displayIndex, ddpi, hdpi, vdpi);

    public static unsafe float GetDisplayScale(int displayIndex)
    {
        float hdpi;
        int result = SDL_GetDisplayDPI(displayIndex, null, &hdpi, null);

        // The function call did not succeed.
        // TODO: We should check why with SDL_GetError
        if (result != 0)
        {
            return 1.0f;
        }

        var defaultDpi = PlatformUtility.GetDefaultDpi();
        return hdpi / defaultDpi;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void SDL_GetWindowSizeInPixels_Delegate(SDL_Window window, int* w, int* h);

    // SDL 2.26+. Older SDL builds (the Windows binaries bundled with Veldrid.SDL2 4.9) lack it,
    // in which case LoadFunction returns null and the GL drawable size is used instead.
    private static readonly SDL_GetWindowSizeInPixels_Delegate GetWindowSizeInPixelsImpl = Sdl2Native.LoadFunction<SDL_GetWindowSizeInPixels_Delegate>("SDL_GetWindowSizeInPixels");

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void SDL_GL_GetDrawableSize_Delegate(SDL_Window window, int* w, int* h);

    private static readonly SDL_GL_GetDrawableSize_Delegate GLGetDrawableSizeImpl = Sdl2Native.LoadFunction<SDL_GL_GetDrawableSize_Delegate>("SDL_GL_GetDrawableSize");

    public static unsafe void SDL_GetWindowSizeInPixels(SDL_Window window, int* w, int* h)
    {
        if (GetWindowSizeInPixelsImpl != null)
        {
            GetWindowSizeInPixelsImpl(window, w, h);
        }
        else if (GLGetDrawableSizeImpl != null)
        {
            GLGetDrawableSizeImpl(window, w, h);
        }
    }

    /// <summary>
    /// Returns the window's drawable size in physical pixels, or <paramref name="logicalSize"/>
    /// when SDL cannot report one.
    /// </summary>
    public static unsafe Mathematics.Size GetWindowSizeInPixels(SDL_Window window, in Mathematics.Size logicalSize)
    {
        int width = 0;
        int height = 0;
        SDL_GetWindowSizeInPixels(window, &width, &height);

        return width > 0 && height > 0
            ? new Mathematics.Size(width, height)
            : logicalSize;
    }

    public struct SDL_Cursor
    {
        public readonly IntPtr NativePointer;

        public SDL_Cursor(IntPtr pointer)
        {
            NativePointer = pointer;
        }

        public static implicit operator IntPtr(SDL_Cursor Sdl2Cursor) => Sdl2Cursor.NativePointer;
        public static implicit operator SDL_Cursor(IntPtr pointer) => new SDL_Cursor(pointer);
    }

    public struct SDL_Surface
    {
        public readonly IntPtr NativePointer;

        public SDL_Surface(IntPtr pointer)
        {
            NativePointer = pointer;
        }

        public static implicit operator IntPtr(SDL_Surface Sdl2Surface) => Sdl2Surface.NativePointer;
        public static implicit operator SDL_Surface(IntPtr pointer) => new SDL_Surface(pointer);
    }

    public enum SDL_PixelFormat
    {
        SDL_PIXELFORMAT_ABGR8888 = 376840196,
    }

    public struct SDL_Rect
    {
        public int X;
        public int Y;
        public int W;
        public int H;

        public SDL_Rect(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }
    }
}
