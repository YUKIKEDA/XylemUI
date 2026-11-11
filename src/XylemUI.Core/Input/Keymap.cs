namespace XylemUI;

/// <summary>
/// A row of a keymap (design §7, "Shortcuts and actions"): the keys, pressed one after another, the action they ask for, and the
/// condition on the contexts of the focused node and its ancestors.
/// </summary>
public sealed class KeyBinding
{
    private readonly Keystroke[] _keystrokes;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyBinding"/> class.
    /// </summary>
    /// <param name="keystrokes">The keys, in the order they are pressed.</param>
    /// <param name="action">The action; <see langword="null"/> takes the keys' binding away.</param>
    /// <param name="context">The condition's text; <see langword="null"/> for none.</param>
    /// <param name="predicate">The condition; <see langword="null"/> for none.</param>
    /// <param name="order">The row's place in the order the rows were added.</param>
    internal KeyBinding(Keystroke[] keystrokes, IAction? action, string? context, KeyContextPredicate? predicate, int order)
    {
        _keystrokes = keystrokes;
        Action = action;
        Context = context;
        Predicate = predicate;
        Order = order;
    }

    /// <summary>
    /// Gets the keys, in the order they are pressed.
    /// </summary>
    public IReadOnlyList<Keystroke> Keystrokes => _keystrokes;

    /// <summary>
    /// Gets the action the keys ask for; <see langword="null"/> for a row that takes the keys' binding away where it applies.
    /// </summary>
    public IAction? Action { get; }

    /// <summary>
    /// Gets the condition, in its text form; <see langword="null"/> for a row that applies everywhere.
    /// </summary>
    public string? Context { get; }

    /// <summary>
    /// Gets the keys as an array, which the matching walks.
    /// </summary>
    internal Keystroke[] Steps => _keystrokes;

    /// <summary>
    /// Gets the condition; <see langword="null"/> for a row that applies everywhere.
    /// </summary>
    internal KeyContextPredicate? Predicate { get; }

    /// <summary>
    /// Gets the row's place in the order the rows were added: a later row wins over an earlier one at the same depth.
    /// </summary>
    internal int Order { get; }

    /// <summary>
    /// Returns the text a menu or a tooltip shows for the keys: each step as <see cref="Keystroke.ToDisplayString"/> writes it,
    /// with a space between the steps.
    /// </summary>
    /// <returns>The text, as <c>Ctrl+K Ctrl+S</c>.</returns>
    public string ToDisplayString() => string.Join(' ', _keystrokes.Select(static step => step.ToDisplayString()));

    /// <summary>
    /// Returns the keys in the text form <see cref="Keymap.Bind"/> reads.
    /// </summary>
    /// <returns>The text, as <c>ctrl-k ctrl-s</c>.</returns>
    public override string ToString() => string.Join(' ', _keystrokes.Select(static step => step.ToString()));
}

/// <summary>
/// The key bindings of an app (design §7, "Shortcuts and actions"): rows of keys, an action, and a condition. A key that goes down
/// is looked up here before the key handlers and the text input get it; the action of the row that wins is dispatched from the
/// focused node.
/// </summary>
/// <remarks>
/// A row whose condition is true deeper in the tree wins, and a row with no condition counts as the deepest. At the same depth a
/// row added later wins, so an app adds its users' rows after its own. A row with no action takes the keys' binding away: the rows
/// it wins over are not tried. When the action of the winning row is not handled, the next row is tried. The keymap belongs to
/// the UI thread.
/// </remarks>
public sealed class Keymap
{
    private KeyBinding[] _bindings = [];
    private int _nextOrder;
    private Signal<int>? _version;
    private int _versionValue;

    /// <summary>
    /// Gets the keymap of the app's windows.
    /// </summary>
    public static Keymap Default { get; } = new();

    /// <summary>
    /// Gets the rows, in the order they were added.
    /// </summary>
    public IReadOnlyList<KeyBinding> Bindings => _bindings;

    /// <summary>
    /// Gets the rows as an array, which a change replaces, so that the matching walks one that does not change under it.
    /// </summary>
    internal KeyBinding[] Rows => _bindings;

    /// <summary>
    /// Adds a row.
    /// </summary>
    /// <param name="keystrokes">The keys, as <c>ctrl-shift-p</c>; keys pressed one after another have a space between them, as
    /// <c>ctrl-k ctrl-s</c>.</param>
    /// <param name="action">The action; <see langword="null"/> takes the keys' binding away where the row applies.</param>
    /// <param name="context">The condition, as <c>Editor &amp;&amp; mode == full</c>; <see langword="null"/> applies everywhere.</param>
    /// <returns>The row, which <see cref="Remove"/> takes out again.</returns>
    /// <exception cref="FormatException">The keys or the condition are not written in their text form.</exception>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public KeyBinding Bind(string keystrokes, IAction? action, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(keystrokes);
        UiThread.VerifyAccess();
        var steps = new List<Keystroke>();
        foreach (Range part in keystrokes.AsSpan().Split(' '))
        {
            ReadOnlySpan<char> text = keystrokes.AsSpan(part);
            if (text.IsEmpty)
            {
                continue;
            }

            steps.Add(Keystroke.Parse(text));
        }

        if (steps.Count == 0)
        {
            throw new FormatException("A key binding has at least one keystroke; write it as 'ctrl-shift-p'.");
        }

        var binding = new KeyBinding([.. steps], action, context, context is null ? null : KeyContextPredicate.Parse(context), _nextOrder++);
        var added = new KeyBinding[_bindings.Length + 1];
        _bindings.CopyTo(added, 0);
        added[^1] = binding;
        _bindings = added;
        Changed();
        return binding;
    }

    /// <summary>
    /// Takes a row out.
    /// </summary>
    /// <param name="binding">The row <see cref="Bind"/> returned.</param>
    /// <returns>Whether the row was in the keymap.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public bool Remove(KeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        UiThread.VerifyAccess();
        int index = Array.IndexOf(_bindings, binding);
        if (index < 0)
        {
            return false;
        }

        var removed = new KeyBinding[_bindings.Length - 1];
        Array.Copy(_bindings, removed, index);
        Array.Copy(_bindings, index + 1, removed, index, removed.Length - index);
        _bindings = removed;
        Changed();
        return true;
    }

    /// <summary>
    /// Finds the row a menu or a tooltip shows for an action: the row added last whose action is of the type. Read in a
    /// computation, it is read again when a row is added or taken out.
    /// </summary>
    /// <typeparam name="TAction">The type of the action.</typeparam>
    /// <returns>The row; <see langword="null"/> when no row asks for the action.</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public KeyBinding? BindingFor<TAction>()
        where TAction : class, IAction
    {
        UiThread.VerifyAccess();
        _ = (_version ??= Signal.Create(0)).Get();
        KeyBinding[] rows = _bindings;
        for (int i = rows.Length - 1; i >= 0; i--)
        {
            if (rows[i].Action?.GetType() == typeof(TAction))
            {
                return rows[i];
            }
        }

        return null;
    }

    private void Changed() => _version?.Set(unchecked(++_versionValue));
}
