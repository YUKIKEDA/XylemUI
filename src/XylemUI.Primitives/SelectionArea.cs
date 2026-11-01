using System.Numerics;
using System.Text;
using XylemUI.Text.LineBreak;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

public static partial class Primitive
{
    /// <summary>
    /// Makes an area whose text can be selected across its text leaves (design §4.10, Flutter's <c>SelectionArea</c>): a press
    /// starts a selection, a drag extends it, a double click selects a word and a triple click a paragraph, Control+A selects all,
    /// and Control+C or Control+Insert copies it, with a line break between leaves. The area takes the focus when pressed, and the
    /// selection goes when it loses it inside the window, but to its own context menu. A text field's text and the text under
    /// <c>.Unselectable()</c> are left out.
    /// </summary>
    /// <param name="child">The content.</param>
    /// <param name="options">The selection's colors and the context menu; <see langword="null"/> for the OS's colors and no menu.</param>
    /// <returns>The area, a box around the content.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element SelectionArea(Element child, SelectionAreaOptions? options = null) =>
        new SelectionAreaState(options ?? new SelectionAreaOptions()).Build(child);

    // A place in the area's text: a leaf of the list the area collected, and a position in its text.
    private readonly record struct TextPoint(int Leaf, int Offset) : IComparable<TextPoint>
    {
        public int CompareTo(TextPoint other) => Leaf != other.Leaf ? Leaf.CompareTo(other.Leaf) : Offset.CompareTo(other.Offset);
    }

    private sealed class SelectionAreaState(SelectionAreaOptions options)
    {
        // Flutter's EdgeDraggingAutoScroller: the speed, in logical pixels a second, for each pixel past the edge
        // (widgets/scrollable.dart line 1185).
        private const float ScrollSpeed = 30;

        // The text leaves of the area in the order of the tree, collected at each press and kept, so that a press allocates nothing
        // once the list is as long as the area.
        private readonly List<NodeHandle> _leaves = [];
        private readonly StringBuilder _copy = new();
        private UiTree _tree = null!;
        private NodeHandle _area;
        private WindowInput? _input;
        private AutoScroll? _scroller;

        // The selection: from the anchor to the focus; the unit a double or a triple click selects by (1, 2, 3), and the unit the
        // press selected, which a drag keeps.
        private TextPoint _anchor;
        private TextPoint _focus;
        private int _unit;
        private TextPoint _unitStart;
        private TextPoint _unitEnd;
        private bool _dragging;
        private bool _started;
        private Vector2 _pointer;

        // The context menu, and the commands it was last opened with.
        private ContextMenuHost? _menu;
        private SelectionMenuCommands? _commands;

        // The leaves the leaves' selections were last set on: from _paintedFrom to _paintedTo, none when _paintedTo is -1.
        private int _paintedFrom;
        private int _paintedTo = -1;

        public Element Build(Element child)
        {
            Element area = Box(child);
            _tree = area.Tree!;
            _area = area.Node;
            _tree.SetSelectsText(_area);
            _ = area.Focusable()
                .On(PointerEventKind.Down, EventPhase.Bubble, PointerDown)
                .On(PointerEventKind.Move, EventPhase.Bubble, PointerMove)
                .On(PointerEventKind.Up, EventPhase.Bubble, PointerUp)
                .On(PointerEventKind.Cancel, EventPhase.Bubble, PointerCancel)
                .On(KeyEventKind.Down, EventPhase.Bubble, KeyDown)
                .On(KeyEventKind.Up, EventPhase.Bubble, KeyUp)
                .On(FocusEventKind.FocusOut, EventPhase.Bubble, FocusOut);
            if (options.ContextMenu is { } menu)
            {
                _menu = new ContextMenuHost(_tree, _area, () => menu(_commands!));
            }

            Scope.OnCleanup(StopScrolling);
            return area;
        }

