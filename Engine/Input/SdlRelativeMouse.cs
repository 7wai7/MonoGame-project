using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace MonoGame_project.Engine.Input;

/// <summary>
/// Provides true relative mouse input through the SDL2 backend used by
/// MonoGame DesktopGL. Unlike cursor warping, this does not trigger macOS's
/// shake-to-locate cursor animation.
/// </summary>
internal static class SdlRelativeMouse
{
    public static void Enable()
    {
        if (SetRelativeMouseMode(enabled: 1) != 0)
        {
            throw new InvalidOperationException(
                $"SDL could not enable relative mouse mode: {GetError()}");
        }

        // Discard any motion accumulated before the mode was enabled.
        ReadDelta();
    }

    public static void Disable()
    {
        if (SetRelativeMouseMode(enabled: 0) != 0)
        {
            throw new InvalidOperationException(
                $"SDL could not disable relative mouse mode: {GetError()}");
        }
    }

    public static Point ReadDelta()
    {
        GetRelativeMouseState(out var deltaX, out var deltaY);
        return new Point(deltaX, deltaY);
    }

    private static string GetError()
    {
        var error = Marshal.PtrToStringUTF8(GetErrorPointer());
        return string.IsNullOrWhiteSpace(error) ? "Unknown SDL error." : error;
    }

    private static int SetRelativeMouseMode(int enabled)
    {
        if (OperatingSystem.IsMacOS())
        {
            return MacOS.SetRelativeMouseMode(enabled);
        }

        if (OperatingSystem.IsWindows())
        {
            return Windows.SetRelativeMouseMode(enabled);
        }

        if (OperatingSystem.IsLinux())
        {
            return Linux.SetRelativeMouseMode(enabled);
        }

        throw new PlatformNotSupportedException(
            "Relative mouse input is supported only on MonoGame DesktopGL platforms.");
    }

    private static uint GetRelativeMouseState(out int deltaX, out int deltaY)
    {
        if (OperatingSystem.IsMacOS())
        {
            return MacOS.GetRelativeMouseState(out deltaX, out deltaY);
        }

        if (OperatingSystem.IsWindows())
        {
            return Windows.GetRelativeMouseState(out deltaX, out deltaY);
        }

        if (OperatingSystem.IsLinux())
        {
            return Linux.GetRelativeMouseState(out deltaX, out deltaY);
        }

        throw new PlatformNotSupportedException(
            "Relative mouse input is supported only on MonoGame DesktopGL platforms.");
    }

    private static nint GetErrorPointer()
    {
        if (OperatingSystem.IsMacOS())
        {
            return MacOS.GetError();
        }

        if (OperatingSystem.IsWindows())
        {
            return Windows.GetError();
        }

        if (OperatingSystem.IsLinux())
        {
            return Linux.GetError();
        }

        return nint.Zero;
    }

    private static class MacOS
    {
        private const string LibraryName = "libSDL2-2.0.0.dylib";

        [DllImport(
            LibraryName,
            EntryPoint = "SDL_SetRelativeMouseMode",
            CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetRelativeMouseMode(int enabled);

        [DllImport(
            LibraryName,
            EntryPoint = "SDL_GetRelativeMouseState",
            CallingConvention = CallingConvention.Cdecl)]
        public static extern uint GetRelativeMouseState(out int deltaX, out int deltaY);

        [DllImport(
            LibraryName,
            EntryPoint = "SDL_GetError",
            CallingConvention = CallingConvention.Cdecl)]
        public static extern nint GetError();
    }

    private static class Windows
    {
        private const string LibraryName = "SDL2.dll";

        [DllImport(
            LibraryName,
            EntryPoint = "SDL_SetRelativeMouseMode",
            CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetRelativeMouseMode(int enabled);

        [DllImport(
            LibraryName,
            EntryPoint = "SDL_GetRelativeMouseState",
            CallingConvention = CallingConvention.Cdecl)]
        public static extern uint GetRelativeMouseState(out int deltaX, out int deltaY);

        [DllImport(
            LibraryName,
            EntryPoint = "SDL_GetError",
            CallingConvention = CallingConvention.Cdecl)]
        public static extern nint GetError();
    }

    private static class Linux
    {
        private const string LibraryName = "libSDL2-2.0.so.0";

        [DllImport(
            LibraryName,
            EntryPoint = "SDL_SetRelativeMouseMode",
            CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetRelativeMouseMode(int enabled);

        [DllImport(
            LibraryName,
            EntryPoint = "SDL_GetRelativeMouseState",
            CallingConvention = CallingConvention.Cdecl)]
        public static extern uint GetRelativeMouseState(out int deltaX, out int deltaY);

        [DllImport(
            LibraryName,
            EntryPoint = "SDL_GetError",
            CallingConvention = CallingConvention.Cdecl)]
        public static extern nint GetError();
    }
}
