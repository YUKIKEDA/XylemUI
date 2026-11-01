namespace XylemUI;

/// <summary>
/// How a popup opened with <see cref="ElementExtensions.Popup"/> is placed, closed, and moved (design §7.3).
/// </summary>
public sealed record PopupOptions
{
    /// <summary>
    /// Gets where the popup is placed against its anchor; <see cref="PopupPlacement.Below"/> by default.
    /// </summary>
    public PopupPlacement Placement { get; init; } = PopupPlacement.Below;

    /// <summary>
    /// Gets a value indicating whether the popup is modal: its root takes the pointer over the whole window, and the focus cannot
    /// leave it (design §7.2).
    /// </summary>
    public bool IsModal { get; init; }

    /// <summary>
    /// Gets a value indicating whether a press outside a popup that is not modal stops there; by default the press closes the popup
    /// and goes on to the node under it (design §7.3, "Closing").
    /// </summary>
    public bool ConsumeOutsidePress { get; init; }

    /// <summary>
    /// Gets whether a press outside the popup closes it, on a modal popup's backdrop; <see langword="null"/>, the default, closes a
    /// popup that is not modal and keeps a modal one.
    /// </summary>
    public bool? DismissOnOutsidePress { get; init; }

    /// <summary>
    /// Gets how the popup enters and leaves; <see langword="null"/>, the default, opens and closes it at once.
    /// </summary>
    public ShowMotion? Motion { get; init; }

    /// <summary>
    /// Gets a value indicating whether the popup is transient, a tooltip's (design §7.3, "Nesting"): it pushes no focus scope, so that
    /// Tab does not go round it; it closes no sibling when it opens; and any other popup that opens closes it.
    /// </summary>
    internal bool IsTransient { get; init; }

    /// <summary>
    /// Gets a value indicating whether the popup holds a drag's preview (design §7.3, "The drag's preview root"): it pushes no focus
    /// scope, closes nothing and is closed by nothing but its drag, stays in front of every other popup, is placed where the drag puts
    /// it rather than against its anchor, and outlives its anchor's scroll.
    /// </summary>
    internal bool IsDragImage { get; init; }

    /// <summary>
    /// Gets a value indicating whether a press outside the popup closes it, after the default is applied.
    /// </summary>
    internal bool DismissesOnOutsidePress => !IsDragImage && (DismissOnOutsidePress ?? !IsModal);
}
