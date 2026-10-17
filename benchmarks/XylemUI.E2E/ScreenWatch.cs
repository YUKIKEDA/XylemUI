using System.Diagnostics;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// Watches the app's window for the scene's marker rectangle (#3366CC at (0, 0, 40, 40) of the client area, Scene.cs) to measure
// startup the same way for every framework: the window's content as the desktop compositor has it (PrintWindow with
// PW_RENDERFULLCONTENT, into a small bitmap), read in a tight loop. The window's own content is read, not the screen: a window
// that a background process starts may open behind another window, and the screen would show that one.
// Unelevated PresentMon misses the first presents of a process it learns of late (S8b/M1a: the first recorded present came
// 656 ms after a launch whose first frame was on screen at about 270 ms), so it is not used for startup.
internal static unsafe class ScreenWatch
{
    // Makes the app's main window topmost once it exists, so that the compositor shows every frame: a window that a background
    // process starts may open behind another one, and the frames of a covered window are not displayed (M1a: 210 of 582).
    public static void BringToFront(Process process)
    {
        long start = Stopwatch.GetTimestamp();
        HWND window = HWND.NULL;
        while (window == HWND.NULL && Stopwatch.GetElapsedTime(start).TotalSeconds < 10)
        {
            Thread.Sleep(10);
            process.Refresh();
            window = (HWND)process.MainWindowHandle;
        }

        _ = SetWindowPos(window, HWND.HWND_TOPMOST, 0, 0, 0, 0, SWP.SWP_NOMOVE | SWP.SWP_NOSIZE | SWP.SWP_SHOWWINDOW);
    }

    // Launches the app and returns the milliseconds from just before the launch to the first read of the marker on screen, or
    // null after 10 s.
    public static double? LaunchToMarker(string app, string arguments, out Process process)
    {
        HDC screen = GetDC(HWND.NULL);
        HDC memory = CreateCompatibleDC(screen);
        HBITMAP bitmap = CreateCompatibleBitmap(screen, 64, 64);
        HGDIOBJ previous = SelectObject(memory, (HGDIOBJ)bitmap);
        try
        {
            long launched = Stopwatch.GetTimestamp();
            process = Process.Start(new ProcessStartInfo(app, arguments) { UseShellExecute = false })!;
            HWND window = HWND.NULL;
            while (Stopwatch.GetElapsedTime(launched).TotalSeconds < 10)
            {
                if (window == HWND.NULL)
                {
                    process.Refresh();
                    window = (HWND)process.MainWindowHandle;
                    continue;
                }

                // PW_CLIENTONLY | PW_RENDERFULLCONTENT: the client area's composed content, clipped to the 64 x 64 bitmap.
                if (!PrintWindow(window, memory, 1 | 2))
                {
                    continue;
                }

                COLORREF color = GetPixel(memory, 20, 20);

                // COLORREF is 0x00BBGGRR; the marker is RGB 0x33, 0x66, 0xCC.
                if ((uint)color == 0x00CC6633)
                {
                    return Stopwatch.GetElapsedTime(launched).TotalMilliseconds;
                }
            }

            return null;
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject((HGDIOBJ)bitmap);
            _ = DeleteDC(memory);
            _ = ReleaseDC(HWND.NULL, screen);
        }
    }
}
