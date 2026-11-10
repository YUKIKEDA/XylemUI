using System.Numerics;

namespace XylemUI;

/// <summary>
/// The node of an image element and its states (design §5.0.4, "Size", "Owner and lifetime", "Display"; row IM1, 1b-2): while its
/// image is read, the loading part is its child; when the read failed, the error part; once read, it is a leaf whose content size
/// is the image's pixels as logical pixels, with the image's ratio, and its draw function draws the image as its fit asks.
/// </summary>
/// <remarks>
/// <para>
/// The node is the one the app holds and styles, so the states change its child and its measure, never the node. A part is made in
/// a scope of its own under the block's, as <see cref="ShowBlock"/> makes its parts.
/// </para>
/// <para>
/// The image belongs to the window's cache (<see cref="WindowImageCache"/>; row IM1, 3a), not to the block: the block asks the
/// cache for its source at its decode size, shows the entry's image, and leaves the entry when its source changes or its scope is
/// disposed. The cache decides when the image is released and what becomes of a decode nobody waits for.
/// </para>
/// </remarks>
internal sealed class ImageBlock
{
    private readonly UiTree _tree;
    private readonly ImageOptions _options;
    private readonly SynchronizationContext? _context;
    private readonly ScopeRef _owner;
    private ImageSource? _source;
    private ImageEntry? _entry;
    private ImageHandle _image;
    private int _width;
    private int _height;
    private ScopeHandle _part;
    private NodeHandle _partNode;
    private bool _ratioSet;

    private ImageBlock(UiTree tree, NodeHandle node, ImageOptions options, ScopeRef owner)
    {
        _tree = tree;
        Node = node;
        _options = options;
        _owner = owner;
        _context = SynchronizationContext.Current;
    }

    /// <summary>
    /// Gets the image element's node.
    /// </summary>
    public NodeHandle Node { get; }

    /// <summary>
    /// Gets the image shown, or <see langword="default"/> while it is read or when it failed.
    /// </summary>
    public ImageHandle Image => _image;

    /// <summary>
    /// Gets or sets the block that waits after this one for the same decode (<see cref="ImageEntry.FirstWaiting"/>); only
    /// <see cref="WindowImageCache"/> writes it.
    /// </summary>
    internal ImageBlock? NextWaiting { get; set; }

    /// <summary>
    /// Makes an image element in a scope of its own under the current one, which reads its first source at once.
    /// </summary>
    /// <param name="tree">The current tree.</param>
    /// <param name="source">The source, read in an effect when it is a function.</param>
    /// <param name="options">The options.</param>
    /// <returns>The element.</returns>
    public static Element Create(UiTree tree, Func<ImageSource> source, ImageOptions options)
    {
        ImageBlock? block = null;
        _ = Scope.Create(
            () =>
            {
                ImageBlock made = null!;
                NodeHandle node = tree.CreateNode(new LayoutStyle(), (_, _) => made.Measure());
                made = new ImageBlock(tree, node, options, Scope.CurrentRef);
                block = made;
                Scope.OnCleanup(() =>
                {
                    if (tree.Nodes.IsAlive(node))
                    {
                        tree.Remove(node);
                    }
                });
                Scope.OnCleanup(made.Dispose);
                tree.SetDraw(node, made.Draw);
                AccessibilityInfo info = options.Label is null
                    ? new AccessibilityInfo { Excluded = true }
                    : new AccessibilityInfo { Role = SemanticsRole.Image, Name = options.Label };
                tree.SetAccessibility(node, info);
                _ = Effect.Create(() => made.Load(source()));
            },
            "Image"
        );
        return new Element(tree, block!.Node);
    }

    // The content size of the leaf: the image's pixels as logical pixels (IM1's Grill, Q12). While a part is the child, the node
    // is not a leaf and this is not asked.
    private MeasureResult Measure() => _image.IsNone ? MeasureResult.Of(0, 0) : MeasureResult.Of(_width, _height);

    // A source to show. An equal one changes nothing; another lets the image or the decode waited for go and asks the window's
    // cache for the new one (IM1's Grill, Q20): an image the cache holds is shown at once, with no loading part in between, and a
    // decode in flight is shared.
    private void Load(ImageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Equals(_source))
        {
            return;
        }