        private void PointerDown(in PointerEvent e)
        {
            // The right button keeps the selection; the area takes the focus itself, as the window gives it on the left button only
            // (design §4.10).
            if (e.Button == PointerButton.Right && _menu is not null)
            {
                if (_tree.Focus.Focused != _area && !KeepsFocus(e.TargetNode))
                {
                    _ = _tree.Focus.Focus(_area);
                }

                return;
            }

            // A press that a focusable descendant took the focus for (a button, a text field), or that kept it elsewhere
            // (.KeepsFocusOnPress()), starts no selection (design §4.10, "Pressable parts"); a part that keeps the focus does not
            // move it from the area either, so the press is looked up from its node to the area (PR #503's review).
            if (e.Button != PointerButton.Left || _tree.Focus.Focused != _area || KeepsFocus(e.TargetNode))
            {
                return;
            }

            _input = e.Input;
            Collect();
            _pointer = e.Position * _input.Frames.Scale;
            if (_leaves.Count == 0 || !Locate(_pointer, out TextPoint at, out bool onLink) || onLink)
            {
                return;
            }

            int count = Math.Min(e.ClickCount, 3);

            // Shift+click extends from the anchor of the last press (Flutter collapses first when there is none).
            if (count == 1 && (e.Modifiers & KeyModifiers.Shift) != 0 && _started)
            {
                _unit = 1;
                _focus = at;
            }
            else if (count == 1)
            {
                _unit = 1;
                _anchor = _focus = at;
            }
            else
            {
                _unit = count;
                (_unitStart, _unitEnd) = UnitAt(at);
                (_anchor, _focus) = (_unitStart, _unitEnd);
            }

            _dragging = true;
            _started = true;
            Paint();
        }

        private void PointerMove(in PointerEvent e)
        {
            if (!_dragging || (e.Buttons & PointerButtons.Left) == 0)
            {
                return;
            }

            _input = e.Input;
            _pointer = e.Position * _input.Frames.Scale;
            DragTo(_pointer);
            Scrolling();
        }

        // The press lost its capture: a drag that a drag source inside the area started took it, or the OS (design §7, "Drag and
        // drop"). The selection stops following the pointer and stays where it is.
        private void PointerCancel(in PointerEvent e)
        {
            _dragging = false;
            StopScrolling();
        }

        private void PointerUp(in PointerEvent e)
        {
            if (e.Button == PointerButton.Left)
            {
                _dragging = false;
                StopScrolling();
            }
            else if (e.Button == PointerButton.Right && _menu is not null)
            {
                e.StopPropagation();
                OpenMenu(e.Position);
            }
        }

        private void KeyDown(in KeyEvent e)
        {
            if (_menu is not null && IsMenuKey(e))
            {
                e.StopPropagation();
                OpenMenu(at: null);
                return;
            }

            if (e.PrefersCharacter || e.Modifiers != KeyModifiers.Control)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.A:
                    SelectAll();
                    e.StopPropagation();
                    return;
                case Key.C or Key.Insert:
                    Copy();
                    e.StopPropagation();
                    return;
            }
        }

        private void KeyUp(in KeyEvent e)
        {
            if (_menu is not null && IsMenuKey(e))
            {
                e.StopPropagation();
                OpenMenu(at: null);
            }
        }

        private void SelectAll()
        {
            Collect();
            if (_leaves.Count > 0)
            {
                _unit = 1;
                _started = true;
                _anchor = new TextPoint(0, 0);
                _focus = new TextPoint(_leaves.Count - 1, LengthOf(_leaves.Count - 1));
                Paint();
            }
        }

        // Opens the context menu with the commands as they are now (design §4.10): at the pointer, or at the bottom left of the
        // selection's end, or below the area with no selection.
        private void OpenMenu(Vector2? at)
        {
            Collect();
            bool text = false;
            for (int i = 0; i < _leaves.Count && !text; i++)
            {
                text = LengthOf(i) > 0;
            }

            _commands = new SelectionMenuCommands(new MenuCommand(_anchor != _focus, Copy), new MenuCommand(text, SelectAll));
            if (at is { } point)
            {
                _menu!.OpenAt(point);
            }
            else if (_anchor != _focus && _focus.Leaf < _leaves.Count)
            {
                _menu!.OpenAt(EndCorner());
            }
            else
            {
                _menu!.OpenBelow();
            }
        }

        // The bottom left of the caret's box at the selection's end, in the window's logical pixels.
        private Vector2 EndCorner()
        {
            NodeHandle node = _leaves[_focus.Leaf];
            TextLeaf leaf = _tree.Nodes.GetRef(node).Text!;
            Vector2 origin = WindowOrigin(_tree, node) + new Vector2(leaf.PaddingBorder.Left, leaf.PaddingBorder.Top);
            if (leaf.LineBreaks is not { Committed: >= 0 })
            {
                return origin;
            }

            Rect caret = TextCaret.CaretGeometry(leaf, _focus.Offset, Affinity.Downstream).Rect;
            return origin + new Vector2(caret.X, caret.Y + caret.Height);
        }

