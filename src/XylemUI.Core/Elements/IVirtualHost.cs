namespace XylemUI;

/// <summary>
/// What the layout, the frame, and the draw stage ask a virtualized list for (design §3.4, "The shape of P02e"), without its item
/// types.
/// </summary>
internal interface IVirtualHost
{
    /// <summary>
    /// Gets the length every row takes along the axis that scrolls, margins included, or <see langword="null"/> when each row is
    /// measured.
    /// </summary>
    float? FixedExtent { get; }

    /// <summary>
    /// Gets a value indicating whether the list's box scrolls along the horizontal axis; along the vertical one otherwise.
    /// </summary>
    bool Horizontal { get; }

    /// <summary>
    /// Returns whether a row is laid out (in view or in the cache extent), with its index; a row kept outside them is not.
    /// </summary>
    /// <param name="row">A child of the list's box.</param>
    /// <param name="index">The row's index among the items.</param>
    /// <returns>Whether the row is laid out.</returns>
    bool LaysOut(NodeHandle row, out int index);

    /// <summary>
    /// Records the length a row took along the axis that scrolls, margins included, when it was laid out.
    /// </summary>
    /// <param name="index">The row's index.</param>
    /// <param name="extent">The length.</param>
    void Measured(int index, float extent);

    /// <summary>
    /// Begins a layout of the box: the gap between rows and the padding before and after them, resolved for this layout.
    /// </summary>
    /// <param name="gap">The gap.</param>
    /// <param name="start">The padding and border before the first row.</param>
    /// <param name="end">The padding after the last row, which counts in the scroll range.</param>
    void BeginLayout(float gap, float start, float end);

    /// <summary>
    /// Returns the position of a row's start along the axis that scrolls, from the start of the content: the lengths of the rows
    /// before it, measured or estimated, and the gaps between them.
    /// </summary>
    /// <param name="index">The index, from 0 to the number of items.</param>
    /// <returns>The position.</returns>
    float PositionOf(int index);

    /// <summary>
    /// Returns the length of all the rows along the axis that scrolls, with the gaps between them.
    /// </summary>
    /// <returns>The length.</returns>
    float ContentLength();

    /// <summary>
    /// Returns the end of the box's scrollable overflow along the axis that scrolls, from its padding box's start: the padding
    /// before, the rows, and the padding after.
    /// </summary>
    /// <returns>The end.</returns>
    float ScrollableEnd();

    /// <summary>
    /// Makes the rows the range needs and takes out the rows outside it (design §3.4, "The frame's stages"). The first round runs
    /// before the layout pass; a later one, after a pass whose rows did not fill the range, makes twice as many more rows as the
    /// round before.
    /// </summary>
    /// <param name="round">The round of the frame, from 0.</param>
    /// <returns>Whether a row was made or taken out.</returns>
    bool Materialize(int round);

    /// <summary>
    /// Writes the position its anchor row gives the box's scroll state, and the rows that show, after a layout pass (design §3.4,
    /// "The anchor of the scroll"). It runs in the batch that writes the scroll ranges.
    /// </summary>
    void AfterLayout();

    /// <summary>
    /// Notes the position the scroll state holds after its range was written, which may have kept it in the range.
    /// </summary>
    void NotePosition();

    /// <summary>
    /// Returns whether a row is drawn: laid out, and in the viewport at the box's position now.
    /// </summary>
    /// <param name="row">A child of the list's box.</param>
    /// <returns>Whether the row is drawn.</returns>
    bool Draws(NodeHandle row);
}
