namespace XylemUI;

/// <summary>
/// What a <see cref="ElementExtensions.Popup"/> keeps (design §7.3, "API"): while its signal is true, a part made in a scope of its
/// own under the block's, put in a new root that the host opens; when the signal turns false, the part leaves (with the motion, if
/// any) and its scope is disposed. A popup that closes itself (an outside press, Esc, the window's deactivation, a scroll) writes
/// false to the signal.
/// </summary>
internal sealed class PopupBlock
{
    private readonly UiTree _tree;
    private readonly NodeHandle _anchor;
    private readonly Signal<bool> _isOpen;
    private readonly Func<Element> _render;
    private readonly PopupOptions _options;
    private readonly ScopeRef _owner;
    private readonly Presence? _presence;
    private ScopeHandle _part;
    private PopupHandle? _popup;

    private PopupBlock(UiTree tree, NodeHandle anchor, Signal<bool> isOpen, Func<Element> render, PopupOptions options, ScopeRef owner)
    {
        _tree = tree;
        _anchor = anchor;
        _isOpen = isOpen;
        _render = render;
        _options = options;
        _owner = owner;
        _presence = options.Motion is null ? null : new Presence(this);
    }

    /// <summary>
    /// Gets the open popup, or <see langword="null"/> while it is closed.
    /// </summary>
    internal PopupHandle? Popup => _popup;

    /// <summary>
    /// Makes a block in a scope of its own under the current one, which opens the popup while the signal is true.
    /// </summary>
    /// <param name="tree">The tree.</param>
    /// <param name="anchor">The node the popup is placed against.</param>
    /// <param name="isOpen">Whether the popup is open.</param>
    /// <param name="render">What the popup shows.</param>
    /// <param name="options">How it is placed and closed.</param>
    /// <returns>The block.</returns>
    internal static PopupBlock Create(UiTree tree, NodeHandle anchor, Signal<bool> isOpen, Func<Element> render, PopupOptions options)
    {
        PopupBlock? block = null;
        _ = Scope.Create(
            () =>
            {
                block = new PopupBlock(tree, anchor, isOpen, render, options, Scope.CurrentRef);
                PopupBlock made = block;
                Scope.OnCleanup(made.CloseNow);
                _ = Effect.Create(() => made.Change(isOpen.Get()));
            },
            "Popup"
        );
        return block!;
    }

    /// <summary>
    /// Closes the popup as it closes itself, and writes false to its signal (design §7.3, "Closing").
    /// </summary>
    internal void Dismiss()
    {
        if (_popup is { Closing: false })
        {
            Leave();
        }

        // The effect then sees the popup closing already.
        _isOpen.Set(false);
    }

    private void Change(bool open)
    {
        if (open)
        {
            if (_popup is { Closing: true })
            {
                Finish();
            }

            if (_popup is null)
            {
                Open();
            }

            return;
        }

        if (_popup is { Closing: false })
        {
            Leave();
        }
    }

    // The part made, put in a frame placed against the anchor, in a root of its own.
    private void Open()
    {
        if (!_tree.Nodes.IsAlive(_anchor))
        {
            return;
        }

        (NodeHandle content, ScopeHandle part) = Build();
        NodeHandle root = _tree.CreateNode(new LayoutStyle());
        bool fills = _options.Placement.Fills;
        LengthPercentageAuto zero = LengthPercentageAuto.Px(0);
        NodeHandle frame = _tree.CreateNode(
            new LayoutStyle
            {
                Position = Position.Absolute,
                Inset = fills
                    ? new EdgeInsets<LengthPercentageAuto>(zero, zero, zero, zero)
                    : new EdgeInsets<LengthPercentageAuto>(zero, zero, default, default),
            }
        );
        _tree.AppendChild(frame, content);
        _tree.AppendChild(root, frame);

        // The content's box takes the pointer, so that a press on it does not reach the window behind and close it (design §7.3,
        // "Hit testing"); a modal popup's root takes it everywhere.
        if (!fills)
        {
            _tree.SetBlocksPointer(frame, true);
        }

        if (_options.IsModal)
        {
            // The modal root takes the pointer over the whole window, behind its content (design §7.3, "Closing").
            _tree.SetBlocksPointer(root, true);
        }

        _part = part;
        _popup = OverlayHost().Open(_anchor, root, frame, _options, this);
        if (_presence is not null)
        {
            _presence.Start(0);
            _presence.Run(1);
        }
    }

