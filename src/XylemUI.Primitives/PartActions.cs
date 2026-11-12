namespace XylemUI.Primitives;

/// <summary>
/// The actions of a menu (design §13.1, "Keys"; §7, "The default keys and actions"). The menu whose item has the focus handles
/// them; the open list of a select handles the four that move and <see cref="Confirm"/> too.
/// </summary>
public static class MenuActions
{
    /// <summary>
    /// Moves the focus to the item before; the default of the up arrow in a menu.
    /// </summary>
    [Action("menu::SelectPrevious")]
    public sealed class SelectPrevious : IAction;

    /// <summary>
    /// Moves the focus to the item after; the default of the down arrow in a menu.
    /// </summary>
    [Action("menu::SelectNext")]
    public sealed class SelectNext : IAction;

    /// <summary>
    /// Moves the focus to the first item; the default of Home in a menu.
    /// </summary>
    [Action("menu::SelectFirst")]
    public sealed class SelectFirst : IAction;

    /// <summary>
    /// Moves the focus to the last item; the default of End in a menu.
    /// </summary>
    [Action("menu::SelectLast")]
    public sealed class SelectLast : IAction;

    /// <summary>
    /// Chooses the focused item, or opens its submenu; the default of Enter and Space in a menu.
    /// </summary>
    [Action("menu::Confirm")]
    public sealed class Confirm : IAction;

    /// <summary>
    /// Opens the submenu of the focused item; the default of the right arrow in a menu. A menu whose focused item has none
    /// handles it and does nothing, so that the page under the menu does not scroll; a menu open from a menu bar leaves it to the
    /// bar, which opens the next menu.
    /// </summary>
    [Action("menu::OpenSubmenu")]
    public sealed class OpenSubmenu : IAction;

    /// <summary>
    /// Closes the submenu the focus is in; the default of the left arrow in a menu. A menu that is no submenu handles it and does
    /// nothing, so that the page under the menu does not scroll; a menu open from a menu bar leaves it to the bar, which opens the
    /// menu before.
    /// </summary>
    [Action("menu::CloseSubmenu")]
    public sealed class CloseSubmenu : IAction;

    /// <summary>
    /// Opens the context menu of the focused node, or of the nearest node above it that has one; the default of Shift+F10. The up
    /// of the application key opens it too, as Windows does, without this action: a row of the keymap is a key that goes down.
    /// </summary>
    [Action("menu::OpenContextMenu")]
    public sealed class OpenContextMenu : IAction;
}

/// <summary>
/// The actions of a select (design §13.1, "Select"; §7, "The default keys and actions").
/// </summary>
public static class SelectActions
{
    /// <summary>
    /// Opens the list, or closes it when it is open; the default of Alt and the up or the down arrow, F4, Enter, and Space on the
    /// box, and of Alt and an arrow and F4 in the list.
    /// </summary>
    [Action("select::Open")]
    public sealed class Open : IAction;

    /// <summary>
    /// Makes the option before the value the value, with the list closed; the default of the up arrow on the box.
    /// </summary>
    [Action("select::Previous")]
    public sealed class Previous : IAction;

    /// <summary>
    /// Makes the option after the value the value, with the list closed; the default of the down arrow on the box.
    /// </summary>
    [Action("select::Next")]
    public sealed class Next : IAction;

    /// <summary>
    /// Moves the focus a page of rows up in the open list; the default of Page Up there.
    /// </summary>
    [Action("select::PageUp")]
    public sealed class PageUp : IAction;

    /// <summary>
    /// Moves the focus a page of rows down in the open list; the default of Page Down there.
    /// </summary>
    [Action("select::PageDown")]
    public sealed class PageDown : IAction;

    /// <summary>
    /// Closes the open list with the value kept and moves the focus on from the box to the next node of the Tab order; the default
    /// of Tab in the list.
    /// </summary>
    [Action("select::CloseAndFocusNext")]
    public sealed class CloseAndFocusNext : IAction;

    /// <summary>
    /// Closes the open list with the value kept and moves the focus on from the box to the previous node of the Tab order; the
    /// default of Shift+Tab in the list.
    /// </summary>
    [Action("select::CloseAndFocusPrevious")]
    public sealed class CloseAndFocusPrevious : IAction;
}

/// <summary>
/// The actions of a menu bar (design §13.1, "The menu bar"; §7, "The default keys and actions").
/// </summary>
public static class MenuBarActions
{
    /// <summary>
    /// Moves to the item before, and opens its menu when a menu was open; the default of the left arrow on the bar.
    /// </summary>
    [Action("menubar::Previous")]
    public sealed class Previous : IAction;

    /// <summary>
    /// Moves to the item after, and opens its menu when a menu was open; the default of the right arrow on the bar.
    /// </summary>
    [Action("menubar::Next")]
    public sealed class Next : IAction;

    /// <summary>
    /// Opens the menu of the focused item; the default of the down and the up arrow, Enter, and Space on the bar.
    /// </summary>
    [Action("menubar::Open")]
    public sealed class Open : IAction
    {
        /// <summary>
        /// Gets or sets a value indicating whether the menu opens with its last item focused, as the up arrow opens it; otherwise
        /// its first.
        /// </summary>
        public bool Last { get; set; }
    }
}
