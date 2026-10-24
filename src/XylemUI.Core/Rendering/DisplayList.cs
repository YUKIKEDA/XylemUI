using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// The paint commands of one frame, in reused arrays of one stream per kind of data (design §5.0, §5.0.1). A renderer reads it
/// with <see cref="GetReader"/>; it never sees the UI tree.
/// </summary>
/// <remarks>
/// <see cref="Reset"/> keeps the arrays and only forgets how much of them is used, so a steady-state frame writes into the
/// slots of the last one and allocates nothing. A stream grows, to twice its size, only when a frame needs more.
/// <see cref="DrawText"/> writes the glyphs the atlas has and leaves room for the others; <see cref="ResolveGlyphs"/> fills it
/// before the list is read (design §5.0.3, "Glyphs seen for the first time").
/// </remarks>
public sealed class DisplayList
{
    private const int FloatsPerRect = 4;

    // Numbers each frame that writes a brush, across every list, so a brush's ListFrame tells which list's frame placed it.
    private static long _brushFrames;

    private CommandTag[] _commands;
    private float[] _rects;
    private uint[] _colors;
    private int[] _runLengths;
    private GlyphCoverage[] _runCoverages;
    private GlyphInstance[] _glyphs;

    // What only ResolveGlyphs uses, apart from the streams, after the fields of a frame with nothing pending, which keep their
    // places (row T5).
    private Resolution _resolve = new();

    // The streams of Shape and Shadow commands, made with the first one (row R4).
    private ShapeStreams? _shapeStreams;
    private long _brushFrame;
    private int _usedShapes;
    private int _brushCount;
    private int _usedRects;
    private int _usedColors;
    private int _usedRuns;
    private int _pendingCount;

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
    /// Gets the number of Shape and Shadow commands written since the last <see cref="Reset"/> (row R4), for a renderer that sizes
    /// its buffers.
    /// </summary>
    public int ShapeCount => _usedShapes;

    /// <summary>
    /// Gets a value indicating whether <see cref="DrawText"/> left room for glyphs that <see cref="ResolveGlyphs"/> has not filled
    /// yet. Such a list cannot be read.
    /// </summary>
    public bool HasPendingGlyphs => _pendingCount > 0;

    /// <summary>
    /// Gets the scale of the frame: the window's <c>ScaleFactor</c>, by which the logical pixels the list is given are multiplied
    /// (design §6.1.1, "Windows' display scale").
    /// </summary>
    public float Scale { get; private set; } = 1;

    /// <summary>
    /// Forgets the commands, keeping the arrays for the next frame, and takes the frame's scale.
    /// </summary>
    /// <param name="scale">
    /// The window's scale (1.25 at 125 %): the list multiplies the logical pixels it is given by it. It defaults to 1, where logical
    /// pixels are physical ones, for the tests and the code that writes in the screen's pixels; a frame of a window passes its
    /// <c>ScaleFactor</c>, or its text and rectangles are drawn too small at 125 %.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not a positive number.</exception>
    public void Reset(float scale = 1)
    {
        if (!(scale > 0) || !float.IsFinite(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale is not a positive number.");
        }

        Scale = scale;
        CommandCount = 0;
        GlyphCount = 0;
        _usedRects = 0;
        _usedColors = 0;
        _usedRuns = 0;
        _pendingCount = 0;
        _usedShapes = 0;
        _brushCount = 0;
    }

    /// <summary>
    /// Appends a solid rectangle, given in logical pixels, with each edge on the nearest pixel at <see cref="Scale"/>.
    /// </summary>
    /// <param name="x">The left edge, in logical pixels.</param>
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

        // Each edge on the nearest pixel, so that rectangles that touch in logical pixels touch on the screen (design §6.1.1, "Pixel
        // snapping").
        float scale = Scale;
        float left = RoundToPixel(x * scale);
        float top = RoundToPixel(y * scale);
        _commands[CommandCount++] = CommandTag.FillRect;
        Span<float> rect = _rects.AsSpan(_usedRects, FloatsPerRect);
        rect[0] = left;
        rect[1] = top;
        rect[2] = RoundToPixel((x + width) * scale) - left;
        rect[3] = RoundToPixel((y + height) * scale) - top;
        _usedRects += FloatsPerRect;
        _colors[_usedColors++] = color.Packed;
    }

