using System.Runtime.CompilerServices;
using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// The paint commands of one frame, in reused arrays of one stream per kind of data (design §5.0, §5.0.1). A renderer reads it
/// with <see cref="GetReader"/>; it never sees the UI tree.
/// </summary>
/// <remarks>
/// <see cref="Reset"/> keeps the arrays and only forgets how much of them is used, so a steady-state frame writes into the
/// slots of the last one and allocates nothing. A stream grows, to twice its size, only when a frame needs more.
/// </remarks>
public sealed class DisplayList
{
    private const int FloatsPerRect = 4;

    private CommandTag[] _commands;
    private float[] _rects;
    private uint[] _colors;
    private int[] _runLengths;
    private GlyphCoverage[] _runCoverages;
    private GlyphInstance[] _glyphs;
    private int _usedRects;
    private int _usedColors;
    private int _usedRuns;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayList"/> class.
    /// </summary>
    /// <param name="commandCapacity">The number of commands the streams hold before they first grow.</param>
    /// <param name="glyphCapacity">The number of glyphs the glyph stream holds before it first grows.</param>
    public DisplayList(int commandCapacity = 1024, int glyphCapacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(glyphCapacity);
        _commands = new CommandTag[commandCapacity];
        _rects = new float[commandCapacity * FloatsPerRect];
        _colors = new uint[commandCapacity];
        _runLengths = new int[commandCapacity];
        _runCoverages = new GlyphCoverage[commandCapacity];
        _glyphs = new GlyphInstance[glyphCapacity];
    }

    /// <summary>
    /// Gets the number of commands written since the last <see cref="Reset"/>.
    /// </summary>
    public int CommandCount { get; private set; }

    /// <summary>
    /// Gets the number of glyphs in the glyph runs written since the last <see cref="Reset"/>.
    /// </summary>
    public int GlyphCount { get; private set; }

    /// <summary>
    /// Gets the number of times a stream had to grow. A steady state does not raise it.
    /// </summary>
    public int GrowthCount { get; private set; }

    /// <summary>
    /// Forgets the commands, keeping the arrays for the next frame.
    /// </summary>
    public void Reset()
    {
        CommandCount = 0;
        GlyphCount = 0;
        _usedRects = 0;
        _usedColors = 0;
        _usedRuns = 0;
    }

    /// <summary>
    /// Appends a solid rectangle.
    /// </summary>
    /// <param name="x">The left edge, in physical pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="color">The color.</param>
    public void FillRect(float x, float y, float width, float height, Color color)
    {
        if (CommandCount == _commands.Length)
        {
            Grow();
        }

        _commands[CommandCount++] = CommandTag.FillRect;
        Span<float> rect = _rects.AsSpan(_usedRects, FloatsPerRect);
        rect[0] = x;
        rect[1] = y;
        rect[2] = width;
        rect[3] = height;
        _usedRects += FloatsPerRect;
        _colors[_usedColors++] = color.Packed;
    }

    /// <summary>
    /// Appends a run of glyphs in one color, whose coverage is already in the glyph atlas. An empty run appends nothing.
    /// </summary>
    /// <param name="glyphs">The glyphs.</param>
    /// <param name="color">The color of the text.</param>
    /// <param name="coverage">The kind of the glyphs' coverage: their pages are among the atlas's pages of this kind.</param>
    public void GlyphRun(ReadOnlySpan<GlyphInstance> glyphs, Color color, GlyphCoverage coverage = GlyphCoverage.Gray)
    {
        if (glyphs.IsEmpty)
        {
            return;
        }

        glyphs.CopyTo(ReserveGlyphs(glyphs.Length));
        EndGlyphRun(glyphs.Length, color, coverage);
    }

