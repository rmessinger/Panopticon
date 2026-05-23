using System;
using System.Runtime.InteropServices;
using Veldrid.Sdl2;

namespace OpenSage;

public static class Platform
{
    public static void Start()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // SDL2's Cocoa_RegisterApp calls [NSApp setMainMenu:] when NSApp is nil at
            // SDL_VideoInit time.  On macOS 14+ the menu nib SDL uses contains a
            // deprecated NSCalendarDate that throws NSGenericException during decoding,
            // which propagates through the C++ exception machinery as an unhandled
            // exception and kills the process with SIGABRT.
            //
            // Calling [NSApplication sharedApplication] first leaves NSApp non-nil, so
            // SDL skips the setMainMenu: call entirely and the crash does not occur.
            InitNSApplication();

            // SDL_SetMainReady() tells SDL2 that the caller has done its own SDL_main-
            // equivalent entry-point setup. Without it SDL may skip some
            // thread-safety checks, so we still want it.
            SetMainReadyIfAvailable();
        }

        Sdl2Native.SDL_Init(SDLInitFlags.Video);
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSend(IntPtr receiver, IntPtr selector);

    private static void InitNSApplication()
    {
        // [NSApplication sharedApplication] — idempotent; safe to call multiple times.
        var nsApplicationClass = ObjcGetClass("NSApplication");
        if (nsApplicationClass == IntPtr.Zero)
        {
            return;
        }

        var sharedApplicationSel = SelRegisterName("sharedApplication");
        ObjcMsgSend(nsApplicationClass, sharedApplicationSel);
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
