using System.Numerics;
using System.Text;
using XylemUI.Text.Unicode;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

/// <content>
/// The text field (design §4.9).
/// </content>
public static partial class Primitive
{
    /// <summary>
    /// Makes a text field (design §4.9): a box that holds a line of text, or many with <see cref="TextFieldOptions.Multiline"/>,
    /// that takes the focus, the typing, the keys of Windows' text fields (moves by cluster, word, line, and page; deletion; Control+A,
    /// C, X, V, Z, Y), the pointer's selection (a click, a double click for a word, a triple click for all or a paragraph, a drag),
    /// and the clipboard, and scrolls to keep the caret in view. It draws its selection and its caret over the text, and no
    /// background or border.
    /// </summary>
    /// <param name="text">The text, written both ways: an edit writes it, and a write from the app moves the caret to the end and
    /// forgets the undo.</param>
    /// <param name="options">What the field does beyond holding text; <see langword="null"/> for a field of one line with none.</param>
    /// <returns>The field.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element TextField(Signal<string> text, TextFieldOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TextFieldState(text, options ?? new TextFieldOptions()).Build();
    }

    // One text field: its editor, the nodes that show it, and the caret's blink.
    private sealed class TextFieldState(Signal<string> text, TextFieldOptions options)
    {
        private const char Bullet = '●';

        private readonly TextEditor _editor = new(options.Multiline) { MaxLength = options.MaxLength, Password = options.Password };

        private readonly Signal<int> _version = Signal.Create(0);
        private readonly ScrollState _scroll = new();
        private readonly SendOrPostCallback _tick = static state => ((TextFieldState)state!).Blink();
        private UiTree _tree = null!;
        private NodeHandle _field;
        private NodeHandle _leaf;
        private NodeHandle _caret;
        private NodeHandle _placeholder;
        private ITimer? _timer;
        private SynchronizationContext? _ui;
        private bool _caretShown = true;
        private bool _writing;
        private float? _preferredX;

        // What a press and a drag select: by cluster, by word (a double click), or by line or all (a triple click), from the anchor
        // range the press made.
        private int _dragUnit;
        private TextSelection _dragStart;
        private bool _dragging;

        // The display text of a hidden text, and where each of its clusters starts in the text.
        private string _shown = string.Empty;
        private int[] _clusterStarts = [];

        private bool Focused => _tree.Nodes.IsAlive(_field) && (_tree.Nodes.GetRef(_field).Pseudo & PseudoState.Focused) != 0;

        private TextLeaf Leaf => _tree.Nodes.GetRef(_leaf).Text!;

        public Element Build()
        {
            Element leaf = Elements.Text(Write);
            _tree = leaf.Tree!;
            _leaf = leaf.Node;
            Element caret = Box().Draw(DrawCaret);
            _caret = caret.Node;
            _tree.SetStyle(_caret, Layer());
            Element field = options.Placeholder is { } placeholder ? Container(leaf, PlaceholderOf(placeholder), caret) : Container(leaf, caret);
            _field = field.Node;
            if (!options.Multiline)
            {
                // One line: the text keeps its width and scrolls sideways (design §4.9, "Scrolling").
                _tree.SetStyle(_leaf, _tree.Nodes.GetRef(_leaf).Layout.Style with { FlexShrink = 0 });
            }

            _ = field
                .Focusable()
                .Cursor(CursorKind.IBeam)
                .Overflow(XylemUI.Overflow.Hidden, XylemUI.Overflow.Hidden)
                .ScrollState(_scroll)
                .On(KeyEventKind.Down, EventPhase.Bubble, KeyDown)
                .OnTextInput(EventPhase.Bubble, TextInput)
                .On(PointerEventKind.Down, EventPhase.Bubble, PointerDown)
                .On(PointerEventKind.Move, EventPhase.Bubble, PointerMove)
                .On(PointerEventKind.Up, EventPhase.Bubble, PointerUp)
                .On(FocusEventKind.FocusIn, EventPhase.Bubble, FocusIn)
                .On(FocusEventKind.FocusOut, EventPhase.Bubble, FocusOut);

            _ = Effect.Create(() =>
            {
                string value = text.Get();
                if (!_writing && value != _editor.Text)
                {
                    _editor.Reset(value);
                    Changed(selectionOnly: false);
                }
            });
            if (options.Selection is { } shared)
            {
                _ = Effect.Create(() =>
                {
                    TextSelection value = shared.Get();
                    if (!_writing && value != _editor.Selection)
                    {
                        _editor.Select(value);
                        Changed(selectionOnly: true);
                    }
                });
            }

            Scope.OnCleanup(StopBlink);
            return field;
        }

        // The field's box: a row for one line, which keeps the text's width, or a column for many, which wraps it.
        private Element Container(params ReadOnlySpan<Element> children) => options.Multiline ? Column(children) : Row(children);

        // An absolute layer over the field's padding box.
        private static LayoutStyle Layer() =>
            new()
            {
                Position = Position.Absolute,
                Inset = new EdgeInsets<LengthPercentageAuto>(
                    LengthPercentageAuto.Px(0),
                    LengthPercentageAuto.Px(0),
                    LengthPercentageAuto.Px(0),
                    LengthPercentageAuto.Px(0)
                ),
            };

        // The placeholder: at the text's place, at half the opacity, while the text is empty.
        private Element PlaceholderOf(string placeholder)
        {
            Element shown = Elements.Text(placeholder).Opacity(0.5f);
            _placeholder = shown.Node;
            _tree.SetStyle(_placeholder, Layer() with { Hidden = _editor.Text.Length > 0 });
            return shown;
        }

        // The text the leaf shows, with the selection's colors over the selected part while the field has the focus.
        private void Write(ref NodeTextWriter writer)
        {
            _ = _version.Get();
            string shown = Shown();
            TextSelection selection = _editor.Selection;
            if (!Focused || selection.IsCollapsed)
            {
                writer.Write(shown.AsSpan());
                return;
            }

            int start = ToShown(selection.Start);
            int end = ToShown(selection.End);
            TextEditingSettings settings = _tree.TextEditing;
            writer.Write(shown.AsSpan(0, start));
            using (
                writer.Style(
                    new SpanStyle(
                        Color: options.SelectionForeground ?? settings.SelectionForeground,
                        Background: options.SelectionBackground ?? settings.SelectionBackground
                    )
                )
            )
            {
                writer.Write(shown.AsSpan(start, end - start));
            }

            writer.Write(shown.AsSpan(end));
        }

        // The text on the screen: the text itself, or one bullet for each cluster of a hidden text.
        private string Shown()
        {
            if (!options.Password)
            {
                return _editor.Text;
            }

            if (_clusterStarts.Length == 0)
            {
                var starts = new List<int>();
                string value = _editor.Text;
                for (int position = 0; position < value.Length; position = Graphemes.NextBoundary(value, position))
                {
                    starts.Add(position);
                }

                starts.Add(value.Length);
                _clusterStarts = [.. starts];
                _shown = new string(Bullet, starts.Count - 1);
            }

            return _shown;
        }

        // A position of the text as a position of the text on the screen, and back.
        private int ToShown(int offset)
        {
            if (!options.Password)
            {
                return offset;
            }

            _ = Shown();
            int index = Array.BinarySearch(_clusterStarts, offset);
            return index >= 0 ? index : ~index;
        }

        private int FromShown(int offset)
        {
            if (!options.Password)
            {
                return offset;
            }

            _ = Shown();
            return _clusterStarts[Math.Clamp(offset, 0, _clusterStarts.Length - 1)];
        }

        // After an edit or a move: the leaf writes again, the placeholder follows the text, the signals take the values, and the caret
        // shows and blinks again.
        private void Changed(bool selectionOnly)
        {
            if (!selectionOnly)
            {
                _clusterStarts = [];
                if (!_placeholder.IsNone && _tree.Nodes.IsAlive(_placeholder))
                {
                    _tree.SetStyle(_placeholder, _tree.Nodes.GetRef(_placeholder).Layout.Style with { Hidden = _editor.Text.Length > 0 });
                }
            }

            _version.Set(_version.Peek() + 1);
            _writing = true;
            try
            {
                text.Set(_editor.Text);
                options.Selection?.Set(_editor.Selection);
            }
            finally
            {
                _writing = false;
            }

            RestartBlink();
        }

        // The caret, over the text: its geometry from the leaf's lines, at the width Windows asks for, while the field has the focus,
        // the selection is collapsed, and the blink shows it. The field scrolls to keep it in view.
        private void DrawCaret(DrawingContext context, Rect bounds)
        {
            if (!Focused || !_editor.Selection.IsCollapsed || Leaf.LineBreaks is not { Committed: >= 0 })
            {
                return;
            }

            CaretPosition caret = TextCaret.CaretGeometry(Leaf, ToShown(_editor.Selection.Caret), Affinity.Downstream);
            Vector2 origin = LeafOrigin();
            float width = Math.Max(1, _tree.TextEditing.CaretWidth);
            float x = origin.X + caret.Rect.X;
            float y = origin.Y + caret.Rect.Y;
            Reveal(x, y, width, caret.Rect.Height);
            if (_caretShown)
            {
                context.FillRect(bounds.X + x, bounds.Y + y, width, caret.Rect.Height, options.CaretColor ?? Foreground());
            }
        }

        // The leaf's content box, from the layer's origin (the field's padding box), in the content's coordinates.
        private Vector2 LeafOrigin()
        {
            Rect leaf = _tree.Nodes.GetRef(_leaf).Layout.Rect;
            Rect layer = _tree.Nodes.GetRef(_caret).Layout.Rect;
            Edges inner = Leaf.PaddingBorder;
            return new Vector2(leaf.X - layer.X + inner.Left, leaf.Y - layer.Y + inner.Top);
        }

        // Scrolls to the nearest offset that shows a box of the content (Flutter's _getOffsetToRevealCaret, editable_text.dart lines
        // 4053 to 4069).
        private void Reveal(float x, float y, float width, float height)
        {
            Size viewport = _scroll.ViewportValue;
            Vector2 offset = _scroll.OffsetValue;
            Vector2 next = offset;
            if (x < offset.X)
            {
                next.X = x;
            }
            else if (x + width > offset.X + viewport.Width)
            {
                next.X = x + width - viewport.Width;
            }

            if (y < offset.Y)
            {
                next.Y = y;
            }
            else if (y + height > offset.Y + viewport.Height)
            {
                next.Y = y + height - viewport.Height;
            }

            if (next != offset)
            {
                _scroll.ScrollTo(next);
            }
        }

        // The color of the field's text: the nearest foreground of the field and its ancestors.
        private Color Foreground()
        {
            for (NodeHandle node = _field; !node.IsNone; node = _tree.Nodes.GetRef(node).Parent)
            {
                if (_tree.Nodes.GetRef(node).Visual?.Foreground is { } color)
                {
                    return color;
                }
            }

            return Color.FromRgb(0x000000);
        }

        private void FocusIn(in FocusEvent e)
        {
            if (e.TargetNode != _field)
            {
                return;
            }

            // A field of one line selects all when it takes the focus (Flutter's selectAllOnFocus on the desktop); a press then puts
            // the caret where it is.
            if (!options.Multiline)
            {
                _editor.SelectAll();
            }

            Changed(selectionOnly: true);
        }

        private void FocusOut(in FocusEvent e)
        {
            if (e.TargetNode != _field)
            {
                return;
            }

            // The selection stays, not drawn (design §4.9, "Drawing the selection").
            StopBlink();
            _dragging = false;
            _version.Set(_version.Peek() + 1);
        }

        private void TextInput(in TextInputEvent e)
        {
            if (e.TargetNode != _field || options.ReadOnly)
            {
                return;
            }

            e.StopPropagation();
            if (_editor.Type(e.Text, e.Timestamp))
            {
                _preferredX = null;
                Changed(selectionOnly: false);
            }
        }

        private void KeyDown(in KeyEvent e)
        {
            if (e.TargetNode != _field || e.PrefersCharacter)
            {
                return;
            }

            if (Command(e.Key, e.Modifiers, e.Timestamp))
            {
                e.StopPropagation();
            }
        }

        // The keys of design §4.9's table. Returns whether the key was a command, which stops it.
        private bool Command(Key key, KeyModifiers modifiers, TimeSpan now)
        {
            bool shift = (modifiers & KeyModifiers.Shift) != 0;
            KeyModifiers rest = modifiers & ~KeyModifiers.Shift;
            bool control = rest == KeyModifiers.Control;
            if (rest is not (KeyModifiers.None or KeyModifiers.Control))
            {
                return false;
            }

            TextSelection selection = _editor.Selection;
            switch (key)
            {
                case Key.Left or Key.Right:
                    bool forward = key == Key.Right;
                    int to =
                        !shift && !control && !selection.IsCollapsed ? (forward ? selection.End : selection.Start)
                        : control ? _editor.Word(selection.Caret, forward)
                        : _editor.Character(selection.Caret, forward);
                    return Move(to, shift);
                case Key.Home or Key.End:
                    return Move(control ? (key == Key.End ? _editor.Text.Length : 0) : LineEnd(key == Key.End), shift, keepX: false);
                case Key.Up or Key.Down or Key.PageUp or Key.PageDown when options.Multiline && !control:
                    return Move(Vertical(key), shift, keepX: true);
                case Key.Back or Key.Delete when !shift:
                    return !options.ReadOnly && Edit(_editor.Delete(forward: key == Key.Delete, word: control, now));
                case Key.Delete when shift && rest == KeyModifiers.None:
                    return Cut(now);
                case Key.Insert when control && !shift:
                    return Copy();
                case Key.Insert when shift && rest == KeyModifiers.None:
                    return Paste(now);
                case Key.Enter when rest == KeyModifiers.None && !shift:
                    if (options.Multiline)
                    {
                        return !options.ReadOnly && Edit(_editor.Type("\n", now));
                    }

                    if (options.OnSubmit is { } submit)
                    {
                        submit(_editor.Text);
                        return true;
                    }

                    return false;
            }

            if (!control)
            {
                return false;
            }

            switch (key)
            {
                case Key.A when !shift:
                    _editor.SelectAll();
                    Changed(selectionOnly: true);
                    return true;
                case Key.C when !shift:
                    return Copy();
                case Key.X when !shift:
                    return Cut(now);
                case Key.V when !shift:
                    return Paste(now);
                case Key.Z when !shift:
                    return !options.ReadOnly && Edit(_editor.Undo());
                case Key.Y when !shift:
                case Key.Z when shift:
                    return !options.ReadOnly && Edit(_editor.Redo());
                default:
                    return false;
            }
        }

        private bool Edit(bool changed)
        {
            if (changed)
            {
                _preferredX = null;
                Changed(selectionOnly: false);
            }

            return true;
        }

        private bool Move(int offset, bool extend, bool keepX = false)
        {
            if (!keepX)
            {
                _preferredX = null;
            }

            _editor.MoveTo(offset, extend);
            Changed(selectionOnly: true);
            return true;
        }

        private bool Copy()
        {
            if (!options.Password && !_editor.Selection.IsCollapsed)
            {
                _ = _tree.Clipboard?.SetText(_editor.SelectedText);
            }

            return true;
        }

        private bool Cut(TimeSpan now)
        {
            if (options.Password || options.ReadOnly || _editor.Selection.IsCollapsed)
            {
                return true;
            }

            // The text leaves only when the clipboard took it, as Avalonia's Cut deletes after the write succeeds.
            if (_tree.Clipboard?.SetText(_editor.SelectedText) == true && _editor.Cut(now) is not null)
            {
                Changed(selectionOnly: false);
            }

            return true;
        }

        private bool Paste(TimeSpan now)
        {
            if (!options.ReadOnly && _tree.Clipboard?.GetText() is { Length: > 0 } pasted)
            {
                _ = Edit(_editor.Paste(pasted, now));
            }

            return true;
        }

        // The start or the end of the line on the screen that holds the caret.
        private int LineEnd(bool end)
        {
            if (!LaidOut(out TextLeaf leaf))
            {
                return end ? _editor.Text.Length : 0;
            }

            CaretPosition caret = TextCaret.CaretGeometry(leaf, ToShown(_editor.Selection.Caret), Affinity.Downstream);
            int line = TextCaret.LineAt(leaf, caret.Rect.Y + (caret.Rect.Height / 2), out _, out _);
            return FromShown(TextCaret.HitTestText(leaf, line, end ? float.MaxValue : float.MinValue).TextOffset);
        }

        // The position a line or a page up or down, at the same x as the caret: the x of the first such move stays for the next.
        private int Vertical(Key key)
        {
            if (!LaidOut(out TextLeaf leaf))
            {
                return _editor.Selection.Caret;
            }

            CaretPosition caret = TextCaret.CaretGeometry(leaf, ToShown(_editor.Selection.Caret), Affinity.Downstream);
            float x = _preferredX ??= caret.Rect.X;
            float step = key is Key.PageUp or Key.PageDown ? Math.Max(caret.Rect.Height, _scroll.ViewportValue.Height) : caret.Rect.Height;
            float y = caret.Rect.Y + (caret.Rect.Height / 2) + (key is Key.Up or Key.PageUp ? -step : step);
            if (y < 0)
            {
                return 0;
            }

            int line = TextCaret.LineAt(leaf, y, out float top, out float height);
            if (y >= top + height)
            {
                return _editor.Text.Length;
            }

            return FromShown(TextCaret.HitTestText(leaf, line, x).TextOffset);
        }

        // The leaf with its lines laid out for the text now: a layout pass first when an edit left it to be laid out.
        private bool LaidOut(out TextLeaf leaf)
        {
            LayoutEngine.RunLayoutPass(_tree);
            leaf = Leaf;
            return leaf.LineBreaks is { Committed: >= 0 };
        }

        // The position under a point of the field.
        private int OffsetAt(Vector2 local)
        {
            if (!LaidOut(out TextLeaf leaf))
            {
                return 0;
            }

            Vector2 point = local + _scroll.OffsetValue - LeafOrigin() + LayerOffset();
            int line = TextCaret.LineAt(leaf, point.Y, out _, out _);
            return line < 0 ? 0 : FromShown(TextCaret.HitTestText(leaf, line, point.X).TextOffset);
        }

        // The layer's origin from the field's border box, which a pointer's local position starts at.
        private Vector2 LayerOffset()
        {
            Rect layer = _tree.Nodes.GetRef(_caret).Layout.Rect;
            return new Vector2(-layer.X, -layer.Y);
        }

        private void PointerDown(in PointerEvent e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            int offset = OffsetAt(e.LocalPosition);
            int clicks = e.ClickCount;
            bool extend = (e.Modifiers & KeyModifiers.Shift) != 0;
            _dragUnit =
                clicks >= 3 ? 3
                : clicks == 2 ? 2
                : 1;
            _dragging = true;
            if (_dragUnit == 1)
            {
                _dragStart = TextSelection.Collapsed(extend ? _editor.Selection.Anchor : offset);
                _editor.MoveTo(offset, extend);
            }
            else
            {
                _dragStart = Unit(offset);
                _editor.Select(_dragStart);
            }

            _preferredX = null;
            Changed(selectionOnly: true);
        }

        private void PointerMove(in PointerEvent e)
        {
            if (!_dragging || (e.Buttons & PointerButtons.Left) == 0)
            {
                return;
            }

            // The press's capture keeps the moves coming outside the field (design §7).
            int offset = OffsetAt(e.LocalPosition);
            TextSelection unit = _dragUnit == 1 ? TextSelection.Collapsed(offset) : Unit(offset);
            TextSelection next =
                unit.End <= _dragStart.Start
                    ? new TextSelection(_dragStart.End, unit.Start)
                    : new TextSelection(Math.Min(_dragStart.Start, unit.Start), Math.Max(_dragStart.End, unit.End));
            if (_dragUnit == 1)
            {
                next = new TextSelection(_dragStart.Anchor, offset);
            }

            if (next != _editor.Selection)
            {
                _editor.Select(next);
                Changed(selectionOnly: true);
            }
        }

        private void PointerUp(in PointerEvent e) => _dragging &= e.Button != PointerButton.Left;

        // The unit a double or a triple click selects at a position: a word; all of a field of one line, or a paragraph of a field of
        // many (Flutter on Windows, text_selection.dart lines 3055 to 3071).
        private TextSelection Unit(int offset)
        {
            if (_dragUnit == 2)
            {
                return options.Password ? new TextSelection(0, _editor.Text.Length) : _editor.WordAt(offset);
            }

            if (!options.Multiline)
            {
                return new TextSelection(0, _editor.Text.Length);
            }

            string value = _editor.Text;
            int start = offset > 0 ? value.LastIndexOf('\n', offset - 1) + 1 : 0;
            int end = value.IndexOf('\n', Math.Min(offset, value.Length));
            return new TextSelection(start, end < 0 ? value.Length : end);
        }

        // The caret shows again, and blinks from there at Windows' interval while the field has the focus.
        private void RestartBlink()
        {
            _caretShown = true;
            Invalidation.Invalidate(_tree, _caret, ChangeKind.Color);
            if (!Focused || _tree.TextEditing.CaretBlink is not { } interval)
            {
                StopBlink();
                return;
            }

            _ui ??= SynchronizationContext.Current;
            if (_timer is null)
            {
                _timer = _tree.Time.CreateTimer(static state => ((TextFieldState)state!).Tick(), this, interval, interval);
            }
            else
            {
                _ = _timer.Change(interval, interval);
            }
        }

        private void StopBlink()
        {
            _timer?.Dispose();
            _timer = null;
            _caretShown = true;
        }

        // A tick of the timer, which may come on another thread: the blink runs on the UI thread.
        private void Tick()
        {
            if (_ui is { } ui && SynchronizationContext.Current != ui)
            {
                ui.Post(_tick, this);
            }
            else
            {
                Blink();
            }
        }

        private void Blink()
        {
            if (_timer is null || !_tree.Nodes.IsAlive(_caret))
            {
                return;
            }

            _caretShown = !_caretShown;
            Invalidation.Invalidate(_tree, _caret, ChangeKind.Color);
        }
    }
}
