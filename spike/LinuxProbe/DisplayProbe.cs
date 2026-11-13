using System.Diagnostics;
using static LinuxProbe.Report;

namespace LinuxProbe;

/// <summary>Whether a Wayland compositor or an X server answers this process (question 3 of the report).</summary>
internal static unsafe class DisplayProbe
{
    public static void Wayland(string label)
    {
        string p = "wayland." + label;
        Probe(p + ".env", $"WAYLAND_DISPLAY={Env("WAYLAND_DISPLAY")} XDG_RUNTIME_DIR={Env("XDG_RUNTIME_DIR")}");

        nint library = Dl.Open("libwayland-client.so.0");
        var connect = (delegate* unmanaged<byte*, nint>)Dl.Symbol(library, "wl_display_connect");
        var getFd = (delegate* unmanaged<nint, int>)Dl.Symbol(library, "wl_display_get_fd");
        var roundtrip = (delegate* unmanaged<nint, int>)Dl.Symbol(library, "wl_display_roundtrip");
        var disconnect = (delegate* unmanaged<nint, void>)Dl.Symbol(library, "wl_display_disconnect");

        long start = Stopwatch.GetTimestamp();
        nint display = connect(null);
        int errno = Dl.Errno();
        long end = Stopwatch.GetTimestamp();
        if (display == 0)
        {
            Failed(p + ".connect", $"wl_display_connect(NULL) returned NULL, errno {errno}, after {Ms(start, end)}");
            return;
        }

        Probe(p + ".connect", $"ok in {Ms(start, end)}, fd {getFd(display)}");
        start = Stopwatch.GetTimestamp();
        int events = roundtrip(display);
        end = Stopwatch.GetTimestamp();
        Probe(p + ".roundtrip", events >= 0 ? $"ok ({events} events) in {Ms(start, end)}" : $"FAILED returned {events}");
        disconnect(display);
    }

    public static void X11(string label)
    {
        string p = "x11." + label;
        Probe(p + ".env", $"DISPLAY={Env("DISPLAY")}");

        Guard(
            p + ".xlib",
            () =>
            {
                nint library = Dl.Open("libX11.so.6");
                var open = (delegate* unmanaged<byte*, nint>)Dl.Symbol(library, "XOpenDisplay");
                var vendor = (delegate* unmanaged<nint, byte*>)Dl.Symbol(library, "XServerVendor");
                var release = (delegate* unmanaged<nint, int>)Dl.Symbol(library, "XVendorRelease");
                var defaultScreen = (delegate* unmanaged<nint, int>)Dl.Symbol(library, "XDefaultScreen");
                var width = (delegate* unmanaged<nint, int, int>)Dl.Symbol(library, "XDisplayWidth");
                var height = (delegate* unmanaged<nint, int, int>)Dl.Symbol(library, "XDisplayHeight");
                var close = (delegate* unmanaged<nint, int>)Dl.Symbol(library, "XCloseDisplay");

                long start = Stopwatch.GetTimestamp();
                nint display = open(null);
                long end = Stopwatch.GetTimestamp();
                if (display == 0)
                {
                    Failed(p + ".xlib", $"XOpenDisplay(NULL) returned NULL after {Ms(start, end)}");
                    return;
                }

                int screen = defaultScreen(display);
                Probe(
                    p + ".xlib",
                    $"ok in {Ms(start, end)}: vendor '{Dl.Str(vendor(display))}' release {release(display)}, "
                        + $"screen {screen} {width(display, screen)}x{height(display, screen)}"
                );
                close(display);
            }
        );

        Guard(
            p + ".xcb",
            () =>
            {
                nint library = Dl.Open("libxcb.so.1");
                var connect = (delegate* unmanaged<byte*, int*, nint>)Dl.Symbol(library, "xcb_connect");
                var hasError = (delegate* unmanaged<nint, int>)Dl.Symbol(library, "xcb_connection_has_error");
                var disconnect = (delegate* unmanaged<nint, void>)Dl.Symbol(library, "xcb_disconnect");

                int screen = -1;
                long start = Stopwatch.GetTimestamp();
                // xcb_connect never returns NULL; a failed connection is an error object that xcb_disconnect frees.
                nint connection = connect(null, &screen);
                long end = Stopwatch.GetTimestamp();
                int error = hasError(connection);
                Probe(
                    p + ".xcb",
                    error == 0 ? $"ok in {Ms(start, end)}, screen {screen}" : $"FAILED xcb_connection_has_error {error} after {Ms(start, end)}"
                );
                disconnect(connection);
            }
        );
    }
}
