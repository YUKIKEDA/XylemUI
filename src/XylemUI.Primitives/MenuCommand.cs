namespace XylemUI.Primitives;

/// <summary>
/// A command of a context menu (design §4.9, "The context menu"): whether it can run, decided when the menu opens, and what it
/// does. A menu item runs it after the menu has closed and given the focus back.
/// </summary>
public sealed class MenuCommand
{
    private readonly Action _run;

    /// <summary>
    /// Initializes a new instance of the <see cref="MenuCommand"/> class.
    /// </summary>
    /// <param name="enabled">Whether the command can run.</param>
    /// <param name="run">What it does.</param>
    internal MenuCommand(bool enabled, Action run)
    {
        Enabled = enabled;
        _run = run;
    }

    /// <summary>
    /// Gets a value indicating whether the command can run: the menu's item for it is enabled with it.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// Runs the command; does nothing when <see cref="Enabled"/> is <see langword="false"/>.
    /// </summary>
    public void Run()
    {
        if (Enabled)
        {
            _run();
        }
    }
}

/// <summary>
/// The commands of a text field's context menu (design §4.9, "The context menu"), made each time it opens. They do what Control+X,
/// C, V, and A do, and are enabled as Flutter enables them on Windows.
/// </summary>
public sealed class TextMenuCommands
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextMenuCommands"/> class.
    /// </summary>
    /// <param name="cut">The cut.</param>
    /// <param name="copy">The copy.</param>
    /// <param name="paste">The paste.</param>
    /// <param name="selectAll">The select-all.</param>
    internal TextMenuCommands(MenuCommand cut, MenuCommand copy, MenuCommand paste, MenuCommand selectAll)
    {
        Cut = cut;
        Copy = copy;
        Paste = paste;
        SelectAll = selectAll;
    }

    /// <summary>
    /// Gets the cut: enabled when the field is neither read-only nor a password, and the selection is not empty.
    /// </summary>
    public MenuCommand Cut { get; }

    /// <summary>
    /// Gets the copy: enabled when the field is not a password, and the selection is not empty.
    /// </summary>
    public MenuCommand Copy { get; }

    /// <summary>
    /// Gets the paste: enabled when the field is not read-only, and the clipboard holds text.
    /// </summary>
    public MenuCommand Paste { get; }

    /// <summary>
    /// Gets the select-all: enabled when the text is not empty and not all selected.
    /// </summary>
    public MenuCommand SelectAll { get; }
}

/// <summary>
/// The commands of a selection area's context menu (design §4.10), made each time it opens.
/// </summary>
public sealed class SelectionMenuCommands
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SelectionMenuCommands"/> class.
    /// </summary>
    /// <param name="copy">The copy.</param>
    /// <param name="selectAll">The select-all.</param>
    internal SelectionMenuCommands(MenuCommand copy, MenuCommand selectAll)
    {
        Copy = copy;
        SelectAll = selectAll;
    }

    /// <summary>
    /// Gets the copy: enabled when the selection is not empty.
    /// </summary>
    public MenuCommand Copy { get; }

    /// <summary>
    /// Gets the select-all: enabled when a leaf the area selects has text.
    /// </summary>
    public MenuCommand SelectAll { get; }
}
