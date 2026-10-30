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

    /// <summary>Creates a blob over a block of memory (<c>hb_blob_create</c>).</summary>
    /// <param name="data">The first byte of the memory.</param>
    /// <param name="length">The length of the memory in bytes.</param>
    /// <param name="mode">How HarfBuzz may use the memory, such as <see cref="MemoryModeReadOnly"/>.</param>
    /// <param name="userData">The data passed to <paramref name="destroy"/>.</param>
    /// <param name="destroy">The callback HarfBuzz calls when it no longer needs the memory, or <see langword="null"/>.</param>
    /// <returns>The new blob, which the caller destroys with <see cref="BlobDestroy"/>.</returns>
    [LibraryImport(Library, EntryPoint = "hb_blob_create")]
    public static partial nint BlobCreate(byte* data, uint length, int mode, void* userData, void* destroy);

    /// <summary>Releases a reference to a blob (<c>hb_blob_destroy</c>).</summary>
    /// <param name="blob">The blob.</param>
    [LibraryImport(Library, EntryPoint = "hb_blob_destroy")]
    public static partial void BlobDestroy(nint blob);

    /// <summary>Counts the faces in a font file (<c>hb_face_count</c>).</summary>
    /// <param name="blob">The blob that holds the font file.</param>
    /// <returns>The number of faces in <paramref name="blob"/>.</returns>
    [LibraryImport(Library, EntryPoint = "hb_face_count")]
    public static partial uint FaceCount(nint blob);

    /// <summary>Creates a face from one face of a font file (<c>hb_face_create</c>).</summary>
    /// <param name="blob">The blob that holds the font file.</param>
    /// <param name="index">The index of the face in <paramref name="blob"/>.</param>
    /// <returns>The new face, which the caller destroys with <see cref="FaceDestroy"/>.</returns>
    [LibraryImport(Library, EntryPoint = "hb_face_create")]
    public static partial nint FaceCreate(nint blob, uint index);

    /// <summary>Releases a reference to a face (<c>hb_face_destroy</c>).</summary>
    /// <param name="face">The face.</param>
    [LibraryImport(Library, EntryPoint = "hb_face_destroy")]
    public static partial void FaceDestroy(nint face);

    /// <summary>Returns the units per em of a face (<c>hb_face_get_upem</c>).</summary>
    /// <param name="face">The face.</param>
    /// <returns>The units per em.</returns>
    [LibraryImport(Library, EntryPoint = "hb_face_get_upem")]
    public static partial uint FaceGetUpem(nint face);

    /// <summary>Returns the number of glyphs in a face (<c>hb_face_get_glyph_count</c>).</summary>
    /// <param name="face">The face.</param>
    /// <returns>The number of glyphs.</returns>
    [LibraryImport(Library, EntryPoint = "hb_face_get_glyph_count")]
    public static partial uint FaceGetGlyphCount(nint face);

    /// <summary>Returns a blob that holds one table of a face (<c>hb_face_reference_table</c>).</summary>
    /// <param name="face">The face.</param>
    /// <param name="tag">The table's tag.</param>
    /// <returns>The table's blob, empty when the face has no such table; the caller destroys it with <see cref="BlobDestroy"/>.</returns>
    [LibraryImport(Library, EntryPoint = "hb_face_reference_table")]
    public static partial nint FaceReferenceTable(nint face, uint tag);

    /// <summary>Returns the bytes of a blob (<c>hb_blob_get_data</c>).</summary>
    /// <param name="blob">The blob.</param>
    /// <param name="length">Receives the length of the bytes.</param>
    /// <returns>The first byte, which stays valid while <paramref name="blob"/> lives.</returns>
    [LibraryImport(Library, EntryPoint = "hb_blob_get_data")]
    public static partial byte* BlobGetData(nint blob, uint* length);

    /// <summary>Lists the entries of a face's <c>name</c> table (<c>hb_ot_name_list_names</c>).</summary>
    /// <param name="face">The face.</param>
    /// <param name="count">Receives the number of entries.</param>
    /// <returns>The first entry, which <paramref name="face"/> owns.</returns>
    [LibraryImport(Library, EntryPoint = "hb_ot_name_list_names")]
    public static partial NameEntry* OtNameListNames(nint face, uint* count);

    /// <summary>Copies a name of a face in UTF-16 (<c>hb_ot_name_get_utf16</c>).</summary>
    /// <param name="face">The face.</param>
    /// <param name="nameId">The name id.</param>
    /// <param name="language">The language of the name.</param>
    /// <param name="textSize">On input, the capacity of <paramref name="text"/> in code units; on output, the number written without the terminator.</param>
    /// <param name="text">The buffer that receives the name.</param>
    /// <returns>The length of the whole name in code units.</returns>
    [LibraryImport(Library, EntryPoint = "hb_ot_name_get_utf16")]
    public static partial uint OtNameGetUtf16(nint face, uint nameId, nint language, uint* textSize, char* text);

    /// <summary>Creates a font from a face (<c>hb_font_create</c>).</summary>
    /// <param name="face">The face.</param>
    /// <returns>The new font, which the caller destroys with <see cref="FontDestroy"/>.</returns>
    [LibraryImport(Library, EntryPoint = "hb_font_create")]
    public static partial nint FontCreate(nint face);

    /// <summary>Releases a reference to a font (<c>hb_font_destroy</c>).</summary>
    /// <param name="font">The font.</param>
    [LibraryImport(Library, EntryPoint = "hb_font_destroy")]
    public static partial void FontDestroy(nint font);

    /// <summary>Sets the horizontal and vertical scale of a font (<c>hb_font_set_scale</c>).</summary>
    /// <param name="font">The font.</param>
    /// <param name="xScale">The horizontal scale.</param>
    /// <param name="yScale">The vertical scale.</param>
    [LibraryImport(Library, EntryPoint = "hb_font_set_scale")]
    public static partial void FontSetScale(nint font, int xScale, int yScale);

    /// <summary>Gets the extents of a font for horizontal text (<c>hb_font_get_h_extents</c>).</summary>
    /// <param name="font">The font.</param>
    /// <param name="extents">Receives the extents.</param>
    /// <returns>Nonzero when the font has the extents; zero otherwise.</returns>
    [LibraryImport(Library, EntryPoint = "hb_font_get_h_extents")]
    public static partial int FontGetHExtents(nint font, FontExtents* extents);

    /// <summary>Gets a position metric of a font from its OpenType tables (<c>hb_ot_metrics_get_position</c>).</summary>
    /// <param name="font">The font.</param>
    /// <param name="tag">The metric's tag, such as <see cref="MetricsXHeight"/>.</param>
    /// <param name="position">Receives the metric in the font's scale.</param>
    /// <returns>Nonzero when the font has the metric; zero otherwise.</returns>
    [LibraryImport(Library, EntryPoint = "hb_ot_metrics_get_position")]
    public static partial int OtMetricsGetPosition(nint font, uint tag, int* position);

    /// <summary>Creates a set of draw callbacks (<c>hb_draw_funcs_create</c>).</summary>
    /// <returns>The new set.</returns>
    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_create")]
    public static partial nint DrawFuncsCreate();

    /// <summary>Sets the callback for a move of the pen (<c>hb_draw_funcs_set_move_to_func</c>).</summary>
    /// <param name="funcs">The set of draw callbacks.</param>
    /// <param name="func">The callback.</param>
    /// <param name="userData">The data passed to <paramref name="func"/>.</param>
    /// <param name="destroy">The callback HarfBuzz calls when it no longer needs <paramref name="userData"/>, or <see langword="null"/>.</param>
    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_move_to_func")]
    public static partial void DrawFuncsSetMoveToFunc(nint funcs, void* func, void* userData, void* destroy);

    /// <summary>Sets the callback for a line segment (<c>hb_draw_funcs_set_line_to_func</c>).</summary>
    /// <param name="funcs">The set of draw callbacks.</param>
    /// <param name="func">The callback.</param>
    /// <param name="userData">The data passed to <paramref name="func"/>.</param>
    /// <param name="destroy">The callback HarfBuzz calls when it no longer needs <paramref name="userData"/>, or <see langword="null"/>.</param>
    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_line_to_func")]
    public static partial void DrawFuncsSetLineToFunc(nint funcs, void* func, void* userData, void* destroy);

    /// <summary>Sets the callback for a quadratic Bezier segment (<c>hb_draw_funcs_set_quadratic_to_func</c>).</summary>
    /// <param name="funcs">The set of draw callbacks.</param>
    /// <param name="func">The callback.</param>
    /// <param name="userData">The data passed to <paramref name="func"/>.</param>
    /// <param name="destroy">The callback HarfBuzz calls when it no longer needs <paramref name="userData"/>, or <see langword="null"/>.</param>
    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_quadratic_to_func")]
    public static partial void DrawFuncsSetQuadraticToFunc(nint funcs, void* func, void* userData, void* destroy);

    /// <summary>Sets the callback for a cubic Bezier segment (<c>hb_draw_funcs_set_cubic_to_func</c>).</summary>
    /// <param name="funcs">The set of draw callbacks.</param>
    /// <param name="func">The callback.</param>
    /// <param name="userData">The data passed to <paramref name="func"/>.</param>
    /// <param name="destroy">The callback HarfBuzz calls when it no longer needs <paramref name="userData"/>, or <see langword="null"/>.</param>
    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_cubic_to_func")]
    public static partial void DrawFuncsSetCubicToFunc(nint funcs, void* func, void* userData, void* destroy);

    /// <summary>Sets the callback that closes a path (<c>hb_draw_funcs_set_close_path_func</c>).</summary>
    /// <param name="funcs">The set of draw callbacks.</param>
    /// <param name="func">The callback.</param>
    /// <param name="userData">The data passed to <paramref name="func"/>.</param>
    /// <param name="destroy">The callback HarfBuzz calls when it no longer needs <paramref name="userData"/>, or <see langword="null"/>.</param>
    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_set_close_path_func")]
    public static partial void DrawFuncsSetClosePathFunc(nint funcs, void* func, void* userData, void* destroy);

    /// <summary>Makes a set of draw callbacks immutable (<c>hb_draw_funcs_make_immutable</c>).</summary>
    /// <param name="funcs">The set of draw callbacks.</param>
    [LibraryImport(Library, EntryPoint = "hb_draw_funcs_make_immutable")]
    public static partial void DrawFuncsMakeImmutable(nint funcs);

    /// <summary>Draws the outline of a glyph through a set of draw callbacks (<c>hb_font_draw_glyph</c>).</summary>
    /// <param name="font">The font.</param>
    /// <param name="glyph">The glyph id.</param>
    /// <param name="funcs">The set of draw callbacks.</param>
    /// <param name="drawData">The data passed to each callback.</param>
    [LibraryImport(Library, EntryPoint = "hb_font_draw_glyph")]
    public static partial void FontDrawGlyph(nint font, uint glyph, nint funcs, void* drawData);

    /// <summary>Creates an empty buffer (<c>hb_buffer_create</c>).</summary>
    /// <returns>The new buffer, which the caller destroys with <see cref="BufferDestroy"/>.</returns>
    [LibraryImport(Library, EntryPoint = "hb_buffer_create")]
    public static partial nint BufferCreate();

    /// <summary>Releases a reference to a buffer (<c>hb_buffer_destroy</c>).</summary>
    /// <param name="buffer">The buffer.</param>
    [LibraryImport(Library, EntryPoint = "hb_buffer_destroy")]
    public static partial void BufferDestroy(nint buffer);

    /// <summary>Empties a buffer and unsets its segment properties, keeping its memory (<c>hb_buffer_clear_contents</c>).</summary>
    /// <param name="buffer">The buffer.</param>
    [LibraryImport(Library, EntryPoint = "hb_buffer_clear_contents")]
    public static partial void BufferClearContents(nint buffer);

    /// <summary>Adds UTF-16 text to a buffer (<c>hb_buffer_add_utf16</c>).</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="text">The whole text, which HarfBuzz reads as context around the item.</param>
    /// <param name="textLength">The length of <paramref name="text"/> in code units.</param>
    /// <param name="itemOffset">The offset of the item to add, in code units.</param>
    /// <param name="itemLength">The length of the item to add, in code units.</param>
    [LibraryImport(Library, EntryPoint = "hb_buffer_add_utf16")]
    public static partial void BufferAddUtf16(nint buffer, char* text, int textLength, uint itemOffset, int itemLength);

    /// <summary>Sets the unset direction, script, and language of a buffer from its contents (<c>hb_buffer_guess_segment_properties</c>).</summary>
    /// <param name="buffer">The buffer.</param>
    [LibraryImport(Library, EntryPoint = "hb_buffer_guess_segment_properties")]
    public static partial void BufferGuessSegmentProperties(nint buffer);

    /// <summary>Sets the language of a buffer (<c>hb_buffer_set_language</c>).</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="language">The language.</param>
    [LibraryImport(Library, EntryPoint = "hb_buffer_set_language")]
    public static partial void BufferSetLanguage(nint buffer, nint language);

    /// <summary>Returns the language for a BCP 47 tag (<c>hb_language_from_string</c>).</summary>
    /// <param name="text">The tag in ASCII.</param>
    /// <param name="length">The length of <paramref name="text"/> in bytes, or -1 when it ends with a zero byte.</param>
    /// <returns>The language, which HarfBuzz owns for the life of the process.</returns>
    [LibraryImport(Library, EntryPoint = "hb_language_from_string")]
    public static partial nint LanguageFromString(byte* text, int length);

    /// <summary>Returns the tag of a language (<c>hb_language_to_string</c>).</summary>
    /// <param name="language">The language.</param>
    /// <returns>The tag as a string that ends with a zero byte, which HarfBuzz owns.</returns>
    [LibraryImport(Library, EntryPoint = "hb_language_to_string")]
    public static partial byte* LanguageToString(nint language);

    /// <summary>Sets the text direction of a buffer (<c>hb_buffer_set_direction</c>).</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="direction">The direction, such as <see cref="DirectionLtr"/>.</param>
    [LibraryImport(Library, EntryPoint = "hb_buffer_set_direction")]
    public static partial void BufferSetDirection(nint buffer, int direction);

    /// <summary>Shapes the text in a buffer, replacing its characters with positioned glyphs (<c>hb_shape</c>).</summary>
    /// <param name="font">The font.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="features">The first <see cref="Feature"/>, or <see langword="null"/>.</param>
    /// <param name="featureCount">The number of features.</param>
    [LibraryImport(Library, EntryPoint = "hb_shape")]
    public static partial void Shape(nint font, nint buffer, void* features, uint featureCount);

    /// <summary>Returns the glyph infos of a buffer (<c>hb_buffer_get_glyph_infos</c>).</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="length">Receives the number of glyph infos.</param>
    /// <returns>The first glyph info, which <paramref name="buffer"/> owns.</returns>
    [LibraryImport(Library, EntryPoint = "hb_buffer_get_glyph_infos")]
    public static partial GlyphInfo* BufferGetGlyphInfos(nint buffer, uint* length);

    /// <summary>Returns the glyph positions of a shaped buffer (<c>hb_buffer_get_glyph_positions</c>).</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="length">Receives the number of glyph positions, or <see langword="null"/>.</param>
    /// <returns>The first glyph position, which <paramref name="buffer"/> owns.</returns>
    [LibraryImport(Library, EntryPoint = "hb_buffer_get_glyph_positions")]
    public static partial GlyphPosition* BufferGetGlyphPositions(nint buffer, uint* length);

    /// <summary><c>hb_ot_name_entry_t</c>: a name id and the language of one of its entries.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NameEntry
    {
        /// <summary>The name id.</summary>
        public uint NameId;

        /// <summary>A field private to HarfBuzz.</summary>
        public uint Var;

        /// <summary>The language of the entry.</summary>
        public nint Language;
    }

    /// <summary><c>hb_font_extents_t</c>, in the font's scale: the ascender is above the baseline and positive, the descender below it
    /// and negative (HarfBuzz 14.5.1's <c>hb-font.h</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FontExtents
    {
        /// <summary>The distance from the baseline to the top of the line.</summary>
        public int Ascender;

        /// <summary>The distance from the baseline to the bottom of the line.</summary>
        public int Descender;

        /// <summary>The space the font asks for between lines.</summary>
        public int LineGap;

        /// <summary>A field reserved by HarfBuzz.</summary>
        public int Reserved9;

        /// <summary>A field reserved by HarfBuzz.</summary>
        public int Reserved8;

        /// <summary>A field reserved by HarfBuzz.</summary>
        public int Reserved7;

        /// <summary>A field reserved by HarfBuzz.</summary>
        public int Reserved6;

        /// <summary>A field reserved by HarfBuzz.</summary>
        public int Reserved5;

        /// <summary>A field reserved by HarfBuzz.</summary>
        public int Reserved4;

        /// <summary>A field reserved by HarfBuzz.</summary>
        public int Reserved3;

        /// <summary>A field reserved by HarfBuzz.</summary>
        public int Reserved2;

        /// <summary>A field reserved by HarfBuzz.</summary>
        public int Reserved1;
    }

    /// <summary><c>hb_glyph_info_t</c>. After shaping, <see cref="Codepoint"/> holds the glyph id.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GlyphInfo
    {
        /// <summary>The code point before shaping, or the glyph id after it.</summary>
        public uint Codepoint;

        /// <summary>After shaping, the glyph flags, such as <see cref="GlyphFlagUnsafeToBreak"/>.</summary>
        public uint Mask;

        /// <summary>The offset in the added text of the cluster the glyph belongs to.</summary>
        public uint Cluster;

        /// <summary>A field private to HarfBuzz.</summary>
        public uint Var1;

        /// <summary>A field private to HarfBuzz.</summary>
        public uint Var2;
    }

    /// <summary>
    /// <c>hb_draw_state_t</c> (HarfBuzz 14.5.1's <c>hb-draw.h</c>, lines 49 to 66): where the pen is, which a draw callback starts
    /// its segment from. Its seven reserved fields are not read.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DrawState
    {
        /// <summary>Nonzero while a path is open.</summary>
        public int PathOpen;

        /// <summary>The x coordinate where the open path starts.</summary>
        public float PathStartX;

        /// <summary>The y coordinate where the open path starts.</summary>
        public float PathStartY;

        /// <summary>The x coordinate of the pen.</summary>
        public float CurrentX;

        /// <summary>The y coordinate of the pen.</summary>
        public float CurrentY;
    }

    /// <summary><c>hb_feature_t</c>: a feature's tag and value over the clusters from <see cref="Start"/> to <see cref="End"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Feature
    {
        /// <summary>The feature's OpenType tag.</summary>
        public uint Tag;

        /// <summary>The feature's value: zero turns it off, and other values turn it on or pick an alternate.</summary>
        public uint Value;

        /// <summary>The first cluster the feature applies to.</summary>
        public uint Start;

        /// <summary>The cluster after the last one the feature applies to.</summary>
        public uint End;
    }

    /// <summary><c>hb_glyph_position_t</c>, in the font's scale.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GlyphPosition
    {
        /// <summary>How far the pen moves along x after the glyph.</summary>
        public int XAdvance;

        /// <summary>How far the pen moves along y after the glyph.</summary>
        public int YAdvance;

        /// <summary>How far the glyph is moved along x from the pen, without moving the pen.</summary>
        public int XOffset;

        /// <summary>How far the glyph is moved along y from the pen, without moving the pen.</summary>
        public int YOffset;

        /// <summary>A field private to HarfBuzz.</summary>
        public uint Var;
    }
}
