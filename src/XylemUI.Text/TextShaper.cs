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
    /// <returns>The glyphs in visual order (left to right). The span is valid until the next call.</returns>
    /// <exception cref="ObjectDisposedException">The shaper was disposed.</exception>
    public ReadOnlySpan<ShapedGlyph> Shape(Font font, ReadOnlySpan<char> text)
    {
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(_buffer == 0, this);
        if (text.IsEmpty)
        {
            return [];
        }

        HarfBuzz.BufferClearContents(_buffer);
        fixed (char* chars = text)
        {
            HarfBuzz.BufferAddUtf16(_buffer, chars, text.Length, 0, text.Length);
        }

        // The script and language are guessed from the text; the direction is fixed to left to right for now.
        HarfBuzz.BufferGuessSegmentProperties(_buffer);
        HarfBuzz.BufferSetDirection(_buffer, HarfBuzz.DirectionLtr);
        HarfBuzz.Shape(font.HarfBuzzFont, _buffer, null, 0);

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
