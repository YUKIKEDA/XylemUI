[assembly: XylemUI.DefaultKeyBindings(typeof(XylemUI.Primitives.PrimitiveKeyBindings))]

namespace XylemUI.Primitives;

/// <summary>
/// The default key bindings of <c>XylemUI.Primitives</c> on Windows (design §7, "The default keys and actions"). An app's
/// generated module initializer registers <see cref="AddDefaults"/> after <see cref="CoreKeyBindings.AddDefaults"/>.
/// </summary>
/// <remarks>
/// Each part gives its node a context, and the rows here name it: a row of a part is then deeper than the rows of
/// <see cref="CoreKeyBindings"/>, which have the tree's own context, and wins over them. The part handles the row's action on
/// its node; when it does not, the next row of the keys is tried, the one of <see cref="CoreKeyBindings"/> last.
/// </remarks>
public static class PrimitiveKeyBindings
{
    /// <summary>
    /// The context of a text field's node; a field of many lines also has <see cref="MultilineContext"/>.
    /// </summary>
    public const string TextFieldContext = "TextField";

    /// <summary>
    /// The name a text field of many lines adds to <see cref="TextFieldContext"/>.
    /// </summary>
    public const string MultilineContext = "multiline";

    /// <summary>
    /// The context of a selection area's node.
    /// </summary>
    public const string SelectionAreaContext = "SelectionArea";

    /// <summary>
    /// The context of a menu's panel.
    /// </summary>
    public const string MenuContext = "Menu";

    /// <summary>
    /// The context of a select's box.
    /// </summary>
    public const string SelectContext = "Select";

    /// <summary>
    /// The context of a select's open list.
    /// </summary>
    public const string SelectListContext = "SelectList";

    /// <summary>
    /// The context of a menu bar's item.
    /// </summary>
    public const string MenuBarContext = "MenuBar";

    /// <summary>
    /// Adds the default rows of the parts.
    /// </summary>
    /// <param name="keymap">The keymap.</param>
    public static void AddDefaults(Keymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        AddTextField(keymap);
        _ = keymap.Bind("ctrl-a", new TextActions.SelectAll(), SelectionAreaContext);
        _ = keymap.Bind("ctrl-insert", new TextActions.Copy(), SelectionAreaContext);
        _ = keymap.Bind("ctrl-c", new TextActions.Copy(), SelectionAreaContext);
        _ = keymap.Bind("up", new MenuActions.SelectPrevious(), MenuContext);
        _ = keymap.Bind("down", new MenuActions.SelectNext(), MenuContext);
        _ = keymap.Bind("home", new MenuActions.SelectFirst(), MenuContext);
        _ = keymap.Bind("end", new MenuActions.SelectLast(), MenuContext);
        _ = keymap.Bind("enter", new MenuActions.Confirm(), MenuContext);
        _ = keymap.Bind("space", new MenuActions.Confirm(), MenuContext);
        _ = keymap.Bind("right", new MenuActions.OpenSubmenu(), MenuContext);
        _ = keymap.Bind("left", new MenuActions.CloseSubmenu(), MenuContext);
        _ = keymap.Bind("shift-f10", new MenuActions.OpenContextMenu(), CoreKeyBindings.WindowContext);
        _ = keymap.Bind("alt-up", new SelectActions.Toggle(), SelectContext);
        _ = keymap.Bind("alt-down", new SelectActions.Toggle(), SelectContext);
        _ = keymap.Bind("f4", new SelectActions.Toggle(), SelectContext);
        _ = keymap.Bind("enter", new SelectActions.Open(), SelectContext);
        _ = keymap.Bind("space", new SelectActions.Open(), SelectContext);
        _ = keymap.Bind("up", new SelectActions.Previous(), SelectContext);
        _ = keymap.Bind("down", new SelectActions.Next(), SelectContext);
        _ = keymap.Bind("up", new MenuActions.SelectPrevious(), SelectListContext);
        _ = keymap.Bind("down", new MenuActions.SelectNext(), SelectListContext);
        _ = keymap.Bind("home", new MenuActions.SelectFirst(), SelectListContext);
        _ = keymap.Bind("end", new MenuActions.SelectLast(), SelectListContext);
        _ = keymap.Bind("pageup", new SelectActions.PageUp(), SelectListContext);
        _ = keymap.Bind("pagedown", new SelectActions.PageDown(), SelectListContext);
        _ = keymap.Bind("enter", new MenuActions.Confirm(), SelectListContext);
        _ = keymap.Bind("space", new MenuActions.Confirm(), SelectListContext);
        _ = keymap.Bind("tab", new SelectActions.CloseAndFocusNext(), SelectListContext);
        _ = keymap.Bind("shift-tab", new SelectActions.CloseAndFocusPrevious(), SelectListContext);
        _ = keymap.Bind("left", new MenuBarActions.Previous(), MenuBarContext);
        _ = keymap.Bind("right", new MenuBarActions.Next(), MenuBarContext);
        _ = keymap.Bind("down", new MenuBarActions.Open(), MenuBarContext);
        _ = keymap.Bind("enter", new MenuBarActions.Open(), MenuBarContext);
        _ = keymap.Bind("space", new MenuBarActions.Open(), MenuBarContext);
        _ = keymap.Bind("up", new MenuBarActions.Open { Last = true }, MenuBarContext);
    }