    /// <summary>
    /// Appends a rounded rectangle with a border, filled with a paint, given in logical pixels (design §5.0, "Shapes, brushes, and
    /// shadows"). Its edges are on the nearest pixel at <see cref="Scale"/>, as <see cref="FillRect(float, float, float, float, Color)"/>
    /// puts them; the radii and the border's widths are multiplied by the scale and not rounded. A solid rectangle with no radius
    /// and no border is written as that solid rectangle.
    /// </summary>
    /// <param name="x">The left edge, in logical pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="fill">The paint of the inside.</param>
    /// <param name="radii">The radius of each corner; when the two on a side sum past it, all are scaled down as CSS does.</param>
    /// <param name="border">The border, drawn inside the rectangle over the fill, or <see langword="null"/>.</param>
    public void FillRect(float x, float y, float width, float height, in Paint fill, in CornerRadii radii, Border? border = null)
    {
        if (fill.Gradient is null && border is null && radii.TopLeft == 0 && radii.TopRight == 0 && radii.BottomRight == 0 && radii.BottomLeft == 0)
        {
            FillRect(x, y, width, height, fill.Color);
            return;
        }

        float scale = Scale;
        ref ShapeRecord record = ref AppendShape(CommandTag.Shape, x, y, width, height, fill.Color);
        record = default;
        record.Radii = Scaled(Fitted(radii, width, height), scale);
        record.FillBrush = BrushIndex(fill.Gradient);
        record.BorderBrush = -1;
        if (border is { } edges)
        {
            BorderWidths widths = edges.Widths;
            record.BorderWidths = new BorderWidths(widths.Left * scale, widths.Top * scale, widths.Right * scale, widths.Bottom * scale);
            record.BorderColor = edges.Paint.Color.Packed;
            record.BorderBrush = BrushIndex(edges.Paint.Gradient);
        }
    }

    /// <summary>
    /// Appends the shadow of a rounded rectangle given in logical pixels (design §5.0, "Shapes, brushes, and shadows"). The shape's
    /// edges are on the nearest pixel, as the shape's own are; the offset, blur, and spread are multiplied by <see cref="Scale"/>
    /// and not rounded. Write an outer shadow before its shape, and an inset one after the shape's fill and before its border.
    /// </summary>
    /// <param name="x">The left edge of the shape that casts the shadow, in logical pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="radii">The radius of each corner of the shape.</param>
    /// <param name="shadow">The shadow, with CSS's values.</param>
    public void DrawShadow(float x, float y, float width, float height, in CornerRadii radii, in BoxShadow shadow)
    {
        float scale = Scale;
        ref ShapeRecord record = ref AppendShape(CommandTag.Shadow, x, y, width, height, shadow.Color);
        record = default;
        record.Radii = Scaled(Fitted(radii, width, height), scale);
        record.FillBrush = -1;
        record.BorderBrush = -1;
        record.Offset = shadow.Offset * scale;
        record.Sigma = shadow.BlurRadius * 0.5f * scale;
        record.Spread = shadow.Spread * scale;
        record.Inset = shadow.Inset;
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
    /// Appends shaped text as glyph runs, placing each glyph on the pen: a glyph the atlas has is written now, and room is left for
    /// one it does not have yet, which <see cref="ResolveGlyphs"/> rasterizes with the other new glyphs of the frame (design §5.0.3,
    /// "Glyphs seen for the first time"; the atlas is uploaded after). Across the line a glyph is placed
    /// on a quarter of a pixel: its quad sits on the whole pixel, and the atlas holds the glyph drawn at the quarter
    /// (<see cref="GlyphPosition"/>). The baseline is a whole pixel. A color glyph (an emoji) is placed on the nearest whole pixel,
    /// and starts a run of its own kind where the kind changes (design §5.0.3, "Color glyphs"); otherwise the text is one run.
    /// Bilevel text (<paramref name="aliased"/>) puts every glyph on the nearest whole pixel and keeps the advances, so it is as
    /// wide as antialiased text (design §5.0.3, "When fonts are not smoothed").
    /// </summary>
    /// <param name="glyphs">The glyphs, from <see cref="TextShaper.Shape"/> with <paramref name="font"/>.</param>
    /// <param name="font">The font the glyphs were shaped with.</param>
    /// <param name="size">The size in logical pixels per em; the glyphs are drawn at it times <see cref="Scale"/>, not rounded.</param>
    /// <param name="atlas">The glyph atlas the renderer draws from.</param>
    /// <param name="x">The pen's start, in logical pixels.</param>
    /// <param name="baseline">The baseline, in logical pixels from the top.</param>
    /// <param name="color">The color of the text; a color glyph takes only its alpha, and its color for the layers in the text's color.</param>
    /// <param name="coverage">
    /// Grayscale, or ClearType where the window allows it (design §5.0.3, "ClearType"): the window decides it, not the text.
    /// </param>
    /// <param name="aliased">
    /// Whether the glyphs are bilevel, because the OS does not smooth the edges of fonts; the window decides it, as it does
    /// <paramref name="coverage"/>, which is then <see cref="GlyphCoverage.Gray"/>.
    /// </param>
    /// <returns>The advance of the text, in logical pixels (the sum of the glyph advances, unrounded), for a layout that adds it.</returns>
    /// <remarks>
    /// A glyph with no ink (a space) only moves the pen. A glyph larger than an atlas plot (<see cref="GlyphAtlas.PlotSize"/>) is
    /// not drawn (roadmap row T3). The returned advance does not depend on the atlas.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not a positive number.</exception>
    /// <exception cref="ArgumentException"><paramref name="aliased"/> is set with ClearType coverage.</exception>
    public float DrawText(
        ReadOnlySpan<ShapedGlyph> glyphs,
        Font font,
        float size,
        GlyphAtlas atlas,
        float x,
        float baseline,
        Color color,
        GlyphCoverage coverage = GlyphCoverage.Gray,
        bool aliased = false
    )
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(atlas);
        float pixelSize = size * Scale;
        if (!(pixelSize > 0) || !float.IsFinite(pixelSize))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The size is not a positive number.");
        }

