namespace XylemUI;

/// <summary>
/// The selection a text leaf draws (design §4.10, "Drawing"): a range of its text, behind which the draw stage fills the line's
/// rectangles, and whose glyphs it draws in the selection's text color. A text field and a selection area set it through
/// <see cref="UiTree.SetTextSelection"/>. A leaf makes one the first time it is selected and keeps it, so that a leaf never
/// selected grows by its reference alone and a selection that moves allocates nothing.
/// </summary>
internal sealed class LeafSelection
{
    /// <summary>
    /// Gets the first UTF-16 code unit of the range.
    /// </summary>
    public int Start { get; private set; }

    /// <summary>
    /// Gets the end of the range, past its last code unit.
    /// </summary>
    public int End { get; private set; }

    /// <summary>
    /// Gets the color filled behind the selected glyphs.
    /// </summary>
    public Color Background { get; private set; }

    /// <summary>
    /// Gets the color of the selected glyphs.
    /// </summary>
    public Color Foreground { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the range holds no text, so that nothing is drawn.
    /// </summary>
    public bool IsEmpty => End <= Start;

    /// <summary>
    /// Returns whether the range holds a cluster that starts at a position.
    /// </summary>
    /// <param name="cluster">The position of the cluster's start.</param>
    /// <returns>Whether the cluster is selected.</returns>
    public bool Holds(int cluster) => cluster >= Start && cluster < End;

    /// <summary>
    /// Sets the range and its colors; an empty range is kept as 0 to 0, so that every empty range is the same.
    /// </summary>
    /// <param name="start">The first code unit of the range.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="background">The color behind the selected glyphs.</param>
    /// <param name="foreground">The color of the selected glyphs.</param>
    /// <returns>Whether anything the draw stage reads changed.</returns>
    public bool Set(int start, int end, Color background, Color foreground)
    {
        if (end <= start)
        {
            (start, end) = (0, 0);
        }

        bool changed = start != Start || end != End || (end > start && (background != Background || foreground != Foreground));
        (Start, End, Background, Foreground) = (start, end, background, foreground);
        return changed;
    }
}
