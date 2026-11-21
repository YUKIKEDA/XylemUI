using System.Diagnostics;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// The resizes of `wm-size` (design §6.0, "What is measured"; Issue #933): each changes the size of the host's window with
// SetWindowPos, so that it takes the path of an app's window in a resize drag: the WM_SIZE that SetWindowPos sends before it
// returns, the host's synchronous render (Win32PlatformWindow resizes the renderer's swap chain, then calls the window core's
// Resize), the frame, and its Present. `frames --resizes` calls the window core's Resize directly, without the message and the
// swap chain's resize. The window's size alternates between its size when the scene was made and 16 physical pixels more each way.
//
// Each resize is timed from the call to the WM_SIZE the window procedure gets (a message hook set for the call alone), and from
// there to the start of the Draw and to the end of the Present that the scene's TimingTarget records. Each is checked: the call
// succeeded, one WM_SIZE came, the frame presented once, and the renderer has the new client size; a resize that did not throws.
internal sealed unsafe class HostResize
{
    private const int Step = 16;

    private readonly Scene _scene;
    private readonly TreeScene.TimingTarget _target;
    private readonly Win32Window _window;
    private readonly Func<Win32Window, uint, bool> _hook;
    private readonly int _width;
    private readonly int _height;
    private readonly int _clientWidth;
    private readonly int _clientHeight;
    private readonly List<double> _calls;
    private readonly List<double> _toWmSize;
    private readonly List<double> _wmSizeToDraw;
    private readonly List<double> _draws;
    private readonly List<double> _presents;
    private readonly List<double> _wmSizeToPresent;
    private long _wmSizeAt;
    private int _wmSizes;
    private int _count;

    public HostResize(Scene scene, TreeScene.TimingTarget target, int capacity)
    {
        _scene = scene;
        _target = target;
        _window = scene.Window;
        _hook = (_, message) => OnMessage(message);
        RECT rect;
        if (GetWindowRect((HWND)_window.Handle, &rect) == BOOL.FALSE)
        {
            throw new InvalidOperationException($"GetWindowRect failed for the window 0x{_window.Handle:X} (error {GetLastError()}).");
        }

        _width = rect.right - rect.left;
        _height = rect.bottom - rect.top;
        (_clientWidth, _clientHeight) = _window.ClientSize;
        _calls = new List<double>(capacity);
        _toWmSize = new List<double>(capacity);
        _wmSizeToDraw = new List<double>(capacity);
        _draws = new List<double>(capacity);
        _presents = new List<double>(capacity);
        _wmSizeToPresent = new List<double>(capacity);
    }

    // Resizes the window once, to the other of its two sizes, and records the times.
    public void Resize()
    {
        int step = (_count & 1) == 0 ? Step : 0;
        int width = _clientWidth + step;
        int height = _clientHeight + step;
        int presents = _target.Presents;
        int sizes = _wmSizes;
        _wmSizeAt = 0;
        _window.MessageHook = _hook;
        long start = Stopwatch.GetTimestamp();
        BOOL resized = SetWindowPos(
            (HWND)_window.Handle,
            HWND.NULL,
            0,
            0,
            _width + step,
            _height + step,
            SWP.SWP_NOMOVE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE
        );
        long end = Stopwatch.GetTimestamp();
        _window.MessageHook = null;
        _count++;
        (int Width, int Height) renderer = (_scene.Renderer.Width, _scene.Renderer.Height);
        if (resized == BOOL.FALSE || _wmSizes != sizes + 1 || _target.Presents != presents + 1 || renderer != (width, height))
        {
            throw new InvalidOperationException(
                $"Resize {_count} of the host's window to a client area of {width} x {height} did not take the WM_SIZE path once: "
                    + $"SetWindowPos returned {(int)resized}, {_wmSizes - sizes} WM_SIZE, {_target.Presents - presents} presents, "
                    + $"a renderer of {renderer.Width} x {renderer.Height}."
            );
        }

        _calls.Add(Milliseconds(start, end));
        _toWmSize.Add(Milliseconds(start, _wmSizeAt));
        _wmSizeToDraw.Add(Milliseconds(_wmSizeAt, _target.DrawStart));
        _draws.Add(Milliseconds(_target.DrawStart, _target.DrawEnd));
        _presents.Add(Milliseconds(_target.DrawEnd, _target.PresentEnd));
        _wmSizeToPresent.Add(Milliseconds(_wmSizeAt, _target.PresentEnd));
    }

    // The resizes done and the WM_SIZE messages they gave (the proof that each took the path), the sizes the window's guard put
    // off, and the series.
    public void Write(Report report)
    {
        report.Number("wm_resizes", _count);
        report.Number("wm_sizes", _wmSizes);
        report.Number("dropped_nested_resizes", _window.DroppedNestedResizes);
        report.Series("wm_size_to_present_ms", _wmSizeToPresent);
        report.Series("set_window_pos_ms", _calls);
        report.Series("to_wm_size_ms", _toWmSize);
        report.Series("wm_size_to_draw_ms", _wmSizeToDraw);
        report.Series("resize_draw_ms", _draws);
        report.Series("resize_present_ms", _presents);
    }

    private static double Milliseconds(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;

    // Sees each message before the window handles it, and lets it go on.
    private bool OnMessage(uint message)
    {
        if (message == WM.WM_SIZE)
        {
            _wmSizes++;
            if (_wmSizeAt == 0)
            {
                _wmSizeAt = Stopwatch.GetTimestamp();
            }
        }

        return false;
    }
}
