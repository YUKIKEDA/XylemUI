using System.Diagnostics;
using System.Globalization;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_DRIVER_TYPE;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// Input to display (design §11.3, "Input-to-photon の測り方"; roadmap row R3, Issue #673): the space key, injected into the OS
// with SendInput, to the compositor's present of the first desktop image whose marker shows the color the key switched it to
// (Desktop Duplication of the chosen monitor's output, DXGI_OUTDUPL_FRAME_INFO.LastPresentTime). The app is any of the
// comparison apps (benchmarks/compare, and XylemUI.E2E's own `app`): each switches its marker between #3366CC and #CC6633 on the
// space key. PresentMon traces the app in the same run, for its input-to-display columns and its present modes.
//   input-latency --presentmon <exe> --app <exe> [--app-args <args>] [--name <label>] [--static] [--covered]
//                 [--presses N] [--runs N]
//     --static   the app draws once and stays idle (otherwise it animates its 1,000 rectangles)
//     --covered  XylemUI.E2E's animated app, topmost, covers the app window's lower right quarter, away from the marker
// The keys reach only the app: the app window is made the foreground window with one click on it, after the window under the
// click point is read and found to be the app's, and before each key the foreground window is read again; a run stops with an
// exception when either is another window. The cursor goes back to where it was after the click.
internal static unsafe class InputLatency
{
    // The marker's two colors, as 0x00RRGGBB (Scene.Marker and Scene.PressedMarker).
    private const uint Marker = 0x003366CC;
    private const uint PressedMarker = 0x00CC6633;

    // winuser.h.
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;
    private const uint AncestorRoot = 2;
    private const uint PeekRemove = 1;

    // A key whose change does not reach the screen in 1 s is counted as lost.
    private static readonly TimeSpan Lost = TimeSpan.FromSeconds(1);

