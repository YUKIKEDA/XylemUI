[assembly: XylemUI.DefaultKeyBindings(typeof(XylemUI.Primitives.PrimitiveKeyBindings))]

namespace XylemUI.Primitives;

/// <summary>
/// The default key bindings of <c>XylemUI.Primitives</c> on Windows (design §7, "The default keys and actions"). An app's
/// generated module initializer registers <see cref="AddDefaults"/> after <see cref="CoreKeyBindings.AddDefaults"/>.
/// </summary>
/// <remarks>
/// The parts still take their keys in key handlers (roadmap row KM2b moves them to actions). The keymap is tried before the key
/// handlers, so each part takes away, in its own context, the rows of <see cref="CoreKeyBindings"/> for the keys it handles: a row
/// with no action leaves the key to the key handlers.
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
        Unbind(keymap, TextFieldContext, "left", "right", "home", "end");
        Unbind(keymap, TextFieldContext + " && " + MultilineContext, "up", "down", "pageup", "pagedown");
        Unbind(keymap, MenuContext, "up", "down", "left", "right", "home", "end");
        Unbind(keymap, SelectContext, "up", "down");
        Unbind(keymap, SelectListContext, "up", "down", "home", "end", "pageup", "pagedown", "tab", "shift-tab");
        Unbind(keymap, MenuBarContext, "left", "right", "up", "down");

        // Esc leaves the bar while one of its items has the focus. In a menu open from the bar, whose keys go on to the item,
        // Esc keeps its row and closes the menu.
        Unbind(keymap, MenuBarContext + " && !" + MenuContext, "escape");
    }

    private static void Unbind(Keymap keymap, string context, params ReadOnlySpan<string> keys)
    {
        foreach (string key in keys)
        {
            _ = keymap.Bind(key, null, context);
        }
    }
}