        // Inside the window, the area that loses the focus loses its selection, but to its own context menu, whose keys go on to the
        // area; a window that goes inactive keeps it (design §4.10, Flutter's SelectableRegion, widgets/selectable_region.dart lines
        // 526 to 542).
        private void FocusOut(in FocusEvent e)
        {
            if (e.TargetNode != _area || !_tree.Focus.IsWindowActive || InOwnPopup(_tree.Focus.Focused))
            {
                return;
            }

            _dragging = false;
            _started = false;
            StopScrolling();
            _anchor = _focus = default;
            Paint();
        }

        // Whether a node is in a popup opened from the area: its context menu, whose keys go on to the area.
        private bool InOwnPopup(NodeHandle node)
        {
            if (!_tree.Nodes.IsAlive(node) || _tree.Popups.PopupContaining(node) is null)
            {
                return false;
            }

            for (NodeHandle at = node; _tree.Nodes.IsAlive(at); at = _tree.KeyParent(at))
            {
                if (at == _area)
                {
                    return true;
                }
            }

            return false;
        }

        // The focus moves to the point, by the unit of the press.
        private void DragTo(Vector2 physical)
        {
            if (!Locate(physical, out TextPoint at, out bool _))
            {
                return;
            }

            if (_unit <= 1)
            {
                _focus = at;
            }
            else
            {
                (TextPoint start, TextPoint end) = UnitAt(at);
                bool before = start.CompareTo(_unitStart) < 0;
                _anchor = before ? _unitEnd : _unitStart;
                _focus =
                    before ? start
                    : end.CompareTo(_unitEnd) > 0 ? end
                    : _unitEnd;
            }

            Paint();
        }

        // The word or the paragraph around a point, by the unit of the press.
        private (TextPoint Start, TextPoint End) UnitAt(TextPoint at)
        {
            ReadOnlySpan<char> text = TextOf(at.Leaf);
            TextSelection unit = _unit == 2 ? TextUnits.WordAt(text, at.Offset) : TextUnits.ParagraphAt(text, at.Offset);
            return (new TextPoint(at.Leaf, unit.Start), new TextPoint(at.Leaf, unit.End));
        }

        // The area's text leaves in the order of the tree, leaving out the subtrees marked unselectable and those hidden; each
        // leaf's selection keeps its place, so that a hit region of the display list finds it.
        private void Collect()
        {
            _leaves.Clear();
            NodeHandle node = _tree.Nodes.GetRef(_area).FirstChild;
            while (!node.IsNone)
            {
                ref UiNode n = ref _tree.Nodes.GetRef(node);
                bool skips = n.Input is { Unselectable: true } || n.Layout.Style.Hidden;
                if (!skips && n.Text is not null)
                {
                    LeafSelection selection = _tree.TextSelectionOf(node);
                    selection.Owner = this;
                    selection.Index = _leaves.Count;
                    _leaves.Add(node);
                }

                if (!skips && !n.FirstChild.IsNone)
                {
                    node = n.FirstChild;
                    continue;
                }

                node = NextOutOf(node);
            }

            if (_paintedTo >= _leaves.Count)
            {
                _paintedTo = -1;
            }
        }

        // The node after a node's subtree in the order of the tree, inside the area; none past its end.
        private NodeHandle NextOutOf(NodeHandle node)
        {
            while (node != _area)
            {
                ref UiNode n = ref _tree.Nodes.GetRef(node);
                if (!n.NextSibling.IsNone)
                {
                    return n.NextSibling;
                }

                node = n.Parent;
            }

            return default;
        }

