using System.Globalization;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// The monitor the scenes' windows open on (roadmap row R3, Issue #184): `--monitor N` takes \\.\DISPLAYN; without it, the monitor
// with the highest refresh rate, the primary one among equals. Windows otherwise opens a window wherever it last was, and a
// measuring machine can have monitors with different refresh rates, so each report names the monitor and its refresh rate.
internal static unsafe class MonitorChoice
{
    // ENUM_CURRENT_SETTINGS (winuser.h).
    private const uint EnumCurrentSettings = unchecked((uint)-1);

    private static Choice? _choice;

    public static string Device => Get().Device;

    public static int RefreshHz => Get().RefreshHz;

    // Reads `--monitor N` from the process's arguments once.
    public static void Configure(string[] args)
    {
        int at = Array.IndexOf(args, "--monitor");
        int? number = at >= 0 && at + 1 < args.Length ? int.Parse(args[at + 1], CultureInfo.InvariantCulture) : null;
        _choice = Choose(number);
    }

    // Moves a window to 40 x 40 pixels inside the chosen monitor's work area, keeping its size. Called before the window is shown,
    // it costs no repaint: moving the shown window took about 3 ms and made the startup scene's launch to display 5 to 8 ms later.
    public static void Place(nint window)
    {
        Choice choice = Get();
        if (
            SetWindowPos(
                (HWND)window,
                HWND.NULL,
                choice.WorkLeft + 40,
                choice.WorkTop + 40,
                0,
                0,
                SWP.SWP_NOSIZE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE
            ) == BOOL.FALSE
        )
        {
            throw new InvalidOperationException($"SetWindowPos did not move the window (error {GetLastError()}).");
        }
    }

    // Keeps a shown window above the windows that are not topmost. A covered window is composed by DWM instead of flipped on an
    // overlay plane, and then shows each frame about a refresh later and misses frames (Issue #184, docs/benchmarks/r3-latency.md).
    // On a hidden window, SetWindowPos returned success but the window did not become topmost.
    public static void KeepOnTop(nint window)
    {
        BOOL raised = SetWindowPos((HWND)window, HWND.HWND_TOPMOST, 0, 0, 0, 0, SWP.SWP_NOMOVE | SWP.SWP_NOSIZE | SWP.SWP_NOACTIVATE);
        if (raised == BOOL.FALSE || !IsTopmost(window))
        {
            throw new InvalidOperationException($"SetWindowPos did not make the window topmost (returned {(int)raised}, error {GetLastError()}).");
        }
    }

    // Whether a window is still topmost, for the report.
    public static bool IsTopmost(nint window) => (GetWindowLongW((HWND)window, GWL.GWL_EXSTYLE) & WS.WS_EX_TOPMOST) != 0;

    private static Choice Get() => _choice ??= Choose(null);

    private static Choice Choose(int? number)
    {
        var monitors = new List<Choice>();
        GCHandle handle = GCHandle.Alloc(monitors);
        try
        {
            _ = EnumDisplayMonitors(HDC.NULL, null, &Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        if (number is int n)
        {
            string device = @"\\.\DISPLAY" + n.ToString(CultureInfo.InvariantCulture);
            return monitors.FirstOrDefault(m => m.Device == device)
                ?? throw new ArgumentException($"No monitor is {device}; the monitors are {string.Join(", ", monitors.Select(m => m.Device))}.");
        }

        return monitors.OrderByDescending(m => m.RefreshHz).ThenByDescending(m => m.Primary).First();
    }

    [UnmanagedCallersOnly]
    private static BOOL Collect(HMONITOR monitor, HDC context, RECT* bounds, LPARAM state)
    {
        MONITORINFOEXW info = default;
        info.Base.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (GetMonitorInfoW(monitor, (MONITORINFO*)&info))
        {
            string device = new((char*)&info.szDevice);
            var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
            int refresh = 0;
            fixed (char* name = device)
            {
                if (EnumDisplaySettingsW(name, EnumCurrentSettings, &mode))
                {
                    refresh = (int)mode.dmDisplayFrequency;
                }
            }

            var list = (List<Choice>)GCHandle.FromIntPtr(state).Target!;
            list.Add(new Choice(device, refresh, (info.Base.dwFlags & MONITORINFOF_PRIMARY) != 0, info.Base.rcWork.left, info.Base.rcWork.top));
        }

        return true;
    }

    private sealed record Choice(string Device, int RefreshHz, bool Primary, int WorkLeft, int WorkTop);
}
