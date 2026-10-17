using System.Runtime.InteropServices;

namespace XylemUI.Text.Interop;

/// <summary>
/// The FreeType C API calls the text pipeline makes, and the parts of its structures it reads (design §4.1, §9).
/// </summary>
/// <remarks>
/// FreeType's <c>FT_Long</c>, <c>FT_Pos</c>, and <c>FT_Fixed</c> are C <c>long</c>: 32 bits on Windows, 64 on Linux and macOS.
/// The structures use <see cref="CLong"/> for them, so the layout follows the platform.
/// </remarks>
internal static unsafe partial class FreeType
{
    private const string Library = "freetype";

    /// <summary><c>FT_LOAD_NO_HINTING</c>: the outline as designed, not fitted to the pixel grid.</summary>
    public const int LoadNoHinting = 1 << 1;

    /// <summary><c>FT_LOAD_RENDER</c>: load the glyph and render it to an 8-bit coverage bitmap.</summary>
    public const int LoadRender = 1 << 2;

    /// <summary><c>FT_PIXEL_MODE_GRAY</c>.</summary>
    public const byte PixelModeGray = 2;

    [LibraryImport(Library, EntryPoint = "FT_Init_FreeType")]
    public static partial int InitFreeType(nint* library);

    [LibraryImport(Library, EntryPoint = "FT_Done_FreeType")]
    public static partial int DoneFreeType(nint library);

    [LibraryImport(Library, EntryPoint = "FT_New_Memory_Face")]
    public static partial int NewMemoryFace(nint library, byte* data, CLong size, CLong faceIndex, FaceRec** face);

    [LibraryImport(Library, EntryPoint = "FT_Done_Face")]
    public static partial int DoneFace(FaceRec* face);

    [LibraryImport(Library, EntryPoint = "FT_Set_Pixel_Sizes")]
    public static partial int SetPixelSizes(FaceRec* face, uint width, uint height);

    [LibraryImport(Library, EntryPoint = "FT_Load_Glyph")]
    public static partial int LoadGlyph(FaceRec* face, uint glyphIndex, int loadFlags);

    /// <summary>The leading fields of <c>FT_FaceRec</c>, up to <c>glyph</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FaceRec
    {
        public CLong NumFaces;
        public CLong FaceIndex;
        public CLong FaceFlags;
        public CLong StyleFlags;
        public CLong NumGlyphs;
        public byte* FamilyName;
        public byte* StyleName;
        public int NumFixedSizes;
        public void* AvailableSizes;
        public int NumCharmaps;
        public void* Charmaps;
        public void* GenericData;
        public void* GenericFinalizer;
        public CLong BBoxXMin;
        public CLong BBoxYMin;
        public CLong BBoxXMax;
        public CLong BBoxYMax;
        public ushort UnitsPerEm;
        public short Ascender;
        public short Descender;
        public short Height;
        public short MaxAdvanceWidth;
        public short MaxAdvanceHeight;
        public short UnderlinePosition;
        public short UnderlineThickness;
        public GlyphSlotRec* Glyph;
    }

    /// <summary>The leading fields of <c>FT_GlyphSlotRec</c>, up to <c>bitmap_top</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GlyphSlotRec
    {
        public nint Library;
        public FaceRec* Face;
        public GlyphSlotRec* Next;
        public uint GlyphIndex;
        public void* GenericData;
        public void* GenericFinalizer;
        public CLong MetricsWidth;
        public CLong MetricsHeight;
        public CLong MetricsHoriBearingX;
        public CLong MetricsHoriBearingY;
        public CLong MetricsHoriAdvance;
        public CLong MetricsVertBearingX;
        public CLong MetricsVertBearingY;
        public CLong MetricsVertAdvance;
        public CLong LinearHoriAdvance;
        public CLong LinearVertAdvance;
        public CLong AdvanceX;
        public CLong AdvanceY;
        public uint Format;
        public Bitmap Bitmap;
        public int BitmapLeft;
        public int BitmapTop;
    }

    /// <summary><c>FT_Bitmap</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Bitmap
    {
        public uint Rows;
        public uint Width;
        public int Pitch;
        public byte* Buffer;
        public ushort NumGrays;
        public byte PixelMode;
        public byte PaletteMode;
        public void* Palette;
    }
}