        // The place of the text at a point of the window (design §4.10, "Order"), in one walk of the display list's hit regions: the
        // place under the point in the leaf that holds it, or else the end of the last leaf, in the order of the tree, that is
        // before the point (on a line above it, or on its line and to its left), or the start of the first leaf. Whether the point
        // is on a link of the leaf comes with it.
        private bool Locate(Vector2 physical, out TextPoint at, out bool onLink)
        {
            at = default;
            onLink = false;
            if (_input is null || _leaves.Count == 0)
            {
                return false;
            }

            DisplayList list = _input.Frames.ListFor(_area);
            float scale = _input.Frames.Scale;
            ReadOnlySpan<HitTestEntry> entries = list.HitEntries;
            int before = -1;
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                NodeHandle node = entries[i].Node;
                if (!_tree.Nodes.IsAlive(node) || _tree.Nodes.GetRef(node).Text is not { Selection: { } selection } leaf)
                {
                    continue;
                }

                if (!ReferenceEquals(selection.Owner, this) || selection.Index >= _leaves.Count || _leaves[selection.Index] != node)
                {
                    continue;
                }

                Vector2 local = list.PointInRegion(i, physical) / scale;
                float width = (entries[i].Right - entries[i].Left) / scale;
                float height = (entries[i].Bottom - entries[i].Top) / scale;
                if (local.X >= 0 && local.Y >= 0 && local.X < width && local.Y < height)
                {
                    at = new TextPoint(selection.Index, OffsetIn(leaf, local, out onLink));
                    return true;
                }

                if ((local.Y >= height || (local.Y >= 0 && local.X >= width)) && selection.Index > before)
                {
                    before = selection.Index;
                }
            }

