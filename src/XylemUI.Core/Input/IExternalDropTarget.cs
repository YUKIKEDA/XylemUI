namespace XylemUI;

/// <summary>
/// What a window's OS drop target calls (design §7, "Drag and drop", "From the OS"): a drag from outside the app that comes over the
/// window, moves, leaves, and drops, delivered as a drag in the app whose value is <see cref="DroppedFiles"/> or
/// <see cref="DroppedText"/>. Positions are in the window's client area, in physical pixels.
/// </summary>
internal interface IExternalDropTarget
{
    /// <summary>
    /// Starts a drag from outside the app over the window.
    /// </summary>
    /// <param name="value">The value: <see cref="DroppedFiles"/> or <see cref="DroppedText"/>.</param>
    /// <param name="x">The x in the client area.</param>
    /// <param name="y">The y in the client area.</param>
    /// <param name="allowed">The effects the OS's source allows.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <returns>The effect a drop would choose now; <see cref="DragEffects.None"/> over no target, or while a drag of the app runs.</returns>
    DragEffects DropEnter(object value, int x, int y, DragEffects allowed, KeyModifiers modifiers);

    /// <summary>
    /// Moves the drag from outside the app.
    /// </summary>
    /// <param name="x">The x in the client area.</param>
    /// <param name="y">The y in the client area.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <returns>The effect a drop would choose now.</returns>
    DragEffects DropOver(int x, int y, KeyModifiers modifiers);

    /// <summary>
    /// Ends the drag from outside the app with no drop: it left the window, or was cancelled.
    /// </summary>
    void DropLeave();

    /// <summary>
    /// Drops the value of the drag from outside the app.
    /// </summary>
    /// <param name="x">The x in the client area.</param>
    /// <param name="y">The y in the client area.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <returns>The effect the drop chose, or <see cref="DragEffects.None"/> when nothing took it.</returns>
    DragEffects Drop(int x, int y, KeyModifiers modifiers);
}