    // The popup leaves: at once, or with the motion, taking neither the pointer nor the focus meanwhile.
    private void Leave()
    {
        PopupHandle popup = _popup!;
        OverlayHost().BeginClose(popup);
        if (_presence is null)
        {
            Finish();
            return;
        }

        _presence.Run(0);
    }

    // The popup is gone: its root out of the tree and its part's scope disposed.
    private void Finish()
    {
        PopupHandle? popup = _popup;
        if (popup is null)
        {
            return;
        }

        _popup = null;
        _presence?.Stop();
        OverlayHost().Close(popup);
        if (_tree.Nodes.IsAlive(popup.Root))
        {
            _tree.Remove(popup.Root);
        }

        ScopeHandle part = _part;
        _part = default;
        if (!part.IsNone)
        {
            Scope.Dispose(part);
        }
    }

    // The block's scope was disposed (its anchor's part went away): the popup goes at once, its signal left as it is.
    private void CloseNow()
    {
        if (_popup is { Closing: false } popup)
        {
            OverlayHost().BeginClose(popup);
        }

        Finish();
    }

    private OverlayPopupHost OverlayHost() => _tree.Popups;

    // Makes the part in a new scope under the block's own, with the tree as the current one, as Show makes its parts.
    private (NodeHandle Node, ScopeHandle Scope) Build()
    {
        Element element = default;
        ScopeHandle made = default;
        UiTree? previousTree = UiTree.Current;
        Computation? running = ReactiveRuntime.Current;
        if (!Scope.TryEnter(_owner, out ScopeRef previousScope))
        {
            throw new InvalidOperationException("The scope of Popup was disposed.");
        }

        UiTree.Current = _tree;
        ReactiveRuntime.Current = null;
        try
        {
            made = Scope.Create(() => element = _render(), "Popup part");
        }
        finally
        {
            ReactiveRuntime.Current = running;
            UiTree.Current = previousTree;
            Scope.Exit(previousScope);
        }

        if (element.IsNone || !ReferenceEquals(element.Tree, _tree) || !_tree.Nodes.GetRef(element.Node).Parent.IsNone)
        {
            Scope.Dispose(made);
            throw new InvalidOperationException("The content of Popup is a node of the same tree with no parent.");
        }

        return (element.Node, made);
    }

    // How far the popup is in, moved on the window's frames along the motion's curve (design §2.14): its frame's presence, as Show's.
    private sealed class Presence(PopupBlock block) : Ticking
    {
        private float _value = 1;
        private float _from;
        private float _to = 1;
        private bool _started;
        private TimeSpan _start;

        public void Start(float value)
        {
            _value = value;
            Write();
        }

        public void Run(float to)
        {
            _from = _value;
            _to = to;
            if (block._tree.Animations.ReduceMotion || _value == to)
            {
                Stop();
                _value = to;
                Write();
                Arrived();
                return;
            }

            _started = false;
            block._tree.Animations.Add(this);
        }

        public void Stop() => block._tree.Animations.Remove(this);

        internal override bool Tick(TimeSpan now)
        {
            if (!_started)
            {
                _started = true;
                _start = now;
            }

            ShowMotion motion = block._options.Motion!;
            TimeSpan length = motion.Duration * Math.Abs(_to - _from);
            double progress = length <= TimeSpan.Zero ? 1 : (now - _start) / length;
            if (progress < 1)
            {
                float p = (float)progress;
                _value = _to > _from ? _from + ((_to - _from) * motion.Curve.Transform(p)) : _to + ((_from - _to) * motion.Curve.Transform(1 - p));
                Write();
                return true;
            }

            _value = _to;
            Write();
            Arrived();
            return false;
        }

        private void Arrived()
        {
            if (_to == 0 && block._popup is { Closing: true })
            {
                block.Finish();
            }
        }

        private void Write()
        {
            if (block._popup is { } popup && block._tree.Nodes.IsAlive(popup.Frame))
            {
                block._tree.SetPresence(popup.Frame, _value, block._options.Motion!.Offset);
            }
        }
    }
}
