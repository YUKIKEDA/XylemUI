using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// A range of a text leaf with one layout: the fields of its <see cref="SpanStyle"/> that the shaping reads (design §4.8). Its
/// <see langword="null"/> fields take the leaf's <see cref="TextStyle"/>.
/// </summary>
internal readonly record struct LayoutRun(
    int Start,
    int Length,
    FontRequest? Font,
    float? Size,
    TextLanguage? Language,
    float? LetterSpacing,
    FontFeatures? Features
)
{
    public bool SameLayout(in SpanStyle style) =>
        Font == style.Font
        && Size == style.Size
        && Language == style.Language
        && LetterSpacing == style.LetterSpacing
        && ReferenceEquals(Features, style.Features);
}

/// <summary>
/// A range of a text leaf with one look: the fields of its <see cref="SpanStyle"/> that only the draw stage reads, and its link
/// (design §4.8). <see cref="Link"/> is an index in <see cref="TextRanges.Links"/>, or -1.
/// </summary>
internal readonly record struct PaintRun(
    int Start,
    int Length,
    Color? Color,
    Color? Background,
    TextDecoration? Decoration,
    TextShadow? Shadow,
    int Link
)
{
    public bool SameLook(in SpanStyle style, int link) =>
        Color == style.Color && Background == style.Background && Decoration == style.Decoration && Shadow == style.Shadow && Link == link;
}

/// <summary>
/// A link of a text leaf (design §4.8): what its click, its hover's start, and its hover's end call, and the look it takes while
/// a pointer hovers it.
/// </summary>
internal readonly record struct TextLink(PointerHandler OnClick, PointerHandler? OnEnter, PointerHandler? OnLeave, SpanStyle Hovered)
{
    public bool Same(in TextLink other) =>
        ReferenceEquals(OnClick, other.OnClick)
        && ReferenceEquals(OnEnter, other.OnEnter)
        && ReferenceEquals(OnLeave, other.OnLeave)
        && Hovered == other.Hovered;
}

/// <summary>
/// An inline object of a text leaf (design §4.8): the child of the text node that takes the room of its U+FFFC, and where it sits
/// on its line.
/// </summary>
/// <remarks>
/// The handle goes first: it is 16 bytes, aligned to 8, and the position and the alignment then share its last 8 (24 bytes, where
/// the position first made it 32).
/// </remarks>
/// <param name="Child">The child node.</param>
/// <param name="Position">The code unit of its U+FFFC in the text.</param>
/// <param name="Alignment">Where it sits on its line.</param>
internal readonly record struct InlineObject(NodeHandle Child, int Position, PlaceholderAlignment Alignment);

/// <summary>
/// The room an inline object takes, from the child's last measurement under the text's width (design §4.8): its margin box, its
/// first baseline from the margin box's top (NaN when it has none), and its margin box's widths under a min-content and a
/// max-content width.
/// </summary>
internal readonly record struct InlineBox(float Width, float Height, float Baseline, float MinWidth, float MaxWidth);

/// <summary>
/// The ranges of a text leaf (design §4.8): two flat lists that cover its text, the layout's and the look's, its links, and its
/// inline objects. Only a leaf that a writer gave a range or an inline object holds one (<see cref="TextLeaf.Ranges"/>); the arrays
/// are kept and grow only when a write needs more.
/// </summary>
internal sealed class TextRanges
{
    private LayoutRun[] _layout = new LayoutRun[2];
    private PaintRun[] _paint = new PaintRun[2];
    private TextLink[] _links = [];
    private InlineObject[] _inlines = [];
    private InlineBox[] _boxes = [];
    private int _layoutCount;
    private int _paintCount;
    private int _linkCount;
    private int _inlineCount;

    /// <summary>
    /// Gets the ranges of one layout, in order, covering the text.
    /// </summary>
    public ReadOnlySpan<LayoutRun> Layout => _layout.AsSpan(0, _layoutCount);

    /// <summary>
    /// Gets the ranges of one look, in order, covering the text.
    /// </summary>
    public ReadOnlySpan<PaintRun> Paint => _paint.AsSpan(0, _paintCount);

    /// <summary>
    /// Gets the links the look's ranges point to.
    /// </summary>
    public ReadOnlySpan<TextLink> Links => _links.AsSpan(0, _linkCount);

    /// <summary>
    /// Gets the inline objects, in the order of the text.
    /// </summary>
    public ReadOnlySpan<InlineObject> Inlines => _inlines.AsSpan(0, _inlineCount);

    /// <summary>
    /// Gets the rooms the inline objects take, by the index of <see cref="Inlines"/>, from their last measurement.
    /// </summary>
    public Span<InlineBox> Boxes => _boxes.AsSpan(0, _inlineCount);

    /// <summary>
    /// Gets a value indicating whether the text has an inline object.
    /// </summary>
    public bool HasInlines => _inlineCount > 0;

