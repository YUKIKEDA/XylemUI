using System.Runtime.InteropServices;
using static OsAppearance.Native;
using static OsAppearance.Report;

namespace OsAppearance;

/// <summary>
/// The OS appearance probe of row TH1 step 1 (Issue #888, design §13.2).
/// <para>
/// <c>OsAppearance --once [--titlebar dark|light] [--attribute 20|19|both]</c> prints UISettings, the light/dark decision,
/// high contrast, and the title bar's DwmSetWindowAttribute results, and exits.
/// </para>
/// <para>
/// <c>OsAppearance [--seconds N] [--titlebar dark|light] [--attribute 20|19|both]</c> also shows a window and logs every
/// WM_SETTINGCHANGE, WM_SYSCOLORCHANGE, WM_THEMECHANGED, WM_DWMCOLORIZATIONCOLORCHANGED and UISettings.ColorValuesChanged,
/// with what changed right after and one second after; it exits after N seconds (default 60) or when the window is closed.
/// </para>
/// </summary>
internal static unsafe class Program
{
    internal const uint WmColorValuesChanged = WmApp + 1;

    private const nuint ExitTimer = 1;
    private const nuint RecheckTimer = 2;
    private const uint RecheckDelayMilliseconds = 1000;

    private static List<(string Name, string Value)> _baseline = [];
    private static int _events;
    private static int _recheckOf;

    /// <summary>The probe's window, read by the ColorValuesChanged callback to post to the UI thread.</summary>
    internal static nint Window { get; private set; }

    internal static uint UiThread { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        if (!Options.TryParse(args, out Options options))
        {
            Console.Error.WriteLine("usage: OsAppearance [--once] [--seconds N] [--titlebar dark|light] [--attribute 20|19|both]");
            return 2;
        }

        try
        {
            Run(options);
            return 0;
        }
        catch (Exception exception)
        {
            Failed("run", exception);
            return 1;
        }
    }

    private static void Run(Options options)
    {
        UiThread = GetCurrentThreadId();
        Probe("mode", options.Once ? "once" : $"watch {Text(options.Seconds)} s");
        Probe("os.version", Environment.OSVersion.VersionString);
        const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        Probe("os.display_version", RegistryValues.ReadString(HkeyLocalMachine, CurrentVersion, "DisplayVersion"));
        Probe("os.ubr", RegistryValues.ReadDword(HkeyLocalMachine, CurrentVersion, "UBR"));
        Probe("runtime.framework", RuntimeInformation.FrameworkDescription);
        long jitted = System.Runtime.JitInfo.GetCompiledMethodCount();
        Probe("runtime.native_aot", jitted == 0 ? "yes (0 methods compiled by a JIT)" : $"no ({jitted} methods compiled by a JIT)");

        Probe("com.ole_initialize", Hr(OleInitialize(null)));
        Probe("ui.thread", $"id={Text(UiThread)} apartment={ColorHandler.Apartment()}");

        // (a) UISettings, then (a)-(c) once.
        UISettingsProbe.Activate();
        _baseline = Appearance.Read();
        Appearance.Print(_baseline);

        // (e) The title bar, on a window made for it (hidden in --once mode).
        Window = CreateProbeWindow();
        Probe("window.handle", Hex((uint)Window));
        TitleBar.Apply(Window, options);

        // (d) ColorValuesChanged: in --once mode only whether it can be added and removed.
        UISettingsProbe.Subscribe();
        if (options.Once)
        {
            UISettingsProbe.Close();
            _ = DestroyWindow(Window);
            OleUninitialize();
            Probe("done", Stamp());
            return;
        }

        _ = ShowWindow(Window, SwShowNormal);
        _ = SetTimer(Window, ExitTimer, (uint)options.Seconds * 1000, 0);
        Probe("watch.started", $"{Stamp()}; change the OS settings now; the probe exits in {Text(options.Seconds)} s or when its window is closed");

        Msg message;
        while (GetMessageW(&message, 0, 0, 0) > 0)
        {
            _ = TranslateMessage(&message);
            _ = DispatchMessageW(&message);
        }

        UISettingsProbe.Close();
        OleUninitialize();
        Probe("watch.ended", $"{Stamp()} events={Text(_events)}");
    }

