using System.Runtime.InteropServices;

namespace XylemUI.Text.Interop;

/// <summary>
/// The HarfBuzz C API calls the text pipeline makes (design §4.1). The library is linked into a NativeAOT executable and loaded as
/// <c>harfbuzz.dll</c> under the JIT, from these same declarations (design §9).
/// </summary>
internal static unsafe partial class HarfBuzz
{
    private const string Library = "harfbuzz";

    /// <summary><c>HB_MEMORY_MODE_READONLY</c>: HarfBuzz reads the bytes in place, without a copy.</summary>
    public const int MemoryModeReadOnly = 1;

    /// <summary><c>HB_DIRECTION_LTR</c>.</summary>
    public const int DirectionLtr = 4;

    [LibraryImport(Library, EntryPoint = "hb_blob_create")]
    public static partial nint BlobCreate(byte* data, uint length, int mode, void* userData, void* destroy);

    [LibraryImport(Library, EntryPoint = "hb_blob_destroy")]
    public static partial void BlobDestroy(nint blob);

    [LibraryImport(Library, EntryPoint = "hb_face_count")]
    public static partial uint FaceCount(nint blob);

    [LibraryImport(Library, EntryPoint = "hb_face_create")]
    public static partial nint FaceCreate(nint blob, uint index);

    [LibraryImport(Library, EntryPoint = "hb_face_destroy")]
    public static partial void FaceDestroy(nint face);

    [LibraryImport(Library, EntryPoint = "hb_face_get_upem")]
    public static partial uint FaceGetUpem(nint face);

    [LibraryImport(Library, EntryPoint = "hb_face_get_glyph_count")]
    public static partial uint FaceGetGlyphCount(nint face);

    [LibraryImport(Library, EntryPoint = "hb_face_reference_table")]
    public static partial nint FaceReferenceTable(nint face, uint tag);

    [LibraryImport(Library, EntryPoint = "hb_blob_get_data")]
    public static partial byte* BlobGetData(nint blob, uint* length);

    [LibraryImport(Library, EntryPoint = "hb_ot_name_list_names")]
    public static partial NameEntry* OtNameListNames(nint face, uint* count);

    [LibraryImport(Library, EntryPoint = "hb_ot_name_get_utf16")]
    public static partial uint OtNameGetUtf16(nint face, uint nameId, nint language, uint* textSize, char* text);

    [LibraryImport(Library, EntryPoint = "hb_font_create")]
    public static partial nint FontCreate(nint face);

    [LibraryImport(Library, EntryPoint = "hb_font_destroy")]
    public static partial void FontDestroy(nint font);

    [LibraryImport(Library, EntryPoint = "hb_font_set_scale")]
    public static partial void FontSetScale(nint font, int xScale, int yScale);

    [LibraryImport(Library, EntryPoint = "hb_buffer_create")]
    public static partial nint BufferCreate();

    [LibraryImport(Library, EntryPoint = "hb_buffer_destroy")]
    public static partial void BufferDestroy(nint buffer);

    [LibraryImport(Library, EntryPoint = "hb_buffer_clear_contents")]
    public static partial void BufferClearContents(nint buffer);

    [LibraryImport(Library, EntryPoint = "hb_buffer_add_utf16")]
    public static partial void BufferAddUtf16(nint buffer, char* text, int textLength, uint itemOffset, int itemLength);

    [LibraryImport(Library, EntryPoint = "hb_buffer_guess_segment_properties")]
    public static partial void BufferGuessSegmentProperties(nint buffer);

    [LibraryImport(Library, EntryPoint = "hb_buffer_set_language")]
    public static partial void BufferSetLanguage(nint buffer, nint language);

    [LibraryImport(Library, EntryPoint = "hb_language_from_string")]
    public static partial nint LanguageFromString(byte* text, int length);

    [LibraryImport(Library, EntryPoint = "hb_language_to_string")]
    public static partial byte* LanguageToString(nint language);

    [LibraryImport(Library, EntryPoint = "hb_buffer_set_direction")]
    public static partial void BufferSetDirection(nint buffer, int direction);

    [LibraryImport(Library, EntryPoint = "hb_shape")]
    public static partial void Shape(nint font, nint buffer, void* features, uint featureCount);

    [LibraryImport(Library, EntryPoint = "hb_buffer_get_glyph_infos")]
    public static partial GlyphInfo* BufferGetGlyphInfos(nint buffer, uint* length);

    [LibraryImport(Library, EntryPoint = "hb_buffer_get_glyph_positions")]
    public static partial GlyphPosition* BufferGetGlyphPositions(nint buffer, uint* length);

    /// <summary><c>hb_ot_name_entry_t</c>: a name id and the language of one of its entries.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NameEntry
    {
        public uint NameId;
        public uint Var;
        public nint Language;
    }

    /// <summary><c>hb_glyph_info_t</c>. After shaping, <see cref="Codepoint"/> holds the glyph id.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GlyphInfo
    {
        public uint Codepoint;
        public uint Mask;
        public uint Cluster;
        public uint Var1;
        public uint Var2;
    }

    /// <summary><c>hb_glyph_position_t</c>, in the font's scale.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GlyphPosition
    {
        public int XAdvance;
        public int YAdvance;
        public int XOffset;
        public int YOffset;
        public uint Var;
    }
}
