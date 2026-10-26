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
    /// Gets the list a text is split into font runs in, reused for every leaf.
    /// </summary>
    public List<FontRun> Runs { get; } = [];

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
}
