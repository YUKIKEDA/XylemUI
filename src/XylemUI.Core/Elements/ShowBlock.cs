namespace XylemUI;

/// <summary>
/// What a <see cref="Elements.Show"/> keeps (design §2.7, "The shape of V3"): the node it put in its parent, the scope of that
/// node's part, and whether a part was shown yet. A change of the condition makes the other part in a new scope, puts it in the
/// place of the old node, and disposes the old part. With a motion (design §2.14), the old part first leaves, paused, and the new
/// one enters; a condition that comes back while the old part leaves turns its exit into an entry.
/// </summary>
internal sealed class ShowBlock
{
    private readonly UiTree _tree;
    private readonly Func<Element> _render;
    private readonly Func<Element>? _fallback;
    private readonly ScopeRef _owner;
    private readonly ShowMotion? _motion;
    private readonly Presence? _presence;
    private ScopeHandle _part;
    private NodeHandle _placeholder;
    private bool _hasPart;

    // The truth the shown part stands for, and the last truth of the condition; they differ while the part leaves.
    private bool _partShown;
    private bool _target;
    private bool _leaving;

    private ShowBlock(UiTree tree, Func<Element> render, Func<Element>? fallback, ShowMotion? motion, ScopeRef owner)
    {
        _tree = tree;
        _render = render;
        _fallback = fallback;
        _motion = motion;
        _owner = owner;
        _presence = motion is null ? null : new Presence(this);
    }

    /// <summary>
    /// Gets the node the block puts in its parent now: the part's node, or the empty hidden node.
    /// </summary>
    internal NodeHandle Node { get; private set; }

    /// <summary>
    /// Makes a block in a scope of its own under the current one, and its first part.
    /// </summary>
    /// <param name="tree">The tree the parts are made in.</param>
    /// <param name="when">The condition.</param>
    /// <param name="render">What the true state shows.</param>
    /// <param name="fallback">What the false state shows; nothing when <see langword="null"/>.</param>
    /// <param name="motion">How a part enters and leaves; at once when <see langword="null"/>.</param>
    /// <returns>The block.</returns>
    internal static ShowBlock Create(UiTree tree, IReadOnlySignal<bool> when, Func<Element> render, Func<Element>? fallback, ShowMotion? motion)
    {
        ShowBlock? block = null;
        _ = Scope.Create(
            () =>
            {
                block = new ShowBlock(tree, render, fallback, motion, Scope.CurrentRef);
                ShowBlock made = block;
                Scope.OnCleanup(made.FreePlaceholder);
                Scope.OnCleanup(made.StopMotion);
                _ = Effect.Create(() => made.Change(when.Get()));
            },
            "Show"
        );
        return block!;
    }

    // A change of the condition's truth. With no motion, or nothing shown yet, the part changes at once. With one, the shown part
    // leaves first (Leave), or, when the condition came back to its truth while it left, enters again from where it is.
    private void Change(bool shown)
    {
        _target = shown;
        if (_motion is null || !_hasPart)
        {
            Show(shown);
            return;
        }

        if (shown == _partShown)
        {
            if (_leaving)
            {
                _leaving = false;
                if (!_part.IsNone)
                {
                    Scope.Resume(_part);
                }

                _presence!.Run(1);
            }

            return;
        }

        if (!_leaving)
        {
            Leave();
        }
    }

    // The shown part starts to leave: its scope paused, so it computes nothing, and its presence going to 0. Nothing shown (the
    // empty node) has nothing to fade, and gives way at once.
    private void Leave()
    {
        if (Node == _placeholder)
        {
            Swap();
            return;
        }

        if (_tree.Nodes.GetRef(Node).Parent.IsNone)
        {
            throw new InvalidOperationException("The condition of Show changed before its node was put in a parent; put the node in a parent first.");
        }

        _leaving = true;
        _tree.Focus.Lose(Node);
        if (!_part.IsNone)
        {
            Scope.Pause(_part);
        }

        _presence!.Run(0);
    }

    // The part that left gives way to the part of the condition's last truth, which enters from nothing.
    private void Swap()
    {
        _leaving = false;
        Show(_target);
        if (Node != _placeholder)
        {
            _presence!.Start(0);
            _presence.Run(1);
        }
    }

    // The presence reached the end it ran to: a part that left gives way.
    private void Arrived(float presence)
    {
        if (presence == 0 && _leaving)
        {
            Swap();
        }
    }

    private void StopMotion() => _presence?.Stop();

