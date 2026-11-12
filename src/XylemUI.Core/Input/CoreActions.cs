[assembly: XylemUI.DefaultKeyBindings(typeof(XylemUI.CoreKeyBindings))]

namespace XylemUI;

/// <summary>
/// An action the tree itself takes when no handler on the route took it (design §7, "The default keys and actions"): moving the
/// focus, closing the front popup, and scrolling. A node that handles one in <c>OnAction</c> takes it first.
/// </summary>
public abstract class CoreAction : IAction
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CoreAction"/> class.
    /// </summary>
    private protected CoreAction() { }

    /// <summary>
    /// Does what the tree does for the action.
    /// </summary>
    /// <param name="input">The input of the tree's window.</param>
    /// <param name="target">The node the action was routed from.</param>
    /// <returns>Whether something changed; <see langword="false"/> leaves the action not handled.</returns>
    internal abstract bool Run(WindowInput input, NodeHandle target);

    /// <summary>
    /// Returns whether <see cref="Run"/> would change something now (design §7, "Menu items that name actions").
    /// </summary>
    /// <param name="input">The input of the tree's window.</param>
    /// <param name="target">The node the action would be routed from.</param>
    /// <returns>Whether something would change.</returns>
    internal abstract bool CanRun(WindowInput input, NodeHandle target);
}

/// <summary>
/// The actions that move the focus (design §7.1, "Focus order").
/// </summary>
public static class FocusActions
{
    /// <summary>
    /// Moves the focus to the next node of the Tab order; the default of Tab.
    /// </summary>
    [Action("focus::Next")]
    public sealed class Next : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.Tree.Focus.MoveInTabOrder(backward: false);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.Tree.Focus.CanMoveInTabOrder(backward: false);
    }

    /// <summary>
    /// Moves the focus to the previous node of the Tab order; the default of Shift+Tab.
    /// </summary>
    [Action("focus::Previous")]
    public sealed class Previous : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.Tree.Focus.MoveInTabOrder(backward: true);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.Tree.Focus.CanMoveInTabOrder(backward: true);
    }
}

/// <summary>
/// The actions of the popups (design §7.3, "Closing").
/// </summary>
public static class PopupActions
{
    /// <summary>
    /// Closes the front popup; the default of Esc. It is not handled while no popup is open.
    /// </summary>
    [Action("popup::Dismiss")]
    public sealed class Dismiss : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.Tree.Popups.Count > 0 && input.Tree.Popups.CloseFront();

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.Tree.Popups.HasFront;
    }
}