    // The rows of design §7's table for the text field (design §4.9, the table of the field's keys).
    private static void AddTextField(Keymap keymap)
    {
        const string Field = TextFieldContext;
        const string Many = TextFieldContext + " && " + MultilineContext;
        const string One = TextFieldContext + " && !" + MultilineContext;
        _ = keymap.Bind("left", new TextActions.MoveLeft(), Field);
        _ = keymap.Bind("right", new TextActions.MoveRight(), Field);
        _ = keymap.Bind("shift-left", new TextActions.SelectLeft(), Field);
        _ = keymap.Bind("shift-right", new TextActions.SelectRight(), Field);
        _ = keymap.Bind("ctrl-left", new TextActions.MoveWordLeft(), Field);
        _ = keymap.Bind("ctrl-right", new TextActions.MoveWordRight(), Field);
        _ = keymap.Bind("ctrl-shift-left", new TextActions.SelectWordLeft(), Field);
        _ = keymap.Bind("ctrl-shift-right", new TextActions.SelectWordRight(), Field);
        _ = keymap.Bind("home", new TextActions.MoveToLineStart(), Field);
        _ = keymap.Bind("end", new TextActions.MoveToLineEnd(), Field);
        _ = keymap.Bind("shift-home", new TextActions.SelectToLineStart(), Field);
        _ = keymap.Bind("shift-end", new TextActions.SelectToLineEnd(), Field);
        _ = keymap.Bind("ctrl-home", new TextActions.MoveToStart(), Field);
        _ = keymap.Bind("ctrl-end", new TextActions.MoveToEnd(), Field);
        _ = keymap.Bind("ctrl-shift-home", new TextActions.SelectToStart(), Field);
        _ = keymap.Bind("ctrl-shift-end", new TextActions.SelectToEnd(), Field);
        _ = keymap.Bind("up", new TextActions.MoveUp(), Many);
        _ = keymap.Bind("down", new TextActions.MoveDown(), Many);
        _ = keymap.Bind("shift-up", new TextActions.SelectUp(), Many);
        _ = keymap.Bind("shift-down", new TextActions.SelectDown(), Many);
        _ = keymap.Bind("pageup", new TextActions.MovePageUp(), Many);
        _ = keymap.Bind("pagedown", new TextActions.MovePageDown(), Many);
        _ = keymap.Bind("shift-pageup", new TextActions.SelectPageUp(), Many);
        _ = keymap.Bind("shift-pagedown", new TextActions.SelectPageDown(), Many);
        _ = keymap.Bind("backspace", new TextActions.DeleteBackward(), Field);
        _ = keymap.Bind("delete", new TextActions.DeleteForward(), Field);
        _ = keymap.Bind("ctrl-backspace", new TextActions.DeleteWordBackward(), Field);
        _ = keymap.Bind("ctrl-delete", new TextActions.DeleteWordForward(), Field);

        // Of two keys for one action, the one a menu shows is added last (KeyBindingFor returns the row added later).
        _ = keymap.Bind("shift-delete", new TextActions.Cut(), Field);
        _ = keymap.Bind("ctrl-x", new TextActions.Cut(), Field);
        _ = keymap.Bind("ctrl-insert", new TextActions.Copy(), Field);
        _ = keymap.Bind("ctrl-c", new TextActions.Copy(), Field);
        _ = keymap.Bind("shift-insert", new TextActions.Paste(), Field);
        _ = keymap.Bind("ctrl-v", new TextActions.Paste(), Field);
        _ = keymap.Bind("ctrl-a", new TextActions.SelectAll(), Field);
        _ = keymap.Bind("ctrl-z", new TextActions.Undo(), Field);
        _ = keymap.Bind("ctrl-shift-z", new TextActions.Redo(), Field);
        _ = keymap.Bind("ctrl-y", new TextActions.Redo(), Field);
        _ = keymap.Bind("shift-enter", new TextActions.InsertNewline(), Many);
        _ = keymap.Bind("enter", new TextActions.InsertNewline(), Many);
        _ = keymap.Bind("enter", new TextActions.Submit(), One);
    }
}
