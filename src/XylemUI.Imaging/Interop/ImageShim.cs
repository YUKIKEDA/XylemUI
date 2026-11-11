using System.Runtime.InteropServices;

namespace XylemUI.Imaging.Interop;

/// <summary>
/// The functions of the image decoders' C shim (<c>native/imaging/xylemui_image.c</c>, design §5.0.4): Wuffs for PNG, GIF, and
/// BMP, libjpeg-turbo for JPEG, and libwebp for WebP. The shim is linked into a NativeAOT executable and loaded as
/// <c>xylemui_image.dll</c> under the JIT, from these same declarations (design §9).
/// </summary>
internal static unsafe partial class ImageShim
{
    /// <summary>The shim read or decoded the image.</summary>
    public const int Ok = 0;

    /// <summary>The bytes are no format the shim reads, or a form of one it does not read (a 12-bit JPEG; an animated WebP asked for
    /// as a still image, whose frames <see cref="AnimationNext"/> gives).</summary>
    public const int Unsupported = 1;

    /// <summary>The bytes are broken: a decoder refused them.</summary>
    public const int Broken = 2;

    /// <summary>A decoder could not allocate its state or its work buffer.</summary>
    public const int OutOfMemory = 3;

    /// <summary>An animation played the plays its file asks for: <see cref="AnimationNext"/> gave no frame.</summary>
    public const int End = 4;

    private const string Library = "xylemui_image";

    /// <summary>
    /// Reads the format and the size of an image, and whether it is an animation, without decoding its pixels (<c>xi_info</c>).
    /// </summary>
    /// <param name="data">The first byte of the image.</param>
    /// <param name="length">The length of the image in bytes.</param>
    /// <param name="format">The format, an <see cref="ImageFormat"/> value; 0 when none is recognized.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="animated">1 for a WebP whose header says it is an animation and for a GIF with a second frame, which
    /// <see cref="AnimationOpen"/> opens; null when the caller does not ask.</param>
    /// <returns>A status: <see cref="Ok"/>, <see cref="Unsupported"/>, <see cref="Broken"/>, or <see cref="OutOfMemory"/>.</returns>
    [LibraryImport(Library, EntryPoint = "xi_info")]
    public static partial int Info(byte* data, nuint length, int* format, int* width, int* height, int* animated);

    /// <summary>
    /// Opens the decoder of an animated GIF or WebP (<c>xi_animation_open</c>). The image's bytes stay in place, unchanged, until
    /// <see cref="AnimationClose"/>.
    /// </summary>
    /// <param name="data">The first byte of the image.</param>
    /// <param name="length">The length of the image in bytes.</param>
    /// <param name="color">The image's color information; null for none.</param>
    /// <param name="maxCanvasBytes">The most bytes a canvas (width × height × 4) may take.</param>
    /// <param name="animation">The decoder, or 0.</param>
    /// <param name="width">The canvas's width in pixels.</param>
    /// <param name="height">The canvas's height in pixels.</param>
    /// <param name="converts">1 when the frames' colors are converted to sRGB, which <see cref="AnimationNext"/> needs a buffer
    /// for.</param>
    /// <returns>A status, as <see cref="Info"/> returns.</returns>
    [LibraryImport(Library, EntryPoint = "xi_animation_open")]
    public static partial int AnimationOpen(
        byte* data,
        nuint length,
        Color* color,
        nuint maxCanvasBytes,
        nint* animation,
        int* width,
        int* height,
        int* converts
    );

    /// <summary>
    /// Composes an animation's next frame (<c>xi_animation_next</c>): the first of the next play after the last of one.
    /// </summary>
    /// <param name="animation">The decoder.</param>
    /// <param name="converted">A buffer as large as the canvas, which the colors are converted into when the decoder converts; null
    /// when it does not.</param>
    /// <param name="frame">The whole canvas in premultiplied BGRA, rows of width × 4 bytes, good until the next call: the decoder's
    /// own, or <paramref name="converted"/>.</param>
    /// <param name="duration">How long the frame shows, in milliseconds, as the file gives it.</param>
    /// <returns><see cref="Ok"/>; <see cref="End"/> once the plays the file asks for are done; or <see cref="Broken"/>.</returns>
    [LibraryImport(Library, EntryPoint = "xi_animation_next")]
    public static partial int AnimationNext(nint animation, byte* converted, byte** frame, int* duration);

    /// <summary>Closes an animation's decoder and frees its canvases (<c>xi_animation_close</c>); 0 is allowed.</summary>
    /// <param name="animation">The decoder.</param>
    [LibraryImport(Library, EntryPoint = "xi_animation_close")]
    public static partial void AnimationClose(nint animation);

    /// <summary>
    /// Decodes an image, the first frame of a GIF, to premultiplied blue, green, red, and alpha, top-down rows of
    /// <paramref name="stride"/> bytes (<c>xi_decode</c>); a JPEG at a scale of eighths, with libjpeg-turbo's DCT scaling.
    /// </summary>
    /// <param name="data">The first byte of the image.</param>
    /// <param name="length">The length of the image in bytes.</param>
    /// <param name="pixels">The first byte of <paramref name="height"/> rows of <paramref name="stride"/> bytes.</param>
    /// <param name="stride">The bytes from a row to the next, at least <paramref name="width"/> × 4.</param>
    /// <param name="width">The width <see cref="Info"/> gave for the same bytes, at the scale: <see cref="Scaled"/>.</param>
    /// <param name="height">The height <see cref="Info"/> gave for the same bytes, at the scale.</param>
    /// <param name="eighths">The scale, from 1 to 8 eighths; 8 for an image that is not a JPEG.</param>
    /// <param name="color">The image's color information, whose colors the shim converts to sRGB; null for none.</param>
    /// <returns>A status, as <see cref="Info"/> returns.</returns>
    [LibraryImport(Library, EntryPoint = "xi_decode")]
    public static partial int Decode(byte* data, nuint length, byte* pixels, nuint stride, int width, int height, int eighths, Color* color);

    /// <summary>
    /// Returns a side of a JPEG decoded at a scale of eighths: the side times <paramref name="eighths"/> / 8, rounded up, as
    /// libjpeg-turbo rounds it.
    /// </summary>
    /// <param name="side">The side, in pixels.</param>
    /// <param name="eighths">The scale, from 1 to 8 eighths.</param>
    /// <returns>The side at that scale.</returns>
    public static int Scaled(int side, int eighths) => (int)((((long)side * eighths) + 7) / 8);

    /// <summary>
    /// The shim's <c>xi_color</c>: an image's color information (design §5.0.4, IM1's Grill, Q23 and Q24).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Color
    {
        /// <summary>The ICC profile's first byte, with room past its end; null for none.</summary>
        public byte* Icc;

        /// <summary>The ICC profile's length.</summary>
        public nuint IccLength;

        /// <summary>1 when <see cref="Chromaticities"/> holds a PNG's cHRM.</summary>
        public int HasChromaticities;

        /// <summary>White, red, green, and blue x and y.</summary>
        public fixed float Chromaticities[8];

        /// <summary>1 when <see cref="Gamma"/> holds a PNG's gAMA.</summary>
        public int HasGamma;

        /// <summary>The encoding gamma.</summary>
        public float Gamma;
    }
}
