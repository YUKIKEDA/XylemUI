namespace XylemUI;

/// <summary>
/// A selection in a text, in UTF-16 code units (design §4.9, "The flow of the values"): where it was started and where the caret is.
/// A collapsed selection is a caret alone.
/// </summary>
/// <param name="Anchor">Where the selection was started; it stays when Shift extends the selection.</param>
/// <param name="Caret">Where the caret is: the end that moves.</param>
public readonly record struct TextSelection(int Anchor, int Caret)
{
    /// <summary>
    /// Gets the smaller end.
    /// </summary>
    public int Start => Math.Min(Anchor, Caret);

    /// <summary>
    /// Gets the larger end.
    /// </summary>
    public int End => Math.Max(Anchor, Caret);

    /// <summary>
    /// Gets the number of code units selected.
    /// </summary>
    public int Length => End - Start;

    /// <summary>
    /// Gets a value indicating whether the selection is a caret alone.
    /// </summary>
    public bool IsCollapsed => Anchor == Caret;

    /// <summary>
    /// Returns a caret alone at a position.
    /// </summary>
    /// <param name="offset">The position.</param>
    /// <returns>The selection.</returns>
    public static TextSelection Collapsed(int offset) => new(offset, offset);
}