    // Shows the part of the condition's truth at once. The effect runs again only when the condition's value changed, and a bool's
    // change is a change of its truth.
    private void Show(bool shown)
    {
        _partShown = shown;
        bool first = !_hasPart;
        NodeHandle old = Node;
        ScopeHandle oldPart = _part;

        // The node the caller holds is the one it puts in a parent; a swap before that would leave the caller a freed node.
        if (!first && _tree.Nodes.GetRef(old).Parent.IsNone)
        {
            throw new InvalidOperationException("The condition of Show changed before its node was put in a parent; put the node in a parent first.");
        }

        _hasPart = true;
        Func<Element>? part = shown ? _render : _fallback;
        (NodeHandle next, ScopeHandle nextPart) = part is null ? (Placeholder(), default) : Build(part);
        if (!first)
        {
            _tree.Focus.Lose(old);
            _tree.ReplaceChild(old, next);
        }

        Node = next;
        _part = nextPart;
        if (!first)
        {
            if (old == _placeholder)
            {
                FreePlaceholder();
            }
            else
            {
                Scope.Dispose(oldPart);
            }
        }
    }

    // Makes a part in a new scope under the block's own, with the tree as the current one, and with no effect reading what the part
    // reads: the part's own effects follow its signals.
    private (NodeHandle Node, ScopeHandle Scope) Build(Func<Element> part)
    {
        Element element = default;
        ScopeHandle made = default;
        UiTree? previousTree = UiTree.Current;
        Computation? running = ReactiveRuntime.Current;
        if (!Scope.TryEnter(_owner, out ScopeRef previousScope))
        {
            throw new InvalidOperationException("The scope of Show was disposed.");
        }

        UiTree.Current = _tree;
        ReactiveRuntime.Current = null;
        try
        {
            made = Scope.Create(() => element = part(), "Show part");
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
            throw new InvalidOperationException("A part of Show returns a node of the same tree with no parent.");
        }

        return (element.Node, made);
    }

    // The empty node that keeps the place of a part that shows nothing: hidden, so the layout does not see it.
    private NodeHandle Placeholder()
    {
        if (!_tree.Nodes.IsAlive(_placeholder))
        {
            _placeholder = _tree.CreateNode(new LayoutStyle { Hidden = true });
        }

        return _placeholder;
    }

    // How far the shown part is in, moved on the window's frames from where it is to an end along the motion's curve (design
    // §2.14): its root node's presence, which the frame multiplies into its opacity and its offset.
    private sealed class Presence(ShowBlock block) : Ticking
    {
        private float _value = 1;
        private float _from;
        private float _to = 1;
        private bool _started;
        private TimeSpan _start;

        // Sets where the part is now, with no move.
        public void Start(float value)
        {
            _value = value;
            Write();
        }

        // Moves the part to an end: in the motion's duration times the distance left, so that a turned exit takes as long back as
        // it went. Less motion, or a part already there, arrives at once.
        public void Run(float to)
        {
            _from = _value;
            _to = to;
            if (block._tree.Animations.ReduceMotion || _value == to)
            {
                Stop();
                _value = to;
                Write();
                block.Arrived(to);
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

            ShowMotion motion = block._motion!;
            TimeSpan length = motion.Duration * Math.Abs(_to - _from);
            double progress = length <= TimeSpan.Zero ? 1 : (now - _start) / length;
            if (progress < 1)
            {
                // The curve runs forward on the way in, and backward on the way out (Flutter's reverse of a curved animation).
                float p = (float)progress;
                _value = _to > _from ? _from + ((_to - _from) * motion.Curve.Transform(p)) : _to + ((_from - _to) * motion.Curve.Transform(1 - p));
                Write();
                return true;
            }

            _value = _to;
            Write();

            // A part that left gives way here, and the part that enters runs this again (Run): it stays in the list then.
            block.Arrived(_to);
            if (!_started && Slot >= 0)
            {
                // The part that entered starts at this frame's time: the frame that ended the exit.
                _started = true;
                _start = now;
                return true;
            }

            return false;
        }

        private void Write()
        {
            if (block._tree.Nodes.IsAlive(block.Node) && block.Node != block._placeholder)
            {
                block._tree.SetPresence(block.Node, _value, block._motion!.Offset);
            }
        }
    }

    private void FreePlaceholder()
    {
        if (_tree.Nodes.IsAlive(_placeholder))
        {
            _tree.Remove(_placeholder);
        }

        _placeholder = default;
    }
}
