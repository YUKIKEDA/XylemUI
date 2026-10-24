using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The window's display scale (design §6.1.1, "Windows' display scale", roadmap row W1). A test cannot change the display's scale,
// so the change of scale is a WM_DPICHANGED sent to the window, as Windows sends it.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class DisplayScaleTests(UiThreadFixture ui)
{
    /// <summary>
    /// The process is per-monitor DPI aware (V2) once a window exists, and the window's scale is its DPI over 96.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window.
    ///
    /// Steps:
    /// - Read the thread's DPI awareness context and the window's DPI.
    ///
    /// Expected:
    /// - The context is <c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>; <see cref="Win32Window.ScaleFactor"/> is
    ///   <c>GetDpiForWindow</c> / 96.
    /// </remarks>
    [Fact]
    public void Window_Created_IsPerMonitorAwareWithItsDpiAsTheScale() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("awareness", 200, 100);

            Assert.True(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) != BOOL.FALSE);
            Assert.Equal(GetDpiForWindow(window.Hwnd) / 96f, window.ScaleFactor);
        });

    /// <summary>
    /// The size a window is made with is the logical size of its client area.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Make a hidden window of 400 x 300 and read its client size.
    ///
    /// Expected:
    /// - The client size is round(400 × ScaleFactor) x round(300 × ScaleFactor) physical pixels; where 400 × ScaleFactor is a whole
    ///   number (100, 125, 150 %), the client size over ScaleFactor is 400 x 300 again, the size the layout root is given.
    /// </remarks>
    [Fact]
    public void Constructor_LogicalSize_IsTheClientAreaAtTheScale() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("size", 400, 300);

            float scale = window.ScaleFactor;
            (int width, int height) = window.ClientSize;
            Assert.Equal(((int)MathF.Round(400 * scale), (int)MathF.Round(300 * scale)), (width, height));
            if (400 * scale == MathF.Round(400 * scale) && 300 * scale == MathF.Round(300 * scale))
            {
                Assert.Equal((400f, 300f), (width / scale, height / scale));
            }
        });

    /// <summary>
    /// A window reads its text rendering once while it is made, after its size and monitor are known.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A text rendering reader that counts its calls.
    ///
    /// Steps:
    /// - Make a hidden window (CreateWindowExW and the constructor's resize send WM_WINDOWPOSCHANGED).
    ///
    /// Expected:
    /// - The reader was called once, for the window's monitor.
    /// </remarks>
    [Fact]
    public void Constructor_ReadsTheTextRenderingOnce() =>
        ui.Invoke(() =>
        {
            Func<HMONITOR, TextRenderingParameters> previous = Win32Window.TextRenderingReader;
            try
            {
                var read = new List<HMONITOR>();
                Win32Window.TextRenderingReader = monitor =>
                {
                    read.Add(monitor);
                    return previous(monitor);
                };

                using var window = new Win32Window("read once", 200, 100);

                Assert.Equal([MonitorFromWindow(window.Hwnd, MONITOR.MONITOR_DEFAULTTONEAREST)], read);
            }
            finally
            {
                Win32Window.TextRenderingReader = previous;
            }
        });

    /// <summary>
    /// WM_DPICHANGED sets the scale, reads the text rendering of the monitor the suggested rectangle is on, applies the rectangle,
    /// and only then renders, so the synchronous render sees the new scale and the new monitor's text rendering.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window, a text rendering reader that records the monitor it reads and returns ClearType, and a synchronous render
    ///   that records the scale and the text rendering it runs with.
    ///
    /// Steps:
    /// - Send WM_DPICHANGED for 144 DPI with a suggested rectangle 1.5 times the window's size at the same place; send it again.
    ///
    /// Expected:
    /// - <see cref="Win32Window.ScaleFactor"/> is 1.5; the window's rectangle is the suggested one.
    /// - The reader was asked for the monitor of the suggested rectangle; the render ran at 1.5 with the reader's value.
    /// - <see cref="Win32Window.ScaleChanged"/> came once (the second message does not change the scale).
    /// </remarks>
    [Fact]
    public void DpiChanged_SetsTheScaleAndTextRenderingBeforeTheRender() =>
        ui.Invoke(() =>
        {
            Func<HMONITOR, TextRenderingParameters> previous = Win32Window.TextRenderingReader;
            try
            {
                using var window = new Win32Window("dpi changed", 300, 200);
                var clearType = new TextRenderingParameters(
                    GlyphCoverage.Rgb,
                    IsBgr: true,
                    Gamma: 2.2f,
                    GrayscaleContrast: 1,
                    ClearTypeContrast: 0.5f
                );
                var read = new List<HMONITOR>();
                Win32Window.TextRenderingReader = monitor =>
                {
                    read.Add(monitor);
                    return clearType;
                };
                var rendered = new List<(float Scale, TextRenderingParameters Rendering)>();
                window.SynchronousRender = (w, _, _) => rendered.Add((w.ScaleFactor, w.TextRendering));
                int changed = 0;
                window.ScaleChanged += (_, _) => changed++;
                RECT before;
                _ = GetWindowRect(window.Hwnd, &before);
                var suggested = new RECT
                {
                    left = before.left,
                    top = before.top,
                    right = before.left + ((before.right - before.left) * 3 / 2),
                    bottom = before.top + ((before.bottom - before.top) * 3 / 2),
                };
                var wParam = (WPARAM)(nuint)((144 << 16) | 144);

                _ = SendMessageW(window.Hwnd, WM.WM_DPICHANGED, wParam, (LPARAM)(nint)(&suggested));
                _ = SendMessageW(window.Hwnd, WM.WM_DPICHANGED, wParam, (LPARAM)(nint)(&suggested));

                RECT after;
                _ = GetWindowRect(window.Hwnd, &after);
                Assert.Equal(1.5f, window.ScaleFactor);
                Assert.Equal((suggested.left, suggested.top, suggested.right, suggested.bottom), (after.left, after.top, after.right, after.bottom));
                Assert.Equal(MonitorFromRect(&suggested, MONITOR.MONITOR_DEFAULTTONEAREST), read[0]);
                Assert.Equal((1.5f, clearType), rendered[0]);
                Assert.Equal(1, changed);
            }
            finally
            {
                Win32Window.TextRenderingReader = previous;
            }
        });

    /// <summary>
    /// A window moved to another monitor reads that monitor's text rendering, also when the monitor's scale is the same, which sends
    /// no WM_DPICHANGED.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two monitors or more (skipped otherwise). A hidden window on the primary monitor, and a text rendering reader that records
    ///   the monitors it reads.
    ///
    /// Steps:
    /// - Move the window into the work area of another monitor with SetWindowPos.
    ///
    /// Expected:
    /// - The reader was asked for that monitor.
    /// </remarks>
    [Fact]
    public void Move_ToAnotherMonitor_ReadsItsTextRendering() =>
        ui.Invoke(() =>
        {
            Assert.SkipUnless(GetSystemMetrics(SM.SM_CMONITORS) >= 2, "This machine has one monitor.");
            Func<HMONITOR, TextRenderingParameters> previous = Win32Window.TextRenderingReader;
            try
            {
                var read = new List<HMONITOR>();
                Win32Window.TextRenderingReader = monitor =>
                {
                    read.Add(monitor);
                    return previous(monitor);
                };
                using var window = new Win32Window("move", 200, 100);
                HMONITOR first = MonitorFromWindow(window.Hwnd, MONITOR.MONITOR_DEFAULTTONEAREST);
                RECT other = WorkAreaOfAnotherMonitor(first);

                _ = SetWindowPos(
                    window.Hwnd,
                    HWND.NULL,
                    other.left + 20,
                    other.top + 20,
                    0,
                    0,
                    SWP.SWP_NOSIZE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE
                );

                HMONITOR second = MonitorFromWindow(window.Hwnd, MONITOR.MONITOR_DEFAULTTONEAREST);
                Assert.NotEqual(first, second);
                Assert.Contains(second, read);
            }
            finally
            {
                Win32Window.TextRenderingReader = previous;
            }
        });

    // The work area of the first monitor that is not the given one.
    private static RECT WorkAreaOfAnotherMonitor(HMONITOR except)
    {
        var found = new List<RECT>();
        var handle = GCHandle.Alloc(found);
        try
        {
            var state = new EnumState { Except = except, Found = GCHandle.ToIntPtr(handle) };
            _ = EnumDisplayMonitors(HDC.NULL, null, &Collect, (LPARAM)(nint)(&state));
        }
        finally
        {
            handle.Free();
        }

        Assert.NotEmpty(found);
        return found[0];
    }

    [UnmanagedCallersOnly]
    private static BOOL Collect(HMONITOR monitor, HDC dc, RECT* bounds, LPARAM data)
    {
        var state = (EnumState*)(nint)data;
        if (monitor != state->Except)
        {
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            if (GetMonitorInfoW(monitor, &info) != BOOL.FALSE)
            {
                var found = (List<RECT>)GCHandle.FromIntPtr(state->Found).Target!;
                found.Add(info.rcWork);
            }
        }

        return BOOL.TRUE;
    }

    private struct EnumState
    {
        public HMONITOR Except;
        public nint Found;
    }
}
