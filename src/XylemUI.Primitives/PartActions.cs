namespace XylemUI.Primitives;

/// <summary>
/// An action of a menu, which the open list of a select handles too (design §7, "The default keys and actions"): the base type of the part's actions, so that the part has
/// one handler for them all.
/// </summary>
public abstract class MenuAction : IAction
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MenuAction"/> class.
    /// </summary>
    private protected MenuAction() { }

    /// <summary>
    /// Gets what the part does.
    /// </summary>
    internal abstract MenuStep Step { get; }
}

/// <summary>
/// What a <see cref="MenuAction"/> does.
/// </summary>
internal enum MenuStep
{
    /// <summary>To the item before.</summary>
    SelectPrevious,

    /// <summary>To the item after.</summary>
    SelectNext,

    /// <summary>To the first item.</summary>
    SelectFirst,

    /// <summary>To the last item.</summary>
    SelectLast,

    /// <summary>Chooses the focused item.</summary>
    Confirm,

    /// <summary>Opens the focused item's submenu.</summary>
    OpenSubmenu,

    /// <summary>Closes the submenu.</summary>
    CloseSubmenu,
}

/// <summary>
/// The actions of a menu (design §13.1, "Keys"; §7, "The default keys and actions"). The menu whose item has the focus handles
/// them; the open list of a select handles the four that move and <see cref="MenuActions.Confirm"/> too.
/// </summary>
public static class MenuActions
{
    /// <summary>
    /// Moves the focus to the item before; the default of the up arrow in a menu.
    /// </summary>
    [Action("menu::SelectPrevious")]
    public sealed class SelectPrevious : MenuAction
    {
        /// <inheritdoc/>
        internal override MenuStep Step => MenuStep.SelectPrevious;
    }

    /// <summary>
    /// Moves the focus to the item after; the default of the down arrow in a menu.
    /// </summary>
    [Action("menu::SelectNext")]
    public sealed class SelectNext : MenuAction
    {
        /// <inheritdoc/>
        internal override MenuStep Step => MenuStep.SelectNext;
    }

    /// <summary>
    /// Moves the focus to the first item; the default of Home in a menu.
    /// </summary>
    [Action("menu::SelectFirst")]
    public sealed class SelectFirst : MenuAction
    {
        /// <inheritdoc/>
        internal override MenuStep Step => MenuStep.SelectFirst;
    }

    /// <summary>
    /// Moves the focus to the last item; the default of End in a menu.
    /// </summary>
    [Action("menu::SelectLast")]
    public sealed class SelectLast : MenuAction
    {
        /// <inheritdoc/>
        internal override MenuStep Step => MenuStep.SelectLast;
    }

    /// <summary>
    /// Chooses the focused item, or opens its submenu; the default of Enter and Space in a menu.
    /// </summary>
    [Action("menu::Confirm")]
    public sealed class Confirm : MenuAction
    {
        /// <inheritdoc/>
        internal override MenuStep Step => MenuStep.Confirm;
    }

    /// <summary>
    /// Opens the submenu of the focused item; the default of the right arrow in a menu. A menu whose focused item has none
    /// handles it and does nothing, so that the page under the menu does not scroll; a menu open from a menu bar leaves it to the
    /// bar, which opens the next menu.
    /// </summary>
    [Action("menu::OpenSubmenu")]
    public sealed class OpenSubmenu : MenuAction
    {
        /// <inheritdoc/>
        internal override MenuStep Step => MenuStep.OpenSubmenu;
    }

    /// <summary>
    /// Closes the submenu the focus is in; the default of the left arrow in a menu. A menu that is no submenu handles it and does
    /// nothing, so that the page under the menu does not scroll; a menu open from a menu bar leaves it to the bar, which opens the
    /// menu before.
    /// </summary>
    [Action("menu::CloseSubmenu")]
    public sealed class CloseSubmenu : MenuAction
    {
        /// <inheritdoc/>
        internal override MenuStep Step => MenuStep.CloseSubmenu;
    }

    /// <summary>
    /// Opens the context menu of the focused node, or of the nearest node above it that has one; the default of Shift+F10. The up
    /// of the application key opens it too, as Windows does, without this action: a row of the keymap is a key that goes down.
    /// </summary>
    [Action("menu::OpenContextMenu")]
    public sealed class OpenContextMenu : IAction;
}

/// <summary>
/// An action of a select (design §7, "The default keys and actions"): the base type of the part's actions, so that the part has
/// one handler for them all.
/// </summary>
public abstract class SelectAction : IAction
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SelectAction"/> class.
    /// </summary>
    private protected SelectAction() { }

    /// <summary>
    /// Gets what the part does.
    /// </summary>
    internal abstract SelectStep Step { get; }
}

/// <summary>
/// What a <see cref="SelectAction"/> does.
/// </summary>
internal enum SelectStep
{
    /// <summary>Opens the list.</summary>
    Open,

    /// <summary>Opens or closes the list.</summary>
    Toggle,

    /// <summary>The option before the value.</summary>
    Previous,

    /// <summary>The option after the value.</summary>
    Next,

    /// <summary>A page of rows up.</summary>
    PageUp,

    /// <summary>A page of rows down.</summary>
    PageDown,

    /// <summary>Closes the list and moves the focus on.</summary>
    CloseAndFocusNext,

