using System.Numerics;
using System.Text;
using XylemUI.Text.LineBreak;
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
    private sealed class TextFieldState(Signal<string> text, TextFieldOptions options) : ITextInputClient
    {
        private const char Bullet = '●';

        private readonly TextEditor _editor = new(options.Multiline) { MaxLength = options.MaxLength, Password = options.Password };

        private readonly Signal<int> _version = Signal.Create(0);
        private readonly ScrollState _scroll = new();
        private readonly SendOrPostCallback _tick = static state => ((TextFieldState)state!).Blink();
        private UiTree _tree = null!;
        private NodeHandle _field;
        private NodeHandle _leaf;
        private NodeHandle _content;
        private NodeHandle _caret;
        private NodeHandle _placeholder;
        private ITimer? _timer;
        private SynchronizationContext? _ui;
        private bool _caretShown = true;
        private bool _writing;
        private float? _preferredX;

        // The tops and heights of the leaf's lines, for the layout they were stacked for.
        private float[] _tops = [];
        private float[] _heights = [];
        private object? _topsOf;
        private float _topsWidth;
        private int _topsEdits;
        private int _topsCount;

        // The count of the edits to the text, which a move of the caret or the selection does not change: the lines' tops are
        // stacked again only after an edit.
        private int _edits;

        // What a press and a drag select: by cluster, by word (a double click), or by line or all (a triple click), from the anchor
        // range the press made.
        private int _dragUnit;
        private TextSelection _dragStart;
        private bool _dragging;

        // The display text of a hidden text, and where each of its clusters starts in the text.
        private string _shown = string.Empty;
        private int[] _clusterStarts = [];

        // The composition of the OS's input method (design §4.7): its range in the text, its clauses with their looks, and the text
        // and the selection it started from, which its commit is recorded against as one undo.
        private (int Start, int End)? _composition;
        private ImeAttributeRun[] _clauses = [];
        private SpanStyle[] _clauseLooks = [];
        private int _clauseCount;
        private string _composeBase = string.Empty;
        private TextSelection _composeBaseSelection;

        // What the input method knows of the field, to tell it what changed without it: the text and the selection it saw last, and
        // where the text was on the screen. A change the input method made itself is not told back.
        private string _known = string.Empty;
        private TextSelection _knownSelection;
        private Vector2 _knownOrigin = new(float.NaN);
        private bool _fromInputMethod;
        private bool _layoutOwed;

        // The context menu, and the commands it was last opened with.
        private ContextMenuHost? _menu;
        private TextMenuCommands? _commands;

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

            // The text and the placeholder share a box in the field's flow, inside the field's padding, at least as wide as the field
            // so that the placeholder has the field's width to wrap in.
            Element content = options.Placeholder is { } placeholder ? Container(leaf, PlaceholderOf(placeholder)) : Container(leaf);
            _content = content.Node;

            // A field's text has the field's own selection, and joins no selection area's (design §4.10).
            _tree.SetUnselectable(_content);
            _tree.SetStyle(
                _content,
                _tree.Nodes.GetRef(_content).Layout.Style with
                {
                    FlexShrink = options.Multiline ? 1 : 0,
                    MinWidth = Length.Percent(1),
                }
            );
            Element field = Container(content, caret);
            _field = field.Node;

            // The caret at the end of a line has room inside the content, so that scrolling to it shows all of it (Flutter's
            // EditableText adds the caret's width the same way). One line keeps its width and scrolls sideways (design §4.9,
            // "Scrolling").
            float room = Math.Max(1, _tree.TextEditing.CaretWidth);
            _tree.SetStyle(
                _leaf,
                _tree.Nodes.GetRef(_leaf).Layout.Style with
                {
                    FlexShrink = options.Multiline ? 1 : 0,
                    Padding = EdgeInsets.Px(0, 0, room, 0),
                }
            );

            _ = field
                .Focusable()
                .Cursor(CursorKind.IBeam)
                .Overflow(XylemUI.Overflow.Hidden, XylemUI.Overflow.Hidden)
                .ScrollState(_scroll)
                .On(KeyEventKind.Down, EventPhase.Bubble, KeyDown)
                .On(KeyEventKind.Up, EventPhase.Bubble, KeyUp)
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
                    CommitComposition();
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
                        CommitComposition();
                        _editor.Select(value);
                        Changed(selectionOnly: true);
                    }
                });
            }

            if (options.ContextMenu is { } menu)
            {
                _menu = new ContextMenuHost(_tree, _field, () => menu(_commands!));
            }

            _tree.SetTextInputClient(_field, this);
            Scope.OnCleanup(StopBlink);
            return field;
        }

        // The field's box and its content's: a row for one line, which keeps the text's width, or a column for many, which wraps it.
        private Element Container(params ReadOnlySpan<Element> children) => options.Multiline ? Column(children) : Row(children);

        // An absolute layer over its parent's padding box: the caret's over the field's, the placeholder's over the content's.
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

        // The placeholder: over the content's box, where the text starts, at half the opacity, while the text is empty.
        private Element PlaceholderOf(string placeholder)
        {
            Element shown = Elements.Text(placeholder).Opacity(0.5f);
            _placeholder = shown.Node;
            _tree.SetStyle(_placeholder, Layer() with { Hidden = _editor.Text.Length > 0 });
            return shown;
        }

        // The text the leaf shows, written again after each edit; the selection is the leaf's own (design §4.10, "Drawing").
        private void Write(ref NodeTextWriter writer)
        {
            _ = _version.Get();
            ReadOnlySpan<char> shown = Shown();
            if (_composition is not { } composition || options.Password)
            {
                writer.Write(shown);
                return;
            }

            // The text being composed, each clause in its look, between the text before it and after it (design §4.7).
            int at = composition.Start;
            writer.Write(shown[..at]);
            for (int i = 0; i < _clauseCount; i++)
            {
                int start = Math.Clamp(composition.Start + _clauses[i].Start, at, composition.End);
                int end = Math.Clamp(composition.Start + _clauses[i].End, start, composition.End);
                writer.Write(shown[at..start]);
                using (writer.Style(_clauseLooks[i]))
                {
                    writer.Write(shown[start..end]);
                }

                at = end;
            }

            writer.Write(shown[at..]);
        }

        // The look of a clause: its underline (thick twice the normal, a clause's, design §4.7) and its text's color.
        private static SpanStyle LookOf(in ImeAttributeRun clause)
        {
            TextDecoration? underline = clause.Line is { } line ? TextDecoration.Clause(line, clause.LineColor, clause.Thick ? 2 : 1) : null;
            return new SpanStyle(Color: clause.TextColor, Decoration: underline);
        }

        // The selection the leaf draws: the field's while it has the focus and the selection holds text, none otherwise (design §4.9,
        // "Drawing the selection").
        private void PaintSelection()
        {
            TextSelection selection = _editor.Selection;
            bool shown = Focused && !selection.IsCollapsed;
            _tree.SetTextSelection(
                _leaf,
                shown ? ToShown(selection.Start) : 0,
                shown ? ToShown(selection.End) : 0,
                options.SelectionBackground,
                options.SelectionForeground
            );
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

        // After an edit or a move: after an edit, the leaf writes again and the placeholder follows the text; the leaf's selection
        // follows the field's, the signals take the values, and the caret shows and blinks again.
        private void Changed(bool selectionOnly)
        {
            if (!selectionOnly)
            {
                _edits++;
                _clusterStarts = [];
                if (!_placeholder.IsNone && _tree.Nodes.IsAlive(_placeholder))
                {
                    _tree.SetStyle(_placeholder, _tree.Nodes.GetRef(_placeholder).Layout.Style with { Hidden = _editor.Text.Length > 0 });
                }

                _version.Set(_version.Peek() + 1);
            }

            PaintSelection();
            TellInputMethod();

            // The signals take committed text only: the text being composed is the field's until its commit (as Avalonia keeps it
            // in PreeditText, out of Text).
            if (_composition is not null)
            {
                RestartBlink();
                return;
            }

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

        // Tells the input method what changed without it, as the change between the text and the selection it saw last and the
        // ones now (design §4.7, "What the app tells the OS"). A change it made itself is not told back.
        private void TellInputMethod()
        {
            string now = _editor.Text;
            TextSelection selection = _editor.Selection;
            if (!_fromInputMethod && Focused && _tree.TextInput is { } platform)
            {
                if (!ReferenceEquals(now, _known) && now != _known)
                {
                    int prefix = now.AsSpan().CommonPrefixLength(_known);
                    int rest = Math.Min(now.Length, _known.Length) - prefix;
                    int suffix = 0;
                    while (suffix < rest && now[^(suffix + 1)] == _known[^(suffix + 1)])
                    {
                        suffix++;
                    }

                    platform.TextChanged(prefix, _known.Length - suffix, now.Length - suffix);
                }

                if (selection != _knownSelection)
                {
                    platform.SelectionChanged();
                }
            }

            _known = now;
            _knownSelection = selection;
        }

        // Asks the input method to commit what it composes, before the app or the user changes the text or the selection: the
        // input method ends the composition before it returns (design §4.7, "The focus and the app's writes").
        private void CommitComposition()
        {
            if (_composition is null)
            {
                return;
            }

            _tree.TextInput?.CommitComposition();
            if (_composition is not null)
            {
                ((ITextInputClient)this).EndComposition();
            }
        }

        // The caret, over the text: its geometry from the leaf's lines, at the width Windows asks for, while the field has the focus,
        // the selection is collapsed, and the blink shows it. The field scrolls to keep it in view.
        private void DrawCaret(DrawingContext context, Rect bounds)
        {
            TellLayout();
            if (!Focused || !_editor.Selection.IsCollapsed || !CaretRect(out Rect caret))
            {
                return;
            }

            Vector2 origin = LeafOrigin();
            float width = Math.Max(1, _tree.TextEditing.CaretWidth);
            float x = origin.X + caret.X;
            float y = origin.Y + caret.Y;
            Reveal(x, y, width, caret.Height);
            if (_caretShown)
            {
                context.FillRect(bounds.X + x, bounds.Y + y, width, caret.Height, options.CaretColor ?? Foreground());
            }
        }

        // The caret's box in the leaf's content, from its lines; an empty text has none, and its caret is at the start of the line
        // the measurement gave it.
        private bool CaretRect(out Rect caret)
        {
            TextLeaf leaf = Leaf;
            if (leaf.LineBreaks is { Committed: >= 0 })
            {
                caret = TextCaret.CaretGeometry(leaf, ToShown(_editor.Selection.Caret), Affinity.Downstream).Rect;
                return true;
            }

            if (leaf.Length > 0 || leaf.Shaped is not { Valid: true })
            {
                caret = default;
                return false;
            }

            caret = new Rect(0, 0, 0, LayoutEngine.EmptyTextLine(leaf).Height);
            return true;
        }

        // The leaf's content box, from the layer's origin (the field's padding box), in the content's coordinates.
        private Vector2 LeafOrigin()
        {
            Rect content = _tree.Nodes.GetRef(_content).Layout.Rect;
            Rect leaf = _tree.Nodes.GetRef(_leaf).Layout.Rect;
            Rect layer = _tree.Nodes.GetRef(_caret).Layout.Rect;
            Edges inner = Leaf.PaddingBorder;
            return new Vector2(content.X + leaf.X - layer.X + inner.Left, content.Y + leaf.Y - layer.Y + inner.Top);
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

            // This runs at each display list (the caret is drawn at each one), so it scrolls only when the offset changes: a frame with
            // the caret in view marks nothing. A scroll draws at the next frame, one frame late.
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
            // the caret where it is. The focus its own context menu gives back keeps the selection the menu's command works on.
            if (!options.Multiline && _menu is not { IsOpen: true })
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

            // A composition the input method left when the focus moved away is committed (design §4.7); a window that stops being
            // active keeps it, as the OS keeps the window's input context. The selection stays, not drawn (design §4.9, "Drawing
            // the selection").
            if (_composition is not null && _tree.Focus.Focused != _field)
            {
                ((ITextInputClient)this).EndComposition();
            }

            StopBlink();
            _dragging = false;
            PaintSelection();
            Invalidation.Invalidate(_tree, _caret, ChangeKind.Color);
        }

        private void TextInput(in TextInputEvent e)
        {
            if (e.TargetNode != _field || options.ReadOnly)
            {
                return;
            }

            e.StopPropagation();
            if (_composition is not null)
            {
                // The input method takes the keys while it composes (design §4.7, "The keys while composing").
                return;
            }

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

            if (_composition is not null)
            {
                // The keys while composing are the input method's, not the field's commands (design §4.7).
                e.StopPropagation();
                return;
            }

            if (_menu is not null && IsMenuKey(e))
            {
                e.StopPropagation();
                OpenMenu(at: null);
                return;
            }

            if (Command(e.Key, e.Modifiers, e.Timestamp))
            {
                e.StopPropagation();
            }
        }

        // The application key asks for the menu on its up.
        private void KeyUp(in KeyEvent e)
        {
            if (e.TargetNode == _field && _composition is null && _menu is not null && IsMenuKey(e))
            {
                e.StopPropagation();
                OpenMenu(at: null);
            }
        }

        // Opens the context menu with the commands as they are now (design §4.9, "The context menu"): at the pointer, or at the
        // bottom left of the caret's box (Microsoft's WM_CONTEXTMENU: a menu from the keyboard opens at the selection).
        private void OpenMenu(Vector2? at)
        {
            TextSelection selection = _editor.Selection;
            bool selected = !selection.IsCollapsed;
            int length = _editor.Text.Length;
            _commands = new TextMenuCommands(
                new MenuCommand(!options.ReadOnly && !options.Password && selected, () => Cut(Now())),
                new MenuCommand(!options.Password && selected, () => Copy()),
                new MenuCommand(!options.ReadOnly && _tree.Clipboard?.HasText() == true, () => Paste(Now())),
                new MenuCommand(length > 0 && (selection.Start > 0 || selection.End < length), SelectAll)
            );
            _menu!.OpenAt(at ?? CaretCorner());
        }

        private void SelectAll()
        {
            _editor.SelectAll();
            Changed(selectionOnly: true);
        }

        // The bottom left of the caret's box, in the window's logical pixels; the field's when the text is not laid out.
        private Vector2 CaretCorner()
        {
            Vector2 layer = WindowOrigin(_tree, _caret);
            if (!LaidOut(out TextLeaf leaf))
            {
                return layer + new Vector2(0, _tree.Nodes.GetRef(_field).Layout.Rect.Height);
            }

            Rect caret = TextCaret.CaretGeometry(leaf, ToShown(_editor.Selection.Caret), Affinity.Downstream).Rect;
            return layer + LeafOrigin() + new Vector2(caret.X, caret.Y + caret.Height);
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
                case Key.Enter when rest == KeyModifiers.None:
                    // Shift+Enter breaks a line too, as in Avalonia (TextBox.cs line 1700) and zed (default-windows.json line 131).
                    if (options.Multiline)
                    {
                        return !options.ReadOnly && Edit(_editor.Type("\n", now));
                    }

                    if (!shift && options.OnSubmit is { } submit)
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
            int line = LineAt(leaf, caret.Rect.Y + (caret.Rect.Height / 2), out _, out _);
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

            int line = LineAt(leaf, y, out float top, out float height);
            if (y >= top + height)
            {
                return _editor.Text.Length;
            }

            return FromShown(TextCaret.HitTestText(leaf, line, x).TextOffset);
        }

        // The line at a height of the leaf, found by halves on the lines' tops, which the field stacks once for each layout of its
        // lines (design §4.9, "The lines' tops"). TextCaret.LineAt stacks them at each call.
        private int LineAt(TextLeaf leaf, float y, out float top, out float height)
        {
            top = 0;
            height = 0;
            if (leaf.Shaped?.Result is not { } result || leaf.LineBreaks is not { Committed: >= 0 } cache)
            {
                return -1;
            }

            LineBreakEntry entry = cache[cache.Committed];
            ReadOnlySpan<LaidLine> lines = entry.Layout.Lines;
            if (lines.IsEmpty)
            {
                return -1;
            }

            if (!ReferenceEquals(entry.Layout, _topsOf) || _topsWidth != entry.Width || _topsEdits != _edits || _topsCount != lines.Length)
            {
                if (_tops.Length < lines.Length)
                {
                    _tops = new float[lines.Length];
                    _heights = new float[lines.Length];
                }

                float stacked = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    _tops[i] = stacked;
                    _heights[i] = LayoutEngine.TextLine(leaf, result, lines[i].Span).Height;
                    stacked += _heights[i];
                }

                (_topsOf, _topsWidth, _topsEdits, _topsCount) = (entry.Layout, entry.Width, _edits, lines.Length);
            }

            int low = 0;
            int high = lines.Length - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (_tops[middle] <= y)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            top = _tops[low];
            height = _heights[low];
            return low;
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
            int line = LineAt(leaf, point.Y, out _, out _);
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
            if (e.Button == PointerButton.Right && _menu is not null)
            {
                RightDown(e.LocalPosition);
                return;
            }

            if (e.Button != PointerButton.Left)
            {
                return;
            }

            // A press commits a composition before it moves the caret (design §4.7, "The focus and the app's writes").
            CommitComposition();
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

        private void PointerUp(in PointerEvent e)
        {
            _dragging &= e.Button != PointerButton.Left;
            if (e.Button == PointerButton.Right && _menu is not null)
            {
                e.StopPropagation();
                OpenMenu(e.Position);
            }
        }

        // The right button's down before the menu (design §4.9, "The context menu"): it commits a composition, and puts the caret at
        // the point when the point is outside the selection or the field had no focus (Flutter selects the position then,
        // text_selection.dart lines 2895 to 2916); inside the selection, it stays. The field takes the focus itself, as the window
        // gives it on the left button only.
        private void RightDown(Vector2 local)
        {
            CommitComposition();
            bool focused = Focused;
            if (!focused && !_tree.Focus.Focus(_field))
            {
                return;
            }

            int offset = OffsetAt(local);
            TextSelection selection = _editor.Selection;
            if (!focused || selection.IsCollapsed || offset < selection.Start || offset > selection.End)
            {
                _ = Move(offset, extend: false);
            }
        }

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

            return TextUnits.ParagraphAt(_editor.Text, offset);
        }

        // Tells the input method that the text moved on the screen: the field's box or its scroll moved since it was told last, or
        // it asked for a box before the text was laid out (design §4.7, "What the app tells the OS"). It runs at each display list,
        // after the frame's layout.
        private void TellLayout()
        {
            if (!Focused || _tree.TextInput is not { } platform)
            {
                _knownOrigin = new Vector2(float.NaN);
                return;
            }

            Vector2 origin = WindowOrigin(_tree, _caret);
            if (origin != _knownOrigin || _layoutOwed)
            {
                bool told = !float.IsNaN(_knownOrigin.X) || _layoutOwed;
                _knownOrigin = origin;
                _layoutOwed = false;
                if (told)
                {
                    platform.LayoutChanged();
                }
            }
        }

        int ITextInputClient.Length => _editor.Text.Length;

        TextSelection ITextInputClient.Selection => _editor.Selection;

        void ITextInputClient.Select(TextSelection selection)
        {
            int length = _editor.Text.Length;
            _editor.Select(new TextSelection(Math.Clamp(selection.Anchor, 0, length), Math.Clamp(selection.Caret, 0, length)));
            FromInputMethod(selectionOnly: true);
        }

        (int Start, int End)? ITextInputClient.Composition => _composition;

        bool ITextInputClient.ReadOnly => options.ReadOnly;

        TextInputKind ITextInputClient.Kind => options.Password ? TextInputKind.Password : options.InputKind;

        void ITextInputClient.CopyText(int start, Span<char> into)
        {
            string value = _editor.Text;
            start = Math.Clamp(start, 0, value.Length);
            value.AsSpan(start, Math.Min(into.Length, value.Length - start)).CopyTo(into);
        }

        bool ITextInputClient.BoundsOf(int start, int end, out Rect bounds)
        {
            if (!LaidOut(out TextLeaf leaf))
            {
                // The input method asks again when the field tells it the layout is there (TSF's TS_E_NOLAYOUT).
                _layoutOwed = true;
                bounds = default;
                return false;
            }

            int length = _editor.Text.Length;
            Rect first = TextCaret.CaretGeometry(leaf, ToShown(Math.Clamp(start, 0, length)), Affinity.Downstream).Rect;
            Rect last = TextCaret.CaretGeometry(leaf, ToShown(Math.Clamp(end, 0, length)), Affinity.Upstream).Rect;
            Vector2 origin = WindowOrigin(_tree, _caret) + LeafOrigin();
            float left = Math.Min(first.X, last.X);
            float top = Math.Min(first.Y, last.Y);
            float right = Math.Max(first.X + first.Width, last.X + last.Width);
            float bottom = Math.Max(first.Y + first.Height, last.Y + last.Height);
            bounds = new Rect(origin.X + left, origin.Y + top, Math.Max(right - left, 1), bottom - top);
            return true;
        }

        bool ITextInputClient.PositionAt(Vector2 point, out int position)
        {
            if (!LaidOut(out _))
            {
                position = 0;
                return false;
            }

            // A pointer's local position starts at the field's border box.
            position = OffsetAt(point - WindowOrigin(_tree, _field));
            return true;
        }

        void ITextInputClient.Replace(int start, int end, ReadOnlySpan<char> value)
        {
            if (options.ReadOnly)
            {
                return;
            }

            int length = _editor.Text.Length;
            start = Math.Clamp(start, 0, length);
            end = Math.Clamp(end, start, length);
            string inserted = value.ToString();
            if (_composition is { } composition)
            {
                // Committed text that comes inside a composition is the composition's, recorded when it ends.
                int from = Math.Min(start, composition.Start);
                int to = Math.Max(end, composition.End) - (end - start) + inserted.Length;
                _editor.Compose(start, end, inserted, start + inserted.Length);
                _composition = (from, Math.Max(from, to));
                _clauseCount = 0;
                FromInputMethod(selectionOnly: false);
                return;
            }

            if (_editor.ReplaceRange(start, end, inserted, Now()))
            {
                _preferredX = null;

                // Text the maximum length cut is told back, as a commit's is.
                if (_editor.Text.Length != length - (end - start) + inserted.Length)
                {
                    Changed(selectionOnly: false);
                }
                else
                {
                    FromInputMethod(selectionOnly: false);
                }
            }
        }

        void ITextInputClient.Compose(int start, int end, ReadOnlySpan<char> value, int cursor, ReadOnlySpan<ImeAttributeRun> clauses)
        {
            if (options.ReadOnly)
            {
                return;
            }

            if (_composition is null)
            {
                _composeBase = _editor.Text;
                _composeBaseSelection = _editor.Selection;
            }

            int length = _editor.Text.Length;
            start = Math.Clamp(start, 0, length);
            end = Math.Clamp(end, start, length);
            _editor.Compose(start, end, value.ToString(), start + Math.Clamp(cursor, 0, value.Length));
            _composition = (start, start + value.Length);
            if (_clauses.Length < clauses.Length)
            {
                _clauses = new ImeAttributeRun[clauses.Length];
                _clauseLooks = new SpanStyle[clauses.Length];
            }

            _clauseCount = clauses.Length;
            for (int i = 0; i < clauses.Length; i++)
            {
                _clauses[i] = clauses[i];
                _clauseLooks[i] = LookOf(clauses[i]);
            }

            _preferredX = null;
            FromInputMethod(selectionOnly: false);
        }

        void ITextInputClient.EndComposition()
        {
            if (_composition is null)
            {
                return;
            }

            _composition = null;
            _clauseCount = 0;
            bool cut = _editor.EndComposition(_composeBase, _composeBaseSelection, Now());
            _composeBase = string.Empty;

            // A commit the maximum length cut is told back: the input method's copy holds what it composed (design §4.7).
            if (cut)
            {
                Changed(selectionOnly: false);
            }
            else
            {
                FromInputMethod(selectionOnly: false);
            }
        }

        // A change the input method made, which is not told back to it.
        private void FromInputMethod(bool selectionOnly)
        {
            _fromInputMethod = true;
            try
            {
                Changed(selectionOnly);
            }
            finally
            {
                _fromInputMethod = false;
            }
        }

        private TimeSpan Now() => _tree.Time.GetElapsedTime(0);

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

            // The timer ticks on the thread pool, and the blink posts itself to the UI thread's context: with none, the caret stays.
            _ui ??= SynchronizationContext.Current;
            if (_ui is null)
            {
                StopBlink();
                return;
            }

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
            if (SynchronizationContext.Current != _ui)
            {
                _ui!.Post(_tick, this);
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
