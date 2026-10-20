using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;

namespace XylemUI.Platform.Windows;

/// <summary>
/// DirectWrite's <c>IDWriteTextAnalysisSource</c> for one text at a time, which <c>IDWriteFontFallback::MapCharacters</c> calls
/// back for the text and its locale: a native object of a vtable of unmanaged functions and the text's pointer, length, and
/// locale. Left to right, without number substitution.
/// </summary>
/// <remarks>
/// The caller points it at a pinned text and locale, makes the call, and clears it; DirectWrite uses the source only during the
/// call. Its owner frees it (<see cref="Free"/>), so the reference count only answers COM.
/// </remarks>
internal unsafe struct TextAnalysisSource
{
    private static readonly void** Vtable = CreateVtable();

    private void** _vtable;
    private char* _text;
    private uint _length;
    private char* _locale;

    /// <summary>
    /// Allocates a source with no text.
    /// </summary>
    /// <returns>The source, as DirectWrite takes it.</returns>
    public static IDWriteTextAnalysisSource* Create()
    {
        var source = (TextAnalysisSource*)NativeMemory.AllocZeroed((nuint)sizeof(TextAnalysisSource));
        source->_vtable = Vtable;
        return (IDWriteTextAnalysisSource*)source;
    }

    /// <summary>
    /// Points the source at a text and its locale, both pinned and the locale null-terminated, until <see cref="Clear"/>.
    /// </summary>
    /// <param name="source">A source from <see cref="Create"/>.</param>
    /// <param name="text">The text.</param>
    /// <param name="length">The number of UTF-16 characters of the text.</param>
    /// <param name="locale">The locale name.</param>
    public static void Set(IDWriteTextAnalysisSource* source, char* text, uint length, char* locale)
    {
        var self = (TextAnalysisSource*)source;
        self->_text = text;
        self->_length = length;
        self->_locale = locale;
    }

    /// <summary>
    /// Points the source at no text.
    /// </summary>
    /// <param name="source">A source from <see cref="Create"/>.</param>
    public static void Clear(IDWriteTextAnalysisSource* source) => Set(source, null, 0, null);

    /// <summary>
    /// Frees a source from <see cref="Create"/>.
    /// </summary>
    /// <param name="source">The source.</param>
    public static void Free(IDWriteTextAnalysisSource* source) => NativeMemory.Free(source);

    private static void** CreateVtable()
    {
        // The order of IUnknown, then of IDWriteTextAnalysisSource in dwrite.h. One table for the process, never freed.
        void** table = (void**)NativeMemory.Alloc((nuint)(8 * sizeof(void*)));
        table[0] = (delegate* unmanaged<TextAnalysisSource*, Guid*, void**, int>)&QueryInterface;
        table[1] = (delegate* unmanaged<TextAnalysisSource*, uint>)&AddRef;
        table[2] = (delegate* unmanaged<TextAnalysisSource*, uint>)&Release;
        table[3] = (delegate* unmanaged<TextAnalysisSource*, uint, char**, uint*, int>)&GetTextAtPosition;
        table[4] = (delegate* unmanaged<TextAnalysisSource*, uint, char**, uint*, int>)&GetTextBeforePosition;
        table[5] = (delegate* unmanaged<TextAnalysisSource*, DWRITE_READING_DIRECTION>)&GetParagraphReadingDirection;
        table[6] = (delegate* unmanaged<TextAnalysisSource*, uint, uint*, char**, int>)&GetLocaleName;
        table[7] = (delegate* unmanaged<TextAnalysisSource*, uint, uint*, void**, int>)&GetNumberSubstitution;
        return table;
    }

    [UnmanagedCallersOnly]
    private static int QueryInterface(TextAnalysisSource* self, Guid* iid, void** result)
    {
        if (*iid == TerraFX.Interop.Windows.IID.IID_IDWriteTextAnalysisSource || *iid == TerraFX.Interop.Windows.IID.IID_IUnknown)
        {
            *result = self;
            return 0;
        }

        *result = null;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    [UnmanagedCallersOnly]
    private static uint AddRef(TextAnalysisSource* self) => 1;

    [UnmanagedCallersOnly]
    private static uint Release(TextAnalysisSource* self) => 1;

    [UnmanagedCallersOnly]
    private static int GetTextAtPosition(TextAnalysisSource* self, uint textPosition, char** textString, uint* textLength)
    {
        bool inside = textPosition < self->_length;
        *textString = inside ? self->_text + textPosition : null;
        *textLength = inside ? self->_length - textPosition : 0;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int GetTextBeforePosition(TextAnalysisSource* self, uint textPosition, char** textString, uint* textLength)
    {
        bool inside = textPosition > 0 && textPosition <= self->_length;
        *textString = inside ? self->_text : null;
        *textLength = inside ? textPosition : 0;
        return 0;
    }

    // Right to left comes with row P03.
    [UnmanagedCallersOnly]
    private static DWRITE_READING_DIRECTION GetParagraphReadingDirection(TextAnalysisSource* self) =>
        DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT;

    [UnmanagedCallersOnly]
    private static int GetLocaleName(TextAnalysisSource* self, uint textPosition, uint* textLength, char** localeName)
    {
        *localeName = self->_locale;
        *textLength = textPosition < self->_length ? self->_length - textPosition : 0;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int GetNumberSubstitution(TextAnalysisSource* self, uint textPosition, uint* textLength, void** numberSubstitution)
    {
        *numberSubstitution = null;
        *textLength = textPosition < self->_length ? self->_length - textPosition : 0;
        return 0;
    }
}
