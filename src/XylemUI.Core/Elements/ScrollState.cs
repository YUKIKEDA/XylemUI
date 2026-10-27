using System.Numerics;

namespace XylemUI;

/// <summary>
/// The scroll position of a scroll container, which the app and the wheel both write (design §3.4.1, "The scroll container"), and
/// the range the last layout gave it. Flutter's <c>ScrollController</c> and GPUI's <c>ScrollHandle</c> hold the same.
/// </summary>
/// <remarks>
/// It belongs to the UI thread, as signals do. It is attached to one node at a time (<see cref="ElementExtensions.ScrollState"/>);
/// <see cref="MaxOffset"/> and <see cref="Viewport"/> are written after each layout of the node, before it is drawn.
/// </remarks>
public sealed class ScrollState
{
    private readonly Signal<Vector2> _offset = Signal.Create(Vector2.Zero);
    private readonly Signal<Vector2> _maxOffset = Signal.Create(Vector2.Zero);
    private readonly Signal<Size> _viewport = Signal.Create(default(Size));

    /// <summary>
    /// Gets the scroll position, in logical pixels: how far the content is moved up and to the left, from 0 to
    /// <see cref="MaxOffset"/>.
    /// </summary>
    public IReadOnlySignal<Vector2> Offset => _offset;

    /// <summary>
    /// Gets the largest scroll position on each axis: the reachable end of the content less the viewport, and at least 0.
    /// </summary>
    public IReadOnlySignal<Vector2> MaxOffset => _maxOffset;

    /// <summary>
    /// Gets the size of the part that shows the content: the padding box less the scrollbars' room, in logical pixels.
    /// </summary>
    public IReadOnlySignal<Size> Viewport => _viewport;

    /// <summary>
    /// Gets the tree of the node it is attached to; <see langword="null"/> while it is not attached.
    /// </summary>
    internal UiTree? Tree { get; private set; }

    /// <summary>
    /// Gets the node it is attached to.
    /// </summary>
    internal NodeHandle Node { get; private set; }

    /// <summary>
    /// Gets the scroll position without reading it as a signal, for the layout and the draw stage.
    /// </summary>
    internal Vector2 OffsetValue => _offset.Peek();

    /// <summary>
    /// Scrolls to a position, kept between 0 and <see cref="MaxOffset"/>. The node is drawn again at the next frame, with no
    /// layout (<see cref="ChangeKind.TransformProp"/>).
    /// </summary>
    /// <param name="offset">The position, in logical pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a number.</exception>
    public void ScrollTo(Vector2 offset)
    {
        if (float.IsNaN(offset.X) || float.IsNaN(offset.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "The scroll position's coordinates are numbers.");
        }

        SetOffset(Vector2.Clamp(offset, Vector2.Zero, _maxOffset.Peek()));
    }

    /// <summary>
    /// Attaches the state to a node; a state attached to another node is moved to this one.
    /// </summary>
    /// <param name="tree">The node's tree.</param>
    /// <param name="node">The node.</param>
    internal void Attach(UiTree tree, NodeHandle node)
    {
        Tree = tree;
        Node = node;
    }

    /// <summary>
    /// Detaches the state from its node.
    /// </summary>
    internal void Detach()
    {
        Tree = null;
        Node = default;
    }

    /// <summary>
    /// Writes the position, and marks the node to be drawn again when it changed.
    /// </summary>
    /// <param name="offset">The position, already kept in the range.</param>
    /// <returns><see langword="true"/> when the position changed.</returns>
    internal bool SetOffset(Vector2 offset)
    {
        if (offset == _offset.Peek())
        {
            return false;
        }

        _offset.Set(offset);
        if (Tree is { } tree && tree.Nodes.IsAlive(Node))
        {
            Invalidation.Invalidate(tree, Node, ChangeKind.TransformProp);
        }

        return true;
    }

    /// <summary>
    /// Writes the range a layout gave the node, and keeps the position in it (a content that shrank brings the position back).
    /// </summary>
    /// <param name="maxOffset">The largest position.</param>
    /// <param name="viewport">The viewport.</param>
    internal void Publish(Vector2 maxOffset, Size viewport)
    {
        _maxOffset.Set(maxOffset);
        _viewport.Set(viewport);
        _ = SetOffset(Vector2.Clamp(_offset.Peek(), Vector2.Zero, maxOffset));
    }
}