    /// <summary>
    /// Appends shaped text as glyph runs: puts each glyph in the atlas (rasterizing it the first time) and places it on the pen
    /// (design §5.0.3: the atlas changes while the display list is built and is uploaded after). Across the line a glyph is placed
    /// on a quarter of a pixel: its quad sits on the whole pixel, and the atlas holds the glyph drawn at the quarter
    /// (<see cref="GlyphPosition"/>). The baseline is a whole pixel. A color glyph (an emoji) is placed on the nearest whole pixel,
    /// and starts a run of its own kind where the kind changes (design §5.0.3, "Color glyphs"); otherwise the text is one run.
    /// </summary>
    /// <param name="glyphs">The glyphs, from <see cref="TextShaper.Shape"/> with <paramref name="font"/>.</param>
    /// <param name="font">The font the glyphs were shaped with.</param>
    /// <param name="pixelSize">The pixels per em: the size in logical pixels times the display's scale, not rounded.</param>
    /// <param name="atlas">The glyph atlas the renderer draws from.</param>
    /// <param name="x">The pen's start, in physical pixels.</param>
    /// <param name="baseline">The baseline, in physical pixels from the top.</param>
    /// <param name="color">The color of the text; a color glyph takes only its alpha, and its color for the layers in the text's color.</param>
    /// <param name="coverage">
    /// Grayscale, or ClearType where the window allows it (design §5.0.3, "ClearType"): the window decides it, not the text.
    /// </param>
    /// <returns>The advance of the text, in pixels (the sum of the glyph advances, unrounded).</returns>
    /// <remarks>
    /// A glyph with no ink (a space) only moves the pen. A glyph larger than an atlas plot (<see cref="GlyphAtlas.PlotSize"/>) is
    /// not drawn (roadmap row T3).
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The font was disposed and a glyph is not in the atlas.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixelSize"/> is not a positive number.</exception>
    public float DrawText(
        ReadOnlySpan<ShapedGlyph> glyphs,
        Font font,
        float pixelSize,
        GlyphAtlas atlas,
        float x,
        float baseline,
        Color color,
        GlyphCoverage coverage = GlyphCoverage.Gray
    )
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(atlas);
        if (!(pixelSize > 0) || !float.IsFinite(pixelSize))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelSize), pixelSize, "The size is not a positive number.");
        }

        // The pen moves in font units and is scaled once per glyph, so the rounding does not add up along the run.
        float scale = pixelSize / font.UnitsPerEm;
        Span<GlyphInstance> destination = ReserveGlyphs(glyphs.Length);
        int written = 0;
        int runStart = 0;
        GlyphCoverage runCoverage = coverage;
        long pen = 0;
        foreach (ShapedGlyph shaped in glyphs)
        {
            float position = x + ((pen + shaped.XOffset) * scale);
            int pixel = GlyphPosition.Snap(position, out int subpixel);
            if (
                atlas.TryGet(new GlyphKey(font, shaped.GlyphId, pixelSize, subpixel, coverage), color.Packed, out AtlasGlyph glyph)
                && glyph.Width > 0
            )
            {
                // A color glyph has its own kind of page and no subpixel steps: a new run where the kind changes, on a whole pixel.
                if (glyph.Coverage != runCoverage)
                {
                    if (written > runStart)
                    {
                        EndGlyphRun(written - runStart, color, runCoverage);
                        runStart = written;
                    }

                    runCoverage = glyph.Coverage;
                }

                int left = (glyph.Coverage == GlyphCoverage.Color ? GlyphPosition.Round(position) : pixel) + glyph.BearingX;
                int top = (int)MathF.Round(baseline - (shaped.YOffset * scale)) - glyph.BearingY;
                destination[written++] = new GlyphInstance(left, top, glyph.Width, glyph.Height, glyph.Page, glyph.X, glyph.Y);
            }

            pen += shaped.XAdvance;
        }

        if (written > runStart)
        {
            EndGlyphRun(written - runStart, color, runCoverage);
        }

        return pen * scale;
    }

    /// <summary>
    /// Returns a reader over the commands written since the last <see cref="Reset"/>.
    /// </summary>
    /// <returns>The reader. It must not outlive the next write.</returns>
    public DisplayListReader GetReader() => new(this);

    internal ReadOnlySpan<CommandTag> Commands => _commands.AsSpan(0, CommandCount);

    internal ReadOnlySpan<float> Rects => _rects.AsSpan(0, _usedRects);

    internal ReadOnlySpan<uint> Colors => _colors.AsSpan(0, _usedColors);

    internal ReadOnlySpan<int> RunLengths => _runLengths.AsSpan(0, _usedRuns);

    internal ReadOnlySpan<GlyphCoverage> RunCoverages => _runCoverages.AsSpan(0, _usedRuns);

    internal ReadOnlySpan<GlyphInstance> Glyphs => _glyphs.AsSpan(0, GlyphCount);

    // Room for `count` glyphs after the used ones; they count as used only when EndGlyphRun closes the run.
    private Span<GlyphInstance> ReserveGlyphs(int count)
    {
        if (GlyphCount + count > _glyphs.Length)
        {
            GrowGlyphs(GlyphCount + count);
        }

        return _glyphs.AsSpan(GlyphCount, count);
    }

    private void EndGlyphRun(int count, Color color, GlyphCoverage coverage)
    {
        if (CommandCount == _commands.Length)
        {
            Grow();
        }

        _commands[CommandCount++] = CommandTag.GlyphRun;
        _colors[_usedColors++] = color.Packed;
        _runCoverages[_usedRuns] = coverage;
        _runLengths[_usedRuns++] = count;
        GlyphCount += count;
    }

    // Every per-command stream grows with the command array, so one check per command is enough (the fast path stays one
    // comparison). The glyph stream has its own length, so it grows on its own.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow()
    {
        int capacity = _commands.Length * 2;
        Array.Resize(ref _commands, capacity);
        Array.Resize(ref _rects, capacity * FloatsPerRect);
        Array.Resize(ref _colors, capacity);
        Array.Resize(ref _runLengths, capacity);
        Array.Resize(ref _runCoverages, capacity);
        GrowthCount++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowGlyphs(int needed)
    {
        Array.Resize(ref _glyphs, Math.Max(needed, _glyphs.Length * 2));
        GrowthCount++;
    }
}
