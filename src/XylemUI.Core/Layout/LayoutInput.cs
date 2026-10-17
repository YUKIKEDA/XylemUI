namespace XylemUI;

/// <summary>
/// Whether a node's own size styles take part in its layout (Taffy's <c>SizingMode</c>).
/// </summary>
internal enum SizingMode : byte
{
    /// <summary>The node's width, height, and minimum and maximum sizes apply.</summary>
    InherentSize,

    /// <summary>
    /// The node's size styles are ignored: a flex container asks for the content's size when it sizes an item (its flex base
    /// size and its automatic minimum size, CSS Flexbox §9.2 and §4.5).
    /// </summary>
    ContentSize,
}

/// <summary>
/// Which axes of a node's size a measuring run needs (Taffy's <c>RequestedAxis</c>).
/// </summary>
internal enum RequestedAxis : byte
{
    /// <summary>Both axes.</summary>
    Both,

    /// <summary>The width only; the height of the result means nothing.</summary>
    Horizontal,

    /// <summary>The height only; the width of the result means nothing.</summary>
    Vertical,
}

/// <summary>
/// What a parent gives a child to lay out or measure (design §3.2, Taffy's <c>LayoutInput</c>). The run mode (lay out or only
/// measure) is the separate <c>commit</c> argument of the layout engine.
/// </summary>
/// <param name="KnownDimensions">The border-box sizes the parent decided, or NaN where the child decides.</param>
/// <param name="KnownWidthIsDefinite">
/// The known width is definite for percentages of the children; <see langword="false"/> when the parent derived it from the
/// child's own content. Means nothing when the width is not known.
/// </param>
/// <param name="KnownHeightIsDefinite">The same, for the height.</param>
/// <param name="ParentSize">The size percentages resolve against: the parent's content box, NaN on an indefinite axis.</param>
/// <param name="Available">The space for the child's border box on each axis, its margins already taken off.</param>
/// <param name="SizingMode">Whether the child's own size styles apply.</param>
/// <param name="Axis">The axes a measuring run needs.</param>
internal readonly record struct LayoutInput(
    OptionalSize KnownDimensions,
    bool KnownWidthIsDefinite,
    bool KnownHeightIsDefinite,
    OptionalSize ParentSize,
    AvailableSize Available,
    SizingMode SizingMode,
    RequestedAxis Axis
)
{
    /// <summary>
    /// Returns the input of a node the parent decides fully: both sizes known and definite, the same size as the parent size and
    /// the available space (a window's root).
    /// </summary>
    public static LayoutInput Exact(Size size) =>
        new(
            new OptionalSize(size.Width, size.Height),
            KnownWidthIsDefinite: true,
            KnownHeightIsDefinite: true,
            new OptionalSize(size.Width, size.Height),
            new AvailableSize(AvailableSpace.Definite(size.Width), AvailableSpace.Definite(size.Height)),
            SizingMode.InherentSize,
            RequestedAxis.Both
        );

    /// <summary>
    /// Gets a value indicating whether the parent decided both sides: the node is a computed boundary unless the parent also
    /// read its content (design §2.4.1).
    /// </summary>
    public bool IsExact => KnownDimensions.IsComplete;
}
