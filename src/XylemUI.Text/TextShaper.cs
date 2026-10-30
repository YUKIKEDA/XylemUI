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
    // Made by the first Shape, so that a tree with no text runs where HarfBuzz is not built (a headless app on Linux and macOS
    // until rows P04 and P06, design §12.3).
    private nint _buffer;
    private bool _disposed;
    private ShapedGlyph[] _glyphs = new ShapedGlyph[64];
    private char[] _scratch = [];
    private HarfBuzz.Feature[] _features = [];

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

        return ShapeRange(font, text, 0, text.Length, language);
    }

    /// <summary>
    /// Returns a work array of this thread of at least <paramref name="length"/> characters, for a copy of a text to shape again
    /// (the line ends of Pass2, design §4.1). It is shared by every text this shaper shapes, so a text leaf does not keep one
    /// (design §3.2, "Text leaves"); its content is valid until the next call.
    /// </summary>
    /// <param name="length">The number of characters the array must hold at least.</param>
    /// <returns>The work array.</returns>
    internal char[] Scratch(int length)
    {
        if (_scratch.Length < length)
        {
            _scratch = new char[Math.Max(length, _scratch.Length * 2)];
        }

        return _scratch;
    }

    /// <summary>
    /// Shapes the part of <paramref name="text"/> from <paramref name="start"/>, with the text before and after it as context
    /// (HarfBuzz's pre- and post-context): the glyphs at its ends take the forms they take inside the whole text. The clusters are
    /// offsets in the whole text. The line breaking shapes the ends of its lines again so (design §4.1, Pass2).
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="text">The whole text, UTF-16.</param>
    /// <param name="start">The first code unit to shape.</param>
    /// <param name="length">The number of code units to shape.</param>
    /// <param name="language">The language of the text.</param>
    /// <param name="features">The OpenType features over the whole part (design §4.8); <see langword="null"/> for the font's
    /// defaults.</param>
    /// <returns>The glyphs in visual order. The span is valid until the next call.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is the default value.</exception>
    /// <exception cref="ObjectDisposedException">The shaper or the font was disposed.</exception>
    internal ReadOnlySpan<ShapedGlyph> ShapeRange(
        Font font,
        ReadOnlySpan<char> text,
        int start,
        int length,
        TextLanguage language,
        FontFeatures? features = null
    )
    {
        ArgumentNullException.ThrowIfNull(font);
        if (language.Handle == 0)
        {
            throw new ArgumentException("The language is the default value; use TextLanguage.FromTag.", nameof(language));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        nint harfBuzzFont = font.HarfBuzzFont;
        if (length == 0)
        {
            return [];
        }

        if (_buffer == 0)
        {
            _buffer = HarfBuzz.BufferCreate();
        }

        HarfBuzz.BufferClearContents(_buffer);
        fixed (char* chars = text)
        {
            HarfBuzz.BufferAddUtf16(_buffer, chars, text.Length, (uint)start, length);
        }

        // The language is given, never the process locale's: the script is guessed from the text (a guess keeps a language set
        // before it), and the direction is fixed to left to right for now.
        HarfBuzz.BufferSetLanguage(_buffer, language.Handle);
        HarfBuzz.BufferGuessSegmentProperties(_buffer);
        HarfBuzz.BufferSetDirection(_buffer, HarfBuzz.DirectionLtr);
        int featureCount = features is null ? 0 : Features(features.Features);
        fixed (HarfBuzz.Feature* pinned = _features)
        {
            HarfBuzz.Shape(harfBuzzFont, _buffer, pinned, (uint)featureCount);
        }

        uint glyphCount;
        HarfBuzz.GlyphInfo* infos = HarfBuzz.BufferGetGlyphInfos(_buffer, &glyphCount);
        HarfBuzz.GlyphPosition* positions = HarfBuzz.BufferGetGlyphPositions(_buffer, null);
        int count = (int)glyphCount;
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
                positions[i].YOffset,
                (infos[i].Mask & HarfBuzz.GlyphFlagUnsafeToBreak) != 0
            );
        }

        return _glyphs.AsSpan(0, count);
    }

    /// <summary>
    /// Releases the HarfBuzz buffer.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        if (_buffer != 0)
        {
            HarfBuzz.BufferDestroy(_buffer);
            _buffer = 0;
        }
    }

    // The features as HarfBuzz takes them, each over every cluster (HB_FEATURE_GLOBAL_START to HB_FEATURE_GLOBAL_END), in the kept
    // array. Returns their count.
    private int Features(ReadOnlySpan<FontFeature> features)
    {
        if (_features.Length < features.Length)
        {
            _features = new HarfBuzz.Feature[Math.Max(features.Length, _features.Length * 2)];
        }

        for (int i = 0; i < features.Length; i++)
        {
            _features[i] = new HarfBuzz.Feature
            {
                Tag = features[i].Tag,
                Value = features[i].Value,
                Start = 0,
                End = uint.MaxValue,
            };
        }

        return features.Length;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int needed) => _glyphs = new ShapedGlyph[Math.Max(needed, _glyphs.Length * 2)];
}