        _source = source;
        LetGo();
        var key = new ImageKey(source, _options.DecodeWidth ?? 0, _options.DecodeHeight ?? 0);
        ImageEntry entry = _tree.CachedImages.Acquire(key, this, _context);
        switch (entry.State)
        {
            case ImageEntryState.Ready:
                _entry = entry;
                Show(entry);
                break;
            case ImageEntryState.Decoding:
                _entry = entry;
                ShowPart(_options.Loading);
                break;
            default:
                // Decoded here, with no loop to post to, and it failed: the cache does not keep the entry.
                ShowPart(_options.Error);
                break;
        }
    }

    /// <summary>
    /// Tells the block that the decode it waits for ended, on the UI thread: it shows the image, or the error part.
    /// </summary>
    /// <param name="entry">The entry <see cref="WindowImageCache.Acquire"/> gave the block.</param>
    internal void Completed(ImageEntry entry)
    {
        if (!ReferenceEquals(entry, _entry) || !_tree.Nodes.IsAlive(Node))
        {
            return;
        }

        if (entry.State == ImageEntryState.Failed)
        {
            _entry = null;
            ShowPart(_options.Error);
            return;
        }

        Show(entry);
    }

    // The entry's image in place of the part.
    private void Show(ImageEntry entry)
    {
        RemovePart();
        _image = entry.Image;
        _width = entry.Width;
        _height = entry.Height;

        // The image's ratio, so that a box given one side takes the other from it (IM1's Grill, Q12), unless the app set a ratio.
        LayoutStyle style = _tree.Nodes.GetRef(Node).Layout.Style;
        if (!style.HasAspectRatio)
        {
            _ratioSet = true;
            _tree.SetStyle(Node, style with { AspectRatio = (float)_width / _height });
        }
        else
        {
            Invalidation.Invalidate(_tree, Node, ChangeKind.Size);
        }
    }

    // Leaves the entry shown or waited for, and lets go of the ratio the image gave the box. The cache releases the image.
    private void LetGo()
    {
        Leave();
        if (!_image.IsNone)
        {
            _image = default;
            if (_tree.Nodes.IsAlive(Node))
            {
                Invalidation.Invalidate(_tree, Node, ChangeKind.Size);
            }
        }

        if (_ratioSet && _tree.Nodes.IsAlive(Node))
        {
            _ratioSet = false;
            _tree.SetStyle(Node, _tree.Nodes.GetRef(Node).Layout.Style with { AspectRatio = float.NaN });
        }
    }

    private void Dispose()
    {
        Leave();
        _image = default;
    }

    private void Leave()
    {
        if (_entry is { } entry)
        {
            _entry = null;
            _tree.CachedImages.Leave(entry, this);
        }
    }

    // Takes the part away, when there is one. It is a method of its own, apart from ShowPart: the function ShowPart gives the scope
    // captures its parameter, so ShowPart makes an object for it on entry, and an image the cache holds is shown with no
    // allocation (the Zero-Alloc test of switching between cached sources).
    private void RemovePart()
    {
        if (_partNode.IsNone)
        {
            return;
        }

        _tree.Focus.Lose(_partNode);
        ScopeHandle old = _part;
        _part = default;
        _partNode = default;
        _tree.DetachChildren(Node);
        Scope.Dispose(old);
    }

    // Makes `part` the node's child in place of the one there: nothing for null.
    private void ShowPart(Func<Element>? part)
    {
        RemovePart();
        if (part is null)
        {
            return;
        }

        Element element = default;
        UiTree? previousTree = UiTree.Current;
        Computation? running = ReactiveRuntime.Current;
        if (!Scope.TryEnter(_owner, out ScopeRef previousScope))
        {
            return;
        }

        UiTree.Current = _tree;
        ReactiveRuntime.Current = null;
        try
        {
            _part = Scope.Create(() => element = part(), "Image part");
        }
        finally
        {
            ReactiveRuntime.Current = running;
            UiTree.Current = previousTree;
            Scope.Exit(previousScope);
        }

        if (element.IsNone || !ReferenceEquals(element.Tree, _tree) || !_tree.Nodes.GetRef(element.Node).Parent.IsNone)
        {
            Scope.Dispose(_part);
            _part = default;
            throw new InvalidOperationException("A part of Image returns a node of the same tree with no parent.");
        }

        _partNode = element.Node;
        _tree.AppendChild(Node, _partNode);
    }

    // The image in the content box, as the fit asks.
    private void Draw(DrawingContext context, Rect bounds)
    {
        if (_image.IsNone)
        {
            return;
        }

        ref UiNode node = ref _tree.Nodes.GetRef(Node);
        float basis = node.Layout.LastInput.ParentSize.Width;
        Edges padding = EdgeInsets.ResolveOrZero(node.Layout.Style.Padding, float.IsNaN(basis) ? 0 : basis);
        Edges border = node.Layout.Border;
        var box = new Vector4(
            bounds.X + border.Left + padding.Left,
            bounds.Y + border.Top + padding.Top,
            bounds.Width - border.Left - border.Right - padding.Left - padding.Right,
            bounds.Height - border.Top - border.Bottom - padding.Top - padding.Bottom
        );
        if (box.Z <= 0 || box.W <= 0)
        {
            return;
        }

        Vector4 part = ImageFitting.Place(box, _width, _height, _options.Fit, out Vector4 destination);
        if (destination.Z > 0 && destination.W > 0)
        {
            context.DrawImage(destination.X, destination.Y, destination.Z, destination.W, _image, part);
        }
    }
}