    private static nint CreateProbeWindow()
    {
        nint instance = GetModuleHandleW(null);
        fixed (char* className = "XylemUI.Th1.OsAppearanceProbe")
        fixed (char* title = "TH1 OS appearance probe")
        {
            WndClassExW windowClass = default;
            windowClass.Size = (uint)sizeof(WndClassExW);
            windowClass.WndProc = &WndProc;
            windowClass.Instance = instance;
            windowClass.Cursor = LoadCursorW(0, 32512); // IDC_ARROW
            windowClass.Background = 5 + 1; // COLOR_WINDOW + 1: the client area follows the system colour.
            windowClass.ClassName = className;
            if (RegisterClassExW(&windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassExW failed.");
            }

            nint window = CreateWindowExW(0, className, title, WsOverlappedWindow, CwUseDefault, CwUseDefault, 640, 360, 0, 0, instance, null);
            if (window == 0)
            {
                throw new InvalidOperationException("CreateWindowExW failed.");
            }

            return window;
        }
    }

    // A callback catches every exception at its entry (.cursor/rules/interop.mdc).
    [UnmanagedCallersOnly]
    private static nint WndProc(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case WmSettingChange:
                    OnEvent($"WM_SETTINGCHANGE wParam={SettingWParam(wParam)} lParam={SettingLParam(lParam)}");
                    break;
                case WmSysColorChange:
                    OnEvent("WM_SYSCOLORCHANGE");
                    break;
                case WmThemeChanged:
                    OnEvent("WM_THEMECHANGED");
                    break;
                case WmDwmColorizationColorChanged:
                    OnEvent($"WM_DWMCOLORIZATIONCOLORCHANGED color={Hex((uint)wParam)} opaque_blend={lParam != 0}");
                    break;
                case WmColorValuesChanged:
                    OnEvent($"WM_APP+1 posted by ColorValuesChanged #{Text((int)wParam)}");
                    return 0;
                case WmTimer when (nuint)wParam == ExitTimer:
                    _ = KillTimer(window, ExitTimer);
                    _ = DestroyWindow(window);
                    return 0;
                case WmTimer when (nuint)wParam == RecheckTimer:
                    _ = KillTimer(window, RecheckTimer);
                    Recheck();
                    return 0;
                case WmDestroy:
                    PostQuitMessage(0);
                    return 0;
            }
        }
        catch (Exception exception)
        {
            Failed("wndproc", exception);
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    /// <summary>Logs one message, re-reads (a)-(c) at once and logs what changed, and re-reads once more a second later.</summary>
    private static void OnEvent(string what)
    {
        int n = ++_events;
        Probe($"event.{Text(n)}", $"{Stamp()} {what}");
        List<(string Name, string Value)> now = Appearance.Read();
        Probe($"event.{Text(n)}.changed", Appearance.Diff(_baseline, now));
        _baseline = now;
        _recheckOf = n;
        _ = SetTimer(Window, RecheckTimer, RecheckDelayMilliseconds, 0);
    }

    /// <summary>What changed in the second after the last event, values that the OS updated late.</summary>
    private static void Recheck()
    {
        List<(string Name, string Value)> now = Appearance.Read();
        Probe($"event.{Text(_recheckOf)}.changed_1s_later", $"{Stamp()} {Appearance.Diff(_baseline, now)}");
        _baseline = now;
    }

    private static string SettingWParam(nint wParam) =>
        (uint)wParam switch
        {
            0 => "0",
            SpiSetHighContrast => Hex((uint)wParam) + " (SPI_SETHIGHCONTRAST)",
            _ => Hex((uint)wParam),
        };

    // lParam of WM_SETTINGCHANGE is a string naming the area that changed, or null (winuser.h, WM_SETTINGCHANGE docs).
    private static string SettingLParam(nint lParam) => lParam == 0 ? "(null)" : $"\"{new string((char*)lParam)}\"";
}

/// <summary>The command line.</summary>
internal readonly record struct Options(bool Once, int Seconds, bool Dark, uint[] Attributes)
{
    public static bool TryParse(string[] args, out Options options)
    {
        bool once = false;
        int seconds = 60;
        bool dark = false;
        uint[] attributes = [20, 19];
        options = default;
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : string.Empty;
            switch (args[i])
            {
                case "--once":
                    once = true;
                    break;
                case "--seconds" when int.TryParse(next, out int value) && value > 0:
                    seconds = value;
                    i++;
                    break;
                case "--titlebar" when next is "dark" or "light":
                    dark = next == "dark";
                    i++;
                    break;
                case "--attribute" when next is "20" or "19" or "both":
                    attributes = next switch
                    {
                        "20" => [20],
                        "19" => [19],
                        _ => [20, 19],
                    };
                    i++;
                    break;
                default:
                    return false;
            }
        }

        options = new Options(once, seconds, dark, attributes);
        return true;
    }
}

/// <summary>(e) DWMWA_USE_IMMERSIVE_DARK_MODE: 20 in dwmapi.h of the Windows SDK; 19 is said to be the value before Windows 10 20H1.</summary>
internal static unsafe class TitleBar
{
    public static void Apply(nint window, Options options)
    {
        int value = options.Dark ? 1 : 0;
        string valueName = options.Dark ? "TRUE (dark)" : "FALSE (light)";
        foreach (uint attribute in options.Attributes)
        {
            int set = value;
            int hr = DwmSetWindowAttribute(window, attribute, &set, sizeof(int));
            Probe($"titlebar.set_{Text(attribute)}", $"{valueName} {Hr(hr)}");

            // dwmapi.h marks attribute 20 "[set]"; reading it back shows whether the OS keeps a readable state.
            int read = -1;
            int hrGet = DwmGetWindowAttribute(window, attribute, &read, sizeof(int));
            Probe($"titlebar.get_{Text(attribute)}", hrGet < 0 ? Hr(hrGet) : $"{Hr(hrGet)} value={Text(read)}");
        }
    }
}
