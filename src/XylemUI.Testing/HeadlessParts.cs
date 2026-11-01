using XylemUI.Text;

namespace XylemUI.Testing;

// The parts a headless app's frames and input are made of in place of a window's (design §12.4, "The host"): no OS window, no swap
// chain, and no GPU.

/// <summary>
/// Holds the one frame a frame scheduler holds (design §2.11), which <see cref="HeadlessApp.PumpAndSettle"/> runs.
/// </summary>
internal sealed class HeadlessScheduler : IFrameScheduler
{
    private Action? _callback;

    /// <summary>
    /// Gets a value indicating whether a frame is asked for and has not run.
    /// </summary>
    public bool Pending => _callback is not null;

    /// <inheritdoc/>
    public void RequestFrame(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
    }

    /// <inheritdoc/>
    public void CancelFrame() => _callback = null;

    /// <summary>
    /// Runs the frame asked for, if there is one.
    /// </summary>
    public void Run()
    {
        Action? callback = _callback;
        _callback = null;
        callback?.Invoke();
    }
}

/// <summary>
/// Keeps what the input asks of a window: the capture and the cursor, which a test may read.
/// </summary>
internal sealed class HeadlessPlatform : IPointerPlatform
{
    /// <summary>
    /// Gets a value indicating whether the pointer is captured.
    /// </summary>
    public bool Captured { get; private set; }

    /// <summary>
    /// Gets the cursor the input set last.
    /// </summary>
    public CursorKind Cursor { get; private set; }

    /// <inheritdoc/>
    public void Capture() => Captured = true;

    /// <inheritdoc/>
    public void ReleaseCapture() => Captured = false;

    /// <inheritdoc/>
    public void SetCursor(CursorKind cursor) => Cursor = cursor;

    /// <summary>
    /// Gets the drags handed to the OS (design §7, "Drag and drop", "Out to the OS"), in order.
    /// </summary>
    public List<(ExternalDragData Data, DragEffects Allowed)> ExternalDrags { get; } = [];

    /// <summary>
    /// Gets or sets the effect the OS's drop chooses for each drag handed to it; <see cref="DragEffects.None"/> by default.
    /// </summary>
    public DragEffects ExternalEffect { get; set; }

    /// <inheritdoc/>
    public DragEffects StartExternalDrag(ExternalDragData data, DragEffects allowed)
    {
        ExternalDrags.Add((data, allowed));
        return ExternalEffect;
    }
}

/// <summary>
/// Counts the presents of a headless app's frames, and hands each frame to the renderer's target when the app has one; with none,
/// it draws nothing (design §12.3).
/// </summary>
/// <param name="inner">The renderer's target, or <see langword="null"/>.</param>
internal sealed class HeadlessTarget(IRenderTarget? inner) : IRenderTarget
{
    /// <summary>
    /// Gets the number of frames presented.
    /// </summary>
    public int Presents { get; private set; }

    /// <inheritdoc/>
    public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas) => inner?.Draw(lists, clearColor, atlas);

    /// <inheritdoc/>
    public void Present()
    {
        inner?.Present();
        Presents++;
    }
}

/// <summary>
/// Gives every glyph an empty box: the headless frames lay text out and draw nothing, so no glyph needs pixels.
/// </summary>
internal sealed class HeadlessGlyphs : IGlyphRasterizer
{
    /// <inheritdoc/>
    public GlyphBox Measure(in GlyphKey glyph) => default;

    /// <inheritdoc/>
    public void Rasterize(in GlyphKey glyph, Span<byte> destination, int stride) { }
}
