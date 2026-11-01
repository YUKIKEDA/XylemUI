using System.Numerics;

namespace XylemUI;

/// <summary>
/// Where popups are put (design §7.3): over the window, in its own swap chain, or (row P10) in a window of their own.
/// </summary>
internal interface IPopupHost
{
    /// <summary>
    /// Opens a popup: its root, a node of the tree with no parent, becomes a render root of its own.
    /// </summary>
    /// <param name="anchor">The node the popup is placed against, whose ancestors get its keys.</param>
    /// <param name="root">The popup's root.</param>
    /// <param name="frame">The node between the root and the content, which is placed against the anchor.</param>
    /// <param name="options">How the popup is placed and closed.</param>
    /// <param name="block">What closes the popup, when it closes itself.</param>
    /// <returns>The open popup.</returns>
    PopupHandle Open(NodeHandle anchor, NodeHandle root, NodeHandle frame, PopupOptions options, PopupBlock block);

    /// <summary>
    /// Closes a popup and its children, and gives the focus back (design §7.2).
    /// </summary>
    /// <param name="popup">The popup.</param>
    void Close(PopupHandle popup);
}

/// <summary>
/// An open popup (design §7.3): its root, the frame placed against its anchor, its display list, and the popup its anchor is in.
/// </summary>
internal sealed class PopupHandle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PopupHandle"/> class.
    /// </summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="root">The root.</param>
    /// <param name="frame">The frame.</param>
    /// <param name="options">The options.</param>
    /// <param name="block">The block that owns it.</param>
    /// <param name="parent">The popup the anchor is in, or <see langword="null"/>.</param>
    /// <param name="list">The display list it draws into.</param>
    public PopupHandle(
        NodeHandle anchor,
        NodeHandle root,
        NodeHandle frame,
        PopupOptions options,
        PopupBlock block,
        PopupHandle? parent,
        DisplayList list
    )
    {
        Anchor = anchor;
        Root = root;
        Frame = frame;
        Options = options;
        Block = block;
        Parent = parent;
        List = list;
    }

    /// <summary>
    /// Gets the node the popup is placed against.
    /// </summary>
    public NodeHandle Anchor { get; }

    /// <summary>
    /// Gets the popup's root: a node with no parent, laid out at the window's size.
    /// </summary>
    public NodeHandle Root { get; }

    /// <summary>
    /// Gets the node that holds the content, placed against the anchor after each layout.
    /// </summary>
    public NodeHandle Frame { get; }

    /// <summary>
    /// Gets how the popup is placed and closed.
    /// </summary>
    public PopupOptions Options { get; }

    /// <summary>
    /// Gets the block that opened the popup, which closes it.
    /// </summary>
    public PopupBlock Block { get; }

    /// <summary>
    /// Gets the popup the anchor is in, or <see langword="null"/> for one opened from the window's tree.
    /// </summary>
    public PopupHandle? Parent { get; }

    /// <summary>
    /// Gets the display list the popup draws into (design §7.3, "Display lists").
    /// </summary>
    public DisplayList List { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the list must be built again in the next frame that draws.
    /// </summary>
    public bool NeedsBuild { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the popup is leaving: it takes neither the pointer nor the focus.
    /// </summary>
    public bool Closing { get; set; }

    /// <summary>
    /// Gets or sets the scroll positions of the anchor's scroll containers when the popup was placed first; a scroll of one closes
    /// a popup that is not modal (design §7.3, "Closing").
    /// </summary>
    public Vector2 AnchorScroll { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="AnchorScroll"/> was read.
    /// </summary>
    public bool Placed { get; set; }
}

/// <summary>
/// Puts popups over the window (design §7.3, <c>OverlayPopupHost</c>): each one's root is a render root of the window's tree, laid
/// out at the window's size and drawn into a display list of its own over the window's list. It keeps the open popups, back to front.
/// </summary>
/// <param name="tree">The window's tree.</param>
internal sealed class OverlayPopupHost(UiTree tree) : IPopupHost
{
    private readonly List<PopupHandle> _open = [];
    private readonly Stack<DisplayList> _lists = new();
    private readonly List<PopupBlock> _waiting = [];

    // The window's client size the window gave last (ResizeAll): a popup that opens before the window's tree is laid out, at the start
    // of a frame, cannot read it from the root's box.
    private Size? _window;

    /// <summary>
    /// Gets the open popups, the first opened first: the order they are drawn in, back to front.
    /// </summary>
    public IReadOnlyList<PopupHandle> Opened => _open;

    /// <summary>
    /// Gets the number of open popups.
    /// </summary>
    public int Count => _open.Count;

    /// <summary>
    /// Gets or sets what the window's input does when a modal popup opens: it releases the captures of the nodes behind it.
    /// </summary>
    public Action<NodeHandle>? ModalOpened { get; set; }

    /// <inheritdoc/>
    public PopupHandle Open(NodeHandle anchor, NodeHandle root, NodeHandle frame, PopupOptions options, PopupBlock block)
    {
        PopupHandle? parent = PopupContaining(anchor);

        // A popup that opens closes the ones its parent had open (Flutter's raw_menu_anchor.dart lines 746 to 747), modal ones aside,
        // and every transient one (a tooltip), whatever its parent. A transient popup closes none (design §7.3, "Nesting"); a drag's
        // preview closes none and is closed by none.
        if (!options.IsTransient && !options.IsDragImage)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                if (i < _open.Count && _open[i] is { Closing: false } other && !other.Options.IsDragImage)
                {
                    if (other.Options.IsTransient || (other.Parent == parent && !other.Options.IsModal))
                    {
                        other.Block.Dismiss();
                    }
                }
            }
        }

        var popup = new PopupHandle(anchor, root, frame, options, block, parent, _lists.Count > 0 ? _lists.Pop() : new DisplayList());

        // A drag's preview stays in front: a popup that opens during the drag goes behind it.
        if (!options.IsDragImage && _open.Count > 0 && _open[^1].Options.IsDragImage)
        {
            _open.Insert(_open.Count - 1, popup);
        }
        else
        {
            _open.Add(popup);
        }

        tree.AttachRoot(root);
        Rect laid = tree.Root.IsNone ? default : tree.Nodes.GetRef(tree.Root).Layout.Rect;
        Resize(popup, _window ?? new Size(laid.Width, laid.Height));
        if (!options.IsTransient && !options.IsDragImage)
        {
            tree.Focus.PushScope(root, options.IsModal);

            // The anchor shows that its popup is open (design §7.3, "The anchor's Open").
            if (tree.Nodes.IsAlive(anchor))
            {
                tree.SetPseudoState(anchor, PseudoState.Open, on: true);
            }
        }

        if (options.IsModal)
        {
            ModalOpened?.Invoke(root);
        }

        tree.NoteWork();
        return popup;
    }

    /// <summary>
    /// Gets a value indicating whether a popup waits for its anchor to join a root.
    /// </summary>
    public bool HasWaiting => _waiting.Count > 0;

    /// <summary>
    /// Keeps a popup asked for while its anchor is in no root, to open at the next frame (<see cref="OpenWaiting"/>).
    /// </summary>
    /// <param name="block">The popup's block.</param>
    public void Wait(PopupBlock block)
    {
        if (!_waiting.Contains(block))
        {
            _waiting.Add(block);
        }

        tree.NoteWork();
    }

    /// <summary>
    /// Forgets a popup that waited: it closed, or its scope went away.
    /// </summary>
    /// <param name="block">The popup's block.</param>
    public void StopWaiting(PopupBlock block)
    {
        if (_waiting.Count > 0)
        {
            _ = _waiting.Remove(block);
        }
    }

    /// <summary>
    /// Opens the popups that waited, at the start of a frame, each once its anchor is in a root: a popup opened from a popup's content
    /// finds its parent popup, which opened before it, in this same call. One whose anchor is still in no root waits on.
    /// </summary>
    public void OpenWaiting()
    {
        for (int i = 0; i < _waiting.Count; )
        {
            PopupBlock block = _waiting[i];
            if (!block.AnchorInRoot)
            {
                i++;
                continue;
            }

            _waiting.RemoveAt(i);
            block.OpenWaiting();

            // A popup that opened may have put the anchors of others in a root: look again from the start.
            i = 0;
        }
    }

    /// <inheritdoc/>
    public void Close(PopupHandle popup)
    {
        // The children first, so that their anchors are alive while they give the focus back.
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            if (i < _open.Count && _open[i].Parent == popup)
            {
                _open[i].Block.Dismiss();
            }
        }

        int index = _open.IndexOf(popup);
        if (index < 0)
        {
            return;
        }

        _open.RemoveAt(index);

        // A list taken again is not built until the next frame; until then, the pointer tests skip it, not the closed popup's hits.
        popup.List.Build = 0;
        _lists.Push(popup.List);

        // The main list is drawn again without the popup's.
        tree.MarkRootsToBuild();
    }

    /// <summary>
    /// Starts a popup's exit (design §7.3, "Motion"): it takes neither the pointer nor the focus from now on, and its focus scope is
    /// popped, so that the focus goes back while it leaves.
    /// </summary>
    /// <param name="popup">The popup.</param>
    public void BeginClose(PopupHandle popup)
    {
        popup.Closing = true;
        tree.SetInert(popup.Root, inert: true);
        tree.Focus.PopScope(popup.Root);
        if (!popup.Options.IsTransient && !popup.Options.IsDragImage && tree.Nodes.IsAlive(popup.Anchor) && !OpenAgainst(popup.Anchor))
        {
            tree.SetPseudoState(popup.Anchor, PseudoState.Open, on: false);
        }
    }

    // Whether another popup that is not transient is open against a node and not leaving.
    private bool OpenAgainst(NodeHandle anchor)
    {
        foreach (PopupHandle popup in _open)
        {
            if (popup.Anchor == anchor && !popup.Closing && !popup.Options.IsTransient && !popup.Options.IsDragImage)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gives each popup's root the window's new size: the window was resized (design §2.11.1).
    /// </summary>
    /// <param name="size">The window's client size, in logical pixels.</param>
    public void ResizeAll(Size size)
    {
        _window = size;
        foreach (PopupHandle popup in _open)
        {
            Resize(popup, size);
        }
    }

    /// <summary>
    /// Returns the open popup whose root is <paramref name="root"/>, or <see langword="null"/>.
    /// </summary>
    /// <param name="root">A node with no parent.</param>
    /// <returns>The popup.</returns>
    public PopupHandle? PopupOf(NodeHandle root)
    {
        foreach (PopupHandle popup in _open)
        {
            if (popup.Root == root)
            {
                return popup;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the open popup a node is in, or <see langword="null"/> for a node of the window's tree.
    /// </summary>
    /// <param name="node">A live node.</param>
    /// <returns>The popup.</returns>
    public PopupHandle? PopupContaining(NodeHandle node)
    {
        if (_open.Count == 0 || !tree.Nodes.IsAlive(node))
        {
            return null;
        }

        NodeHandle at = node;
        while (!tree.Nodes.GetRef(at).Parent.IsNone)
        {
            at = tree.Nodes.GetRef(at).Parent;
        }

        return PopupOf(at);
    }

    /// <summary>
    /// Closes the popups a press outside them closes (design §7.3, "Closing"): with the press in <paramref name="pressed"/>, each
    /// popup that is not modal, dismisses on an outside press, and is neither that popup nor one of its ancestors or descendants;
    /// on a modal popup's backdrop, that popup when it dismisses on it.
    /// </summary>
    /// <param name="pressed">The popup the press is in, or <see langword="null"/> for the window's tree.</param>
    /// <param name="onBackdrop">Whether the press is on a modal popup's root itself, outside its content.</param>
    /// <returns>Whether the press stops here: it was on a modal backdrop, or a popup it closed consumes outside presses.</returns>
    public bool PressOutside(PopupHandle? pressed, bool onBackdrop)
    {
        if (onBackdrop && pressed is not null)
        {
            if (pressed.Options.DismissesOnOutsidePress)
            {
                pressed.Block.Dismiss();
            }

            return true;
        }

        bool consumed = false;
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            if (i >= _open.Count)
            {
                continue;
            }

            PopupHandle popup = _open[i];
            if (popup.Options.IsModal || popup.Closing || !popup.Options.DismissesOnOutsidePress || Related(popup, pressed))
            {
                continue;
            }

            consumed |= popup.Options.ConsumeOutsidePress;
            popup.Block.Dismiss();
        }

        return consumed;
    }

    /// <summary>
    /// Closes the front popup that is not leaving: Esc's default action, after no handler stopped the key (design §7.3).
    /// </summary>
    /// <returns>Whether a popup was closed.</returns>
    public bool CloseFront()
    {
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            if (!_open[i].Closing && !_open[i].Options.IsDragImage)
            {
                _open[i].Block.Dismiss();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Closes every popup that is not modal: the window stopped being active (Avalonia's <c>Popup.cs</c> lines 994 to 1033).
    /// </summary>
    public void CloseNonModal()
    {
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            if (i < _open.Count && !_open[i].Options.IsModal && !_open[i].Options.IsDragImage && !_open[i].Closing)
            {
                _open[i].Block.Dismiss();
            }
        }
    }

    /// <summary>
    /// Gets where a drag's preview goes: its frame's top left, in the window's logical pixels (design §7.3, "The drag's preview
    /// root").
    /// </summary>
    public Vector2 DragImageAt { get; private set; }

    /// <summary>
    /// Puts a drag's preview at a point: its frame's top left, in the window's logical pixels. The preview's list is built again in
    /// the next frame; with no popup open yet, the point waits for it.
    /// </summary>
    /// <param name="popup">The preview's popup, or <see langword="null"/> while it opens.</param>
    /// <param name="topLeft">The point.</param>
    public void MoveDragImage(PopupHandle? popup, Vector2 topLeft)
    {
        DragImageAt = topLeft;
        if (popup is null || popup.Closing || !tree.Nodes.IsAlive(popup.Frame))
        {
            return;
        }

        ref LayoutState frame = ref tree.Nodes.GetRef(popup.Frame).Layout;
        if (frame.Rect.X != topLeft.X || frame.Rect.Y != topLeft.Y)
        {
            frame.Rect = frame.Rect with { X = topLeft.X, Y = topLeft.Y };
            Invalidation.Invalidate(tree, popup.Root, ChangeKind.Color);
        }
    }

    // A popup in the same chain as the pressed one: the pressed one, an ancestor, or a descendant (Flutter's TapRegion group).
    private static bool Related(PopupHandle popup, PopupHandle? pressed)
    {
        if (pressed is null)
        {
            return false;
        }

        for (PopupHandle? at = pressed; at is not null; at = at.Parent)
        {
            if (at == popup)
            {
                return true;
            }
        }

        for (PopupHandle? at = popup; at is not null; at = at.Parent)
        {
            if (at == pressed)
            {
                return true;
            }
        }

        return false;
    }

    // The root takes the window's size, and the frame no more than it on the axes the popup resizes on (design §7.3, "Placement").
    private void Resize(PopupHandle popup, Size size)
    {
        PopupAdjustment adjust = popup.Options.Placement.Adjustment;
        ref UiNode frame = ref tree.Nodes.GetRef(popup.Frame);
        LayoutStyle style = frame.Layout.Style with
        {
            MaxWidth = (adjust & PopupAdjustment.ResizeX) != 0 ? Length.Px(size.Width) : default,
            MaxHeight = (adjust & PopupAdjustment.ResizeY) != 0 ? Length.Px(size.Height) : default,
        };
        if (style != frame.Layout.Style)
        {
            tree.SetStyle(popup.Frame, style);
        }

        Invalidation.NotifyPopupResized(tree, popup.Root, size);
    }
}
