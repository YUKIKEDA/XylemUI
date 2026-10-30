using XylemUI.Text;

namespace XylemUI.Testing;

/// <summary>
/// Resolves every text to one font, with no fallback (design §12.3, "Fonts"): the test font, a Noto Sans JP subset, unless another
/// font is given. Text then measures the same on every OS and in every CI image, which the OS's fonts do not.
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
        return new FontMatch(_font, text.Length - start, 1);
    }

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
