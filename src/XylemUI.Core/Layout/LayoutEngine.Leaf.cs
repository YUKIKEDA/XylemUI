namespace XylemUI;

internal static partial class LayoutEngine
{
    // A node without children (Taffy's compute_leaf_layout): its known or styled size on each axis, otherwise its content's (the
    // measure function's, 0 without one) plus its padding and border; clamped by its minimum and maximum, and never smaller than
    // its padding and border.
    private static Size ComputeLeaf(in LayoutStyle style, MeasureFunc? measure, in LayoutInput input, bool commit)
    {
        Edges paddingBorder = PaddingBorder(in style, input.ParentSize.Width);
        Size inset = paddingBorder.Sum();

        // A content-size run ignores the node's own size styles (CSS Flexbox §4.5, the content size suggestion).
        OptionalSize nodeSize = input.KnownDimensions;
        OptionalSize min = OptionalSize.None;
        OptionalSize max = OptionalSize.None;
        if (input.SizingMode == SizingMode.InherentSize)
        {
            nodeSize = nodeSize.Or(StyleSize(in style, input.ParentSize));
            min = MinSize(in style, input.ParentSize);
            max = MaxSize(in style, input.ParentSize);
        }

        if (!commit && nodeSize.IsComplete)
        {
            return ToSize(nodeSize.Clamp(min, max).AtLeast(inset));
        }

        var available = new AvailableSize(
            ContentSpace(input.Available.Width.OrDefinite(nodeSize.Width), min.Width, max.Width, inset.Width),
            ContentSpace(input.Available.Height.OrDefinite(nodeSize.Height), min.Height, max.Height, inset.Height)
        );
        Size content = default;
        if (measure is not null)
        {
            OptionalSize knownContent = commit ? OptionalSize.None : input.KnownDimensions.Minus(inset);
            content = measure(knownContent, available).Size;
        }

        OptionalSize size = nodeSize.Or(new OptionalSize(content.Width + inset.Width, content.Height + inset.Height)).Clamp(min, max).AtLeast(inset);
        return ToSize(size);
    }

    // The space for the content box: a definite border-box space clamped by the node's minimum and maximum, less its padding
    // and border. A content constraint stays.
    private static AvailableSpace ContentSpace(AvailableSpace space, float min, float max, float inset) =>
        space.IsDefinite ? AvailableSpace.Definite(Maybe.Clamp(space.Value, min, max) - inset) : space;

    private static Size ToSize(OptionalSize size) => new(size.Width, size.Height);
}