        if (aliased && coverage == GlyphCoverage.Rgb)
        {
            throw new ArgumentException("Bilevel text has no ClearType coverage.", nameof(aliased));
        }

        // The pen moves in font units and is scaled once per glyph, so the rounding does not add up along the run. The pen and the
        // baseline are in physical pixels from here on.
        float scale = pixelSize / font.UnitsPerEm;
        x *= Scale;
        baseline *= Scale;
        Span<GlyphInstance> destination = ReserveGlyphs(glyphs.Length);
        int first = GlyphCount;
        int written = 0;
        int runStart = 0;
        GlyphCoverage runCoverage = coverage;
        long pen = 0;
        foreach (ShapedGlyph shaped in glyphs)
        {
            float position = x + ((pen + shaped.XOffset) * scale);
            int subpixel = 0;
            int pixel = aliased ? GlyphPosition.Round(position) : GlyphPosition.Snap(position, out subpixel);
            var key = new GlyphKey(font, shaped.GlyphId, pixelSize, subpixel, coverage, Aliased: aliased);
            int row = (int)MathF.Round(baseline - (shaped.YOffset * scale));
            GlyphLookup lookup = atlas.Lookup(key, color.Packed, out AtlasGlyph glyph);
            if (lookup == GlyphLookup.Found && glyph.Width > 0)
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

                destination[written++] = Place(glyph, position, pixel, row);
            }
            else if (lookup == GlyphLookup.Missing)
            {
                // Room for the glyph in a run of the text's own kind; ResolveGlyphs fills it, or drops it, and splits the run where
                // the glyph turns out to be a color glyph.
                if (runCoverage != coverage)
                {
                    if (written > runStart)
                    {
                        EndGlyphRun(written - runStart, color, runCoverage);
                        runStart = written;
                    }

                    runCoverage = coverage;
                }

                destination[written] = Pend(first + written, key, color, position, pixel, row);
                written++;
            }

