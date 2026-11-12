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
    private static readonly Lock DefaultsLock = new();
    private static Action<Keymap>[] _defaults = [];
    private static Keymap? _default;

    private KeyBinding[] _bindings = [];
    private int _nextOrder;
    private Signal<int>? _version;
    private int _versionValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="Keymap"/> class with the default rows of the packages the app references
    /// (<see cref="RegisterDefaults"/>), in the order the packages depend on each other. The rows added after them win over them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public Keymap()
    {
        foreach (Action<Keymap> add in _defaults)
        {
            add(this);
        }
    }

    /// <summary>
    /// Gets the keymap of the app's windows. It is made when it is first read, on the UI thread.
    /// </summary>
    public static Keymap Default => _default ??= new Keymap();

    /// <summary>
    /// Registers a method that adds a package's default rows to every keymap made after the call (design §7, "The default keys
    /// and actions"). The module initializer the source generator writes for an app calls it for each package the app references
    /// that names such a method with <see cref="DefaultKeyBindingsAttribute"/>; code does not call it. It can be called from any
    /// thread.
    /// </summary>
    /// <param name="add">The method.</param>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void RegisterDefaults(Action<Keymap> add)
    {
        ArgumentNullException.ThrowIfNull(add);
        lock (DefaultsLock)
        {
            if (Array.IndexOf(_defaults, add) < 0)
            {
                _defaults = [.. _defaults, add];
            }
        }
    }

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
    /// Adds a row whose action is made by its name (design §7, "Actions with arguments made by name"), as a row of a user's
    /// keymap file is.
    /// </summary>
    /// <param name="keystrokes">The keys, as <see cref="Bind"/> reads them.</param>
    /// <param name="actionName">The name of the action, as <c>editor::GoToLine</c>.</param>
    /// <param name="arguments">The action's arguments: the names of its properties and their values as text.</param>
    /// <param name="context">The condition; <see langword="null"/> applies everywhere.</param>
    /// <returns>The row.</returns>
    /// <exception cref="FormatException">
    /// The keys or the condition are not written in their text form, no action has the name, or an argument is not one of the
    /// action's.
    /// </exception>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread.</exception>
    public KeyBinding BindByName(
        string keystrokes,
        string actionName,
        ReadOnlySpan<KeyValuePair<string, string>> arguments = default,
        string? context = null
    ) =>
        ActionRegistry.TryCreate(actionName, arguments, out IAction? action, out string? error)
            ? Bind(keystrokes, action, context)
            : throw new FormatException(error);

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
    /// Reads the count of the keymap's changes, so that the reading computation runs again when a row is added or taken out.
    /// </summary>
    internal void TrackChanges() => _ = (_version ??= Signal.Create(0)).Get();

    private void Changed() => _version?.Set(unchecked(++_versionValue));
}
