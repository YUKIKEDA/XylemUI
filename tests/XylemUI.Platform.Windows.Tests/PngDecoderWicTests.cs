using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The PNG decoder of bitmap color glyphs (design §5.0.3, "PNG bitmaps", roadmap row T9) against WIC, Windows' own PNG decoder,
// as an independent reference, on every image of PngSuite (tests/XylemUI.Text.Tests/Png/README.md).
public sealed class PngDecoderWicTests
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Png", "PngSuite");

    /// <summary>
    /// Every valid image of PngSuite (every color type, bit depth, transparency, interlacing, and chunk order) reads to the same
    /// premultiplied blue, green, red, and alpha as WIC gives; every broken image (a name that starts with "x") is broken. WIC
    /// rounds 16-bit samples to 8 bits its own way (in a first run, 25 of the 16-bit images differed by 1 in some bytes), so a
    /// 16-bit image is compared with WIC's 16-bit samples rounded as the design says, <c>(v + 128) / 257</c>.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The 176 images of PngSuite 2017jul19, a decoder, and WIC's imaging factory on this thread.
    ///
    /// Steps:
    /// - For each image: read it with the decoder (sides up to 4,096); for a valid one, read it with WIC, converted to
    ///   <c>GUID_WICPixelFormat32bppPBGRA</c>, or for a 16-bit one (its name ends in "16") to <c>GUID_WICPixelFormat64bppRGBA</c>
    ///   then rounded to 8 bits as <c>(v + 128) / 257</c> and premultiplied, and compare the sizes and every byte.
    ///
    /// Expected:
    /// - Each of the 162 valid images reads, with WIC's size and bytes. Each of the 14 broken images returns
    ///   <see langword="false"/>, and none throws.
    /// </remarks>
    [Fact]
    public void TryDecode_PngSuite_IsWhatWicReads()
    {
        string[] files = System.IO.Directory.GetFiles(Directory, "*.png");
        Assert.Equal(176, files.Length);
        var decoder = new PngDecoder();
        var differing = new List<string>();
        int valid = 0;
        int broken = 0;
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            byte[] png = File.ReadAllBytes(file);
            bool read = decoder.TryDecode(png, 4096, out int width, out int height);
            if (name.StartsWith('x'))
            {
                Assert.False(read, $"{name} is broken but read.");
                broken++;
                continue;
            }

            Assert.True(read, $"{name} did not read.");
            (int wicWidth, int wicHeight, byte[] wic) = name.EndsWith("16.png", StringComparison.Ordinal) ? WicSixteen(png) : Wic(png);
            if ((width, height) != (wicWidth, wicHeight) || !decoder.Pixels.SequenceEqual(wic))
            {
                differing.Add($"{name}: {width}x{height} against {wicWidth}x{wicHeight}, {Differences(decoder.Pixels, wic)}");
            }

            valid++;
        }

        Assert.Equal((162, 14), (valid, broken));
        Assert.True(differing.Count == 0, string.Join(Environment.NewLine, differing));
    }

    private static string Differences(ReadOnlySpan<byte> ours, byte[] wic)
    {
        if (ours.Length != wic.Length)
        {
            return $"{ours.Length} bytes against {wic.Length}";
        }

        int count = 0;
        int largest = 0;
        int first = -1;
        for (int i = 0; i < wic.Length; i++)
        {
            int difference = Math.Abs(ours[i] - wic[i]);
            if (difference > 0)
            {
                count++;
                largest = Math.Max(largest, difference);
                first = first < 0 ? i : first;
            }
        }

        return $"{count} bytes differ by up to {largest}, the first at byte {first} ({ours[first]} against {wic[first]})";
    }

    // WIC's 16-bit samples (red, green, blue, alpha, not premultiplied), rounded to 8 bits and premultiplied as the decoder does.
    // A gray image without tRNS is read in WIC's own 16-bit gray: converted to 64-bit RGBA, WIC gave its gray through 8 bits (in
    // a second run, the 6 such images differed by 1 in 18 bytes each).
    private static (int Width, int Height, byte[] Pixels) WicSixteen(byte[] png)
    {
        bool gray = png[25] == 0 && png.AsSpan().IndexOf("tRNS"u8) < 0;
        if (gray)
        {
            (int grayWidth, int grayHeight, byte[] samples) = Wic(png, GUID.GUID_WICPixelFormat16bppGray, 2);
            byte[] opaque = new byte[grayWidth * grayHeight * 4];
            for (int i = 0; i < grayWidth * grayHeight; i++)
            {
                byte value = (byte)((BitConverter.ToUInt16(samples, i * 2) + 128) / 257);
                (opaque[i * 4], opaque[(i * 4) + 1], opaque[(i * 4) + 2], opaque[(i * 4) + 3]) = (value, value, value, 255);
            }

            return (grayWidth, grayHeight, opaque);
        }

        (int width, int height, byte[] wide) = Wic(png, GUID.GUID_WICPixelFormat64bppRGBA, 8);
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            int Eight(int channel) => (BitConverter.ToUInt16(wide, (i * 8) + (channel * 2)) + 128) / 257;
            int alpha = Eight(3);
            pixels[i * 4] = (byte)(((Eight(2) * alpha) + 127) / 255);
            pixels[(i * 4) + 1] = (byte)(((Eight(1) * alpha) + 127) / 255);
            pixels[(i * 4) + 2] = (byte)(((Eight(0) * alpha) + 127) / 255);
            pixels[(i * 4) + 3] = (byte)alpha;
        }

        return (width, height, pixels);
    }

    private static (int Width, int Height, byte[] Pixels) Wic(byte[] png) => Wic(png, GUID.GUID_WICPixelFormat32bppPBGRA, 4);

    private static unsafe (int Width, int Height, byte[] Pixels) Wic(byte[] png, Guid format, int bytesPerPixel)
    {
        _ = CoInitializeEx(null, (uint)COINIT.COINIT_MULTITHREADED);
        IWICImagingFactory* factory;
        Guid clsid = CLSID.CLSID_WICImagingFactory;
        Guid iid = IID.IID_IWICImagingFactory;
        Assert.True(CoCreateInstance(&clsid, null, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &iid, (void**)&factory) >= 0);
        IWICStream* stream;
        Assert.True(factory->CreateStream(&stream) >= 0);
        fixed (byte* data = png)
        {
            Assert.True(stream->InitializeFromMemory(data, (uint)png.Length) >= 0);
            IWICBitmapDecoder* decoder;
            Assert.True(factory->CreateDecoderFromStream((IStream*)stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnDemand, &decoder) >= 0);
            IWICBitmapFrameDecode* frame;
            Assert.True(decoder->GetFrame(0, &frame) >= 0);
            IWICBitmapSource* converted;
            Assert.True(WICConvertBitmapSource(&format, (IWICBitmapSource*)frame, &converted) >= 0);
            uint width;
            uint height;
            Assert.True(converted->GetSize(&width, &height) >= 0);
            byte[] pixels = new byte[width * height * bytesPerPixel];
            fixed (byte* to = pixels)
            {
                Assert.True(converted->CopyPixels(null, width * (uint)bytesPerPixel, (uint)pixels.Length, to) >= 0);
            }

            _ = converted->Release();
            _ = frame->Release();
            _ = decoder->Release();
            _ = stream->Release();
            _ = factory->Release();
            return ((int)width, (int)height, pixels);
        }
    }
}
