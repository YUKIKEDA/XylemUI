using XylemUI.Text;

namespace XylemUI.Testing;

/// <summary>
/// Resolves every text to one font, with no fallback (design §12.3, "Fonts"): the test font, a Noto Sans JP subset, unless another
/// font is given. Text then measures the same on every OS and in every CI image, which the OS's fonts do not. A family an app
/// registered (<c>AppFonts.Add</c>, design §4.5) is drawn with the registered font, and the characters it lacks with this one.
/// </summary>
/// <remarks>
/// The test font holds printable ASCII (U+0020 to U+007E), the characters of 日本語のテキスト, and <c>.notdef</c>; it has 1000 units
/// per em. It is opened the first time a text asks for a font, so that a tree with no text runs where HarfBuzz, which opens fonts,
/// is not built (Linux and macOS until rows P04 and P06).
/// </remarks>
public sealed class TestFontProvider : IFontProvider, IDisposable
{
    private readonly bool _owned;
    private Font? _font;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestFontProvider"/> class that resolves to the test font, and disposes it.
    /// </summary>
    public TestFontProvider() => _owned = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestFontProvider"/> class that resolves to a font the caller owns.
    /// </summary>
    /// <param name="font">The font.</param>
    public TestFontProvider(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        _font = font;
    }

    /// <summary>
    /// Gets the bytes of the test font, an OpenType file.
    /// </summary>
    public static ReadOnlyMemory<byte> TestFontData { get; } = Read();

    /// <inheritdoc/>
    public FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request)
    {
        _font ??= new Font(TestFontData.Span);

        // A family an app registered draws with its font (design §4.5, "App fonts"): an app's own font is the same on every OS.
        // The characters it lacks go to the test font, as the OS's fallback takes them outside the tests.
        if (
            AppFontSet.Generation > 0
            && request.Family is { } family
            && AppFontSet.Find(family, request.Weight, request.Slant, request.Stretch) is { } face
        )
        {
            bool has = face.Font.HasGlyph(CodePointAt(text, start));
            int end = start;
            while (end < text.Length && face.Font.HasGlyph(CodePointAt(text, end)) == has)
            {
                end += char.IsHighSurrogate(text[end]) && end + 1 < text.Length && char.IsLowSurrogate(text[end + 1]) ? 2 : 1;
            }

            return new FontMatch(has ? face.Font : _font, end - start, 1);
        }

        return new FontMatch(_font, text.Length - start, 1);
    }

    // The code point at a position: a surrogate pair's, or a lone surrogate's own value.
    private static int CodePointAt(ReadOnlySpan<char> text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? char.ConvertToUtf32(text[index], text[index + 1])
            : text[index];

    /// <summary>
    /// Disposes the test font if this provider opened it; a font given to the constructor stays its owner's.
    /// </summary>
    public void Dispose()
    {
        if (_owned)
        {
            _font?.Dispose();
            _font = null;
        }
    }

    private static byte[] Read()
    {
        using Stream stream =
            typeof(TestFontProvider).Assembly.GetManifestResourceStream("XylemUITestSans.ttf")
            ?? throw new InvalidOperationException("The test font is not embedded in XylemUI.Testing.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