    /// <summary>
    /// Gets or sets the link a pointer is over, whose range takes its hovered look; -1 when none is.
    /// </summary>
    public int HoveredLink { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the leaf's hit region in the display list of <see cref="HitBuild"/>, which a leaf with links writes
    /// (design §4.8).
    /// </summary>
    public int HitEntry { get; set; }

    /// <summary>
    /// Gets or sets the build of the display list <see cref="HitEntry"/> belongs to (<see cref="WindowFrames.Builds"/>); 0 for none.
    /// </summary>
    public int HitBuild { get; set; }

    /// <summary>
    /// Gets a value indicating whether the text has a link.
    /// </summary>
    public bool HasLinks => _linkCount > 0;

    /// <summary>
    /// Takes the hovered link and the hit region of the ranges a leaf held before these: a rewrite keeps the pointer's hover until
    /// the next hit test, and the region until the next frame. A hovered link these do not have is dropped.
    /// </summary>
    /// <param name="before">The leaf's ranges before.</param>
    public void TakeStateOf(TextRanges before)
    {
        HoveredLink = before.HoveredLink < _linkCount ? before.HoveredLink : -1;
        HitEntry = before.HitEntry;
        HitBuild = before.HitBuild;

        // The same inline objects keep the rooms they were measured to: the layout stays (TextLeaf.TakeRanges).
        if (Inlines.SequenceEqual(before.Inlines))
        {
            before.Boxes.CopyTo(Boxes);
        }
    }

    /// <summary>
    /// Forgets the ranges, keeping the arrays.
    /// </summary>
    public void Clear()
    {
        _layoutCount = 0;
        _paintCount = 0;
        Array.Clear(_links, 0, _linkCount);
        _linkCount = 0;
        _inlineCount = 0;
        HoveredLink = -1;
        HitBuild = 0;
    }

    /// <summary>
    /// Adds an inline object, its room not measured yet.
    /// </summary>
    /// <param name="inline">The object.</param>
    public void AddInline(in InlineObject inline)
    {
        if (_inlineCount == _inlines.Length)
        {
            int size = Math.Max(2, _inlineCount * 2);
            Array.Resize(ref _inlines, size);
            Array.Resize(ref _boxes, size);
        }

        _inlines[_inlineCount] = inline;
        _boxes[_inlineCount] = default;
        _inlineCount++;
    }

    /// <summary>
    /// Returns whether a node is one of the inline objects.
    /// </summary>
    /// <param name="child">The node.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool HasInline(NodeHandle child)
    {
        foreach (InlineObject inline in Inlines)
        {
            if (inline.Child == child)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds a link and returns its index.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>The index.</returns>
    public int AddLink(in TextLink link)
    {
        if (_linkCount == _links.Length)
        {
            Array.Resize(ref _links, Math.Max(2, _linkCount * 2));
        }

        _links[_linkCount] = link;
        return _linkCount++;
    }

    /// <summary>
    /// Adds the next piece of text with its style and link, joining it to the range before it when the layout, or the look, is
    /// the same.
    /// </summary>
    /// <param name="start">Where the piece starts: where the last one ended.</param>
    /// <param name="length">Its length.</param>
    /// <param name="style">Its style, resolved through the ranges around it.</param>
    /// <param name="link">Its link, or -1.</param>
    public void Add(int start, int length, in SpanStyle style, int link)
    {
        if (_layoutCount > 0 && _layout[_layoutCount - 1].SameLayout(style))
        {
            _layout[_layoutCount - 1] = _layout[_layoutCount - 1] with { Length = start + length - _layout[_layoutCount - 1].Start };
        }
        else
        {
            Grow(ref _layout, _layoutCount);
            _layout[_layoutCount++] = new LayoutRun(start, length, style.Font, style.Size, style.Language, style.LetterSpacing, style.Features);
        }

        if (_paintCount > 0 && _paint[_paintCount - 1].SameLook(style, link))
        {
            _paint[_paintCount - 1] = _paint[_paintCount - 1] with { Length = start + length - _paint[_paintCount - 1].Start };
        }
        else
        {
            Grow(ref _paint, _paintCount);
            _paint[_paintCount++] = new PaintRun(start, length, style.Color, style.Background, style.Decoration, style.Shadow, link);
        }
    }

    /// <summary>
    /// Returns whether the layout's ranges and the inline objects are the same as another's.
    /// </summary>
    /// <param name="other">The other ranges.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    public bool LayoutEquals(TextRanges other) => Layout.SequenceEqual(other.Layout) && Inlines.SequenceEqual(other.Inlines);

    /// <summary>
    /// Returns whether the look's ranges and the links are the same as another's.
    /// </summary>
    /// <param name="other">The other ranges.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    public bool LookEquals(TextRanges other)
    {
        if (!Paint.SequenceEqual(other.Paint) || _linkCount != other._linkCount)
        {
            return false;
        }

        for (int i = 0; i < _linkCount; i++)
        {
            if (!_links[i].Same(other._links[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void Grow<T>(ref T[] array, int count)
    {
        if (count == array.Length)
        {
            Array.Resize(ref array, count * 2);
        }
    }
}

/// <summary>
/// Turns the nested ranges a text writer opens and closes into the flat <see cref="TextRanges"/> of the text it writes (design
/// §4.8), as Flutter's paragraph builder turns its pushed and popped styles into runs. One for the UI thread, as the writer's
/// buffer is (<see cref="TextContext"/>).
/// </summary>
internal sealed class TextRangeWriter
{
    private (SpanStyle Style, int Link)[] _open = new (SpanStyle, int)[4];
    private int _depth;
    private int _boundary;

    /// <summary>
    /// Gets or sets the ranges being written; a leaf that takes them gives its own back here.
    /// </summary>
    public TextRanges Ranges { get; set; } = new();

    /// <summary>
    /// Gets a value indicating whether the write opened a range or wrote an inline object: a text with neither keeps no ranges.
    /// </summary>
    public bool Used { get; private set; }

    /// <summary>
    /// Gets or sets the tree of the text node being written, which its inline objects belong to.
    /// </summary>
    public UiTree? Tree { get; set; }

    /// <summary>
    /// Gets or sets the text node being written, which its inline objects become children of.
    /// </summary>
    public NodeHandle Owner { get; set; }

    /// <summary>
    /// Starts a write.
    /// </summary>
    public void Reset()
    {
        Ranges.Clear();
        Array.Clear(_open, 0, _depth);
        _depth = 0;
        _boundary = 0;
        Used = false;
    }

    /// <summary>
    /// Opens a range at a position, its style over the ranges around it.
    /// </summary>
    /// <param name="at">Where the range starts: what the writer has written.</param>
    /// <param name="style">Its style.</param>
    /// <param name="link">Its link, or -1 to keep the link of the range around it.</param>
    public void Open(int at, in SpanStyle style, int link)
    {
        Close(at, pop: false);
        (SpanStyle Style, int Link) outer = _depth > 0 ? _open[_depth - 1] : (default, -1);
        if (_depth == _open.Length)
        {
            Array.Resize(ref _open, _depth * 2);
        }

        _open[_depth++] = (style.Over(outer.Style), link >= 0 ? link : outer.Link);
        Used = true;
    }

    /// <summary>
    /// Adds an inline object at a position (design §4.8): the child the text node takes as its own, which a component made outside
    /// the write and keeps.
    /// </summary>
    /// <param name="at">The position of its U+FFFC: what the writer has written.</param>
    /// <param name="child">The child.</param>
    /// <param name="alignment">Where it sits on its line.</param>
    /// <exception cref="ArgumentException">The child is not an element of the text node's tree.</exception>
    /// <exception cref="InvalidOperationException">The child was removed, has another parent, or was written already.</exception>
    public void AddInline(int at, Element child, PlaceholderAlignment alignment)
    {
        if (Tree is not { } tree || child.Tree != tree)
        {
            throw new ArgumentException("An inline object is an element of the text node's own tree.", nameof(child));
        }

        NodeHandle node = child.Node;
        if (!tree.Nodes.IsAlive(node))
        {
            throw new InvalidOperationException("An inline object was removed; the scope that made it was disposed.");
        }

        NodeHandle parent = tree.Nodes.GetRef(node).Parent;
        if (node == Owner || (!parent.IsNone && parent != Owner))
        {
            throw new InvalidOperationException("An inline object has a parent of its own; an element sits in one place of the tree.");
        }

        if (Ranges.HasInline(node))
        {
            throw new InvalidOperationException("An inline object was written twice; an element sits in one place of the text.");
        }

        Ranges.AddInline(new InlineObject(node, at, alignment));
        Used = true;
    }

    /// <summary>
    /// Closes the innermost range at a position.
    /// </summary>
    /// <param name="at">Where the range ends: what the writer has written.</param>
    /// <exception cref="InvalidOperationException">No range is open.</exception>
    public void Close(int at)
    {
        if (_depth == 0)
        {
            throw new InvalidOperationException("A text writer closed a range that it had not opened.");
        }

        Close(at, pop: true);
    }

    /// <summary>
    /// Ends the write at its length.
    /// </summary>
    /// <param name="length">The length of the text written.</param>
    /// <exception cref="InvalidOperationException">A range is still open: each <c>Style</c> and <c>Link</c> is closed with <c>using</c>.</exception>
    public void Finish(int length)
    {
        if (_depth > 0)
        {
            throw new InvalidOperationException("A text writer ended with a range still open; close each Style and Link with using.");
        }

        if (Used)
        {
            Close(length, pop: false);
        }
    }

    // The text written since the last boundary goes to the ranges with the innermost open range's style; then, for a close, that
    // range ends.
    private void Close(int at, bool pop)
    {
        if (at > _boundary)
        {
            (SpanStyle style, int link) = _depth > 0 ? _open[_depth - 1] : (default(SpanStyle), -1);
            Ranges.Add(_boundary, at - _boundary, style, link);
            _boundary = at;
        }

        if (pop)
        {
            _open[--_depth] = default;
        }
    }
}
