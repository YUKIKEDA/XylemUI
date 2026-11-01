using System.Text;
using static XylemUI.Elements;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Primitives;

/// <summary>
/// Which options of a select can be chosen, how they are found by typing, and how high its list is (design §13.1, "Select").
/// </summary>
/// <typeparam name="T">The type of an option.</typeparam>
public sealed record SelectOptions<T>
{
    /// <summary>
    /// Gets whether an option can be chosen; <see langword="null"/>, the default, lets every option be chosen. The keys pass over
    /// the options that cannot.
    /// </summary>
    public Func<T, bool>? Enabled { get; init; }

    /// <summary>
    /// Gets the text an option is found by when the user types its first letters; <see langword="null"/>, the default, finds none.
    /// </summary>
    public Func<T, string>? TextOf { get; init; }

    /// <summary>
    /// Gets the most height of the open list, in logical pixels, past which it scrolls; 200 by default.
    /// </summary>
    public float MaxDropDownHeight { get; init; } = 200;

    /// <summary>
    /// Gets the look of the open list: given the list, it returns a node that holds it, or the list itself with a look (a background,
    /// a border, a padding); <see langword="null"/>, the default, shows the list with no look.
    /// </summary>
    public Func<Element, Element>? Panel { get; init; }

    /// <summary>
    /// Gets how the list comes in and leaves; <see langword="null"/> for at once.
    /// </summary>
    public ShowMotion? Motion { get; init; }
}