    /// <summary>Closes the list and moves the focus back.</summary>
    CloseAndFocusPrevious,
}

/// <summary>
/// The actions of a select (design §13.1, "Select"; §7, "The default keys and actions").
/// </summary>
public static class SelectActions
{
    /// <summary>
    /// Opens the list of the closed box; the default of Enter and Space on the box.
    /// </summary>
    [Action("select::Open")]
    public sealed class Open : SelectAction
    {
        /// <inheritdoc/>
        internal override SelectStep Step => SelectStep.Open;
    }

    /// <summary>
    /// Opens the list, or closes it when it is open; the default of Alt and the up or the down arrow and of F4, on the box and
    /// in the list.
    /// </summary>
    [Action("select::Toggle")]
    public sealed class Toggle : SelectAction
    {
        /// <inheritdoc/>
        internal override SelectStep Step => SelectStep.Toggle;
    }

    /// <summary>
    /// Makes the option before the value the value, with the list closed; the default of the up arrow on the box.
    /// </summary>
    [Action("select::Previous")]
    public sealed class Previous : SelectAction
    {
        /// <inheritdoc/>
        internal override SelectStep Step => SelectStep.Previous;
    }

    /// <summary>
    /// Makes the option after the value the value, with the list closed; the default of the down arrow on the box.
    /// </summary>
    [Action("select::Next")]
    public sealed class Next : SelectAction
    {
        /// <inheritdoc/>
        internal override SelectStep Step => SelectStep.Next;
    }

    /// <summary>
    /// Moves the focus a page of rows up in the open list; the default of Page Up there.
    /// </summary>
    [Action("select::PageUp")]
    public sealed class PageUp : SelectAction
    {
        /// <inheritdoc/>
        internal override SelectStep Step => SelectStep.PageUp;
    }

    /// <summary>
    /// Moves the focus a page of rows down in the open list; the default of Page Down there.
    /// </summary>
    [Action("select::PageDown")]
    public sealed class PageDown : SelectAction
    {
        /// <inheritdoc/>
        internal override SelectStep Step => SelectStep.PageDown;
    }

    /// <summary>
    /// Closes the open list with the value kept and moves the focus on from the box to the next node of the Tab order; the default
    /// of Tab in the list.
    /// </summary>
    [Action("select::CloseAndFocusNext")]
    public sealed class CloseAndFocusNext : SelectAction
    {
        /// <inheritdoc/>
        internal override SelectStep Step => SelectStep.CloseAndFocusNext;
    }

    /// <summary>
    /// Closes the open list with the value kept and moves the focus on from the box to the previous node of the Tab order; the
    /// default of Shift+Tab in the list.
    /// </summary>
    [Action("select::CloseAndFocusPrevious")]
    public sealed class CloseAndFocusPrevious : SelectAction
    {
        /// <inheritdoc/>
        internal override SelectStep Step => SelectStep.CloseAndFocusPrevious;
    }
}

/// <summary>
/// An action of a menu bar (design §7, "The default keys and actions"): the base type of the part's actions, so that the part has
/// one handler for them all.
/// </summary>
public abstract class MenuBarAction : IAction
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MenuBarAction"/> class.
    /// </summary>
    private protected MenuBarAction() { }

    /// <summary>
    /// Gets what the part does.
    /// </summary>
    internal abstract MenuBarStep Step { get; }
}

/// <summary>
/// What a <see cref="MenuBarAction"/> does.
/// </summary>
internal enum MenuBarStep
{
    /// <summary>To the item before.</summary>
    Previous,

    /// <summary>To the item after.</summary>
    Next,

    /// <summary>Opens the item's menu.</summary>
    Open,
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
    public sealed class Previous : MenuBarAction
    {
        /// <inheritdoc/>
        internal override MenuBarStep Step => MenuBarStep.Previous;
    }

    /// <summary>
    /// Moves to the item after, and opens its menu when a menu was open; the default of the right arrow on the bar.
    /// </summary>
    [Action("menubar::Next")]
    public sealed class Next : MenuBarAction
    {
        /// <inheritdoc/>
        internal override MenuBarStep Step => MenuBarStep.Next;
    }

    /// <summary>
    /// Opens the menu of the focused item; the default of the down and the up arrow, Enter, and Space on the bar.
    /// </summary>
    [Action("menubar::Open")]
    public sealed class Open : MenuBarAction, IEquatable<IAction>
    {
        /// <inheritdoc/>
        internal override MenuBarStep Step => MenuBarStep.Open;

        /// <summary>
        /// Gets or sets a value indicating whether the menu opens with its last item focused, as the up arrow opens it; otherwise
        /// its first.
        /// </summary>
        public bool Last { get; set; }

        /// <summary>
        /// Returns whether another action opens the menu the same way, so that the keys shown for this action are those of a row
        /// with the same <see cref="Last"/> (design §7, "Menu items that name actions").
        /// </summary>
        /// <param name="other">The other action.</param>
        /// <returns>Whether it is an <see cref="Open"/> with the same <see cref="Last"/>.</returns>
        public bool Equals(IAction? other) => other is Open open && open.Last == Last;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is IAction action && Equals(action);

        /// <inheritdoc/>
        public override int GetHashCode() => Last.GetHashCode();
    }
}