            at = before >= 0 ? new TextPoint(before, LengthOf(before)) : new TextPoint(0, 0);
            return true;
        }

        // The position in a leaf's text under a point of its border box, by its committed lines; the start when it has none.
        private static int OffsetIn(TextLeaf leaf, Vector2 local, out bool onLink)
        {
            onLink = false;
            if (leaf.Shaped?.Result is null || leaf.LineBreaks is not { Committed: >= 0 })
            {
                return 0;
            }

            float x = local.X - leaf.PaddingBorder.Left;
            float y = local.Y - leaf.PaddingBorder.Top;
            int line = TextCaret.LineAt(leaf, y, out _, out _);
            if (line < 0)
            {
                return 0;
            }

            if (leaf.Ranges is { HasLinks: true } ranges && TextCaret.ClusterAt(leaf, x, y) is >= 0 and int cluster)
            {
                foreach (PaintRun run in ranges.Paint)
                {
                    if (cluster < run.Start + run.Length)
                    {
                        onLink = run.Link >= 0;
                        break;
                    }
                }
            }

            return TextCaret.HitTestText(leaf, line, x).TextOffset;
        }

        // Sets the leaves' selections to the area's: the leaves between the two ends in full, the ends from or to their places;
        // the leaves that leave the selection are cleared.
        private void Paint()
        {
            bool empty = _anchor == _focus || _leaves.Count == 0;
            (TextPoint from, TextPoint to) = _anchor.CompareTo(_focus) <= 0 ? (_anchor, _focus) : (_focus, _anchor);
            int first = empty ? 0 : from.Leaf;
            int last = empty ? -1 : to.Leaf;
            for (int i = _paintedFrom; i <= _paintedTo; i++)
            {
                if ((i < first || i > last) && i < _leaves.Count && _tree.Nodes.IsAlive(_leaves[i]))
                {
                    _tree.SetTextSelection(_leaves[i], 0, 0, null, null);
                }
            }

            for (int i = first; i <= last; i++)
            {
                int start = i == from.Leaf ? from.Offset : 0;
                int end = i == to.Leaf ? to.Offset : LengthOf(i);
                _tree.SetTextSelection(_leaves[i], start, end, options.SelectionBackground, options.SelectionForeground);
            }

            (_paintedFrom, _paintedTo) = (first, last);
        }

        // Puts the selected text on the clipboard: each leaf's part, a line break between leaves, the rooms of inline objects left
        // out (design §4.10, "Copy").
        private void Copy()
        {
            if (_anchor == _focus || _tree.Clipboard is not { } clipboard)
            {
                return;
            }

            (TextPoint from, TextPoint to) = _anchor.CompareTo(_focus) <= 0 ? (_anchor, _focus) : (_focus, _anchor);
            _copy.Clear();
            for (int i = from.Leaf; i <= to.Leaf && i < _leaves.Count; i++)
            {
                if (i > from.Leaf)
                {
                    _ = _copy.Append('\n');
                }

                TextLeaf leaf = _tree.Nodes.GetRef(_leaves[i]).Text!;
                int start = i == from.Leaf ? from.Offset : 0;
                int end = Math.Min(i == to.Leaf ? to.Offset : leaf.Length, leaf.Length);
                ReadOnlySpan<InlineObject> rooms = leaf.Ranges is { } ranges ? ranges.Inlines : default;
                for (int at = start; at < end; at++)
                {
                    if (!IsRoom(rooms, at))
                    {
                        _ = _copy.Append(leaf.Text[at]);
                    }
                }
            }

            _ = clipboard.SetText(_copy.ToString());
        }

        private static bool IsRoom(ReadOnlySpan<InlineObject> rooms, int at)
        {
            foreach (InlineObject room in rooms)
            {
                if (room.Position == at)
                {
                    return true;
                }
            }

            return false;
        }

        private ReadOnlySpan<char> TextOf(int index) => _tree.Nodes.GetRef(_leaves[index]).Text!.Text;

        private int LengthOf(int index) => _tree.Nodes.GetRef(_leaves[index]).Text!.Length;

        // While the drag is past the edge of the nearest scroll container around the area, the container scrolls on the frame clock
        // (design §4.10, "Scrolling while dragging").
        private void Scrolling()
        {
            if (Container() is not { } container || _input is null)
            {
                StopScrolling();
                return;
            }

            _scroller ??= new AutoScroll(this);
            _scroller.Container = container;
            if (Past(container) != Vector2.Zero)
            {
                _tree.Animations.Add(_scroller);
            }
            else
            {
                StopScrolling();
            }
        }

        private void StopScrolling()
        {
            if (_scroller is not null)
            {
                _scroller.Since = null;
                _tree.Animations.Remove(_scroller);
            }
        }

        // The nearest ancestor of the area that scrolls.
        private NodeHandle? Container()
        {
            for (NodeHandle node = _tree.Nodes.GetRef(_area).Parent; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
            {
                if (_tree.ScrollStateOf(node) is not null)
                {
                    return node;
                }
            }

            return null;
        }

        // How far the pointer is past the container's viewport, in logical pixels: negative before its start, positive past its end.
        private Vector2 Past(NodeHandle container)
        {
            if (_input is null || _tree.ScrollStateOf(container) is not { } state)
            {
                return Vector2.Zero;
            }

            DisplayList list = _input.Frames.ListFor(_area);
            ReadOnlySpan<HitTestEntry> entries = list.HitEntries;
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                if (entries[i].Node == container)
                {
                    Vector2 local = list.PointInRegion(i, _pointer) / _input.Frames.Scale;
                    Size viewport = state.ViewportValue;
                    float x =
                        local.X < 0 ? local.X
                        : local.X > viewport.Width ? local.X - viewport.Width
                        : 0;
                    float y =
                        local.Y < 0 ? local.Y
                        : local.Y > viewport.Height ? local.Y - viewport.Height
                        : 0;
                    return new Vector2(x, y);
                }
            }

            return Vector2.Zero;
        }

        // A frame of the scrolling: the container moves by the distance past its edge times the speed, for the time since the last
        // frame, and the focus follows the pointer.
        private bool Scroll(NodeHandle container, TimeSpan elapsed)
        {
            if (!_dragging || _tree.ScrollStateOf(container) is not { } state)
            {
                return false;
            }

            Vector2 past = Past(container);
            if (past == Vector2.Zero)
            {
                return false;
            }

            state.ScrollTo(state.OffsetValue + (past * ScrollSpeed * (float)elapsed.TotalSeconds));

            // The leaves' regions are the last frame's, so the focus reaches what the scroll shows one frame late.
            DragTo(_pointer);
            return true;
        }

        // Whether a node between a pressed node and the area keeps the focus on press (.KeepsFocusOnPress()), the area left out.
        private bool KeepsFocus(NodeHandle pressed)
        {
            for (NodeHandle node = pressed; !node.IsNone && node != _area; node = _tree.Nodes.GetRef(node).Parent)
            {
                if (_tree.Nodes.GetRef(node).Input is { KeepsFocusOnPress: true })
                {
                    return true;
                }
            }

            return false;
        }

        // The area's scrolling on the window's animation list.
        private sealed class AutoScroll(SelectionAreaState area) : Ticking
        {
            public NodeHandle Container { get; set; }

            public TimeSpan? Since { get; set; }

            internal override bool Tick(TimeSpan now)
            {
                TimeSpan elapsed = Since is { } since ? now - since : TimeSpan.Zero;
                Since = now;
                bool runs = area.Scroll(Container, elapsed);
                if (!runs)
                {
                    Since = null;
                }

                return runs;
            }
        }
    }
}
