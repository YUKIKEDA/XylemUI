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
/// The image is added to the window's <see cref="TextureCache"/> on the UI thread, when the decode's result arrives, and released
/// when the source changes or the block's scope is disposed. A decode the block let go of is skipped, or its result dropped.
/// </para>
/// </remarks>
internal sealed class ImageBlock
{
    private readonly UiTree _tree;
    private readonly ImageOptions _options;
    private readonly SynchronizationContext? _context;
    private readonly ScopeRef _owner;
    private readonly Action<ImageRequest> _complete;
    private ImageSource? _source;
    private ImageRequest? _pending;
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
        _complete = Complete;
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

    // A source to show. An equal one changes nothing; another lets the image or the decode in flight go, shows the loading part,
    // and asks for the decode (IM1's Grill, Q20).
    private void Load(ImageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Equals(_source))
        {
            return;
        }

        _source = source;
        LetGo();
        ShowPart(_options.Loading);
        int width = _options.DecodeWidth ?? 0;
        int height = _options.DecodeHeight ?? 0;
        if (_context is null)
        {
            // No loop to post to (a tree drawn without one, as a unit test's): the decode runs here.
            var request = new ImageRequest(source, width, height, SynchronizationContext.Current ?? new SynchronizationContext(), _complete);
            _pending = request;
            Complete(request, ImageDecodeQueue.Decode(source, width, height));
            return;
        }

        _pending = new ImageRequest(source, width, height, _context, _complete);
        ImageDecodeQueue.Enqueue(_pending);
    }

    private void Complete(ImageRequest request) => Complete(request, request.Result);

    // A decode's result on the UI thread: the image, or the error part.
    private void Complete(ImageRequest request, ImageResult result)
    {
        if (request != _pending || !_tree.Nodes.IsAlive(Node))
        {
            return;
        }

        _pending = null;
        if (result.Pixels is null)
        {
            ShowPart(_options.Error);
            return;
        }

        ShowPart(null);
        _image = _tree.Images.Add(result.Width, result.Height, result.Pixels);
        _width = result.Width;
        _height = result.Height;

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

    // Lets the image or the decode in flight go, and the ratio the image gave the box.
    private void LetGo()
    {
        _pending?.Cancel();
        _pending = null;
        if (!_image.IsNone)
        {
            _tree.Images.Release(_image);
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
        _pending?.Cancel();
        _pending = null;
        if (!_image.IsNone && _tree.Images.IsAlive(_image))
        {
            _tree.Images.Release(_image);
        }

        _image = default;
    }

    // Makes `part` the node's child in place of the one there: nothing for null.
    private void ShowPart(Func<Element>? part)
    {
        if (!_partNode.IsNone)
        {
            _tree.Focus.Lose(_partNode);
            ScopeHandle old = _part;
            _part = default;
            _partNode = default;
            _tree.DetachChildren(Node);
            Scope.Dispose(old);
        }

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