            pen += shaped.XAdvance;
        }

        if (written > runStart)
        {
            EndGlyphRun(written - runStart, color, runCoverage);
        }

        return pen * (size / font.UnitsPerEm);
    }

    /// <summary>
    /// Rasterizes the glyphs <see cref="DrawText"/> left room for, together (on the atlas's worker threads when there are enough),
    /// and fills their room: a glyph with no ink or one the atlas refuses leaves the list, and a run splits where a glyph turns out
    /// to be a color glyph (design §5.0.3, "Glyphs seen for the first time"). Called once the list is built and before it is read;
    /// it does nothing when no glyph is pending, so a steady-state frame allocates nothing here.
    /// </summary>
    /// <param name="atlas">The atlas the pending glyphs were looked up in.</param>
    /// <exception cref="InvalidOperationException">A rasterizer could not draw a glyph.</exception>
    /// <exception cref="ObjectDisposedException">A font was disposed and its glyph is not in the atlas.</exception>
    public void ResolveGlyphs(GlyphAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        if (_pendingCount == 0)
        {
            return;
        }

        int requests = _resolve.RequestIndex.Count;
        if (_resolve.Resolved.Length < requests)
        {
            Array.Resize(ref _resolve.Resolved, Math.Max(requests, _resolve.Resolved.Length * 2));
            Array.Resize(ref _resolve.ResolvedFound, _resolve.Resolved.Length);
        }

        atlas.GetMany(_resolve.Requests.AsSpan(0, requests), _resolve.Resolved, _resolve.ResolvedFound);
        Rebuild();
        _pendingCount = 0;
        _resolve.RequestIndex.Clear();
    }

    /// <summary>
    /// Returns a reader over the commands written since the last <see cref="Reset"/>.
    /// </summary>
    /// <returns>The reader. It must not outlive the next write.</returns>
    public DisplayListReader GetReader()
    {
        if (_pendingCount > 0)
        {
            Invariant.Fail("The display list has glyphs left for ResolveGlyphs; resolve them before reading the list.");
        }

        return new(this);
    }

    internal ReadOnlySpan<CommandTag> Commands => _commands.AsSpan(0, CommandCount);

    internal ReadOnlySpan<float> Rects => _rects.AsSpan(0, _usedRects);

    internal ReadOnlySpan<uint> Colors => _colors.AsSpan(0, _usedColors);

    internal ReadOnlySpan<int> RunLengths => _runLengths.AsSpan(0, _usedRuns);

    internal ReadOnlySpan<GlyphCoverage> RunCoverages => _runCoverages.AsSpan(0, _usedRuns);

    internal ReadOnlySpan<GlyphInstance> Glyphs => _glyphs.AsSpan(0, GlyphCount);

    internal ReadOnlySpan<ShapeRecord> Shapes => _shapeStreams is null ? default : _shapeStreams.Records.AsSpan(0, _usedShapes);

    internal ReadOnlySpan<GradientBrush> Brushes => _shapeStreams is null ? default : _shapeStreams.Brushes.AsSpan(0, _brushCount);

    // The nearest whole pixel, ties to even: MathF.Round. In the NativeAOT build, the frames scene's display list took 32.1 to
    // 32.7 µs to write at 125 % with MathF.Round, and 30.2 to 30.7 µs with SSE4.1's ROUNDSS, the same rounding in one instruction,
    // against 26.8 to 28.0 µs on main (row W1, docs/benchmarks/w1-dpi.md).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float RoundToPixel(float value) =>
        Sse41.IsSupported ? Sse41.RoundToNearestIntegerScalar(Vector128.CreateScalarUnsafe(value)).ToScalar() : MathF.Round(value);

    // A glyph's quad: on the whole pixel of its quarter, or, for a color glyph, on the nearest whole pixel; its top from the row of
    // its baseline.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static GlyphInstance Place(in AtlasGlyph glyph, float position, int pixel, int row)
    {
        int left = (glyph.Coverage == GlyphCoverage.Color ? GlyphPosition.Round(position) : pixel) + glyph.BearingX;
        return new GlyphInstance(left, row - glyph.BearingY, glyph.Width, glyph.Height, glyph.Page, glyph.X, glyph.Y);
    }

    // Leaves room for a glyph the atlas does not have, for ResolveGlyphs; out of DrawText's loop, which a frame runs with all its
    // glyphs found.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private GlyphInstance Pend(int instance, in GlyphKey key, Color color, float position, int pixel, int row)
    {
        // The table of a list reset with glyphs still pending is emptied here, so that Reset only writes: a frame starts with cold
        // caches, where reading the table cost 100 ns (the draw-text --cold scenario of benchmarks/XylemUI.E2E, row T5).
        if (_pendingCount == 0)
        {
            _resolve.RequestIndex.Clear();
        }

        var request = new GlyphRequest(key, color.Packed);
        if (!_resolve.RequestIndex.TryGetValue(request, out int index))
        {
            index = _resolve.RequestIndex.Count;
            if (index == _resolve.Requests.Length)
            {
                Array.Resize(ref _resolve.Requests, _resolve.Requests.Length * 2);
            }

            _resolve.Requests[index] = request;
            _resolve.RequestIndex.Add(request, index);
        }

        if (_pendingCount == _resolve.Pending.Length)
        {
            Array.Resize(ref _resolve.Pending, _resolve.Pending.Length * 2);
        }

        _resolve.Pending[_pendingCount++] = new Pending(instance, index, position, pixel, row);
        return default;
    }

    // Writes the list again with the pending glyphs filled: a dropped glyph leaves its run, an empty run leaves the list, and a run
    // splits where the kind of its glyphs changes. Rectangles keep their place, and their stream is not touched.
    private void Rebuild()
    {
        // Each pending glyph can split its run in three, adding two runs.
        while (_commands.Length < CommandCount + (2 * _pendingCount))
        {
            Grow();
        }

        if (_resolve.NextCommands.Length != _commands.Length || _resolve.NextGlyphs.Length != _glyphs.Length)
        {
            _resolve.NextCommands = new CommandTag[_commands.Length];
            _resolve.NextColors = new uint[_colors.Length];
            _resolve.NextRunLengths = new int[_runLengths.Length];
            _resolve.NextRunCoverages = new GlyphCoverage[_runCoverages.Length];
            _resolve.NextGlyphs = new GlyphInstance[_glyphs.Length];
            GrowthCount++;
        }

        int commands = 0;
        int colors = 0;
        int runs = 0;
        int glyphs = 0;
        int color = 0;
        int run = 0;
        int glyph = 0;
        int pending = 0;
        for (int c = 0; c < CommandCount; c++)
        {
            if (_commands[c] != CommandTag.GlyphRun)
            {
                _resolve.NextCommands[commands++] = _commands[c];
                _resolve.NextColors[colors++] = _colors[color++];
                continue;
            }

            uint packed = _colors[color++];
            GlyphCoverage kind = _runCoverages[run];
            int length = _runLengths[run++];
            int start = glyphs;
            GlyphCoverage segment = kind;
            for (int i = glyph; i < glyph + length; i++)
            {
                GlyphInstance instance;
                GlyphCoverage instanceKind = kind;
                if (pending < _pendingCount && _resolve.Pending[pending].Instance == i)
                {
                    Pending room = _resolve.Pending[pending++];
                    AtlasGlyph resolved = _resolve.Resolved[room.Request];
                    if (!_resolve.ResolvedFound[room.Request] || resolved.Width == 0)
                    {
                        continue;
                    }

                    instanceKind = resolved.Coverage;
                    instance = Place(resolved, room.Position, room.Pixel, room.Row);
                }
                else
                {
                    instance = _glyphs[i];
                }

                if (glyphs > start && instanceKind != segment)
                {
                    EmitRun(ref commands, ref colors, ref runs, packed, segment, glyphs - start);
                    start = glyphs;
                }

                segment = instanceKind;
                _resolve.NextGlyphs[glyphs++] = instance;
            }

            if (glyphs > start)
            {
                EmitRun(ref commands, ref colors, ref runs, packed, segment, glyphs - start);
            }

            glyph += length;
        }

        (_commands, _resolve.NextCommands) = (_resolve.NextCommands, _commands);
        (_colors, _resolve.NextColors) = (_resolve.NextColors, _colors);
        (_runLengths, _resolve.NextRunLengths) = (_resolve.NextRunLengths, _runLengths);
        (_runCoverages, _resolve.NextRunCoverages) = (_resolve.NextRunCoverages, _runCoverages);
        (_glyphs, _resolve.NextGlyphs) = (_resolve.NextGlyphs, _glyphs);
        CommandCount = commands;
        _usedColors = colors;
        _usedRuns = runs;
        GlyphCount = glyphs;
    }

    private void EmitRun(ref int commands, ref int colors, ref int runs, uint color, GlyphCoverage coverage, int count)
    {
        _resolve.NextCommands[commands++] = CommandTag.GlyphRun;
        _resolve.NextColors[colors++] = color;
        _resolve.NextRunCoverages[runs] = coverage;
        _resolve.NextRunLengths[runs++] = count;
    }

    // Room for `count` glyphs after the used ones; they count as used only when EndGlyphRun closes the run.
    private Span<GlyphInstance> ReserveGlyphs(int count)
    {
        if (GlyphCount + count > _glyphs.Length)
        {
            GrowGlyphs(GlyphCount + count);
        }

        return _glyphs.AsSpan(GlyphCount, count);
    }

    // CSS's overlapping curves (CSS Backgrounds 3, 5.5; Skia's SkRRect::scaleRadii, which Flutter and Avalonia draw with): when
    // the two radii on a side sum past it, every radius is scaled by the smallest ratio of a side to its sum.
    private static CornerRadii Fitted(in CornerRadii radii, float width, float height)
    {
        float top = radii.TopLeft + radii.TopRight;
        float bottom = radii.BottomLeft + radii.BottomRight;
        float left = radii.TopLeft + radii.BottomLeft;
        float right = radii.TopRight + radii.BottomRight;
        float f = 1;
        f = top > width ? MathF.Min(f, width / top) : f;
        f = bottom > width ? MathF.Min(f, width / bottom) : f;
        f = left > height ? MathF.Min(f, height / left) : f;
        f = right > height ? MathF.Min(f, height / right) : f;
        return f < 1 ? new CornerRadii(radii.TopLeft * f, radii.TopRight * f, radii.BottomRight * f, radii.BottomLeft * f) : radii;
    }

    private static CornerRadii Scaled(in CornerRadii radii, float scale) =>
        new(radii.TopLeft * scale, radii.TopRight * scale, radii.BottomRight * scale, radii.BottomLeft * scale);

    // A Shape or Shadow command: its rectangle with each edge on a pixel, its color, and the record it fills in.
    private ref ShapeRecord AppendShape(CommandTag tag, float x, float y, float width, float height, Color color)
    {
        if (CommandCount == _commands.Length)
        {
            Grow();
        }

        float scale = Scale;
        float left = RoundToPixel(x * scale);
        float top = RoundToPixel(y * scale);
        _commands[CommandCount++] = tag;
        Span<float> rect = _rects.AsSpan(_usedRects, FloatsPerRect);
        rect[0] = left;
        rect[1] = top;
        rect[2] = RoundToPixel((x + width) * scale) - left;
        rect[3] = RoundToPixel((y + height) * scale) - top;
        _usedRects += FloatsPerRect;
        _colors[_usedColors++] = color.Packed;
        ShapeStreams streams = _shapeStreams ??= new ShapeStreams();
        if (_usedShapes == streams.Records.Length)
        {
            Array.Resize(ref streams.Records, streams.Records.Length * 2);
            GrowthCount++;
        }

        return ref streams.Records[_usedShapes++];
    }

    // The brush's place among the frame's brushes, adding it the first time the frame uses it; -1 for none. The brush keeps the
    // frame and the place it was given, so a brush used many times is found without a lookup.
    private int BrushIndex(GradientBrush? brush)
    {
        if (brush is null)
        {
            return -1;
        }

        if (_brushCount == 0)
        {
            _brushFrame = Interlocked.Increment(ref _brushFrames);
        }

        if (brush.ListFrame == _brushFrame)
        {
            return brush.ListIndex;
        }

        ShapeStreams streams = _shapeStreams!;
        if (_brushCount == streams.Brushes.Length)
        {
            Array.Resize(ref streams.Brushes, streams.Brushes.Length * 2);
            GrowthCount++;
        }

        brush.ListFrame = _brushFrame;
        brush.ListIndex = _brushCount;
        streams.Brushes[_brushCount] = brush;
        return _brushCount++;
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

    // The records of the Shape and Shadow commands, and the gradients they use, in the order of their first use in the frame.
    private sealed class ShapeStreams
    {
        public ShapeRecord[] Records = new ShapeRecord[64];
        public GradientBrush[] Brushes = new GradientBrush[16];
    }

    // A glyph DrawText left room for: its instance, its request, and where it goes once its box is known.
    private readonly record struct Pending(int Instance, int Request, float Position, int Pixel, int Row);

    // The glyphs DrawText left room for, in the order of their instances; the distinct glyphs they ask the atlas for, with each one's
    // index in Requests, and the atlas's answers; and the streams Rebuild writes the resolved list into, then swaps with the list's
    // own, made the first time they are needed.
    private sealed class Resolution
    {
        public readonly Dictionary<GlyphRequest, int> RequestIndex = [];
        public Pending[] Pending = new Pending[64];
        public GlyphRequest[] Requests = new GlyphRequest[64];
        public AtlasGlyph[] Resolved = new AtlasGlyph[64];
        public bool[] ResolvedFound = new bool[64];
        public CommandTag[] NextCommands = [];
        public uint[] NextColors = [];
        public int[] NextRunLengths = [];
        public GlyphCoverage[] NextRunCoverages = [];
        public GlyphInstance[] NextGlyphs = [];
    }
}
