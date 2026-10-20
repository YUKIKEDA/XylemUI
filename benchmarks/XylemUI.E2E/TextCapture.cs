using System.Diagnostics;
using System.IO.Compression;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.E2E;

// Takes the picture of an app's text scene for the rendering comparison (roadmap row T4, docs/benchmarks/t4-glyphs.md): launches
// the app, makes its window topmost, waits 3 s for it to draw, copies its client area from the screen, writes it as a PNG
// (8-bit RGB), and ends the app. The process is per-monitor DPI aware, so the copy has the display's pixels.
//   text-capture <out.png> <app> [app arguments ...]
internal static unsafe class TextCapture
{
    public static int Run(string output, string app, string arguments)
    {
        _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        using Process process = Process.Start(new ProcessStartInfo(app, arguments) { UseShellExecute = false })!;
        try
        {
            ScreenWatch.BringToFront(process);
            Thread.Sleep(3_000);
            process.Refresh();
            var window = (HWND)process.MainWindowHandle;
            RECT client;
            if (window == HWND.NULL || !GetClientRect(window, &client))
            {
                Console.Error.WriteLine($"{app} showed no window.");
                return 1;
            }

            var origin = new POINT { x = 0, y = 0 };
            _ = ClientToScreen(window, &origin);
            int width = client.right - client.left;
            int height = client.bottom - client.top;
            byte[] pixels = Copy(origin.x, origin.y, width, height);
            WritePng(output, width, height, pixels);
            Console.WriteLine(
                $"{{\"capture\":\"{Path.GetFileName(output)}\",\"width\":{width},\"height\":{height},\"dpi\":{GetDpiForWindow(window)}}}"
            );
            return 0;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }

    // The screen's pixels in the rectangle, as RGB rows from the top.
    private static byte[] Copy(int left, int top, int width, int height)
    {
        HDC screen = GetDC(HWND.NULL);
        HDC memory = CreateCompatibleDC(screen);
        BITMAPINFO info = default;
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        void* bits;
        HBITMAP bitmap = CreateDIBSection(memory, &info, DIB_RGB_COLORS, &bits, HANDLE.NULL, 0);
        HGDIOBJ previous = SelectObject(memory, (HGDIOBJ)bitmap);
        try
        {
            _ = BitBlt(memory, 0, 0, width, height, screen, left, top, SRCCOPY);
            var bgra = new ReadOnlySpan<byte>(bits, width * height * 4);
            byte[] rgb = new byte[width * height * 3];
            for (int i = 0; i < width * height; i++)
            {
                rgb[(i * 3) + 0] = bgra[(i * 4) + 2];
                rgb[(i * 3) + 1] = bgra[(i * 4) + 1];
                rgb[(i * 3) + 2] = bgra[(i * 4) + 0];
            }

            return rgb;
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject((HGDIOBJ)bitmap);
            _ = DeleteDC(memory);
            _ = ReleaseDC(HWND.NULL, screen);
        }
    }

    // A minimal PNG: IHDR (8-bit RGB), one IDAT of the rows with filter 0, compressed with zlib, and IEND.
    private static void WritePng(string path, int width, int height, byte[] rgb)
    {
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgb, y * width * 3, width * 3);
            }
        }

        using FileStream file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        stream.Write(typed);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typed));
        stream.Write(number);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
