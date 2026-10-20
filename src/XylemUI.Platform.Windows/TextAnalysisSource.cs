using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace XylemUI.Platform.Windows;

/// <summary>
/// DirectWrite's <c>IDWriteTextAnalysisSource</c>, which <c>IDWriteFontFallback::MapCharacters</c> calls back for the text and
/// its locale. The methods keep the order of the vtable in <c>dwrite.h</c>; the OS calls them, so the interface is source-
/// generated COM (.cursor/rules/interop.mdc).
/// </summary>
[GeneratedComInterface]
[Guid("688e1a58-5094-47c8-adc8-fbcea60ae92b")]
internal unsafe partial interface IDWriteTextAnalysisSourceCallback
{
    /// <summary>Points at the text from a position to its end.</summary>
    /// <param name="textPosition">The position in the text.</param>
    /// <param name="textString">Receives the text.</param>
    /// <param name="textLength">Receives the number of characters.</param>
    /// <returns>S_OK.</returns>
    [PreserveSig]
    int GetTextAtPosition(uint textPosition, char** textString, uint* textLength);

    /// <summary>Points at the text from its start to a position.</summary>
    /// <param name="textPosition">The position in the text.</param>
    /// <param name="textString">Receives the text.</param>
    /// <param name="textLength">Receives the number of characters.</param>
    /// <returns>S_OK.</returns>
    [PreserveSig]
    int GetTextBeforePosition(uint textPosition, char** textString, uint* textLength);

    /// <summary>Returns the reading direction of the paragraph (a <c>DWRITE_READING_DIRECTION</c>, not an <c>HRESULT</c>).</summary>
    /// <returns>DWRITE_READING_DIRECTION_LEFT_TO_RIGHT (0) or another direction.</returns>
    [PreserveSig]
    int GetParagraphReadingDirection();

    /// <summary>Points at the locale of the text from a position.</summary>
    /// <param name="textPosition">The position in the text.</param>
    /// <param name="textLength">Receives the number of characters.</param>
    /// <param name="localeName">Receives the null-terminated locale name.</param>
    /// <returns>S_OK.</returns>
    [PreserveSig]
    int GetLocaleName(uint textPosition, uint* textLength, char** localeName);

    /// <summary>Returns the number substitution of the text from a position: none.</summary>
    /// <param name="textPosition">The position in the text.</param>
    /// <param name="textLength">Receives the number of characters.</param>
    /// <param name="numberSubstitution">Receives the number substitution.</param>
    /// <returns>S_OK.</returns>
    [PreserveSig]
    int GetNumberSubstitution(uint textPosition, uint* textLength, void** numberSubstitution);
}

/// <summary>
/// One text at a time for <c>MapCharacters</c>: the caller points it at a pinned text and a locale, makes the call, and clears
/// it. Left to right, without number substitution.
/// </summary>
[GeneratedComClass]
internal sealed unsafe partial class TextAnalysisSource : IDWriteTextAnalysisSourceCallback
{
    private char* _text;
    private uint _length;
    private char* _locale;

    public void Set(char* text, uint length, char* locale)
    {
        _text = text;
        _length = length;
        _locale = locale;
    }

    public void Clear() => Set(null, 0, null);

    public int GetTextAtPosition(uint textPosition, char** textString, uint* textLength)
    {
        if (textPosition >= _length)
        {
            *textString = null;
            *textLength = 0;
        }
        else
        {
            *textString = _text + textPosition;
            *textLength = _length - textPosition;
        }

        return 0;
    }

    public int GetTextBeforePosition(uint textPosition, char** textString, uint* textLength)
    {
        if (textPosition == 0 || textPosition > _length)
        {
            *textString = null;
            *textLength = 0;
        }
        else
        {
            *textString = _text;
            *textLength = textPosition;
        }

        return 0;
    }

    // DWRITE_READING_DIRECTION_LEFT_TO_RIGHT: right to left comes with row P03.
    public int GetParagraphReadingDirection() => 0;

    public int GetLocaleName(uint textPosition, uint* textLength, char** localeName)
    {
        *localeName = _locale;
        *textLength = textPosition >= _length ? 0 : _length - textPosition;
        return 0;
    }

    public int GetNumberSubstitution(uint textPosition, uint* textLength, void** numberSubstitution)
    {
        *numberSubstitution = null;
        *textLength = textPosition >= _length ? 0 : _length - textPosition;
        return 0;
    }
}