    public static int Run(string[] args)
    {
        string presentMon = External.Option(args, "--presentmon") ?? throw new ArgumentException("--presentmon is required.");
        string app = External.Option(args, "--app") ?? throw new ArgumentException("--app is required.");
        string name = External.Option(args, "--name") ?? Path.GetFileNameWithoutExtension(app);
        string prefix = External.Option(args, "--app-args") ?? string.Empty;
        bool isStatic = args.Contains("--static");
        bool covered = args.Contains("--covered");
        int presses = int.Parse(External.Option(args, "--presses") ?? "200", CultureInfo.InvariantCulture);
        int runs = int.Parse(External.Option(args, "--runs") ?? "1", CultureInfo.InvariantCulture);
        string appArgs = (prefix + (isStatic ? " --static" : string.Empty)).Trim();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063))
        {
            _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }

        string work = Path.Combine(Path.GetTempPath(), "xylemui-e2e-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        _ = Directory.CreateDirectory(work);
        var toDisplay = new List<double>();
        var presentMonInput = new List<double>();
        var modes = new Dictionary<string, int>();
        int lost = 0;

        // The waits between keys sleep in steps of 1 ms: at the default timer resolution (15.6 ms) the random part of the interval
        // would be rounded away.
        _ = timeBeginPeriod(1);
        try
        {
            for (int run = 0; run < runs; run++)
            {
                string csv = Path.Combine(work, $"input{run}.csv");
                lost += MeasureRun(presentMon, app, appArgs, covered, presses, seed: run, csv, toDisplay, presentMonInput, modes);
            }
        }
        catch (InvalidOperationException stopped)
        {
            // Caught here, so that the run's finally ends the app and the covering app: an exception no frame catches ended the
            // process without them, and left the app running after the foreground check stopped a run (Issue #673).
            Console.Error.WriteLine(stopped.Message);
            return 1;
        }
        finally
        {
            _ = timeEndPeriod(1);
            Directory.Delete(work, recursive: true);
        }

        using var report = new Report("input-latency");
        report.Text("name", name);
        report.Text("app_args", appArgs);
        report.Text("scene", isStatic ? "static" : "animated");
        report.Number("covered", covered ? 1 : 0);
        report.Number("presses", presses * runs);
        report.Number("lost", lost);
        report.Series("input_to_display_ms", toDisplay);
        report.Series("presentmon_input_to_photon_ms", presentMonInput);
        report.Text("present_modes", string.Join("; ", modes.OrderByDescending(m => m.Value).Select(m => $"{m.Key}={m.Value}")));
        return 0;
    }

    // One launch of the app: `presses` keys, and the app's presents traced by PresentMon over them. Returns the lost keys.
    private static int MeasureRun(
        string presentMon,
        string app,
        string appArgs,
        bool covered,
        int presses,
        int seed,
        string csv,
        List<double> toDisplay,
        List<double> presentMonInput,
        Dictionary<string, int> modes
    )
    {
        using Process process = Process.Start(new ProcessStartInfo(app, appArgs) { UseShellExecute = false })!;
        Process? coverProcess = null;
        Process? monitor = null;
        string session = $"XylemUIInput{Environment.ProcessId}x{seed}";
        HWND cover = HWND.NULL;
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        IDXGIOutputDuplication* duplication = null;
        ID3D11Texture2D* pixel = null;
        try
        {
            ScreenWatch.BringToFront(process);
            var window = (HWND)process.MainWindowHandle;
            MonitorChoice.Place(window);
            MonitorChoice.KeepOnTop(window);
            Pump(TimeSpan.FromSeconds(1));
            if (covered)
            {
                coverProcess = Cover(window, out cover);
            }

            Activate(window, cover);
            if (coverProcess is not null)
            {
                RaiseCover(window, cover);
            }

            D3D_FEATURE_LEVEL level = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0;
            ThrowIfFailed(
                D3D11CreateDevice(null, D3D_DRIVER_TYPE_HARDWARE, HMODULE.NULL, 0, &level, 1, D3D11.D3D11_SDK_VERSION, &device, null, &context)
            );
            duplication = DisplayWatch.Duplicate(device, MonitorChoice.Device, out RECT output);
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = 1,
                Height = 1,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
                Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
                CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            };
            ThrowIfFailed(device->CreateTexture2D(&description, null, &pixel));
            var point = new POINT { x = 20, y = 20 };
            _ = ClientToScreen(window, &point);
            point.x -= output.left;
            point.y -= output.top;

            // The trace covers every key: the wait between keys (at most 117 ms) and the wait for the display (up to about 50 ms
            // in the slowest scene). 0.13 s a key cut the slow runs short (PR #674's review). PresentMon's --terminate_on_proc_exit
            // would end it with the app, but then it wrote no file.
            int seconds = 5 + (int)Math.Ceiling(presses * 0.2);
            monitor = External.StartPresentMon(presentMon, csv, seconds, process.Id, session);
            uint shown = WaitForMarker(duplication, context, pixel, point, Marker, PressedMarker, out _);
            var random = new Random(seed);
            int lost = 0;
            for (int i = 0; i < presses; i++)
            {
                if (GetForegroundWindow() != window)
                {
                    throw new InvalidOperationException(
                        $"The foreground window changed to 0x{(nint)GetForegroundWindow():X} after {i} keys; no key was sent to it."
                    );
                }

                uint expected = shown == Marker ? PressedMarker : Marker;
                long sent = Stopwatch.GetTimestamp();
                Press();
                if (WaitForMarker(duplication, context, pixel, point, expected, expected, out long presentQpc) == expected)
                {
                    toDisplay.Add(Stopwatch.GetElapsedTime(sent, presentQpc).TotalMilliseconds);
                    shown = expected;
                }
                else
                {
                    lost++;
                    shown = WaitForMarker(duplication, context, pixel, point, Marker, PressedMarker, out _);
                }

                // 100 ms and a random part of a refresh, so that the keys fall evenly over the refresh.
                Pump(TimeSpan.FromMilliseconds(100 + (random.NextDouble() * 16.7)));
            }

            External.Kill(process);
            monitor.WaitForExit();
            foreach (Dictionary<string, string> row in External.Rows(csv, process.Id))
            {
                modes[row["PresentMode"]] = modes.GetValueOrDefault(row["PresentMode"]) + 1;
                if (row.TryGetValue("MsAllInputToPhotonLatency", out string? cell))
                {
                    External.Add(presentMonInput, cell);
                }
            }

            return lost;
        }
        finally
        {
            External.Kill(process);

            // A run that stopped (the foreground check) ends its trace too, which would otherwise hold its file open until its
            // length passes, and the work directory could not be removed (PR #674's review).
            if (monitor is not null)
            {
                External.StopPresentMon(presentMon, session, monitor);
                monitor.Dispose();
            }

            if (coverProcess is not null)
            {
                External.Kill(coverProcess);
                coverProcess.Dispose();
            }

            if (pixel is not null)
            {
                _ = pixel->Release();
            }

            if (duplication is not null)
            {
                _ = duplication->Release();
            }

            if (context is not null)
            {
                _ = context->Release();
            }

            if (device is not null)
            {
                _ = device->Release();
            }
        }
    }

    // Reads the marker's pixel in the desktop images until it is one of the two colors, or `Lost` passes; returns the color read
    // last, with the present time of the image that showed it.
    private static uint WaitForMarker(
        IDXGIOutputDuplication* duplication,
        ID3D11DeviceContext* context,
        ID3D11Texture2D* pixel,
        POINT point,
        uint first,
        uint second,
        out long presentQpc
    )
    {
        presentQpc = 0;
        uint color = 0;
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < Lost)
        {
            DXGI_OUTDUPL_FRAME_INFO info;
            IDXGIResource* resource;
            HRESULT result = duplication->AcquireNextFrame(2, &info, &resource);
            if (result == DXGI.DXGI_ERROR_WAIT_TIMEOUT)
            {
                continue;
            }

            ThrowIfFailed(result);
            try
            {
                // An image that only moved the pointer has no present time.
                if (info.LastPresentTime.QuadPart == 0)
                {
                    continue;
                }

                ID3D11Texture2D* image;
                ThrowIfFailed(resource->QueryInterface(__uuidof<ID3D11Texture2D>(), (void**)&image));
                var box = new D3D11_BOX(point.x, point.y, 0, point.x + 1, point.y + 1, 1);
                context->CopySubresourceRegion((ID3D11Resource*)pixel, 0, 0, 0, 0, (ID3D11Resource*)image, 0, &box);
                _ = image->Release();
                D3D11_MAPPED_SUBRESOURCE mapped;
                ThrowIfFailed(context->Map((ID3D11Resource*)pixel, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped));
                color = *(uint*)mapped.pData & 0x00FFFFFF;
                context->Unmap((ID3D11Resource*)pixel, 0);
                if (color == first || color == second)
                {
                    presentQpc = info.LastPresentTime.QuadPart;
                    return color;
                }
            }
            finally
            {
                _ = resource->Release();
                _ = duplication->ReleaseFrame();
            }
        }

        return color;
    }

    // A space key down and up, injected into the OS input queue.
    private static void Press()
    {
        INPUT* keys = stackalloc INPUT[2];
        keys[0] = default;
        keys[0].type = InputKeyboard;
        keys[0].ki.wVk = VK.VK_SPACE;
        keys[0].ki.wScan = (ushort)MapVirtualKeyW(VK.VK_SPACE, MAPVK_VK_TO_VSC);
        keys[1] = keys[0];
        keys[1].ki.dwFlags = KEYEVENTF_KEYUP;
        if (SendInput(2, keys, sizeof(INPUT)) != 2)
        {
            throw new InvalidOperationException($"SendInput did not send the key (error {GetLastError()}).");
        }
    }

    // Makes the app window the foreground window with one click a quarter of the way into its client area, where no marker and
    // no cover is. The window under that point is read first: a click goes nowhere else.
    private static void Activate(HWND window, HWND cover)
    {
        RECT client;
        _ = GetClientRect(window, &client);
        var target = new POINT { x = client.right / 4, y = client.bottom / 2 };
        _ = ClientToScreen(window, &target);
        HWND under = WindowFromPoint(target);
        if (GetAncestor(under, AncestorRoot) != window || under == cover)
        {
            throw new InvalidOperationException($"The window under ({target.x}, {target.y}) is 0x{(nint)under:X}, not the app's; no click was sent.");
        }

        POINT before;
        _ = GetCursorPos(&before);
        int left = GetSystemMetrics(SM.SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM.SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM.SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM.SM_CYVIRTUALSCREEN);
        INPUT* clicks = stackalloc INPUT[3];
        clicks[0] = default;
        clicks[0].type = InputMouse;
        clicks[0].mi.dx = (int)(((target.x - left) * 65535L) / (width - 1));
        clicks[0].mi.dy = (int)(((target.y - top) * 65535L) / (height - 1));
        clicks[0].mi.dwFlags = MouseMove | MouseAbsolute | MouseVirtualDesk;
        clicks[1] = clicks[0];
        clicks[1].mi.dwFlags |= MouseLeftDown;
        clicks[2] = clicks[0];
        clicks[2].mi.dwFlags |= MouseLeftUp;
        if (SendInput(3, clicks, sizeof(INPUT)) != 3)
        {
            throw new InvalidOperationException($"SendInput did not send the click (error {GetLastError()}).");
        }

        long start = Stopwatch.GetTimestamp();
        while (GetForegroundWindow() != window && Stopwatch.GetElapsedTime(start).TotalSeconds < 2)
        {
            Pump(TimeSpan.FromMilliseconds(10));
        }

        _ = SetCursorPos(before.x, before.y);
        if (GetForegroundWindow() != window)
        {
            throw new InvalidOperationException("The click did not make the app window the foreground window.");
        }

        // The app's own handling of the click (a focus change, a repaint) settles before the first key.
        Pump(TimeSpan.FromMilliseconds(500));
    }

    // Another window over the lower right quarter of the app window's client area: XylemUI.E2E's animated app, which presents
    // every frame through a flip-model swap chain, made topmost and moved there without taking the activation.
    private static Process Cover(HWND window, out HWND coverWindow)
    {
        RECT client;
        _ = GetClientRect(window, &client);
        var corner = new POINT { x = client.right / 2, y = client.bottom / 2 };
        _ = ClientToScreen(window, &corner);
        Process cover = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "app") { UseShellExecute = false })!;
        ScreenWatch.BringToFront(cover);

        // The covering app places itself on the chosen monitor when it starts; it is moved once it has.
        Pump(TimeSpan.FromMilliseconds(500));
        coverWindow = (HWND)cover.MainWindowHandle;
        if (SetWindowPos(coverWindow, HWND.HWND_TOPMOST, corner.x, corner.y, client.right / 2, client.bottom / 2, SWP.SWP_NOACTIVATE) == BOOL.FALSE)
        {
            throw new InvalidOperationException($"The covering window was not moved (error {GetLastError()}).");
        }

        Pump(TimeSpan.FromMilliseconds(500));
        return cover;
    }

    // Puts the covering window back over the app's: the click that made the app the foreground window also raised it to the top
    // of the topmost windows, under which the covering window would have stayed (PR #674's review found the scene uncovered).
    // The window under the covering window's middle is read, so that the scene is known to be covered, and the app must still be
    // the foreground window.
    private static void RaiseCover(HWND window, HWND cover)
    {
        if (SetWindowPos(cover, HWND.HWND_TOPMOST, 0, 0, 0, 0, SWP.SWP_NOMOVE | SWP.SWP_NOSIZE | SWP.SWP_NOACTIVATE) == BOOL.FALSE)
        {
            throw new InvalidOperationException($"The covering window was not raised (error {GetLastError()}).");
        }

        Pump(TimeSpan.FromMilliseconds(300));
        RECT bounds;
        _ = GetWindowRect(cover, &bounds);
        var middle = new POINT { x = (bounds.left + bounds.right) / 2, y = (bounds.top + bounds.bottom) / 2 };
        HWND under = GetAncestor(WindowFromPoint(middle), AncestorRoot);
        if (under != cover)
        {
            throw new InvalidOperationException($"The window over the covered corner is 0x{(nint)under:X}, not the covering window.");
        }

        if (GetForegroundWindow() != window)
        {
            throw new InvalidOperationException("Raising the covering window took the foreground from the app.");
        }
    }

    // Waits while handling this thread's messages, so that the covering window paints and answers.
    private static void Pump(TimeSpan time)
    {
        long start = Stopwatch.GetTimestamp();
        MSG message;
        while (Stopwatch.GetElapsedTime(start) < time)
        {
            while (PeekMessageW(&message, HWND.NULL, 0, 0, PeekRemove))
            {
                _ = TranslateMessage(&message);
                _ = DispatchMessageW(&message);
            }

            Thread.Sleep(1);
        }
    }

    private static void ThrowIfFailed(HRESULT result)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"Desktop Duplication failed: 0x{(uint)result:X8}.");
        }
    }
}
