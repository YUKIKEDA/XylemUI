using System.Runtime.InteropServices;

namespace XylemUI.Imaging.Interop;

/// <summary>
/// The two functions of the image decoders' C shim (<c>native/imaging/xylemui_image.c</c>, design §5.0.4): Wuffs for PNG, GIF, and
/// BMP, libjpeg-turbo for JPEG, and libwebp for WebP. The shim is linked into a NativeAOT executable and loaded as
/// <c>xylemui_image.dll</c> under the JIT, from these same declarations (design §9).
/// </summary>
internal static unsafe partial class ImageShim
{
    /// <summary>The shim read or decoded the image.</summary>
    public const int Ok = 0;

    /// <summary>The bytes are no format the shim reads, or a form of one it does not read (a 12-bit JPEG, an animated WebP).</summary>
    public const int Unsupported = 1;

    /// <summary>The bytes are broken: a decoder refused them.</summary>
    public const int Broken = 2;

    /// <summary>A decoder could not allocate its state or its work buffer.</summary>
    public const int OutOfMemory = 3;

    private const string Library = "xylemui_image";

    /// <summary>Reads the format and the size of an image without decoding its pixels (<c>xi_info</c>).</summary>
    /// <param name="data">The first byte of the image.</param>
    /// <param name="length">The length of the image in bytes.</param>
    /// <param name="format">The format, an <see cref="ImageFormat"/> value; 0 when none is recognized.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>A status: <see cref="Ok"/>, <see cref="Unsupported"/>, <see cref="Broken"/>, or <see cref="OutOfMemory"/>.</returns>
    [LibraryImport(Library, EntryPoint = "xi_info")]
    public static partial int Info(byte* data, nuint length, int* format, int* width, int* height);

    /// <summary>
    /// Decodes an image, the first frame of a GIF, to premultiplied blue, green, red, and alpha, top-down rows of
    /// <paramref name="stride"/> bytes (<c>xi_decode</c>).
    /// </summary>
    /// <param name="data">The first byte of the image.</param>
    /// <param name="length">The length of the image in bytes.</param>
    /// <param name="pixels">The first byte of <paramref name="height"/> rows of <paramref name="stride"/> bytes.</param>
    /// <param name="stride">The bytes from a row to the next, at least <paramref name="width"/> × 4.</param>
    /// <param name="width">The width <see cref="Info"/> gave for the same bytes.</param>
    /// <param name="height">The height <see cref="Info"/> gave for the same bytes.</param>
    /// <returns>A status, as <see cref="Info"/> returns.</returns>
    [LibraryImport(Library, EntryPoint = "xi_decode")]
    public static partial int Decode(byte* data, nuint length, byte* pixels, nuint stride, int width, int height);
}