/// <content>
/// The select (design §13.1, "Select").
/// </content>
public static partial class Primitive
{
    /// <summary>
    /// Makes a select (design §13.1, "Select", Avalonia's <c>ComboBox</c>): a box that takes the focus and shows the value made by
    /// <paramref name="item"/>, and opens a list of the options below it. While it is closed, Up and Down change the value to the
    /// option after or before, stopping at the ends; Alt+Up, Alt+Down, and F4 open and close it; Enter and Space open it. While it
    /// is open, the focus is on the value's option; Up, Down, Home, End, PageUp, and PageDown move it, stopping at the ends; Enter,
    /// Space, or a press choose the option focused, close the list, and give the focus back to the box; Esc and a press outside close
    /// it with the value kept, and Tab closes it and goes on. Typing an option's first letters finds it, when
    /// <see cref="SelectOptions{T}.TextOf"/> is given. The select has no look of its own.
    /// </summary>
    /// <typeparam name="T">The type of an option.</typeparam>
    /// <param name="value">The value chosen, written both ways.</param>
    /// <param name="options">The options, in the order of the list.</param>
    /// <param name="item">Makes the look of an option: in the box for the value, and each row of the list. A row takes the focus and
    /// is disabled when its option cannot be chosen, so that the look's <c>.Focused</c>, <c>.Hovered</c>, and <c>.Disabled</c> show
    /// there.</param>
    /// <param name="selectOptions">Which options can be chosen, how they are found by typing, and how high the list is; the defaults
    /// when <see langword="null"/>.</param>
    /// <returns>The box.</returns>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Select<T>(Signal<T> value, IReadOnlyList<T> options, Func<T, Element> item, SelectOptions<T>? selectOptions = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(item);
        RequireScope(nameof(Select));
        return new SelectState<T>(value, options, item, selectOptions ?? new SelectOptions<T>()).Build();
    }

    // The value shown in the box: a new row each time the value changes, so that the box makes its look again.
    private readonly record struct Shown<T>(T Value, int Version);

    // One select: its box, its list's popup, and the rows of the list while it is open.
    private sealed class SelectState<T>(Signal<T> value, IReadOnlyList<T> options, Func<T, Element> item, SelectOptions<T> settings)
    {
        private static readonly TimeSpan TypeAhead = TimeSpan.FromSeconds(1);

        private readonly Signal<bool> _open = Signal.Create(false);
        private readonly StringBuilder _typed = new();
        private UiTree _tree = null!;
        private NodeHandle _box;
        private PopupBlock _block = null!;
        private NodeHandle[] _rows = [];
        private long _typedAt;
        private int _version;

        public Element Build()
        {
            IReadOnlySignal<IReadOnlyList<Shown<T>>> shown = Computed.Create<IReadOnlyList<Shown<T>>>(() => [new Shown<T>(value.Get(), ++_version)]);
            Element box = Box().For(shown, s => s.Version, s => item(s.Get().Value));
            _tree = box.Tree!;
            _box = box.Node;

            // The list takes an outside press, as Avalonia's ComboBox does, so that a press on the box while the list is open closes
            // it and does not open it again.
            _block = PopupBlock.Create(
                _tree,
                _box,
                _open,
                List,
                new PopupOptions
                {
                    Placement = PopupPlacement.Below,
                    ConsumeOutsidePress = true,
                    Motion = settings.Motion,
                },
                _ => FocusRow(IndexOf(value.Peek()) is int at and >= 0 && Enabled(at) ? at : Next(-1, 1))
            );
            return box.Focusable().OnClick(Toggle).On(KeyEventKind.Down, EventPhase.Bubble, BoxKeys).OnTextInput(EventPhase.Bubble, Typed);
        }

        // The list: a row for each option, the option's own look, in a column that scrolls past the most height, at least as wide as
        // the box, in the app's panel.
        private Element List()
        {
            _rows = new NodeHandle[options.Count];
            var rows = new Element[options.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                int index = i;
                Element row = item(options[i]).Focusable().Disabled(!Enabled(i));
                row = row.OnClick(() => Choose(index)).On(PointerEventKind.Enter, EventPhase.Bubble, RowEnter);
                _rows[i] = row.Node;
                rows[i] = row;
            }

            Element list = ScrollView(Column(rows));
            float width = _tree.Nodes.GetRef(_box).Layout.Rect.Width;
            _tree.SetStyle(
                list.Node,
                _tree.Nodes.GetRef(list.Node).Layout.Style with
                {
                    MinWidth = Length.Px(width),
                    MaxHeight = Length.Px(settings.MaxDropDownHeight),
                }
            );
            _ = list.On(KeyEventKind.Down, EventPhase.Bubble, ListKeys).OnTextInput(EventPhase.Bubble, Typed);
            return settings.Panel?.Invoke(list) ?? list;
        }

        private bool IsOpen => _block.Popup is { Closing: false };

        private void Toggle()
        {
            if (IsOpen)
            {
                _block.Dismiss();
            }
            else
            {
                _open.Set(true);
            }
        }

        // The box's keys while the list is closed (Avalonia's ComboBox.cs lines 258 to 292); the list's keys go on to the box, so
        // that Alt+Up, Alt+Down, and F4 close it from there too.
        private void BoxKeys(in KeyEvent e)
        {
            bool alt = e.Modifiers == KeyModifiers.Alt;
            bool none = e.Modifiers == KeyModifiers.None;
            if ((alt && e.Key is Key.Up or Key.Down) || (none && e.Key == Key.F4))
            {
                e.StopPropagation();
                Toggle();
                return;
            }

            if (IsOpen || !none || e.TargetNode != _box)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter or Key.Space:
                    _open.Set(true);
                    break;
                case Key.Down or Key.Up:
                    bool down = e.Key == Key.Down;
                    int at = IndexOf(value.Peek());
                    int next = at < 0 ? (down ? Next(-1, 1) : Next(options.Count, -1)) : Next(at, down ? 1 : -1);
                    if (next >= 0)
                    {
                        value.Set(options[next]);
                    }

                    break;
                default:
                    return;
            }

            e.StopPropagation();
        }

        // The list's keys, from its focused row: the moves stop at the ends; Enter and Space choose; Tab closes and goes on.
        private void ListKeys(in KeyEvent e)
        {
            if (e.Modifiers != KeyModifiers.None)
            {
                return;
            }

            int at = Array.IndexOf(_rows, _tree.Focus.Focused);
            switch (e.Key)
            {
                case Key.Down or Key.Up:
                    FocusRow(Next(at, e.Key == Key.Down ? 1 : -1));
                    break;
                case Key.Home:
                    FocusRow(Next(-1, 1));
                    break;
                case Key.End:
                    FocusRow(Next(options.Count, -1));
                    break;
                case Key.PageDown or Key.PageUp:
                    FocusRow(Page(at, e.Key == Key.PageDown ? 1 : -1));
                    break;
                case Key.Enter or Key.Space when at >= 0:
                    Choose(at);
                    break;
                case Key.Tab:
                    // The list closes and gives the focus back to the box; Tab's default action then moves it on from there.
                    _block.Dismiss();
                    return;
                default:
                    return;
            }

            e.StopPropagation();
        }

        private void RowEnter(in PointerEvent e)
        {
            if (_tree.Focus.CanFocus(e.CurrentNode))
            {
                _ = _tree.Focus.Focus(e.CurrentNode);
            }
        }

        // The list closes and gives the focus back to the box, and then the value changes.
        private void Choose(int index)
        {
            _block.Dismiss();
            value.Set(options[index]);
        }

        private void FocusRow(int index)
        {
            if (index >= 0 && index < _rows.Length)
            {
                _ = _tree.Focus.Focus(_rows[index]);
            }
        }

        // Typing finds the next option whose text starts with what was typed in the last second, from the one shown or focused: a
        // letter typed again goes on to the next option of that letter (Avalonia's SelectingItemsControl.cs lines 631 to 650).
        private void Typed(in TextInputEvent e)
        {
            if (settings.TextOf is not { } textOf || string.IsNullOrEmpty(e.Text) || (!IsOpen && e.TargetNode != _box))
            {
                return;
            }

            e.StopPropagation();
            long now = _tree.Time.GetTimestamp();
            if (_typed.Length > 0 && _tree.Time.GetElapsedTime(_typedAt, now) > TypeAhead)
            {
                _ = _typed.Clear();
            }

            _typedAt = now;
            _ = _typed.Append(e.Text);
            int from = IsOpen ? Array.IndexOf(_rows, _tree.Focus.Focused) : IndexOf(value.Peek());
            int found = Find(textOf, from);
            if (found < 0)
            {
                return;
            }

            if (IsOpen)
            {
                FocusRow(found);
            }
            else
            {
                value.Set(options[found]);
            }
        }

        // The first enabled option after from (or from itself while more than one letter is typed), round the end, whose text
        // starts with what was typed.
        private int Find(Func<T, string> textOf, int from)
        {
            int count = options.Count;
            int start = _typed.Length > 1 && from >= 0 ? from : from + 1;
            string typed = _typed.ToString();
            for (int i = 0; i < count; i++)
            {
                int at = (((start + i) % count) + count) % count;
                if (Enabled(at) && textOf(options[at]).StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                {
                    return at;
                }
            }

            return -1;
        }

        // The enabled option a page after or before a row: as many rows on as fit in the list's viewport, or the end.
        private int Page(int at, int step)
        {
            if (at < 0)
            {
                return Next(-1, 1);
            }

            float row = Math.Max(1, _tree.Nodes.GetRef(_rows[at]).Layout.Rect.Height);
            int perPage = Math.Max(1, (int)(settings.MaxDropDownHeight / row));
            int target = Math.Clamp(at + (step * perPage), 0, options.Count - 1);
            if (Enabled(target))
            {
                return target;
            }

            int toward = Next(target, step);
            return toward >= 0 ? toward : Next(target, -step);
        }

        // The next enabled option from an index in a direction, or -1 past the end.
        private int Next(int from, int step)
        {
            for (int i = from + step; i >= 0 && i < options.Count; i += step)
            {
                if (Enabled(i))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool Enabled(int index) => settings.Enabled?.Invoke(options[index]) ?? true;

        private int IndexOf(T current)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < options.Count; i++)
            {
                if (comparer.Equals(options[i], current))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