/// <summary>
/// The actions that scroll (design §7, "Keyboard scrolling"). The nearest scroll container on the route from the focused node
/// that scrolls on the action's axis and is not at that end takes one; with none, the action is not handled.
/// </summary>
public static class ScrollActions
{
    /// <summary>
    /// Scrolls up by a line; the default of the up arrow.
    /// </summary>
    [Action("scroll::LineUp")]
    public sealed class LineUp : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.ScrollFrom(target, ScrollStep.LineUp);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.CanScrollFrom(target, ScrollStep.LineUp);
    }

    /// <summary>
    /// Scrolls down by a line; the default of the down arrow.
    /// </summary>
    [Action("scroll::LineDown")]
    public sealed class LineDown : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.ScrollFrom(target, ScrollStep.LineDown);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.CanScrollFrom(target, ScrollStep.LineDown);
    }

    /// <summary>
    /// Scrolls left by a line; the default of the left arrow.
    /// </summary>
    [Action("scroll::LineLeft")]
    public sealed class LineLeft : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.ScrollFrom(target, ScrollStep.LineLeft);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.CanScrollFrom(target, ScrollStep.LineLeft);
    }

    /// <summary>
    /// Scrolls right by a line; the default of the right arrow.
    /// </summary>
    [Action("scroll::LineRight")]
    public sealed class LineRight : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.ScrollFrom(target, ScrollStep.LineRight);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.CanScrollFrom(target, ScrollStep.LineRight);
    }

    /// <summary>
    /// Scrolls up by 0.8 of the viewport's height; the default of Page Up.
    /// </summary>
    [Action("scroll::PageUp")]
    public sealed class PageUp : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.ScrollFrom(target, ScrollStep.PageUp);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.CanScrollFrom(target, ScrollStep.PageUp);
    }

    /// <summary>
    /// Scrolls down by 0.8 of the viewport's height; the default of Page Down.
    /// </summary>
    [Action("scroll::PageDown")]
    public sealed class PageDown : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.ScrollFrom(target, ScrollStep.PageDown);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.CanScrollFrom(target, ScrollStep.PageDown);
    }

    /// <summary>
    /// Scrolls to the start: the top, or the left end of a container that scrolls only its width; the default of Home.
    /// </summary>
    [Action("scroll::ToStart")]
    public sealed class ToStart : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.ScrollFrom(target, ScrollStep.ToStart);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.CanScrollFrom(target, ScrollStep.ToStart);
    }

    /// <summary>
    /// Scrolls to the end: the bottom, or the right end of a container that scrolls only its width; the default of End.
    /// </summary>
    [Action("scroll::ToEnd")]
    public sealed class ToEnd : CoreAction
    {
        /// <inheritdoc/>
        internal override bool Run(WindowInput input, NodeHandle target) => input.ScrollFrom(target, ScrollStep.ToEnd);

        /// <inheritdoc/>
        internal override bool CanRun(WindowInput input, NodeHandle target) => input.CanScrollFrom(target, ScrollStep.ToEnd);
    }
}

/// <summary>
/// The default key bindings of <c>XylemUI.Core</c> on Windows (design §7, "The default keys and actions"). An app's generated
/// module initializer registers <see cref="AddDefaults"/>, so that every keymap starts with these rows.
/// </summary>
public static class CoreKeyBindings
{
    /// <summary>
    /// The context the tree itself has, before the context of any node: the condition of the rows of this class. It is the
    /// shallowest there is, so a row with a node's context, and a row with no condition, win over them.
    /// </summary>
    public const string WindowContext = "Window";

    /// <summary>
    /// Adds the default rows: Tab and Shift+Tab move the focus, Esc closes the front popup, and the arrows, Page Up, Page Down,
    /// Home, and End scroll.
    /// </summary>
    /// <param name="keymap">The keymap.</param>
    public static void AddDefaults(Keymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        _ = keymap.Bind("tab", new FocusActions.Next(), WindowContext);
        _ = keymap.Bind("shift-tab", new FocusActions.Previous(), WindowContext);
        _ = keymap.Bind("escape", new PopupActions.Dismiss(), WindowContext);
        _ = keymap.Bind("up", new ScrollActions.LineUp(), WindowContext);
        _ = keymap.Bind("down", new ScrollActions.LineDown(), WindowContext);
        _ = keymap.Bind("left", new ScrollActions.LineLeft(), WindowContext);
        _ = keymap.Bind("right", new ScrollActions.LineRight(), WindowContext);
        _ = keymap.Bind("pageup", new ScrollActions.PageUp(), WindowContext);
        _ = keymap.Bind("pagedown", new ScrollActions.PageDown(), WindowContext);
        _ = keymap.Bind("home", new ScrollActions.ToStart(), WindowContext);
        _ = keymap.Bind("end", new ScrollActions.ToEnd(), WindowContext);
    }
}

/// <summary>
/// What a scrolling action moves a container by.
/// </summary>
internal enum ScrollStep
{
    /// <summary>Up by a line.</summary>
    LineUp,

    /// <summary>Down by a line.</summary>
    LineDown,

    /// <summary>Left by a line.</summary>
    LineLeft,

    /// <summary>Right by a line.</summary>
    LineRight,

    /// <summary>Up by a page.</summary>
    PageUp,

    /// <summary>Down by a page.</summary>
    PageDown,

    /// <summary>To the start.</summary>
    ToStart,

    /// <summary>To the end.</summary>
    ToEnd,
}
