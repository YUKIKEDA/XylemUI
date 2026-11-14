namespace XylemUI;

/// <summary>
/// The handlers of the actions of assistive technology other than a click that a node has (design §4.4.1, "Actions other than a
/// click"): kept for the nodes that have one only, apart from <see cref="UiNode"/>.
/// </summary>
internal sealed class AccessibilityHandlers
{
    /// <summary>
    /// Gets or sets the handler of Increment (<see cref="ElementExtensions.OnIncrement"/>).
    /// </summary>
    public Action? Increment { get; set; }

    /// <summary>
    /// Gets or sets the handler of Decrement (<see cref="ElementExtensions.OnDecrement"/>).
    /// </summary>
    public Action? Decrement { get; set; }

    /// <summary>
    /// Gets or sets the handler of a number set (<see cref="ElementExtensions.OnSetRangeValue"/>).
    /// </summary>
    public Action<double>? SetRangeValue { get; set; }

    /// <summary>
    /// Gets or sets the handler of Expand (<see cref="ElementExtensions.OnExpand"/>).
    /// </summary>
    public Action? Expand { get; set; }

    /// <summary>
    /// Gets or sets the handler of Collapse (<see cref="ElementExtensions.OnCollapse"/>).
    /// </summary>
    public Action? Collapse { get; set; }

    /// <summary>
    /// Gets a value indicating whether no handler is left.
    /// </summary>
    public bool IsEmpty => Increment is null && Decrement is null && SetRangeValue is null && Expand is null && Collapse is null;

    /// <summary>
    /// Returns the states the handlers give the node's node.
    /// </summary>
    /// <returns>The states.</returns>
    public AccessibilityStates States() =>
        (Increment is null ? AccessibilityStates.None : AccessibilityStates.Increments)
        | (Decrement is null ? AccessibilityStates.None : AccessibilityStates.Decrements)
        | (SetRangeValue is null ? AccessibilityStates.None : AccessibilityStates.SetsRangeValue)
        | (Expand is null && Collapse is null ? AccessibilityStates.None : AccessibilityStates.ExpandsByHandler);
}
