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

    /// <summary><c>HB_GLYPH_FLAG_UNSAFE_TO_BREAK</c>: in a glyph info's mask after shaping, a break before the glyph's cluster changes the shaping.</summary>
    public const uint GlyphFlagUnsafeToBreak = 1;

    /// <summary><c>HB_OT_METRICS_TAG_UNDERLINE_OFFSET</c> (<c>'undo'</c>, <c>hb-ot-metrics.h</c> line 102).</summary>
    public const uint MetricsUnderlineOffset = 0x756E646F;

    /// <summary><c>HB_OT_METRICS_TAG_UNDERLINE_SIZE</c> (<c>'unds'</c>, line 101).</summary>
    public const uint MetricsUnderlineSize = 0x756E6473;

    /// <summary><c>HB_OT_METRICS_TAG_STRIKEOUT_OFFSET</c> (<c>'stro'</c>, line 100).</summary>
    public const uint MetricsStrikeoutOffset = 0x7374726F;

    /// <summary><c>HB_OT_METRICS_TAG_STRIKEOUT_SIZE</c> (<c>'strs'</c>, line 99).</summary>
    public const uint MetricsStrikeoutSize = 0x73747273;

    /// <summary><c>HB_OT_METRICS_TAG_X_HEIGHT</c> (<c>'xhgt'</c>, line 89).</summary>
    public const uint MetricsXHeight = 0x78686774;

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

    [LibraryImport(Library, EntryPoint = "hb_font_get_h_extents")]
    public static partial int FontGetHExtents(nint font, FontExtents* extents);

    [LibraryImport(Library, EntryPoint = "hb_ot_metrics_get_position")]
    public static partial int OtMetricsGetPosition(nint font, uint tag, int* position);

    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_create")]
    public static partial nint DrawFuncsCreate();

    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_move_to_func")]
    public static partial void DrawFuncsSetMoveToFunc(nint funcs, void* func, void* userData, void* destroy);

    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_line_to_func")]
    public static partial void DrawFuncsSetLineToFunc(nint funcs, void* func, void* userData, void* destroy);

    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_quadratic_to_func")]
    public static partial void DrawFuncsSetQuadraticToFunc(nint funcs, void* func, void* userData, void* destroy);

    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_cubic_to_func")]
    public static partial void DrawFuncsSetCubicToFunc(nint funcs, void* func, void* userData, void* destroy);

    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_close_path_func")]
    public static partial void DrawFuncsSetClosePathFunc(nint funcs, void* func, void* userData, void* destroy);

    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_make_immutable")]
    public static partial void DrawFuncsMakeImmutable(nint funcs);

    [LibraryImport(Library, EntryPoint = "hb_font_draw_glyph")]
    public static partial void FontDrawGlyph(nint font, uint glyph, nint funcs, void* drawData);

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

    /// <summary><c>hb_font_extents_t</c>, in the font's scale: the ascender is above the baseline and positive, the descender below it
    /// and negative (HarfBuzz 14.5.1's <c>hb-font.h</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FontExtents
    {
        public int Ascender;
        public int Descender;
        public int LineGap;
        public int Reserved9;
        public int Reserved8;
        public int Reserved7;
        public int Reserved6;
        public int Reserved5;
        public int Reserved4;
        public int Reserved3;
        public int Reserved2;
        public int Reserved1;
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

    /// <summary>
    /// <c>hb_draw_state_t</c> (HarfBuzz 14.5.1's <c>hb-draw.h</c>, lines 49 to 66): where the pen is, which a draw callback starts
    /// its segment from. Its seven reserved fields are not read.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DrawState
    {
        public int PathOpen;
        public float PathStartX;
        public float PathStartY;
        public float CurrentX;
        public float CurrentY;
    }

    /// <summary><c>hb_feature_t</c>: a feature's tag and value over the clusters from <see cref="Start"/> to <see cref="End"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Feature
    {
        public uint Tag;
        public uint Value;
        public uint Start;
        public uint End;
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
