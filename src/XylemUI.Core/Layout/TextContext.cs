using XylemUI.Text;
using XylemUI.Text.LineBreak;

namespace XylemUI;

/// <summary>
/// The tools a UI thread shapes and breaks its text leaves with (design §3.2, "Text leaves"): one for the thread, handed to each
/// <see cref="UiTree"/> of it, so that every window shares the fonts its provider keeps open.
/// </summary>
/// <remarks>
/// It belongs to the UI thread, as the provider and the shaper do (design §4.5). The one who creates it disposes it, when the
/// thread's trees are done with it.
/// </remarks>
internal sealed class TextContext : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextContext"/> class.
    /// </summary>
    /// <param name="fonts">The fonts of the OS, or a provider of tests.</param>
    public TextContext(IFontProvider fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = fonts;
    }

    // The ranges a text writer writes (design §4.8), rented with the buffer below: one for the thread, and a leaf that takes
    // written ranges gives its old ones back to it.
    private readonly TextRangeWriter _ranges = new();

    // The buffer a text writer writes into before its text is compared with the leaf's (design §2.13, "TextBuilder"). One for the
    // thread, as the shaper's scratch is (design §3.2, "Memory"): a buffer in each leaf made every text leaf 8 bytes larger, and
    // V1a's bound on a leaf's first measurement is exact. A leaf that takes a written text gives its old buffer back here.
    private char[]? _spare;
    private bool _spareRented;

    /// <summary>
    /// Gets the fonts the leaves' text is split into runs of.
    /// </summary>
    public IFontProvider Fonts { get; }

    /// <summary>
    /// Gets the shaper.
    /// </summary>
    public TextShaper Shaper { get; } = new();

    /// <summary>
    /// Gets the line breaker.
    /// </summary>
    public LineBreaker Breaker { get; } = new();

    /// <summary>
    /// Gets where the glyphs' outlines cross the underlines and overlines drawn under them (design §4.8), kept for every window of
    /// the thread.
    /// </summary>
    public GlyphIntercepts Intercepts { get; } = new();

    /// <summary>
    /// Gets the list a text is split into font runs in, reused for every leaf.
    /// </summary>
    public List<FontRun> Runs { get; } = [];

    /// <summary>
    /// Gets the list of what each of <see cref="Runs"/> is shaped with besides its font, reused for every leaf.
    /// </summary>
    public List<RunStyle> RunStyles { get; } = [];

    /// <summary>
    /// Gets the list of the positions of a text's inline objects as it is shaped, reused for every leaf (design §4.8).
    /// </summary>
    public List<int> Rooms { get; } = [];

    /// <summary>
    /// Gets the list of the widths of a text's inline objects, by the index of <see cref="Rooms"/>.
    /// </summary>
    public List<float> RoomWidths { get; } = [];

    /// <summary>
    /// Gets the list of the widths of a text's inline objects under a min-content width, by the index of <see cref="Rooms"/>.
    /// </summary>
    public List<float> RoomMinWidths { get; } = [];

    /// <summary>
    /// Gets the list of the widths of a text's inline objects under a max-content width, by the index of <see cref="Rooms"/>.
    /// </summary>
    public List<float> RoomMaxWidths { get; } = [];

#if DEBUG
    /// <summary>
    /// Gets the entry Debug builds break a leaf's text into to check its content widths, apart from the leaf's cache.
    /// </summary>
    public LineBreakEntry CheckEntry { get; } = new();
#endif

    /// <summary>
    /// Releases the shaper.
    /// </summary>
    public void Dispose() => Shaper.Dispose();

    /// <summary>
    /// Returns the buffer for a text writer, at least 16 characters long. A writer that starts while another writes (a builder that
    /// runs an effect that writes) gets a buffer of its own.
    /// </summary>
    /// <param name="shared"><see langword="true"/> when it is the thread's buffer, which <see cref="ReturnSpare"/> takes back.</param>
    /// <param name="ranges">The writer of the text's ranges, reset: the thread's with its buffer, otherwise one of its own.</param>
    /// <returns>The buffer.</returns>
    internal char[] RentSpare(out bool shared, out TextRangeWriter ranges)
    {
        if (_spareRented)
        {
            shared = false;
            ranges = new TextRangeWriter();
            return new char[16];
        }

        _spareRented = true;
        shared = true;
        ranges = _ranges;
        ranges.Reset();
        return _spare ??= new char[16];
    }

    /// <summary>
    /// Takes the thread's buffer back after a write: the one the writer grew to, or the one a leaf gave back for it.
    /// </summary>
    /// <param name="buffer">The buffer; <see langword="null"/> when the leaf that took the written text had none.</param>
    internal void ReturnSpare(char[]? buffer)
    {
        _spare = buffer;
        _spareRented = false;
    }
}
