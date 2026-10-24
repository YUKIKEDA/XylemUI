namespace XylemUI;

/// <summary>
/// The shape of the ends of a line, of an open arc, and of each dash (design §5.0, "The API of lines, ellipses, arcs, and the paint
/// of text"), as SVG's <c>stroke-linecap</c>.
/// </summary>
public enum LineCap : byte
{
    /// <summary>The line ends where it ends.</summary>
    Flat,

    /// <summary>A half disc of half the width is added past each end.</summary>
    Round,

    /// <summary>Half a square of the width is added past each end.</summary>
    Square,
}
