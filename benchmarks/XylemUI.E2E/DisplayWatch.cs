using System.Diagnostics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_DRIVER_TYPE;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// Startup to the screen (design §11.3, the startup target; M1a review): the desktop image the compositor presents to the display
// (Desktop Duplication, IDXGIOutputDuplication), read at the scene's marker in the app's window, with the time the compositor
// presented that image (DXGI_OUTDUPL_FRAME_INFO.LastPresentTime, the performance counter). The window must be on the primary
// output and not covered: measure.ps1 minimizes every other window first. The process is made per-monitor DPI aware so that
// the window's client position and the desktop image use the same pixels.
internal static unsafe class DisplayWatch
{
    // Launches the app and returns the milliseconds from just before the launch to the compositor's present of the first desktop
    // image that shows the marker, or null after 10 s.
    public static double? LaunchToMarkerOnScreen(string app, string arguments, out Process process) =>
        LaunchToMarkerOnScreen(app, arguments, out process, out _);

    // The same, also giving the performance counter of that present. The marker is read at (x, y) of the client area, in physical
    // pixels: (20, 20), inside the scene's marker, unless another app's marker is elsewhere (app-startup).
    public static double? LaunchToMarkerOnScreen(string app, string arguments, out Process process, out long presentQpc, int x = 20, int y = 20)
    {
        presentQpc = 0;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063))
        {
            _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }

        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        IDXGIOutputDuplication* duplication = null;
        ID3D11Texture2D* pixel = null;
        try
        {
            D3D_FEATURE_LEVEL level = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0;
            ThrowIfFailed(
                D3D11CreateDevice(null, D3D_DRIVER_TYPE_HARDWARE, HMODULE.NULL, 0, &level, 1, D3D11.D3D11_SDK_VERSION, &device, null, &context)
            );
            duplication = Duplicate(device, out RECT output);
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

            long launched = Stopwatch.GetTimestamp();

            // The app's standard output is read and dropped, so that a child that prints (startup-child) does not mix its lines
            // into this process's report.
            process = Process.Start(new ProcessStartInfo(app, arguments) { UseShellExecute = false, RedirectStandardOutput = true })!;
            process.OutputDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            try
            {
                HWND window = HWND.NULL;
                while (Stopwatch.GetElapsedTime(launched).TotalSeconds < 10)
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
                        if (window == HWND.NULL)
                        {
                            process.Refresh();
                            window = (HWND)process.MainWindowHandle;
                        }

                        if (window == HWND.NULL || info.LastPresentTime.QuadPart == 0)
                        {
                            continue;
                        }

                        var point = new POINT { x = x, y = y };
                        _ = ClientToScreen(window, &point);

                        // The desktop image is the duplicated output's; a window outside it would never show the marker.
                        if (point.x < output.left || point.y < output.top || point.x >= output.right || point.y >= output.bottom)
                        {
                            throw new InvalidOperationException(
                                $"The app's window ({point.x}, {point.y}) is not on the duplicated output ({output.left}, {output.top}, {output.right}, {output.bottom})."
                            );
                        }

                        point.x -= output.left;
                        point.y -= output.top;
                        ID3D11Texture2D* image;
                        ThrowIfFailed(resource->QueryInterface(__uuidof<ID3D11Texture2D>(), (void**)&image));
                        var box = new D3D11_BOX(point.x, point.y, 0, point.x + 1, point.y + 1, 1);
                        context->CopySubresourceRegion((ID3D11Resource*)pixel, 0, 0, 0, 0, (ID3D11Resource*)image, 0, &box);
                        _ = image->Release();
                        D3D11_MAPPED_SUBRESOURCE mapped;
                        ThrowIfFailed(context->Map((ID3D11Resource*)pixel, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped));
                        uint bgra = *(uint*)mapped.pData;
                        context->Unmap((ID3D11Resource*)pixel, 0);

                        // B8G8R8A8 in memory is 0xAARRGGBB as a little-endian uint; the marker is RGB 0x33, 0x66, 0xCC.
                        if ((bgra & 0x00FFFFFF) == 0x003366CC)
                        {
                            presentQpc = info.LastPresentTime.QuadPart;
                            return Stopwatch.GetElapsedTime(launched, presentQpc).TotalMilliseconds;
                        }
                    }
                    finally
                    {
                        _ = resource->Release();
                        _ = duplication->ReleaseFrame();
                    }
                }

                return null;
            }
            catch (Exception watchError)
            {
                // A throw leaves the caller without the process, so the launched app is ended here (M1a review). Kill did not
                // throw for a process that had exited (.NET 10.0.13), but its documentation names a Win32Exception for one that
                // is exiting; the watch's exception then comes first (.cursor/rules/csharp.mdc).
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception killError)
                {
                    throw new AggregateException(watchError, killError);
                }

                throw;
            }
        }
        finally
        {
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

    // The duplication of the adapter's first output (the primary display), and where that output is on the desktop.
    private static IDXGIOutputDuplication* Duplicate(ID3D11Device* device, out RECT desktop)
    {
        IDXGIDevice* dxgiDevice;
        ThrowIfFailed(device->QueryInterface(__uuidof<IDXGIDevice>(), (void**)&dxgiDevice));
        IDXGIAdapter* adapter;
        ThrowIfFailed(dxgiDevice->GetAdapter(&adapter));
        IDXGIOutput* output;
        ThrowIfFailed(adapter->EnumOutputs(0, &output));
        DXGI_OUTPUT_DESC description;
        ThrowIfFailed(output->GetDesc(&description));
        desktop = description.DesktopCoordinates;
        IDXGIOutput1* output1;
        ThrowIfFailed(output->QueryInterface(__uuidof<IDXGIOutput1>(), (void**)&output1));
        IDXGIOutputDuplication* duplication;
        ThrowIfFailed(output1->DuplicateOutput((IUnknown*)device, &duplication));
        _ = output1->Release();
        _ = output->Release();
        _ = adapter->Release();
        _ = dxgiDevice->Release();
        return duplication;
    }

    private static void ThrowIfFailed(HRESULT result)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"Desktop Duplication failed: 0x{(uint)result:X8}.");
        }
    }
}
