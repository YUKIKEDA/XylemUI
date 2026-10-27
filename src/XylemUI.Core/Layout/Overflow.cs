namespace XylemUI;

/// <summary>
/// What a box does with the content that overflows it on one axis (CSS's <c>overflow</c>, design §3.4.1, "The scroll
/// container"). <see cref="Hidden"/> and <see cref="Scroll"/> make the box a scroll container.
/// </summary>
public enum Overflow : byte
{
    /// <summary>
    /// The content is drawn outside the box, and the box's parent counts it in its own scrollable overflow.
    /// </summary>
    Visible,

    /// <summary>
    /// The content is clipped to the padding box, and the box does not scroll.
    /// </summary>
    Clip,

    /// <summary>
    /// The content is clipped to the padding box, and the box scrolls when its position is set, with no scrollbar's room.
    /// </summary>
    Hidden,

    /// <summary>
    /// The content is clipped to the padding box, and the box scrolls, with room for a scrollbar of
    /// <c>ScrollbarWidth</c> on the other axis.
    /// </summary>
    Scroll,
}
