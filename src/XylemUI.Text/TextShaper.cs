using System.Runtime.CompilerServices;
using XylemUI.Text.Interop;

namespace XylemUI.Text;

/// <summary>
/// Shapes a run of text with HarfBuzz: one font, left to right, one line (design §4.1; the line breaking of the design is roadmap
/// row T1, bidirectional text P03).
/// </summary>
/// <remarks>
/// The UTF-16 text goes to HarfBuzz as it is (<c>hb_buffer_add_utf16</c>), without a conversion. The HarfBuzz buffer and the
/// result array are kept and reused, so a steady state allocates nothing. One shaper belongs to one thread.
/// </remarks>
public sealed unsafe class TextShaper : IDisposable
{
    private nint _buffer = HarfBuzz.BufferCreate();
    private ShapedGlyph[] _glyphs = new ShapedGlyph[64];

    /// <summary>
    /// Shapes <paramref name="text"/> with <paramref name="font"/>.
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="text">The text, UTF-16.</param>
    /// <param name="language">
    /// The language of the text, usually the UI's; it picks the font's language forms (<c>locl</c>), such as the Japanese or the
    /// Chinese shape of a Han character.
    /// </param>
    /// <returns>The glyphs in visual order (left to right). The span is valid until the next call.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is the default value.</exception>
    /// <exception cref="ObjectDisposedException">The shaper or the font was disposed.</exception>
    public ReadOnlySpan<ShapedGlyph> Shape(Font font, ReadOnlySpan<char> text, TextLanguage language)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (language.Handle == 0)
        {
            throw new ArgumentException("The language is the default value; use TextLanguage.FromTag.", nameof(language));
        }

        ObjectDisposedException.ThrowIf(_buffer == 0, this);
        nint harfBuzzFont = font.HarfBuzzFont;
        if (text.IsEmpty)
        {
            return [];
        }

        HarfBuzz.BufferClearContents(_buffer);
        fixed (char* chars = text)
        {
            HarfBuzz.BufferAddUtf16(_buffer, chars, text.Length, 0, text.Length);
        }

        // The language is given, never the process locale's: the script is guessed from the text (a guess keeps a language set
        // before it), and the direction is fixed to left to right for now.
        HarfBuzz.BufferSetLanguage(_buffer, language.Handle);
        HarfBuzz.BufferGuessSegmentProperties(_buffer);
        HarfBuzz.BufferSetDirection(_buffer, HarfBuzz.DirectionLtr);
        HarfBuzz.Shape(harfBuzzFont, _buffer, null, 0);

        uint length;
        HarfBuzz.GlyphInfo* infos = HarfBuzz.BufferGetGlyphInfos(_buffer, &length);
        HarfBuzz.GlyphPosition* positions = HarfBuzz.BufferGetGlyphPositions(_buffer, null);
        int count = (int)length;
        if (count > _glyphs.Length)
        {
            Grow(count);
        }

        for (int i = 0; i < count; i++)
        {
            _glyphs[i] = new ShapedGlyph(
                infos[i].Codepoint,
                (int)infos[i].Cluster,
                positions[i].XAdvance,
                positions[i].XOffset,
                positions[i].YOffset
            );
        }

        return _glyphs.AsSpan(0, count);
    }

    /// <summary>
    /// Releases the HarfBuzz buffer.
    /// </summary>
    public void Dispose()
    {
        if (_buffer != 0)
        {
            HarfBuzz.BufferDestroy(_buffer);
            _buffer = 0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int needed) => _glyphs = new ShapedGlyph[Math.Max(needed, _glyphs.Length * 2)];
}
